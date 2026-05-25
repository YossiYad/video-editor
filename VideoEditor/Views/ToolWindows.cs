using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace VideoEditor.Views;

// Shared dialog chrome - every tool window in the app is built on top of this so the design feels
// cohesive end to end. The OS window chrome stays; we add an in-window titlebar (icon tile + title
// + subtitle), a padded body, and a footer with a Cancel + primary CTA.
internal static class WindowBuilder
{
    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static Brush R(string key, string darkHex, string lightHex)
    {
        var res = Application.Current?.Resources[key] as Brush;
        if (res != null) return res;
        return new SolidColorBrush(C(VideoEditor.Services.Theming.IsLight() ? lightHex : darkHex));
    }

    public static Brush Bg0       => R("Bg0",        "#07080D", "#EBEDF3");
    public static Brush Bg1       => R("Bg1",        "#0F1119", "#F8F9FC");
    public static Brush Bg2       => R("Bg2",        "#161A25", "#F1F3F8");
    public static Brush Bg3       => R("Bg3",        "#1D2231", "#E5E8EF");
    public static Brush LineMute  => R("Line",       "#0FFFFFFF", "#12000000");
    public static Brush Line      => R("LineStrong", "#1FFFFFFF", "#24000000");
    public static Brush LineBright=> R("LineBright", "#38FFFFFF", "#40000000");
    public static Brush TextBr    => R("Text",       "#E8EAF2", "#1A1D24");
    public static Brush TextMute  => R("TextMute",   "#8A91A8", "#52596B");
    public static Brush TextDim   => R("TextDim",    "#5A6178", "#7A8295");
    public static Brush Accent    => R("Accent",     "#8B5CFF", "#8B5CFF");
    public static Brush AccentDark=> R("AccentDark", "#6E44D6", "#6E44D6");

    public sealed class Chrome
    {
        public required Grid Root { get; init; }
        public required StackPanel Body { get; init; }
        public required StackPanel Footer { get; init; }
        public required Button Cancel { get; init; }
        public required Button Primary { get; init; }
    }

    /// Build a complete chrome for a dialog. The caller adds fields to <c>Body</c> and wires up
    /// <c>Primary.Click</c> to read values back and set <c>window.DialogResult = true</c>.
    /// <paramref name="iconText">A single emoji or short string for the 28×28 purple title tile.</paramref>
    public static Chrome Build(Window window, string iconText, string title, string subtitle,
                               double width, double height,
                               string primaryText = "OK", bool primarySuccess = false)
    {
        window.Width = width;
        window.Height = height;
        window.MinWidth = Math.Min(380, width);
        window.MinHeight = Math.Min(220, height);
        window.Background = Bg1;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        window.ResizeMode = ResizeMode.NoResize;
        window.FontFamily = new FontFamily("Segoe UI, Heebo");

        // ESC closes the window
        window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { window.DialogResult = false; window.Close(); } };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // titlebar
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // body
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // footer

