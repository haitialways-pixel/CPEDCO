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
    public CashLocationType SourceType { get; set; } = CashLocationType.Vault;
    public Guid? SourceId { get; set; }
    public CashLocationType DestinationType { get; set; } = CashLocationType.Till;
    public Guid? DestinationId { get; set; }
    public Guid? TellerUserId { get; set; }
    public DateOnly? BusinessDate { get; set; }
    public CashMovementReason Reason { get; set; } = CashMovementReason.Refill;
    public Guid? SourceTillSessionId { get; set; }
    public Guid? DestinationTillSessionId { get; set; }
    public string? Note { get; set; }
    public string? BagId { get; set; }
    public decimal? ReceivedAmount { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public Guid? AcceptedByUserId { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public Guid? RejectedByUserId { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string? IdempotencyKey { get; set; }

    public TillSession? SourceTillSession { get; set; }
    public TillSession? DestinationTillSession { get; set; }
    public User? CreatedByUser { get; set; }
    public ICollection<InternalCashDenomination> Denominations { get; set; } = new List<InternalCashDenomination>();
}

public class InternalCashDenomination
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MovementId { get; set; }
    public decimal FaceValue { get; set; }
    public int Quantity { get; set; }

    public InternalCashMovement? Movement { get; set; }
}

public enum InternalCashDirection
{
    VaultToTill = 1,
    TillToVault = 2,
    TillToTill = 3,
    BankToTill = 4,
    TillToBank = 5,
    ExternalToVault = 6,
    ExternalToTill = 7
}

public enum InternalCashStatus
{
    Pending = 1,
    Accepted = 2,
    Rejected = 3
}

public enum CashLocationType
{
    Vault = 1,
    Till = 2,
    CorrespondentBank = 3,
    ExternalPayer = 4
}

public enum CashMovementReason
{
    OpeningFloat = 1,
    Refill = 2,
    Skim = 3,
    CloseReturn = 4,
    Handover = 5,
    EcartShortage = 6,
    EcartOverage = 7,
    ExternalLoan = 8,
    Grant = 9
}
