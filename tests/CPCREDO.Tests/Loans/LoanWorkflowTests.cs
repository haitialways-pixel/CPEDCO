using CPCREDO.Application.Loans;
using CPCREDO.Application.Teller;
using CPCREDO.Domain.Accounting;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Domain.Loans;
using CPCREDO.Domain.Members;
using CPCREDO.Domain.Savings;
using CPCREDO.Domain.Tenancy;
using CPCREDO.Infrastructure.Accounting;
using CPCREDO.Infrastructure.Audit;
using CPCREDO.Infrastructure.Loans;
using CPCREDO.Infrastructure.Persistence;
using CPCREDO.Infrastructure.Savings;
using CPCREDO.Infrastructure.Teller;
using CPCREDO.Tests.Accounting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CPCREDO.Tests.Loans;

public sealed class LoanWorkflowTests
{
    [Fact]
    public async Task Fixture_schedule_and_day_45_payoff_via_services()
    {
        using var h = new LoanHarness();
        h.AsAdmin();
        var product = await h.CreateDefaultProductAsync();
        var preview = await h.Loans.PreviewScheduleAsync(new PreviewLoanRequest
        {
            ProductId = product.Id,
            Principal = 10_000m,
            AgreedRatePercent = 20m,
            StartDate = new DateOnly(2026, 1, 5)
        });
        Assert.True(preview.IsSuccess, preview.ErrorMessage);
        Assert.Equal(2_000m, preview.Value!.TotalInterest);
        Assert.Equal(12_000m, preview.Value.TotalDue);
        Assert.Equal(12, preview.Value.Installments.Count);
        Assert.All(preview.Value.Installments, line => Assert.Equal(1_000m, line.TotalDue));
        Assert.Equal(10_000m, preview.Value.Installments.Sum(l => l.PrincipalDue));
        Assert.Equal(2_000m, preview.Value.Installments.Sum(l => l.InterestDue));

        var payoff = await h.Loans.PreviewPayoffAsync(
            new PreviewLoanRequest { ProductId = product.Id, Principal = 10_000m, AgreedRatePercent = 20m },
            daysElapsed: 45);
        Assert.True(payoff.IsSuccess, payoff.ErrorMessage);
        Assert.Equal(1_000m, payoff.Value!.InterestDue);
        Assert.Equal(11_000m, payoff.Value.TotalDue);
        Assert.False(payoff.Value.ChargesFullFlatInterest);
    }

    [Fact]
    public async Task Draft_loan_stores_typed_rate_and_factory_schedule_without_journal()
    {
        using var h = new LoanHarness();
        h.AsOfficer();
        var product = await h.SeedProductAsync();
        var created = await h.Loans.CreateDraftAsync(new CreateLoanRequest
        {
            MemberId = h.MemberId,
            ProductId = product.Id,
            Principal = 10_000m,
            AgreedRatePercent = 20m
        });
        Assert.True(created.IsSuccess, created.ErrorMessage);
        Assert.Equal(nameof(LoanStatus.Draft), created.Value!.Status);
        Assert.Equal("LN-000001", created.Value.LoanNo);
        Assert.Equal(20m, created.Value.AgreedRatePercent);
        Assert.Equal(90, created.Value.RateAppliesToTermDays);
        Assert.Equal(2_000m, created.Value.TotalInterest);
        Assert.Equal(12_000m, created.Value.TotalDue);
        Assert.Equal(1, created.Value.CycleNumber);
        Assert.Null(created.Value.RenewedFromLoanId);
        Assert.Null(created.Value.RenewedToLoanId);
        Assert.Equal(12, created.Value.Schedule.Installments.Count);
        Assert.All(created.Value.Schedule.Installments, line => Assert.Equal(1_000m, line.TotalDue));
        Assert.Equal(0, await h.Db.JournalEntries.CountAsync());
        Assert.Equal(1, await h.Db.LoanProducts.CountAsync());
    }

