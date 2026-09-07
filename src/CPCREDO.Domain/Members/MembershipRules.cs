using CPCREDO.Domain.Common;

namespace CPCREDO.Domain.Members;

/// <summary>
/// Haiti CEC mapping: one sociétaire, one vote — never derived from permanent shares
/// or from a stored voting flag.
/// </summary>
public static class MembershipRules
{
    public const decimal DefaultShareParValue = 500.0000m;
    public const decimal MaxShareParValue = 500.0000m;
    public const int DefaultProbationDays = 90;
    public const int MaxProbationDays = 180;
    public const string VoteRule = "one_member_one_vote";
    public const string MemberNoPrefix = "M-";
    public const string ShareAccountNoPrefix = "S-";
    public const string PermanentShareAccountNoPrefix = "P-";
    public const string QualificationCapitalGl = "3010";
    public const string PermanentCapitalGl = "3011";
    public const string CashHtgGl = "1010";
    public const string Micro90Product = "Micro90";
    public const string OfficerCapability = "Officer";

    public static decimal NormalizeParValue(decimal parValue)
    {
        var normalized = MoneyAmount.Normalize(parValue <= 0m ? DefaultShareParValue : parValue);
        return normalized > MaxShareParValue ? MaxShareParValue : normalized;
    }

    public static int ClampProbationDays(int? days)
    {
        var value = days is null or < 1 ? DefaultProbationDays : days.Value;
        return Math.Min(MaxProbationDays, value);
    }

    public static bool HasVotingRights(LegalStatus legalStatus, MemberStatus membershipStatus, int paidQualificationShares) =>
        legalStatus == LegalStatus.Societaire
        && membershipStatus == MemberStatus.Active
        && paidQualificationShares >= 1;

    public static bool HasVotingRights(Member member) =>
        HasVotingRights(member.LegalStatus, member.Status, member.QualificationShareCount);

    public static int VoteWeight(Member member) => HasVotingRights(member) ? 1 : 0;

    public static bool IsKycActive(KycStatus kyc) => kyc == KycStatus.Verified;

    public static bool AllowsMicro90(LegalStatus legalStatus) => legalStatus == LegalStatus.Societaire;

    public static bool AllowsOfficerRole(LegalStatus legalStatus) => legalStatus == LegalStatus.Societaire;

    public static bool AllowsSavings(Member member, DateTime utcNow) =>
        member.Status == MemberStatus.Active && !ServicesBlocked(member, utcNow);

    public static bool ServicesBlocked(Member member, DateTime utcNow) =>
        member.LegalStatus == LegalStatus.Usager && UsagerDaysElapsed(member, utcNow) >= member.ProbationDays;

    public static int UsagerDaysElapsed(Member member, DateTime utcNow)
    {
        if (member.LegalStatus != LegalStatus.Usager)
            return 0;
        var start = member.UsagerSinceUtc ?? member.CreatedAtUtc;
        var today = CpcredoTimeZone.Today(utcNow);
        var startDay = DateOnly.FromDateTime(CpcredoTimeZone.ToDisplay(start));
        return Math.Max(0, today.DayNumber - startDay.DayNumber);
    }

    public static int? UsagerDaysLeft(Member member, DateTime utcNow) =>
        member.LegalStatus == LegalStatus.Usager
            ? Math.Max(0, member.ProbationDays - UsagerDaysElapsed(member, utcNow))
            : null;

    public static decimal BookValue(int shareCount, decimal parValue) =>
        MoneyAmount.Normalize(Math.Max(0, shareCount) * parValue);
}