        // ===== Title bar =====
        var titleWrap = new Border
        {
            Background = Bg1,
            BorderBrush = Line,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 12, 12, 12)
        };
        var titleGrid = new Grid();
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconTile = new Border
        {
            Width = 28, Height = 28,
            CornerRadius = new CornerRadius(7),
            Background = Accent,
            BorderBrush = AccentDark,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = C("#8B5CFF"), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.45
            }
        };
        iconTile.Child = new TextBlock
        {
            Text = iconText,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(iconTile, 0);
        titleGrid.Children.Add(iconTile);

        var titleStack = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13.5, FontWeight = FontWeights.Bold,
            Foreground = TextBr
        });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            titleStack.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 11,
                Foreground = TextMute,
                Margin = new Thickness(0, 2, 0, 0)
            });
        }
        Grid.SetColumn(titleStack, 1);
        titleGrid.Children.Add(titleStack);

        var closeBtn = new Button
        {
            Width = 24, Height = 24, Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = TextMute,
            FontSize = 14,
            Cursor = Cursors.Hand,
            Content = "✕",
            VerticalAlignment = VerticalAlignment.Center
        };
        closeBtn.Click += (_, _) => { window.DialogResult = false; window.Close(); };
        Grid.SetColumn(closeBtn, 2);
        titleGrid.Children.Add(closeBtn);

        titleWrap.Child = titleGrid;
        Grid.SetRow(titleWrap, 0);
        root.Children.Add(titleWrap);

        // ===== Body (scrollable) =====
        var body = new StackPanel { Margin = new Thickness(18) };
        var bodyScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = body,
            Background = Bg1,
            BorderThickness = new Thickness(0)
        };
        Grid.SetRow(bodyScroll, 1);
        root.Children.Add(bodyScroll);

        // ===== Footer =====
        var footerWrap = new Border
        {
            Background = Bg2,
            BorderBrush = Line,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(16, 12, 16, 12)
        };
        var footerGrid = new Grid();
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // Left side stack (caller can append context info)
        var leftFooter = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(leftFooter, 0);
        footerGrid.Children.Add(leftFooter);
        // Right side: Cancel + Primary
        var rightFooter = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(rightFooter, 1);
        footerGrid.Children.Add(rightFooter);

        var cancel = new Button
        {
            Content = "Cancel",
            MinWidth = 88, Height = 32,
            Margin = new Thickness(0, 0, 8, 0)
        };
        cancel.Style = (Style)window.TryFindResource("GhostButton") ?? cancel.Style;
        cancel.Click += (_, _) => { window.DialogResult = false; window.Close(); };
        rightFooter.Children.Add(cancel);

        var primary = new Button
        {
            Content = primaryText,
            MinWidth = 96, Height = 32
        };
        primary.Style = (Style)window.TryFindResource(primarySuccess ? "SuccessButton" : "PrimaryButton") ?? primary.Style;
        rightFooter.Children.Add(primary);

        footerWrap.Child = footerGrid;
        Grid.SetRow(footerWrap, 2);
        root.Children.Add(footerWrap);

        window.Content = root;
        return new Chrome { Root = root, Body = body, Footer = leftFooter, Cancel = cancel, Primary = primary };
    }

    // ===== Field helpers =====
    public static TextBlock Lbl(string t) => new()
    {
        Text = t.ToUpperInvariant(),
        Foreground = TextDim,
        FontSize = 10.5,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(0, 12, 0, 5)
    };

    public static TextBox Tb(string text = "")
    {
        return new TextBox
        {
            Text = text,
            Background = Bg1,
            Foreground = TextBr,
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 7, 9, 7),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12.5,
            CaretBrush = Brushes.White
        };
    }

    // Footer info chip - supplemental info on the left side of the footer.
    public static TextBlock FooterInfo(string text) => new()
    {
        Text = text,
        FontSize = 11,
        Foreground = TextMute,
        VerticalAlignment = VerticalAlignment.Center
    };

    // Legacy adapters so existing callers (and any future tests) still compile.
    [Obsolete("Use WindowBuilder.Build")]
    public static Window Make(string title, double w, double h, UIElement content)
    {
        return new Window
        {
            Title = title, Width = w, Height = h,
            Background = Bg1,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = content,
            ResizeMode = ResizeMode.NoResize
        };
    }

    [Obsolete("Use WindowBuilder.Build")]
    public static (Grid grid, StackPanel body, StackPanel footer) Layout()
    {
        var g = new Grid { Margin = new Thickness(16) };
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var body = new StackPanel();
        Grid.SetRow(body, 0);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        Grid.SetRow(footer, 1);
        g.Children.Add(body);
        g.Children.Add(footer);
        return (g, body, footer);
    }

    [Obsolete("Use WindowBuilder.Build")]
    public static (Button ok, Button cancel) OkCancel(Window w, StackPanel footer)
    {
        var cancel = new Button { Content = "Cancel", Width = 90, Margin = new Thickness(4) };
        cancel.Style = (Style)w.FindResource("GhostButton");
        cancel.Click += (_, _) => { w.DialogResult = false; w.Close(); };
        var ok = new Button { Content = "OK", Width = 90, Margin = new Thickness(4) };
        ok.Style = (Style)w.FindResource("PrimaryButton");
        ok.Click += (_, _) => { w.DialogResult = true; w.Close(); };
        footer.Children.Add(cancel);
        footer.Children.Add(ok);
        return (ok, cancel);
    }
}

// =========================================================================================
// Dialogs
// =========================================================================================

public class TrimWindow : Window
{
    public double StartSec { get; private set; }
    public double EndSec { get; private set; }

