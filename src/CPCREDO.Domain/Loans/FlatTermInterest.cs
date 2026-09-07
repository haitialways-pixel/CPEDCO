using CPCREDO.Domain.Common;

namespace CPCREDO.Domain.Loans;

/// <summary>
/// Flat interest on original principal for the whole contractual term.
/// <paramref name="agreedRatePercent"/> applies to that term (e.g. 90 days), not per year and not declining.
/// </summary>
public static class FlatTermInterest
{
    public static decimal TotalInterest(decimal principal, decimal agreedRatePercent) =>
        MoneyAmount.Normalize(principal * (agreedRatePercent / 100m));

    public static decimal TotalDue(decimal principal, decimal agreedRatePercent) =>
        MoneyAmount.Normalize(principal + TotalInterest(principal, agreedRatePercent));

    public static decimal AccruedInterest(
        decimal principal,
        decimal agreedRatePercent,
        int termDays,
        int daysElapsed,
        bool chargesFullFlatInterest)
    {
        var full = TotalInterest(principal, agreedRatePercent);
        if (chargesFullFlatInterest)
            return full;
        if (termDays <= 0)
            return 0m;
        var elapsed = Math.Clamp(daysElapsed, 0, termDays);
        return MoneyAmount.Normalize(principal * (agreedRatePercent / 100m) * ((decimal)elapsed / termDays));
    }
}
