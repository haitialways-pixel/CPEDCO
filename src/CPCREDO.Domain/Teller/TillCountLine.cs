using CPCREDO.Domain.Common;

namespace CPCREDO.Domain.Teller;

public class TillCountLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TillSessionId { get; set; }
    public decimal FaceValue { get; set; }
    public int Quantity { get; set; }

    public TillSession? TillSession { get; set; }

    public decimal Subtotal => MoneyAmount.Normalize(FaceValue * Quantity);
}
