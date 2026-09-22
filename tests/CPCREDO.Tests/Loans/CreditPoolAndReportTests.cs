using CPCREDO.Application.Accounting;
using CPCREDO.Application.Loans;
using CPCREDO.Application.Teller;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Domain.Loans;
using CPCREDO.Infrastructure.Reports;
using CPCREDO.Tests.Accounting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CPCREDO.Tests.Loans;

public sealed class CreditPoolAndReportTests
{
    [Fact]
    public async Task Teller_cannot_fund_pool_and_pool_is_not_opening_float()
    {
        using var h = new LoanHarness();
        h.AsCaissier();
        var denied = await h.Pool.FundAsync(new FundCreditPoolRequest { Amount = 5_000m, SourceKind = "Vault" }, "pool-teller");
        Assert.False(denied.IsSuccess);
        Assert.Equal("auth.forbidden", denied.ErrorCode);

        h.AsGerant();
        var viaTill = await h.Teller.CreateInternalMovementAsync(
            new CreateInternalCashRequest
            {
                Direction = "VaultToTill",
                Reason = "CreditPoolFunding",
                Amount = 1_000m,
                DestinationTellerUserId = SeedGuids.CaissierUserId
            },
            "pool-as-float");
        Assert.False(viaTill.IsSuccess);
        Assert.Equal("internal.pool", viaTill.ErrorCode);
    }

    [Fact]
    public async Task Pool_available_rises_on_funding_and_falls_on_disbursement()
    {
        using var h = new LoanHarness();
        var loan = await ApproveAsync(h, 10_000m);
        h.AsAdmin();
        var vault = await h.Journals.PostAsync(
            new CreateJournalRequest
            {
                Description = "Coffre pour fonds",
                CurrencyCode = Currencies.Htg,
                Lines =
                [
                    new CreateJournalLineRequest { GlAccountId = SeedGuids.Gl("1030"), Debit = 50_000m, Credit = 0m },
                    new CreateJournalLineRequest { GlAccountId = SeedGuids.Gl("3010"), Debit = 0m, Credit = 50_000m }
                ]
            },
            "vault-pool");
        Assert.True(vault.IsSuccess, vault.ErrorMessage);

        h.AsGerant();
        var funded = await h.Pool.FundAsync(new FundCreditPoolRequest { Amount = 20_000m, SourceKind = "Vault" }, "fund-pool");
        Assert.True(funded.IsSuccess, funded.ErrorMessage);
        Assert.Equal(20_000m, funded.Value!.FundedTotal);
        Assert.Equal(10_000m, funded.Value.ReservedApprovals);
        Assert.Equal(10_000m, funded.Value.AvailableToLend);

        h.AsCaissier();
        await h.OpenTillAsync(50_000m);
        var savings = await h.Savings.OpenAccountAsync(h.MemberId, SeedGuids.SavingsProductHtg);
        var disbursed = await h.Loans.DisburseAsync(
            loan.Id,
            new DisburseLoanRequest { SavingsAccountId = savings.Value!.Id },
            "disb-pool");
        Assert.True(disbursed.IsSuccess, disbursed.ErrorMessage);

        h.AsGerant();
        var after = await h.Pool.GetAsync("HTG");
        Assert.Equal(10_000m, after.Value!.DisbursedTotal);
        Assert.Equal(10_000m, after.Value.AvailableToLend);
        Assert.Equal(0m, after.Value.ReservedApprovals);
    }

    [Fact]
    public async Task Credit_report_counts_match_dossiers_and_interest_is_collected_only()
    {
        using var h = new LoanHarness();
        var loan = await ApproveAsync(h, 10_000m);
        h.AsAdmin();
        await h.Journals.PostAsync(
            new CreateJournalRequest
            {
                Description = "Coffre pour fonds",
                CurrencyCode = Currencies.Htg,
                Lines =
                [
                    new CreateJournalLineRequest { GlAccountId = SeedGuids.Gl("1030"), Debit = 50_000m, Credit = 0m },
                    new CreateJournalLineRequest { GlAccountId = SeedGuids.Gl("3010"), Debit = 0m, Credit = 50_000m }
                ]
            },
            "vault-rep");
        h.AsGerant();
        await h.Pool.FundAsync(new FundCreditPoolRequest { Amount = 20_000m, SourceKind = "Vault" }, "fund-rep");
        h.AsCaissier();
        await h.OpenTillAsync(50_000m);
        var savings = await h.Savings.OpenAccountAsync(h.MemberId, SeedGuids.SavingsProductHtg);
        var disbursed = await h.Loans.DisburseAsync(
            loan.Id,
            new DisburseLoanRequest { SavingsAccountId = savings.Value!.Id },
            "disb-rep");
        Assert.True(disbursed.IsSuccess, disbursed.ErrorMessage);
        var repay = await h.Loans.RepayAsync(loan.Id, new RepayLoanRequest { Amount = 1_000m }, "pay-rep");
        Assert.True(repay.IsSuccess, repay.ErrorMessage);

        h.AsGerant();
        var reports = new ReportService(h.Db, h.User, new FixedClock(), h.Journals);
        var report = await reports.GetCreditReportAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 3), null, null);
        Assert.True(report.IsSuccess, report.ErrorMessage);
        Assert.Equal(1, report.Value!.Received);
        Assert.Equal(1, report.Value.Approved);
        Assert.Equal(1, report.Value.Disbursed);
        Assert.Equal(10_000m, report.Value.ApprovedAmount);
        Assert.Equal(10_000m, report.Value.DisbursedAmount);
        Assert.Equal(repay.Value!.Receipt.Interest, report.Value.InterestReceived);
        Assert.True(report.Value.InterestAccrued >= 0m);
        Assert.Equal(0, report.Value.Cancelled);
        Assert.True(report.Value.PoolFunded >= 20_000m);
        Assert.True(report.Value.PoolDisbursed >= 10_000m);
    }

    private static async Task<LoanDto> ApproveAsync(LoanHarness h, decimal principal)
    {
        h.AsOfficer();
        var product = await h.SeedProductAsync();
        var draft = await h.Loans.CreateDraftAsync(new CreateLoanRequest
        {
            MemberId = h.MemberId,
            ProductId = product.Id,
            Principal = principal,
            AgreedRatePercent = 20m
        });
        Assert.True(draft.IsSuccess, draft.ErrorMessage);
        var submitted = await h.Loans.SubmitAsync(draft.Value!.Id);
        Assert.True(submitted.IsSuccess, submitted.ErrorMessage);
        h.AsGerant();
        var approved = await h.Loans.ApproveAsync(draft.Value.Id);
        Assert.True(approved.IsSuccess, approved.ErrorMessage);
        return approved.Value!;
    }
}
