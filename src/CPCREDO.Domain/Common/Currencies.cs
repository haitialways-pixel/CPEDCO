namespace CPCREDO.Domain.Common;

public static class Currencies
{
    public const string Htg = "HTG";
    public const string Usd = "USD";

    public static readonly IReadOnlyList<string> All = [Htg, Usd];

    public static bool IsSupported(string code) =>
        string.Equals(code, Htg, StringComparison.OrdinalIgnoreCase)
        || string.Equals(code, Usd, StringComparison.OrdinalIgnoreCase);
}
