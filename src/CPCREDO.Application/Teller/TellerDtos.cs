using CPCREDO.Domain.Common;

namespace CPCREDO.Application.Teller;

public sealed class OpenTillRequest
{
    public string CurrencyCode { get; set; } = Currencies.Htg;
    public decimal OpeningFloat { get; set; }
}

public sealed class TillCountLineRequest
{
    public decimal FaceValue { get; set; }
    public int Quantity { get; set; }
}

public sealed class CloseTillRequest
{
    public List<TillCountLineRequest> Denominations { get; set; } = [];
}

public sealed class CashPostRequest
{
    public Guid SavingsAccountId { get; set; }
    public decimal Amount { get; set; }
}

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
    DateTime PostedAtPortAuPrince);

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
    DateTime? ClosedAtUtc);

public sealed record CashPostResultDto(
    CashReceiptDto Receipt,
    Guid JournalId,
    int JournalLineCount,
    decimal LedgerBalance,
    decimal AvailableBalance);
