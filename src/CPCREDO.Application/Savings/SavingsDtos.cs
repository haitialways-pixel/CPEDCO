namespace CPCREDO.Application.Savings;

public sealed record SavingsProductDto(
    Guid Id,
    string Name,
    string CurrencyCode,
    decimal MinimumBalance,
    Guid LiabilityGlAccountId,
    Guid CashGlAccountId,
    bool IsActive);

public sealed record SavingsHoldDto(
    Guid Id,
    decimal Amount,
    string Reason,
    DateTime CreatedAtUtc);

public sealed record PlaceHoldRequest
{
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed record BlockAccountRequest
{
    public string Reason { get; set; } = string.Empty;
}

public sealed record SavingsAccountDto(
    Guid Id,
    Guid MemberId,
    Guid ProductId,
    string AccountNo,
    string ProductName,
    string CurrencyCode,
    decimal LedgerBalance,
    decimal AvailableBalance,
    bool IsActive,
    bool IsBlocked,
    string? BlockedReason,
    DateTime OpenedAtUtc,
    DateTime? LastPassbookPrintAtUtc,
    IReadOnlyList<SavingsHoldDto> Holds);

public sealed record LivretLineDto(
    DateTime ValueDateUtc,
    string Description,
    decimal Debit,
    decimal Credit,
    decimal RunningBalance,
    string CashierName);

public sealed record LivretDto(
    Guid AccountId,
    string AccountNo,
    string ProductName,
    string CurrencyCode,
    string MemberNo,
    string MemberName,
    DateOnly From,
    DateOnly To,
    decimal AvailableBalance,
    DateTime? LastOperationAtUtc,
    decimal? LastOperationAmount,
    string? LastOperationType,
    IReadOnlyList<LivretLineDto> Lines);

public sealed record StatementPdfDto(
    byte[] Content,
    string FileName);

public sealed record SavingsStatementEntryDto(
    DateTime ValueDateUtc,
    DateTime PostedAtUtc,
    string EntryType,
    decimal Amount,
    string Description,
    decimal RunningBalance);

public sealed record SavingsStatementDto(
    Guid AccountId,
    string AccountNo,
    string ProductName,
    string CurrencyCode,
    decimal LedgerBalance,
    decimal AvailableBalance,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<SavingsStatementEntryDto> Entries);
