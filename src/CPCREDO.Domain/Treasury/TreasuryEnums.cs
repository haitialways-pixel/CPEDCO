namespace CPCREDO.Domain.Treasury;

public enum TreasuryDirection
{
    VaultToBank = 0,
    BankToVault = 1,
    TillToVault = 2,
    VaultToTill = 3
}

public static class TreasuryDirections
{
    public static bool InvolvesBank(TreasuryDirection direction) =>
        direction is TreasuryDirection.VaultToBank or TreasuryDirection.BankToVault;

    public static bool InvolvesTill(TreasuryDirection direction) =>
        direction is TreasuryDirection.TillToVault or TreasuryDirection.VaultToTill;

    public static string LabelFr(TreasuryDirection direction) =>
        direction switch
        {
            TreasuryDirection.VaultToBank => "Coffre → Banque",
            TreasuryDirection.BankToVault => "Banque → Coffre",
            TreasuryDirection.TillToVault => "Caisse → Coffre",
            TreasuryDirection.VaultToTill => "Coffre → Caisse",
            _ => direction.ToString()
        };
}

public enum TreasuryTransferStatus
{
    Draft = 0,
    Approved1 = 1,
    Approved2 = 2,
    Executed = 3,
    Cancelled = 4
}

public enum TreasurySlipType
{
    DepositSlip = 0,
    WithdrawalSlip = 1
}
