using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Domain.Tenancy;

namespace CPCREDO.Domain.Accounting;

/// <summary>
/// Posted journals are immutable. A reversal is a new opposite journal, never an update or delete.
/// </summary>
public class JournalEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public string JournalNo { get; set; } = string.Empty;
    public DateOnly ValueDate { get; set; }
    public DateTime PostedAtUtc { get; set; }
    public Guid PostedByUserId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = Currencies.Htg;
    public JournalStatus Status { get; set; } = JournalStatus.Posted;
    public bool IsReversal { get; set; }
    public Guid? ReversalOfJournalId { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Tenant? Tenant { get; set; }
    public Branch? Branch { get; set; }
    public User? PostedByUser { get; set; }
    public JournalEntry? ReversalOf { get; set; }
    public ICollection<JournalLine> Lines { get; set; } = new List<JournalLine>();

    public decimal TotalDebit => Lines.Sum(l => l.Debit);
    public decimal TotalCredit => Lines.Sum(l => l.Credit);

    public void EnsureBalanced()
    {
        foreach (var line in Lines)
            line.NormalizeAndValidate();

        var debit = MoneyAmount.Normalize(TotalDebit);
        var credit = MoneyAmount.Normalize(TotalCredit);

        if (debit != credit)
            throw new DomainException(
                "journal.unbalanced",
                $"Écriture non équilibrée : débit {debit.ToString("0.0000")} ≠ crédit {credit.ToString("0.0000")}.");

        if (debit == 0m)
            throw new DomainException("journal.empty", "Une écriture doit contenir au moins une ligne.");
    }

    public JournalEntry CreateReversal(
        Guid newId,
        string newJournalNo,
        Guid postedByUserId,
        DateTime postedAtUtc,
        DateOnly valueDate)
    {
        if (Status != JournalStatus.Posted)
            throw new DomainException("journal.not_posted", "Seule une écriture validée peut être contre-passée.");

        var reversal = new JournalEntry
        {
            Id = newId,
            TenantId = TenantId,
            BranchId = BranchId,
            JournalNo = newJournalNo,
            ValueDate = valueDate,
            PostedAtUtc = postedAtUtc,
            PostedByUserId = postedByUserId,
            Description = $"Contre-passation de {JournalNo}",
            CurrencyCode = CurrencyCode,
            Status = JournalStatus.Posted,
            IsReversal = true,
            ReversalOfJournalId = Id,
            CreatedAtUtc = postedAtUtc
        };

        foreach (var line in Lines.OrderBy(l => l.LineNo))
        {
            reversal.Lines.Add(new JournalLine
            {
                Id = Guid.NewGuid(),
                JournalEntryId = reversal.Id,
                LineNo = line.LineNo,
                GlAccountId = line.GlAccountId,
                Debit = line.Credit,
                Credit = line.Debit,
                Description = $"Contre-passation : {line.Description}"
            });
        }

        reversal.EnsureBalanced();
        return reversal;
    }
}
