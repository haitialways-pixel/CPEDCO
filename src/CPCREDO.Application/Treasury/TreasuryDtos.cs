namespace CPCREDO.Application.Treasury;

public sealed record BankAccountDto(
    Guid Id,
    string BankName,
    string? CustomBankName,
    string DisplayBankName,
    string? Label,
    string AccountNumber,
    string AccountNumberMasked,
    string PickerLabel,
    string CurrencyCode,
    string GlCode,
    decimal Balance,
    bool IsActive,
    string? Notes);

public sealed record SaveBankAccountRequest
{
    public string BankName { get; set; } = string.Empty;
    public string? CustomBankName { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = "HTG";
    public string? Label { get; set; }
    public string? GlCode { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed record TreasuryTransferDto(
    Guid Id,
    string TransferNo,
    string Direction,
    Guid? BankAccountId,
    string BankName,
    string Bank,
    string BankAccountLabel,
    string AccountNumberMasked,
    decimal Amount,
    decimal FeeAmount,
    string CurrencyCode,
    string Status,
    Guid CreatedByUserId,
    string CreatedByName,
    string? CreatedByRole,
    Guid InitiatedById,
    string InitiatedByName,
    string? InitiatedByRole,
    Guid? Approver1Id,
    string? Approver1Name,
    string? Approver1Role,
    DateTime? Approved1AtUtc,
    Guid? Approver2Id,
    string? Approver2Name,
    string? Approver2Role,
    DateTime? Approved2AtUtc,
    Guid? ExecutedById,
    string? ExecutedByName,
    string? ExecutedByRole,
    string? BankSlipRef,
    string? SlipType,
    string? SlipFileName,
    string? SlipContentType,
    bool HasSlip,
    string? Notes,
    string? CancelReason,
    Guid? PostedJournalId,
    DateTime CreatedAtUtc,
    DateTime? ExecutedAtUtc);

public sealed record CreateTreasuryTransferRequest
{
    public string Direction { get; set; } = "VaultToBank";
    public Guid? BankAccountId { get; set; }
    public decimal Amount { get; set; }
    public decimal FeeAmount { get; set; }
    public string CurrencyCode { get; set; } = "HTG";
    public string? Notes { get; set; }
}

public sealed record AttachTreasurySlipRequest
{
    public string SlipRef { get; set; } = string.Empty;
    public string? SlipType { get; set; }
    public byte[]? SlipContent { get; set; }
    public string? SlipFileName { get; set; }
    public string? SlipContentType { get; set; }
}

public sealed record ExecuteTreasuryTransferRequest
{
    public string? Password { get; set; }
    public string? Notes { get; set; }
}

public sealed record TreasurySlipFileDto(byte[] Content, string ContentType, string FileName);

public sealed record CancelTreasuryTransferRequest
{
    public string Reason { get; set; } = string.Empty;
}

public sealed record VaultBalanceDto(
    string GlCode,
    string Name,
    string CurrencyCode,
    decimal Balance);
