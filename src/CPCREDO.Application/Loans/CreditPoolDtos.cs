using CPCREDO.Application.Common;
using CPCREDO.Domain.Common;

namespace CPCREDO.Application.Loans;

public sealed record CreditPoolDto(
    Guid Id,
    string CurrencyCode,
    decimal FundedTotal,
    decimal DisbursedTotal,
    decimal ReservedApprovals,
    decimal AvailableToLend,
    IReadOnlyList<CreditPoolMovementDto> Movements);

public sealed record CreditPoolMovementDto(
    Guid Id,
    string Kind,
    string SourceKind,
    decimal Amount,
    string CurrencyCode,
    string? Note,
    Guid? LoanId,
    DateTime CreatedAtUtc);

public sealed class FundCreditPoolRequest
{
    public string SourceKind { get; set; } = "Vault";
    public Guid? BankAccountId { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = Currencies.Htg;
    public string? Note { get; set; }
}

public interface ICreditPoolService
{
    Task<Result<CreditPoolDto>> GetAsync(string? currencyCode, CancellationToken cancellationToken = default);

    Task<Result<CreditPoolDto>> FundAsync(
        FundCreditPoolRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<Result<bool>> ConsumeDisbursementAsync(
        Guid loanId,
        decimal principal,
        string currencyCode,
        CancellationToken cancellationToken = default);
}
