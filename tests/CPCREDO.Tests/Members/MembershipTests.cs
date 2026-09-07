using CPCREDO.Application.Common;
using CPCREDO.Application.Members;
using CPCREDO.Domain.Accounting;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Domain.Members;
using CPCREDO.Domain.Tenancy;
using CPCREDO.Infrastructure.Accounting;
using CPCREDO.Infrastructure.Audit;
using CPCREDO.Infrastructure.Members;
using CPCREDO.Infrastructure.Persistence;
using CPCREDO.Tests.Accounting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CPCREDO.Tests.Members;

public sealed class MembershipTests
{
    [Fact]
    public async Task Member_numbers_are_sequential_per_tenant()
    {
        using var harness = new MembershipHarness();
        var first = await harness.Members.CreateAsync(Request("Anne", "Lamothe", "CIN-A"));
        var second = await harness.Members.CreateAsync(Request("Paul", "Joseph", "CIN-B"));

        Assert.True(first.IsSuccess, first.ErrorMessage);
        Assert.True(second.IsSuccess, second.ErrorMessage);
        Assert.Equal("M-000001", first.Value!.MemberNo);
        Assert.Equal("M-000002", second.Value!.MemberNo);
        Assert.Equal("S-000001", first.Value.Shares.QualificationAccountNo);
        Assert.Equal("S-000002", second.Value.Shares.QualificationAccountNo);
    }

    [Fact]
    public async Task One_societaire_one_vote_regardless_of_qualification_count()
    {
        using var harness = new MembershipHarness();
        var oneShare = await harness.Members.CreateAsync(
            Request("Marie-Claire", "Jean", "CIN-1", shares: 1, status: MemberStatus.Active, legal: LegalStatus.Societaire));
        var manyShares = await harness.Members.CreateAsync(
            Request("Nadège", "Toussaint", "CIN-20", shares: 20, status: MemberStatus.Active, legal: LegalStatus.Societaire));

        Assert.True(oneShare.IsSuccess, oneShare.ErrorMessage);
        Assert.True(manyShares.IsSuccess, manyShares.ErrorMessage);
        Assert.True(oneShare.Value!.VotingRights);
        Assert.True(manyShares.Value!.VotingRights);
        Assert.Equal(1, oneShare.Value.Shares.QualificationShareCount);
        Assert.Equal(20, manyShares.Value.Shares.QualificationShareCount);
        Assert.Equal(0, manyShares.Value.Shares.PermanentShareCount);
        Assert.Equal(MembershipRules.VoteRule, manyShares.Value.VoteRule);

        var export = await harness.Members.GetAgExportAsync();
        Assert.True(export.IsSuccess, export.ErrorMessage);
        Assert.Equal(2, export.Value!.TotalVoters);
        Assert.Equal(2, export.Value.TotalVotes);
        Assert.All(export.Value.Voters, v => Assert.Equal(1, v.Votes));
    }

    [Fact]
    public async Task Zero_qualification_shares_cannot_claim_a_vote()
    {
        using var harness = new MembershipHarness();
        var created = await harness.Members.CreateAsync(
            Request("Usager", "Test", "CIN-0", shares: 0, status: MemberStatus.Active, legal: LegalStatus.Societaire, votingRights: true));

        Assert.False(created.IsSuccess);
        Assert.Equal("member.voting_requires_qualification", created.ErrorCode);
    }

    [Fact]
    public async Task Usager_has_no_vote_and_cannot_use_micro90_or_officer_roles()
    {
        using var harness = new MembershipHarness();
        var created = await harness.Members.CreateAsync(
            Request("Odette", "Cinéas", "CIN-U", shares: 0, status: MemberStatus.Active, legal: LegalStatus.Usager));

        Assert.True(created.IsSuccess, created.ErrorMessage);
        Assert.False(created.Value!.VotingRights);
        Assert.Equal(nameof(LegalStatus.Usager), created.Value.LegalStatus);
        Assert.False(MembershipRules.AllowsMicro90(LegalStatus.Usager));
        Assert.False(MembershipRules.AllowsOfficerRole(LegalStatus.Usager));

        var micro = await harness.Members.AssertCapabilityAsync(created.Value.Id, MembershipRules.Micro90Product);
        var officer = await harness.Members.AssertCapabilityAsync(created.Value.Id, MembershipRules.OfficerCapability);
        Assert.Equal("member.usager_micro90", micro.ErrorCode);
        Assert.Equal("member.usager_officer", officer.ErrorCode);
    }

