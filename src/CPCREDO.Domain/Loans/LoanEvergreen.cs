using CPCREDO.Domain.Common;

namespace CPCREDO.Domain.Loans;

public static class LoanEvergreen
{
    public const decimal ThresholdPercent = 80m;
    public const int MinCycle = 3;

    public static bool Matches(int cycleNumber, decimal principal, decimal? previousOriginalPrincipal)
    {
        if (cycleNumber < MinCycle || previousOriginalPrincipal is null || previousOriginalPrincipal.Value <= 0m)
            return false;
        var floor = MoneyAmount.Normalize(previousOriginalPrincipal.Value * ThresholdPercent / 100m);
        return principal >= floor;
    }

    public static int? RemainingRenewals(int cycleNumber, int? maxRenewals) =>
        maxRenewals is null ? null : Math.Max(0, maxRenewals.Value - cycleNumber);

    public static bool IsCt90(LoanProduct? product) =>
        product is not null
        && (string.Equals(product.SmsName, "CT90", StringComparison.OrdinalIgnoreCase)
            || string.Equals(product.Code, "CT90", StringComparison.OrdinalIgnoreCase)
            || string.Equals(product.Code, "ST90", StringComparison.OrdinalIgnoreCase));
}
