using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using VideoEditor.Models;
using VideoEditor.Services;

namespace VideoEditor.Controls;

public partial class Timeline : UserControl
{
    public double TotalSeconds { get; private set; } = 30;
    public double CurrentSeconds { get; private set; }
    public double PixelsPerSecond { get; private set; } = VideoEditor.Services.AppSettings.InitialPixelsPerSecond;

    public ObservableCollection<VideoBlock> Blocks { get; } = new();
    public ObservableCollection<VideoClip> Clips { get; } = new();
    public ObservableCollection<TextOverlay> TextOverlays { get; } = new();

    public event Action<double>? Seek;
    public event Action<VideoBlock>? BlockSelected;
    public event Action<VideoClip>? ClipSelected;
    public event Action<VideoClip>? ClipDoubleClicked;
    public event Action<VideoClip, string>? ClipContextAction;
    public event Action<string[], double>? FilesDropped;
    public event Action? ClipsChanged;
    public event Action? BlocksChanged;
    public event Action? TextOverlaysChanged;
    public event Action<TextOverlay>? TextOverlaySelected;
    public event Action<TextOverlay, string>? TextOverlayContextAction;
    /// <summary>Fires when a single overlay's properties / timing change (timeline-bar drag or picker save).
    /// MainWindow uses this to mark only that overlay's preview control as dirty rather than restyling
    /// every overlay on every Tick - a big difference when there are ~40 AI-generated captions.</summary>
    public event Action<TextOverlay>? TextOverlayChanged;
    public event Action<VideoClip, double>? ClipScrubPreview;
    public event Action<VideoClip>? ClipEdgeDragEnded;
    public event Action<VideoClip>? AudioSelected;
    public event Action<VideoClip, string>? AudioContextAction;

    internal void RaiseTextOverlaySelected(TextOverlay o) { SelectTextOverlay(o); TextOverlaySelected?.Invoke(o); }
    internal void RaiseTextOverlayContext(TextOverlay o, string action) => TextOverlayContextAction?.Invoke(o, action);

    private TextOverlay? _selectedTextOverlay;
    public TextOverlay? SelectedTextOverlay => _selectedTextOverlay;
    public void SelectTextOverlay(TextOverlay? o)
    {
        ClearAllSelectionSets();
        _selectedTextOverlay = o;
        if (o != null) _selectedTextOverlays.Add(o);
        RefreshAllVisuals();
        SelectionChanged?.Invoke();
    }
    private void ClearTextOverlaySelectionVisuals() { foreach (var bb in _textBars.Values) bb.SetSelected(false); _selectedTextOverlay = null; }

    internal void RaiseClipScrub(VideoClip c, double sourceTime) => ClipScrubPreview?.Invoke(c, sourceTime);
    internal void RaiseClipEdgeDragEnded(VideoClip c) => ClipEdgeDragEnded?.Invoke(c);
    internal void RaiseAudioSelected(VideoClip c) { SelectAudio(c); AudioSelected?.Invoke(c); }
    internal void RaiseAudioContext(VideoClip c, string action) => AudioContextAction?.Invoke(c, action);

    private VideoClip? _selectedAudio;
    public void SelectAudio(VideoClip? c)
    {
        ClearAllSelectionSets();
        _selectedAudio = c;
        if (c != null) _selectedAudios.Add(c);
        RefreshAllVisuals();
        SelectionChanged?.Invoke();
    }
    private void UpdateAudioBarSelection() { foreach (var kv in _audioBars) kv.Value.SetSelected(_selectedAudios.Contains(kv.Key)); }

    private Line? _trimMarker;
    private Border? _trimMarkerLabel;
    private TextBlock? _trimMarkerText;

    private void EnsureTrimMarker()
    {
        if (_trimMarker != null) return;
        _trimMarker = new Line
        {
            Y1 = 0, Y2 = 400,
            Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 5, 3 },
            IsHitTestVisible = false
        };
        playheadLayer.Children.Add(_trimMarker);

