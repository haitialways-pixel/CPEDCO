using System.Text;
using CPCREDO.Application.Accounting;
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
using CPCREDO.Infrastructure.Reports;
using CPCREDO.Tests.Accounting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CPCREDO.Tests.Reports;

public sealed class ReportTests
{
    [Fact]
    public async Task Trial_balance_pdf_uses_letterhead_and_two_decimals()
    {
        using var h = new ReportHarness();
        await h.PostOpeningAsync();

        var pdf = await h.Reports.ExportTrialBalanceAsync(new DateOnly(2026, 1, 2), Currencies.Htg, "pdf");

        Assert.True(pdf.IsSuccess, pdf.ErrorMessage);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf.Value!.Content.AsSpan(0, 4)));
        Assert.Equal("balance-verification-2026-01-02.pdf", pdf.Value.FileName);
        Assert.Contains("CPCREDO", Encoding.Latin1.GetString(pdf.Value.Content), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Balance_sheet_equals_assets_to_liabilities_plus_equity()
    {
        using var h = new ReportHarness();
        await h.PostOpeningAsync();
        await h.PostDepositAsync(1_000m);

        var report = await h.Reports.GetFinancialsAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), Currencies.Htg);

        Assert.True(report.IsSuccess, report.ErrorMessage);
        Assert.Equal(report.Value!.TotalAssets, report.Value.TotalLiabilitiesAndEquity);
        Assert.Equal(501_000.0000m, report.Value.TotalAssets);
        Assert.Equal(1_000.0000m, report.Value.TotalLiabilities);
    }

    [Fact]
    public async Task Liquidity_ratio_is_liquid_assets_over_member_deposits()
    {
        using var h = new ReportHarness();
        await h.PostOpeningAsync();
        await h.PostDepositAsync(25_000m);

        var report = await h.Reports.GetLiquidityAsync(new DateOnly(2026, 1, 2), Currencies.Htg);

        Assert.True(report.IsSuccess, report.ErrorMessage);
        Assert.Equal(525_000.0000m, report.Value!.TotalLiquidAssets);
        Assert.Equal(25_000.0000m, report.Value.TotalMemberDeposits);
        Assert.Equal(21.0000m, report.Value.Ratio);
    }

    [Fact]
    public async Task Deposit_listing_includes_cash_deposit()
    {
        using var h = new ReportHarness();
        await h.PostDepositAsync(1_500m);

        var report = await h.Reports.GetDepositsAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), Currencies.Htg);

        Assert.True(report.IsSuccess, report.ErrorMessage);
        Assert.Single(report.Value!.Rows);
        Assert.Equal(1_500.0000m, report.Value.Total);
        Assert.Equal("M-000099", report.Value.Rows[0].MemberNo);
    }

    [Fact]
    public async Task Teller_cash_proof_lists_session_for_the_day()
    {
        using var h = new ReportHarness();
        h.Db.TillSessions.Add(new TillSession
        {
            TenantId = SeedGuids.TenantId,
            BranchId = SeedGuids.BranchId,
            UserId = SeedGuids.AdminUserId,
            CurrencyCode = Currencies.Htg,
            Status = TillSessionStatus.Closed,
            OpeningFloat = 5_000m,
            ExpectedCash = 6_000m,
            CountedCash = 5_900m,
            OverShortAmount = -100m,
            OpenedAtUtc = h.Clock.UtcNow.AddHours(-3),
            ClosedAtUtc = h.Clock.UtcNow
        });
        h.Db.SaveChanges();

        var report = await h.Reports.GetTellerCashProofAsync(h.Clock.TodayInPortAuPrince(), Currencies.Htg);

        Assert.True(report.IsSuccess, report.ErrorMessage);
        Assert.Single(report.Value!.Sessions);
        Assert.Equal(5_000m, report.Value.Sessions[0].OpeningFloat);
        Assert.Equal(-100m, report.Value.Sessions[0].OverShortAmount);
    }

    [Fact]
    public async Task Commissaire_can_read_reports()
    {
        using var h = new ReportHarness();
        await h.PostOpeningAsync();
        h.User.Roles = [RoleNames.Commissaire];

        var tb = await h.Reports.GetTrialBalanceAsync(new DateOnly(2026, 1, 2), Currencies.Htg);
        var csv = await h.Reports.ExportLiquidityAsync(new DateOnly(2026, 1, 2), Currencies.Htg, "csv");

        Assert.True(tb.IsSuccess, tb.ErrorMessage);
        Assert.True(csv.IsSuccess, csv.ErrorMessage);
        Assert.Equal("text/csv; charset=utf-8", csv.Value!.ContentType);
        var csvText = Encoding.UTF8.GetString(csv.Value.Content);
        Assert.Contains("Actifs liquides", csvText, StringComparison.Ordinal);
    }
}

internal sealed class ReportHarness : IDisposable
{
    public CpcredoDbContext Db { get; }
    public ReportService Reports { get; }
    public JournalService Journals { get; }
    public TestCurrentUser User { get; }
    public FixedClock Clock { get; }

