using CPCREDO.Domain.Common;

namespace CPCREDO.Domain.Accounting;

public class JournalLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JournalEntryId { get; set; }
    public int LineNo { get; set; }
    public Guid GlAccountId { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string Description { get; set; } = string.Empty;

    public JournalEntry? JournalEntry { get; set; }
    public GlAccount? GlAccount { get; set; }

    public void NormalizeAndValidate()
    {
        Debit = MoneyAmount.Normalize(Debit);
        Credit = MoneyAmount.Normalize(Credit);

        if (Debit < 0m || Credit < 0m)
            throw new DomainException("journal.line.negative", "Le débit et le crédit d’une ligne ne peuvent pas être négatifs.");

        if (Debit > 0m && Credit > 0m)
            throw new DomainException("journal.line.both_sides", "Une ligne d’écriture ne peut pas avoir à la fois un débit et un crédit.");

        if (Debit == 0m && Credit == 0m)
            throw new DomainException("journal.line.empty", "Une ligne d’écriture doit avoir un débit ou un crédit.");
    }
}
