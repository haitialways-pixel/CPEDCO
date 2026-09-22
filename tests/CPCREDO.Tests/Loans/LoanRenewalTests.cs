using CPCREDO.Application.Loans;
using CPCREDO.Application.Teller;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Loans;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CPCREDO.Tests.Loans;

public sealed class LoanRenewalTests
{
    [Fact]
    public async Task Dpd_8_blocks_renewal()
    {
        using var h = new LoanHarness();
        var loan = await ActivateAsync(h, maxRenewals: 3, renewalMaxOutstandingPercent: 100m);
        var today = CpcredoTimeZone.Today(new DateTime(2026, 1, 2, 13, 0, 0, DateTimeKind.Utc));
        var first = await h.Db.LoanInstallments.OrderBy(i => i.LineNo).FirstAsync(i => i.LoanId == loan.Id);
        first.DueDate = today.AddDays(-8);
        await h.Db.SaveChangesAsync();

        h.AsOfficer();
        var renewed = await h.Loans.RenewAsync(loan.Id, "ren-dpd8");
        Assert.False(renewed.IsSuccess);
        Assert.Equal("loan.dpd", renewed.ErrorCode);
        Assert.Equal(nameof(LoanStatus.Active), (await h.Db.Loans.SingleAsync(l => l.Id == loan.Id)).Status.ToString());
    }

    [Fact]
    public async Task Renewal_marks_old_renewed_and_new_approved_cycle_plus_one()
    {
        using var h = new LoanHarness();
        var loan = await ActivateAsync(h, maxRenewals: 3, renewalMaxOutstandingPercent: 0m);
        foreach (var line in await h.Db.LoanInstallments.Where(i => i.LoanId == loan.Id).ToListAsync())
        {
            line.PrincipalPaid = line.PrincipalDue;
            line.InterestPaid = line.InterestDue;
        }
        await h.Db.SaveChangesAsync();

        h.AsOfficer();
        var renewed = await h.Loans.RenewAsync(loan.Id, "ren-ok");
        Assert.True(renewed.IsSuccess, renewed.ErrorMessage);
        Assert.Equal(nameof(LoanStatus.Approved), renewed.Value!.Status);
        Assert.Equal(2, renewed.Value.CycleNumber);
        Assert.Equal("LN-000002", renewed.Value.LoanNo);
        Assert.Equal(loan.Id, renewed.Value.RenewedFromLoanId);
        Assert.Equal(10_000m, renewed.Value.Principal);
        Assert.Null(renewed.Value.DisbursedAtUtc);

        var old = await h.Db.Loans.SingleAsync(l => l.Id == loan.Id);
        Assert.Equal(LoanStatus.Renewed, old.Status);
        Assert.Equal(renewed.Value.Id, old.RenewedToLoanId);
    }

    [Fact]
    public async Task Renewal_does_not_capitalize_unpaid_interest()
    {
        using var h = new LoanHarness();
        var loan = await ActivateAsync(h, maxRenewals: 3, renewalMaxOutstandingPercent: 20m);
        var lines = await h.Db.LoanInstallments.Where(i => i.LoanId == loan.Id).OrderBy(i => i.LineNo).ToListAsync();
        var leftover = 1_000m;
        foreach (var line in lines)
        {
            line.InterestPaid = 0m;
            var remaining = leftover > line.PrincipalDue ? line.PrincipalDue : leftover;
            line.PrincipalPaid = MoneyAmount.Normalize(line.PrincipalDue - remaining);
            leftover = MoneyAmount.Normalize(leftover - remaining);
        }
        await h.Db.SaveChangesAsync();

        h.AsOfficer();
        var renewed = await h.Loans.RenewAsync(loan.Id, "ren-interest");
        Assert.True(renewed.IsSuccess, renewed.ErrorMessage);
        Assert.Equal(1_000m, renewed.Value!.Principal);
        Assert.Equal(200m, renewed.Value.TotalInterest);
    }

    private static async Task<LoanDto> ActivateAsync(
        LoanHarness h,
        int? maxRenewals,
        decimal? renewalMaxOutstandingPercent)
    {
        h.AsOfficer();
        var product = await h.SeedProductAsync(20_000m, maxRenewals, renewalMaxOutstandingPercent);
        var draft = await h.Loans.CreateDraftAsync(new CreateLoanRequest
        {
            MemberId = h.MemberId,
            ProductId = product.Id,
            Principal = 10_000m,
            AgreedRatePercent = 20m
        });
        Assert.True(draft.IsSuccess, draft.ErrorMessage);
        await h.Loans.SubmitAsync(draft.Value!.Id);
        h.AsGerant();
        var approved = await h.Loans.ApproveAsync(draft.Value.Id);
        Assert.True(approved.IsSuccess, approved.ErrorMessage);
        h.AsCaissier();
        var till = await h.OpenTillAsync(50_000m);
        Assert.True(till.IsSuccess, till.ErrorMessage);
        var savings = await h.Savings.OpenAccountAsync(h.MemberId, SeedGuids.SavingsProductHtg);
        var disbursed = await h.Loans.DisburseAsync(
            draft.Value.Id,
            new DisburseLoanRequest { SavingsAccountId = savings.Value!.Id },
            "ren-disb");
        Assert.True(disbursed.IsSuccess, disbursed.ErrorMessage);
        return disbursed.Value!;
    }
}
