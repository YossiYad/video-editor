using System;
using System.Runtime.InteropServices;

namespace VideoEditor.Services;

/// <summary>
/// Central place for the handful of things that differ between Windows, macOS and
/// Linux. The app targets all three; this keeps every per-OS decision (executable
/// suffix, runtime identifier, where bundled binaries are downloaded from, fonts
/// directory, reveal-in-folder) in one file instead of scattered hardcoded ".exe"
/// strings.
///
/// Windows behaviour is intentionally identical to before this type existed:
/// <see cref="ExeName"/>("ffmpeg") returns "ffmpeg.exe" on Windows, "ffmpeg"
/// elsewhere.
/// </summary>
public static class Platform
{
    public static bool IsWindows { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static bool IsMacOS   { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    public static bool IsLinux   { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    /// <summary>Append the platform's native executable extension. ".exe" on Windows,
    /// nothing on macOS/Linux.</summary>
    public static string ExeName(string baseName) =>
        IsWindows ? baseName + ".exe" : baseName;

    /// <summary>.NET runtime identifier for the current OS + architecture, e.g.
    /// "win-x64", "osx-arm64", "osx-x64", "linux-x64". Used to pick per-OS download
    /// assets and publish profiles.</summary>
    public static string RuntimeId
    {
        get
        {
            string arch = RuntimeInformation.OSArchitecture switch
            {
                Architecture.Arm64 => "arm64",
                Architecture.X64   => "x64",
                Architecture.X86   => "x86",
                _ => "x64"
            };
            if (IsWindows) return $"win-{arch}";
            if (IsMacOS)   return $"osx-{arch}";
            if (IsLinux)   return $"linux-{arch}";
            return $"unknown-{arch}";
        }
    }

    /// <summary>Latest yt-dlp release asset for this OS. yt-dlp publishes a separate
    /// binary per platform: yt-dlp.exe (Windows), yt-dlp_macos (macOS), yt-dlp (Linux).</summary>
    public static string YtDlpDownloadUrl
    {
        get
        {
            const string baseUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/";
            if (IsWindows) return baseUrl + "yt-dlp.exe";
            if (IsMacOS)   return baseUrl + "yt-dlp_macos";
            return baseUrl + "yt-dlp"; // Linux (and any other *nix)
        }
    }

    /// <summary>
    /// Directories the OS keeps TrueType/OpenType fonts in, most-preferred first. Used by
    /// the ffmpeg drawtext filter so burned-in text has a real font file to point at.
    /// </summary>
    public static string[] FontDirectories
    {
        get
        {
            if (IsWindows)
            {
                var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                return new[] { System.IO.Path.Combine(win, "Fonts") };
            }
            if (IsMacOS)
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return new[]
                {
                    "/System/Library/Fonts",
                    "/Library/Fonts",
                    System.IO.Path.Combine(home, "Library", "Fonts")
                };
            }
            // Linux
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new[]
            {
                "/usr/share/fonts",
                "/usr/local/share/fonts",
                System.IO.Path.Combine(homeDir, ".local", "share", "fonts"),
                System.IO.Path.Combine(homeDir, ".fonts")
            };
        }
    }

    /// <summary>
    /// Opens the OS file manager with <paramref name="path"/> selected (Windows / macOS)
    /// or its containing folder opened (Linux, which has no universal "select" verb).
    /// Best-effort: never throws.
    /// </summary>
    public static void RevealInFileManager(string path)
    {
        try
        {
            if (IsWindows)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            else if (IsMacOS)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "open", $"-R \"{path}\"") { UseShellExecute = false });
            }
            else // Linux
            {
                var dir = System.IO.Directory.Exists(path)
                    ? path
                    : System.IO.Path.GetDirectoryName(path) ?? path;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "xdg-open", $"\"{dir}\"") { UseShellExecute = false });
            }
        }
        catch { /* reveal is a convenience; never crash on it */ }
    }
}
