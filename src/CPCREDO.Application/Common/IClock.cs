namespace CPCREDO.Application.Common;

public interface IClock
{
    DateTime UtcNow { get; }
    DateTime ToPortAuPrince(DateTime utc);
    DateOnly TodayInPortAuPrince();
}