        _trimMarkerLabel = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2, 6, 2),
            IsHitTestVisible = false
        };
        _trimMarkerText = new TextBlock
        {
            Foreground = Brushes.Black,
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            FontFamily = new FontFamily("Consolas")
        };
        _trimMarkerLabel.Child = _trimMarkerText;
        playheadLayer.Children.Add(_trimMarkerLabel);
    }

    internal void ShowTrimMarker(double timelineX, double sourceTime)
    {
        EnsureTrimMarker();
        if (_trimMarker == null || _trimMarkerLabel == null || _trimMarkerText == null) return;
        Canvas.SetLeft(_trimMarker, timelineX);
        Canvas.SetTop(_trimMarker, 0);
        _trimMarker.Y2 = Math.Max(120, content.ActualHeight);
        _trimMarkerText.Text = FormatTime(sourceTime);
        _trimMarkerLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var w = _trimMarkerLabel.DesiredSize.Width;
        Canvas.SetLeft(_trimMarkerLabel, timelineX - w / 2);
        Canvas.SetTop(_trimMarkerLabel, 24);
        _trimMarker.Visibility = Visibility.Visible;
        _trimMarkerLabel.Visibility = Visibility.Visible;
        Panel.SetZIndex(_trimMarker, 200);
        Panel.SetZIndex(_trimMarkerLabel, 200);
    }

    internal void HideTrimMarker()
    {
        if (_trimMarker != null) _trimMarker.Visibility = Visibility.Collapsed;
        if (_trimMarkerLabel != null) _trimMarkerLabel.Visibility = Visibility.Collapsed;
    }

    // ===== Insert / Reorder indicator =====
    private Line? _insertLine;
    private Polygon? _insertArrowTop;
    private Polygon? _insertArrowBottom;

    private void EnsureInsertIndicator()
    {
        if (_insertLine != null) return;
        var green = Color.FromRgb(0x4D, 0xFF, 0x88);
        _insertLine = new Line
        {
            Y1 = 0, Y2 = 300,
            Stroke = new SolidColorBrush(green),
            StrokeThickness = 3,
            IsHitTestVisible = false,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = green, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.9
            }
        };
        playheadLayer.Children.Add(_insertLine);

        _insertArrowTop = new Polygon
        {
            Fill = new SolidColorBrush(green),
            Points = new PointCollection { new Point(-9, 0), new Point(9, 0), new Point(0, 14) },
            IsHitTestVisible = false
        };
        playheadLayer.Children.Add(_insertArrowTop);

        _insertArrowBottom = new Polygon
        {
            Fill = new SolidColorBrush(green),
            Points = new PointCollection { new Point(-9, 0), new Point(9, 0), new Point(0, -14) },
            IsHitTestVisible = false
        };
        playheadLayer.Children.Add(_insertArrowBottom);

        Panel.SetZIndex(_insertLine, 180);
        Panel.SetZIndex(_insertArrowTop, 180);
        Panel.SetZIndex(_insertArrowBottom, 180);
    }

    internal void ShowInsertIndicator(double timelineX)
    {
        EnsureInsertIndicator();
        if (_insertLine == null || _insertArrowTop == null || _insertArrowBottom == null) return;
        var h = Math.Max(120, content.ActualHeight);
        Canvas.SetLeft(_insertLine, timelineX);
        Canvas.SetTop(_insertLine, 0);
        _insertLine.Y2 = h;
        Canvas.SetLeft(_insertArrowTop, timelineX);
        Canvas.SetTop(_insertArrowTop, 24);
        Canvas.SetLeft(_insertArrowBottom, timelineX);
        Canvas.SetTop(_insertArrowBottom, h - 4);
        _insertLine.Visibility = Visibility.Visible;
        _insertArrowTop.Visibility = Visibility.Visible;
        _insertArrowBottom.Visibility = Visibility.Visible;
    }

    internal void HideInsertIndicator()
    {
        if (_insertLine != null) _insertLine.Visibility = Visibility.Collapsed;
        if (_insertArrowTop != null) _insertArrowTop.Visibility = Visibility.Collapsed;
        if (_insertArrowBottom != null) _insertArrowBottom.Visibility = Visibility.Collapsed;
    }

    // Where the indicator should appear: the cursor position, magnetically snapped to nearby clip edges.
    internal double ComputeInsertSlot(VideoClip? excludeTarget, double cursorSec)
    {
        double snapThresh = VideoEditor.Services.AppSettings.SnapThreshold;
        var others = Clips.Where(c => c != excludeTarget && !c.IsAudioOnly).ToList();
        double best = Math.Max(0, cursorSec);
        foreach (var c in others)
        {
            var endTime = c.TimelineStart + c.EffectiveDuration;
            if (Math.Abs(cursorSec - endTime) < snapThresh) return endTime;
            if (Math.Abs(cursorSec - c.TimelineStart) < snapThresh) return c.TimelineStart;
        }
        return best;
    }

    // When a clip is added with a deliberate position, this flag prevents ClipsOnChanged
    // from auto-relocating it to the end-of-timeline (which would defeat the explicit drop position).
    private readonly HashSet<VideoClip> _explicitlyPositioned = new();

    // Place a clip at cursorSec on the timeline.
    // Behavior depends on AppSettings.RippleMode:
    //  - Free (default): clip stays at cursor, magnetic snap to nearby edges, overlap resolved by push-right.
    //  - Ripple: clips abut sequentially; the dropped clip inserts at the appropriate position and others ripple.
    internal void ReorderClipToPosition(VideoClip target, double cursorSec)
    {
        if (target.IsAudioOnly)
        {
            target.TimelineStart = Math.Max(0, cursorSec);
            AddWithExplicitPosition(target);
        }
        else if (VideoEditor.Services.AppSettings.RippleMode)
        {
            RippleAbutPlacement(target, cursorSec);
        }
        else
        {
            target.TimelineStart = Math.Max(0, cursorSec);
            AddWithExplicitPosition(target);
            MagneticSnap(target);
            ResolveOverlaps(target);
        }
        RecomputeTotal();
        UpdateCanvasSize();
        UpdateAllClipPositions();
    }

    private void AddWithExplicitPosition(VideoClip target)
    {
        if (Clips.Contains(target)) return;
        _explicitlyPositioned.Add(target);
        Clips.Add(target);
    }

    private void RippleAbutPlacement(VideoClip target, double cursorSec)
    {
        // The "old" abut-everything behavior - kept under Ripple Mode toggle.
        var others = Clips.Where(c => c != target && !c.IsAudioOnly).OrderBy(c => c.TimelineStart).ToList();
        int insertIdx = others.Count;
        double pos = 0;
        for (int i = 0; i < others.Count; i++)
        {
            var c = others[i];
            var mid = pos + c.EffectiveDuration / 2;
            if (cursorSec < mid) { insertIdx = i; break; }
            pos += c.EffectiveDuration;
        }
        double targetFinalPos = 0;
        for (int i = 0; i < insertIdx; i++) targetFinalPos += others[i].EffectiveDuration;
        target.TimelineStart = targetFinalPos > 0.0001 ? targetFinalPos : 0.0002;
        AddWithExplicitPosition(target);
        others.Insert(insertIdx, target);
        double p = 0;
        foreach (var c in others) { c.TimelineStart = p; p += c.EffectiveDuration; }
    }

    private void MagneticSnap(VideoClip target)
    {
        double snapThresh = VideoEditor.Services.AppSettings.SnapThreshold;
        var others = Clips.Where(c => c != target && !c.IsAudioOnly).ToList();
        var ts = target.TimelineStart;
        var te = ts + target.EffectiveDuration;
        foreach (var c in others)
        {
            var cs = c.TimelineStart;
            var ce = cs + c.EffectiveDuration;
            // Snap target's left edge to c's right edge
            if (Math.Abs(ts - ce) < snapThresh) { target.TimelineStart = ce; return; }
            // Snap target's left edge to c's left edge
            if (Math.Abs(ts - cs) < snapThresh) { target.TimelineStart = cs; return; }
            // Snap target's right edge to c's left edge (place clip ending exactly where c starts)
            if (Math.Abs(te - cs) < snapThresh) { target.TimelineStart = cs - target.EffectiveDuration; return; }
        }
    }

    private void ResolveOverlaps(VideoClip target)
    {
        var others = Clips.Where(c => c != target && !c.IsAudioOnly).OrderBy(c => c.TimelineStart).ToList();
        int guard = 0;
        bool keepGoing = true;
        while (keepGoing && guard++ < 200)
        {
            keepGoing = false;
            var ts = target.TimelineStart;
            var te = ts + target.EffectiveDuration;
            foreach (var c in others)
            {
                var cs = c.TimelineStart;
                var ce = cs + c.EffectiveDuration;
                if (ts < ce - 0.001 && te > cs + 0.001)
                {
                    // Overlap - push past the end of this clip
                    target.TimelineStart = ce;
                    keepGoing = true;
                    break;
                }
            }
        }
    }

    private VideoBlock? _selectedBlock;
    private VideoClip? _selectedClip;

    // Multi-selection sets. The "_selectedXxx" singular fields above are the ANCHOR
    // (the most recently clicked item), which drives the inspector when exactly one
    // item is selected. The anchor is always a member of its set (or null when empty).
    private readonly HashSet<VideoClip> _selectedClips = new();
    private readonly HashSet<VideoBlock> _selectedBlocks = new();
    private readonly HashSet<TextOverlay> _selectedTextOverlays = new();
    private readonly HashSet<VideoClip> _selectedAudios = new();

    public IReadOnlyCollection<VideoClip> SelectedClips => _selectedClips;
    public IReadOnlyCollection<VideoBlock> SelectedBlocks => _selectedBlocks;
    public IReadOnlyCollection<TextOverlay> SelectedTextOverlays => _selectedTextOverlays;
    public IReadOnlyCollection<VideoClip> SelectedAudios => _selectedAudios;
    public int TotalSelectionCount => _selectedClips.Count + _selectedBlocks.Count + _selectedTextOverlays.Count + _selectedAudios.Count;

    public event Action? SelectionChanged;

    private readonly Dictionary<VideoClip, ClipBar> _clipBars = new();
    private readonly Dictionary<VideoClip, AudioBar> _audioBars = new();
    private readonly Dictionary<VideoBlock, BlockBar> _blockBars = new();
    private readonly Dictionary<TextOverlay, TextOverlayBar> _textBars = new();

    private bool _isDragging;

    public FFmpegService? FFmpeg { get; set; }

    // Set when user requests "Split Here" - the source-video seconds where to split.
    internal double LastRequestedSplitSourceSec { get; set; }

    public Timeline()
    {
        InitializeComponent();
        Blocks.CollectionChanged += BlocksOnChanged;
        Clips.CollectionChanged += ClipsOnChanged;
        TextOverlays.CollectionChanged += TextOverlaysOnChanged;
        zoomInBtn.Click += (_, _) => { PixelsPerSecond = Math.Min(400, PixelsPerSecond * 1.4); FullRefresh(); };
        zoomOutBtn.Click += (_, _) => { PixelsPerSecond = Math.Max(8, PixelsPerSecond / 1.4); FullRefresh(); };
        fitBtn.Click += (_, _) => FitToView();
        SizeChanged += (_, _) => FullRefresh();
        Loaded += (_, _) => FullRefresh();

        // Playhead dragging - direct mouse handling, 1:1 with cursor
        // Works from either the invisible Thumb (anywhere along the yellow line) or the visible yellow flag at top.
        double playheadDragStartSec = 0;
        Point playheadDragStartMouse = default;

        void StartPlayheadDrag(IInputElement target, MouseButtonEventArgs e)
        {
            playheadDragStartSec = CurrentSeconds;
            playheadDragStartMouse = e.GetPosition(playheadThumbLayer);
            target.CaptureMouse();
            e.Handled = true;
        }
        void PlayheadDragMove(IInputElement target, MouseEventArgs e)
        {
            if (target.IsMouseCaptured)
            {
                var p = e.GetPosition(playheadThumbLayer);
                var dxPx = p.X - playheadDragStartMouse.X;
                SetCurrent(playheadDragStartSec + dxPx / PixelsPerSecond);
                Seek?.Invoke(CurrentSeconds);
            }
        }
        void EndPlayheadDrag(IInputElement target, MouseButtonEventArgs e)
        {
            if (target.IsMouseCaptured)
            {
                target.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        playheadThumb.PreviewMouseLeftButtonDown += (s, e) => StartPlayheadDrag(playheadThumb, e);
        playheadThumb.PreviewMouseMove += (s, e) => PlayheadDragMove(playheadThumb, e);
        playheadThumb.PreviewMouseLeftButtonUp += (s, e) => EndPlayheadDrag(playheadThumb, e);

        playheadFlagBorder.PreviewMouseLeftButtonDown += (s, e) => StartPlayheadDrag(playheadFlagBorder, e);
        playheadFlagBorder.PreviewMouseMove += (s, e) => PlayheadDragMove(playheadFlagBorder, e);
        playheadFlagBorder.PreviewMouseLeftButtonUp += (s, e) => EndPlayheadDrag(playheadFlagBorder, e);

        // Click on ruler to seek + drag from ruler to scrub
        rulerCanvas.MouseLeftButtonDown += Ruler_MouseDown;
        rulerCanvas.MouseMove += Ruler_MouseMove;
        rulerCanvas.MouseLeftButtonUp += Ruler_MouseUp;
        rulerCanvas.Cursor = Cursors.Hand;
    }

    private bool _rulerDragging;
    private void Ruler_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _rulerDragging = true;
        rulerCanvas.CaptureMouse();
        var x = e.GetPosition(rulerCanvas).X;
        SetCurrent(x / PixelsPerSecond);
        Seek?.Invoke(CurrentSeconds);
    }
    private void Ruler_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_rulerDragging) return;
        var x = e.GetPosition(rulerCanvas).X;
        SetCurrent(x / PixelsPerSecond);
        Seek?.Invoke(CurrentSeconds);
    }
    private void Ruler_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _rulerDragging = false;
        rulerCanvas.ReleaseMouseCapture();
    }

    public void FitToView()
    {
        if (TotalSeconds <= 0) return;
        var avail = Math.Max(200, scroll.ActualWidth - 16);
        PixelsPerSecond = Math.Max(8, Math.Min(400, avail / TotalSeconds));
        FullRefresh();
    }

    public void SetCurrent(double seconds)
    {
        CurrentSeconds = Math.Max(0, Math.Min(TotalSeconds, seconds));
        if (playhead != null)
        {
            var x = CurrentSeconds * PixelsPerSecond;
            var h = Math.Max(80, content.ActualHeight);
            Canvas.SetLeft(playhead, x);
            Canvas.SetTop(playhead, 0);
            playhead.Y2 = h;
            // Thumb runs the full height so the line is draggable from anywhere
            Canvas.SetLeft(playheadThumb, x - 7);
            Canvas.SetTop(playheadThumb, 0);
            playheadThumb.Height = h;
            playheadFlagText.Text = FormatTime(CurrentSeconds);
            playheadFlagBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var flagW = Math.Max(playheadFlagBorder.DesiredSize.Width, playheadFlagBorder.ActualWidth);
            if (flagW < 30) flagW = 68;
            Canvas.SetLeft(playheadFlagBorder, x - flagW / 2);
            Canvas.SetTop(playheadFlagBorder, 0);
        }
        if (timeLabel != null) timeLabel.Text = $"{FormatTime(CurrentSeconds)} / {FormatTime(TotalSeconds)}";
    }

    public void SelectBlock(VideoBlock? b)
    {
        ClearAllSelectionSets();
        _selectedBlock = b;
        if (b != null) _selectedBlocks.Add(b);
        RefreshAllVisuals();
        SelectionChanged?.Invoke();
    }
    public void SelectClip(VideoClip? c)
    {
        ClearAllSelectionSets();
        _selectedClip = c;
        if (c != null) _selectedClips.Add(c);
        RefreshAllVisuals();
        SelectionChanged?.Invoke();
    }
    private void ClearClipSelectionVisuals() { foreach (var cb in _clipBars.Values) cb.SetSelected(false); }
    private void ClearBlockSelectionVisuals() { foreach (var bb in _blockBars.Values) bb.SetSelected(false); }

    private void ClearAllSelectionSets()
    {
        _selectedClip = null;
        _selectedBlock = null;
        _selectedTextOverlay = null;
        _selectedAudio = null;
        _selectedClips.Clear();
        _selectedBlocks.Clear();
        _selectedTextOverlays.Clear();
        _selectedAudios.Clear();
    }

    public void ClearAllSelection()
    {
        ClearAllSelectionSets();
        RefreshAllVisuals();
        SelectionChanged?.Invoke();
    }

    private void RefreshAllVisuals()
    {
        foreach (var kv in _clipBars) kv.Value.SetSelected(_selectedClips.Contains(kv.Key));
        foreach (var kv in _blockBars) kv.Value.SetSelected(_selectedBlocks.Contains(kv.Key));
        foreach (var kv in _textBars) kv.Value.SetSelected(_selectedTextOverlays.Contains(kv.Key));
        foreach (var kv in _audioBars) kv.Value.SetSelected(_selectedAudios.Contains(kv.Key));
    }

    // === Ctrl-aware click routers (called from each Bar's MouseLeftButtonDown).
    // Without Ctrl: replaces the selection with just this item. With Ctrl: toggles
    // membership in the matching set, unless a different type is currently selected
    // (per-type rule) - in that case the click acts like a fresh single-select.

    internal void HandleClipClick(VideoClip c, bool ctrl)
    {
        if (!ctrl) { SelectClip(c); return; }
        bool otherTypeSelected = _selectedBlocks.Count + _selectedTextOverlays.Count + _selectedAudios.Count > 0;
        if (otherTypeSelected) { SelectClip(c); return; }
        if (_selectedClips.Contains(c))
        {
            _selectedClips.Remove(c);
            if (_selectedClip == c) _selectedClip = _selectedClips.LastOrDefault();
        }
        else
        {
            _selectedClips.Add(c);
            _selectedClip = c;
        }
        RefreshAllVisuals();
        SelectionChanged?.Invoke();
    }

    internal void HandleBlockClick(VideoBlock b, bool ctrl)
    {
        if (!ctrl) { SelectBlock(b); return; }
        bool otherTypeSelected = _selectedClips.Count + _selectedTextOverlays.Count + _selectedAudios.Count > 0;
        if (otherTypeSelected) { SelectBlock(b); return; }
        if (_selectedBlocks.Contains(b))
        {
            _selectedBlocks.Remove(b);
            if (_selectedBlock == b) _selectedBlock = _selectedBlocks.LastOrDefault();
        }
        else
        {
            _selectedBlocks.Add(b);
            _selectedBlock = b;
        }
        RefreshAllVisuals();
        SelectionChanged?.Invoke();
    }

    internal void HandleTextOverlayClick(TextOverlay o, bool ctrl)
    {
        if (!ctrl) { SelectTextOverlay(o); return; }
        bool otherTypeSelected = _selectedClips.Count + _selectedBlocks.Count + _selectedAudios.Count > 0;
        if (otherTypeSelected) { SelectTextOverlay(o); return; }
        if (_selectedTextOverlays.Contains(o))
        {
            _selectedTextOverlays.Remove(o);
            if (_selectedTextOverlay == o) _selectedTextOverlay = _selectedTextOverlays.LastOrDefault();
        }
        else
        {
            _selectedTextOverlays.Add(o);
            _selectedTextOverlay = o;
        }
        RefreshAllVisuals();
        SelectionChanged?.Invoke();
    }

    internal void HandleAudioClick(VideoClip c, bool ctrl)
    {
        if (!ctrl) { SelectAudio(c); return; }
        bool otherTypeSelected = _selectedClips.Count + _selectedBlocks.Count + _selectedTextOverlays.Count > 0;
        if (otherTypeSelected) { SelectAudio(c); return; }
        if (_selectedAudios.Contains(c))
        {
            _selectedAudios.Remove(c);
            if (_selectedAudio == c) _selectedAudio = _selectedAudios.LastOrDefault();
        }
        else
        {
            _selectedAudios.Add(c);
            _selectedAudio = c;
        }
        RefreshAllVisuals();
        SelectionChanged?.Invoke();
    }

    // === Multi-drag: cache start positions of all selected items at DragStarted,
    // apply the same clamped delta to all of them on each DragDelta.

    private Dictionary<VideoClip, double>? _multiDragStartClipTL;
    private Dictionary<VideoBlock, (double start, double end)>? _multiDragStartBlocks;
    private Dictionary<TextOverlay, (double start, double end)>? _multiDragStartOverlays;
    private Dictionary<VideoClip, double>? _multiDragStartAudioTL;

    internal void BeginMultiDrag()
    {
        _multiDragStartClipTL = _selectedClips.Count > 1
            ? _selectedClips.ToDictionary(c => c, c => c.TimelineStart) : null;
        _multiDragStartBlocks = _selectedBlocks.Count > 1
            ? _selectedBlocks.ToDictionary(b => b, b => (b.StartSeconds, b.EndSeconds)) : null;
        _multiDragStartOverlays = _selectedTextOverlays.Count > 1
            ? _selectedTextOverlays.ToDictionary(o => o, o => (o.StartSeconds, o.EndSeconds)) : null;
        _multiDragStartAudioTL = _selectedAudios.Count > 1
            ? _selectedAudios.ToDictionary(c => c, c => c.TimelineStart) : null;
    }

    internal bool EndMultiDrag()
    {
        bool wasMulti = _multiDragStartClipTL != null || _multiDragStartBlocks != null
                       || _multiDragStartOverlays != null || _multiDragStartAudioTL != null;
        _multiDragStartClipTL = null;
        _multiDragStartBlocks = null;
        _multiDragStartOverlays = null;
        _multiDragStartAudioTL = null;
        return wasMulti;
    }

    internal bool TryApplyMultiClipDrag(double dxSec)
    {
        if (_multiDragStartClipTL == null) return false;
        double minStart = _multiDragStartClipTL.Values.Min();
        double clampedDx = Math.Max(dxSec, -minStart);
        foreach (var kv in _multiDragStartClipTL) kv.Key.TimelineStart = kv.Value + clampedDx;
        return true;
    }

    internal bool TryApplyMultiBlockDrag(double dxSec)
    {
        if (_multiDragStartBlocks == null) return false;
        double minStart = _multiDragStartBlocks.Values.Min(v => v.start);
        double clampedDx = Math.Max(dxSec, -minStart);
        foreach (var kv in _multiDragStartBlocks)
        {
            kv.Key.StartSeconds = kv.Value.start + clampedDx;
            kv.Key.EndSeconds = kv.Value.end + clampedDx;
        }
        return true;
    }

    internal bool TryApplyMultiTextOverlayDrag(double dxSec)
    {
        if (_multiDragStartOverlays == null) return false;
        double minStart = _multiDragStartOverlays.Values.Min(v => v.start);
        double clampedDx = Math.Max(dxSec, -minStart);
        foreach (var kv in _multiDragStartOverlays)
        {
            kv.Key.StartSeconds = kv.Value.start + clampedDx;
            kv.Key.EndSeconds = kv.Value.end + clampedDx;
            NotifyTextOverlayChanged(kv.Key);
        }
        return true;
    }

    internal bool TryApplyMultiAudioDrag(double dxSec)
    {
        if (_multiDragStartAudioTL == null) return false;
        double minStart = _multiDragStartAudioTL.Values.Min();
        double clampedDx = Math.Max(dxSec, -minStart);
        foreach (var kv in _multiDragStartAudioTL) kv.Key.TimelineStart = kv.Value + clampedDx;
        return true;
    }

    // Called by MarqueeSelector when the user clicks an empty canvas without dragging.
    internal void OnEmptyCanvasClick(Canvas canvas, Point pos)
    {
        SetCurrent(pos.X / PixelsPerSecond);
        Seek?.Invoke(CurrentSeconds);
        ClearAllSelection();
    }

    // Called by MarqueeSelector when the user finishes a rubber-band drag.
    internal void ApplyMarqueeSelection(MarqueeKind kind, Rect marquee, bool ctrl)
    {
        static bool Hits(Rect m, FrameworkElement el)
        {
            double l = Canvas.GetLeft(el); if (double.IsNaN(l)) l = 0;
            double t = Canvas.GetTop(el); if (double.IsNaN(t)) t = 0;
            double w = el.Width > 0 ? el.Width : (el.ActualWidth > 0 ? el.ActualWidth : 1);
            double h = el.Height > 0 ? el.Height : (el.ActualHeight > 0 ? el.ActualHeight : 1);
            return m.IntersectsWith(new Rect(l, t, w, h));
        }

        switch (kind)
        {
            case MarqueeKind.Clip:
            {
                var hits = _clipBars.Where(kv => Hits(marquee, kv.Value.Root)).Select(kv => kv.Key).ToList();
                bool otherType = _selectedBlocks.Count + _selectedTextOverlays.Count + _selectedAudios.Count > 0;
                if (!ctrl || otherType) ClearAllSelectionSets();
                foreach (var c in hits) _selectedClips.Add(c);
                if (hits.Count > 0) _selectedClip = hits.Last();
                else if (_selectedClip != null && !_selectedClips.Contains(_selectedClip)) _selectedClip = _selectedClips.LastOrDefault();
                break;
            }
            case MarqueeKind.Audio:
            {
                var hits = _audioBars.Where(kv => Hits(marquee, kv.Value.Root)).Select(kv => kv.Key).ToList();
                bool otherType = _selectedClips.Count + _selectedBlocks.Count + _selectedTextOverlays.Count > 0;
                if (!ctrl || otherType) ClearAllSelectionSets();
                foreach (var c in hits) _selectedAudios.Add(c);
                if (hits.Count > 0) _selectedAudio = hits.Last();
                else if (_selectedAudio != null && !_selectedAudios.Contains(_selectedAudio)) _selectedAudio = _selectedAudios.LastOrDefault();
                break;
            }
            case MarqueeKind.BlockOrText:
            {
                var blockHits = _blockBars.Where(kv => Hits(marquee, kv.Value.Root)).Select(kv => kv.Key).ToList();
                var textHits = _textBars.Where(kv => Hits(marquee, kv.Value.Root)).Select(kv => kv.Key).ToList();
                // Per-type rule: pick the type with more hits; tie → blocks.
                bool pickBlocks = blockHits.Count >= textHits.Count;
                bool otherType = pickBlocks
                    ? (_selectedClips.Count + _selectedTextOverlays.Count + _selectedAudios.Count > 0)
                    : (_selectedClips.Count + _selectedBlocks.Count + _selectedAudios.Count > 0);
                if (!ctrl || otherType) ClearAllSelectionSets();
                if (pickBlocks)
                {
                    foreach (var b in blockHits) _selectedBlocks.Add(b);
                    if (blockHits.Count > 0) _selectedBlock = blockHits.Last();
                }
                else
                {
                    foreach (var o in textHits) _selectedTextOverlays.Add(o);
                    if (textHits.Count > 0) _selectedTextOverlay = textHits.Last();
                }
                break;
            }
        }
        RefreshAllVisuals();
        SelectionChanged?.Invoke();
    }

    public double GetClipStart(VideoClip clip) => clip.TimelineStart;

    public VideoClip? GetClipAt(double seconds)
    {
        VideoClip? candidate = null;
        double bestStart = -1;
        foreach (var c in Clips)
        {
            if (seconds >= c.TimelineStart && seconds < c.TimelineStart + c.EffectiveDuration)
                return c;
            if (c.TimelineStart > seconds && (candidate == null || c.TimelineStart < bestStart))
            {
                candidate = c; bestStart = c.TimelineStart;
            }
        }
        return candidate ?? Clips.OrderByDescending(c => c.TimelineStart + c.EffectiveDuration).FirstOrDefault();
    }

    private void RecomputeTotal()
    {
        double max = 1;
        foreach (var c in Clips)
        {
            var end = c.TimelineStart + c.EffectiveDuration;
            if (end > max) max = end;
        }
        foreach (var b in Blocks)
        {
            if (!b.CoversWholeVideo)
            {
                var end = b.EndSeconds;
                if (end > max) max = end;
            }
        }
        foreach (var o in TextOverlays)
        {
            if (o.EndSeconds > max) max = o.EndSeconds;
        }
        TotalSeconds = Math.Max(1, max);
    }

    private void BlocksOnChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (VideoBlock b in e.NewItems)
            {
                b.PropertyChanged += (_, _) => OnBlockPropChanged(b);
                AddBlockBar(b);
            }
        if (e.OldItems != null)
            foreach (VideoBlock b in e.OldItems)
            {
                if (_blockBars.TryGetValue(b, out var bar))
                {
                    trackCanvas.Children.Remove(bar.Root);
                    _blockBars.Remove(b);
                }
            }
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            trackCanvas.Children.Clear();
            _blockBars.Clear();
        }
        RecomputeTotal();
        UpdateCanvasSize();
        UpdateAllBlockPositions();
        UpdateAllClipPositions(); // for "CoversWholeVideo" blocks affecting total
        UpdateInfoLabels();
        SetCurrent(CurrentSeconds);
        BlocksChanged?.Invoke();
    }

    private void TextOverlaysOnChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (TextOverlay o in e.NewItems)
                AddTextOverlayBar(o);
        if (e.OldItems != null)
            foreach (TextOverlay o in e.OldItems)
            {
                if (_textBars.TryGetValue(o, out var bar))
                {
                    trackCanvas.Children.Remove(bar.Root);
                    _textBars.Remove(o);
                }
                if (_selectedTextOverlay == o) _selectedTextOverlay = null;
            }
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var bar in _textBars.Values) trackCanvas.Children.Remove(bar.Root);
            _textBars.Clear();
            _selectedTextOverlay = null;
        }
        RecomputeTotal();
        UpdateCanvasSize();
        UpdateAllTextOverlayPositions();
        UpdateInfoLabels();
        SetCurrent(CurrentSeconds);
        TextOverlaysChanged?.Invoke();
    }

    private void AddTextOverlayBar(TextOverlay o)
    {
        var row = _blockBars.Count + _textBars.Count;
        var bar = new TextOverlayBar(o, this, row);
        _textBars[o] = bar;
        trackCanvas.Children.Add(bar.Root);
    }

    private void UpdateAllTextOverlayPositions()
    {
        int idx = _blockBars.Count;
        foreach (var bar in _textBars.Values)
        {
            bar.UpdatePosition(PixelsPerSecond, TotalSeconds, idx++);
        }
    }

    internal void NotifyTextOverlayChanged(TextOverlay o)
    {
        if (_textBars.TryGetValue(o, out var bar)) bar.UpdatePosition(PixelsPerSecond, TotalSeconds, null);
        RecomputeTotal();
        UpdateCanvasSize();
        UpdateInfoLabels();
        TextOverlayChanged?.Invoke(o);
        TextOverlaysChanged?.Invoke();
    }

    private void ClipsOnChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (VideoClip c in e.NewItems)
            {
                // Only auto-append-to-end when the caller didn't supply a deliberate position.
                bool wasExplicit = _explicitlyPositioned.Remove(c);
                if (!wasExplicit && c.TimelineStart <= 0.0001 && c.OriginalDuration > 0)
                {
                    double maxEnd = 0;
                    foreach (var other in Clips)
                    {
                        if (other == c) continue;
                        maxEnd = Math.Max(maxEnd, other.TimelineStart + other.EffectiveDuration);
                    }
                    c.TimelineStart = maxEnd;
                }
                c.PropertyChanged += (_, _) => OnClipPropChanged(c);
                AddClipBar(c);
                AddAudioBar(c);
            }
        if (e.OldItems != null)
            foreach (VideoClip c in e.OldItems)
            {
                if (_clipBars.TryGetValue(c, out var bar))
                {
                    clipCanvas.Children.Remove(bar.Root);
                    _clipBars.Remove(c);
                }
                if (_audioBars.TryGetValue(c, out var ab))
                {
                    audioCanvas.Children.Remove(ab.Root);
                    _audioBars.Remove(c);
                }
            }
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            clipCanvas.Children.Clear();
            audioCanvas.Children.Clear();
            _clipBars.Clear();
            _audioBars.Clear();
        }
        RecomputeTotal();
        UpdateCanvasSize();
        UpdateAllClipPositions();
        UpdateDropHint();
        UpdateInfoLabels();
        SetCurrent(CurrentSeconds);
        ClipsChanged?.Invoke();
    }

    private void OnClipPropChanged(VideoClip c)
    {
        RecomputeTotal();
        if (_clipBars.TryGetValue(c, out var bar))
        {
            bar.UpdatePosition(PixelsPerSecond);
            bar.UpdateLabels();
        }
        if (_audioBars.TryGetValue(c, out var ab))
        {
            ab.UpdatePosition(PixelsPerSecond);
        }
        UpdateCanvasSize();
        UpdateInfoLabels();
        if (!_isDragging) SetCurrent(CurrentSeconds);
    }

    private void OnBlockPropChanged(VideoBlock b)
    {
        RecomputeTotal();
        if (_blockBars.TryGetValue(b, out var bar))
        {
            bar.UpdatePosition(PixelsPerSecond, TotalSeconds);
        }
        UpdateCanvasSize();
        if (!_isDragging) SetCurrent(CurrentSeconds);
    }

    private void UpdateAllClipPositions()
    {
        foreach (var bar in _clipBars.Values) bar.UpdatePosition(PixelsPerSecond);
        foreach (var ab in _audioBars.Values) ab.UpdatePosition(PixelsPerSecond);
    }
    private void UpdateAllBlockPositions() { foreach (var bar in _blockBars.Values) bar.UpdatePosition(PixelsPerSecond, TotalSeconds); }

    private void UpdateCanvasSize()
    {
        double width = Math.Max(TotalSeconds * PixelsPerSecond, scroll.ActualWidth);
        content.Width = width;
        rulerCanvas.Width = width;
        clipCanvas.Width = width;
        audioCanvas.Width = width;
        trackCanvas.Width = width;
        trackCanvas.Height = Math.Max(60, (_blockBars.Count + _textBars.Count) * 30 + 12);
        playheadLayer.Width = width;
        playheadThumbLayer.Width = width;
    }

    private void UpdateInfoLabels()
    {
        if (clipCountLabel != null)
        {
            clipCountLabel.Text = VideoEditor.Services.Localization.IsHebrew
                ? $"{Clips.Count} קליפים · {Blocks.Count} בלוקים · {TextOverlays.Count} טקסטים · {FormatTime(TotalSeconds)}"
                : $"{Clips.Count} clips · {Blocks.Count} blocks · {TextOverlays.Count} texts · {FormatTime(TotalSeconds)}";
        }
        if (zoomLabel != null) zoomLabel.Text = $"{(int)(PixelsPerSecond / 60.0 * 100)}%";
        if (blockTrackCountLabel != null)
        {
            blockTrackCountLabel.Text = VideoEditor.Services.Localization.IsHebrew
                ? $"{_blockBars.Count + _textBars.Count} מסלולים"
                : $"{_blockBars.Count + _textBars.Count} tracks";
        }
    }

    private Border? _dropHint;
    private void UpdateDropHint()
    {
        if (Clips.Count == 0)
        {
            if (_dropHint == null)
            {
                _dropHint = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x36)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x66)),
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(20, 12, 20, 12),
                    IsHitTestVisible = false
                };
                _dropHint.Child = new TextBlock
                {
                    Text = "⬇ Drag video files here",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB0)),
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold
                };
                Canvas.SetLeft(_dropHint, 16);
                Canvas.SetTop(_dropHint, 14);
                clipCanvas.Children.Add(_dropHint);
            }
            _dropHint.Visibility = Visibility.Visible;
        }
        else if (_dropHint != null) _dropHint.Visibility = Visibility.Collapsed;
    }

    public void FullRefresh()
    {
        if (rulerCanvas == null) return;
        UpdateCanvasSize();
        RebuildRuler();
        UpdateAllClipPositions();
        UpdateAllBlockPositions();
        UpdateInfoLabels();
        UpdateDropHint();
        SetCurrent(CurrentSeconds);
    }

    private void RebuildRuler()
    {
        rulerCanvas.Children.Clear();
        double tickEvery = PixelsPerSecond switch
        {
            > 200 => 0.25,
            > 100 => 0.5,
            > 40 => 1,
            > 20 => 5,
            _ => 10
        };
        // Design tokens: major ticks 25% white, minor 12% white, labels --text-mute
        var majorStroke = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        var minorStroke = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
        var labelBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x91, 0xA8));
        for (double t = 0; t <= TotalSeconds; t += tickEvery)
        {
            bool major = Math.Abs(t / tickEvery % 4) < 0.001;
            var line = new Line
            {
                X1 = t * PixelsPerSecond, X2 = t * PixelsPerSecond,
                Y1 = major ? 6 : 14, Y2 = 22,
                Stroke = major ? majorStroke : minorStroke,
                StrokeThickness = 1
            };
            rulerCanvas.Children.Add(line);
            if (major)
            {
                var tb = new TextBlock
                {
                    Text = FormatTime(t),
                    Foreground = labelBrush,
                    FontSize = 9.5,
                    FontFamily = new FontFamily("Consolas")
                };
                Canvas.SetLeft(tb, t * PixelsPerSecond + 4);
                Canvas.SetTop(tb, 1);
                rulerCanvas.Children.Add(tb);
            }
        }
    }

    // ===== Clip Bar =====

    private void AddClipBar(VideoClip clip)
    {
        // Audio-only clips appear only in A1 lane, not in V1.
        if (clip.IsAudioOnly) return;
        var bar = new ClipBar(clip, this);
        _clipBars[clip] = bar;
        clipCanvas.Children.Add(bar.Root);
        bar.UpdatePosition(PixelsPerSecond);
    }

    private void AddAudioBar(VideoClip clip)
    {
        var bar = new AudioBar(clip, this);
        _audioBars[clip] = bar;
        audioCanvas.Children.Add(bar.Root);
        bar.UpdatePosition(PixelsPerSecond);
    }

    internal void RaiseClipSelected(VideoClip c) { _selectedClip = c; SelectClip(c); ClipSelected?.Invoke(c); }
    internal void RaiseClipDoubleClicked(VideoClip c) { ClipDoubleClicked?.Invoke(c); }
    internal void RaiseClipContext(VideoClip c, string action) { ClipContextAction?.Invoke(c, action); }
    internal void BeginDrag() { _isDragging = true; }
    internal void EndDrag()
    {
        _isDragging = false;
        // After moving a clip, allow overlap collision avoidance: snap closely-overlapping clips
        SnapAdjacentClips();
        RecomputeTotal();
        UpdateCanvasSize();
        UpdateAllClipPositions();
        UpdateInfoLabels();
        SetCurrent(CurrentSeconds);
        ClipsChanged?.Invoke();
    }

    private void SnapAdjacentClips()
    {
        const double snapThresh = 0.3;
        var sorted = Clips.OrderBy(c => c.TimelineStart).ToList();
        for (int i = 1; i < sorted.Count; i++)
        {
            var prev = sorted[i - 1];
            var cur = sorted[i];
            var prevEnd = prev.TimelineStart + prev.EffectiveDuration;
            if (Math.Abs(cur.TimelineStart - prevEnd) < snapThresh)
                cur.TimelineStart = prevEnd;
        }
    }

    // ===== Block Bar =====

    private void AddBlockBar(VideoBlock b)
    {
        var bar = new BlockBar(b, this, _blockBars.Count);
        _blockBars[b] = bar;
        trackCanvas.Children.Add(bar.Root);
        bar.UpdatePosition(PixelsPerSecond, TotalSeconds);
    }

    internal void RaiseBlockSelected(VideoBlock b) { _selectedBlock = b; SelectBlock(b); BlockSelected?.Invoke(b); }

    // ===== Mouse handlers =====

    private void trackCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == trackCanvas)
        {
            var pos = e.GetPosition(trackCanvas);
            SetCurrent(pos.X / PixelsPerSecond);
            Seek?.Invoke(CurrentSeconds);
        }
    }

    private void ClipCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == clipCanvas)
        {
            var pos = e.GetPosition(clipCanvas);
            SetCurrent(pos.X / PixelsPerSecond);
            Seek?.Invoke(CurrentSeconds);
            SelectClip(null);
        }
    }

    private void ClipCanvas_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var p = e.GetPosition(clipCanvas);
            var sec = p.X / PixelsPerSecond;
            var slotSec = ComputeInsertSlot(null, sec);
            ShowInsertIndicator(slotSec * PixelsPerSecond);
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }
    private void ClipCanvas_DragLeave(object sender, DragEventArgs e)
    {
        HideInsertIndicator();
    }
    private void ClipCanvas_Drop(object sender, DragEventArgs e)
    {
        HideInsertIndicator();
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            var p = e.GetPosition(clipCanvas);
            var sec = p.X / PixelsPerSecond;
            FilesDropped?.Invoke(files, sec);
        }
        e.Handled = true;
    }

    // Catch-all drop handlers on the timeline UserControl - so drops anywhere on the timeline
    // (ruler, blocks track, gaps, scrollbars) all insert clips at the right position.
    private void Timeline_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var p = e.GetPosition(clipCanvas);
            var sec = Math.Max(0, p.X / PixelsPerSecond);
            var slotSec = ComputeInsertSlot(null, sec);
            ShowInsertIndicator(slotSec * PixelsPerSecond);
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }
    private void Timeline_DragLeave(object sender, DragEventArgs e)
    {
        HideInsertIndicator();
    }
    private void Timeline_Drop(object sender, DragEventArgs e)
    {
        HideInsertIndicator();
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            var p = e.GetPosition(clipCanvas);
            var sec = Math.Max(0, p.X / PixelsPerSecond);
            FilesDropped?.Invoke(files, sec);
        }
        e.Handled = true;
    }

    public static string FormatTime(double sec)
    {
        if (double.IsNaN(sec) || sec < 0) sec = 0;
        var ts = TimeSpan.FromSeconds(sec);
        // For videos ≥ 1 hour, include hours so 1h 43m 50s shows as "01:43:50.008", not "43:50.008"
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
        return $"{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
    }
}

