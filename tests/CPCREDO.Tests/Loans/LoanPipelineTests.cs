using CPCREDO.Application.Loans;
using CPCREDO.Application.Teller;
using CPCREDO.Domain.Accounting;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Loans;
using CPCREDO.Domain.Members;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CPCREDO.Tests.Loans;

public sealed class LoanPipelineTests
{
    [Fact]
    public async Task Officer_submits_draft_to_pending_approval()
    {
        using var h = new LoanHarness();
        h.AsOfficer();
        var product = await h.SeedProductAsync();
        var draft = await h.Loans.CreateDraftAsync(Request(h, product.Id));
        Assert.Equal(nameof(LoanStatus.Draft), draft.Value!.Status);

        var submitted = await h.Loans.SubmitAsync(draft.Value.Id);
        Assert.True(submitted.IsSuccess, submitted.ErrorMessage);
        Assert.Equal(nameof(LoanStatus.PendingApproval), submitted.Value!.Status);
        Assert.NotNull(submitted.Value.SubmittedAtUtc);
    }

    [Fact]
    public async Task Gerant_approves_up_to_officer_max_in_one_step()
    {
        using var h = new LoanHarness();
        h.AsOfficer();
        var product = await h.SeedProductAsync(officerMax: 20_000m);
        var draft = await h.Loans.CreateDraftAsync(Request(h, product.Id, 10_000m));
        await h.Loans.SubmitAsync(draft.Value!.Id);

        h.AsGerant();
        var approved = await h.Loans.ApproveAsync(draft.Value.Id);
        Assert.True(approved.IsSuccess, approved.ErrorMessage);
        Assert.Equal(nameof(LoanStatus.Approved), approved.Value!.Status);
        Assert.NotNull(approved.Value.Approver1Id);
        Assert.Null(approved.Value.Approver2Id);
        Assert.False(approved.Value.RequiresSecondApproval);
    }

    [Fact]
    public async Task Above_officer_max_needs_second_approver()
    {
        using var h = new LoanHarness();
        h.AsOfficer();
        var product = await h.SeedProductAsync(officerMax: 5_000m);
        var draft = await h.Loans.CreateDraftAsync(Request(h, product.Id, 10_000m));
        await h.Loans.SubmitAsync(draft.Value!.Id);

        h.AsGerant();
        var first = await h.Loans.ApproveAsync(draft.Value.Id);
        Assert.True(first.IsSuccess, first.ErrorMessage);
        Assert.Equal(nameof(LoanStatus.PendingApproval), first.Value!.Status);
        Assert.True(first.Value.RequiresSecondApproval);

        var same = await h.Loans.ApproveAsync(draft.Value.Id);
        Assert.False(same.IsSuccess);
        Assert.Equal("loan.same_approver", same.ErrorCode);

        h.AsAdmin();
        var second = await h.Loans.ApproveAsync(draft.Value.Id);
        Assert.True(second.IsSuccess, second.ErrorMessage);
        Assert.Equal(nameof(LoanStatus.Approved), second.Value!.Status);
        Assert.NotNull(second.Value.Approver2Id);
    }

    [Fact]
    public async Task Officer_and_caissier_cannot_approve()
    {
        using var h = new LoanHarness();
        h.AsOfficer();
        var product = await h.SeedProductAsync();
        var draft = await h.Loans.CreateDraftAsync(Request(h, product.Id));
        await h.Loans.SubmitAsync(draft.Value!.Id);

        var asOfficer = await h.Loans.ApproveAsync(draft.Value.Id);
        Assert.Equal("auth.forbidden", asOfficer.ErrorCode);

        h.AsCaissier();
        var asCashier = await h.Loans.ApproveAsync(draft.Value.Id);
        Assert.Equal("auth.forbidden", asCashier.ErrorCode);
    }

    [Fact]
    public async Task Usager_cannot_receive_ct90()
    {
        using var h = new LoanHarness();
        var usager = new Member
        {
            Id = Guid.NewGuid(),
            TenantId = SeedGuids.TenantId,
            BranchId = SeedGuids.BranchId,
            MemberNo = "M-000099",
            FirstName = "Odette",
            LastName = "Usager",
            Phone = "30000001",
            AddressLine = "PV",
            City = "Pétion-Ville",
            Status = MemberStatus.Active,
            KycStatus = KycStatus.Verified,
            LegalStatus = LegalStatus.Usager,
            CreatedAtUtc = DateTime.UtcNow
        };
        h.Db.Members.Add(usager);
        await h.Db.SaveChangesAsync();

        h.AsOfficer();
        var product = await h.SeedProductAsync();
        var created = await h.Loans.CreateDraftAsync(new CreateLoanRequest
        {
            MemberId = usager.Id,
            ProductId = product.Id,
            Principal = 10_000m,
            AgreedRatePercent = 20m
        });
        Assert.False(created.IsSuccess);
        Assert.Equal("member.usager_micro90", created.ErrorCode);
    }

    [Fact]
    public async Task Kyc_must_be_verified()
    {
        using var h = new LoanHarness();
        var member = await h.Db.Members.SingleAsync(m => m.Id == h.MemberId);
        member.KycStatus = KycStatus.Incomplete;
        await h.Db.SaveChangesAsync();

        h.AsOfficer();
        var product = await h.SeedProductAsync();
        var created = await h.Loans.CreateDraftAsync(Request(h, product.Id));
        Assert.False(created.IsSuccess);
        Assert.Equal("loan.kyc_not_active", created.ErrorCode);
    }

