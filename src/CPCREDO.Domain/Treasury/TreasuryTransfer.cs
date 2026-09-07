using CPCREDO.Domain.Common;

namespace CPCREDO.Domain.Treasury;

public class TreasuryTransfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public string TransferNo { get; set; } = string.Empty;
    public TreasuryDirection Direction { get; set; }
    public Guid? BankAccountId { get; set; }
    public decimal Amount { get; set; }
    public decimal FeeAmount { get; set; }
    public string CurrencyCode { get; set; } = Currencies.Htg;
    public TreasuryTransferStatus Status { get; set; } = TreasuryTransferStatus.Draft;
    public Guid CreatedByUserId { get; set; }
    public Guid? Approver1Id { get; set; }
    public Guid? Approver2Id { get; set; }
    public Guid? ExecutedById { get; set; }
    public DateTime? Approved1AtUtc { get; set; }
    public DateTime? Approved2AtUtc { get; set; }
    public DateTime? ExecutedAtUtc { get; set; }
    public string? BankSlipRef { get; set; }
    public TreasurySlipType? SlipType { get; set; }
    public string? SlipFileName { get; set; }
    public string? SlipContentType { get; set; }
    public byte[]? SlipContent { get; set; }
    public DateTime? SlipUploadedAtUtc { get; set; }
    public string? Notes { get; set; }
    public string? CancelReason { get; set; }
    public Guid? PostedJournalId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public BankAccount? BankAccount { get; set; }
}
