using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;

namespace CPCREDO.Domain.Teller;

public class InternalCashMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public string MovementNo { get; set; } = string.Empty;
    public InternalCashDirection Direction { get; set; }
    public InternalCashStatus Status { get; set; } = InternalCashStatus.Pending;
    public string CurrencyCode { get; set; } = Currencies.Htg;
    public decimal Amount { get; set; }
    public Guid? SourceTillSessionId { get; set; }
    public Guid? DestinationTillSessionId { get; set; }
    public string? Note { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public Guid? AcceptedByUserId { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string? IdempotencyKey { get; set; }

    public TillSession? SourceTillSession { get; set; }
    public TillSession? DestinationTillSession { get; set; }
    public User? CreatedByUser { get; set; }
}

public enum InternalCashDirection
{
    VaultToTill = 1,
    TillToVault = 2,
    TillToTill = 3
}

public enum InternalCashStatus
{
    Pending = 1,
    Accepted = 2
}
