using CPCREDO.Application.Loans;
using CPCREDO.Application.Teller;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Loans;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CPCREDO.Tests.Loans;

public sealed class LoanRepaymentTests
{
    [Fact]
    public void Allocation_applies_penalty_then_interest_then_principal()
    {
        var line = new LoanInstallment
        {
            LineNo = 1,
            PrincipalDue = 833.3333m,
            InterestDue = 166.6667m,
            PenaltyDue = 50m,
            TotalDue = 1000m
        };

        var allocated = LoanRepaymentAllocator.Allocate([line], 200m);
        Assert.Single(allocated);
        Assert.Equal(50m, allocated[0].Penalty);
        Assert.Equal(150m, allocated[0].Interest);
        Assert.Equal(0m, allocated[0].Principal);

        LoanRepaymentAllocator.Apply([line], allocated);
        Assert.Equal(50m, line.PenaltyPaid);
        Assert.Equal(150m, line.InterestPaid);
        Assert.Equal(0m, line.PrincipalPaid);
        Assert.Equal(850m, LoanRepaymentAllocator.RemainingTotal(line));
    }

    [Fact]
    public void Partial_payment_leaves_remaining_balance()
    {
        var line = new LoanInstallment
        {
            LineNo = 1,
            PrincipalDue = 833.3333m,
            InterestDue = 166.6667m,
            TotalDue = 1000m
        };

        var allocated = LoanRepaymentAllocator.Allocate([line], 100m);
        Assert.Equal(0m, allocated[0].Penalty);
        Assert.Equal(100m, allocated[0].Interest);
        Assert.Equal(0m, allocated[0].Principal);
        LoanRepaymentAllocator.Apply([line], allocated);
        Assert.Equal(66.6667m, LoanRepaymentAllocator.RemainingInterest(line));
        Assert.Equal(833.3333m, LoanRepaymentAllocator.RemainingPrincipal(line));
        Assert.Equal(900m, LoanRepaymentAllocator.RemainingTotal(line));
    }

    [Fact]
    public void Dpd_is_days_since_oldest_unpaid_due_date()
    {
        var asOf = new DateOnly(2026, 1, 2);
        var lines = new[]
        {
            new LoanInstallment { LineNo = 1, DueDate = new DateOnly(2025, 12, 28), PrincipalDue = 833.3333m, InterestDue = 166.6667m, TotalDue = 1000m },
            new LoanInstallment { LineNo = 2, DueDate = new DateOnly(2026, 1, 4), PrincipalDue = 833.3333m, InterestDue = 166.6667m, TotalDue = 1000m }
        };
        Assert.Equal(5, LoanDelinquency.DaysPastDue(lines, asOf));
        Assert.Equal(50m, LoanDelinquency.AccruePenalty(lines[0], asOf, 1m));
        Assert.Equal(0m, LoanDelinquency.AccruePenalty(lines[1], asOf, 1m));
    }

    [Fact]
    public async Task Repayment_posts_journal_and_receipt_uses_commercial_name()
    {
        using var h = new LoanHarness();
        var loan = await DisburseAsync(h, commercial: "Court terme");
        h.AsCaissier();
        await h.Teller.OpenAsync(new OpenTillRequest { CurrencyCode = Currencies.Htg, OpeningFloat = 50_000m });

        var repaid = await h.Loans.RepayAsync(loan.Id, new RepayLoanRequest { Amount = 100m }, "rp-100");
        Assert.True(repaid.IsSuccess, repaid.ErrorMessage);
        Assert.Equal(100m, repaid.Value!.Receipt.Interest);
        Assert.Equal(0m, repaid.Value.Receipt.Principal);
        Assert.Equal(0m, repaid.Value.Receipt.Penalty);
        Assert.Equal("Court terme", repaid.Value.Receipt.ProductName);
        Assert.Equal("CPCREDO", repaid.Value.Receipt.LetterheadSigle);
        Assert.Equal(100m, repaid.Value.Loan.Schedule.Installments[0].InterestPaid);
        Assert.Equal(900m, repaid.Value.Loan.Schedule.Installments[0].Remaining);

        var postedId = (await h.Db.LoanRepayments.SingleAsync()).PostedJournalId;
        var journal = await h.Db.JournalEntries.Include(j => j.Lines)
            .SingleAsync(j => j.Id == postedId);
        Assert.Equal(100m, journal.Lines.Sum(l => l.Debit));
        Assert.Equal(100m, journal.Lines.Single(l => l.GlAccountId == SeedGuids.Gl(LoanGl.CashHtg)).Debit);
        Assert.Equal(100m, journal.Lines.Single(l => l.GlAccountId == SeedGuids.Gl(LoanGl.InterestIncomeHtg)).Credit);
    }

