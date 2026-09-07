using CPCREDO.Domain.Common;

namespace CPCREDO.Domain.Accounting;

public sealed class PostedJournalImmutableException()
    : DomainException(
        "journal.immutable",
        "Les écritures comptables validées sont immuables. Effectuez une contre-passation (nouvelle écriture inverse).")
{
}
