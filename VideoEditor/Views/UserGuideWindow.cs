using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VideoEditor.Views;

public class UserGuideWindow : Window
{
    private static bool L => VideoEditor.Services.Theming.IsLight();
    private static Color Bg0        => L ? Color.FromRgb(0xEB, 0xED, 0xF3) : Color.FromRgb(0x07, 0x08, 0x0D);
    private static Color Bg1        => L ? Color.FromRgb(0xF8, 0xF9, 0xFC) : Color.FromRgb(0x0F, 0x11, 0x19);
    private static Color Bg2        => L ? Color.FromRgb(0xF1, 0xF3, 0xF8) : Color.FromRgb(0x16, 0x1A, 0x25);
    private static Color Bg3        => L ? Color.FromRgb(0xE5, 0xE8, 0xEF) : Color.FromRgb(0x1D, 0x22, 0x31);
    private static Color Line       => L ? Color.FromArgb(0x18, 0x00, 0x00, 0x00) : Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF);
    private static Color LineStrong => L ? Color.FromArgb(0x30, 0x00, 0x00, 0x00) : Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF);
    private static Color Text       => L ? Color.FromRgb(0x1A, 0x1D, 0x24) : Color.FromRgb(0xE8, 0xEA, 0xF2);
    private static Color TextMute   => L ? Color.FromRgb(0x52, 0x59, 0x6B) : Color.FromRgb(0x8A, 0x91, 0xA8);
    private static Color TextDim    => L ? Color.FromRgb(0x7A, 0x82, 0x95) : Color.FromRgb(0x5A, 0x61, 0x78);
    private static readonly Color Accent = Color.FromRgb(0x8B, 0x5C, 0xFF);
    private static readonly Color Success = Color.FromRgb(0x4C, 0xAF, 0x50);
    private static readonly Color Warn = Color.FromRgb(0xFF, 0xD4, 0x3B);
    private static readonly Color Danger = Color.FromRgb(0xFF, 0x6B, 0x6B);
    private static readonly Color Info = Color.FromRgb(0x00, 0xB8, 0xD9);

    private readonly ContentControl _pane;

    private static readonly (string key, string icon, string heb, string en)[] Sections =
    {
        ("start",     "🚀", "התחלה מהירה",        "Getting Started"),
        ("tools",     "🧰", "הכלים בתפריט",       "Tools"),
        ("timeline",  "▤",  "ציר הזמן",            "Timeline"),
        ("clip",      "✎",  "עריכת קליפ",          "Clip editing"),
        ("blocks",    "◼",  "בלוקי הסתרה",         "Hide blocks"),
        ("audio",     "♫",  "אודיו",               "Audio"),
        ("url",       "🌐", "הורדה מ-URL",         "URL Import"),
        ("export",    "💾", "ייצוא",               "Export"),
        ("keys",      "⌨",  "קיצורי מקלדת",        "Keyboard shortcuts"),
        ("settings",  "⚙",  "הגדרות",              "Settings"),
        ("ffmpeg",    "🛠", "FFmpeg",              "FFmpeg backend"),
    };

    public UserGuideWindow()
    {
        Title = "User Guide";
        Width = 1100; Height = 780;
        Background = new SolidColorBrush(Bg0);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });

        // Titlebar
        var titlebar = new Border
        {
            Background = (Application.Current?.Resources["DialogTitlebarBg"] as Brush)
                ?? new SolidColorBrush(Bg1),
            BorderBrush = new SolidColorBrush(LineStrong),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        var tRow = new Grid { Margin = new Thickness(20, 0, 16, 0) };
        tRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var iconTile = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Accent),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x6E, 0x44, 0xD6)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Accent, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.4 }
        };
        iconTile.Child = new TextBlock
        {
            Text = "?", FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        left.Children.Add(iconTile);
        var titleStack = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(new TextBlock { Text = "User Guide", FontSize = 13.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Text) });
        titleStack.Children.Add(new TextBlock { Text = "Press F1 or ? anytime to open this", FontSize = 11, Foreground = new SolidColorBrush(TextMute), Margin = new Thickness(0, 2, 0, 0) });
        left.Children.Add(titleStack);
        Grid.SetColumn(left, 0);
        tRow.Children.Add(left);
        var kbdEsc = MakeKbd("Esc");
        Grid.SetColumn(kbdEsc, 2);
        tRow.Children.Add(kbdEsc);
        titlebar.Child = tRow;
        Grid.SetRow(titlebar, 0);
        root.Children.Add(titlebar);

        // Body
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });

        // Pane on left
        _pane = new ContentControl();
        var paneScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _pane,
            Background = new SolidColorBrush(Bg0)
        };
        Grid.SetColumn(paneScroll, 0);
        body.Children.Add(paneScroll);

        // Rail on right (RTL nav)
        var railBg = new Border { Background = new SolidColorBrush(Bg1), BorderBrush = new SolidColorBrush(Line), BorderThickness = new Thickness(1, 0, 0, 0) };
        var rail = new DockPanel { Margin = new Thickness(8, 12, 8, 12) };
        var nav = new StackPanel();
        DockPanel.SetDock(nav, Dock.Top);
        foreach (var s in Sections) nav.Children.Add(MakeRailItem(s.key, s.icon, s.heb, s.en));
        rail.Children.Add(nav);
        var repoLink = new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(10, 8, 10, 8),
            Background = new SolidColorBrush(Bg2),
            BorderBrush = new SolidColorBrush(Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            VerticalAlignment = VerticalAlignment.Bottom
        };
        DockPanel.SetDock(repoLink, Dock.Bottom);
        var rl = new StackPanel();
        rl.Children.Add(new TextBlock { Text = "REPO", FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(TextDim) });
        rl.Children.Add(new TextBlock { Text = "YossiYad/video-editor", FontSize = 11, FontFamily = new FontFamily("Consolas"), Foreground = new SolidColorBrush(Accent), Margin = new Thickness(0, 4, 0, 0) });
        rl.Children.Add(new TextBlock { Text = "github.com · master", FontSize = 10, Foreground = new SolidColorBrush(TextDim), Margin = new Thickness(0, 2, 0, 0) });
        repoLink.Child = rl;
        rail.Children.Add(repoLink);

        railBg.Child = rail;
        Grid.SetColumn(railBg, 1);
        body.Children.Add(railBg);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        // Footer
        var footer = new Border { Background = new SolidColorBrush(Bg1), BorderBrush = new SolidColorBrush(Line), BorderThickness = new Thickness(0, 1, 0, 0) };
        var fRow = new Grid { Margin = new Thickness(14, 0, 14, 0) };
        fRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var info = new TextBlock { Text = "Tip: every keyboard shortcut is rebindable in Settings → Keyboard.", FontSize = 11, Foreground = new SolidColorBrush(TextDim), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(info, 0); fRow.Children.Add(info);
        var doneBtn = MakeButton("Got it", true);
        doneBtn.Click += (_, _) => Close();
        Grid.SetColumn(doneBtn, 1); fRow.Children.Add(doneBtn);
        footer.Child = fRow;
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        SetActive("start");
    }

    private Button MakeRailItem(string key, string icon, string heb, string en)
    {
        var btn = new Button
        {
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 9, 10, 9),
            Margin = new Thickness(0, 1, 0, 1),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Cursor = Cursors.Hand,
            Tag = key
        };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var ico = new TextBlock { Text = icon, FontSize = 13, Foreground = new SolidColorBrush(TextMute), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(ico, 0); row.Children.Add(ico);
        var stk = new StackPanel();
        stk.Children.Add(new TextBlock { Text = heb, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Text) });
        stk.Children.Add(new TextBlock { Text = en, FontSize = 10, Foreground = new SolidColorBrush(TextDim), Margin = new Thickness(0, 1, 0, 0) });
        Grid.SetColumn(stk, 1); row.Children.Add(stk);
        btn.Template = new ControlTemplate(typeof(Button))
        {
            VisualTree = MakeBtnTemplate()
        };
        btn.Click += (_, _) => SetActive(key);
        btn.Content = row;
        return btn;
    }
    private FrameworkElementFactory MakeBtnTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        border.AppendChild(content);
        return border;
    }

    private void SetActive(string key) => _pane.Content = Build(key);

    private UIElement Build(string key) => key switch
    {
        "start" => StartSection(),
        "tools" => ToolsSection(),
        "timeline" => TimelineSection(),
        "clip" => ClipSection(),
        "blocks" => BlocksSection(),
        "audio" => AudioSection(),
        "url" => UrlSection(),
        "export" => ExportSection(),
        "keys" => KeysSection(),
        "settings" => SettingsSection(),
        "ffmpeg" => FFmpegSection(),
        _ => new TextBlock { Text = "Coming soon", Foreground = new SolidColorBrush(TextMute) }
    };

    // ---------- sections ----------

    private UIElement StartSection()
    {
        var p = MakePane("התחלה מהירה", "Getting Started — open a video and start editing in three steps.");
        p.Children.Add(MakeNumberedStep(1, "Open a video", "Click 'Open' in the topbar, drag video files anywhere into the window, or paste a URL via 'Download from URL' in the sidebar."));
        p.Children.Add(MakeNumberedStep(2, "Edit on the timeline", "Drag clip edges to trim, drag the body to reorder. Hit S to split at the playhead. Right-click for the full toolbox."));
        p.Children.Add(MakeNumberedStep(3, "Export", "Click the green Export button. Pick a destination and the editor renders all your edits, blocks, and effects."));

        var layoutTitle = new TextBlock { Text = "Workspace layout", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Text), Margin = new Thickness(0, 24, 0, 12) };
        p.Children.Add(layoutTitle);
        var layoutGrid = new Grid();
        layoutGrid.ColumnDefinitions.Add(new ColumnDefinition());
        layoutGrid.ColumnDefinitions.Add(new ColumnDefinition());
        layoutGrid.RowDefinitions.Add(new RowDefinition());
        layoutGrid.RowDefinitions.Add(new RowDefinition());
        layoutGrid.Children.Add(WithGridPos(MakeCard("Sidebar (left)", "All capture, transform, overlay, and audio tools — grouped by category. Click to apply on the selected clip or open the matching dialog."), 0, 0));
        layoutGrid.Children.Add(WithGridPos(MakeCard("Preview (center)", "Live video preview with overlay layer for hide blocks. HUD strip at the bottom shows current clip name and time in source."), 0, 1));
        layoutGrid.Children.Add(WithGridPos(MakeCard("Inspector (right)", "Context-sensitive panel — shows clip properties (in/out, speed, volume, rotate/flip) or hide-block properties (mode, color, time range)."), 1, 0));
        layoutGrid.Children.Add(WithGridPos(MakeCard("Timeline (bottom)", "Ruler, V1 video track, A1 audio track with waveforms, and Hide Blocks tracks. Yellow playhead is draggable from anywhere along the line."), 1, 1));
        p.Children.Add(layoutGrid);
        return WrapSection(p);
    }
    private UIElement ToolsSection()
    {
        var p = MakePane("הכלים בתפריט", "Every tool in the left sidebar, grouped by category.");
        p.Children.Add(MakeGroupHeading("Capture", "Bring new media into the project"));
        p.Children.Add(MakeToolCard("🖥 Screen Recorder", "Record any region of your screen via FFmpeg gdigrab."));
        p.Children.Add(MakeToolCard("🎙 Text to Speech", "Use Windows SAPI voices to generate a narration WAV."));
        p.Children.Add(MakeToolCard("🎥 Video Recorder", "Webcam via DirectShow."));
        p.Children.Add(MakeToolCard("🌐 Download from URL", "Paste any YouTube / Vimeo / Twitter / TikTok URL — auto-uses yt-dlp. Direct .mp4 links download via HTTPS.", true));

        p.Children.Add(MakeGroupHeading("Transform & Trim", "Edit selected clips"));
        p.Children.Add(MakeToolCard("🔗 Merge Videos", "Concat multiple files into one."));
        p.Children.Add(MakeToolCard("✂ Trim Video", "Set in/out points precisely with a dialog."));
        p.Children.Add(MakeToolCard("✄ Crop Video", "Visual region picker — drag the green rectangle over the keep area."));
        p.Children.Add(MakeToolCard("↻ Rotate / ⇄ Flip / ⛶ Resize", "Quick geometric transforms."));
        p.Children.Add(MakeToolCard("🔁 Loop / ⏩ Speed", "Repeat a clip N times or change playback speed (0.25× – 4×)."));
        p.Children.Add(MakeToolCard("🤚 Stabilize", "Two-pass vidstabdetect + vidstabtransform."));

        p.Children.Add(MakeGroupHeading("Overlays", "Add things on top of the video"));
        p.Children.Add(MakeToolCard("🚫 Remove Logo", "Visual picker — drag yellow rectangle over a logo. FFmpeg delogo masks the region."));
        p.Children.Add(MakeToolCard("🖼 Add Image", "Place an image overlay at a fixed position."));
        p.Children.Add(MakeToolCard("🔤 Add Text", "Burn-in text via FFmpeg drawtext (font, size, color, position)."));

        p.Children.Add(MakeGroupHeading("Audio", "Sound control"));
        p.Children.Add(MakeToolCard("🎵 Add Audio", "Attach an external audio track to the clip."));
        p.Children.Add(MakeToolCard("🔊 Change Volume", "Per-clip volume slider (0×–4×)."));
        p.Children.Add(MakeToolCard("↗ Extract Audio", "Save the audio as MP3/AAC/WAV/OGG/FLAC."));
        p.Children.Add(MakeToolCard("🔇 Mute / Remove Audio Track", "Silence (live) or re-encode without audio."));
        return WrapSection(p);
    }
    private UIElement TimelineSection()
    {
        var p = MakePane("ציר הזמן", "Timeline — three lanes: V1 video, A1 audio, and Hide Blocks tracks.");
        p.Children.Add(MakeBullet("Click on the ruler at the top to seek. The yellow playhead jumps there."));
        p.Children.Add(MakeBullet("Drag the yellow line (or its time-stamp flag at top) from anywhere along its length to scrub."));
        p.Children.Add(MakeBullet("Drag a clip body to reorder it. A green insertion arrow shows where it'll land."));
        p.Children.Add(MakeBullet("Drag clip edges to trim — preview updates live with the trimmed frame."));
        p.Children.Add(MakeBullet("Right-click a clip for the full 20-action context menu."));
        p.Children.Add(MakeBullet("Drop files anywhere on the timeline — a slot indicator shows where they'll be inserted."));
        p.Children.Add(MakeBullet("Zoom: + / − buttons in the timeline header, or scroll-wheel while hovering."));
        return WrapSection(p);
    }
    private UIElement ClipSection()
    {
        var p = MakePane("עריכת קליפ", "Clip editing — non-destructive in/out + per-clip filters.");
        p.Children.Add(MakeNotice("Non-destructive properties", "InPoint, OutPoint, Speed, Volume, Rotate, Flip, Loop — all stored on the clip object and applied at export. Source file is never modified.", Success));
        p.Children.Add(MakeNotice("Destructive operations", "Crop, Stabilize, Add Image, Add Text, Add Audio, Remove Logo, Remove Audio — re-encode through FFmpeg and replace the source file with a new one.", Warn));
        p.Children.Add(MakeBullet("S — split selected clip at playhead. New clip preserves all properties (incl. IsAudioOnly for audio clips)."));
        p.Children.Add(MakeBullet("Shift+Click on any clip — split at the exact pixel you clicked."));
        p.Children.Add(MakeBullet("Right-click → 'Split into N Parts…' — slice the clip into N equal segments in one operation."));
        p.Children.Add(MakeBullet("Ctrl+D / Ctrl+C+V — duplicate or copy/paste a clip."));
        return WrapSection(p);
    }
    private UIElement BlocksSection()
    {
        var p = MakePane("בלוקי הסתרה", "Hide Blocks — cover sensitive areas in the video.");
        p.Children.Add(MakeNumberedStep(1, "Add a block", "Click '◼ Add Hide Block' in the sidebar — a black box appears on the preview."));
        p.Children.Add(MakeNumberedStep(2, "Drag & resize", "Click the block on the preview, then drag its body or corners. Yellow border shows it's selected."));
        p.Children.Add(MakeNumberedStep(3, "Pick a mode", "In the Inspector → Mode: Solid / Blur / Pixelate."));
        p.Children.Add(MakeNumberedStep(4, "Time range", "Either 'Cover whole video', or set start/end seconds. The timeline shows a purple bar for each block."));
        p.Children.Add(MakeNumberedStep(5, "Tune & export", "Adjust blur/pixel strength with the slider. On export, FFmpeg applies drawbox / boxblur / pixelate over the chosen range."));

        var modesTitle = new TextBlock { Text = "Block modes", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Text), Margin = new Thickness(0, 22, 0, 12) };
        p.Children.Add(modesTitle);
        var modesRow = new Grid();
        modesRow.ColumnDefinitions.Add(new ColumnDefinition());
        modesRow.ColumnDefinitions.Add(new ColumnDefinition());
        modesRow.ColumnDefinitions.Add(new ColumnDefinition());
        var solid = MakeModeSwatch("Solid", "Full color fill", System.Windows.Media.Brushes.Black);
        Grid.SetColumn(solid, 0); modesRow.Children.Add(solid);
        var blur = MakeModeSwatch("Blur", "Frosted glass look", new SolidColorBrush(Color.FromArgb(0xE6, 0x1A, 0x1F, 0x2C)));
        Grid.SetColumn(blur, 1); modesRow.Children.Add(blur);
        var pix = MakeModeSwatch("Pixelate", "Mosaic pattern", new SolidColorBrush(Color.FromArgb(0xE6, 0x2A, 0x20, 0x18)));
        Grid.SetColumn(pix, 2); modesRow.Children.Add(pix);
        p.Children.Add(modesRow);
        return WrapSection(p);
    }
    private UIElement AudioSection()
    {
        var p = MakePane("אודיו", "Audio — every clip has its own waveform in the A1 lane.");
        p.Children.Add(MakeBullet("Per-clip volume — drag the slider in the Inspector, or right-click the A1 bar → Change Volume."));
        p.Children.Add(MakeBullet("Mute — Backspace on a selected audio bar mutes that clip (red diagonal stripes overlay)."));
        p.Children.Add(MakeBullet("Remove track — full re-encode without any audio stream."));
        p.Children.Add(MakeBullet("Extract — save the audio as a standalone MP3/AAC/WAV/OGG/FLAC."));
        p.Children.Add(MakeNotice("🔗➜ Detach Audio", "Right-click an audio bar → 'Detach Audio'. The audio extracts to its own clip (purple bar in A1) that you can drag, trim, split, copy, and delete independently from the video.", Accent));
        return WrapSection(p);
    }
    private UIElement UrlSection()
    {
        var p = MakePane("הורדה מ-URL", "Import video from a URL.");
        p.Children.Add(MakeBullet("YouTube, Vimeo, Twitter / X, TikTok, Twitch, Instagram, Reddit, Facebook — auto-detected and downloaded via yt-dlp."));
        p.Children.Add(MakeBullet("yt-dlp.exe (~12 MB) is auto-downloaded on first use and cached in the app's ffmpeg folder."));
        p.Children.Add(MakeBullet("Direct .mp4 / .mov / .mkv links download via HTTPS."));
        p.Children.Add(MakeBullet("Quality: yt-dlp picks the best video+audio combo up to 1080p and merges them via the bundled ffmpeg."));
        p.Children.Add(MakeBullet("After download, the clip is added to the timeline automatically with thumbnails and waveform."));
        return WrapSection(p);
    }
    private UIElement ExportSection()
    {
        var p = MakePane("ייצוא", "Export — render everything to a final video file.");
        p.Children.Add(MakeBullet("Click the green 'Export' button or press Ctrl+E."));
        p.Children.Add(MakeBullet("Three-pass pipeline: 1) render each clip with its filters → 2) concat them in timeline order → 3) overlay all hide blocks."));
        p.Children.Add(MakeBullet("Progress bar in the topbar updates in real time."));
        p.Children.Add(MakeBullet("Output: H.264 MP4 by default (matches the project's first clip resolution). Container and codec are configurable in Settings → Export."));
        return WrapSection(p);
    }
    private UIElement KeysSection()
    {
        var p = MakePane("קיצורי מקלדת", "All keyboard shortcuts.");
        var groups = new (string title, string[][] keys)[]
        {
            ("Playback", new[]{
                new[]{"Space","Play / Pause"},
                new[]{"←  →","Step 1 frame"},
                new[]{"-5s / +5s","Back / Forward buttons"},
                new[]{"Home / End","Start / End"},
            }),
            ("Editing", new[]{
                new[]{"S","Split at playhead"},
                new[]{"Shift+Click","Split at clicked pixel"},
                new[]{"Backspace / Delete","Delete (or mute if attached audio)"},
                new[]{"Ctrl+C / Ctrl+V","Copy / Paste"},
            }),
            ("Tools", new[]{
                new[]{"?  /  F1","Open user guide"},
                new[]{",","Open Settings"},
                new[]{"Ctrl+E","Export"},
                new[]{"Ctrl+O","Open files"},
                new[]{"Esc","Close dialog"},
            }),
        };
        foreach (var g in groups)
        {
            p.Children.Add(new TextBlock { Text = g.title, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Accent), Margin = new Thickness(0, 16, 0, 8) });
            var box = new Border { Background = new SolidColorBrush(Bg2), BorderBrush = new SolidColorBrush(Line), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10) };
            var col = new StackPanel();
            foreach (var k in g.keys)
            {
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var kb = MakeKbd(k[0]); kb.Margin = new Thickness(0); Grid.SetColumn(kb, 0); row.Children.Add(kb);
                var lbl = new TextBlock { Text = k[1], FontSize = 12, Foreground = new SolidColorBrush(Text), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(lbl, 1); row.Children.Add(lbl);
                col.Children.Add(row);
            }
            box.Child = col;
            p.Children.Add(box);
        }
        return WrapSection(p);
    }
    private UIElement SettingsSection()
    {
        var p = MakePane("הגדרות", "Settings — open via the ⚙ icon in the topbar or press , (comma).");
        var cats = new[]
        {
            ("General",  "Language, theme, startup behavior, auto-save"),
            ("Player",   "Default volume, scrubbing, proxy media"),
            ("Editor",   "Snap, ripple, defaults for new blocks"),
            ("Export",   "Container, codec, CRF, hardware acceleration"),
            ("Storage",  "Folders for projects, downloads, cache"),
            ("FFmpeg",   "Binary paths, encoder detection"),
            ("Keyboard", "Rebind every shortcut"),
            ("Updates",  "Channel + manual check"),
            ("About",    "License, version, credits"),
        };
        foreach (var (n, d) in cats)
            p.Children.Add(MakeToolCard(n, d));
        return WrapSection(p);
    }
    private UIElement FFmpegSection()
    {
        var p = MakePane("FFmpeg", "FFmpeg backend — all heavy lifting is done by FFmpeg under the hood.");
        p.Children.Add(MakeBullet("On first run, the app auto-downloads ffmpeg.exe + ffprobe.exe to the 'ffmpeg' subfolder next to the EXE (~150 MB)."));
        p.Children.Add(MakeBullet("Filters used: scale, crop, transpose, hflip/vflip, setpts, atempo, drawbox, boxblur, delogo, overlay, drawtext, showwavespic, vidstabdetect, vidstabtransform."));
        p.Children.Add(MakeBullet("Encoder: libx264 (default), libx265, AV1, or ProRes — configurable in Settings → Export."));
        p.Children.Add(MakeBullet("yt-dlp uses the bundled ffmpeg to merge separate video + audio streams from streaming sites."));
        return WrapSection(p);
    }

    // ---------- helpers ----------

    private StackPanel MakePane(string heb, string en)
    {
        var sp = new StackPanel { Margin = new Thickness(36, 28, 36, 28) };
        sp.Children.Add(new TextBlock { Text = heb, FontSize = 24, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Text) });
        sp.Children.Add(new TextBlock { Text = en, FontSize = 13, Foreground = new SolidColorBrush(TextMute), Margin = new Thickness(0, 4, 0, 22), TextWrapping = TextWrapping.Wrap });
        return sp;
    }
    private UIElement WrapSection(StackPanel p) => p;

    private Border MakeNumberedStep(int num, string title, string desc)
    {
        var b = new Border
        {
            Background = new SolidColorBrush(Bg1),
            BorderBrush = new SolidColorBrush(Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var numBg = new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Accent), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
        };
        numBg.Child = new TextBlock { Text = num.ToString(), FontSize = 14, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(numBg, 0); grid.Children.Add(numBg);
        var stk = new StackPanel();
        stk.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Text) });
        stk.Children.Add(new TextBlock { Text = desc, FontSize = 12.5, Foreground = new SolidColorBrush(TextMute), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(stk, 1); grid.Children.Add(stk);
        b.Child = grid;
        return b;
    }
    private Border MakeCard(string title, string desc)
    {
        var b = new Border
        {
            Background = new SolidColorBrush(Bg1),
            BorderBrush = new SolidColorBrush(Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 8, 8)
        };
        var stk = new StackPanel();
        stk.Children.Add(new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Text) });
        stk.Children.Add(new TextBlock { Text = desc, FontSize = 11.5, Foreground = new SolidColorBrush(TextMute), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        b.Child = stk;
        return b;
    }
    private UIElement WithGridPos(UIElement el, int col, int row)
    {
        if (el is FrameworkElement f) { Grid.SetColumn(f, col); Grid.SetRow(f, row); }
        return el;
    }
    private UIElement MakeBullet(string text)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var dot = new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(Accent), Margin = new Thickness(0, 7, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(dot, 0); grid.Children.Add(dot);
        var t = new TextBlock { Text = text, FontSize = 12.5, Foreground = new SolidColorBrush(Text), TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(t, 1); grid.Children.Add(t);
        return grid;
    }
    private UIElement MakeGroupHeading(string title, string desc)
    {
        var b = new Border { Margin = new Thickness(0, 16, 0, 8) };
        var stk = new StackPanel();
        stk.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Accent), Margin = new Thickness(0, 0, 0, 2) });
        stk.Children.Add(new TextBlock { Text = desc, FontSize = 11, Foreground = new SolidColorBrush(TextDim) });
        b.Child = stk;
        return b;
    }
    private UIElement MakeToolCard(string title, string desc, bool isNew = false)
    {
        var b = new Border
        {
            Background = new SolidColorBrush(Bg1),
            BorderBrush = new SolidColorBrush(Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 0, 6)
        };
        var stk = new StackPanel();
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Text) });
        if (isNew)
        {
            var newChip = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x35, Accent.R, Accent.G, Accent.B)),
                BorderBrush = new SolidColorBrush(Accent),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 0, 5, 0),
                Margin = new Thickness(8, 0, 0, 0),
                Child = new TextBlock { Text = "NEW", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Accent) }
            };
            titleRow.Children.Add(newChip);
        }
        stk.Children.Add(titleRow);
        stk.Children.Add(new TextBlock { Text = desc, FontSize = 11.5, Foreground = new SolidColorBrush(TextMute), Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
        b.Child = stk;
        return b;
    }
    private Border MakeModeSwatch(string name, string desc, Brush fill)
    {
        var b = new Border
        {
            Background = new SolidColorBrush(Bg1),
            BorderBrush = new SolidColorBrush(Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var stk = new StackPanel();
        var swatch = new Border
        {
            Height = 60, CornerRadius = new CornerRadius(5), Background = fill,
            BorderBrush = new SolidColorBrush(LineStrong), BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 10)
        };
        stk.Children.Add(swatch);
        stk.Children.Add(new TextBlock { Text = name, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Text) });
        stk.Children.Add(new TextBlock { Text = desc, FontSize = 11, Foreground = new SolidColorBrush(TextMute), Margin = new Thickness(0, 2, 0, 0) });
        b.Child = stk;
        return b;
    }
    private Border MakeNotice(string title, string desc, Color color)
    {
        var b = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x18, color.R, color.G, color.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, color.R, color.G, color.B)),
            BorderThickness = new Thickness(1, 1, 1, 1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 8, 0, 8)
        };
        var stk = new StackPanel();
        stk.Children.Add(new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(color) });
        stk.Children.Add(new TextBlock { Text = desc, FontSize = 12, Foreground = new SolidColorBrush(Text), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        b.Child = stk;
        return b;
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
            Margin = new Thickness(0),
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
            Padding = new Thickness(16, 6, 16, 6), Height = 32,
            Cursor = Cursors.Hand
        };
    }
}
