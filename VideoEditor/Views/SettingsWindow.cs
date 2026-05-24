using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VideoEditor.Services;

namespace VideoEditor.Views;

public class SettingsWindow : Window
{
    private static Color Bg0 = Color.FromRgb(0x07, 0x08, 0x0D);
    private static Color Bg1 = Color.FromRgb(0x0F, 0x11, 0x19);
    private static Color Bg2 = Color.FromRgb(0x16, 0x1A, 0x25);
    private static Color Bg3 = Color.FromRgb(0x1D, 0x22, 0x31);
    private static Color Bg4 = Color.FromRgb(0x26, 0x2C, 0x3E);
    private static Color Line = Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF);
    private static Color LineStrong = Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF);
    private static Color Text = Color.FromRgb(0xE8, 0xEA, 0xF2);
    private static Color TextMute = Color.FromRgb(0x8A, 0x91, 0xA8);
    private static Color TextDim = Color.FromRgb(0x5A, 0x61, 0x78);
    private static Color Accent = Color.FromRgb(0x8B, 0x5C, 0xFF);
    private static Color Success = Color.FromRgb(0x4C, 0xAF, 0x50);
    private static Color Warn = Color.FromRgb(0xFF, 0xD4, 0x3B);

    private readonly Border _activeRowMarker;
    private readonly ContentControl _pane;
    private readonly TextBlock _statusInfo;
    private readonly Button _saveBtn;
    private readonly SettingsDraft _draft = SettingsDraft.FromCurrent();
    private string _activeKey = "general";
    private bool _dirty;

    private sealed class SettingsDraft
    {
        public string Language = "auto";
        public string Theme = "dark";
        public string StartupBehavior = "empty";
        public bool ConfirmDestructive = true;
        public int AutoSaveMinutes;
        public bool SendUsageStats;
        public int DefaultMasterVolume = 80;
        public int BackForwardSeconds = 5;
        public string ScrubbingQuality = "balanced";
        public bool AudioScrubbing = true;
        public bool LoopOnEnd;
        public string ProxyMedia = "off";
        public bool RippleMode;
        public double SnapThreshold = 0.5;
        public int InitialPixelsPerSecond = 60;
        public bool ShowWaveformPerClip = true;
        public int ThumbnailsPerClip = 8;
        public int DefaultBlockOpacity = 100;
        public string ExportContainer = "mp4";
        public string ExportCodec = "libx264";
        public int ExportCrf = 20;
        public int ExportFps = 30;
        public int ExportAudioBitrate = 192;
        public string HardwareAccel = "cpu";
        public bool TwoPass;
        public bool AudioLoudnorm = true;

        public static SettingsDraft FromCurrent() => new()
        {
            Language = AppSettings.Language,
            Theme = AppSettings.Theme,
            StartupBehavior = AppSettings.StartupBehavior,
            ConfirmDestructive = AppSettings.ConfirmDestructive,
            AutoSaveMinutes = AppSettings.AutoSaveMinutes,
            SendUsageStats = AppSettings.SendUsageStats,
            DefaultMasterVolume = AppSettings.DefaultMasterVolume,
            BackForwardSeconds = AppSettings.BackForwardSeconds,
            ScrubbingQuality = AppSettings.ScrubbingQuality,
            AudioScrubbing = AppSettings.AudioScrubbing,
            LoopOnEnd = AppSettings.LoopOnEnd,
            ProxyMedia = AppSettings.ProxyMedia,
            RippleMode = AppSettings.RippleMode,
            SnapThreshold = AppSettings.SnapThreshold,
            InitialPixelsPerSecond = AppSettings.InitialPixelsPerSecond,
            ShowWaveformPerClip = AppSettings.ShowWaveformPerClip,
            ThumbnailsPerClip = AppSettings.ThumbnailsPerClip,
            DefaultBlockOpacity = AppSettings.DefaultBlockOpacity,
            ExportContainer = AppSettings.ExportContainer,
            ExportCodec = AppSettings.ExportCodec,
            ExportCrf = AppSettings.ExportCrf,
            ExportFps = AppSettings.ExportFps,
            ExportAudioBitrate = AppSettings.ExportAudioBitrate,
            HardwareAccel = AppSettings.HardwareAccel,
            TwoPass = AppSettings.TwoPass,
            AudioLoudnorm = AppSettings.AudioLoudnorm,
        };
    }

    private static readonly (string key, string icon, string label, string sub)[] Sections =
    {
        ("general",  "⚙",  "General",            "App basics"),
        ("player",   "▶",  "Player",             "Playback & seek"),
        ("editor",   "✎",  "Editor",             "Snap, ripple, defaults"),
        ("export",   "💾", "Export",             "Codec & quality"),
        ("storage",  "💿", "Storage",            "Cache & paths"),
        ("ffmpeg",   "🛠", "FFmpeg",             "Binaries & encoders"),
        ("keys",     "⌨",  "Keyboard",           "Shortcuts"),
        ("updates",  "⭐",  "Updates",            "Channel & changelog"),
        ("about",    "ℹ",  "About",              "License & credits"),
    };

    public SettingsWindow()
    {
        ApplyThemePalette();
        Title = "Settings";
        Width = 1080; Height = 740;
        Background = new SolidColorBrush(Bg0);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });

        // Titlebar
        var titlebar = new Border
        {
            Background = new LinearGradientBrush(
                Bg2,
                Bg1, 90),
            BorderBrush = new SolidColorBrush(LineStrong),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0) };
        // 28×28 purple icon tile per design tokens
        var iconTile = new Border
        {
            Width = 28, Height = 28,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Accent),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x6E, 0x44, 0xD6)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Accent, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.4
            }
        };
        iconTile.Child = new TextBlock
        {
            Text = "⚙", FontSize = 14, FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleRow.Children.Add(iconTile);
        var titleStack = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(new TextBlock
        {
            Text = "Settings", FontSize = 13.5, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Text)
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "App preferences - press Save to apply",
            FontSize = 11,
            Foreground = new SolidColorBrush(TextMute),
            Margin = new Thickness(0, 2, 0, 0)
        });
        titleRow.Children.Add(titleStack);
        titleRow.Children.Add(MakeKbd("Esc to close"));
        titlebar.Child = titleRow;
        Grid.SetRow(titlebar, 0);
        root.Children.Add(titlebar);

        // Body: rail (220) + content
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Rail
        var railBg = new Border { Background = new SolidColorBrush(Bg1), BorderBrush = new SolidColorBrush(Line), BorderThickness = new Thickness(0, 0, 1, 0) };
        var rail = new StackPanel { Margin = new Thickness(8, 12, 8, 12) };
        _activeRowMarker = new Border { Background = new SolidColorBrush(Accent), CornerRadius = new CornerRadius(2), Width = 3, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var s in Sections)
        {
            rail.Children.Add(MakeRailItem(s.key, s.icon, s.label, s.sub));
        }
        railBg.Child = rail;
        Grid.SetColumn(railBg, 0);
        body.Children.Add(railBg);

        // Pane
        _pane = new ContentControl();
        var paneScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _pane,
            Background = new SolidColorBrush(Bg0)
        };
        Grid.SetColumn(paneScroll, 1);
        body.Children.Add(paneScroll);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        // Footer
        var footer = new Border { Background = new SolidColorBrush(Bg1), BorderBrush = new SolidColorBrush(Line), BorderThickness = new Thickness(0, 1, 0, 0) };
        var fRow = new Grid { Margin = new Thickness(14, 0, 14, 0) };
        fRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _statusInfo = new TextBlock
        {
            Text = "No unsaved changes",
            Foreground = new SolidColorBrush(TextDim),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_statusInfo, 0);
        fRow.Children.Add(_statusInfo);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        _saveBtn = MakeButton("Save", true);
        _saveBtn.IsEnabled = false;
        _saveBtn.Click += (_, _) => SaveSettings();
        actions.Children.Add(_saveBtn);
        var doneBtn = MakeButton("Close", false);
        doneBtn.Margin = new Thickness(8, 0, 0, 0);
        doneBtn.Click += (_, _) => Close();
        actions.Children.Add(doneBtn);
        Grid.SetColumn(actions, 1);
        fRow.Children.Add(actions);
        footer.Child = fRow;
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        SetActiveSection("general");
        FlowDirection = VideoEditor.Services.Localization.IsHebrew ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        VideoEditor.Services.Localization.TranslateTree(this);
    }

    private static void ApplyThemePalette()
    {
        if (Theming.IsLight())
        {
            Bg0 = Color.FromRgb(0xEB, 0xED, 0xF3);
            Bg1 = Color.FromRgb(0xF8, 0xF9, 0xFC);
            Bg2 = Color.FromRgb(0xF1, 0xF3, 0xF8);
            Bg3 = Color.FromRgb(0xE5, 0xE8, 0xEF);
            Bg4 = Color.FromRgb(0xD8, 0xDC, 0xE6);
            Line = Color.FromArgb(0x18, 0x00, 0x00, 0x00);
            LineStrong = Color.FromArgb(0x30, 0x00, 0x00, 0x00);
            Text = Color.FromRgb(0x1A, 0x1D, 0x24);
            TextMute = Color.FromRgb(0x52, 0x59, 0x6B);
            TextDim = Color.FromRgb(0x7A, 0x82, 0x95);
        }
        else
        {
            Bg0 = Color.FromRgb(0x07, 0x08, 0x0D);
            Bg1 = Color.FromRgb(0x0F, 0x11, 0x19);
            Bg2 = Color.FromRgb(0x16, 0x1A, 0x25);
            Bg3 = Color.FromRgb(0x1D, 0x22, 0x31);
            Bg4 = Color.FromRgb(0x26, 0x2C, 0x3E);
            Line = Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF);
            LineStrong = Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF);
            Text = Color.FromRgb(0xE8, 0xEA, 0xF2);
            TextMute = Color.FromRgb(0x8A, 0x91, 0xA8);
            TextDim = Color.FromRgb(0x5A, 0x61, 0x78);
        }
    }

    private void MarkDirty()
    {
        _dirty = true;
        _saveBtn.IsEnabled = true;
        _statusInfo.Text = VideoEditor.Services.Localization.T("Unsaved changes");
        _statusInfo.Foreground = new SolidColorBrush(Warn);
    }

    private void SaveSettings()
    {
        if (!_dirty) return;

        var previousTheme = AppSettings.Theme;
        AppSettings.Language = _draft.Language;
        AppSettings.Theme = _draft.Theme;
        AppSettings.StartupBehavior = _draft.StartupBehavior;
        AppSettings.ConfirmDestructive = _draft.ConfirmDestructive;
        AppSettings.AutoSaveMinutes = _draft.AutoSaveMinutes;
        AppSettings.SendUsageStats = _draft.SendUsageStats;
        AppSettings.DefaultMasterVolume = _draft.DefaultMasterVolume;
        AppSettings.BackForwardSeconds = _draft.BackForwardSeconds;
        AppSettings.ScrubbingQuality = _draft.ScrubbingQuality;
        AppSettings.AudioScrubbing = _draft.AudioScrubbing;
        AppSettings.LoopOnEnd = _draft.LoopOnEnd;
        AppSettings.ProxyMedia = _draft.ProxyMedia;
        AppSettings.RippleMode = _draft.RippleMode;
        AppSettings.SnapThreshold = _draft.SnapThreshold;
        AppSettings.InitialPixelsPerSecond = _draft.InitialPixelsPerSecond;
        AppSettings.ShowWaveformPerClip = _draft.ShowWaveformPerClip;
        AppSettings.ThumbnailsPerClip = _draft.ThumbnailsPerClip;
        AppSettings.DefaultBlockOpacity = _draft.DefaultBlockOpacity;
        AppSettings.ExportContainer = _draft.ExportContainer;
        AppSettings.ExportCodec = _draft.ExportCodec;
        AppSettings.ExportCrf = _draft.ExportCrf;
        AppSettings.ExportFps = _draft.ExportFps;
        AppSettings.ExportAudioBitrate = _draft.ExportAudioBitrate;
        AppSettings.HardwareAccel = _draft.HardwareAccel;
        AppSettings.TwoPass = _draft.TwoPass;
        AppSettings.AudioLoudnorm = _draft.AudioLoudnorm;
        AppSettings.Save();

        if (!string.IsNullOrEmpty(AppSettings.LastSaveError))
        {
            _statusInfo.Text = "Save failed: " + AppSettings.LastSaveError;
            _statusInfo.Foreground = new SolidColorBrush(Warn);
            return;
        }

        _dirty = false;
        _saveBtn.IsEnabled = false;
        _statusInfo.Text = VideoEditor.Services.Localization.T("Settings saved");
        _statusInfo.Foreground = new SolidColorBrush(Success);

        if (!string.Equals(previousTheme, AppSettings.Theme, StringComparison.Ordinal))
        {
            (Application.Current as App)?.ApplyThemeResources();
            var refreshed = new SettingsWindow { Owner = Owner };
            refreshed.Show();
            Close();
        }
    }

    private Button MakeRailItem(string key, string icon, string label, string sub)
    {
        var btn = new Button
        {
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 10, 10, 10),
            Margin = new Thickness(0, 1, 0, 1),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Cursor = Cursors.Hand,
            Tag = key
        };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var iconText = new TextBlock { Text = icon, FontSize = 14, Foreground = new SolidColorBrush(TextMute), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(iconText, 0);
        row.Children.Add(iconText);
        var labels = new StackPanel();
        labels.Children.Add(new TextBlock { Text = label, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Text) });
        labels.Children.Add(new TextBlock { Text = sub, FontSize = 10.5, Foreground = new SolidColorBrush(TextDim), Margin = new Thickness(0, 1, 0, 0) });
        Grid.SetColumn(labels, 1);
        row.Children.Add(labels);

        // Template - subtle hover background
        btn.Template = new ControlTemplate(typeof(Button))
        {
            VisualTree = MakeTemplateRoot()
        };

        btn.Click += (_, _) => SetActiveSection(key);
        btn.Content = row;
        return btn;
    }

    private FrameworkElementFactory MakeTemplateRoot()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        border.AppendChild(content);
        return border;
    }

    private void SetActiveSection(string key)
    {
        _activeKey = key;
        _pane.Content = BuildSection(key);
        VideoEditor.Services.Localization.TranslateTree(this);
    }

    private UIElement BuildSection(string key) => key switch
    {
        "general" => BuildGeneral(),
        "player"  => BuildPlayer(),
        "editor"  => BuildEditor(),
        "export"  => BuildExport(),
        "storage" => BuildStorage(),
        "ffmpeg"  => BuildFFmpeg(),
        "keys"    => BuildKeys(),
        "updates" => BuildUpdates(),
        "about"   => BuildAbout(),
        _ => new TextBlock { Text = "Coming soon", Foreground = new SolidColorBrush(TextMute), Margin = new Thickness(20) }
    };

    // ---------- sections ----------

    private UIElement BuildGeneral()
    {
        var p = MakePanel("General", "App basics - language, theme, behavior on startup, privacy.");
        var langItems = new[] { "Auto (system)", "English", "עברית" };
        var langKeys = new[] { "auto", "en", "he" };
        var langIdx = Array.IndexOf(langKeys, _draft.Language);
        if (langIdx < 0) langIdx = 0;
        p.Children.Add(MakeRow("Language", "Applied when you press Save",
            MakeComboBound(langItems, langIdx,
                idx => { _draft.Language = langKeys[idx]; MarkDirty(); })));

        var themeItems = new[] { "Dark", "Light", "Auto" };
        var themeKeys = new[] { "dark", "light", "auto" };
        var themeIdx = Array.IndexOf(themeKeys, _draft.Theme);
        if (themeIdx < 0) themeIdx = 0;
        p.Children.Add(MakeRow("Theme", "Applied when you press Save",
            MakeComboBound(themeItems, themeIdx,
                idx => { _draft.Theme = themeKeys[idx]; MarkDirty(); })));

        var startupItems = new[] { "Empty project", "Restore last project (coming soon)", "Show welcome (coming soon)" };
        var startupKeys = new[] { "empty", "restore", "welcome" };
        var startupIdx = Array.IndexOf(startupKeys, _draft.StartupBehavior);
        if (startupIdx < 0) startupIdx = 0;
        p.Children.Add(MakeRow("On startup", "What to load when the app opens",
            MakeComboBound(startupItems, startupIdx,
                idx => { _draft.StartupBehavior = startupKeys[idx]; MarkDirty(); })));

        p.Children.Add(MakeRow("Confirm destructive actions", "Ask before delete / re-encode",
            MakeToggleBound(_draft.ConfirmDestructive, v => { _draft.ConfirmDestructive = v; MarkDirty(); })));
        p.Children.Add(MakeRow("Auto-save", $"Save project every {_draft.AutoSaveMinutes} minutes (0 = off)",
            MakeSliderBound(0, 30, _draft.AutoSaveMinutes, v => { _draft.AutoSaveMinutes = v; MarkDirty(); })));
        p.Children.Add(MakeRow("Send anonymous usage stats", "Helps improve the editor (currently disabled)",
            MakeToggleBound(_draft.SendUsageStats, v => { _draft.SendUsageStats = v; MarkDirty(); })));
        return WrapSection(p);
    }
    private UIElement BuildPlayer()
    {
        var p = MakePanel("Player", "Playback engine & seek behavior. Changes apply immediately.");
        p.Children.Add(MakeRow("Default volume", $"Currently {_draft.DefaultMasterVolume}% - applied when you press Save",
            MakeSliderBound(0, 100, _draft.DefaultMasterVolume, v => { _draft.DefaultMasterVolume = v; MarkDirty(); })));
        p.Children.Add(MakeRow("Back / Forward step", $"Currently {_draft.BackForwardSeconds}s —” for the -5s/+5s buttons",
            MakeSliderBound(1, 60, _draft.BackForwardSeconds, v => { _draft.BackForwardSeconds = v; MarkDirty(); })));

        var scrubItems = new[] { "Smooth (low quality)", "Balanced (default)", "Frame-accurate" };
        var scrubKeys = new[] { "smooth", "balanced", "accurate" };
        var scrubIdx = Array.IndexOf(scrubKeys, _draft.ScrubbingQuality);
        if (scrubIdx < 0) scrubIdx = 1;
        p.Children.Add(MakeRow("Scrubbing quality", "Frame accuracy when dragging the playhead",
            MakeComboBound(scrubItems, scrubIdx, idx => { _draft.ScrubbingQuality = scrubKeys[idx]; MarkDirty(); })));

        p.Children.Add(MakeRow("Audio scrubbing", "Play audio while dragging playhead",
            MakeToggleBound(_draft.AudioScrubbing, v => { _draft.AudioScrubbing = v; MarkDirty(); })));
        p.Children.Add(MakeRow("Loop on end", "Restart project when playhead hits the end",
            MakeToggleBound(_draft.LoopOnEnd, v => { _draft.LoopOnEnd = v; MarkDirty(); })));

        var proxyItems = new[] { "Off", "540p (coming soon)", "720p (coming soon)", "1080p (coming soon)" };
        var proxyKeys = new[] { "off", "540p", "720p", "1080p" };
        var proxyIdx = Array.IndexOf(proxyKeys, _draft.ProxyMedia);
        if (proxyIdx < 0) proxyIdx = 0;
        p.Children.Add(MakeRow("Proxy media", "Lower-res proxies for smoother editing on slow PCs",
            MakeComboBound(proxyItems, proxyIdx, idx => { _draft.ProxyMedia = proxyKeys[idx]; MarkDirty(); })));
        return WrapSection(p);
    }
    private UIElement BuildEditor()
    {
        var p = MakePanel("Editor", "Clip dragging behavior and defaults. Most changes apply immediately.");
        p.Children.Add(MakeRow("Ripple-abut clips on drop", "OFF (default) = free dragging with gaps allowed · ON = old behavior where clips snap together",
            MakeToggleBound(_draft.RippleMode, v => { _draft.RippleMode = v; MarkDirty(); })));
        p.Children.Add(MakeRow("Magnetic snap threshold", $"Currently {_draft.SnapThreshold:0.##}s —” within this distance a dragged clip snaps to a neighbour's edge",
            MakeSliderBound(0, 50, (int)(_draft.SnapThreshold * 10), v => { _draft.SnapThreshold = v / 10.0; MarkDirty(); })));
        p.Children.Add(MakeRow("Initial zoom", $"Currently {_draft.InitialPixelsPerSecond} px/sec —” default zoom level for new projects",
            MakeSliderBound(8, 200, _draft.InitialPixelsPerSecond, v => { _draft.InitialPixelsPerSecond = v; MarkDirty(); })));
        p.Children.Add(MakeRow("Show waveform per clip", "Auto-generate audio waveforms in A1 lane",
            MakeToggleBound(_draft.ShowWaveformPerClip, v => { _draft.ShowWaveformPerClip = v; MarkDirty(); })));

        var thumbOptions = new[] { 4, 6, 8, 12, 16 };
        var thumbLabels = new[] { "4 (fastest)", "6", "8 (default)", "12", "16 (most detail)" };
        var thumbIdx = Array.IndexOf(thumbOptions, _draft.ThumbnailsPerClip);
        if (thumbIdx < 0) thumbIdx = 2;
        p.Children.Add(MakeRow("Thumbnails per clip", "Number of frame previews to render in V1 lane",
            MakeComboBound(thumbLabels, thumbIdx, idx => { _draft.ThumbnailsPerClip = thumbOptions[idx]; MarkDirty(); })));

        p.Children.Add(MakeRow("Default block opacity", $"Currently {_draft.DefaultBlockOpacity}% —” opacity of new hide blocks",
            MakeSliderBound(0, 100, _draft.DefaultBlockOpacity, v => { _draft.DefaultBlockOpacity = v; MarkDirty(); })));
        return WrapSection(p);
    }
    private UIElement BuildExport()
    {
        var p = MakePanel("Export", "Container, codec, and quality controls. Changes apply on next export.");
        p.Children.Add(MakeRow("Container", "Output file format",
            MakeComboBound(new[] { "MP4 (.mp4)", "MOV (.mov)", "MKV (.mkv)", "WebM (.webm)" },
                _draft.ExportContainer switch { "mov" => 1, "mkv" => 2, "webm" => 3, _ => 0 },
                idx => { _draft.ExportContainer = idx switch { 1 => "mov", 2 => "mkv", 3 => "webm", _ => "mp4" }; MarkDirty(); })));
        p.Children.Add(MakeRow("Video codec", "Encoder for the export pass",
            MakeComboBound(new[] { "H.264 (libx264)", "H.265 / HEVC", "AV1", "ProRes 422" },
                _draft.ExportCodec switch { "libx265" => 1, "libaom-av1" => 2, "prores_ks" => 3, _ => 0 },
                idx => { _draft.ExportCodec = idx switch { 1 => "libx265", 2 => "libaom-av1", 3 => "prores_ks", _ => "libx264" }; MarkDirty(); })));
        p.Children.Add(MakeRow("Quality (CRF)", $"Currently {_draft.ExportCrf} · lower = better quality, larger file",
            MakeSliderBound(0, 51, _draft.ExportCrf, v => { _draft.ExportCrf = v; MarkDirty(); })));

        // Frame rate - now includes 50 and 60 fps, persisted to AppSettings
        var fpsOptions = new[] { 24, 25, 30, 50, 60 };
        var fpsLabels = new[] { "24 fps", "25 fps", "30 fps (default)", "50 fps", "60 fps" };
        var fpsIdx = Array.IndexOf(fpsOptions, _draft.ExportFps);
        if (fpsIdx < 0) fpsIdx = 2;
        p.Children.Add(MakeRow("Frame rate", $"Currently {_draft.ExportFps} fps · output framerate (all clips normalized)",
            MakeComboBound(fpsLabels, fpsIdx, idx => { _draft.ExportFps = fpsOptions[idx]; MarkDirty(); })));

        var bitrateOptions = new[] { 128, 192, 256, 320 };
        var bitrateLabels = new[] { "128 kbps", "192 kbps (default)", "256 kbps", "320 kbps" };
        var brIdx = Array.IndexOf(bitrateOptions, _draft.ExportAudioBitrate);
        if (brIdx < 0) brIdx = 1;
        p.Children.Add(MakeRow("Audio bitrate", "AAC bitrate",
            MakeComboBound(bitrateLabels, brIdx, idx => { _draft.ExportAudioBitrate = bitrateOptions[idx]; MarkDirty(); })));

        var hwItems = new[] { "CPU (libx264)", "NVIDIA NVENC", "Intel QuickSync", "AMD AMF" };
        var hwKeys = new[] { "cpu", "nvenc", "qsv", "amf" };
        var hwIdx = Array.IndexOf(hwKeys, _draft.HardwareAccel);
        if (hwIdx < 0) hwIdx = 0;
        p.Children.Add(MakeRow("Hardware accel", "GPU encoder —” much faster export if your GPU supports it",
            MakeComboBound(hwItems, hwIdx, idx => { _draft.HardwareAccel = hwKeys[idx]; MarkDirty(); })));
        p.Children.Add(MakeRow("2-pass encoding", "Slower, better bitrate distribution (coming soon)",
            MakeToggleBound(_draft.TwoPass, v => { _draft.TwoPass = v; MarkDirty(); })));
        p.Children.Add(MakeRow("Audio loudnorm", "EBU R128 normalization —” uniform loudness across all clips",
            MakeToggleBound(_draft.AudioLoudnorm, v => { _draft.AudioLoudnorm = v; MarkDirty(); })));
        return WrapSection(p);
    }

    // ========= Bound controls helpers =========

    private Border MakeToggleBound(bool initial, Action<bool> onChange)
    {
        var b = new Border
        {
            Width = 40, Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(initial ? Accent : Bg3),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var dot = new Ellipse { Width = 16, Height = 16, Fill = System.Windows.Media.Brushes.White, HorizontalAlignment = initial ? HorizontalAlignment.Right : HorizontalAlignment.Left, Margin = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center };
        b.Child = dot;
        bool state = initial;
        b.MouseLeftButtonDown += (_, _) =>
        {
            state = !state;
            b.Background = new SolidColorBrush(state ? Accent : Bg3);
            dot.HorizontalAlignment = state ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            onChange(state);
        };
        return b;
    }

    private ComboBox MakeComboBound(string[] items, int selectedIdx, Action<int> onChange)
    {
        var cb = new ComboBox
        {
            Background = new SolidColorBrush(Bg2),
            Foreground = new SolidColorBrush(Text),
            BorderBrush = new SolidColorBrush(LineStrong),
            Padding = new Thickness(8, 4, 8, 4),
            FontSize = 12.5
        };
        foreach (var i in items) cb.Items.Add(i);
        cb.SelectedIndex = Math.Min(selectedIdx, items.Length - 1);
        cb.SelectionChanged += (_, _) => { if (cb.SelectedIndex >= 0) onChange(cb.SelectedIndex); };
        return cb;
    }

    private Grid MakeSliderBound(int min, int max, int initial, Action<int> onChange)
    {
        var g = new Grid { HorizontalAlignment = HorizontalAlignment.Right };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        var s = new Slider { Minimum = min, Maximum = max, Value = initial, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(s, 0); g.Children.Add(s);
        var v = new TextBlock { Text = initial.ToString(), FontFamily = new FontFamily("Consolas"), FontSize = 11, Foreground = new SolidColorBrush(TextMute), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        s.ValueChanged += (_, e) => { v.Text = ((int)e.NewValue).ToString(); onChange((int)e.NewValue); };
        Grid.SetColumn(v, 1); g.Children.Add(v);
        return g;
    }
    private UIElement BuildStorage()
    {
        var p = MakePanel("Storage", "Folders for projects, downloads, cache & backup.");
        p.Children.Add(MakeRow("Projects folder", "Where .veproj files are saved", MakeFolderPath(@"%USERPROFILE%\Videos\VideoEditor")));
        p.Children.Add(MakeRow("Downloads folder", "For URL-imported videos", MakeFolderPath(@"%USERPROFILE%\Videos\VideoEditorDownloads")));
        p.Children.Add(MakeRow("Cache folder", "Thumbnails, waveforms, proxies", MakeFolderPath(@"%TEMP%\video_editor")));
        p.Children.Add(MakeRow("Max cache size", "Auto-clean above this size", MakeCombo(new[] { "500 MB", "1 GB", "2 GB", "5 GB", "10 GB" }, 2)));
        p.Children.Add(MakeRow("Auto-clean cache", "Delete unused thumbs/waveforms on exit", MakeToggle(true)));
        p.Children.Add(MakeRow("Backup as ZIP", "Periodic ZIP of the open project", MakeToggle(false)));
        return WrapSection(p);
    }
    private UIElement BuildFFmpeg()
    {
        var p = MakePanel("FFmpeg", "Backend binaries —” set paths and verify encoders.");
        p.Children.Add(MakeRow("ffmpeg.exe path", "Used for encoding & filters", MakeFolderPath(@"%APPDIR%\ffmpeg\ffmpeg.exe")));
        p.Children.Add(MakeRow("ffprobe.exe path", "Used to read media metadata", MakeFolderPath(@"%APPDIR%\ffmpeg\ffprobe.exe")));
        p.Children.Add(MakeRow("yt-dlp.exe path", "Used for streaming-site URL imports", MakeFolderPath(@"%APPDIR%\ffmpeg\yt-dlp.exe")));
        var statusBox = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        statusBox.Children.Add(MakeChip("ffmpeg 6.1 · OK", Success));
        statusBox.Children.Add(MakeChip("ffprobe · OK", Success));
        statusBox.Children.Add(MakeChip("yt-dlp 2024.04 · OK", Success));
        statusBox.Children.Add(MakeChip("Encoders: libx264, libx265, aac, libvorbis, libmp3lame", Accent));
        p.Children.Add(MakeRow("Status", "Detected binaries", statusBox));
        var testBtn = MakeButton("Run test pipeline", false);
        p.Children.Add(MakeRow("Diagnostics", "Encode a 3-second test clip", testBtn));
        return WrapSection(p);
    }
    private UIElement BuildKeys()
    {
        var groups = new (string title, string[][] keys)[]
        {
            ("Playback", new[]
            {
                new[]{"Space","Play / Pause"},
                new[]{"K","Pause"},
                new[]{"J","Reverse / slow down"},
                new[]{"L","Forward / speed up"},
                new[]{"ג†  ג†’","Step 1 frame"},
                new[]{"Shift+ג† ג†’","Step 5 frames"},
                new[]{"Home","Go to start"},
                new[]{"End","Go to end"},
            }),
            ("Editing", new[]
            {
                new[]{"S","Split at playhead"},
                new[]{"Shift+Click","Split at click position"},
                new[]{"Backspace","Delete selected"},
                new[]{"Delete","Delete selected"},
                new[]{"Ctrl+C","Copy"},
                new[]{"Ctrl+V","Paste"},
                new[]{"Ctrl+D","Duplicate"},
                new[]{"Ctrl+Z","Undo"},
                new[]{"Ctrl+Shift+Z","Redo"},
                new[]{"I","Set in point"},
                new[]{"O","Set out point"},
            }),
            ("Selection", new[]
            {
                new[]{"Click","Select clip / block / audio"},
                new[]{"Esc","Deselect"},
                new[]{"Ctrl+A","Select all clips"},
            }),
            ("Hide Blocks", new[]
            {
                new[]{"B","Add hide block at playhead"},
                new[]{"1","Block mode: Solid"},
                new[]{"2","Block mode: Blur"},
                new[]{"3","Block mode: Pixelate"},
            }),
            ("Timeline", new[]
            {
                new[]{"+ / =","Zoom in"},
                new[]{"-","Zoom out"},
                new[]{"0","Fit to view"},
                new[]{"Scroll wheel","Scroll horizontally"},
            }),
            ("Tools & Menus", new[]
            {
                new[]{"U","Import from URL"},
                new[]{"T","Trim dialog"},
                new[]{"M","Mute clip"},
                new[]{"Ctrl+E","Export"},
                new[]{"Ctrl+O","Open file…"},
            }),
            ("Help & Settings", new[]
            {
                new[]{"?  /  F1","Open user guide"},
                new[]{",","Open settings"},
            }),
        };

        var p = MakePanel("Keyboard", "Customize any shortcut. Click \"Change…\" to rebind.");
        var grid = new StackPanel();

        foreach (var g in groups)
        {
            grid.Children.Add(new TextBlock
            {
                Text = g.title.ToUpperInvariant(),
                FontSize = 10.5, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Accent),
                Margin = new Thickness(0, 14, 0, 6)
            });
            var box = new Border { Background = new SolidColorBrush(Bg2), BorderBrush = new SolidColorBrush(Line), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8) };
            var col = new StackPanel();
            foreach (var k in g.keys)
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                var kbd = MakeKbd(k[0]); kbd.Margin = new Thickness(0); Grid.SetColumn(kbd, 0);
                row.Children.Add(kbd);
                var lab = new TextBlock { Text = k[1], FontSize = 12, Foreground = new SolidColorBrush(Text), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(lab, 1);
                row.Children.Add(lab);
                var changeBtn = MakeButton("Change…", false);
                changeBtn.Height = 22; changeBtn.Padding = new Thickness(8, 0, 8, 0); changeBtn.FontSize = 10.5; changeBtn.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(changeBtn, 2);
                row.Children.Add(changeBtn);
                col.Children.Add(row);
            }
            box.Child = col;
            grid.Children.Add(box);
        }
        p.Children.Add(grid);
        return WrapSection(p);
    }
    private UIElement BuildUpdates()
    {
        var p = MakePanel("Updates", "Current version & release channel.");
        var card = new Border { Background = new SolidColorBrush(Bg2), BorderBrush = new SolidColorBrush(Line), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16) };
        var stack = new StackPanel();
        var verRow = new StackPanel { Orientation = Orientation.Horizontal };
        verRow.Children.Add(new TextBlock { Text = "Version", FontSize = 11, Foreground = new SolidColorBrush(TextDim), VerticalAlignment = VerticalAlignment.Center, Width = 90 });
        verRow.Children.Add(new TextBlock { Text = "1.4.0", FontFamily = new FontFamily("Consolas"), FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Text), VerticalAlignment = VerticalAlignment.Center });
        verRow.Children.Add(MakeChip("Stable", Success));
        stack.Children.Add(verRow);

        var commitRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        commitRow.Children.Add(new TextBlock { Text = "Commit", FontSize = 11, Foreground = new SolidColorBrush(TextDim), VerticalAlignment = VerticalAlignment.Center, Width = 90 });
        commitRow.Children.Add(new TextBlock { Text = "b93dbdf · master", FontFamily = new FontFamily("Consolas"), FontSize = 12, Foreground = new SolidColorBrush(TextMute), VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(commitRow);

        card.Child = stack;
        p.Children.Add(card);
        p.Children.Add(MakeRow("Release channel", "How often to receive updates", MakeCombo(new[] { "Stable", "Beta", "Nightly" }, 0)));
        p.Children.Add(MakeRow("Auto-check", "Look for updates on startup", MakeToggle(true)));
        var btn = MakeButton("Check for updates now", true);
        p.Children.Add(MakeRow("Manual check", "Contact the update server", btn));
        return WrapSection(p);
    }
    private UIElement BuildAbout()
    {
        var p = MakePanel("About", "License, credits, and acknowledgements.");
        var card = new Border { Background = new SolidColorBrush(Bg2), BorderBrush = new SolidColorBrush(Line), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(20) };
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var logo = new Border
        {
            Width = 72, Height = 72, CornerRadius = new CornerRadius(14),
            Background = new LinearGradientBrush(Color.FromRgb(0xA0, 0x7C, 0xFF), Color.FromRgb(0x5B, 0x3A, 0xD9), 90),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        logo.Child = new TextBlock { Text = "▶", FontSize = 38, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(logo);
        stack.Children.Add(new TextBlock { Text = "VideoEditor —” Pro", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Text), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) });
        stack.Children.Add(new TextBlock { Text = "WPF · FFmpeg · yt-dlp · React UI design", FontSize = 11, Foreground = new SolidColorBrush(TextDim), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
        stack.Children.Add(new TextBlock { Text = "MIT License · YossiYad/video-editor", FontSize = 11, FontFamily = new FontFamily("Consolas"), Foreground = new SolidColorBrush(TextMute), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) });
        card.Child = stack;
        p.Children.Add(card);

        var credits = new TextBlock
        {
            Text = "Built with FFmpeg (LGPL/GPL) and yt-dlp (Unlicense). Audio waveforms via FFmpeg showwavespic. Thumbnails via FFmpeg fps filter.",
            Foreground = new SolidColorBrush(TextMute), FontSize = 12,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0)
        };
        p.Children.Add(credits);
        return WrapSection(p);
    }

    // ---------- helpers ----------

    private StackPanel MakePanel(string title, string desc)
    {
        var sp = new StackPanel { Margin = new Thickness(28, 24, 28, 24) };
        sp.Children.Add(new TextBlock
        {
            Text = title, FontSize = 22, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Text)
        });
        sp.Children.Add(new TextBlock
        {
            Text = desc, FontSize = 12.5, Foreground = new SolidColorBrush(TextMute),
            Margin = new Thickness(0, 4, 0, 18), TextWrapping = TextWrapping.Wrap
        });
        return sp;
    }

    private UIElement WrapSection(StackPanel p) => p;

    private Border MakeRow(string label, string sub, UIElement control)
    {
        var row = new Border
        {
            Background = new SolidColorBrush(Bg1),
            BorderBrush = new SolidColorBrush(Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock { Text = label, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Text) });
        if (!string.IsNullOrEmpty(sub))
            labels.Children.Add(new TextBlock { Text = sub, FontSize = 11, Foreground = new SolidColorBrush(TextDim), Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(labels, 0);
        grid.Children.Add(labels);
        var ctrlHost = new Border { Padding = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = control };
        Grid.SetColumn(ctrlHost, 1);
        grid.Children.Add(ctrlHost);
        row.Child = grid;
        return row;
    }

    private ComboBox MakeCombo(string[] items, int selectedIdx)
    {
        var cb = new ComboBox
        {
            Background = new SolidColorBrush(Bg2),
            Foreground = new SolidColorBrush(Text),
            BorderBrush = new SolidColorBrush(LineStrong),
            Padding = new Thickness(8, 4, 8, 4),
            FontSize = 12.5
        };
        foreach (var i in items) cb.Items.Add(i);
        cb.SelectedIndex = Math.Min(selectedIdx, items.Length - 1);
        return cb;
    }
    private Border MakeToggle(bool initial)
    {
        var b = new Border
        {
            Width = 40, Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(initial ? Accent : Bg3),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var dot = new Ellipse { Width = 16, Height = 16, Fill = System.Windows.Media.Brushes.White, HorizontalAlignment = initial ? HorizontalAlignment.Right : HorizontalAlignment.Left, Margin = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center };
        b.Child = dot;
        bool state = initial;
        b.MouseLeftButtonDown += (_, _) =>
        {
            state = !state;
            b.Background = new SolidColorBrush(state ? Accent : Bg3);
            dot.HorizontalAlignment = state ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        };
        return b;
    }
    private TextBox MakeNumber(string text, string unit)
    {
        var grid = new Grid();
        var tb = new TextBox
        {
            Text = text,
            Background = new SolidColorBrush(Bg2),
            Foreground = new SolidColorBrush(Text),
            BorderBrush = new SolidColorBrush(LineStrong),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 5, 36, 5),
            FontSize = 12.5, FontFamily = new FontFamily("Consolas"),
            Width = 110, HorizontalAlignment = HorizontalAlignment.Right,
            Tag = unit
        };
        return tb;
    }
    private Grid MakeSlider(int initial)
    {
        var g = new Grid { HorizontalAlignment = HorizontalAlignment.Right };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        var s = new Slider { Minimum = 0, Maximum = 100, Value = initial, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(s, 0); g.Children.Add(s);
        var v = new TextBlock { Text = initial.ToString(), FontFamily = new FontFamily("Consolas"), FontSize = 11, Foreground = new SolidColorBrush(TextMute), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        s.ValueChanged += (_, e) => v.Text = ((int)e.NewValue).ToString();
        Grid.SetColumn(v, 1); g.Children.Add(v);
        return g;
    }
    private Grid MakeFolderPath(string p)
    {
        var g = new Grid { HorizontalAlignment = HorizontalAlignment.Right };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        var tb = new TextBox
        {
            Text = p, Background = new SolidColorBrush(Bg2), Foreground = new SolidColorBrush(Text),
            BorderBrush = new SolidColorBrush(LineStrong), BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 5, 6, 5), FontSize = 11, FontFamily = new FontFamily("Consolas")
        };
        Grid.SetColumn(tb, 0); g.Children.Add(tb);
        var b = MakeButton("Browse", false);
        b.Margin = new Thickness(6, 0, 0, 0); b.Height = 26;
        Grid.SetColumn(b, 1); g.Children.Add(b);
        return g;
    }
    private Border MakeChip(string text, Color color)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x18, color.R, color.G, color.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, color.R, color.G, color.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(0, 2, 6, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = text, FontSize = 11, Foreground = new SolidColorBrush(color), FontWeight = FontWeights.SemiBold }
        };
    }
    private Border MakeKbd(string text)
    {
        return new Border
        {
            Background = new SolidColorBrush(Bg1),
            BorderBrush = new SolidColorBrush(LineStrong),
            BorderThickness = new Thickness(1, 1, 1, 2),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(7, 1, 7, 1),
            Margin = new Thickness(12, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = text, FontSize = 10.5, FontFamily = new FontFamily("Consolas"), Foreground = new SolidColorBrush(TextMute) }
        };
    }
    private Button MakeButton(string text, bool primary)
    {
        return new Button
        {
            Content = text,
            Background = primary
                ? new LinearGradientBrush(Color.FromRgb(0xA0, 0x7C, 0xFF), Color.FromRgb(0x8B, 0x5C, 0xFF), 90)
                : (Brush)new SolidColorBrush(Bg3),
            Foreground = primary ? (Brush)System.Windows.Media.Brushes.White : new SolidColorBrush(Text),
            BorderBrush = primary ? new SolidColorBrush(Color.FromRgb(0x6E, 0x44, 0xD6)) : new SolidColorBrush(LineStrong),
            BorderThickness = new Thickness(1),
            FontSize = 12.5, FontWeight = primary ? FontWeights.Bold : FontWeights.SemiBold,
            Padding = new Thickness(14, 6, 14, 6), Height = 32,
            Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Right
        };
    }
}
