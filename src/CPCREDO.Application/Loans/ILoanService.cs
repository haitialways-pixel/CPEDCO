using CPCREDO.Application.Common;

namespace CPCREDO.Application.Loans;

public interface ILoanService
{
    Task<Result<LoanScheduleDto>> PreviewScheduleAsync(PreviewLoanRequest request, CancellationToken cancellationToken = default);

    Task<Result<PayoffQuoteDto>> PreviewPayoffAsync(PreviewLoanRequest request, int daysElapsed, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<LoanDto>>> ListAsync(string? status = null, Guid? memberId = null, CancellationToken cancellationToken = default);

    Task<Result<LoanDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<LoanDto>> CreateDraftAsync(CreateLoanRequest request, CancellationToken cancellationToken = default);

    Task<Result<LoanDto>> SubmitAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<LoanDto>> ApproveAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<LoanDto>> RejectAsync(Guid id, RejectLoanRequest request, CancellationToken cancellationToken = default);

    Task<Result<LoanDto>> DisburseAsync(Guid id, DisburseLoanRequest request, string? idempotencyKey, CancellationToken cancellationToken = default);

    Task<Result<RepaymentResultDto>> RepayAsync(Guid id, RepayLoanRequest request, string? idempotencyKey, CancellationToken cancellationToken = default);

    Task<Result<AccrualResultDto>> RunAccrualAsync(CancellationToken cancellationToken = default);

    Task<Result<CollectionSheetDto>> GetCollectionSheetAsync(string? period, CancellationToken cancellationToken = default);
}