// ============================ ClipBar control ============================

internal class ClipBar
{
    public Grid Root { get; }
    public VideoClip Clip { get; }
    private readonly Timeline _owner;
    private readonly Rectangle _bg;
    private readonly Grid _thumbGrid;
    private readonly Border _labelBg;
    private readonly TextBlock _nameText;
    private readonly TextBlock _detailsText;
    private readonly Thumb _dragThumb;
    private readonly Thumb _leftHandle;
    private readonly Thumb _rightHandle;
    private CancellationTokenSource? _thumbCts;
    private double _lastThumbInPoint = -1;
    private double _lastThumbOutPoint = -1;

    // Edge-drag deferred state (so bar doesn't resize during drag)
    private double _dragStartInPoint;
    private double _dragStartOutPoint;
    private double _dragStartTimelineStart;

    public ClipBar(VideoClip clip, Timeline owner)
    {
        Clip = clip; _owner = owner;
        Root = new Grid { Height = 56, ClipToBounds = true };

        _bg = new Rectangle
        {
            RadiusX = 6, RadiusY = 6,
            Fill = MakeGradient(clip.AccentColor),
            Stroke = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1
        };
        Root.Children.Add(_bg);

        // Thumbnail strip
        _thumbGrid = new Grid { IsHitTestVisible = false, ClipToBounds = true, Margin = new Thickness(2) };
        Root.Children.Add(_thumbGrid);

        // Color tint over thumbnails (so the accent color still shows through)
        var tint = new Rectangle
        {
            RadiusX = 6, RadiusY = 6,
            Opacity = 0.25,
            IsHitTestVisible = false,
            Fill = new SolidColorBrush(clip.AccentColor)
        };
        Root.Children.Add(tint);

        // Label background (gradient fading at top for readability)
        _labelBg = new Border
        {
            Height = 22,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            CornerRadius = new CornerRadius(6, 6, 0, 0),
            Background = new LinearGradientBrush(
                Color.FromArgb(0xCC, 0, 0, 0),
                Color.FromArgb(0x00, 0, 0, 0),
                90)
        };
        Root.Children.Add(_labelBg);

        var labelStack = new StackPanel { Margin = new Thickness(8, 2, 8, 2), IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top };
        _nameText = new TextBlock { Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        _detailsText = new TextBlock { Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), FontSize = 9, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 1, 0, 0) };
        labelStack.Children.Add(_nameText);
        labelStack.Children.Add(_detailsText);
        Root.Children.Add(labelStack);

