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
        ["Recording source"] = "מקור ההקלטה",
        ["Entire desktop (all monitors)"] = "כל המסכים",
        ["Capture a monitor - or the whole virtual desktop - using gdigrab"] = "הקלטת מסך ספציפי או של כל הצגים יחד",
        ["Saved"] = "נשמר",
        ["Recording saved"] = "ההקלטה נשמרה",
        ["Screen recording saved"] = "הקלטת המסך נשמרה",
        ["Export complete"] = "הייצוא הסתיים",
        ["Choose what to do next with your video."] = "בחר מה לעשות עם הסרטון עכשיו.",
        ["Edit it in the timeline, share it, or just keep it on disk."] = "ערוך בציר הזמן, שתף, או רק שמור על הדיסק.",
        ["Pick where to send it - your editor project is still open."] = "בחר לאן לשלוח אותו - הפרויקט בעורך עדיין פתוח.",
        ["🎬 Open in editor"] = "🎬 פתח בעורך",
        ["📁 Open folder"] = "📁 פתח תיקייה",
        ["Upload to a platform - opens the upload page in your browser:"] = "העלאה לפלטפורמה - פותח את עמוד ההעלאה בדפדפן:",
        ["Tip: when the upload page opens, drag the file from \"Open folder\" onto it."] = "טיפ: כשעמוד ההעלאה ייפתח, גרור את הקובץ מ\"פתח תיקייה\" לתוכו.",
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
        ["START"] = "התחלה",
        ["END"] = "סיום",
        ["TRIM - IN / OUT"] = "חיתוך - התחלה / סוף",
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

        ["Add Text"] = "הוספת טקסט",
        ["Drag the text on the video to position it. The result burns into the clip on export."] = "גרור את הטקסט על הוידאו כדי למקם אותו. התוצאה נצרבת לקליפ בייצוא.",
        ["Drag the text on the video to position it."] = "גרור את הטקסט על הוידאו כדי למקם אותו.",
        ["TEXT"] = "טקסט",
        ["SIZE"] = "גודל",
        ["STYLE"] = "סגנון",
        ["TEXT COLOR"] = "צבע טקסט",
        ["BACKGROUND HIGHLIGHT"] = "רקע הדגשה",
        ["Add text"] = "הוסף טקסט",
        ["Text overlay added. Drag the teal bar on the timeline to move or resize it."] = "טקסט נוסף. גרור את הבר הטורקיז בציר הזמן כדי להזיז או לשנות אורך.",
        ["Text overlay updated."] = "הטקסט עודכן.",
        ["Text overlay deleted."] = "הטקסט נמחק.",
        ["Edit text…"] = "ערוך טקסט…",
        ["Delete text"] = "מחק טקסט",
        ["Add a video clip first."] = "הוסף קליפ וידאו קודם.",

        ["Choose project format"] = "בחר פורמט פרויקט",
        ["Pick the canvas you'll be editing for. You can change this anytime."] = "בחר את המסגרת שלפיה תערוך. אפשר לשנות בכל רגע.",
        ["Use this format"] = "השתמש בפורמט הזה",
        ["Cancel"] = "ביטול",
        ["Source - match first clip"] = "מקור - לפי הקליפ הראשון",
        ["Source"] = "מקור",
        ["YouTube · 16:9"] = "YouTube · 16:9",
        ["Reels / TikTok / Shorts · 9:16"] = "Reels / TikTok / Shorts · 9:16",
        ["Instagram Square · 1:1"] = "אינסטגרם ריבוע · 1:1",
        ["Instagram Portrait · 4:5"] = "אינסטגרם פורטרט · 4:5",
        ["Cinematic · 21:9"] = "קולנועי · 21:9",
        ["Custom W×H"] = "מותאם אישית W×H",
        ["Match the first clip"] = "התאם לקליפ הראשון",
        ["Landscape video for desktop / TV"] = "וידאו אופקי למחשב / טלוויזיה",
        ["Vertical video for phones"] = "וידאו אנכי לפלאפונים",
        ["Square feed post"] = "פוסט ריבועי בפיד",
        ["Tall feed post"] = "פוסט גבוה בפיד",
        ["Ultrawide cinematic letterbox"] = "ייצוג קולנועי רחב במיוחד",
        ["Pick any width and height"] = "בחר רוחב וגובה כרצונך",
        ["Width"] = "רוחב",
        ["Height"] = "גובה",
        ["Change project format"] = "שנה פורמט פרויקט",
        ["FIT"] = "התאמה",
        ["Contain"] = "הכל בפנים",
        ["Cover"] = "מילוי + חיתוך",
        ["Blur bg"] = "רקע מטושטש",
        ["Preview shows the target canvas. Cover/Blur effects render on export."] = "התצוגה המקדימה מציגה את מסגרת היעד. אפקטים של מילוי וטשטוש יוצרו בייצוא.",

        // ----- AI Captions -----
        ["AI Captions"] = "כתוביות AI",
        ["Auto-caption with an LLM"] = "כתוביות אוטומטיות עם LLM",
        ["Auto-generate short on-screen captions from the spoken audio. Uses Google Gemini (free tier)."] = "צור אוטומטית כתוביות קצרות על המסך מתוך הדיבור בסרטון. משתמש ב-Google Gemini (חינמי).",
        ["Auto-generate short on-screen captions with Gemini"] = "צור אוטומטית כתוביות קצרות עם Gemini",
        ["Provider"] = "ספק",
        ["Google Gemini · free tier (1500 requests/day on gemini-2.0-flash)"] = "Google Gemini · חינמי (1500 בקשות ביום על gemini-2.0-flash)",
        ["API key"] = "מפתח API",
        ["Paste your Gemini API key - starts with AIza… · stored in settings.json next to the EXE"] = "הדבק את מפתח ה-API של Gemini - מתחיל ב-AIza… · נשמר ב-settings.json ליד קובץ ההפעלה",
        ["Fallback API key (optional)"] = "מפתח גיבוי (אופציונלי)",
        ["Used automatically when the primary key hits its daily quota. MUST be from a DIFFERENT Google account - a key from the same account shares the same quota and won't help."] = "ייכנס לפעולה אוטומטית כשהמפתח הראשי מגיע למכסה היומית. חייב להיות מחשבון Google אחר - מפתח מאותו חשבון חולק את אותה מכסה ולא יעזור.",
        ["⚠ The fallback key has to be from a SECOND Google account. Two keys from the same Google account share the same daily quota - adding a sibling key from the same account does NOT increase how many captions you can generate."] = "⚠ מפתח הגיבוי חייב להיות מחשבון Google שני. שני מפתחות מאותו חשבון חולקים את אותה מכסה יומית - הוספת מפתח נוסף מאותו חשבון לא תגדיל את מספר הכתוביות שתוכל ליצור.",
        ["Get an API key"] = "השג מפתח API",
        ["Test connection"] = "בדוק חיבור",
        ["Quick actions"] = "פעולות מהירות",
        ["Open Google AI Studio · ping the API with your key"] = "פתח את Google AI Studio · בדוק את ה-API עם המפתח שלך",
        ["How to get an API key"] = "איך להשיג מפתח API",
        ["1.  Go to https://aistudio.google.com/apikey"] = "1.  היכנס ל-https://aistudio.google.com/apikey",
        ["2.  Sign in with a Google account."] = "2.  התחבר עם חשבון Google.",
        ["3.  Click \"Create API key\" → \"Create API key in new project\"."] = "3.  לחץ על \"Create API key\" → \"Create API key in new project\".",
        ["4.  Copy the key (starts with AIza…)."] = "4.  העתק את המפתח (מתחיל ב-AIza…).",
        ["5.  Paste it in the field above and click Save."] = "5.  הדבק אותו בשדה למעלה ולחץ שמור.",
        ["Paste an API key first."] = "הדבק קודם מפתח API.",
        ["Contacting Gemini…"] = "מתחבר ל-Gemini…",
        ["OK - the key is valid."] = "OK - המפתח תקין.",
        ["OK - using {0}"] = "OK - משתמש ב-{0}",
        ["Usage today"] = "שימוש היום",
        ["Counted by this app · Google free tier is ~1500 requests/day, resets at midnight"] = "נספר ע\"י האפליקציה · המכסה החינמית של Google היא ~1500 בקשות ביום, מתאפסת בחצות",
        ["{0} requests today"] = "{0} בקשות היום",
        ["Refresh"] = "רענן",
        ["Gemini · {0} requests today (free tier ~1500/day)"] = "Gemini · {0} בקשות היום (מכסה חינמית ~1500 ליום)",
        ["Gemini answered but the response was unexpected."] = "Gemini ענה אך התשובה לא הייתה כצפוי.",
        ["Set your Gemini API key first - opening Settings…"] = "הגדר קודם את מפתח ה-Gemini - פותח את ההגדרות…",
        ["AI Captions added · {0} overlays - drag bars on the timeline to tweak."] = "כתוביות AI נוספו · {0} שכבות - גרור את הברים בציר הזמן כדי לשנות.",
        ["Whisper model"] = "מודל Whisper",
        ["Caption language"] = "שפת הכתוביות",
        ["Same as audio (no translation)"] = "כשפת הדיבור (בלי תרגום)",
        ["Hebrew"] = "עברית",
        ["English"] = "אנגלית",
        ["Arabic"] = "ערבית",
        ["Spanish"] = "ספרדית",
        ["French"] = "צרפתית",
        ["Russian"] = "רוסית",
        ["Portuguese"] = "פורטוגזית",
        ["German"] = "גרמנית",
        ["Step 1/3 - Transcribing audio"] = "שלב 1/3 - תמלול האודיו",
        ["Step 2/3 - Generating captions with Gemini"] = "שלב 2/3 - יצירת כתוביות עם Gemini",
        ["Step 3/3 - Applying overlays"] = "שלב 3/3 - החלת השכבות",
        ["Sending {0} segments to Gemini…"] = "שולח {0} מקטעים ל-Gemini…",
        ["No video clips to transcribe."] = "אין קליפי וידאו לתמלול.",
        ["Whisper produced no segments - is there spoken audio?"] = "Whisper לא הפיק מקטעים - האם יש דיבור בסרטון?",
        ["Done · {0} overlays generated."] = "סיים · {0} שכבות נוצרו.",
        ["Cancelled."] = "בוטל.",
        ["Gemini took too long to respond. Try a shorter clip or run AI Captions again - Whisper's output is cached for this run."] = "Gemini לקח יותר מדי זמן לענות. נסה קליפ קצר יותר או הרץ שוב - Whisper לא צריך לרוץ מחדש.",
        ["Stop"] = "עצור",
        ["Generate"] = "צור",

        ["Recorder"] = "מקליט",
        ["Camera Recorder"] = "מצלמה",
        ["Click the preview to open the recorder"] = "לחץ על התצוגה כדי לפתוח את המקליט",
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
