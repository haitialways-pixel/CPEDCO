using System.Text;
using CPCREDO.Application.Loans;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Loans;
using CPCREDO.Infrastructure.Audit;
using CPCREDO.Infrastructure.Reports;
using CPCREDO.Tests.Accounting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CPCREDO.Tests.Loans;

public sealed class LoanPortfolioReportsTests
{
    [Fact]
    public void Evergreen_is_cycle_3_and_80_percent_of_previous_original()
    {
        Assert.True(LoanEvergreen.Matches(3, 8_000m, 10_000m));
        Assert.False(LoanEvergreen.Matches(3, 7_999m, 10_000m));
        Assert.False(LoanEvergreen.Matches(2, 10_000m, 10_000m));
        Assert.Equal(2, LoanEvergreen.RemainingRenewals(1, 3));
        Assert.Equal(0, LoanEvergreen.RemainingRenewals(3, 3));
        Assert.Null(LoanEvergreen.RemainingRenewals(1, null));
    }

    [Fact]
    public async Task Par_1_7_30_on_ct90_uses_dpd_buckets()
    {
        using var h = new LoanHarness();
        var loan = await ActivateCt90Async(h);
        var today = CpcredoTimeZone.Today(new DateTime(2026, 1, 2, 13, 0, 0, DateTimeKind.Utc));
        var first = await h.Db.LoanInstallments.OrderBy(i => i.LineNo).FirstAsync(i => i.LoanId == loan.Id);
        first.DueDate = today.AddDays(-8);
        await h.Db.SaveChangesAsync();

        h.AsGerant();
        var reports = Reports(h);
        var par = await reports.GetParCt90Async(today);
        Assert.True(par.IsSuccess, par.ErrorMessage);
        Assert.Equal(10_000m, par.Value!.PortfolioOutstanding);
        Assert.Equal(10_000m, par.Value.Par1.Outstanding);
        Assert.Equal(10_000m, par.Value.Par7.Outstanding);
        Assert.Equal(0m, par.Value.Par30.Outstanding);
        Assert.Equal(1m, par.Value.Par1.Ratio);
        Assert.Equal(1m, par.Value.Par7.Ratio);
        Assert.Equal(0m, par.Value.Par30.Ratio);

        var pdf = await reports.ExportParCt90Async(today, "pdf");
        Assert.True(pdf.IsSuccess, pdf.ErrorMessage);
        Assert.Contains("CPCREDO", Encoding.Latin1.GetString(pdf.Value!.Content), StringComparison.Ordinal);
        Assert.StartsWith("par-ct90-", pdf.Value.FileName);
    }

    [Fact]
    public async Task Collection_sheet_pdf_has_letterhead_and_french_filename()
    {
        using var h = new LoanHarness();
        await ActivateCt90Async(h);
        h.AsGerant();
        var pdf = await h.Loans.ExportCollectionSheetAsync("today", "pdf");
        Assert.True(pdf.IsSuccess, pdf.ErrorMessage);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf.Value!.Content.AsSpan(0, 4)));
        Assert.Contains("CPCREDO", Encoding.Latin1.GetString(pdf.Value.Content), StringComparison.Ordinal);
        Assert.StartsWith("recouvrement-", pdf.Value.FileName);
        Assert.EndsWith(".pdf", pdf.Value.FileName);
    }

    [Fact]
    public async Task Renewal_register_lists_renewed_pair()
    {
        using var h = new LoanHarness();
        var loan = await ActivateCt90Async(h);
        foreach (var line in await h.Db.LoanInstallments.Where(i => i.LoanId == loan.Id).ToListAsync())
        {
            line.PrincipalPaid = line.PrincipalDue;
            line.InterestPaid = line.InterestDue;
        }
        await h.Db.SaveChangesAsync();
        h.AsOfficer();
        var renewed = await h.Loans.RenewAsync(loan.Id, "ren-reg");
        Assert.True(renewed.IsSuccess, renewed.ErrorMessage);

        h.AsGerant();
        var register = await Reports(h).GetRenewalRegisterAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2));
        Assert.True(register.IsSuccess, register.ErrorMessage);
        var row = Assert.Single(register.Value!.Rows);
        Assert.Equal(loan.LoanNo, row.OldLoanNo);
        Assert.Equal(renewed.Value!.LoanNo, row.NewLoanNo);
        Assert.Equal(2, row.NewCycle);
        Assert.False(row.IsEvergreen);
    }

    [Fact]
    public async Task Cycle_3_at_80_percent_of_previous_original_is_evergreen()
    {
        using var h = new LoanHarness();
        h.AsAdmin();
        var product = await h.SeedProductAsync(20_000m, 5, 100m);
        var previous = new Loan
        {
            TenantId = SeedGuids.TenantId,
            BranchId = SeedGuids.BranchId,
            MemberId = h.MemberId,
            ProductId = product.Id,
            LoanNo = "LN-OLD",
            Principal = 10_000m,
            Status = LoanStatus.Renewed,
            CycleNumber = 2,
            OriginationDate = new DateOnly(2025, 10, 1),
            CreatedByUserId = SeedGuids.AdminUserId,
            CreatedAtUtc = new DateTime(2025, 10, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var current = new Loan
        {
            TenantId = SeedGuids.TenantId,
            BranchId = SeedGuids.BranchId,
            MemberId = h.MemberId,
            ProductId = product.Id,
            LoanNo = "LN-NEW",
            Principal = 8_000m,
            Status = LoanStatus.Active,
            CycleNumber = 3,
            RenewedFromLoanId = previous.Id,
            OriginationDate = new DateOnly(2026, 1, 2),
            CreatedByUserId = SeedGuids.AdminUserId,
            CreatedAtUtc = new DateTime(2026, 1, 2, 13, 0, 0, DateTimeKind.Utc)
        };
        h.Db.Loans.AddRange(previous, current);
        await h.Db.SaveChangesAsync();

        var mapped = await h.Loans.GetAsync(current.Id);
        Assert.True(mapped.IsSuccess, mapped.ErrorMessage);
        Assert.True(mapped.Value!.IsEvergreen);
        Assert.Equal(2, LoanEvergreen.RemainingRenewals(mapped.Value.CycleNumber, 5));
    }

    private static ReportService Reports(LoanHarness h)
    {
        var clock = new FixedClock { UtcNow = new DateTime(2026, 1, 2, 13, 0, 0, DateTimeKind.Utc) };
        var audit = new AuditLogger(h.Db, clock, h.User);
        var journals = new CPCREDO.Infrastructure.Accounting.JournalService(h.Db, h.User, clock, audit);
        return new ReportService(h.Db, h.User, clock, journals);
    }

    private static async Task<LoanDto> ActivateCt90Async(LoanHarness h)
    {
        h.AsOfficer();
        var product = await h.SeedProductAsync(20_000m, 3, 0m);
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
        await h.Loans.ApproveAsync(draft.Value.Id);
        h.AsCaissier();
        await h.OpenTillAsync(50_000m);
        var savings = await h.Savings.OpenAccountAsync(h.MemberId, SeedGuids.SavingsProductHtg);
        var disbursed = await h.Loans.DisburseAsync(
            draft.Value.Id,
            new DisburseLoanRequest { SavingsAccountId = savings.Value!.Id },
            "par-disb");
        Assert.True(disbursed.IsSuccess, disbursed.ErrorMessage);
        return disbursed.Value!;
    }
}
