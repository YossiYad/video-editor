using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using VideoEditor.Models;
using VideoEditor.Services;

namespace VideoEditor.Desktop;

public partial class MainWindow : Window
{
    // The shared, platform-neutral export/probe engine from VideoEditor.Core. The ffmpeg
    // binaries live in an "ffmpeg" folder next to the binary; they auto-download on first
    // use just like the Windows app (per-OS asset names via Platform.ExeName).
    private readonly FFmpegService _ff;

    // The project timeline clips. Same VideoClip type the WPF app and the Core export
    // engine use, so Stage C6 can hand this straight to ExportProjectAsync.
    private readonly ObservableCollection<VideoClip> _clips = new();

    private VideoClip? _selectedClip;
    private int _previewToken; // guards against out-of-order async frame loads

    private static readonly string[] VideoExtensions =
        { ".mp4", ".mov", ".mkv", ".avi", ".webm", ".wmv", ".flv", ".m4v", ".ts", ".mpg", ".mpeg" };

    public MainWindow()
    {
        InitializeComponent();
        var ffmpegDir = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
        Directory.CreateDirectory(ffmpegDir);
        _ff = new FFmpegService(ffmpegDir);
        clipList.ItemsSource = _clips;
        platformText.Text = $"{Platform.RuntimeId}  ·  ffmpeg: {Platform.ExeName("ffmpeg")}";
    }

    private async void OpenFiles_Click(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open video files",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Video files")
                {
                    Patterns = VideoExtensions.Select(x => "*" + x).ToList()
                },
                FilePickerFileTypes.All
            }
        });

        if (files is null || files.Count == 0) return;

        var paths = files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();

        await AddClipsAsync(paths);
    }

    private async Task AddClipsAsync(IEnumerable<string> paths)
    {
        int added = 0;
        double timelineEnd = _clips.Sum(c => c.EffectiveDuration);
        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            statusText.Text = $"Probing {Path.GetFileName(path)}…";
            try
            {
                var (w, h, dur) = await _ff.ProbeAsync(path);
                if (dur <= 0) dur = 0.1;
                var clip = new VideoClip
                {
                    SourceFile = path,
                    OriginalDuration = dur,
                    InPoint = 0,
                    OutPoint = dur,
                    VideoWidth = w,
                    VideoHeight = h,
                    IsAudioOnly = w <= 0 || h <= 0,
                    TimelineStart = timelineEnd,
                    AccentColor = VideoClip.NextColor()
                };
                _clips.Add(clip);
                timelineEnd += clip.EffectiveDuration;
                added++;
            }
            catch (Exception ex)
            {
                statusText.Text = $"Could not read {Path.GetFileName(path)}: {ex.Message}";
            }
        }

        exportBtn.IsEnabled = _clips.Count > 0;
        previewHint.IsVisible = _clips.Count == 0;
        if (added > 0)
        {
            statusText.Text = $"Added {added} clip(s). Total {_clips.Count}.";
            if (_selectedClip == null) clipList.SelectedIndex = _clips.Count - added;
        }
    }

    private async void ClipList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selectedClip = clipList.SelectedItem as VideoClip;
        if (_selectedClip is null)
        {
            inspectorEmpty.IsVisible = true;
            inspectorClip.IsVisible = false;
            return;
        }

        inspectorEmpty.IsVisible = false;
        inspectorClip.IsVisible = true;
        inspName.Text = _selectedClip.DisplayName;
        inspMeta.Text = _selectedClip.IsAudioOnly
            ? $"audio · {_selectedClip.OriginalDuration:0.0}s"
            : $"{_selectedClip.VideoWidth}×{_selectedClip.VideoHeight} · {_selectedClip.OriginalDuration:0.0}s";

        await ShowPreviewFrameAsync(_selectedClip, _selectedClip.InPoint);
    }

    // Extracts a single frame at <paramref name="timeSeconds"/> into the source clip and
    // shows it in the preview Image. This is the MVP "scrub preview" - real-time playback
    // (an ffmpeg frame-pipe) is a later optimization.
    private async Task ShowPreviewFrameAsync(VideoClip clip, double timeSeconds)
    {
        if (clip.IsAudioOnly)
        {
            previewImage.Source = null;
            previewHint.IsVisible = true;
            previewHint.Text = "Audio-only clip (no preview)";
            return;
        }

        int token = ++_previewToken;
        var tmp = Path.Combine(Path.GetTempPath(), $"ve_preview_{Guid.NewGuid():N}.jpg");
        try
        {
            await _ff.ExtractFrameAsync(clip.SourceFile, tmp, Math.Max(0, timeSeconds));
            if (token != _previewToken) { TryDelete(tmp); return; } // superseded by a newer selection
            if (!File.Exists(tmp)) return;

            // Load fully into memory so the temp file isn't locked, then delete it.
            Bitmap bmp;
            await using (var fs = File.OpenRead(tmp))
                bmp = new Bitmap(fs);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token != _previewToken) { bmp.Dispose(); return; }
                previewImage.Source = bmp;
                previewHint.IsVisible = false;
            });
        }
        catch (Exception ex)
        {
            statusText.Text = "Preview failed: " + ex.Message;
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    private async void Export_Click(object? sender, RoutedEventArgs e)
    {
        var videoClips = _clips.Where(c => !c.IsAudioOnly).ToList();
        if (videoClips.Count == 0)
        {
            statusText.Text = "Nothing to export - add at least one video clip.";
            return;
        }

        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var firstName = Path.GetFileNameWithoutExtension(videoClips[0].SourceFile);
        var save = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export video",
            SuggestedFileName = $"{firstName}_export.mp4",
            DefaultExtension = "mp4",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("MP4 video") { Patterns = new[] { "*.mp4" } }
            }
        });
        if (save is null) return;
        var output = save.TryGetLocalPath();
        if (string.IsNullOrEmpty(output)) { statusText.Text = "Could not resolve the save path."; return; }

        // Project canvas = the first video clip's dimensions (sensible MVP default).
        int canvasW = videoClips[0].VideoWidth > 0 ? videoClips[0].VideoWidth : 1920;
        int canvasH = videoClips[0].VideoHeight > 0 ? videoClips[0].VideoHeight : 1080;
        double totalDuration = _clips.Sum(c => c.EffectiveDuration);

        openBtn.IsEnabled = false;
        exportBtn.IsEnabled = false;
        exportProgress.IsVisible = true;
        exportProgress.Value = 0;
        statusText.Text = "Exporting…";

        var progress = new Progress<double>(v => Dispatcher.UIThread.Post(() => exportProgress.Value = v));
        try
        {
            await _ff.ExportProjectAsync(
                _clips.ToList(),
                new List<VideoBlock>(),
                canvasW, canvasH,
                canvasW, canvasH,
                totalDuration,
                output!,
                fitMode: "contain",
                textOverlays: null,
                progress: progress);
            exportProgress.Value = 1;
            statusText.Text = $"Exported → {Path.GetFileName(output)}";
            Platform.RevealInFileManager(output!);
        }
        catch (Exception ex)
        {
            statusText.Text = "Export failed: " + ex.Message;
        }
        finally
        {
            openBtn.IsEnabled = true;
            exportBtn.IsEnabled = _clips.Count > 0;
            exportProgress.IsVisible = false;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
