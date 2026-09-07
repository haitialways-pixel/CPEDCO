using CPCREDO.Domain.Common;

namespace CPCREDO.Application.Accounting;

public sealed class CreateJournalLineRequest
{
    public Guid GlAccountId { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? Description { get; set; }
}

public sealed class CreateJournalRequest
{
    public DateOnly? ValueDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = Currencies.Htg;
    public Guid? BranchId { get; set; }
    public List<CreateJournalLineRequest> Lines { get; set; } = [];
}

public sealed class ReverseJournalRequest
{
    public DateOnly? ValueDate { get; set; }
    public string? Description { get; set; }
}

public sealed record JournalLineDto(
    Guid Id,
    int LineNo,
    Guid GlAccountId,
    string? AccountCode,
    decimal Debit,
    decimal Credit,
    string Description);

public sealed record JournalDto(
    Guid Id,
    string JournalNo,
    DateOnly ValueDate,
    DateTime PostedAtUtc,
    DateTime PostedAtPortAuPrince,
    Guid PostedByUserId,
    string Description,
    string CurrencyCode,
    string Status,
    bool IsReversal,
    Guid? ReversalOfJournalId,
    decimal TotalDebit,
    decimal TotalCredit,
    IReadOnlyList<JournalLineDto> Lines);

public sealed record TrialBalanceRowDto(
    Guid GlAccountId,
    string AccountCode,
    string AccountName,
    string AccountType,
    decimal Debit,
    decimal Credit);

public sealed record TrialBalanceDto(
    DateOnly AsOf,
    string CurrencyCode,
    IReadOnlyList<TrialBalanceRowDto> Rows,
    decimal TotalDebit,
    decimal TotalCredit,
    decimal Net);
