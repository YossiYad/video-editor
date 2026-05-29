namespace VideoEditor.Views;

/// <summary>
/// Plain data describing a single burned-in text overlay for the single-clip
/// "Add Text" quick tool (<see cref="VideoEditor.Services.FFmpegService.AddTextAsync"/>).
/// Lives in Core (kept in the <c>VideoEditor.Views</c> namespace it has always used so
/// existing references resolve unchanged) because the export service consumes it. The
/// WPF visual picker (TextOverlayPickerWindow) fills it in.
/// </summary>
public sealed class TextOverlayOptions
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
}
