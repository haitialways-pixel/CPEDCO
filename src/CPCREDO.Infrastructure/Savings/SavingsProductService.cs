using CPCREDO.Application.Common;
using CPCREDO.Application.Savings;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Domain.Savings;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CPCREDO.Infrastructure.Savings;

public sealed class SavingsProductService : ISavingsProductService
{
    private readonly CpcredoDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditLogger _audit;

    public SavingsProductService(CpcredoDbContext db, ICurrentUser currentUser, IClock clock, IAuditLogger audit)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
    }

    public async Task<Result<IReadOnlyList<SavingsProductDto>>> ListAsync(bool activeOnly, CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<IReadOnlyList<SavingsProductDto>>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var query = _db.SavingsProducts.AsNoTracking().Where(p => p.TenantId == _currentUser.TenantId);
        if (activeOnly || !CanManage())
            query = query.Where(p => p.IsActive);

        var list = await query.OrderBy(p => p.LegalName).ThenBy(p => p.Name).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<SavingsProductDto>>.Ok(list.Select(Map).ToList());
    }

    public async Task<Result<SavingsProductDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<SavingsProductDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);
        var product = await _db.SavingsProducts.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == _currentUser.TenantId, cancellationToken);
        if (product is null)
            return Result<SavingsProductDto>.Fail("savings.product.not_found", "Produit d’épargne introuvable.");
        return Result<SavingsProductDto>.Ok(Map(product));
    }

    public async Task<Result<SavingsProductDto>> CreateAsync(SaveSavingsProductRequest request, CancellationToken cancellationToken = default)
    {
        var gate = RequireManager();
        if (!gate.IsSuccess)
            return Result<SavingsProductDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);
        var parsed = await ParseAsync(request, requireCode: true, cancellationToken);
        if (!parsed.IsSuccess)
            return Result<SavingsProductDto>.Fail(parsed.ErrorCode!, parsed.ErrorMessage!);

        var code = parsed.Value!.Code;
        var exists = await _db.SavingsProducts.AnyAsync(
            p => p.TenantId == _currentUser.TenantId && p.Code == code, cancellationToken);
        if (exists)
            return Result<SavingsProductDto>.Fail("savings.product_code_taken", "Ce code de produit existe déjà.");

        var product = parsed.Value;
        product.Id = Guid.NewGuid();
        product.TenantId = _currentUser.TenantId!.Value;
        product.CreatedAtUtc = _clock.UtcNow;
        _db.SavingsProducts.Add(product);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Savings.ProductCreated", nameof(SavingsProduct), product.Id, new { product.Code, product.LegalName }, product.TenantId, _currentUser.UserId, cancellationToken: cancellationToken);
        return Result<SavingsProductDto>.Ok(Map(product));
    }

    public async Task<Result<SavingsProductDto>> UpdateAsync(Guid id, SaveSavingsProductRequest request, CancellationToken cancellationToken = default)
    {
        var gate = RequireManager();
        if (!gate.IsSuccess)
            return Result<SavingsProductDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);
        var product = await _db.SavingsProducts.FirstOrDefaultAsync(
            p => p.Id == id && p.TenantId == _currentUser.TenantId, cancellationToken);
        if (product is null)
            return Result<SavingsProductDto>.Fail("savings.product.not_found", "Produit d’épargne introuvable.");

        var parsed = await ParseAsync(request, requireCode: false, cancellationToken);
        if (!parsed.IsSuccess)
            return Result<SavingsProductDto>.Fail(parsed.ErrorCode!, parsed.ErrorMessage!);
        var next = parsed.Value!;
        product.LegalName = next.LegalName;
        product.CommercialName = next.CommercialName;
        product.Name = next.Name;
        product.CurrencyCode = next.CurrencyCode;
        product.ProductKind = next.ProductKind;
        product.TermDays = next.TermDays;
        product.InterestRatePercent = next.InterestRatePercent;
        product.InterestMethod = next.InterestMethod;
        product.MinOpeningAmount = next.MinOpeningAmount;
        product.MinimumBalance = next.MinimumBalance;
        product.AllowWithdrawBeforeTerm = next.AllowWithdrawBeforeTerm;
        product.LiabilityGlAccountId = next.LiabilityGlAccountId;
        product.CashGlAccountId = next.CashGlAccountId;
        product.IsActive = next.IsActive;
        product.UpdatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Savings.ProductUpdated", nameof(SavingsProduct), product.Id, new { product.Code }, product.TenantId, _currentUser.UserId, cancellationToken: cancellationToken);
        return Result<SavingsProductDto>.Ok(Map(product));
    }

    public async Task<Result<SavingsProductDto>> DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var gate = RequireManager();
        if (!gate.IsSuccess)
            return Result<SavingsProductDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);
        var product = await _db.SavingsProducts.FirstOrDefaultAsync(
            p => p.Id == id && p.TenantId == _currentUser.TenantId, cancellationToken);
        if (product is null)
            return Result<SavingsProductDto>.Fail("savings.product.not_found", "Produit d’épargne introuvable.");

        var hasAccounts = await _db.SavingsAccounts.AnyAsync(a => a.ProductId == id, cancellationToken);
        product.IsActive = false;
        product.UpdatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(
            "Savings.ProductDeactivated",
            nameof(SavingsProduct),
            product.Id,
            new { product.Code, hasAccounts },
            product.TenantId,
            _currentUser.UserId,
            cancellationToken: cancellationToken);
        return Result<SavingsProductDto>.Ok(Map(product));
    }

    private async Task<Result<SavingsProduct>> ParseAsync(SaveSavingsProductRequest request, bool requireCode, CancellationToken cancellationToken)
    {
        var legal = request.LegalName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(legal))
            return Result<SavingsProduct>.Fail("savings.product_name", "Le nom légal du produit est obligatoire.");
        var code = request.Code?.Trim().ToUpperInvariant() ?? "";
        if (requireCode && string.IsNullOrWhiteSpace(code))
            return Result<SavingsProduct>.Fail("savings.product_code", "Le code du produit est obligatoire.");
        if (!Enum.TryParse<SavingsProductKind>(request.ProductKind, true, out var kind))
            return Result<SavingsProduct>.Fail("savings.product_kind", "Type de produit invalide (AVue, Terme ou Autre).");
        var currency = (request.CurrencyCode ?? Currencies.Htg).Trim().ToUpperInvariant();
        if (currency is not (Currencies.Htg or Currencies.Usd))
            return Result<SavingsProduct>.Fail("savings.product_currency", "Devise invalide (HTG ou USD).");
        if (!Enum.TryParse<SavingsInterestMethod>(request.InterestMethod, true, out var interest))
            interest = SavingsInterestMethod.None;
        var termDays = request.TermDays;
        if (termDays is null && request.TermMonths is > 0)
            termDays = request.TermMonths.Value * 30;
        if (kind == SavingsProductKind.AVue)
            termDays = null;
        if (kind == SavingsProductKind.Terme && termDays is null or <= 0)
            return Result<SavingsProduct>.Fail("savings.product_term", "L’épargne à terme exige une durée (jours ou mois).");
        if (request.InterestRatePercent < 0m)
            return Result<SavingsProduct>.Fail("savings.product_rate", "Le taux ne peut pas être négatif.");
        if (request.InterestRatePercent == 0m)
            interest = SavingsInterestMethod.None;
        if (request.MinOpeningAmount < 0m || request.MinimumBalance < 0m)
            return Result<SavingsProduct>.Fail("savings.product_amount", "Les montants minimums ne peuvent pas être négatifs.");

        var gl = await ResolveGlAsync(currency, kind, cancellationToken);
        if (!gl.IsSuccess)
            return Result<SavingsProduct>.Fail(gl.ErrorCode!, gl.ErrorMessage!);

        var commercial = string.IsNullOrWhiteSpace(request.CommercialName) ? null : request.CommercialName.Trim();
        var display = commercial ?? legal;
        return Result<SavingsProduct>.Ok(new SavingsProduct
        {
            Code = code,
            LegalName = legal,
            CommercialName = commercial,
            Name = display,
            CurrencyCode = currency,
            ProductKind = kind,
            TermDays = termDays,
            InterestRatePercent = request.InterestRatePercent,
            InterestMethod = interest,
            MinOpeningAmount = MoneyAmount.Normalize(request.MinOpeningAmount),
            MinimumBalance = MoneyAmount.Normalize(request.MinimumBalance),
            AllowWithdrawBeforeTerm = kind == SavingsProductKind.Terme && request.AllowWithdrawBeforeTerm,
            LiabilityGlAccountId = gl.Value!.Liability,
            CashGlAccountId = gl.Value.Cash,
            IsActive = request.IsActive
        });
    }

    private async Task<Result<(Guid Cash, Guid Liability)>> ResolveGlAsync(
        string currency,
        SavingsProductKind kind,
        CancellationToken cancellationToken)
    {
        var cashCode = currency == Currencies.Usd ? "1020" : "1010";
        var liabCode = kind == SavingsProductKind.Terme
            ? (currency == Currencies.Usd ? "2120" : "2110")
            : (currency == Currencies.Usd ? "2020" : "2010");
        var cash = await _db.GlAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.TenantId == _currentUser.TenantId && a.Code == cashCode, cancellationToken);
        var liab = await _db.GlAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.TenantId == _currentUser.TenantId && a.Code == liabCode, cancellationToken);
        if (cash is null || liab is null)
            return Result<(Guid, Guid)>.Fail("savings.product_gl", "Comptes de caisse ou d’épargne introuvables pour ce produit.");
        return Result<(Guid, Guid)>.Ok((cash.Id, liab.Id));
    }

    internal static SavingsProductDto Map(SavingsProduct p) =>
        new(
            p.Id,
            p.Code,
            p.LegalName,
            p.CommercialName,
            p.DisplayName,
            p.DisplayName,
            p.CurrencyCode,
            p.ProductKind.ToString(),
            p.TermDays,
            p.InterestRatePercent,
            p.InterestMethod.ToString(),
            p.MinOpeningAmount,
            p.MinimumBalance,
            p.AllowWithdrawBeforeTerm,
            p.LiabilityGlAccountId,
            p.CashGlAccountId,
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
        if (!CanManage())
            return Result<bool>.Fail("auth.forbidden", "Seuls l’administrateur et le gérant gèrent les produits d’épargne.");
        return Result<bool>.Ok(true);
    }

    private bool CanManage() =>
        _currentUser.Roles.Contains(RoleNames.Admin) || _currentUser.Roles.Contains(RoleNames.Gerant);
}
