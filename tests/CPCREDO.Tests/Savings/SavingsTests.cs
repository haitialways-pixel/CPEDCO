using System.Text;
using CPCREDO.Application.Savings;
using CPCREDO.Domain.Accounting;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Domain.Members;
using CPCREDO.Domain.Savings;
using CPCREDO.Domain.Tenancy;
using CPCREDO.Infrastructure.Audit;
using CPCREDO.Infrastructure.Persistence;
using CPCREDO.Infrastructure.Savings;
using CPCREDO.Tests.Accounting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CPCREDO.Tests.Savings;

public sealed class SavingsTests
{
    [Fact]
    public async Task Products_are_listed_and_account_opens_at_zero()
    {
        using var harness = new SavingsHarness();

        var products = await harness.Savings.ListProductsAsync();
        Assert.True(products.IsSuccess, products.ErrorMessage);
        Assert.Equal(2, products.Value!.Count);
        Assert.Contains(products.Value, x => x.Name == "Épargne à vue HTG" && x.CurrencyCode == Currencies.Htg);
        Assert.Contains(products.Value, x => x.Name == "Épargne à vue USD" && x.CurrencyCode == Currencies.Usd);

        var member = await harness.CreateMemberAsync("Alice", "Durand", "CIN-S-001");
        var htg = products.Value.Single(x => x.CurrencyCode == Currencies.Htg);
        var opened = await harness.Savings.OpenAccountAsync(member.Id, htg.Id);

        Assert.True(opened.IsSuccess, opened.ErrorMessage);
        Assert.Equal(0m, opened.Value!.LedgerBalance);
        Assert.Equal(0m, opened.Value.AvailableBalance);
        Assert.Equal("A-000001", opened.Value.AccountNo);
        Assert.Equal("AVue", opened.Value.ProductKind);
    }

    [Fact]
    public async Task Catalog_creates_product_and_code_is_immutable()
    {
        using var harness = new SavingsHarness();
        harness.Db.GlAccounts.AddRange(
            new GlAccount
            {
                Id = SeedGuids.Gl("1010"),
                TenantId = SeedGuids.TenantId,
                Code = "1010",
                NameFr = "Caisse",
                NameHt = "Caisse",
                NameEn = "Cash",
                AccountType = GlAccountType.Asset,
                NormalBalance = NormalBalance.Debit,
                CurrencyCode = Currencies.Htg,
                IsPostable = true,
                IsActive = true,
                CreatedAtUtc = harness.Clock.UtcNow
            },
            new GlAccount
            {
                Id = SeedGuids.Gl("2010"),
                TenantId = SeedGuids.TenantId,
                Code = "2010",
                NameFr = "Epargne",
                NameHt = "Epargne",
                NameEn = "Savings",
                AccountType = GlAccountType.Liability,
                NormalBalance = NormalBalance.Credit,
                CurrencyCode = Currencies.Htg,
                IsPostable = true,
                IsActive = true,
                CreatedAtUtc = harness.Clock.UtcNow
            });
        await harness.Db.SaveChangesAsync();
        var products = new SavingsProductService(harness.Db, harness.User, harness.Clock, new AuditLogger(harness.Db, harness.Clock, harness.User));
        var created = await products.CreateAsync(new SaveSavingsProductRequest
        {
            Code = "EAV-2",
            LegalName = "Épargne à vue 2",
            CurrencyCode = "HTG",
            ProductKind = "AVue"
        });
        Assert.True(created.IsSuccess, created.ErrorMessage);
        Assert.Equal("EAV-2", created.Value!.Code);
        Assert.Equal("Épargne à vue 2", created.Value.DisplayName);

        var updated = await products.UpdateAsync(created.Value.Id, new SaveSavingsProductRequest
        {
            Code = "CHANGED",
            LegalName = "Nouveau nom",
            CurrencyCode = "HTG",
            ProductKind = "AVue",
            CommercialName = "Livret"
        });
        Assert.True(updated.IsSuccess, updated.ErrorMessage);
        Assert.Equal("EAV-2", updated.Value!.Code);
        Assert.Equal("Livret", updated.Value.DisplayName);
    }