    [Fact]
    public async Task Accrual_sets_dpd_and_penalty_then_allocation_takes_penalty_first()
    {
        using var h = new LoanHarness();
        var loan = await DisburseAsync(h, penaltyPerDay: 1m);
        var first = await h.Db.LoanInstallments.SingleAsync(i => i.LoanId == loan.Id && i.LineNo == 1);
        first.DueDate = new DateOnly(2025, 12, 28);
        await h.Db.SaveChangesAsync();

        h.AsGerant();
        var accrued = await h.Loans.RunAccrualAsync();
        Assert.True(accrued.IsSuccess, accrued.ErrorMessage);

        var after = await h.Loans.GetAsync(loan.Id);
        Assert.Equal(5, after.Value!.DaysPastDue);
        Assert.Equal(50m, after.Value.Schedule.Installments[0].PenaltyDue);

        h.AsCaissier();
        await h.Teller.OpenAsync(new OpenTillRequest { CurrencyCode = Currencies.Htg, OpeningFloat = 50_000m });
        var repaid = await h.Loans.RepayAsync(loan.Id, new RepayLoanRequest { Amount = 200m }, "rp-pen");
        Assert.True(repaid.IsSuccess, repaid.ErrorMessage);
        Assert.Equal(50m, repaid.Value!.Receipt.Penalty);
        Assert.Equal(150m, repaid.Value.Receipt.Interest);
        Assert.Equal(0m, repaid.Value.Receipt.Principal);
    }

    [Fact]
    public async Task Collection_sheet_lists_dues_today_and_this_week()
    {
        using var h = new LoanHarness();
        var loan = await DisburseAsync(h);
        var first = await h.Db.LoanInstallments.SingleAsync(i => i.LoanId == loan.Id && i.LineNo == 1);
        first.DueDate = new DateOnly(2026, 1, 2);
        var second = await h.Db.LoanInstallments.SingleAsync(i => i.LoanId == loan.Id && i.LineNo == 2);
        second.DueDate = new DateOnly(2026, 1, 6);
        await h.Db.SaveChangesAsync();

        h.AsGerant();
        var today = await h.Loans.GetCollectionSheetAsync("today");
        Assert.True(today.IsSuccess, today.ErrorMessage);
        Assert.Contains(today.Value!.Rows, r => r.LineNo == 1 && r.DueDate == new DateOnly(2026, 1, 2));
        Assert.DoesNotContain(today.Value.Rows, r => r.LineNo == 2);

        var week = await h.Loans.GetCollectionSheetAsync("week");
        Assert.Contains(week.Value!.Rows, r => r.LineNo == 1);
        Assert.Contains(week.Value.Rows, r => r.LineNo == 2 && r.DueDate == new DateOnly(2026, 1, 6));
        Assert.All(week.Value.Rows, r => Assert.Equal(1000m, r.TotalRemaining));
    }

    [Fact]
    public async Task Gerant_can_collect_repayment()
    {
        using var h = new LoanHarness();
        var loan = await DisburseAsync(h);
        h.AsGerant();
        await h.Teller.OpenAsync(new OpenTillRequest { CurrencyCode = Currencies.Htg, OpeningFloat = 50_000m });
        var repaid = await h.Loans.RepayAsync(
            loan.Id,
            new RepayLoanRequest { Amount = 100m, CurrencyCode = "HTG" },
            "rp-gerant");
        Assert.True(repaid.IsSuccess, repaid.ErrorMessage);
        Assert.Equal(100m, repaid.Value!.Receipt.Amount);
        Assert.Equal(11_900m, repaid.Value.Receipt.RemainingBalance);
    }

