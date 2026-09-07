using CPCREDO.Domain.Common;
using CPCREDO.Domain.Members;

namespace CPCREDO.Infrastructure.Seed;

internal sealed record MembershipClassSpec(
    Guid Id,
    string MemberNo,
    string FirstName,
    string LastName,
    string Cin,
    string Nif,
    string Phone,
    string Address,
    LegalStatus LegalStatus,
    FounderGroup? FounderGroup,
    int QualificationShares,
    int PermanentShares,
    Guid QualShareId,
    string QualAccountNo,
    Guid? PermShareId,
    string? PermAccountNo,
    int UsagerDaysElapsed);

internal static class MembershipClassSeed
{
    public static IReadOnlyList<MembershipClassSpec> All() =>
    [
        Row(1, SeedGuids.MemberMarieClaire, "Marie-Claire", "Jean", LegalStatus.Societaire, FounderGroup.QualifyingFounder, 1, 0, SeedGuids.ShareMarieClaire, null),
        Row(2, SeedGuids.MemberJeanBaptiste, "Jean-Baptiste", "Pierre", LegalStatus.Societaire, null, 1, 0, SeedGuids.ShareJeanBaptiste, null),
        Row(3, SeedGuids.MemberNadege, "Nadège", "Toussaint", LegalStatus.Societaire, FounderGroup.CapitalFounder, 1, 10, SeedGuids.ShareNadege, SeedGuids.SeedShare(260), permNo: 1),
        Row(4, SeedGuids.SeedMember(204), "Antoine", "Estimé", LegalStatus.Societaire, FounderGroup.CapitalFounder, 1, 10, SeedGuids.SeedShare(240), SeedGuids.SeedShare(261), permNo: 2),
        Row(5, SeedGuids.SeedMember(205), "Claire", "Magloire", LegalStatus.Societaire, FounderGroup.CapitalFounder, 1, 10, SeedGuids.SeedShare(241), SeedGuids.SeedShare(262), permNo: 3),
        Row(6, SeedGuids.SeedMember(206), "Franck", "Sylvain", LegalStatus.Societaire, FounderGroup.CapitalFounder, 1, 10, SeedGuids.SeedShare(242), SeedGuids.SeedShare(263), permNo: 4),
        Row(7, SeedGuids.SeedMember(207), "Gisèle", "Dorsainvil", LegalStatus.Societaire, FounderGroup.CapitalFounder, 1, 10, SeedGuids.SeedShare(243), SeedGuids.SeedShare(264), permNo: 5),
        Row(8, SeedGuids.SeedMember(208), "Henri", "César", LegalStatus.Societaire, FounderGroup.CapitalFounder, 1, 10, SeedGuids.SeedShare(244), SeedGuids.SeedShare(265), permNo: 6),
        Row(9, SeedGuids.SeedMember(209), "Irène", "Belizaire", LegalStatus.Societaire, FounderGroup.QualifyingFounder, 1, 0, SeedGuids.SeedShare(245), null),
        Row(10, SeedGuids.SeedMember(210), "Jacques", "Saint-Fleur", LegalStatus.Societaire, FounderGroup.QualifyingFounder, 1, 0, SeedGuids.SeedShare(246), null),
        Row(11, SeedGuids.SeedMember(271), "Ketia", "Pierre-Louis", LegalStatus.Societaire, FounderGroup.QualifyingFounder, 1, 0, SeedGuids.SeedShare(247), null),
        Row(12, SeedGuids.SeedMember(272), "Louis-Joseph", "Rigaud", LegalStatus.Societaire, FounderGroup.QualifyingFounder, 1, 0, SeedGuids.SeedShare(248), null),
        Row(13, SeedGuids.SeedMember(273), "Monique", "Beauvoir", LegalStatus.Societaire, FounderGroup.QualifyingFounder, 1, 0, SeedGuids.SeedShare(249), null),
        Row(14, SeedGuids.SeedMember(214), "Nicolas", "Lamour", LegalStatus.Societaire, FounderGroup.QualifyingFounder, 1, 0, SeedGuids.SeedShare(250), null),
        Row(15, SeedGuids.SeedMember(215), "Odette", "Cinéas", LegalStatus.Usager, null, 0, 0, SeedGuids.SeedShare(251), null, 12),
        Row(16, SeedGuids.SeedMember(216), "Patrick", "Dorvil", LegalStatus.Usager, null, 0, 0, SeedGuids.SeedShare(252), null, 45),
        Row(17, SeedGuids.SeedMember(217), "Rose-Marie", "Cadet", LegalStatus.Usager, null, 0, 0, SeedGuids.SeedShare(253), null, 88),
        Row(18, SeedGuids.SeedMember(218), "Samuel", "Hyppolite", LegalStatus.Societaire, null, 1, 0, SeedGuids.SeedShare(254), null)
    ];

    private static MembershipClassSpec Row(
        int serial,
        Guid id,
        string first,
        string last,
        LegalStatus legal,
        FounderGroup? founder,
        int qual,
        int perm,
        Guid qualShareId,
        Guid? permShareId,
        int usagerDays = 0,
        int permNo = 0) =>
        new(
            id,
            $"M-{serial:000000}",
            first,
            last,
            $"00-01-80-{serial:0000}-01-{serial:00000}",
            $"000-{serial:000}-100-{serial % 9}",
            $"+509 28{serial:00} {1100 + serial}",
            $"{10 + serial}, rue Lamarre",
            legal,
            founder,
            qual,
            perm,
            qualShareId,
            $"S-{serial:000000}",
            permShareId,
            permShareId is null ? null : $"P-{permNo:000000}",
            usagerDays);
}
