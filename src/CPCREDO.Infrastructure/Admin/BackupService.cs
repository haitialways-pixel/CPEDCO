using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using CPCREDO.Application.Admin;
using CPCREDO.Application.Common;
using CPCREDO.Application.Members;
using CPCREDO.Domain.Identity;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CPCREDO.Infrastructure.Admin;

public sealed class BackupService : IBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly IBackupProcess _process;
    private readonly IBackupTaskScheduler _tasks;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;
    private readonly IOptions<BackupOptions> _defaults;
    private readonly IOptions<KycStorageOptions> _kyc;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IClock _clock;
    private readonly CpcredoDbContext _db;
    private readonly PasswordHasher<User> _hasher = new();

    public BackupService(
        IBackupProcess process,
        IBackupTaskScheduler tasks,
        IConfiguration config,
        IHostEnvironment env,
        IOptions<BackupOptions> defaults,
        IOptions<KycStorageOptions> kyc,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IClock clock,
        CpcredoDbContext db)
    {
        _process = process;
        _tasks = tasks;
        _config = config;
        _env = env;
        _defaults = defaults;
        _kyc = kyc;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
        _db = db;
    }

    public Task<Result<BackupSettingsDto>> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanBackup())
            return Task.FromResult(Result<BackupSettingsDto>.Fail("auth.forbidden", "Accès refusé."));
        return Task.FromResult(Result<BackupSettingsDto>.Ok(LoadSettings()));
    }

    public async Task<Result<BackupSettingsDto>> SaveSettingsAsync(BackupSettingsDto settings, CancellationToken cancellationToken = default)
    {
        if (!CanBackup())
            return Result<BackupSettingsDto>.Fail("auth.forbidden", "Accès refusé.");

        var folder = (settings.Folder ?? "").Trim();
        var pgDump = (settings.PgDumpPath ?? "").Trim();
        if (string.IsNullOrWhiteSpace(folder))
            return Result<BackupSettingsDto>.Fail("backup.folder_required", "Le dossier de sauvegarde est obligatoire.");

        var current = LoadSettings();
        var resolved = ResolveFolder(folder);
        Directory.CreateDirectory(resolved);
        var stored = new BackupSettingsDto(
            resolved,
            pgDump,
            current.AutoBackupEnabled,
            ClampRetentionDays(settings.RetentionDays),
            ClampKeepFiles(settings.KeepFiles));
        await PersistSettingsAsync(stored, cancellationToken);
        await _audit.LogAsync("Backup.SettingsSaved", "Backup", null, new { folder = resolved }, cancellationToken: cancellationToken);
        return Result<BackupSettingsDto>.Ok(stored);
    }

    public Task<Result<IReadOnlyList<BackupFileDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!CanBackup())
            return Task.FromResult(Result<IReadOnlyList<BackupFileDto>>.Fail("auth.forbidden", "Accès refusé."));

        var folder = ResolveFolder(LoadSettings().Folder);
        if (!Directory.Exists(folder))
            return Task.FromResult(Result<IReadOnlyList<BackupFileDto>>.Ok(Array.Empty<BackupFileDto>()));

        var last = ReadLastResult();
        var dumps = Directory.GetFiles(folder, "cpcredo-*.dump")
            .Select(path =>
            {
                var name = Path.GetFileName(path);
                var kycName = name.StartsWith("cpcredo-", StringComparison.Ordinal)
                    ? "cpcredo-kyc-" + name["cpcredo-".Length..]
                    : null;
                if (kycName is not null)
                    kycName = Path.ChangeExtension(kycName, ".zip");
                var kycPath = kycName is null ? null : Path.Combine(folder, kycName);
                var kycExists = kycPath is not null && File.Exists(kycPath);
                var info = new FileInfo(path);
                var ok = info.Length > 0
                    && !(string.Equals(last.DumpFileName, name, StringComparison.OrdinalIgnoreCase) && last.Ok == false);
                return new BackupFileDto(
                    name,
                    DateTime.SpecifyKind(info.LastWriteTimeUtc, DateTimeKind.Utc),
                    info.Length,
                    kycExists ? kycName : null,
                    kycExists ? new FileInfo(kycPath!).Length : null,
                    ok ? "OK" : "FAIL");
            })
            .OrderByDescending(x => x.DumpFileName)
            .ToList();

        if (last.Ok == false
            && !string.IsNullOrWhiteSpace(last.DumpFileName)
            && dumps.All(d => !string.Equals(d.DumpFileName, last.DumpFileName, StringComparison.OrdinalIgnoreCase)))
        {
            dumps.Insert(0, new BackupFileDto(
                last.DumpFileName!,
                last.AtUtc ?? DateTime.UtcNow,
                0,
                null,
                null,
                "FAIL"));
        }

        return Task.FromResult(Result<IReadOnlyList<BackupFileDto>>.Ok(dumps));
    }

    public async Task<Result<BackupRunResult>> BackupNowAsync(CancellationToken cancellationToken = default)
    {
        if (!CanBackup())
            return Result<BackupRunResult>.Fail("auth.forbidden", "Accès refusé.");

        var settings = LoadSettings();
        var folder = ResolveFolder(settings.Folder);
        Directory.CreateDirectory(folder);

        var pgDump = ResolvePgDump(settings.PgDumpPath);
        if (pgDump is null)
        {
            var missing = "pg_dump introuvable. Indiquez le dossier bin de PostgreSQL (ex. C:\\Program Files\\PostgreSQL\\16\\bin) dans le chemin pg_dump.";
            FailBackupLog(missing);
            return Result<BackupRunResult>.Fail("backup.pg_dump_missing", missing);
        }

        var stamp = _clock.ToPortAuPrince(_clock.UtcNow).ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        var dumpName = $"cpcredo-{stamp}.dump";
        var dumpPath = Path.Combine(folder, dumpName);
        var cs = ParseConnection();
        var args = new[]
        {
            "-h", cs.Host,
            "-p", cs.Port.ToString(CultureInfo.InvariantCulture),
            "-U", cs.Username,
            "-d", cs.Database,
            "-Fc",
            "-f", dumpPath
        };
        if (args.Any(a => a.Contains("DROP", StringComparison.OrdinalIgnoreCase) || a is "--create" or "--clean"))
            return Result<BackupRunResult>.Fail("backup.refused", "Sauvegarde refusée.");

        var env = new Dictionary<string, string> { ["PGPASSWORD"] = cs.Password ?? "" };
        var (code, err) = await _process.RunAsync(pgDump, args, env, cancellationToken);
        if (code != 0 || !File.Exists(dumpPath))
        {
            var fail = string.IsNullOrWhiteSpace(err) ? "pg_dump a échoué." : err;
            FailBackupLog(fail, dumpName);
            return Result<BackupRunResult>.Fail("backup.dump_failed", fail);
        }

        string? kycName = null;
        var kycRoot = string.IsNullOrWhiteSpace(_kyc.Value.RootPath)
            ? Path.Combine(_env.ContentRootPath, "data", "kyc")
            : _kyc.Value.RootPath;
        if (Directory.Exists(kycRoot))
        {
            kycName = $"cpcredo-kyc-{stamp}.zip";
            var zipPath = Path.Combine(folder, kycName);
            if (File.Exists(zipPath))
                File.Delete(zipPath);
            ZipFile.CreateFromDirectory(kycRoot, zipPath, CompressionLevel.SmallestSize, false);
        }

        ApplyRetention(folder, settings.RetentionDays, settings.KeepFiles);

        await _audit.LogAsync(
            "Backup.Created",
            "Backup",
            null,
            new { dump = dumpName, kyc = kycName, folder },
            cancellationToken: cancellationToken);

        WriteBackupLog("OK", dumpName);
        WriteLastResult(true, dumpName, null);
        return Result<BackupRunResult>.Ok(new BackupRunResult(dumpName, kycName, folder));
    }

    public async Task<Result<BackupStatusDto>> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!CanBackup())
            return Result<BackupStatusDto>.Fail("auth.forbidden", "Accès refusé.");

        var files = await ListAsync(cancellationToken);
        var list = files.IsSuccess ? files.Value! : Array.Empty<BackupFileDto>();
        return Result<BackupStatusDto>.Ok(BuildStatus(list));
    }

    public async Task<Result<BackupStatusDto>> SetAutoBackupAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (!_currentUser.Roles.Contains(RoleNames.Admin))
            return Result<BackupStatusDto>.Fail("auth.forbidden", "Seul l’administrateur peut activer ou désactiver la sauvegarde automatique.");

        var settings = LoadSettings();
        if (enabled)
        {
            var pgDump = ResolvePgDump(settings.PgDumpPath);
            if (pgDump is null)
            {
                var missing = "pg_dump introuvable. Indiquez le dossier bin de PostgreSQL (ex. C:\\Program Files\\PostgreSQL\\16\\bin) dans Administration > Sauvegarde. La sauvegarde automatique n’a pas été activée.";
                await PersistSettingsAsync(settings with { AutoBackupEnabled = false }, cancellationToken);
                FailBackupLog(missing);
                return Result<BackupStatusDto>.Fail("backup.pg_dump_missing", missing);
            }

            BackupTaskSnapshot task;
            try
            {
                task = _tasks.Query();
            }
            catch
            {
                task = new BackupTaskSnapshot(false, false, false, null, "Le Planificateur de tâches est indisponible. La sauvegarde automatique n’a pas été activée.");
            }

            if (!task.SchedulerAvailable)
            {
                var msg = string.IsNullOrWhiteSpace(task.Error)
                    ? "Le Planificateur de tâches est indisponible. La sauvegarde automatique n’a pas été activée."
                    : task.Error;
                await PersistSettingsAsync(settings with { AutoBackupEnabled = false }, cancellationToken);
                FailBackupLog(msg);
                return Result<BackupStatusDto>.Fail("backup.scheduler_missing", msg);
            }

            Result<BackupTaskSnapshot> ensured;
            try
            {
                ensured = _tasks.EnsureEnabled(ScriptPath());
            }
            catch
            {
                ensured = Result<BackupTaskSnapshot>.Fail(
                    "backup.scheduler_missing",
                    "Le Planificateur de tâches est indisponible. La sauvegarde automatique n’a pas été activée.");
            }

            if (!ensured.IsSuccess)
            {
                var msg = ensured.ErrorMessage
                    ?? "Le Planificateur de tâches est indisponible. La sauvegarde automatique n’a pas été activée.";
                await PersistSettingsAsync(settings with { AutoBackupEnabled = false }, cancellationToken);
                FailBackupLog(msg);
                return Result<BackupStatusDto>.Fail(ensured.ErrorCode ?? "backup.scheduler_failed", msg);
            }

            await PersistSettingsAsync(settings with { AutoBackupEnabled = true }, cancellationToken);
            await _audit.LogAsync("Backup.AutoEnabled", "Backup", null, new { task = "CPCREDO-Backup" }, cancellationToken: cancellationToken);
            return await GetStatusAsync(cancellationToken);
        }

        Result<BackupTaskSnapshot> disabled;
        try
        {
            disabled = _tasks.Disable();
        }
        catch
        {
            disabled = Result<BackupTaskSnapshot>.Fail(
                "backup.scheduler_missing",
                "Impossible de désactiver la tâche planifiée CPCREDO-Backup.");
        }

        if (!disabled.IsSuccess)
        {
            var msg = disabled.ErrorMessage ?? "Impossible de désactiver la tâche planifiée CPCREDO-Backup.";
            FailBackupLog(msg);
            return Result<BackupStatusDto>.Fail(disabled.ErrorCode ?? "backup.scheduler_failed", msg);
        }

        await PersistSettingsAsync(settings with { AutoBackupEnabled = false }, cancellationToken);
        await _audit.LogAsync("Backup.AutoDisabled", "Backup", null, new { task = "CPCREDO-Backup" }, cancellationToken: cancellationToken);
        return await GetStatusAsync(cancellationToken);
    }

    public async Task<Result<RestoreBackupResult>> RestoreAsync(RestoreBackupRequest request, CancellationToken cancellationToken = default)
    {
        if (!_currentUser.Roles.Contains(RoleNames.Admin))
            return Result<RestoreBackupResult>.Fail("auth.forbidden", "Seul l’administrateur peut restaurer.");

        if (!string.Equals((request.Confirmation ?? "").Trim(), "SAUVEGARDE", StringComparison.Ordinal))
        {
            await _audit.LogAsync("Backup.RestoreDenied", "Backup", null, new { reason = "confirmation" }, cancellationToken: cancellationToken);
            return Result<RestoreBackupResult>.Fail("backup.confirmation_required", "Tapez SAUVEGARDE pour confirmer la restauration.");
        }

        var password = request.Password ?? "";
        if (string.IsNullOrWhiteSpace(password))
            return Result<RestoreBackupResult>.Fail("backup.password_required", "Confirmez avec votre mot de passe.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == _currentUser.UserId, cancellationToken);
        if (user is null || _hasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            await _audit.LogAsync("Backup.RestoreDenied", "Backup", null, new { reason = "password" }, cancellationToken: cancellationToken);
            return Result<RestoreBackupResult>.Fail("auth.invalid_credentials", "Mot de passe incorrect.");
        }

        var dumpName = Path.GetFileName(request.DumpFileName ?? "");
        if (string.IsNullOrWhiteSpace(dumpName) || !dumpName.StartsWith("cpcredo-", StringComparison.Ordinal) || !dumpName.EndsWith(".dump", StringComparison.Ordinal))
            return Result<RestoreBackupResult>.Fail("backup.file_invalid", "Fichier de sauvegarde invalide.");

        var folder = ResolveFolder(LoadSettings().Folder);
        var dumpPath = Path.Combine(folder, dumpName);
        if (!File.Exists(dumpPath))
            return Result<RestoreBackupResult>.Fail("backup.file_not_found", "Fichier de sauvegarde introuvable.");

        var pgDump = ResolvePgDump(LoadSettings().PgDumpPath);
        var pgRestore = ResolvePgRestore(pgDump);
        if (pgRestore is null)
            return Result<RestoreBackupResult>.Fail(
                "backup.pg_restore_missing",
                "pg_restore introuvable. Indiquez le dossier bin de PostgreSQL dans le chemin pg_dump.");

        var cs = ParseConnection();
        var args = new[]
        {
            "--clean",
            "--if-exists",
            "--no-owner",
            "--no-acl",
            "-h", cs.Host,
            "-p", cs.Port.ToString(CultureInfo.InvariantCulture),
            "-U", cs.Username,
            "-d", cs.Database,
            dumpPath
        };

        var env = new Dictionary<string, string> { ["PGPASSWORD"] = cs.Password ?? "" };
        var (code, err) = await _process.RunAsync(pgRestore, args, env, cancellationToken);
        if (code != 0)
            return Result<RestoreBackupResult>.Fail("backup.restore_failed", string.IsNullOrWhiteSpace(err) ? "pg_restore a échoué." : err);

        var kycRestored = false;
        var kycZip = Path.Combine(folder, "cpcredo-kyc-" + dumpName["cpcredo-".Length..]);
        kycZip = Path.ChangeExtension(kycZip, ".zip");
        var kycRoot = string.IsNullOrWhiteSpace(_kyc.Value.RootPath)
            ? Path.Combine(_env.ContentRootPath, "data", "kyc")
            : _kyc.Value.RootPath;
        if (File.Exists(kycZip))
        {
            Directory.CreateDirectory(kycRoot);
            ZipFile.ExtractToDirectory(kycZip, kycRoot, overwriteFiles: true);
            kycRestored = true;
        }

        await _audit.LogAsync(
            "Backup.Restored",
            "Backup",
            null,
            new { dump = dumpName, kycRestored },
            cancellationToken: cancellationToken);

        return Result<RestoreBackupResult>.Ok(new RestoreBackupResult(dumpName, kycRestored));
    }

    private bool CanBackup() =>
        _currentUser.Roles.Any(r => RoleNames.BackupRoles.Contains(r));

    private string SettingsPath() => Path.Combine(_env.ContentRootPath, "data", "backup-settings.json");

    private string ScriptPath() => Path.Combine(_env.ContentRootPath, "backup.ps1");

    private string BackupLogPath() => Path.Combine(_env.ContentRootPath, "backup.log");

    private BackupSettingsDto LoadSettings()
    {
        if (File.Exists(SettingsPath()))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<BackupSettingsFile>(File.ReadAllText(SettingsPath()), JsonOptions);
                if (parsed is not null && !string.IsNullOrWhiteSpace(parsed.Folder))
                {
                    return new BackupSettingsDto(
                        parsed.Folder,
                        parsed.PgDumpPath ?? "",
                        parsed.AutoBackupEnabled ?? _defaults.Value.AutoBackupEnabled,
                        ClampRetentionDays(parsed.RetentionDays ?? _defaults.Value.RetentionDays),
                        ClampKeepFiles(parsed.KeepFiles ?? _defaults.Value.KeepFiles));
                }
            }
            catch (JsonException)
            {
            }
        }

        var folder = string.IsNullOrWhiteSpace(_defaults.Value.Folder)
            ? (OperatingSystem.IsWindows() ? @"C:\CPCREDO\backups" : Path.Combine(_env.ContentRootPath, "backups"))
            : _defaults.Value.Folder;
        return new BackupSettingsDto(
            folder,
            _defaults.Value.PgDumpPath ?? "",
            _defaults.Value.AutoBackupEnabled,
            ClampRetentionDays(_defaults.Value.RetentionDays),
            ClampKeepFiles(_defaults.Value.KeepFiles));
    }

    private async Task PersistSettingsAsync(BackupSettingsDto stored, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath())!);
        await File.WriteAllTextAsync(SettingsPath(), JsonSerializer.Serialize(stored, JsonOptions), cancellationToken);
    }

    private BackupStatusDto BuildStatus(IReadOnlyList<BackupFileDto> list)
    {
        var settings = LoadSettings();
        var last = ReadLastResult();
        BackupTaskSnapshot task;
        try
        {
            task = _tasks.Query();
        }
        catch
        {
            task = new BackupTaskSnapshot(false, false, false, null, null);
        }

        var on = task.SchedulerAvailable && task.Exists && task.Enabled;
        DateTime? next = on ? (task.NextRunLocal ?? NextRunLocal()) : null;
        return new BackupStatusDto(
            settings.Folder,
            settings.PgDumpPath,
            last.Ok is null ? "" : last.Ok.Value ? "OK" : "FAIL",
            last.DumpFileName,
            last.Error,
            last.AtUtc,
            next,
            list,
            on,
            on ? "Activée" : "Désactivée",
            settings.RetentionDays,
            settings.KeepFiles);
    }

    private void FailBackupLog(string message, string? dumpFileName = null)
    {
        WriteBackupLog("FAIL", message);
        WriteLastResult(false, dumpFileName, message);
    }

    private void WriteBackupLog(string status, string detail)
    {
        try
        {
            var line = string.Format(
                CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd HH:mm:ss}  {1}  {2}{3}",
                DateTime.Now,
                status,
                detail,
                Environment.NewLine);
            File.AppendAllText(BackupLogPath(), line);
        }
        catch (IOException)
        {
        }
    }

    private void ApplyRetention(string folder, int retentionDays, int keepFiles)
    {
        try
        {
            if (!Directory.Exists(folder))
                return;
            var dumps = Directory.GetFiles(folder, "cpcredo-*.dump")
                .Select(p => new FileInfo(p))
                .OrderBy(p => p.LastWriteTimeUtc)
                .ToList();
            var cutoff = DateTime.Now.AddDays(-ClampRetentionDays(retentionDays));
            keepFiles = ClampKeepFiles(keepFiles);
            while (dumps.Count > keepFiles)
            {
                var oldest = dumps[0];
                if (oldest.LastWriteTime > cutoff)
                    break;
                try { oldest.Delete(); } catch (IOException) { }
                var kycOld = Path.ChangeExtension(
                    Path.Combine(folder, "cpcredo-kyc-" + oldest.Name["cpcredo-".Length..]),
                    ".zip");
                if (File.Exists(kycOld))
                {
                    try { File.Delete(kycOld); } catch (IOException) { }
                }
                dumps.RemoveAt(0);
            }
        }
        catch (IOException)
        {
        }
    }

    private static int ClampRetentionDays(int days) => days < 1 ? 14 : Math.Min(days, 365);

    private static int ClampKeepFiles(int keep) => keep < 1 ? 7 : Math.Min(keep, 100);

    private sealed class BackupSettingsFile
    {
        public string? Folder { get; set; }
        public string? PgDumpPath { get; set; }
        public bool? AutoBackupEnabled { get; set; }
        public int? RetentionDays { get; set; }
        public int? KeepFiles { get; set; }
    }

    private string LastResultPath() => Path.Combine(_env.ContentRootPath, "data", "backup-last.json");

    private void WriteLastResult(bool ok, string? dumpFileName, string? error)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LastResultPath())!);
            var payload = new BackupLastFile(ok, dumpFileName, error, DateTime.UtcNow);
            File.WriteAllText(LastResultPath(), JsonSerializer.Serialize(payload, JsonOptions));
        }
        catch (IOException)
        {
        }
    }

    private BackupLastFile ReadLastResult()
    {
        try
        {
            if (File.Exists(LastResultPath()))
                return JsonSerializer.Deserialize<BackupLastFile>(File.ReadAllText(LastResultPath()), JsonOptions)
                    ?? new BackupLastFile(null, null, null, null);
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }
        return new BackupLastFile(null, null, null, null);
    }

    private static DateTime NextRunLocal()
    {
        var now = DateTime.Now;
        var next = now.Date.AddHours(18).AddMinutes(30);
        if (now >= next)
            next = next.AddDays(1);
        return next;
    }

    private sealed record BackupLastFile(bool? Ok, string? DumpFileName, string? Error, DateTime? AtUtc);

    private string ResolveFolder(string folder)
    {
        var path = folder.Trim();
        if (!Path.IsPathRooted(path))
            path = Path.Combine(_env.ContentRootPath, path);
        return Path.GetFullPath(path);
    }

    private string? ResolvePgDump(string? configured)
    {
        var value = (configured ?? "").Trim();
        if (value.Length > 0)
        {
            if (Directory.Exists(value))
            {
                var nested = Path.Combine(value, OperatingSystem.IsWindows() ? "pg_dump.exe" : "pg_dump");
                if (File.Exists(nested))
                    return nested;
            }
            if (File.Exists(value))
                return value;
        }

        if (!_defaults.Value.SearchSystemPgDump)
            return null;

        foreach (var name in new[] { "pg_dump.exe", "pg_dump" })
        {
            var fromPath = FindOnPath(name);
            if (fromPath is not null)
                return fromPath;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        foreach (var version in new[] { "16", "17", "15" })
        {
            var candidate = Path.Combine(programFiles, "PostgreSQL", version, "bin", "pg_dump.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string? ResolvePgRestore(string? pgDump)
    {
        if (!string.IsNullOrWhiteSpace(pgDump))
        {
            var dir = Path.GetDirectoryName(pgDump);
            if (dir is not null)
            {
                var restore = Path.Combine(dir, OperatingSystem.IsWindows() ? "pg_restore.exe" : "pg_restore");
                if (File.Exists(restore))
                    return restore;
            }
        }

        foreach (var name in new[] { "pg_restore.exe", "pg_restore" })
        {
            var fromPath = FindOnPath(name);
            if (fromPath is not null)
                return fromPath;
        }

        return pgDump is null ? null : pgDump.Replace("pg_dump", "pg_restore", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), fileName);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private sealed record Conn(string Host, int Port, string Database, string Username, string? Password);

    private Conn ParseConnection()
    {
        var raw = _config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");
        var builder = new NpgsqlConnectionStringBuilder(raw);
        return new Conn(
            string.IsNullOrWhiteSpace(builder.Host) ? "localhost" : builder.Host,
            builder.Port == 0 ? 5432 : builder.Port,
            builder.Database ?? "cpcredo",
            builder.Username ?? "cpcredo",
            builder.Password);
    }
}
