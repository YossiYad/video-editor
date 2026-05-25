using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VideoEditor.Services;

internal static class Localization
{
    private static readonly Dictionary<string, string> He = new(StringComparer.Ordinal)
    {
        ["Settings"] = "הגדרות",
        ["App preferences - press Save to apply"] = "העדפות אפליקציה - לחץ שמור כדי להחיל",
        ["No unsaved changes"] = "אין שינויים שלא נשמרו",
        ["Unsaved changes"] = "שינויים שלא נשמרו",
        ["Settings saved"] = "ההגדרות נשמרו",
        ["Save"] = "שמור",
        ["Close"] = "סגור",
        ["General"] = "כללי",
        ["Player"] = "נגן",
        ["Editor"] = "עורך",
        ["Export"] = "ייצוא",
        ["Storage"] = "אחסון",
        ["FFmpeg"] = "FFmpeg",
        ["Keyboard"] = "מקלדת",
        ["Updates"] = "עדכונים",
        ["About"] = "אודות",
        ["App basics"] = "בסיס האפליקציה",
        ["Playback & seek"] = "ניגון ודילוג",
        ["Snap, ripple, defaults"] = "הצמדה, הזזה וברירות מחדל",
        ["Codec & quality"] = "קודק ואיכות",
        ["Cache & paths"] = "מטמון ונתיבים",
        ["Binaries & encoders"] = "קבצים ומקודדים",
        ["Shortcuts"] = "קיצורים",
        ["Channel & changelog"] = "ערוץ ועדכונים",
        ["License & credits"] = "רישיון וקרדיטים",
        ["Language"] = "שפה",
        ["Applied when you press Save"] = "מוחל לאחר לחיצה על שמור",
        ["Theme"] = "ערכת נושא",
        ["On startup"] = "בפתיחה",
        ["Confirm destructive actions"] = "אשר פעולות הרסניות",
        ["Auto-save"] = "שמירה אוטומטית",
        ["Send anonymous usage stats"] = "שליחת נתוני שימוש אנונימיים",
        ["Default volume"] = "עוצמת קול ברירת מחדל",
        ["Back / Forward step"] = "צעד אחורה / קדימה",
        ["Scrubbing quality"] = "איכות גרירה",
        ["Audio scrubbing"] = "גרירת אודיו",
        ["Loop on end"] = "לולאה בסיום",
        ["Proxy media"] = "מדיה Proxy",
        ["Ripple-abut clips on drop"] = "הצמד קליפים בגרירה",
        ["Magnetic snap threshold"] = "סף הצמדה מגנטית",
        ["Initial zoom"] = "זום התחלתי",
        ["Show waveform per clip"] = "הצג גל קול לכל קליפ",
        ["Thumbnails per clip"] = "תמונות ממוזערות לכל קליפ",
        ["Default block opacity"] = "אטימות ברירת מחדל לבלוק",
        ["Container"] = "מיכל",
        ["Video codec"] = "קודק וידאו",
        ["Quality (CRF)"] = "איכות (CRF)",
        ["Frame rate"] = "קצב פריימים",
        ["Audio bitrate"] = "ביטרייט אודיו",
        ["Hardware accel"] = "האצת חומרה",
        ["2-pass encoding"] = "קידוד בשני מעברים",
        ["Audio loudnorm"] = "נרמול עוצמת אודיו",

        ["Timeline"] = "ציר זמן",
        ["Hide Blocks"] = "בלוקי הסתרה",
        ["Zoom out"] = "הקטן זום",
        ["Zoom in"] = "הגדל זום",
        ["Fit to view"] = "התאם לתצוגה",
        ["Fit"] = "התאם",
        ["0 clips · 0 blocks · 00:00.000"] = "0 קליפים · 0 בלוקים · 00:00.000",
        ["0 tracks"] = "0 מסלולים",

        ["Open"] = "פתח",
        [" Open"] = " פתח",
        ["Hide Block"] = "בלוק הסתרה",
        ["Add Hide Block"] = "הוסף בלוק הסתרה",
        ["Delete Block"] = "מחק בלוק",
        ["WORKSPACE"] = "סביבת עבודה",
        ["CAPTURE"] = "לכידה",
        ["TRANSFORM & TRIM"] = "עריכה וחיתוך",
        ["OVERLAYS"] = "שכבות",
        ["AUDIO"] = "אודיו",
        ["HIDE BLOCKS"] = "בלוקי הסתרה",
        ["SPLIT"] = "פיצול",
        ["VideoEditor"] = "עורך וידאו",
        ["Video Editor"] = "עורך וידאו",
        ["Import from URL"] = "ייבוא מכתובת URL",
        ["Screen Recorder"] = "הקלטת מסך",
        ["Text to Speech"] = "טקסט לדיבור",
        ["Video Recorder"] = "הקלטת וידאו",
        ["Merge Videos"] = "מיזוג סרטונים",
        ["Trim Video"] = "חיתוך סרטון",
        ["Crop Video"] = "חיתוך תמונה",
        ["Rotate Video"] = "סיבוב וידאו",
        ["Flip Video"] = "היפוך וידאו",
        ["Resize Video"] = "שינוי גודל",
        ["Loop Video"] = "לולאה",
        ["Change Speed"] = "שינוי מהירות",
        ["Stabilize"] = "ייצוב",
        ["Remove Logo"] = "הסרת לוגו",
        ["Add Image"] = "הוספת תמונה",
        ["Add Text"] = "הוספת טקסט",
        ["Add Audio"] = "הוספת אודיו",
        ["Change Volume"] = "שינוי עוצמת קול",
        ["Extract Audio"] = "חילוץ אודיו",
        ["Mute / Remove Audio"] = "השתקה / הסרת אודיו",
        ["Split at Playhead"] = "פצל בנקודת הניגון",
        ["Block"] = "בלוק",
        ["Clip"] = "קליפ",
        ["Output settings"] = "הגדרות ייצוא",
        ["Select a clip or block to edit it."] = "בחר קליפ או בלוק כדי לערוך אותו.",
        ["FORMAT"] = "פורמט",
        ["RESOLUTION"] = "רזולוציה",
        ["FPS"] = "FPS",
        ["PROJECT DURATION"] = "משך הפרויקט",
        ["CLIPS"] = "קליפים",
        ["CODEC"] = "קודק",
        ["BLOCKS"] = "בלוקים",
        ["ready"] = "מוכן",
        ["Play"] = "נגן",
        ["Delete"] = "מחק",
        ["Copy/Paste"] = "העתק/הדבק",
        ["Ready · drop video files to add"] = "מוכן · גרור קבצי וידאו כדי להוסיף",

        ["Renders over video at export"] = "מוצג מעל הווידאו בייצוא",
        ["LABEL"] = "תווית",
        ["MODE"] = "מצב",
        ["Solid"] = "אטום",
        ["Blur"] = "טשטוש",
        ["Pixelate"] = "פיקסול",
        ["COLOR"] = "צבע",
        ["STRENGTH"] = "עוצמה",
        ["Subtle"] = "עדין",
        ["Soft"] = "רך",
        ["Heavy"] = "כבד",
        ["Cover whole timeline"] = "כיסוי כל ציר הזמן",
        ["START (s)"] = "התחלה (שניות)",
        ["END (s)"] = "סיום (שניות)",
        ["TRIM — IN / OUT"] = "חיתוך — התחלה / סוף",
        ["SPEED"] = "מהירות",
        ["VOLUME"] = "עוצמת קול",
        ["TRANSFORM"] = "טרנספורם",
        ["EXPORT-TIME EFFECTS"] = "אפקטים בייצוא",
        ["ARRANGE"] = "סידור",
        ["QUALITY (CRF)"] = "איכות (CRF)",
        ["Visually lossless"] = "ללא איבוד נראה",
        ["Smaller file"] = "קובץ קטן יותר",
        ["Export project"] = "ייצוא הפרויקט",
        ["Extract project audio only"] = "חילוץ אודיו בלבד מהפרויקט",
        ["Source"] = "מקור",
        ["No clip loaded"] = "לא נטען קליפ",
        ["FFmpeg 6.1 · ffprobe ready"] = "FFmpeg 6.1 · ffprobe מוכן",
        ["NEW"] = "חדש",
        ["2-pass"] = "2 מעברים",
        ["Split into N Parts…"] = "פיצול ל-N חלקים…",
        ["Open video files"] = "פתח קבצי וידאו",
        ["Export project (Ctrl+E)"] = "ייצוא הפרויקט (Ctrl+E)",
        ["Settings · ,"] = "הגדרות · ,",
        ["User Guide · ?"] = "מדריך למשתמש · ?",
        ["Drag video files here"] = "גרור קבצי וידאו לכאן",
        ["Fit"] = "התאם",
    };

