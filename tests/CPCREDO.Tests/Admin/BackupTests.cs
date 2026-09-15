using CPCREDO.Application.Admin;
using CPCREDO.Application.Members;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Domain.Tenancy;
using CPCREDO.Infrastructure.Admin;
using CPCREDO.Infrastructure.Audit;
using CPCREDO.Infrastructure.Persistence;
using CPCREDO.Tests.Accounting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace CPCREDO.Tests.Admin;

public sealed class BackupTests
{
    [Fact]
    public async Task Backup_writes_timestamped_dump_and_kyc_zip_without_drop()
    {
        using var harness = new BackupHarness();
        Directory.CreateDirectory(Path.Combine(harness.KycRoot, "member-a"));
        File.WriteAllText(Path.Combine(harness.KycRoot, "member-a", "photo.jpg"), "x");

        var result = await harness.Backup.BackupNowAsync();

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Matches(@"^cpcredo-\d{8}-\d{4}\.dump$", result.Value!.DumpFileName);
        Assert.Equal("cpcredo-kyc-" + result.Value.DumpFileName["cpcredo-".Length..].Replace(".dump", ".zip"), result.Value.KycZipFileName);
        Assert.True(File.Exists(Path.Combine(harness.BackupFolder, result.Value.DumpFileName)));
        Assert.True(File.Exists(Path.Combine(harness.BackupFolder, result.Value.KycZipFileName!)));
        Assert.Contains(harness.Process.Calls, c => c.Exe.Contains("pg_dump", StringComparison.OrdinalIgnoreCase));
        var dumpArgs = harness.Process.Calls[0].Args;
        Assert.DoesNotContain(dumpArgs, a => a.Contains("DROP", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("--create", dumpArgs);
        Assert.DoesNotContain("--clean", dumpArgs);
        Assert.Contains("-Fc", dumpArgs);
        Assert.True(await harness.Db.AuditLogs.AnyAsync(a => a.Action == "Backup.Created"));
        var status = await harness.Backup.GetStatusAsync();
        Assert.True(status.IsSuccess, status.ErrorMessage);
        Assert.Equal("OK", status.Value!.LastStatus);
        Assert.Equal(result.Value.DumpFileName, status.Value.LastDumpFileName);
        Assert.True(status.Value.NextRunAtLocal > DateTime.Now.AddMinutes(-1));
    }

    [Fact]
    public async Task Gerant_can_backup_but_cannot_restore()
    {
        using var harness = new BackupHarness();
        harness.User.Roles = [RoleNames.Gerant];

        var backup = await harness.Backup.BackupNowAsync();
        Assert.True(backup.IsSuccess, backup.ErrorMessage);

        var restore = await harness.Backup.RestoreAsync(new RestoreBackupRequest
        {
            DumpFileName = backup.Value!.DumpFileName,
            Password = "AdminPass!12",
            Confirmation = "SAUVEGARDE"
        });
        Assert.False(restore.IsSuccess);
        Assert.Equal("auth.forbidden", restore.ErrorCode);
    }

    [Fact]
    public async Task Restore_requires_password_and_does_not_drop_database()
    {
        using var harness = new BackupHarness();
        var backup = await harness.Backup.BackupNowAsync();
        Assert.True(backup.IsSuccess, backup.ErrorMessage);

        var noConfirm = await harness.Backup.RestoreAsync(new RestoreBackupRequest
        {
            DumpFileName = backup.Value!.DumpFileName,
            Password = "AdminPass!12",
            Confirmation = "oui"
        });
        Assert.False(noConfirm.IsSuccess);
        Assert.Equal("backup.confirmation_required", noConfirm.ErrorCode);

        var denied = await harness.Backup.RestoreAsync(new RestoreBackupRequest
        {
            DumpFileName = backup.Value.DumpFileName,
            Password = "wrong",
            Confirmation = "SAUVEGARDE"
        });
        Assert.False(denied.IsSuccess);
        Assert.Equal("auth.invalid_credentials", denied.ErrorCode);
        Assert.True(await harness.Db.AuditLogs.AnyAsync(a => a.Action == "Backup.RestoreDenied"));

        var ok = await harness.Backup.RestoreAsync(new RestoreBackupRequest
        {
            DumpFileName = backup.Value.DumpFileName,
            Password = "AdminPass!12",
            Confirmation = "SAUVEGARDE"
        });
        Assert.True(ok.IsSuccess, ok.ErrorMessage);
        var restoreArgs = harness.Process.Calls.Last(c => c.Exe.Contains("pg_restore", StringComparison.OrdinalIgnoreCase)).Args;
        Assert.Contains("--clean", restoreArgs);
        Assert.Contains("--if-exists", restoreArgs);
        Assert.DoesNotContain("--create", restoreArgs);
        Assert.DoesNotContain(restoreArgs, a => a.Contains("DROP DATABASE", StringComparison.OrdinalIgnoreCase));
        Assert.True(await harness.Db.AuditLogs.AnyAsync(a => a.Action == "Backup.Restored"));
    }

    [Fact]
    public async Task Settings_persist_folder_and_pg_dump_path()
    {
        using var harness = new BackupHarness();
        var folder = Path.Combine(harness.Root, "custom-backups");
        var saved = await harness.Backup.SaveSettingsAsync(new BackupSettingsDto(folder, @"C:\Program Files\PostgreSQL\16\bin\pg_dump.exe"));
        Assert.True(saved.IsSuccess, saved.ErrorMessage);
        var loaded = await harness.Backup.GetSettingsAsync();
        Assert.Equal(Path.GetFullPath(folder), loaded.Value!.Folder);
        Assert.Contains("pg_dump.exe", loaded.Value.PgDumpPath, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class BackupHarness : IDisposable
{
    public string Root { get; }
    public string BackupFolder { get; }
    public string KycRoot { get; }
    public CpcredoDbContext Db { get; }
    public BackupService Backup { get; }
    public TestCurrentUser User { get; }
    public FakeBackupProcess Process { get; }

    public BackupHarness()
    {
        Root = Path.Combine(Path.GetTempPath(), "cpcredo-bak-" + Guid.NewGuid().ToString("N"));
        BackupFolder = Path.Combine(Root, "backups");
        KycRoot = Path.Combine(Root, "data", "kyc");
        Directory.CreateDirectory(BackupFolder);
        Directory.CreateDirectory(KycRoot);

        var options = new DbContextOptionsBuilder<CpcredoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        Db = new CpcredoDbContext(options);
        Db.Database.EnsureCreated();

        var now = new DateTime(2026, 9, 2, 16, 0, 0, DateTimeKind.Utc);
        Db.Tenants.Add(new Tenant
        {
            Id = SeedGuids.TenantId,
            Sigle = Letterhead.Sigle,
            LegalName = Letterhead.LegalName,
            City = Letterhead.City,
            Country = Letterhead.Country,
            CreatedAtUtc = now
        });
        Db.Branches.Add(new Branch
        {
            Id = SeedGuids.BranchId,
            TenantId = SeedGuids.TenantId,
            Code = "SIEGE",
            Name = "Siège",
            City = Letterhead.City,
            Country = Letterhead.Country,
            IsHeadquarters = true,
            CreatedAtUtc = now
        });
        var admin = new User
        {
            Id = SeedGuids.AdminUserId,
            TenantId = SeedGuids.TenantId,
            BranchId = SeedGuids.BranchId,
            Username = "admin",
            Email = "admin@cpcredo.ht",
            FullName = "Administrateur",
            IsActive = true,
            CreatedAtUtc = now
        };
        admin.PasswordHash = new PasswordHasher<User>().HashPassword(admin, "AdminPass!12");
        Db.Users.Add(admin);
        Db.SaveChanges();

        User = new TestCurrentUser();
        var clock = new FixedClock { UtcNow = now };
        var audit = new AuditLogger(Db, clock, User);
        Process = new FakeBackupProcess();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=localhost;Port=5432;Database=cpcredo;Username=cpcredo;Password=cpcredo"
            })
            .Build();
        var env = new TestHost { ContentRootPath = Root };
        Backup = new BackupService(
            Process,
            config,
            env,
            Options.Create(new BackupOptions { Folder = BackupFolder, PgDumpPath = Path.Combine(Root, "pg_dump.exe") }),
            Options.Create(new KycStorageOptions { RootPath = KycRoot }),
            User,
            audit,
            clock,
            Db);
        File.WriteAllText(Path.Combine(Root, "pg_dump.exe"), "fake");
        File.WriteAllText(Path.Combine(Root, "pg_restore.exe"), "fake");
    }

    public void Dispose()
    {
        Db.Dispose();
        try { Directory.Delete(Root, true); } catch (IOException) { }
    }

    private sealed class TestHost : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "CPCREDO";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

internal sealed class FakeBackupProcess : IBackupProcess
{
    public List<(string Exe, List<string> Args)> Calls { get; } = [];

    public Task<(int ExitCode, string StandardError)> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        Calls.Add((executable, arguments.ToList()));
        var dashF = arguments.ToList().IndexOf("-f");
        if (dashF >= 0 && dashF + 1 < arguments.Count)
            File.WriteAllText(arguments[dashF + 1], "DUMP");
        return Task.FromResult((0, ""));
    }
}
