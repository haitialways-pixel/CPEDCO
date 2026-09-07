using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Domain.Tenancy;

namespace CPCREDO.Domain.Teller;

public class TillSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public Guid UserId { get; set; }
    public string CurrencyCode { get; set; } = Currencies.Htg;
    public TillSessionStatus Status { get; set; } = TillSessionStatus.Open;
    public decimal OpeningFloat { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal? CountedCash { get; set; }
    public decimal? OverShortAmount { get; set; }
    public Guid? OverShortJournalId { get; set; }
    public DateTime OpenedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }

    public Branch? Branch { get; set; }
    public User? User { get; set; }
    public ICollection<TillCountLine> CountLines { get; set; } = new List<TillCountLine>();
}