    [Fact]
    public async Task Product_code_is_immutable_and_display_prefers_commercial_name()
    {
        using var h = new LoanHarness();
        h.AsAdmin();
        var created = await h.Products.CreateAsync(h.ProductRequest(code: "ST90", legal: "Crédit court terme", commercial: "Court terme"));
        Assert.True(created.IsSuccess, created.ErrorMessage);
        Assert.Equal("Court terme", created.Value!.DisplayName);

        var updated = await h.Products.UpdateAsync(created.Value.Id, h.ProductRequest(code: "CHANGED", legal: "Crédit 90 jours", commercial: ""));
        Assert.True(updated.IsSuccess, updated.ErrorMessage);
        Assert.Equal("ST90", updated.Value!.Code);
        Assert.Equal("Crédit 90 jours", updated.Value.DisplayName);
        Assert.Null(updated.Value.CommercialName);
    }

    [Fact]
    public async Task Officer_and_caissier_cannot_manage_products()
    {
        using var h = new LoanHarness();
        h.AsOfficer();
        var asOfficer = await h.Products.CreateAsync(h.ProductRequest());
        Assert.False(asOfficer.IsSuccess);
        Assert.Equal("auth.forbidden", asOfficer.ErrorCode);

        h.AsCaissier();
        var asCashier = await h.Products.CreateAsync(h.ProductRequest());
        Assert.False(asCashier.IsSuccess);
        Assert.Equal("auth.forbidden", asCashier.ErrorCode);
    }

    [Fact]
    public async Task Deactivate_hides_product_from_active_list_and_does_not_delete()
    {
        using var h = new LoanHarness();
        h.AsGerant();
        var created = await h.Products.CreateAsync(h.ProductRequest(code: "P1"));
        Assert.True(created.IsSuccess, created.ErrorMessage);

        h.AsOfficer();
        var draft = await h.Loans.CreateDraftAsync(new CreateLoanRequest
        {
            MemberId = h.MemberId,
            ProductId = created.Value!.Id,
            Principal = 10_000m,
            AgreedRatePercent = 20m
        });
        Assert.True(draft.IsSuccess, draft.ErrorMessage);

        h.AsGerant();
        var deactivated = await h.Products.DeactivateAsync(created.Value.Id);
        Assert.True(deactivated.IsSuccess, deactivated.ErrorMessage);
        Assert.False(deactivated.Value!.IsActive);
        Assert.Equal(1, await h.Db.LoanProducts.CountAsync());
        Assert.Equal(1, await h.Db.Loans.CountAsync());

        var active = await h.Products.ListAsync(activeOnly: true);
        Assert.Empty(active.Value!);
        var all = await h.Products.ListAsync(activeOnly: false);
        Assert.Single(all.Value!);
    }

    [Fact]
    public async Task Full_flat_early_payoff_flag_charges_entire_term_interest()
    {
        using var h = new LoanHarness();
        h.AsAdmin();
        var request = h.ProductRequest();
        request.EarlyPayoffChargesFullFlatInterest = true;
        var created = await h.Products.CreateAsync(request);
        var payoff = await h.Loans.PreviewPayoffAsync(
            new PreviewLoanRequest { ProductId = created.Value!.Id, Principal = 10_000m, AgreedRatePercent = 20m },
            45);
        Assert.Equal(2_000m, payoff.Value!.InterestDue);
        Assert.Equal(12_000m, payoff.Value.TotalDue);
        Assert.True(payoff.Value.ChargesFullFlatInterest);
    }
}

internal sealed class LoanHarness : IDisposable
{
    public CpcredoDbContext Db { get; }
    public LoanProductService Products { get; }
    public LoanService Loans { get; }
    public TestCurrentUser User { get; }
    public Guid MemberId { get; } = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeee0001");

