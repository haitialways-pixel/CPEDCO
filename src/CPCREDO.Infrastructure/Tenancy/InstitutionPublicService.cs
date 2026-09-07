using CPCREDO.Application.Tenancy;
using CPCREDO.Domain.Common;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CPCREDO.Infrastructure.Tenancy;

public sealed class InstitutionPublicService : IInstitutionPublicService
{
    private readonly CpcredoDbContext _db;

    public InstitutionPublicService(CpcredoDbContext db)
    {
        _db = db;
    }

    public async Task<InstitutionPublicDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants
            .AsNoTracking()
            .Include(t => t.DefaultBranch)
            .FirstOrDefaultAsync(t => t.Id == SeedGuids.TenantId, cancellationToken);

        if (tenant is null)
        {
            return new InstitutionPublicDto(
                Letterhead.Sigle,
                Letterhead.LegalName,
                Letterhead.Line2,
                Letterhead.Line3,
                Letterhead.Line4,
                Letterhead.City,
                Letterhead.Country,
                Letterhead.DefaultBranchName,
                Letterhead.DefaultBranchCode,
                Currencies.Htg,
                Currencies.Usd,
                CpcredoTimeZone.DisplayId);
        }

        return new InstitutionPublicDto(
            tenant.Sigle,
            tenant.LegalName,
            Letterhead.Line2,
            Letterhead.Line3,
            Letterhead.Line4,
            tenant.City,
            tenant.Country,
            tenant.DefaultBranch?.Name ?? Letterhead.DefaultBranchName,
            tenant.DefaultBranch?.Code ?? Letterhead.DefaultBranchCode,
            tenant.PrimaryCurrency,
            tenant.SecondaryCurrency,
            tenant.DisplayTimeZone);
    }
}
