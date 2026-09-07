namespace CPCREDO.Domain.Common;

/// <summary>
/// All monetary values in CPCREDO are <see cref="decimal"/> stored as numeric(19,4).
/// Floating-point types are forbidden for money.
/// Intermediate calculations (including future loan interest) stay at 4 decimals.
/// Round to 2 decimals only in <see cref="MoneyDisplay"/>, never before a posted journal line.
/// </summary>
public static class MoneyAmount
{
    public const int Precision = 19;
    public const int Scale = 4;

    public static decimal Normalize(decimal value) =>
        decimal.Round(value, Scale, MidpointRounding.AwayFromZero);

    public static bool IsZero(decimal value) => Normalize(value) == 0m;
}
