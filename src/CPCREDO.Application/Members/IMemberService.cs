using CPCREDO.Application.Common;
using CPCREDO.Domain.Members;

namespace CPCREDO.Application.Members;

public interface IMemberService
{
    Task<Result<MemberListDto>> SearchAsync(
        string? query,
        MemberStatus? status,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    Task<Result<Member360Dto>> Get360Async(Guid id, CancellationToken cancellationToken = default);

    Task<Result<Member360Dto>> CreateAsync(MemberWriteRequest request, CancellationToken cancellationToken = default);

    Task<Result<Member360Dto>> UpdateAsync(Guid id, MemberWriteRequest request, CancellationToken cancellationToken = default);

    Task<Result<Member360Dto>> ConvertToSocietaireAsync(
        Guid id,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<Result<Member360Dto>> SubscribePermanentSharesAsync(
        Guid id,
        SubscribePermanentSharesRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<Result<AgExportDto>> GetAgExportAsync(CancellationToken cancellationToken = default);

    Task<Result<bool>> AssertCapabilityAsync(
        Guid memberId,
        string capability,
        CancellationToken cancellationToken = default);

    Task<Result<MemberTicketDto>> OpenTicketAsync(Guid memberId, OpenTicketRequest request, CancellationToken cancellationToken = default);

    Task<Result<MemberTicketDto>> AssignTicketAsync(Guid memberId, Guid ticketId, AssignTicketRequest request, CancellationToken cancellationToken = default);

    Task<Result<MemberTicketDto>> CloseTicketAsync(Guid memberId, Guid ticketId, CancellationToken cancellationToken = default);
}
