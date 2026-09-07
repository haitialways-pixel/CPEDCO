namespace CPCREDO.Application.Tenancy;

public interface IInstitutionPublicService
{
    Task<InstitutionPublicDto> GetAsync(CancellationToken cancellationToken = default);
}
