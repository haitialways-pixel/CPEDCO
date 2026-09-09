using CPCREDO.Infrastructure.Persistence;
using CPCREDO.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CPCREDO.Tests.Identity;

public sealed class ProductionBootstrapTests
{
    [Fact]
    public async Task Seed_disabled_creates_admin_with_must_change_password_and_no_founders()
    {
        var root = Path.Combine(Path.GetTempPath(), "cpcredo-boot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Seed:Enabled"] = "false" })
                .Build();
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(config);
            services.AddSingleton<IHostEnvironment>(new TestHost { ContentRootPath = root });
            services.AddSingleton<ILogger<DataSeeder>>(NullLogger<DataSeeder>.Instance);
            services.AddDbContext<CpcredoDbContext>(o =>
                o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
            var provider = services.BuildServiceProvider();

            await DataSeeder.SeedAsync(provider);

            var db = provider.GetRequiredService<CpcredoDbContext>();
            Assert.Equal(0, await db.Members.CountAsync());
            var admin = Assert.Single(db.Users);
            Assert.Equal("admin", admin.Username);
            Assert.True(admin.MustChangePassword);
            Assert.False(await db.Members.AnyAsync(m => m.IsFounder));
            var once = Path.Combine(root, "data", "admin-initial-password.txt");
            Assert.True(File.Exists(once));
            var text = await File.ReadAllTextAsync(once);
            Assert.Contains("username=admin", text);
            Assert.DoesNotContain("Admin@Cpcredo2026", text);
            var passLine = text.Split('\n').Select(l => l.Trim()).First(l => l.StartsWith("password=", StringComparison.Ordinal));
            var password = passLine.Substring("password=".Length);
            Assert.Equal(8, password.Length);
            Assert.Matches("^[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{8}$", password);
            Assert.DoesNotContain(" ", password);
            Assert.DoesNotContain("-", password);
            Assert.DoesNotContain("0", password);
            Assert.DoesNotContain("O", password);
            Assert.DoesNotContain("1", password);
            Assert.DoesNotContain("I", password);
            Assert.DoesNotContain("L", password);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    private sealed class TestHost : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "CPCREDO";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
