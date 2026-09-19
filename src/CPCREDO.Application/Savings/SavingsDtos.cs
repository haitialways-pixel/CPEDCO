namespace CPCREDO.Application.Savings;

public sealed record SavingsProductDto(
    Guid Id,
    string Code,
    string LegalName,
    string? CommercialName,
    string DisplayName,
    string Name,
    string CurrencyCode,
    string ProductKind,
    int? TermDays,
    decimal InterestRatePercent,
    string InterestMethod,
    decimal MinOpeningAmount,
    decimal MinimumBalance,
    bool AllowWithdrawBeforeTerm,
    Guid LiabilityGlAccountId,
    Guid CashGlAccountId,
    bool IsActive);

public sealed class SaveSavingsProductRequest
{
    public string Code { get; set; } = string.Empty;
    public string LegalName { get; set; } = string.Empty;
    public string? CommercialName { get; set; }
    public string CurrencyCode { get; set; } = "HTG";
    public string ProductKind { get; set; } = "AVue";
    public int? TermDays { get; set; }
    public int? TermMonths { get; set; }
    public decimal InterestRatePercent { get; set; }
    public string InterestMethod { get; set; } = "None";
    public decimal MinOpeningAmount { get; set; }
    public decimal MinimumBalance { get; set; }
    public bool AllowWithdrawBeforeTerm { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class OpenMemberAccountRequest
{
    public Guid MemberId { get; set; }
    public string Kind { get; set; } = "Epargne";
    public Guid? ProductId { get; set; }
}

public sealed record OpenedAccountDto(
    string Kind,
    Guid Id,
    string AccountNo,
    string Label,
    string CurrencyCode,
    decimal Balance,
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
    DateOnly? MaturesOn,
    bool AllowWithdrawBeforeTerm,
    string ProductKind,
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
