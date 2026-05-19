# Video Editor (C# / WPF)

אפליקציית עריכת וידאו עם כל הכלים מהתמונה + בלוקי הסתרה עם ציר זמן.

## הפעלה
```powershell
cd C:\Projects\video_editor
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
