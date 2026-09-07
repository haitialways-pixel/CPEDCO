using CPCREDO.Domain.Common;

namespace CPCREDO.Domain.Savings;

public class SavingsLedgerEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SavingsAccountId { get; set; }
    public DateTime ValueDateUtc { get; set; }
    public DateTime PostedAtUtc { get; set; }
    public string EntryType { get; set; } = "Credit";
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = Currencies.Htg;
    public string Description { get; set; } = string.Empty;
    public Guid? JournalEntryId { get; set; }
    public Guid? TillSessionId { get; set; }
    public string? IdempotencyKey { get; set; }

    public SavingsAccount? SavingsAccount { get; set; }

    public decimal SignedAmount => string.Equals(EntryType, "Debit", StringComparison.OrdinalIgnoreCase) ? -Amount : Amount;
}
