namespace CPCREDO.Domain.Treasury;

/// <summary>
/// Vault would be 1020 in a single-currency chart, but 1020 is already Caisse USD.
/// Coffre is therefore 1030 HTG / 1031 USD. Teller cash (1010/1020) is unchanged.
/// </summary>
public static class TreasuryGl
{
    public const string TillHtg = "1010";
    public const string TillUsd = "1020";
    public const string VaultHtg = "1030";
    public const string VaultUsd = "1031";
    public const string BankFeeHtg = "5035";
    public const string BankFeeUsd = "5036";
    public const string DefaultBankHtg = "1110";
    public const string DefaultBankUsd = "1120";
    public const string UnibankHtg = "1111";

    public static string Till(string currency) =>
        string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase) ? TillUsd : TillHtg;

    public static string Vault(string currency) =>
        string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase) ? VaultUsd : VaultHtg;

    public static string BankFee(string currency) =>
        string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase) ? BankFeeUsd : BankFeeHtg;
}
