using System;
using System.IO;
using System.Text.Json;

namespace VideoEditor.Services;

/// <summary>
/// App-wide settings persisted to settings.json next to the EXE.
/// Each property has a comment marking how it's wired into the app.
/// </summary>
public static class AppSettings
{
    private static readonly string Path = System.IO.Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "settings.json");

    public static event Action? Changed;

    // ----- General -----
    /// <summary>"auto" | "en" | "he". Wired in App.OnStartup → MainWindow.FlowDirection + status string overrides.</summary>
    public static string Language { get; set; } = "auto";
    /// <summary>"dark" | "light" | "auto". Theme switch requires restart.</summary>
    public static string Theme { get; set; } = "dark";
    /// <summary>"empty" | "restore" | "welcome". On startup behavior.</summary>
    public static string StartupBehavior { get; set; } = "empty";
    /// <summary>If true, ask before delete / overwrite. Wired in destructive op paths.</summary>
    public static bool ConfirmDestructive { get; set; } = true;
    /// <summary>0 = off, otherwise minutes between auto-saves.</summary>
    public static int AutoSaveMinutes { get; set; } = 0;
    public static bool SendUsageStats { get; set; } = false;

    // ----- Player -----
    /// <summary>0-100. Initial master volume slider value. Wired in MainWindow startup.</summary>
    public static int DefaultMasterVolume { get; set; } = 80;
    /// <summary>Seconds for the −5s / +5s buttons. Wired in MainWindow.Back5/Fwd5.</summary>
    public static int BackForwardSeconds { get; set; } = 5;
    /// <summary>"smooth" | "balanced" | "accurate". Maps to MediaElement.ScrubbingEnabled + Pause behavior.</summary>
    public static string ScrubbingQuality { get; set; } = "balanced";
    public static bool AudioScrubbing { get; set; } = true;
    /// <summary>Restart from start when playback hits end.</summary>
    public static bool LoopOnEnd { get; set; } = false;
    /// <summary>"off" | "540p" | "720p" | "1080p" - lower-res proxies for smoother editing (cosmetic for now).</summary>
    public static string ProxyMedia { get; set; } = "off";

    // ----- Editor -----
    /// <summary>true = ripple (clips abut), false = free dragging. Wired in Timeline.ReorderClipToPosition.</summary>
    public static bool RippleMode { get; set; } = false;
    /// <summary>Seconds within which a free-dragged clip snaps to a neighbour's edge.</summary>
    public static double SnapThreshold { get; set; } = 0.5;
    /// <summary>Initial pixels-per-second when the timeline first appears. Wired in Timeline().</summary>
    public static int InitialPixelsPerSecond { get; set; } = 60;
    /// <summary>If false, ClipBar skips waveform generation.</summary>
    public static bool ShowWaveformPerClip { get; set; } = true;
    /// <summary>Number of thumbnail tiles per clip (4/6/8/12/16).</summary>
    public static int ThumbnailsPerClip { get; set; } = 8;
    /// <summary>0-100 - default opacity for new hide blocks.</summary>
    public static int DefaultBlockOpacity { get; set; } = 100;

    // ----- Export -----
    public static int ExportFps { get; set; } = 30;         // 24, 25, 30, 50, 60
    public static int ExportCrf { get; set; } = 20;         // 0-51 (lower = better)
    /// <summary>"libx264" | "libx265" | "libaom-av1" | "prores_ks"</summary>
    public static string ExportCodec { get; set; } = "libx264";
    /// <summary>"mp4" | "mov" | "mkv" | "webm". Wired in MainWindow.SaveBtn_Click (default file dialog ext) + FFmpegService output.</summary>
    public static string ExportContainer { get; set; } = "mp4";
    public static int ExportAudioBitrate { get; set; } = 192;
    /// <summary>"cpu" | "nvenc" | "qsv" | "amf"</summary>
    public static string HardwareAccel { get; set; } = "cpu";
    public static bool TwoPass { get; set; } = false;
    /// <summary>EBU R128 normalization. Wired in FFmpegService.RenderClipAsync.</summary>
    public static bool AudioLoudnorm { get; set; } = true;

    // ----- Storage -----
    public static string DownloadsFolder { get; set; } = "";
    public static string CacheFolder { get; set; } = "";
    /// <summary>"500" | "1g" | "2g" | "5g" | "10g". Max cache size (cosmetic for now).</summary>
    public static string MaxCacheSize { get; set; } = "2g";
    public static bool AutoCleanCache { get; set; } = true;

    private class Data
    {
        public string? Language { get; set; }
        public string? Theme { get; set; }
        public string? StartupBehavior { get; set; }
        public bool ConfirmDestructive { get; set; } = true;
        public int AutoSaveMinutes { get; set; }
        public bool SendUsageStats { get; set; }

        public int DefaultMasterVolume { get; set; } = 80;
        public int BackForwardSeconds { get; set; } = 5;
        public string? ScrubbingQuality { get; set; }
        public bool AudioScrubbing { get; set; } = true;
        public bool LoopOnEnd { get; set; }
        public string? ProxyMedia { get; set; }

        public bool RippleMode { get; set; }
        public double SnapThreshold { get; set; } = 0.5;
        public int InitialPixelsPerSecond { get; set; } = 60;
        public bool ShowWaveformPerClip { get; set; } = true;
        public int ThumbnailsPerClip { get; set; } = 8;
        public int DefaultBlockOpacity { get; set; } = 100;

        public int ExportFps { get; set; } = 30;
        public int ExportCrf { get; set; } = 20;
        public string? ExportCodec { get; set; }
        public string? ExportContainer { get; set; }
        public int ExportAudioBitrate { get; set; } = 192;
        public string? HardwareAccel { get; set; }
        public bool TwoPass { get; set; }
        public bool AudioLoudnorm { get; set; } = true;

        public string? DownloadsFolder { get; set; }
        public string? CacheFolder { get; set; }
        public string? MaxCacheSize { get; set; }
        public bool AutoCleanCache { get; set; } = true;
    }

    static AppSettings() { Load(); }

    public static string? LastLoadError { get; private set; }

    public static void Load()
    {
        try
        {
            if (!File.Exists(Path)) return;
            var d = JsonSerializer.Deserialize<Data>(File.ReadAllText(Path));
            if (d == null) return;
            if (!string.IsNullOrEmpty(d.Language)) Language = d.Language;
            if (!string.IsNullOrEmpty(d.Theme)) Theme = d.Theme;
            if (!string.IsNullOrEmpty(d.StartupBehavior)) StartupBehavior = d.StartupBehavior;
            ConfirmDestructive = d.ConfirmDestructive;
            AutoSaveMinutes = d.AutoSaveMinutes;
            SendUsageStats = d.SendUsageStats;
            DefaultMasterVolume = Clamp(d.DefaultMasterVolume, 0, 100);
            BackForwardSeconds = Clamp(d.BackForwardSeconds, 1, 120);
            if (!string.IsNullOrEmpty(d.ScrubbingQuality)) ScrubbingQuality = d.ScrubbingQuality;
            AudioScrubbing = d.AudioScrubbing;
            LoopOnEnd = d.LoopOnEnd;
            if (!string.IsNullOrEmpty(d.ProxyMedia)) ProxyMedia = d.ProxyMedia;
            RippleMode = d.RippleMode;
            SnapThreshold = Math.Max(0, Math.Min(5, d.SnapThreshold));
            InitialPixelsPerSecond = Clamp(d.InitialPixelsPerSecond, 8, 400);
            ShowWaveformPerClip = d.ShowWaveformPerClip;
            ThumbnailsPerClip = Clamp(d.ThumbnailsPerClip, 1, 32);
            DefaultBlockOpacity = Clamp(d.DefaultBlockOpacity, 0, 100);
            ExportFps = Clamp(d.ExportFps, 1, 240);
            ExportCrf = Clamp(d.ExportCrf, 0, 51);
            if (!string.IsNullOrEmpty(d.ExportCodec)) ExportCodec = d.ExportCodec;
            if (!string.IsNullOrEmpty(d.ExportContainer)) ExportContainer = d.ExportContainer;
            ExportAudioBitrate = Clamp(d.ExportAudioBitrate, 32, 512);
            if (!string.IsNullOrEmpty(d.HardwareAccel)) HardwareAccel = d.HardwareAccel;
            TwoPass = d.TwoPass;
            AudioLoudnorm = d.AudioLoudnorm;
            if (!string.IsNullOrEmpty(d.DownloadsFolder)) DownloadsFolder = d.DownloadsFolder;
            if (!string.IsNullOrEmpty(d.CacheFolder)) CacheFolder = d.CacheFolder;
            if (!string.IsNullOrEmpty(d.MaxCacheSize)) MaxCacheSize = d.MaxCacheSize;
            AutoCleanCache = d.AutoCleanCache;
        }
        catch (Exception ex)
        {
            // Don't crash on a malformed/partial settings file; preserve the error so the UI
            // can surface it instead of silently overwriting on next Save().
            LastLoadError = ex.Message;
        }
    }

    public static string? LastSaveError { get; private set; }

    public static void Save()
    {
        try
        {
            var d = new Data
            {
                Language = Language, Theme = Theme, StartupBehavior = StartupBehavior,
                ConfirmDestructive = ConfirmDestructive, AutoSaveMinutes = AutoSaveMinutes, SendUsageStats = SendUsageStats,
                DefaultMasterVolume = DefaultMasterVolume, BackForwardSeconds = BackForwardSeconds,
                ScrubbingQuality = ScrubbingQuality, AudioScrubbing = AudioScrubbing,
                LoopOnEnd = LoopOnEnd, ProxyMedia = ProxyMedia,
                RippleMode = RippleMode, SnapThreshold = SnapThreshold,
                InitialPixelsPerSecond = InitialPixelsPerSecond,
                ShowWaveformPerClip = ShowWaveformPerClip, ThumbnailsPerClip = ThumbnailsPerClip,
                DefaultBlockOpacity = DefaultBlockOpacity,
                ExportFps = ExportFps, ExportCrf = ExportCrf,
                ExportCodec = ExportCodec, ExportContainer = ExportContainer,
                ExportAudioBitrate = ExportAudioBitrate,
                HardwareAccel = HardwareAccel, TwoPass = TwoPass, AudioLoudnorm = AudioLoudnorm,
                DownloadsFolder = DownloadsFolder, CacheFolder = CacheFolder,
                MaxCacheSize = MaxCacheSize, AutoCleanCache = AutoCleanCache,
            };
            File.WriteAllText(Path, JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
            LastSaveError = null;
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            LastSaveError = ex.Message;
        }
    }

    private static int Clamp(int v, int min, int max) => Math.Max(min, Math.Min(max, v));
}
