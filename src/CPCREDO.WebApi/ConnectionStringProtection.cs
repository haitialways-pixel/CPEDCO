using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

internal static class ConnectionStringProtection
{
    public const string FileName = "connection.dpapi";

    public static void Apply(ConfigurationManager configuration, string contentRoot)
    {
        var path = Path.Combine(contentRoot, "data", FileName);
        if (!File.Exists(path))
            return;
        if (!OperatingSystem.IsWindows())
            return;
        UnprotectWindows(configuration, path);
    }

    [SupportedOSPlatform("windows")]
    private static void UnprotectWindows(ConfigurationManager configuration, string path)
    {
        try
        {
            var raw = File.ReadAllText(path).Trim();
            if (string.IsNullOrWhiteSpace(raw))
                return;
            var protectedBytes = Convert.FromBase64String(raw);
            var plain = ProtectedData.Unprotect(
                protectedBytes,
                Encoding.UTF8.GetBytes("CPCREDO"),
                DataProtectionScope.LocalMachine);
            var conn = Encoding.UTF8.GetString(plain);
            if (!string.IsNullOrWhiteSpace(conn))
                configuration["ConnectionStrings:Default"] = conn;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Connexion base illisible. Relancez l’installeur CPCREDO. " + ex.Message,
                ex);
        }
    }
}
