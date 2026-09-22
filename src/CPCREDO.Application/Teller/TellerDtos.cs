using CPCREDO.Domain.Common;

namespace CPCREDO.Application.Teller;

public sealed class OpenTillRequest
{
    public string CurrencyCode { get; set; } = Currencies.Htg;
    public decimal? OpeningFloat { get; set; }
}

public sealed class CreateInternalCashRequest
{
    public string Direction { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? SourceType { get; set; }
    public string? DestinationType { get; set; }
    public decimal? Amount { get; set; }
    public string CurrencyCode { get; set; } = Currencies.Htg;
    public Guid? SourceTillSessionId { get; set; }
    public Guid? DestinationTillSessionId { get; set; }
    public Guid? DestinationTellerUserId { get; set; }
    public Guid? BankAccountId { get; set; }
    public string? BagId { get; set; }
    public string? Note { get; set; }
    public List<TillCountLineRequest> Denominations { get; set; } = [];
}

public sealed class AcceptMovementRequest
{
    public Guid? MovementId { get; set; }
    public decimal? CountedAmount { get; set; }
    public List<TillCountLineRequest> Denominations { get; set; } = [];
}

public sealed class RejectMovementRequest
{
    public string Reason { get; set; } = string.Empty;
}

public sealed record OpenTillPeerDto(
    Guid Id,
    Guid UserId,
    string CashierName,
    string CurrencyCode,
    decimal ExpectedCash);

public sealed record CashSourceDto(
    string Kind,
    Guid? TillSessionId,
    string Label,
    decimal Available,
    string CurrencyCode);

public sealed class FundDrawerRequest
{
    public string SourceKind { get; set; } = "Vault";
    public Guid? SourceTillSessionId { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = Currencies.Htg;
    public string? Note { get; set; }
    public Guid? LoanId { get; set; }
}

public sealed record InternalCashMovementDto(
    Guid Id,
    string MovementNo,
    string Direction,
    string Status,
    string CurrencyCode,
    decimal Amount,
    Guid? SourceTillSessionId,
    string? SourceCashierName,
    Guid? DestinationTillSessionId,
    string? DestinationCashierName,
    string? Note,
    DateTime CreatedAtUtc,
    DateTime? AcceptedAtUtc,
    Guid? JournalEntryId,
    bool CanAccept,
    string Reason = "",
    string SourceType = "",
    string DestinationType = "",
    Guid? TellerUserId = null,
    DateOnly? BusinessDate = null,
    string? BagId = null,
    decimal? ReceivedAmount = null,
    bool CanReject = false);

public sealed class TillCountLineRequest
{
    public decimal FaceValue { get; set; }
    public int Quantity { get; set; }
}

public sealed class CloseTillRequest
{
    public decimal? CountedBalance { get; set; }

    public string? Notes { get; set; }

    public List<TillCountLineRequest> Denominations { get; set; } = [];

    public string? CloseReturnDestination { get; set; }

    public string? BagId { get; set; }
}

public sealed class CashPostRequest
{
    public Guid SavingsAccountId { get; set; }
    public decimal Amount { get; set; }
    public string? GerantOverrideNote { get; set; }
}

public sealed class MixedCollectLineRequest
{
    public string Kind { get; set; } = "";
    public Guid? SavingsAccountId { get; set; }
    public decimal Amount { get; set; }
}

public sealed class MixedCollectRequest
{
    public Guid MemberId { get; set; }
    public decimal CashReceived { get; set; }
    public string CurrencyCode { get; set; } = Currencies.Htg;
    public List<MixedCollectLineRequest> Lines { get; set; } = [];
}

public sealed record ReceiptAllocationDto(
    string Kind,
    string AccountNo,
    string Label,
    decimal Amount);

public sealed record ReceiptLetterheadDto(
    string Sigle,
    string Line2,
    string Line3,
    string Line4);

public sealed record CashReceiptDto(
    ReceiptLetterheadDto Letterhead,
    string Type,
    string Title,
    string ReceiptNo,
    string JournalNo,
    string MemberNo,
    string MemberName,
    string AccountNo,
    string ProductName,
    decimal Amount,
    string CurrencyCode,
    decimal LedgerBalance,
    decimal AvailableBalance,
    string CashierName,
    string BranchName,
    DateTime PostedAtUtc,
    DateTime PostedAtPortAuPrince,
    IReadOnlyList<ReceiptAllocationDto>? Allocations = null);

public sealed record TillSessionDto(
    Guid Id,
    Guid BranchId,
    Guid UserId,
    string CurrencyCode,
    string Status,
    decimal OpeningFloat,
    decimal ExpectedCash,
    decimal? CountedCash,
    decimal? OverShortAmount,
    Guid? OverShortJournalId,
    DateTime OpenedAtUtc,
    DateTime? ClosedAtUtc,
    string? Notes);

public sealed record CashPostResultDto(
    CashReceiptDto Receipt,
    Guid JournalId,
    int JournalLineCount,
    decimal LedgerBalance,
    decimal AvailableBalance);
