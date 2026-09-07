namespace CPCREDO.Domain.Common;

/// <summary>Stable identifiers for the CPCREDO seed so reruns are idempotent.</summary>
public static class SeedGuids
{
    public static readonly Guid TenantId = Guid.Parse("0c0ec0de-0001-4000-a000-000000000001");
    public static readonly Guid BranchId = Guid.Parse("0c0ec0de-0001-4000-a000-000000000002");
    public static readonly Guid AdminUserId = Guid.Parse("0c0ec0de-0001-4000-a000-000000000010");
    public static readonly Guid GerantUserId = Guid.Parse("0c0ec0de-0001-4000-a000-000000000011");
    public static readonly Guid CaissierUserId = Guid.Parse("0c0ec0de-0001-4000-a000-000000000012");

    public static readonly Guid RoleAdmin = Guid.Parse("0c0ec0de-0001-4000-a000-000000000101");
    public static readonly Guid RoleGerant = Guid.Parse("0c0ec0de-0001-4000-a000-000000000102");
    public static readonly Guid RoleCaissier = Guid.Parse("0c0ec0de-0001-4000-a000-000000000103");
    public static readonly Guid RoleOfficierCredit = Guid.Parse("0c0ec0de-0001-4000-a000-000000000104");
    public static readonly Guid RoleServiceClient = Guid.Parse("0c0ec0de-0001-4000-a000-000000000105");
    public static readonly Guid RoleCommissaire = Guid.Parse("0c0ec0de-0001-4000-a000-000000000106");

    public static readonly Guid OpeningJournalId = Guid.Parse("0c0ec0de-0001-4000-a000-00000000f001");
    public static readonly Guid OpeningLineCashId = Guid.Parse("0c0ec0de-0001-4000-a000-00000000f002");
    public static readonly Guid OpeningLineBankId = Guid.Parse("0c0ec0de-0001-4000-a000-00000000f003");
    public static readonly Guid OpeningLineCapitalId = Guid.Parse("0c0ec0de-0001-4000-a000-00000000f004");

    public static readonly Guid MemberMarieClaire = Guid.Parse("0c0ec0de-0001-4000-a000-000000000201");
    public static readonly Guid MemberJeanBaptiste = Guid.Parse("0c0ec0de-0001-4000-a000-000000000202");
    public static readonly Guid MemberNadege = Guid.Parse("0c0ec0de-0001-4000-a000-000000000203");
    public static readonly Guid ShareMarieClaire = Guid.Parse("0c0ec0de-0001-4000-a000-000000000211");
    public static readonly Guid ShareJeanBaptiste = Guid.Parse("0c0ec0de-0001-4000-a000-000000000212");
    public static readonly Guid ShareNadege = Guid.Parse("0c0ec0de-0001-4000-a000-000000000213");

    public static Guid SeedMember(int serial) =>
        Guid.Parse($"0c0ec0de-0001-4000-a000-{serial:D12}");

    public static Guid SeedShare(int serial) =>
        Guid.Parse($"0c0ec0de-0001-4000-a000-{serial:D12}");

    public static readonly Guid SavingsProductHtg = Guid.Parse("0c0ec0de-0001-4000-a000-000000000301");
    public static readonly Guid SavingsProductUsd = Guid.Parse("0c0ec0de-0001-4000-a000-000000000302");

    public static readonly Guid BankBrhHtg = Guid.Parse("0c0ec0de-0001-4000-a000-000000000401");
    public static readonly Guid BankUnibankHtg = Guid.Parse("0c0ec0de-0001-4000-a000-000000000402");
    public static readonly Guid BankSogebankUsd = Guid.Parse("0c0ec0de-0001-4000-a000-000000000403");

    public static Guid Gl(string code)
    {
        var padded = code.PadLeft(12, '0');
        return Guid.Parse($"0c0ec0de-0001-4000-a000-{padded}");
    }
}
