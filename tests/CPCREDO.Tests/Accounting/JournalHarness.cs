using CPCREDO.Application.Common;
using CPCREDO.Domain.Accounting;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Domain.Tenancy;
using CPCREDO.Infrastructure.Accounting;
using CPCREDO.Infrastructure.Audit;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CPCREDO.Tests.Accounting;

internal sealed class TestCurrentUser : ICurrentUser
{
    public bool IsAuthenticated { get; set; } = true;
    public Guid? UserId { get; set; } = SeedGuids.AdminUserId;
    public Guid? TenantId { get; set; } = SeedGuids.TenantId;
    public Guid? BranchId { get; set; } = SeedGuids.BranchId;
    public string? Username { get; set; } = "admin";
    public IReadOnlyList<string> Roles { get; set; } = [RoleNames.Admin];
    public string? IpAddress { get; set; } = "127.0.0.1";
}

internal sealed class FixedClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 9, 2, 16, 0, 0, DateTimeKind.Utc);

    public DateTime ToPortAuPrince(DateTime utc) => CpcredoTimeZone.ToDisplay(utc);

    public DateOnly TodayInPortAuPrince() => CpcredoTimeZone.Today(UtcNow);
}

internal sealed class JournalHarness : IDisposable
{
    public CpcredoDbContext Db { get; }
    public JournalService Journals { get; }
    public TestCurrentUser User { get; }
    public FixedClock Clock { get; }

    public Guid CashId { get; } = SeedGuids.Gl("1010");
    public Guid CapitalId { get; } = SeedGuids.Gl("3010");

    public JournalHarness()
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
        Db.GlAccounts.AddRange(
            Account(CashId, "1010", "Caisse HTG", GlAccountType.Asset, NormalBalance.Debit),
            Account(CapitalId, "3010", "Parts sociales", GlAccountType.Equity, NormalBalance.Credit));
        Db.SaveChanges();

        User = new TestCurrentUser();
        Clock = new FixedClock();
        var audit = new AuditLogger(Db, Clock, User);
        Journals = new JournalService(Db, User, Clock, audit);
    }

    private static GlAccount Account(
        Guid id,
        string code,
        string name,
        GlAccountType type,
        NormalBalance balance) =>
        new()
        {
            Id = id,
            TenantId = SeedGuids.TenantId,
            Code = code,
            NameFr = name,
            NameHt = name,
            NameEn = name,
            AccountType = type,
            NormalBalance = balance,
            CurrencyCode = Currencies.Htg,
            IsPostable = true,
            IsActive = true,
            CreatedAtUtc = new DateTime(2026, 1, 2, 13, 0, 0, DateTimeKind.Utc)
        };

    public void Dispose() => Db.Dispose();
}
