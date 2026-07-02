using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NAudio.Wave;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;
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
    private bool _syncingExportFps;
    private System.Diagnostics.Process? _inlineRecorderProc;

    private WaveOutEvent? _ttsPreviewPlayer;
    private WaveFileReader? _ttsPreviewReader;
    private MemoryStream? _ttsPreviewStream;
    private bool _ttsVoicesLoaded;

    private sealed record TtsVoiceItem(string Id, string DisplayName, string Language)
    {
        public override string ToString() =>
            string.IsNullOrEmpty(Language) ? DisplayName : $"{DisplayName} ({Language})";
    }

    public sealed class MergeQueueItem : System.ComponentModel.INotifyPropertyChanged
    {
        public string FullPath { get; }
        public string DisplayName { get; }
        public string FolderHint { get; }
        private int _position;
        public int Position
        {
            get => _position;
            set { if (_position == value) return; _position = value; OnChanged(nameof(Position)); OnChanged(nameof(PositionLabel)); }
        }
        public string PositionLabel => $"{Position:00}.";
        public MergeQueueItem(string fullPath, int position)
        {
            FullPath = fullPath;
            DisplayName = Path.GetFileName(fullPath);
            var dir = Path.GetDirectoryName(fullPath) ?? "";
            FolderHint = dir.Length > 48 ? "…" + dir[^48..] : dir;
            _position = position;
        }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) =>
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }
    private readonly System.Collections.ObjectModel.ObservableCollection<MergeQueueItem> _mergeQueue = new();
    private static readonly string[] _mergeVideoExtensions = new[]
    {
        ".mp4", ".mov", ".mkv", ".avi", ".webm", ".wmv", ".flv", ".m4v", ".ts", ".mpg", ".mpeg"
    };

    private DispatcherTimer? _inlineRecorderPreviewTimer;
    private List<MonitorInfo.Display> _inlineRecorderMonitors = new();
    private VideoBlock? _inlineRecorderWebcamBlock;
    private ResizableBlock? _inlineRecorderWebcamControl;
    private const string DefaultWebcamDeviceName = "USB Video Device";
    private string _inlineRecorderWebcamDeviceName = DefaultWebcamDeviceName;
    private System.Diagnostics.Process? _inlineRecorderWebcamPreviewProc;
    private System.Threading.CancellationTokenSource? _inlineRecorderWebcamPreviewCts;
    private readonly Queue<string> _inlineRecorderLogTail = new();
    private string _inlineRecorderLastError = "";
    private readonly Queue<string> _inlineRecorderVisibleLog = new();
    private bool _inlineRecorderCameraOnly;
    private readonly CameraBackground _inlineCameraBackground = new();
    private readonly BackgroundRemovalService _inlineCameraBgService = new();
    private string? _inlineCameraRecordTempPath;
    private bool _inlineCameraBgReady;
    private int _inlineCameraAiPreviewInFlight;
    private long _inlineCameraLastAiPreviewTicks;
    private long _inlineCameraAiProcessed;
    private long _inlineCameraAiSkippedBusy;
    private long _inlineCameraAiSkippedThrottle;
    private long _inlineCameraLastStatsTicks;
    private DispatcherTimer? _inlineCameraDiagTimer;
    private long _inlineCameraJpegsReceived;
    private long _inlineCameraLastJpegTicks;
    private long _inlineCameraLastAiDoneTicks;
    private long _inlineCameraLastInferenceMs;
    private string _inlineCameraPreviewFfmpegError = "";
    private long _inlineCameraDiagWindowStartTicks;
    private long _inlineCameraDiagWindowJpegsStart;
    private long _inlineCameraDiagWindowAiStart;
    private int _inlineCameraPreviewRetryCount;
    private bool _inlineCameraPreviewRetryPending;
    private bool _suppressBgComboChange;
    private int _inlineCameraStartGen;
    private string? _screenCamScreenTempPath;
    private string? _screenCamRawTempPath;
    private string? _screenCamFinalOutputPath;
    private (int X, int Y, int W, int H, int OutW, int OutH)? _screenCamPipRect;
    private int _screenCamAiPreviewInFlight;
    private long _screenCamLastAiPreviewTicks;
    private enum InlineTabKind { Recording, Camera, Block }
    private sealed class InlineRecorderTab
    {
        public InlineTabKind Kind;
        public string Title = "";
        public VideoBlock? Block;
        public System.Windows.Controls.Primitives.ToggleButton? Button;
    }
    private readonly List<InlineRecorderTab> _inlineRecorderTabList = new();
    private InlineRecorderTab? _activeInlineTab;

    public MainWindow()
    {
        InitializeComponent();
        mergeQueueList.ItemsSource = _mergeQueue;
        InitUndoRedo();
        _inlineCameraBgService.Log += msg =>
        {
            try { AppendInlineRecorderLog("AI: " + msg); } catch { }
        };

        // Apply Language preference (RTL for Hebrew)
        ApplyLanguage();
        // Apply default master volume from settings
        _masterVolume = AppSettings.DefaultMasterVolume / 100.0;
        volumeSlider.Value = AppSettings.DefaultMasterVolume;
        videoView.Volume = _masterVolume;
        videoView.ScrubbingEnabled = AppSettings.ScrubbingQuality != "smooth";
        timeline.FFmpeg = _ff;
        InitFormatControls();
        ConfigureUsabilityHints();
        SetSafeAreasEnabled(AppSettings.PreviewSafeAreas, save: false);
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
                UpdateExportFpsControls();
                UpdatePreviewAspect();
                SetSafeAreasEnabled(AppSettings.PreviewSafeAreas, save: false);
            });
        };

        _tick.Tick += Tick_OnTick;
        _tick.Start();

        PreviewKeyDown += MainWindow_PreviewKeyDown;
        PreviewKeyUp += MainWindow_PreviewKeyUp;

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
        timeline.TextOverlaySelected += o => RefreshTextOverlayPreviewSelectionVisuals();
        timeline.TextOverlayContextAction += OnTextOverlayContext;
        timeline.TextOverlayChanged += o => _textOverlayDirty.Add(o);
        timeline.TextOverlaysChanged += () => UpdateStats();
        timeline.ClipsChanged += () =>
        {
            if (timeline.Clips.Count > 0 && _playingClip == null) LoadClipForPreview(timeline.Clips[0], 0);
            UpdateStats();
            UpdateEmptyStartPanel();
            UpdateTimeDisplays();
        };
        timeline.BlocksChanged += () => UpdateStats();
        timeline.SelectionChanged += OnTimelineSelectionChanged;

        overlayCanvas.SizeChanged += (_, _) =>
        {
            RepositionOverlay();
            UpdateMeasurementRulerVisual();
            UpdateSafeAreasVisual();
            UpdatePreviewOverlayGroupBox();
            // Scale of every text-overlay preview control depends on canvas size - mark all dirty
            // so the next tick re-styles + re-places them. Cheap because it only happens on resize.
            foreach (var ov in timeline.TextOverlays) _textOverlayDirty.Add(ov);
        };
        overlayCanvas.MouseLeftButtonDown += OverlayCanvas_BackgroundClick;
        videoContainerOuter.MouseLeftButtonDown += PreviewViewPan_MouseDown;
        videoContainerOuter.MouseMove += PreviewViewPan_MouseMove;
        videoContainerOuter.MouseLeftButtonUp += PreviewViewPan_MouseUp;
        videoContainerOuter.MouseWheel += PreviewViewPan_MouseWheel;
        videoContainerOuter.MouseLeave += PreviewCanvas_MouseLeave;
        WirePreviewCanvasTransformGestures();
    }

    // ===== Direct manipulation of the canvas transform =====
    //
    // - Click and drag inside the preview to pan the selected clip on the canvas.
    // - Scroll the mouse wheel inside the preview to zoom in/out (Ctrl+Scroll = finer step).
    // - Hold Space to pan/zoom the preview view itself, matching OBS fixed-preview scaling.
    // - Double-click resets the clip to "fit the canvas exactly" (scale=1, offset=0,0).
    //
    // Behaviour is no-op when no video clip is selected.
    private bool _canvasDragActive;
    private bool _canvasDragMoved;
    private UIElement? _canvasDragCaptureElement;
    private VideoClip? _canvasDragClip;
    private bool _canvasDragCtrlClickCandidate;
    private bool _canvasDragWasSelectedAtMouseDown;
    private Point _canvasDragStartMouse;
    private double _canvasDragStartOffX;
    private double _canvasDragStartOffY;
    private bool _previewMarqueeActive;
    private bool _previewMarqueeMoved;
    private Point _previewMarqueeStart;
    private bool _previewOverlayDragActive;
    private bool _previewOverlayDragMoved;
    private Point _previewOverlayDragLast;
    private Point _previewOverlayDragStart;
    private object? _previewOverlayClickThroughItem;
    private bool _previewOverlayClickThroughCandidate;
    private bool _previewGroupResizeActive;
    private string _previewGroupResizeTag = "";
    private Point _previewGroupResizeStartMouse;
    private Rect _previewGroupResizeStartBounds;
    private Dictionary<VideoBlock, Rect>? _previewGroupResizeStartBlocks;
    private Dictionary<TextOverlay, (double x, double y, int fontSize, int pad)>? _previewGroupResizeStartTexts;
    private const double CanvasSnapDistance = 10.0;
    private double? _canvasSnapGuideX;
    private double? _canvasSnapGuideY;
    private bool _measurementRulerEnabled;
    private bool _safeAreasEnabled;
    private bool _previewViewPanMode;
    private bool _previewViewPanActive;
    private bool _previewViewPanMoved;
    private Point _previewViewPanStartMouse;
    private Point _previewViewPanStartOffset;
    private Point _previewViewPanOffset;
    private int _previewViewZoomLevel;
    private double _previewViewZoom = 1.0;
    private const int PreviewViewMaxZoomLevel = 32;
    private const double PreviewViewMaxZoomAmount = 8.0;
    private string _measurementRulerDragMode = "";
    private Point _measurementRulerDragStartMouse;
    private Point _measurementRulerDragStartA;
    private Point _measurementRulerDragStartB;
    private Point _measurementRulerA = new(80, 80);
    private Point _measurementRulerB = new(320, 80);
    private readonly List<MeasurementRulerItem> _measurementRulers = new();
    private readonly List<UIElement> _measurementRulerOverlays = new();
    private int _activeMeasurementRulerIndex = -1;
    private CanvasTransformSnapshot? _copiedCanvasTransform;

    private sealed class MeasurementRulerItem
    {
        public string Name { get; set; } = "Ruler";
        public Point A { get; set; }
        public Point B { get; set; }
        public string Unit { get; set; } = "px";
    }

    private sealed class CanvasTransformSnapshot
    {
        public double Scale;
        public double ScaleX;
        public double ScaleY;
        public double OffsetX;
        public double OffsetY;
        public double CropLeft;
        public double CropTop;
        public double CropRight;
        public double CropBottom;
        public double RotateDegrees;
        public bool FlipH;
        public bool FlipV;

        public static CanvasTransformSnapshot From(VideoClip c) => new()
        {
            Scale = c.CanvasScale,
            ScaleX = c.CanvasScaleX,
            ScaleY = c.CanvasScaleY,
            OffsetX = c.CanvasOffsetX,
            OffsetY = c.CanvasOffsetY,
            CropLeft = c.CanvasCropLeft,
            CropTop = c.CanvasCropTop,
            CropRight = c.CanvasCropRight,
            CropBottom = c.CanvasCropBottom,
            RotateDegrees = c.RotateDegrees,
            FlipH = c.FlipH,
            FlipV = c.FlipV,
        };

        public void ApplyTo(VideoClip c)
        {
            c.CanvasScale = Scale;
            c.CanvasScaleX = ScaleX;
            c.CanvasScaleY = ScaleY;
            c.CanvasOffsetX = OffsetX;
            c.CanvasOffsetY = OffsetY;
            c.CanvasCropLeft = CropLeft;
            c.CanvasCropTop = CropTop;
            c.CanvasCropRight = CropRight;
            c.CanvasCropBottom = CropBottom;
            c.RotateDegrees = RotateDegrees;
            c.FlipH = FlipH;
            c.FlipV = FlipV;
        }
    }

    private void WirePreviewCanvasTransformGestures()
    {
        if (videoView == null) return;
        videoView.MouseLeftButtonDown += PreviewCanvas_MouseDown;
        videoView.MouseMove += PreviewCanvas_MouseMove;
        videoView.MouseLeftButtonUp += PreviewCanvas_MouseUp;
        videoView.LostMouseCapture += PreviewCanvas_LostMouseCapture;
        videoView.MouseWheel += PreviewCanvas_MouseWheel;
        videoView.MouseRightButtonDown += PreviewCanvas_MouseRight;
        videoView.Cursor = Cursors.Arrow;

        if (overlayCanvas != null)
        {
            overlayCanvas.MouseLeftButtonDown += PreviewCanvas_MouseDown;
            overlayCanvas.MouseMove += PreviewCanvas_MouseMove;
            overlayCanvas.MouseLeftButtonUp += PreviewCanvas_MouseUp;
            overlayCanvas.LostMouseCapture += PreviewCanvas_LostMouseCapture;
            overlayCanvas.MouseWheel += PreviewCanvas_MouseWheel;
            overlayCanvas.MouseRightButtonDown += PreviewCanvas_MouseRight;
            overlayCanvas.Cursor = Cursors.Arrow;
        }
    }

    private void ToggleRuler_Click(object sender, RoutedEventArgs e)
    {
        _measurementRulerEnabled = !_measurementRulerEnabled;
        if (_measurementRulerEnabled)
        {
            if (measurementRulerLayer.ActualWidth <= 1 || measurementRulerLayer.ActualHeight <= 1)
            {
                measurementRulerLayer.Width = Math.Max(1, videoStack.ActualWidth);
                measurementRulerLayer.Height = Math.Max(1, videoStack.ActualHeight);
            }

            if (_measurementRulers.Count == 0)
                AddMeasurementRuler();
            else
                SelectMeasurementRuler(Math.Clamp(_activeMeasurementRulerIndex, 0, _measurementRulers.Count - 1));

            measurementRulerLayer.Visibility = Visibility.Visible;
            toggleRulerBtn.Background = (Brush)FindResource("AccentSoft");
            toggleRulerBtn.BorderBrush = (Brush)FindResource("Accent");
            UpdateMeasurementRulerVisual();
            ShowInspectorTab("ruler");
            status.Text = "Measurement ruler on - drag the line or its endpoints.";
        }
        else
        {
            measurementRulerLayer.Visibility = Visibility.Collapsed;
            toggleRulerBtn.ClearValue(BackgroundProperty);
            toggleRulerBtn.ClearValue(BorderBrushProperty);
            status.Text = "Measurement ruler off.";
        }
    }

    private void TabRuler_Click(object sender, RoutedEventArgs e) => ShowInspectorTab("ruler");

    private void AddRuler_Click(object sender, RoutedEventArgs e)
    {
        if (!_measurementRulerEnabled)
            SetMeasurementRulerEnabled(true);
        AddMeasurementRuler();
        ShowInspectorTab("ruler");
    }

    private void DeleteRuler_Click(object sender, RoutedEventArgs e)
    {
        if (_activeMeasurementRulerIndex < 0 || _activeMeasurementRulerIndex >= _measurementRulers.Count)
            return;

        _measurementRulers.RemoveAt(_activeMeasurementRulerIndex);
        if (_measurementRulers.Count == 0)
        {
            _activeMeasurementRulerIndex = -1;
            _measurementRulerEnabled = false;
            measurementRulerLayer.Visibility = Visibility.Collapsed;
            toggleRulerBtn.ClearValue(BackgroundProperty);
            toggleRulerBtn.ClearValue(BorderBrushProperty);
        }
        else
        {
            SelectMeasurementRuler(Math.Min(_activeMeasurementRulerIndex, _measurementRulers.Count - 1));
        }

        RefreshRulerInspector();
        UpdateMeasurementRulerVisual();
        status.Text = "Measurement ruler deleted.";
    }

    private void ResetRuler_Click(object sender, RoutedEventArgs e)
    {
        var ruler = ActiveMeasurementRuler();
        if (ruler == null) return;
        var (a, b) = DefaultMeasurementRulerPoints();
        ruler.A = a;
        ruler.B = b;
        SyncActiveMeasurementRulerFields();
        RefreshRulerInspector();
        UpdateMeasurementRulerVisual();
    }

    private void RulerList_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || rulerListBox.SelectedIndex < 0) return;
        SelectMeasurementRuler(rulerListBox.SelectedIndex);
    }

    private void RulerUnit_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        var ruler = ActiveMeasurementRuler();
        if (ruler == null || rulerUnitBox.SelectedIndex < 0) return;
        ruler.Unit = rulerUnitBox.SelectedIndex switch
        {
            1 => "pct",
            2 => "cm",
            3 => "in",
            4 => "pt",
            _ => "px"
        };
        RefreshRulerInspector();
        UpdateMeasurementRulerVisual();
    }

    private void SetMeasurementRulerEnabled(bool enabled)
    {
        if (_measurementRulerEnabled == enabled) return;
        _measurementRulerEnabled = !enabled;
        ToggleRuler_Click(this, new RoutedEventArgs());
    }

    private void ToggleSafeAreas_Click(object sender, RoutedEventArgs e)
    {
        SetSafeAreasEnabled(!_safeAreasEnabled, save: true);
        status.Text = _safeAreasEnabled ? "Safe areas on." : "Safe areas off.";
    }

    private void SetSafeAreasEnabled(bool enabled, bool save)
    {
        _safeAreasEnabled = enabled;
        if (save)
        {
            AppSettings.PreviewSafeAreas = enabled;
            AppSettings.Save();
        }

        if (safeAreasLayer != null)
            safeAreasLayer.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

        if (toggleSafeAreasBtn != null)
        {
            if (enabled)
            {
                toggleSafeAreasBtn.Background = (Brush)FindResource("AccentSoft");
                toggleSafeAreasBtn.BorderBrush = (Brush)FindResource("Accent");
            }
            else
            {
                toggleSafeAreasBtn.ClearValue(BackgroundProperty);
                toggleSafeAreasBtn.ClearValue(BorderBrushProperty);
            }
        }

        UpdateSafeAreasVisual();
    }

    private void UpdateSafeAreasVisual()
    {
        if (!_safeAreasEnabled || safeAreasLayer == null || videoStack == null)
            return;

        double w = Math.Max(1, videoStack.ActualWidth);
        double h = Math.Max(1, videoStack.ActualHeight);
        safeAreasLayer.Width = w;
        safeAreasLayer.Height = h;
        safeAreasLayer.Visibility = Visibility.Visible;

        // OBS-style defaults: action safe = 90%, graphics/title safe = 80%.
        SetSafeAreaRect(safeAreaActionRect, w * 0.05, h * 0.05, w * 0.90, h * 0.90);
        SetSafeAreaRect(safeAreaGraphicsRect, w * 0.10, h * 0.10, w * 0.80, h * 0.80);

        double fourByThreeW = Math.Min(w, h * 4.0 / 3.0);
        double fourByThreeH = Math.Min(h, w * 3.0 / 4.0);
        SetSafeAreaRect(safeAreaFourByThreeRect, (w - fourByThreeW) / 2, (h - fourByThreeH) / 2, fourByThreeW, fourByThreeH);

        safeAreaCenterV.X1 = safeAreaCenterV.X2 = w / 2;
        safeAreaCenterV.Y1 = 0;
        safeAreaCenterV.Y2 = h;
        safeAreaCenterH.X1 = 0;
        safeAreaCenterH.X2 = w;
        safeAreaCenterH.Y1 = safeAreaCenterH.Y2 = h / 2;
    }

    private static void SetSafeAreaRect(System.Windows.Shapes.Rectangle rect, double x, double y, double w, double h)
    {
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        rect.Width = Math.Max(1, w);
        rect.Height = Math.Max(1, h);
    }

    private void ResetMeasurementRuler()
    {
        var (a, b) = DefaultMeasurementRulerPoints();
        _measurementRulerA = a;
        _measurementRulerB = b;
    }

    private (Point a, Point b) DefaultMeasurementRulerPoints()
    {
        var w = Math.Max(1, videoStack.ActualWidth);
        var h = Math.Max(1, videoStack.ActualHeight);
        return (new Point(w * 0.25, h * 0.5), new Point(w * 0.75, h * 0.5));
    }

    private MeasurementRulerItem? ActiveMeasurementRuler()
    {
        if (_activeMeasurementRulerIndex < 0 || _activeMeasurementRulerIndex >= _measurementRulers.Count)
            return null;
        return _measurementRulers[_activeMeasurementRulerIndex];
    }

    private void AddMeasurementRuler()
    {
        var (a, b) = DefaultMeasurementRulerPoints();
        var ruler = new MeasurementRulerItem
        {
            Name = $"Ruler {_measurementRulers.Count + 1}",
            A = a,
            B = b,
            Unit = ActiveMeasurementRuler()?.Unit ?? "px"
        };
        _measurementRulers.Add(ruler);
        SelectMeasurementRuler(_measurementRulers.Count - 1);
        status.Text = $"{ruler.Name} added.";
    }

    private void SelectMeasurementRuler(int index)
    {
        if (index < 0 || index >= _measurementRulers.Count) return;
        _activeMeasurementRulerIndex = index;
        SyncActiveMeasurementRulerFields();
        RefreshRulerInspector();
        UpdateMeasurementRulerVisual();
    }

    private void SyncActiveMeasurementRulerFields()
    {
        var ruler = ActiveMeasurementRuler();
        if (ruler == null) return;
        _measurementRulerA = ruler.A;
        _measurementRulerB = ruler.B;
    }

    private void SaveActiveMeasurementRulerFields()
    {
        var ruler = ActiveMeasurementRuler();
        if (ruler == null) return;
        ruler.A = _measurementRulerA;
        ruler.B = _measurementRulerB;
    }

    private void RefreshRulerInspector()
    {
        if (rulerListBox == null || rulerUnitBox == null) return;
        _suppress = true;
        try
        {
            rulerListBox.Items.Clear();
            for (int i = 0; i < _measurementRulers.Count; i++)
            {
                var r = _measurementRulers[i];
                rulerListBox.Items.Add($"{i + 1}. {FormatMeasurementRuler(r)}");
            }
            rulerListBox.SelectedIndex = _activeMeasurementRulerIndex;
            rulerUnitBox.SelectedIndex = ActiveMeasurementRuler()?.Unit switch
            {
                "pct" => 1,
                "cm" => 2,
                "in" => 3,
                "pt" => 4,
                _ => 0
            };
            rulerDetailsText.Text = ActiveMeasurementRuler() is { } active
                ? $"{active.Name}: {FormatMeasurementRuler(active, includeAngle: true)}"
                : "No ruler selected";
        }
        finally
        {
            _suppress = false;
        }
    }

    private void MeasurementRuler_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement el) return;
        _measurementRulerDragMode = el.Tag?.ToString() ?? "";
        _measurementRulerDragStartMouse = e.GetPosition(measurementRulerLayer);
        _measurementRulerDragStartA = _measurementRulerA;
        _measurementRulerDragStartB = _measurementRulerB;
        el.CaptureMouse();
        e.Handled = true;
    }

    private void MeasurementRuler_MouseMove(object sender, MouseEventArgs e)
    {
        if (string.IsNullOrEmpty(_measurementRulerDragMode) ||
            sender is not FrameworkElement el ||
            !el.IsMouseCaptured)
            return;

        var p = e.GetPosition(measurementRulerLayer);
        var delta = p - _measurementRulerDragStartMouse;
        if (_measurementRulerDragMode == "start")
            _measurementRulerA = ClampMeasurementPoint(_measurementRulerDragStartA + delta);
        else if (_measurementRulerDragMode == "end")
            _measurementRulerB = ClampMeasurementPoint(_measurementRulerDragStartB + delta);
        else
        {
            _measurementRulerA = ClampMeasurementPoint(_measurementRulerDragStartA + delta);
            _measurementRulerB = ClampMeasurementPoint(_measurementRulerDragStartB + delta);
        }

        SaveActiveMeasurementRulerFields();
        UpdateMeasurementRulerVisual();
        RefreshRulerInspector();
        e.Handled = true;
    }

    private void MeasurementRuler_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el && el.IsMouseCaptured)
            el.ReleaseMouseCapture();
        _measurementRulerDragMode = "";
        e.Handled = true;
    }

    private Point ClampMeasurementPoint(Point p)
    {
        var w = Math.Max(1, measurementRulerLayer.ActualWidth);
        var h = Math.Max(1, measurementRulerLayer.ActualHeight);
        return new Point(Math.Clamp(p.X, 0, w), Math.Clamp(p.Y, 0, h));
    }

    private void UpdateMeasurementRulerVisual()
    {
        if (!_measurementRulerEnabled || measurementRulerLayer == null) return;
        measurementRulerLayer.Width = Math.Max(1, videoStack.ActualWidth);
        measurementRulerLayer.Height = Math.Max(1, videoStack.ActualHeight);
        RenderInactiveMeasurementRulers();

        var active = ActiveMeasurementRuler();
        bool hasActive = active != null;
        measurementRulerLine.Visibility = hasActive ? Visibility.Visible : Visibility.Collapsed;
        measurementRulerShadow.Visibility = hasActive ? Visibility.Visible : Visibility.Collapsed;
        measurementRulerStartHandle.Visibility = hasActive ? Visibility.Visible : Visibility.Collapsed;
        measurementRulerEndHandle.Visibility = hasActive ? Visibility.Visible : Visibility.Collapsed;
        measurementRulerMoveHandle.Visibility = hasActive ? Visibility.Visible : Visibility.Collapsed;
        measurementRulerLabel.Visibility = hasActive ? Visibility.Visible : Visibility.Collapsed;
        if (!hasActive) return;

        measurementRulerLine.X1 = measurementRulerShadow.X1 = _measurementRulerA.X;
        measurementRulerLine.Y1 = measurementRulerShadow.Y1 = _measurementRulerA.Y;
        measurementRulerLine.X2 = measurementRulerShadow.X2 = _measurementRulerB.X;
        measurementRulerLine.Y2 = measurementRulerShadow.Y2 = _measurementRulerB.Y;

        PlaceRulerHandle(measurementRulerStartHandle, _measurementRulerA);
        PlaceRulerHandle(measurementRulerEndHandle, _measurementRulerB);
        PlaceRulerHandle(measurementRulerMoveHandle, new Point(
            (_measurementRulerA.X + _measurementRulerB.X) / 2,
            (_measurementRulerA.Y + _measurementRulerB.Y) / 2));

        var dx = _measurementRulerB.X - _measurementRulerA.X;
        var dy = _measurementRulerB.Y - _measurementRulerA.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        var angle = Math.Atan2(dy, dx) * 180 / Math.PI;
        measurementRulerText.Text = $"{len:0}px · {angle:0.#}°";

        measurementRulerText.Text = FormatMeasurementRuler(active!, includeAngle: true);
        measurementRulerLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var labelW = Math.Max(130, measurementRulerLabel.DesiredSize.Width);
        var labelX = Math.Min(Math.Max((measurementRulerLine.X1 + measurementRulerLine.X2) / 2 + 14, 4), Math.Max(4, measurementRulerLayer.Width - labelW - 4));
        var labelY = Math.Min(Math.Max((measurementRulerLine.Y1 + measurementRulerLine.Y2) / 2 - 34, 4), Math.Max(4, measurementRulerLayer.Height - 28));
        Canvas.SetLeft(measurementRulerLabel, labelX);
        Canvas.SetTop(measurementRulerLabel, labelY);
    }

    private static void PlaceRulerHandle(FrameworkElement handle, Point p)
    {
        Canvas.SetLeft(handle, p.X - handle.Width / 2);
        Canvas.SetTop(handle, p.Y - handle.Height / 2);
    }

    private void RenderInactiveMeasurementRulers()
    {
        foreach (var el in _measurementRulerOverlays)
            measurementRulerLayer.Children.Remove(el);
        _measurementRulerOverlays.Clear();

        for (int i = 0; i < _measurementRulers.Count; i++)
        {
            if (i == _activeMeasurementRulerIndex) continue;
            var r = _measurementRulers[i];
            AddInactiveRulerLine(r.A, r.B, Color.FromArgb(0x66, 0x00, 0x00, 0x00), 5);
            AddInactiveRulerLine(r.A, r.B, Color.FromArgb(0x99, 0xFF, 0xD4, 0x3B), 2);

            var label = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x0A, 0x0C, 0x12)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = FormatMeasurementRuler(r),
                    Foreground = Brushes.White,
                    FontSize = 10.5,
                    FontFamily = new FontFamily("Consolas")
                }
            };
            measurementRulerLayer.Children.Add(label);
            _measurementRulerOverlays.Add(label);
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, Math.Clamp((r.A.X + r.B.X) / 2 + 10, 2, Math.Max(2, measurementRulerLayer.Width - label.DesiredSize.Width - 2)));
            Canvas.SetTop(label, Math.Clamp((r.A.Y + r.B.Y) / 2 - 28, 2, Math.Max(2, measurementRulerLayer.Height - label.DesiredSize.Height - 2)));
        }
    }

    private void AddInactiveRulerLine(Point a, Point b, Color color, double thickness)
    {
        var line = new System.Windows.Shapes.Line
        {
            X1 = a.X,
            Y1 = a.Y,
            X2 = b.X,
            Y2 = b.Y,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false
        };
        measurementRulerLayer.Children.Add(line);
        _measurementRulerOverlays.Add(line);
    }

    private string FormatMeasurementRuler(MeasurementRulerItem ruler, bool includeAngle = false)
    {
        var (dx, dy, len) = ProjectRulerDelta(ruler);
        string value = ruler.Unit switch
        {
            "pct" => $"{Math.Abs(dx) / Math.Max(1, ProjectCanvasSize().width) * 100:0.#}% W, {Math.Abs(dy) / Math.Max(1, ProjectCanvasSize().height) * 100:0.#}% H",
            "cm" => $"{len / 96.0 * 2.54:0.##} cm",
            "in" => $"{len / 96.0:0.###} in",
            "pt" => $"{len / 96.0 * 72.0:0.#} pt",
            _ => $"{len:0.#} px"
        };

        if (!includeAngle)
            return value;

        var angle = Math.Atan2(ruler.B.Y - ruler.A.Y, ruler.B.X - ruler.A.X) * 180 / Math.PI;
        return $"{value} - {angle:0.#} deg";
    }

    private (double dx, double dy, double len) ProjectRulerDelta(MeasurementRulerItem ruler)
    {
        var (width, height) = ProjectCanvasSize();
        double layerW = Math.Max(1, measurementRulerLayer?.ActualWidth ?? videoStack.ActualWidth);
        double layerH = Math.Max(1, measurementRulerLayer?.ActualHeight ?? videoStack.ActualHeight);
        double dx = (ruler.B.X - ruler.A.X) * width / layerW;
        double dy = (ruler.B.Y - ruler.A.Y) * height / layerH;
        return (dx, dy, Math.Sqrt(dx * dx + dy * dy));
    }

    private (double width, double height) ProjectCanvasSize()
    {
        var first = timeline.Clips.FirstOrDefault(c => !c.IsAudioOnly);
        var (tw, th) = VideoEditor.Services.ProjectFormats.Resolve(AppSettings.TargetFormatPreset, first);
        return (Math.Max(1, tw), Math.Max(1, th));
    }

    private static (double left, double top, double right, double bottom) NormalizedCrop(VideoClip c)
    {
        double maxW = Math.Max(0, c.VideoWidth - 2);
        double maxH = Math.Max(0, c.VideoHeight - 2);
        double l = Math.Clamp(c.CanvasCropLeft, 0, maxW);
        double r = Math.Clamp(c.CanvasCropRight, 0, Math.Max(0, maxW - l));
        double t = Math.Clamp(c.CanvasCropTop, 0, maxH);
        double b = Math.Clamp(c.CanvasCropBottom, 0, Math.Max(0, maxH - t));
        return (l, t, r, b);
    }

    private static void ApplyClampedCrop(VideoClip c, double left, double top, double right, double bottom)
    {
        double maxW = Math.Max(0, c.VideoWidth - 2);
        double maxH = Math.Max(0, c.VideoHeight - 2);
        left = Math.Clamp(left, 0, maxW);
        right = Math.Clamp(right, 0, Math.Max(0, maxW - left));
        top = Math.Clamp(top, 0, maxH);
        bottom = Math.Clamp(bottom, 0, Math.Max(0, maxH - top));
        c.CanvasCropLeft = left;
        c.CanvasCropTop = top;
        c.CanvasCropRight = right;
        c.CanvasCropBottom = bottom;
    }

    private Rect GetVisibleCanvasBounds(VideoClip c, double offsetX, double offsetY, double scale, double? scaleXOverride = null, double? scaleYOverride = null)
    {
        if (videoStack.ActualWidth < 1 || videoStack.ActualHeight < 1 || c.VideoWidth <= 0 || c.VideoHeight <= 0)
            return Rect.Empty;

        double canvasW = videoStack.ActualWidth;
        double canvasH = videoStack.ActualHeight;
        double baseScale = Math.Min(canvasW / c.VideoWidth, canvasH / c.VideoHeight);
        double scaleX = scaleXOverride ?? c.CanvasScaleX;
        double scaleY = scaleYOverride ?? c.CanvasScaleY;
        var crop = NormalizedCrop(c);
        double visibleW = Math.Max(1, (c.VideoWidth - crop.left - crop.right) * baseScale * scale * scaleX);
        double visibleH = Math.Max(1, (c.VideoHeight - crop.top - crop.bottom) * baseScale * scale * scaleY);
        double cropShiftX = ((crop.left - crop.right) * baseScale * scale * scaleX) / 2;
        double cropShiftY = ((crop.top - crop.bottom) * baseScale * scale * scaleY) / 2;
        double centerX = canvasW / 2 + offsetX * canvasW + cropShiftX;
        double centerY = canvasH / 2 + offsetY * canvasH + cropShiftY;
        return RotatedBounds(centerX, centerY, visibleW, visibleH, c.RotateDegrees);
    }

    private static Rect RotatedBounds(double centerX, double centerY, double width, double height, double degrees)
    {
        double radians = degrees * Math.PI / 180.0;
        double cos = Math.Abs(Math.Cos(radians));
        double sin = Math.Abs(Math.Sin(radians));
        double rotatedW = width * cos + height * sin;
        double rotatedH = width * sin + height * cos;
        return new Rect(centerX - rotatedW / 2, centerY - rotatedH / 2, rotatedW, rotatedH);
    }

    private Vector GetCanvasSnapOffset(VideoClip c, double offsetX, double offsetY, double scale, double? scaleXOverride = null, double? scaleYOverride = null)
    {
        _canvasSnapGuideX = null;
        _canvasSnapGuideY = null;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            return new Vector();

        var bounds = GetVisibleCanvasBounds(c, offsetX, offsetY, scale, scaleXOverride, scaleYOverride);
        if (bounds.IsEmpty) return new Vector();

        double canvasW = Math.Max(1, videoStack.ActualWidth);
        double canvasH = Math.Max(1, videoStack.ActualHeight);
        double dx = SnapAxis(bounds.Left, bounds.Right, canvasW, out var guideX);
        double dy = SnapAxis(bounds.Top, bounds.Bottom, canvasH, out var guideY);

        var sourceSnap = GetOverlaySourceSnapOffset(bounds, dx, dy, out var sourceGuideX, out var sourceGuideY);
        if (Math.Abs(sourceSnap.X) > 0.001)
        {
            dx = sourceSnap.X;
            guideX = sourceGuideX;
        }
        if (Math.Abs(sourceSnap.Y) > 0.001)
        {
            dy = sourceSnap.Y;
            guideY = sourceGuideY;
        }

        _canvasSnapGuideX = Math.Abs(dx) > 0.001 ? guideX : null;
        _canvasSnapGuideY = Math.Abs(dy) > 0.001 ? guideY : null;
        return new Vector(dx, dy);
    }

    private static double SnapAxis(double start, double end, double canvasSize, out double guide)
    {
        double offset = 0;
        guide = double.NaN;
        if (Math.Abs(start) < CanvasSnapDistance)
        {
            offset = -start;
            guide = 0;
        }
        if (Math.Abs(offset) < 0.001 && Math.Abs(canvasSize - end) < CanvasSnapDistance)
        {
            offset = canvasSize - end;
            guide = canvasSize;
        }

        double size = end - start;
        double center = start + size / 2;
        if (Math.Abs(offset) < 0.001 &&
            Math.Abs(canvasSize - size) > CanvasSnapDistance &&
            Math.Abs(canvasSize / 2 - center) < CanvasSnapDistance)
        {
            offset = canvasSize / 2 - center;
            guide = canvasSize / 2;
        }
        return offset;
    }

    private Vector GetOverlaySourceSnapOffset(Rect movingBounds, double screenDx, double screenDy, out double guideX, out double guideY)
    {
        guideX = double.NaN;
        guideY = double.NaN;
        double bestDx = 0;
        double bestDy = 0;
        double bestXDistance = Math.Abs(screenDx) > 0.001 ? Math.Abs(screenDx) : CanvasSnapDistance;
        double bestYDistance = Math.Abs(screenDy) > 0.001 ? Math.Abs(screenDy) : CanvasSnapDistance;

        foreach (var target in GetVisibleOverlaySnapBounds())
        {
            TrySnapToTargetAxis(
                new[] { movingBounds.Left, movingBounds.Left + movingBounds.Width / 2, movingBounds.Right },
                new[] { target.Left, target.Left + target.Width / 2, target.Right },
                ref bestDx,
                ref guideX,
                ref bestXDistance);
            TrySnapToTargetAxis(
                new[] { movingBounds.Top, movingBounds.Top + movingBounds.Height / 2, movingBounds.Bottom },
                new[] { target.Top, target.Top + target.Height / 2, target.Bottom },
                ref bestDy,
                ref guideY,
                ref bestYDistance);
        }

        return new Vector(bestDx, bestDy);
    }

    private IEnumerable<Rect> GetVisibleOverlaySnapBounds(bool excludeCurrentSelection = false)
    {
        if (overlayCanvas == null) yield break;

        foreach (var kv in _blockControls)
        {
            var block = kv.Key;
            var ctl = kv.Value;
            if (excludeCurrentSelection && timeline.SelectedBlocks.Contains(block)) continue;
            if (ctl.Visibility != Visibility.Visible || !ReferenceEquals(ctl.Parent, overlayCanvas)) continue;
            if (block.Width <= 1 || block.Height <= 1) continue;
            yield return new Rect(block.X, block.Y, block.Width, block.Height);
        }

        foreach (var kv in _textOverlayPreviewControls)
        {
            if (excludeCurrentSelection && timeline.SelectedTextOverlays.Contains(kv.Key)) continue;
            var ctl = kv.Value;
            if (ctl.Visibility != Visibility.Visible || !ReferenceEquals(ctl.Parent, overlayCanvas)) continue;
            double x = Canvas.GetLeft(ctl);
            double y = Canvas.GetTop(ctl);
            double w = ctl.ActualWidth;
            double h = ctl.ActualHeight;
            if (double.IsNaN(x) || double.IsNaN(y) || w <= 1 || h <= 1) continue;
            yield return new Rect(x, y, w, h);
        }
    }

    private static void TrySnapToTargetAxis(double[] movingLines, double[] targetLines, ref double bestOffset, ref double guide, ref double bestDistance)
    {
        foreach (double moving in movingLines)
        {
            foreach (double target in targetLines)
            {
                double offset = target - moving;
                double distance = Math.Abs(offset);
                if (distance < bestDistance && distance < CanvasSnapDistance)
                {
                    bestDistance = distance;
                    bestOffset = offset;
                    guide = target;
                }
            }
        }
    }

    private void UpdateCanvasSnapGuides(VideoClip c, double offsetX, double offsetY, double scale, Vector snap, double? scaleXOverride = null, double? scaleYOverride = null)
    {
        if (canvasSnapGuideLayer == null) return;
        if (Math.Abs(snap.X) < 0.001 && Math.Abs(snap.Y) < 0.001)
        {
            HideCanvasSnapGuides();
            return;
        }

        double canvasW = Math.Max(1, videoStack.ActualWidth);
        double canvasH = Math.Max(1, videoStack.ActualHeight);
        canvasSnapGuideLayer.Width = canvasW;
        canvasSnapGuideLayer.Height = canvasH;
        canvasSnapGuideLayer.Visibility = Visibility.Visible;

        var bounds = GetVisibleCanvasBounds(c, offsetX, offsetY, scale, scaleXOverride, scaleYOverride);
        if (Math.Abs(snap.X) >= 0.001)
        {
            double x = _canvasSnapGuideX ?? SnapGuidePosition(bounds.Left, bounds.Right, canvasW);
            canvasSnapGuideV.X1 = canvasSnapGuideV.X2 = x;
            canvasSnapGuideV.Y1 = 0;
            canvasSnapGuideV.Y2 = canvasH;
            canvasSnapGuideV.Visibility = Visibility.Visible;
        }
        else
        {
            canvasSnapGuideV.Visibility = Visibility.Collapsed;
        }

        if (Math.Abs(snap.Y) >= 0.001)
        {
            double y = _canvasSnapGuideY ?? SnapGuidePosition(bounds.Top, bounds.Bottom, canvasH);
            canvasSnapGuideH.X1 = 0;
            canvasSnapGuideH.X2 = canvasW;
            canvasSnapGuideH.Y1 = canvasSnapGuideH.Y2 = y;
            canvasSnapGuideH.Visibility = Visibility.Visible;
        }
        else
        {
            canvasSnapGuideH.Visibility = Visibility.Collapsed;
        }
    }

    private void HideCanvasSnapGuides()
    {
        if (canvasSnapGuideLayer == null) return;
        canvasSnapGuideLayer.Visibility = Visibility.Collapsed;
        canvasSnapGuideV.Visibility = Visibility.Collapsed;
        canvasSnapGuideH.Visibility = Visibility.Collapsed;
        _canvasSnapGuideX = null;
        _canvasSnapGuideY = null;
    }

    private Vector GetPreviewOverlaySnapOffset(Rect movingBounds)
    {
        _canvasSnapGuideX = null;
        _canvasSnapGuideY = null;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            return new Vector();

        double canvasW = Math.Max(1, overlayCanvas.ActualWidth);
        double canvasH = Math.Max(1, overlayCanvas.ActualHeight);
        double dx = SnapAxis(movingBounds.Left, movingBounds.Right, canvasW, out var guideX);
        double dy = SnapAxis(movingBounds.Top, movingBounds.Bottom, canvasH, out var guideY);

        double bestXDistance = Math.Abs(dx) > 0.001 ? Math.Abs(dx) : CanvasSnapDistance;
        double bestYDistance = Math.Abs(dy) > 0.001 ? Math.Abs(dy) : CanvasSnapDistance;
        foreach (var target in GetVisibleOverlaySnapBounds(excludeCurrentSelection: true))
        {
            TrySnapToTargetAxis(
                new[] { movingBounds.Left, movingBounds.Left + movingBounds.Width / 2, movingBounds.Right },
                new[] { target.Left, target.Left + target.Width / 2, target.Right },
                ref dx,
                ref guideX,
                ref bestXDistance);
            TrySnapToTargetAxis(
                new[] { movingBounds.Top, movingBounds.Top + movingBounds.Height / 2, movingBounds.Bottom },
                new[] { target.Top, target.Top + target.Height / 2, target.Bottom },
                ref dy,
                ref guideY,
                ref bestYDistance);
        }

        _canvasSnapGuideX = Math.Abs(dx) > 0.001 ? guideX : null;
        _canvasSnapGuideY = Math.Abs(dy) > 0.001 ? guideY : null;
        return new Vector(dx, dy);
    }

    private void UpdatePreviewOverlaySnapGuides(Rect bounds, Vector snap)
    {
        if (canvasSnapGuideLayer == null) return;
        if (Math.Abs(snap.X) < 0.001 && Math.Abs(snap.Y) < 0.001)
        {
            HideCanvasSnapGuides();
            return;
        }

        double canvasW = Math.Max(1, overlayCanvas.ActualWidth);
        double canvasH = Math.Max(1, overlayCanvas.ActualHeight);
        canvasSnapGuideLayer.Width = canvasW;
        canvasSnapGuideLayer.Height = canvasH;
        canvasSnapGuideLayer.Visibility = Visibility.Visible;

        if (Math.Abs(snap.X) >= 0.001)
        {
            double x = _canvasSnapGuideX ?? SnapGuidePosition(bounds.Left, bounds.Right, canvasW);
            canvasSnapGuideV.X1 = canvasSnapGuideV.X2 = x;
            canvasSnapGuideV.Y1 = 0;
            canvasSnapGuideV.Y2 = canvasH;
            canvasSnapGuideV.Visibility = Visibility.Visible;
        }
        else
        {
            canvasSnapGuideV.Visibility = Visibility.Collapsed;
        }

        if (Math.Abs(snap.Y) >= 0.001)
        {
            double y = _canvasSnapGuideY ?? SnapGuidePosition(bounds.Top, bounds.Bottom, canvasH);
            canvasSnapGuideH.X1 = 0;
            canvasSnapGuideH.X2 = canvasW;
            canvasSnapGuideH.Y1 = canvasSnapGuideH.Y2 = y;
            canvasSnapGuideH.Visibility = Visibility.Visible;
        }
        else
        {
            canvasSnapGuideH.Visibility = Visibility.Collapsed;
        }
    }

    private static double SnapGuidePosition(double start, double end, double canvasSize)
    {
        double size = end - start;
        double center = start + size / 2;
        if (Math.Abs(start) <= CanvasSnapDistance)
            return 0;
        if (Math.Abs(canvasSize - end) <= CanvasSnapDistance)
            return canvasSize;
        if (Math.Abs(canvasSize - size) > CanvasSnapDistance &&
            Math.Abs(canvasSize / 2 - center) <= CanvasSnapDistance)
            return canvasSize / 2;
        return center;
    }

    private static double AngleFromCenter(Point p, Point center)
    {
        return Math.Atan2(p.Y - center.Y, p.X - center.X) * 180 / Math.PI + 90;
    }

    private static double SnapCanvasRotation(double angle)
    {
        double normalized = ((angle % 360) + 360) % 360;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            return normalized;

        double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 15 : 45;
        double snapped = Math.Round(normalized / step) * step;
        double delta = Math.Abs(NormalizeAngleDelta(normalized - snapped));
        if (delta <= ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 7.5 : 5))
            normalized = snapped;
        return ((normalized % 360) + 360) % 360;
    }

    private static double NormalizeAngleDelta(double value)
    {
        value = ((value + 180) % 360 + 360) % 360 - 180;
        return value;
    }

    private Rect BuildCanvasCropClipRect(VideoClip c)
    {
        if (videoView.ActualWidth < 1 || videoView.ActualHeight < 1 || c.VideoWidth <= 0 || c.VideoHeight <= 0)
            return Rect.Empty;

        var crop = NormalizedCrop(c);
        double baseScale = Math.Min(videoView.ActualWidth / c.VideoWidth, videoView.ActualHeight / c.VideoHeight);
        double contentW = c.VideoWidth * baseScale;
        double contentH = c.VideoHeight * baseScale;
        double x = (videoView.ActualWidth - contentW) / 2 + crop.left * baseScale;
        double y = (videoView.ActualHeight - contentH) / 2 + crop.top * baseScale;
        double w = Math.Max(1, contentW - (crop.left + crop.right) * baseScale);
        double h = Math.Max(1, contentH - (crop.top + crop.bottom) * baseScale);
        return new Rect(x, y, w, h);
    }

    private VideoClip? CanvasTargetClip() =>
        _selectedClip != null && !_selectedClip.IsAudioOnly ? _selectedClip : _playingClip;

    private void PreviewCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender == overlayCanvas && e.OriginalSource != overlayCanvas) return;
        if (_previewViewPanMode)
        {
            if (e.ClickCount >= 2)
            {
                ResetPreviewViewPan();
                status.Text = "Preview view reset.";
                e.Handled = true;
                return;
            }
            BeginPreviewViewPan(e.GetPosition(videoContainerOuter));
            e.Handled = true;
            return;
        }

        var c = CanvasTargetClip();
        var previewPoint = e.GetPosition(videoStack);
        bool hitClip = c != null && PreviewPointHitsCanvasClip(c, previewPoint);
        if (c == null || !hitClip)
        {
            if (sender == overlayCanvas || sender == videoView)
            {
                StartPreviewMarquee(e.GetPosition(overlayCanvas));
                e.Handled = true;
            }
            return;
        }
        bool ctrlDown = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool wasSelected = timeline.SelectedClips.Contains(c);
        if (ctrlDown)
        {
            if (!wasSelected)
                SelectClip(c);
        }
        else if (_selectedClip != c || timeline.SelectedClips.Count != 1)
        {
            SelectClip(c);
        }

        if (e.ClickCount >= 2)
        {
            c.CanvasScale = 1.0;
            c.CanvasScaleX = 1.0;
            c.CanvasScaleY = 1.0;
            c.CanvasOffsetX = 0;
            c.CanvasOffsetY = 0;
            c.CanvasCropLeft = 0;
            c.CanvasCropTop = 0;
            c.CanvasCropRight = 0;
            c.CanvasCropBottom = 0;
            ApplyClipTransform(c);
            UpdateInspectorCanvasFields();
            e.Handled = true;
            return;
        }
        _canvasDragActive = true;
        _canvasDragMoved = false;
        _canvasDragCaptureElement = sender as UIElement ?? videoView;
        _canvasDragClip = c;
        _canvasDragCtrlClickCandidate = ctrlDown;
        _canvasDragWasSelectedAtMouseDown = wasSelected;
        _canvasDragStartMouse = e.GetPosition(videoStack);
        _canvasDragStartOffX = c.CanvasOffsetX;
        _canvasDragStartOffY = c.CanvasOffsetY;
        _canvasDragCaptureElement.CaptureMouse();
        e.Handled = true;
    }

    private bool PreviewPointHitsCanvasClip(VideoClip c, Point p)
    {
        if (c.IsAudioOnly || c.VideoWidth <= 0 || c.VideoHeight <= 0)
            return false;

        var bounds = GetVisibleCanvasBounds(c, c.CanvasOffsetX, c.CanvasOffsetY, c.CanvasScale);
        if (bounds.IsEmpty || !bounds.Contains(p))
            return false;

        if (Math.Abs(c.RotateDegrees) < 0.001)
            return true;

        double cx = bounds.Left + bounds.Width / 2;
        double cy = bounds.Top + bounds.Height / 2;
        var local = RotateVector(new Vector(p.X - cx, p.Y - cy), -c.RotateDegrees);
        double canvasW = Math.Max(1, videoStack.ActualWidth);
        double canvasH = Math.Max(1, videoStack.ActualHeight);
        double baseScale = Math.Min(canvasW / c.VideoWidth, canvasH / c.VideoHeight);
        var crop = NormalizedCrop(c);
        double visibleW = Math.Max(1, (c.VideoWidth - crop.left - crop.right) * baseScale * c.CanvasScale * c.CanvasScaleX);
        double visibleH = Math.Max(1, (c.VideoHeight - crop.top - crop.bottom) * baseScale * c.CanvasScale * c.CanvasScaleY);
        return Math.Abs(local.X) <= visibleW / 2 && Math.Abs(local.Y) <= visibleH / 2;
    }

    private void PreviewViewPan_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_previewViewPanMode || e.Handled) return;
        if (e.ClickCount >= 2)
        {
            ResetPreviewViewPan();
            status.Text = "Preview view reset.";
            e.Handled = true;
            return;
        }
        BeginPreviewViewPan(e.GetPosition(videoContainerOuter));
        e.Handled = true;
    }

    private void PreviewViewPan_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_previewViewPanActive) return;
        var p = e.GetPosition(videoContainerOuter);
        var delta = p - _previewViewPanStartMouse;
        if (Math.Abs(delta.X) > 1 || Math.Abs(delta.Y) > 1)
            _previewViewPanMoved = true;
        _previewViewPanOffset = new Point(_previewViewPanStartOffset.X + delta.X, _previewViewPanStartOffset.Y + delta.Y);
        ApplyPreviewViewTransform();
        e.Handled = true;
    }

    private void PreviewViewPan_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_previewViewPanActive) return;
        EndPreviewViewPan();
        e.Handled = true;
    }

    private void PreviewViewPan_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_previewViewPanMode || e.Handled) return;
        AdjustPreviewViewZoom(e.Delta);
        e.Handled = true;
    }

    private void BeginPreviewViewPan(Point p)
    {
        _previewViewPanActive = true;
        _previewViewPanStartMouse = p;
        _previewViewPanStartOffset = _previewViewPanOffset;
        videoContainerOuter.CaptureMouse();
        SetPreviewBaseCursor(Cursors.ScrollAll);
    }

    private void EndPreviewViewPan()
    {
        _previewViewPanActive = false;
        if (videoContainerOuter.IsMouseCaptured)
            videoContainerOuter.ReleaseMouseCapture();
        SetPreviewBaseCursor(_previewViewPanMode ? Cursors.Hand : Cursors.Arrow);
    }

    private void SetPreviewViewPanMode(bool enabled)
    {
        _previewViewPanMode = enabled;
        if (!enabled && _previewViewPanActive)
            EndPreviewViewPan();
        SetPreviewBaseCursor(enabled ? Cursors.Hand : Cursors.Arrow);
    }

    private void SetPreviewBaseCursor(Cursor cursor)
    {
        if (videoContainerOuter != null) videoContainerOuter.Cursor = cursor;
        if (videoView != null) videoView.Cursor = cursor;
        if (overlayCanvas != null) overlayCanvas.Cursor = cursor;
    }

    private void UpdatePreviewCanvasCursor(Point previewPoint)
    {
        if (_previewViewPanMode)
        {
            SetPreviewBaseCursor(_previewViewPanActive ? Cursors.ScrollAll : Cursors.Hand);
            return;
        }

        if (_previewMarqueeActive)
        {
            SetPreviewBaseCursor(Cursors.Cross);
            return;
        }

        if (_canvasDragActive)
        {
            SetPreviewBaseCursor(Cursors.SizeAll);
            return;
        }

        var c = CanvasTargetClip();
        SetPreviewBaseCursor(c != null && PreviewPointHitsCanvasClip(c, previewPoint)
            ? Cursors.SizeAll
            : Cursors.Arrow);
    }

    private void PreviewCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_previewMarqueeActive || _canvasDragActive || _previewViewPanActive)
            return;

        SetPreviewBaseCursor(_previewViewPanMode ? Cursors.Hand : Cursors.Arrow);
    }

    private void ApplyPreviewViewTransform()
    {
        if (videoContainer == null) return;
        ClampPreviewViewPanOffset();
        bool isDefault =
            Math.Abs(_previewViewPanOffset.X) < 0.01 &&
            Math.Abs(_previewViewPanOffset.Y) < 0.01 &&
            Math.Abs(_previewViewZoom - 1.0) < 0.001;

        if (isDefault)
        {
            videoContainer.RenderTransform = null;
            UpdatePreviewViewZoomHud();
            return;
        }

        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(_previewViewZoom, _previewViewZoom));
        transform.Children.Add(new TranslateTransform(_previewViewPanOffset.X, _previewViewPanOffset.Y));
        videoContainer.RenderTransformOrigin = new Point(0.5, 0.5);
        videoContainer.RenderTransform = transform;
        UpdatePreviewViewZoomHud();
    }

    private void ClampPreviewViewPanOffset()
    {
        if (videoContainerOuter == null || videoContainer == null) return;

        double viewportW = videoContainerOuter.ActualWidth;
        double viewportH = videoContainerOuter.ActualHeight;
        double baseW = videoContainer.ActualWidth > 0 ? videoContainer.ActualWidth : videoContainer.Width;
        double baseH = videoContainer.ActualHeight > 0 ? videoContainer.ActualHeight : videoContainer.Height;
        if (viewportW <= 0 || viewportH <= 0 || baseW <= 0 || baseH <= 0) return;

        double scaledW = baseW * _previewViewZoom;
        double scaledH = baseH * _previewViewZoom;
        double limitX = Math.Max((scaledW - viewportW) * 0.5, 0.0) + viewportW * 0.5;
        double limitY = Math.Max((scaledH - viewportH) * 0.5, 0.0) + viewportH * 0.5;

        _previewViewPanOffset = new Point(
            Math.Clamp(_previewViewPanOffset.X, -limitX, limitX),
            Math.Clamp(_previewViewPanOffset.Y, -limitY, limitY));
    }

    private void AdjustPreviewViewZoom(int wheelDelta)
    {
        int notches = wheelDelta == 0
            ? 0
            : Math.Max(1, Math.Abs(wheelDelta) / 120) * Math.Sign(wheelDelta);
        AdjustPreviewViewZoomLevel(notches);
    }

    private void AdjustPreviewViewZoomLevel(int levelDelta)
    {
        if (levelDelta == 0) return;
        int nextLevel = Math.Clamp(_previewViewZoomLevel + levelDelta, -PreviewViewMaxZoomLevel, PreviewViewMaxZoomLevel);
        if (nextLevel == _previewViewZoomLevel) return;

        double oldZoom = _previewViewZoom;
        _previewViewZoomLevel = nextLevel;
        _previewViewZoom = Math.Pow(PreviewViewMaxZoomAmount, _previewViewZoomLevel / (double)PreviewViewMaxZoomLevel);
        double offsetRatio = oldZoom <= 0 ? 1.0 : _previewViewZoom / oldZoom;
        _previewViewPanOffset = new Point(_previewViewPanOffset.X * offsetRatio, _previewViewPanOffset.Y * offsetRatio);
        ApplyPreviewViewTransform();
        status.Text = $"Preview view {Math.Round(_previewViewZoom * 100)}%.";
    }

    private void ResetPreviewViewPan()
    {
        _previewViewPanOffset = new Point();
        _previewViewZoomLevel = 0;
        _previewViewZoom = 1.0;
        ApplyPreviewViewTransform();
    }

    private void UpdatePreviewViewZoomHud()
    {
        if (previewViewZoomText == null) return;
        previewViewZoomText.Text = $"{Math.Round(_previewViewZoom * 100)}%";
    }

    private void PreviewViewZoomIn_Click(object sender, RoutedEventArgs e)
    {
        AdjustPreviewViewZoomLevel(1);
    }

    private void PreviewViewZoomOut_Click(object sender, RoutedEventArgs e)
    {
        AdjustPreviewViewZoomLevel(-1);
    }

    private void PreviewViewZoomFit_Click(object sender, RoutedEventArgs e)
    {
        ResetPreviewViewPan();
        status.Text = "Preview view reset.";
    }

    private void PreviewCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_previewMarqueeActive)
        {
            UpdatePreviewMarquee(e.GetPosition(overlayCanvas));
            e.Handled = true;
            return;
        }

        if (!_canvasDragActive)
        {
            UpdatePreviewCanvasCursor(e.GetPosition(videoStack));
            return;
        }
        var c = CanvasTargetClip();
        if (c == null) return;
        var p = e.GetPosition(videoStack);
        if (!_canvasDragMoved)
        {
            double dx = p.X - _canvasDragStartMouse.X;
            double dy = p.Y - _canvasDragStartMouse.Y;
            double thresholdX = Math.Max(2.0, SystemParameters.MinimumHorizontalDragDistance);
            double thresholdY = Math.Max(2.0, SystemParameters.MinimumVerticalDragDistance);
            if (Math.Abs(dx) < thresholdX && Math.Abs(dy) < thresholdY)
            {
                e.Handled = true;
                return;
            }

            _canvasDragMoved = true;
            _canvasDragStartMouse = p;
            _canvasDragStartOffX = c.CanvasOffsetX;
            _canvasDragStartOffY = c.CanvasOffsetY;
            e.Handled = true;
            return;
        }

        double w = videoStack.ActualWidth, h = videoStack.ActualHeight;
        if (w < 1 || h < 1) return;
        double newOffX = _canvasDragStartOffX + (p.X - _canvasDragStartMouse.X) / w;
        double newOffY = _canvasDragStartOffY + (p.Y - _canvasDragStartMouse.Y) / h;
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            var snap = GetCanvasSnapOffset(c, newOffX, newOffY, c.CanvasScale);
            newOffX += snap.X / w;
            newOffY += snap.Y / h;
            UpdateCanvasSnapGuides(c, newOffX, newOffY, c.CanvasScale, snap);
        }
        else
        {
            HideCanvasSnapGuides();
        }
        c.CanvasOffsetX = newOffX;
        c.CanvasOffsetY = newOffY;
        ApplyClipTransform(c);
        UpdateInspectorCanvasFields();
        e.Handled = true;
    }

    private void PreviewCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_previewMarqueeActive)
        {
            FinishPreviewMarquee(e.GetPosition(overlayCanvas));
            e.Handled = true;
            return;
        }

        if (!_canvasDragActive) return;
        bool moved = _canvasDragMoved;
        var clickClip = _canvasDragClip;
        bool ctrlClickCandidate = _canvasDragCtrlClickCandidate;
        bool wasSelectedAtMouseDown = _canvasDragWasSelectedAtMouseDown;
        _canvasDragActive = false;
        _canvasDragMoved = false;
        _canvasDragClip = null;
        _canvasDragCtrlClickCandidate = false;
        _canvasDragWasSelectedAtMouseDown = false;
        _canvasDragCaptureElement?.ReleaseMouseCapture();
        _canvasDragCaptureElement = null;
        HideCanvasSnapGuides();
        if (ctrlClickCandidate && !moved && clickClip != null)
        {
            if (wasSelectedAtMouseDown)
                timeline.HandleClipClick(clickClip, ctrl: true);
            else
                status.Text = $"Clip selection: {timeline.SelectedClips.Count}";
            RepositionCanvasHandles();
        }
        UpdatePreviewCanvasCursor(e.GetPosition(videoStack));
        e.Handled = true;
    }

    private void PreviewCanvas_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_canvasDragActive || !ReferenceEquals(sender, _canvasDragCaptureElement))
            return;

        _canvasDragActive = false;
        _canvasDragMoved = false;
        _canvasDragClip = null;
        _canvasDragCtrlClickCandidate = false;
        _canvasDragWasSelectedAtMouseDown = false;
        _canvasDragCaptureElement = null;
        HideCanvasSnapGuides();
        SetPreviewBaseCursor(Cursors.Arrow);
    }

    private void StartPreviewMarquee(Point p)
    {
        _previewMarqueeActive = true;
        _previewMarqueeMoved = false;
        _previewMarqueeStart = p;
        previewSelectionLayer.Width = Math.Max(1, overlayCanvas.ActualWidth);
        previewSelectionLayer.Height = Math.Max(1, overlayCanvas.ActualHeight);
        previewSelectionLayer.Visibility = Visibility.Collapsed;
        overlayCanvas.CaptureMouse();
    }

    private void UpdatePreviewMarquee(Point p)
    {
        if (previewSelectionLayer == null) return;
        if (!_previewMarqueeMoved)
        {
            double dx = p.X - _previewMarqueeStart.X;
            double dy = p.Y - _previewMarqueeStart.Y;
            double thresholdX = Math.Max(2.0, SystemParameters.MinimumHorizontalDragDistance);
            double thresholdY = Math.Max(2.0, SystemParameters.MinimumVerticalDragDistance);
            if (Math.Abs(dx) < thresholdX && Math.Abs(dy) < thresholdY)
                return;

            _previewMarqueeMoved = true;
            previewSelectionLayer.Visibility = Visibility.Visible;
            SetPreviewBaseCursor(Cursors.Cross);
        }

        var rect = MakeRect(_previewMarqueeStart, p);
        Canvas.SetLeft(previewSelectionBox, rect.Left);
        Canvas.SetTop(previewSelectionBox, rect.Top);
        previewSelectionBox.Width = rect.Width;
        previewSelectionBox.Height = rect.Height;
    }

    private void FinishPreviewMarquee(Point p)
    {
        bool moved = _previewMarqueeMoved;
        _previewMarqueeActive = false;
        _previewMarqueeMoved = false;
        if (overlayCanvas.IsMouseCaptured) overlayCanvas.ReleaseMouseCapture();
        previewSelectionLayer.Visibility = Visibility.Collapsed;
        UpdatePreviewCanvasCursor(p);

        var marquee = MakeRect(_previewMarqueeStart, p);
        var mode = CurrentPreviewOverlaySelectionMode();
        if (!moved || marquee.Width < 4 || marquee.Height < 4)
        {
            if (mode == Timeline.PreviewOverlaySelectionMode.Replace)
                timeline.ClearAllSelection();
            return;
        }

        var blockHits = _blockControls
            .Where(kv => kv.Value.Visibility == Visibility.Visible &&
                         ReferenceEquals(kv.Value.Parent, overlayCanvas) &&
                         marquee.IntersectsWith(new Rect(kv.Key.X, kv.Key.Y, kv.Key.Width, kv.Key.Height)))
            .Select(kv => kv.Key)
            .ToList();

        var textHits = _textOverlayPreviewControls
            .Where(kv => kv.Value.Visibility == Visibility.Visible &&
                         ReferenceEquals(kv.Value.Parent, overlayCanvas) &&
                         PreviewElementRect(kv.Value).IntersectsWith(marquee))
            .Select(kv => kv.Key)
            .ToList();

        if (blockHits.Count == 0 && textHits.Count == 0)
        {
            if (mode == Timeline.PreviewOverlaySelectionMode.Replace) timeline.ClearAllSelection();
            return;
        }

        timeline.SelectPreviewOverlayItems(blockHits, textHits, mode);
        RefreshTextOverlayPreviewSelectionVisuals();
        status.Text = mode switch
        {
            Timeline.PreviewOverlaySelectionMode.Remove => $"Preview removed {blockHits.Count + textHits.Count} item{(blockHits.Count + textHits.Count == 1 ? "" : "s")} from selection.",
            Timeline.PreviewOverlaySelectionMode.Toggle => $"Preview toggled {blockHits.Count + textHits.Count} item{(blockHits.Count + textHits.Count == 1 ? "" : "s")}.",
            Timeline.PreviewOverlaySelectionMode.Add => $"Preview added {blockHits.Count + textHits.Count} item{(blockHits.Count + textHits.Count == 1 ? "" : "s")} to selection.",
            _ => $"Preview selected {blockHits.Count + textHits.Count} item{(blockHits.Count + textHits.Count == 1 ? "" : "s")}."
        };
    }

    private static Timeline.PreviewOverlaySelectionMode CurrentPreviewOverlaySelectionMode()
    {
        var modifiers = Keyboard.Modifiers;
        if ((modifiers & ModifierKeys.Alt) != 0)
            return Timeline.PreviewOverlaySelectionMode.Remove;
        if ((modifiers & ModifierKeys.Control) != 0)
            return Timeline.PreviewOverlaySelectionMode.Toggle;
        if ((modifiers & ModifierKeys.Shift) != 0)
            return Timeline.PreviewOverlaySelectionMode.Add;
        return Timeline.PreviewOverlaySelectionMode.Replace;
    }

    private static Rect MakeRect(Point a, Point b)
    {
        double x = Math.Min(a.X, b.X);
        double y = Math.Min(a.Y, b.Y);
        return new Rect(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    private static Rect PreviewElementRect(FrameworkElement el)
    {
        double x = Canvas.GetLeft(el);
        double y = Canvas.GetTop(el);
        if (double.IsNaN(x)) x = 0;
        if (double.IsNaN(y)) y = 0;
        return new Rect(x, y, Math.Max(1, el.ActualWidth), Math.Max(1, el.ActualHeight));
    }

    private void SelectPreviewItemBelow(Point overlayPoint, object? currentItem)
    {
        if (overlayCanvas == null) return;

        var hits = overlayCanvas.Children
            .OfType<FrameworkElement>()
            .Where(el => el.Visibility == Visibility.Visible)
            .OrderByDescending(Panel.GetZIndex)
            .ThenByDescending(el => overlayCanvas.Children.IndexOf(el));

        foreach (var el in hits)
        {
            if (el is VideoEditor.Controls.ResizableBlock rb)
            {
                if (ReferenceEquals(currentItem, rb.Model))
                    continue;

                var rect = new Rect(rb.Model.X, rb.Model.Y, Math.Max(1, rb.Model.Width), Math.Max(1, rb.Model.Height));
                if (!rect.Contains(overlayPoint))
                    continue;

                timeline.HandleBlockClick(rb.Model, ctrl: false);
                _selectedClip = null;
                RefreshTextOverlayPreviewSelectionVisuals();
                status.Text = "Selected hide block below.";
                return;
            }

            if (el is Border { Tag: TextOverlay ov })
            {
                if (ReferenceEquals(currentItem, ov))
                    continue;

                if (!PreviewElementRect(el).Contains(overlayPoint))
                    continue;

                timeline.HandleTextOverlayClick(ov, ctrl: false);
                _selectedBlock = null;
                _selectedClip = null;
                RefreshTextOverlayPreviewSelectionVisuals();
                status.Text = "Selected text overlay below.";
                return;
            }
        }

        var c = CanvasTargetClip();
        if (c != null && PreviewPointHitsCanvasClip(c, overlayPoint))
        {
            SelectClip(c);
            status.Text = "Selected video below.";
            return;
        }

        timeline.ClearAllSelection();
        _selectedBlock = null;
        _selectedClip = null;
        RefreshTextOverlayPreviewSelectionVisuals();
        RepositionCanvasHandles();
        status.Text = "Preview selection cleared.";
    }

    private Rect? PreviewOverlaySelectionBounds()
    {
        Rect? bounds = null;

        foreach (var block in timeline.SelectedBlocks)
        {
            if (!_blockControls.TryGetValue(block, out var ctl)) continue;
            if (ctl.Visibility != Visibility.Visible || !ReferenceEquals(ctl.Parent, overlayCanvas)) continue;
            var rect = new Rect(block.X, block.Y, Math.Max(1, block.Width), Math.Max(1, block.Height));
            bounds = bounds.HasValue ? Rect.Union(bounds.Value, rect) : rect;
        }

        foreach (var ov in timeline.SelectedTextOverlays)
        {
            if (!_textOverlayPreviewControls.TryGetValue(ov, out var ctl)) continue;
            if (ctl.Visibility != Visibility.Visible || !ReferenceEquals(ctl.Parent, overlayCanvas)) continue;
            var rect = PreviewElementRect(ctl);
            bounds = bounds.HasValue ? Rect.Union(bounds.Value, rect) : rect;
        }

        return bounds;
    }

    private void UpdatePreviewOverlayGroupBox()
    {
        if (previewOverlayGroupLayer == null || previewOverlayGroupBox == null || overlayCanvas == null)
            return;

        if (!ShouldShowPreviewOverlayTransformBox())
        {
            previewOverlayGroupLayer.Visibility = Visibility.Collapsed;
            return;
        }

        var bounds = PreviewOverlaySelectionBounds();
        if (!bounds.HasValue)
        {
            previewOverlayGroupLayer.Visibility = Visibility.Collapsed;
            return;
        }

        previewOverlayGroupLayer.Width = Math.Max(1, overlayCanvas.ActualWidth);
        previewOverlayGroupLayer.Height = Math.Max(1, overlayCanvas.ActualHeight);
        var rect = bounds.Value;
        double left = Math.Max(0, rect.Left - 3);
        double top = Math.Max(0, rect.Top - 3);
        double width = Math.Max(1, rect.Width + 6);
        double height = Math.Max(1, rect.Height + 6);
        Canvas.SetLeft(previewOverlayGroupBox, left);
        Canvas.SetTop(previewOverlayGroupBox, top);
        previewOverlayGroupBox.Width = width;
        previewOverlayGroupBox.Height = height;
        PositionPreviewGroupHandles(left, top, width, height);
        previewOverlayGroupLayer.Visibility = Visibility.Visible;
    }

    private void PositionPreviewGroupHandles(double left, double top, double width, double height)
    {
        PositionPreviewHandle(previewGroupHandleTL, left, top);
        PositionPreviewHandle(previewGroupHandleTR, left + width, top);
        PositionPreviewHandle(previewGroupHandleBL, left, top + height);
        PositionPreviewHandle(previewGroupHandleBR, left + width, top + height);
        PositionPreviewHandle(previewGroupHandleTC, left + width / 2, top);
        PositionPreviewHandle(previewGroupHandleBC, left + width / 2, top + height);
        PositionPreviewHandle(previewGroupHandleCL, left, top + height / 2);
        PositionPreviewHandle(previewGroupHandleCR, left + width, top + height / 2);
    }

    private static void PositionPreviewHandle(FrameworkElement handle, double centerX, double centerY)
    {
        Canvas.SetLeft(handle, centerX - handle.Width / 2);
        Canvas.SetTop(handle, centerY - handle.Height / 2);
    }

    private bool ShouldShowPreviewOverlayTransformBox()
    {
        int count = PreviewOverlaySelectionCount();
        return count > 1 || (count == 1 && timeline.SelectedTextOverlays.Count == 1);
    }

    private void PreviewGroupHandle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border handle || !ShouldShowPreviewOverlayTransformBox())
            return;
        var bounds = PreviewOverlaySelectionBounds();
        if (!bounds.HasValue || bounds.Value.Width < 1 || bounds.Value.Height < 1)
            return;

        _previewGroupResizeActive = true;
        _previewGroupResizeTag = handle.Tag?.ToString() ?? "";
        _previewGroupResizeStartMouse = e.GetPosition(overlayCanvas);
        _previewGroupResizeStartBounds = bounds.Value;
        _previewGroupResizeStartBlocks = timeline.SelectedBlocks.ToDictionary(
            b => b,
            b => new Rect(b.X, b.Y, Math.Max(1, b.Width), Math.Max(1, b.Height)));
        _previewGroupResizeStartTexts = timeline.SelectedTextOverlays.ToDictionary(
            o => o,
            o => (x: (double)o.X, y: (double)o.Y, fontSize: o.FontSize, pad: o.BackgroundPadding));
        handle.CaptureMouse();
        e.Handled = true;
    }

    private void PreviewGroupHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_previewGroupResizeActive || sender is not Border handle || !handle.IsMouseCaptured)
            return;
        ResizePreviewOverlayGroup(e.GetPosition(overlayCanvas));
        e.Handled = true;
    }

    private void PreviewGroupHandle_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border handle && handle.IsMouseCaptured)
            handle.ReleaseMouseCapture();
        if (_previewGroupResizeStartTexts != null)
        {
            foreach (var ov in _previewGroupResizeStartTexts.Keys.ToList())
                timeline.NotifyTextOverlayChanged(ov);
        }
        _previewGroupResizeActive = false;
        _previewGroupResizeTag = "";
        _previewGroupResizeStartBlocks = null;
        _previewGroupResizeStartTexts = null;
        HideCanvasSnapGuides();
        e.Handled = true;
    }

    private void ResizePreviewOverlayGroup(Point mouse)
    {
        if (_previewGroupResizeStartBlocks == null || _previewGroupResizeStartTexts == null)
            return;

        var start = _previewGroupResizeStartBounds;
        double left = start.Left;
        double top = start.Top;
        double right = start.Right;
        double bottom = start.Bottom;

        if (_previewGroupResizeTag.Contains('l')) left = Math.Min(right - 8, mouse.X);
        if (_previewGroupResizeTag.Contains('r')) right = Math.Max(left + 8, mouse.X);
        if (_previewGroupResizeTag.Contains('t')) top = Math.Min(bottom - 8, mouse.Y);
        if (_previewGroupResizeTag.Contains('b')) bottom = Math.Max(top + 8, mouse.Y);

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            double sx = (right - left) / Math.Max(1, start.Width);
            double sy = (bottom - top) / Math.Max(1, start.Height);
            double s = Math.Max(0.05, Math.Min(Math.Abs(sx), Math.Abs(sy)));
            if (_previewGroupResizeTag.Contains('l')) left = right - start.Width * s;
            if (_previewGroupResizeTag.Contains('r')) right = left + start.Width * s;
            if (_previewGroupResizeTag.Contains('t')) top = bottom - start.Height * s;
            if (_previewGroupResizeTag.Contains('b')) bottom = top + start.Height * s;
        }

        var target = new Rect(left, top, Math.Max(8, right - left), Math.Max(8, bottom - top));
        var snap = GetPreviewOverlaySnapOffset(target);
        target.Offset(snap);
        UpdatePreviewOverlaySnapGuides(target, snap);

        double scaleX = target.Width / Math.Max(1, start.Width);
        double scaleY = target.Height / Math.Max(1, start.Height);
        double textScale = Math.Sqrt(Math.Abs(scaleX * scaleY));
        double textPreviewScale = GetTextOverlayPreviewScale();

        foreach (var kv in _previewGroupResizeStartBlocks)
        {
            var b = kv.Key;
            var r = kv.Value;
            b.X = target.Left + (r.Left - start.Left) * scaleX;
            b.Y = target.Top + (r.Top - start.Top) * scaleY;
            b.Width = Math.Max(8, r.Width * scaleX);
            b.Height = Math.Max(8, r.Height * scaleY);
        }

        foreach (var kv in _previewGroupResizeStartTexts)
        {
            var ov = kv.Key;
            var p = kv.Value;
            double oldPreviewX = p.x * textPreviewScale;
            double oldPreviewY = p.y * textPreviewScale;
            double newPreviewX = target.Left + (oldPreviewX - start.Left) * scaleX;
            double newPreviewY = target.Top + (oldPreviewY - start.Top) * scaleY;
            ov.X = (int)Math.Round(newPreviewX / textPreviewScale);
            ov.Y = (int)Math.Round(newPreviewY / textPreviewScale);
            ov.FontSize = Math.Max(6, (int)Math.Round(p.fontSize * textScale));
            ov.BackgroundPadding = Math.Max(0, (int)Math.Round(p.pad * textScale));
            if (_textOverlayPreviewControls.TryGetValue(ov, out var ctl))
            {
                ApplyOverlayStyle(ctl, ov, textPreviewScale);
                ApplyOverlayPlacement(ctl, ov, textPreviewScale);
            }
            _textOverlayDirty.Add(ov);
        }

        SyncBlockInspector();
        RefreshTextOverlayPreviewSelectionVisuals();
        UpdatePreviewOverlayGroupBox();
        status.Text = $"Resized {PreviewOverlaySelectionCount()} preview items.";
    }

    private void PreviewCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender == overlayCanvas && e.OriginalSource != overlayCanvas) return;
        if (_previewViewPanMode)
        {
            AdjustPreviewViewZoom(e.Delta);
            e.Handled = true;
            return;
        }

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
        if (sender == overlayCanvas && e.OriginalSource != overlayCanvas) return;
        var c = CanvasTargetClip();
        if (c == null) return;
        var previewPoint = e.GetPosition(videoStack);
        if (!PreviewPointHitsCanvasClip(c, previewPoint))
        {
            e.Handled = false;
            return;
        }

        if (_selectedClip != c) SelectClip(c);

        var menu = new ContextMenu();
        menu.Items.Add(PreviewTransformMenuItem("Edit Transform...", () =>
        {
            var dlg = new CanvasTransformWindow(c, Math.Max(1, videoStack.ActualWidth), Math.Max(1, videoStack.ActualHeight))
            {
                Owner = this
            };
            if (dlg.ShowDialog() != true) return;

            c.CanvasOffsetX = dlg.CanvasOffsetX;
            c.CanvasOffsetY = dlg.CanvasOffsetY;
            c.CanvasScale = dlg.CanvasScale;
            c.CanvasScaleX = dlg.CanvasScaleX;
            c.CanvasScaleY = dlg.CanvasScaleY;
            c.RotateDegrees = dlg.RotateDegrees;
            ApplyClampedCrop(c, dlg.CropLeft, dlg.CropTop, dlg.CropRight, dlg.CropBottom);
            ApplyPreviewTransformChange(c, "Canvas transform edited.");
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(PreviewTransformMenuItem("Fit to Canvas", () =>
        {
            c.CanvasScale = 1.0;
            c.CanvasScaleX = 1.0;
            c.CanvasScaleY = 1.0;
            c.CanvasOffsetX = 0;
            c.CanvasOffsetY = 0;
            ApplyPreviewTransformChange(c, "Clip fitted to canvas.");
        }));
        menu.Items.Add(PreviewTransformMenuItem("Fill Canvas", () =>
        {
            FillPreviewCanvas(c);
            ApplyPreviewTransformChange(c, "Clip filled the canvas.");
        }));
        menu.Items.Add(PreviewTransformMenuItem("Stretch to Canvas", () =>
        {
            StretchPreviewCanvas(c);
            ApplyPreviewTransformChange(c, "Clip stretched to canvas.");
        }));
        menu.Items.Add(PreviewTransformMenuItem("Center to Canvas", () =>
        {
            c.CanvasOffsetX = 0;
            c.CanvasOffsetY = 0;
            ApplyPreviewTransformChange(c, "Clip centered on canvas.");
        }));
        menu.Items.Add(PreviewTransformMenuItem("Center Horizontally", () =>
        {
            c.CanvasOffsetX = 0;
            ApplyPreviewTransformChange(c, "Clip centered horizontally.");
        }));
        menu.Items.Add(PreviewTransformMenuItem("Center Vertically", () =>
        {
            c.CanvasOffsetY = 0;
            ApplyPreviewTransformChange(c, "Clip centered vertically.");
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(PreviewTransformMenuItem("Rotate 90 CW", () =>
        {
            c.RotateDegrees = (c.RotateDegrees + 90) % 360;
            ApplyPreviewTransformChange(c, "Clip rotated 90 degrees.");
        }));
        menu.Items.Add(PreviewTransformMenuItem("Rotate 90 CCW", () =>
        {
            c.RotateDegrees = (c.RotateDegrees + 270) % 360;
            ApplyPreviewTransformChange(c, "Clip rotated -90 degrees.");
        }));
        menu.Items.Add(PreviewTransformMenuItem("Rotate 180", () =>
        {
            c.RotateDegrees = (c.RotateDegrees + 180) % 360;
            ApplyPreviewTransformChange(c, "Clip rotated 180 degrees.");
        }));
        menu.Items.Add(PreviewTransformMenuItem("Flip Horizontal", () =>
        {
            c.FlipH = !c.FlipH;
            ApplyPreviewTransformChange(c, "Clip horizontal flip toggled.");
        }));
        menu.Items.Add(PreviewTransformMenuItem("Flip Vertical", () =>
        {
            c.FlipV = !c.FlipV;
            ApplyPreviewTransformChange(c, "Clip vertical flip toggled.");
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(PreviewTransformMenuItem("Copy Transform", () =>
        {
            _copiedCanvasTransform = CanvasTransformSnapshot.From(c);
            status.Text = "Canvas transform copied.";
        }));
        menu.Items.Add(PreviewTransformMenuItem("Paste Transform", () =>
        {
            _copiedCanvasTransform?.ApplyTo(c);
            ApplyPreviewTransformChange(c, "Canvas transform pasted.");
        }, _copiedCanvasTransform != null));
        menu.Items.Add(new Separator());
        menu.Items.Add(PreviewTransformMenuItem("Reset Crop", () =>
        {
            c.CanvasCropLeft = 0;
            c.CanvasCropTop = 0;
            c.CanvasCropRight = 0;
            c.CanvasCropBottom = 0;
            ApplyPreviewTransformChange(c, "Canvas crop reset.");
        }, c.HasManualCanvasCrop));
        menu.Items.Add(PreviewTransformMenuItem("Reset Transform", () =>
        {
            ResetPreviewTransform(c);
            ApplyPreviewTransformChange(c, "Canvas transform reset.");
        }));

        menu.PlacementTarget = sender as UIElement ?? videoStack;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private MenuItem PreviewTransformMenuItem(string header, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }

    private void ResetPreviewTransform(VideoClip c)
    {
        c.CanvasScale = 1.0;
        c.CanvasScaleX = 1.0;
        c.CanvasScaleY = 1.0;
        c.CanvasOffsetX = 0;
        c.CanvasOffsetY = 0;
        c.CanvasCropLeft = 0;
        c.CanvasCropTop = 0;
        c.CanvasCropRight = 0;
        c.CanvasCropBottom = 0;
        c.RotateDegrees = 0;
        c.FlipH = false;
        c.FlipV = false;
    }

    private void FillPreviewCanvas(VideoClip c)
    {
        double canvasW = Math.Max(1, videoStack.ActualWidth);
        double canvasH = Math.Max(1, videoStack.ActualHeight);
        if (c.VideoWidth <= 0 || c.VideoHeight <= 0) return;

        var crop = NormalizedCrop(c);
        double visibleW = Math.Max(1, c.VideoWidth - crop.left - crop.right);
        double visibleH = Math.Max(1, c.VideoHeight - crop.top - crop.bottom);
        double baseScale = Math.Min(canvasW / c.VideoWidth, canvasH / c.VideoHeight);
        double fillScale = Math.Max(canvasW / (visibleW * baseScale), canvasH / (visibleH * baseScale));

        c.CanvasScale = Math.Max(0.05, Math.Min(8.0, fillScale));
        c.CanvasScaleX = 1.0;
        c.CanvasScaleY = 1.0;
        c.CanvasOffsetX = 0;
        c.CanvasOffsetY = 0;
    }

    private void StretchPreviewCanvas(VideoClip c)
    {
        double canvasW = Math.Max(1, videoStack.ActualWidth);
        double canvasH = Math.Max(1, videoStack.ActualHeight);
        if (c.VideoWidth <= 0 || c.VideoHeight <= 0) return;

        var crop = NormalizedCrop(c);
        double visibleW = Math.Max(1, c.VideoWidth - crop.left - crop.right);
        double visibleH = Math.Max(1, c.VideoHeight - crop.top - crop.bottom);
        double baseScale = Math.Min(canvasW / c.VideoWidth, canvasH / c.VideoHeight);

        c.CanvasScale = 1.0;
        c.CanvasScaleX = Math.Max(0.05, Math.Min(8.0, canvasW / (visibleW * baseScale)));
        c.CanvasScaleY = Math.Max(0.05, Math.Min(8.0, canvasH / (visibleH * baseScale)));
        c.CanvasOffsetX = 0;
        c.CanvasOffsetY = 0;
    }

    private void ApplyPreviewTransformChange(VideoClip c, string message)
    {
        ApplyClipTransform(c);
        UpdateInspectorCanvasFields();
        UpdateCanvasLabels(c);
        status.Text = message;
    }

    private Action? _updateInspectorCanvasFields;
    private void UpdateInspectorCanvasFields()
    {
        _updateInspectorCanvasFields?.Invoke();
        RepositionCanvasHandles();
    }

    // ===== OBS-style transform handles =====
    //
    // Four 20×20 squares pinned to the four corners of the project canvas via XAML
    // aspect ratio while anchoring the opposite side/corner. This feels much closer
    // to OBS than scaling everything from the centre.
    // scale = startScale × (currentDistFromCentre / startDistFromCentre).

    private bool _handleDragActive;
    private bool _handleDragCropMode;
    private bool _handleDragRotateMode;
    private string _handleDragTag = "";
    private Point _handleDragStartMouse;
    private double _handleDragStartScale;
    private double _handleDragStartScaleX;
    private double _handleDragStartScaleY;
    private double _handleDragStartOffX;
    private double _handleDragStartOffY;
    private double _handleDragStartW;
    private double _handleDragStartH;
    private double _handleDragStartCropLeft;
    private double _handleDragStartCropTop;
    private double _handleDragStartCropRight;
    private double _handleDragStartCropBottom;
    private Point _handleDragRotateCenter;
    private double _handleDragStartAngle;
    private double _handleDragStartRotate;

    private void RepositionCanvasHandles()
    {
        if (canvasHandlesLayer == null) return;
        var c = CanvasTargetClip();
        if (c == null || c.IsAudioOnly || c.VideoWidth <= 0 || c.VideoHeight <= 0 ||
            videoStack.ActualWidth < 1 || videoStack.ActualHeight < 1)
        {
            canvasHandlesLayer.Visibility = Visibility.Collapsed;
            HideCanvasSpacingHelpers();
            return;
        }
        canvasHandlesLayer.Visibility = Visibility.Visible;

        // Size the layer to match the *actual* displayed video, not the whole canvas.
        // MediaElement uses Stretch=Uniform so the base displayed size is the source dimensions
        // scaled by min(canvasW/srcW, canvasH/srcH); the user's CanvasScale multiplies that.
        // When scale > 1 the displayed image is bigger than the canvas - clamp the layer to
        // canvas size so handles stay inside the visible region.
        double canvasW = videoStack.ActualWidth;
        double canvasH = videoStack.ActualHeight;
        double baseScale = Math.Min(canvasW / c.VideoWidth, canvasH / c.VideoHeight);
        var crop = NormalizedCrop(c);
        double dispW = (c.VideoWidth - crop.left - crop.right) * baseScale * c.CanvasScale * c.CanvasScaleX;
        double dispH = (c.VideoHeight - crop.top - crop.bottom) * baseScale * c.CanvasScale * c.CanvasScaleY;
        double fullW = c.VideoWidth * baseScale * c.CanvasScale * c.CanvasScaleX;
        double fullH = c.VideoHeight * baseScale * c.CanvasScale * c.CanvasScaleY;
        double layerW = Math.Max(2, Math.Min(dispW, canvasW));
        double layerH = Math.Max(2, Math.Min(dispH, canvasH));
        double cropShiftX = ((crop.left - crop.right) * baseScale * c.CanvasScale * c.CanvasScaleX) / 2;
        double cropShiftY = ((crop.top - crop.bottom) * baseScale * c.CanvasScale * c.CanvasScaleY) / 2;
        canvasHandlesLayer.Width = layerW;
        canvasHandlesLayer.Height = layerH;
        var tg = new TransformGroup();
        if (c.RotateDegrees != 0)
            tg.Children.Add(new RotateTransform(c.RotateDegrees));
        tg.Children.Add(new TranslateTransform(
            c.CanvasOffsetX * canvasW + cropShiftX,
            c.CanvasOffsetY * canvasH + cropShiftY));
        canvasHandlesLayer.RenderTransform = tg;
        if (canvasHandleRotText != null)
            canvasHandleRotText.RenderTransform = c.RotateDegrees == 0 ? null : new RotateTransform(-c.RotateDegrees);
        UpdateCanvasHandleVisuals(c);
        UpdateCanvasSpacingHelpers(c);
    }

    private void UpdateCanvasSpacingHelpers(VideoClip c)
    {
        if (canvasSpacingHelperLayer == null || videoStack.ActualWidth < 1 || videoStack.ActualHeight < 1)
            return;

        if (_selectedClip != c || _handleDragCropMode || c.IsAudioOnly || c.VideoWidth <= 0 || c.VideoHeight <= 0)
        {
            HideCanvasSpacingHelpers();
            return;
        }

        var bounds = GetVisibleCanvasBounds(c, c.CanvasOffsetX, c.CanvasOffsetY, c.CanvasScale);
        if (bounds.IsEmpty)
        {
            HideCanvasSpacingHelpers();
            return;
        }

        double canvasW = Math.Max(1, videoStack.ActualWidth);
        double canvasH = Math.Max(1, videoStack.ActualHeight);
        canvasSpacingHelperLayer.Width = canvasW;
        canvasSpacingHelperLayer.Height = canvasH;
        canvasSpacingHelperLayer.Visibility = Visibility.Visible;

        double centerX = Math.Clamp(bounds.Left + bounds.Width / 2, 0, canvasW);
        double centerY = Math.Clamp(bounds.Top + bounds.Height / 2, 0, canvasH);
        UpdateSpacingLine(
            canvasSpacingLeftLine, canvasSpacingLeftLabel, canvasSpacingLeftText,
            0, centerY, Math.Clamp(bounds.Left, 0, canvasW), centerY,
            Math.Max(0, bounds.Left), horizontal: true);
        UpdateSpacingLine(
            canvasSpacingRightLine, canvasSpacingRightLabel, canvasSpacingRightText,
            Math.Clamp(bounds.Right, 0, canvasW), centerY, canvasW, centerY,
            Math.Max(0, canvasW - bounds.Right), horizontal: true);
        UpdateSpacingLine(
            canvasSpacingTopLine, canvasSpacingTopLabel, canvasSpacingTopText,
            centerX, 0, centerX, Math.Clamp(bounds.Top, 0, canvasH),
            Math.Max(0, bounds.Top), horizontal: false);
        UpdateSpacingLine(
            canvasSpacingBottomLine, canvasSpacingBottomLabel, canvasSpacingBottomText,
            centerX, Math.Clamp(bounds.Bottom, 0, canvasH), centerX, canvasH,
            Math.Max(0, canvasH - bounds.Bottom), horizontal: false);
    }

    private static void UpdateSpacingLine(System.Windows.Shapes.Line line, FrameworkElement label, TextBlock text,
        double x1, double y1, double x2, double y2, double distance, bool horizontal)
    {
        if (distance < 4)
        {
            line.Visibility = Visibility.Collapsed;
            label.Visibility = Visibility.Collapsed;
            return;
        }

        line.X1 = x1;
        line.Y1 = y1;
        line.X2 = x2;
        line.Y2 = y2;
        line.Visibility = Visibility.Visible;
        text.Text = $"{distance:0}px";
        label.Visibility = Visibility.Visible;
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double labelW = Math.Max(1, label.DesiredSize.Width);
        double labelH = Math.Max(1, label.DesiredSize.Height);
        double labelX = (x1 + x2) / 2 - labelW / 2;
        double labelY = (y1 + y2) / 2 - labelH / 2;

        if (horizontal)
            labelY -= 18;
        else
            labelX += 12;

        Canvas.SetLeft(label, Math.Max(2, labelX));
        Canvas.SetTop(label, Math.Max(2, labelY));
    }

    private void HideCanvasSpacingHelpers()
    {
        if (canvasSpacingHelperLayer == null) return;
        canvasSpacingHelperLayer.Visibility = Visibility.Collapsed;
        foreach (var el in new UIElement[]
        {
            canvasSpacingLeftLine, canvasSpacingRightLine, canvasSpacingTopLine, canvasSpacingBottomLine,
            canvasSpacingLeftLabel, canvasSpacingRightLabel, canvasSpacingTopLabel, canvasSpacingBottomLabel
        })
        {
            el.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateCanvasHandleVisuals(VideoClip c)
    {
        var accent = TryFindResource("Accent") as Brush ?? Brushes.MediumPurple;
        var cropBrush = new SolidColorBrush(Color.FromRgb(0x35, 0xD0, 0x7F));
        bool activeCrop = _handleDragActive && _handleDragCropMode;
        var outlineBrush = activeCrop ? cropBrush : accent;

        if (canvasSelectionBorder != null)
            canvasSelectionBorder.BorderBrush = outlineBrush;

        foreach (var handle in new[]
        {
            canvasHandleTL, canvasHandleTR, canvasHandleTC, canvasHandleCL,
            canvasHandleCR, canvasHandleBL, canvasHandleBR, canvasHandleBC
        })
        {
            if (handle != null)
                handle.BorderBrush = outlineBrush;
        }

        if (canvasHandleRot != null)
            canvasHandleRot.Background = accent;
        UpdateCanvasHandleCursors(c);

        SetCanvasCropLine(canvasCropLeftLine, c.CanvasCropLeft > 0.5 || activeCrop && _handleDragTag.Contains('l'));
        SetCanvasCropLine(canvasCropTopLine, c.CanvasCropTop > 0.5 || activeCrop && _handleDragTag.Contains('t'));
        SetCanvasCropLine(canvasCropRightLine, c.CanvasCropRight > 0.5 || activeCrop && _handleDragTag.Contains('r'));
        SetCanvasCropLine(canvasCropBottomLine, c.CanvasCropBottom > 0.5 || activeCrop && _handleDragTag.Contains('b'));
    }

    private const int CanvasHandleLeft = 1 << 0;
    private const int CanvasHandleRight = 1 << 1;
    private const int CanvasHandleTop = 1 << 2;
    private const int CanvasHandleBottom = 1 << 3;

    private void UpdateCanvasHandleCursors(VideoClip c)
    {
        SetCanvasHandleCursor(canvasHandleTL, CanvasHandleTop | CanvasHandleLeft, c);
        SetCanvasHandleCursor(canvasHandleTR, CanvasHandleTop | CanvasHandleRight, c);
        SetCanvasHandleCursor(canvasHandleTC, CanvasHandleTop, c);
        SetCanvasHandleCursor(canvasHandleCL, CanvasHandleLeft, c);
        SetCanvasHandleCursor(canvasHandleCR, CanvasHandleRight, c);
        SetCanvasHandleCursor(canvasHandleBL, CanvasHandleBottom | CanvasHandleLeft, c);
        SetCanvasHandleCursor(canvasHandleBR, CanvasHandleBottom | CanvasHandleRight, c);
        SetCanvasHandleCursor(canvasHandleBC, CanvasHandleBottom, c);
        if (canvasHandleRot != null)
            canvasHandleRot.Cursor = Cursors.Hand;
    }

    private static void SetCanvasHandleCursor(FrameworkElement? handle, int flags, VideoClip c)
    {
        if (handle == null) return;
        handle.Cursor = CursorForCanvasHandle(AdjustedCanvasHandleFlags(flags, c));
    }

    private static int AdjustedCanvasHandleFlags(int flags, VideoClip c)
    {
        bool isCorner = (flags & (flags - 1)) != 0;
        if (isCorner && c.FlipH)
            flags ^= CanvasHandleLeft | CanvasHandleRight;
        if (isCorner && c.FlipV)
            flags ^= CanvasHandleTop | CanvasHandleBottom;

        int octant = (int)Math.Round((((c.RotateDegrees % 360) + 360) % 360) / 45.0);
        if (octant % 4 >= 2)
        {
            if (isCorner)
                flags ^= CanvasHandleTop | CanvasHandleBottom;
            else
                flags = SwapCanvasHandleAxes(flags);
        }

        if (octant % 2 == 1)
        {
            if (isCorner)
            {
                bool diagonalDown = (flags & CanvasHandleLeft) != 0 && (flags & CanvasHandleTop) != 0 ||
                                    (flags & CanvasHandleRight) != 0 && (flags & CanvasHandleBottom) != 0;
                flags = diagonalDown
                    ? (flags & ~(CanvasHandleTop | CanvasHandleBottom))
                    : (flags & ~(CanvasHandleLeft | CanvasHandleRight));
            }
            else
            {
                flags = SwapCanvasHandleAxes(flags);
            }
        }

        return flags;
    }

    private static int SwapCanvasHandleAxes(int flags)
    {
        int swapped = 0;
        if ((flags & CanvasHandleLeft) != 0) swapped |= CanvasHandleTop;
        if ((flags & CanvasHandleRight) != 0) swapped |= CanvasHandleBottom;
        if ((flags & CanvasHandleTop) != 0) swapped |= CanvasHandleLeft;
        if ((flags & CanvasHandleBottom) != 0) swapped |= CanvasHandleRight;
        return swapped;
    }

    private static Cursor CursorForCanvasHandle(int flags)
    {
        bool left = (flags & CanvasHandleLeft) != 0;
        bool right = (flags & CanvasHandleRight) != 0;
        bool top = (flags & CanvasHandleTop) != 0;
        bool bottom = (flags & CanvasHandleBottom) != 0;

        if (left && top || right && bottom)
            return Cursors.SizeNWSE;
        if (left && bottom || right && top)
            return Cursors.SizeNESW;
        if (left || right)
            return Cursors.SizeWE;
        if (top || bottom)
            return Cursors.SizeNS;
        return Cursors.Arrow;
    }

    private static void SetCanvasCropLine(UIElement? line, bool visible)
    {
        if (line != null)
            line.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CanvasHandle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var c = CanvasTargetClip();
        if (c == null || sender is not Border h) return;
        double canvasW = videoStack.ActualWidth;
        double canvasH = videoStack.ActualHeight;
        if (canvasW < 1 || canvasH < 1 || c.VideoWidth <= 0 || c.VideoHeight <= 0) return;

        double baseScale = Math.Min(canvasW / c.VideoWidth, canvasH / c.VideoHeight);
        var crop = NormalizedCrop(c);
        _handleDragStartW = (c.VideoWidth - crop.left - crop.right) * baseScale * c.CanvasScale * c.CanvasScaleX;
        _handleDragStartH = (c.VideoHeight - crop.top - crop.bottom) * baseScale * c.CanvasScale * c.CanvasScaleY;
        if (_handleDragStartW < 1 || _handleDragStartH < 1) return;

        _handleDragTag = h.Tag?.ToString() ?? "";
        _handleDragStartRotate = c.RotateDegrees;
        _handleDragStartMouse = e.GetPosition(videoStack);
        if (_handleDragTag == "rot")
        {
            var bounds = GetVisibleCanvasBounds(c, c.CanvasOffsetX, c.CanvasOffsetY, c.CanvasScale);
            if (bounds.IsEmpty) return;
            _handleDragRotateMode = true;
            _handleDragRotateCenter = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            var screenMouse = e.GetPosition(videoStack);
            _handleDragStartAngle = AngleFromCenter(screenMouse, _handleDragRotateCenter);
            _handleDragCropMode = false;
            _handleDragActive = true;
            h.CaptureMouse();
            e.Handled = true;
            return;
        }

        _handleDragStartScale = c.CanvasScale;
        _handleDragStartScaleX = c.CanvasScaleX;
        _handleDragStartScaleY = c.CanvasScaleY;
        _handleDragStartOffX = c.CanvasOffsetX;
        _handleDragStartOffY = c.CanvasOffsetY;
        _handleDragStartCropLeft = c.CanvasCropLeft;
        _handleDragStartCropTop = c.CanvasCropTop;
        _handleDragStartCropRight = c.CanvasCropRight;
        _handleDragStartCropBottom = c.CanvasCropBottom;
        _handleDragCropMode = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        _handleDragActive = true;
        UpdateCanvasHandleVisuals(c);
        h.CaptureMouse();
        e.Handled = true;
    }

    private void CanvasHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_handleDragActive) return;
        var c = CanvasTargetClip();
        if (c == null) return;
        double canvasW = Math.Max(1, videoStack.ActualWidth);
        double canvasH = Math.Max(1, videoStack.ActualHeight);
        var p = e.GetPosition(videoStack);
        var delta = RotateVector(p - _handleDragStartMouse, -_handleDragStartRotate);

        if (_handleDragRotateMode)
        {
            var screenMouse = e.GetPosition(videoStack);
            double angle = _handleDragStartRotate + AngleFromCenter(screenMouse, _handleDragRotateCenter) - _handleDragStartAngle;
            c.RotateDegrees = SnapCanvasRotation(angle);
            ApplyClipTransform(c);
            UpdateInspectorCanvasFields();
            return;
        }

        if (_handleDragCropMode)
        {
            ApplyCanvasCropDrag(c, delta);
            ApplyClipTransform(c);
            UpdateCanvasHandleVisuals(c);
            UpdateInspectorCanvasFields();
            return;
        }

        bool left = _handleDragTag.Contains('l');
        bool right = _handleDragTag.Contains('r');
        bool top = _handleDragTag.Contains('t');
        bool bottom = _handleDragTag.Contains('b');

        double factorX = 1.0;
        double factorY = 1.0;
        if (left) factorX = (_handleDragStartW - delta.X) / _handleDragStartW;
        else if (right) factorX = (_handleDragStartW + delta.X) / _handleDragStartW;
        if (top) factorY = (_handleDragStartH - delta.Y) / _handleDragStartH;
        else if (bottom) factorY = (_handleDragStartH + delta.Y) / _handleDragStartH;

        bool hasX = left || right;
        bool hasY = top || bottom;
        bool freeformStretch = hasX != hasY || (hasX && hasY && (Keyboard.Modifiers & ModifierKeys.Shift) != 0);

        double newScale = _handleDragStartScale;
        double newScaleX = _handleDragStartScaleX;
        double newScaleY = _handleDragStartScaleY;
        double appliedFactorX;
        double appliedFactorY;

        if (freeformStretch)
        {
            if (hasX)
                newScaleX = Math.Max(0.05, Math.Min(8.0, _handleDragStartScaleX * factorX));
            if (hasY)
                newScaleY = Math.Max(0.05, Math.Min(8.0, _handleDragStartScaleY * factorY));

            appliedFactorX = newScaleX / Math.Max(0.0001, _handleDragStartScaleX);
            appliedFactorY = newScaleY / Math.Max(0.0001, _handleDragStartScaleY);
        }
        else
        {
            double factor = Math.Abs(factorX - 1.0) >= Math.Abs(factorY - 1.0) ? factorX : factorY;
            newScale = Math.Max(0.05, Math.Min(8.0, _handleDragStartScale * factor));
            appliedFactorX = appliedFactorY = newScale / Math.Max(0.0001, _handleDragStartScale);
        }

        double newW = _handleDragStartW * appliedFactorX;
        double newH = _handleDragStartH * appliedFactorY;

        double startCenterX = canvasW / 2 + _handleDragStartOffX * canvasW;
        double startCenterY = canvasH / 2 + _handleDragStartOffY * canvasH;
        double newCenterX = startCenterX;
        double newCenterY = startCenterY;

        if (left)
            newCenterX = (startCenterX + _handleDragStartW / 2) - newW / 2;
        else if (right)
            newCenterX = (startCenterX - _handleDragStartW / 2) + newW / 2;
        if (top)
            newCenterY = (startCenterY + _handleDragStartH / 2) - newH / 2;
        else if (bottom)
            newCenterY = (startCenterY - _handleDragStartH / 2) + newH / 2;

        double proposedOffX = (newCenterX - canvasW / 2) / canvasW;
        double proposedOffY = (newCenterY - canvasH / 2) / canvasH;
        var snap = GetCanvasSnapOffset(c, proposedOffX, proposedOffY, newScale, newScaleX, newScaleY);
        newCenterX += snap.X;
        newCenterY += snap.Y;
        UpdateCanvasSnapGuides(c, proposedOffX + snap.X / canvasW, proposedOffY + snap.Y / canvasH, newScale, snap, newScaleX, newScaleY);

        c.CanvasScale = newScale;
        c.CanvasScaleX = newScaleX;
        c.CanvasScaleY = newScaleY;
        c.CanvasOffsetX = (newCenterX - canvasW / 2) / canvasW;
        c.CanvasOffsetY = (newCenterY - canvasH / 2) / canvasH;
        ApplyClipTransform(c);
        UpdateInspectorCanvasFields();
    }

    private void ApplyCanvasCropDrag(VideoClip c, Vector delta)
    {
        double canvasW = Math.Max(1, videoStack.ActualWidth);
        double canvasH = Math.Max(1, videoStack.ActualHeight);
        double baseScale = Math.Min(canvasW / c.VideoWidth, canvasH / c.VideoHeight) * Math.Max(0.0001, _handleDragStartScale);
        double dxSource = delta.X / (baseScale * Math.Max(0.0001, c.CanvasScaleX));
        double dySource = delta.Y / (baseScale * Math.Max(0.0001, c.CanvasScaleY));

        bool left = _handleDragTag.Contains('l');
        bool right = _handleDragTag.Contains('r');
        bool top = _handleDragTag.Contains('t');
        bool bottom = _handleDragTag.Contains('b');

        double l = _handleDragStartCropLeft;
        double t = _handleDragStartCropTop;
        double r = _handleDragStartCropRight;
        double b = _handleDragStartCropBottom;
        if (left) l += dxSource;
        if (right) r -= dxSource;
        if (top) t += dySource;
        if (bottom) b -= dySource;

        ApplyClampedCrop(c, l, t, r, b);
    }

    private static Vector RotateVector(Vector v, double degrees)
    {
        if (Math.Abs(degrees) < 0.001) return v;
        double radians = degrees * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        return new Vector(
            v.X * cos - v.Y * sin,
            v.X * sin + v.Y * cos);
    }

    private void CanvasHandle_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_handleDragActive) return;
        _handleDragActive = false;
        _handleDragCropMode = false;
        _handleDragRotateMode = false;
        _handleDragTag = "";
        HideCanvasSnapGuides();
        if (CanvasTargetClip() is { } c)
            UpdateCanvasHandleVisuals(c);
        if (sender is Border h) h.ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _tick.Stop();
        try { _inlineRecorderPreviewTimer?.Stop(); } catch { }
        try { StopInlineRecorderWebcamPreview(); } catch { }
        try { StopInlineScreenRecording(); } catch { }
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

    // Click-away: a MouseDown anywhere outside the inspector / preview / recorder area
    // collapses the inspector (or in recorder mode, returns to the Recording tab).
    // We use PreviewMouseDown so we run before child handlers — but we don't set e.Handled,
    // so existing click logic still runs.
    private void Window_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject src) return;
        // ResizableBlock click handlers fire from this same press → those are object
        // selections, not deselections.
        if (FindAncestor<VideoEditor.Controls.ResizableBlock>(src) != null) return;
        if (IsInInspectorKeepZone(src)) return;
        if (IsInRecorderPreviewKeepZone(src)) return;

        bool inRecorder = IsInlineRecorderVisible();
        bool hadSelection = _selectedBlock != null || _selectedClip != null;
        // If we're in recorder mode and a Block tab is open via SelectBlock, click-away
        // returns to the Recording tab so the user keeps the recorder context. If we're
        // outside recorder mode and something was selected, fully deselect.
        if (inRecorder)
        {
            if (hadSelection || recorderInspectorPanel.Visibility != Visibility.Visible)
            {
                _selectedBlock = null;
                _selectedClip = null;
                try { timeline.SelectBlock(null); } catch { }
                try { timeline.SelectClip(null); } catch { }
                ShowInspectorTab("recording");
            }
        }
        else if (hadSelection || blockPanel.Visibility == Visibility.Visible
                              || clipPanel.Visibility == Visibility.Visible
                              || emptyInspector.Visibility == Visibility.Visible)
        {
            _selectedBlock = null;
            _selectedClip = null;
            try { timeline.SelectBlock(null); } catch { }
            try { timeline.SelectClip(null); } catch { }
            ShowInspectorTab("none");
        }
    }

    // Click landed inside the right-side inspector — keep current tab open.
    private bool IsInInspectorKeepZone(DependencyObject src)
    {
        for (var node = src; node != null; node = VisualTreeHelper.GetParent(node))
            if (node is FrameworkElement fe && ReferenceEquals(fe, inspectorBorder)) return true;
        return false;
    }

    // Click landed on the centre recorder preview area — RecorderPreview_Clicked handles
    // re-opening the tab; don't also collapse it here.
    private bool IsInRecorderPreviewKeepZone(DependencyObject src)
    {
        for (var node = src; node != null; node = VisualTreeHelper.GetParent(node))
            if (node is FrameworkElement fe && ReferenceEquals(fe, screenRecorderPanel)) return true;
        return false;
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node != null)
        {
            if (node is T t) return t;
            node = VisualTreeHelper.GetParent(node);
        }
        return null;
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

    private async System.Threading.Tasks.Task<VideoClip?> AddClipAsync(string path, double? insertAtSec = null)
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
            SelectClip(clip);
            return clip;
        }
        catch (Exception ex)
        {
            status.Text = VideoEditor.Services.Localization.IsHebrew ? "ההוספה נכשלה: " + ex.Message : "Failed to add: " + ex.Message;
        }
        return null;
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
        UpdateExportFpsControls();
        exportFpsBox.SelectionChanged += ExportFpsBox_SelectionChanged;
        // Defer preview-aspect computation to first Loaded so the container has a real size
        Loaded += (_, _) => UpdatePreviewAspect();
    }

    private void ExportFpsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingExportFps) return;
        if (exportFpsBox.SelectedItem is not ComboBoxItem item) return;
        if (!int.TryParse(Convert.ToString(item.Content), out var fps)) return;

        AppSettings.ExportFps = fps;
        AppSettings.Save();
        UpdateExportFpsControls();
        status.Text = $"Export FPS set to {fps}.";
    }

    private void UpdateExportFpsControls()
    {
        if (exportFpsBox == null || projFps == null) return;

        _syncingExportFps = true;
        try
        {
            var fpsText = AppSettings.ExportFps.ToString();
            foreach (var item in exportFpsBox.Items.OfType<ComboBoxItem>())
            {
                if (Convert.ToString(item.Content) == fpsText)
                {
                    exportFpsBox.SelectedItem = item;
                    break;
                }
            }
            projFps.Text = $"{AppSettings.ExportFps} fps";
        }
        finally
        {
            _syncingExportFps = false;
        }
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
        // The videoView's actual size just changed → the ScaleTransform pivot and
        // TranslateTransform amount baked into RenderTransform are wrong. Re-apply
        // the transform so it tracks the new size, and refresh the handles.
        if (_playingClip != null)
            Dispatcher.BeginInvoke(new Action(() => ApplyClipTransform(_playingClip)),
                System.Windows.Threading.DispatcherPriority.Loaded);
        RepositionCanvasHandles();
        UpdateSafeAreasVisual();
        ApplyPreviewViewTransform();
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
        UpdateEmptyStartPanel();
    }

    private void UpdateEmptyStartPanel()
    {
        if (emptyStartPanel == null) return;
        emptyStartPanel.Visibility =
            timeline.Clips.Count == 0 && !IsInlineRecorderVisible()
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void ConfigureUsabilityHints()
    {
        btnScreenRec.ToolTip = "Open the screen recording scene. Use Add Hide Block or Camera Recorder before Start.";
        btnRecord.ToolTip = "Record from your camera by itself, or add a camera layer when Screen Recorder is open.";
        addBlockOverlayBtn.ToolTip = "Add a draggable hide block to the current video or screen recording scene.";
        deleteBlockBtn.ToolTip = "Delete the selected hide block.";
        btnAddText.ToolTip = "Select a clip, then add text that can be edited on the timeline.";
        btnAiCaptions.ToolTip = "Generate editable caption text overlays from the project audio.";
        splitBtn.ToolTip = "Split the selected/current clip at the playhead.";
        saveBtn.ToolTip = "Export the whole timeline.";
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
            // Show resize handles as soon as a clip is loaded - even before the user
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
        if (clip.HasManualCanvasCrop)
        {
            var rect = BuildCanvasCropClipRect(clip);
            videoView.Clip = rect.IsEmpty ? null : new RectangleGeometry(rect);
        }
        else
        {
            videoView.Clip = null;
        }

        double cx = videoView.ActualWidth / 2, cy = videoView.ActualHeight / 2;
        var tg = new TransformGroup();
        // 1) flip around centre
        double sx = clip.FlipH ? -1 : 1;
        double sy = clip.FlipV ? -1 : 1;
        // 2) user canvas zoom multiplies the flip scale
        sx *= clip.CanvasScale * clip.CanvasScaleX;
        sy *= clip.CanvasScale * clip.CanvasScaleY;
        tg.Children.Add(new ScaleTransform(sx, sy, cx, cy));
        if (clip.RotateDegrees != 0)
            tg.Children.Add(new RotateTransform(clip.RotateDegrees, cx, cy));
        // 3) user canvas offset, expressed as a fraction of canvas size
        if (Math.Abs(clip.CanvasOffsetX) > 0.001 || Math.Abs(clip.CanvasOffsetY) > 0.001)
        {
            tg.Children.Add(new TranslateTransform(
                clip.CanvasOffsetX * videoStack.ActualWidth,
                clip.CanvasOffsetY * videoStack.ActualHeight));
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
            // Source change is unavoidable heavy work - do it once, not coalesced.
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
        // Block + text overlay visibility tracks the playhead in real time, paused or not -
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
        // Video dimensions are first reliable here - RepositionCanvasHandles needs them to
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

        // If the playhead is parked at the end of the timeline (i.e. playback finished
        // and the user is pressing Play again), rewind to the first clip and play from
        // the start. Otherwise pressing Play would do nothing because the player is
        // already past the last clip's OutPoint.
        if (timeline.CurrentSeconds >= timeline.TotalSeconds - 0.1 && timeline.TotalSeconds > 0)
        {
            var first = timeline.Clips.OrderBy(c => c.TimelineStart).First();
            timeline.SetCurrent(first.TimelineStart);
            LoadClipForPreview(first, 0);
        }
        else if (_playingClip == null)
        {
            LoadClipForPreview(timeline.Clips[0], 0);
        }

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
        var c = RequireCurrentClip("Extract Audio");
        if (c == null) return;
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
        var targetCanvas = ActiveOverlayCanvas();
        var canvasW = targetCanvas.ActualWidth > 1 ? targetCanvas.ActualWidth : Math.Max(320, videoContainer.ActualWidth);
        var canvasH = targetCanvas.ActualHeight > 1 ? targetCanvas.ActualHeight : Math.Max(180, videoContainer.ActualHeight);
        var blockW = Math.Min(200, Math.Max(80, canvasW * 0.35));
        var blockH = Math.Min(120, Math.Max(60, canvasH * 0.25));
        var b = new VideoBlock
        {
            X = Math.Max(0, canvasW / 2 - blockW / 2),
            Y = Math.Max(0, canvasH / 2 - blockH / 2),
            Width = blockW, Height = blockH,
            StartSeconds = 0, EndSeconds = timeline.TotalSeconds, CoversWholeVideo = true,
            Color = Colors.Black, Mode = BlockMode.Solid,
            Label = $"Block {timeline.Blocks.Count + GetInlineRecorderBlocks().Count + 1}"
        };
        if (!IsInlineRecorderVisible()) timeline.Blocks.Add(b);
        var ctl = new ResizableBlock(b);
        WireResizableBlock(ctl);
        targetCanvas.Children.Add(ctl);
        _blockControls[b] = ctl;
        SelectBlock(b);
        UpdateStats();
        if (IsInlineRecorderVisible() && !_inlineRecorderCameraOnly) RebuildInlineRecorderTabs();
        status.Text = timeline.Clips.Count == 0
            ? (VideoEditor.Services.Localization.IsHebrew ? "בלוק הסתרה נוסף. הוסף וידאו לפני ייצוא." : "Hide block added. Add a video before export.")
            : (VideoEditor.Services.Localization.IsHebrew ? "בלוק הסתרה נוסף." : "Hide block added.");
    }

    private void OverlayCanvas_BackgroundClick(object sender, MouseButtonEventArgs e)
    {
        // OBS waits until mouse release to decide whether an empty preview click
        // clears selection or a drag marquee changes it. PreviewCanvas_MouseUp
        // owns that decision; clearing here on MouseDown makes tiny drags feel wrong.
    }

    private bool IsInlineRecorderVisible() => screenRecorderPanel?.Visibility == Visibility.Visible;

    private Canvas ActiveOverlayCanvas() =>
        IsInlineRecorderVisible() && inlineRecorderOverlayCanvas != null ? inlineRecorderOverlayCanvas : overlayCanvas;

    private void DeleteBlock_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedBlock == null) return;
        DeleteBlock(_selectedBlock);
    }

    private void DeleteBlock(VideoBlock block)
    {
        bool wasInlineBlock = _blockControls.TryGetValue(block, out var ctlPeek)
                              && ReferenceEquals(ctlPeek.Parent, inlineRecorderOverlayCanvas);
        if (_blockControls.TryGetValue(block, out var ctl))
        {
            if (ctl.Parent is Canvas parentCanvas) parentCanvas.Children.Remove(ctl);
            _blockControls.Remove(block);
        }
        timeline.Blocks.Remove(block);
        _selectedBlock = null;
        blockPanel.Visibility = Visibility.Collapsed;
        if (wasInlineBlock && IsInlineRecorderVisible() && !_inlineRecorderCameraOnly) RebuildInlineRecorderTabs();
        UpdateBlockVisibility();
        UpdatePreviewOverlayGroupBox();
    }

    // ===== Selection =====

    private void SelectBlock(VideoBlock? b)
    {
        _selectedBlock = b;
        if (b != null) _selectedClip = null;
        foreach (var kv in _blockControls) kv.Value.SetSelected(kv.Key == b);
        timeline.SelectBlock(b);
        ShowInspectorTab(b != null ? "block" : (_selectedClip != null ? "clip" : (IsInlineRecorderVisible() ? "recorder" : "none")));
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
        ShowInspectorTab(c != null ? "clip" : (_selectedBlock != null ? "block" : (IsInlineRecorderVisible() ? "recorder" : "none")));
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
        if (_playingClip != c) ScrubToClipFrame(c, c.InPoint);
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
        _selectedClip.CanvasScaleX = 1.0;
        _selectedClip.CanvasScaleY = 1.0;
        _selectedClip.CanvasOffsetX = 0;
        _selectedClip.CanvasOffsetY = 0;
        _selectedClip.CanvasCropLeft = 0;
        _selectedClip.CanvasCropTop = 0;
        _selectedClip.CanvasCropRight = 0;
        _selectedClip.CanvasCropBottom = 0;
        _suppress = true;
        canvasScaleSlider.Value = 1.0;
        canvasOffsetXSlider.Value = 0;
        canvasOffsetYSlider.Value = 0;
        _suppress = false;
        UpdateCanvasLabels(_selectedClip);
        if (_playingClip == _selectedClip) ApplyClipTransform(_selectedClip);
    }

    // Show the relevant inspector panel.
    //   "none"      → tab row and all panels empty.
    //   "block"/"clip"/"export" → static inspector tab + matching panel.
    //   "recording" → recorderInspectorPanel with source/fps/output visible (Start/Stop).
    //   "camera"    → recorderInspectorPanel with camera controls visible.
    //   "multi"     → multi-select placeholder shows "N items selected".
    // In recorder mode the static tabs (Block/Clip/Export) stay hidden — the dynamic
    // recorderTabHost holds Recording / Camera / Block N tabs instead.
    private void ShowInspectorTab(string key)
    {
        // "recorder" is kept as an alias for "recording" — older code paths still call it.
        if (key == "recorder") key = "recording";

        bool isBlock       = key == "block";
        bool isClip        = key == "clip";
        bool isExp         = key == "export";
        bool isRecording   = key == "recording";
        bool isCamera      = key == "camera";
        bool isMulti       = key == "multi";
        bool isTts         = key == "tts";
        bool isMerge       = key == "merge";
        bool isAiCaptions  = key == "aiCaptions";
        bool isDownload    = key == "download";
        bool isRuler       = key == "ruler";
        bool inRecorder    = IsInlineRecorderVisible();

        // Content panels
        blockPanel.Visibility             = isBlock ? Visibility.Visible : Visibility.Collapsed;
        clipPanel.Visibility              = isClip  ? Visibility.Visible : Visibility.Collapsed;
        emptyInspector.Visibility         = isExp   ? Visibility.Visible : Visibility.Collapsed;
        recorderInspectorPanel.Visibility = (isRecording || isCamera) ? Visibility.Visible : Visibility.Collapsed;
        ttsInspectorPanel.Visibility      = isTts ? Visibility.Visible : Visibility.Collapsed;
        mergePanel.Visibility             = isMerge ? Visibility.Visible : Visibility.Collapsed;
        aiCaptionsPanel.Visibility        = isAiCaptions ? Visibility.Visible : Visibility.Collapsed;
        downloadPanel.Visibility          = isDownload   ? Visibility.Visible : Visibility.Collapsed;
        rulerInspectorPanel.Visibility    = isRuler      ? Visibility.Visible : Visibility.Collapsed;
        if (!isTts) StopTtsPreview();
        if (multiSelectInspector != null)
        {
            multiSelectInspector.Visibility = isMulti ? Visibility.Visible : Visibility.Collapsed;
            if (isMulti && multiSelectCountText != null)
                multiSelectCountText.Text = $"{timeline.TotalSelectionCount} items selected";
        }

        // Sub-sections inside the recorder panel
        inlineRecorderSourcePanel.Visibility = isRecording ? Visibility.Visible : Visibility.Collapsed;
        inlineRecorderFpsPanel.Visibility    = isRecording ? Visibility.Visible : Visibility.Collapsed;
        inlineRecorderOutputPanel.Visibility = isRecording ? Visibility.Visible : Visibility.Collapsed;
        inlineRecorderCameraPanel.Visibility = isCamera    ? Visibility.Visible : Visibility.Collapsed;

        _suppress = true;
        // Static tabs are only used outside recorder mode.
        tabBlockBtn.IsChecked       = !inRecorder && isBlock;
        tabClipBtn.IsChecked        = !inRecorder && isClip;
        tabExportBtn.IsChecked      = !inRecorder && isExp;
        tabTtsBtn.IsChecked         = isTts;
        tabMergeBtn.IsChecked       = isMerge;
        tabAiCaptionsBtn.IsChecked  = isAiCaptions;
        tabDownloadBtn.IsChecked    = isDownload;
        tabRulerBtn.IsChecked       = isRuler;
        tabBlockBtn.Visibility       = (!inRecorder && isBlock) ? Visibility.Visible : Visibility.Collapsed;
        tabClipBtn.Visibility        = (!inRecorder && isClip)  ? Visibility.Visible : Visibility.Collapsed;
        tabExportBtn.Visibility      = (!inRecorder && isExp)   ? Visibility.Visible : Visibility.Collapsed;
        tabTtsBtn.Visibility         = isTts ? Visibility.Visible : Visibility.Collapsed;
        tabMergeBtn.Visibility       = isMerge ? Visibility.Visible : Visibility.Collapsed;
        tabAiCaptionsBtn.Visibility  = isAiCaptions ? Visibility.Visible : Visibility.Collapsed;
        tabDownloadBtn.Visibility    = isDownload ? Visibility.Visible : Visibility.Collapsed;
        tabRulerBtn.Visibility       = isRuler ? Visibility.Visible : Visibility.Collapsed;
        normalTabsBar.Visibility = (!inRecorder && (isBlock || isClip || isExp))
            ? Visibility.Visible : Visibility.Collapsed;
        // The dynamic recorder tabs (Recording / Camera / Block N) are built by
        // RebuildRecorderTopLevelTabs. Here we just check the right one if applicable.
        if (inRecorder) MarkRecorderTopLevelTabChecked(key, isBlock ? _selectedBlock : null);
        // Whole tab bar shows iff at least one tab is visible. recorderTabHost manages its
        // own visibility in RebuildRecorderTopLevelTabs.
        UpdateInspectorTabsBarVisibility();
        if (tabBlockDot != null) tabBlockDot.Visibility = _selectedBlock != null ? Visibility.Visible : Visibility.Collapsed;
        if (tabClipDot != null)  tabClipDot.Visibility  = _selectedClip != null ? Visibility.Visible : Visibility.Collapsed;
        if (tabRulerDot != null) tabRulerDot.Visibility = _measurementRulers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        tabClipBtn.Visibility = _selectedClip != null ? Visibility.Visible : Visibility.Collapsed;
        if (isRuler) RefreshRulerInspector();
        _suppress = false;
    }

    // Outer inspectorTabsBar is collapsed when neither static tabs nor recorder tabs show.
    private void UpdateInspectorTabsBarVisibility()
    {
        bool anyVisible = normalTabsBar.Visibility == Visibility.Visible
                          || recorderTabHost.Visibility == Visibility.Visible
                          || tabTtsBtn.Visibility == Visibility.Visible
                          || tabMergeBtn.Visibility == Visibility.Visible
                          || tabAiCaptionsBtn.Visibility == Visibility.Visible
                          || tabDownloadBtn.Visibility == Visibility.Visible
                          || tabRulerBtn.Visibility == Visibility.Visible;
        inspectorTabsBar.Visibility = anyVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    // Find and check the dynamic RadioButton matching the current panel.
    private void MarkRecorderTopLevelTabChecked(string key, VideoBlock? selectedBlock)
    {
        foreach (var tab in _inlineRecorderTabList)
        {
            if (tab.Button == null) continue;
            bool match = (key == "recording" && tab.Kind == InlineTabKind.Recording)
                      || (key == "camera"    && tab.Kind == InlineTabKind.Camera)
                      || (key == "block"     && tab.Kind == InlineTabKind.Block && ReferenceEquals(tab.Block, selectedBlock));
            tab.Button.IsChecked = match;
        }
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
    private void TabTts_Click(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        ShowInspectorTab("tts");
    }
    private void TabAiCaptions_Click(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        ShowInspectorTab("aiCaptions");
    }
    private void TabDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        ShowInspectorTab("download");
    }

    // Recording is in progress — hide the inspector tabs and panel so the preview gets full
    // width. The only way to bring controls back is to click the centre preview.
    private void CollapseRecorderTab()
    {
        _suppress = true;
        foreach (var tab in _inlineRecorderTabList)
            if (tab.Button != null) tab.Button.IsChecked = false;
        recorderInspectorPanel.Visibility = Visibility.Collapsed;
        recorderTabHost.Visibility = Visibility.Collapsed;
        normalTabsBar.Visibility = Visibility.Collapsed;
        UpdateInspectorTabsBarVisibility();
        _suppress = false;
    }

    // Click on the central recording preview — whenever recorder mode is open (whether or
    // not recording has started), bring the Recorder tab back. Selecting a clip/block
    // switches the inspector away, so this is the way back.
    private void RecorderPreview_Clicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!IsInlineRecorderVisible()) return;
        if (recorderInspectorPanel.Visibility == Visibility.Visible
            && recorderTabHost.Visibility == Visibility.Visible) return;
        recorderTabHost.Visibility = _inlineRecorderTabList.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowInspectorTab("recording");
        HideRecordingHint();
        e.Handled = true;
    }

    // Top status hint shown while recording with the tab closed. The previous status text
    // is saved so HideRecordingHint can restore it.
    private string? _savedStatusBeforeHint;
    private void ShowRecordingHint()
    {
        if (_savedStatusBeforeHint == null) _savedStatusBeforeHint = status.Text;
        status.Text = VideoEditor.Services.Localization.T(
            "Click the preview to open the recorder");
    }
    private void HideRecordingHint()
    {
        if (_savedStatusBeforeHint == null) return;
        status.Text = _savedStatusBeforeHint;
        _savedStatusBeforeHint = null;
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
        WireResizableBlock(ctl);
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
        // Parse both first, then clamp against each other -” otherwise we'd clamp In against
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

        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None)
        {
            SetPreviewViewPanMode(true);
            e.Handled = true;
            return;
        }

        if (_previewViewPanMode)
        {
            if (e.Key == Key.Add || e.Key == Key.OemPlus)
            {
                AdjustPreviewViewZoomLevel(1);
                _previewViewPanMoved = true;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Subtract || e.Key == Key.OemMinus)
            {
                AdjustPreviewViewZoomLevel(-1);
                _previewViewPanMoved = true;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.D0 || e.Key == Key.NumPad0)
            {
                ResetPreviewViewPan();
                _previewViewPanMoved = true;
                status.Text = "Preview view reset.";
                e.Handled = true;
                return;
            }
        }

        if (TryHandlePreviewNudge(e))
            return;

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
        if (e.Key == Key.Z && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            UndoRedo_Redo();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            UndoRedo_Undo();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
        {
            UndoRedo_Redo();
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
            if (DeleteAllSelected()) e.Handled = true;
        }
    }

    private void MainWindow_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBoxBase) return;
        if (e.Key != Key.Space || !_previewViewPanMode) return;

        bool shouldTogglePlayback = !_previewViewPanMoved;
        SetPreviewViewPanMode(false);
        _previewViewPanMoved = false;
        if (shouldTogglePlayback)
        {
            if (_isPlaying) PauseBtn_Click(this, new RoutedEventArgs());
            else PlayBtn_Click(this, new RoutedEventArgs());
        }
        e.Handled = true;
    }

    private bool TryHandlePreviewNudge(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is not (Key.Left or Key.Right or Key.Up or Key.Down))
            return false;

        var modifiers = Keyboard.Modifiers;
        if ((modifiers & ~(ModifierKeys.Shift | ModifierKeys.Control)) != 0)
            return false;

        double step = (modifiers & ModifierKeys.Shift) != 0 ? 10.0 : 1.0;
        double dx = key == Key.Left ? -step : key == Key.Right ? step : 0;
        double dy = key == Key.Up ? -step : key == Key.Down ? step : 0;

        if (PreviewOverlaySelectionCount() > 0)
        {
            ApplyPreviewOverlayGroupDelta(dx, dy);
            e.Handled = true;
            return true;
        }

        var clip = _selectedClip != null && !_selectedClip.IsAudioOnly ? _selectedClip : null;
        if (clip == null || videoStack.ActualWidth < 1 || videoStack.ActualHeight < 1)
            return false;

        clip.CanvasOffsetX += dx / videoStack.ActualWidth;
        clip.CanvasOffsetY += dy / videoStack.ActualHeight;
        ApplyClipTransform(clip);
        UpdateInspectorCanvasFields();
        status.Text = $"Nudged canvas {step:0}px.";
        e.Handled = true;
        return true;
    }

    // Deletes/mutes every currently-selected item (single or multi). Each item is routed
    // through the existing per-type handling so side effects (block visual cleanup,
    // stopping video preview on a playing clip, attached-audio mute vs. audio-only remove)
    // match what the user already gets in single-select.
    private bool DeleteAllSelected()
    {
        if (timeline.TotalSelectionCount == 0) return false;
        int muted = 0, deleted = 0;
        // Snapshot before mutation - the sets get cleared at the end.
        var audios = timeline.SelectedAudios.ToList();
        var clips  = timeline.SelectedClips.ToList();
        var blocks = timeline.SelectedBlocks.ToList();
        var texts  = timeline.SelectedTextOverlays.ToList();
        foreach (var a in audios)
        {
            if (a.IsAudioOnly) { timeline.Clips.Remove(a); deleted++; }
            else { a.Volume = 0; muted++; }
        }
        foreach (var c in clips)
        {
            if (_playingClip == c) { _playingClip = null; videoView.Stop(); }
            timeline.Clips.Remove(c);
            deleted++;
        }
        foreach (var b in blocks)
        {
            if (_blockControls.TryGetValue(b, out var ctl))
            {
                if (ctl.Parent is Canvas parentCanvas) parentCanvas.Children.Remove(ctl);
                _blockControls.Remove(b);
            }
            timeline.Blocks.Remove(b);
            deleted++;
        }
        foreach (var o in texts) { timeline.TextOverlays.Remove(o); deleted++; }
        _selectedBlock = null;
        _selectedClip = null;
        _selectedAudio = null;
        timeline.ClearAllSelection();
        if (deleted > 0 && muted > 0) status.Text = $"Deleted {deleted}, muted {muted}";
        else if (deleted > 0) status.Text = $"Deleted {deleted} item{(deleted == 1 ? "" : "s")}";
        else if (muted > 0) status.Text = $"Muted {muted} audio track{(muted == 1 ? "" : "s")}";
        return true;
    }

    // Wires a ResizableBlock for Ctrl-aware selection. Ctrl is read synchronously from
    // Keyboard.Modifiers because Selected fires from MouseLeftButtonDown / DragStarted
    // while modifier state is still current.
    private void WireResizableBlock(VideoEditor.Controls.ResizableBlock ctl)
    {
        ctl.Selected += rb =>
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool preserveGroup = !ctrl && timeline.SelectedBlocks.Contains(rb.Model) && PreviewOverlaySelectionCount() > 1;
            if (!preserveGroup) timeline.HandleBlockClick(rb.Model, ctrl);
        };
        ctl.MoveRequested += (rb, dx, dy) =>
        {
            if (!timeline.SelectedBlocks.Contains(rb.Model) || PreviewOverlaySelectionCount() <= 1)
                return false;

            ApplyPreviewOverlayGroupDelta(dx, dy);
            return true;
        };
        ctl.MoveCompleted += _ => HideCanvasSnapGuides();
        ctl.Changed += _ => SyncBlockInspector();
        ctl.ClickedWithoutDrag += (rb, wasSelectedAtMouseDown) =>
        {
            if (!wasSelectedAtMouseDown || (Keyboard.Modifiers & ModifierKeys.Control) != 0)
                return;

            SelectPreviewItemBelow(Mouse.GetPosition(overlayCanvas), rb.Model);
        };
        ctl.MouseRightButtonUp += ResizableBlock_MouseRightButtonUp;
    }

    private void ResizableBlock_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not VideoEditor.Controls.ResizableBlock rb) return;
        SelectBlock(rb.Model);

        var menu = new ContextMenu();
        var cover = new MenuItem { Header = "Cover whole video" };
        cover.Click += (_, _) =>
        {
            rb.Model.CoversWholeVideo = true;
            rb.Model.StartSeconds = 0;
            rb.Model.EndSeconds = timeline.TotalSeconds;
            SelectBlock(rb.Model);
            status.Text = "Hide block covers the whole video.";
        };
        menu.Items.Add(cover);
        var del = new MenuItem { Header = "Delete hide block" };
        del.Click += (_, _) =>
        {
            DeleteBlock(rb.Model);
            status.Text = "Hide block deleted.";
        };
        menu.Items.Add(del);
        rb.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    // Routes timeline selection changes to the inspector and to ResizableBlock outlines
    // on the preview canvas. Single-selection delegates to the existing per-type
    // SelectBlock/SelectClip; multi-selection shows the "N items selected" panel.
    private void OnTimelineSelectionChanged()
    {
        foreach (var kv in _blockControls)
            kv.Value.SetSelected(timeline.SelectedBlocks.Contains(kv.Key));
        RefreshTextOverlayPreviewSelectionVisuals();
        UpdatePreviewOverlayGroupBox();

        int count = timeline.TotalSelectionCount;
        if (count > 1) { ShowInspectorTab("multi"); UpdateBlockVisibility(); return; }
        if (count == 0)
        {
            _selectedBlock = null; _selectedClip = null; _selectedAudio = null;
            ShowInspectorTab("export");
            UpdateBlockVisibility();
            return;
        }
        if (timeline.SelectedBlocks.Count == 1)
        {
            var b = timeline.SelectedBlocks.First();
            if (_selectedBlock != b) SelectBlock(b);
        }
        else if (timeline.SelectedClips.Count == 1)
        {
            var c = timeline.SelectedClips.First();
            if (_selectedClip != c) SelectClip(c);
        }
        else if (timeline.SelectedAudios.Count == 1)
        {
            var c = timeline.SelectedAudios.First();
            if (_selectedAudio != c) OnAudioSelected(c);
        }
        else if (timeline.SelectedTextOverlays.Count == 1)
        {
            ShowInspectorTab("export");
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
                CanvasScale = c.CanvasScale,
                CanvasScaleX = c.CanvasScaleX,
                CanvasScaleY = c.CanvasScaleY,
                CanvasOffsetX = c.CanvasOffsetX,
                CanvasOffsetY = c.CanvasOffsetY,
                CanvasCropLeft = c.CanvasCropLeft,
                CanvasCropTop = c.CanvasCropTop,
                CanvasCropRight = c.CanvasCropRight,
                CanvasCropBottom = c.CanvasCropBottom,
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
            WireResizableBlock(ctl);
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
            // visible even outside its range - otherwise it'd disappear the moment you
            // selected it to drag/resize.
            bool inRange = block.CoversWholeVideo || (t >= block.StartSeconds && t <= block.EndSeconds);
            bool isEditing = block == _selectedBlock || timeline.SelectedBlocks.Contains(block);
            ctl.Visibility = (inRange || isEditing) ? Visibility.Visible : Visibility.Hidden;
        }
        UpdateTextOverlaysVisibility(t);
        UpdatePreviewOverlayGroupBox();
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
            // Per-tick we only flip Visibility - the cheap part. Style + placement
            // are no-ops unless the overlay was just added or marked dirty.
            bool active = !_isPlaying || (currentSec >= ov.StartSeconds && currentSec <= ov.EndSeconds);
            ctl!.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        }
        RefreshTextOverlayPreviewSelectionVisuals();
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
            IsHitTestVisible = true,
            Cursor = Cursors.SizeAll,
            Tag = ov,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(2)
        };
        border.MouseLeftButtonDown += TextOverlayPreview_MouseDown;
        border.MouseMove += TextOverlayPreview_MouseMove;
        border.MouseLeftButtonUp += TextOverlayPreview_MouseUp;
        border.MouseRightButtonUp += TextOverlayPreview_MouseRightButtonUp;
        ApplyOverlayStyle(border, ov, 1.0);
        return border;
    }

    private void TextOverlayPreview_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not TextOverlay ov) return;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (e.ClickCount >= 2)
        {
            timeline.HandleTextOverlayClick(ov, false);
            _selectedBlock = null;
            _selectedClip = null;
            RefreshTextOverlayPreviewSelectionVisuals();
            OnTextOverlayContext(ov, "edit");
            e.Handled = true;
            return;
        }

        bool preserveGroup = !ctrl && timeline.SelectedTextOverlays.Contains(ov) && PreviewOverlaySelectionCount() > 1;
        bool wasSelectedAtMouseDown = timeline.SelectedTextOverlays.Contains(ov);
        if (!preserveGroup) timeline.HandleTextOverlayClick(ov, ctrl);
        _selectedBlock = null;
        _selectedClip = null;
        _previewOverlayDragActive = true;
        _previewOverlayDragMoved = false;
        _previewOverlayDragLast = _previewOverlayDragStart = e.GetPosition(overlayCanvas);
        _previewOverlayClickThroughItem = ov;
        _previewOverlayClickThroughCandidate = wasSelectedAtMouseDown && !ctrl;
        border.CaptureMouse();
        RefreshTextOverlayPreviewSelectionVisuals();
        status.Text = ctrl
            ? $"Text overlay selection: {timeline.SelectedTextOverlays.Count}"
            : "Text overlay selected.";
        e.Handled = true;
    }

    private void TextOverlayPreview_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_previewOverlayDragActive || sender is not Border border || !border.IsMouseCaptured)
            return;

        var p = e.GetPosition(overlayCanvas);
        if (!_previewOverlayDragMoved)
        {
            double dxFromStart = p.X - _previewOverlayDragStart.X;
            double dyFromStart = p.Y - _previewOverlayDragStart.Y;
            double thresholdX = Math.Max(2.0, SystemParameters.MinimumHorizontalDragDistance);
            double thresholdY = Math.Max(2.0, SystemParameters.MinimumVerticalDragDistance);
            if (Math.Abs(dxFromStart) < thresholdX && Math.Abs(dyFromStart) < thresholdY)
                return;

            _previewOverlayDragMoved = true;
            _previewOverlayDragLast = p;
            e.Handled = true;
            return;
        }

        var delta = p - _previewOverlayDragLast;
        if (Math.Abs(delta.X) < 0.01 && Math.Abs(delta.Y) < 0.01)
            return;

        ApplyPreviewOverlayGroupDelta(delta.X, delta.Y);
        _previewOverlayDragLast = p;
        e.Handled = true;
    }

    private void TextOverlayPreview_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.IsMouseCaptured)
            border.ReleaseMouseCapture();
        bool clickThrough = _previewOverlayClickThroughCandidate && !_previewOverlayDragMoved && _previewOverlayClickThroughItem != null;
        var clickItem = _previewOverlayClickThroughItem;
        var clickPoint = e.GetPosition(overlayCanvas);
        _previewOverlayDragActive = false;
        _previewOverlayDragMoved = false;
        _previewOverlayClickThroughCandidate = false;
        _previewOverlayClickThroughItem = null;
        HideCanvasSnapGuides();
        if (clickThrough)
            SelectPreviewItemBelow(clickPoint, clickItem);
        e.Handled = true;
    }

    private void TextOverlayPreview_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not TextOverlay ov) return;

        timeline.HandleTextOverlayClick(ov, false);
        _selectedBlock = null;
        _selectedClip = null;
        RefreshTextOverlayPreviewSelectionVisuals();

        var menu = new ContextMenu();
        var edit = new MenuItem { Header = "Edit text..." };
        edit.Click += (_, _) => OnTextOverlayContext(ov, "edit");
        menu.Items.Add(edit);
        var del = new MenuItem { Header = "Delete text" };
        del.Click += (_, _) => OnTextOverlayContext(ov, "delete");
        menu.Items.Add(del);
        border.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private int PreviewOverlaySelectionCount() => timeline.SelectedBlocks.Count + timeline.SelectedTextOverlays.Count;

    private double GetTextOverlayPreviewScale()
    {
        var firstVideo = timeline.Clips.FirstOrDefault(c => !c.IsAudioOnly);
        if (firstVideo == null || firstVideo.VideoWidth <= 0 || firstVideo.VideoHeight <= 0)
            return 1.0;

        double canvasW = Math.Max(1, overlayCanvas.ActualWidth);
        double canvasH = Math.Max(1, overlayCanvas.ActualHeight);
        return Math.Min(canvasW / firstVideo.VideoWidth, canvasH / firstVideo.VideoHeight);
    }

    private void ApplyPreviewOverlayGroupDelta(double dx, double dy)
    {
        if (overlayCanvas == null) return;
        var selectedBlocks = timeline.SelectedBlocks.ToList();
        var selectedTexts = timeline.SelectedTextOverlays.ToList();
        if (selectedBlocks.Count + selectedTexts.Count == 0) return;

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;

        foreach (var b in selectedBlocks)
        {
            minX = Math.Min(minX, b.X);
            minY = Math.Min(minY, b.Y);
            maxX = Math.Max(maxX, b.X + b.Width);
            maxY = Math.Max(maxY, b.Y + b.Height);
        }

        foreach (var ov in selectedTexts)
        {
            if (!_textOverlayPreviewControls.TryGetValue(ov, out var ctl)) continue;
            var rect = PreviewElementRect(ctl);
            minX = Math.Min(minX, rect.Left);
            minY = Math.Min(minY, rect.Top);
            maxX = Math.Max(maxX, rect.Right);
            maxY = Math.Max(maxY, rect.Bottom);
        }

        if (double.IsInfinity(minX)) return;
        var movingBounds = new Rect(minX + dx, minY + dy, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
        var snap = GetPreviewOverlaySnapOffset(movingBounds);
        dx += snap.X;
        dy += snap.Y;
        movingBounds.Offset(snap);
        UpdatePreviewOverlaySnapGuides(movingBounds, snap);

        if (minX + dx < 0) dx = -minX;
        if (minY + dy < 0) dy = -minY;
        if (maxX + dx > overlayCanvas.ActualWidth) dx = overlayCanvas.ActualWidth - maxX;
        if (maxY + dy > overlayCanvas.ActualHeight) dy = overlayCanvas.ActualHeight - maxY;

        foreach (var b in selectedBlocks)
        {
            b.X += dx;
            b.Y += dy;
        }

        double scale = GetTextOverlayPreviewScale();
        foreach (var ov in selectedTexts)
        {
            ov.X = (int)Math.Round(ov.X + dx / scale);
            ov.Y = (int)Math.Round(ov.Y + dy / scale);
            if (_textOverlayPreviewControls.TryGetValue(ov, out var ctl))
                ApplyOverlayPlacement(ctl, ov, scale);
            _textOverlayDirty.Add(ov);
        }

        SyncBlockInspector();
        RefreshTextOverlayPreviewSelectionVisuals();
        UpdatePreviewOverlayGroupBox();
        int movedCount = selectedBlocks.Count + selectedTexts.Count;
        status.Text = movedCount > 1
            ? $"Moved {movedCount} preview items."
            : selectedBlocks.Count == 1 ? "Moved hide block." : "Moved text overlay.";
    }

    private void RefreshTextOverlayPreviewSelectionVisuals()
    {
        foreach (var kv in _textOverlayPreviewControls)
        {
            var selected = timeline.SelectedTextOverlays.Contains(kv.Key);
            kv.Value.BorderBrush = selected ? (Brush)FindResource("AccentHover") : Brushes.Transparent;
        }
        UpdatePreviewOverlayGroupBox();
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
            CanvasScale = clip.CanvasScale,
            CanvasScaleX = clip.CanvasScaleX,
            CanvasScaleY = clip.CanvasScaleY,
            CanvasOffsetX = clip.CanvasOffsetX,
            CanvasOffsetY = clip.CanvasOffsetY,
            CanvasCropLeft = clip.CanvasCropLeft,
            CanvasCropTop = clip.CanvasCropTop,
            CanvasCropRight = clip.CanvasCropRight,
            CanvasCropBottom = clip.CanvasCropBottom,
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
                CanvasScale = clip.CanvasScale,
                CanvasScaleX = clip.CanvasScaleX,
                CanvasScaleY = clip.CanvasScaleY,
                CanvasOffsetX = clip.CanvasOffsetX,
                CanvasOffsetY = clip.CanvasOffsetY,
                CanvasCropLeft = clip.CanvasCropLeft,
                CanvasCropTop = clip.CanvasCropTop,
                CanvasCropRight = clip.CanvasCropRight,
                CanvasCropBottom = clip.CanvasCropBottom,
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
            CanvasScale = clip.CanvasScale,
            CanvasScaleX = clip.CanvasScaleX,
            CanvasScaleY = clip.CanvasScaleY,
            CanvasOffsetX = clip.CanvasOffsetX,
            CanvasOffsetY = clip.CanvasOffsetY,
            CanvasCropLeft = clip.CanvasCropLeft,
            CanvasCropTop = clip.CanvasCropTop,
            CanvasCropRight = clip.CanvasCropRight,
            CanvasCropBottom = clip.CanvasCropBottom,
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
        var destinationDialog = new ExportDestinationWindow { Owner = this };
        if (destinationDialog.ShowDialog() != true) return;

        var publishAfterExport = destinationDialog.Destination == ExportDestination.Publish;
        string outputPath;
        if (publishAfterExport)
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "VideoEditorExports");
            try { Directory.CreateDirectory(folder); }
            catch (Exception ex)
            {
                MessageBox.Show("Cannot create export folder: " + ex.Message, "Export",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            outputPath = Path.Combine(folder, $"project_export_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}");
        }
        else
        {
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
            outputPath = sfd.FileName;
        }

        status.Text = "Exporting...";
        progress.Value = 0;
        progressLabel.Text = "0 %";
        // Stream both the bar value and the readable percent label from the export pipeline.
        var prog = new Progress<double>(v => Dispatcher.Invoke(() =>
        {
            progress.Value = v;
            progressLabel.Text = ((int)Math.Round(Math.Max(0, Math.Min(1, v)) * 100)) + " %";
        }));
        try
        {
            // Export in timeline order
            var orderedClips = timeline.Clips.OrderBy(c => c.TimelineStart).ToList();
            var firstVideo = orderedClips.FirstOrDefault(c => !c.IsAudioOnly);
            var (tW, tH) = VideoEditor.Services.ProjectFormats.Resolve(AppSettings.TargetFormatPreset, firstVideo);
            await _ff.ExportProjectAsync(orderedClips, timeline.Blocks.ToList(),
                tW, tH,
                overlayCanvas.ActualWidth, overlayCanvas.ActualHeight,
                timeline.TotalSeconds, outputPath, AppSettings.TargetFitMode,
                timeline.TextOverlays.ToList(), prog);
            status.Text = "Exported: " + outputPath;
            progress.Value = 1;
            progressLabel.Text = "Done";
            if (!publishAfterExport)
            {
                MessageBox.Show("Export saved:\n" + outputPath, "Saved to files",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            // Show the unified share / upload dialog. offerOpenInEditor=false because
            // the exported file IS the final output - re-loading it as a new clip
            // doesn't make sense here (unlike a screen recording).
            new ShareDialog(outputPath,
                title: publishAfterExport ? "Ready to publish" : "Saved to files",
                subtitle: "Pick where to send it - your editor project is still open.",
                offerOpenInEditor: false)
            { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            status.Text = "Export failed: " + ex.Message;
            progressLabel.Text = "Failed";
            MessageBox.Show(ex.Message, "Export Error");
        }
    }

    // ===== Quick Tool buttons (operate on selected clip; default to first if none) =====

    private VideoClip? CurrentClip() => _selectedClip ?? _playingClip ?? (timeline.Clips.Count > 0 ? timeline.Clips[0] : null);

    private VideoClip? RequireCurrentClip(string action)
    {
        var clip = CurrentClip();
        if (clip != null) return clip;

        status.Text = $"Add or select a video clip before using {action}.";
        MessageBox.Show($"Add or select a video clip before using {action}.", action,
            MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }

    private void ScreenRec_Click(object s, RoutedEventArgs e) => ShowInlineScreenRecorder();
    private void OpenScreenRecorder(bool webcam)
    {
        var dlg = new ScreenRecorderWindow(_ff, webcam) { Owner = this };
        dlg.ShowDialog();
        // "Open in editor" path - user chose to keep editing the recording.
        if (!string.IsNullOrEmpty(dlg.OpenInEditorPath) && File.Exists(dlg.OpenInEditorPath))
            AddFiles(new[] { dlg.OpenInEditorPath });
    }
    private void ShowInlineScreenRecorder()
    {
        _inlineRecorderCameraOnly = false;
        ClearInlineRecorderVisibleLog();
        screenRecorderPanel.Visibility = Visibility.Visible;
        inlineRecorderTitle.Text = "Screen Recorder";
        inlineRecorderStatus.Text = "Choose a source, record, then edit the result on the timeline.";
        ShowInspectorTab("recorder");
        HideRecordingHint();
        RebuildInlineRecorderTabs();
        UpdateEmptyStartPanel();
        if (string.IsNullOrWhiteSpace(inlineRecorderPathBox.Text))
            inlineRecorderPathBox.Text = DefaultScreenRecordingPath();

        _inlineRecorderMonitors = MonitorInfo.EnumerateAll();
        inlineRecorderSourceBox.Items.Clear();
        inlineRecorderSourceBox.Items.Add("Entire desktop (all monitors)");
        foreach (var m in _inlineRecorderMonitors) inlineRecorderSourceBox.Items.Add(m.FriendlyName);
        int saved = AppSettings.LastScreenRecorderMonitor;
        inlineRecorderSourceBox.SelectedIndex = saved >= 0 && saved < _inlineRecorderMonitors.Count ? saved + 1 : 0;

        RefreshInlineRecorderDiag();
        CaptureInlineRecorderPreview();
        _inlineRecorderPreviewTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _inlineRecorderPreviewTimer.Tick -= InlineRecorderPreviewTimer_Tick;
        _inlineRecorderPreviewTimer.Tick += InlineRecorderPreviewTimer_Tick;
        _inlineRecorderPreviewTimer.Start();
    }

    private void ShowInlineCameraRecorder()
    {
        if (_inlineRecorderProc != null && !_inlineRecorderProc.HasExited)
        {
            MessageBox.Show("Stop the current recording first.");
            return;
        }

        var picker = new VideoRecorderPickerWindow(_ff, _inlineRecorderWebcamDeviceName) { Owner = this };
        if (picker.ShowDialog() != true) return;

        _inlineRecorderCameraOnly = true;
        _inlineRecorderWebcamDeviceName = picker.SelectedCameraName;
        ClearInlineRecorderVisibleLog();
        _inlineCameraBgReady = false;
        Interlocked.Exchange(ref _inlineCameraLastAiPreviewTicks, 0);
        ResetInlineCameraAiCounters();

        // Stop the screen-recorder timer and replace the preview source BEFORE the panel
        // becomes visible. Otherwise WPF renders one frame with whatever stale screen
        // capture was left behind by a previous Screen Recorder session, then swaps to
        // the camera - showing the user a brief flash of their desktop.
        _inlineRecorderPreviewTimer?.Stop();
        StopInlineRecorderWebcamPreview();
        ClearInlineRecorderWebcamLayer();
        inlineRecorderPreviewImage.Source = picker.LastPreviewFrame;

        screenRecorderPanel.Visibility = Visibility.Visible;
        inlineRecorderTitle.Text = "Camera Recorder";
        inlineRecorderStatus.Text = "";
        ShowInspectorTab("recorder");
        HideRecordingHint();
        ApplyInlineCameraOnlyPanelLayout();
        UpdateEmptyStartPanel();

        inlineRecorderSourceBox.Items.Clear();
        inlineRecorderSourceBox.Items.Add(_inlineRecorderWebcamDeviceName);
        inlineRecorderSourceBox.SelectedIndex = 0;
        inlineRecorderSourceBox.IsEnabled = false;
        _suppressBgComboChange = true;
        try
        {
            inlineCameraBgBox.Items.Clear();
            inlineCameraBgBox.Items.Add("Keep original background");
            inlineCameraBgBox.Items.Add("Blur the background");
            inlineCameraBgBox.Items.Add("Remove background (transparent)");
            inlineCameraBgBox.Items.Add("Replace with blue background");
            inlineCameraBgBox.SelectedIndex = 0;
        }
        finally
        {
            _suppressBgComboChange = false;
        }
        _inlineCameraBackground.Mode = CameraBackgroundMode.None;
        _inlineCameraBgReady = false;
        inlineRecorderFpsBox.Text = "30";
        inlineRecorderPathBox.Text = DefaultCameraRecordingPath();
        inlineRecorderStartBtn.Content = "Start";
        inlineRecorderStopBtn.Content = "Stop + Add";
        inlineRecorderStartBtn.IsEnabled = true;
        inlineRecorderStopBtn.IsEnabled = false;
        closeInlineRecorderBtn.IsEnabled = true;
        inlineRecorderDot.Fill = new SolidColorBrush(Color.FromRgb(0x8A, 0x91, 0xA6));
        inlineRecorderStatus.Text = $"Camera ready: {_inlineRecorderWebcamDeviceName}.";
        ResetInlineCameraDiagWindow();
        inlineRecorderDiagText.Text = "Camera preview starting...";
        AppendInlineRecorderLog($"Camera selected: {_inlineRecorderWebcamDeviceName}");
        StartInlineCameraDiagTimer();
        StartInlineCameraOnlyPreview();
    }

    private static string DefaultScreenRecordingPath()
    {
        var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        return Path.Combine(videos, $"screen_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
    }

    private static string DefaultCameraRecordingPath()
    {
        var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        return Path.Combine(videos, $"webcam_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
    }

    private void CloseInlineRecorder_Click(object sender, RoutedEventArgs e)
    {
        if (_inlineRecorderProc != null && !_inlineRecorderProc.HasExited)
        {
            MessageBox.Show("Stop the recording before closing the recorder.");
            return;
        }

        bool wasCameraOnly = _inlineRecorderCameraOnly;

        _inlineRecorderPreviewTimer?.Stop();
        StopInlineCameraDiagTimer();
        StopInlineRecorderWebcamPreview();
        ClearInlineRecorderWebcamLayer();
        // Drop the last captured frame so the next time the panel opens (camera or
        // screen) it doesn't briefly show a stale desktop/camera image from this session.
        inlineRecorderPreviewImage.Source = null;
        _inlineCameraPreviewRetryCount = 0;
        _inlineCameraPreviewRetryPending = false;
        _inlineRecorderCameraOnly = false;
        _inlineCameraRecordTempPath = null;
        _inlineCameraBackground.Mode = CameraBackgroundMode.None;
        _inlineCameraBgReady = false;
        _screenCamScreenTempPath = null;
        _screenCamRawTempPath = null;
        _screenCamFinalOutputPath = null;
        _screenCamPipRect = null;
        // Hide blocks added in recorder mode are scene-only; closing the recorder discards
        // them so they don't ghost into the timeline's block list. Also clears _selectedBlock
        // if it pointed to one of them — otherwise the inspector would re-open a Block tab
        // bound to a deleted control.
        foreach (var b in GetInlineRecorderBlocks().ToList())
        {
            if (_blockControls.TryGetValue(b, out var ctl))
            {
                inlineRecorderOverlayCanvas.Children.Remove(ctl);
                _blockControls.Remove(b);
            }
            if (ReferenceEquals(_selectedBlock, b)) _selectedBlock = null;
        }
        _inlineRecorderTabList.Clear();
        _activeInlineTab = null;
        recorderTabHost.Items.Clear();
        recorderTabHost.Visibility = Visibility.Collapsed;
        inlineRecorderSourcePanel.Visibility = Visibility.Visible;
        inlineRecorderCameraPanel.Visibility = Visibility.Collapsed;
        inlineRecorderFpsPanel.Visibility = Visibility.Visible;
        inlineRecorderOutputPanel.Visibility = Visibility.Visible;
        inlineRecorderSourceBox.IsEnabled = true;
        if (wasCameraOnly)
        {
            recorderInspectorPanel.Visibility = Visibility.Collapsed;
        }
        screenRecorderPanel.Visibility = Visibility.Collapsed;
        HideRecordingHint();
        // Returning to a non-recorder state: pick the inspector tab that matches the current
        // selection, otherwise leave the right panel empty.
        ShowInspectorTab(_selectedBlock != null ? "block" : (_selectedClip != null ? "clip" : "none"));
        UpdateEmptyStartPanel();
    }

    private void ClearInlineRecorderWebcamLayer()
    {
        if (_inlineRecorderWebcamControl != null)
        {
            inlineRecorderOverlayCanvas.Children.Remove(_inlineRecorderWebcamControl);
            _inlineRecorderWebcamControl = null;
        }
        _inlineRecorderWebcamBlock = null;
    }

    private void AddInlineRecorderWebcam()
    {
        ShowInlineScreenRecorder();
        if (_inlineRecorderWebcamControl != null)
        {
            status.Text = "Camera recorder is already in the screen recording scene.";
            return;
        }

        var picker = new VideoRecorderPickerWindow(_ff, _inlineRecorderWebcamDeviceName) { Owner = this };
        if (picker.ShowDialog() != true) return;
        _inlineRecorderWebcamDeviceName = picker.SelectedCameraName;

        double canvasW = inlineRecorderOverlayCanvas.ActualWidth > 1 ? inlineRecorderOverlayCanvas.ActualWidth : 640;
        double canvasH = inlineRecorderOverlayCanvas.ActualHeight > 1 ? inlineRecorderOverlayCanvas.ActualHeight : 360;
        double w = Math.Min(240, Math.Max(120, canvasW * 0.28));
        double h = w * 9 / 16;
        _inlineRecorderWebcamBlock = new VideoBlock
        {
            X = Math.Max(0, canvasW - w - 24),
            Y = Math.Max(0, canvasH - h - 24),
            Width = w,
            Height = h,
            Color = Color.FromRgb(0x25, 0x67, 0xFF),
            Mode = BlockMode.Solid,
            Label = "Camera Recorder"
        };
        _inlineRecorderWebcamControl = new ResizableBlock(_inlineRecorderWebcamBlock);
        _inlineRecorderWebcamControl.Changed += _ => { };
        inlineRecorderOverlayCanvas.Children.Add(_inlineRecorderWebcamControl);
        Panel.SetZIndex(_inlineRecorderWebcamControl, 200);

        if (picker.LastPreviewFrame != null)
            _inlineRecorderWebcamControl.SetLivePreviewSource(picker.LastPreviewFrame);

        _suppressBgComboChange = true;
        try
        {
            if (inlineCameraBgBox.Items.Count == 0)
            {
                inlineCameraBgBox.Items.Add("Keep original background");
                inlineCameraBgBox.Items.Add("Blur the background");
                inlineCameraBgBox.Items.Add("Remove background (transparent)");
                inlineCameraBgBox.Items.Add("Replace with blue background");
            }
            inlineCameraBgBox.SelectedIndex = 0;
        }
        finally
        {
            _suppressBgComboChange = false;
        }
        _inlineCameraBackground.Mode = CameraBackgroundMode.None;
        _inlineCameraBgReady = false;
        RebuildInlineRecorderTabs();

        inlineRecorderStatus.Text = $"Camera layer: {_inlineRecorderWebcamDeviceName}. The blue box marks where the camera will appear in the recording.";
        status.Text = $"Camera added: {_inlineRecorderWebcamDeviceName}. Drag or resize it before Start.";
        StartInlineRecorderWebcamPreview();
    }

    private void InlineRecorderSource_Changed(object sender, SelectionChangedEventArgs e)
    {
        RefreshInlineRecorderDiag();
        CaptureInlineRecorderPreview();
    }

    // Camera-only mode (user clicked Camera Record): no need for tabs — one combined panel.
    private void ApplyInlineCameraOnlyPanelLayout()
    {
        inlineRecorderSourcePanel.Visibility = Visibility.Visible;
        inlineRecorderCameraPanel.Visibility = Visibility.Visible;
        inlineRecorderFpsPanel.Visibility    = Visibility.Visible;
        inlineRecorderOutputPanel.Visibility = Visibility.Visible;
        inlineCameraRemoveBtn.Visibility     = Visibility.Collapsed;
        recorderInspectorPanel.Visibility    = Visibility.Visible;
        _inlineRecorderTabList.Clear();
        _activeInlineTab = null;
        recorderTabHost.Items.Clear();
        recorderTabHost.Visibility = Visibility.Collapsed;
        UpdateInspectorTabsBarVisibility();
    }

    // Rebuild the dynamic top-level tabs in recorderTabHost: Recording / Camera (if a camera
    // layer is in the scene) / Block N (one per recorder-scoped hide block). Old name kept
    // because there are existing call sites in AddBlock_Click / DeleteBlock_Click.
    private void RebuildInlineRecorderTabs()
    {
        if (_inlineRecorderCameraOnly)
        {
            ApplyInlineCameraOnlyPanelLayout();
            return;
        }

        // Remember what was active so we can re-select it after rebuild (e.g. Block N tab
        // that was just edited should stay selected even when a new block is added).
        var rememberedKind  = _activeInlineTab?.Kind;
        var rememberedBlock = _activeInlineTab?.Block;

        _inlineRecorderTabList.Clear();
        _inlineRecorderTabList.Add(new InlineRecorderTab { Kind = InlineTabKind.Recording, Title = "Recording" });
        if (_inlineRecorderWebcamBlock != null)
            _inlineRecorderTabList.Add(new InlineRecorderTab { Kind = InlineTabKind.Camera, Title = "Camera" });

        int n = 1;
        foreach (var b in GetInlineRecorderBlocks())
        {
            if (ReferenceEquals(b, _inlineRecorderWebcamBlock)) continue;
            _inlineRecorderTabList.Add(new InlineRecorderTab { Kind = InlineTabKind.Block, Title = $"Block {n++}", Block = b });
        }

        recorderTabHost.Items.Clear();
        foreach (var tab in _inlineRecorderTabList)
        {
            var rb = new RadioButton
            {
                Content = tab.Title,
                GroupName = "InspectorTabs",
                Style = (Style)FindResource("InspectorTab"),
                Margin = new Thickness(0, 0, 6, 6)
            };
            var tabRef = tab;
            rb.Click += (_, _) => ActivateRecorderTopLevelTab(tabRef);
            tab.Button = rb;
            recorderTabHost.Items.Add(rb);
        }
        recorderTabHost.Visibility = _inlineRecorderTabList.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateInspectorTabsBarVisibility();

        InlineRecorderTab? toActivate = null;
        if (rememberedBlock != null)
            toActivate = _inlineRecorderTabList.FirstOrDefault(t => t.Kind == InlineTabKind.Block && ReferenceEquals(t.Block, rememberedBlock));
        if (toActivate == null && rememberedKind.HasValue && rememberedKind.Value != InlineTabKind.Block)
            toActivate = _inlineRecorderTabList.FirstOrDefault(t => t.Kind == rememberedKind.Value);
        toActivate ??= _inlineRecorderTabList[0];
        ActivateRecorderTopLevelTab(toActivate);
    }

    // Click handler for the dynamic recorder tabs. Routes to the right ShowInspectorTab key
    // or, for Block tabs, to SelectBlock so the full blockPanel is shown.
    private void ActivateRecorderTopLevelTab(InlineRecorderTab tab)
    {
        _activeInlineTab = tab;
        switch (tab.Kind)
        {
            case InlineTabKind.Recording:
                ShowInspectorTab("recording");
                break;
            case InlineTabKind.Camera:
                inlineCameraDeviceLabel.Text = string.IsNullOrWhiteSpace(_inlineRecorderWebcamDeviceName)
                    ? ""
                    : $"Device: {_inlineRecorderWebcamDeviceName}";
                inlineCameraRemoveBtn.Visibility = Visibility.Visible;
                ShowInspectorTab("camera");
                break;
            case InlineTabKind.Block when tab.Block is not null:
                SelectBlock(tab.Block);
                break;
        }
    }

    private void InlineCameraRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_inlineRecorderProc != null && !_inlineRecorderProc.HasExited)
        {
            MessageBox.Show("Stop the recording before removing the camera.");
            return;
        }
        StopInlineRecorderWebcamPreview();
        ClearInlineRecorderWebcamLayer();
        _inlineCameraBackground.Mode = CameraBackgroundMode.None;
        _inlineCameraBgReady = false;
        inlineRecorderStatus.Text = "Camera removed from the scene.";
        RebuildInlineRecorderTabs();
    }

    private async void InlineCameraBg_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressBgComboChange) return;
        bool screenCamMode = !_inlineRecorderCameraOnly && _inlineRecorderWebcamBlock != null;
        if (!_inlineRecorderCameraOnly && !screenCamMode) return;

        _inlineCameraBackground.Mode = inlineCameraBgBox.SelectedIndex switch
        {
            1 => CameraBackgroundMode.Blur,
            2 => CameraBackgroundMode.Transparent,
            3 => CameraBackgroundMode.Color,
            _ => CameraBackgroundMode.None
        };
        _inlineCameraBackground.ColorR = 0x10;
        _inlineCameraBackground.ColorG = 0x6E;
        _inlineCameraBackground.ColorB = 0xBE;
        _inlineCameraBgReady = false;
        Interlocked.Exchange(ref _inlineCameraLastAiPreviewTicks, 0);
        ResetInlineCameraAiCounters();
        ResetInlineCameraDiagWindow();
        _inlineCameraPreviewRetryCount = 0;
        StartInlineCameraDiagTimer();

        if (_inlineCameraBackground.NeedsModel)
        {
            inlineRecorderStatus.Text = "Preparing AI background model...";
            AppendInlineRecorderLog($"Preparing AI background: {_inlineCameraBackground.Mode}");
            try
            {
                await _inlineCameraBgService.EnsureModelAsync(new Progress<double>(p =>
                {
                    inlineRecorderStatus.Text = $"Downloading AI background model... {p:P0}";
                    AppendInlineRecorderLog($"AI model download: {p:P0}");
                }));
                AppendInlineRecorderLog("Initializing ONNX session...");
                await _inlineCameraBgService.InitializeAsync();
                _inlineCameraBgReady = true;
                inlineRecorderStatus.Text = $"Fast live AI preview ({_inlineCameraBgService.ExecutionProvider}).";
                AppendInlineRecorderLog($"AI preview ready. Provider: {_inlineCameraBgService.ExecutionProvider}");
            }
            catch (Exception ex)
            {
                inlineRecorderStatus.Text = "AI background failed: " + ex.Message;
                AppendInlineRecorderLog("AI background init failed: " + ex);
            }
        }
        else
        {
            _inlineCameraBgReady = false;
            inlineRecorderStatus.Text = $"Camera ready: {_inlineRecorderWebcamDeviceName}.";
            AppendInlineRecorderLog("AI background disabled.");
        }

        if (_inlineRecorderProc == null || _inlineRecorderProc.HasExited)
        {
            bool previewAlive = _inlineRecorderWebcamPreviewProc != null && !_inlineRecorderWebcamPreviewProc.HasExited;
            if (_inlineRecorderCameraOnly)
            {
                if (!previewAlive) StartInlineCameraOnlyPreview();
            }
            else if (screenCamMode)
            {
                if (!previewAlive) StartInlineRecorderWebcamPreview();
            }
        }
    }

    private void InlineRecorderPreviewTimer_Tick(object? sender, EventArgs e) => CaptureInlineRecorderPreview();

    private void RefreshInlineRecorderDiag()
    {
        if (inlineRecorderDiagText == null || inlineRecorderSourceBox == null) return;
        int idx = inlineRecorderSourceBox.SelectedIndex;
        if (idx > 0 && idx - 1 < _inlineRecorderMonitors.Count)
        {
            var m = _inlineRecorderMonitors[idx - 1];
            inlineRecorderDiagText.Text = m.HasDpiScaling
                ? $"Recording monitor area {m.Width}x{m.Height} via gdigrab. Windows scaling is active on this display."
                : $"Recording monitor area {m.Width}x{m.Height} via gdigrab.";
        }
        else
        {
            inlineRecorderDiagText.Text =
                $"Recording entire virtual desktop via gdigrab: x={(int)SystemParameters.VirtualScreenLeft}, " +
                $"y={(int)SystemParameters.VirtualScreenTop}, w={(int)SystemParameters.VirtualScreenWidth}, " +
                $"h={(int)SystemParameters.VirtualScreenHeight}.";
        }
    }

    private void CaptureInlineRecorderPreview()
    {
        if (inlineRecorderPreviewImage == null || screenRecorderPanel.Visibility != Visibility.Visible) return;
        try
        {
            int x, y, w, h;
            int idx = inlineRecorderSourceBox.SelectedIndex;
            if (idx > 0 && idx - 1 < _inlineRecorderMonitors.Count)
            {
                var m = _inlineRecorderMonitors[idx - 1];
                x = m.X;
                y = m.Y;
                w = m.HasDpiScaling ? m.PhysicalWidth : m.Width;
                h = m.HasDpiScaling ? m.PhysicalHeight : m.Height;
            }
            else
            {
                x = (int)SystemParameters.VirtualScreenLeft;
                y = (int)SystemParameters.VirtualScreenTop;
                w = (int)SystemParameters.VirtualScreenWidth;
                h = (int)SystemParameters.VirtualScreenHeight;
            }
            if (w <= 0 || h <= 0) return;

            using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h),
                    System.Drawing.CopyPixelOperation.SourceCopy);
            }
            inlineRecorderPreviewImage.Source = BitmapToBitmapSource(bmp);
        }
        catch
        {
            // Preview can fail while Windows is showing secure surfaces; the next tick can recover.
        }
    }

    private static System.Windows.Media.Imaging.BitmapSource BitmapToBitmapSource(System.Drawing.Bitmap bmp)
    {
        var data = bmp.LockBits(
            new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var src = System.Windows.Media.Imaging.BitmapSource.Create(
                bmp.Width, bmp.Height, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null,
                data.Scan0, data.Stride * bmp.Height, data.Stride);
            src.Freeze();
            return src;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private async void InlineRecorderStart_Click(object sender, RoutedEventArgs e)
    {
        if (_inlineRecorderProc != null && !_inlineRecorderProc.HasExited) return;
        if (!int.TryParse(inlineRecorderFpsBox.Text, out var fpsValue) || fpsValue < 1 || fpsValue > 120)
        {
            MessageBox.Show("FPS must be between 1 and 120.");
            return;
        }

        var outputPath = string.IsNullOrWhiteSpace(inlineRecorderPathBox.Text)
            ? (_inlineRecorderCameraOnly ? DefaultCameraRecordingPath() : DefaultScreenRecordingPath())
            : inlineRecorderPathBox.Text.Trim();
        _inlineCameraRecordTempPath = null;
        _screenCamScreenTempPath = null;
        _screenCamRawTempPath = null;
        _screenCamFinalOutputPath = null;
        _screenCamPipRect = null;
        if (_inlineRecorderCameraOnly && _inlineCameraBackground.NeedsModel)
        {
            if (_inlineCameraBackground.NeedsAlpha)
                outputPath = Path.ChangeExtension(outputPath, ".webm");
            _inlineCameraRecordTempPath = Path.Combine(Path.GetTempPath(), $"ve_camraw_{Guid.NewGuid():N}.mp4");
        }
        else if (!_inlineRecorderCameraOnly && _inlineRecorderWebcamBlock != null && _inlineCameraBackground.NeedsModel)
        {
            int monIdx = inlineRecorderSourceBox.SelectedIndex;
            int outW, outH;
            if (monIdx > 0 && monIdx - 1 < _inlineRecorderMonitors.Count)
            {
                var m = _inlineRecorderMonitors[monIdx - 1];
                outW = m.Width;
                outH = m.Height;
            }
            else
            {
                outW = (int)SystemParameters.VirtualScreenWidth;
                outH = (int)SystemParameters.VirtualScreenHeight;
            }
            var (px, py, pw, ph) = ScaleInlineRecorderRect(_inlineRecorderWebcamBlock, outW, outH);
            _screenCamPipRect = (px, py, pw, ph, outW, outH);
            var guid = Guid.NewGuid().ToString("N");
            _screenCamScreenTempPath = Path.Combine(Path.GetTempPath(), $"ve_scrcam_screen_{guid}.mp4");
            _screenCamRawTempPath = Path.Combine(Path.GetTempPath(), $"ve_scrcam_raw_{guid}.mp4");
            _screenCamFinalOutputPath = outputPath;
        }
        inlineRecorderPathBox.Text = outputPath;
        try
        {
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Cannot create output folder: " + ex.Message);
            return;
        }

        var args = _inlineRecorderCameraOnly
            ? BuildInlineCameraRecorderArgs(fpsValue, _inlineCameraRecordTempPath ?? outputPath)
            : BuildInlineRecorderArgs(fpsValue, outputPath);

        try
        {
            StopInlineRecorderWebcamPreview();
            if (_inlineRecorderCameraOnly || _inlineRecorderWebcamBlock != null)
            {
                inlineRecorderStartBtn.IsEnabled = false;
                inlineRecorderStatus.Text = "Releasing camera for recording...";
                await System.Threading.Tasks.Task.Delay(450);
            }
            bool hasCameraPreviewPipe = _inlineRecorderCameraOnly || _inlineRecorderWebcamBlock != null;
            _inlineRecorderLastError = "";
            _inlineRecorderLogTail.Clear();
            _inlineRecorderProc = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _ff.FFmpegExe,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                },
                EnableRaisingEvents = true
            };
            _inlineRecorderProc.ErrorDataReceived += (_, ev) => CaptureInlineRecorderLog(ev.Data);
            _inlineRecorderProc.Start();
            _inlineRecorderProc.BeginErrorReadLine();
            if (hasCameraPreviewPipe)
                _ = ReadMjpegPreviewAsync(_inlineRecorderProc.StandardOutput.BaseStream, System.Threading.CancellationToken.None);
            else
                _inlineRecorderProc.BeginOutputReadLine();
            inlineRecorderStartBtn.IsEnabled = false;
            inlineRecorderStopBtn.IsEnabled = true;
            closeInlineRecorderBtn.IsEnabled = false;
            inlineRecorderDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
            inlineRecorderStatus.Text = _inlineRecorderCameraOnly ? "Camera recording..." : "Recording...";
            // Recording is live: collapse the inspector tab to give the preview full width,
            // and surface a top-status hint telling the user how to bring the controls back.
            CollapseRecorderTab();
            ShowRecordingHint();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message);
            _inlineRecorderProc = null;
            inlineRecorderStartBtn.IsEnabled = true;
            inlineRecorderStopBtn.IsEnabled = false;
            closeInlineRecorderBtn.IsEnabled = true;
            HideRecordingHint();
            if (_inlineRecorderCameraOnly) StartInlineCameraOnlyPreview();
            else if (_inlineRecorderWebcamBlock != null) StartInlineRecorderWebcamPreview();
        }
    }

    private string BuildInlineRecorderArgs(int fpsValue, string outputPath)
    {
        bool useWebcam = _inlineRecorderWebcamBlock != null;
        var liveBlocks = GetInlineRecorderBlocks();
        int monIdx = inlineRecorderSourceBox.SelectedIndex;
        bool specificMonitor = monIdx > 0 && monIdx - 1 < _inlineRecorderMonitors.Count;

        int outputW, outputH;
        string inputArgs = "-y ";
        string filter;
        string finalPad;
        string? webcamPad = null;

        if (specificMonitor)
        {
            var m = _inlineRecorderMonitors[monIdx - 1];
            AppSettings.LastScreenRecorderMonitor = m.Index;
            AppSettings.Save();
            outputW = m.Width;
            outputH = m.Height;
            inputArgs += $"-f gdigrab -framerate {fpsValue} -offset_x {m.X} -offset_y {m.Y} -video_size {outputW}x{outputH} -i desktop ";
            if (useWebcam)
            {
                inputArgs += $"-f dshow -rtbufsize 200M -framerate {fpsValue} -i video=\"{EscapeRecorderArg(_inlineRecorderWebcamDeviceName)}\" ";
                webcamPad = "[1:v]";
            }

            filter = "[0:v]format=yuv420p[s0]";
            finalPad = "[s0]";
        }
        else
        {
            AppSettings.LastScreenRecorderMonitor = -1;
            AppSettings.Save();
            outputW = (int)SystemParameters.VirtualScreenWidth;
            outputH = (int)SystemParameters.VirtualScreenHeight;
            inputArgs += $"-f gdigrab -framerate {fpsValue} -i desktop ";
            if (useWebcam)
            {
                inputArgs += $"-f dshow -rtbufsize 200M -framerate {fpsValue} -i video=\"{EscapeRecorderArg(_inlineRecorderWebcamDeviceName)}\" ";
                webcamPad = "[1:v]";
            }

            filter = "[0:v]format=yuv420p[s0]";
            finalPad = "[s0]";
        }

        var filters = new System.Text.StringBuilder(filter);
        int stage = 1;

        foreach (var (block, x, y, w, h) in ScaleInlineRecorderBlocks(liveBlocks, outputW, outputH))
        {
            string next = $"[s{stage++}]";
            var colorHex = $"{block.Color.R:X2}{block.Color.G:X2}{block.Color.B:X2}";
            filters.Append($";{finalPad}drawbox=x={x}:y={y}:w={w}:h={h}:color=0x{colorHex}@1.0:t=fill{next}");
            finalPad = next;
        }

        bool screenCamAiActive = useWebcam && _inlineRecorderWebcamBlock != null
                                 && _screenCamScreenTempPath != null && _screenCamRawTempPath != null;

        if (useWebcam && webcamPad != null && _inlineRecorderWebcamBlock != null)
        {
            if (screenCamAiActive)
            {
                filters.Append($";{webcamPad}hflip,split[camrec][campreview];[campreview]fps=12,scale=640:-1[camout];[camrec]format=yuv420p[finalCamRaw]");
            }
            else
            {
                var (x, y, w, h) = ScaleInlineRecorderRect(_inlineRecorderWebcamBlock, outputW, outputH);
                string camPad = $"[cam{stage}]";
                string next = $"[s{stage++}]";
                filters.Append($";{webcamPad}hflip,split[camrec][campreview];[campreview]fps=12,scale=640:-1[camout];[camrec]scale={w}:{h}{camPad};{finalPad}{camPad}overlay={x}:{y}{next}");
                finalPad = next;
            }
        }

        var filterArg = filters.ToString();
        string args;
        if (screenCamAiActive)
        {
            args = $"{inputArgs}-filter_complex \"{filterArg}\" " +
                   $"-map \"{finalPad}\" -c:v libx264 -preset ultrafast -pix_fmt yuv420p -fps_mode cfr -r {fpsValue} \"{_screenCamScreenTempPath}\" " +
                   $"-map \"[finalCamRaw]\" -c:v libx264 -preset ultrafast -pix_fmt yuv420p -fps_mode cfr -r {fpsValue} \"{_screenCamRawTempPath}\" " +
                   "-map \"[camout]\" -f image2pipe -vcodec mjpeg pipe:1";
        }
        else
        {
            args = $"{inputArgs}-filter_complex \"{filterArg}\" -map \"{finalPad}\" -c:v libx264 -preset ultrafast -pix_fmt yuv420p -fps_mode cfr -r {fpsValue} \"{outputPath}\"";
            if (useWebcam)
                args += " -map \"[camout]\" -f image2pipe -vcodec mjpeg pipe:1";
        }
        return args;
    }

    private string BuildInlineCameraRecorderArgs(int fpsValue, string outputPath)
    {
        var cameraName = EscapeRecorderArg(_inlineRecorderWebcamDeviceName);
        return $"-y -f dshow -rtbufsize 200M -i video=\"{cameraName}\" " +
               $"-filter_complex \"[0:v]hflip,fps={fpsValue},split[camrec][campreview];" +
               "[campreview]fps=12,scale=640:-1[camout];" +
               "[camrec]format=yuv420p[record]\" " +
               $"-map \"[record]\" -c:v libx264 -preset ultrafast -pix_fmt yuv420p -fps_mode cfr -r {fpsValue} \"{outputPath}\" " +
               "-map \"[camout]\" -f image2pipe -vcodec mjpeg pipe:1";
    }

    private List<VideoBlock> GetInlineRecorderBlocks() =>
        _blockControls
            .Where(kv => ReferenceEquals(kv.Value.Parent, inlineRecorderOverlayCanvas))
            .Select(kv => kv.Key)
            .ToList();

    private IEnumerable<(VideoBlock block, int x, int y, int w, int h)> ScaleInlineRecorderBlocks(
        IEnumerable<VideoBlock> blocks, int outputW, int outputH)
    {
        foreach (var block in blocks)
        {
            var (x, y, w, h) = ScaleInlineRecorderRect(block, outputW, outputH);
            yield return (block, x, y, w, h);
        }
    }

    private (int x, int y, int w, int h) ScaleInlineRecorderRect(VideoBlock block, int outputW, int outputH)
    {
        double canvasW = inlineRecorderOverlayCanvas.ActualWidth > 1 ? inlineRecorderOverlayCanvas.ActualWidth : outputW;
        double canvasH = inlineRecorderOverlayCanvas.ActualHeight > 1 ? inlineRecorderOverlayCanvas.ActualHeight : outputH;
        double sx = outputW / canvasW;
        double sy = outputH / canvasH;
        int x = Math.Clamp((int)Math.Round(block.X * sx), 0, Math.Max(0, outputW - 2));
        int y = Math.Clamp((int)Math.Round(block.Y * sy), 0, Math.Max(0, outputH - 2));
        int w = Math.Clamp((int)Math.Round(block.Width * sx), 2, Math.Max(2, outputW - x));
        int h = Math.Clamp((int)Math.Round(block.Height * sy), 2, Math.Max(2, outputH - y));
        return (x, y, w, h);
    }

    private static string EscapeRecorderArg(string value) => (value ?? "").Replace("\"", "\\\"");

    private void StartInlineRecorderWebcamPreview()
    {
        if (_inlineRecorderWebcamControl == null) return;
        StopInlineRecorderWebcamPreview();
        try
        {
            _inlineRecorderWebcamPreviewCts = new System.Threading.CancellationTokenSource();
            _inlineRecorderWebcamPreviewProc = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _ff.FFmpegExe,
                    Arguments = $"-hide_banner -loglevel error -f dshow -i video=\"{EscapeRecorderArg(_inlineRecorderWebcamDeviceName)}\" -vf fps=12,hflip,scale=640:-1 -q:v 5 -f image2pipe -vcodec mjpeg pipe:1",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };
            _inlineRecorderWebcamPreviewProc.ErrorDataReceived += (_, ev) =>
            {
                if (!string.IsNullOrEmpty(ev.Data)) AppendInlineRecorderLog("ffmpeg-preview: " + ev.Data);
            };
            _inlineRecorderWebcamPreviewProc.Start();
            _inlineRecorderWebcamPreviewProc.BeginErrorReadLine();
            _ = ReadMjpegPreviewAsync(_inlineRecorderWebcamPreviewProc.StandardOutput.BaseStream,
                _inlineRecorderWebcamPreviewCts.Token);
        }
        catch
        {
            _inlineRecorderWebcamControl.SetLivePreviewSource(null);
        }
    }

    private async void StartInlineCameraOnlyPreview()
    {
        int myGen = Interlocked.Increment(ref _inlineCameraStartGen);
        StopInlineRecorderWebcamPreview();
        Interlocked.Exchange(ref _inlineCameraJpegsReceived, 0);
        Interlocked.Exchange(ref _inlineCameraLastJpegTicks, 0);
        _inlineCameraPreviewFfmpegError = "";
        ResetInlineCameraDiagWindow();

        await System.Threading.Tasks.Task.Delay(300);
        if (!_inlineRecorderCameraOnly) return;
        if (myGen != Interlocked.CompareExchange(ref _inlineCameraStartGen, 0, 0))
        {
            AppendInlineRecorderLog("(preview start superseded by a newer request)");
            return;
        }

        try
        {
            _inlineRecorderWebcamPreviewCts = new System.Threading.CancellationTokenSource();
            _inlineRecorderWebcamPreviewProc = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _ff.FFmpegExe,
                    Arguments = $"-hide_banner -loglevel error -f dshow -i video=\"{EscapeRecorderArg(_inlineRecorderWebcamDeviceName)}\" " +
                                "-vf fps=12,hflip,scale=640:-1 -q:v 5 -f image2pipe -vcodec mjpeg pipe:1",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };
            var launchedProc = _inlineRecorderWebcamPreviewProc;
            _inlineRecorderWebcamPreviewProc.ErrorDataReceived += (_, ev) =>
            {
                if (string.IsNullOrEmpty(ev.Data)) return;
                _inlineCameraPreviewFfmpegError = ev.Data;
                AppendInlineRecorderLog("ffmpeg-preview: " + ev.Data);
                if (ev.Data.IndexOf("Could not run graph", StringComparison.OrdinalIgnoreCase) >= 0
                    || ev.Data.IndexOf("I/O error", StringComparison.OrdinalIgnoreCase) >= 0
                    || ev.Data.IndexOf("Error opening input", StringComparison.OrdinalIgnoreCase) >= 0
                    || ev.Data.IndexOf("device already in use", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Dispatcher.BeginInvoke(new Action(() => MaybeSchedulePreviewRetry(launchedProc)));
                }
            };
            _inlineRecorderWebcamPreviewProc.Start();
            _inlineRecorderWebcamPreviewProc.BeginErrorReadLine();
            _ = ReadInlineCameraOnlyPreviewAsync(_inlineRecorderWebcamPreviewProc.StandardOutput.BaseStream,
                _inlineRecorderWebcamPreviewCts.Token);
        }
        catch (Exception ex)
        {
            _inlineCameraPreviewFfmpegError = ex.Message;
            AppendInlineRecorderLog("preview start failed: " + ex);
            inlineRecorderPreviewImage.Source = null;
            MaybeSchedulePreviewRetry(null);
        }
    }

    private void MaybeSchedulePreviewRetry(System.Diagnostics.Process? sourceProc)
    {
        if (!_inlineRecorderCameraOnly) return;
        if (sourceProc != null && !ReferenceEquals(sourceProc, _inlineRecorderWebcamPreviewProc))
        {
            AppendInlineRecorderLog("(ignoring stale ffmpeg-preview error from a process we already replaced)");
            return;
        }
        var lastJpegTicks = Interlocked.Read(ref _inlineCameraLastJpegTicks);
        var sinceJpegMs = lastJpegTicks == 0 ? long.MaxValue : (DateTime.UtcNow.Ticks - lastJpegTicks) / TimeSpan.TicksPerMillisecond;
        if (sinceJpegMs < 1500)
        {
            AppendInlineRecorderLog($"(ignoring ffmpeg-preview error - frames still arriving, last JPEG {sinceJpegMs} ms ago)");
            return;
        }
        if (_inlineCameraPreviewRetryPending) return;
        if (_inlineCameraPreviewRetryCount >= 4)
        {
            AppendInlineRecorderLog("Preview retry limit reached. Close the recorder and reopen, or check whether another app is using the camera.");
            return;
        }
        _inlineCameraPreviewRetryPending = true;
        _inlineCameraPreviewRetryCount++;
        int delayMs = 600 + _inlineCameraPreviewRetryCount * 400;
        AppendInlineRecorderLog($"Camera busy. Retrying preview in {delayMs} ms (attempt {_inlineCameraPreviewRetryCount}/4)...");
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            _inlineCameraPreviewRetryPending = false;
            if (!_inlineRecorderCameraOnly) return;
            var freshLast = Interlocked.Read(ref _inlineCameraLastJpegTicks);
            var freshSince = freshLast == 0 ? long.MaxValue : (DateTime.UtcNow.Ticks - freshLast) / TimeSpan.TicksPerMillisecond;
            if (freshSince < 1500)
            {
                AppendInlineRecorderLog($"(retry skipped - frames now flowing, last JPEG {freshSince} ms ago)");
                return;
            }
            StartInlineCameraOnlyPreview();
        };
        t.Start();
    }

    private void StopInlineRecorderWebcamPreview()
    {
        try { _inlineRecorderWebcamPreviewCts?.Cancel(); } catch { }
        if (_inlineRecorderWebcamPreviewProc != null)
        {
            try
            {
                if (!_inlineRecorderWebcamPreviewProc.HasExited)
                    _inlineRecorderWebcamPreviewProc.Kill(entireProcessTree: true);
            }
            catch { }
            try { _inlineRecorderWebcamPreviewProc.WaitForExit(1500); } catch { }
            try { _inlineRecorderWebcamPreviewProc.Dispose(); } catch { }
        }
        _inlineRecorderWebcamPreviewProc = null;
        _inlineRecorderWebcamPreviewCts = null;
    }

    private async System.Threading.Tasks.Task ReadMjpegPreviewAsync(Stream stream, System.Threading.CancellationToken token)
    {
        var buffer = new byte[8192];
        var bytes = new List<byte>(256 * 1024);
        try
        {
            while (!token.IsCancellationRequested)
            {
                int read = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                if (read <= 0) break;
                for (int i = 0; i < read; i++) bytes.Add(buffer[i]);

                byte[]? latest = null;
                int extracted = 0;
                while (TryExtractJpeg(bytes, out var jpg)) { latest = jpg; extracted++; }
                if (latest == null) continue;

                if (_inlineRecorderCameraOnly)
                {
                    Interlocked.Add(ref _inlineCameraJpegsReceived, extracted);
                    Interlocked.Exchange(ref _inlineCameraLastJpegTicks, DateTime.UtcNow.Ticks);

                    if (_inlineCameraBackground.NeedsModel && _inlineCameraBgReady)
                    {
                        QueueInlineCameraAiPreview(latest);
                        continue;
                    }

                    var rawImage = BitmapSourceFromJpeg(latest);
                    Dispatcher.Invoke(() => inlineRecorderPreviewImage.Source = rawImage);
                    continue;
                }

                if (_inlineRecorderWebcamBlock != null && _inlineCameraBackground.NeedsModel && _inlineCameraBgReady)
                {
                    QueueScreenCamAiPreview(latest);
                    continue;
                }

                var image = BitmapSourceFromJpeg(latest);
                Dispatcher.Invoke(() => _inlineRecorderWebcamControl?.SetLivePreviewSource(image));
            }
        }
        catch
        {
            // Preview is best-effort; recording failures are handled by the recorder process.
        }
    }

    private async System.Threading.Tasks.Task ReadInlineCameraOnlyPreviewAsync(Stream stream, System.Threading.CancellationToken token)
    {
        var buffer = new byte[8192];
        var bytes = new List<byte>(256 * 1024);
        try
        {
            while (!token.IsCancellationRequested)
            {
                int read = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                if (read <= 0) break;
                for (int i = 0; i < read; i++) bytes.Add(buffer[i]);

                byte[]? latest = null;
                int extracted = 0;
                while (TryExtractJpeg(bytes, out var jpg)) { latest = jpg; extracted++; }
                if (latest == null) continue;

                Interlocked.Add(ref _inlineCameraJpegsReceived, extracted);
                Interlocked.Exchange(ref _inlineCameraLastJpegTicks, DateTime.UtcNow.Ticks);
                _inlineCameraPreviewRetryCount = 0;

                if (_inlineRecorderCameraOnly && _inlineCameraBackground.NeedsModel && _inlineCameraBgReady)
                {
                    QueueInlineCameraAiPreview(latest);
                    continue;
                }

                var image = BitmapSourceFromJpeg(latest);
                Dispatcher.Invoke(() => inlineRecorderPreviewImage.Source = image);
            }
        }
        catch
        {
            // Preview is best-effort; recording errors are shown from ffmpeg stderr.
        }
    }

    private void QueueInlineCameraAiPreview(byte[] jpg)
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        ReportInlineCameraAiStats(nowTicks);

        var minIntervalTicks = TimeSpan.FromMilliseconds(120).Ticks;
        if (nowTicks - Interlocked.Read(ref _inlineCameraLastAiPreviewTicks) < minIntervalTicks)
        {
            Interlocked.Increment(ref _inlineCameraAiSkippedThrottle);
            return;
        }
        if (Interlocked.CompareExchange(ref _inlineCameraAiPreviewInFlight, 1, 0) != 0)
        {
            Interlocked.Increment(ref _inlineCameraAiSkippedBusy);
            return;
        }

        Interlocked.Exchange(ref _inlineCameraLastAiPreviewTicks, nowTicks);
        var jpgCopy = jpg;
        AppendInlineRecorderLog($"AI frame start: {jpgCopy.Length:N0} bytes");
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var image = RenderInlineCameraAiPreviewFrame(jpgCopy);
                sw.Stop();
                Interlocked.Increment(ref _inlineCameraAiProcessed);
                Interlocked.Exchange(ref _inlineCameraLastInferenceMs, sw.ElapsedMilliseconds);
                Interlocked.Exchange(ref _inlineCameraLastAiDoneTicks, DateTime.UtcNow.Ticks);
                Dispatcher.Invoke(() =>
                {
                    inlineRecorderPreviewImage.Source = image;
                    inlineRecorderStatus.Text = $"Fast live AI preview ({_inlineCameraBgService.ExecutionProvider}).";
                    AppendInlineRecorderLog($"AI frame done: {sw.ElapsedMilliseconds} ms ({_inlineCameraBgService.ExecutionProvider})");
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    inlineRecorderStatus.Text = "Fast live AI preview failed: " + ex.Message;
                    AppendInlineRecorderLog("Fast live AI preview failed: " + ex);
                });
            }
            finally
            {
                Interlocked.Exchange(ref _inlineCameraAiPreviewInFlight, 0);
            }
        });
    }

    private System.Windows.Media.Imaging.BitmapSource RenderInlineCameraAiPreviewFrame(byte[] jpg)
    {
        using var ms = new MemoryStream(jpg);
        using var raw = new System.Drawing.Bitmap(ms);
        using var composited = _inlineCameraBgService.ProcessBitmap(raw, _inlineCameraBackground);
        return BitmapToBitmapSource(composited);
    }

    private void QueueScreenCamAiPreview(byte[] jpg)
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        var minIntervalTicks = TimeSpan.FromMilliseconds(120).Ticks;
        if (nowTicks - Interlocked.Read(ref _screenCamLastAiPreviewTicks) < minIntervalTicks) return;
        if (Interlocked.CompareExchange(ref _screenCamAiPreviewInFlight, 1, 0) != 0) return;

        Interlocked.Exchange(ref _screenCamLastAiPreviewTicks, nowTicks);
        var jpgCopy = jpg;
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var image = RenderInlineCameraAiPreviewFrame(jpgCopy);
                Dispatcher.Invoke(() => _inlineRecorderWebcamControl?.SetLivePreviewSource(image));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => AppendInlineRecorderLog("Screen+Cam AI preview failed: " + ex.Message));
            }
            finally
            {
                Interlocked.Exchange(ref _screenCamAiPreviewInFlight, 0);
            }
        });
    }

    private void ResetInlineCameraAiCounters()
    {
        Interlocked.Exchange(ref _inlineCameraAiProcessed, 0);
        Interlocked.Exchange(ref _inlineCameraAiSkippedBusy, 0);
        Interlocked.Exchange(ref _inlineCameraAiSkippedThrottle, 0);
        Interlocked.Exchange(ref _inlineCameraLastStatsTicks, 0);
    }

    private void ReportInlineCameraAiStats(long nowTicks)
    {
        var intervalTicks = TimeSpan.FromSeconds(3).Ticks;
        var last = Interlocked.Read(ref _inlineCameraLastStatsTicks);
        if (nowTicks - last < intervalTicks) return;
        if (Interlocked.CompareExchange(ref _inlineCameraLastStatsTicks, nowTicks, last) != last) return;

        var processed = Interlocked.Read(ref _inlineCameraAiProcessed);
        var busy = Interlocked.Read(ref _inlineCameraAiSkippedBusy);
        var throttle = Interlocked.Read(ref _inlineCameraAiSkippedThrottle);
        AppendInlineRecorderLog($"AI stats: processed={processed}, skipped_busy={busy}, skipped_throttle={throttle}, provider={_inlineCameraBgService.ExecutionProvider}");
    }

    private void StartInlineCameraDiagTimer()
    {
        if (_inlineCameraDiagTimer == null)
        {
            _inlineCameraDiagTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _inlineCameraDiagTimer.Tick += (_, _) => RefreshInlineCameraAiDiag();
        }
        if (!_inlineCameraDiagTimer.IsEnabled) _inlineCameraDiagTimer.Start();
        RefreshInlineCameraAiDiag();
    }

    private void StopInlineCameraDiagTimer()
    {
        try { _inlineCameraDiagTimer?.Stop(); } catch { }
    }

    private void ResetInlineCameraDiagWindow()
    {
        var now = DateTime.UtcNow.Ticks;
        Interlocked.Exchange(ref _inlineCameraDiagWindowStartTicks, now);
        Interlocked.Exchange(ref _inlineCameraDiagWindowJpegsStart, Interlocked.Read(ref _inlineCameraJpegsReceived));
        Interlocked.Exchange(ref _inlineCameraDiagWindowAiStart, Interlocked.Read(ref _inlineCameraAiProcessed));
    }

    private void RefreshInlineCameraAiDiag()
    {
        if (inlineRecorderDiagText == null || !_inlineRecorderCameraOnly) return;

        var nowTicks = DateTime.UtcNow.Ticks;
        var windowStart = Interlocked.Read(ref _inlineCameraDiagWindowStartTicks);
        if (windowStart == 0)
        {
            ResetInlineCameraDiagWindow();
            windowStart = Interlocked.Read(ref _inlineCameraDiagWindowStartTicks);
        }
        var elapsedSec = Math.Max(0.001, (nowTicks - windowStart) / (double)TimeSpan.TicksPerSecond);

        long jpegsTotal = Interlocked.Read(ref _inlineCameraJpegsReceived);
        long aiTotal = Interlocked.Read(ref _inlineCameraAiProcessed);
        long jpegsInWindow = jpegsTotal - Interlocked.Read(ref _inlineCameraDiagWindowJpegsStart);
        long aiInWindow = aiTotal - Interlocked.Read(ref _inlineCameraDiagWindowAiStart);
        double jpegsFps = jpegsInWindow / elapsedSec;
        double aiFps = aiInWindow / elapsedSec;

        if (elapsedSec > 5.0)
        {
            Interlocked.Exchange(ref _inlineCameraDiagWindowStartTicks, nowTicks);
            Interlocked.Exchange(ref _inlineCameraDiagWindowJpegsStart, jpegsTotal);
            Interlocked.Exchange(ref _inlineCameraDiagWindowAiStart, aiTotal);
        }

        long lastJpegTicks = Interlocked.Read(ref _inlineCameraLastJpegTicks);
        long lastAiTicks = Interlocked.Read(ref _inlineCameraLastAiDoneTicks);
        bool procAlive = _inlineRecorderWebcamPreviewProc != null && !_inlineRecorderWebcamPreviewProc.HasExited;
        bool warnFreeze = _inlineCameraBackground.NeedsModel && lastAiTicks != 0
                          && (nowTicks - lastAiTicks) > TimeSpan.FromSeconds(2).Ticks;
        bool warnNoFrames = lastJpegTicks != 0
                            && (nowTicks - lastJpegTicks) > TimeSpan.FromSeconds(2).Ticks;
        bool warn = warnFreeze || warnNoFrames || !procAlive;

        if (!AppSettings.CameraDebugDiagnostics)
        {
            inlineRecorderDiagText.Text = "";
            return;
        }

        string sinceJpeg = lastJpegTicks == 0 ? "n/a" : $"{(nowTicks - lastJpegTicks) / (double)TimeSpan.TicksPerSecond:F1}s";
        string sinceAi = lastAiTicks == 0 ? "n/a" : $"{(nowTicks - lastAiTicks) / (double)TimeSpan.TicksPerSecond:F1}s";
        long inferMs = Interlocked.Read(ref _inlineCameraLastInferenceMs);
        long busy = Interlocked.Read(ref _inlineCameraAiSkippedBusy);
        long throttle = Interlocked.Read(ref _inlineCameraAiSkippedThrottle);

        string mode = _inlineCameraBackground.Mode.ToString();
        string provider = _inlineCameraBgReady ? _inlineCameraBgService.ExecutionProvider : (_inlineCameraBackground.NeedsModel ? "loading..." : "off");
        string procState = procAlive ? "alive" : "STOPPED";

        var sb = new System.Text.StringBuilder();
        sb.Append("AI DIAGNOSTICS  ");
        sb.Append($"bg={mode}  provider={provider}  ffmpeg={procState}\n");
        sb.Append($"camera in: {jpegsFps:F1} fps (total {jpegsTotal}, since last {sinceJpeg})\n");
        sb.Append($"ai render: {aiFps:F1} fps (total {aiTotal}, last inference {inferMs} ms, since last {sinceAi})\n");
        sb.Append($"skipped (still busy with previous): {busy}    skipped (rate-limit): {throttle}");
        if (!string.IsNullOrWhiteSpace(_inlineCameraPreviewFfmpegError))
            sb.Append($"\nlast ffmpeg-preview line: {_inlineCameraPreviewFfmpegError}");

        if (warn)
            inlineRecorderDiagText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB1, 0x4D));
        else if (_inlineCameraBackground.NeedsModel && _inlineCameraBgReady)
            inlineRecorderDiagText.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        else
            inlineRecorderDiagText.Foreground = (Brush)FindResource("TextDim");

        inlineRecorderDiagText.Text = sb.ToString();
    }

    private static bool TryExtractJpeg(List<byte> bytes, out byte[] jpg)
    {
        jpg = Array.Empty<byte>();
        int start = -1;
        for (int i = 0; i < bytes.Count - 1; i++)
        {
            if (bytes[i] == 0xFF && bytes[i + 1] == 0xD8)
            {
                start = i;
                break;
            }
        }
        if (start < 0)
        {
            if (bytes.Count > 4096) bytes.RemoveRange(0, bytes.Count - 2);
            return false;
        }

        int end = -1;
        for (int i = start + 2; i < bytes.Count - 1; i++)
        {
            if (bytes[i] == 0xFF && bytes[i + 1] == 0xD9)
            {
                end = i + 2;
                break;
            }
        }
        if (end < 0)
        {
            if (start > 0) bytes.RemoveRange(0, start);
            return false;
        }

        jpg = bytes.GetRange(start, end - start).ToArray();
        bytes.RemoveRange(0, end);
        return true;
    }

    private static System.Windows.Media.Imaging.BitmapSource BitmapSourceFromJpeg(byte[] jpg)
    {
        using var ms = new MemoryStream(jpg);
        var bmp = new System.Windows.Media.Imaging.BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private void CaptureInlineRecorderLog(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        AppendInlineRecorderLog("ffmpeg: " + line);
        lock (_inlineRecorderLogTail)
        {
            _inlineRecorderLogTail.Enqueue(line);
            while (_inlineRecorderLogTail.Count > 16) _inlineRecorderLogTail.Dequeue();
            _inlineRecorderLastError = string.Join(Environment.NewLine,
                _inlineRecorderLogTail.Where(l =>
                    !l.Contains("frame=", StringComparison.OrdinalIgnoreCase) &&
                    !l.Contains("size=", StringComparison.OrdinalIgnoreCase) &&
                    !l.Contains("time=", StringComparison.OrdinalIgnoreCase))
                .TakeLast(8));
        }
    }

    private static readonly object _cameraLogFileLock = new();
    private static readonly string _cameraLogFilePath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "camera-diag.log");
    private static bool _cameraLogFileTruncated;

    private void ClearInlineRecorderVisibleLog()
    {
        lock (_inlineRecorderVisibleLog)
        {
            _inlineRecorderVisibleLog.Clear();
        }
        if (inlineRecorderLogText != null)
        {
            inlineRecorderLogText.Text = "";
            inlineRecorderLogText.Visibility = AppSettings.CameraDebugDiagnostics ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void AppendInlineRecorderLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        var stamped = $"[{DateTime.Now:HH:mm:ss}] {line}";

        WriteCameraLogFile(stamped);

        string visibleText;
        lock (_inlineRecorderVisibleLog)
        {
            _inlineRecorderVisibleLog.Enqueue(stamped);
            while (_inlineRecorderVisibleLog.Count > 80) _inlineRecorderVisibleLog.Dequeue();
            visibleText = string.Join(Environment.NewLine, _inlineRecorderVisibleLog);
        }

        if (!AppSettings.CameraDebugDiagnostics) return;

        void UpdateUi()
        {
            if (inlineRecorderLogText == null) return;
            inlineRecorderLogText.Visibility = Visibility.Visible;
            inlineRecorderLogText.Text = visibleText;
            inlineRecorderLogText.ScrollToEnd();
        }

        if (Dispatcher.CheckAccess()) UpdateUi();
        else Dispatcher.Invoke(UpdateUi);
    }

    private static void WriteCameraLogFile(string stamped)
    {
        try
        {
            lock (_cameraLogFileLock)
            {
                if (!_cameraLogFileTruncated)
                {
                    File.WriteAllText(_cameraLogFilePath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] --- camera diagnostics log started ---" + Environment.NewLine);
                    _cameraLogFileTruncated = true;
                }
                File.AppendAllText(_cameraLogFilePath, stamped + Environment.NewLine);
            }
        }
        catch { }
    }

    private string? DetectFirstDshowVideoDevice()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = _ff.FFmpegExe,
                Arguments = "-hide_banner -list_devices true -f dshow -i dummy",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return null;
            var output = proc.StandardError.ReadToEnd() + "\n" + proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(3000))
            {
                try { proc.Kill(); } catch { }
                return null;
            }
            return ParseFirstDshowVideoDevice(output);
        }
        catch
        {
            return null;
        }
    }

    private static string? ParseFirstDshowVideoDevice(string ffmpegOutput)
    {
        var inVideoSection = false;
        foreach (var raw in ffmpegOutput.Split('\n'))
        {
            var line = raw.Trim();
            var first = line.IndexOf('"');
            var last = line.LastIndexOf('"');
            if (first >= 0 && last > first && line.Contains("(video)", StringComparison.OrdinalIgnoreCase))
            {
                var directName = line.Substring(first + 1, last - first - 1);
                if (!directName.StartsWith("@device_", StringComparison.OrdinalIgnoreCase))
                    return directName;
            }

            if (line.Contains("DirectShow video devices", StringComparison.OrdinalIgnoreCase))
            {
                inVideoSection = true;
                continue;
            }
            if (line.Contains("DirectShow audio devices", StringComparison.OrdinalIgnoreCase))
                inVideoSection = false;
            if (!inVideoSection) continue;

            first = line.IndexOf('"');
            last = line.LastIndexOf('"');
            if (first >= 0 && last > first)
            {
                var name = line.Substring(first + 1, last - first - 1);
                if (!name.StartsWith("@device_", StringComparison.OrdinalIgnoreCase))
                    return name;
            }
        }
        return null;
    }


    private async void InlineRecorderStop_Click(object sender, RoutedEventArgs e)
    {
        var outputPath = inlineRecorderPathBox.Text.Trim();
        StopInlineScreenRecording();
        inlineRecorderStartBtn.IsEnabled = true;
        inlineRecorderStopBtn.IsEnabled = false;
        closeInlineRecorderBtn.IsEnabled = true;
        inlineRecorderDot.Fill = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));

        if (_inlineCameraRecordTempPath != null && File.Exists(_inlineCameraRecordTempPath))
        {
            inlineRecorderStartBtn.IsEnabled = false;
            inlineRecorderStopBtn.IsEnabled = false;
            closeInlineRecorderBtn.IsEnabled = false;
            inlineRecorderDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xB1, 0x4D));
            int.TryParse(inlineRecorderFpsBox.Text, out var fpsValue);
            if (fpsValue < 1) fpsValue = 30;
            var temp = _inlineCameraRecordTempPath;
            _inlineCameraRecordTempPath = null;
            try
            {
                var progress = new Progress<double>(p =>
                    inlineRecorderStatus.Text = $"Applying AI background... {p:P0}");
                await _inlineCameraBgService.EnsureModelAsync();
                await _inlineCameraBgService.ProcessVideoAsync(
                    _ff, temp, outputPath, _inlineCameraBackground, fpsValue, progress);
            }
            catch (Exception ex)
            {
                MessageBox.Show("AI background render failed:\n" + ex.Message,
                    "Camera Recording Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                inlineRecorderStatus.Text = "AI background render failed.";
                return;
            }
            finally
            {
                try { File.Delete(temp); } catch { }
                inlineRecorderStartBtn.IsEnabled = true;
                closeInlineRecorderBtn.IsEnabled = true;
            }
        }
        else if (_screenCamScreenTempPath != null && _screenCamRawTempPath != null
                 && _screenCamFinalOutputPath != null && _screenCamPipRect.HasValue
                 && File.Exists(_screenCamScreenTempPath) && File.Exists(_screenCamRawTempPath))
        {
            inlineRecorderStartBtn.IsEnabled = false;
            inlineRecorderStopBtn.IsEnabled = false;
            closeInlineRecorderBtn.IsEnabled = false;
            inlineRecorderDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xB1, 0x4D));
            int.TryParse(inlineRecorderFpsBox.Text, out var fpsValue);
            if (fpsValue < 1) fpsValue = 30;
            var screenTemp = _screenCamScreenTempPath;
            var camRawTemp = _screenCamRawTempPath;
            var finalOut = _screenCamFinalOutputPath;
            var pip = _screenCamPipRect.Value;
            _screenCamScreenTempPath = null;
            _screenCamRawTempPath = null;
            _screenCamFinalOutputPath = null;
            _screenCamPipRect = null;
            string camAiExt = _inlineCameraBackground.NeedsAlpha ? ".webm" : ".mp4";
            string camAiTemp = Path.ChangeExtension(camRawTemp, ".ai" + camAiExt);
            try
            {
                var progress = new Progress<double>(p =>
                    inlineRecorderStatus.Text = $"Applying AI background to camera... {p:P0}");
                AppendInlineRecorderLog("Screen+Cam: starting AI render on camera track");
                await _inlineCameraBgService.EnsureModelAsync();
                await _inlineCameraBgService.ProcessVideoAsync(
                    _ff, camRawTemp, camAiTemp, _inlineCameraBackground, fpsValue, progress);

                inlineRecorderStatus.Text = "Compositing camera onto screen...";
                AppendInlineRecorderLog($"Screen+Cam: compositing at x={pip.X} y={pip.Y} w={pip.W} h={pip.H}");
                outputPath = finalOut;
                var composeArgs = $"-y -i \"{screenTemp}\" -i \"{camAiTemp}\" " +
                                  $"-filter_complex \"[1:v]scale={pip.W}:{pip.H}[cam];[0:v][cam]overlay={pip.X}:{pip.Y}[v]\" " +
                                  $"-map \"[v]\" -c:v libx264 -preset veryfast -pix_fmt yuv420p -r {fpsValue} \"{finalOut}\"";
                var composeProc = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _ff.FFmpegExe,
                        Arguments = composeArgs,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true
                    },
                    EnableRaisingEvents = true
                };
                _inlineRecorderLastError = "";
                composeProc.ErrorDataReceived += (_, ev) =>
                {
                    if (!string.IsNullOrEmpty(ev.Data))
                    {
                        AppendInlineRecorderLog("compose: " + ev.Data);
                        _inlineRecorderLastError = ev.Data;
                    }
                };
                composeProc.Start();
                composeProc.BeginErrorReadLine();
                await composeProc.WaitForExitAsync();
                if (composeProc.ExitCode != 0)
                    throw new Exception($"Composit failed (ffmpeg exit {composeProc.ExitCode}). {_inlineRecorderLastError}");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Screen+Camera AI render failed:\n" + ex.Message,
                    "Screen Recording Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                inlineRecorderStatus.Text = "Screen+Camera AI render failed.";
                try { File.Delete(screenTemp); } catch { }
                try { File.Delete(camRawTemp); } catch { }
                try { File.Delete(camAiTemp); } catch { }
                inlineRecorderStartBtn.IsEnabled = true;
                closeInlineRecorderBtn.IsEnabled = true;
                return;
            }
            finally
            {
                try { File.Delete(screenTemp); } catch { }
                try { File.Delete(camRawTemp); } catch { }
                try { File.Delete(camAiTemp); } catch { }
                inlineRecorderStartBtn.IsEnabled = true;
                closeInlineRecorderBtn.IsEnabled = true;
            }
        }

        if (!File.Exists(outputPath))
        {
            inlineRecorderStatus.Text = "Recording stopped, but the output file was not created.";
            var detail = string.IsNullOrWhiteSpace(_inlineRecorderLastError)
                ? ""
                : "\n\nFFmpeg details:\n" + _inlineRecorderLastError;
            MessageBox.Show("Recording finished but the file wasn't created:\n" + outputPath + detail,
                "Screen Recording Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        inlineRecorderStatus.Text = "Saved and added to the timeline: " + Path.GetFileName(outputPath);
        status.Text = (_inlineRecorderCameraOnly ? "Camera recording" : "Screen recording") + " added to timeline: " + Path.GetFileName(outputPath);
        AddFiles(new[] { outputPath });
        inlineRecorderPathBox.Text = _inlineRecorderCameraOnly ? DefaultCameraRecordingPath() : DefaultScreenRecordingPath();
        // Stop+Add succeeded — leave recorder mode entirely so the main preview returns
        // and the right inspector goes back to whatever fits the current selection.
        CloseInlineRecorder_Click(this, new RoutedEventArgs());
    }

    private void StopInlineScreenRecording()
    {
        if (_inlineRecorderProc == null) return;
        if (!_inlineRecorderProc.HasExited)
        {
            try { _inlineRecorderProc.StandardInput.WriteLine("q"); } catch { }
            if (!_inlineRecorderProc.WaitForExit(4000))
            {
                try { _inlineRecorderProc.Kill(); } catch { }
                _inlineRecorderProc.WaitForExit(2000);
            }
        }
        try { _inlineRecorderProc.Dispose(); } catch { }
        _inlineRecorderProc = null;
    }

    private void Tts_Click(object s, RoutedEventArgs e)
    {
        PopulateTtsVoicesOnce();
        ShowInspectorTab("tts");
    }

    private void PopulateTtsVoicesOnce()
    {
        if (_ttsVoicesLoaded) return;
        try
        {
            string? defaultId = null;
            try { defaultId = SpeechSynthesizer.DefaultVoice?.Id; } catch { }
            foreach (var v in SpeechSynthesizer.AllVoices)
                ttsVoiceBox.Items.Add(new TtsVoiceItem(v.Id, v.DisplayName, v.Language));
            if (defaultId != null)
            {
                for (int i = 0; i < ttsVoiceBox.Items.Count; i++)
                {
                    if (ttsVoiceBox.Items[i] is TtsVoiceItem it && it.Id == defaultId)
                    {
                        ttsVoiceBox.SelectedIndex = i;
                        break;
                    }
                }
            }
            if (ttsVoiceBox.SelectedIndex < 0 && ttsVoiceBox.Items.Count > 0) ttsVoiceBox.SelectedIndex = 0;
        }
        catch { }
        _ttsVoicesLoaded = true;
    }

    // WinRT SpeechSynthesizer.Options.SpeakingRate is 0.5..6.0 with 1.0 = default.
    // Map the legacy -10..+10 slider symmetrically: 0 → 1.0, +10 → 2.0, -10 → 0.5.
    private static double SliderToSpeakingRate(double sliderValue)
        => Math.Clamp(Math.Pow(2.0, sliderValue / 10.0), 0.5, 6.0);

    private async Task<byte[]?> SynthesizeAsync(string text)
    {
        using var synth = new SpeechSynthesizer();
        if (ttsVoiceBox.SelectedItem is TtsVoiceItem item)
        {
            var voice = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Id == item.Id);
            if (voice != null) synth.Voice = voice;
        }
        synth.Options.SpeakingRate = SliderToSpeakingRate(ttsRateSlider.Value);
        using var stream = await synth.SynthesizeTextToStreamAsync(text);
        var size = (uint)stream.Size;
        if (size == 0) return null;
        var buffer = new byte[size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync(size);
        reader.ReadBytes(buffer);
        return buffer;
    }

    private async void TtsPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_ttsPreviewPlayer != null)
        {
            StopTtsPreview();
            return;
        }
        var text = ttsTextBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            ttsStatusText.Text = "Type some text to preview.";
            return;
        }
        ttsPreviewBtn.IsEnabled = false;
        ttsStatusText.Text = "Synthesizing...";
        try
        {
            var bytes = await SynthesizeAsync(text);
            if (bytes == null)
            {
                ttsStatusText.Text = "TTS produced no audio.";
                return;
            }
            _ttsPreviewStream = new MemoryStream(bytes);
            _ttsPreviewReader = new WaveFileReader(_ttsPreviewStream);
            _ttsPreviewPlayer = new WaveOutEvent();
            _ttsPreviewPlayer.Init(_ttsPreviewReader);
            _ttsPreviewPlayer.PlaybackStopped += (_, _) => Dispatcher.Invoke(StopTtsPreview);
            _ttsPreviewPlayer.Play();
            ttsPreviewBtn.Content = "⏹ Stop";
            ttsStatusText.Text = "Playing...";
        }
        catch (Exception ex)
        {
            StopTtsPreview();
            ttsStatusText.Text = "Preview failed: " + ex.Message;
        }
        finally
        {
            ttsPreviewBtn.IsEnabled = true;
        }
    }

    private void StopTtsPreview()
    {
        if (_ttsPreviewPlayer != null)
        {
            try { _ttsPreviewPlayer.Stop(); } catch { }
            try { _ttsPreviewPlayer.Dispose(); } catch { }
            _ttsPreviewPlayer = null;
        }
        if (_ttsPreviewReader != null)
        {
            try { _ttsPreviewReader.Dispose(); } catch { }
            _ttsPreviewReader = null;
        }
        if (_ttsPreviewStream != null)
        {
            try { _ttsPreviewStream.Dispose(); } catch { }
            _ttsPreviewStream = null;
        }
        if (ttsPreviewBtn != null) ttsPreviewBtn.Content = "▶ Preview";
        if (ttsStatusText != null && ttsStatusText.Text == "Playing...") ttsStatusText.Text = "";
    }

    private async Task<string?> SynthesizeAndSaveAsync()
    {
        var text = ttsTextBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            ttsStatusText.Text = "Type some text first.";
            return null;
        }
        StopTtsPreview();
        var sfd = new SaveFileDialog { FileName = "tts.wav", Filter = "WAV|*.wav" };
        if (sfd.ShowDialog(this) != true) return null;
        ttsStatusText.Text = "Synthesizing...";
        try
        {
            var bytes = await SynthesizeAsync(text);
            if (bytes == null)
            {
                ttsStatusText.Text = "TTS produced no audio.";
                return null;
            }
            await File.WriteAllBytesAsync(sfd.FileName, bytes);
            ttsStatusText.Text = "Saved: " + sfd.FileName;
            return sfd.FileName;
        }
        catch (Exception ex)
        {
            ttsStatusText.Text = "TTS failed: " + ex.Message;
            return null;
        }
    }

    private async void TtsSaveWav_Click(object sender, RoutedEventArgs e)
    {
        ttsSaveBtn.IsEnabled = false;
        ttsAddToTimelineBtn.IsEnabled = false;
        try { await SynthesizeAndSaveAsync(); }
        finally
        {
            ttsSaveBtn.IsEnabled = true;
            ttsAddToTimelineBtn.IsEnabled = true;
        }
    }

    private async void TtsAddToTimeline_Click(object sender, RoutedEventArgs e)
    {
        var text = ttsTextBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            ttsStatusText.Text = "Type some text first.";
            return;
        }
        StopTtsPreview();
        ttsSaveBtn.IsEnabled = false;
        ttsAddToTimelineBtn.IsEnabled = false;
        ttsStatusText.Text = "Synthesizing...";
        try
        {
            var bytes = await SynthesizeAsync(text);
            if (bytes == null)
            {
                ttsStatusText.Text = "TTS produced no audio.";
                return;
            }
            var ttsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tts_audio");
            Directory.CreateDirectory(ttsDir);
            var path = Path.Combine(ttsDir, $"tts_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.wav");
            await File.WriteAllBytesAsync(path, bytes);
            await AddTtsClipToTimelineAsync(path);
            ttsStatusText.Text = "";
        }
        catch (Exception ex)
        {
            ttsStatusText.Text = "Add to timeline failed: " + ex.Message;
        }
        finally
        {
            ttsSaveBtn.IsEnabled = true;
            ttsAddToTimelineBtn.IsEnabled = true;
        }
    }

    private async Task AddTtsClipToTimelineAsync(string wavPath)
    {
        try
        {
            var (_, _, d) = await _ff.ProbeAsync(wavPath);
            var duration = d > 0 ? d : 1;
            var clip = new VideoClip
            {
                SourceFile = wavPath,
                OriginalDuration = duration,
                InPoint = 0,
                OutPoint = duration,
                VideoWidth = 0,
                VideoHeight = 0,
                AccentColor = System.Windows.Media.Color.FromRgb(0x6E, 0x44, 0xD6),
                IsAudioOnly = true,
                TimelineStart = timeline.CurrentSeconds
            };
            timeline.Clips.Add(clip);
            timeline.SelectAudio(clip);
            UpdateStats();
            status.Text = $"Added TTS audio to timeline: {Path.GetFileName(wavPath)} · {Timeline.FormatTime(duration)}";
        }
        catch (Exception ex)
        {
            ttsStatusText.Text = "Add to timeline failed: " + ex.Message;
        }
    }
    private void Merge_Click(object s, RoutedEventArgs e)
    {
        ShowInspectorTab("merge");
        UpdateMergeQueueUI();
    }

    private void TabMerge_Click(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        ShowInspectorTab("merge");
    }

    private static bool IsAcceptedMergeFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return _mergeVideoExtensions.Contains(ext);
    }

    private static IEnumerable<string> CollectMergeFilesFromDrop(System.Windows.IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop)) return Array.Empty<string>();
        var raw = data.GetData(DataFormats.FileDrop) as string[];
        if (raw == null) return Array.Empty<string>();
        var result = new List<string>();
        foreach (var p in raw)
        {
            if (Directory.Exists(p))
            {
                foreach (var f in Directory.EnumerateFiles(p, "*", SearchOption.TopDirectoryOnly))
                    if (IsAcceptedMergeFile(f)) result.Add(f);
            }
            else if (File.Exists(p) && IsAcceptedMergeFile(p))
            {
                result.Add(p);
            }
        }
        return result;
    }

    private void MergeDropZone_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        bool hasVideo = false;
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var raw = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (raw != null)
            {
                foreach (var p in raw)
                {
                    if (Directory.Exists(p) || IsAcceptedMergeFile(p)) { hasVideo = true; break; }
                }
            }
        }
        e.Effects = hasVideo ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        if (hasVideo)
            mergeDropZone.BorderBrush = (System.Windows.Media.Brush)FindResource("Accent");
        e.Handled = true;
    }

    private void MergeDropZone_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        mergeDropZone.BorderBrush = (System.Windows.Media.Brush)FindResource("Line");
    }

    private void MergeDropZone_Drop(object sender, System.Windows.DragEventArgs e)
    {
        mergeDropZone.BorderBrush = (System.Windows.Media.Brush)FindResource("Line");
        var files = CollectMergeFilesFromDrop(e.Data).ToList();
        if (files.Count == 0)
        {
            status.Text = "No video files in the drop. Supported: " + string.Join(", ", _mergeVideoExtensions);
            return;
        }
        AddToMergeQueue(files);
        e.Handled = true;
    }

    private void MergeDropZone_AddFiles_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Video files|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.wmv;*.flv;*.m4v;*.ts;*.mpg;*.mpeg|All files|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        AddToMergeQueue(dlg.FileNames);
    }

    private void AddToMergeQueue(IEnumerable<string> paths)
    {
        int added = 0;
        foreach (var p in paths)
        {
            if (!IsAcceptedMergeFile(p)) continue;
            // Allow the same file twice (user might intentionally repeat); only skip if it
            // already appears as the very last item to avoid accidental double-drops.
            if (_mergeQueue.Count > 0 && string.Equals(_mergeQueue[^1].FullPath, p, StringComparison.OrdinalIgnoreCase))
                continue;
            _mergeQueue.Add(new MergeQueueItem(p, _mergeQueue.Count + 1));
            added++;
        }
        if (added > 0) status.Text = $"Added {added} video(s) to the merge queue.";
        UpdateMergeQueueUI();
    }

    private void RenumberMergeQueue()
    {
        for (int i = 0; i < _mergeQueue.Count; i++)
            _mergeQueue[i].Position = i + 1;
    }

    private void UpdateMergeQueueUI()
    {
        bool any = _mergeQueue.Count > 0;
        mergeEmptyHint.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        mergeQueueList.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        mergeClearBtn.IsEnabled = any;
        mergeNowBtn.IsEnabled = _mergeQueue.Count >= 2;
        mergeCountText.Text = _mergeQueue.Count switch
        {
            0 => "No videos yet",
            1 => "1 video (drop one more to merge)",
            _ => $"{_mergeQueue.Count} videos ready"
        };
        mergeHintText.Text = _mergeQueue.Count >= 2
            ? "Output will be created next to the first video, or pick a path."
            : "Add at least 2 videos to merge";
    }

    private MergeQueueItem? MergeItemFromSender(object sender) =>
        (sender as FrameworkElement)?.Tag as MergeQueueItem;

    private void MergeRemove_Click(object sender, RoutedEventArgs e)
    {
        var item = MergeItemFromSender(sender); if (item == null) return;
        _mergeQueue.Remove(item);
        RenumberMergeQueue();
        UpdateMergeQueueUI();
    }

    private void MergeMoveUp_Click(object sender, RoutedEventArgs e)
    {
        var item = MergeItemFromSender(sender); if (item == null) return;
        int idx = _mergeQueue.IndexOf(item);
        if (idx <= 0) return;
        _mergeQueue.Move(idx, idx - 1);
        RenumberMergeQueue();
    }

    private void MergeMoveDown_Click(object sender, RoutedEventArgs e)
    {
        var item = MergeItemFromSender(sender); if (item == null) return;
        int idx = _mergeQueue.IndexOf(item);
        if (idx < 0 || idx >= _mergeQueue.Count - 1) return;
        _mergeQueue.Move(idx, idx + 1);
        RenumberMergeQueue();
    }

    private void MergeClear_Click(object sender, RoutedEventArgs e)
    {
        if (_mergeQueue.Count == 0) return;
        _mergeQueue.Clear();
        UpdateMergeQueueUI();
        status.Text = "Merge queue cleared.";
    }

    private async void MergeNow_Click(object sender, RoutedEventArgs e)
    {
        if (_mergeQueue.Count < 2)
        {
            MessageBox.Show("Add at least 2 videos to the queue.", "Merge", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var first = _mergeQueue[0].FullPath;
        var defaultFolder = Path.GetDirectoryName(first) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        var defaultName = $"merged_{DateTime.Now:yyyyMMdd_HHmmss}{Path.GetExtension(first)}";
        var sfd = new SaveFileDialog
        {
            InitialDirectory = defaultFolder,
            FileName = defaultName,
            Filter = "Video files|*.mp4;*.mov;*.mkv;*.avi;*.webm|All files|*.*"
        };
        if (sfd.ShowDialog() != true) return;
        var output = sfd.FileName;

        mergeNowBtn.IsEnabled = false;
        mergeClearBtn.IsEnabled = false;
        try
        {
            status.Text = $"Merging {_mergeQueue.Count} videos...";
            progress.Value = 0;
            var prog = new Progress<double>(v => Dispatcher.Invoke(() => progress.Value = v));
            await _ff.MergeAsync(_mergeQueue.Select(m => m.FullPath), output, prog);
            progress.Value = 1;
            status.Text = $"Merged → {Path.GetFileName(output)}";
            var openResult = MessageBox.Show(
                "Merge complete.\n\nOpen the output folder?",
                "Merge Videos", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (openResult == MessageBoxResult.Yes)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{output}\"") { UseShellExecute = true });
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            status.Text = "Merge failed: " + ex.Message;
            MessageBox.Show(
                "Merge failed:\n\n" + ex.Message +
                "\n\nIf the videos have different codecs/resolutions, ffmpeg's fast 'concat copy' can fail. " +
                "Re-encoding support is not in this version - convert them to the same format first and try again.",
                "Merge Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            UpdateMergeQueueUI();
        }
    }
    private void Record_Click(object s, RoutedEventArgs e)
    {
        if (IsInlineRecorderVisible()) AddInlineRecorderWebcam();
        else ShowInlineCameraRecorder();
    }

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

    private void DownloadUrl_Click(object s, RoutedEventArgs e)
    {
        OpenDownloadTab();
    }

    private bool _downloadTabInitialized;
    private System.Threading.CancellationTokenSource? _downloadCts;
    private string? _downloadLastPath;
    private static readonly int[] DownloadQualityHeights = { 0, 4320, 2160, 1440, 1080, 720, 480 };

    private void OpenDownloadTab()
    {
        if (!_downloadTabInitialized)
        {
            downloadFolderBox.Text = ResolveDownloadFolder();
            SyncDownloadOptionsFromSettings();
            UpdateDownloadDetection();
            _downloadTabInitialized = true;
        }
        else
        {
            SyncDownloadOptionsFromSettings();
        }
        ShowInspectorTab("download");
    }

    private static string ResolveDownloadFolder()
    {
        if (!string.IsNullOrWhiteSpace(AppSettings.DownloadsFolder))
            return AppSettings.DownloadsFolder;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "VideoEditorDownloads");
    }

    private void DownloadUrlBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _downloadLastPath = null;
        if (downloadAddToTimelineBtn != null) downloadAddToTimelineBtn.IsEnabled = false;
        UpdateDownloadDetection();
    }

    private void UpdateDownloadDetection()
    {
        if (downloadDetectionText == null) return;
        var url = downloadUrlBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(url))
        {
            downloadDetectionText.Text = "Awaiting URL…";
            downloadDetectionDot.Fill = (System.Windows.Media.Brush)FindResource("TextDim");
            return;
        }
        if (DownloadService.LooksLikeStreamingSite(url))
        {
            downloadDetectionText.Text = "🎬 Streaming site detected · will use yt-dlp";
            downloadDetectionDot.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8B, 0x5C, 0xFF));
        }
        else if (DownloadService.LooksLikeDirectFile(url))
        {
            downloadDetectionText.Text = "📦 Direct video file · will use HTTP";
            downloadDetectionDot.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50));
        }
        else
        {
            downloadDetectionText.Text = "🤔 Unknown format · will try yt-dlp as fallback";
            downloadDetectionDot.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xD4, 0x3B));
        }
    }

    private void DownloadBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Pick any file in the destination folder",
            CheckFileExists = false,
            CheckPathExists = true,
            FileName = "Select folder",
            Filter = "Folder|*.folder"
        };
        if (dlg.ShowDialog() == true)
        {
            var folder = Path.GetDirectoryName(dlg.FileName);
            if (!string.IsNullOrEmpty(folder))
            {
                downloadFolderBox.Text = folder;
                AppSettings.DownloadsFolder = folder;
                AppSettings.Save();
            }
        }
    }

    private void DownloadSettings_Click(object sender, RoutedEventArgs e)
    {
        new SettingsWindow("downloads") { Owner = this }.ShowDialog();
        if (downloadFolderBox != null && !string.IsNullOrWhiteSpace(AppSettings.DownloadsFolder))
            downloadFolderBox.Text = AppSettings.DownloadsFolder;
        SyncDownloadOptionsFromSettings();
    }

    private void SyncDownloadOptionsFromSettings()
    {
        if (downloadQualityBox == null) return;

        _suppress = true;
        try
        {
            var idx = Array.IndexOf(DownloadQualityHeights, AppSettings.DownloadMaxHeight);
            downloadQualityBox.SelectedIndex = idx >= 0 ? idx : 4;
            if (downloadRememberLoginCheck != null)
                downloadRememberLoginCheck.IsChecked = AppSettings.DownloadRememberBrowserLogin;
            if (downloadAutoAddCheck != null)
                downloadAutoAddCheck.IsChecked = AppSettings.DownloadAutoAddToTimeline;
        }
        finally
        {
            _suppress = false;
        }

        UpdateDownloadOptionsSummary();
    }

    private void UpdateDownloadOptionsSummary()
    {
        if (downloadOptionsSummaryText == null) return;

        var maxHeight = AppSettings.DownloadMaxHeight <= 0 ? "best available" : $"up to {AppSettings.DownloadMaxHeight}p";
        var login = AppSettings.DownloadRememberBrowserLogin ? "browser login remembered" : "login asked only when needed";
        var add = AppSettings.DownloadAutoAddToTimeline ? "auto-add enabled" : "manual Add to Timeline";
        downloadOptionsSummaryText.Text = $"Quality: {maxHeight} · {login} · {add}";
    }

    private void DownloadQuality_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || downloadQualityBox == null) return;
        var idx = Math.Clamp(downloadQualityBox.SelectedIndex, 0, DownloadQualityHeights.Length - 1);
        AppSettings.DownloadMaxHeight = DownloadQualityHeights[idx];
        AppSettings.Save();
        UpdateDownloadOptionsSummary();
    }

    private void DownloadOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        if (downloadRememberLoginCheck != null)
            AppSettings.DownloadRememberBrowserLogin = downloadRememberLoginCheck.IsChecked == true;
        if (downloadAutoAddCheck != null)
            AppSettings.DownloadAutoAddToTimeline = downloadAutoAddCheck.IsChecked == true;
        AppSettings.Save();
        UpdateDownloadOptionsSummary();
    }

    private async void DownloadStart_Click(object sender, RoutedEventArgs e)
    {
        // Toggle off: a running download → cancel.
        if (_downloadCts != null)
        {
            try { _downloadCts.Cancel(); } catch { }
            downloadStatusText.Text = "Cancelling…";
            return;
        }

        var url = (downloadUrlBox.Text ?? "").Trim();
        var folder = (downloadFolderBox.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            downloadStatusText.Text = "Enter a URL first.";
            return;
        }
        if (string.IsNullOrWhiteSpace(folder))
        {
            downloadStatusText.Text = "Pick a destination folder first.";
            return;
        }
        try { Directory.CreateDirectory(folder); }
        catch (Exception ex)
        {
            downloadStatusText.Text = "Cannot create folder: " + ex.Message;
            return;
        }
        AppSettings.DownloadsFolder = folder;
        AppSettings.Save();

        _downloadCts = new System.Threading.CancellationTokenSource();
        downloadStartBtn.Content = "⏹ Cancel";
        downloadAddToTimelineBtn.IsEnabled = false;
        _downloadLastPath = null;
        tabDownloadDot.Visibility = Visibility.Visible;
        downloadPhaseText.Text = "Downloading…";
        downloadProgress.Value = 0;
        downloadStatusText.Text = "Starting download…";

        var dl = new DownloadService();
        dl.Log += msg => Dispatcher.Invoke(() =>
        {
            var t = msg.Length > 96 ? msg[..96] + "…" : msg;
            downloadStatusText.Text = t;
            status.Text = t;
        });
        var prog = new Progress<double>(v => Dispatcher.Invoke(() =>
        {
            downloadProgress.Value = v;
            progress.Value = v;
        }));

        progress.Value = 0;
        status.Text = "Starting download…";
        try
        {
            string finalPath = await DownloadWithLoginPromptsAsync(dl, url, folder, prog, _downloadCts.Token);
            _downloadLastPath = finalPath;
            downloadProgress.Value = 1;
            progress.Value = 1;
            downloadPhaseText.Text = "Done";
            downloadStatusText.Text = $"Downloaded: {Path.GetFileName(finalPath)}";
            downloadAddToTimelineBtn.IsEnabled = true;
            status.Text = $"Downloaded clip: {Path.GetFileName(finalPath)}";
            if (AppSettings.DownloadAutoAddToTimeline)
            {
                try
                {
                    await AddDownloadedPathToTimelineAsync(finalPath);
                }
                catch (Exception addEx)
                {
                    downloadPhaseText.Text = "Downloaded";
                    downloadStatusText.Text = "Downloaded, but add to timeline failed: " + addEx.Message;
                    status.Text = "Add to timeline failed: " + addEx.Message;
                    downloadAddToTimelineBtn.IsEnabled = true;
                }
            }
        }
        catch (OperationCanceledException)
        {
            downloadPhaseText.Text = "Cancelled";
            downloadStatusText.Text = "Download cancelled.";
            status.Text = "Download cancelled.";
        }
        catch (Exception ex)
        {
            downloadPhaseText.Text = "Failed";
            var logPath = DownloadService.DownloadLogPath;
            var msg = ex.Message;
            if (System.IO.File.Exists(logPath))
                msg += $"\nLog: {logPath}";
            downloadStatusText.Text = msg;
            status.Text = "Download failed: " + ex.Message;
        }
        finally
        {
            _downloadCts?.Dispose();
            _downloadCts = null;
            downloadStartBtn.Content = "Download";
            tabDownloadDot.Visibility = Visibility.Collapsed;
        }
    }

    private async Task<string> DownloadWithLoginPromptsAsync(
        DownloadService dl,
        string url,
        string folder,
        IProgress<double> prog,
        System.Threading.CancellationToken ct)
    {
        var allowBrowserLogin = AppSettings.DownloadRememberBrowserLogin;

        while (true)
        {
            try
            {
                return await dl.DownloadFromUrlAsync(
                    url, folder, App.FFmpegPath, prog, ct,
                    allowBrowserLogin: allowBrowserLogin,
                    maxHeight: AppSettings.DownloadMaxHeight);
            }
            catch (LoginRequiredDownloadException loginEx)
            {
                downloadPhaseText.Text = "Login required";
                downloadStatusText.Text = loginEx.Message;
                status.Text = "Download needs browser login.";

                var dlg = new BrowserLoginRequiredDialog(loginEx) { Owner = this };
                if (dlg.ShowDialog() != true)
                    throw;

                AppSettings.DownloadRememberBrowserLogin = dlg.RememberMe;
                AppSettings.Save();

                allowBrowserLogin = true;
                downloadPhaseText.Text = "Retrying with browser login…";
                downloadStatusText.Text = "Using browser login…";
                status.Text = "Retrying download with browser login…";
                downloadProgress.Value = 0;
                progress.Value = 0;
            }
        }
    }

    private async void DownloadAddToTimeline_Click(object sender, RoutedEventArgs e)
    {
        var path = _downloadLastPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            downloadStatusText.Text = "Download a video first.";
            downloadAddToTimelineBtn.IsEnabled = false;
            return;
        }

        try
        {
            await AddDownloadedPathToTimelineAsync(path);
        }
        catch (Exception ex)
        {
            downloadPhaseText.Text = "Failed";
            downloadStatusText.Text = "Add to timeline failed: " + ex.Message;
            status.Text = "Add to timeline failed: " + ex.Message;
            downloadAddToTimelineBtn.IsEnabled = true;
        }
    }

    private async Task AddDownloadedPathToTimelineAsync(string path)
    {
        downloadAddToTimelineBtn.IsEnabled = false;
        downloadPhaseText.Text = "Adding to timeline…";
        status.Text = $"Adding downloaded clip: {Path.GetFileName(path)}";
        var addedClip = await AddClipAsync(path);
        if (addedClip == null) return;
        downloadPhaseText.Text = "Done";
        downloadStatusText.Text = $"Added: {Path.GetFileName(path)}";
        status.Text = $"Added downloaded clip: {Path.GetFileName(path)}";
        downloadAddToTimelineBtn.IsEnabled = true;
    }

    private void Trim_Click(object s, RoutedEventArgs e)
    {
        var c = RequireCurrentClip("Trim Video"); if (c == null) return;
        var dlg = new TrimWindow(c.OriginalDuration) { Owner = this };
        if (dlg.ShowDialog() == true) { c.InPoint = dlg.StartSec; c.OutPoint = dlg.EndSec; SelectClip(c); }
    }
    private void Speed_Click(object s, RoutedEventArgs e)
    {
        var c = RequireCurrentClip("Change Speed"); if (c == null) return;
        var dlg = new SpeedWindow() { Owner = this };
        if (dlg.ShowDialog() == true) { c.Speed = dlg.Speed; SelectClip(c); }
    }
    private void Volume_Click(object s, RoutedEventArgs e)
    {
        var c = RequireCurrentClip("Change Volume"); if (c == null) return;
        var dlg = new VolumeWindow() { Owner = this };
        if (dlg.ShowDialog() == true) { c.Volume = dlg.Volume; SelectClip(c); }
    }
    private void Rotate_Click(object s, RoutedEventArgs e)
    {
        var c = RequireCurrentClip("Rotate Video"); if (c == null) return;
        var dlg = new RotateWindow() { Owner = this };
        if (dlg.ShowDialog() == true) { c.RotateDegrees = dlg.Degrees; ApplyClipTransform(c); }
    }
    private void Flip_Click(object s, RoutedEventArgs e)
    {
        var c = RequireCurrentClip("Flip Video"); if (c == null) return;
        var dlg = new FlipWindow() { Owner = this };
        if (dlg.ShowDialog() == true) { if (dlg.Horizontal) c.FlipH = !c.FlipH; else c.FlipV = !c.FlipV; ApplyClipTransform(c); }
    }
    private void Loop_Click(object s, RoutedEventArgs e)
    {
        var c = RequireCurrentClip("Loop Video"); if (c == null) return;
        var dlg = new LoopWindow() { Owner = this };
        if (dlg.ShowDialog() == true) { c.LoopCount = dlg.Times; status.Text = $"Clip will loop {c.LoopCount}x on export"; }
    }
    private async void Crop_Click(object s, RoutedEventArgs e)
    {
        var c = RequireCurrentClip("Crop Video");
        if (c == null) return;

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
        var c = RequireCurrentClip("Resize Video"); if (c == null) return;
        var dlg = new ResizeWindow(c.VideoWidth, c.VideoHeight) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        await ApplyDestructiveOpAsync(c, async (input, output, prog) =>
            await _ff.ResizeAsync(input, output, dlg.W, dlg.H, c.OriginalDuration, prog));
    }
    private async void Stabilize_Click(object s, RoutedEventArgs e)
    {
        var c = RequireCurrentClip("Stabilize"); if (c == null) return;
        if (MessageBox.Show("Run 2-pass stabilization on this clip? Replaces source.", "Stabilize", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        await ApplyDestructiveOpAsync(c, async (input, output, prog) =>
            await _ff.StabilizeAsync(input, output, c.OriginalDuration, prog));
    }
    private async void AddImage_Click(object s, RoutedEventArgs e)
    {
        var c = RequireCurrentClip("Add Image"); if (c == null) return;
        var dlg = new AddImageWindow() { Owner = this };
        if (dlg.ShowDialog() != true) return;
        await ApplyDestructiveOpAsync(c, async (input, output, prog) =>
            await _ff.AddImageAsync(input, dlg.ImageFile, output, dlg.X, dlg.Y, c.OriginalDuration, prog));
    }
    private async void AddText_Click(object s, RoutedEventArgs e)
    {
        var c = RequireCurrentClip("Add Text");
        if (c == null) return;
        await OpenTextPickerAndAddAsync(c, null);
    }

    private void AiCaptions_Click(object s, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AppSettings.LlmApiKey))
        {
            MessageBox.Show(
                VideoEditor.Services.Localization.T("Set your Gemini API key first - opening Settings…"),
                "AI Captions", MessageBoxButton.OK, MessageBoxImage.Information);
            new SettingsWindow("ai") { Owner = this }.ShowDialog();
            if (string.IsNullOrWhiteSpace(AppSettings.LlmApiKey)) return;
        }
        OpenAiCaptionsTab();
    }

    // ---- AI Captions inspector tab ----

    private static readonly string[] _captionsLanguageItems = { "Auto-detect", "Hebrew", "English" };
    private static readonly string[] _captionsLanguageKeys  = { "auto",        "he",     "en"      };
    private static readonly string[] _captionsModelItems =
    {
        "Tiny - fastest (75 MB)",
        "Base - balanced (140 MB)",
        "Small - better Hebrew (470 MB)",
        "Medium - very accurate (1.5 GB)",
        "Large v3 Turbo - best Hebrew (800 MB, recommended)"
    };
    private static readonly string[] _captionsModelKeys =
    {
        "tiny", "base", "small", "medium", "large-v3-turbo"
    };
    private static readonly string[] _captionsSourceItems = { "Selected clip", "All video clips" };
    private static readonly string[] _captionsSourceKeys  = { "clip",          "all" };
    private static readonly string[] _captionsOutputLangItems =
    {
        "Same as audio (no translation)",
        "Hebrew", "English", "Arabic", "Spanish", "French", "Russian", "Portuguese", "German"
    };
    private static readonly string[] _captionsOutputLangKeys =
    {
        "auto", "he", "en", "ar", "es", "fr", "ru", "pt", "de"
    };
    private static readonly string[] _captionsOutputLangNames =
    {
        "auto", "Hebrew", "English", "Arabic", "Spanish", "French", "Russian", "Portuguese", "German"
    };

    private bool _aiCaptionsTabInitialized;
    private System.Threading.CancellationTokenSource? _aiCaptionsCts;

    private void OpenAiCaptionsTab()
    {
        if (!_aiCaptionsTabInitialized)
        {
            PopulateAiCaptionsBoxes();
            _aiCaptionsTabInitialized = true;
        }
        RefreshAiCaptionsBadge();
        // Snap source picker to current selection so the user can click Generate immediately.
        var sel = CurrentClip();
        if (sel == null || sel.IsAudioOnly) captionsSourceBox.SelectedIndex = 1; // "All"
        ShowInspectorTab("aiCaptions");
    }

    private void PopulateAiCaptionsBoxes()
    {
        captionsSourceBox.Items.Clear();
        foreach (var s in _captionsSourceItems) captionsSourceBox.Items.Add(s);
        int srcIdx = Array.IndexOf(_captionsSourceKeys, AppSettings.LastTranscribeSource);
        captionsSourceBox.SelectedIndex = srcIdx < 0 ? 0 : srcIdx;

        captionsLangBox.Items.Clear();
        foreach (var s in _captionsLanguageItems) captionsLangBox.Items.Add(s);
        int langIdx = Array.IndexOf(_captionsLanguageKeys, AppSettings.LastTranscribeLanguage);
        captionsLangBox.SelectedIndex = langIdx < 0 ? 0 : langIdx;

        captionsModelBox.Items.Clear();
        foreach (var s in _captionsModelItems) captionsModelBox.Items.Add(s);
        int modelIdx = Array.IndexOf(_captionsModelKeys, AppSettings.LastTranscribeModel);
        captionsModelBox.SelectedIndex = modelIdx < 0 ? 1 : modelIdx;

        captionsOutputLangBox.Items.Clear();
        foreach (var s in _captionsOutputLangItems) captionsOutputLangBox.Items.Add(s);
        int capIdx = Array.IndexOf(_captionsOutputLangKeys, AppSettings.LastCaptionLanguage);
        captionsOutputLangBox.SelectedIndex = capIdx < 0 ? 0 : capIdx;
    }

    private void RefreshAiCaptionsBadge()
    {
        var used = VideoEditor.Services.LlmCaptionService.GetUsageToday();
        captionsGeminiBadge.Text = $"Gemini · {used} requests today (free tier ~1500/day)";
        captionsGeminiBadge.Foreground = used >= 1400
            ? System.Windows.Media.Brushes.OrangeRed
            : (System.Windows.Media.Brush)FindResource("TextDim");
    }

    private async void AiCaptionsGenerate_Click(object sender, RoutedEventArgs e)
    {
        if (_aiCaptionsCts != null)
        {
            try { _aiCaptionsCts.Cancel(); } catch { }
            captionsStatusText.Text = "Stopping…";
            return;
        }

        var videoClips = new List<VideoClip>();
        foreach (var c in timeline.Clips) if (!c.IsAudioOnly) videoClips.Add(c);
        if (videoClips.Count == 0)
        {
            captionsStatusText.Text = "Add a video clip first.";
            return;
        }

        var sourceKey  = _captionsSourceKeys [Math.Max(0, captionsSourceBox.SelectedIndex)];
        var languageKey = _captionsLanguageKeys[Math.Max(0, captionsLangBox.SelectedIndex)];
        var modelKey   = _captionsModelKeys  [Math.Max(0, captionsModelBox.SelectedIndex)];
        int capIdx = Math.Max(0, captionsOutputLangBox.SelectedIndex);
        var captionLangKey  = _captionsOutputLangKeys [capIdx];
        var captionLangName = _captionsOutputLangNames[capIdx];

        AppSettings.LastTranscribeSource   = sourceKey;
        AppSettings.LastTranscribeLanguage = languageKey;
        AppSettings.LastTranscribeModel    = modelKey;
        AppSettings.LastCaptionLanguage    = captionLangKey;
        AppSettings.Save();

        var selected = CurrentClip();
        var clipsToProcess = new List<VideoClip>();
        if (sourceKey == "clip" && selected != null && !selected.IsAudioOnly)
            clipsToProcess.Add(selected);
        else
            clipsToProcess.AddRange(videoClips);

        if (clipsToProcess.Count == 0)
        {
            captionsStatusText.Text = "No video clips to transcribe.";
            return;
        }

        int width = 1920, height = 1080;
        foreach (var clip in clipsToProcess)
        {
            if (clip.VideoWidth > 0 && clip.VideoHeight > 0)
            {
                width = clip.VideoWidth;
                height = clip.VideoHeight;
                break;
            }
        }

        _aiCaptionsCts = new System.Threading.CancellationTokenSource();
        captionsGenerateBtn.Content = "⏹ Stop";
        tabAiCaptionsDot.Visibility = Visibility.Visible;
        captionsProgress.Value = 0;

        var allSegments = new List<SubtitleSegment>();
        try
        {
            var whisper = new WhisperService();
            whisper.Log += line => Dispatcher.Invoke(() => captionsStatusText.Text = line);

            captionsPhaseText.Text = "Step 1/3 - Transcribing audio";
            int idx = 0;
            foreach (var clip in clipsToProcess)
            {
                _aiCaptionsCts.Token.ThrowIfCancellationRequested();
                int slot = idx;
                double slotShare = 1.0 / clipsToProcess.Count;
                var clipProgress = new Progress<double>(p =>
                    Dispatcher.Invoke(() => captionsProgress.Value = (slot * slotShare + p * slotShare) * 0.60));
                var segs = await whisper.TranscribeAsync(
                    clip.SourceFile, clip.InPoint, clip.OutPoint - clip.InPoint,
                    languageKey, modelKey, clipProgress, _aiCaptionsCts.Token);

                double speed = Math.Max(0.01, clip.Speed);
                double offset = clip.TimelineStart;
                foreach (var s in segs)
                {
                    allSegments.Add(new SubtitleSegment
                    {
                        StartSeconds = offset + s.StartSeconds / speed,
                        EndSeconds   = offset + s.EndSeconds   / speed,
                        Text = s.Text
                    });
                }
                idx++;
            }

            if (allSegments.Count == 0)
                throw new Exception("Whisper produced no segments - is there spoken audio?");

            captionsPhaseText.Text = "Step 2/3 - Generating captions with Gemini";
            captionsStatusText.Text = $"Sending {allSegments.Count} segments to Gemini…";
            var llm = new LlmCaptionService();
            llm.Log += line => Dispatcher.Invoke(() => captionsStatusText.Text = line);

            var llmProgress = new Progress<double>(p =>
                Dispatcher.Invoke(() => captionsProgress.Value = 0.60 + p * 0.35));

            var overlays = await llm.GenerateOverlaysAsync(
                allSegments, width, height, captionLangName, llmProgress, _aiCaptionsCts.Token);

            captionsPhaseText.Text = "Step 3/3 - Applying overlays";
            captionsProgress.Value = 1.0;

            foreach (var ov in overlays) timeline.TextOverlays.Add(ov);
            captionsStatusText.Text = $"Done · {overlays.Count} overlays added.";
            status.Text = $"AI Captions added · {overlays.Count} overlays - drag bars on the timeline to tweak.";
            RefreshAiCaptionsBadge();
        }
        catch (OperationCanceledException)
        {
            if (_aiCaptionsCts?.IsCancellationRequested == true)
            {
                captionsPhaseText.Text = "Cancelled";
                captionsStatusText.Text = "Stopped by user.";
            }
            else
            {
                captionsPhaseText.Text = "Timed out";
                captionsStatusText.Text = "Gemini took too long to respond. Try a shorter clip or run AI Captions again.";
            }
        }
        catch (Exception ex)
        {
            captionsPhaseText.Text = "Failed";
            captionsStatusText.Text = ex.Message;
        }
        finally
        {
            _aiCaptionsCts?.Dispose();
            _aiCaptionsCts = null;
            captionsGenerateBtn.Content = "✨ Generate";
            tabAiCaptionsDot.Visibility = Visibility.Collapsed;
        }
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
        var c = RequireCurrentClip("Add Audio"); if (c == null) return;
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
                // Replace the prior temp output (if any) -” first destructive op uses the user's
                // original file (don't touch), subsequent ones supersede our own temp files.
                if (IsAppTempFile(previousSource))
                {
                    try { File.Delete(previousSource); } catch { }
                }
            }
            else
            {
                // Operation failed -” clean up the half-written temp if it exists.
                try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
            }
        }
    }

    // Use the ProcessStartInfo argument list so the path is quoted by the runtime -” avoids
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

    // =========================================================================
    // UNDO / REDO  (Ctrl+Z / Ctrl+Shift+Z / Ctrl+Y)
    //
    // Snapshot-based: every change to the timeline.Clips collection or to any
    // clip's properties triggers a debounced (500ms) snapshot of the whole
    // clip list. Slider-drag flurries get coalesced into one snapshot at the
    // end of the drag. On Ctrl+Z we walk one step back in the history and
    // restore the clip list from the snapshot.
    //
    // What it covers: clip add / remove / move, trim in/out, speed, volume,
    // rotate, flip, loop, canvas zoom/offset on every clip (including the
    // audio-only "Add Audio" clips that live on the A1 lane).
    //
    // What it does NOT cover: hide blocks, text overlays, image overlays
    // (their state is left alone on undo), and destructive file operations
    // like crop/resize/stabilize/extract-audio that rewrite the source file
    // on disk.
    // =========================================================================

    private sealed class ClipSnapshot
    {
        public string SourceFile = "";
        public double OriginalDuration;
        public double InPoint;
        public double OutPoint;
        public double Speed = 1.0;
        public double Volume = 1.0;
        public double RotateDegrees;
        public bool FlipH;
        public bool FlipV;
        public int VideoWidth;
        public int VideoHeight;
        public int LoopCount = 1;
        public double TimelineStart;
        public bool IsAudioOnly;
        public double CanvasScale = 1.0;
        public double CanvasScaleX = 1.0;
        public double CanvasScaleY = 1.0;
        public double CanvasOffsetX;
        public double CanvasOffsetY;
        public double CanvasCropLeft;
        public double CanvasCropTop;
        public double CanvasCropRight;
        public double CanvasCropBottom;
        public Color AccentColor;

        public static ClipSnapshot FromClip(VideoClip c) => new()
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
            LoopCount = c.LoopCount,
            TimelineStart = c.TimelineStart,
            IsAudioOnly = c.IsAudioOnly,
            CanvasScale = c.CanvasScale,
            CanvasScaleX = c.CanvasScaleX,
            CanvasScaleY = c.CanvasScaleY,
            CanvasOffsetX = c.CanvasOffsetX,
            CanvasOffsetY = c.CanvasOffsetY,
            CanvasCropLeft = c.CanvasCropLeft,
            CanvasCropTop = c.CanvasCropTop,
            CanvasCropRight = c.CanvasCropRight,
            CanvasCropBottom = c.CanvasCropBottom,
            AccentColor = c.AccentColor,
        };

        public VideoClip ToClip() => new()
        {
            SourceFile = SourceFile,
            OriginalDuration = OriginalDuration,
            InPoint = InPoint,
            OutPoint = OutPoint,
            Speed = Speed,
            Volume = Volume,
            RotateDegrees = RotateDegrees,
            FlipH = FlipH,
            FlipV = FlipV,
            VideoWidth = VideoWidth,
            VideoHeight = VideoHeight,
            LoopCount = LoopCount,
            TimelineStart = TimelineStart,
            IsAudioOnly = IsAudioOnly,
            CanvasScale = CanvasScale,
            CanvasScaleX = CanvasScaleX,
            CanvasScaleY = CanvasScaleY,
            CanvasOffsetX = CanvasOffsetX,
            CanvasOffsetY = CanvasOffsetY,
            CanvasCropLeft = CanvasCropLeft,
            CanvasCropTop = CanvasCropTop,
            CanvasCropRight = CanvasCropRight,
            CanvasCropBottom = CanvasCropBottom,
            AccentColor = AccentColor,
        };
    }

    private sealed class UndoSnapshot
    {
        public List<ClipSnapshot> Clips = new();

        public bool StructurallyEquals(UndoSnapshot other)
        {
            if (Clips.Count != other.Clips.Count) return false;
            for (int i = 0; i < Clips.Count; i++)
            {
                var a = Clips[i]; var b = other.Clips[i];
                if (a.SourceFile != b.SourceFile) return false;
                if (a.IsAudioOnly != b.IsAudioOnly) return false;
                if (Math.Abs(a.RotateDegrees - b.RotateDegrees) > 0.001) return false;
                if (a.FlipH != b.FlipH) return false;
                if (a.FlipV != b.FlipV) return false;
                if (a.LoopCount != b.LoopCount) return false;
                if (Math.Abs(a.TimelineStart - b.TimelineStart) > 0.001) return false;
                if (Math.Abs(a.InPoint - b.InPoint) > 0.001) return false;
                if (Math.Abs(a.OutPoint - b.OutPoint) > 0.001) return false;
                if (Math.Abs(a.Speed - b.Speed) > 0.001) return false;
                if (Math.Abs(a.Volume - b.Volume) > 0.001) return false;
                if (Math.Abs(a.CanvasScale - b.CanvasScale) > 0.001) return false;
                if (Math.Abs(a.CanvasScaleX - b.CanvasScaleX) > 0.001) return false;
                if (Math.Abs(a.CanvasScaleY - b.CanvasScaleY) > 0.001) return false;
                if (Math.Abs(a.CanvasOffsetX - b.CanvasOffsetX) > 0.001) return false;
                if (Math.Abs(a.CanvasOffsetY - b.CanvasOffsetY) > 0.001) return false;
                if (Math.Abs(a.CanvasCropLeft - b.CanvasCropLeft) > 0.001) return false;
                if (Math.Abs(a.CanvasCropTop - b.CanvasCropTop) > 0.001) return false;
                if (Math.Abs(a.CanvasCropRight - b.CanvasCropRight) > 0.001) return false;
                if (Math.Abs(a.CanvasCropBottom - b.CanvasCropBottom) > 0.001) return false;
            }
            return true;
        }
    }

    private readonly List<UndoSnapshot> _undoHistory = new();
    private int _undoIndex = -1;
    private const int MaxUndoSteps = 50;
    private DispatcherTimer? _undoSnapshotTimer;
    private bool _undoRestoreInProgress;
    private bool _undoInitialized;

    private void InitUndoRedo()
    {
        if (_undoInitialized) return;
        _undoInitialized = true;

        // Snapshot the initial (empty) state so the very first user action can be undone
        // back to "empty timeline".
        _undoHistory.Add(CaptureUndoSnapshot());
        _undoIndex = 0;

        timeline.Clips.CollectionChanged += OnUndoClipsCollectionChanged;
        foreach (var c in timeline.Clips) c.PropertyChanged += OnUndoClipPropertyChanged;
    }

    private void OnUndoClipsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // Keep PropertyChanged subscriptions in sync with the live collection regardless
        // of whether we're restoring — otherwise newly-added clips after a restore would
        // silently miss property change tracking.
        if (e.NewItems != null)
            foreach (VideoClip c in e.NewItems) c.PropertyChanged += OnUndoClipPropertyChanged;
        if (e.OldItems != null)
            foreach (VideoClip c in e.OldItems) c.PropertyChanged -= OnUndoClipPropertyChanged;

        if (_undoRestoreInProgress) return;
        ScheduleUndoSnapshot();
    }

    private void OnUndoClipPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_undoRestoreInProgress) return;
        // EffectiveDuration is recomputed off other properties; ignore so we don't double-count.
        if (e.PropertyName == nameof(VideoClip.EffectiveDuration)) return;
        if (e.PropertyName == "DisplayName") return;
        ScheduleUndoSnapshot();
    }

    private void ScheduleUndoSnapshot()
    {
        _undoSnapshotTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _undoSnapshotTimer.Tick -= UndoSnapshotTimer_Tick;
        _undoSnapshotTimer.Tick += UndoSnapshotTimer_Tick;
        _undoSnapshotTimer.Stop();
        _undoSnapshotTimer.Start();
    }

    private void UndoSnapshotTimer_Tick(object? sender, EventArgs e)
    {
        _undoSnapshotTimer?.Stop();
        PushUndoSnapshot();
    }

    private void PushUndoSnapshot()
    {
        var snap = CaptureUndoSnapshot();
        // Skip when the snapshot is identical to the current head — avoids piling up
        // duplicates from no-op PropertyChanged events.
        if (_undoIndex >= 0 && _undoIndex < _undoHistory.Count && snap.StructurallyEquals(_undoHistory[_undoIndex]))
            return;
        // Drop any redo states past the current head — taking a new action invalidates them.
        if (_undoIndex < _undoHistory.Count - 1)
            _undoHistory.RemoveRange(_undoIndex + 1, _undoHistory.Count - _undoIndex - 1);
        _undoHistory.Add(snap);
        _undoIndex = _undoHistory.Count - 1;
        // Cap the history so it can't grow unbounded.
        while (_undoHistory.Count > MaxUndoSteps)
        {
            _undoHistory.RemoveAt(0);
            _undoIndex--;
        }
    }

    private UndoSnapshot CaptureUndoSnapshot() => new()
    {
        Clips = timeline.Clips.Select(ClipSnapshot.FromClip).ToList()
    };

    private void FlushPendingUndoSnapshot()
    {
        if (_undoSnapshotTimer != null && _undoSnapshotTimer.IsEnabled)
        {
            _undoSnapshotTimer.Stop();
            PushUndoSnapshot();
        }
    }

    private void UndoRedo_Undo()
    {
        if (!_undoInitialized) return;
        FlushPendingUndoSnapshot();
        if (_undoIndex <= 0) { status.Text = "Nothing to undo."; return; }
        _undoIndex--;
        RestoreUndoSnapshot(_undoHistory[_undoIndex]);
        status.Text = $"Undo. ({_undoIndex + 1}/{_undoHistory.Count})";
    }

    private void UndoRedo_Redo()
    {
        if (!_undoInitialized) return;
        if (_undoIndex >= _undoHistory.Count - 1) { status.Text = "Nothing to redo."; return; }
        _undoIndex++;
        RestoreUndoSnapshot(_undoHistory[_undoIndex]);
        status.Text = $"Redo. ({_undoIndex + 1}/{_undoHistory.Count})";
    }

    private void RestoreUndoSnapshot(UndoSnapshot snap)
    {
        _undoRestoreInProgress = true;
        try
        {
            // If something is currently playing it may be tied to a clip instance we're about
            // to replace — stop first so the player doesn't end up holding a dangling reference.
            if (_playingClip != null) { try { videoView.Stop(); } catch { } }
            _playingClip = null;
            _selectedClip = null;
            _selectedAudio = null;

            // Replace the clip list with fresh instances built from the snapshot.
            timeline.Clips.Clear();
            foreach (var cs in snap.Clips)
                timeline.Clips.Add(cs.ToClip());

            timeline.ClearAllSelection();
            timeline.FullRefresh();

            // Re-sync the inspector tab — we just blew away the selection so nothing should
            // be showing a stale Clip / Block panel.
            ShowInspectorTab(IsInlineRecorderVisible() ? "recorder" : "none");
            UpdatePreviewAspect();
            UpdateTopbarDims();
            UpdateExportFpsControls();
        }
        finally
        {
            _undoRestoreInProgress = false;
        }
    }
}