        _dragThumb = new Thumb { Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Margin = new Thickness(10, 0, 10, 0) };
        _dragThumb.Template = TransparentThumbTemplate();
        Root.Children.Add(_dragThumb);

        _leftHandle = new Thumb { Width = 8, HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.SizeWE };
        _leftHandle.Template = HandleTemplate();
        Root.Children.Add(_leftHandle);

        _rightHandle = new Thumb { Width = 8, HorizontalAlignment = HorizontalAlignment.Right, Cursor = Cursors.SizeWE };
        _rightHandle.Template = HandleTemplate();
        Root.Children.Add(_rightHandle);

        Canvas.SetTop(Root, 6);
        UpdateLabels();

        // PreviewMouseLeftButtonDown fires BEFORE the Thumb captures the click,
        // so Shift+Click works even when clicking the drag area / handles.
        Root.PreviewMouseLeftButtonDown += (s, e) =>
        {
            if (e.ClickCount == 1 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                var p = e.GetPosition(Root);
                var w = Root.Width > 0 ? Root.Width : 1;
                var fraction = Math.Max(0.05, Math.Min(0.95, p.X / w));
                _owner.LastRequestedSplitSourceSec = Clip.InPoint + fraction * (Clip.OutPoint - Clip.InPoint);
                _owner.HandleClipClick(Clip, false);
                _owner.RaiseClipContext(Clip, "splitHere");
                e.Handled = true;
            }
        };
        Root.MouseLeftButtonDown += (s, e) =>
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            _owner.HandleClipClick(Clip, ctrl);
            if (e.ClickCount == 2) _owner.RaiseClipDoubleClicked(Clip);
            e.Handled = true;
        };
        Root.MouseRightButtonUp += (s, e) =>
        {
            // Remember where the user right-clicked (for "Split Here")
            var p = e.GetPosition(Root);
            var w = Root.Width > 0 ? Root.Width : 1;
            var fraction = Math.Max(0.05, Math.Min(0.95, p.X / w));
            _owner.LastRequestedSplitSourceSec = Clip.InPoint + fraction * (Clip.OutPoint - Clip.InPoint);
            if (!_owner.SelectedClips.Contains(Clip)) _owner.HandleClipClick(Clip, false);
            ShowContextMenu();
            e.Handled = true;
        };

        // === Body drag - FREE positioning. Clip follows cursor, gaps are allowed.
        // On release: magnetic snap to nearby edges + overlap resolution if any. ===
        _dragThumb.DragStarted += (_, _) =>
        {
            // Don't reset selection if this clip is already part of a multi-select.
            if (!_owner.SelectedClips.Contains(Clip)) _owner.HandleClipClick(Clip, false);
            _owner.BeginMultiDrag();
            _owner.BeginDrag();
            _dragStartTimelineStart = Clip.TimelineStart;
            _bg.Opacity = 0.7;
            Panel.SetZIndex(Root, 100);
        };
        _dragThumb.DragDelta += (_, e) =>
        {
            var totalDxSec = e.HorizontalChange / _owner.PixelsPerSecond;
            if (_owner.TryApplyMultiClipDrag(totalDxSec)) return;
            Clip.TimelineStart = Math.Max(0, _dragStartTimelineStart + totalDxSec);
            // No insert indicator during free drag - the clip itself shows you where it's going.
        };
        _dragThumb.DragCompleted += (_, _) =>
        {
            _bg.Opacity = 1.0;
            Panel.SetZIndex(Root, 0);
            _owner.HideInsertIndicator();
            bool wasMulti = _owner.EndMultiDrag();
            // Snap + overlap-resolve only for single-clip drags; multi-drag keeps free positions.
            if (!wasMulti) _owner.ReorderClipToPosition(Clip, Clip.TimelineStart);
            _owner.EndDrag();
        };

        // === Left edge drag - DEFERRED ===
        // Note: WPF Thumb.DragDelta.HorizontalChange is CUMULATIVE since drag start (not incremental).
        // Use it directly - do NOT accumulate.
        _leftHandle.DragStarted += (_, _) =>
        {
            // Trim handles stay single-item; force single-select on this clip.
            _owner.HandleClipClick(Clip, false);
            _dragStartInPoint = Clip.InPoint;
            _dragStartOutPoint = Clip.OutPoint;
            _dragStartTimelineStart = Clip.TimelineStart;
            _owner.RaiseClipScrub(Clip, Clip.InPoint);
            var x0 = _dragStartTimelineStart * _owner.PixelsPerSecond;
            _owner.ShowTrimMarker(x0, _dragStartInPoint);
        };
        _leftHandle.DragDelta += (_, e) =>
        {
            var totalDxSec = e.HorizontalChange / _owner.PixelsPerSecond;
            var speed = Math.Max(0.01, Clip.Speed);
            var dxSource = totalDxSec * speed;
            var targetIn = Math.Max(0, Math.Min(_dragStartOutPoint - 0.1, _dragStartInPoint + dxSource));
            var actualDxSource = targetIn - _dragStartInPoint;
            var actualDxTimeline = actualDxSource / speed;
            var targetTLStart = Math.Max(0, _dragStartTimelineStart + actualDxTimeline);
            var markerX = targetTLStart * _owner.PixelsPerSecond;
            _owner.ShowTrimMarker(markerX, targetIn);
            _owner.RaiseClipScrub(Clip, targetIn);
        };
        _leftHandle.DragCompleted += (_, e) =>
        {
            _owner.HideTrimMarker();
            var totalDxSec = e.HorizontalChange / _owner.PixelsPerSecond;
            var speed = Math.Max(0.01, Clip.Speed);
            var dxSource = totalDxSec * speed;
            var newIn = Math.Max(0, Math.Min(_dragStartOutPoint - 0.1, _dragStartInPoint + dxSource));
            var actualDxSource = newIn - _dragStartInPoint;
            var actualDxTimeline = actualDxSource / speed;
            _owner.BeginDrag();
            Clip.InPoint = newIn;
            Clip.TimelineStart = Math.Max(0, _dragStartTimelineStart + actualDxTimeline);
            _owner.EndDrag();
            _owner.RaiseClipEdgeDragEnded(Clip);
            ScheduleThumbnailRefresh();
        };

        // === Right edge drag - DEFERRED ===
        _rightHandle.DragStarted += (_, _) =>
        {
            _owner.HandleClipClick(Clip, false);
            _dragStartInPoint = Clip.InPoint;
            _dragStartOutPoint = Clip.OutPoint;
            _dragStartTimelineStart = Clip.TimelineStart;
            _owner.RaiseClipScrub(Clip, Clip.OutPoint);
            var x0 = (_dragStartTimelineStart + (_dragStartOutPoint - _dragStartInPoint) / Math.Max(0.01, Clip.Speed)) * _owner.PixelsPerSecond;
            _owner.ShowTrimMarker(x0, _dragStartOutPoint);
        };
        _rightHandle.DragDelta += (_, e) =>
        {
            var totalDxSec = e.HorizontalChange / _owner.PixelsPerSecond;
            var speed = Math.Max(0.01, Clip.Speed);
            var dxSource = totalDxSec * speed;
            var targetOut = Math.Min(Clip.OriginalDuration, Math.Max(_dragStartInPoint + 0.1, _dragStartOutPoint + dxSource));
            var newEffDur = (targetOut - _dragStartInPoint) / speed;
            var markerX = (_dragStartTimelineStart + newEffDur) * _owner.PixelsPerSecond;
            _owner.ShowTrimMarker(markerX, targetOut);
            _owner.RaiseClipScrub(Clip, targetOut);
        };
        _rightHandle.DragCompleted += (_, e) =>
        {
            _owner.HideTrimMarker();
            var totalDxSec = e.HorizontalChange / _owner.PixelsPerSecond;
            var speed = Math.Max(0.01, Clip.Speed);
            var dxSource = totalDxSec * speed;
            var newOut = Math.Min(Clip.OriginalDuration, Math.Max(_dragStartInPoint + 0.1, _dragStartOutPoint + dxSource));
            _owner.BeginDrag();
            Clip.OutPoint = newOut;
            _owner.EndDrag();
            _owner.RaiseClipEdgeDragEnded(Clip);
            ScheduleThumbnailRefresh();
        };

        // Kick off thumbnail load
        ScheduleThumbnailRefresh();
    }

    private void ScheduleThumbnailRefresh()
    {
        if (_owner.FFmpeg == null) return;
        if (Math.Abs(Clip.InPoint - _lastThumbInPoint) < 0.05 && Math.Abs(Clip.OutPoint - _lastThumbOutPoint) < 0.05 && _thumbGrid.Children.Count > 0)
            return;

        _thumbCts?.Cancel();
        _thumbCts = new CancellationTokenSource();
        var token = _thumbCts.Token;
        _lastThumbInPoint = Clip.InPoint;
        _lastThumbOutPoint = Clip.OutPoint;

        var dispatcher = Root.Dispatcher;
        var sourceFile = Clip.SourceFile;
        var inSec = Clip.InPoint;
        var outSec = Clip.OutPoint;
        var ff = _owner.FFmpeg;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(150, token); // debounce
                if (token.IsCancellationRequested) return;
                if (!System.IO.File.Exists(sourceFile)) return;
                long stamp = 0;
                try { stamp = new System.IO.FileInfo(sourceFile).LastWriteTimeUtc.Ticks; } catch { }
                int count = VideoEditor.Services.AppSettings.ThumbnailsPerClip;
                var key = $"{System.IO.Path.GetFileNameWithoutExtension(sourceFile)}_{stamp}_{(int)(inSec * 1000)}_{(int)(outSec * 1000)}_{count}";
                var paths = await ff.ExtractThumbnailStripAsync(sourceFile, inSec, outSec, count, 160, key);
                if (token.IsCancellationRequested) return;
                dispatcher.Invoke(() => ApplyThumbnails(paths));
            }
            catch { }
        });
    }

    private void ApplyThumbnails(List<string> paths)
    {
        _thumbGrid.Children.Clear();
        _thumbGrid.ColumnDefinitions.Clear();
        if (paths.Count == 0) return;
        for (int i = 0; i < paths.Count; i++)
        {
            _thumbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.UriSource = new Uri(paths[i]);
                bmp.EndInit();
                bmp.Freeze();
                var img = new Image
                {
                    Source = bmp,
                    Stretch = Stretch.UniformToFill,
                    ClipToBounds = true
                };
                Grid.SetColumn(img, i);
                _thumbGrid.Children.Add(img);
            }
            catch { }
        }
    }

    public void UpdatePosition(double pps)
    {
        Canvas.SetLeft(Root, Clip.TimelineStart * pps);
        Root.Width = Math.Max(20, Clip.EffectiveDuration * pps);
    }

    public void UpdateLabels()
    {
        _nameText.Text = Clip.DisplayName;
        var sb = new System.Text.StringBuilder();
        sb.Append(Timeline.FormatTime(Clip.EffectiveDuration));
        if (Math.Abs(Clip.Speed - 1.0) > 0.01) sb.Append($"  •  {Clip.Speed:0.##}x");
        if (Clip.RotateDegrees != 0) sb.Append($"  •  ↻{Clip.RotateDegrees}°");
        if (Clip.FlipH || Clip.FlipV) sb.Append("  •  ⇄");
        if (Math.Abs(Clip.Volume - 1.0) > 0.01) sb.Append($"  •  🔊{Clip.Volume:0.##}");
        if (Clip.LoopCount > 1) sb.Append($"  •  🔁x{Clip.LoopCount}");
        _detailsText.Text = sb.ToString();
    }

    public void SetSelected(bool sel)
    {
        _bg.Stroke = new SolidColorBrush(sel ? Color.FromRgb(0xFF, 0xD4, 0x3B) : Color.FromArgb(0x2D, 0xFF, 0xFF, 0xFF));
        _bg.StrokeThickness = sel ? 2 : 1;
    }

    // Match the design's 90deg gradient: top = clip accent, bottom = same accent darkened ~45%
    private LinearGradientBrush MakeGradient(Color c)
        => new LinearGradientBrush(
            Color.FromArgb(0xFF, c.R, c.G, c.B),
            Color.FromArgb(0xFF, (byte)(c.R * 0.55), (byte)(c.G * 0.55), (byte)(c.B * 0.55)),
            90);

    private void ShowContextMenu()
    {
        var menu = new ContextMenu();
        void AddItem(string h, string action) { var mi = new MenuItem { Header = h }; mi.Click += (_, _) => _owner.RaiseClipContext(Clip, action); menu.Items.Add(mi); }
        AddItem("✂ Trim (In/Out)", "trim");
        AddItem("⏩ Change Speed", "speed");
        AddItem("🔊 Change Volume", "volume");
        AddItem("↻ Rotate 90°", "rotate90");
        AddItem("⇄ Flip Horizontal", "flipH");
        AddItem("⇅ Flip Vertical", "flipV");
        AddItem("🔁 Loop (Duplicate)", "loop");
        menu.Items.Add(new Separator());
        AddItem("✄ Crop (Export)", "crop");
        AddItem("🚫 Remove Logo (Export)", "removeLogo");
        AddItem("🖼 Add Image (Export)", "addImage");
        AddItem("🔤 Add Text (Export)", "addText");
        AddItem("🎵 Add Audio (Export)", "addAudio");
        AddItem("🤚 Stabilize (Export)", "stabilize");
        menu.Items.Add(new Separator());
        AddItem("🎵 Extract Audio…", "extractAudio");
        AddItem("🔇 Mute Clip", "mute");
        AddItem("🔊 Restore Audio", "unmute");
        AddItem("❌ Remove Audio Track", "removeAudioTrack");
        menu.Items.Add(new Separator());
        AddItem("✂ Split Here (Shift+Click)", "splitHere");
        AddItem("✂ Split at Playhead (S)", "split");
        AddItem("✂ Split into N Parts…", "splitN");
        AddItem("⧉ Duplicate", "duplicate");
        AddItem("🗑 Delete", "delete");
        menu.PlacementTarget = Root;
        menu.IsOpen = true;
    }

    public static ControlTemplate HandleTemplate()
    {
        var t = new ControlTemplate(typeof(Thumb));
        var border = new FrameworkElementFactory(typeof(Border));
        // Design: 4 px ledge handles at 55% white opacity
        border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF)));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(1));
        border.SetValue(Border.MarginProperty, new Thickness(0, 2, 0, 2));
        t.VisualTree = border;
        return t;
    }
    public static ControlTemplate TransparentThumbTemplate()
    {
        var t = new ControlTemplate(typeof(Thumb));
        var rect = new FrameworkElementFactory(typeof(Rectangle));
        rect.SetValue(Shape.FillProperty, Brushes.Transparent);
        t.VisualTree = rect;
        return t;
    }
}

