namespace CPCREDO.Domain.Common;

/// <summary>
/// Official letterhead hierarchy for header, login, and PDF (later).
/// 1) Sigle — largest, bold
/// 2) Line2 — medium
/// 3) Line3 — smaller, not bold
/// 4) CityCountry — smallest
/// </summary>
public static class Letterhead
{
    public const string Sigle = "CPCREDO";
    public const string Line2 = "Caisse Populaire d’Épargne et de Crédit";
    public const string Line3 = "pour le Développement de l’Ouest";
    public const string Line4 = "Pétion-Ville, Haïti";
    public const string LegalName = "Caisse Populaire d’Épargne et de Crédit pour le Développement de l’Ouest";
    public const string City = "Pétion-Ville";
    public const string Country = "Haïti";
    public const string DefaultBranchName = "Siège Pétion-Ville";
    public const string DefaultBranchCode = "SIEGE";
}
