namespace VideoEditor.Models;

public class TextOverlay
{
    public string Text { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int FontSize { get; set; } = 48;
    public string FontColor { get; set; } = "white";
    public bool Bold { get; set; } = true;
    public bool Italic { get; set; }
    public bool BackgroundEnabled { get; set; } = true;
    public string BackgroundColor { get; set; } = "black";
    public double BackgroundOpacity { get; set; } = 0.55;
    public int BackgroundPadding { get; set; } = 14;
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
}
