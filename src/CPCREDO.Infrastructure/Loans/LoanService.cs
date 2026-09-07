using CPCREDO.Application.Accounting;
using CPCREDO.Application.Common;
using CPCREDO.Application.Loans;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Domain.Loans;
using CPCREDO.Domain.Members;
using CPCREDO.Domain.Savings;
using CPCREDO.Domain.Teller;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CPCREDO.Infrastructure.Loans;

public sealed class LoanService : ILoanService
{
    private readonly CpcredoDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditLogger _audit;
    private readonly IJournalService _journals;

    public LoanService(
        CpcredoDbContext db,
        ICurrentUser currentUser,
        IClock clock,
        IAuditLogger audit,
        IJournalService journals)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _journals = journals;
    }

    public async Task<Result<LoanScheduleDto>> PreviewScheduleAsync(PreviewLoanRequest request, CancellationToken cancellationToken = default)
    {
        var built = await BuildScheduleAsync(request, cancellationToken);
        if (!built.IsSuccess)
            return Result<LoanScheduleDto>.Fail(built.ErrorCode!, built.ErrorMessage!);
        return Result<LoanScheduleDto>.Ok(built.Value!.Schedule);
    }

    public async Task<Result<PayoffQuoteDto>> PreviewPayoffAsync(
        PreviewLoanRequest request,
        int daysElapsed,
        CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<PayoffQuoteDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);
        var product = await LoadProductAsync(request.ProductId, cancellationToken);
        if (product is null)
            return Result<PayoffQuoteDto>.Fail("loan.product_not_found", "Produit de crédit introuvable.");
        var principal = MoneyAmount.Normalize(request.Principal);
        var rate = request.AgreedRatePercent;
        var interest = FlatTermInterest.AccruedInterest(
            principal, rate, product.TermDays, daysElapsed, product.EarlyPayoffChargesFullFlatInterest);
        return Result<PayoffQuoteDto>.Ok(new PayoffQuoteDto(
            principal,
            interest,
            MoneyAmount.Normalize(principal + interest),
            daysElapsed,
            product.TermDays,
            product.EarlyPayoffChargesFullFlatInterest));
    }

    public async Task<Result<IReadOnlyList<LoanDto>>> ListAsync(string? status = null, Guid? memberId = null, CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<IReadOnlyList<LoanDto>>.Fail(auth.ErrorCode!, auth.ErrorMessage!);
        var query = _db.Loans.AsNoTracking()
            .Include(l => l.Product)
            .Include(l => l.Installments)
            .Where(l => l.TenantId == _currentUser.TenantId);
        if (memberId is { } mid)
            query = query.Where(l => l.MemberId == mid);
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<LoanStatus>(status, true, out var parsed))
            query = query.Where(l => l.Status == parsed);
        var loans = await query.OrderByDescending(l => l.CreatedAtUtc).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<LoanDto>>.Ok(await MapManyAsync(loans, cancellationToken));
    }

    public async Task<Result<LoanDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<LoanDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);
        var loan = await LoadLoanAsync(id, tracking: false, cancellationToken);
        if (loan is null)
            return Result<LoanDto>.Fail("loan.not_found", "Dossier de crédit introuvable.");
        return Result<LoanDto>.Ok(await MapAsync(loan, cancellationToken));
    }

    public async Task<Result<LoanDto>> CreateDraftAsync(CreateLoanRequest request, CancellationToken cancellationToken = default)
    {
        var gate = RequireWriter();
        if (!gate.IsSuccess)
            return Result<LoanDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var member = await _db.Members.FirstOrDefaultAsync(
            m => m.Id == request.MemberId && m.TenantId == _currentUser.TenantId, cancellationToken);
        if (member is null)
            return Result<LoanDto>.Fail("loan.member_not_found", "Membre introuvable.");
        var eligible = EnsureEligible(member);
        if (!eligible.IsSuccess)
            return Result<LoanDto>.Fail(eligible.ErrorCode!, eligible.ErrorMessage!);

        var built = await BuildScheduleAsync(new PreviewLoanRequest
        {
            ProductId = request.ProductId,
            Principal = request.Principal,
            AgreedRatePercent = request.AgreedRatePercent
        }, cancellationToken);
        if (!built.IsSuccess)
            return Result<LoanDto>.Fail(built.ErrorCode!, built.ErrorMessage!);

        var product = built.Value!.Product;
        var schedule = built.Value.Schedule;
        var loan = new Loan
        {
            TenantId = _currentUser.TenantId!.Value,
            BranchId = _currentUser.BranchId!.Value,
            MemberId = request.MemberId,
            ProductId = product.Id,
            LoanNo = await NextLoanNoAsync(cancellationToken),
            Principal = schedule.Principal,
            AgreedRatePercent = schedule.AgreedRatePercent,
            RateAppliesToTermDays = product.TermDays,
            TermDays = product.TermDays,
            InstallmentCount = product.InstallmentCount,
            RepaymentFrequency = product.RepaymentFrequency,
            InterestMethod = InterestMethod.FlatOnOriginalPrincipalForTerm,
            TotalInterest = schedule.TotalInterest,
            TotalDue = schedule.TotalDue,
            CurrencyCode = Currencies.Htg,
            Status = LoanStatus.Draft,
            CycleNumber = 1,
            OriginationDate = schedule.Installments[0].DueDate.AddDays(-7),
            CreatedByUserId = _currentUser.UserId!.Value,
            CreatedAtUtc = _clock.UtcNow,
            CompulsorySavingsPercent = product.CompulsorySavingsPercent,
            Product = product
        };
        foreach (var line in schedule.Installments)
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

        _db.Loans.Add(loan);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Loan.DraftCreated", nameof(Loan), loan.Id, new { loan.LoanNo, loan.Principal, loan.AgreedRatePercent }, loan.TenantId, _currentUser.UserId, cancellationToken: cancellationToken);
        return Result<LoanDto>.Ok(await MapAsync(loan, cancellationToken));
    }

    public async Task<Result<LoanDto>> SubmitAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var gate = RequireWriter();
        if (!gate.IsSuccess)
            return Result<LoanDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var loan = await LoadLoanAsync(id, tracking: true, cancellationToken);
        if (loan is null)
            return Result<LoanDto>.Fail("loan.not_found", "Dossier de crédit introuvable.");
        if (loan.Status != LoanStatus.Draft)
            return Result<LoanDto>.Fail("loan.not_draft", "Seul un brouillon peut être soumis pour approbation.");

        var member = await _db.Members.FirstOrDefaultAsync(
            m => m.Id == loan.MemberId && m.TenantId == _currentUser.TenantId, cancellationToken);
        if (member is null)
            return Result<LoanDto>.Fail("loan.member_not_found", "Membre introuvable.");
        var eligible = EnsureEligible(member);
        if (!eligible.IsSuccess)
            return Result<LoanDto>.Fail(eligible.ErrorCode!, eligible.ErrorMessage!);

        loan.Status = LoanStatus.PendingApproval;
        loan.SubmittedByUserId = _currentUser.UserId;
        loan.SubmittedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Loan.Submitted", nameof(Loan), loan.Id, new { loan.LoanNo }, loan.TenantId, _currentUser.UserId, cancellationToken: cancellationToken);
        return Result<LoanDto>.Ok(await MapAsync(loan, cancellationToken));
    }

    public async Task<Result<LoanDto>> ApproveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var gate = RequireApprover();
        if (!gate.IsSuccess)
            return Result<LoanDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var loan = await LoadLoanAsync(id, tracking: true, cancellationToken);
        if (loan is null)
            return Result<LoanDto>.Fail("loan.not_found", "Dossier de crédit introuvable.");
        if (loan.Status != LoanStatus.PendingApproval)
            return Result<LoanDto>.Fail("loan.not_pending", "Ce dossier n’est pas en attente d’approbation.");

        var userId = _currentUser.UserId!.Value;
        var needsSecond = RequiresSecondApproval(loan);
        if (loan.Approver1Id is null)
        {
            loan.Approver1Id = userId;
            loan.Approved1AtUtc = _clock.UtcNow;
            if (!needsSecond)
                loan.Status = LoanStatus.Approved;
        }
        else
        {
            if (!needsSecond)
                return Result<LoanDto>.Fail("loan.already_approved", "Ce dossier est déjà approuvé.");
            if (loan.Approver1Id == userId)
                return Result<LoanDto>.Fail("loan.same_approver", "Le second approbateur doit être une autre personne.");
            loan.Approver2Id = userId;
            loan.Approved2AtUtc = _clock.UtcNow;
            loan.Status = LoanStatus.Approved;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Loan.Approved", nameof(Loan), loan.Id, new { loan.LoanNo, loan.Status, loan.Approver1Id, loan.Approver2Id }, loan.TenantId, userId, cancellationToken: cancellationToken);
        return Result<LoanDto>.Ok(await MapAsync(loan, cancellationToken));
    }

    public async Task<Result<LoanDto>> RejectAsync(Guid id, RejectLoanRequest request, CancellationToken cancellationToken = default)
    {
        var gate = RequireApprover();
        if (!gate.IsSuccess)
            return Result<LoanDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var loan = await LoadLoanAsync(id, tracking: true, cancellationToken);
        if (loan is null)
            return Result<LoanDto>.Fail("loan.not_found", "Dossier de crédit introuvable.");
        if (loan.Status != LoanStatus.PendingApproval)
            return Result<LoanDto>.Fail("loan.not_pending", "Ce dossier n’est pas en attente d’approbation.");

        loan.Status = LoanStatus.Rejected;
        loan.RejectedByUserId = _currentUser.UserId;
        loan.RejectedAtUtc = _clock.UtcNow;
        loan.RejectReason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Loan.Rejected", nameof(Loan), loan.Id, new { loan.LoanNo, loan.RejectReason }, loan.TenantId, _currentUser.UserId, cancellationToken: cancellationToken);
        return Result<LoanDto>.Ok(await MapAsync(loan, cancellationToken));
    }

    public async Task<Result<LoanDto>> DisburseAsync(
        Guid id,
        DisburseLoanRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var gate = RequireCashier();
        if (!gate.IsSuccess)
            return Result<LoanDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var loan = await LoadLoanAsync(id, tracking: true, cancellationToken);
        if (loan is null)
            return Result<LoanDto>.Fail("loan.not_found", "Dossier de crédit introuvable.");
        if (loan.Status == LoanStatus.Active && loan.PostedJournalId is not null)
            return Result<LoanDto>.Ok(await MapAsync(loan, cancellationToken));
        if (loan.Status != LoanStatus.Approved)
            return Result<LoanDto>.Fail("loan.not_approved", "Seul un dossier approuvé peut être décaissé.");

        var member = await _db.Members.FirstOrDefaultAsync(
            m => m.Id == loan.MemberId && m.TenantId == _currentUser.TenantId, cancellationToken);
        if (member is null)
            return Result<LoanDto>.Fail("loan.member_not_found", "Membre introuvable.");
        var eligible = EnsureEligible(member);
        if (!eligible.IsSuccess)
            return Result<LoanDto>.Fail(eligible.ErrorCode!, eligible.ErrorMessage!);

        var till = await FindOpenTillAsync(loan.CurrencyCode, cancellationToken);
        if (till is null)
            return Result<LoanDto>.Fail("till.not_open", "Impossible de décaisser : aucune caisse ouverte pour cet utilisateur et cette agence.");

        var compulsory = MoneyAmount.Normalize(loan.Principal * (loan.CompulsorySavingsPercent / 100m));
        var cashAmount = MoneyAmount.Normalize(loan.Principal - compulsory);
        SavingsAccount? savings = null;
        if (compulsory > 0m)
        {
            if (request.SavingsAccountId is null)
                return Result<LoanDto>.Fail("loan.savings_required", "Un compte d’épargne est obligatoire pour l’épargne bloquée.");
            savings = await _db.SavingsAccounts
                .Include(a => a.Product)
                .FirstOrDefaultAsync(
                    a => a.Id == request.SavingsAccountId && a.TenantId == _currentUser.TenantId && a.IsActive,
                    cancellationToken);
            if (savings is null)
                return Result<LoanDto>.Fail("savings.account.not_found", "Compte d’épargne introuvable.");
            if (savings.MemberId != loan.MemberId)
                return Result<LoanDto>.Fail("loan.savings_member", "Le compte d’épargne n’appartient pas à ce membre.");
            if (savings.IsBlocked)
                return Result<LoanDto>.Fail("savings.blocked", "Ce compte d’épargne est bloqué.");
            if (!string.Equals(savings.CurrencyCode, loan.CurrencyCode, StringComparison.OrdinalIgnoreCase))
                return Result<LoanDto>.Fail("loan.savings_currency", "Le compte d’épargne n’est pas dans la devise du prêt.");
            if (savings.Product is null)
                return Result<LoanDto>.Fail("savings.product.not_found", "Produit d’épargne introuvable.");
        }

        var portfolioId = SeedGuids.Gl(LoanGl.ShortTermPortfolioHtg);
        var cashGlId = SeedGuids.Gl(LoanGl.CashHtg);
        var lines = new List<CreateJournalLineRequest>
        {
            new() { GlAccountId = portfolioId, Debit = loan.Principal, Credit = 0m, Description = "Portefeuille crédit" }
        };
        if (cashAmount > 0m)
            lines.Add(new CreateJournalLineRequest { GlAccountId = cashGlId, Debit = 0m, Credit = cashAmount, Description = "Caisse" });
        if (compulsory > 0m && savings?.Product is not null)
            lines.Add(new CreateJournalLineRequest
            {
                GlAccountId = savings.Product.LiabilityGlAccountId,
                Debit = 0m,
                Credit = compulsory,
                Description = "Épargne obligatoire"
            });

        var journal = await _journals.PostAsync(new CreateJournalRequest
        {
            Description = $"Décaissement {loan.LoanNo}",
            CurrencyCode = loan.CurrencyCode,
            BranchId = till.BranchId,
            Lines = lines
        }, idempotencyKey, cancellationToken);
        if (!journal.IsSuccess)
            return Result<LoanDto>.Fail(journal.ErrorCode!, journal.ErrorMessage!);

        Guid? lienId = null;
        if (compulsory > 0m && savings is not null)
        {
            _db.SavingsLedgerEntries.Add(new SavingsLedgerEntry
            {
                TenantId = loan.TenantId,
                SavingsAccountId = savings.Id,
                ValueDateUtc = DateTime.SpecifyKind(
                    _clock.TodayInPortAuPrince().ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
                PostedAtUtc = _clock.UtcNow,
                EntryType = "Credit",
                Amount = compulsory,
                CurrencyCode = loan.CurrencyCode,
                Description = $"Épargne obligatoire {loan.LoanNo}",
                JournalEntryId = journal.Value!.Id,
                TillSessionId = till.Id,
                IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim()
            });
            var lien = new SavingsLien
            {
                TenantId = loan.TenantId,
                SavingsAccountId = savings.Id,
                CurrencyCode = loan.CurrencyCode,
                Amount = compulsory,
                Reason = $"Épargne obligatoire {loan.LoanNo}",
                CreatedAtUtc = _clock.UtcNow
            };
            _db.SavingsLiens.Add(lien);
            lienId = lien.Id;
        }

        if (cashAmount > 0m)
            till.ExpectedCash = MoneyAmount.Normalize(till.ExpectedCash - cashAmount);

        loan.Status = LoanStatus.Active;
        loan.CompulsorySavingsAmount = compulsory;
        loan.CashDisbursedAmount = cashAmount;
        loan.SavingsDisbursedAmount = compulsory;
        loan.SavingsAccountId = savings?.Id;
        loan.LienId = lienId;
        loan.PostedJournalId = journal.Value!.Id;
        loan.TillSessionId = till.Id;
        loan.DisbursedByUserId = _currentUser.UserId;
        loan.DisbursedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(
            "Loan.Disbursed",
            nameof(Loan),
            loan.Id,
            new { loan.LoanNo, cashAmount, compulsory, journal.Value.Id },
            loan.TenantId,
            _currentUser.UserId,
            cancellationToken: cancellationToken);
        return Result<LoanDto>.Ok(await MapAsync(loan, cancellationToken));
    }

    public async Task<Result<RepaymentResultDto>> RepayAsync(
        Guid id,
        RepayLoanRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var gate = RequireCollector();
        if (!gate.IsSuccess)
            return Result<RepaymentResultDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var amount = MoneyAmount.Normalize(request.Amount);
        if (amount <= 0m)
            return Result<RepaymentResultDto>.Fail("loan.repay.amount", "Le montant du remboursement doit être supérieur à zéro.");

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await _db.LoanRepayments.AsNoTracking()
                .FirstOrDefaultAsync(
                    r => r.TenantId == _currentUser.TenantId && r.IdempotencyKey == idempotencyKey.Trim(),
                    cancellationToken);
            if (existing is not null)
            {
                var replayLoan = await LoadLoanAsync(existing.LoanId, tracking: false, cancellationToken);
                if (replayLoan is not null)
                {
                    var mapped = await MapAsync(replayLoan, cancellationToken);
                    return Result<RepaymentResultDto>.Ok(new RepaymentResultDto(mapped, await BuildReceiptAsync(existing, mapped, cancellationToken)));
                }
            }
        }

        var loan = await LoadLoanAsync(id, tracking: true, cancellationToken);
        if (loan is null)
            return Result<RepaymentResultDto>.Fail("loan.not_found", "Dossier de crédit introuvable.");
        if (loan.Status != LoanStatus.Active)
            return Result<RepaymentResultDto>.Fail("loan.not_active", "Seul un prêt actif peut recevoir un remboursement.");
        if (!string.IsNullOrWhiteSpace(request.CurrencyCode)
            && !string.Equals(request.CurrencyCode.Trim(), loan.CurrencyCode, StringComparison.OrdinalIgnoreCase))
            return Result<RepaymentResultDto>.Fail("loan.currency", "La devise du paiement doit correspondre à celle du prêt.");

        var till = await FindOpenTillAsync(loan.CurrencyCode, cancellationToken);
        if (till is null)
            return Result<RepaymentResultDto>.Fail("till.not_open", "Impossible d’encaisser : aucune caisse ouverte pour cet utilisateur et cette agence.");

        AccrueOne(loan, _clock.TodayInPortAuPrince());
        var leftover = LoanRepaymentAllocator.Unallocated(loan.Installments, amount);
        if (leftover > 0m)
            return Result<RepaymentResultDto>.Fail("loan.overpayment", "Le montant dépasse le solde restant du prêt.");

        var allocations = LoanRepaymentAllocator.Allocate(loan.Installments, amount);
        var penalty = MoneyAmount.Normalize(allocations.Sum(a => a.Penalty));
        var interest = MoneyAmount.Normalize(allocations.Sum(a => a.Interest));
        var principal = MoneyAmount.Normalize(allocations.Sum(a => a.Principal));
        LoanRepaymentAllocator.Apply(loan.Installments, allocations);

        var lines = new List<CreateJournalLineRequest>
        {
            new() { GlAccountId = SeedGuids.Gl(LoanGl.CashHtg), Debit = amount, Credit = 0m, Description = "Caisse" }
        };
        if (penalty > 0m)
            lines.Add(new CreateJournalLineRequest { GlAccountId = SeedGuids.Gl(LoanGl.PenaltyIncomeHtg), Debit = 0m, Credit = penalty, Description = "Pénalité" });
        if (interest > 0m)
            lines.Add(new CreateJournalLineRequest { GlAccountId = SeedGuids.Gl(LoanGl.InterestIncomeHtg), Debit = 0m, Credit = interest, Description = "Intérêt" });
        if (principal > 0m)
            lines.Add(new CreateJournalLineRequest { GlAccountId = SeedGuids.Gl(LoanGl.ShortTermPortfolioHtg), Debit = 0m, Credit = principal, Description = "Capital" });

        var journal = await _journals.PostAsync(new CreateJournalRequest
        {
            Description = $"Remboursement {loan.LoanNo}",
            CurrencyCode = loan.CurrencyCode,
            BranchId = till.BranchId,
            Lines = lines
        }, idempotencyKey, cancellationToken);
        if (!journal.IsSuccess)
            return Result<RepaymentResultDto>.Fail(journal.ErrorCode!, journal.ErrorMessage!);

        till.ExpectedCash = MoneyAmount.Normalize(till.ExpectedCash + amount);
        if (loan.Installments.All(i => LoanRepaymentAllocator.RemainingTotal(i) == 0m))
            loan.Status = LoanStatus.PaidOff;
        loan.DaysPastDue = LoanDelinquency.DaysPastDue(loan.Installments, _clock.TodayInPortAuPrince());

        var repayment = new LoanRepayment
        {
            TenantId = loan.TenantId,
            LoanId = loan.Id,
            ReceiptNo = await NextReceiptNoAsync(cancellationToken),
            Amount = amount,
            PenaltyAllocated = penalty,
            InterestAllocated = interest,
            PrincipalAllocated = principal,
            CurrencyCode = loan.CurrencyCode,
            PostedJournalId = journal.Value!.Id,
            TillSessionId = till.Id,
            CreatedByUserId = _currentUser.UserId!.Value,
            CreatedAtUtc = _clock.UtcNow,
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim()
        };
        _db.LoanRepayments.Add(repayment);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(
            "Loan.Repaid",
            nameof(Loan),
            loan.Id,
            new { loan.LoanNo, amount, penalty, interest, principal, repayment.ReceiptNo },
            loan.TenantId,
            _currentUser.UserId,
            cancellationToken: cancellationToken);

        var dto = await MapAsync(loan, cancellationToken);
        return Result<RepaymentResultDto>.Ok(new RepaymentResultDto(dto, await BuildReceiptAsync(repayment, dto, cancellationToken)));
    }

    public async Task<Result<AccrualResultDto>> RunAccrualAsync(CancellationToken cancellationToken = default)
    {
        var gate = RequireApprover();
        if (!gate.IsSuccess)
            return Result<AccrualResultDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var asOf = _clock.TodayInPortAuPrince();
        var loans = await _db.Loans
            .Include(l => l.Product)
            .Include(l => l.Installments)
            .Where(l => l.TenantId == _currentUser.TenantId && l.Status == LoanStatus.Active)
            .ToListAsync(cancellationToken);
        foreach (var loan in loans)
            AccrueOne(loan, asOf);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(
            "Loan.AccrualRun",
            nameof(Loan),
            _currentUser.TenantId,
            new { count = loans.Count, asOf },
            _currentUser.TenantId!.Value,
            _currentUser.UserId,
            cancellationToken: cancellationToken);
        return Result<AccrualResultDto>.Ok(new AccrualResultDto(loans.Count, asOf));
    }

    public async Task<Result<CollectionSheetDto>> GetCollectionSheetAsync(
        string? period,
        CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<CollectionSheetDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var asOf = _clock.TodayInPortAuPrince();
        var week = string.Equals(period, "week", StringComparison.OrdinalIgnoreCase);
        var from = asOf;
        var to = week ? asOf.AddDays(6) : asOf;
        var loans = await _db.Loans.AsNoTracking()
            .Include(l => l.Product)
            .Include(l => l.Installments)
            .Where(l => l.TenantId == _currentUser.TenantId && l.Status == LoanStatus.Active)
            .ToListAsync(cancellationToken);

        var memberIds = loans.Select(l => l.MemberId).Distinct().ToList();
        var members = await _db.Members.AsNoTracking()
            .Where(m => memberIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        var rows = new List<CollectionSheetRowDto>();
        foreach (var loan in loans)
        {
            members.TryGetValue(loan.MemberId, out var member);
            foreach (var line in loan.Installments.OrderBy(i => i.LineNo))
            {
                var remaining = LoanRepaymentAllocator.RemainingTotal(line);
                if (remaining <= 0m)
                    continue;
                if (line.DueDate > to)
                    continue;
                var dpd = line.DueDate < asOf ? asOf.DayNumber - line.DueDate.DayNumber : 0;
                rows.Add(new CollectionSheetRowDto(
                    loan.Id,
                    loan.LoanNo,
                    member?.MemberNo ?? string.Empty,
                    member is null ? string.Empty : $"{member.FirstName} {member.LastName}".Trim(),
                    loan.Product?.DisplayName ?? string.Empty,
                    line.LineNo,
                    line.DueDate,
                    LoanRepaymentAllocator.RemainingPrincipal(line),
                    LoanRepaymentAllocator.RemainingInterest(line),
                    LoanRepaymentAllocator.RemainingPenalty(line),
                    remaining,
                    dpd,
                    line.DueDate < asOf));
            }
        }

        return Result<CollectionSheetDto>.Ok(new CollectionSheetDto(
            week ? "week" : "today",
            from,
            to,
            rows));
    }

    private async Task<Result<BuiltSchedule>> BuildScheduleAsync(PreviewLoanRequest request, CancellationToken cancellationToken)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<BuiltSchedule>.Fail(auth.ErrorCode!, auth.ErrorMessage!);
        var product = await LoadProductAsync(request.ProductId, cancellationToken);
        if (product is null)
            return Result<BuiltSchedule>.Fail("loan.product_not_found", "Produit de crédit introuvable.");
        if (!product.IsActive)
            return Result<BuiltSchedule>.Fail("loan.product_inactive", "Ce produit de crédit n’est pas actif.");
        if (product.InterestMethod != InterestMethod.FlatOnOriginalPrincipalForTerm)
            return Result<BuiltSchedule>.Fail("loan.interest_method", "Seuls les produits à intérêt forfaitaire sur le capital initial sont supportés.");

        var principal = MoneyAmount.Normalize(request.Principal);
        if (principal <= 0m)
            return Result<BuiltSchedule>.Fail("loan.principal", "Le capital doit être saisi et supérieur à zéro.");
        if (product.MinPrincipal is { } min && principal < min)
            return Result<BuiltSchedule>.Fail("loan.principal_min", "Le capital est inférieur au minimum du produit.");
        if (product.MaxPrincipal is { } max && principal > max)
            return Result<BuiltSchedule>.Fail("loan.principal_max", "Le capital dépasse le maximum du produit.");
        if (request.AgreedRatePercent < 0m)
            return Result<BuiltSchedule>.Fail("loan.rate", "Le taux convenu ne peut pas être négatif.");

        var start = request.StartDate ?? _clock.TodayInPortAuPrince();
        var lines = LoanScheduleFactory.BuildWeeklyFlat(principal, request.AgreedRatePercent, product.InstallmentCount, start);
        var schedule = new LoanScheduleDto(
            principal,
            request.AgreedRatePercent,
            product.TermDays,
            FlatTermInterest.TotalInterest(principal, request.AgreedRatePercent),
            FlatTermInterest.TotalDue(principal, request.AgreedRatePercent),
            product.InstallmentCount,
            lines.Select(l => new LoanInstallmentDto(l.LineNo, l.DueDate, l.PrincipalDue, l.InterestDue, l.TotalDue)).ToList());
        return Result<BuiltSchedule>.Ok(new BuiltSchedule(product, schedule));
    }

    private Result<bool> EnsureEligible(Member member)
    {
        if (member.Status != MemberStatus.Active)
            return Result<bool>.Fail("loan.member_inactive", "Seuls les membres actifs peuvent recevoir un crédit.");
        if (!MembershipRules.IsKycActive(member.KycStatus))
            return Result<bool>.Fail("loan.kyc_not_active", "Le KYC doit être actif (vérifié) pour un crédit.");
        if (!MembershipRules.AllowsMicro90(member.LegalStatus))
            return Result<bool>.Fail("member.usager_micro90", "Micro90 est interdit aux usagers. Conversion en sociétaire requise.");
        if (MembershipRules.ServicesBlocked(member, _clock.UtcNow))
            return Result<bool>.Fail("member.usager_expired", "Période d’usage échue : conversion en sociétaire requise avant tout service.");
        return Result<bool>.Ok(true);
    }

    private static bool RequiresSecondApproval(Loan loan)
    {
        var cap = loan.Product?.OfficerMaxApproval;
        return cap is not null && loan.Principal > cap.Value;
    }

    private Task<LoanProduct?> LoadProductAsync(Guid id, CancellationToken cancellationToken) =>
        _db.LoanProducts.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == _currentUser.TenantId, cancellationToken);

    private Task<Loan?> LoadLoanAsync(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        var query = tracking ? _db.Loans.AsQueryable() : _db.Loans.AsNoTracking();
        return query.Include(l => l.Product).Include(l => l.Installments)
            .FirstOrDefaultAsync(l => l.Id == id && l.TenantId == _currentUser.TenantId, cancellationToken);
    }

    private Task<TillSession?> FindOpenTillAsync(string currency, CancellationToken cancellationToken) =>
        _db.TillSessions.FirstOrDefaultAsync(
            t => t.TenantId == _currentUser.TenantId
                 && t.UserId == _currentUser.UserId
                 && t.BranchId == _currentUser.BranchId
                 && t.CurrencyCode == currency
                 && t.Status == TillSessionStatus.Open,
            cancellationToken);

    private async Task<string> NextLoanNoAsync(CancellationToken cancellationToken)
    {
        var sequence = await _db.NumberSequences
            .FirstOrDefaultAsync(s => s.TenantId == _currentUser.TenantId && s.Key == "LoanNo", cancellationToken);
        if (sequence is null)
        {
            sequence = new NumberSequence
            {
                Id = Guid.NewGuid(),
                TenantId = _currentUser.TenantId!.Value,
                Key = "LoanNo",
                LastValue = 0
            };
            _db.NumberSequences.Add(sequence);
        }

        sequence.LastValue += 1;
        await _db.SaveChangesAsync(cancellationToken);
        return $"LN-{sequence.LastValue:000000}";
    }

    private async Task<string> NextReceiptNoAsync(CancellationToken cancellationToken)
    {
        var sequence = await _db.NumberSequences
            .FirstOrDefaultAsync(s => s.TenantId == _currentUser.TenantId && s.Key == "RepaymentNo", cancellationToken);
        if (sequence is null)
        {
            sequence = new NumberSequence
            {
                Id = Guid.NewGuid(),
                TenantId = _currentUser.TenantId!.Value,
                Key = "RepaymentNo",
                LastValue = 0
            };
            _db.NumberSequences.Add(sequence);
        }

        sequence.LastValue += 1;
        await _db.SaveChangesAsync(cancellationToken);
        return $"RP-{sequence.LastValue:000000}";
    }

    private static void AccrueOne(Loan loan, DateOnly asOf)
    {
        var rate = loan.Product?.LatePenaltyPercentPerDay ?? 0m;
        foreach (var line in loan.Installments)
        {
            var add = LoanDelinquency.AccruePenalty(line, asOf, rate);
            if (add <= 0m)
                continue;
            line.PenaltyDue = MoneyAmount.Normalize(line.PenaltyDue + add);
            line.LastPenaltyAccruedOn = asOf;
        }

        loan.DaysPastDue = LoanDelinquency.DaysPastDue(loan.Installments, asOf);
        loan.LastAccruedOn = asOf;
    }

    private async Task<LoanReceiptDto> BuildReceiptAsync(LoanRepayment repayment, LoanDto loan, CancellationToken cancellationToken)
    {
        var branch = await _db.Branches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == _currentUser.BranchId, cancellationToken);
        var journal = await _journals.GetAsync(repayment.PostedJournalId, cancellationToken);
        return new LoanReceiptDto(
            Letterhead.Sigle,
            Letterhead.Line2,
            Letterhead.Line3,
            Letterhead.Line4,
            "repayment",
            "Remboursement",
            repayment.ReceiptNo,
            journal.IsSuccess ? journal.Value!.JournalNo : repayment.PostedJournalId.ToString(),
            loan.LoanNo,
            loan.MemberNo,
            loan.MemberName,
            loan.ProductDisplayName,
            repayment.Amount,
            repayment.PenaltyAllocated,
            repayment.InterestAllocated,
            repayment.PrincipalAllocated,
            repayment.CurrencyCode,
            MoneyAmount.Normalize(loan.Schedule.Installments.Sum(i => i.Remaining)),
            _currentUser.Username ?? string.Empty,
            branch?.Name ?? Letterhead.DefaultBranchName,
            repayment.CreatedAtUtc,
            _clock.ToPortAuPrince(repayment.CreatedAtUtc));
    }

    private async Task<IReadOnlyList<LoanDto>> MapManyAsync(IReadOnlyList<Loan> loans, CancellationToken cancellationToken)
    {
        var mapped = new List<LoanDto>(loans.Count);
        foreach (var loan in loans)
            mapped.Add(await MapAsync(loan, cancellationToken));
        return mapped;
    }

    private async Task<LoanDto> MapAsync(Loan loan, CancellationToken cancellationToken)
    {
        var member = await _db.Members.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == loan.MemberId, cancellationToken);
        var userIds = new[] { loan.Approver1Id, loan.Approver2Id, loan.DisbursedByUserId }
            .Where(x => x is not null).Select(x => x!.Value).Distinct().ToList();
        var users = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        var lines = loan.Installments.OrderBy(i => i.LineNo).Select(i =>
            new LoanInstallmentDto(
                i.LineNo,
                i.DueDate,
                i.PrincipalDue,
                i.InterestDue,
                i.TotalDue,
                i.PrincipalPaid,
                i.InterestPaid,
                i.PenaltyDue,
                i.PenaltyPaid,
                LoanRepaymentAllocator.RemainingTotal(i))).ToList();
        var schedule = new LoanScheduleDto(
            loan.Principal,
            loan.AgreedRatePercent,
            loan.RateAppliesToTermDays,
            loan.TotalInterest,
            loan.TotalDue,
            loan.InstallmentCount,
            lines);
        return new LoanDto(
            loan.Id,
            loan.LoanNo,
            loan.MemberId,
            member?.MemberNo ?? string.Empty,
            member is null ? string.Empty : $"{member.FirstName} {member.LastName}".Trim(),
            loan.ProductId,
            loan.Product?.DisplayName ?? string.Empty,
            loan.Principal,
            loan.AgreedRatePercent,
            loan.RateAppliesToTermDays,
            loan.TotalInterest,
            loan.TotalDue,
            loan.CurrencyCode,
            loan.Status.ToString(),
            loan.CycleNumber,
            loan.RenewedFromLoanId,
            loan.RenewedToLoanId,
            loan.OriginationDate,
            loan.CompulsorySavingsPercent,
            loan.CompulsorySavingsAmount,
            loan.CashDisbursedAmount,
            loan.SavingsDisbursedAmount,
            loan.Product?.OfficerMaxApproval,
            RequiresSecondApproval(loan),
            loan.SubmittedByUserId,
            loan.SubmittedAtUtc,
            loan.Approver1Id,
            loan.Approver1Id is { } a1 && users.TryGetValue(a1, out var n1) ? n1 : null,
            loan.Approved1AtUtc,
            loan.Approver2Id,
            loan.Approver2Id is { } a2 && users.TryGetValue(a2, out var n2) ? n2 : null,
            loan.Approved2AtUtc,
            loan.DisbursedByUserId,
            loan.DisbursedByUserId is { } d && users.TryGetValue(d, out var dn) ? dn : null,
            loan.DisbursedAtUtc,
            loan.PostedJournalId,
            loan.SavingsAccountId,
            loan.LienId,
            loan.RejectReason,
            LoanDelinquency.DaysPastDue(loan.Installments, _clock.TodayInPortAuPrince()),
            schedule);
    }

    private Result<bool> RequireUser()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.TenantId is null || _currentUser.UserId is null || _currentUser.BranchId is null)
            return Result<bool>.Fail("auth.unauthorized", "Session invalide.");
        return Result<bool>.Ok(true);
    }

    private Result<bool> RequireWriter()
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return auth;
        if (!_currentUser.Roles.Any(r => r is RoleNames.Admin or RoleNames.Gerant or RoleNames.OfficierCredit))
            return Result<bool>.Fail("auth.forbidden", "La création de dossier de crédit est réservée à l’officier, au gérant ou à l’administrateur.");
        return Result<bool>.Ok(true);
    }

    private Result<bool> RequireApprover()
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return auth;
        if (!_currentUser.Roles.Any(r => r is RoleNames.Admin or RoleNames.Gerant))
            return Result<bool>.Fail("auth.forbidden", "Seuls le gérant et l’administrateur approuvent un crédit.");
        return Result<bool>.Ok(true);
    }

    private Result<bool> RequireCashier()
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return auth;
        if (!_currentUser.Roles.Contains(RoleNames.Caissier))
            return Result<bool>.Fail("auth.forbidden", "Seul le caissier décaisse un crédit approuvé.");
        return Result<bool>.Ok(true);
    }

    private Result<bool> RequireCollector()
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return auth;
        if (!_currentUser.Roles.Any(r => r is RoleNames.Caissier or RoleNames.Gerant))
            return Result<bool>.Fail("auth.forbidden", "Seul le caissier encaisse un remboursement. Le gérant peut se substituer.");
        return Result<bool>.Ok(true);
    }

    private sealed record BuiltSchedule(LoanProduct Product, LoanScheduleDto Schedule);
}
