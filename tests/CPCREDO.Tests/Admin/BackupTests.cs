using CPCREDO.Application.Admin;
using CPCREDO.Application.Common;
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
        Assert.Equal("OK", status.Value.Files.First(f => f.DumpFileName == result.Value.DumpFileName).Status);
        Assert.Contains("OK", File.ReadAllText(Path.Combine(harness.Root, "backup.log")));
        Assert.DoesNotContain("DROP DATABASE", File.ReadAllText(Path.Combine(harness.Root, "backup.log")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Admin_enables_auto_backup_task_without_drop()
    {
        using var harness = new BackupHarness();
        harness.Tasks.Exists = false;
        harness.Tasks.Enabled = false;

        var result = await harness.Backup.SetAutoBackupAsync(true);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(harness.Tasks.Exists);
        Assert.True(harness.Tasks.Enabled);
        Assert.Equal("Activée", result.Value!.AutoBackupState);
        Assert.True(result.Value.AutoBackupEnabled);
        Assert.NotNull(result.Value.NextRunAtLocal);
        Assert.Contains("EnsureEnabled", harness.Tasks.Actions);
        Assert.DoesNotContain(harness.Process.Calls, c => c.Args.Any(a => a.Contains("DROP", StringComparison.OrdinalIgnoreCase)));
        Assert.True(File.Exists(Path.Combine(harness.Root, "backup.ps1")));
        Assert.True(await harness.Db.AuditLogs.AnyAsync(a => a.Action == "Backup.AutoEnabled"));
    }

    [Fact]
    public async Task Admin_enable_fails_when_pg_dump_missing_leaves_toggle_off()
    {
        using var harness = new BackupHarness();
        File.Delete(Path.Combine(harness.Root, "pg_dump.exe"));
        harness.Backup = harness.RecreateService(pgDumpPath: "", searchSystem: false);
        harness.Tasks.Exists = false;
        harness.Tasks.Enabled = false;

        var result = await harness.Backup.SetAutoBackupAsync(true);

        Assert.False(result.IsSuccess);
        Assert.Equal("backup.pg_dump_missing", result.ErrorCode);
        Assert.Contains("pg_dump", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(harness.Tasks.Enabled);
        Assert.DoesNotContain("EnsureEnabled", harness.Tasks.Actions);
        var loaded = await harness.Backup.GetSettingsAsync();
        Assert.False(loaded.Value!.AutoBackupEnabled);
        Assert.Contains("FAIL", File.ReadAllText(Path.Combine(harness.Root, "backup.log")));
        var status = await harness.Backup.GetStatusAsync();
        Assert.Equal("Désactivée", status.Value!.AutoBackupState);
        Assert.Null(status.Value.NextRunAtLocal);
    }

    [Fact]
    public async Task Admin_enable_fails_when_scheduler_missing_leaves_toggle_off()
    {
        using var harness = new BackupHarness();
        harness.Tasks.SchedulerAvailable = false;

        var result = await harness.Backup.SetAutoBackupAsync(true);

        Assert.False(result.IsSuccess);
        Assert.Equal("backup.scheduler_missing", result.ErrorCode);
        Assert.Contains("Planificateur", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(harness.Tasks.Enabled);
        var loaded = await harness.Backup.GetSettingsAsync();
        Assert.False(loaded.Value!.AutoBackupEnabled);
        Assert.Contains("FAIL", File.ReadAllText(Path.Combine(harness.Root, "backup.log")));
    }

    [Fact]
    public async Task Gerant_can_backup_but_cannot_toggle_auto()
    {
        using var harness = new BackupHarness();
        harness.User.Roles = [RoleNames.Gerant];

        var backup = await harness.Backup.BackupNowAsync();
        Assert.True(backup.IsSuccess, backup.ErrorMessage);

        var toggle = await harness.Backup.SetAutoBackupAsync(true);
        Assert.False(toggle.IsSuccess);
        Assert.Equal("auth.forbidden", toggle.ErrorCode);
        Assert.Empty(harness.Tasks.Actions);
    }

    [Fact]
    public async Task Admin_disable_does_not_delete_script_or_dumps()
    {
        using var harness = new BackupHarness();
        harness.Tasks.Exists = true;
        harness.Tasks.Enabled = true;
        var dump = Path.Combine(harness.BackupFolder, "cpcredo-20260915-1200.dump");
        File.WriteAllText(dump, "DUMP");
        var script = Path.Combine(harness.Root, "backup.ps1");

        var result = await harness.Backup.SetAutoBackupAsync(false);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(harness.Tasks.Enabled);
        Assert.True(harness.Tasks.Exists);
        Assert.Equal("Désactivée", result.Value!.AutoBackupState);
        Assert.Null(result.Value.NextRunAtLocal);
        Assert.True(File.Exists(dump));
        Assert.True(File.Exists(script));
        Assert.True(await harness.Db.AuditLogs.AnyAsync(a => a.Action == "Backup.AutoDisabled"));
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
    public async Task Settings_persist_folder_pg_dump_and_retention()
    {
        using var harness = new BackupHarness();
        var folder = Path.Combine(harness.Root, "custom-backups");
        var saved = await harness.Backup.SaveSettingsAsync(new BackupSettingsDto(
            folder,
            @"C:\Program Files\PostgreSQL\16\bin\pg_dump.exe",
            AutoBackupEnabled: false,
            RetentionDays: 21,
            KeepFiles: 9));
        Assert.True(saved.IsSuccess, saved.ErrorMessage);
        var loaded = await harness.Backup.GetSettingsAsync();
        Assert.Equal(Path.GetFullPath(folder), loaded.Value!.Folder);
        Assert.Contains("pg_dump.exe", loaded.Value.PgDumpPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(21, loaded.Value.RetentionDays);
        Assert.Equal(9, loaded.Value.KeepFiles);
        Assert.True(loaded.Value.AutoBackupEnabled);
    }
}

internal sealed class BackupHarness : IDisposable
{
    public string Root { get; }
    public string BackupFolder { get; }
    public string KycRoot { get; }
    public CpcredoDbContext Db { get; }
    public BackupService Backup { get; set; }
    public TestCurrentUser User { get; }
    public FakeBackupProcess Process { get; }
    public FakeBackupTaskScheduler Tasks { get; }

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
        Tasks = new FakeBackupTaskScheduler();
        File.WriteAllText(Path.Combine(Root, "pg_dump.exe"), "fake");
        File.WriteAllText(Path.Combine(Root, "pg_restore.exe"), "fake");
        File.WriteAllText(Path.Combine(Root, "backup.ps1"), "# CPCREDO backup");
        Backup = CreateService(Path.Combine(Root, "pg_dump.exe"), clock, audit, searchSystem: true);
    }

    public BackupService RecreateService(string pgDumpPath, bool searchSystem = true)
    {
        var clock = new FixedClock { UtcNow = new DateTime(2026, 9, 2, 16, 0, 0, DateTimeKind.Utc) };
        return Backup = CreateService(pgDumpPath, clock, new AuditLogger(Db, clock, User), searchSystem);
    }

    private BackupService CreateService(string pgDumpPath, FixedClock clock, AuditLogger audit, bool searchSystem)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=localhost;Port=5432;Database=cpcredo;Username=cpcredo;Password=cpcredo"
            })
            .Build();
        var env = new TestHost { ContentRootPath = Root };
        return new BackupService(
            Process,
            Tasks,
            config,
            env,
            Options.Create(new BackupOptions
            {
                Folder = BackupFolder,
                PgDumpPath = pgDumpPath,
                SearchSystemPgDump = searchSystem
            }),
            Options.Create(new KycStorageOptions { RootPath = KycRoot }),
            User,
            audit,
            clock,
            Db);
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

internal sealed class FakeBackupTaskScheduler : IBackupTaskScheduler
{
    public bool SchedulerAvailable { get; set; } = true;
    public bool Exists { get; set; }
    public bool Enabled { get; set; }
    public DateTime? NextRun { get; set; } = DateTime.Today.AddHours(18).AddMinutes(30);
    public List<string> Actions { get; } = [];

    public BackupTaskSnapshot Query()
    {
        if (!SchedulerAvailable)
            return new BackupTaskSnapshot(false, false, false, null, "Le Planificateur de tâches est indisponible. La sauvegarde automatique n’a pas été activée.");
        return new BackupTaskSnapshot(true, Exists, Enabled, Enabled ? NextRun : null, null);
    }

    public Result<BackupTaskSnapshot> EnsureEnabled(string scriptPath)
    {
        Actions.Add("EnsureEnabled");
        if (!SchedulerAvailable)
            return Result<BackupTaskSnapshot>.Fail(
                "backup.scheduler_missing",
                "Le Planificateur de tâches est indisponible. La sauvegarde automatique n’a pas été activée.");
        if (string.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
            return Result<BackupTaskSnapshot>.Fail("backup.script_missing", "Le script backup.ps1 est introuvable. Relancez INSTALLER-SERVEUR.bat.");
        Exists = true;
        Enabled = true;
        return Result<BackupTaskSnapshot>.Ok(Query());
    }

    public Result<BackupTaskSnapshot> Disable()
    {
        Actions.Add("Disable");
        if (!SchedulerAvailable)
            return Result<BackupTaskSnapshot>.Fail(
                "backup.scheduler_missing",
                "Impossible de désactiver la tâche planifiée CPCREDO-Backup.");
        Enabled = false;
        return Result<BackupTaskSnapshot>.Ok(Query());
    }
}
