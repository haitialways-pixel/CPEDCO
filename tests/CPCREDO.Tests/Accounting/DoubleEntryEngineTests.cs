using CPCREDO.Application.Accounting;
using CPCREDO.Domain.Accounting;
using CPCREDO.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CPCREDO.Tests.Accounting;

public sealed class DoubleEntryEngineTests
{
    [Fact]
    public async Task Unbalanced_journal_is_rejected()
    {
        using var harness = new JournalHarness();

        var result = await harness.Journals.PostAsync(new CreateJournalRequest
        {
            Description = "Non équilibrée",
            Lines =
            [
                new CreateJournalLineRequest { GlAccountId = harness.CashId, Debit = 100m, Credit = 0m },
                new CreateJournalLineRequest { GlAccountId = harness.CapitalId, Debit = 0m, Credit = 90m }
            ]
        }, idempotencyKey: "unbalanced-1");

        Assert.False(result.IsSuccess);
        Assert.Equal("journal.unbalanced", result.ErrorCode);
        Assert.Equal(0, await harness.Db.JournalEntries.CountAsync());
    }

    [Fact]
    public async Task Posted_journal_cannot_be_edited_or_deleted()
    {
        using var harness = new JournalHarness();
        var posted = await PostBalancedAsync(harness, 250m, "edit-1");
        Assert.True(posted.IsSuccess);

        var entity = await harness.Db.JournalEntries.FirstAsync(j => j.Id == posted.Value!.Id);
        entity.Description = "mutation interdite";

        await Assert.ThrowsAsync<PostedJournalImmutableException>(() => harness.Db.SaveChangesAsync());
        harness.Db.ChangeTracker.Clear();

        var reload = await harness.Db.JournalEntries.FirstAsync(j => j.Id == posted.Value!.Id);
        harness.Db.JournalEntries.Remove(reload);

        await Assert.ThrowsAsync<PostedJournalImmutableException>(() => harness.Db.SaveChangesAsync());
        harness.Db.ChangeTracker.Clear();

        var line = await harness.Db.JournalLines.FirstAsync(l => l.JournalEntryId == posted.Value!.Id);
        line.Debit = 1m;
        await Assert.ThrowsAsync<PostedJournalImmutableException>(() => harness.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Reversal_balances_the_original()
    {
        using var harness = new JournalHarness();
        var original = await PostBalancedAsync(harness, 1_000m, "rev-orig");
        Assert.True(original.IsSuccess);

        var reversal = await harness.Journals.ReverseAsync(
            original.Value!.Id,
            new ReverseJournalRequest(),
            "rev-offset");

        Assert.True(reversal.IsSuccess, reversal.ErrorMessage);
        Assert.True(reversal.Value!.IsReversal);
        Assert.Equal(original.Value.Id, reversal.Value.ReversalOfJournalId);
        Assert.Equal(original.Value.TotalDebit, reversal.Value.TotalDebit);
        Assert.Equal(original.Value.TotalCredit, reversal.Value.TotalCredit);

        var nets = await harness.Db.JournalLines
            .Where(l => l.JournalEntryId == original.Value.Id || l.JournalEntryId == reversal.Value.Id)
            .GroupBy(l => l.GlAccountId)
            .Select(g => new { g.Key, Net = g.Sum(x => x.Debit) - g.Sum(x => x.Credit) })
            .ToListAsync();

        Assert.NotEmpty(nets);
        Assert.All(nets, row => Assert.Equal(0m, row.Net));
    }

    [Fact]
    public async Task Trial_balance_is_zero_net()
    {
        using var harness = new JournalHarness();
        var posted = await PostBalancedAsync(harness, 75_000m, "tb-1");
        Assert.True(posted.IsSuccess);

        var tb = await harness.Journals.GetTrialBalanceAsync(new DateOnly(2026, 9, 2), "HTG");
        Assert.True(tb.IsSuccess, tb.ErrorMessage);
        Assert.Equal(0m, tb.Value!.Net);
        Assert.Equal(tb.Value.TotalDebit, tb.Value.TotalCredit);
        Assert.True(tb.Value.TotalDebit > 0m);

        var reversed = await harness.Journals.ReverseAsync(posted.Value!.Id, new ReverseJournalRequest(), "tb-rev");
        Assert.True(reversed.IsSuccess, reversed.ErrorMessage);

        var after = await harness.Journals.GetTrialBalanceAsync(new DateOnly(2026, 9, 2), "HTG");
        Assert.True(after.IsSuccess);
        Assert.Equal(0m, after.Value!.Net);
        Assert.Equal(after.Value.TotalDebit, after.Value.TotalCredit);
        Assert.Empty(after.Value.Rows);
    }

    [Fact]
    public async Task Only_admin_and_gerant_can_reverse()
    {
        using var harness = new JournalHarness();
        var posted = await PostBalancedAsync(harness, 10m, "perm-orig");
        Assert.True(posted.IsSuccess);

        harness.User.Roles = [RoleNames.Caissier];
        var denied = await harness.Journals.ReverseAsync(posted.Value!.Id, new ReverseJournalRequest(), "perm-deny");
        Assert.False(denied.IsSuccess);
        Assert.Equal("auth.forbidden", denied.ErrorCode);

        harness.User.Roles = [RoleNames.Gerant];
        var allowed = await harness.Journals.ReverseAsync(posted.Value.Id, new ReverseJournalRequest(), "perm-ok");
        Assert.True(allowed.IsSuccess, allowed.ErrorMessage);
    }

    private static Task<CPCREDO.Application.Common.Result<JournalDto>> PostBalancedAsync(
        JournalHarness harness,
        decimal amount,
        string idempotencyKey) =>
        harness.Journals.PostAsync(new CreateJournalRequest
        {
            Description = "Écriture d’essai",
            CurrencyCode = "HTG",
            Lines =
            [
                new CreateJournalLineRequest { GlAccountId = harness.CashId, Debit = amount, Credit = 0m, Description = "Débit caisse" },
                new CreateJournalLineRequest { GlAccountId = harness.CapitalId, Debit = 0m, Credit = amount, Description = "Crédit capital" }
            ]
        }, idempotencyKey);
}
