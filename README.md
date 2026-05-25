# Video Editor — Pro

[![Download Latest](https://img.shields.io/github/v/release/YossiYad/video-editor?label=%E2%AC%87%EF%B8%8F%20Download%20Latest&style=for-the-badge&color=2ea44f)](https://github.com/YossiYad/video-editor/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=for-the-badge)](LICENSE)
[![Open Source](https://img.shields.io/badge/Open%20Source-%E2%9D%A4-red?style=for-the-badge)](https://github.com/YossiYad/video-editor)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078d6?style=for-the-badge&logo=windows)](https://github.com/YossiYad/video-editor/releases/latest)

A free, open-source desktop video editor for Windows. Built on C# / WPF with FFmpeg under the hood. Designed to be powerful for creators but approachable for first-time editors.

## ⬇️ Download

**[Click here to download the latest release](https://github.com/YossiYad/video-editor/releases/latest)** — extract the ZIP and run `VideoEditor.exe`. No installer, no admin rights. Everything you need (FFmpeg, yt-dlp, whisper.cpp) downloads automatically on first use.

## What it does

### Editing
- **Multi-clip timeline** — drag-drop video files, reorder, trim by dragging edges, split with **S**, free dragging or ripple-abut mode.
- **Non-destructive properties** per clip: in / out, speed (0.25× – 4×), volume, rotate (90 / 180 / 270°), flip H / V, loop. Source file is never touched until export.
- **Canvas Transform** — for each clip individually: zoom and position the video on the project canvas. Drag the preview to move, scroll-wheel to zoom, drag the corner handles to resize. Great for placing a 16:9 video inside a 9:16 Reels canvas without black bars.
- **Project formats** — YouTube 16:9, Reels / TikTok / Shorts 9:16, Instagram Square 1:1, Portrait 4:5, Cinematic 21:9, or custom. Fit mode: Contain / Cover / Blurred-background.
- **Hide blocks** — cover faces, phone numbers, license plates with Solid colour, Blur, or Pixelate. Per-block time range with hours : minutes : seconds . milliseconds inputs.
- **Text overlays** — visual picker with drag-on-frame positioning. Each overlay is a draggable bar on the timeline you control when it appears.

### AI Captions (✨ NEW)
- Auto-generate **kinetic-typography captions** from the spoken audio. Whisper.cpp transcribes locally (free, 5 model sizes from Tiny 75 MB to Large-v3-Turbo 800 MB), Google Gemini turns the transcript into punchy short captions in the kinetic-typography style.
- **Auto-fixes Hebrew transcription errors** by giving Gemini full sentence context.
- **Caption translation** — generate Hebrew captions for an English video (or any of: Hebrew, English, Arabic, Spanish, French, Russian, Portuguese, German). Perfect for fan-subbing.
- **Fallback API key** — paste a second Gemini key from a *different* Google account to double your free-tier quota.
- **Daily usage counter** so you know how close you are to the free-tier cap (~1500 requests / day).

### Capture
- **Screen Recorder** — record any region of your screen.
- **Webcam Recorder** — record from any DirectShow-compatible camera.
- **Text-to-Speech** — Windows SAPI voices, saved as WAV for narration.
- **URL Import** — paste any YouTube / TikTok / Vimeo / Twitter / Instagram / Facebook / Twitch / Reddit URL. yt-dlp downloads automatically.

### Per-clip tools
Crop · Resize · Rotate · Flip · Loop · Speed · Stabilize · Remove Logo · Add Image · Add Text · Add Audio · Extract Audio · Mute · Change Volume.

### Export
- H.264 (default), H.265 / HEVC, AV1, or ProRes 422. MP4 / MOV / MKV / WebM container.
- **Hardware acceleration**: NVIDIA NVENC, Intel QuickSync, AMD AMF — 3–5× faster encoding.
- Real-time progress bar with percent label.
- CRF, frame rate (24 / 25 / 30 / 50 / 60 fps), audio bitrate, 2-pass, loudnorm all configurable.

### UI
- Bilingual — English / Hebrew (with RTL layout for Hebrew).
- Light / Dark / Auto theme with live switching.
- Customisable keyboard shortcuts.
- All settings persisted to `settings.json` next to the EXE.

## Requirements

- Windows 10 / 11 (x64).
- ~500 MB free disk space (FFmpeg + the optional Whisper model you choose).
- Optional: an NVIDIA / Intel / AMD GPU for hardware-accelerated export.
- Optional: a free Google account for AI Captions (Gemini's free tier is generous).

## Built from source

```powershell
git clone https://github.com/YossiYad/video-editor.git
cd video-editor
dotnet run --project VideoEditor
```

On first launch the app auto-downloads `ffmpeg.exe` + `ffprobe.exe` into `bin\Debug\net8.0-windows\ffmpeg\`. If the download fails, grab them from <https://www.gyan.dev/ffmpeg/builds/> and drop both binaries in that folder.

## Project layout

```
VideoEditor/
├── App.xaml(.cs)                  - Theme + FFmpeg auto-install
├── MainWindow.xaml(.cs)           - Main window
├── Models/                        - VideoClip, VideoBlock, TextOverlay, SubtitleSegment
├── Services/
│   ├── FFmpegService.cs           - All video operations via FFmpeg
│   ├── WhisperService.cs          - Speech-to-text via whisper.cpp
│   ├── LlmCaptionService.cs       - Google Gemini integration
│   ├── AppSettings.cs             - settings.json persistence
│   ├── Localization.cs            - English / Hebrew strings
│   └── ProjectFormats.cs          - 16:9 / 9:16 / 1:1 / 4:5 / 21:9 presets
├── Controls/
│   ├── ResizableBlock.xaml(.cs)   - Draggable hide block on preview
│   └── Timeline.xaml(.cs)         - Timeline with clips / blocks / texts
└── Views/                         - All dialogs (Settings, AI Captions, etc.)
```

## Contributing

This is **open source** — contributions are welcome!

- 🐛 **Bug reports**: [Open an issue](https://github.com/YossiYad/video-editor/issues/new)
- 💡 **Feature requests**: [Open an issue](https://github.com/YossiYad/video-editor/issues/new) tagged `enhancement`
- 🔧 **Pull requests**: fork → branch → PR
- ⭐ **Star the repo** to show support

## License

[MIT](LICENSE) — use it commercially, modify it, redistribute it, contribute back. Just keep the copyright notice.

---

# עורך וידאו — Pro

[![הורדה](https://img.shields.io/github/v/release/YossiYad/video-editor?label=%E2%AC%87%EF%B8%8F%20%D7%94%D7%95%D7%A8%D7%93%D7%94%20%D7%90%D7%97%D7%A8%D7%95%D7%A0%D7%94&style=for-the-badge&color=2ea44f)](https://github.com/YossiYad/video-editor/releases/latest)

עורך וידאו **חינמי וקוד פתוח** ל-Windows. בנוי על C# / WPF עם FFmpeg מאחורי הקלעים. מתוכנן להיות חזק ליוצרי תוכן וגם נגיש למתחילים שלא ערכו וידאו מימיהם.

## ⬇️ הורדה

**[לחץ כאן להורדת הגרסה האחרונה](https://github.com/YossiYad/video-editor/releases/latest)** — חלץ את קובץ ה-ZIP והרץ את `VideoEditor.exe`. בלי התקנה, בלי הרשאות מנהל. כל מה שצריך (FFmpeg, yt-dlp, whisper.cpp) יורד אוטומטית בשימוש הראשון.

## מה האפליקציה עושה

### עריכה
- **ציר זמן עם כמה קליפים** — גרור קבצי וידאו, סדר אותם, חתוך ע"י גרירת קצוות, פצל עם **S**, גרירה חופשית או מצב Ripple (קליפים נצמדים).
- **שינויים לא הרסניים לכל קליפ**: זמן התחלה / סיום, מהירות (0.25× עד 4×), עוצמת קול, סיבוב (90 / 180 / 270°), היפוך, לולאה. הקובץ המקורי לא נוגע עד לייצוא.
- **גודל ומיקום בקנבס** — לכל קליפ בנפרד: זום והזזה בתוך מסגרת הפרויקט. גרור את התצוגה כדי להזיז, גלגלת לזום, גרור ידיות בפינות לשינוי גודל. מצוין להכניס סרטון אופקי לפורמט אנכי בלי פסים שחורים.
- **פורמטים מובנים** — YouTube 16:9, Reels / TikTok / Shorts 9:16, אינסטגרם ריבוע 1:1, פורטרט 4:5, קולנועי 21:9, או מותאם אישית. אופן הצגה: Contain / Cover / רקע מטושטש.
- **בלוקי הסתרה** — כיסוי פנים, מספרי טלפון, לוחיות רישוי, בצבע מלא / טשטוש / פיקסול. טווח זמן לכל בלוק עם שדות שעה : דקה : שניה . מיליסקנדה בנפרד.
- **טקסט על הסרטון** — Picker ויזואלי עם גרירה ישירות על הפריים למיקום. כל טקסט הוא פס בציר הזמן שאפשר לגרור לזמן הופעה.

### כתוביות AI (✨ חדש)
- יצירה אוטומטית של כתוביות **kinetic typography** מהדיבור בסרטון. Whisper.cpp מתמלל מקומית (חינם, 5 גדלי מודלים מ-Tiny 75MB עד Large-v3-Turbo 800MB), Google Gemini הופך את התמלול לכתוביות קצרות וסגנוניות.
- **תיקון אוטומטי של שגיאות תמלול בעברית** — Gemini מקבל את כל ההקשר ומתקן מילים שגויות.
- **תרגום כתוביות** — אפשר לייצר כתוביות עברית לסרטון אנגלית (או אחת מתוך: עברית, אנגלית, ערבית, ספרדית, צרפתית, רוסית, פורטוגזית, גרמנית). מצוין לתרגום סדרות / הרצאות.
- **מפתח Fallback** — הדבק מפתח שני של Gemini מ-**חשבון Google אחר** והכפל את המכסה החינמית.
- **מונה שימוש יומי** כדי שתדע כמה קרוב אתה למגבלת המכסה החינמית (~1500 בקשות / יום).

### לכידה
- **הקלטת מסך** — מקליט אזור על המסך שלך.
- **הקלטת מצלמה** — מצלמת רשת או כל מצלמה תואמת.
- **טקסט לדיבור** — קולות Windows SAPI, נשמר כ-WAV לקריינות.
- **הורדה מ-URL** — הדבק קישור YouTube / TikTok / Vimeo / Twitter / Instagram / Facebook / Twitch / Reddit. yt-dlp מוריד אוטומטית.

### כלים על קליפ
חיתוך · רזולוציה · סיבוב · היפוך · לולאה · מהירות · ייצוב · הסרת לוגו · הוספת תמונה · הוספת טקסט · הוספת אודיו · חילוץ אודיו · השתקה · שינוי עוצמה.

### ייצוא
- H.264 (ברירת מחדל), H.265 / HEVC, AV1, או ProRes 422. מיכל MP4 / MOV / MKV / WebM.
- **האצת חומרה**: NVIDIA NVENC, Intel QuickSync, AMD AMF — ייצוא מהיר פי 3 עד 5.
- פס התקדמות בזמן אמת עם אחוז.
- CRF, קצב פריימים (24 / 25 / 30 / 50 / 60), ביטרייט אודיו, 2-pass, loudnorm — הכל ניתן להגדרה.

### ממשק
- דו-לשוני — אנגלית / עברית (עם תמיכת RTL).
- ערכת נושא בהיר / כהה / אוטומטי עם החלפה בזמן אמת.
- קיצורי מקלדת מותאמים אישית.
- כל ההגדרות נשמרות ל-`settings.json` ליד ה-EXE.

## דרישות מערכת

- Windows 10 / 11 (x64).
- כ-500MB שטח דיסק פנוי (FFmpeg + מודל ה-Whisper שתבחר).
- אופציונלי: כרטיס מסך NVIDIA / Intel / AMD לייצוא מואץ בחומרה.
- אופציונלי: חשבון Google חינמי עבור AI Captions (המכסה החינמית של Gemini נדיבה).

## בנייה מהמקור

```powershell
git clone https://github.com/YossiYad/video-editor.git
cd video-editor
dotnet run --project VideoEditor
```

בהפעלה הראשונה האפליקציה תוריד אוטומטית את `ffmpeg.exe` ו-`ffprobe.exe` לתיקייה `bin\Debug\net8.0-windows\ffmpeg\`. אם ההורדה נכשלה, הורד ידנית מ-<https://www.gyan.dev/ffmpeg/builds/> ושים את שני הקבצים שם.

## תרומה לפרויקט

זהו פרויקט **קוד פתוח** ואנחנו מזמינים אותך לתרום!

- 🐛 **דיווח על באגים**: [פתח Issue](https://github.com/YossiYad/video-editor/issues/new)
- 💡 **הצעת פיצ'רים**: [פתח Issue](https://github.com/YossiYad/video-editor/issues/new) עם תגית `enhancement`
- 🔧 **שליחת קוד**: עשה Fork לפרויקט, צור branch חדש, ושלח Pull Request
- ⭐ **תמיכה בפרויקט**: תן ⭐ ב-GitHub כדי לתמוך!

## רישיון

הפרויקט הזה מופץ תחת רישיון [MIT](LICENSE).

זה אומר שאתה חופשי:
- ✅ להשתמש באפליקציה לכל מטרה (אישית או מסחרית)
- ✅ לשנות את הקוד ולהתאים לצרכים שלך
- ✅ להפיץ את האפליקציה (גם בגרסה ששינית)
- ✅ לתרום בחזרה לקהילה
