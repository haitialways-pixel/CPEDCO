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

    public static readonly IReadOnlyList<string> KycUploadRoles =
    [
        Admin, Gerant, ServiceClient, OfficierCredit, Caissier
    ];

    public static readonly IReadOnlyList<string> KycManageRoles =
    [
        Admin, Gerant
    ];

    public static readonly IReadOnlyList<string> MemberCreateRoles =
    [
        Admin, Gerant, OfficierCredit, ServiceClient
    ];

    public static readonly IReadOnlyList<string> BackupRoles =
    [
        Admin, Gerant
    ];

    /// <summary>Till open/close and cash pad. Admin kept: it was not previously forbidden.</summary>
    public static readonly IReadOnlyList<string> TillRoles =
    [
        Admin, Gerant, Caissier
    ];

    public static readonly IReadOnlyList<string> DisburseRoles =
    [
        Caissier
    ];

    public static readonly IReadOnlyList<string> CollectRoles =
    [
        Caissier, Gerant
    ];

    public static readonly IReadOnlyList<string> LoanDraftRoles =
    [
        Admin, Gerant, OfficierCredit
    ];

    public static readonly IReadOnlyList<string> LoanApproveRoles =
    [
        Admin, Gerant
    ];

    public static readonly IReadOnlyList<string> MemberEditRoles =
    [
        Admin, Gerant, OfficierCredit, ServiceClient
    ];

    public static readonly IReadOnlyList<string> TicketRoles =
    [
        Admin, Gerant, ServiceClient
    ];

    public static readonly IReadOnlyList<string> TreasuryRoles =
    [
        Admin, Gerant
    ];

    public static readonly IReadOnlyList<string> CreditReportRoles =
    [
        Admin, Gerant, OfficierCredit, Commissaire
    ];

    public static readonly IReadOnlyList<string> TreasuryDraftRoles =
    [
        Admin, Gerant, Caissier
    ];

    public static readonly IReadOnlyList<string> ProductRoles =
    [
        Admin, Gerant
    ];

    public static readonly IReadOnlyList<string> LivretRoles =
    [
        Admin, Gerant, ServiceClient, Caissier
    ];

    public static readonly IReadOnlyList<string> SavingsManageRoles =
    [
        Admin, Gerant
    ];

    public static readonly IReadOnlyList<string> JournalPostRoles =
    [
        Admin, Gerant, Caissier
    ];

    public static readonly IReadOnlyList<string> MfaRequiredRoles =
    [
        Admin, Gerant
    ];
}