    [Fact]
    public async Task Caissier_cannot_disburse_without_open_till()
    {
        using var h = new LoanHarness();
        var loan = await ApproveAsync(h, 10_000m, officerMax: 20_000m);
        h.AsCaissier();
        var savings = await h.Savings.OpenAccountAsync(h.MemberId, SeedGuids.SavingsProductHtg);
        var failed = await h.Loans.DisburseAsync(loan.Id, new DisburseLoanRequest { SavingsAccountId = savings.Value!.Id }, "disb-no-till");
        Assert.False(failed.IsSuccess);
        Assert.Equal("till.not_open", failed.ErrorCode);
    }

    [Fact]
    public async Task Caissier_disburses_approved_loan_with_compulsory_savings_lien()
    {
        using var h = new LoanHarness();
        var loan = await ApproveAsync(h, 10_000m, officerMax: 20_000m);
        h.AsCaissier();
        var till = await h.Teller.OpenAsync(new OpenTillRequest { CurrencyCode = Currencies.Htg, OpeningFloat = 50_000m });
        Assert.True(till.IsSuccess, till.ErrorMessage);
        var savings = await h.Savings.OpenAccountAsync(h.MemberId, SeedGuids.SavingsProductHtg);
        Assert.True(savings.IsSuccess, savings.ErrorMessage);

        var disbursed = await h.Loans.DisburseAsync(
            loan.Id,
            new DisburseLoanRequest { SavingsAccountId = savings.Value!.Id },
            "disb-10000");
        Assert.True(disbursed.IsSuccess, disbursed.ErrorMessage);
        Assert.Equal(nameof(LoanStatus.Active), disbursed.Value!.Status);
        Assert.Equal(1_000m, disbursed.Value.CompulsorySavingsAmount);
        Assert.Equal(9_000m, disbursed.Value.CashDisbursedAmount);
        Assert.NotNull(disbursed.Value.PostedJournalId);
        Assert.NotNull(disbursed.Value.LienId);

        var journal = await h.Db.JournalEntries.Include(j => j.Lines)
            .SingleAsync(j => j.Id == disbursed.Value.PostedJournalId);
        Assert.Equal(10_000m, journal.Lines.Sum(l => l.Debit));
        Assert.Equal(10_000m, journal.Lines.Sum(l => l.Credit));
        Assert.Equal(10_000m, journal.Lines.Single(l => l.GlAccountId == SeedGuids.Gl(LoanGl.ShortTermPortfolioHtg)).Debit);
        Assert.Equal(9_000m, journal.Lines.Single(l => l.GlAccountId == SeedGuids.Gl(LoanGl.CashHtg)).Credit);
        Assert.Equal(1_000m, journal.Lines.Single(l => l.GlAccountId == SeedGuids.Gl("2010")).Credit);

        var lien = await h.Db.SavingsLiens.SingleAsync(l => l.Id == disbursed.Value.LienId);
        Assert.Equal(1_000m, lien.Amount);
        Assert.Null(lien.ReleasedAtUtc);

        var ledger = await h.Db.SavingsLedgerEntries.Where(e => e.SavingsAccountId == savings.Value.Id).ToListAsync();
        Assert.Equal(1_000m, ledger.Sum(e => e.SignedAmount));

        var tillAfter = await h.Db.TillSessions.SingleAsync(t => t.Id == till.Value!.Id);
        Assert.Equal(41_000m, tillAfter.ExpectedCash);

        var replay = await h.Loans.DisburseAsync(
            loan.Id,
            new DisburseLoanRequest { SavingsAccountId = savings.Value.Id },
            "disb-10000");
        Assert.True(replay.IsSuccess, replay.ErrorMessage);
        Assert.Equal(1, await h.Db.JournalEntries.CountAsync(j => j.Description.Contains("Décaissement")));
    }

    [Fact]
    public async Task Officer_cannot_disburse()
    {
        using var h = new LoanHarness();
        var loan = await ApproveAsync(h, 10_000m, officerMax: 20_000m);
        h.AsOfficer();
        var denied = await h.Loans.DisburseAsync(loan.Id, new DisburseLoanRequest(), "disb-officer");
        Assert.Equal("auth.forbidden", denied.ErrorCode);
    }

    [Fact]
    public async Task Caissier_cannot_disburse_draft()
    {
        using var h = new LoanHarness();
        h.AsOfficer();
        var product = await h.SeedProductAsync();
        var draft = await h.Loans.CreateDraftAsync(Request(h, product.Id));
        h.AsCaissier();
        await h.Teller.OpenAsync(new OpenTillRequest { CurrencyCode = Currencies.Htg, OpeningFloat = 10_000m });
        var failed = await h.Loans.DisburseAsync(draft.Value!.Id, new DisburseLoanRequest(), "disb-draft");
        Assert.Equal("loan.not_approved", failed.ErrorCode);
    }

    private static CreateLoanRequest Request(LoanHarness h, Guid productId, decimal principal = 10_000m) =>
        new()
        {
            MemberId = h.MemberId,
            ProductId = productId,
            Principal = principal,
            AgreedRatePercent = 20m
        };

    private static async Task<LoanDto> ApproveAsync(LoanHarness h, decimal principal, decimal officerMax)
    {
        h.AsOfficer();
        var product = await h.SeedProductAsync(officerMax);
        var draft = await h.Loans.CreateDraftAsync(Request(h, product.Id, principal));
        Assert.True(draft.IsSuccess, draft.ErrorMessage);
        var submitted = await h.Loans.SubmitAsync(draft.Value!.Id);
        Assert.True(submitted.IsSuccess, submitted.ErrorMessage);
        h.AsGerant();
        var approved = await h.Loans.ApproveAsync(draft.Value.Id);
        Assert.True(approved.IsSuccess, approved.ErrorMessage);
        Assert.Equal(nameof(LoanStatus.Approved), approved.Value!.Status);
        return approved.Value;
    }
}
