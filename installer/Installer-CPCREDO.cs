using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var dir = AppDomain.CurrentDomain.BaseDirectory;
        var script = Path.Combine(dir, "Setup-Serveur.ps1");
        if (!File.Exists(script))
        {
            MessageBox.Show(
                "File not found on the USB key: Setup-Serveur.ps1" + Environment.NewLine +
                "Use the complete USB key (CPCREDO-USB folder).",
                "CPCREDO",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        if (!IsAdministrator())
        {
            try
            {
                var self = new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    WorkingDirectory = dir,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(self);
                return;
            }
            catch
            {
                MessageBox.Show(
                    "Ask someone who manages this computer to right-click Installer CPCREDO and choose Run as administrator.",
                    "CPCREDO",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
        }

        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell\\v1.0\\powershell.exe"),
            Arguments = "-NoProfile -ExecutionPolicy Bypass -STA -File \"" + script + "\"",
            WorkingDirectory = dir,
            UseShellExecute = false
        };
        try
        {
            var p = Process.Start(psi);
            if (p != null) p.WaitForExit();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Installation stopped. Run Installer CPCREDO again." + Environment.NewLine + Environment.NewLine +
                "Technical details: " + ex.Message,
                "CPCREDO",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static bool IsAdministrator()
    {
        try
        {
            var id = WindowsIdentity.GetCurrent();
            var p = new WindowsPrincipal(id);
            return p.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
