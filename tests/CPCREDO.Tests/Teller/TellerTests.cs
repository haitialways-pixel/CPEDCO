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
        Assert.Equal(500m, closed.Value.ExpectedCash);
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
        Assert.Equal(0, await h.Db.JournalEntries.CountAsync());
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
        Assert.Equal("till.float", opened.ErrorCode);
    }

    [Fact]
    public async Task VaultToTill_accept_posts_dr_till_cr_vault()
    {
        using var h = new TellerHarness();
        await h.FundVaultAsync(10_000m);
        var till = await h.OpenTillAsync(0m);
        Assert.True(till.IsSuccess, till.ErrorMessage);

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
        Db.GlAccounts.AddRange(
            Gl("1010", GlAccountType.Asset, NormalBalance.Debit, Currencies.Htg),
            Gl("1030", GlAccountType.Asset, NormalBalance.Debit, Currencies.Htg),
            Gl("2010", GlAccountType.Liability, NormalBalance.Credit, Currencies.Htg),
            Gl("3010", GlAccountType.Equity, NormalBalance.Credit, Currencies.Htg),
            Gl("4040", GlAccountType.Income, NormalBalance.Credit, Currencies.Htg),
            Gl("5050", GlAccountType.Expense, NormalBalance.Debit, Currencies.Htg));
        Db.SavingsProducts.Add(new SavingsProduct
        {
            Id = SeedGuids.SavingsProductHtg,
            TenantId = SeedGuids.TenantId,
            Name = "Épargne à vue HTG",
            CurrencyCode = Currencies.Htg,
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

    public Task<CPCREDO.Application.Common.Result<TillSessionDto>> OpenTillAsync(decimal openingFloat = 0m) =>
        Teller.OpenAsync(new OpenTillRequest { CurrencyCode = Currencies.Htg, OpeningFloat = openingFloat });

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
