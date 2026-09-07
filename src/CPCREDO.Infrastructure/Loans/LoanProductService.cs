using CPCREDO.Application.Common;
using CPCREDO.Application.Loans;
using CPCREDO.Domain.Identity;
using CPCREDO.Domain.Loans;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CPCREDO.Infrastructure.Loans;

public sealed class LoanProductService : ILoanProductService
{
    private readonly CpcredoDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditLogger _audit;

    public LoanProductService(CpcredoDbContext db, ICurrentUser currentUser, IClock clock, IAuditLogger audit)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
    }

    public async Task<Result<IReadOnlyList<LoanProductDto>>> ListAsync(bool activeOnly, CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<IReadOnlyList<LoanProductDto>>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var query = _db.LoanProducts.AsNoTracking().Where(p => p.TenantId == _currentUser.TenantId);
        if (activeOnly || !CanManageProducts())
            query = query.Where(p => p.IsActive);

        var list = await query.OrderBy(p => p.LegalName).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<LoanProductDto>>.Ok(list.Select(Map).ToList());
    }

    public async Task<Result<LoanProductDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<LoanProductDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);
        var product = await _db.LoanProducts.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == _currentUser.TenantId, cancellationToken);
        if (product is null)
            return Result<LoanProductDto>.Fail("loan.product_not_found", "Produit de crédit introuvable.");
        return Result<LoanProductDto>.Ok(Map(product));
    }

    public async Task<Result<LoanProductDto>> CreateAsync(SaveLoanProductRequest request, CancellationToken cancellationToken = default)
    {
        var gate = RequireManager();
        if (!gate.IsSuccess)
            return Result<LoanProductDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);
        var parsed = Parse(request, requireCode: true);
        if (!parsed.IsSuccess)
            return Result<LoanProductDto>.Fail(parsed.ErrorCode!, parsed.ErrorMessage!);

        var code = parsed.Value!.Code;
        var exists = await _db.LoanProducts.AnyAsync(
            p => p.TenantId == _currentUser.TenantId && p.Code == code, cancellationToken);
        if (exists)
            return Result<LoanProductDto>.Fail("loan.product_code_taken", "Ce code de produit existe déjà.");

        var product = parsed.Value;
        product.Id = Guid.NewGuid();
        product.TenantId = _currentUser.TenantId!.Value;
        product.CreatedAtUtc = _clock.UtcNow;
        _db.LoanProducts.Add(product);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Loan.ProductCreated", nameof(LoanProduct), product.Id, new { product.Code, product.LegalName }, product.TenantId, _currentUser.UserId, cancellationToken: cancellationToken);
        return Result<LoanProductDto>.Ok(Map(product));
    }

    public async Task<Result<LoanProductDto>> UpdateAsync(Guid id, SaveLoanProductRequest request, CancellationToken cancellationToken = default)
    {
        var gate = RequireManager();
        if (!gate.IsSuccess)
            return Result<LoanProductDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);
        var product = await _db.LoanProducts.FirstOrDefaultAsync(
            p => p.Id == id && p.TenantId == _currentUser.TenantId, cancellationToken);
        if (product is null)
            return Result<LoanProductDto>.Fail("loan.product_not_found", "Produit de crédit introuvable.");

        var parsed = Parse(request, requireCode: false);
        if (!parsed.IsSuccess)
            return Result<LoanProductDto>.Fail(parsed.ErrorCode!, parsed.ErrorMessage!);
        var next = parsed.Value!;
        product.LegalName = next.LegalName;
        product.CommercialName = next.CommercialName;
        product.SmsName = next.SmsName;
        product.TermDays = next.TermDays;
        product.InstallmentCount = next.InstallmentCount;
        product.RepaymentFrequency = next.RepaymentFrequency;
        product.DefaultRatePercent = next.DefaultRatePercent;
        product.MaxRenewals = next.MaxRenewals;
        product.CompulsorySavingsPercent = next.CompulsorySavingsPercent;
        product.MinPrincipal = next.MinPrincipal;
        product.MaxPrincipal = next.MaxPrincipal;
        product.OfficerMaxApproval = next.OfficerMaxApproval;
        product.EarlyPayoffChargesFullFlatInterest = next.EarlyPayoffChargesFullFlatInterest;
        product.LatePenaltyPercentPerDay = next.LatePenaltyPercentPerDay;
        product.IsActive = next.IsActive;
        product.UpdatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Loan.ProductUpdated", nameof(LoanProduct), product.Id, new { product.Code }, product.TenantId, _currentUser.UserId, cancellationToken: cancellationToken);
        return Result<LoanProductDto>.Ok(Map(product));
    }

    public async Task<Result<LoanProductDto>> DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var gate = RequireManager();
        if (!gate.IsSuccess)
            return Result<LoanProductDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);
        var product = await _db.LoanProducts.FirstOrDefaultAsync(
            p => p.Id == id && p.TenantId == _currentUser.TenantId, cancellationToken);
        if (product is null)
            return Result<LoanProductDto>.Fail("loan.product_not_found", "Produit de crédit introuvable.");
        product.IsActive = false;
        product.UpdatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Loan.ProductDeactivated", nameof(LoanProduct), product.Id, new { product.Code }, product.TenantId, _currentUser.UserId, cancellationToken: cancellationToken);
        return Result<LoanProductDto>.Ok(Map(product));
    }

    private Result<LoanProduct> Parse(SaveLoanProductRequest request, bool requireCode)
    {
        var legal = request.LegalName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(legal))
            return Result<LoanProduct>.Fail("loan.product_name", "Le nom légal du produit est obligatoire.");
        var code = request.Code?.Trim() ?? "";
        if (requireCode && string.IsNullOrWhiteSpace(code))
            return Result<LoanProduct>.Fail("loan.product_code", "Le code du produit est obligatoire.");
        var sms = string.IsNullOrWhiteSpace(request.SmsName) ? null : request.SmsName.Trim();
        if (sms is { Length: > 20 })
            return Result<LoanProduct>.Fail("loan.product_sms", "Le nom SMS ne peut pas dépasser 20 caractères.");
        if (request.TermDays <= 0)
            return Result<LoanProduct>.Fail("loan.product_term", "La durée en jours doit être supérieure à zéro.");
        if (request.InstallmentCount <= 0)
            return Result<LoanProduct>.Fail("loan.product_installments", "Le nombre d’échéances doit être supérieur à zéro.");
        if (request.DefaultRatePercent < 0m)
            return Result<LoanProduct>.Fail("loan.product_rate", "Le taux ne peut pas être négatif.");
        if (request.CompulsorySavingsPercent < 0m)
            return Result<LoanProduct>.Fail("loan.product_savings", "L’épargne obligatoire ne peut pas être négative.");
        if (!Enum.TryParse<RepaymentFrequency>(request.RepaymentFrequency, true, out var frequency))
            frequency = RepaymentFrequency.Weekly;

        return Result<LoanProduct>.Ok(new LoanProduct
        {
            Code = code,
            LegalName = legal,
            CommercialName = string.IsNullOrWhiteSpace(request.CommercialName) ? null : request.CommercialName.Trim(),
            SmsName = sms,
            TermDays = request.TermDays,
            InstallmentCount = request.InstallmentCount,
            RepaymentFrequency = frequency,
            InterestMethod = InterestMethod.FlatOnOriginalPrincipalForTerm,
            DefaultRatePercent = request.DefaultRatePercent,
            MaxRenewals = request.MaxRenewals,
            CompulsorySavingsPercent = request.CompulsorySavingsPercent,
            MinPrincipal = request.MinPrincipal,
            MaxPrincipal = request.MaxPrincipal,
            OfficerMaxApproval = request.OfficerMaxApproval,
            EarlyPayoffChargesFullFlatInterest = request.EarlyPayoffChargesFullFlatInterest,
            LatePenaltyPercentPerDay = request.LatePenaltyPercentPerDay < 0m ? 0m : request.LatePenaltyPercentPerDay,
            IsActive = request.IsActive
        });
    }

    private static LoanProductDto Map(LoanProduct p) =>
        new(
            p.Id,
            p.Code,
            p.LegalName,
            p.CommercialName,
            p.DisplayName,
            p.SmsName,
            p.TermDays,
            p.InstallmentCount,
            p.RepaymentFrequency.ToString(),
            p.InterestMethod.ToString(),
            p.DefaultRatePercent,
            p.MaxRenewals,
            p.CompulsorySavingsPercent,
            p.MinPrincipal,
            p.MaxPrincipal,
            p.OfficerMaxApproval,
            p.EarlyPayoffChargesFullFlatInterest,
            p.LatePenaltyPercentPerDay,
            p.IsActive);

    private Result<bool> RequireUser()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.TenantId is null || _currentUser.UserId is null)
            return Result<bool>.Fail("auth.unauthorized", "Session invalide.");
        return Result<bool>.Ok(true);
    }

    private Result<bool> RequireManager()
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return auth;
        if (!CanManageProducts())
            return Result<bool>.Fail("auth.forbidden", "Seuls l’administrateur et le gérant gèrent les produits de crédit.");
        return Result<bool>.Ok(true);
    }

    private bool CanManageProducts() =>
        _currentUser.Roles.Contains(RoleNames.Admin) || _currentUser.Roles.Contains(RoleNames.Gerant);
}
