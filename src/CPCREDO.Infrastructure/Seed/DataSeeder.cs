using CPCREDO.Domain.Accounting;
using CPCREDO.Domain.Audit;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Domain.Members;
using CPCREDO.Domain.Savings;
using CPCREDO.Domain.Tenancy;
using CPCREDO.Domain.Treasury;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CPCREDO.Infrastructure.Seed;

public sealed class DataSeeder
{
    private static readonly DateTime SeedInstant = new(2026, 1, 2, 13, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly OpeningValueDate = new(2026, 1, 2);

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var db = services.GetRequiredService<CpcredoDbContext>();
        var config = services.GetRequiredService<IConfiguration>();
        var logger = services.GetRequiredService<ILogger<DataSeeder>>();

        if (await db.Tenants.AnyAsync(t => t.Id == SeedGuids.TenantId, cancellationToken))
        {
            var existing = await db.Tenants.FirstAsync(t => t.Id == SeedGuids.TenantId, cancellationToken);
            if (existing.Sigle != Letterhead.Sigle)
            {
                existing.Sigle = Letterhead.Sigle;
            }

            if (existing.ShareParValue == 0m)
                existing.ShareParValue = MembershipRules.DefaultShareParValue;

            var existingAdmin = await db.Users.FirstOrDefaultAsync(u => u.Id == SeedGuids.AdminUserId, cancellationToken);
            if (existingAdmin is not null && existingAdmin.FullName != "Administrateur CPCREDO")
                existingAdmin.FullName = "Administrateur CPCREDO";

            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
            await EnsureChartOfAccountsAsync(db, cancellationToken);
            await EnsureSavingsProductsAsync(db, logger, cancellationToken);
            await EnsureMembershipClassesAsync(db, logger, cancellationToken);
            await EnsureTreasuryAsync(db, config, logger, cancellationToken);
            logger.LogInformation("CPCREDO seed already present.");
            return;
        }

        logger.LogInformation("Seeding CPCREDO tenant, siège, roles, admin, and chart of accounts.");

        var tenant = new Tenant
        {
            Id = SeedGuids.TenantId,
            Sigle = Letterhead.Sigle,
            LegalName = Letterhead.LegalName,
            City = Letterhead.City,
            Country = Letterhead.Country,
            PrimaryCurrency = Currencies.Htg,
            SecondaryCurrency = Currencies.Usd,
            DisplayTimeZone = CpcredoTimeZone.DisplayId,
            ShareParValue = MembershipRules.DefaultShareParValue,
            IsActive = true,
            CreatedAtUtc = SeedInstant
        };

        var branch = new Branch
        {
            Id = SeedGuids.BranchId,
            TenantId = tenant.Id,
            Code = Letterhead.DefaultBranchCode,
            Name = Letterhead.DefaultBranchName,
            City = Letterhead.City,
            Country = Letterhead.Country,
            IsHeadquarters = true,
            IsActive = true,
            CreatedAtUtc = SeedInstant
        };

        db.Tenants.Add(tenant);
        db.Branches.Add(branch);
        await db.SaveChangesAsync(cancellationToken);

        tenant.DefaultBranchId = branch.Id;
        await db.SaveChangesAsync(cancellationToken);

        var roles = CreateRoles();
        db.Roles.AddRange(roles);

        var adminPassword = config["Seed:AdminPassword"] ?? "Admin@Cpcredo2026";
        var adminUsername = config["Seed:AdminUsername"] ?? "admin";
        var hasher = new PasswordHasher<User>();
        var admin = new User
        {
            Id = SeedGuids.AdminUserId,
            TenantId = tenant.Id,
            BranchId = branch.Id,
            Username = adminUsername,
            Email = "admin@cpcredo.ht",
            FullName = "Administrateur CPCREDO",
            IsActive = true,
            CreatedAtUtc = SeedInstant
        };
        admin.PasswordHash = hasher.HashPassword(admin, adminPassword);
        db.Users.Add(admin);
        db.UserRoles.Add(new UserRole { UserId = admin.Id, RoleId = SeedGuids.RoleAdmin });
        await db.SaveChangesAsync(cancellationToken);

        var accounts = CreateChartOfAccounts();
        db.GlAccounts.AddRange(accounts);
        await db.SaveChangesAsync(cancellationToken);

        var products = CreateSavingsProducts(tenant.Id);
        db.SavingsProducts.AddRange(products);
        await db.SaveChangesAsync(cancellationToken);

        var opening = CreateOpeningJournal();
        opening.EnsureBalanced();
        db.JournalEntries.Add(opening);

        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserId = admin.Id,
            OccurredAtUtc = SeedInstant,
            Action = "Seed.Initial",
            EntityType = nameof(Tenant),
            EntityId = tenant.Id,
            DetailsJson = """{"message":"CPCREDO tenant, siège, admin, plan comptable et journal d'ouverture."}"""
        });

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        await EnsureSavingsProductsAsync(db, logger, cancellationToken);
        await EnsureMembershipClassesAsync(db, logger, cancellationToken);
        await EnsureTreasuryAsync(db, config, logger, cancellationToken);
        logger.LogInformation("CPCREDO seed completed. Admin user: {Username}", adminUsername);
    }

    private static async Task EnsureSavingsProductsAsync(CpcredoDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        var existing = await db.SavingsProducts.AsNoTracking().Where(x => x.TenantId == SeedGuids.TenantId).ToListAsync(cancellationToken);
        if (existing.Count >= 2)
            return;

        var now = SeedInstant;
        var products = CreateSavingsProducts(SeedGuids.TenantId, now);
        db.SavingsProducts.AddRange(products.Where(x => existing.All(e => e.Id != x.Id && e.Name != x.Name)));
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded savings products for CPCREDO tenant.");
    }


    private static async Task EnsureMembershipClassesAsync(
        CpcredoDbContext db,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var par = MembershipRules.DefaultShareParValue;
        var now = SeedInstant;
        var specs = MembershipClassSeed.All();
        var existing = await db.Members
            .Include(m => m.ShareAccounts)
            .Where(m => m.TenantId == SeedGuids.TenantId)
            .ToListAsync(cancellationToken);
        var byId = existing.ToDictionary(m => m.Id);

        foreach (var spec in specs)
        {
            var created = !byId.TryGetValue(spec.Id, out var member);
            if (created)
            {
                member = new Member
                {
                    Id = spec.Id,
                    TenantId = SeedGuids.TenantId,
                    BranchId = SeedGuids.BranchId,
                    MemberNo = spec.MemberNo,
                    FirstName = spec.FirstName,
                    LastName = spec.LastName,
                    Cin = spec.Cin,
                    Nif = spec.Nif,
                    Phone = spec.Phone,
                    AddressLine = spec.Address,
                    City = Letterhead.City,
                    Commune = Letterhead.City,
                    CreatedAtUtc = now
                };
                db.Members.Add(member);
                byId[spec.Id] = member;
            }

            var backfill = created
                || (member!.UpdatedAtUtc is null
                    && member.LegalStatus == LegalStatus.Usager
                    && spec.LegalStatus != LegalStatus.Usager);

            if (backfill)
            {
                member.FirstName = spec.FirstName;
                member.LastName = spec.LastName;
                member.Status = MemberStatus.Active;
                member.KycStatus = spec.LegalStatus == LegalStatus.Usager ? KycStatus.Pending : KycStatus.Verified;
                member.LegalStatus = spec.LegalStatus;
                member.IsFounder = spec.FounderGroup is not null;
                member.FounderGroup = spec.FounderGroup;
                member.ProbationDays = MembershipRules.DefaultProbationDays;
                member.UsagerSinceUtc = spec.LegalStatus == LegalStatus.Usager
                    ? DateTime.SpecifyKind(DateTime.UtcNow.AddDays(-spec.UsagerDaysElapsed), DateTimeKind.Utc)
                    : null;

                UpsertShare(db, member, ShareType.Qualification, spec.QualShareId, spec.QualAccountNo, spec.QualificationShares, par, now);
                if (spec.PermanentShares > 0 && spec.PermShareId is { } permId && spec.PermAccountNo is { } permNo)
                    UpsertShare(db, member, ShareType.Permanent, permId, permNo, spec.PermanentShares, par, now);
            }
            else if (spec.PermanentShares > 0
                     && spec.PermShareId is { } existingPermId
                     && spec.PermAccountNo is { } existingPermNo
                     && member!.ShareAccounts.All(s => s.ShareType != ShareType.Permanent))
            {
                UpsertShare(db, member, ShareType.Permanent, existingPermId, existingPermNo, spec.PermanentShares, par, now);
            }
        }

        await EnsureSequenceAsync(db, "MemberNo", specs.Max(s => int.Parse(s.MemberNo[2..])), cancellationToken);
        await EnsureSequenceAsync(db, "ShareAccountNo", specs.Max(s => int.Parse(s.QualAccountNo[2..])), cancellationToken);
        var permMax = specs.Where(s => s.PermAccountNo is not null).Select(s => int.Parse(s.PermAccountNo![2..])).DefaultIfEmpty(0).Max();
        await EnsureSequenceAsync(db, "PermanentShareAccountNo", permMax, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Membership classes: {Capital} capital founders, {Qual} qualifying founders, {Usager} usagers, {Ordinary} ordinary sociétaires.",
            specs.Count(s => s.FounderGroup == FounderGroup.CapitalFounder),
            specs.Count(s => s.FounderGroup == FounderGroup.QualifyingFounder),
            specs.Count(s => s.LegalStatus == LegalStatus.Usager),
            specs.Count(s => s.LegalStatus == LegalStatus.Societaire && s.FounderGroup is null));
    }

    private static void UpsertShare(
        CpcredoDbContext db,
        Member member,
        ShareType type,
        Guid id,
        string accountNo,
        int count,
        decimal par,
        DateTime now)
    {
        var account = member.ShareAccounts.FirstOrDefault(s => s.ShareType == type);
        if (account is null)
        {
            account = new ShareAccount
            {
                Id = id,
                TenantId = SeedGuids.TenantId,
                MemberId = member.Id,
                BranchId = SeedGuids.BranchId,
                AccountNo = accountNo,
                ShareType = type,
                OpenedAtUtc = now
            };
            member.ShareAccounts.Add(account);
            db.ShareAccounts.Add(account);
        }

        account.ShareCount = count;
        account.ParValue = par;
        account.CurrencyCode = Currencies.Htg;
        account.IsActive = true;
    }

    private static async Task EnsureSequenceAsync(
        CpcredoDbContext db,
        string key,
        int lastValue,
        CancellationToken cancellationToken)
    {
        var sequence = await db.NumberSequences
            .FirstOrDefaultAsync(s => s.TenantId == SeedGuids.TenantId && s.Key == key, cancellationToken);
        if (sequence is null)
        {
            db.NumberSequences.Add(new NumberSequence
            {
                Id = Guid.NewGuid(),
                TenantId = SeedGuids.TenantId,
                Key = key,
                LastValue = lastValue
            });
            return;
        }

        if (sequence.LastValue < lastValue)
            sequence.LastValue = lastValue;
    }

    private static List<SavingsProduct> CreateSavingsProducts(Guid tenantId, DateTime? createdAtUtc = null)
    {
        var now = createdAtUtc ?? SeedInstant;
        return
        [
            new SavingsProduct
            {
                Id = SeedGuids.SavingsProductHtg,
                TenantId = tenantId,
                Name = "Épargne à vue HTG",
                CurrencyCode = Currencies.Htg,
                MinimumBalance = 0m,
                LiabilityGlAccountId = SeedGuids.Gl("2010"),
                CashGlAccountId = SeedGuids.Gl("1010"),
                IsActive = true,
                CreatedAtUtc = now
            },
            new SavingsProduct
            {
                Id = SeedGuids.SavingsProductUsd,
                TenantId = tenantId,
                Name = "Épargne à vue USD",
                CurrencyCode = Currencies.Usd,
                MinimumBalance = 0m,
                LiabilityGlAccountId = SeedGuids.Gl("2020"),
                CashGlAccountId = SeedGuids.Gl("1020"),
                IsActive = true,
                CreatedAtUtc = now
            }
        ];
    }

    private static List<Role> CreateRoles() =>
    [
        Role(SeedGuids.RoleAdmin, RoleNames.Admin, "Administrateur", "Administratè", "Administrator",
            "Accès complet à la configuration et à la supervision.", isReadOnly: false),
        Role(SeedGuids.RoleGerant, RoleNames.Gerant, "Gérant", "Jeran", "Manager",
            "Direction de l’agence et validation des opérations.", isReadOnly: false),
        Role(SeedGuids.RoleCaissier, RoleNames.Caissier, "Caissier", "Kesye", "Teller",
            "Opérations de caisse (dépôts, retraits, encaissements).", isReadOnly: false),
        Role(SeedGuids.RoleOfficierCredit, RoleNames.OfficierCredit, "Officier de crédit", "Ofisye kredi", "Credit officer",
            "Dossiers de crédit et suivi des prêts.", isReadOnly: false),
        Role(SeedGuids.RoleServiceClient, RoleNames.ServiceClient, "Service client", "Sèvis kliyan", "Member services",
            "Accueil des membres et mise à jour des dossiers.", isReadOnly: false),
        Role(SeedGuids.RoleCommissaire, RoleNames.Commissaire, "Commissaire", "Komisè", "Supervisor (read-only)",
            "Contrôle interne en lecture seule.", isReadOnly: true)
    ];

    private static Role Role(
        Guid id,
        string name,
        string fr,
        string ht,
        string en,
        string descriptionFr,
        bool isReadOnly) =>
        new()
        {
            Id = id,
            Name = name,
            DisplayNameFr = fr,
            DisplayNameHt = ht,
            DisplayNameEn = en,
            DescriptionFr = descriptionFr,
            IsReadOnly = isReadOnly,
            IsSystem = true,
            CreatedAtUtc = SeedInstant
        };

    private static List<GlAccount> CreateChartOfAccounts()
    {
        var t = SeedGuids.TenantId;
        Guid Id(string code) => SeedGuids.Gl(code);

        GlAccount A(
            string code,
            string? parent,
            string fr,
            string ht,
            string en,
            GlAccountType type,
            NormalBalance nb,
            string currency,
            bool postable) =>
            new()
            {
                Id = Id(code),
                TenantId = t,
                ParentId = parent is null ? null : Id(parent),
                Code = code,
                NameFr = fr,
                NameHt = ht,
                NameEn = en,
                AccountType = type,
                NormalBalance = nb,
                CurrencyCode = currency,
                IsPostable = postable,
                IsActive = true,
                CreatedAtUtc = SeedInstant
            };

        return
        [
            A("1", null, "Actif", "Aktif", "Assets", GlAccountType.Asset, NormalBalance.Debit, Currencies.Htg, false),
            A("10", "1", "Disponibilités", "Lajan disponib", "Cash and banks", GlAccountType.Asset, NormalBalance.Debit, Currencies.Htg, false),
            A("1010", "10", "Caisse HTG", "Kes HTG", "Cash HTG", GlAccountType.Asset, NormalBalance.Debit, Currencies.Htg, true),
            A("1020", "10", "Caisse USD", "Kes USD", "Cash USD", GlAccountType.Asset, NormalBalance.Debit, Currencies.Usd, true),
            A("1030", "10", "Coffre HTG", "Kòf HTG", "Vault HTG", GlAccountType.Asset, NormalBalance.Debit, Currencies.Htg, true),
            A("1031", "10", "Coffre USD", "Kòf USD", "Vault USD", GlAccountType.Asset, NormalBalance.Debit, Currencies.Usd, true),
            A("1110", "10", "Banque HTG", "Bank HTG", "Bank HTG", GlAccountType.Asset, NormalBalance.Debit, Currencies.Htg, true),
            A("1111", "10", "Banque Unibank HTG", "Bank Unibank HTG", "Unibank HTG", GlAccountType.Asset, NormalBalance.Debit, Currencies.Htg, true),
            A("1120", "10", "Banque USD", "Bank USD", "Bank USD", GlAccountType.Asset, NormalBalance.Debit, Currencies.Usd, true),
            A("12", "1", "Prêts aux membres", "Pre manm yo", "Loans to members", GlAccountType.Asset, NormalBalance.Debit, Currencies.Htg, false),
            A("1210", "12", "Prêts à court terme HTG", "Pre kout tèm HTG", "Short-term loans HTG", GlAccountType.Asset, NormalBalance.Debit, Currencies.Htg, true),
            A("1220", "12", "Prêts à moyen et long terme HTG", "Pre long tèm HTG", "Medium/long-term loans HTG", GlAccountType.Asset, NormalBalance.Debit, Currencies.Htg, true),
            A("1290", "12", "Provision pour créances douteuses", "Pwovizyon move pre", "Allowance for loan losses", GlAccountType.Asset, NormalBalance.Credit, Currencies.Htg, true),
            A("13", "1", "Autres actifs", "Lòt aktif", "Other assets", GlAccountType.Asset, NormalBalance.Debit, Currencies.Htg, false),
            A("1310", "13", "Immobilisations", "Imobilye", "Fixed assets", GlAccountType.Asset, NormalBalance.Debit, Currencies.Htg, true),
            A("1390", "13", "Amortissements cumulés", "Amòtisaman akimile", "Accumulated depreciation", GlAccountType.Asset, NormalBalance.Credit, Currencies.Htg, true),

            A("2", null, "Passif", "Pasif", "Liabilities", GlAccountType.Liability, NormalBalance.Credit, Currencies.Htg, false),
            A("20", "2", "Épargne des membres", "Epay manm yo", "Member savings", GlAccountType.Liability, NormalBalance.Credit, Currencies.Htg, false),
            A("2010", "20", "Épargne à vue HTG", "Epay vizib HTG", "Demand savings HTG", GlAccountType.Liability, NormalBalance.Credit, Currencies.Htg, true),
            A("2020", "20", "Épargne à vue USD", "Epay vizib USD", "Demand savings USD", GlAccountType.Liability, NormalBalance.Credit, Currencies.Usd, true),
            A("2110", "20", "Épargne à terme HTG", "Epay tèm HTG", "Term deposits HTG", GlAccountType.Liability, NormalBalance.Credit, Currencies.Htg, true),
            A("2120", "20", "Épargne à terme USD", "Epay tèm USD", "Term deposits USD", GlAccountType.Liability, NormalBalance.Credit, Currencies.Usd, true),
            A("22", "2", "Autres passifs", "Lòt pasif", "Other liabilities", GlAccountType.Liability, NormalBalance.Credit, Currencies.Htg, false),
            A("2210", "22", "Intérêts à payer", "Enterè pou peye", "Interest payable", GlAccountType.Liability, NormalBalance.Credit, Currencies.Htg, true),
            A("2220", "22", "Charges à payer", "Depans pou peye", "Accrued expenses", GlAccountType.Liability, NormalBalance.Credit, Currencies.Htg, true),

            A("3", null, "Capitaux propres", "Kapital pwòp", "Equity", GlAccountType.Equity, NormalBalance.Credit, Currencies.Htg, false),
            A("3010", "3", "Parts de qualification", "Pataj kalifikasyon", "Qualification shares", GlAccountType.Equity, NormalBalance.Credit, Currencies.Htg, true),
            A("3011", "3", "Parts permanentes", "Pataj pèmanan", "Permanent shares", GlAccountType.Equity, NormalBalance.Credit, Currencies.Htg, true),
            A("3020", "3", "Réserves légales", "Rezèv legal", "Legal reserves", GlAccountType.Equity, NormalBalance.Credit, Currencies.Htg, true),
            A("3030", "3", "Résultat de l’exercice", "Rezilta egzèsis la", "Current year earnings", GlAccountType.Equity, NormalBalance.Credit, Currencies.Htg, true),
            A("3040", "3", "Résultats reportés", "Rezilta reporte", "Retained earnings", GlAccountType.Equity, NormalBalance.Credit, Currencies.Htg, true),

            A("4", null, "Produits", "Revni", "Income", GlAccountType.Income, NormalBalance.Credit, Currencies.Htg, false),
            A("4010", "4", "Intérêts sur prêts", "Enterè sou pre", "Interest income on loans", GlAccountType.Income, NormalBalance.Credit, Currencies.Htg, true),
            A("4020", "4", "Frais et commissions", "Frè ak komisyon", "Fees and commissions", GlAccountType.Income, NormalBalance.Credit, Currencies.Htg, true),
            A("4030", "4", "Produits divers", "Lòt revni", "Other income", GlAccountType.Income, NormalBalance.Credit, Currencies.Htg, true),
            A("4040", "4", "Écarts de caisse (produits)", "Eka kès (revni)", "Till overage income", GlAccountType.Income, NormalBalance.Credit, Currencies.Htg, true),

            A("5", null, "Charges", "Depans", "Expenses", GlAccountType.Expense, NormalBalance.Debit, Currencies.Htg, false),
            A("5010", "5", "Intérêts sur dépôts", "Enterè sou depo", "Interest expense on deposits", GlAccountType.Expense, NormalBalance.Debit, Currencies.Htg, true),
            A("5020", "5", "Salaires et charges sociales", "Salè ak chaj sosyal", "Salaries and social charges", GlAccountType.Expense, NormalBalance.Debit, Currencies.Htg, true),
            A("5030", "5", "Frais généraux", "Frè jeneral", "Operating expenses", GlAccountType.Expense, NormalBalance.Debit, Currencies.Htg, true),
            A("5035", "5", "Frais bancaires HTG", "Frè bank HTG", "Bank fees HTG", GlAccountType.Expense, NormalBalance.Debit, Currencies.Htg, true),
            A("5036", "5", "Frais bancaires USD", "Frè bank USD", "Bank fees USD", GlAccountType.Expense, NormalBalance.Debit, Currencies.Usd, true),
            A("5040", "5", "Dotations aux provisions", "Dotasyon pwovizyon", "Provision expense", GlAccountType.Expense, NormalBalance.Debit, Currencies.Htg, true),
            A("5050", "5", "Écarts de caisse (charges)", "Eka kès (depans)", "Till shortage expense", GlAccountType.Expense, NormalBalance.Debit, Currencies.Htg, true)
        ];
    }

    private static async Task EnsureChartOfAccountsAsync(CpcredoDbContext db, CancellationToken cancellationToken)
    {
        var expected = CreateChartOfAccounts();
        var existing = await db.GlAccounts.AsNoTracking()
            .Where(a => a.TenantId == SeedGuids.TenantId)
            .Select(a => a.Code)
            .ToListAsync(cancellationToken);

        var missing = expected
            .Where(a => !existing.Contains(a.Code))
            .OrderBy(a => a.ParentId is null ? 0 : 1)
            .ThenBy(a => a.Code)
            .ToList();

        var qualification = await db.GlAccounts.FirstOrDefaultAsync(
            a => a.TenantId == SeedGuids.TenantId && a.Code == MembershipRules.QualificationCapitalGl,
            cancellationToken);
        if (qualification is not null && qualification.NameFr != "Parts de qualification")
        {
            qualification.NameFr = "Parts de qualification";
            qualification.NameHt = "Pataj kalifikasyon";
            qualification.NameEn = "Qualification shares";
            await db.SaveChangesAsync(cancellationToken);
        }

        if (missing.Count == 0)
            return;

        var roots = missing.Where(a => a.ParentId is null).ToList();
        var children = missing.Where(a => a.ParentId is not null).ToList();

        if (roots.Count > 0)
        {
            db.GlAccounts.AddRange(roots);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (children.Count > 0)
        {
            db.GlAccounts.AddRange(children);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task EnsureTreasuryAsync(
        CpcredoDbContext db,
        IConfiguration config,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var hasher = new PasswordHasher<User>();
        await EnsureStaffUserAsync(
            db,
            hasher,
            SeedGuids.GerantUserId,
            "gerant",
            "Gérant CPCREDO",
            "gerant@cpcredo.ht",
            config["Seed:GerantPassword"] ?? "Gerant@Cpcredo2026",
            SeedGuids.RoleGerant,
            cancellationToken);
        await EnsureStaffUserAsync(
            db,
            hasher,
            SeedGuids.CaissierUserId,
            "caissier",
            "Caissier CPCREDO",
            "caissier@cpcredo.ht",
            config["Seed:CaissierPassword"] ?? "Caissier@Cpcredo2026",
            SeedGuids.RoleCaissier,
            cancellationToken);

        var placeholders = await db.BankAccounts
            .Where(b => b.TenantId == SeedGuids.TenantId && b.IsActive)
            .ToListAsync(cancellationToken);
        var deactivated = 0;
        foreach (var bank in placeholders)
        {
            if (!CorrespondentBanks.IsPlaceholderAccountNumber(bank.Number))
                continue;
            bank.IsActive = false;
            deactivated++;
        }

        if (deactivated > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Deactivated {Count} placeholder correspondent bank account(s).", deactivated);
        }
    }

    private static async Task EnsureStaffUserAsync(
        CpcredoDbContext db,
        PasswordHasher<User> hasher,
        Guid id,
        string username,
        string fullName,
        string email,
        string password,
        Guid roleId,
        CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(
                u => u.Id == id || (u.TenantId == SeedGuids.TenantId && u.Username == username),
                cancellationToken))
            return;

        var user = new User
        {
            Id = id,
            TenantId = SeedGuids.TenantId,
            BranchId = SeedGuids.BranchId,
            Username = username,
            Email = email,
            FullName = fullName,
            IsActive = true,
            MustChangePassword = false,
            CreatedAtUtc = SeedInstant
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static JournalEntry CreateOpeningJournal()
    {
        const decimal cash = 75_000.0000m;
        const decimal bank = 425_000.0000m;
        const decimal capital = 500_000.0000m;

        var journal = new JournalEntry
        {
            Id = SeedGuids.OpeningJournalId,
            TenantId = SeedGuids.TenantId,
            BranchId = SeedGuids.BranchId,
            JournalNo = "J-OUV-0001",
            ValueDate = OpeningValueDate,
            PostedAtUtc = SeedInstant,
            PostedByUserId = SeedGuids.AdminUserId,
            Description = "Écriture d’ouverture — capital social CPCREDO (HTG)",
            CurrencyCode = Currencies.Htg,
            Status = JournalStatus.Posted,
            IsReversal = false,
            CreatedAtUtc = SeedInstant,
            Lines =
            [
                new JournalLine
                {
                    Id = SeedGuids.OpeningLineCashId,
                    JournalEntryId = SeedGuids.OpeningJournalId,
                    LineNo = 1,
                    GlAccountId = SeedGuids.Gl("1010"),
                    Debit = MoneyAmount.Normalize(cash),
                    Credit = 0m,
                    Description = "Caisse HTG — ouverture"
                },
                new JournalLine
                {
                    Id = SeedGuids.OpeningLineBankId,
                    JournalEntryId = SeedGuids.OpeningJournalId,
                    LineNo = 2,
                    GlAccountId = SeedGuids.Gl("1110"),
                    Debit = MoneyAmount.Normalize(bank),
                    Credit = 0m,
                    Description = "Banque HTG — ouverture"
                },
                new JournalLine
                {
                    Id = SeedGuids.OpeningLineCapitalId,
                    JournalEntryId = SeedGuids.OpeningJournalId,
                    LineNo = 3,
                    GlAccountId = SeedGuids.Gl("3010"),
                    Debit = 0m,
                    Credit = MoneyAmount.Normalize(capital),
                    Description = "Parts sociales — capital d’ouverture"
                }
            ]
        };

        return journal;
    }
}
