using System.Diagnostics;
using CPCREDO.Application.Admin;

namespace CPCREDO.Infrastructure.Admin;

public sealed class ProcessBackupRunner : IBackupProcess
{
    public async Task<(int ExitCode, string StandardError)> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in arguments)
            start.ArgumentList.Add(arg);
        foreach (var pair in environment)
            start.Environment[pair.Key] = pair.Value;

        using var process = new Process { StartInfo = start };
        if (!process.Start())
            return (-1, "Impossible de démarrer le processus.");

        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        _ = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, (await stderr).Trim());
    }
}
