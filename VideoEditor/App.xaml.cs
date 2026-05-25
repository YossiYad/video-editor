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

    private ResourceDictionary? _activeThemeDict;

    public void ApplyThemeResources()
    {
        var newDict = VideoEditor.Services.Theming.IsLight() ? BuildLightTheme() : BuildDarkTheme();

        if (_activeThemeDict != null)
        {
            Resources.MergedDictionaries.Remove(_activeThemeDict);
        }
        Resources.MergedDictionaries.Add(newDict);
        _activeThemeDict = newDict;
    }

    private static void AddBrush(ResourceDictionary d, string key, Color color)
    {
        d[key] = new SolidColorBrush(color);
    }

    private static void AddLinear(ResourceDictionary d, string key, Color top, Color bottom)
    {
        var g = new LinearGradientBrush();
        g.StartPoint = new Point(0, 0);
        g.EndPoint = new Point(0, 1);
        g.GradientStops.Add(new GradientStop(top, 0));
        g.GradientStops.Add(new GradientStop(bottom, 1));
        d[key] = g;
    }

    private static void AddRadial(ResourceDictionary d, string key, Color center, Color edge)
    {
        var g = new RadialGradientBrush();
        g.GradientOrigin = new Point(0.5, 0.5);
        g.Center = new Point(0.5, 0.5);
        g.RadiusX = 0.7;
        g.RadiusY = 0.7;
        g.GradientStops.Add(new GradientStop(center, 0));
        g.GradientStops.Add(new GradientStop(edge, 0.85));
        d[key] = g;
    }

    private static ResourceDictionary BuildDarkTheme()
    {
        var d = new ResourceDictionary();
        AddBrush(d, "Bg0", Color.FromRgb(0x07, 0x08, 0x0D));
        AddBrush(d, "Bg1", Color.FromRgb(0x0F, 0x11, 0x19));
        AddBrush(d, "Bg2", Color.FromRgb(0x16, 0x1A, 0x25));
        AddBrush(d, "Bg3", Color.FromRgb(0x1D, 0x22, 0x31));
        AddBrush(d, "Bg4", Color.FromRgb(0x26, 0x2C, 0x3E));
        AddBrush(d, "BgRaise", Color.FromRgb(0x2D, 0x34, 0x47));
        AddBrush(d, "BgHover", Color.FromRgb(0x1A, 0x1F, 0x2C));
        AddBrush(d, "BgPress", Color.FromRgb(0x25, 0x2B, 0x3D));

        AddBrush(d, "Line", Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF));
        AddBrush(d, "LineStrong", Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
        AddBrush(d, "LineBright", Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));

        AddBrush(d, "Text", Color.FromRgb(0xE8, 0xEA, 0xF2));
        AddBrush(d, "TextMute", Color.FromRgb(0x8A, 0x91, 0xA8));
        AddBrush(d, "TextDim", Color.FromRgb(0x5A, 0x61, 0x78));
        AddBrush(d, "TextBright", Colors.White);
        AddBrush(d, "AccentSoft", Color.FromArgb(0x24, 0x8B, 0x5C, 0xFF));

        AddLinear(d, "TopbarBg", Color.FromRgb(0x13, 0x17, 0x24), Color.FromRgb(0x0E, 0x11, 0x19));
        AddLinear(d, "StatusbarBg", Color.FromRgb(0x10, 0x13, 0x1C), Color.FromRgb(0x0A, 0x0C, 0x12));
        AddLinear(d, "DialogTitlebarBg", Color.FromRgb(0x1A, 0x1E, 0x2B), Color.FromRgb(0x13, 0x17, 0x2A));
        AddRadial(d, "PreviewWrapBg", Color.FromRgb(0x0D, 0x10, 0x19), Color.FromRgb(0x05, 0x06, 0x09));

        AddBrush(d, "PanelBg", Color.FromRgb(0x0F, 0x11, 0x19));
        AddBrush(d, "PanelBgLight", Color.FromRgb(0x1D, 0x22, 0x31));
        AddBrush(d, "TextBrush", Color.FromRgb(0xE8, 0xEA, 0xF2));
        AddBrush(d, "TextBrushMuted", Color.FromRgb(0x8A, 0x91, 0xA8));
        AddBrush(d, "BorderBrush", Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
        return d;
    }

    private static ResourceDictionary BuildLightTheme()
    {
        var d = new ResourceDictionary();
        AddBrush(d, "Bg0", Color.FromRgb(0xEB, 0xED, 0xF3));
        AddBrush(d, "Bg1", Color.FromRgb(0xF8, 0xF9, 0xFC));
        AddBrush(d, "Bg2", Color.FromRgb(0xF1, 0xF3, 0xF8));
        AddBrush(d, "Bg3", Color.FromRgb(0xE5, 0xE8, 0xEF));
        AddBrush(d, "Bg4", Color.FromRgb(0xD8, 0xDC, 0xE6));
        AddBrush(d, "BgRaise", Color.FromRgb(0xCB, 0xD0, 0xDC));
        AddBrush(d, "BgHover", Color.FromRgb(0xED, 0xEF, 0xF5));
        AddBrush(d, "BgPress", Color.FromRgb(0xDF, 0xE3, 0xEC));

        AddBrush(d, "Line", Color.FromArgb(0x12, 0x00, 0x00, 0x00));
        AddBrush(d, "LineStrong", Color.FromArgb(0x24, 0x00, 0x00, 0x00));
        AddBrush(d, "LineBright", Color.FromArgb(0x40, 0x00, 0x00, 0x00));

        AddBrush(d, "Text", Color.FromRgb(0x1A, 0x1D, 0x24));
        AddBrush(d, "TextMute", Color.FromRgb(0x52, 0x59, 0x6B));
        AddBrush(d, "TextDim", Color.FromRgb(0x7A, 0x82, 0x95));
        AddBrush(d, "TextBright", Colors.Black);
        AddBrush(d, "AccentSoft", Color.FromArgb(0x24, 0x8B, 0x5C, 0xFF));

        AddLinear(d, "TopbarBg", Colors.White, Color.FromRgb(0xEE, 0xF1, 0xF7));
        AddLinear(d, "StatusbarBg", Color.FromRgb(0xF0, 0xF2, 0xF7), Color.FromRgb(0xE5, 0xE8, 0xEF));
        AddLinear(d, "DialogTitlebarBg", Color.FromRgb(0xF8, 0xF9, 0xFC), Color.FromRgb(0xEC, 0xEF, 0xF5));
        AddRadial(d, "PreviewWrapBg", Color.FromRgb(0xE5, 0xE8, 0xF1), Color.FromRgb(0xC9, 0xCE, 0xDA));

        AddBrush(d, "PanelBg", Color.FromRgb(0xF8, 0xF9, 0xFC));
        AddBrush(d, "PanelBgLight", Color.FromRgb(0xE5, 0xE8, 0xEF));
        AddBrush(d, "TextBrush", Color.FromRgb(0x1A, 0x1D, 0x24));
        AddBrush(d, "TextBrushMuted", Color.FromRgb(0x52, 0x59, 0x6B));
        AddBrush(d, "BorderBrush", Color.FromArgb(0x24, 0x00, 0x00, 0x00));
        return d;
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

        var whisperDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "whisper");
        Directory.CreateDirectory(whisperDir);

        // Both files must be present - if a previous run crashed mid-download we'd otherwise
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