// ============================ BlockBar control ============================

internal class BlockBar
{
    public Grid Root { get; }
    public VideoBlock Block { get; }
    private readonly Timeline _owner;
    private readonly Rectangle _bg;
    private readonly TextBlock _label;
    private readonly Thumb _dragThumb;
    private readonly Thumb _leftHandle;
    private readonly Thumb _rightHandle;
    private readonly int _row;
    private double _dragStartStart;
    private double _dragStartEnd;

    public BlockBar(VideoBlock b, Timeline owner, int row)
    {
        Block = b; _owner = owner; _row = row;
        Root = new Grid { Height = 26, ClipToBounds = true };

        _bg = new Rectangle
        {
            Fill = new LinearGradientBrush(Color.FromRgb(0x7C, 0x4D, 0xFF), Color.FromRgb(0x55, 0x33, 0xCC), 90),
            Opacity = 0.85,
            RadiusX = 4, RadiusY = 4,
            Stroke = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1
        };
        _label = new TextBlock { Text = b.Label, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), FontWeight = FontWeights.SemiBold, FontSize = 11, IsHitTestVisible = false };

        _leftHandle = new Thumb { Width = 6, HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.SizeWE };
        _leftHandle.Template = ClipBar.HandleTemplate();
        _rightHandle = new Thumb { Width = 6, HorizontalAlignment = HorizontalAlignment.Right, Cursor = Cursors.SizeWE };
        _rightHandle.Template = ClipBar.HandleTemplate();
        _dragThumb = new Thumb { Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Margin = new Thickness(8, 0, 8, 0) };
        _dragThumb.Template = ClipBar.TransparentThumbTemplate();

