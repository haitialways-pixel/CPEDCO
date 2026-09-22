using CPCREDO.Application.Accounting;
using CPCREDO.Application.Teller;
using CPCREDO.Domain.Accounting;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Domain.Members;
using CPCREDO.Domain.Savings;
using CPCREDO.Domain.Teller;
using CPCREDO.Domain.Tenancy;
using CPCREDO.Infrastructure.Accounting;
using CPCREDO.Infrastructure.Audit;
using CPCREDO.Infrastructure.Persistence;
using CPCREDO.Infrastructure.Savings;
using CPCREDO.Infrastructure.Teller;
using CPCREDO.Tests.Accounting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CPCREDO.Tests.Teller;

public sealed class TellerTests
{
    [Fact]
    public async Task Deposit_1000_HTG_posts_two_lines_and_increases_balance()
    {
        using var h = new TellerHarness();
        await h.OpenTillAsync();
        var account = await h.OpenSavingsAsync();

        var posted = await h.Teller.DepositAsync(
            new CashPostRequest { SavingsAccountId = account.Id, Amount = 1000m },
            "dep-1000");

        Assert.True(posted.IsSuccess, posted.ErrorMessage);
        Assert.Equal(2, posted.Value!.JournalLineCount);
        Assert.Equal(1000m, posted.Value.LedgerBalance);
        Assert.Equal(1000m, posted.Value.AvailableBalance);
        Assert.Equal("CPCREDO", posted.Value.Receipt.Letterhead.Sigle);
        Assert.Equal(Letterhead.Line2, posted.Value.Receipt.Letterhead.Line2);

        var journal = await h.Db.JournalEntries.Include(j => j.Lines).SingleAsync(j => j.Id == posted.Value.JournalId);
        Assert.Equal(2, journal.Lines.Count);
        Assert.Equal(1000m, journal.Lines.Sum(l => l.Debit));
        Assert.Equal(1000m, journal.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Withdraw_200_when_balance_150_is_rejected()
    {
        using var h = new TellerHarness();
        await h.OpenTillAsync();
        var account = await h.OpenSavingsAsync();
        var dep = await h.Teller.DepositAsync(
            new CashPostRequest { SavingsAccountId = account.Id, Amount = 150m },
            "dep-150");
        Assert.True(dep.IsSuccess, dep.ErrorMessage);

        var withdraw = await h.Teller.WithdrawAsync(
            new CashPostRequest { SavingsAccountId = account.Id, Amount = 200m },
            "wd-200");

        Assert.False(withdraw.IsSuccess);
        Assert.Equal("teller.insufficient", withdraw.ErrorCode);
    }

    [Fact]
    public async Task Post_without_open_till_is_rejected()
    {
        using var h = new TellerHarness();
        var account = await h.OpenSavingsAsync();

        var posted = await h.Teller.DepositAsync(
            new CashPostRequest { SavingsAccountId = account.Id, Amount = 1000m },
            "dep-no-till");

        Assert.False(posted.IsSuccess);
        Assert.Equal("till.not_open", posted.ErrorCode);
    }

    [Fact]
    public async Task Close_till_records_over_short()
    {
        using var h = new TellerHarness();
        var till = await h.OpenTillAsync(500m);
        Assert.True(till.IsSuccess, till.ErrorMessage);

        var closed = await h.Teller.CloseAsync(
            till.Value!.Id,
            new CloseTillRequest
            {
                CountedBalance = 400m,
                Notes = "Manquant constaté au comptage.",
                Denominations = [new TillCountLineRequest { FaceValue = 100m, Quantity = 4 }]
            },
            "close-short");

        Assert.True(closed.IsSuccess, closed.ErrorMessage);
        Assert.Equal("Closed", closed.Value!.Status);
        Assert.Equal(0m, closed.Value.ExpectedCash);
        Assert.Equal(400m, closed.Value.CountedCash);
        Assert.Equal(-100m, closed.Value.OverShortAmount);
        Assert.Equal("Manquant constaté au comptage.", closed.Value.Notes);
        Assert.NotNull(closed.Value.OverShortJournalId);

        var journal = await h.Db.JournalEntries.Include(j => j.Lines)
            .SingleAsync(j => j.Id == closed.Value.OverShortJournalId);
        Assert.Equal(2, journal.Lines.Count);
        Assert.Equal(100m, journal.Lines.Sum(l => l.Debit));
        Assert.Equal(100m, journal.Lines.Single(l => l.GlAccountId == SeedGuids.Gl("5050")).Debit);
        Assert.Equal(100m, journal.Lines.Single(l => l.GlAccountId == SeedGuids.Gl("1010")).Credit);
    }

    [Fact]
    public async Task Close_without_counted_balance_is_rejected_and_does_not_default_to_expected()
    {
        using var h = new TellerHarness();
        var till = await h.OpenTillAsync(500m);
        Assert.True(till.IsSuccess, till.ErrorMessage);

        var closed = await h.Teller.CloseAsync(
            till.Value!.Id,
            new CloseTillRequest
            {
                Denominations = [new TillCountLineRequest { FaceValue = 100m, Quantity = 5 }]
            },
            "close-empty-counted");

        Assert.False(closed.IsSuccess);
        Assert.Equal("till.counted", closed.ErrorCode);

        var persisted = await h.Db.TillSessions.SingleAsync(t => t.Id == till.Value.Id);
        Assert.Equal(TillSessionStatus.Open, persisted.Status);
        Assert.Null(persisted.CountedCash);
        Assert.Equal(500m, persisted.ExpectedCash);
    }

    [Fact]
    public async Task Close_zero_counted_is_allowed_when_typed()
    {
        using var h = new TellerHarness();
        var till = await h.OpenTillAsync(0m);
        Assert.True(till.IsSuccess, till.ErrorMessage);

        var closed = await h.Teller.CloseAsync(
            till.Value!.Id,
            new CloseTillRequest { CountedBalance = 0m },
            "close-zero");

        Assert.True(closed.IsSuccess, closed.ErrorMessage);
        Assert.Equal("Closed", closed.Value!.Status);
        Assert.Equal(0m, closed.Value.CountedCash);
        Assert.Equal(0m, closed.Value.OverShortAmount);
        Assert.Null(closed.Value.OverShortJournalId);
        Assert.Null(closed.Value.Notes);
    }

    [Fact]
    public async Task Close_difference_without_notes_is_rejected()
    {
        using var h = new TellerHarness();
        var till = await h.OpenTillAsync(500m);
        Assert.True(till.IsSuccess, till.ErrorMessage);

        var closed = await h.Teller.CloseAsync(
            till.Value!.Id,
            new CloseTillRequest { CountedBalance = 480m },
            "close-no-notes");

        Assert.False(closed.IsSuccess);
        Assert.Equal("till.notes", closed.ErrorCode);
    }

    [Fact]
    public async Task Close_balanced_does_not_post_pnl()
    {
        using var h = new TellerHarness();
        var till = await h.OpenTillAsync(500m);
        Assert.True(till.IsSuccess, till.ErrorMessage);

        var closed = await h.Teller.CloseAsync(
            till.Value!.Id,
            new CloseTillRequest { CountedBalance = 500m },
            "close-balanced");

        Assert.True(closed.IsSuccess, closed.ErrorMessage);
        Assert.Equal("Closed", closed.Value!.Status);
        Assert.Equal(500m, closed.Value.CountedCash);
        Assert.Equal(0m, closed.Value.OverShortAmount);
        Assert.Null(closed.Value.OverShortJournalId);
        Assert.False(await h.Db.JournalEntries.AnyAsync(j => j.Lines.Any(l => l.GlAccountId == SeedGuids.Gl("4040") || l.GlAccountId == SeedGuids.Gl("5050"))));
        Assert.True(await h.Db.InternalCashMovements.AnyAsync(m => m.Reason == CashMovementReason.CloseReturn && m.Status == InternalCashStatus.Accepted));
    }

    [Fact]
    public async Task Close_overage_posts_income()
    {
        using var h = new TellerHarness();
        var till = await h.OpenTillAsync(500m);
        Assert.True(till.IsSuccess, till.ErrorMessage);

        var closed = await h.Teller.CloseAsync(
            till.Value!.Id,
            new CloseTillRequest
            {
                CountedBalance = 525m,
                Notes = "Excédent au comptage."
            },
            "close-over");

        Assert.True(closed.IsSuccess, closed.ErrorMessage);
        Assert.Equal(25m, closed.Value!.OverShortAmount);
        Assert.NotNull(closed.Value.OverShortJournalId);

        var journal = await h.Db.JournalEntries.Include(j => j.Lines)
            .SingleAsync(j => j.Id == closed.Value.OverShortJournalId);
        Assert.Equal(25m, journal.Lines.Single(l => l.GlAccountId == SeedGuids.Gl("1010")).Debit);
        Assert.Equal(25m, journal.Lines.Single(l => l.GlAccountId == SeedGuids.Gl("4040")).Credit);
    }

    [Fact]
    public async Task Close_denomination_mismatch_is_rejected()
    {
        using var h = new TellerHarness();
        var till = await h.OpenTillAsync(500m);
        Assert.True(till.IsSuccess, till.ErrorMessage);

        var closed = await h.Teller.CloseAsync(
            till.Value!.Id,
            new CloseTillRequest
            {
                CountedBalance = 500m,
                Denominations = [new TillCountLineRequest { FaceValue = 100m, Quantity = 4 }]
            },
            "close-mismatch");

        Assert.False(closed.IsSuccess);
        Assert.Equal("till.count_mismatch", closed.ErrorCode);
    }

    [Fact]
    public async Task Open_without_counted_float_is_rejected()
    {
        using var h = new TellerHarness();
        var opened = await h.Teller.OpenAsync(new OpenTillRequest { CurrencyCode = Currencies.Htg });
        Assert.False(opened.IsSuccess);
        Assert.Equal("till.movement_required", opened.ErrorCode);
    }

    [Fact]
    public async Task VaultToTill_accept_posts_dr_till_cr_vault()
    {
        using var h = new TellerHarness();
        await h.FundVaultAsync(10_000m);
        var till = await h.OpenTillAsync(0m);
        Assert.True(till.IsSuccess, till.ErrorMessage);

        var tellerId = h.User.UserId;
        h.User.UserId = SeedGuids.GerantUserId;
        h.User.Roles = [RoleNames.Gerant];
        var created = await h.Teller.CreateInternalMovementAsync(
            new CreateInternalCashRequest
            {
                Direction = "VaultToTill",
                Amount = 500m,
                CurrencyCode = Currencies.Htg,
                DestinationTillSessionId = till.Value!.Id
            },
            "int-v2t");
        Assert.True(created.IsSuccess, created.ErrorMessage);
        Assert.Equal("Pending", created.Value!.Status);
        h.User.UserId = tellerId;
        h.User.Roles = [RoleNames.Caissier];
        var accepted = await h.Teller.AcceptInternalMovementAsync(created.Value.Id, "int-v2t-acc");
        Assert.True(accepted.IsSuccess, accepted.ErrorMessage);
        Assert.Equal("Accepted", accepted.Value!.Status);
        Assert.Equal(500m, (await h.Db.TillSessions.SingleAsync(t => t.Id == till.Value.Id)).ExpectedCash);

        var journal = await h.Db.JournalEntries.Include(j => j.Lines).SingleAsync(j => j.Id == accepted.Value.JournalEntryId);
        Assert.Equal(500m, journal.Lines.Single(l => l.GlAccountId == SeedGuids.Gl("1010")).Debit);
        Assert.Equal(500m, journal.Lines.Single(l => l.GlAccountId == SeedGuids.Gl("1030")).Credit);
    }

    [Fact]
    public async Task TillToVault_accept_posts_dr_vault_cr_till()
    {
        using var h = new TellerHarness();
        var till = await h.OpenTillAsync(1_000m);
        var created = await h.Teller.CreateInternalMovementAsync(
            new CreateInternalCashRequest
            {
                Direction = "TillToVault",
                Amount = 200m,
                CurrencyCode = Currencies.Htg,
                SourceTillSessionId = till.Value!.Id
            },
            "int-t2v");
        Assert.True(created.IsSuccess, created.ErrorMessage);

        h.User.UserId = SeedGuids.GerantUserId;
        h.User.Roles = [RoleNames.Gerant];
        var accepted = await h.Teller.AcceptInternalMovementAsync(created.Value!.Id, "int-t2v-acc");
        Assert.True(accepted.IsSuccess, accepted.ErrorMessage);
        Assert.Equal(800m, (await h.Db.TillSessions.SingleAsync(t => t.Id == till.Value.Id)).ExpectedCash);

        var journal = await h.Db.JournalEntries.Include(j => j.Lines).SingleAsync(j => j.Id == accepted.Value!.JournalEntryId);
        Assert.Equal(200m, journal.Lines.Single(l => l.GlAccountId == SeedGuids.Gl("1030")).Debit);
        Assert.Equal(200m, journal.Lines.Single(l => l.GlAccountId == SeedGuids.Gl("1010")).Credit);
    }

    [Fact]
    public async Task TillToTill_requires_other_open_till_and_receipt()
    {
        using var h = new TellerHarness();
        var source = await h.OpenTillAsync(1_000m);
        var dest = await h.OpenSecondTillAsync(100m);

        var self = await h.Teller.CreateInternalMovementAsync(
            new CreateInternalCashRequest
            {
                Direction = "TillToTill",
                Amount = 50m,
                CurrencyCode = Currencies.Htg,
                SourceTillSessionId = source.Value!.Id,
                DestinationTillSessionId = source.Value.Id
            },
            "int-self");
        Assert.False(self.IsSuccess);
        Assert.Equal("internal.same_till", self.ErrorCode);

        var created = await h.Teller.CreateInternalMovementAsync(
            new CreateInternalCashRequest
            {
                Direction = "TillToTill",
                Amount = 250m,
                CurrencyCode = Currencies.Htg,
                SourceTillSessionId = source.Value.Id,
                DestinationTillSessionId = dest.Id
            },
            "int-t2t");
        Assert.True(created.IsSuccess, created.ErrorMessage);

        var closed = await h.Teller.CloseAsync(
            source.Value.Id,
            new CloseTillRequest { CountedBalance = 1_000m },
            "close-pending");
        Assert.False(closed.IsSuccess);
        Assert.Equal("till.pending_internal", closed.ErrorCode);

        h.User.UserId = SeedGuids.CaissierUserId;
        h.User.Roles = [RoleNames.Caissier];
        var accepted = await h.Teller.AcceptInternalMovementAsync(created.Value!.Id, "int-t2t-acc");
        Assert.True(accepted.IsSuccess, accepted.ErrorMessage);
        Assert.Equal(750m, (await h.Db.TillSessions.SingleAsync(t => t.Id == source.Value.Id)).ExpectedCash);
        Assert.Equal(350m, (await h.Db.TillSessions.SingleAsync(t => t.Id == dest.Id)).ExpectedCash);

        var journal = await h.Db.JournalEntries.Include(j => j.Lines).SingleAsync(j => j.Id == accepted.Value!.JournalEntryId);
        Assert.Equal(2, journal.Lines.Count);
        Assert.Equal(250m, journal.Lines.Sum(l => l.Debit));
        Assert.All(journal.Lines, l => Assert.Equal(SeedGuids.Gl("1010"), l.GlAccountId));
    }

    [Fact]
    public async Task Internal_amount_empty_is_rejected()
    {
        using var h = new TellerHarness();
        var till = await h.OpenTillAsync(500m);
        var created = await h.Teller.CreateInternalMovementAsync(
            new CreateInternalCashRequest
            {
                Direction = "TillToVault",
                CurrencyCode = Currencies.Htg,
                SourceTillSessionId = till.Value!.Id
            },
            "int-empty");
        Assert.False(created.IsSuccess);
        Assert.Equal("internal.amount", created.ErrorCode);
    }

    [Fact]
    public async Task Ticket_before_accept_is_conflict()
    {
        using var h = new TellerHarness();
        var opened = await h.Teller.OpenAsync(new OpenTillRequest { CurrencyCode = Currencies.Htg, OpeningFloat = 100m });
        Assert.False(opened.IsSuccess);
        Assert.Equal("till.movement_required", opened.ErrorCode);
        var account = await h.OpenSavingsAsync();
        var posted = await h.Teller.DepositAsync(
            new CashPostRequest { SavingsAccountId = account.Id, Amount = 10m },
            "dep-before-open");
        Assert.False(posted.IsSuccess);
        Assert.Equal("till.not_open", posted.ErrorCode);
    }

    [Fact]
    public async Task Accept_count_mismatch_and_self_accept_and_teller_vault_to_self_are_rejected()
    {
        using var h = new TellerHarness();
        await h.FundVaultAsync(1_000m);
        h.User.UserId = SeedGuids.GerantUserId;
        h.User.Roles = [RoleNames.Gerant];
        var issued = await h.Teller.CreateInternalMovementAsync(
            new CreateInternalCashRequest
            {
                Direction = "VaultToTill",
                Reason = "OpeningFloat",
                Amount = 200m,
                DestinationTellerUserId = SeedGuids.AdminUserId
            },
            "gap-open");
        Assert.True(issued.IsSuccess, issued.ErrorMessage);
        var self = await h.Teller.AcceptInternalMovementAsync(
            issued.Value!.Id,
            "gap-self",
            accept: new AcceptMovementRequest { CountedAmount = 200m });
        Assert.False(self.IsSuccess);
        Assert.Equal("internal.self_accept", self.ErrorCode);

        h.User.UserId = SeedGuids.AdminUserId;
        h.User.Roles = [RoleNames.Caissier];
        var mismatch = await h.Teller.AcceptInternalMovementAsync(
            issued.Value.Id,
            "gap-mis",
            accept: new AcceptMovementRequest { CountedAmount = 199m });
        Assert.False(mismatch.IsSuccess);
        Assert.Equal("till.count_mismatch", mismatch.ErrorCode);

        var vaultSelf = await h.Teller.CreateInternalMovementAsync(
            new CreateInternalCashRequest
            {
                Direction = "VaultToTill",
                Reason = "OpeningFloat",
                Amount = 50m,
                DestinationTellerUserId = SeedGuids.AdminUserId
            },
            "gap-self-issue");
        Assert.False(vaultSelf.IsSuccess);
        Assert.Equal("auth.forbidden", vaultSelf.ErrorCode);
    }

    [Fact]
    public async Task Accept_opening_float_opens_till_reject_leaves_closed()
    {
        using var h = new TellerHarness();
        await h.FundVaultAsync(5_000m);
        h.User.UserId = SeedGuids.GerantUserId;
        h.User.Roles = [RoleNames.Gerant];
        var issued = await h.Teller.CreateInternalMovementAsync(
            new CreateInternalCashRequest
            {
                Direction = "VaultToTill",
                Reason = "OpeningFloat",
                Amount = 300m,
                DestinationTellerUserId = SeedGuids.AdminUserId
            },
            "gap-ok");
        h.User.UserId = SeedGuids.AdminUserId;
        h.User.Roles = [RoleNames.Caissier];
        var ok = await h.Teller.AcceptInternalMovementAsync(
            issued.Value!.Id,
            "gap-ok-acc",
            accept: new AcceptMovementRequest { CountedAmount = 300m });
        Assert.True(ok.IsSuccess, ok.ErrorMessage);
        var till = await h.Db.TillSessions.SingleAsync(t => t.UserId == SeedGuids.AdminUserId && t.Status == TillSessionStatus.Open);
        Assert.Equal(300m, till.OpeningFloat);
        Assert.Equal(issued.Value.Id, till.OpeningMovementId);

        h.User.UserId = SeedGuids.GerantUserId;
        h.User.Roles = [RoleNames.Gerant];
        var other = await h.Teller.CreateInternalMovementAsync(
            new CreateInternalCashRequest
            {
                Direction = "VaultToTill",
                Reason = "OpeningFloat",
                Amount = 80m,
                DestinationTellerUserId = SeedGuids.CaissierUserId
            },
            "gap-rej");
        h.User.UserId = SeedGuids.CaissierUserId;
        h.User.Roles = [RoleNames.Caissier];
        var rejected = await h.Teller.RejectInternalMovementAsync(
            other.Value!.Id,
            new RejectMovementRequest { Reason = "Billet manquant" });
        Assert.True(rejected.IsSuccess, rejected.ErrorMessage);
        Assert.Equal("Rejected", rejected.Value!.Status);
        Assert.False(await h.Db.TillSessions.AnyAsync(t => t.UserId == SeedGuids.CaissierUserId && t.Status == TillSessionStatus.Open));
    }

    [Fact]
    public async Task Source_shares_is_rejected()
    {
        using var h = new TellerHarness();
        h.User.UserId = SeedGuids.GerantUserId;
        h.User.Roles = [RoleNames.Gerant];
        var created = await h.Teller.CreateInternalMovementAsync(
            new CreateInternalCashRequest
            {
                Direction = "VaultToTill",
                SourceType = "Shares",
                Amount = 10m,
                DestinationTellerUserId = SeedGuids.AdminUserId
            },
            "gap-shares");
        Assert.False(created.IsSuccess);
        Assert.Equal("internal.location", created.ErrorCode);
    }

    [Fact]
    public async Task Terme_withdraw_before_maturity_is_blocked_unless_gerant_note()
    {
        using var h = new TellerHarness();
        await h.OpenTillAsync();
        var product = new SavingsProduct
        {
            Id = Guid.NewGuid(),
            TenantId = SeedGuids.TenantId,
            Code = "ETM-HTG",
            LegalName = "Épargne à terme",
            Name = "Épargne à terme",
            CurrencyCode = Currencies.Htg,
            ProductKind = SavingsProductKind.Terme,
            TermDays = 90,
            AllowWithdrawBeforeTerm = false,
            LiabilityGlAccountId = SeedGuids.Gl("2110"),
            CashGlAccountId = SeedGuids.Gl("1010"),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        h.Db.SavingsProducts.Add(product);
        var member = new Member
        {
            Id = Guid.NewGuid(),
            TenantId = SeedGuids.TenantId,
            BranchId = SeedGuids.BranchId,
            MemberNo = "M-TERM",
            FirstName = "Terme",
            LastName = "Test",
            Phone = "1",
            AddressLine = "x",
            City = Letterhead.City,
            Status = MemberStatus.Active,
            LegalStatus = LegalStatus.Societaire,
            KycStatus = KycStatus.Verified,
            CreatedAtUtc = DateTime.UtcNow
        };
        h.Db.Members.Add(member);
        await h.Db.SaveChangesAsync();
        var opened = await h.Savings.OpenAccountAsync(member.Id, product.Id);
        Assert.True(opened.IsSuccess, opened.ErrorMessage);
        Assert.NotNull(opened.Value!.MaturesOn);
        var dep = await h.Teller.DepositAsync(
            new CashPostRequest { SavingsAccountId = opened.Value.Id, Amount = 1000m },
            "term-dep");
        Assert.True(dep.IsSuccess, dep.ErrorMessage);

        h.User.Roles = [RoleNames.Caissier];
        var blocked = await h.Teller.WithdrawAsync(
            new CashPostRequest { SavingsAccountId = opened.Value.Id, Amount = 100m },
            "term-wd");
        Assert.False(blocked.IsSuccess);
        Assert.Equal("teller.terme_locked", blocked.ErrorCode);

        h.User.Roles = [RoleNames.Gerant];
        var still = await h.Teller.WithdrawAsync(
            new CashPostRequest { SavingsAccountId = opened.Value.Id, Amount = 100m },
            "term-wd-2");
        Assert.False(still.IsSuccess);

        var ok = await h.Teller.WithdrawAsync(
            new CashPostRequest
            {
                SavingsAccountId = opened.Value.Id,
                Amount = 100m,
                GerantOverrideNote = "Urgence médicale"
            },
            "term-wd-ok");
        Assert.True(ok.IsSuccess, ok.ErrorMessage);
    }

    [Fact]
    public async Task Mixed_collect_splits_cash_without_putting_parts_on_savings()
    {
        using var h = new TellerHarness();
        await h.OpenTillAsync();
        var savings = await h.OpenSavingsAsync();
        var member = await h.Db.Members.Include(m => m.ShareAccounts).SingleAsync(m => m.Id == savings.MemberId);
        member.LegalStatus = LegalStatus.Societaire;
        h.Db.ShareAccounts.Add(new ShareAccount
        {
            TenantId = SeedGuids.TenantId,
            MemberId = member.Id,
            BranchId = SeedGuids.BranchId,
            AccountNo = "S-000099",
            ShareType = ShareType.Qualification,
            ShareCount = 0,
            ParValue = 500m,
            CurrencyCode = Currencies.Htg,
            IsActive = true,
            OpenedAtUtc = DateTime.UtcNow
        });
        await h.Db.SaveChangesAsync();

        var posted = await h.Teller.CollectMixedAsync(
            new MixedCollectRequest
            {
                MemberId = member.Id,
                CashReceived = 2500m,
                CurrencyCode = Currencies.Htg,
                Lines =
                [
                    new MixedCollectLineRequest { Kind = "Epargne", SavingsAccountId = savings.Id, Amount = 1000m },
                    new MixedCollectLineRequest { Kind = "Qualification", Amount = 1500m }
                ]
            },
            "mix-2500");

        Assert.True(posted.IsSuccess, posted.ErrorMessage);
        Assert.Equal(3, posted.Value!.JournalLineCount);
        Assert.Equal(1000m, posted.Value.LedgerBalance);
        Assert.Equal(2, posted.Value.Receipt.Allocations!.Count);
        var journal = await h.Db.JournalEntries.Include(j => j.Lines).SingleAsync(j => j.Id == posted.Value.JournalId);
        Assert.Equal(2500m, journal.Lines.Single(l => l.GlAccountId == SeedGuids.Gl("1010")).Debit);
        Assert.Equal(1000m, journal.Lines.Single(l => l.GlAccountId == SeedGuids.Gl("2010")).Credit);
        Assert.Equal(1500m, journal.Lines.Single(l => l.GlAccountId == SeedGuids.Gl("3010")).Credit);
        var share = await h.Db.ShareAccounts.SingleAsync(s => s.MemberId == member.Id && s.ShareType == ShareType.Qualification);
        Assert.Equal(3, share.ShareCount);
    }

    [Fact]
    public async Task Mixed_collect_refuses_usager_qualification_and_unbalanced()
    {
        using var h = new TellerHarness();
        await h.OpenTillAsync();
        var savings = await h.OpenSavingsAsync();
        var unbalanced = await h.Teller.CollectMixedAsync(
            new MixedCollectRequest
            {
                MemberId = savings.MemberId,
                CashReceived = 2500m,
                Lines =
                [
                    new MixedCollectLineRequest { Kind = "Epargne", SavingsAccountId = savings.Id, Amount = 1000m }
                ]
            },
            "mix-unbal");
        Assert.False(unbalanced.IsSuccess);
        Assert.Equal("teller.collect_unbalanced", unbalanced.ErrorCode);

        var usager = await h.Teller.CollectMixedAsync(
            new MixedCollectRequest
            {
                MemberId = savings.MemberId,
                CashReceived = 500m,
                Lines =
                [
                    new MixedCollectLineRequest { Kind = "Qualification", Amount = 500m }
                ]
            },
            "mix-usager");
        Assert.False(usager.IsSuccess);
        Assert.Equal("savings.usager_parts", usager.ErrorCode);
    }

    [Fact]
    public async Task Usager_cannot_open_terme_account()
    {
        using var h = new TellerHarness();
        var product = new SavingsProduct
        {
            Id = Guid.NewGuid(),
            TenantId = SeedGuids.TenantId,
            Code = "ETM-U",
            LegalName = "Terme",
            Name = "Terme",
            CurrencyCode = Currencies.Htg,
            ProductKind = SavingsProductKind.Terme,
            TermDays = 30,
            LiabilityGlAccountId = SeedGuids.Gl("2110"),
            CashGlAccountId = SeedGuids.Gl("1010"),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        h.Db.SavingsProducts.Add(product);
        var member = new Member
        {
            Id = Guid.NewGuid(),
            TenantId = SeedGuids.TenantId,
            BranchId = SeedGuids.BranchId,
            MemberNo = "M-USG",
            FirstName = "U",
            LastName = "Sager",
            Phone = "1",
            AddressLine = "x",
            City = Letterhead.City,
            Status = MemberStatus.Active,
            LegalStatus = LegalStatus.Usager,
            CreatedAtUtc = DateTime.UtcNow
        };
        h.Db.Members.Add(member);
        await h.Db.SaveChangesAsync();
        var opened = await h.Savings.OpenAccountAsync(member.Id, product.Id);
        Assert.False(opened.IsSuccess);
        Assert.Equal("savings.usager_terme", opened.ErrorCode);
    }
}

internal sealed class TellerHarness : IDisposable
{
    public CpcredoDbContext Db { get; }
    public TellerService Teller { get; }
    public SavingsService Savings { get; }
    public TestCurrentUser User { get; }

    public TellerHarness()
    {
        var options = new DbContextOptionsBuilder<CpcredoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        Db = new CpcredoDbContext(options);
        Db.Database.EnsureCreated();

        var now = new DateTime(2026, 1, 2, 13, 0, 0, DateTimeKind.Utc);
        Db.Tenants.Add(new Tenant
        {
            Id = SeedGuids.TenantId,
            Sigle = Letterhead.Sigle,
            City = Letterhead.City,
            Country = Letterhead.Country,
            LegalName = Letterhead.LegalName,
            CreatedAtUtc = now
        });
        Db.Branches.Add(new Branch
        {
            Id = SeedGuids.BranchId,
            TenantId = SeedGuids.TenantId,
            Code = "SIEGE",
            Name = Letterhead.DefaultBranchName,
            City = Letterhead.City,
            Country = Letterhead.Country,
            IsHeadquarters = true,
            CreatedAtUtc = now
        });
        Db.Users.Add(new User
        {
            Id = SeedGuids.AdminUserId,
            TenantId = SeedGuids.TenantId,
            BranchId = SeedGuids.BranchId,
            Username = "caissier",
            Email = "caisse@cpcredo.ht",
            FullName = "Caissier Test",
            PasswordHash = "hash",
            CreatedAtUtc = now
        });
        Db.Users.Add(new User
        {
            Id = SeedGuids.CaissierUserId,
            TenantId = SeedGuids.TenantId,
            BranchId = SeedGuids.BranchId,
            Username = "caissier-b",
            Email = "caisse-b@cpcredo.ht",
            FullName = "Caissier B",
            PasswordHash = "hash",
            CreatedAtUtc = now
        });
        Db.Users.Add(new User
        {
            Id = SeedGuids.GerantUserId,
            TenantId = SeedGuids.TenantId,
            BranchId = SeedGuids.BranchId,
            Username = "gerant",
            Email = "gerant@cpcredo.ht",
            FullName = "Gérant Test",
            PasswordHash = "hash",
            CreatedAtUtc = now
        });
        Db.GlAccounts.AddRange(
            Gl("1010", GlAccountType.Asset, NormalBalance.Debit, Currencies.Htg),
            Gl("1030", GlAccountType.Asset, NormalBalance.Debit, Currencies.Htg),
            Gl("2010", GlAccountType.Liability, NormalBalance.Credit, Currencies.Htg),
            Gl("2110", GlAccountType.Liability, NormalBalance.Credit, Currencies.Htg),
            Gl("3010", GlAccountType.Equity, NormalBalance.Credit, Currencies.Htg),
            Gl("4040", GlAccountType.Income, NormalBalance.Credit, Currencies.Htg),
            Gl("5050", GlAccountType.Expense, NormalBalance.Debit, Currencies.Htg));
        Db.GlAccounts.Add(Gl("3011", GlAccountType.Equity, NormalBalance.Credit, Currencies.Htg));
        Db.SavingsProducts.Add(new SavingsProduct
        {
            Id = SeedGuids.SavingsProductHtg,
            TenantId = SeedGuids.TenantId,
            Code = "EAV-HTG",
            LegalName = "Épargne à vue",
            Name = "Épargne à vue HTG",
            CurrencyCode = Currencies.Htg,
            ProductKind = SavingsProductKind.AVue,
            LiabilityGlAccountId = SeedGuids.Gl("2010"),
            CashGlAccountId = SeedGuids.Gl("1010"),
            IsActive = true,
            CreatedAtUtc = now
        });
        Db.SaveChanges();

        User = new TestCurrentUser { Roles = [RoleNames.Caissier] };
        var clock = new FixedClock();
        var audit = new AuditLogger(Db, clock, User);
        var journals = new JournalService(Db, User, clock, audit);
        Savings = new SavingsService(Db, User, clock, audit);
        Teller = new TellerService(Db, User, clock, journals, audit);
    }

    public async Task<CPCREDO.Application.Common.Result<TillSessionDto>> OpenTillAsync(decimal openingFloat = 0m)
    {
        if (openingFloat > 0m)
            await FundVaultAsync(openingFloat + 50_000m);
        var tellerId = User.UserId!.Value;
        var tellerRoles = User.Roles;
        User.UserId = SeedGuids.GerantUserId;
        User.Roles = [RoleNames.Gerant];
        var issued = await Teller.CreateInternalMovementAsync(
            new CreateInternalCashRequest
            {
                Direction = "VaultToTill",
                Reason = "OpeningFloat",
                Amount = openingFloat,
                CurrencyCode = Currencies.Htg,
                DestinationTellerUserId = tellerId
            },
            "open-" + Guid.NewGuid().ToString("N"));
        Assert.True(issued.IsSuccess, issued.ErrorMessage);
        User.UserId = tellerId;
        User.Roles = tellerRoles;
        var accepted = await Teller.AcceptInternalMovementAsync(
            issued.Value!.Id,
            "open-acc-" + Guid.NewGuid().ToString("N"),
            accept: new AcceptMovementRequest { CountedAmount = openingFloat });
        Assert.True(accepted.IsSuccess, accepted.ErrorMessage);
        return await Teller.GetCurrentAsync(Currencies.Htg);
    }

    public async Task FundVaultAsync(decimal amount)
    {
        var posted = await new JournalService(Db, User, new FixedClock(), new AuditLogger(Db, new FixedClock(), User))
            .PostAsync(new CreateJournalRequest
            {
                Description = "Alimentation coffre",
                CurrencyCode = Currencies.Htg,
                Lines =
                [
                    new CreateJournalLineRequest { GlAccountId = SeedGuids.Gl("1030"), Debit = amount, Credit = 0m, Description = "Coffre" },
                    new CreateJournalLineRequest { GlAccountId = SeedGuids.Gl("3010"), Debit = 0m, Credit = amount, Description = "Capital" }
                ]
            }, "fund-vault");
        Assert.True(posted.IsSuccess, posted.ErrorMessage);
    }

    public async Task<TillSession> OpenSecondTillAsync(decimal openingFloat)
    {
        var previous = User.UserId;
        User.UserId = SeedGuids.CaissierUserId;
        var opened = await OpenTillAsync(openingFloat);
        Assert.True(opened.IsSuccess, opened.ErrorMessage);
        User.UserId = previous;
        return await Db.TillSessions.SingleAsync(t => t.Id == opened.Value!.Id);
    }

    public async Task<SavingsAccount> OpenSavingsAsync()
    {
        var member = new Member
        {
            Id = Guid.NewGuid(),
            TenantId = SeedGuids.TenantId,
            BranchId = SeedGuids.BranchId,
            MemberNo = "M-009999",
            FirstName = "Test",
            LastName = "Membre",
            Cin = Guid.NewGuid().ToString("N")[..12],
            Phone = "+509 0000 0000",
            AddressLine = "Rue Test",
            City = Letterhead.City,
            Status = MemberStatus.Active,
            KycStatus = KycStatus.Verified,
            CreatedAtUtc = DateTime.UtcNow
        };
        Db.Members.Add(member);
        await Db.SaveChangesAsync();
        var opened = await Savings.OpenAccountAsync(member.Id, SeedGuids.SavingsProductHtg);
        Assert.True(opened.IsSuccess, opened.ErrorMessage);
        return await Db.SavingsAccounts.Include(a => a.Product).Include(a => a.Member)
            .SingleAsync(a => a.Id == opened.Value!.Id);
    }

    private static GlAccount Gl(string code, GlAccountType type, NormalBalance nb, string currency) =>
        new()
        {
            Id = SeedGuids.Gl(code),
            TenantId = SeedGuids.TenantId,
            Code = code,
            NameFr = code,
            NameHt = code,
            NameEn = code,
            AccountType = type,
            NormalBalance = nb,
            CurrencyCode = currency,
            IsPostable = true,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

    public void Dispose() => Db.Dispose();
}
