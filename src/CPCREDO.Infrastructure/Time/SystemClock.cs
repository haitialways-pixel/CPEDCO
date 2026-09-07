using CPCREDO.Application.Common;
using CPCREDO.Domain.Common;

namespace CPCREDO.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    public DateTime ToPortAuPrince(DateTime utc) => CpcredoTimeZone.ToDisplay(utc);

    public DateOnly TodayInPortAuPrince() => CpcredoTimeZone.Today(UtcNow);
}