    public LoanHarness()
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
        Db.Members.Add(new Member
        {
            Id = MemberId,
            TenantId = SeedGuids.TenantId,
            BranchId = SeedGuids.BranchId,
            MemberNo = "M-000001",
            FirstName = "Anne",
            LastName = "Lamothe",
            Phone = "30000000",
            AddressLine = "Pétion-Ville",
            City = "Pétion-Ville",
            Status = MemberStatus.Active,
            KycStatus = KycStatus.Verified,
            LegalStatus = LegalStatus.Societaire,
            CreatedAtUtc = now
        });
        Db.Users.AddRange(
            new User
            {
                Id = SeedGuids.GerantUserId,
                TenantId = SeedGuids.TenantId,
                BranchId = SeedGuids.BranchId,
                Username = "gerant",
                Email = "gerant@cpcredo.ht",
                FullName = "Gérant CPCREDO",
                PasswordHash = "hash",
                CreatedAtUtc = now
            },
            new User
            {
                Id = SeedGuids.CaissierUserId,
                TenantId = SeedGuids.TenantId,
                BranchId = SeedGuids.BranchId,
                Username = "caissier",
                Email = "caissier@cpcredo.ht",
                FullName = "Caissier CPCREDO",
                PasswordHash = "hash",
                CreatedAtUtc = now
            });
        Db.GlAccounts.AddRange(
            Gl("1010", GlAccountType.Asset, NormalBalance.Debit),
            Gl("1210", GlAccountType.Asset, NormalBalance.Debit),
            Gl("2010", GlAccountType.Liability, NormalBalance.Credit),
            Gl("4010", GlAccountType.Income, NormalBalance.Credit),
            Gl("4020", GlAccountType.Income, NormalBalance.Credit));
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

        User = new TestCurrentUser();
        var clock = new FixedClock { UtcNow = now };
        var audit = new AuditLogger(Db, clock, User);
        var journals = new JournalService(Db, User, clock, audit);
        Products = new LoanProductService(Db, User, clock, audit);
        Loans = new LoanService(Db, User, clock, audit, journals);
        Savings = new SavingsService(Db, User, clock, audit);
        Teller = new TellerService(Db, User, clock, journals, audit);
    }

    public SavingsService Savings { get; }
    public TellerService Teller { get; }

    public void AsAdmin()
    {
        User.UserId = SeedGuids.AdminUserId;
        User.Username = "admin";
        User.Roles = [RoleNames.Admin];
    }

    public void AsGerant()
    {
        User.UserId = SeedGuids.GerantUserId;
        User.Username = "gerant";
        User.Roles = [RoleNames.Gerant];
    }

    public void AsOfficer()
    {
        User.UserId = SeedGuids.AdminUserId;
        User.Username = "officer";
        User.Roles = [RoleNames.OfficierCredit];
    }

    public void AsCaissier()
    {
        User.UserId = SeedGuids.CaissierUserId;
        User.Username = "caissier";
        User.Roles = [RoleNames.Caissier];
    }

    private static GlAccount Gl(string code, GlAccountType type, NormalBalance nb) => new()
    {
        Id = SeedGuids.Gl(code),
        TenantId = SeedGuids.TenantId,
        Code = code,
        NameFr = code,
        NameHt = code,
        NameEn = code,
        AccountType = type,
        NormalBalance = nb,
        CurrencyCode = Currencies.Htg,
        IsPostable = true,
        IsActive = true,
        CreatedAtUtc = new DateTime(2026, 1, 2, 13, 0, 0, DateTimeKind.Utc)
    };

    public SaveLoanProductRequest ProductRequest(
        string code = "ST90",
        string legal = "Crédit court terme 90 jours",
        string? commercial = null) =>
        new()
        {
            Code = code,
            LegalName = legal,
            CommercialName = commercial,
            SmsName = "CT90",
            TermDays = 90,
            InstallmentCount = 12,
            RepaymentFrequency = "Weekly",
            DefaultRatePercent = 20m,
            CompulsorySavingsPercent = 10m,
            IsActive = true
        };

    public async Task<LoanProductDto> CreateDefaultProductAsync()
    {
        var created = await Products.CreateAsync(ProductRequest());
        Assert.True(created.IsSuccess, created.ErrorMessage);
        return created.Value!;
    }

    public async Task<LoanProductDto> SeedProductAsync(decimal? officerMax = null)
    {
        var previousRoles = User.Roles;
        var previousId = User.UserId;
        AsAdmin();
        var request = ProductRequest();
        request.OfficerMaxApproval = officerMax;
        var created = await Products.CreateAsync(request);
        Assert.True(created.IsSuccess, created.ErrorMessage);
        User.Roles = previousRoles;
        User.UserId = previousId;
        return created.Value!;
    }

    public void Dispose() => Db.Dispose();
}
