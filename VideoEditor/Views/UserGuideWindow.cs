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
        ("formats",   "📐", "פורמט הפרויקט",       "Project formats"),
        ("timeline",  "▤",  "ציר הזמן",            "Timeline"),
        ("clip",      "✎",  "עריכת קליפ",          "Clip editing"),
        ("canvas",    "⛶",  "גודל ומיקום בקנבס",   "Canvas transform"),
        ("text",      "🔤", "טקסט על הסרטון",       "Text overlays"),
        ("blocks",    "◼",  "בלוקי הסתרה",         "Hide blocks"),
        ("audio",     "♫",  "אודיו",               "Audio"),
        ("aicaptions","✨", "כתוביות AI",           "AI Captions"),
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
        "formats" => FormatsSection(),
        "timeline" => TimelineSection(),
        "clip" => ClipSection(),
        "canvas" => CanvasSection(),
        "text" => TextOverlaysSection(),
        "blocks" => BlocksSection(),
        "audio" => AudioSection(),
        "aicaptions" => AiCaptionsSection(),
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
        var p = MakePane("התחלה מהירה", "Open a video and start editing in three steps.");
        p.Children.Add(MakeNumberedStep(1,
            "פתח סרטון  ·  Open a video",
            "לחץ על Open למעלה, או פשוט גרור קובץ וידאו מהמחשב לתוך החלון. אפשר גם להוריד מיוטיוב — לחץ \"Import from URL\" בסיידבר השמאלי, הדבק קישור, והאפליקציה תוריד את הסרטון.\nClick Open in the topbar, drag a video file into the window, or use Import from URL to grab a YouTube video."));
        p.Children.Add(MakeNumberedStep(2,
            "ערוך בציר הזמן  ·  Edit on the timeline",
            "הציר זמן למטה. תגרור את הקצוות של הקליפ כדי לחתוך, תגרור באמצע כדי להזיז. לחיצה ימנית על קליפ פותחת את כל הפעולות (לחתוך, לשנות מהירות, להעתיק, למחוק וכו').\nDrag clip edges to trim, drag the body to reorder. Right-click for the full toolbox."));
        p.Children.Add(MakeNumberedStep(3,
            "ייצא  ·  Export",
            "לחץ על הכפתור הירוק Export למעלה מימין. בחר איפה לשמור את הקובץ — האפליקציה תרכיב את כל מה שערכת לסרטון אחד.\nClick the green Export button at top-right. Pick a destination — the editor renders everything into a single video file."));

        var layoutTitle = new TextBlock { Text = "המסך שלך  ·  Workspace layout", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Text), Margin = new Thickness(0, 24, 0, 12) };
        p.Children.Add(layoutTitle);
        var layoutGrid = new Grid();
        layoutGrid.ColumnDefinitions.Add(new ColumnDefinition());
        layoutGrid.ColumnDefinitions.Add(new ColumnDefinition());
        layoutGrid.RowDefinitions.Add(new RowDefinition());
        layoutGrid.RowDefinitions.Add(new RowDefinition());
        layoutGrid.Children.Add(WithGridPos(MakeCard(
            "סיידבר שמאלי  ·  Left sidebar",
            "כל הכלים — הקלטת מסך, ייבוא מיוטיוב, חיתוך, סיבוב, הוספת אודיו, AI Captions ועוד. מקובץ לפי קטגוריות.\nAll capture, transform, overlay, audio, and AI tools — grouped by category."), 0, 0));
        layoutGrid.Children.Add(WithGridPos(MakeCard(
            "תצוגה מקדימה במרכז  ·  Preview (center)",
            "כאן רואים את הסרטון מתנגן. גם רואים בלוקי הסתרה וכתוביות שתוסיף.\nThe live video preview. Hide blocks and text overlays render here too."), 0, 1));
        layoutGrid.Children.Add(WithGridPos(MakeCard(
            "Inspector מימין  ·  Right inspector",
            "פאנל ההגדרות של מה שבחרת — קליפ או בלוק. שולט במהירות, עוצמה, מיקום על הקנבס, צבע ועוד.\nProperty panel for whatever is selected — clip or block. Controls speed, volume, canvas position, color, time range, and more."), 1, 0));
        layoutGrid.Children.Add(WithGridPos(MakeCard(
            "ציר זמן למטה  ·  Timeline (bottom)",
            "סרגל זמן + שכבת וידאו + שכבת אודיו עם גלי קול + רצועות בלוקים וטקסטים. הקו הצהוב הוא ראש הניגון — אפשר לגרור אותו לאן שרוצים.\nRuler + video track + audio track with waveforms + hide-block and text-overlay rows. Drag the yellow playhead from anywhere along its length."), 1, 1));
        p.Children.Add(layoutGrid);
        return WrapSection(p);
    }
    private UIElement ToolsSection()
    {
        var p = MakePane("הכלים בתפריט", "Every tool in the left sidebar, in plain language.");

        p.Children.Add(MakeGroupHeading("Capture · לכידה", "להכניס וידאו חדש לפרויקט  ·  Bring new media into the project"));
        p.Children.Add(MakeToolCard("🌐 Import from URL · הורדה מיוטיוב",
            "מדביק כתובת של סרטון מ-YouTube / Vimeo / TikTok / Twitter / Instagram וכו', והאפליקציה מורידה את הסרטון אליך אוטומטית. בפעם הראשונה היא מורידה גם את הכלי שעושה את זה (yt-dlp).\nPaste a video URL from YouTube/Vimeo/TikTok/Twitter/Instagram — the app downloads it for you. First run also installs yt-dlp."));
        p.Children.Add(MakeToolCard("🖥 Screen Recorder · הקלטת מסך",
            "מקליט את המסך שלך לוידאו. בוחר אזור, לוחץ הקלטה, וזה נשמר לקובץ.\nRecord a region of your screen straight into a video file."));
        p.Children.Add(MakeToolCard("🎙 Text to Speech · טקסט לדיבור",
            "מקליד טקסט, האפליקציה מקריאה אותו בקול ושומרת כקובץ אודיו. שימושי לקריינות אוטומטית.\nType text, the app speaks it out loud and saves to an audio file — for auto-narration."));
        p.Children.Add(MakeToolCard("🎥 Camera Recorder · הקלטה ממצלמה",
            "מקליט מהמצלמה של המחשב שלך (webcam).\nRecords from your webcam."));

        p.Children.Add(MakeGroupHeading("Transform & Trim · עריכה", "פעולות עריכה על הקליפ שנבחר  ·  Edit the selected clip"));
        p.Children.Add(MakeToolCard("🔗 Merge Videos · מיזוג סרטונים",
            "מאחד כמה קבצי וידאו לסרטון אחד.\nGlues multiple video files into one."));
        p.Children.Add(MakeToolCard("✂ Trim Video · חיתוך",
            "חותך חלק מהסרטון לפי זמן התחלה וסיום.\nKeeps only a chosen segment of the video by start/end time."));
        p.Children.Add(MakeToolCard("✄ Crop Video · גזירת תמונה",
            "גוזר מסגרת מתוך הפריים. גורר מלבן ירוק על אזור שרוצים להשאיר.\nDraw a green rectangle over the area to keep; the rest is cut off."));
        p.Children.Add(MakeToolCard("↻ Rotate · סיבוב",
            "מסובב את הסרטון 90 / 180 / 270 מעלות.\nRotate the video 90 / 180 / 270 degrees."));
        p.Children.Add(MakeToolCard("⇄ Flip · היפוך",
            "הופך את הסרטון אופקית או אנכית (אפקט מראה).\nMirror the video horizontally or vertically."));
        p.Children.Add(MakeToolCard("⛶ Resize · שינוי רזולוציה",
            "משנה את הרזולוציה של הסרטון (לדוגמה — להוריד מ-4K ל-1080p).\nChange the video resolution (e.g. 4K → 1080p)."));
        p.Children.Add(MakeToolCard("🔁 Loop · לולאה",
            "משכפל את הסרטון N פעמים ברצף.\nRepeats the clip N times in a row."));
        p.Children.Add(MakeToolCard("⏩ Change Speed · שינוי מהירות",
            "מאיץ או מאט את הסרטון (0.25× עד 4×). אודיו מתואם אוטומטית כך שאף אחד לא ישמע צפצופים.\nSpeed the video up or down (0.25× – 4×). Audio is pitch-corrected automatically."));
        p.Children.Add(MakeToolCard("🤚 Stabilize · ייצוב",
            "מתקן רעידות וזעזועי מצלמה (כמו וידאו שצולם ביד). לוקח שני מעברים, איטי יותר אבל איכות גבוהה.\nFixes camera shake. Two-pass — slower but high quality."));

        p.Children.Add(MakeGroupHeading("Overlays · שכבות", "להוסיף דברים על גבי הסרטון  ·  Add things on top of the video"));
        p.Children.Add(MakeToolCard("🚫 Remove Logo · הסרת לוגו",
            "מסיר לוגו של ערוץ או סימן מים. גורר מסגרת צהובה על הלוגו והאפליקציה תטשטש אותו.\nDrag a yellow rectangle over a watermark/logo — the app blurs it out."));
        p.Children.Add(MakeToolCard("🖼 Add Image · הוספת תמונה",
            "מדביק תמונה (PNG/JPG) על הסרטון במיקום שתבחר.\nPaste an image (PNG/JPG) on the video at any position."));
        p.Children.Add(MakeToolCard("🔤 Add Text · הוספת טקסט",
            "מוסיף טקסט על הסרטון. בוחר פונט, גודל, צבע, רקע — וגורר את הטקסט על הפריים למיקום שאתה רוצה. הטקסט מופיע כפס בציר הזמן שאפשר לגרור ולשנות לאיזה זמנים הוא מופיע.\nAdds text over the video. Pick font / size / colour / background and drag it to position on the frame. Shows up as a draggable bar on the timeline so you control when it appears."));
        p.Children.Add(MakeToolCard("✨ AI Captions · כתוביות AI",
            "מייצר אוטומטית כתוביות לסרטון ע\"י זיהוי הדיבור (Whisper) + עריכה חכמה (Google Gemini). יודע גם לתרגם — אם הסרטון באנגלית הוא יודע לכתוב כתוביות בעברית, ולהפך. ראה סקציית AI Captions לפרטים.\nAuto-generate kinetic-typography captions from the spoken audio. Can translate to a different language. See the AI Captions section.", true));

        p.Children.Add(MakeGroupHeading("Audio · אודיו", "שליטה בקול  ·  Sound control"));
        p.Children.Add(MakeToolCard("🎵 Add Audio · הוספת אודיו",
            "מצרף לסרטון פסקול אודיו חיצוני (לדוגמה — מוזיקת רקע).\nAttach an external audio track (e.g. background music)."));
        p.Children.Add(MakeToolCard("🔊 Change Volume · עוצמת קול",
            "מעלה או מוריד את עוצמת הקליפ (0% — שקט גמור, 200% — פי שניים).\nRaise or lower the clip's volume (0% = silent, 200% = double)."));
        p.Children.Add(MakeToolCard("↗ Extract Audio · חילוץ אודיו",
            "שומר רק את האודיו של הסרטון כקובץ נפרד (MP3 / WAV / AAC).\nSaves just the audio of the video as a separate file."));
        p.Children.Add(MakeToolCard("🔇 Mute · השתקה",
            "משתיק את הקול של הקליפ (לחיצה אחת = שקט, לחיצה שוב = חזרה).\nMutes / un-mutes the clip with one click."));

        p.Children.Add(MakeGroupHeading("Hide Blocks · בלוקי הסתרה", "כיסוי מידע רגיש (פנים, מספרים)  ·  Cover sensitive areas"));
        p.Children.Add(MakeToolCard("◼ Add Hide Block · הוסף בלוק הסתרה",
            "מוסיף מסגרת על הסרטון לכיסוי משהו רגיש — פנים, מספרי טלפון, כתובות. אפשר מילוי שחור, טשטוש או פיקסול. ראה סקציית בלוקי הסתרה.\nAdds a box over the video to cover faces, phone numbers, addresses, etc. Solid / Blur / Pixelate. See Hide Blocks section."));
        return WrapSection(p);
    }
    private UIElement FormatsSection()
    {
        var p = MakePane("פורמט הפרויקט", "Choose the canvas shape — YouTube, Reels, Square, etc.");
        p.Children.Add(MakeNotice(
            "מה זה פורמט?  ·  What's a project format?",
            "כל פלטפורמה רוצה צורת סרטון אחרת. ביוטיוב — מלבן רחב 16:9. בריילז של אינסטגרם וטיקטוק — אנכי 9:16. באינסטגרם רגיל — ריבוע 1:1. כשאתה בוחר פורמט, התצוגה המקדימה משתנה בהתאם וגם הסרטון הסופי ייצא בגודל הזה.\nEach platform expects a different shape: YouTube 16:9, Reels/TikTok 9:16, Instagram square 1:1. The preview and final export both match the format you choose.",
            Accent));
        p.Children.Add(MakeBullet("הכפתור למעלה (\"→ 1920×1080 · YouTube\") פותח את הבחירה. \nThe chip at the top-bar (\"→ 1920×1080 · YouTube\") opens the format picker."));
        p.Children.Add(MakeBullet("פורמטים זמינים: Source (כמו המקור), YouTube 16:9, Reels/TikTok/Shorts 9:16, Square 1:1, Portrait 4:5, Cinematic 21:9, Custom (כל גודל שתבחר). \nPresets: Source, YouTube 16:9, Reels 9:16, Square 1:1, Portrait 4:5, Cinematic 21:9, or Custom."));
        p.Children.Add(MakeNotice(
            "Fit Mode — איך לכווץ סרטון לפורמט",
            "כשהסרטון בצורה אחת ובחרת פורמט אחר — צריך להחליט מה לעשות:\n• Contain — להכניס את כל הסרטון, יהיו פסים שחורים בצדדים (לא חותך כלום).\n• Cover — למלא את הפורמט עד הסוף, פינות הסרטון ייחתכו (אין פסים).\n• Blur bg — להכניס את כל הסרטון, ולמלא את הפסים השחורים בגרסה מטושטשת של הסרטון עצמו (יפה מאוד).\nWhen the clip aspect doesn't match the format you picked:\n• Contain — fit it all, with black bars\n• Cover — fill the canvas, cropping clip edges\n• Blur bg — fit it all, fill bars with a blurred copy of the clip itself.",
            Info));
        p.Children.Add(MakeBullet("Pro-tip: לסרטון אנכי שמיועד לריילז, בחר \"Reels 9:16\" ואז Fit=Cover או השתמש ב-Canvas Transform למיקום ידני (סקציה נפרדת).\nFor a vertical Reel, pick 9:16 + Fit=Cover, or use Canvas Transform for manual placement."));
        return WrapSection(p);
    }

    private UIElement TimelineSection()
    {
        var p = MakePane("ציר הזמן", "The timeline at the bottom of the window.");
        p.Children.Add(MakeBullet("הסרגל למעלה  ·  The ruler at the top — לחץ בכל מקום עליו והקו הצהוב יקפוץ לשם. זה הזמן בסרטון.\nClick anywhere — the yellow playhead jumps there. This is the current time in your project."));
        p.Children.Add(MakeBullet("הקו הצהוב  ·  The yellow line — אפשר לגרור אותו בכל אורכו כדי לנוע בסרטון. ניתן גם לגרור את התווית הצהובה למעלה (חיץ עליון).\nDrag the yellow line — or its time-flag at the top — from any height to scrub through the video."));
        p.Children.Add(MakeBullet("רצועת וידאו (V1)  ·  Video lane — כל סרטון שטענת מופיע כקליפ סגול עם תמונות ממוזערות. גרור את הקצוות לקצר/להאריך, גרור את כל הקליפ להזיז.\nEach loaded clip appears as a purple bar with thumbnails. Drag edges to trim, drag the body to move."));
        p.Children.Add(MakeBullet("רצועת אודיו (A1)  ·  Audio lane — מציגה את גל הקול של כל קליפ. גרור בה ימינה לשמאל לזיהוי קטעים שקטים.\nShows each clip's waveform. Drag through it to spot silent passages."));
        p.Children.Add(MakeBullet("רצועות בלוקי הסתרה  ·  Hide-block lanes — כל בלוק שיצרת מופיע כפס סגול. גרור קצוות כדי לקבוע מתי הבלוק מופיע בסרטון.\nEach hide block has its own purple bar. Drag its edges to set when it appears."));
        p.Children.Add(MakeBullet("רצועות טקסט (T)  ·  Text-overlay lanes — כל טקסט שהוספת מופיע כפס טורקיז. אותו רעיון — קצוות = זמן הופעה.\nText overlays show up as teal bars. Same edge-drag = appearance time."));
        p.Children.Add(MakeBullet("זום  ·  Zoom — כפתורי + / − בראש ציר הזמן, או גלגלת עכבר עליו.\n+ / − buttons at the timeline header, or mouse-wheel while hovering."));
        p.Children.Add(MakeBullet("גרירת קבצים  ·  Drag-drop — תגרור קבצים מהמחשב ישירות לציר הזמן — חץ ירוק יראה לך איפה הם ייכנסו.\nDrop files from Explorer right onto the timeline — a green arrow shows where they'll land."));
        p.Children.Add(MakeBullet("לחיצה ימנית  ·  Right-click — כל אלמנט (קליפ / בלוק / טקסט / אודיו) פותח תפריט עם כל הפעולות עליו.\nRight-click any element for its full action menu."));
        return WrapSection(p);
    }

    private UIElement ClipSection()
    {
        var p = MakePane("עריכת קליפ", "Editing a single video clip — speed, trim, rotate, etc.");
        p.Children.Add(MakeNotice("פעולות בלי לגעת בקובץ המקור  ·  Non-destructive",
            "מהירות, עוצמת קול, חיתוך זמן, סיבוב, היפוך, לולאה, וגודל בקנבס — האפליקציה זוכרת את ההגדרה ומפעילה אותה רק בעת ייצוא. הקובץ המקורי במחשב שלך נשאר זהה. אפשר לחזור אחורה תמיד.\nSpeed, volume, in/out, rotate, flip, loop, canvas transform — all stored on the clip object and only applied at export. The source file on disk is never modified.",
            Success));
        p.Children.Add(MakeNotice("פעולות שמשנות את הקליפ  ·  Destructive operations",
            "Crop, Stabilize, Add Image, Add Text מהסיידבר, Add Audio, Remove Logo, Remove Audio — אלה עוברים דרך FFmpeg ויוצרים קובץ חדש. הקליפ ה\"חדש\" מחליף את הישן בציר הזמן.\nCrop, Stabilize, Add Image, sidebar Add Text, Add Audio, Remove Logo, Remove Audio — re-encode through FFmpeg and replace the clip with a new file.",
            Warn));
        p.Children.Add(MakeBullet("פיצול ב-S  ·  Split with S — בחר קליפ, מקם את הקו הצהוב, ותלחץ S. הקליפ נחצה לשני חלקים שאפשר לערוך בנפרד.\nSelect a clip, position the playhead, press S. Splits into two pieces."));
        p.Children.Add(MakeBullet("פיצול בלחיצה  ·  Shift+Click — Shift+לחיצה על קליפ חוצה אותו בדיוק במקום שלחצת.\nShift-click anywhere on a clip to split at that exact pixel."));
        p.Children.Add(MakeBullet("פיצול ל-N חלקים  ·  Right-click → Split into N — חותך את הקליפ ל-N חלקים שווים בפעולה אחת.\nRight-click → Split into N Parts… slices the clip into N equal segments."));
        p.Children.Add(MakeBullet("העתק/הדבק  ·  Copy/Paste — Ctrl+C על קליפ, Ctrl+V מציב עותק. Ctrl+D = שכפול מהיר.\nCtrl+C / Ctrl+V copies a clip; Ctrl+D duplicates in place."));
        p.Children.Add(MakeBullet("פאנל ה-Inspector מימין  ·  Right-side Inspector — בחירת קליפ מציגה: TRIM (חיתוך), SPEED (מהירות), VOLUME (עוצמה), TRANSFORM (סיבוב/היפוך/לולאה/ייצוב), CANVAS (גודל ומיקום).\nSelecting a clip shows TRIM, SPEED, VOLUME, TRANSFORM, CANVAS sections in the right pane."));
        return WrapSection(p);
    }

    private UIElement CanvasSection()
    {
        var p = MakePane("גודל ומיקום בקנבס", "Resize and reposition the clip within the project format.");
        p.Children.Add(MakeNotice("למה זה שימושי  ·  Why this matters",
            "בחרת פורמט Reels אנכי 9:16, אבל הסרטון שלך אופקי 16:9 — יוצא עם פסים שחורים מלמעלה ולמטה. עם Canvas Transform אתה יכול להגדיל את הסרטון, להזיז אותו, ולחתוך את החלק הלא חשוב. ככה הסרטון ממלא את הפורמט.\nYou picked Reels 9:16 but your video is landscape 16:9 → black bars top and bottom. With Canvas Transform you can zoom in, shift, and crop the part you don't need — making the clip fit the format exactly the way you want.",
            Accent));
        p.Children.Add(MakeNumberedStep(1,
            "בחר קליפ  ·  Select a clip",
            "לחץ על קליפ בציר הזמן. בתצוגה המקדימה יופיעו 4 ריבועים לבנים בפינות וגם מסגרת סגולה — אלה הידיות לשליטה.\nClick a clip on the timeline. Four white squares appear at the corners of the displayed video, with a purple dashed border."));
        p.Children.Add(MakeNumberedStep(2,
            "גרור פינה להגדלה/הקטנה  ·  Drag a corner to resize",
            "גרור פינה החוצה — הסרטון גדל. גרור פנימה — הסרטון קטן. הסקלה תמיד מהמרכז של הקנבס.\nDrag a corner outward to enlarge, inward to shrink. The scale is uniform — width and height grow together."));
        p.Children.Add(MakeNumberedStep(3,
            "גרור באמצע להזיז  ·  Drag the middle to move",
            "לחיצה על אזור הסרטון (לא על ידית) + גרירה = הזזה לכל כיוון. החלק שיוצא מהקנבס לא יראה בסרטון הסופי.\nClick the video itself (not a handle) and drag to move it around. Anything that goes outside the canvas borders won't appear in the final video."));
        p.Children.Add(MakeNumberedStep(4,
            "גלגלת עכבר לזום  ·  Mouse wheel to zoom",
            "סובב את גלגלת העכבר על הסרטון כדי להגדיל/להקטין. Ctrl+גלגלת = צעדים קטנים יותר ועדינים יותר.\nMouse-wheel on the preview to zoom. Ctrl+wheel = finer step."));
        p.Children.Add(MakeNumberedStep(5,
            "איפוס  ·  Reset",
            "דאבל-קליק או קליק ימני על הסרטון = איפוס מלא. גם כפתור Reset בקטע CANVAS בפאנל הימני.\nDouble-click or right-click the video to reset. Also a Reset button in the right-pane CANVAS section."));
        p.Children.Add(MakeBullet("הסליידרים בפאנל ימין (Zoom / Offset X / Offset Y)  ·  Sliders in right pane (Zoom / Offset X / Offset Y) — חלופה מדויקת לגרירה.\nPrecise alternative to dragging."));
        return WrapSection(p);
    }

    private UIElement TextOverlaysSection()
    {
        var p = MakePane("טקסט על הסרטון", "Add text overlays — non-destructive, with full timeline control.");
        p.Children.Add(MakeNumberedStep(1,
            "פתח את ה-Picker  ·  Open the visual picker",
            "סיידבר → \"🔤 Add Text\". נפתח חלון עם פריים נוכחי של הסרטון. תקליד טקסט, תבחר פונט/גודל/צבע/רקע, ותגרור את הטקסט על הפריים למיקום שאתה רוצה.\nSidebar → \"🔤 Add Text\". A picker opens with the current frame. Type text, pick font/size/colour/background, drag the text onto the frame."));
        p.Children.Add(MakeNumberedStep(2,
            "אישור  ·  Save",
            "לחץ \"Add text\". הטקסט מופיע בתצוגה המקדימה וגם כפס טורקיז ברצועה משלו בציר הזמן.\nClick \"Add text\". The text appears in the preview and as a teal bar on its own timeline row."));
        p.Children.Add(MakeNumberedStep(3,
            "שליטה בזמני הופעה  ·  Control when it appears",
            "גרור את הקצוות של הפס הטורקיז בציר הזמן כדי לקבוע מתי הטקסט מופיע ולכמה זמן. גרור באמצע הפס להזיז את כל הקטע.\nDrag the edges of the teal bar to set start/end. Drag the middle to shift the whole appearance window."));
        p.Children.Add(MakeNumberedStep(4,
            "עריכה  ·  Edit",
            "לחיצה ימנית על הפס → \"Edit text…\" פותח את ה-Picker עם הערכים הקיימים.\nRight-click the bar → \"Edit text…\" reopens the picker pre-filled with current values."));
        p.Children.Add(MakeNumberedStep(5,
            "מחיקה  ·  Delete",
            "לחיצה ימנית על הפס → \"Delete text\", או בחירה ולחיצה על Delete.\nRight-click → \"Delete text\", or select + Delete key."));
        p.Children.Add(MakeNotice("פעולה לא הרסנית  ·  Non-destructive",
            "טקסט מציר הזמן לא משנה את קובץ המקור. הוא נצרב לסרטון רק כשתעשה Export. אפשר תמיד למחוק או לערוך.\nTimeline text overlays don't touch the source file. They're burnt in only at Export time. Always reversible.",
            Success));
        return WrapSection(p);
    }

    private UIElement BlocksSection()
    {
        var p = MakePane("בלוקי הסתרה", "Cover sensitive areas — faces, phone numbers, license plates.");
        p.Children.Add(MakeNumberedStep(1,
            "הוסף בלוק  ·  Add a block",
            "סיידבר → \"◼ Add Hide Block\" (או קיצור B). מופיעה מסגרת שחורה על הסרטון.\nSidebar → \"◼ Add Hide Block\" (or press B). A black box appears on the preview."));
        p.Children.Add(MakeNumberedStep(2,
            "מיקום וגודל  ·  Position & size",
            "לחיצה על הבלוק = בחירה. גרור באמצע להזיז, גרור פינה לשנות גודל.\nClick the block to select. Drag the middle to move, corners to resize."));
        p.Children.Add(MakeNumberedStep(3,
            "בחר סגנון  ·  Pick a style",
            "בפאנל ה-Inspector בחר Mode:\n• Solid — מילוי בצבע אחיד (ברירת מחדל — שחור)\n• Blur — טשטוש (זכוכית קפואה)\n• Pixelate — פיקסול (אפקט מוזאיקה)\nPick a Mode in the Inspector: Solid (solid colour), Blur (frosted glass) or Pixelate (mosaic)."));
        p.Children.Add(MakeNumberedStep(4,
            "טווח זמן  ·  Time range",
            "בפאנל ה-Inspector → טופס START / END עם שדות שעה : דקה : שניה . מילי-שניות (כל שדה בנפרד למקסימום דיוק). או סמן \"Cover Whole Video\" לכיסוי לכל אורך הסרטון.\nFill in the START/END fields (hours : minutes : seconds . milliseconds). Or tick \"Cover Whole Video\" to cover the entire timeline."));
        p.Children.Add(MakeNumberedStep(5,
            "ייצוא  ·  Export",
            "בלחיצה על Export הבלוקים נצרבים לסרטון בדיוק לפי הגדרת הזמן.\nOn Export, FFmpeg renders the boxes precisely over the requested time range."));

        var modesTitle = new TextBlock { Text = "סוגי בלוקים  ·  Block modes", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Text), Margin = new Thickness(0, 22, 0, 12) };
        p.Children.Add(modesTitle);
        var modesRow = new Grid();
        modesRow.ColumnDefinitions.Add(new ColumnDefinition());
        modesRow.ColumnDefinitions.Add(new ColumnDefinition());
        modesRow.ColumnDefinitions.Add(new ColumnDefinition());
        var solid = MakeModeSwatch("Solid · מילוי", "צבע אחיד · Full colour", System.Windows.Media.Brushes.Black);
        Grid.SetColumn(solid, 0); modesRow.Children.Add(solid);
        var blur = MakeModeSwatch("Blur · טשטוש", "זכוכית קפואה · Frosted glass", new SolidColorBrush(Color.FromArgb(0xE6, 0x1A, 0x1F, 0x2C)));
        Grid.SetColumn(blur, 1); modesRow.Children.Add(blur);
        var pix = MakeModeSwatch("Pixelate · פיקסול", "מוזאיקה · Mosaic", new SolidColorBrush(Color.FromArgb(0xE6, 0x2A, 0x20, 0x18)));
        Grid.SetColumn(pix, 2); modesRow.Children.Add(pix);
        p.Children.Add(modesRow);

        p.Children.Add(MakeNotice("טיפ  ·  Tip",
            "כשהסרטון בעצירה (Pause), בלוק מוצג רק אם הקו הצהוב בטווח שלו — בדיוק כמו שיהיה בסרטון הסופי. אם בחרת בלוק ב-Inspector, הוא נראה גם מחוץ לטווח כדי שתוכל לערוך אותו.\nWhen paused, a block is visible only if the playhead is within its range — just like in the exported file. The block you have selected in the Inspector stays visible outside its range so you can still edit it.",
            Info));
        return WrapSection(p);
    }

    private UIElement AudioSection()
    {
        var p = MakePane("אודיו", "Sound — every clip has its own waveform in the audio lane.");
        p.Children.Add(MakeBullet("עוצמה לכל קליפ בנפרד  ·  Per-clip volume — בחר קליפ → סליידר VOLUME בפאנל הימני (0% עד 200%).\nSelect a clip → VOLUME slider in the right pane (0% – 200%)."));
        p.Children.Add(MakeBullet("עוצמה במהלך הניגון  ·  Master volume — סליידר 🔊 ב-topbar שולט בעוצמה הכוללת של התצוגה (לא משפיע על הייצוא).\nThe topbar 🔊 slider sets the preview master volume (doesn't affect export)."));
        p.Children.Add(MakeBullet("השתקת קליפ  ·  Mute a clip — בחר את הפס באודיו (A1) ולחץ Backspace, או הסליידר ל-0%.\nSelect the A1 audio bar and press Backspace, or drag VOLUME to 0%."));
        p.Children.Add(MakeBullet("הסרת אודיו  ·  Remove track — בכל קליפ אפשר להסיר את האודיו לחלוטין (סיידבר → \"🔇 Mute / Remove Audio\").\nFully strip audio from a clip (sidebar → \"🔇 Mute / Remove Audio\")."));
        p.Children.Add(MakeBullet("חילוץ אודיו  ·  Extract audio — שמירת האודיו של הקליפ כקובץ נפרד (MP3 / WAV / AAC / OGG / FLAC).\nSave a clip's audio as a standalone file."));
        p.Children.Add(MakeNotice("ניתוק אודיו  ·  Detach Audio",
            "לחיצה ימנית על פס אודיו ב-A1 → Detach Audio. האודיו הופך לקליפ נפרד שאפשר לגרור, לחתוך, להזיז ולמחוק בלי קשר לוידאו המקורי. שימושי כשרוצים להחליף בלעדית את הסאונד.\nRight-click an audio bar → Detach Audio. The audio splits off as its own clip — drag, trim, split, copy and delete independently of the video. Great for replacing the soundtrack.",
            Accent));
        return WrapSection(p);
    }

    private UIElement AiCaptionsSection()
    {
        var p = MakePane("כתוביות AI", "Auto-generate kinetic-typography captions — and translate them if you want.");
        p.Children.Add(MakeNotice("מה זה עושה  ·  What it does",
            "האפליקציה מקשיבה לדיבור בסרטון (באמצעות מודל בשם Whisper שרץ במחשב שלך, חינם), שולחת את הטקסט לבינה מלאכותית של גוגל (Gemini), והיא מחזירה כתוביות קצרות מסוג \"kinetic typography\" — קצרות, מודגשות, צבעוניות — בדיוק כמו בריילז וטיקטוק. הכתוביות מופיעות אוטומטית בציר הזמן ואפשר לערוך כל אחת בנפרד.\nThe app listens to the spoken audio (with a local model called Whisper, free), sends the transcript to Google's AI (Gemini), and gets back short kinetic-typography captions — punchy, coloured, ready for Reels/TikTok. Each caption appears as a teal bar on the timeline you can edit individually.",
            Accent));

        var setupTitle = new TextBlock { Text = "התקנה ראשונית  ·  First-time setup", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Text), Margin = new Thickness(0, 16, 0, 10) };
        p.Children.Add(setupTitle);
        p.Children.Add(MakeNumberedStep(1,
            "קבל מפתח API מגוגל (חינם)  ·  Get a free API key from Google",
            "פתח Settings → ✨ AI Captions → לחץ \"Get an API key\" — נפתח אתר Google AI Studio. התחבר עם חשבון Google, צור פרויקט חדש, ולחץ \"Create API key\". העתק את המפתח (מתחיל ב-AIza…).\nOpen Settings → ✨ AI Captions → click \"Get an API key\". Sign in with a Google account, create a project, click \"Create API key\". Copy the key (starts with AIza…)."));
        p.Children.Add(MakeNumberedStep(2,
            "הדבק והדלק  ·  Paste & test",
            "חזור ל-Settings → AI Captions, הדבק את המפתח בשדה \"API key\", ולחץ \"Test connection\". אם הכל בסדר תקבל הודעה ירוקה.\nBack in Settings → AI Captions, paste the key in the \"API key\" field and click \"Test connection\". You should get a green confirmation."));
        p.Children.Add(MakeNumberedStep(3,
            "(אופציונלי) מפתח גיבוי  ·  (Optional) Fallback key",
            "במקרה שאתה מגיע למכסה החינמית היומית (1500 בקשות), אפשר להוסיף מפתח שני מ-חשבון Google שונה. אזהרה: אם זה אותו חשבון, זה לא יעזור — המכסה משותפת לחשבון.\nIf you hit the 1500-requests-per-day cap, you can add a second key — from a DIFFERENT Google account. A second key from the same account does NOT help (quota is shared)."));

        var useTitle = new TextBlock { Text = "שימוש  ·  Using AI Captions", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Text), Margin = new Thickness(0, 20, 0, 10) };
        p.Children.Add(useTitle);
        p.Children.Add(MakeNumberedStep(1,
            "פתח חלון  ·  Open the dialog",
            "סיידבר → \"✨ AI Captions\". נפתח חלון עם 4 הגדרות.\nSidebar → \"✨ AI Captions\". A dialog with four options opens."));
        p.Children.Add(MakeNumberedStep(2,
            "בחר הגדרות  ·  Pick options",
            "• Source — איזה קליפ לתמלל (הקליפ שנבחר / כל הקליפים)\n• Language — באיזו שפה הדיבור בסרטון (Auto / Hebrew / English)\n• Whisper model — איכות התמלול. Tiny הכי מהיר אבל פחות מדויק, Large v3 Turbo הכי מדויק (800MB, מוריד פעם אחת)\n• Caption language — באיזו שפה תרצה את הכתוביות. \"Same as audio\" = אותה שפה. אחר = תרגום אוטומטי.\n• Source — which clips to transcribe (selected one / all)\n• Language — spoken language (Auto / Hebrew / English)\n• Whisper model — accuracy. Tiny fastest, Large v3 Turbo most accurate (800MB)\n• Caption language — what language the captions should be in. \"Same as audio\" or auto-translate."));
        p.Children.Add(MakeNumberedStep(3,
            "לחץ Generate  ·  Click Generate",
            "החלון מציג 3 שלבים: 1) תמלול האודיו ע\"י Whisper, 2) שליחה ל-Gemini ויצירת הכתוביות, 3) הוספה לציר זמן. בפעם הראשונה לוקח קצת יותר זמן כי האפליקציה מורידה את מודל ה-Whisper.\nThe dialog shows three steps: transcribe → generate → apply. First run downloads the chosen Whisper model — slower one-time cost."));
        p.Children.Add(MakeNumberedStep(4,
            "ערוך כתוביות בציר הזמן  ·  Edit on the timeline",
            "כל כתובית מופיעה כפס טורקיז בציר הזמן. לחיצה ימנית → Edit text כדי לשנות (גופן, צבע, מיקום, טקסט). לחיצה ימנית → Delete למחיקה. גרירת קצוות = שינוי זמן ההופעה.\nEach caption is a teal bar on the timeline. Right-click → Edit text to change text/font/colour/position. Right-click → Delete. Drag edges to change appearance time."));

        p.Children.Add(MakeNotice("דוגמת תרגום  ·  Translation example",
            "סרטון באנגלית של מורה בלי כתוביות → בחר Caption language = Hebrew. Whisper יתמלל באנגלית, Gemini יתרגם לעברית טבעית, וכל הכתוביות יופיעו בעברית בתחתית הסרטון. מצוין לתרגומי סדרות/קורסים שלא ימצאת להם כתוביות.\nEnglish video with no subtitles → set Caption language = Hebrew. Whisper transcribes in English, Gemini translates to natural Hebrew, captions land at the bottom in Hebrew. Perfect for fan-subbing courses or shows.",
            Info));

        p.Children.Add(MakeNotice("עלות  ·  Cost",
            "מודל ה-Whisper רץ במחשב שלך — חינם לחלוטין. השימוש ב-Gemini הוא בתוך המכסה החינמית של Google: כ-1500 בקשות ביום לכל חשבון. כל הפעלת AI Captions = בקשה אחת או שתיים. תוכל לראות כמה נשארו לך ב-Settings → AI Captions → Usage today.\nWhisper runs locally — totally free. Gemini falls under Google's free tier (~1500 requests/day per account). Each AI Captions run uses 1–2 requests. Counter in Settings → AI Captions → Usage today.",
            Success));
        return WrapSection(p);
    }

    private UIElement UrlSection()
    {
        var p = MakePane("הורדה מ-URL", "Import a video from YouTube, TikTok, etc.");
        p.Children.Add(MakeBullet("סיידבר → \"🌐 Import from URL\". הדבק את הקישור של הסרטון, לחץ Download.\nSidebar → \"🌐 Import from URL\". Paste the video URL and click Download."));
        p.Children.Add(MakeBullet("עובד עם: YouTube, TikTok, Twitter / X, Instagram, Facebook, Vimeo, Twitch, Reddit ועוד עשרות אתרים.\nWorks with YouTube, TikTok, Twitter/X, Instagram, Facebook, Vimeo, Twitch, Reddit, and dozens more."));
        p.Children.Add(MakeBullet("בפעם הראשונה האפליקציה מורידה את הכלי שעושה את ההורדה (yt-dlp, ~12MB) — חד-פעמי.\nFirst use downloads the yt-dlp downloader (~12MB) — one-time."));
        p.Children.Add(MakeBullet("גם קישורי .mp4 / .mov / .mkv ישירים עובדים — מורידים פשוט דרך HTTPS.\nDirect .mp4 / .mov / .mkv links work too — pulled over HTTPS."));
        p.Children.Add(MakeBullet("איכות: עד 1080p, האפליקציה בוחרת אוטומטית את האיכות הטובה ביותר ומאחדת וידאו + אודיו.\nQuality: up to 1080p. App auto-picks best video+audio and merges."));
        p.Children.Add(MakeBullet("בסיום ההורדה הסרטון נכנס אוטומטית לציר הזמן עם תמונות ממוזערות וגל קול.\nWhen done, the clip auto-lands on the timeline with thumbnails and waveform."));
        return WrapSection(p);
    }

    private UIElement ExportSection()
    {
        var p = MakePane("ייצוא", "Render everything into a final video file.");
        p.Children.Add(MakeBullet("לחץ Export הירוק למעלה מימין, או Ctrl+E.\nClick the green Export button at top-right, or press Ctrl+E."));
        p.Children.Add(MakeBullet("בחר איפה לשמור. ברירת מחדל — MP4 H.264 ברזולוציה של הקליפ הראשון.\nPick a destination. Default is MP4 H.264 at the first clip's resolution."));
        p.Children.Add(MakeBullet("פס ההתקדמות בטופ-בר זז בזמן אמת — אחוז + מספר. גם בתוך קליפ ארוך הוא יזוז (לא רק בין קליפים).\nThe topbar progress bar moves in real time — percent + number. It updates within a single long clip, not only between clips."));
        p.Children.Add(MakeBullet("הקידוד עצמו נעשה ע\"י ffmpeg.exe (תהליך נפרד). אם תפתח Task Manager — האפליקציה עצמה תהיה ב-0% CPU וה-ffmpeg יהיה ב-100%. זה תקין.\nThe encoding itself runs in a separate ffmpeg.exe process. Task Manager will show VideoEditor at 0% CPU and ffmpeg.exe doing the work — that's normal."));
        p.Children.Add(MakeNotice("האצת חומרה  ·  Hardware acceleration",
            "ב-Settings → Export → \"Hardware accel\" אפשר לבחור NVIDIA NVENC / Intel QuickSync / AMD AMF (תלוי בכרטיס המסך). זה מעביר את הקידוד ל-GPU — פי 3 עד 5 יותר מהיר ועומס CPU נמוך משמעותית.\nSettings → Export → Hardware accel: NVIDIA NVENC / Intel QuickSync / AMD AMF (depends on your GPU). Moves encoding to the GPU — 3-5x faster with much lower CPU.",
            Info));
        p.Children.Add(MakeBullet("צינור הייצוא: 1) מרנדר כל קליפ עם המסננים שלו  2) מחבר אותם בסדר ציר הזמן  3) צורב טקסטים  4) צורב בלוקי הסתרה.\nPipeline: per-clip render → concat → text overlays → hide blocks."));
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
        var p = MakePane("הגדרות", "Open via the ⚙ icon in the topbar or press , (comma).");
        var cats = new (string n, string heb, string en)[]
        {
            ("General",     "כללי", "שפה (עברית / אנגלית), ערכת נושא (Dark / Light), שמירה אוטומטית, אישור פעולות מסוכנות.\nLanguage (Hebrew / English), theme (Dark / Light), auto-save interval, confirm destructive actions."),
            ("Player",      "נגן", "עוצמת קול ברירת מחדל, איכות הגרירה (Smooth / Balanced / Accurate), הפעלה בלולאה, רזולוציית proxy.\nDefault volume, scrubbing quality, loop on end, proxy resolution."),
            ("Editor",      "עורך", "מצב Ripple (קליפים נצמדים זה לזה), סף הצמדה מגנטית, זום התחלתי, מספר תמונות ממוזערות.\nRipple-abut mode, magnetic snap threshold, initial zoom, thumbnails per clip."),
            ("Export",      "ייצוא", "מיכל פלט (MP4 / MOV / MKV), codec (H.264 / H.265 / AV1 / ProRes), איכות CRF, FPS, האצת חומרה (NVENC / QSV / AMF), 2-pass.\nContainer (MP4/MOV/MKV), codec, CRF quality, FPS, hardware acceleration, 2-pass."),
            ("AI Captions", "כתוביות AI", "מפתח Gemini הראשי, מפתח גיבוי (לחשבון Google שני), בדיקת חיבור, ספירת בקשות יומית, מדריך השגת מפתח.\nGemini API key, fallback key (different Google account), test-connection, daily usage counter, guide on how to get a key."),
            ("Storage",     "אחסון", "תיקיות שמירה — פרויקטים, הורדות, מטמון.\nFolders for projects, downloads, cache."),
            ("FFmpeg",      "FFmpeg", "סטטוס הבינארי + ה-codecs המותקנים.\nBinary status + installed encoders."),
            ("Keyboard",    "מקלדת", "כל קיצורי המקלדת — מקובץ לפי פעולה.\nFull keyboard cheat-sheet."),
            ("Updates",     "עדכונים", "גרסה נוכחית של האפליקציה.\nCurrent app version."),
            ("About",       "אודות", "רישיון MIT, כתובת ה-Repo.\nMIT licence, repo link."),
        };
        foreach (var (n, heb, en) in cats)
            p.Children.Add(MakeToolCard(n + " · " + heb, en));
        return WrapSection(p);
    }
    private UIElement FFmpegSection()
    {
        var p = MakePane("FFmpeg", "What works behind the scenes.");
        p.Children.Add(MakeNotice("מה זה FFmpeg?  ·  What is FFmpeg?",
            "תוכנת קוד פתוח חזקה לעיבוד וידאו ואודיו. כמעט כל אפליקציית עריכה בעולם (כולל יוטיוב עצמה) משתמשת בה. אצלנו היא מבצעת את כל הקידוד והפילטרים, כך שהאפליקציה עצמה נשארת קלה ומהירה.\nA powerful open-source media-processing tool used by almost every video app in existence (YouTube itself uses it). It does all the encoding and filtering for us — our app stays light.",
            Info));
        p.Children.Add(MakeBullet("בפעם הראשונה האפליקציה מורידה את ffmpeg.exe + ffprobe.exe (~150MB) לתיקיית \"ffmpeg\" שליד ה-EXE. חד-פעמי.\nFirst run downloads ffmpeg.exe + ffprobe.exe (~150 MB) into the ffmpeg/ folder next to the EXE. One-time."));
        p.Children.Add(MakeBullet("גם yt-dlp (להורדה מיוטיוב) ו-whisper.cpp (לתמלול) מורדים אוטומטית בפעם הראשונה. הכל חינם וקוד פתוח.\nyt-dlp (URL imports) and whisper.cpp (AI Captions transcription) also auto-install on first use. All free + open source."));
        p.Children.Add(MakeBullet("המיקום של הקבצים נמצא ב-Settings → FFmpeg.\nFile paths shown in Settings → FFmpeg."));
        p.Children.Add(MakeBullet("בעיות? אם משהו לא עובד, נסה למחוק את התיקייה \"ffmpeg\" וההפעלה הבאה תוריד מחדש.\nTroubleshooting: delete the ffmpeg/ folder and the app will re-download on next launch."));
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
