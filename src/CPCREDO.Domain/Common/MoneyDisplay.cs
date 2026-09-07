using System.Globalization;
using System.Text;

namespace CPCREDO.Domain.Common;

/// <summary>
/// Staff-facing money text: 2 decimals, thin-space thousands, fr-HT style.
/// Example: 1000.25 HTG → "1 000,25 G". Storage remains numeric(19,4).
/// </summary>
public static class MoneyDisplay
{
    public const int Scale = 2;
    public const char ThinSpace = '\u202F';

    public static decimal RoundForDisplay(decimal value) =>
        decimal.Round(value, Scale, MidpointRounding.AwayFromZero);

    public static string Symbol(string currencyCode) =>
        string.Equals(currencyCode, Currencies.Usd, StringComparison.OrdinalIgnoreCase) ? "$US" : "G";

    public static string Format(decimal amount, string currencyCode) =>
        $"{FormatNumber(amount)} {Symbol(currencyCode)}";

    public static string FormatNumber(decimal amount)
    {
        var rounded = RoundForDisplay(amount);
        var negative = rounded < 0m;
        var absolute = Math.Abs(rounded);
        var whole = decimal.Truncate(absolute);
        var fraction = (int)decimal.Round((absolute - whole) * 100m, 0, MidpointRounding.AwayFromZero);

        var digits = ((long)whole).ToString(CultureInfo.InvariantCulture);
        var grouped = new StringBuilder();
        var remaining = digits.Length;
        for (var i = 0; i < digits.Length; i++)
        {
            grouped.Append(digits[i]);
            remaining--;
            if (remaining > 0 && remaining % 3 == 0)
                grouped.Append(ThinSpace);
        }

        var sign = negative ? "-" : string.Empty;
        return $"{sign}{grouped},{fraction.ToString("00", CultureInfo.InvariantCulture)}";
    }
}
