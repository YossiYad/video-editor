# Video Editor (C# / WPF)

[![Download Latest](https://img.shields.io/github/v/release/YossiYad/video-editor?label=%E2%AC%87%EF%B8%8F%20Download%20Latest&style=for-the-badge&color=2ea44f)](https://github.com/YossiYad/video-editor/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=for-the-badge)](LICENSE)
[![Open Source](https://img.shields.io/badge/Open%20Source-%E2%9D%A4-red?style=for-the-badge)](https://github.com/YossiYad/video-editor)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078d6?style=for-the-badge&logo=windows)](https://github.com/YossiYad/video-editor/releases/latest)

אפליקציית עריכת וידאו **קוד פתוח** (Open Source) עם כל הכלים מהתמונה + בלוקי הסתרה עם ציר זמן.

## ⬇️ הורדה

### אפשרות 1: הורדה מהירה (מומלץ למשתמשים)
👉 **[לחץ כאן להורדת הגרסה האחרונה](https://github.com/YossiYad/video-editor/releases/latest)**

לאחר ההורדה: חלץ את הקובץ ZIP והרץ `VideoEditor.exe`.

### אפשרות 2: שיבוט והפעלה מהמקור (למפתחים)
```powershell
git clone https://github.com/YossiYad/video-editor.git
cd video-editor
dotnet run --project VideoEditor
```

בהפעלה הראשונה האפליקציה תוריד אוטומטית את `ffmpeg.exe` ו-`ffprobe.exe` לתיקייה `bin\Debug\net8.0-windows\ffmpeg\`.
אם ההורדה נכשלת, הורד ידנית מ-https://www.gyan.dev/ffmpeg/builds/ ושים את שני הקבצים שם.

## הכלים בתפריט השמאלי (לפי התמונה)
- **Video Editor** - המסך הראשי (פתוח כברירת מחדל)
- **Screen Recorder** - הקלטת מסך (gdigrab via FFmpeg)
- **Text to Speech** - הקראת טקסט ושמירה כ-WAV (System.Speech)
- **Merge Videos** - איחוד מספר סרטונים
- **Trim Video** - חיתוך לפי זמן התחלה/סיום
- **Add Audio** - הוספת פסקול אודיו
- **Add Image** - הוספת תמונה (overlay)
- **Add Text** - הוספת טקסט (drawtext)
- **Remove Logo** - הסרת לוגו (delogo)
- **Crop Video** - חיתוך מלבני
- **Rotate Video** - 90/180/270 מעלות
- **Flip Video** - היפוך אופקי/אנכי
- **Resize Video** - שינוי רזולוציה
- **Loop Video** - שכפול N פעמים
- **Change Volume** - שינוי עוצמת אודיו
- **Change Speed** - שינוי מהירות (0.25x – 4x)
- **Stabilize Video** - ייצוב (vidstabdetect/transform, 2-pass)
- **Video Recorder** - הקלטה ממצלמה (dshow)

## בלוקי הסתרה (הפיצ'ר המיוחד)
1. פתח סרטון
2. לחץ על **➕ Add Block** בפאנל הימני
3. הבלוק יופיע על הוידאו - אפשר:
   - לגרור אותו לכל מקום
   - לחיצה עליו בוחרת אותו - יופיעו 4 פינות לשינוי גודל
   - לבחור צבע/Blur/Pixelate בפאנל הימני
4. בציר הזמן למטה:
   - כל בלוק מופיע כפס סגול
   - גרור את הקצוות של הפס לקבוע התחלה וסוף
   - גרור את הפס כולו להזיז אותו על הציר
   - או סמן **Cover Whole Video** לכיסוי לכל אורך הסרטון
5. לחץ **💾 Export** וכל הבלוקים ירונדרו לוידאו הסופי

## מבנה הפרויקט
```
VideoEditor/
├── App.xaml(.cs)              - אתחול + הורדת FFmpeg
├── MainWindow.xaml(.cs)       - חלון ראשי
├── Models/VideoBlock.cs       - מודל בלוק
├── Services/FFmpegService.cs  - כל פעולות הוידאו דרך FFmpeg
├── Controls/
│   ├── ResizableBlock.xaml(.cs)  - בלוק גריר על הוידאו
│   └── Timeline.xaml(.cs)        - ציר זמן עם בלוקים
└── Views/
    ├── ToolWindows.cs            - חלונות דיאלוג לכל כלי
    ├── TextToSpeechWindow.cs
    ├── MergeVideosWindow.cs
    └── ScreenRecorderWindow.cs
```

## 🤝 תרומה לפרויקט (Contributing)

זהו פרויקט **קוד פתוח** ואנחנו מזמינים אותך לתרום!

- 🐛 **דיווח על באגים**: [פתח Issue](https://github.com/YossiYad/video-editor/issues/new)
- 💡 **הצעת פיצ'רים**: [פתח Issue](https://github.com/YossiYad/video-editor/issues/new) עם תגית `enhancement`
- 🔧 **שליחת קוד**: עשה Fork לפרויקט, צור branch חדש, ושלח Pull Request
- ⭐ **תמיכה בפרויקט**: תן ⭐ ב-GitHub כדי לתמוך!

## 📄 רישיון (License)

הפרויקט הזה מופץ תחת רישיון **MIT** - ראה את קובץ [LICENSE](LICENSE) לפרטים מלאים.

זה אומר שאתה חופשי:
- ✅ להשתמש באפליקציה לכל מטרה (אישית או מסחרית)
- ✅ לשנות את הקוד ולהתאים לצרכים שלך
- ✅ להפיץ את האפליקציה (גם בגרסה ששינית)
- ✅ לתרום בחזרה לקהילה
