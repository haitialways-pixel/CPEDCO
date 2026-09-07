using CPCREDO.Application.Savings;
using CPCREDO.Domain.Members;

namespace CPCREDO.Application.Members;

public sealed class MemberWriteRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Cin { get; set; }
    public string? Nif { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? AlternatePhone { get; set; }
    public string AddressLine { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? Commune { get; set; }
    public Guid? BranchId { get; set; }
    public MemberStatus? Status { get; set; }
    public KycStatus? KycStatus { get; set; }
    public LegalStatus? LegalStatus { get; set; }
    public bool? IsFounder { get; set; }
    public FounderGroup? FounderGroup { get; set; }
    public int? ProbationDays { get; set; }
    public int? QualificationShareCount { get; set; }
    public bool? VotingRights { get; set; }
}

public sealed record MemberSummaryDto(
    Guid Id,
    string MemberNo,
    string FirstName,
    string LastName,
    string FullName,
    string Phone,
    string City,
    string Status,
    string KycStatus,
    string LegalStatus,
    bool IsFounder,
    bool VotingRights);

public sealed record ShareHoldingsDto(
    string? QualificationAccountNo,
    string? PermanentAccountNo,
    int QualificationShareCount,
    int PermanentShareCount,
    decimal ParValue,
    string CurrencyCode,
    decimal QualificationBookValue,
    decimal PermanentBookValue,
    bool VotingRights,
    string VoteRule);

public sealed record ConvertToSocietaireRequest;

public sealed record SubscribePermanentSharesRequest
{
    public int Quantity { get; set; } = 1;
}

public sealed record AgVoterDto(
    Guid Id,
    string MemberNo,
    string FullName,
    string LegalStatus,
    string? FounderGroup,
    int QualificationShareCount,
    int PermanentShareCount,
    int Votes);

public sealed record AgExportDto(
    DateOnly AsOf,
    string VoteRule,
    int TotalVoters,
    int TotalVotes,
    IReadOnlyList<AgVoterDto> Voters);

public sealed record MemberTransactionDto(
    Guid SavingsAccountId,
    string AccountNo,
    DateTime ValueDateUtc,
    DateTime PostedAtUtc,
    DateTime PostedAtPortAuPrince,
    string EntryType,
    decimal Amount,
    string CurrencyCode,
    string Description);

public sealed record OpenTicketRequest
{
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public Guid? AssignToUserId { get; set; }
}

public sealed record AssignTicketRequest
{
    public Guid AssignedToUserId { get; set; }
}

public sealed record MemberTicketDto(
    Guid Id,
    string TicketNo,
    string Subject,
    string Body,
    string Status,
    Guid CreatedByUserId,
    string CreatedByName,
    Guid? AssignedToUserId,
    string? AssignedToName,
    DateTime CreatedAtUtc,
    DateTime? ClosedAtUtc);

public sealed record Member360Dto(
    Guid Id,
    string MemberNo,
    string FirstName,
    string LastName,
    string FullName,
    string? Cin,
    string? Nif,
    string Phone,
    string? AlternatePhone,
    string AddressLine,
    string City,
    string? Commune,
    Guid BranchId,
    string BranchName,
    string Status,
    string KycStatus,
    string LegalStatus,
    bool IsFounder,
    string? FounderGroup,
    DateTime? UsagerSinceUtc,
    int ProbationDays,
    int? UsagerDaysElapsed,
    int? UsagerDaysLeft,
    bool ServicesBlocked,
    bool VotingRights,
    string VoteRule,
    DateTime CreatedAtUtc,
    DateTime CreatedAtPortAuPrince,
    DateTime? UpdatedAtUtc,
    ShareHoldingsDto Shares,
    IReadOnlyList<SavingsAccountDto> SavingsAccounts,
    IReadOnlyList<MemberTransactionDto> RecentTransactions,
    IReadOnlyList<MemberTicketDto> Tickets,
    string LoansPlaceholder);

public sealed record MemberListDto(
    IReadOnlyList<MemberSummaryDto> Items,
    int Total);
