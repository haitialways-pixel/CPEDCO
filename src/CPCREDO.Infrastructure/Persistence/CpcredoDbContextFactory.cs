using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CPCREDO.Infrastructure.Persistence;

public sealed class CpcredoDbContextFactory : IDesignTimeDbContextFactory<CpcredoDbContext>
{
    public CpcredoDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=cpcredo;Username=cpcredo;Password=cpcredo";

        var options = new DbContextOptionsBuilder<CpcredoDbContext>()
            .UseNpgsql(connectionString, npg =>
            {
                npg.MigrationsHistoryTable("__ef_migrations_history");
                npg.MigrationsAssembly(typeof(CpcredoDbContext).Assembly.FullName);
            })
            .UseSnakeCaseNamingConvention()
            .Options;

        return new CpcredoDbContext(options);
    }
}
