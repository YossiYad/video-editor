using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using VideoEditor.Controls;
using VideoEditor.Models;
using VideoEditor.Services;
using VideoEditor.Views;

namespace VideoEditor;

public partial class MainWindow : Window
{
    private readonly FFmpegService _ff = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(80) };

    private readonly Dictionary<VideoBlock, ResizableBlock> _blockControls = new();
    private VideoBlock? _selectedBlock;
    private VideoClip? _selectedClip;
    private VideoClip? _playingClip;
    private bool _suppress;
    private bool _isPlaying;
    private double _masterVolume = 0.8;

    // Clipboard for Ctrl+C / Ctrl+V
    private VideoClip? _clipboardClip;
    private VideoBlock? _clipboardBlock;
    private double? _clipboardAudioVolume;

    // The audio bar of a clip currently selected on the timeline (separate from clip body selection)
    private VideoClip? _selectedAudio;

    private bool _formatPickedThisSession;

    public MainWindow()
    {
        InitializeComponent();

        // Apply Language preference (RTL for Hebrew)
        ApplyLanguage();
        // Apply default master volume from settings
        _masterVolume = AppSettings.DefaultMasterVolume / 100.0;
        volumeSlider.Value = AppSettings.DefaultMasterVolume;
        videoView.Volume = _masterVolume;
        videoView.ScrubbingEnabled = AppSettings.ScrubbingQuality != "smooth";
        timeline.FFmpeg = _ff;
        InitFormatControls();
        // Re-apply on settings change at runtime
        AppSettings.Changed += () =>
        {
            Dispatcher.Invoke(() =>
            {
                if (Application.Current is App app) app.ApplyThemeResources();
                ApplyLanguage();
                VideoEditor.Services.Localization.TranslateTree(timeline);
                timeline.FullRefresh();
                if (_playingClip == null) volumeSlider.Value = AppSettings.DefaultMasterVolume;
                videoView.ScrubbingEnabled = AppSettings.ScrubbingQuality != "smooth";
                UpdateTopbarDims();
                UpdateInspectorFormatText();
                UpdatePreviewAspect();
            });
        };

        _tick.Tick += Tick_OnTick;
        _tick.Start();

        PreviewKeyDown += MainWindow_PreviewKeyDown;

        timeline.Seek += sec => SeekTo(sec);
        timeline.BlockSelected += b => SelectBlock(b);
        timeline.ClipSelected += c => SelectClip(c);
        timeline.ClipDoubleClicked += c => { SeekTo(timeline.GetClipStart(c)); videoView.Play(); _isPlaying = true; };
        timeline.ClipContextAction += OnClipContextAction;
        timeline.FilesDropped += (files, sec) => AddFiles(files, sec);
        timeline.ClipScrubPreview += ScrubToClipFrame;
        timeline.ClipEdgeDragEnded += c => { /* leave preview at the trim point */ };
        timeline.AudioSelected += OnAudioSelected;
        timeline.AudioContextAction += OnAudioContextAction;
        timeline.TextOverlaySelected += o => { /* selection visual handled by Timeline */ };
        timeline.TextOverlayContextAction += OnTextOverlayContext;
        timeline.TextOverlayChanged += o => _textOverlayDirty.Add(o);
        timeline.TextOverlaysChanged += () => UpdateStats();
        timeline.ClipsChanged += () =>
        {
            if (timeline.Clips.Count > 0 && _playingClip == null) LoadClipForPreview(timeline.Clips[0], 0);
            UpdateStats();
            UpdateTimeDisplays();
        };
        timeline.BlocksChanged += () => UpdateStats();

        overlayCanvas.SizeChanged += (_, _) =>
        {
            RepositionOverlay();
            // Scale of every text-overlay preview control depends on canvas size — mark all dirty
            // so the next tick re-styles + re-places them. Cheap because it only happens on resize.
            foreach (var ov in timeline.TextOverlays) _textOverlayDirty.Add(ov);
        };
        overlayCanvas.MouseLeftButtonDown += OverlayCanvas_BackgroundClick;
        WirePreviewCanvasTransformGestures();
    }

    // ===== Direct manipulation of the canvas transform =====
    //
    // - Click and drag inside the preview to pan the selected clip on the canvas.
    // - Scroll the mouse wheel inside the preview to zoom in/out (Ctrl+Scroll = finer step).
    // - Double-click resets the clip to "fit the canvas exactly" (scale=1, offset=0,0).
    //
    // Behaviour is no-op when no video clip is selected.
    private bool _canvasDragActive;
    private Point _canvasDragStartMouse;
    private double _canvasDragStartOffX;
    private double _canvasDragStartOffY;

    private void WirePreviewCanvasTransformGestures()
    {
        if (videoView == null) return;
        videoView.MouseLeftButtonDown += PreviewCanvas_MouseDown;
        videoView.MouseMove += PreviewCanvas_MouseMove;
        videoView.MouseLeftButtonUp += PreviewCanvas_MouseUp;
        videoView.MouseWheel += PreviewCanvas_MouseWheel;
        videoView.MouseRightButtonDown += PreviewCanvas_MouseRight;
        videoView.Cursor = Cursors.SizeAll;
    }

    private VideoClip? CanvasTargetClip() =>
        _selectedClip != null && !_selectedClip.IsAudioOnly ? _selectedClip : _playingClip;

    private void PreviewCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var c = CanvasTargetClip();
        if (c == null) return;
        if (e.ClickCount >= 2)
        {
            c.CanvasScale = 1.0;
            c.CanvasOffsetX = 0;
            c.CanvasOffsetY = 0;
            ApplyClipTransform(c);
            UpdateInspectorCanvasFields();
            e.Handled = true;
            return;
        }
        _canvasDragActive = true;
        _canvasDragStartMouse = e.GetPosition(videoView);
        _canvasDragStartOffX = c.CanvasOffsetX;
        _canvasDragStartOffY = c.CanvasOffsetY;
        videoView.CaptureMouse();
        e.Handled = true;
    }

    private void PreviewCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_canvasDragActive) return;
        var c = CanvasTargetClip();
        if (c == null) return;
        var p = e.GetPosition(videoView);
        double w = videoView.ActualWidth, h = videoView.ActualHeight;
        if (w < 1 || h < 1) return;
        c.CanvasOffsetX = _canvasDragStartOffX + (p.X - _canvasDragStartMouse.X) / w;
        c.CanvasOffsetY = _canvasDragStartOffY + (p.Y - _canvasDragStartMouse.Y) / h;
        ApplyClipTransform(c);
        UpdateInspectorCanvasFields();
    }

    private void PreviewCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_canvasDragActive) return;
        _canvasDragActive = false;
        videoView.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void PreviewCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var c = CanvasTargetClip();
        if (c == null) return;
        double step = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? 0.02 : 0.08;
        double delta = e.Delta > 0 ? step : -step;
        c.CanvasScale = Math.Max(0.1, Math.Min(6.0, c.CanvasScale * (1 + delta)));
        ApplyClipTransform(c);
        UpdateInspectorCanvasFields();
        e.Handled = true;
    }

    private void PreviewCanvas_MouseRight(object sender, MouseButtonEventArgs e)
    {
        // Right-click to reset, mirrors double-click but easier on touchpads.
        var c = CanvasTargetClip();
        if (c == null) return;
        c.CanvasScale = 1.0;
        c.CanvasOffsetX = 0;
        c.CanvasOffsetY = 0;
        ApplyClipTransform(c);
        UpdateInspectorCanvasFields();
        e.Handled = true;
    }

    private Action? _updateInspectorCanvasFields;
    private void UpdateInspectorCanvasFields()
    {
        _updateInspectorCanvasFields?.Invoke();
        RepositionCanvasHandles();
    }

    // ===== Corner resize handles =====
    //
    // Four 20×20 squares pinned to the four corners of the project canvas via XAML
    // alignment, so they don't depend on the Canvas's measured size (which is fragile).
    // Dragging any handle scales the clip uniformly from the canvas centre — the new
    // scale = startScale × (currentDistFromCentre / startDistFromCentre).

    private bool _handleDragActive;
    private Point _handleDragCentre;
    private double _handleDragStartDist;
    private double _handleDragStartScale;

    private void RepositionCanvasHandles()
    {
        if (canvasHandlesLayer == null) return;
        var c = CanvasTargetClip();
        if (c == null || c.IsAudioOnly || c.VideoWidth <= 0 || c.VideoHeight <= 0 ||
            videoStack.ActualWidth < 1 || videoStack.ActualHeight < 1)
        {
            canvasHandlesLayer.Visibility = Visibility.Collapsed;
            return;
        }
        canvasHandlesLayer.Visibility = Visibility.Visible;

        // Size the layer to match the *actual* displayed video, not the whole canvas.
        // MediaElement uses Stretch=Uniform so the base displayed size is the source dimensions
        // scaled by min(canvasW/srcW, canvasH/srcH); the user's CanvasScale multiplies that.
        // When scale > 1 the displayed image is bigger than the canvas — clamp the layer to
        // canvas size so handles stay inside the visible region.
        double canvasW = videoStack.ActualWidth;
        double canvasH = videoStack.ActualHeight;
        double baseScale = Math.Min(canvasW / c.VideoWidth, canvasH / c.VideoHeight);
        double dispW = c.VideoWidth * baseScale * c.CanvasScale;
        double dispH = c.VideoHeight * baseScale * c.CanvasScale;
        double layerW = Math.Min(dispW, canvasW);
        double layerH = Math.Min(dispH, canvasH);
        canvasHandlesLayer.Width = layerW;
        canvasHandlesLayer.Height = layerH;
        // Center in the canvas, then shift by the user's offset.
        canvasHandlesLayer.RenderTransform = new TranslateTransform(
            c.CanvasOffsetX * canvasW,
            c.CanvasOffsetY * canvasH);
    }

    private void CanvasHandle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var c = CanvasTargetClip();
        if (c == null || sender is not Border h) return;
        // Centre of the visible project canvas — handles drag relative to this point.
        double w = canvasHandlesLayer.ActualWidth;
        double hh = canvasHandlesLayer.ActualHeight;
        if (w < 1 || hh < 1) return;
        _handleDragCentre = new Point(w / 2, hh / 2);
        var mp = e.GetPosition(canvasHandlesLayer);
        _handleDragStartDist = Math.Max(8, Distance(mp, _handleDragCentre));
        _handleDragStartScale = c.CanvasScale;
        _handleDragActive = true;
        h.CaptureMouse();
        e.Handled = true;
    }

    private void CanvasHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_handleDragActive) return;
        var c = CanvasTargetClip();
        if (c == null) return;
        var mp = e.GetPosition(canvasHandlesLayer);
        double dist = Math.Max(4, Distance(mp, _handleDragCentre));
        c.CanvasScale = _handleDragStartScale * (dist / _handleDragStartDist);
        ApplyClipTransform(c);
        UpdateInspectorCanvasFields();
    }

    private void CanvasHandle_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_handleDragActive) return;
        _handleDragActive = false;
        if (sender is Border h) h.ReleaseMouseCapture();
        e.Handled = true;
    }

    private static double Distance(Point a, Point b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    protected override void OnClosed(EventArgs e)
    {
        _tick.Stop();
        try { videoView.Stop(); videoView.Close(); } catch { }
        base.OnClosed(e);
    }

    // ===== Drag & Drop =====

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            AddFiles(files);
        }
        e.Handled = true;
    }

    private async void AddFiles(string[] files, double? insertAtSec = null)
    {
        var exts = new[] { ".mp4", ".mov", ".mkv", ".avi", ".webm", ".wmv", ".flv", ".m4v" };
        foreach (var f in files)
        {
            if (!File.Exists(f)) continue;
            if (!exts.Contains(Path.GetExtension(f).ToLowerInvariant())) continue;
            await AddClipAsync(f, insertAtSec);
            if (insertAtSec.HasValue) insertAtSec = insertAtSec.Value + 0.001; // next file goes after this one
        }
    }

    private async System.Threading.Tasks.Task AddClipAsync(string path, double? insertAtSec = null)
    {
        status.Text = "Probing " + Path.GetFileName(path) + "...";
        bool wasFirstVideo = !timeline.Clips.Any(c => !c.IsAudioOnly);
        try
        {
            var (w, h, d) = await _ff.ProbeAsync(path);
            var clip = new VideoClip
            {
                SourceFile = path,
                OriginalDuration = d > 0 ? d : 5,
                InPoint = 0,
                OutPoint = d > 0 ? d : 5,
                VideoWidth = w,
                VideoHeight = h,
                AccentColor = VideoClip.NextColor()
            };
            if (insertAtSec.HasValue)
            {
                timeline.ReorderClipToPosition(clip, insertAtSec.Value);
            }
            else
            {
                timeline.Clips.Add(clip);
            }
            status.Text = VideoEditor.Services.Localization.IsHebrew
                ? $"נוסף: {Path.GetFileName(path)} · {w}×{h} · {Timeline.FormatTime(d)}"
                : $"Added: {Path.GetFileName(path)} · {w}×{h} · {Timeline.FormatTime(d)}";
            projectName.Text = Path.GetFileNameWithoutExtension(path);
            projDims.Text = $"{w}×{h}";
            UpdateStats();
            UpdateTopbarDims();
            UpdatePreviewAspect();
            if (timeline.Clips.Count == 1)
            {
                LoadClipForPreview(clip, 0);
                timeline.FitToView();
            }
            if (wasFirstVideo && !clip.IsAudioOnly && !_formatPickedThisSession)
            {
                _formatPickedThisSession = true;
                ShowFormatPicker();
            }
        }
        catch (Exception ex)
        {
            status.Text = VideoEditor.Services.Localization.IsHebrew ? "ההוספה נכשלה: " + ex.Message : "Failed to add: " + ex.Message;
        }
    }

    // ===== Project format helpers =====

    private void InitFormatControls()
    {
        var fit = AppSettings.TargetFitMode ?? "contain";
        fitContainBtn.IsChecked = fit == "contain";
        fitCoverBtn.IsChecked   = fit == "cover";
        fitBlurBtn.IsChecked    = fit == "blur";
        fitPreviewHint.Visibility = fit == "contain" ? Visibility.Collapsed : Visibility.Visible;
        UpdateTopbarDims();
        UpdateInspectorFormatText();
        // Defer preview-aspect computation to first Loaded so the container has a real size
        Loaded += (_, _) => UpdatePreviewAspect();
    }

    private void ShowFormatPicker()
    {
        var dlg = new VideoEditor.Views.ProjectFormatPickerWindow { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            AppSettings.TargetFormatPreset = dlg.SelectedPresetKey;
            if (dlg.SelectedPresetKey == "custom")
            {
                AppSettings.CustomTargetWidth  = dlg.SelectedCustomWidth;
                AppSettings.CustomTargetHeight = dlg.SelectedCustomHeight;
            }
            AppSettings.Save();
            UpdateTopbarDims();
            UpdateInspectorFormatText();
            UpdatePreviewAspect();
        }
    }

    private void ChangeFormat_Click(object sender, RoutedEventArgs e) => ShowFormatPicker();

    private void FitMode_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.RadioButton rb && rb.Tag is string mode)
        {
            AppSettings.TargetFitMode = mode;
            AppSettings.Save();
            fitPreviewHint.Visibility = mode == "contain" ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void VideoContainerOuter_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePreviewAspect();

    private void UpdatePreviewAspect()
    {
        if (videoContainerOuter == null || videoContainer == null) return;
        var first = timeline.Clips.FirstOrDefault(c => !c.IsAudioOnly);
        var (tw, th) = VideoEditor.Services.ProjectFormats.Resolve(AppSettings.TargetFormatPreset, first);
        if (tw <= 0 || th <= 0) return;
        double aw = videoContainerOuter.ActualWidth;
        double ah = videoContainerOuter.ActualHeight;
        if (aw <= 0 || ah <= 0) return;
        double targetRatio = (double)tw / th;
        double w, h;
        if (aw / ah > targetRatio) { h = ah; w = h * targetRatio; }
        else                       { w = aw; h = w / targetRatio; }
        videoContainer.Width  = Math.Max(40, w);
        videoContainer.Height = Math.Max(40, h);
        // Project-format change resizes the canvas — handles must follow.
        RepositionCanvasHandles();
    }

    private void UpdateTopbarDims()
    {
        if (projTargetChip == null) return;
        var first = timeline.Clips.FirstOrDefault(c => !c.IsAudioOnly);
        var (tw, th) = VideoEditor.Services.ProjectFormats.Resolve(AppSettings.TargetFormatPreset, first);
        var p = VideoEditor.Services.ProjectFormats.Lookup(AppSettings.TargetFormatPreset);
        var shortName = VideoEditor.Services.Localization.IsHebrew
            ? VideoEditor.Services.Localization.T(p.ShortName)
            : p.ShortName;
        projTargetChip.Text = $"→ {tw}×{th} · {shortName}";
        var fit = AppSettings.TargetFitMode ?? "contain";
        var fitLabel = VideoEditor.Services.Localization.T(fit == "cover" ? "Cover" : fit == "blur" ? "Blur bg" : "Contain");
        changeFormatBtn.ToolTip = $"{VideoEditor.Services.Localization.T("Change project format")} · {p.Label} ({fitLabel})";
    }

    private void UpdateInspectorFormatText()
    {
        if (inspectorFormatText == null) return;
        var first = timeline.Clips.FirstOrDefault(c => !c.IsAudioOnly);
        var (tw, th) = VideoEditor.Services.ProjectFormats.Resolve(AppSettings.TargetFormatPreset, first);
        var p = VideoEditor.Services.ProjectFormats.Lookup(AppSettings.TargetFormatPreset);
        var shortName = VideoEditor.Services.Localization.IsHebrew
            ? VideoEditor.Services.Localization.T(p.ShortName)
            : p.ShortName;
        inspectorFormatText.Text = $"{shortName} · {tw}×{th}";
    }

    private void UpdateStats()
    {
        statClips.Text = timeline.Clips.Count.ToString();
        statBlocks.Text = timeline.Blocks.Count.ToString();
        statusCache.Text = $"Cache: {timeline.Clips.Count * 8} thumbnails";
        hudBlocks.Text = $"{timeline.Blocks.Count} blocks";
        if (metaClips != null)  metaClips.Text = timeline.Clips.Count.ToString();
        if (metaBlocks != null) metaBlocks.Text = timeline.Blocks.Count.ToString();
        if (metaDuration != null) metaDuration.Text = Timeline.FormatTime(timeline.TotalSeconds);
    }

    private async void OpenBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Multiselect = true, Filter = "Video files|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.wmv;*.flv|All files|*.*" };
        if (dlg.ShowDialog() != true) return;
        AddFiles(dlg.FileNames);
        await System.Threading.Tasks.Task.CompletedTask;
    }

    // ===== Playback (multi-clip) =====

    private readonly DispatcherTimer _scrubTimer = new() { Interval = TimeSpan.FromMilliseconds(35) };
    private VideoClip? _pendingScrubClip;
    private double _pendingScrubSourceTime;
    private bool _scrubInited;

    private void EnsureScrubTimer()
    {
        if (_scrubInited) return;
        _scrubInited = true;
        _scrubTimer.Tick += (_, _) =>
        {
            _scrubTimer.Stop();
            if (_pendingScrubClip != null) DoScrub(_pendingScrubClip, _pendingScrubSourceTime);
        };
    }

    private void ScrubToClipFrame(VideoClip clip, double sourceTime)
    {
        EnsureScrubTimer();
        _pendingScrubClip = clip;
        _pendingScrubSourceTime = sourceTime;
        if (!_scrubTimer.IsEnabled) _scrubTimer.Start();
    }

    private void DoScrub(VideoClip clip, double sourceTime)
    {
        sourceTime = Math.Max(0, Math.Min(clip.OriginalDuration - 0.01, sourceTime));
        try
        {
            var srcUri = new Uri(clip.SourceFile);
            if (videoView.Source == null || !string.Equals(videoView.Source.LocalPath, srcUri.LocalPath, StringComparison.OrdinalIgnoreCase))
            {
                videoView.Source = srcUri;
                _playingClip = clip;
                ApplyClipTransform(clip);
                videoView.SpeedRatio = 1.0;
            }
            videoView.Pause();
            _isPlaying = false;
            videoView.Position = TimeSpan.FromSeconds(sourceTime);
        }
        catch { }
    }

    private void LoadClipForPreview(VideoClip clip, double offsetWithinClip)
    {
        try
        {
            _playingClip = clip;
            videoView.Source = new Uri(clip.SourceFile);
            videoView.Position = TimeSpan.FromSeconds(clip.InPoint + Math.Max(0, offsetWithinClip));
            videoView.SpeedRatio = clip.Speed;
            videoView.Volume = _masterVolume * clip.Volume;
            ApplyClipTransform(clip);
            // Show resize handles as soon as a clip is loaded — even before the user
            // explicitly selects it in the timeline.
            Dispatcher.BeginInvoke(new Action(RepositionCanvasHandles), System.Windows.Threading.DispatcherPriority.Loaded);
            if (!_isPlaying)
            {
                videoView.Play();
                System.Threading.Tasks.Task.Delay(80).ContinueWith(_ => Dispatcher.Invoke(() => { if (!_isPlaying) videoView.Pause(); }));
            }
        }
        catch (Exception ex)
        {
            status.Text = "Preview failed: " + ex.Message;
        }
    }

    private void ApplyClipTransform(VideoClip clip)
    {
        // Any transform change should also slide the handles to the new corners.
        Dispatcher.BeginInvoke(new Action(RepositionCanvasHandles), System.Windows.Threading.DispatcherPriority.Loaded);
        double cx = videoView.ActualWidth / 2, cy = videoView.ActualHeight / 2;
        var tg = new TransformGroup();
        // 1) flip around centre
        double sx = clip.FlipH ? -1 : 1;
        double sy = clip.FlipV ? -1 : 1;
        // 2) user canvas zoom multiplies the flip scale
        sx *= clip.CanvasScale;
        sy *= clip.CanvasScale;
        tg.Children.Add(new ScaleTransform(sx, sy, cx, cy));
        if (clip.RotateDegrees != 0)
            tg.Children.Add(new RotateTransform(clip.RotateDegrees, cx, cy));
        // 3) user canvas offset, expressed as a fraction of canvas size
        if (Math.Abs(clip.CanvasOffsetX) > 0.001 || Math.Abs(clip.CanvasOffsetY) > 0.001)
        {
            tg.Children.Add(new TranslateTransform(
                clip.CanvasOffsetX * videoView.ActualWidth,
                clip.CanvasOffsetY * videoView.ActualHeight));
        }
        videoView.RenderTransform = tg;
    }

    private void SeekTo(double seconds)
    {
        var clip = timeline.GetClipAt(seconds);
        if (clip == null) { timeline.SetCurrent(seconds); UpdateBlockVisibility(); return; }
        timeline.SetCurrent(seconds);

        var withinClip = Math.Max(0, seconds - clip.TimelineStart) * clip.Speed;
        if (clip != _playingClip)
        {
            // Source change is unavoidable heavy work — do it once, not coalesced.
            LoadClipForPreview(clip, withinClip);
        }
        else
        {
            // Playhead drag fires Seek ~100×/sec. Writing videoView.Position that fast
            // backs up the decoder queue (choppy audio, freezes). Route through the
            // existing 35 ms scrub timer so we coalesce a burst of seeks into a single
            // decode + Position write. Single-shot seeks (Back/Fwd buttons, click-on-
            // ruler) still feel instant because there's only one call.
            ScrubToClipFrame(clip, clip.InPoint + withinClip);
        }
        // Block + text overlay visibility tracks the playhead in real time, paused or not —
        // so scrubbing past a hide block's range immediately reveals the underlying frame.
        UpdateBlockVisibility();
    }

    private VideoClip? NextClipAfter(VideoClip c)
    {
        return timeline.Clips
            .Where(x => x.TimelineStart > c.TimelineStart - 0.001 && x != c)
            .OrderBy(x => x.TimelineStart)
            .FirstOrDefault();
    }

    private VideoClip? PrevClipBefore(VideoClip c)
    {
        return timeline.Clips
            .Where(x => x.TimelineStart < c.TimelineStart && x != c)
            .OrderByDescending(x => x.TimelineStart)
            .FirstOrDefault();
    }

    private void Tick_OnTick(object? sender, EventArgs e)
    {
        if (!_isPlaying || _playingClip == null) return;
        try
        {
            var mediaPos = videoView.Position.TotalSeconds;
            if (mediaPos >= _playingClip.OutPoint - 0.05)
            {
                var next = NextClipAfter(_playingClip);
                if (next != null)
                {
                    timeline.SetCurrent(next.TimelineStart);
                    LoadClipForPreview(next, 0);
                    if (_isPlaying) videoView.Play();
                }
                else
                {
                    if (AppSettings.LoopOnEnd && timeline.Clips.Count > 0)
                    {
                        // Loop: restart from beginning
                        var first = timeline.Clips.OrderBy(c => c.TimelineStart).First();
                        timeline.SetCurrent(first.TimelineStart);
                        LoadClipForPreview(first, 0);
                        if (_isPlaying) videoView.Play();
                    }
                    else
                    {
                        _isPlaying = false;
                        videoView.Pause();
                    }
                }
                return;
            }
            var clipStart = _playingClip.TimelineStart;
            var withinClip = (mediaPos - _playingClip.InPoint) / Math.Max(0.01, _playingClip.Speed);
            timeline.SetCurrent(clipStart + withinClip);
            UpdateBlockVisibility();
            UpdateTextOverlaysVisibility(clipStart + withinClip);
            UpdateTimeDisplays();
        }
        catch (Exception ex)
        {
            // The tick fires 12×/sec; surface any unexpected failure in the debugger and the
            // status bar so problems don't silently snowball.
            System.Diagnostics.Debug.WriteLine($"Tick error: {ex}");
            status.Text = "Playback tick error: " + ex.Message;
        }
    }

    private void UpdateTimeDisplays()
    {
        curTimeText.Text = Timeline.FormatTime(timeline.CurrentSeconds);
        totTimeText.Text = Timeline.FormatTime(timeline.TotalSeconds);
        if (_playingClip != null)
        {
            hudClipName.Text = Path.GetFileName(_playingClip.SourceFile);
            var pos = videoView.Position.TotalSeconds;
            hudTimeWithin.Text = Timeline.FormatTime(pos) + " in source";
        }
        else
        {
            hudClipName.Text = "No clip loaded";
            hudTimeWithin.Text = "--:--";
        }
    }

    private void VideoView_MediaOpened(object sender, RoutedEventArgs e)
    {
        // Video dimensions are first reliable here — RepositionCanvasHandles needs them to
        // compute the actual displayed bounds.
        RepositionCanvasHandles();
    }
    private void VideoView_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        var fileName = _playingClip?.DisplayName ?? videoView.Source?.LocalPath ?? "(unknown)";
        var hint = "Common cause: codec not installed (VP9 / AV1 / HEVC). Re-import as H.264 / AAC or install the matching codec.";
        var msg = $"Cannot play {fileName}.\n\n{e.ErrorException?.Message ?? "Unknown media error"}.\n\n{hint}";
        status.Text = "Playback failed: " + (e.ErrorException?.Message ?? "codec issue") + " · see dialog";
        MessageBox.Show(this, msg, "Playback Error", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    private void VideoView_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (_playingClip == null) return;
        var next = NextClipAfter(_playingClip);
        if (next != null)
        {
            LoadClipForPreview(next, 0);
            timeline.SetCurrent(next.TimelineStart);
            if (_isPlaying) videoView.Play();
        }
        else
        {
            _isPlaying = false;
        }
    }

    private void PlayBtn_Click(object sender, RoutedEventArgs e)
    {
        if (timeline.Clips.Count == 0) return;
        if (_playingClip == null) LoadClipForPreview(timeline.Clips[0], 0);
        videoView.Play(); _isPlaying = true;
        UpdateBlockVisibility();
    }
    private void PauseBtn_Click(object sender, RoutedEventArgs e) { videoView.Pause(); _isPlaying = false; UpdateBlockVisibility(); }
    private void StopBtn_Click(object sender, RoutedEventArgs e)
    {
        videoView.Pause(); _isPlaying = false;
        if (timeline.Clips.Count > 0)
        {
            LoadClipForPreview(timeline.Clips[0], 0);
            timeline.SetCurrent(0);
            videoView.Pause();
        }
        UpdateBlockVisibility();
    }
    private void Back5_Click(object sender, RoutedEventArgs e) => SeekTo(Math.Max(0, timeline.CurrentSeconds - AppSettings.BackForwardSeconds));
    private void Fwd5_Click(object sender, RoutedEventArgs e) => SeekTo(Math.Min(timeline.TotalSeconds, timeline.CurrentSeconds + AppSettings.BackForwardSeconds));
    private void PrevClip_Click(object sender, RoutedEventArgs e)
    {
        if (_playingClip == null) return;
        var prev = PrevClipBefore(_playingClip);
        if (prev != null) SeekTo(prev.TimelineStart);
    }
    private void NextClip_Click(object sender, RoutedEventArgs e)
    {
        if (_playingClip == null) return;
        var next = NextClipAfter(_playingClip);
        if (next != null) SeekTo(next.TimelineStart);
    }
    private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _masterVolume = e.NewValue / 100.0;
        if (videoView != null)
        {
            if (_playingClip != null) videoView.Volume = _masterVolume * _playingClip.Volume;
            else videoView.Volume = _masterVolume;
        }
        if (volText != null) volText.Text = ((int)Math.Round(e.NewValue)).ToString();
    }

    private async void ExtractAudio_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip();
        if (c == null) { MessageBox.Show("Select a clip first."); return; }
        var sfd = new SaveFileDialog
        {
            FileName = Path.GetFileNameWithoutExtension(c.SourceFile) + "_audio.mp3",
            Filter = "MP3|*.mp3|AAC|*.aac|WAV|*.wav|OGG|*.ogg|FLAC|*.flac"
        };
        if (sfd.ShowDialog() != true) return;
        status.Text = "Extracting audio...";
        progress.Value = 0;
        var prog = new Progress<double>(v => Dispatcher.Invoke(() => progress.Value = v));
        try
        {
            await _ff.ExtractAudioAsync(c.SourceFile, sfd.FileName, c.OriginalDuration, prog);
            status.Text = "Audio extracted: " + Path.GetFileName(sfd.FileName);
            progress.Value = 1;
        }
        catch (Exception ex)
        {
            status.Text = "Extract failed: " + ex.Message;
            MessageBox.Show(ex.Message, "Error");
        }
    }

    private async void MuteAudio_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip();
        if (c == null) { MessageBox.Show("Select a clip first."); return; }
        var result = MessageBox.Show(
            "Mute audio on this clip (live, no re-encode), or remove the audio track entirely (re-encode)?\n\nYes = Mute  |  No = Remove track  |  Cancel = nothing",
            "Mute / Remove Audio", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel) return;
        if (result == MessageBoxResult.Yes)
        {
            c.Volume = 0;
            status.Text = "Clip muted (export will silence it).";
            return;
        }
        // Remove audio track via FFmpeg
        await ApplyDestructiveOpAsync(c, async (input, output, prog) =>
            await _ff.RemoveAudioAsync(input, output, c.OriginalDuration, prog));
        status.Text = "Audio track removed from clip.";
    }

    // ===== Block management =====

    private void AddBlock_Click(object sender, RoutedEventArgs e)
    {
        var canvasW = overlayCanvas.ActualWidth > 1 ? overlayCanvas.ActualWidth : Math.Max(320, videoContainer.ActualWidth);
        var canvasH = overlayCanvas.ActualHeight > 1 ? overlayCanvas.ActualHeight : Math.Max(180, videoContainer.ActualHeight);
        var blockW = Math.Min(200, Math.Max(80, canvasW * 0.35));
        var blockH = Math.Min(120, Math.Max(60, canvasH * 0.25));
        var b = new VideoBlock
        {
            X = Math.Max(0, canvasW / 2 - blockW / 2),
            Y = Math.Max(0, canvasH / 2 - blockH / 2),
            Width = blockW, Height = blockH,
            StartSeconds = 0, EndSeconds = timeline.TotalSeconds, CoversWholeVideo = true,
            Color = Colors.Black, Mode = BlockMode.Solid,
            Label = $"Block {timeline.Blocks.Count + 1}"
        };
        timeline.Blocks.Add(b);
        var ctl = new ResizableBlock(b);
        ctl.Selected += rb => SelectBlock(rb.Model);
        ctl.Changed += _ => SyncBlockInspector();
        overlayCanvas.Children.Add(ctl);
        _blockControls[b] = ctl;
        SelectBlock(b);
        UpdateStats();
        status.Text = timeline.Clips.Count == 0
            ? (VideoEditor.Services.Localization.IsHebrew ? "בלוק הסתרה נוסף. הוסף וידאו לפני ייצוא." : "Hide block added. Add a video before export.")
            : (VideoEditor.Services.Localization.IsHebrew ? "בלוק הסתרה נוסף." : "Hide block added.");
    }

    private void OverlayCanvas_BackgroundClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == overlayCanvas) SelectBlock(null);
    }

    private void DeleteBlock_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedBlock == null) return;
        if (_blockControls.TryGetValue(_selectedBlock, out var ctl))
        {
            overlayCanvas.Children.Remove(ctl);
            _blockControls.Remove(_selectedBlock);
        }
        timeline.Blocks.Remove(_selectedBlock);
        _selectedBlock = null;
        blockPanel.Visibility = Visibility.Collapsed;
    }

    // ===== Selection =====

    private void SelectBlock(VideoBlock? b)
    {
        _selectedBlock = b;
        if (b != null) _selectedClip = null;
        foreach (var kv in _blockControls) kv.Value.SetSelected(kv.Key == b);
        timeline.SelectBlock(b);
        ShowInspectorTab(b != null ? "block" : (_selectedClip != null ? "clip" : "export"));
        // Selection changed → previously-selected block (if out of range) must now hide;
        // the newly-selected one must now show even if out of range.
        UpdateBlockVisibility();
        if (b == null) return;
        _suppress = true;
        lblBox.Text = b.Label;
        modeBox.SelectedIndex = (int)b.Mode;
        UpdateModeRadios();
        strengthSlider.Value = b.BlurStrength;
        strengthLabel.Text = ((int)b.BlurStrength).ToString();
        wholeCheck.IsChecked = b.CoversWholeVideo;
        FillHmsBoxes(b.StartSeconds, startH, startM, startS, startMs);
        FillHmsBoxes(b.EndSeconds, endH, endM, endS, endMs);
        _suppress = false;
    }

    private static void FillHmsBoxes(double totalSeconds, TextBox hBox, TextBox mBox, TextBox sBox, TextBox msBox)
    {
        if (totalSeconds < 0) totalSeconds = 0;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var h = (int)(totalSeconds / 3600);
        var m = (int)((totalSeconds - h * 3600) / 60);
        var s = (int)(totalSeconds - h * 3600 - m * 60);
        var ms = (int)Math.Round((totalSeconds - h * 3600 - m * 60 - s) * 1000);
        // Floating-point round-up: 12.9996 → 13 s 0 ms (not 12 s 1000 ms).
        if (ms >= 1000) { ms -= 1000; s += 1; if (s >= 60) { s -= 60; m += 1; if (m >= 60) { m -= 60; h += 1; } } }
        hBox.Text = h.ToString(inv);
        mBox.Text = m.ToString(inv);
        sBox.Text = s.ToString(inv);
        msBox.Text = ms.ToString("000", inv);
    }

    private static double ReadHmsBoxes(TextBox hBox, TextBox mBox, TextBox sBox, TextBox msBox)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var any = System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands;
        double h = 0, m = 0, s = 0, ms = 0;
        double.TryParse(hBox.Text, any, inv, out h);
        double.TryParse(mBox.Text, any, inv, out m);
        double.TryParse(sBox.Text, any, inv, out s);
        double.TryParse(msBox.Text, any, inv, out ms);
        if (h < 0) h = 0;
        if (m < 0) m = 0;
        if (s < 0) s = 0;
        if (ms < 0) ms = 0;
        return h * 3600 + m * 60 + s + ms / 1000.0;
    }

    private void SelectClip(VideoClip? c)
    {
        _selectedClip = c;
        if (c != null) _selectedBlock = null;
        timeline.SelectClip(c);
        ShowInspectorTab(c != null ? "clip" : (_selectedBlock != null ? "block" : "export"));
        RepositionCanvasHandles();
        if (c == null) return;
        _suppress = true;
        clipNameText.Text = c.DisplayName;
        clipMetaText.Text = $"{(c.VideoWidth > 0 ? c.VideoWidth + "×" + c.VideoHeight : "audio")} · {Timeline.FormatTime(c.OriginalDuration)}";
        var accent = new SolidColorBrush(c.AccentColor);
        clipAccentTile.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x33, c.AccentColor.R, c.AccentColor.G, c.AccentColor.B));
        clipAccentTile.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x66, c.AccentColor.R, c.AccentColor.G, c.AccentColor.B));
        clipInBox.Text = c.InPoint.ToString("0.###");
        clipOutBox.Text = c.OutPoint.ToString("0.###");
        clipDurText.Text = c.OriginalDuration.ToString("0.00");
        clipEffText.Text = "Effective: " + Timeline.FormatTime(c.EffectiveDuration);
        clipSpeedSlider.Value = c.Speed;
        clipSpeedLabel.Text = c.Speed.ToString("0.00") + "×";
        clipVolSlider.Value = c.Volume;
        clipVolLabel.Text = (c.Volume * 100).ToString("0") + "%";
        canvasScaleSlider.Value = c.CanvasScale;
        canvasOffsetXSlider.Value = c.CanvasOffsetX;
        canvasOffsetYSlider.Value = c.CanvasOffsetY;
        UpdateCanvasLabels(c);
        _suppress = false;
        // Capture once for the gesture handlers (re-applies on every drag).
        _updateInspectorCanvasFields = () =>
        {
            if (_selectedClip == null) return;
            _suppress = true;
            canvasScaleSlider.Value = _selectedClip.CanvasScale;
            canvasOffsetXSlider.Value = _selectedClip.CanvasOffsetX;
            canvasOffsetYSlider.Value = _selectedClip.CanvasOffsetY;
            UpdateCanvasLabels(_selectedClip);
            _suppress = false;
        };
    }

    private void UpdateCanvasLabels(VideoClip c)
    {
        if (canvasScaleText != null)
            canvasScaleText.Text = ((int)Math.Round(c.CanvasScale * 100)).ToString() + "%";
        if (canvasOffsetXText != null)
            canvasOffsetXText.Text = ((int)Math.Round(c.CanvasOffsetX * 100)).ToString() + "%";
        if (canvasOffsetYText != null)
            canvasOffsetYText.Text = ((int)Math.Round(c.CanvasOffsetY * 100)).ToString() + "%";
    }

    private void CanvasScale_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress || _selectedClip == null) return;
        _selectedClip.CanvasScale = e.NewValue;
        UpdateCanvasLabels(_selectedClip);
        if (_playingClip == _selectedClip) ApplyClipTransform(_selectedClip);
    }
    private void CanvasOffsetX_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress || _selectedClip == null) return;
        _selectedClip.CanvasOffsetX = e.NewValue;
        UpdateCanvasLabels(_selectedClip);
        if (_playingClip == _selectedClip) ApplyClipTransform(_selectedClip);
    }
    private void CanvasOffsetY_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress || _selectedClip == null) return;
        _selectedClip.CanvasOffsetY = e.NewValue;
        UpdateCanvasLabels(_selectedClip);
        if (_playingClip == _selectedClip) ApplyClipTransform(_selectedClip);
    }
    private void CanvasReset_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedClip == null) return;
        _selectedClip.CanvasScale = 1.0;
        _selectedClip.CanvasOffsetX = 0;
        _selectedClip.CanvasOffsetY = 0;
        _suppress = true;
        canvasScaleSlider.Value = 1.0;
        canvasOffsetXSlider.Value = 0;
        canvasOffsetYSlider.Value = 0;
        _suppress = false;
        UpdateCanvasLabels(_selectedClip);
        if (_playingClip == _selectedClip) ApplyClipTransform(_selectedClip);
    }

    // Show one of the three inspector panels (block / clip / export).
    private void ShowInspectorTab(string key)
    {
        bool isBlock = key == "block";
        bool isClip  = key == "clip";
        bool isExp   = key == "export";
        blockPanel.Visibility    = isBlock ? Visibility.Visible : Visibility.Collapsed;
        clipPanel.Visibility     = isClip  ? Visibility.Visible : Visibility.Collapsed;
        emptyInspector.Visibility = isExp  ? Visibility.Visible : Visibility.Collapsed;
        _suppress = true;
        tabBlockBtn.IsChecked = isBlock;
        tabClipBtn.IsChecked  = isClip;
        tabExportBtn.IsChecked = isExp;
        if (tabBlockDot != null) tabBlockDot.Visibility = _selectedBlock != null ? Visibility.Visible : Visibility.Collapsed;
        if (tabClipDot != null)  tabClipDot.Visibility  = _selectedClip != null ? Visibility.Visible : Visibility.Collapsed;
        _suppress = false;
    }

    private void TabBlock_Click(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        ShowInspectorTab("block");
    }
    private void TabClip_Click(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        ShowInspectorTab("clip");
    }
    private void TabExport_Click(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        ShowInspectorTab("export");
    }

    // Segmented mode buttons drive the hidden ComboBox so existing logic still runs.
    private void ModeSolid_Click(object sender, RoutedEventArgs e) { if (!_suppress) modeBox.SelectedIndex = 0; }
    private void ModeBlur_Click(object sender, RoutedEventArgs e)  { if (!_suppress) modeBox.SelectedIndex = 1; }
    private void ModePixel_Click(object sender, RoutedEventArgs e) { if (!_suppress) modeBox.SelectedIndex = 2; }

    // Sync segmented radio button state from a model value (e.g. when SelectBlock loads a block).
    // Null-safe because Mode_Changed fires during XAML init (ComboBoxItem IsSelected="True") before
    // the rows that come later in the panel finish loading.
    private void UpdateModeRadios()
    {
        if (modeBox == null) return;
        var idx = modeBox.SelectedIndex;
        if (modeSolidBtn != null) modeSolidBtn.IsChecked = idx == 0;
        if (modeBlurBtn != null)  modeBlurBtn.IsChecked  = idx == 1;
        if (modePixelBtn != null) modePixelBtn.IsChecked = idx == 2;
        if (colorRow != null)    colorRow.Visibility    = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (strengthRow != null) strengthRow.Visibility = idx == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ExportCrf_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (exportCrfLabel != null) exportCrfLabel.Text = ((int)Math.Round(e.NewValue)).ToString();
    }

    private void DuplicateClip_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedClip != null) DuplicateClip(_selectedClip);
    }

    private void DuplicateBlock_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedBlock == null) return;
        var b = _selectedBlock;
        var nb = new VideoBlock
        {
            X = Math.Min(overlayCanvas.ActualWidth - b.Width - 1, b.X + 20),
            Y = Math.Min(overlayCanvas.ActualHeight - b.Height - 1, b.Y + 20),
            Width = b.Width, Height = b.Height,
            StartSeconds = b.StartSeconds, EndSeconds = b.EndSeconds,
            CoversWholeVideo = b.CoversWholeVideo,
            Color = b.Color, Mode = b.Mode, BlurStrength = b.BlurStrength,
            Label = $"Block {timeline.Blocks.Count + 1}"
        };
        timeline.Blocks.Add(nb);
        var ctl = new VideoEditor.Controls.ResizableBlock(nb);
        ctl.Selected += rb => SelectBlock(rb.Model);
        ctl.Changed  += _  => SyncBlockInspector();
        overlayCanvas.Children.Add(ctl);
        _blockControls[nb] = ctl;
        SelectBlock(nb);
    }

    private void SyncBlockInspector()
    {
        if (_selectedBlock == null) return;
        _suppress = true;
        FillHmsBoxes(_selectedBlock.StartSeconds, startH, startM, startS, startMs);
        FillHmsBoxes(_selectedBlock.EndSeconds, endH, endM, endS, endMs);
        wholeCheck.IsChecked = _selectedBlock.CoversWholeVideo;
        _suppress = false;
    }

    private void RepositionOverlay()
    {
        foreach (var kv in _blockControls)
        {
            Canvas.SetLeft(kv.Value, kv.Key.X);
            Canvas.SetTop(kv.Value, kv.Key.Y);
            kv.Value.Width = kv.Key.Width;
            kv.Value.Height = kv.Key.Height;
        }
    }

    // ===== Inspector handlers =====

    private void LblBox_Changed(object sender, TextChangedEventArgs e) { if (!_suppress && _selectedBlock != null) _selectedBlock.Label = lblBox.Text; }
    private void Mode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppress && _selectedBlock != null) _selectedBlock.Mode = (BlockMode)modeBox.SelectedIndex;
        UpdateModeRadios();
    }
    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedBlock == null) return;
        if (sender is Button b && b.Tag is string name) _selectedBlock.Color = (Color)ColorConverter.ConvertFromString(name);
    }
    private void Strength_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_suppress && _selectedBlock != null) _selectedBlock.BlurStrength = (int)e.NewValue;
        if (strengthLabel != null) strengthLabel.Text = ((int)e.NewValue).ToString();
    }
    private void Whole_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress || _selectedBlock == null) return;
        _selectedBlock.CoversWholeVideo = wholeCheck.IsChecked == true;
        if (_selectedBlock.CoversWholeVideo) { _selectedBlock.StartSeconds = 0; _selectedBlock.EndSeconds = timeline.TotalSeconds; }
    }
    private void StartEnd_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppress || _selectedBlock == null) return;
        var startTotal = ReadHmsBoxes(startH, startM, startS, startMs);
        var endTotal = ReadHmsBoxes(endH, endM, endS, endMs);
        _selectedBlock.StartSeconds = Math.Max(0, startTotal);
        _selectedBlock.EndSeconds = Math.Min(timeline.TotalSeconds, Math.Max(_selectedBlock.StartSeconds + 0.1, endTotal));
        if (_selectedBlock.EndSeconds < timeline.TotalSeconds || _selectedBlock.StartSeconds > 0) _selectedBlock.CoversWholeVideo = false;
    }

    private void ClipInOut_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppress || _selectedClip == null) return;
        // Parse both first, then clamp against each other —” otherwise we'd clamp In against
        // an outdated Out (or vice versa) when the user is editing the second field.
        var inOk = double.TryParse(clipInBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var newIn);
        var outOk = double.TryParse(clipOutBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var newOut);
        if (!inOk) newIn = _selectedClip.InPoint;
        if (!outOk) newOut = _selectedClip.OutPoint;
        newIn = Math.Max(0, newIn);
        newOut = Math.Min(_selectedClip.OriginalDuration, newOut);
        if (newOut < newIn + 0.1) newOut = newIn + 0.1;
        _selectedClip.InPoint = newIn;
        _selectedClip.OutPoint = newOut;
        if (clipEffText != null) clipEffText.Text = "Effective: " + Timeline.FormatTime(_selectedClip.EffectiveDuration);
    }
    private void ClipSpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress || _selectedClip == null) return;
        _selectedClip.Speed = e.NewValue;
        clipSpeedLabel.Text = e.NewValue.ToString("0.00") + "×";
        if (clipEffText != null) clipEffText.Text = "Effective: " + Timeline.FormatTime(_selectedClip.EffectiveDuration);
        if (_playingClip == _selectedClip) videoView.SpeedRatio = e.NewValue;
    }
    private void ClipVol_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress || _selectedClip == null) return;
        _selectedClip.Volume = e.NewValue;
        clipVolLabel.Text = (e.NewValue * 100).ToString("0") + "%";
        if (_playingClip == _selectedClip) videoView.Volume = _masterVolume * e.NewValue;
    }
    private void ClipRotate_Click(object s, RoutedEventArgs e) { if (_selectedClip != null) { _selectedClip.RotateDegrees = (_selectedClip.RotateDegrees + 90) % 360; ApplyClipTransform(_selectedClip); } }
    private void ClipRotateLeft_Click(object s, RoutedEventArgs e) { if (_selectedClip != null) { _selectedClip.RotateDegrees = (_selectedClip.RotateDegrees + 270) % 360; ApplyClipTransform(_selectedClip); } }
    private void ClipFlipH_Click(object s, RoutedEventArgs e) { if (_selectedClip != null) { _selectedClip.FlipH = !_selectedClip.FlipH; ApplyClipTransform(_selectedClip); } }
    private void ClipFlipV_Click(object s, RoutedEventArgs e) { if (_selectedClip != null) { _selectedClip.FlipV = !_selectedClip.FlipV; ApplyClipTransform(_selectedClip); } }
    private void ClipDelete_Click(object s, RoutedEventArgs e)
    {
        if (_selectedClip == null) return;
        var c = _selectedClip;
        timeline.Clips.Remove(c);
        if (_playingClip == c) { _playingClip = null; videoView.Stop(); }
        SelectClip(null);
    }

    // ===== Per-clip context actions =====

    private void OnClipContextAction(VideoClip clip, string action)
    {
        _selectedClip = clip;
        SelectClip(clip);
        switch (action)
        {
            case "trim": Trim_Click(this, new RoutedEventArgs()); break;
            case "speed": Speed_Click(this, new RoutedEventArgs()); break;
            case "volume": Volume_Click(this, new RoutedEventArgs()); break;
            case "rotate90": clip.RotateDegrees = (clip.RotateDegrees + 90) % 360; ApplyClipTransform(clip); break;
            case "flipH": clip.FlipH = !clip.FlipH; ApplyClipTransform(clip); break;
            case "flipV": clip.FlipV = !clip.FlipV; ApplyClipTransform(clip); break;
            case "loop":
                clip.LoopCount += 1;
                status.Text = $"Clip will loop {clip.LoopCount}x on export";
                break;
            case "crop": Crop_Click(this, new RoutedEventArgs()); break;
            case "removeLogo": RemoveLogo_Click(this, new RoutedEventArgs()); break;
            case "addImage": AddImage_Click(this, new RoutedEventArgs()); break;
            case "addText": AddText_Click(this, new RoutedEventArgs()); break;
            case "addAudio": AddAudio_Click(this, new RoutedEventArgs()); break;
            case "stabilize": Stabilize_Click(this, new RoutedEventArgs()); break;
            case "split": SplitAtPlayhead(clip); break;
            case "splitHere":
                SplitClipAtSourceTime(clip, timeline.LastRequestedSplitSourceSec);
                status.Text = "Clip split at click position.";
                break;
            case "splitN":
                {
                    var dlg = new SplitNWindow() { Owner = this };
                    if (dlg.ShowDialog() == true) { SplitIntoNParts(clip, dlg.Parts); status.Text = $"Clip split into {dlg.Parts} parts."; }
                }
                break;
            case "duplicate": DuplicateClip(clip); break;
            case "delete": timeline.Clips.Remove(clip); if (_playingClip == clip) { _playingClip = null; videoView.Stop(); } SelectClip(null); break;
            case "extractAudio": ExtractAudioClip_Click(this, new RoutedEventArgs()); break;
            case "mute": MuteClip_Click(this, new RoutedEventArgs()); break;
            case "unmute": UnmuteClip_Click(this, new RoutedEventArgs()); break;
            case "removeAudioTrack": RemoveAudioTrack_Click(this, new RoutedEventArgs()); break;
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Skip shortcuts if a text box has focus (so typing works normally)
        if (Keyboard.FocusedElement is TextBoxBase) return;

        if (e.Key == Key.F1 || (e.Key == Key.OemQuestion && Keyboard.Modifiers == ModifierKeys.None))
        {
            Help_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        if (e.Key == Key.OemComma && Keyboard.Modifiers == ModifierKeys.None)
        {
            Settings_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        if (e.Key == Key.U && Keyboard.Modifiers == ModifierKeys.None)
        {
            DownloadUrl_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SaveBtn_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenBtn_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            HandleCopy();
            e.Handled = true;
        }
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            HandlePaste();
            e.Handled = true;
        }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.None)
        {
            Split_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.None)
        {
            AddBlock_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Space)
        {
            if (_isPlaying) PauseBtn_Click(this, new RoutedEventArgs());
            else PlayBtn_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            if (_selectedAudio != null && _selectedAudio.IsAudioOnly)
            {
                // Audio-only clip: full delete - it's an independent entity, not just a mute target.
                var ac = _selectedAudio;
                timeline.Clips.Remove(ac);
                _selectedAudio = null;
                _selectedClip = null;
                status.Text = $"Deleted audio clip: {ac.DisplayName}";
                e.Handled = true;
            }
            else if (_selectedAudio != null)
            {
                // Attached audio (sub-bar of a video clip): mute, don't destroy the clip.
                _selectedAudio.Volume = 0;
                status.Text = $"Muted: {_selectedAudio.DisplayName}. Right-click for Remove track / Restore.";
                e.Handled = true;
            }
            else if (_selectedClip != null)
            {
                ClipDelete_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (_selectedBlock != null)
            {
                DeleteBlock_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
    }

    private void OnAudioSelected(VideoClip c)
    {
        _selectedAudio = c;
        _selectedBlock = null;
        // For audio-only clips: treat as a full clip selection so every timeline shortcut works.
        // For attached audio (sub-bar of a video clip): keep selection focused on the audio,
        // not the clip itself.
        SelectClip(c);
        _selectedAudio = c; // SelectClip clears it; restore
        if (!c.IsAudioOnly) _selectedClip = null; // attached audio: don't claim the clip
        timeline.SelectAudio(c);
        if (c.IsAudioOnly)
            status.Text = $"Audio clip: {c.DisplayName} · S = Split · Backspace = Delete · Ctrl+C/V · drag edges to trim · drag body to move";
        else
            status.Text = $"Audio: {c.DisplayName} · Vol {(int)(c.Volume * 100)}% · Backspace = Mute · Ctrl+C/V = copy/paste volume";
    }

    private async void OnAudioContextAction(VideoClip c, string action)
    {
        _selectedClip = c;
        _selectedAudio = c;
        switch (action)
        {
            case "volume":
                {
                    var dlg = new VolumeWindow() { Owner = this };
                    if (dlg.ShowDialog() == true) { c.Volume = dlg.Volume; status.Text = $"Volume ג†’ {(int)(dlg.Volume * 100)}%"; }
                }
                break;
            case "mute":
                c.Volume = 0;
                status.Text = "Audio muted (export will silence).";
                break;
            case "unmute":
                c.Volume = 1;
                status.Text = "Audio restored.";
                break;
            case "removeAudioTrack":
                await ApplyDestructiveOpAsync(c, async (input, output, prog) =>
                    await _ff.RemoveAudioAsync(input, output, c.OriginalDuration, prog));
                status.Text = "Audio track removed from clip.";
                break;
            case "extractAudio":
                ExtractAudio_Click(this, new RoutedEventArgs());
                break;
            case "addAudio":
                AddAudio_Click(this, new RoutedEventArgs());
                break;
            case "detachAudio":
                await DetachAudioFromClipAsync(c);
                break;
            case "deleteAudioClip":
                timeline.Clips.Remove(c);
                _selectedAudio = null;
                status.Text = "Detached audio clip deleted.";
                break;
        }
    }

    private async System.Threading.Tasks.Task DetachAudioFromClipAsync(VideoClip parent)
    {
        if (parent.IsAudioOnly) { status.Text = "Already an audio-only clip."; return; }

        status.Text = "Detaching audio from " + parent.DisplayName + "...";
        progress.Value = 0;
        var prog = new Progress<double>(v => Dispatcher.Invoke(() => progress.Value = v));

        try
        {
            // Extract to a stable cache folder next to the EXE
            var detachedDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "detached_audio");
            Directory.CreateDirectory(detachedDir);
            var outputPath = Path.Combine(detachedDir,
                $"{Path.GetFileNameWithoutExtension(parent.SourceFile)}_{Guid.NewGuid():N}.m4a");

            await _ff.ExtractAudioAsync(parent.SourceFile, outputPath, parent.OriginalDuration, prog);
            var (w, h, d) = await _ff.ProbeAsync(outputPath);

            // Create a new audio-only clip with the same in/out points as the parent.
            var audioClip = new VideoClip
            {
                SourceFile = outputPath,
                OriginalDuration = d > 0 ? d : parent.OriginalDuration,
                InPoint = parent.InPoint,
                OutPoint = Math.Min(parent.OutPoint, d > 0 ? d : parent.OutPoint),
                Speed = parent.Speed,
                Volume = parent.Volume,
                VideoWidth = 0, VideoHeight = 0,
                AccentColor = System.Windows.Media.Color.FromRgb(0x6E, 0x44, 0xD6),
                IsAudioOnly = true,
                TimelineStart = parent.TimelineStart
            };
            timeline.Clips.Add(audioClip);

            progress.Value = 1;
            status.Text = $"ג“ Audio detached as independent clip. Drag the purple bar in A1, trim its edges, split with S, copy with Ctrl+C, delete with Backspace.";
            // Select the new audio clip so the user can immediately work with it.
            timeline.SelectAudio(audioClip);
        }
        catch (Exception ex)
        {
            status.Text = "Detach failed: " + ex.Message;
            MessageBox.Show("Failed to detach audio:\n" + ex.Message, "Detach Audio");
        }
    }

    private void HandleCopy()
    {
        if (_selectedAudio != null)
        {
            _clipboardAudioVolume = _selectedAudio.Volume;
            _clipboardClip = null;
            _clipboardBlock = null;
            status.Text = $"Copied audio (volume {(int)(_selectedAudio.Volume * 100)}%). Select another clip's audio and Ctrl+V to paste.";
        }
        else if (_selectedClip != null)
        {
            _clipboardClip = _selectedClip;
            _clipboardBlock = null;
            _clipboardAudioVolume = null;
            status.Text = "Copied clip: " + _selectedClip.DisplayName;
        }
        else if (_selectedBlock != null)
        {
            _clipboardBlock = _selectedBlock;
            _clipboardClip = null;
            _clipboardAudioVolume = null;
            status.Text = "Copied block: " + _selectedBlock.Label;
        }
        else
        {
            status.Text = "Nothing selected to copy. Click a clip / audio bar / block first.";
        }
    }

    private void HandlePaste()
    {
        if (_clipboardAudioVolume.HasValue && _selectedAudio != null)
        {
            _selectedAudio.Volume = _clipboardAudioVolume.Value;
            status.Text = $"Pasted audio volume ({(int)(_clipboardAudioVolume.Value * 100)}%) to {_selectedAudio.DisplayName}";
            return;
        }
        if (_clipboardClip != null)
        {
            var c = _clipboardClip;
            var newClip = new VideoClip
            {
                SourceFile = c.SourceFile,
                OriginalDuration = c.OriginalDuration,
                InPoint = c.InPoint,
                OutPoint = c.OutPoint,
                Speed = c.Speed,
                Volume = c.Volume,
                RotateDegrees = c.RotateDegrees,
                FlipH = c.FlipH,
                FlipV = c.FlipV,
                VideoWidth = c.VideoWidth,
                VideoHeight = c.VideoHeight,
                AccentColor = c.IsAudioOnly ? c.AccentColor : VideoClip.NextColor(),
                LoopCount = c.LoopCount,
                IsAudioOnly = c.IsAudioOnly
            };
            // Insert right after the source clip (audio-only: free position; video: ripple-abut)
            timeline.ReorderClipToPosition(newClip, c.TimelineStart + c.EffectiveDuration + 0.001);
            status.Text = c.IsAudioOnly ? "Pasted audio clip." : "Pasted clip after original.";
        }
        else if (_clipboardBlock != null)
        {
            var b = _clipboardBlock;
            var newBlock = new VideoBlock
            {
                X = Math.Min(overlayCanvas.ActualWidth - b.Width - 1, b.X + 20),
                Y = Math.Min(overlayCanvas.ActualHeight - b.Height - 1, b.Y + 20),
                Width = b.Width,
                Height = b.Height,
                StartSeconds = b.StartSeconds,
                EndSeconds = b.EndSeconds,
                CoversWholeVideo = b.CoversWholeVideo,
                Color = b.Color,
                Mode = b.Mode,
                BlurStrength = b.BlurStrength,
                Label = $"Block {timeline.Blocks.Count + 1}"
            };
            timeline.Blocks.Add(newBlock);
            var ctl = new ResizableBlock(newBlock);
            ctl.Selected += rb => SelectBlock(rb.Model);
            ctl.Changed += _ => SyncBlockInspector();
            overlayCanvas.Children.Add(ctl);
            _blockControls[newBlock] = ctl;
            SelectBlock(newBlock);
            status.Text = "Pasted block.";
        }
        else
        {
            status.Text = "Clipboard is empty - copy something first with Ctrl+C.";
        }
    }

    private void UpdateBlockVisibility()
    {
        var t = timeline.CurrentSeconds;
        foreach (var kv in _blockControls)
        {
            var block = kv.Key;
            var ctl = kv.Value;
            // Blocks render exactly the way the exported video will look: a block is on-
            // screen iff the playhead is inside its time range (or it covers the whole
            // project). One concession to editability: the *currently selected* block stays
            // visible even outside its range — otherwise it'd disappear the moment you
            // selected it to drag/resize.
            bool inRange = block.CoversWholeVideo || (t >= block.StartSeconds && t <= block.EndSeconds);
            bool isEditing = block == _selectedBlock;
            ctl.Visibility = (inRange || isEditing) ? Visibility.Visible : Visibility.Hidden;
        }
        UpdateTextOverlaysVisibility(t);
    }

    private readonly Dictionary<TextOverlay, Border> _textOverlayPreviewControls = new();
    /// <summary>Overlays that need their preview control restyled / repositioned on next Tick.
    /// New / edited / resize-affected overlays go in here; the Tick clears it after processing.
    /// This avoids the per-Tick brush churn that made scrubbing stutter once ~40 AI captions
    /// were on the timeline.</summary>
    private readonly HashSet<TextOverlay> _textOverlayDirty = new();

    private void UpdateTextOverlaysVisibility(double currentSec)
    {
        if (overlayCanvas == null) return;
        // Clean up stale controls (deleted overlays).
        foreach (var stale in _textOverlayPreviewControls.Keys.Where(o => !timeline.TextOverlays.Contains(o)).ToList())
        {
            if (_textOverlayPreviewControls.TryGetValue(stale, out var ctl)) overlayCanvas.Children.Remove(ctl);
            _textOverlayPreviewControls.Remove(stale);
            _textOverlayDirty.Remove(stale);
        }

        double canvasW = overlayCanvas.ActualWidth, canvasH = overlayCanvas.ActualHeight;
        if (canvasW < 1 || canvasH < 1) return;
        var firstVideo = timeline.Clips.FirstOrDefault(c => !c.IsAudioOnly);
        if (firstVideo == null || firstVideo.VideoWidth <= 0 || firstVideo.VideoHeight <= 0) return;
        double sx = canvasW / firstVideo.VideoWidth;
        double sy = canvasH / firstVideo.VideoHeight;
        double s = Math.Min(sx, sy);

        foreach (var ov in timeline.TextOverlays)
        {
            bool isNew = !_textOverlayPreviewControls.TryGetValue(ov, out var ctl);
            if (isNew)
            {
                ctl = MakeTextOverlayPreviewControl(ov);
                _textOverlayPreviewControls[ov] = ctl;
                overlayCanvas.Children.Add(ctl);
                ApplyOverlayStyle(ctl, ov, s);
                ApplyOverlayPlacement(ctl, ov, s);
            }
            else if (_textOverlayDirty.Contains(ov))
            {
                ApplyOverlayStyle(ctl!, ov, s);
                ApplyOverlayPlacement(ctl!, ov, s);
            }
            // Per-tick we only flip Visibility — the cheap part. Style + placement
            // are no-ops unless the overlay was just added or marked dirty.
            bool active = !_isPlaying || (currentSec >= ov.StartSeconds && currentSec <= ov.EndSeconds);
            ctl!.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        }
        _textOverlayDirty.Clear();
    }

    private Border MakeTextOverlayPreviewControl(TextOverlay ov)
    {
        var tb = new TextBlock
        {
            Text = ov.Text,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = ov.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = ov.Italic ? FontStyles.Italic : FontStyles.Normal,
            TextWrapping = TextWrapping.Wrap
        };
        tb.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 0, Opacity = 0.9 };
        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            Child = tb,
            IsHitTestVisible = false
        };
        ApplyOverlayStyle(border, ov, 1.0);
        return border;
    }

    private static void ApplyOverlayStyle(Border ctl, TextOverlay ov, double scale)
    {
        if (ctl.Child is not TextBlock tb) return;
        tb.Text = ov.Text;
        tb.FontWeight = ov.Bold ? FontWeights.Bold : FontWeights.Normal;
        tb.FontStyle = ov.Italic ? FontStyles.Italic : FontStyles.Normal;
        tb.Foreground = new SolidColorBrush(ParseDrawtextColor(ov.FontColor));
        tb.FontSize = Math.Max(6, ov.FontSize * scale);

        if (ov.BackgroundEnabled && ov.BackgroundOpacity > 0)
        {
            var c = ParseDrawtextColor(ov.BackgroundColor);
            ctl.Background = new SolidColorBrush(Color.FromArgb((byte)(ov.BackgroundOpacity * 255), c.R, c.G, c.B));
            var pad = Math.Max(2, ov.BackgroundPadding * scale);
            ctl.Padding = new Thickness(pad, pad * 0.55, pad, pad * 0.55);
        }
        else
        {
            ctl.Background = Brushes.Transparent;
            ctl.Padding = new Thickness(0);
        }
    }

    private void ApplyOverlayPlacement(Border ctl, TextOverlay ov, double scale)
    {
        Canvas.SetLeft(ctl, ov.X * scale);
        Canvas.SetTop(ctl, ov.Y * scale);
    }

    private static Color ParseDrawtextColor(string s)
    {
        if (string.IsNullOrEmpty(s)) return Colors.White;
        var t = s.Trim();
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = "#" + t.Substring(2);
        try { return (Color)ColorConverter.ConvertFromString(t); } catch { return Colors.White; }
    }

    private void Split_Click(object sender, RoutedEventArgs e)
    {
        var clip = _selectedClip ?? timeline.GetClipAt(timeline.CurrentSeconds);
        if (clip == null) { status.Text = "Move playhead onto a clip first to split."; return; }
        if (timeline.CurrentSeconds <= clip.TimelineStart + 0.05 ||
            timeline.CurrentSeconds >= clip.TimelineStart + clip.EffectiveDuration - 0.05)
        {
            status.Text = "Move the red playhead INSIDE the clip first, then press Split.";
            return;
        }
        SplitAtPlayhead(clip);
        status.Text = "Clip split at playhead.";
    }

    private void SplitN_Click(object sender, RoutedEventArgs e)
    {
        var clip = _selectedClip ?? timeline.GetClipAt(timeline.CurrentSeconds);
        if (clip == null) { status.Text = "Select a clip first."; return; }
        var dlg = new SplitNWindow() { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            SplitIntoNParts(clip, dlg.Parts);
            status.Text = $"Clip split into {dlg.Parts} parts.";
        }
    }

    private void SplitAtPlayhead(VideoClip clip)
    {
        var withinClip = (timeline.CurrentSeconds - clip.TimelineStart) * clip.Speed;
        var splitAt = clip.InPoint + withinClip;
        SplitClipAtSourceTime(clip, splitAt);
    }

    private void SplitClipAtSourceTime(VideoClip clip, double splitAt)
    {
        if (splitAt <= clip.InPoint + 0.1 || splitAt >= clip.OutPoint - 0.1)
        {
            status.Text = "Split point too close to clip edge - try further inside.";
            return;
        }
        // When the clip is looped, both halves should also loop the same number of times so
        // the total timeline length stays consistent (the previous code only kept LoopCount on
        // the left, producing a left half that loops and a right half that doesn't).
        var loopCount = Math.Max(1, clip.LoopCount);
        var leftDur = (splitAt - clip.InPoint) / Math.Max(0.01, clip.Speed) * loopCount;
        var newClip = new VideoClip
        {
            SourceFile = clip.SourceFile,
            OriginalDuration = clip.OriginalDuration,
            InPoint = splitAt,
            OutPoint = clip.OutPoint,
            Speed = clip.Speed,
            Volume = clip.Volume,
            RotateDegrees = clip.RotateDegrees,
            FlipH = clip.FlipH,
            FlipV = clip.FlipV,
            VideoWidth = clip.VideoWidth,
            VideoHeight = clip.VideoHeight,
            AccentColor = clip.IsAudioOnly ? clip.AccentColor : VideoClip.NextColor(),
            LoopCount = loopCount,
            IsAudioOnly = clip.IsAudioOnly,  // Preserve type when splitting audio-only clips
            TimelineStart = clip.TimelineStart + leftDur
        };
        clip.OutPoint = splitAt;
        timeline.Clips.Add(newClip);
    }

    private void SplitIntoNParts(VideoClip clip, int n)
    {
        if (n < 2) return;
        var totalSrc = clip.OutPoint - clip.InPoint;
        var partSrc = totalSrc / n;
        var origIn = clip.InPoint;
        var origOut = clip.OutPoint;
        var origTLStart = clip.TimelineStart;
        var origSpeed = Math.Max(0.01, clip.Speed);
        var partTL = partSrc / origSpeed;

        // Shrink the original to be the first part
        clip.OutPoint = origIn + partSrc;

        // Add N-1 new parts
        var newClips = new List<VideoClip>();
        for (int i = 1; i < n; i++)
        {
            var newIn = origIn + partSrc * i;
            var newOut = (i == n - 1) ? origOut : (origIn + partSrc * (i + 1));
            var nc = new VideoClip
            {
                SourceFile = clip.SourceFile,
                OriginalDuration = clip.OriginalDuration,
                InPoint = newIn,
                OutPoint = newOut,
                Speed = origSpeed,
                Volume = clip.Volume,
                RotateDegrees = clip.RotateDegrees,
                FlipH = clip.FlipH,
                FlipV = clip.FlipV,
                VideoWidth = clip.VideoWidth,
                VideoHeight = clip.VideoHeight,
                AccentColor = VideoClip.NextColor(),
                LoopCount = 1,
                TimelineStart = origTLStart + partTL * i
            };
            newClips.Add(nc);
        }
        foreach (var nc in newClips) timeline.Clips.Add(nc);
    }

    private void DuplicateClip(VideoClip clip)
    {
        var d = new VideoClip
        {
            SourceFile = clip.SourceFile,
            OriginalDuration = clip.OriginalDuration,
            InPoint = clip.InPoint,
            OutPoint = clip.OutPoint,
            Speed = clip.Speed,
            Volume = clip.Volume,
            RotateDegrees = clip.RotateDegrees,
            FlipH = clip.FlipH,
            FlipV = clip.FlipV,
            VideoWidth = clip.VideoWidth,
            VideoHeight = clip.VideoHeight,
            AccentColor = clip.IsAudioOnly ? clip.AccentColor : VideoClip.NextColor(),
            LoopCount = clip.LoopCount,
            IsAudioOnly = clip.IsAudioOnly,
            TimelineStart = clip.TimelineStart + clip.EffectiveDuration
        };
        timeline.Clips.Add(d);
    }

    // ===== Export =====

    private async void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (timeline.Clips.Count == 0) { MessageBox.Show("Add at least one video clip."); return; }
        var ext = AppSettings.ExportContainer;
        var sfd = new SaveFileDialog
        {
            FileName = "project_export." + ext,
            DefaultExt = ext,
            Filter = ext switch
            {
                "mov" => "MOV|*.mov|MP4|*.mp4|MKV|*.mkv|WebM|*.webm",
                "mkv" => "MKV|*.mkv|MP4|*.mp4|MOV|*.mov|WebM|*.webm",
                "webm" => "WebM|*.webm|MP4|*.mp4|MOV|*.mov|MKV|*.mkv",
                _ => "MP4|*.mp4|MOV|*.mov|MKV|*.mkv|WebM|*.webm"
            }
        };
        if (sfd.ShowDialog() != true) return;

        status.Text = "Exporting...";
        progress.Value = 0;
        var prog = new Progress<double>(v => Dispatcher.Invoke(() => progress.Value = v));
        try
        {
            // Export in timeline order
            var orderedClips = timeline.Clips.OrderBy(c => c.TimelineStart).ToList();
            var firstVideo = orderedClips.FirstOrDefault(c => !c.IsAudioOnly);
            var (tW, tH) = VideoEditor.Services.ProjectFormats.Resolve(AppSettings.TargetFormatPreset, firstVideo);
            await _ff.ExportProjectAsync(orderedClips, timeline.Blocks.ToList(),
                tW, tH,
                overlayCanvas.ActualWidth, overlayCanvas.ActualHeight,
                timeline.TotalSeconds, sfd.FileName, AppSettings.TargetFitMode,
                timeline.TextOverlays.ToList(), prog);
            status.Text = "Exported: " + sfd.FileName;
            progress.Value = 1;
            if (MessageBox.Show("Open output folder?", "Done", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                RevealInExplorer(sfd.FileName);
        }
        catch (Exception ex)
        {
            status.Text = "Export failed: " + ex.Message;
            MessageBox.Show(ex.Message, "Export Error");
        }
    }

    // ===== Quick Tool buttons (operate on selected clip; default to first if none) =====

    private VideoClip? CurrentClip() => _selectedClip ?? _playingClip ?? (timeline.Clips.Count > 0 ? timeline.Clips[0] : null);

    private void ScreenRec_Click(object s, RoutedEventArgs e) => new ScreenRecorderWindow(_ff) { Owner = this }.ShowDialog();
    private void Tts_Click(object s, RoutedEventArgs e) => new TextToSpeechWindow() { Owner = this }.ShowDialog();
    private async void Merge_Click(object s, RoutedEventArgs e)
    {
        if (timeline.Clips.Count < 2) { MessageBox.Show("Need at least 2 clips."); return; }
        SaveBtn_Click(s, e);
        await System.Threading.Tasks.Task.CompletedTask;
    }
    private void Record_Click(object s, RoutedEventArgs e) => new ScreenRecorderWindow(_ff, true) { Owner = this }.ShowDialog();

    private void Settings_Click(object s, RoutedEventArgs e) => new SettingsWindow() { Owner = this }.ShowDialog();

    private void ApplyLanguage()
    {
        bool isHe = VideoEditor.Services.Localization.IsHebrew;
        FlowDirection = isHe ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight;

        openBtn.ToolTip = isHe ? "פתח קבצי וידאו" : "Open video files";
        saveBtn.ToolTip = isHe ? "ייצוא הפרויקט (Ctrl+E)" : "Export project (Ctrl+E)";
        settingsBtn.ToolTip = isHe ? "הגדרות · ," : "Settings · ,";
        helpBtn.ToolTip = isHe ? "מדריך למשתמש · ?" : "User Guide · ?";

        VideoEditor.Services.Localization.TranslateTree(this);
    }

private void Help_Click(object s, RoutedEventArgs e) => new UserGuideWindow() { Owner = this }.ShowDialog();

    private async void DownloadUrl_Click(object s, RoutedEventArgs e)
    {
        var defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "VideoEditorDownloads");
        var dlg = new DownloadUrlWindow(defaultFolder) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var url = dlg.Url;
        var folder = dlg.OutputFolder;
        bool useStreaming = dlg.UseStreamingDownloader;
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Cannot create destination folder: " + ex.Message);
            return;
        }

        var dl = new DownloadService();
        dl.Log += msg => Dispatcher.Invoke(() => status.Text = msg.Length > 80 ? msg[..80] + "…" : msg);
        var prog = new Progress<double>(v => Dispatcher.Invoke(() => progress.Value = v));

        status.Text = "Starting download...";
        progress.Value = 0;
        try
        {
            string finalPath;
            if (useStreaming)
            {
                finalPath = await dl.DownloadViaYtDlpAsync(url, folder, App.FFmpegPath, prog);
            }
            else
            {
                var uri = new Uri(url);
                var fileName = Path.GetFileName(uri.LocalPath);
                if (string.IsNullOrWhiteSpace(fileName) || !fileName.Contains('.')) fileName = "downloaded.mp4";
                var outputPath = Path.Combine(folder, fileName);
                finalPath = await dl.DownloadDirectAsync(url, outputPath, prog);
            }
            status.Text = $"Downloaded ג†’ adding to timeline: {Path.GetFileName(finalPath)}";
            await AddClipAsync(finalPath);
            progress.Value = 1;
            status.Text = $"Added downloaded clip: {Path.GetFileName(finalPath)}";
        }
        catch (Exception ex)
        {
            status.Text = "Download failed: " + ex.Message;
            MessageBox.Show("Failed to download:\n\n" + ex.Message, "Download Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Trim_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip(); if (c == null) return;
        var dlg = new TrimWindow(c.OriginalDuration) { Owner = this };
        if (dlg.ShowDialog() == true) { c.InPoint = dlg.StartSec; c.OutPoint = dlg.EndSec; SelectClip(c); }
    }
    private void Speed_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip(); if (c == null) return;
        var dlg = new SpeedWindow() { Owner = this };
        if (dlg.ShowDialog() == true) { c.Speed = dlg.Speed; SelectClip(c); }
    }
    private void Volume_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip(); if (c == null) return;
        var dlg = new VolumeWindow() { Owner = this };
        if (dlg.ShowDialog() == true) { c.Volume = dlg.Volume; SelectClip(c); }
    }
    private void Rotate_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip(); if (c == null) return;
        var dlg = new RotateWindow() { Owner = this };
        if (dlg.ShowDialog() == true) { c.RotateDegrees = dlg.Degrees; ApplyClipTransform(c); }
    }
    private void Flip_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip(); if (c == null) return;
        var dlg = new FlipWindow() { Owner = this };
        if (dlg.ShowDialog() == true) { if (dlg.Horizontal) c.FlipH = !c.FlipH; else c.FlipV = !c.FlipV; ApplyClipTransform(c); }
    }
    private void Loop_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip(); if (c == null) return;
        var dlg = new LoopWindow() { Owner = this };
        if (dlg.ShowDialog() == true) { c.LoopCount = dlg.Times; status.Text = $"Clip will loop {c.LoopCount}x on export"; }
    }
    private async void Crop_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip();
        if (c == null) { MessageBox.Show("Select a clip first."); return; }

        // Extract a preview frame so the user can see what they're cropping
        var tempFrame = Path.Combine(Path.GetTempPath(), $"crop_{Guid.NewGuid():N}.jpg");
        try
        {
            double frameTime;
            if (timeline.CurrentSeconds >= c.TimelineStart && timeline.CurrentSeconds <= c.TimelineStart + c.EffectiveDuration)
            {
                var withinClip = (timeline.CurrentSeconds - c.TimelineStart) * c.Speed;
                frameTime = c.InPoint + withinClip;
            }
            else
            {
                frameTime = c.InPoint + (c.OutPoint - c.InPoint) / 2;
            }
            status.Text = "Extracting preview frame...";
            await _ff.ExtractFrameAsync(c.SourceFile, tempFrame, frameTime);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to extract preview: " + ex.Message);
            return;
        }

        var picker = new VisualRegionPickerWindow(tempFrame,
            c.VideoWidth > 0 ? c.VideoWidth : 1920,
            c.VideoHeight > 0 ? c.VideoHeight : 1080,
            "Crop Video - Mark Area to Keep",
            "Drag the green rectangle to mark the area you want to KEEP. The darkened area outside will be cropped out. Drag corners to resize.",
            darkenOutside: true,
            selectionLabel: "ג„ KEEP",
            accentColor: System.Windows.Media.Color.FromRgb(0x4D, 0xFF, 0x88))
        { Owner = this };

        var ok = picker.ShowDialog() == true;
        try { File.Delete(tempFrame); } catch { }
        if (!ok) { status.Text = "Crop cancelled."; return; }

        await ApplyDestructiveOpAsync(c, async (input, output, prog) =>
            await _ff.CropAsync(input, output, picker.X, picker.Y, picker.W, picker.H, c.OriginalDuration, prog));
        status.Text = "Clip cropped.";
    }
    private async void Resize_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip(); if (c == null) return;
        var dlg = new ResizeWindow(c.VideoWidth, c.VideoHeight) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        await ApplyDestructiveOpAsync(c, async (input, output, prog) =>
            await _ff.ResizeAsync(input, output, dlg.W, dlg.H, c.OriginalDuration, prog));
    }
    private async void Stabilize_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip(); if (c == null) return;
        if (MessageBox.Show("Run 2-pass stabilization on this clip? Replaces source.", "Stabilize", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        await ApplyDestructiveOpAsync(c, async (input, output, prog) =>
            await _ff.StabilizeAsync(input, output, c.OriginalDuration, prog));
    }
    private async void AddImage_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip(); if (c == null) return;
        var dlg = new AddImageWindow() { Owner = this };
        if (dlg.ShowDialog() != true) return;
        await ApplyDestructiveOpAsync(c, async (input, output, prog) =>
            await _ff.AddImageAsync(input, dlg.ImageFile, output, dlg.X, dlg.Y, c.OriginalDuration, prog));
    }
    private async void AddText_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip();
        if (c == null) { MessageBox.Show("Select a clip first."); return; }
        await OpenTextPickerAndAddAsync(c, null);
    }

    private void AiCaptions_Click(object s, RoutedEventArgs e)
    {
        var anyVideo = false;
        foreach (var c in timeline.Clips) if (!c.IsAudioOnly) { anyVideo = true; break; }
        if (!anyVideo)
        {
            MessageBox.Show(VideoEditor.Services.Localization.T("Add a video clip first."),
                "AI Captions", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(AppSettings.LlmApiKey))
        {
            MessageBox.Show(
                VideoEditor.Services.Localization.T("Set your Gemini API key first — opening Settings…"),
                "AI Captions", MessageBoxButton.OK, MessageBoxImage.Information);
            new SettingsWindow("ai") { Owner = this }.ShowDialog();
            if (string.IsNullOrWhiteSpace(AppSettings.LlmApiKey)) return;
        }

        var selected = CurrentClip();
        int width = 1920, height = 1080;
        foreach (var clip in timeline.Clips)
        {
            if (clip.IsAudioOnly) continue;
            if (clip.VideoWidth > 0 && clip.VideoHeight > 0)
            {
                width = clip.VideoWidth;
                height = clip.VideoHeight;
                break;
            }
        }

        var dlg = new AiCaptionsWindow(selected, timeline.Clips, width, height) { Owner = this };
        var ok = dlg.ShowDialog() == true;
        if (!ok || dlg.Result.Count == 0) return;

        foreach (var ov in dlg.Result) timeline.TextOverlays.Add(ov);

        status.Text = VideoEditor.Services.Localization.T("AI Captions added · {0} overlays — drag bars on the timeline to tweak.")
            .Replace("{0}", dlg.Result.Count.ToString());
    }

    private async System.Threading.Tasks.Task OpenTextPickerAndAddAsync(VideoClip c, TextOverlay? edit)
    {
        var tempFrame = Path.Combine(Path.GetTempPath(), $"text_frame_{Guid.NewGuid():N}.jpg");
        try
        {
            double frameTime;
            if (edit != null)
            {
                var midProject = (edit.StartSeconds + edit.EndSeconds) / 2.0;
                if (midProject >= c.TimelineStart && midProject <= c.TimelineStart + c.EffectiveDuration)
                    frameTime = c.InPoint + (midProject - c.TimelineStart) * c.Speed;
                else
                    frameTime = c.InPoint + (c.OutPoint - c.InPoint) / 2;
            }
            else if (timeline.CurrentSeconds >= c.TimelineStart && timeline.CurrentSeconds <= c.TimelineStart + c.EffectiveDuration)
            {
                frameTime = c.InPoint + (timeline.CurrentSeconds - c.TimelineStart) * c.Speed;
            }
            else
            {
                frameTime = c.InPoint + (c.OutPoint - c.InPoint) / 2;
            }
            status.Text = "Extracting preview frame...";
            await _ff.ExtractFrameAsync(c.SourceFile, tempFrame, frameTime);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to extract preview: " + ex.Message);
            return;
        }

        TextOverlayOptions? initial = null;
        if (edit != null)
        {
            initial = new TextOverlayOptions
            {
                Text = edit.Text, X = edit.X, Y = edit.Y,
                FontSize = edit.FontSize, FontColor = edit.FontColor,
                Bold = edit.Bold, Italic = edit.Italic,
                BackgroundEnabled = edit.BackgroundEnabled,
                BackgroundColor = edit.BackgroundColor,
                BackgroundOpacity = edit.BackgroundOpacity,
                BackgroundPadding = edit.BackgroundPadding
            };
        }

        var picker = new TextOverlayPickerWindow(tempFrame,
            c.VideoWidth > 0 ? c.VideoWidth : 1920,
            c.VideoHeight > 0 ? c.VideoHeight : 1080,
            initial)
        { Owner = this };
        var ok = picker.ShowDialog() == true;
        try { File.Delete(tempFrame); } catch { }
        if (!ok || string.IsNullOrWhiteSpace(picker.Result.Text)) return;

        var opt = picker.Result;
        if (edit == null)
        {
            double start = Math.Max(c.TimelineStart, timeline.CurrentSeconds);
            double end = c.TimelineStart + c.EffectiveDuration;
            if (end - start < 0.5) { start = c.TimelineStart; end = c.TimelineStart + c.EffectiveDuration; }
            var ov = new TextOverlay
            {
                Text = opt.Text, X = opt.X, Y = opt.Y,
                FontSize = opt.FontSize, FontColor = opt.FontColor,
                Bold = opt.Bold, Italic = opt.Italic,
                BackgroundEnabled = opt.BackgroundEnabled,
                BackgroundColor = opt.BackgroundColor,
                BackgroundOpacity = opt.BackgroundOpacity,
                BackgroundPadding = opt.BackgroundPadding,
                StartSeconds = start, EndSeconds = end
            };
            timeline.TextOverlays.Add(ov);
            timeline.SelectTextOverlay(ov);
            status.Text = "Text overlay added. Drag the teal bar on the timeline to move or resize it.";
        }
        else
        {
            edit.Text = opt.Text; edit.X = opt.X; edit.Y = opt.Y;
            edit.FontSize = opt.FontSize; edit.FontColor = opt.FontColor;
            edit.Bold = opt.Bold; edit.Italic = opt.Italic;
            edit.BackgroundEnabled = opt.BackgroundEnabled;
            edit.BackgroundColor = opt.BackgroundColor;
            edit.BackgroundOpacity = opt.BackgroundOpacity;
            edit.BackgroundPadding = opt.BackgroundPadding;
            timeline.NotifyTextOverlayChanged(edit);
            status.Text = "Text overlay updated.";
        }
    }

    private async void OnTextOverlayContext(TextOverlay o, string action)
    {
        if (action == "delete")
        {
            timeline.TextOverlays.Remove(o);
            status.Text = "Text overlay deleted.";
        }
        else if (action == "edit")
        {
            var clip = CurrentClip() ?? timeline.Clips.FirstOrDefault(c => !c.IsAudioOnly);
            if (clip == null) { MessageBox.Show("Add a video clip first."); return; }
            await OpenTextPickerAndAddAsync(clip, o);
        }
    }
    private async void RemoveLogo_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip();
        if (c == null) { MessageBox.Show("Select a clip first."); return; }

        // Extract a preview frame so the user can see what they're selecting
        var tempFrame = Path.Combine(Path.GetTempPath(), $"removelogo_{Guid.NewGuid():N}.jpg");
        try
        {
            double frameTime;
            if (timeline.CurrentSeconds >= c.TimelineStart && timeline.CurrentSeconds <= c.TimelineStart + c.EffectiveDuration)
            {
                var withinClip = (timeline.CurrentSeconds - c.TimelineStart) * c.Speed;
                frameTime = c.InPoint + withinClip;
            }
            else
            {
                frameTime = c.InPoint + (c.OutPoint - c.InPoint) / 2;
            }
            status.Text = "Extracting preview frame...";
            await _ff.ExtractFrameAsync(c.SourceFile, tempFrame, frameTime);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to extract preview frame: " + ex.Message);
            return;
        }

        var picker = new VisualRegionPickerWindow(tempFrame,
            c.VideoWidth > 0 ? c.VideoWidth : 1920,
            c.VideoHeight > 0 ? c.VideoHeight : 1080,
            "Remove Logo - Mark the Logo Area",
            "Drag the yellow rectangle over the logo, and resize it using the corners. The marked area will be blurred out across the entire clip.")
        { Owner = this };

        var ok = picker.ShowDialog() == true;
        try { File.Delete(tempFrame); } catch { }
        if (!ok) { status.Text = "Remove Logo cancelled."; return; }

        await ApplyDestructiveOpAsync(c, async (input, output, prog) =>
            await _ff.RemoveLogoAsync(input, output, picker.X, picker.Y, picker.W, picker.H, c.OriginalDuration, prog));
    }
    private async void AddAudio_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip(); if (c == null) return;
        var dlg = new AddAudioWindow() { Owner = this };
        if (dlg.ShowDialog() != true) return;
        await ApplyDestructiveOpAsync(c, async (input, output, prog) =>
            await _ff.AddAudioAsync(input, dlg.AudioFile, output, c.OriginalDuration, prog));
    }

    // ===== Audio operations =====

    private async void ExtractAudioClip_Click(object sender, RoutedEventArgs e)
    {
        var c = CurrentClip();
        if (c == null) { status.Text = "Select a clip first."; return; }
        var sfd = new SaveFileDialog
        {
            FileName = Path.GetFileNameWithoutExtension(c.SourceFile) + "_audio.mp3",
            Filter = "MP3|*.mp3|WAV|*.wav|M4A|*.m4a|AAC|*.aac|OGG|*.ogg|FLAC|*.flac"
        };
        if (sfd.ShowDialog() != true) return;
        var fmt = Path.GetExtension(sfd.FileName).TrimStart('.').ToLowerInvariant();
        var dur = c.OutPoint - c.InPoint;
        status.Text = "Extracting audio...";
        progress.Value = 0;
        var prog = new Progress<double>(v => Dispatcher.Invoke(() => progress.Value = v));
        try
        {
            await _ff.ExtractAudioAsync(c.SourceFile, sfd.FileName, c.InPoint, dur, fmt, prog);
            status.Text = "Audio saved: " + sfd.FileName;
            progress.Value = 1;
            if (MessageBox.Show("Open output folder?", "Done", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                RevealInExplorer(sfd.FileName);
        }
        catch (Exception ex)
        {
            status.Text = "Extract failed: " + ex.Message;
            MessageBox.Show(ex.Message, "Error");
        }
    }

    private async void ExtractAudioProject_Click(object sender, RoutedEventArgs e)
    {
        if (timeline.Clips.Count == 0) { MessageBox.Show("Add clips first."); return; }
        var sfd = new SaveFileDialog
        {
            FileName = "project_audio.mp3",
            Filter = "MP3|*.mp3|WAV|*.wav|M4A|*.m4a|AAC|*.aac|OGG|*.ogg|FLAC|*.flac"
        };
        if (sfd.ShowDialog() != true) return;

        var tempVideo = Path.Combine(Path.GetTempPath(), $"ve_audio_export_{Guid.NewGuid():N}.mp4");
        var fmt = Path.GetExtension(sfd.FileName).TrimStart('.').ToLowerInvariant();
        status.Text = "Rendering project for audio extraction...";
        progress.Value = 0;
        try
        {
            var orderedClips = timeline.Clips.OrderBy(x => x.TimelineStart).ToList();
            var firstVideo = orderedClips.FirstOrDefault(c => !c.IsAudioOnly);
            var (tW, tH) = VideoEditor.Services.ProjectFormats.Resolve(AppSettings.TargetFormatPreset, firstVideo);
            var prog1 = new Progress<double>(v => Dispatcher.Invoke(() => progress.Value = v * 0.8));
            await _ff.ExportProjectAsync(orderedClips, new List<VideoBlock>(),
                tW, tH,
                overlayCanvas.ActualWidth, overlayCanvas.ActualHeight,
                timeline.TotalSeconds, tempVideo, AppSettings.TargetFitMode,
                null, prog1);

            status.Text = "Extracting audio...";
            var prog2 = new Progress<double>(v => Dispatcher.Invoke(() => progress.Value = 0.8 + v * 0.2));
            await _ff.ExtractAudioAsync(tempVideo, sfd.FileName, 0, timeline.TotalSeconds, fmt, prog2);

            status.Text = "Audio saved: " + sfd.FileName;
            progress.Value = 1;
            if (MessageBox.Show("Open output folder?", "Done", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                RevealInExplorer(sfd.FileName);
        }
        catch (Exception ex)
        {
            status.Text = "Audio export failed: " + ex.Message;
            MessageBox.Show(ex.Message, "Error");
        }
        finally
        {
            try { File.Delete(tempVideo); } catch { }
        }
    }

    private void MuteClip_Click(object sender, RoutedEventArgs e)
    {
        var c = CurrentClip();
        if (c == null) { status.Text = "Select a clip first."; return; }
        c.Volume = 0;
        SelectClip(c);
        status.Text = "Clip muted (volume = 0). Use Restore Audio to undo.";
    }

    private void UnmuteClip_Click(object sender, RoutedEventArgs e)
    {
        var c = CurrentClip();
        if (c == null) return;
        c.Volume = 1.0;
        SelectClip(c);
        status.Text = "Clip audio restored (volume = 100%).";
    }

    private async void RemoveAudioTrack_Click(object sender, RoutedEventArgs e)
    {
        var c = CurrentClip();
        if (c == null) { status.Text = "Select a clip first."; return; }
        if (MessageBox.Show(
            "Permanently strip the audio track from this clip? The clip's source file will be replaced with a silent version (the original file on disk is not modified).",
            "Remove Audio Track", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

        await ApplyDestructiveOpAsync(c, async (input, output, prog) =>
            await _ff.RemoveAudioTrackAsync(input, output, c.OriginalDuration, prog));
        status.Text = "Audio track removed.";
    }

    private async System.Threading.Tasks.Task ApplyDestructiveOpAsync(VideoClip clip, Func<string, string, IProgress<double>, System.Threading.Tasks.Task> op)
    {
        if (AppSettings.ConfirmDestructive)
        {
            var ok = MessageBox.Show(
                "This operation re-encodes the clip and replaces its source file. Continue?",
                "Confirm destructive operation",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (ok != MessageBoxResult.OK) return;
        }
        var tempOut = Path.Combine(Path.GetTempPath(), $"ve_{Guid.NewGuid()}.mp4");
        status.Text = "Processing clip...";
        progress.Value = 0;
        var prog = new Progress<double>(v => Dispatcher.Invoke(() => progress.Value = v));
        var previousSource = clip.SourceFile;
        bool succeeded = false;
        try
        {
            await op(previousSource, tempOut, prog);
            var (w, h, d) = await _ff.ProbeAsync(tempOut);
            // Release the previous file from the MediaElement before we swap and try to delete it.
            if (_playingClip == clip) { try { videoView.Stop(); videoView.Close(); } catch { } _playingClip = null; }
            clip.SourceFile = tempOut;
            clip.VideoWidth = w; clip.VideoHeight = h;
            clip.OriginalDuration = d;
            if (clip.OutPoint > d) clip.OutPoint = d;
            if (clip.InPoint >= d) clip.InPoint = 0;
            status.Text = "Clip updated.";
            LoadClipForPreview(clip, 0);
            SelectClip(clip);
            succeeded = true;
        }
        catch (Exception ex)
        {
            status.Text = "Op failed: " + ex.Message;
            MessageBox.Show(ex.Message, "Error");
        }
        finally
        {
            if (succeeded)
            {
                // Replace the prior temp output (if any) —” first destructive op uses the user's
                // original file (don't touch), subsequent ones supersede our own temp files.
                if (IsAppTempFile(previousSource))
                {
                    try { File.Delete(previousSource); } catch { }
                }
            }
            else
            {
                // Operation failed —” clean up the half-written temp if it exists.
                try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
            }
        }
    }

    // Use the ProcessStartInfo argument list so the path is quoted by the runtime —” avoids
    // breaking when the file name contains characters that interact with the shell parser.
    private static void RevealInExplorer(string filePath)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = true
            };
            psi.ArgumentList.Add("/select,");
            psi.ArgumentList.Add(filePath);
            System.Diagnostics.Process.Start(psi);
        }
        catch { }
    }

    private static bool IsAppTempFile(string path)
    {
        try
        {
            var tempDir = Path.GetFullPath(Path.GetTempPath());
            var full = Path.GetFullPath(path);
            var name = Path.GetFileName(full);
            return full.StartsWith(tempDir, StringComparison.OrdinalIgnoreCase)
                   && name.StartsWith("ve_", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