        Root.Children.Add(_bg);
        Root.Children.Add(_label);
        Root.Children.Add(_dragThumb);
        Root.Children.Add(_leftHandle);
        Root.Children.Add(_rightHandle);

        Canvas.SetTop(Root, row * 30 + 6);

        Root.MouseLeftButtonDown += (s, e) =>
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            _owner.HandleBlockClick(Block, ctrl);
            e.Handled = true;
        };

        _dragThumb.DragStarted += (_, _) =>
        {
            if (Block.CoversWholeVideo) Block.CoversWholeVideo = false;
            if (!_owner.SelectedBlocks.Contains(Block)) _owner.HandleBlockClick(Block, false);
            _owner.BeginMultiDrag();
            _owner.BeginDrag();
            _dragStartStart = Block.StartSeconds;
            _dragStartEnd = Block.EndSeconds;
        };
        _dragThumb.DragDelta += (_, e) =>
        {
            // Free positioning - block can be dragged anywhere on the timeline, even past current end.
            // The timeline auto-expands via RecomputeTotal (which considers block.EndSeconds).
            var totalDxSec = e.HorizontalChange / _owner.PixelsPerSecond;
            if (_owner.TryApplyMultiBlockDrag(totalDxSec)) return;
            var len = _dragStartEnd - _dragStartStart;
            var newStart = Math.Max(0, _dragStartStart + totalDxSec);
            Block.StartSeconds = newStart;
            Block.EndSeconds = newStart + len;
        };
        _dragThumb.DragCompleted += (_, _) => { _owner.EndMultiDrag(); _owner.EndDrag(); };

        _leftHandle.DragStarted += (_, _) =>
        {
            if (Block.CoversWholeVideo) Block.CoversWholeVideo = false;
            _owner.HandleBlockClick(Block, false);
            _owner.BeginDrag();
            _dragStartStart = Block.StartSeconds;
            _dragStartEnd = Block.EndSeconds;
        };
        _leftHandle.DragDelta += (_, e) =>
        {
            var totalDxSec = e.HorizontalChange / _owner.PixelsPerSecond;
            Block.StartSeconds = Math.Max(0, Math.Min(_dragStartEnd - 0.1, _dragStartStart + totalDxSec));
        };
        _leftHandle.DragCompleted += (_, _) => _owner.EndDrag();

        _rightHandle.DragStarted += (_, _) =>
        {
            if (Block.CoversWholeVideo) Block.CoversWholeVideo = false;
            _owner.HandleBlockClick(Block, false);
            _owner.BeginDrag();
            _dragStartStart = Block.StartSeconds;
            _dragStartEnd = Block.EndSeconds;
        };
        _rightHandle.DragDelta += (_, e) =>
        {
            var totalDxSec = e.HorizontalChange / _owner.PixelsPerSecond;
            Block.EndSeconds = Math.Max(_dragStartStart + 0.1, _dragStartEnd + totalDxSec);
        };
        _rightHandle.DragCompleted += (_, _) => _owner.EndDrag();
    }

    public void UpdatePosition(double pps, double totalSec)
    {
        if (Block.CoversWholeVideo)
        {
            Canvas.SetLeft(Root, 0);
            Root.Width = Math.Max(20, totalSec * pps);
        }
        else
        {
            Canvas.SetLeft(Root, Block.StartSeconds * pps);
            Root.Width = Math.Max(20, (Block.EndSeconds - Block.StartSeconds) * pps);
        }
        _label.Text = Block.Label;
    }

    public void SetSelected(bool sel)
    {
        _bg.Opacity = sel ? 1.0 : 0.92;
        _bg.Stroke = new SolidColorBrush(sel ? Color.FromRgb(0xFF, 0xD4, 0x3B) : Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF));
        _bg.StrokeThickness = sel ? 2 : 1;
    }
}

