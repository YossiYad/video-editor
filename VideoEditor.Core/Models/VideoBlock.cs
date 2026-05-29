using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VideoEditor.Models;

public class VideoBlock : INotifyPropertyChanged
{
    private double _x;
    private double _y;
    private double _width = 200;
    private double _height = 120;
    private double _startSeconds;
    private double _endSeconds;
    private bool _coversWholeVideo = true;
    private RgbColor _color = new(0, 0, 0); // Black
    private BlockMode _mode = BlockMode.Solid;
    private int _blurStrength = 25;
    private string _label = "Block";

    public double X { get => _x; set => Set(ref _x, value); }
    public double Y { get => _y; set => Set(ref _y, value); }
    public double Width { get => _width; set => Set(ref _width, value); }
    public double Height { get => _height; set => Set(ref _height, value); }
    public double StartSeconds { get => _startSeconds; set => Set(ref _startSeconds, value); }
    public double EndSeconds { get => _endSeconds; set => Set(ref _endSeconds, value); }
    public bool CoversWholeVideo { get => _coversWholeVideo; set => Set(ref _coversWholeVideo, value); }
    public RgbColor Color { get => _color; set => Set(ref _color, value); }
    public BlockMode Mode { get => _mode; set => Set(ref _mode, value); }
    public int BlurStrength { get => _blurStrength; set => Set(ref _blurStrength, value); }

    public string Label { get => _label; set => Set(ref _label, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (!Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

public enum BlockMode
{
    Solid,
    Blur,
    Pixelate
}