    [Fact]
    public async Task Convert_to_societaire_requires_kyc_and_posts_qualification_share()
    {
        using var harness = new MembershipHarness();
        var created = await harness.Members.CreateAsync(
            Request("Patrick", "Dorvil", "CIN-C", shares: 0, status: MemberStatus.Active, legal: LegalStatus.Usager, kyc: KycStatus.Incomplete));
        var refused = await harness.Members.ConvertToSocietaireAsync(created.Value!.Id, "idem-kyc");
        Assert.Equal("member.kyc_inactive", refused.ErrorCode);

        var ready = await harness.Members.UpdateAsync(
            created.Value.Id,
            Request("Patrick", "Dorvil", "CIN-C", shares: 0, status: MemberStatus.Active, legal: LegalStatus.Usager, kyc: KycStatus.Verified));
        Assert.True(ready.IsSuccess, ready.ErrorMessage);

        var converted = await harness.Members.ConvertToSocietaireAsync(ready.Value!.Id, "idem-convert");
        Assert.True(converted.IsSuccess, converted.ErrorMessage);
        Assert.Equal(nameof(LegalStatus.Societaire), converted.Value!.LegalStatus);
        Assert.True(converted.Value.VotingRights);
        Assert.Equal(1, converted.Value.Shares.QualificationShareCount);

        var replay = await harness.Members.ConvertToSocietaireAsync(ready.Value.Id, "idem-convert");
        Assert.Equal("member.already_societaire", replay.ErrorCode);

        var cash = harness.Db.JournalLines.Where(l => l.GlAccountId == SeedGuids.Gl("1010")).Sum(l => l.Debit);
        var capital = harness.Db.JournalLines.Where(l => l.GlAccountId == SeedGuids.Gl("3010")).Sum(l => l.Credit);
        Assert.Equal(500.0000m, cash);
        Assert.Equal(500.0000m, capital);
    }

    [Fact]
    public async Task Permanent_shares_do_not_change_voting_rights()
    {
        using var harness = new MembershipHarness();
        var created = await harness.Members.CreateAsync(
            Request("Henri", "César", "CIN-P", shares: 1, status: MemberStatus.Active, legal: LegalStatus.Societaire, kyc: KycStatus.Verified));
        var before = created.Value!.VotingRights;

        var subscribed = await harness.Members.SubscribePermanentSharesAsync(
            created.Value.Id,
            new SubscribePermanentSharesRequest { Quantity = 10 },
            "idem-perm");

        Assert.True(subscribed.IsSuccess, subscribed.ErrorMessage);
        Assert.Equal(before, subscribed.Value!.VotingRights);
        Assert.True(subscribed.Value.VotingRights);
        Assert.Equal(1, subscribed.Value.Shares.QualificationShareCount);
        Assert.Equal(10, subscribed.Value.Shares.PermanentShareCount);
        Assert.Equal(5000.0000m, subscribed.Value.Shares.PermanentBookValue);

        var permCredit = harness.Db.JournalLines.Where(l => l.GlAccountId == SeedGuids.Gl("3011")).Sum(l => l.Credit);
        Assert.Equal(5000.0000m, permCredit);
    }

    [Fact]
    public async Task Expired_usager_is_blocked_until_conversion()
    {
        using var harness = new MembershipHarness();
        harness.Clock.UtcNow = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
        var created = await harness.Members.CreateAsync(
            Request("Rose", "Cadet", "CIN-X", shares: 0, status: MemberStatus.Active, legal: LegalStatus.Usager, kyc: KycStatus.Verified));
        Assert.True(created.IsSuccess, created.ErrorMessage);
        Assert.Equal(90, created.Value!.UsagerDaysLeft);

        harness.Clock.UtcNow = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        var expired = await harness.Members.Get360Async(created.Value.Id);
        Assert.True(expired.Value!.ServicesBlocked);
        Assert.Equal(0, expired.Value.UsagerDaysLeft);
        Assert.False(expired.Value.VotingRights);
    }

    [Fact]
    public async Task Commissaire_is_read_only()
    {
        using var harness = new MembershipHarness();
        harness.User.Roles = [RoleNames.Commissaire];

        var created = await harness.Members.CreateAsync(Request("X", "Y", "CIN-X"));
        Assert.False(created.IsSuccess);
        Assert.Equal("auth.forbidden", created.ErrorCode);
    }

    private static MemberWriteRequest Request(
        string first,
        string last,
        string cin,
        int shares = 0,
        MemberStatus status = MemberStatus.Pending,
        LegalStatus legal = LegalStatus.Usager,
        KycStatus kyc = KycStatus.Verified,
        bool? votingRights = null) =>
        new()
        {
            FirstName = first,
            LastName = last,
            Cin = cin,
            Phone = "+509 2812 0000",
            AddressLine = "Rue Test",
            City = Letterhead.City,
            QualificationShareCount = shares,
            Status = status,
            KycStatus = kyc,
            LegalStatus = legal,
            VotingRights = votingRights
        };
}

internal sealed class MembershipHarness : IDisposable
{
    public CpcredoDbContext Db { get; }
    public MemberService Members { get; }
    public TestCurrentUser User { get; }
    public FixedClock Clock { get; }

    public MembershipHarness()
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
        Db.GlAccounts.AddRange(
            Gl("1010", GlAccountType.Asset, NormalBalance.Debit),
            Gl("3010", GlAccountType.Equity, NormalBalance.Credit),
            Gl("3011", GlAccountType.Equity, NormalBalance.Credit));
        Db.SaveChanges();

        User = new TestCurrentUser();
        Clock = new FixedClock { UtcNow = now };
        var audit = new AuditLogger(Db, Clock, User);
        var journals = new JournalService(Db, User, Clock, audit);
        Members = new MemberService(Db, User, Clock, audit, journals);
    }

    public void Dispose() => Db.Dispose();

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
}
