using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace VideoEditor;

public partial class App : Application
{
    public static string FFmpegPath { get; private set; } = string.Empty;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Global crash handler - shows the exception in a dialog instead of silently closing.
        DispatcherUnhandledException += (s, ex) =>
        {
            MessageBox.Show($"Unhandled exception:\n\n{ex.Exception.Message}\n\n{ex.Exception.StackTrace?.Substring(0, Math.Min(1500, ex.Exception.StackTrace?.Length ?? 0))}",
                "VideoEditor crashed", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
        {
            if (ex.ExceptionObject is Exception exc)
                MessageBox.Show($"Fatal exception:\n\n{exc.Message}\n\n{exc.StackTrace?.Substring(0, Math.Min(1500, exc.StackTrace?.Length ?? 0))}",
                    "VideoEditor fatal", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        base.OnStartup(e);

        var ffmpegDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");
        Directory.CreateDirectory(ffmpegDir);
        FFmpegPath = ffmpegDir;
        FFmpeg.SetExecutablesPath(ffmpegDir);

        // Both files must be present — if a previous run crashed mid-download we'd otherwise
        // skip re-fetching even though one of them is missing/corrupt.
        var ffmpegExe = Path.Combine(ffmpegDir, "ffmpeg.exe");
        var ffprobeExe = Path.Combine(ffmpegDir, "ffprobe.exe");
        bool needFfmpeg = !File.Exists(ffmpegExe);
        bool needFfprobe = !File.Exists(ffprobeExe);
        if (needFfmpeg || needFfprobe)
        {
            try
            {
                // The Xabe official download bundles both ffmpeg.exe and ffprobe.exe.
                await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, ffmpegDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"FFmpeg download failed: {ex.Message}\n\nPlease place ffmpeg.exe AND ffprobe.exe in:\n{ffmpegDir}",
                    "FFmpeg Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
