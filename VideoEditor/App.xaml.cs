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
        base.OnStartup(e);

        var ffmpegDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");
        Directory.CreateDirectory(ffmpegDir);
        FFmpegPath = ffmpegDir;
        FFmpeg.SetExecutablesPath(ffmpegDir);

        if (!File.Exists(Path.Combine(ffmpegDir, "ffmpeg.exe")))
        {
            try
            {
                await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, ffmpegDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"FFmpeg download failed: {ex.Message}\nPlease place ffmpeg.exe and ffprobe.exe in:\n{ffmpegDir}",
                    "FFmpeg Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
