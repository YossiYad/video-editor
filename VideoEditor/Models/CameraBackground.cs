namespace VideoEditor.Models;

/// <summary>What to do with the area behind the person in a camera recording.</summary>
public enum CameraBackgroundMode
{
    /// <summary>Leave the camera frame untouched (no AI, fastest).</summary>
    None,
    /// <summary>Keep the person sharp, blur everything behind them.</summary>
    Blur,
    /// <summary>Remove the background entirely - the person is composited over
    /// transparency so the recording can be overlaid on anything in the editor.</summary>
    Transparent,
    /// <summary>Replace the background with a flat colour.</summary>
    Color,
    /// <summary>Replace the background with a still image.</summary>
    Image
}

/// <summary>
/// Background-replacement settings chosen for a camera recording. Mode None means
/// no AI segmentation runs at all. The colour is stored as plain RGB bytes so this
/// model stays free of any WPF / System.Drawing dependency.
/// </summary>
public sealed class CameraBackground
{
    public CameraBackgroundMode Mode { get; set; } = CameraBackgroundMode.None;

    // Background colour for Mode == Color (default a neutral studio blue).
    public byte ColorR { get; set; } = 0x10;
    public byte ColorG { get; set; } = 0x6E;
    public byte ColorB { get; set; } = 0xBE;

    // Path to the still image for Mode == Image.
    public string? ImagePath { get; set; }

    /// <summary>True when this background needs the AI segmentation model.</summary>
    public bool NeedsModel => Mode != CameraBackgroundMode.None;

    /// <summary>True when the output keeps a real alpha channel (Transparent only).</summary>
    public bool NeedsAlpha => Mode == CameraBackgroundMode.Transparent;

    public CameraBackground Clone() => new()
    {
        Mode = Mode,
        ColorR = ColorR,
        ColorG = ColorG,
        ColorB = ColorB,
        ImagePath = ImagePath
    };
}