// ============================ TextOverlayBar control ============================
// Bar for a TextOverlay on the Overlays lane. Teal gradient to distinguish from blocks.
internal class TextOverlayBar
{
    public Grid Root { get; }
    public TextOverlay Overlay { get; }
    private readonly Timeline _owner;
    private readonly Rectangle _bg;
    private readonly TextBlock _label;
    private readonly Thumb _dragThumb;
    private readonly Thumb _leftHandle;
    private readonly Thumb _rightHandle;
    private int _row;
    private double _dragStartStart;
    private double _dragStartEnd;

    public TextOverlayBar(TextOverlay o, Timeline owner, int row)
    {
        Overlay = o; _owner = owner; _row = row;
        Root = new Grid { Height = 26, ClipToBounds = true };

        _bg = new Rectangle
        {
            Fill = new LinearGradientBrush(Color.FromRgb(0x14, 0xB8, 0xA6), Color.FromRgb(0x0F, 0x6E, 0x68), 90),
            Opacity = 0.9,
            RadiusX = 4, RadiusY = 4,
            Stroke = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1
        };
        _label = new TextBlock
        {
            Text = LabelText(),
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20, 0, 20, 0),
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            IsHitTestVisible = false,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var prefix = new TextBlock
        {
            Text = "T",
            Foreground = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 0, 0, 0),
            FontFamily = new FontFamily("Consolas"),
            FontWeight = FontWeights.Bold,
            FontSize = 10,
            IsHitTestVisible = false
        };

        _leftHandle = new Thumb { Width = 6, HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.SizeWE };
        _leftHandle.Template = ClipBar.HandleTemplate();
        _rightHandle = new Thumb { Width = 6, HorizontalAlignment = HorizontalAlignment.Right, Cursor = Cursors.SizeWE };
        _rightHandle.Template = ClipBar.HandleTemplate();
        _dragThumb = new Thumb { Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Margin = new Thickness(8, 0, 8, 0) };
        _dragThumb.Template = ClipBar.TransparentThumbTemplate();

        Root.Children.Add(_bg);
        Root.Children.Add(prefix);
        Root.Children.Add(_label);
        Root.Children.Add(_dragThumb);
        Root.Children.Add(_leftHandle);
        Root.Children.Add(_rightHandle);

        Canvas.SetTop(Root, _row * 30 + 6);

        Root.MouseLeftButtonDown += (_, e) =>
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            _owner.HandleTextOverlayClick(Overlay, ctrl);
            e.Handled = true;
        };

        Root.MouseRightButtonUp += (_, e) =>
        {
            var menu = new ContextMenu();
            var edit = new MenuItem { Header = "Edit text…" };
            edit.Click += (_, _) => _owner.RaiseTextOverlayContext(Overlay, "edit");
            menu.Items.Add(edit);
            var del = new MenuItem { Header = "Delete text" };
            del.Click += (_, _) => _owner.RaiseTextOverlayContext(Overlay, "delete");
            menu.Items.Add(del);
            menu.IsOpen = true;
            e.Handled = true;
        };

        _dragThumb.DragStarted += (_, _) =>
        {
            if (!_owner.SelectedTextOverlays.Contains(Overlay)) _owner.HandleTextOverlayClick(Overlay, false);
            _owner.BeginMultiDrag();
            _owner.BeginDrag();
            _dragStartStart = Overlay.StartSeconds;
            _dragStartEnd = Overlay.EndSeconds;
        };
        _dragThumb.DragDelta += (_, e) =>
        {
            var totalDxSec = e.HorizontalChange / _owner.PixelsPerSecond;
            if (_owner.TryApplyMultiTextOverlayDrag(totalDxSec)) return;
            var len = _dragStartEnd - _dragStartStart;
            var newStart = Math.Max(0, _dragStartStart + totalDxSec);
            Overlay.StartSeconds = newStart;
            Overlay.EndSeconds = newStart + len;
            _owner.NotifyTextOverlayChanged(Overlay);
        };
        _dragThumb.DragCompleted += (_, _) => { _owner.EndMultiDrag(); _owner.EndDrag(); };

        _leftHandle.DragStarted += (_, _) =>
        {
            _owner.HandleTextOverlayClick(Overlay, false);
            _owner.BeginDrag();
            _dragStartStart = Overlay.StartSeconds;
            _dragStartEnd = Overlay.EndSeconds;
        };
        _leftHandle.DragDelta += (_, e) =>
        {
            var totalDxSec = e.HorizontalChange / _owner.PixelsPerSecond;
            Overlay.StartSeconds = Math.Max(0, Math.Min(_dragStartEnd - 0.1, _dragStartStart + totalDxSec));
            _owner.NotifyTextOverlayChanged(Overlay);
        };
        _leftHandle.DragCompleted += (_, _) => _owner.EndDrag();

        _rightHandle.DragStarted += (_, _) =>
        {
            _owner.HandleTextOverlayClick(Overlay, false);
            _owner.BeginDrag();
            _dragStartStart = Overlay.StartSeconds;
            _dragStartEnd = Overlay.EndSeconds;
        };
        _rightHandle.DragDelta += (_, e) =>
        {
            var totalDxSec = e.HorizontalChange / _owner.PixelsPerSecond;
            Overlay.EndSeconds = Math.Max(_dragStartStart + 0.1, _dragStartEnd + totalDxSec);
            _owner.NotifyTextOverlayChanged(Overlay);
        };
        _rightHandle.DragCompleted += (_, _) => _owner.EndDrag();
    }

    public void UpdatePosition(double pps, double totalSec, int? newRow)
    {
        if (newRow.HasValue) { _row = newRow.Value; Canvas.SetTop(Root, _row * 30 + 6); }
        Canvas.SetLeft(Root, Overlay.StartSeconds * pps);
        Root.Width = Math.Max(28, (Overlay.EndSeconds - Overlay.StartSeconds) * pps);
        _label.Text = LabelText();
    }

    public void SetSelected(bool sel)
    {
        _bg.Opacity = sel ? 1.0 : 0.92;
        _bg.Stroke = new SolidColorBrush(sel ? Color.FromRgb(0xFF, 0xD4, 0x3B) : Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
        _bg.StrokeThickness = sel ? 2 : 1;
    }

    private string LabelText()
    {
        var t = (Overlay.Text ?? "").Replace("\r", "").Replace("\n", " ").Trim();
        if (t.Length > 40) t = t.Substring(0, 40) + "…";
        return string.IsNullOrEmpty(t) ? "(empty)" : t;
    }
}

// ============================ AudioBar control ============================
// Permanent waveform bar for each clip's audio - matches the A1 lane design.
internal class AudioBar
{
    public Grid Root { get; }
    public VideoClip Clip { get; }
    private readonly Timeline _owner;
    private readonly Rectangle _bg;
    private readonly Image _waveImg;
    private readonly Border _label;
    private readonly TextBlock _labelText;
    private readonly Rectangle _mutedStripes;
    private CancellationTokenSource? _waveCts;

