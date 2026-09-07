using System.Text;
using CPCREDO.Application.Members;
using CPCREDO.Application.Savings;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Domain.Members;
using CPCREDO.Domain.Savings;
using CPCREDO.Domain.Tenancy;
using CPCREDO.Infrastructure.Accounting;
using CPCREDO.Infrastructure.Audit;
using CPCREDO.Infrastructure.Members;
using CPCREDO.Infrastructure.Persistence;
using CPCREDO.Infrastructure.Savings;
using CPCREDO.Tests.Accounting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CPCREDO.Tests.ServiceClient;

public sealed class ServiceClientTests
{
    [Fact]
    public async Task Hold_reduces_available_balance()
    {
        using var h = new ServiceClientHarness();
        var account = await h.OpenFundedAccountAsync(1000m);

        var held = await h.Savings.PlaceHoldAsync(account.Id, new PlaceHoldRequest { Amount = 300m, Reason = "Gage crédit" });

        Assert.True(held.IsSuccess, held.ErrorMessage);
        Assert.Equal(1000m, held.Value!.LedgerBalance);
        Assert.Equal(700m, held.Value.AvailableBalance);
        Assert.Single(held.Value.Holds);
        Assert.Equal("Gage crédit", held.Value.Holds[0].Reason);
    }

    [Fact]
    public async Task Block_account_records_reason()
    {
        using var h = new ServiceClientHarness();
        var account = await h.OpenFundedAccountAsync(100m);

        var blocked = await h.Savings.BlockAccountAsync(account.Id, new BlockAccountRequest { Reason = "Pièce d’identité expirée" });

        Assert.True(blocked.IsSuccess, blocked.ErrorMessage);
        Assert.True(blocked.Value!.IsBlocked);
        Assert.Equal("Pièce d’identité expirée", blocked.Value.BlockedReason);
    }

    [Fact]
    public async Task Ticket_open_assign_close()
    {
        using var h = new ServiceClientHarness();
        var member = await h.CreateMemberAsync();

        var opened = await h.Members.OpenTicketAsync(member.Id, new OpenTicketRequest
        {
            Subject = "Mise à jour KYC",
            Body = "CIN à renouveler"
        });
        Assert.True(opened.IsSuccess, opened.ErrorMessage);
        Assert.Equal("Open", opened.Value!.Status);
        Assert.StartsWith("TK-", opened.Value.TicketNo);

        var assigned = await h.Members.AssignTicketAsync(
            member.Id,
            opened.Value.Id,
            new AssignTicketRequest { AssignedToUserId = SeedGuids.AdminUserId });
        Assert.True(assigned.IsSuccess, assigned.ErrorMessage);
        Assert.Equal("Assigned", assigned.Value!.Status);
        Assert.Equal(SeedGuids.AdminUserId, assigned.Value.AssignedToUserId);

        var closed = await h.Members.CloseTicketAsync(member.Id, opened.Value.Id);
        Assert.True(closed.IsSuccess, closed.ErrorMessage);
        Assert.Equal("Closed", closed.Value!.Status);
        Assert.NotNull(closed.Value.ClosedAtUtc);
    }

    [Fact]
    public async Task Member_360_includes_transactions_and_loan_placeholder()
    {
        using var h = new ServiceClientHarness();
        var account = await h.OpenFundedAccountAsync(250m);
        await h.Members.OpenTicketAsync(account.MemberId, new OpenTicketRequest { Subject = "Note" });

        var view = await h.Members.Get360Async(account.MemberId);
        Assert.True(view.IsSuccess, view.ErrorMessage);
        Assert.Equal("Crédits: aucun", view.Value!.LoansPlaceholder);
        Assert.NotEmpty(view.Value.SavingsAccounts);
        Assert.Single(view.Value.RecentTransactions);
        Assert.Equal(250m, view.Value.RecentTransactions[0].Amount);
        Assert.Single(view.Value.Tickets);
        Assert.Equal("Verified", view.Value.KycStatus);
    }

    [Fact]
    public async Task Statement_pdf_uses_cpcredo_letterhead()
    {
        using var h = new ServiceClientHarness();
        var account = await h.OpenFundedAccountAsync(100m);

        var pdf = await h.Savings.GetStatementPdfAsync(
            account.Id,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31));

        Assert.True(pdf.IsSuccess, pdf.ErrorMessage);
        Assert.True(pdf.Value!.Content.Length > 100);
        var header = Encoding.ASCII.GetString(pdf.Value.Content.AsSpan(0, 4));
        Assert.Equal("%PDF", header);
        var text = Encoding.Latin1.GetString(pdf.Value.Content);
        Assert.Contains("CPCREDO", text, StringComparison.Ordinal);
    }
}

internal sealed class ServiceClientHarness : IDisposable
{
    public CpcredoDbContext Db { get; }
    public MemberService Members { get; }
    public SavingsService Savings { get; }
    public TestCurrentUser User { get; }

    public ServiceClientHarness()
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

        User = new TestCurrentUser { Roles = [RoleNames.ServiceClient] };
        var clock = new FixedClock();
        var audit = new AuditLogger(Db, clock, User);
        Members = new MemberService(Db, User, clock, audit, new JournalService(Db, User, clock, audit));
        Savings = new SavingsService(Db, User, clock, audit);
    }

    public async Task<Member> CreateMemberAsync()
    {
        var created = await Members.CreateAsync(new MemberWriteRequest
        {
            FirstName = "Marie",
            LastName = "Client",
            Cin = Guid.NewGuid().ToString("N")[..12],
            Phone = "+509 1111 2222",
            AddressLine = "Rue Service",
            City = Letterhead.City,
            Status = MemberStatus.Active,
            KycStatus = KycStatus.Verified
        });
        Assert.True(created.IsSuccess, created.ErrorMessage);
        return await Db.Members.SingleAsync(m => m.Id == created.Value!.Id);
    }

    public async Task<SavingsAccount> OpenFundedAccountAsync(decimal amount)
    {
        var member = await CreateMemberAsync();
        var opened = await Savings.OpenAccountAsync(member.Id, SeedGuids.SavingsProductHtg);
        Assert.True(opened.IsSuccess, opened.ErrorMessage);
        Db.SavingsLedgerEntries.Add(new SavingsLedgerEntry
        {
            TenantId = SeedGuids.TenantId,
            SavingsAccountId = opened.Value!.Id,
            ValueDateUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            PostedAtUtc = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
            EntryType = "Credit",
            Amount = amount,
            CurrencyCode = Currencies.Htg,
            Description = "Dépôt test"
        });
        await Db.SaveChangesAsync();
        return await Db.SavingsAccounts.SingleAsync(a => a.Id == opened.Value.Id);
    }

    public void Dispose() => Db.Dispose();
}