    [Fact]
    public async Task Usager_cannot_open_qualification_share_account()
    {
        using var harness = new SavingsHarness();
        var member = await harness.CreateMemberAsync("Usa", "Ger", "CIN-U-Q");
        var opened = await harness.Savings.OpenMemberAccountAsync(new OpenMemberAccountRequest
        {
            MemberId = member.Id,
            Kind = "Qualification"
        });
        Assert.False(opened.IsSuccess);
        Assert.Equal("savings.usager_parts", opened.ErrorCode);
    }

    [Fact]
    public async Task Auxiliaire_can_open_permanent_share_account_at_zero()
    {
        using var harness = new SavingsHarness();
        var member = await harness.CreateMemberAsync("Aux", "Iliaire", "CIN-A-P");
        member.LegalStatus = LegalStatus.Auxiliaire;
        await harness.Db.SaveChangesAsync();
        var opened = await harness.Savings.OpenMemberAccountAsync(new OpenMemberAccountRequest
        {
            MemberId = member.Id,
            Kind = "Permanent"
        });
        Assert.True(opened.IsSuccess, opened.ErrorMessage);
        Assert.Equal(0m, opened.Value!.Balance);
        Assert.StartsWith("P-", opened.Value.AccountNo);
    }

    [Fact]
    public async Task Available_balance_is_ledger_minus_liens()
    {
        using var harness = new SavingsHarness();
        var member = await harness.CreateMemberAsync("Bob", "Lefort", "CIN-S-002");
        var product = (await harness.Savings.ListProductsAsync()).Value!.First();
        var opened = await harness.Savings.OpenAccountAsync(member.Id, product.Id);
        Assert.True(opened.IsSuccess, opened.ErrorMessage);

        harness.Db.SavingsLedgerEntries.Add(new SavingsLedgerEntry
        {
            TenantId = SeedGuids.TenantId,
            SavingsAccountId = opened.Value!.Id,
            ValueDateUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            PostedAtUtc = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
            EntryType = "Credit",
            Amount = 5000m,
            CurrencyCode = Currencies.Htg,
            Description = "Solde de test"
        });
        harness.Db.SavingsLiens.Add(new SavingsLien
        {
            TenantId = SeedGuids.TenantId,
            SavingsAccountId = opened.Value.Id,
            CurrencyCode = Currencies.Htg,
            Amount = 1200m,
            Reason = "Gage test",
            CreatedAtUtc = harness.Clock.UtcNow
        });
        await harness.Db.SaveChangesAsync();

        var account = await harness.Savings.GetAccountAsync(opened.Value.Id);
        Assert.True(account.IsSuccess, account.ErrorMessage);
        Assert.Equal(5000m, account.Value!.LedgerBalance);
        Assert.Equal(3800m, account.Value.AvailableBalance);
    }

