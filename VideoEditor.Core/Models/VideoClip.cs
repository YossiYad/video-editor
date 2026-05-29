using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace VideoEditor.Models;

public class VideoClip : INotifyPropertyChanged
{
    private string _sourceFile = "";
    private double _originalDuration;
    private double _inPoint;
    private double _outPoint;
    private double _speed = 1.0;
    private double _volume = 1.0;
    private int _rotateDegrees;
    private bool _flipH;
    private bool _flipV;
    private RgbColor _accentColor = new(0x93, 0x70, 0xDB); // MediumPurple
    private int _videoWidth;
    private int _videoHeight;
    private int _loopCount = 1;
    private double _timelineStart;
    private bool _isAudioOnly;
    private double _canvasScale = 1.0;
    private double _canvasOffsetX;
    private double _canvasOffsetY;

    /// <summary>
    /// When true, this clip carries only audio (no video). Used for "detached" audio that the user
    /// has split off from a video clip. Audio-only clips appear in the A1 lane only and don't
    /// participate in V1 ripple-abut.
    /// </summary>
    public bool IsAudioOnly { get => _isAudioOnly; set => Set(ref _isAudioOnly, value); }
    public double TimelineStart { get => _timelineStart; set => Set(ref _timelineStart, value); }
    public string SourceFile { get => _sourceFile; set => Set(ref _sourceFile, value); }
    public double OriginalDuration { get => _originalDuration; set => Set(ref _originalDuration, value); }
    public double InPoint { get => _inPoint; set => Set(ref _inPoint, value); }
    public double OutPoint { get => _outPoint; set => Set(ref _outPoint, value); }
    public double Speed { get => _speed; set => Set(ref _speed, value); }
    public double Volume { get => _volume; set => Set(ref _volume, value); }
    public int RotateDegrees { get => _rotateDegrees; set => Set(ref _rotateDegrees, value); }
    public bool FlipH { get => _flipH; set => Set(ref _flipH, value); }
    public bool FlipV { get => _flipV; set => Set(ref _flipV, value); }
    public RgbColor AccentColor { get => _accentColor; set => Set(ref _accentColor, value); }
    public int VideoWidth { get => _videoWidth; set => Set(ref _videoWidth, value); }
    public int VideoHeight { get => _videoHeight; set => Set(ref _videoHeight, value); }
    public int LoopCount { get => _loopCount; set => Set(ref _loopCount, value); }

    /// <summary>Per-clip zoom on the project canvas. 1.0 = contain-fit (the standard
    /// project-format behaviour); higher = video grows beyond the canvas (cropped on export);
    /// lower = video shrinks inside the canvas with black bars around it.</summary>
    public double CanvasScale { get => _canvasScale; set => Set(ref _canvasScale, Math.Max(0.05, Math.Min(8.0, value))); }
    /// <summary>Horizontal offset on the canvas as a fraction of canvas width.
    /// 0 = centered. ±0.5 = shifted by half the canvas width. Can take the video off-canvas.</summary>
    public double CanvasOffsetX { get => _canvasOffsetX; set => Set(ref _canvasOffsetX, Math.Max(-2.0, Math.Min(2.0, value))); }
    /// <summary>Vertical offset as a fraction of canvas height. 0 = centered.</summary>
    public double CanvasOffsetY { get => _canvasOffsetY; set => Set(ref _canvasOffsetY, Math.Max(-2.0, Math.Min(2.0, value))); }

    /// <summary>True iff the clip uses any non-default canvas transform. Lets the export
    /// pipeline skip the heavier overlay-on-colour filter chain when nothing was touched.</summary>
    public bool HasManualCanvasTransform =>
        Math.Abs(_canvasScale - 1.0) > 0.001 ||
        Math.Abs(_canvasOffsetX) > 0.001 ||
        Math.Abs(_canvasOffsetY) > 0.001;

    public double EffectiveDuration => Math.Max(0.1, (OutPoint - InPoint) / Math.Max(0.01, Speed)) * Math.Max(1, LoopCount);

    public string DisplayName => Path.GetFileNameWithoutExtension(SourceFile);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (!Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            if (name != nameof(EffectiveDuration))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveDuration)));
        }
    }

    private static readonly RgbColor[] _palette = new[]
    {
        new RgbColor(0x7C, 0x4D, 0xFF),
        new RgbColor(0x00, 0xB8, 0xD9),
        new RgbColor(0xFF, 0x6B, 0x6B),
        new RgbColor(0x51, 0xCF, 0x66),
        new RgbColor(0xFF, 0xD4, 0x3B),
        new RgbColor(0xE6, 0x40, 0x9C),
        new RgbColor(0xF7, 0x6B, 0x1C),
        new RgbColor(0x4D, 0x96, 0xFF),
    };
    private static int _colorIdx;
    public static RgbColor NextColor()
    {
        var c = _palette[_colorIdx % _palette.Length];
        _colorIdx++;
        return c;
    }
}