    public TrimWindow(double duration)
    {
        Title = "Trim Video";
        var ch = WindowBuilder.Build(this, "✂", "Trim Video",
            "Pick the In / Out points (in seconds)", 500, 320);

        ch.Body.Children.Add(WindowBuilder.Lbl("Start (s)"));
        var s = WindowBuilder.Tb("0");
        ch.Body.Children.Add(s);

        ch.Body.Children.Add(WindowBuilder.Lbl("End (s)"));
        var en = WindowBuilder.Tb(duration.ToString("0.##", CultureInfo.InvariantCulture));
        ch.Body.Children.Add(en);

        ch.Footer.Children.Add(WindowBuilder.FooterInfo($"Original duration: {duration:0.##}s"));
        ch.Primary.Content = "Apply trim";
        ch.Primary.Click += (_, _) =>
        {
            double.TryParse(s.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var sv);
            double.TryParse(en.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var ev);
            StartSec = sv; EndSec = ev;
            DialogResult = true; Close();
        };
    }
}

public class AddAudioWindow : Window
{
    public string AudioFile { get; private set; } = "";

    public AddAudioWindow()
    {
        Title = "Add Audio";
        var ch = WindowBuilder.Build(this, "🎵", "Add Audio",
            "Layer an external audio file onto this clip", 520, 260);

        ch.Body.Children.Add(WindowBuilder.Lbl("Audio file"));
        var path = WindowBuilder.Tb();
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(path, 0);
        row.Children.Add(path);
        var browse = new Button { Content = "📁 Browse", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 6, 12, 6) };
        browse.Style = (Style)FindResource("ToolButton");
        browse.Click += (_, _) =>
        {
            var d = new OpenFileDialog { Filter = "Audio|*.mp3;*.wav;*.aac;*.m4a;*.ogg;*.flac|All|*.*" };
            if (d.ShowDialog() == true) path.Text = d.FileName;
        };
        Grid.SetColumn(browse, 1);
        row.Children.Add(browse);
        ch.Body.Children.Add(row);

        ch.Primary.Content = "Add audio";
        ch.Primary.Click += (_, _) => { AudioFile = path.Text; DialogResult = true; Close(); };
    }
}

public class AddImageWindow : Window
{
    public string ImageFile { get; private set; } = "";
    public int X { get; private set; }
    public int Y { get; private set; }

    public AddImageWindow()
    {
        Title = "Add Image to Video";
        var ch = WindowBuilder.Build(this, "🖼", "Add Image",
            "Overlay an image at a given (X, Y) - applied at export", 520, 330);

        ch.Body.Children.Add(WindowBuilder.Lbl("Image file"));
        var path = WindowBuilder.Tb();
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(path, 0);
        row.Children.Add(path);
        var browse = new Button { Content = "📁 Browse", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 6, 12, 6) };
        browse.Style = (Style)FindResource("ToolButton");
        browse.Click += (_, _) =>
        {
            var d = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All|*.*" };
            if (d.ShowDialog() == true) path.Text = d.FileName;
        };
        Grid.SetColumn(browse, 1);
        row.Children.Add(browse);
        ch.Body.Children.Add(row);

        ch.Body.Children.Add(WindowBuilder.Lbl("Position X / Y (px)"));
        var px = WindowBuilder.Tb("10");
        var py = WindowBuilder.Tb("10");
        var posRow = new Grid();
        posRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        posRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(px, 0);
        Grid.SetColumn(py, 1); py.Margin = new Thickness(6, 0, 0, 0);
        posRow.Children.Add(px); posRow.Children.Add(py);
        ch.Body.Children.Add(posRow);

        ch.Primary.Content = "Add image";
        ch.Primary.Click += (_, _) =>
        {
            ImageFile = path.Text;
            int.TryParse(px.Text, out var ix);
            int.TryParse(py.Text, out var iy);
            X = ix; Y = iy;
            DialogResult = true; Close();
        };
    }
}

public class AddTextWindow : Window
{
    public string TextValue { get; private set; } = "";
    public int X { get; private set; }
    public int Y { get; private set; }
    public new int FontSize { get; private set; }
    public string ColorHex { get; private set; } = "white";

