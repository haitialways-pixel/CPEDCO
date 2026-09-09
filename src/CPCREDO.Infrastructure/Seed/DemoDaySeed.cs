using CPCREDO.Domain.Accounting;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Loans;
using CPCREDO.Domain.Members;
using CPCREDO.Domain.Savings;
using CPCREDO.Domain.Teller;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CPCREDO.Infrastructure.Seed;

internal static class DemoDaySeed
{
    public static async Task EnsureAsync(CpcredoDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        if (await db.TillSessions.AnyAsync(t => t.Id == SeedGuids.DemoTillId, cancellationToken))
            return;

        var memberId = SeedGuids.MemberMarieClaire;
        if (!await db.Members.AnyAsync(m => m.Id == memberId, cancellationToken))
            return;

        await EnsureCt90ProductAsync(db, cancellationToken);
        await EnsureSavingsAsync(db, cancellationToken);

        var till = new TillSession
        {
            Id = SeedGuids.DemoTillId,
            TenantId = SeedGuids.TenantId,
            BranchId = SeedGuids.BranchId,
            UserId = SeedGuids.CaissierUserId,
            CurrencyCode = Currencies.Htg,
            Status = TillSessionStatus.Open,
            OpeningFloat = 50_000m,
            ExpectedCash = 50_000m,
            OpenedAtUtc = SeedInstant
        };
        db.TillSessions.Add(till);

        await PostCashAsync(db, till, isDeposit: true, 2_000m, "demo-dep-1", "J-DEMO-01", "Dépôt en espèces (démo 1)", cancellationToken);
        await PostCashAsync(db, till, isDeposit: true, 1_000m, "demo-dep-2", "J-DEMO-02", "Dépôt en espèces (démo 2)", cancellationToken);
        await PostCashAsync(db, till, isDeposit: false, 500m, "demo-wd-1", "J-DEMO-03", "Retrait en espèces (démo)", cancellationToken);
        await DisburseCt90Async(db, till, cancellationToken);
        await RepayAsync(db, till, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Demo day seeded: 1 till, 2 deposits, 1 withdrawal, 1 CT90 disbursement, 1 repayment.");
    }

    private static readonly DateTime SeedInstant = new(2026, 1, 2, 13, 0, 0, DateTimeKind.Utc);

    private static async Task EnsureCt90ProductAsync(CpcredoDbContext db, CancellationToken cancellationToken)
    {
        if (await db.LoanProducts.AnyAsync(p => p.TenantId == SeedGuids.TenantId, cancellationToken))
            return;
        db.LoanProducts.Add(new LoanProduct
        {
            Id = SeedGuids.LoanProductCt90,
            TenantId = SeedGuids.TenantId,
            Code = "ST90",
            LegalName = "Crédit court terme 90 jours",
            CommercialName = "CT90",
            SmsName = "CT90",
            TermDays = 90,
            InstallmentCount = 12,
            DefaultRatePercent = 20m,
            MaxRenewals = 3,
            RenewalMaxOutstandingPercent = 20m,
            CompulsorySavingsPercent = 10m,
            IsActive = true,
            CreatedAtUtc = SeedInstant
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureSavingsAsync(CpcredoDbContext db, CancellationToken cancellationToken)
    {
        if (await db.SavingsAccounts.AnyAsync(a => a.Id == SeedGuids.DemoSavingsMarie, cancellationToken))
            return;
        db.SavingsAccounts.Add(new SavingsAccount
        {
            Id = SeedGuids.DemoSavingsMarie,
            TenantId = SeedGuids.TenantId,
            MemberId = SeedGuids.MemberMarieClaire,
            BranchId = SeedGuids.BranchId,
            ProductId = SeedGuids.SavingsProductHtg,
            AccountNo = "A-000201",
            CurrencyCode = Currencies.Htg,
            IsActive = true,
            OpenedAtUtc = SeedInstant
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task PostCashAsync(
        CpcredoDbContext db,
        TillSession till,
        bool isDeposit,
        decimal amount,
        string idempotency,
        string journalNo,
        string title,
        CancellationToken cancellationToken)
    {
        amount = MoneyAmount.Normalize(amount);
        var cashGl = SeedGuids.Gl("1010");
        var liabilityGl = SeedGuids.Gl("2010");
        var journal = NewJournal(
            journalNo,
            isDeposit ? $"Dépôt {amount} HTG" : $"Retrait {amount} HTG",
            isDeposit
                ?
                [
                    new CreateDemoLine(cashGl, amount, 0m, "Caisse"),
                    new CreateDemoLine(liabilityGl, 0m, amount, "Épargne membre")
                ]
                :
                [
                    new CreateDemoLine(liabilityGl, amount, 0m, "Épargne membre"),
                    new CreateDemoLine(cashGl, 0m, amount, "Caisse")
                ],
            idempotency);
        journal.EnsureBalanced();
        db.JournalEntries.Add(journal);
        db.SavingsLedgerEntries.Add(new SavingsLedgerEntry
        {
            TenantId = SeedGuids.TenantId,
            SavingsAccountId = SeedGuids.DemoSavingsMarie,
            ValueDateUtc = DateTime.SpecifyKind(SeedInstant.Date, DateTimeKind.Utc),
            PostedAtUtc = SeedInstant,
            EntryType = isDeposit ? "Credit" : "Debit",
            Amount = amount,
            CurrencyCode = Currencies.Htg,
            Description = title,
            JournalEntryId = journal.Id,
            TillSessionId = till.Id,
            IdempotencyKey = idempotency
        });
        till.ExpectedCash = MoneyAmount.Normalize(till.ExpectedCash + (isDeposit ? amount : -amount));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task DisburseCt90Async(CpcredoDbContext db, TillSession till, CancellationToken cancellationToken)
    {
        if (await db.Loans.AnyAsync(l => l.Id == SeedGuids.DemoLoanId, cancellationToken))
            return;

        var product = await db.LoanProducts.FirstAsync(p => p.TenantId == SeedGuids.TenantId, cancellationToken);
        const decimal principal = 10_000m;
        var schedule = LoanScheduleFactory.BuildWeeklyFlat(principal, 20m, 12, new DateOnly(2026, 1, 2));
        var compulsory = MoneyAmount.Normalize(principal * 0.10m);
        var cash = MoneyAmount.Normalize(principal - compulsory);
        var loan = new Loan
        {
            Id = SeedGuids.DemoLoanId,
            TenantId = SeedGuids.TenantId,
            BranchId = SeedGuids.BranchId,
            MemberId = SeedGuids.MemberMarieClaire,
            ProductId = product.Id,
            LoanNo = "LN-DEMO-01",
            Principal = principal,
            AgreedRatePercent = 20m,
            RateAppliesToTermDays = 90,
            TermDays = 90,
            InstallmentCount = 12,
            TotalInterest = 2_000m,
            TotalDue = 12_000m,
            Status = LoanStatus.Active,
            CycleNumber = 1,
            OriginationDate = new DateOnly(2026, 1, 2),
            CreatedByUserId = SeedGuids.AdminUserId,
            CreatedAtUtc = SeedInstant,
            CompulsorySavingsPercent = 10m,
            CompulsorySavingsAmount = compulsory,
            CashDisbursedAmount = cash,
            SavingsDisbursedAmount = compulsory,
            SavingsAccountId = SeedGuids.DemoSavingsMarie,
            DisbursedByUserId = SeedGuids.CaissierUserId,
            DisbursedAtUtc = SeedInstant,
            TillSessionId = till.Id,
            Approver1Id = SeedGuids.GerantUserId,
            Approved1AtUtc = SeedInstant
        };
        foreach (var line in schedule)
        {
            loan.Installments.Add(new LoanInstallment
            {
                LoanId = loan.Id,
                LineNo = line.LineNo,
                DueDate = line.DueDate,
                PrincipalDue = line.PrincipalDue,
                InterestDue = line.InterestDue,
                TotalDue = line.TotalDue
            });
        }

        var journal = NewJournal(
            "J-DEMO-04",
            $"Décaissement {loan.LoanNo}",
            [
                new CreateDemoLine(SeedGuids.Gl(LoanGl.ShortTermPortfolioHtg), principal, 0m, "Portefeuille"),
                new CreateDemoLine(SeedGuids.Gl(LoanGl.CashHtg), 0m, cash, "Caisse"),
                new CreateDemoLine(SeedGuids.Gl("2010"), 0m, compulsory, "Épargne obligatoire")
            ],
            "demo-disb-ct90");
        journal.EnsureBalanced();
        loan.PostedJournalId = journal.Id;
        db.JournalEntries.Add(journal);
        db.Loans.Add(loan);
        db.SavingsLedgerEntries.Add(new SavingsLedgerEntry
        {
            TenantId = SeedGuids.TenantId,
            SavingsAccountId = SeedGuids.DemoSavingsMarie,
            ValueDateUtc = DateTime.SpecifyKind(SeedInstant.Date, DateTimeKind.Utc),
            PostedAtUtc = SeedInstant,
            EntryType = "Credit",
            Amount = compulsory,
            CurrencyCode = Currencies.Htg,
            Description = "Épargne obligatoire CT90",
            JournalEntryId = journal.Id,
            TillSessionId = till.Id,
            IdempotencyKey = "demo-disb-ct90-sav"
        });
        db.SavingsLiens.Add(new SavingsLien
        {
            TenantId = SeedGuids.TenantId,
            SavingsAccountId = SeedGuids.DemoSavingsMarie,
            Amount = compulsory,
            Reason = $"Épargne obligatoire {loan.LoanNo}",
            CreatedAtUtc = SeedInstant
        });
        till.ExpectedCash = MoneyAmount.Normalize(till.ExpectedCash - cash);
        await EnsureSequenceAsync(db, "LoanNo", 1, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task RepayAsync(CpcredoDbContext db, TillSession till, CancellationToken cancellationToken)
    {
        if (await db.LoanRepayments.AnyAsync(r => r.Id == SeedGuids.DemoRepaymentId, cancellationToken))
            return;

        const decimal amount = 1_000m;
        var loan = await db.Loans.Include(l => l.Installments)
            .FirstAsync(l => l.Id == SeedGuids.DemoLoanId, cancellationToken);
        var allocations = LoanRepaymentAllocator.Allocate(loan.Installments, amount);
        LoanRepaymentAllocator.Apply(loan.Installments, allocations);
        var penalty = allocations.Sum(a => a.Penalty);
        var interest = allocations.Sum(a => a.Interest);
        var principal = allocations.Sum(a => a.Principal);

        var lines = new List<CreateDemoLine>
        {
            new(SeedGuids.Gl(LoanGl.CashHtg), amount, 0m, "Caisse")
        };
        if (principal > 0m)
            lines.Add(new(SeedGuids.Gl(LoanGl.ShortTermPortfolioHtg), 0m, principal, "Capital"));
        if (interest > 0m)
            lines.Add(new(SeedGuids.Gl(LoanGl.InterestIncomeHtg), 0m, interest, "Intérêt"));
        if (penalty > 0m)
            lines.Add(new(SeedGuids.Gl(LoanGl.PenaltyIncomeHtg), 0m, penalty, "Pénalité"));

        var journal = NewJournal("J-DEMO-05", $"Remboursement {loan.LoanNo}", lines, "demo-repay-1");
        journal.EnsureBalanced();
        db.JournalEntries.Add(journal);
        db.LoanRepayments.Add(new LoanRepayment
        {
            Id = SeedGuids.DemoRepaymentId,
            TenantId = SeedGuids.TenantId,
            LoanId = loan.Id,
            ReceiptNo = "RP-DEMO-01",
            Amount = amount,
            PenaltyAllocated = penalty,
            InterestAllocated = interest,
            PrincipalAllocated = principal,
            CurrencyCode = Currencies.Htg,
            PostedJournalId = journal.Id,
            TillSessionId = till.Id,
            CreatedByUserId = SeedGuids.CaissierUserId,
            CreatedAtUtc = SeedInstant,
            IdempotencyKey = "demo-repay-1"
        });
        till.ExpectedCash = MoneyAmount.Normalize(till.ExpectedCash + amount);
        await EnsureSequenceAsync(db, "RepaymentNo", 1, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private readonly record struct CreateDemoLine(Guid Gl, decimal Debit, decimal Credit, string Description);

    private static JournalEntry NewJournal(string journalNo, string description, IReadOnlyList<CreateDemoLine> lines, string idempotency)
    {
        var journal = new JournalEntry
        {
            Id = Guid.NewGuid(),
            TenantId = SeedGuids.TenantId,
            BranchId = SeedGuids.BranchId,
            JournalNo = journalNo,
            ValueDate = new DateOnly(2026, 1, 2),
            PostedAtUtc = SeedInstant,
            PostedByUserId = SeedGuids.CaissierUserId,
            Description = description,
            CurrencyCode = Currencies.Htg,
            Status = JournalStatus.Posted,
            IdempotencyKey = idempotency,
            CreatedAtUtc = SeedInstant
        };
        var n = 1;
        foreach (var line in lines)
        {
            journal.Lines.Add(new JournalLine
            {
                Id = Guid.NewGuid(),
                JournalEntryId = journal.Id,
                LineNo = n++,
                GlAccountId = line.Gl,
                Debit = line.Debit,
                Credit = line.Credit,
                Description = line.Description
            });
        }
        return journal;
    }

    private static async Task EnsureSequenceAsync(CpcredoDbContext db, string key, int lastValue, CancellationToken cancellationToken)
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
}