    [Fact]
    public async Task Statement_filters_by_date_range()
    {
        using var harness = new SavingsHarness();
        var member = await harness.CreateMemberAsync("Claire", "Moreau", "CIN-S-003");
        var product = (await harness.Savings.ListProductsAsync()).Value!.First();
        var opened = await harness.Savings.OpenAccountAsync(member.Id, product.Id);

        harness.Db.SavingsLedgerEntries.AddRange(
            Entry(opened.Value!.Id, new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc), "Credit", 1000m, "Avant période"),
            Entry(opened.Value.Id, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), "Credit", 5000m, "Dépôt"),
            Entry(opened.Value.Id, new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc), "Debit", 200m, "Retrait"));
        await harness.Db.SaveChangesAsync();

        var statement = await harness.Savings.GetStatementAsync(
            opened.Value.Id,
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 2));

        Assert.True(statement.IsSuccess, statement.ErrorMessage);
        Assert.Equal(2, statement.Value!.Entries.Count);
        Assert.Equal(6000m, statement.Value.Entries[0].RunningBalance);
        Assert.Equal(5800m, statement.Value.Entries[1].RunningBalance);
        Assert.Equal(5800m, statement.Value.LedgerBalance);
    }

    [Fact]
    public async Task Livret_pdf_is_amounts_only_and_does_not_stamp()
    {
        using var harness = new SavingsHarness();
        var member = await harness.CreateMemberAsync("Claire", "Moreau", "CIN-S-LIV");
        var product = (await harness.Savings.ListProductsAsync()).Value!.First();
        var opened = await harness.Savings.OpenAccountAsync(member.Id, product.Id);
        harness.Db.SavingsLedgerEntries.AddRange(
            Entry(opened.Value!.Id, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), "Credit", 5000m, "Dépôt"),
            Entry(opened.Value.Id, new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc), "Debit", 200m, "Retrait"));
        await harness.Db.SaveChangesAsync();

        var pdf = await harness.Savings.PrintLivretPdfAsync(opened.Value.Id, null, null);
        Assert.True(pdf.IsSuccess, pdf.ErrorMessage);
        var text = Encoding.UTF8.GetString(pdf.Value!.Content);
        Assert.DoesNotContain("CPCREDO", text);
        Assert.DoesNotContain(member.MemberNo, text);
        Assert.DoesNotContain(opened.Value.AccountNo, text);
        Assert.True(pdf.Value.Content.Length > 200);

        var unstamped = await harness.Savings.GetAccountAsync(opened.Value.Id);
        Assert.Null(unstamped.Value!.LastPassbookPrintAtUtc);
        Assert.All(
            harness.Db.SavingsLedgerEntries.Where(e => e.SavingsAccountId == opened.Value.Id),
            e => Assert.Null(e.PrintedOnLivretAtUtc));
        Assert.StartsWith("livret-", pdf.Value.FileName);
    }

    [Fact]
    public async Task Livret_prints_only_unprinted_lines_and_confirm_prevents_reprint()
    {
        using var harness = new SavingsHarness();
        var member = await harness.CreateMemberAsync("Claire", "Moreau", "CIN-S-LIV2");
        var product = (await harness.Savings.ListProductsAsync()).Value!.First();
        var opened = await harness.Savings.OpenAccountAsync(member.Id, product.Id);
        harness.Db.SavingsLedgerEntries.AddRange(
            Entry(opened.Value!.Id, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), "Credit", 5000m, "Dépôt"),
            Entry(opened.Value.Id, new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc), "Debit", 200m, "Retrait"));
        await harness.Db.SaveChangesAsync();

        var preview = await harness.Savings.GetUnprintedLivretAsync(opened.Value.Id);
        Assert.True(preview.IsSuccess, preview.ErrorMessage);
        Assert.Equal(2, preview.Value!.Lines.Count);
        Assert.Equal(0m, preview.Value.Lines[0].Debit);
        Assert.Equal(5000m, preview.Value.Lines[0].Credit);
        Assert.Equal(5000m, preview.Value.Lines[0].RunningBalance);
        Assert.Equal(200m, preview.Value.Lines[1].Debit);
        Assert.Equal(0m, preview.Value.Lines[1].Credit);
        Assert.Equal(4800m, preview.Value.Lines[1].RunningBalance);

        var ids = preview.Value.Lines.Select(l => l.EntryId!.Value).ToList();
        var confirmed = await harness.Savings.ConfirmLivretPrintAsync(
            opened.Value.Id,
            new ConfirmLivretPrintRequest { EntryIds = ids });
        Assert.True(confirmed.IsSuccess, confirmed.ErrorMessage);
        Assert.Empty(confirmed.Value!.Lines);

        var again = await harness.Savings.GetUnprintedLivretAsync(opened.Value.Id);
        Assert.True(again.IsSuccess, again.ErrorMessage);
        Assert.Empty(again.Value!.Lines);

        harness.Db.SavingsLedgerEntries.Add(
            Entry(opened.Value.Id, new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc), "Credit", 100m, "Nouveau"));
        await harness.Db.SaveChangesAsync();
        var next = await harness.Savings.GetUnprintedLivretAsync(opened.Value.Id);
        Assert.Single(next.Value!.Lines);
        Assert.Equal(100m, next.Value.Lines[0].Credit);
        Assert.Equal(4900m, next.Value.Lines[0].RunningBalance);
    }

    [Fact]
    public async Task Commissaire_cannot_open_account()
    {
        using var harness = new SavingsHarness();
        var member = await harness.CreateMemberAsync("Dan", "Pierre", "CIN-S-004");
        var product = (await harness.Savings.ListProductsAsync()).Value!.First();
        harness.User.Roles = [RoleNames.Commissaire];

        var opened = await harness.Savings.OpenAccountAsync(member.Id, product.Id);
        Assert.False(opened.IsSuccess);
        Assert.Equal("auth.forbidden", opened.ErrorCode);
    }

    private static SavingsLedgerEntry Entry(Guid accountId, DateTime valueDate, string type, decimal amount, string description) =>
        new()
        {
            TenantId = SeedGuids.TenantId,
            SavingsAccountId = accountId,
            ValueDateUtc = valueDate,
            PostedAtUtc = valueDate.AddHours(8),
            EntryType = type,
            Amount = amount,
            CurrencyCode = Currencies.Htg,
            Description = description
        };
}