    public AddTextWindow()
    {
        Title = "Add Text";
        var ch = WindowBuilder.Build(this, "🔤", "Add Text",
            "Burn-in text overlay (applied at export)", 560, 400);

        ch.Body.Children.Add(WindowBuilder.Lbl("Text"));
        var t = WindowBuilder.Tb("Sample Text");
        ch.Body.Children.Add(t);

        ch.Body.Children.Add(WindowBuilder.Lbl("Position X / Y · Font size"));
        var posRow = new Grid();
        posRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        posRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        posRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var px = WindowBuilder.Tb("20");
        var py = WindowBuilder.Tb("20");
        var pf = WindowBuilder.Tb("36");
        Grid.SetColumn(px, 0); Grid.SetColumn(py, 1); Grid.SetColumn(pf, 2);
        py.Margin = new Thickness(6, 0, 0, 0); pf.Margin = new Thickness(6, 0, 0, 0);
        posRow.Children.Add(px); posRow.Children.Add(py); posRow.Children.Add(pf);
        ch.Body.Children.Add(posRow);

        ch.Body.Children.Add(WindowBuilder.Lbl("Color (name or hex)"));
        var c = WindowBuilder.Tb("white");
        ch.Body.Children.Add(c);

        ch.Primary.Content = "Add text";
        ch.Primary.Click += (_, _) =>
        {
            TextValue = t.Text;
            int.TryParse(px.Text, out var ix);
            int.TryParse(py.Text, out var iy);
            int.TryParse(pf.Text, out var fs);
            X = ix; Y = iy; FontSize = fs > 0 ? fs : 36;
            ColorHex = string.IsNullOrWhiteSpace(c.Text) ? "white" : c.Text;
            DialogResult = true; Close();
        };
    }
}

public class RemoveLogoWindow : Window
{
    public int X { get; private set; }
    public int Y { get; private set; }
    public int W { get; private set; }
    public int H { get; private set; }

    public RemoveLogoWindow()
    {
        Title = "Remove Logo";
        var ch = WindowBuilder.Build(this, "🚫", "Remove Logo",
            "Manually pick a region (or use the visual picker for accuracy)", 500, 320);
        ch.Body.Children.Add(WindowBuilder.Lbl("Region (X, Y, W, H)"));
        var px = WindowBuilder.Tb("10");
        var py = WindowBuilder.Tb("10");
        var pw = WindowBuilder.Tb("120");
        var ph = WindowBuilder.Tb("60");
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(px, 0); Grid.SetColumn(py, 1); Grid.SetColumn(pw, 2); Grid.SetColumn(ph, 3);
        py.Margin = pw.Margin = ph.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(px); row.Children.Add(py); row.Children.Add(pw); row.Children.Add(ph);
        ch.Body.Children.Add(row);
        ch.Primary.Content = "Apply";
        ch.Primary.Click += (_, _) =>
        {
            int.TryParse(px.Text, out var ix); int.TryParse(py.Text, out var iy);
            int.TryParse(pw.Text, out var iw); int.TryParse(ph.Text, out var ih);
            X = ix; Y = iy; W = iw; H = ih;
            DialogResult = true; Close();
        };
    }
}

public class CropWindow : Window
{
    public int X { get; private set; }
    public int Y { get; private set; }
    public int W { get; private set; }
    public int H { get; private set; }

    public CropWindow(int vw, int vh)
    {
        Title = "Crop Video";
        var ch = WindowBuilder.Build(this, "◳", "Crop Video",
            $"Source: {vw} × {vh} px", 500, 320);
        ch.Body.Children.Add(WindowBuilder.Lbl("Keep region (X, Y, W, H)"));
        var px = WindowBuilder.Tb("0");
        var py = WindowBuilder.Tb("0");
        var pw = WindowBuilder.Tb(vw.ToString());
        var ph = WindowBuilder.Tb(vh.ToString());
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(px, 0); Grid.SetColumn(py, 1); Grid.SetColumn(pw, 2); Grid.SetColumn(ph, 3);
        py.Margin = pw.Margin = ph.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(px); row.Children.Add(py); row.Children.Add(pw); row.Children.Add(ph);
        ch.Body.Children.Add(row);

        ch.Primary.Content = "Crop";
        ch.Primary.Click += (_, _) =>
        {
            int.TryParse(px.Text, out var ix); int.TryParse(py.Text, out var iy);
            int.TryParse(pw.Text, out var iw); int.TryParse(ph.Text, out var ih);
            X = ix; Y = iy; W = iw; H = ih;
            DialogResult = true; Close();
        };
    }
}

