using CPCREDO.Domain.Common;

namespace CPCREDO.Domain.Treasury;

public class BankAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Bank { get; set; } = string.Empty;
    public string? CustomBankName { get; set; }
    public string Number { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = Currencies.Htg;
    public string GlCode { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public string DisplayBankName => CorrespondentBanks.DisplayName(Bank, CustomBankName);

    public string MaskedNumber => CorrespondentBanks.MaskAccountNumber(Number);

    public string PickerLabel =>
        CorrespondentBanks.PickerLabel(Name, Bank, CustomBankName, Number, CurrencyCode);
}
