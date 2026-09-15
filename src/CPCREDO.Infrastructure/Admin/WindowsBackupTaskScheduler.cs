using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CPCREDO.Application.Admin;
using CPCREDO.Application.Common;

namespace CPCREDO.Infrastructure.Admin;

public sealed class WindowsBackupTaskScheduler : IBackupTaskScheduler
{
    public const string TaskName = "CPCREDO-Backup";

    public BackupTaskSnapshot Query()
    {
        try
        {
            var exe = SchtasksPath();
            if (exe is null)
                return Unavailable();

            var (code, output) = Run(exe, ["/Query", "/TN", TaskName, "/FO", "LIST", "/V"]);
            if (code != 0)
                return new BackupTaskSnapshot(true, false, false, null, null);

            var enabled = ParseEnabled(output);
            return new BackupTaskSnapshot(true, true, enabled, enabled ? ParseNextRun(output) : null, null);
        }
        catch
        {
            return Unavailable();
        }
    }

    public Result<BackupTaskSnapshot> EnsureEnabled(string scriptPath)
    {
        try
        {
            var exe = SchtasksPath();
            if (exe is null)
                return Result<BackupTaskSnapshot>.Fail("backup.scheduler_missing", Unavailable().Error!);

            if (string.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
            {
                return Result<BackupTaskSnapshot>.Fail(
                    "backup.script_missing",
                    "Le script backup.ps1 est introuvable. Relancez INSTALLER-SERVEUR.bat.");
            }

            var tr = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"" + scriptPath + "\"";
            var current = Query();
            if (!current.SchedulerAvailable)
                return Result<BackupTaskSnapshot>.Fail("backup.scheduler_missing", current.Error ?? Unavailable().Error!);

            int code;
            string output;
            if (!current.Exists)
            {
                (code, output) = Run(exe, [
                    "/Create", "/TN", TaskName, "/SC", "DAILY", "/ST", "18:30",
                    "/RL", "HIGHEST", "/RU", "SYSTEM", "/F", "/TR", tr
                ]);
                if (code != 0)
                {
                    return Result<BackupTaskSnapshot>.Fail(
                        "backup.scheduler_failed",
                        "Impossible de créer la tâche planifiée CPCREDO-Backup. " + ShortError(output));
                }
            }
            else
            {
                (code, output) = Run(exe, ["/Change", "/TN", TaskName, "/TR", tr, "/ST", "18:30"]);
                if (code != 0)
                {
                    return Result<BackupTaskSnapshot>.Fail(
                        "backup.scheduler_failed",
                        "Impossible de mettre à jour la tâche planifiée CPCREDO-Backup. " + ShortError(output));
                }
            }

            (code, output) = Run(exe, ["/Change", "/TN", TaskName, "/ENABLE"]);
            if (code != 0)
            {
                return Result<BackupTaskSnapshot>.Fail(
                    "backup.scheduler_failed",
                    "Impossible d’activer la tâche planifiée CPCREDO-Backup. " + ShortError(output));
            }

            var snap = Query();
            if (!snap.Enabled)
            {
                return Result<BackupTaskSnapshot>.Fail(
                    "backup.scheduler_failed",
                    "La tâche planifiée CPCREDO-Backup n’est pas activée.");
            }

            return Result<BackupTaskSnapshot>.Ok(snap);
        }
        catch
        {
            return Result<BackupTaskSnapshot>.Fail("backup.scheduler_missing", Unavailable().Error!);
        }
    }

    public Result<BackupTaskSnapshot> Disable()
    {
        try
        {
            var exe = SchtasksPath();
            if (exe is null)
                return Result<BackupTaskSnapshot>.Fail("backup.scheduler_missing", Unavailable().Error!);

            var current = Query();
            if (!current.Exists)
                return Result<BackupTaskSnapshot>.Ok(new BackupTaskSnapshot(true, false, false, null, null));

            var (code, output) = Run(exe, ["/Change", "/TN", TaskName, "/DISABLE"]);
            if (code != 0)
            {
                return Result<BackupTaskSnapshot>.Fail(
                    "backup.scheduler_failed",
                    "Impossible de désactiver la tâche planifiée CPCREDO-Backup. " + ShortError(output));
            }

            return Result<BackupTaskSnapshot>.Ok(Query());
        }
        catch
        {
            return Result<BackupTaskSnapshot>.Fail("backup.scheduler_missing", Unavailable().Error!);
        }
    }

    private static BackupTaskSnapshot Unavailable() =>
        new(false, false, false, null, "Le Planificateur de tâches est indisponible. La sauvegarde automatique n’a pas été activée.");

    private static string? SchtasksPath()
    {
        var exe = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
        return File.Exists(exe) ? exe : null;
    }

    private static (int Code, string Output) Run(string exe, string[] arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = exe,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in arguments)
            start.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = start };
        if (!process.Start())
            return (-1, "Impossible de démarrer schtasks.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(15_000);
        var combined = (stdout + Environment.NewLine + stderr).Trim();
        return (process.HasExited ? process.ExitCode : -1, combined);
    }

    private static bool ParseEnabled(string output)
    {
        foreach (var raw in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.StartsWith("Scheduled Task State", StringComparison.OrdinalIgnoreCase)
                || line.Contains("tâche planifiée", StringComparison.OrdinalIgnoreCase)
                || ContainsIgnoreDiacritics(line, "tache planifiee"))
            {
                if (ContainsDisabled(line))
                    return false;
                if (ContainsEnabled(line))
                    return true;
            }

            if (line.StartsWith("Status", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Statut", StringComparison.OrdinalIgnoreCase))
            {
                if (ContainsDisabled(line))
                    return false;
            }
        }

        return !ContainsDisabled(output);
    }

    private static DateTime? ParseNextRun(string output)
    {
        foreach (var raw in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            var prefix = line.StartsWith("Next Run Time", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Prochaine", StringComparison.OrdinalIgnoreCase);
            if (!prefix)
                continue;
            var colon = line.IndexOf(':');
            if (colon < 0 || colon + 1 >= line.Length)
                continue;
            var value = line[(colon + 1)..].Trim();
            if (value.Length == 0
                || value.Equals("N/A", StringComparison.OrdinalIgnoreCase)
                || ContainsDisabled(value))
                return null;
            if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var parsed)
                || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed))
                return parsed;
        }

        return null;
    }

    private static bool ContainsDisabled(string text) =>
        text.Contains("Disabled", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Désactivé", StringComparison.OrdinalIgnoreCase)
        || ContainsIgnoreDiacritics(text, "desactive");

    private static bool ContainsEnabled(string text) =>
        text.Contains("Enabled", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Activé", StringComparison.OrdinalIgnoreCase)
        || Regex.IsMatch(text, @"\bActiv", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool ContainsIgnoreDiacritics(string text, string asciiNeedle)
    {
        var normalized = string.Concat(text.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark));
        return normalized.Contains(asciiNeedle, StringComparison.OrdinalIgnoreCase);
    }

    private static string ShortError(string output)
    {
        var line = output.Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0);
        if (string.IsNullOrWhiteSpace(line))
            return "Vérifiez que CPCREDO s’exécute en tant qu’administrateur.";
        return line.Length > 180 ? line[..180] : line;
    }
}
