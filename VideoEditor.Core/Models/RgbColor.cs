namespace VideoEditor.Models;

/// <summary>
/// A platform-neutral 24-bit RGB colour. Replaces the WPF `System.Windows.Media.Color`
/// that the models used to carry, so they can live in `VideoEditor.Core` (no WPF / no
/// System.Drawing) and be shared by the WPF and Avalonia UIs. Each UI converts to/from
/// its own colour type at the boundary.
/// </summary>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public static RgbColor FromRgb(byte r, byte g, byte b) => new(r, g, b);
}
