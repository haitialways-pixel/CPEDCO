using CPCREDO.Domain.Common;
using CPCREDO.Domain.Members;

namespace CPCREDO.Domain.Savings;

public class SavingsAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid MemberId { get; set; }
    public Guid BranchId { get; set; }
    public Guid ProductId { get; set; }
    public string AccountNo { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = Currencies.Htg;
    public decimal MinimumBalance { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsBlocked { get; set; }
    public string? BlockedReason { get; set; }
    public DateTime? BlockedAtUtc { get; set; }
    public Guid? BlockedByUserId { get; set; }
    public DateTime OpenedAtUtc { get; set; }

    public Member? Member { get; set; }
    public SavingsProduct? Product { get; set; }
    public ICollection<SavingsLien> Liens { get; set; } = new List<SavingsLien>();
    public ICollection<SavingsLedgerEntry> LedgerEntries { get; set; } = new List<SavingsLedgerEntry>();

    public decimal LedgerBalance => LedgerEntries.Sum(x => x.SignedAmount);
    public decimal AvailableBalance => LedgerBalance - Liens.Sum(x => x.Amount);
}