public class RotateWindow : Window
{
    public int Degrees { get; private set; }
    public RotateWindow()
    {
        Title = "Rotate Video";
        var ch = WindowBuilder.Build(this, "↻", "Rotate Video", "Pick a direction", 480, 260);
        var seg = new Border { Background = WindowBuilder.Bg1, BorderBrush = WindowBuilder.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(3) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var b90R = MakeSeg("↻ 90° R", true);
        var b180 = MakeSeg("↻ 180°", false);
        var b90L = MakeSeg("↺ 90° L", false);
        Grid.SetColumn(b90R, 0); Grid.SetColumn(b180, 1); Grid.SetColumn(b90L, 2);
        grid.Children.Add(b90R); grid.Children.Add(b180); grid.Children.Add(b90L);
        seg.Child = grid;
        ch.Body.Children.Add(seg);

        int picked = 90;
        b90R.Click += (_, _) => { picked = 90;  ToggleSel(b90R, b180, b90L); };
        b180.Click += (_, _) => { picked = 180; ToggleSel(b180, b90R, b90L); };
        b90L.Click += (_, _) => { picked = 270; ToggleSel(b90L, b90R, b180); };

        ch.Primary.Content = "Rotate";
        ch.Primary.Click += (_, _) => { Degrees = picked; DialogResult = true; Close(); };
    }
    private static Button MakeSeg(string text, bool isOn)
    {
        var b = new Button
        {
            Content = text,
            Background = isOn ? WindowBuilder.Bg3 : Brushes.Transparent,
            Foreground = isOn ? WindowBuilder.TextBr : WindowBuilder.TextMute,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 6, 10, 6),
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            Margin = new Thickness(1)
        };
        return b;
    }
    private static void ToggleSel(Button on, params Button[] off)
    {
        on.Background = WindowBuilder.Bg3;
        on.Foreground = WindowBuilder.TextBr;
        foreach (var o in off) { o.Background = Brushes.Transparent; o.Foreground = WindowBuilder.TextMute; }
    }
}

public class FlipWindow : Window
{
    public bool Horizontal { get; private set; } = true;
    public FlipWindow()
    {
        Title = "Flip Video";
        var ch = WindowBuilder.Build(this, "⇄", "Flip Video", "Pick a direction", 480, 240);
        var seg = new Border { Background = WindowBuilder.Bg1, BorderBrush = WindowBuilder.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(3) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var bH = MakeSeg("⇄ Horizontal", true);
        var bV = MakeSeg("⇅ Vertical", false);
        Grid.SetColumn(bH, 0); Grid.SetColumn(bV, 1);
        grid.Children.Add(bH); grid.Children.Add(bV);
        seg.Child = grid;
        ch.Body.Children.Add(seg);

        bH.Click += (_, _) => { Horizontal = true;  ToggleSel(bH, bV); };
        bV.Click += (_, _) => { Horizontal = false; ToggleSel(bV, bH); };

        ch.Primary.Content = "Flip";
        ch.Primary.Click += (_, _) => { DialogResult = true; Close(); };
    }
    private static Button MakeSeg(string text, bool isOn)
    {
        return new Button
        {
            Content = text,
            Background = isOn ? WindowBuilder.Bg3 : Brushes.Transparent,
            Foreground = isOn ? WindowBuilder.TextBr : WindowBuilder.TextMute,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 6, 10, 6),
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            Margin = new Thickness(1)
        };
    }
    private static void ToggleSel(Button on, params Button[] off)
    {
        on.Background = WindowBuilder.Bg3;
        on.Foreground = WindowBuilder.TextBr;
        foreach (var o in off) { o.Background = Brushes.Transparent; o.Foreground = WindowBuilder.TextMute; }
    }
}

public class ResizeWindow : Window
{
    public int W { get; private set; }
    public int H { get; private set; }
    public ResizeWindow(int origW, int origH)
    {
        Title = "Resize Video";
        var ch = WindowBuilder.Build(this, "⛶", "Resize Video", $"Source: {origW} × {origH} px", 500, 300);
        ch.Body.Children.Add(WindowBuilder.Lbl("New width / height (px)"));
        var pw = WindowBuilder.Tb(origW.ToString());
        var ph = WindowBuilder.Tb(origH.ToString());
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(pw, 0); Grid.SetColumn(ph, 1);
        ph.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(pw); row.Children.Add(ph);
        ch.Body.Children.Add(row);

        ch.Primary.Content = "Resize";
        ch.Primary.Click += (_, _) =>
        {
            int.TryParse(pw.Text, out var iw);
            int.TryParse(ph.Text, out var ih);
            W = iw; H = ih;
            DialogResult = true; Close();
        };
    }
}

