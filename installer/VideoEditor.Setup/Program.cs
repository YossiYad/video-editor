using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VideoEditor.Setup;

internal static class Program
{
    private const string AppName = "VideoEditor";
    private const string Version = "1.9.0";

    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            var answer = MessageBox.Show(
                $"Install {AppName} v{Version}?\n\nThe app will be installed for the current user.",
                $"{AppName} Setup",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (answer != DialogResult.Yes)
                return 0;

            var installDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                AppName);

            var tempDir = Path.Combine(Path.GetTempPath(), $"{AppName}-setup-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                var zipPath = Path.Combine(tempDir, "payload.zip");
                using (var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
                {
                    if (payload == null)
                        throw new InvalidOperationException("Installer payload is missing.");

                    using var output = File.Create(zipPath);
                    payload.CopyTo(output);
                }

                if (Directory.Exists(installDir))
                    Directory.Delete(installDir, recursive: true);

                Directory.CreateDirectory(installDir);
                ZipFile.ExtractToDirectory(zipPath, installDir, overwriteFiles: true);

                var exePath = Path.Combine(installDir, "VideoEditor.exe");
                if (!File.Exists(exePath))
                    throw new FileNotFoundException("Installed VideoEditor.exe was not found.", exePath);

                CreateShortcut(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "VideoEditor.lnk"),
                    exePath,
                    installDir);

                var startMenuDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    "Programs",
                    AppName);
                Directory.CreateDirectory(startMenuDir);
                CreateShortcut(Path.Combine(startMenuDir, "VideoEditor.lnk"), exePath, installDir);

                MessageBox.Show(
                    $"{AppName} v{Version} installed successfully.",
                    $"{AppName} Setup",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                try
                {
                    Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
                }
                catch
                {
                    // Launch is best-effort; installation already succeeded.
                }

                return 0;
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                $"{AppName} Setup failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new COMException("WScript.Shell is not available.");

        dynamic shell = Activator.CreateInstance(shellType)
            ?? throw new COMException("Could not create WScript.Shell.");

        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.Arguments = "";
        shortcut.WorkingDirectory = workingDirectory;
        shortcut.IconLocation = $"{targetPath},0";
        shortcut.Save();
    }
}
