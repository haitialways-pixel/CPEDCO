using CPCREDO.Application.Common;

namespace CPCREDO.Application.Admin;

public sealed class BackupOptions
{
    public const string SectionName = "Backup";
    public string Folder { get; set; } = "";
    public string PgDumpPath { get; set; } = "";
}

public sealed record BackupSettingsDto(string Folder, string PgDumpPath);

public sealed record BackupFileDto(
    string DumpFileName,
    DateTime CreatedAtUtc,
    long DumpBytes,
    string? KycZipFileName,
    long? KycZipBytes);

public sealed record BackupRunResult(
    string DumpFileName,
    string? KycZipFileName,
    string Folder);

public sealed class RestoreBackupRequest
{
    public string DumpFileName { get; set; } = "";
    public string Password { get; set; } = "";
    public string Confirmation { get; set; } = "";
}

public sealed record RestoreBackupResult(string DumpFileName, bool KycRestored);

public sealed record BackupStatusDto(
    string Folder,
    string PgDumpPath,
    string LastStatus,
    string? LastDumpFileName,
    string? LastError,
    DateTime? LastRunAtUtc,
    DateTime NextRunAtLocal,
    IReadOnlyList<BackupFileDto> Files);

public interface IBackupProcess
{
    Task<(int ExitCode, string StandardError)> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken);
}

public interface IBackupService
{
    Task<Result<BackupSettingsDto>> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<Result<BackupSettingsDto>> SaveSettingsAsync(BackupSettingsDto settings, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<BackupFileDto>>> ListAsync(CancellationToken cancellationToken = default);
    Task<Result<BackupRunResult>> BackupNowAsync(CancellationToken cancellationToken = default);
    Task<Result<RestoreBackupResult>> RestoreAsync(RestoreBackupRequest request, CancellationToken cancellationToken = default);
    Task<Result<BackupStatusDto>> GetStatusAsync(CancellationToken cancellationToken = default);
}
