using CPCREDO.Application.Accounting;
using CPCREDO.Application.Teller;
using CPCREDO.Domain.Accounting;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Domain.Members;
using CPCREDO.Domain.Savings;
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
                Denominations = [new TillCountLineRequest { FaceValue = 100m, Quantity = 4 }]
            },
            "close-short");

        Assert.True(closed.IsSuccess, closed.ErrorMessage);
        Assert.Equal("Closed", closed.Value!.Status);
        Assert.Equal(500m, closed.Value.ExpectedCash);
        Assert.Equal(400m, closed.Value.CountedCash);
        Assert.Equal(-100m, closed.Value.OverShortAmount);
        Assert.NotNull(closed.Value.OverShortJournalId);

        var journal = await h.Db.JournalEntries.Include(j => j.Lines)
            .SingleAsync(j => j.Id == closed.Value.OverShortJournalId);
        Assert.Equal(2, journal.Lines.Count);
        Assert.Equal(100m, journal.Lines.Sum(l => l.Debit));
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
        Db.GlAccounts.AddRange(
            Gl("1010", GlAccountType.Asset, NormalBalance.Debit, Currencies.Htg),
            Gl("2010", GlAccountType.Liability, NormalBalance.Credit, Currencies.Htg),
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
