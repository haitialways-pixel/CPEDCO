namespace CPCREDO.Domain.Common;

public static class CpcredoTimeZone
{
    public const string DisplayId = "America/Port-au-Prince";
    public const string WindowsDisplayId = "Haiti Standard Time";

    private static readonly Lazy<TimeZoneInfo> DisplayZone = new(Resolve);

    public static TimeZoneInfo Display => DisplayZone.Value;

    public static DateTime ToDisplay(DateTime utc)
    {
        var utcValue = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utcValue, Display);
    }

    public static DateOnly Today(DateTime utcNow) => DateOnly.FromDateTime(ToDisplay(utcNow));

    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { DisplayId, WindowsDisplayId })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            DisplayId,
            TimeSpan.FromHours(-5),
            "Haïti (UTC-05:00)",
            "Heure d’Haïti");
    }
}