internal sealed class SavingsHarness : IDisposable
{
    public CpcredoDbContext Db { get; }
    public SavingsService Savings { get; }
    public TestCurrentUser User { get; }
    public FixedClock Clock { get; }

    public SavingsHarness()
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
            LegalName = Letterhead.LegalName,
            City = Letterhead.City,
            Country = Letterhead.Country,
            ShareParValue = MembershipRules.DefaultShareParValue,
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
        Db.SavingsProducts.AddRange(
            new SavingsProduct
            {
                Id = SeedGuids.SavingsProductHtg,
                TenantId = SeedGuids.TenantId,
                Code = "EAV-HTG",
                LegalName = "Épargne à vue HTG",
                Name = "Épargne à vue HTG",
                CurrencyCode = Currencies.Htg,
                ProductKind = SavingsProductKind.AVue,
                MinimumBalance = 0m,
                LiabilityGlAccountId = SeedGuids.Gl("2010"),
                CashGlAccountId = SeedGuids.Gl("1010"),
                IsActive = true,
                CreatedAtUtc = now
            },
            new SavingsProduct
            {
                Id = SeedGuids.SavingsProductUsd,
                TenantId = SeedGuids.TenantId,
                Code = "EAV-USD",
                LegalName = "Épargne à vue USD",
                Name = "Épargne à vue USD",
                CurrencyCode = Currencies.Usd,
                ProductKind = SavingsProductKind.AVue,
                MinimumBalance = 0m,
                LiabilityGlAccountId = SeedGuids.Gl("2020"),
                CashGlAccountId = SeedGuids.Gl("1020"),
                IsActive = true,
                CreatedAtUtc = now
            });
        Db.SaveChanges();

        User = new TestCurrentUser();
        Clock = new FixedClock();
        Savings = new SavingsService(Db, User, Clock, new AuditLogger(Db, Clock, User));
    }

    public async Task<Member> CreateMemberAsync(string firstName, string lastName, string cin)
    {
        var member = new Member
        {
            Id = Guid.NewGuid(),
            TenantId = SeedGuids.TenantId,
            BranchId = SeedGuids.BranchId,
            MemberNo = $"M-{Guid.NewGuid():N}"[..12],
            FirstName = firstName,
            LastName = lastName,
            Cin = cin,
            Phone = "+509 2812 0000",
            AddressLine = "Rue Test",
            City = Letterhead.City,
            Status = MemberStatus.Active,
            KycStatus = KycStatus.Verified,
            CreatedAtUtc = Clock.UtcNow
        };
        Db.Members.Add(member);
        await Db.SaveChangesAsync();
        return member;
    }

    public void Dispose() => Db.Dispose();
}
