namespace CPCREDO.Domain.Treasury;

public static class CorrespondentBanks
{
    public const string Autre = "Autre";

    public static readonly IReadOnlyList<string> Names =
    [
        "Sogebank",
        "Unibank",
        "BNC",
        "BUH",
        "Capital Bank",
        "Citibank",
        "Sogebel",
        "Fédération / Le Levier",
        "Fédération / Le Sociétaire",
        Autre
    ];

    public static bool IsCatalogName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && Names.Any(n => string.Equals(n, name.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string CanonicalName(string name) =>
        Names.First(n => string.Equals(n, name.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool IsAutre(string? name) =>
        string.Equals(name?.Trim(), Autre, StringComparison.OrdinalIgnoreCase);

    public static string DisplayName(string bankName, string? customBankName) =>
        IsAutre(bankName)
            ? (string.IsNullOrWhiteSpace(customBankName) ? Autre : customBankName.Trim())
            : bankName.Trim();

    public static string MaskAccountNumber(string? number)
    {
        var raw = (number ?? string.Empty).Trim();
        if (raw.Length == 0)
            return "";
        var digits = new string(raw.Where(char.IsLetterOrDigit).ToArray());
        var visible = digits.Length > 0 ? digits : raw;
        if (visible.Length <= 4)
            return new string('*', visible.Length);
        return "****" + visible[^4..];
    }

    public static string PickerLabel(string? label, string bankName, string? customBankName, string number, string currency)
    {
        var bank = DisplayName(bankName, customBankName);
        var head = string.IsNullOrWhiteSpace(label) ? bank : label.Trim();
        return $"{head} · {bank} · {MaskAccountNumber(number)} · {currency}";
    }

    public static bool IsPlaceholderAccountNumber(string? number) =>
        number is "BRH-HTG-001" or "UNI-HTG-001" or "SGB-USD-001";
}