    public AudioBar(VideoClip clip, Timeline owner)
    {
        Clip = clip; _owner = owner;
        Root = new Grid { Height = 52, ClipToBounds = true };

        // Audio-only clips get a purple gradient to visually distinguish them as their own entity.
        // Regular (clip-attached) audio bars get the design's blue gradient.
        var (top, bot, stroke) = clip.IsAudioOnly
            ? (Color.FromRgb(0x6E, 0x44, 0xD6), Color.FromRgb(0x3A, 0x22, 0x6E), Color.FromArgb(0x80, 0xC0, 0xA0, 0xFF))
            : (Color.FromRgb(0x1D, 0x3A, 0x6E), Color.FromRgb(0x12, 0x22, 0x44), Color.FromArgb(0x40, 0xA0, 0xB4, 0xFF));
        _bg = new Rectangle
        {
            RadiusX = 5, RadiusY = 5,
            Fill = new LinearGradientBrush(top, bot, 90),
            Stroke = new SolidColorBrush(stroke),
            StrokeThickness = 1
        };
        Root.Children.Add(_bg);

        _waveImg = new Image
        {
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
            Margin = new Thickness(2)
        };
        Root.Children.Add(_waveImg);

        // Diagonal red stripes when muted
        _mutedStripes = new Rectangle
        {
            RadiusX = 5, RadiusY = 5,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Fill = new DrawingBrush
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 8, 8),
                ViewportUnits = BrushMappingMode.Absolute,
                Drawing = new GeometryDrawing
                {
                    Brush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x6B, 0x6B)),
                    Geometry = Geometry.Parse("M0,8 L8,0 L8,4 L4,8 Z")
                }
            }
        };
        Root.Children.Add(_mutedStripes);

        _label = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 1, 5, 1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6, 4, 0, 0),
            IsHitTestVisible = false
        };
        _labelText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)),
            FontSize = 9.5,
            FontFamily = new FontFamily("Consolas"),
            FontWeight = FontWeights.SemiBold
        };
        _label.Child = _labelText;
        Root.Children.Add(_label);

        Canvas.SetTop(Root, 6);
        UpdateLabel();

        Root.Cursor = Cursors.Hand;
        Root.MouseLeftButtonDown += (s, e) =>
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            _owner.HandleAudioClick(Clip, ctrl);
            e.Handled = true;
        };
        Root.MouseRightButtonUp += (s, e) =>
        {
            if (!_owner.SelectedAudios.Contains(Clip)) _owner.HandleAudioClick(Clip, false);
            ShowAudioContextMenu();
            e.Handled = true;
        };

        // Audio-only clips are first-class timeline entities - independently draggable, trimmable,
        // splittable, and selectable. The body drag moves the clip; left/right edge handles trim.
        if (Clip.IsAudioOnly)
        {
            double dragStartTL = 0;
            Point dragStartMouse = default;
            bool multiAudioDrag = false;
            Root.Cursor = Cursors.SizeAll;
            Root.MouseLeftButtonDown += (s, e) =>
            {
                // Don't start a drag when the user is Shift-clicking to split (handled by the preview handler).
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) return;
                // Ctrl is the multi-select modifier; don't start a drag in that case.
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) return;
                if (e.ClickCount == 1)
                {
                    dragStartTL = Clip.TimelineStart;
                    dragStartMouse = e.GetPosition(_owner);
                    multiAudioDrag = _owner.SelectedAudios.Contains(Clip) && _owner.SelectedAudios.Count > 1;
                    if (multiAudioDrag) _owner.BeginMultiDrag();
                    Root.CaptureMouse();
                }
            };
            Root.MouseMove += (s, e) =>
            {
                if (Root.IsMouseCaptured)
                {
                    var p = e.GetPosition(_owner);
                    var dxPx = p.X - dragStartMouse.X;
                    var totalDxSec = dxPx / _owner.PixelsPerSecond;
                    if (multiAudioDrag && _owner.TryApplyMultiAudioDrag(totalDxSec)) return;
                    Clip.TimelineStart = Math.Max(0, dragStartTL + totalDxSec);
                }
            };
            Root.MouseLeftButtonUp += (s, e) =>
            {
                if (Root.IsMouseCaptured)
                {
                    Root.ReleaseMouseCapture();
                    if (multiAudioDrag) { _owner.EndMultiDrag(); multiAudioDrag = false; }
                }
            };

            // Shift+Click → split at clicked position (same as ClipBar)
            Root.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 1 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                {
                    var p = e.GetPosition(Root);
                    var w = Root.Width > 0 ? Root.Width : 1;
                    var fraction = Math.Max(0.05, Math.Min(0.95, p.X / w));
                    _owner.LastRequestedSplitSourceSec = Clip.InPoint + fraction * (Clip.OutPoint - Clip.InPoint);
                    _owner.HandleAudioClick(Clip, false);
                    _owner.RaiseClipContext(Clip, "splitHere");
                    e.Handled = true;
                }
            };

            // Left trim handle
            var leftHandle = new Thumb { Width = 6, HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.SizeWE };
            leftHandle.Template = ClipBar.HandleTemplate();
            Root.Children.Add(leftHandle);
            // Right trim handle
            var rightHandle = new Thumb { Width = 6, HorizontalAlignment = HorizontalAlignment.Right, Cursor = Cursors.SizeWE };
            rightHandle.Template = ClipBar.HandleTemplate();
            Root.Children.Add(rightHandle);

            double tIn = 0, tOut = 0, tTL = 0;
            leftHandle.DragStarted += (_, _) =>
            {
                _owner.HandleAudioClick(Clip, false);
                tIn = Clip.InPoint; tOut = Clip.OutPoint; tTL = Clip.TimelineStart;
            };
            leftHandle.DragDelta += (_, e) =>
            {
                var totalDxSec = e.HorizontalChange / _owner.PixelsPerSecond;
                var speed = Math.Max(0.01, Clip.Speed);
                var dxSource = totalDxSec * speed;
                var newIn = Math.Max(0, Math.Min(tOut - 0.1, tIn + dxSource));
                var actualDxSource = newIn - tIn;
                var actualDxTimeline = actualDxSource / speed;
                Clip.InPoint = newIn;
                Clip.TimelineStart = Math.Max(0, tTL + actualDxTimeline);
            };
            rightHandle.DragStarted += (_, _) =>
            {
                _owner.HandleAudioClick(Clip, false);
                tIn = Clip.InPoint; tOut = Clip.OutPoint;
            };
            rightHandle.DragDelta += (_, e) =>
            {
                var totalDxSec = e.HorizontalChange / _owner.PixelsPerSecond;
                var speed = Math.Max(0.01, Clip.Speed);
                var dxSource = totalDxSec * speed;
                Clip.OutPoint = Math.Min(Clip.OriginalDuration, Math.Max(tIn + 0.1, tOut + dxSource));
            };
        }

        // Trigger waveform generation
        ScheduleWaveformLoad();
    }

    private void ShowAudioContextMenu()
    {
        var menu = new ContextMenu();
        void Add(string h, string a)
        {
            var mi = new MenuItem { Header = h };
            mi.Click += (_, _) => _owner.RaiseAudioContext(Clip, a);
            menu.Items.Add(mi);
        }
        bool muted = Clip.Volume <= 0.001;
        if (!Clip.IsAudioOnly)
        {
            // Detach is the headline action - styled to stand out
            var detachMi = new MenuItem
            {
                Header = "🔗➜ Detach Audio (split into its own track)",
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xFF))
            };
            detachMi.Click += (_, _) => _owner.RaiseAudioContext(Clip, "detachAudio");
            menu.Items.Add(detachMi);
            menu.Items.Add(new Separator());
        }
        Add("🔊 Change Volume…", "volume");
        if (muted) Add("🔊 Restore Audio", "unmute");
        else Add("🔇 Mute Audio", "mute");
        Add("🚫 Remove Audio Track (re-encode)", "removeAudioTrack");
        menu.Items.Add(new Separator());
        Add("↗ Extract Audio…", "extractAudio");
        Add("🎵 Add External Audio…", "addAudio");
        if (Clip.IsAudioOnly)
        {
            menu.Items.Add(new Separator());
            Add("🗑 Delete Audio Clip", "deleteAudioClip");
        }
        menu.PlacementTarget = Root;
        menu.IsOpen = true;
    }

    public void SetSelected(bool sel)
    {
        _bg.Stroke = new SolidColorBrush(sel
            ? Color.FromRgb(0xFF, 0xD4, 0x3B)
            : Color.FromArgb(0x40, 0xA0, 0xB4, 0xFF));
        _bg.StrokeThickness = sel ? 2 : 1;
    }

    public void UpdatePosition(double pps)
    {
        Canvas.SetLeft(Root, Clip.TimelineStart * pps);
        Root.Width = Math.Max(20, Clip.EffectiveDuration * pps);
        UpdateLabel();
    }

    private void UpdateLabel()
    {
        bool muted = Clip.Volume <= 0.001;
        _mutedStripes.Visibility = muted ? Visibility.Visible : Visibility.Collapsed;
        _bg.Opacity = muted ? 0.4 : 1.0;
        _waveImg.Opacity = muted ? 0.3 : 0.85;
        if (Clip.IsAudioOnly)
            _labelText.Text = muted ? "🔇 Detached · Muted" : $"🔗➜ Detached · Vol {(int)(Clip.Volume * 100)}%";
        else
            _labelText.Text = muted ? "🔇 Muted" : $"♫ Vol {(int)(Clip.Volume * 100)}%";
    }

    private void ScheduleWaveformLoad()
    {
        if (_owner.FFmpeg == null) return;
        if (!VideoEditor.Services.AppSettings.ShowWaveformPerClip) return;
        _waveCts?.Cancel();
        _waveCts = new CancellationTokenSource();
        var token = _waveCts.Token;
        var dispatcher = Root.Dispatcher;
        var ff = _owner.FFmpeg;
        var sourceFile = Clip.SourceFile;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(200, token);
                if (token.IsCancellationRequested) return;
                if (!System.IO.File.Exists(sourceFile)) return;
                long stamp = 0;
                try { stamp = new System.IO.FileInfo(sourceFile).LastWriteTimeUtc.Ticks; } catch { }
                var key = $"{System.IO.Path.GetFileNameWithoutExtension(sourceFile)}_{stamp}_wave";
                var path = await ff.GenerateWaveformAsync(sourceFile, 1024, 56, key);
                if (token.IsCancellationRequested || path == null) return;
                dispatcher.Invoke(() => ApplyWaveform(path));
            }
            catch { }
        });
    }

    private void ApplyWaveform(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            _waveImg.Source = bmp;
        }
        catch { }
    }
}