public class LoopWindow : Window
{
    public int Times { get; private set; } = 2;
    public LoopWindow()
    {
        Title = "Loop Video";
        var ch = WindowBuilder.Build(this, "🔁", "Loop Video", "Repeat this clip N times", 480, 260);
        ch.Body.Children.Add(WindowBuilder.Lbl("Repeat count"));
        var sl = new Slider { Minimum = 2, Maximum = 10, Value = 2, TickFrequency = 1, IsSnapToTickEnabled = true };
        var lbl = new TextBlock { Text = "× 2", Foreground = WindowBuilder.TextBr, FontFamily = new FontFamily("Consolas"), FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
        sl.ValueChanged += (_, e) => lbl.Text = "× " + (int)e.NewValue;
        ch.Body.Children.Add(sl);
        ch.Body.Children.Add(lbl);
        ch.Primary.Content = "Apply loop";
        ch.Primary.Click += (_, _) => { Times = Math.Max(2, (int)sl.Value); DialogResult = true; Close(); };
    }
}

public class VolumeWindow : Window
{
    public double Volume { get; private set; } = 1.0;
    public VolumeWindow()
    {
        Title = "Change Volume";
        var ch = WindowBuilder.Build(this, "🔊", "Change Volume",
            "1.00× = original, 0.5× = half, 2.0× = double", 480, 280);
        ch.Body.Children.Add(WindowBuilder.Lbl("Volume multiplier"));
        var sl = new Slider { Minimum = 0, Maximum = 4, Value = 1, TickFrequency = 0.1, IsSnapToTickEnabled = false };
        var lbl = new TextBlock { Text = "1.00×", Foreground = WindowBuilder.TextBr, FontFamily = new FontFamily("Consolas"), FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
        sl.ValueChanged += (_, e) => lbl.Text = e.NewValue.ToString("0.00") + "×";
        ch.Body.Children.Add(sl);
        ch.Body.Children.Add(lbl);

        ch.Primary.Content = "Apply volume";
        ch.Primary.Click += (_, _) => { Volume = sl.Value; DialogResult = true; Close(); };
    }
}

public class SpeedWindow : Window
{
    public double Speed { get; private set; } = 1.0;
    public SpeedWindow()
    {
        Title = "Change Speed";
        var ch = WindowBuilder.Build(this, "⏩", "Change Speed",
            "Time-stretch or slow-down the clip", 480, 280);
        ch.Body.Children.Add(WindowBuilder.Lbl("Speed multiplier"));
        var sl = new Slider { Minimum = 0.25, Maximum = 4, Value = 1 };
        var lbl = new TextBlock { Text = "1.00×", Foreground = WindowBuilder.TextBr, FontFamily = new FontFamily("Consolas"), FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
        sl.ValueChanged += (_, e) => lbl.Text = e.NewValue.ToString("0.00") + "×";
        ch.Body.Children.Add(sl);
        ch.Body.Children.Add(lbl);

        ch.Primary.Content = "Apply speed";
        ch.Primary.Click += (_, _) => { Speed = sl.Value; DialogResult = true; Close(); };
    }
}

public class SplitNWindow : Window
{
    public int Parts { get; private set; } = 2;
    public SplitNWindow()
    {
        Title = "Split into N Parts";
        var ch = WindowBuilder.Build(this, "⋯", "Split into N Parts",
            "Cut the selected clip into N equal segments", 480, 300);
        ch.Body.Children.Add(WindowBuilder.Lbl("Number of parts"));
        var partsBox = WindowBuilder.Tb("2");
        partsBox.FontSize = 14;
        partsBox.Padding = new Thickness(10, 8, 10, 8);
        ch.Body.Children.Add(partsBox);
        ch.Body.Children.Add(new TextBlock
        {
            Text = "Enter a number between 2 and 20",
            Foreground = WindowBuilder.TextDim,
            FontSize = 11, Margin = new Thickness(0, 6, 0, 0)
        });

        ch.Primary.Content = "Split";
        ch.Primary.Click += (_, _) =>
        {
            if (int.TryParse(partsBox.Text, out var n) && n >= 2 && n <= 20) Parts = n;
            else Parts = 2;
            DialogResult = true; Close();
        };
    }
}

public class ConfirmWindow : Window
{
    public ConfirmWindow(string title, string msg)
    {
        Title = title;
        var ch = WindowBuilder.Build(this, "⚠", title, "Confirm to continue", 440, 220);
        ch.Body.Children.Add(new TextBlock
        {
            Text = msg,
            Foreground = WindowBuilder.TextBr,
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap
        });
        ch.Primary.Content = "Confirm";
        ch.Primary.Click += (_, _) => { DialogResult = true; Close(); };
    }
}
