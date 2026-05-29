using WpfColor = System.Windows.Media.Color;

// Declared in the VideoEditor.Models namespace (even though it lives in the WPF project)
// so every file that already does `using VideoEditor.Models;` sees these conversions
// without an extra using. Core can't reference System.Windows.Media, so the bridge is here.
namespace VideoEditor.Models;

/// <summary>
/// Bridges the platform-neutral <see cref="RgbColor"/> from VideoEditor.Core and WPF's
/// <see cref="WpfColor"/>.
/// </summary>
internal static class RgbColorWpf
{
    public static WpfColor ToMediaColor(this RgbColor c) => WpfColor.FromRgb(c.R, c.G, c.B);
    public static RgbColor ToRgbColor(this WpfColor c) => new(c.R, c.G, c.B);
}