    [Fact]
    public async Task Officer_cannot_collect_repayment()
    {
        using var h = new LoanHarness();
        var loan = await DisburseAsync(h);
        h.AsOfficer();
        var denied = await h.Loans.RepayAsync(loan.Id, new RepayLoanRequest { Amount = 100m, CurrencyCode = "HTG" }, "rp-off");
        Assert.Equal("auth.forbidden", denied.ErrorCode);
    }

    [Fact]
    public async Task Currency_mismatch_is_rejected()
    {
        using var h = new LoanHarness();
        var loan = await DisburseAsync(h);
        h.AsCaissier();
        await h.Teller.OpenAsync(new OpenTillRequest { CurrencyCode = Currencies.Htg, OpeningFloat = 50_000m });
        var failed = await h.Loans.RepayAsync(
            loan.Id,
            new RepayLoanRequest { Amount = 100m, CurrencyCode = "USD" },
            "rp-usd");
        Assert.Equal("loan.currency", failed.ErrorCode);
    }

    [Fact]
    public async Task Partial_repayment_does_not_pay_off_loan()
    {
        using var h = new LoanHarness();
        var loan = await DisburseAsync(h);
        h.AsCaissier();
        await h.Teller.OpenAsync(new OpenTillRequest { CurrencyCode = Currencies.Htg, OpeningFloat = 50_000m });
        var repaid = await h.Loans.RepayAsync(loan.Id, new RepayLoanRequest { Amount = 500m }, "rp-partial");
        Assert.True(repaid.IsSuccess, repaid.ErrorMessage);
        Assert.Equal(nameof(LoanStatus.Active), repaid.Value!.Loan.Status);
        Assert.Equal(11_500m, repaid.Value.Loan.Schedule.Installments.Sum(i => i.Remaining));
    }

    private static async Task<LoanDto> DisburseAsync(LoanHarness h, string? commercial = null, decimal penaltyPerDay = 0m)
    {
        h.AsOfficer();
        var previous = h.User.Roles;
        var previousId = h.User.UserId;
        h.AsAdmin();
        var request = h.ProductRequest(commercial: commercial);
        request.OfficerMaxApproval = 20_000m;
        request.LatePenaltyPercentPerDay = penaltyPerDay;
        var product = await h.Products.CreateAsync(request);
        Assert.True(product.IsSuccess, product.ErrorMessage);
        h.User.Roles = previous;
        h.User.UserId = previousId;

        var draft = await h.Loans.CreateDraftAsync(new CreateLoanRequest
        {
            MemberId = h.MemberId,
            ProductId = product.Value!.Id,
            Principal = 10_000m,
            AgreedRatePercent = 20m
        });
        Assert.True(draft.IsSuccess, draft.ErrorMessage);
        await h.Loans.SubmitAsync(draft.Value!.Id);
        h.AsGerant();
        await h.Loans.ApproveAsync(draft.Value.Id);
        h.AsCaissier();
        await h.Teller.OpenAsync(new OpenTillRequest { CurrencyCode = Currencies.Htg, OpeningFloat = 50_000m });
        var savings = await h.Savings.OpenAccountAsync(h.MemberId, SeedGuids.SavingsProductHtg);
        var disbursed = await h.Loans.DisburseAsync(
            draft.Value.Id,
            new DisburseLoanRequest { SavingsAccountId = savings.Value!.Id },
            "disb-repay");
        Assert.True(disbursed.IsSuccess, disbursed.ErrorMessage);
        var till = await h.Db.TillSessions.SingleAsync(t => t.Status == CPCREDO.Domain.Teller.TillSessionStatus.Open);
        till.Status = CPCREDO.Domain.Teller.TillSessionStatus.Closed;
        await h.Db.SaveChangesAsync();
        return disbursed.Value!;
    }
}
