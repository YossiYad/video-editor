using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;

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
    private Color _accentColor = Colors.MediumPurple;
    private int _videoWidth;
    private int _videoHeight;
    private int _loopCount = 1;
    private double _timelineStart;

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
    public Color AccentColor { get => _accentColor; set => Set(ref _accentColor, value); }
    public int VideoWidth { get => _videoWidth; set => Set(ref _videoWidth, value); }
    public int VideoHeight { get => _videoHeight; set => Set(ref _videoHeight, value); }
    public int LoopCount { get => _loopCount; set => Set(ref _loopCount, value); }

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

    private static readonly Color[] _palette = new[]
    {
        Color.FromRgb(0x7C, 0x4D, 0xFF),
        Color.FromRgb(0x00, 0xB8, 0xD9),
        Color.FromRgb(0xFF, 0x6B, 0x6B),
        Color.FromRgb(0x51, 0xCF, 0x66),
        Color.FromRgb(0xFF, 0xD4, 0x3B),
        Color.FromRgb(0xE6, 0x40, 0x9C),
        Color.FromRgb(0xF7, 0x6B, 0x1C),
        Color.FromRgb(0x4D, 0x96, 0xFF),
    };
    private static int _colorIdx;
    public static Color NextColor()
    {
        var c = _palette[_colorIdx % _palette.Length];
        _colorIdx++;
        return c;
    }
}
