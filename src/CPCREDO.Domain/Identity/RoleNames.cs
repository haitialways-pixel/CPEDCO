namespace CPCREDO.Domain.Identity;

public static class RoleNames
{
    public const string Admin = "Admin";
    public const string Gerant = "Gerant";
    public const string Caissier = "Caissier";
    public const string OfficierCredit = "OfficierCredit";
    public const string ServiceClient = "ServiceClient";
    public const string Commissaire = "Commissaire";

    public static readonly IReadOnlyList<string> All =
    [
        Admin, Gerant, Caissier, OfficierCredit, ServiceClient, Commissaire
    ];

    public static readonly IReadOnlyList<string> WriteRoles =
    [
        Admin, Gerant, Caissier, OfficierCredit, ServiceClient
    ];

    public static readonly IReadOnlyList<string> ReverseRoles =
    [
        Admin, Gerant
    ];

    public static readonly IReadOnlyList<string> ServiceClientRoles =
    [
        Admin, Gerant, ServiceClient
    ];
}
