using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace VideoEditor;

public partial class App : Application
{
    public static string FFmpegPath { get; private set; } = string.Empty;

    public void ApplyThemeResources()
    {
        if (VideoEditor.Services.Theming.IsLight()) ApplyLightTheme();
        else ApplyDarkTheme();

        foreach (Window w in Windows)
        {
            w.InvalidateVisual();
        }
    }

    private void SetBrush(string key, Color color)
    {
        Resources[key] = new SolidColorBrush(color);
    }

    private void SetLinear(string key, Color top, Color bottom)
    {
        var created = new LinearGradientBrush();
        created.StartPoint = new Point(0, 0);
        created.EndPoint = new Point(0, 1);
        created.GradientStops.Add(new GradientStop(top, 0));
        created.GradientStops.Add(new GradientStop(bottom, 1));
        Resources[key] = created;
    }

    private void SetRadial(string key, Color center, Color edge)
    {
        var created = new RadialGradientBrush();
        created.GradientOrigin = new Point(0.5, 0.5);
        created.Center = new Point(0.5, 0.5);
        created.RadiusX = 0.7;
        created.RadiusY = 0.7;
        created.GradientStops.Add(new GradientStop(center, 0));
        created.GradientStops.Add(new GradientStop(edge, 0.85));
        Resources[key] = created;
    }

    private void ApplyDarkTheme()
    {
        SetBrush("Bg0", Color.FromRgb(0x07, 0x08, 0x0D));
        SetBrush("Bg1", Color.FromRgb(0x0F, 0x11, 0x19));
        SetBrush("Bg2", Color.FromRgb(0x16, 0x1A, 0x25));
        SetBrush("Bg3", Color.FromRgb(0x1D, 0x22, 0x31));
        SetBrush("Bg4", Color.FromRgb(0x26, 0x2C, 0x3E));
        SetBrush("BgRaise", Color.FromRgb(0x2D, 0x34, 0x47));
        SetBrush("BgHover", Color.FromRgb(0x1A, 0x1F, 0x2C));
        SetBrush("BgPress", Color.FromRgb(0x25, 0x2B, 0x3D));

        SetBrush("Line", Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF));
        SetBrush("LineStrong", Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
        SetBrush("LineBright", Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));

        SetBrush("Text", Color.FromRgb(0xE8, 0xEA, 0xF2));
        SetBrush("TextMute", Color.FromRgb(0x8A, 0x91, 0xA8));
        SetBrush("TextDim", Color.FromRgb(0x5A, 0x61, 0x78));
        SetBrush("TextBright", Colors.White);
        SetBrush("AccentSoft", Color.FromArgb(0x24, 0x8B, 0x5C, 0xFF));

        SetLinear("TopbarBg", Color.FromRgb(0x13, 0x17, 0x24), Color.FromRgb(0x0E, 0x11, 0x19));
        SetLinear("StatusbarBg", Color.FromRgb(0x10, 0x13, 0x1C), Color.FromRgb(0x0A, 0x0C, 0x12));
        SetLinear("DialogTitlebarBg", Color.FromRgb(0x1A, 0x1E, 0x2B), Color.FromRgb(0x13, 0x17, 0x2A));
        SetRadial("PreviewWrapBg", Color.FromRgb(0x0D, 0x10, 0x19), Color.FromRgb(0x05, 0x06, 0x09));

        SetBrush("PanelBg", Color.FromRgb(0x0F, 0x11, 0x19));
        SetBrush("PanelBgLight", Color.FromRgb(0x1D, 0x22, 0x31));
        SetBrush("TextBrush", Color.FromRgb(0xE8, 0xEA, 0xF2));
        SetBrush("TextBrushMuted", Color.FromRgb(0x8A, 0x91, 0xA8));
        SetBrush("BorderBrush", Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
    }

    // Apply light-theme overrides on top of the dark palette defined in App.xaml.
    // Surfaces become near-white, text becomes dark, borders use black-alpha, accents stay.
    private void ApplyLightTheme()
    {
        SetBrush("Bg0", Color.FromRgb(0xEB, 0xED, 0xF3));
        SetBrush("Bg1", Color.FromRgb(0xF8, 0xF9, 0xFC));
        SetBrush("Bg2", Color.FromRgb(0xF1, 0xF3, 0xF8));
        SetBrush("Bg3", Color.FromRgb(0xE5, 0xE8, 0xEF));
        SetBrush("Bg4", Color.FromRgb(0xD8, 0xDC, 0xE6));
        SetBrush("BgRaise", Color.FromRgb(0xCB, 0xD0, 0xDC));
        SetBrush("BgHover", Color.FromRgb(0xED, 0xEF, 0xF5));
        SetBrush("BgPress", Color.FromRgb(0xDF, 0xE3, 0xEC));

        SetBrush("Line", Color.FromArgb(0x12, 0x00, 0x00, 0x00));
        SetBrush("LineStrong", Color.FromArgb(0x24, 0x00, 0x00, 0x00));
        SetBrush("LineBright", Color.FromArgb(0x40, 0x00, 0x00, 0x00));

        SetBrush("Text", Color.FromRgb(0x1A, 0x1D, 0x24));
        SetBrush("TextMute", Color.FromRgb(0x52, 0x59, 0x6B));
        SetBrush("TextDim", Color.FromRgb(0x7A, 0x82, 0x95));
        SetBrush("TextBright", Colors.Black);
        SetBrush("AccentSoft", Color.FromArgb(0x24, 0x8B, 0x5C, 0xFF));

        SetLinear("TopbarBg", Colors.White, Color.FromRgb(0xEE, 0xF1, 0xF7));
        SetLinear("StatusbarBg", Color.FromRgb(0xF0, 0xF2, 0xF7), Color.FromRgb(0xE5, 0xE8, 0xEF));
        SetLinear("DialogTitlebarBg", Color.FromRgb(0xF8, 0xF9, 0xFC), Color.FromRgb(0xEC, 0xEF, 0xF5));
        SetRadial("PreviewWrapBg", Color.FromRgb(0xE5, 0xE8, 0xF1), Color.FromRgb(0xC9, 0xCE, 0xDA));

        SetBrush("PanelBg", Color.FromRgb(0xF8, 0xF9, 0xFC));
        SetBrush("PanelBgLight", Color.FromRgb(0xE5, 0xE8, 0xEF));
        SetBrush("TextBrush", Color.FromRgb(0x1A, 0x1D, 0x24));
        SetBrush("TextBrushMuted", Color.FromRgb(0x52, 0x59, 0x6B));
        SetBrush("BorderBrush", Color.FromArgb(0x24, 0x00, 0x00, 0x00));
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Apply the theme as early as possible so the main window inherits the right palette.
        try
        {
            ApplyThemeResources();
        }
        catch { /* never block app startup on theme failure */ }

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
