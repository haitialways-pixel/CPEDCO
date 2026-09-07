using CPCREDO.Application.Common;

namespace CPCREDO.Application.Loans;

public interface ILoanProductService
{
    Task<Result<IReadOnlyList<LoanProductDto>>> ListAsync(bool activeOnly, CancellationToken cancellationToken = default);

    Task<Result<LoanProductDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<LoanProductDto>> CreateAsync(SaveLoanProductRequest request, CancellationToken cancellationToken = default);

    Task<Result<LoanProductDto>> UpdateAsync(Guid id, SaveLoanProductRequest request, CancellationToken cancellationToken = default);

    Task<Result<LoanProductDto>> DeactivateAsync(Guid id, CancellationToken cancellationToken = default);
}