    public ReportHarness()
    {
        var options = new DbContextOptionsBuilder<CpcredoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        Db = new CpcredoDbContext(options);
        Db.Database.EnsureCreated();

        var now = new DateTime(2026, 1, 2, 16, 0, 0, DateTimeKind.Utc);
        Db.Tenants.Add(new Tenant
        {
            Id = SeedGuids.TenantId,
            Sigle = Letterhead.Sigle,
            LegalName = Letterhead.LegalName,
            City = Letterhead.City,
            Country = Letterhead.Country,
            CreatedAtUtc = now
        });
        Db.Branches.Add(new Branch
        {
            Id = SeedGuids.BranchId,
            TenantId = SeedGuids.TenantId,
            Code = Letterhead.DefaultBranchCode,
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
            Username = "admin",
            Email = "admin@cpcredo.ht",
            FullName = "Administrateur CPCREDO",
            PasswordHash = "hash",
            CreatedAtUtc = now
        });
        Db.GlAccounts.AddRange(
            Gl("1010", "Caisse HTG", GlAccountType.Asset, NormalBalance.Debit),
            Gl("1110", "Banque HTG", GlAccountType.Asset, NormalBalance.Debit),
            Gl("2010", "Épargne à vue HTG", GlAccountType.Liability, NormalBalance.Credit),
            Gl("3010", "Parts de qualification", GlAccountType.Equity, NormalBalance.Credit),
            Gl("4040", "Écarts de caisse (produits)", GlAccountType.Income, NormalBalance.Credit),
            Gl("5050", "Écarts de caisse (charges)", GlAccountType.Expense, NormalBalance.Debit));
        var memberId = Guid.Parse("0c0ec0de-0001-4000-a000-000000000299");
        Db.Members.Add(new Member
        {
            Id = memberId,
            TenantId = SeedGuids.TenantId,
            BranchId = SeedGuids.BranchId,
            MemberNo = "M-000099",
            FirstName = "Test",
            LastName = "Dépôt",
            Cin = "CIN-REP-99",
            Phone = "+509 0000 0099",
            AddressLine = "Rue Test",
            City = Letterhead.City,
            Status = MemberStatus.Active,
            KycStatus = KycStatus.Verified,
            CreatedAtUtc = now
        });
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
        Db.SavingsAccounts.Add(new SavingsAccount
        {
            Id = Guid.Parse("0c0ec0de-0001-4000-a000-000000000399"),
            TenantId = SeedGuids.TenantId,
            MemberId = memberId,
            BranchId = SeedGuids.BranchId,
            ProductId = SeedGuids.SavingsProductHtg,
            AccountNo = "A-000099",
            CurrencyCode = Currencies.Htg,
            IsActive = true,
            OpenedAtUtc = now
        });
        Db.SaveChanges();

        User = new TestCurrentUser();
        Clock = new FixedClock { UtcNow = now };
        var audit = new AuditLogger(Db, Clock, User);
        Journals = new JournalService(Db, User, Clock, audit);
        Reports = new ReportService(Db, User, Clock, Journals);
    }

    public async Task PostOpeningAsync()
    {
        var posted = await Journals.PostAsync(new CreateJournalRequest
        {
            Description = "Ouverture",
            ValueDate = new DateOnly(2026, 1, 2),
            Lines =
            [
                new CreateJournalLineRequest { GlAccountId = SeedGuids.Gl("1010"), Debit = 75_000m, Credit = 0m },
                new CreateJournalLineRequest { GlAccountId = SeedGuids.Gl("1110"), Debit = 425_000m, Credit = 0m },
                new CreateJournalLineRequest { GlAccountId = SeedGuids.Gl("3010"), Debit = 0m, Credit = 500_000m }
            ]
        }, "rep-open");
        Assert.True(posted.IsSuccess, posted.ErrorMessage);
    }

    public async Task PostDepositAsync(decimal amount)
    {
        var posted = await Journals.PostAsync(new CreateJournalRequest
        {
            Description = "Dépôt en espèces A-000099",
            ValueDate = new DateOnly(2026, 1, 2),
            Lines =
            [
                new CreateJournalLineRequest { GlAccountId = SeedGuids.Gl("1010"), Debit = amount, Credit = 0m },
                new CreateJournalLineRequest { GlAccountId = SeedGuids.Gl("2010"), Debit = 0m, Credit = amount }
            ]
        }, $"rep-dep-{amount}");
        Assert.True(posted.IsSuccess, posted.ErrorMessage);
        Db.SavingsLedgerEntries.Add(new SavingsLedgerEntry
        {
            TenantId = SeedGuids.TenantId,
            SavingsAccountId = Guid.Parse("0c0ec0de-0001-4000-a000-000000000399"),
            ValueDateUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            PostedAtUtc = Clock.UtcNow,
            EntryType = "Credit",
            Amount = amount,
            CurrencyCode = Currencies.Htg,
            Description = "Dépôt en espèces",
            JournalEntryId = posted.Value!.Id
        });
        await Db.SaveChangesAsync();
    }

    public void Dispose() => Db.Dispose();

    private static GlAccount Gl(string code, string name, GlAccountType type, NormalBalance nb) => new()
    {
        Id = SeedGuids.Gl(code),
        TenantId = SeedGuids.TenantId,
        Code = code,
        NameFr = name,
        NameHt = name,
        NameEn = name,
        AccountType = type,
        NormalBalance = nb,
        CurrencyCode = Currencies.Htg,
        IsPostable = true,
        IsActive = true,
        CreatedAtUtc = new DateTime(2026, 1, 2, 13, 0, 0, DateTimeKind.Utc)
    };
}