    public static bool IsHebrew =>
        AppSettings.Language == "he" ||
        (AppSettings.Language == "auto" &&
         System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "he");

    private static Dictionary<string, string>? _heToEn;
    private static Dictionary<string, string> HeToEn
    {
        get
        {
            if (_heToEn != null) return _heToEn;
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in He) d[kv.Value] = kv.Key;
            _heToEn = d;
            return d;
        }
    }

    public static string T(string text)
    {
        if (!IsHebrew) return text;
        if (He.TryGetValue(text, out var he)) return he;
        return text;
    }

    private static string Translate(string text, bool toHebrew)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (toHebrew)
        {
            return He.TryGetValue(text, out var he) ? he : text;
        }
        return HeToEn.TryGetValue(text, out var en) ? en : text;
    }

    public static void TranslateTree(DependencyObject root)
    {
        bool toHebrew = IsHebrew;
        var seen = new HashSet<DependencyObject>();

        void Recurse(DependencyObject? node)
        {
            if (node == null || !seen.Add(node)) return;

            if (node is TextBlock tb && !string.IsNullOrEmpty(tb.Text))
                tb.Text = Translate(tb.Text, toHebrew);
            else if (node is HeaderedContentControl hc && hc.Header is string hs)
                hc.Header = Translate(hs, toHebrew);
            else if (node is ContentControl cc && cc.Content is string s)
                cc.Content = Translate(s, toHebrew);

            if (node is FrameworkElement fe && fe.ToolTip is string tip)
                fe.ToolTip = Translate(tip, toHebrew);

            foreach (var child in LogicalTreeHelper.GetChildren(node))
                if (child is DependencyObject d) Recurse(d);

            var count = 0;
            try { count = VisualTreeHelper.GetChildrenCount(node); } catch { }
            for (var i = 0; i < count; i++) Recurse(VisualTreeHelper.GetChild(node, i));
        }

        Recurse(root);
    }
}
