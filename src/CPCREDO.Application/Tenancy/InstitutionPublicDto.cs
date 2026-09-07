namespace CPCREDO.Application.Tenancy;

public sealed record InstitutionPublicDto(
    string Sigle,
    string LegalName,
    string LetterheadLine2,
    string LetterheadLine3,
    string LetterheadLine4,
    string City,
    string Country,
    string DefaultBranchName,
    string DefaultBranchCode,
    string PrimaryCurrency,
    string SecondaryCurrency,
    string DisplayTimeZone);
