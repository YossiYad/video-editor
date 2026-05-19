using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace VideoEditor.Views;

internal static class WindowBuilder
{
    public static Window Make(string title, double w, double h, UIElement content)
    {
        return new Window
        {
            Title = title,
            Width = w,
            Height = h,
            Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x1F)),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = content,
            ResizeMode = ResizeMode.NoResize
        };
    }

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

    public static (Button ok, Button cancel) OkCancel(Window w, StackPanel footer)
    {
        var cancel = new Button { Content = "Cancel", Width = 90, Margin = new Thickness(4) };
        cancel.Style = (Style)w.FindResource("ToolButton");
        cancel.Click += (_, _) => { w.DialogResult = false; w.Close(); };
        var ok = new Button { Content = "OK", Width = 90, Margin = new Thickness(4) };
        ok.Style = (Style)w.FindResource("PrimaryButton");
        ok.Click += (_, _) => { w.DialogResult = true; w.Close(); };
        footer.Children.Add(cancel);
        footer.Children.Add(ok);
        return (ok, cancel);
    }

    public static Label Lbl(string t) => new() { Content = t, Foreground = Brushes.White, Margin = new Thickness(0, 6, 0, 2) };
    public static TextBox Tb(string text = "") { var tb = new TextBox { Text = text }; return tb; }
}

public class TrimWindow : Window
{
    public double StartSec { get; private set; }
    public double EndSec { get; private set; }
    public TrimWindow(double duration)
    {
        Title = "Trim Video"; Width = 380; Height = 220;
        Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x1F));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var (g, body, footer) = WindowBuilder.Layout();
        body.Children.Add(WindowBuilder.Lbl($"Start (seconds, max {duration:0.##})"));
        var s = WindowBuilder.Tb("0");
        body.Children.Add(s);
        body.Children.Add(WindowBuilder.Lbl("End (seconds)"));
        var en = WindowBuilder.Tb(duration.ToString("0.##", CultureInfo.InvariantCulture));
        body.Children.Add(en);
        Content = g;
        var (ok, _) = WindowBuilder.OkCancel(this, footer);
        ok.Click += (_, _) =>
        {
            double.TryParse(s.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var sv);
            double.TryParse(en.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var ev);
            StartSec = sv; EndSec = ev;
        };
    }
}

public class AddAudioWindow : Window
{
    public string AudioFile { get; private set; } = "";
    public AddAudioWindow()
    {
        Title = "Add Audio"; Width = 480; Height = 180;
        Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x1F));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var (g, body, footer) = WindowBuilder.Layout();
        body.Children.Add(WindowBuilder.Lbl("Audio file"));
        var path = WindowBuilder.Tb();
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(path); path.Width = 320;
        var browse = new Button { Content = "Browse", Width = 100, Margin = new Thickness(8, 0, 0, 0), Style = (Style)FindResource("ToolButton") };
        browse.Click += (_, _) =>
        {
            var d = new OpenFileDialog { Filter = "Audio|*.mp3;*.wav;*.aac;*.m4a;*.ogg|All|*.*" };
            if (d.ShowDialog() == true) path.Text = d.FileName;
        };
        sp.Children.Add(browse);
        body.Children.Add(sp);
        Content = g;
        var (ok, _) = WindowBuilder.OkCancel(this, footer);
        ok.Click += (_, _) => AudioFile = path.Text;
    }
}

public class AddImageWindow : Window
{
    public string ImageFile { get; private set; } = "";
    public int X { get; private set; }
    public int Y { get; private set; }
    public AddImageWindow()
    {
        Title = "Add Image to Video"; Width = 480; Height = 260;
        Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x1F));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var (g, body, footer) = WindowBuilder.Layout();
        body.Children.Add(WindowBuilder.Lbl("Image file"));
        var path = WindowBuilder.Tb();
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(path); path.Width = 320;
        var browse = new Button { Content = "Browse", Width = 100, Margin = new Thickness(8, 0, 0, 0), Style = (Style)FindResource("ToolButton") };
        browse.Click += (_, _) =>
        {
            var d = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All|*.*" };
            if (d.ShowDialog() == true) path.Text = d.FileName;
        };
        sp.Children.Add(browse);
        body.Children.Add(sp);
        body.Children.Add(WindowBuilder.Lbl("Position X / Y"));
        var px = WindowBuilder.Tb("10"); var py = WindowBuilder.Tb("10");
        var pos = new StackPanel { Orientation = Orientation.Horizontal };
        px.Width = 80; py.Width = 80;
        pos.Children.Add(px); pos.Children.Add(new TextBlock { Text = " , ", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center }); pos.Children.Add(py);
        body.Children.Add(pos);
        Content = g;
        var (ok, _) = WindowBuilder.OkCancel(this, footer);
        ok.Click += (_, _) => { ImageFile = path.Text; int.TryParse(px.Text, out var ix); int.TryParse(py.Text, out var iy); X = ix; Y = iy; };
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
        Title = "Add Text"; Width = 480; Height = 320;
        Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x1F));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var (g, body, footer) = WindowBuilder.Layout();
        body.Children.Add(WindowBuilder.Lbl("Text"));
        var t = WindowBuilder.Tb("Sample Text");
        body.Children.Add(t);
        body.Children.Add(WindowBuilder.Lbl("X / Y / Font Size"));
        var px = WindowBuilder.Tb("20"); var py = WindowBuilder.Tb("20"); var pf = WindowBuilder.Tb("36");
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        px.Width = 80; py.Width = 80; pf.Width = 80;
        sp.Children.Add(px); sp.Children.Add(new TextBlock { Text = "  ", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center }); sp.Children.Add(py); sp.Children.Add(new TextBlock { Text = "  ", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center }); sp.Children.Add(pf);
        body.Children.Add(sp);
        body.Children.Add(WindowBuilder.Lbl("Color (name or hex)"));
        var c = WindowBuilder.Tb("white"); body.Children.Add(c);
        Content = g;
        var (ok, _) = WindowBuilder.OkCancel(this, footer);
        ok.Click += (_, _) =>
        {
            TextValue = t.Text;
            int.TryParse(px.Text, out var ix); int.TryParse(py.Text, out var iy);
            int.TryParse(pf.Text, out var fs);
            X = ix; Y = iy; FontSize = fs > 0 ? fs : 36; ColorHex = string.IsNullOrWhiteSpace(c.Text) ? "white" : c.Text;
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
        Title = "Remove Logo"; Width = 380; Height = 260;
        Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x1F));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var (g, body, footer) = WindowBuilder.Layout();
        body.Children.Add(WindowBuilder.Lbl("Logo Rect (X, Y, W, H)"));
        var px = WindowBuilder.Tb("10"); var py = WindowBuilder.Tb("10"); var pw = WindowBuilder.Tb("120"); var ph = WindowBuilder.Tb("60");
        px.Width = py.Width = pw.Width = ph.Width = 60;
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(px); sp.Children.Add(new TextBlock { Text = " ", VerticalAlignment = VerticalAlignment.Center }); sp.Children.Add(py); sp.Children.Add(new TextBlock { Text = " ", VerticalAlignment = VerticalAlignment.Center }); sp.Children.Add(pw); sp.Children.Add(new TextBlock { Text = " ", VerticalAlignment = VerticalAlignment.Center }); sp.Children.Add(ph);
        body.Children.Add(sp);
        Content = g;
        var (ok, _) = WindowBuilder.OkCancel(this, footer);
        ok.Click += (_, _) =>
        {
            int.TryParse(px.Text, out var ix); int.TryParse(py.Text, out var iy);
            int.TryParse(pw.Text, out var iw); int.TryParse(ph.Text, out var ih);
            X = ix; Y = iy; W = iw; H = ih;
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
        Title = "Crop Video"; Width = 380; Height = 260;
        Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x1F));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var (g, body, footer) = WindowBuilder.Layout();
        body.Children.Add(WindowBuilder.Lbl($"Video: {vw}x{vh}"));
        body.Children.Add(WindowBuilder.Lbl("Crop Rect (X, Y, W, H)"));
        var px = WindowBuilder.Tb("0"); var py = WindowBuilder.Tb("0"); var pw = WindowBuilder.Tb(vw.ToString()); var ph = WindowBuilder.Tb(vh.ToString());
        px.Width = py.Width = pw.Width = ph.Width = 60;
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(px); sp.Children.Add(new TextBlock { Text = " ", VerticalAlignment = VerticalAlignment.Center }); sp.Children.Add(py); sp.Children.Add(new TextBlock { Text = " ", VerticalAlignment = VerticalAlignment.Center }); sp.Children.Add(pw); sp.Children.Add(new TextBlock { Text = " ", VerticalAlignment = VerticalAlignment.Center }); sp.Children.Add(ph);
        body.Children.Add(sp);
        Content = g;
        var (ok, _) = WindowBuilder.OkCancel(this, footer);
        ok.Click += (_, _) =>
        {
            int.TryParse(px.Text, out var ix); int.TryParse(py.Text, out var iy);
            int.TryParse(pw.Text, out var iw); int.TryParse(ph.Text, out var ih);
            X = ix; Y = iy; W = iw; H = ih;
        };
    }
}

public class RotateWindow : Window
{
    public int Degrees { get; private set; }
    public RotateWindow()
    {
        Title = "Rotate Video"; Width = 320; Height = 200;
        Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x1F));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var (g, body, footer) = WindowBuilder.Layout();
        body.Children.Add(WindowBuilder.Lbl("Rotation"));
        var cb = new ComboBox { Margin = new Thickness(0, 4, 0, 0) };
        cb.Items.Add("90° Right"); cb.Items.Add("180°"); cb.Items.Add("270° (90° Left)"); cb.SelectedIndex = 0;
        body.Children.Add(cb);
        Content = g;
        var (ok, _) = WindowBuilder.OkCancel(this, footer);
        ok.Click += (_, _) => Degrees = cb.SelectedIndex switch { 0 => 90, 1 => 180, 2 => 270, _ => 90 };
    }
}

public class FlipWindow : Window
{
    public bool Horizontal { get; private set; } = true;
    public FlipWindow()
    {
        Title = "Flip Video"; Width = 300; Height = 180;
        Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x1F));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var (g, body, footer) = WindowBuilder.Layout();
        var cb = new ComboBox(); cb.Items.Add("Horizontal"); cb.Items.Add("Vertical"); cb.SelectedIndex = 0;
        body.Children.Add(WindowBuilder.Lbl("Direction"));
        body.Children.Add(cb);
        Content = g;
        var (ok, _) = WindowBuilder.OkCancel(this, footer);
        ok.Click += (_, _) => Horizontal = cb.SelectedIndex == 0;
    }
}

public class ResizeWindow : Window
{
    public int W { get; private set; }
    public int H { get; private set; }
    public ResizeWindow(int origW, int origH)
    {
        Title = "Resize Video"; Width = 320; Height = 220;
        Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x1F));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var (g, body, footer) = WindowBuilder.Layout();
        body.Children.Add(WindowBuilder.Lbl($"Original: {origW}x{origH}"));
        body.Children.Add(WindowBuilder.Lbl("New Width / Height"));
        var pw = WindowBuilder.Tb(origW.ToString()); var ph = WindowBuilder.Tb(origH.ToString());
        pw.Width = ph.Width = 80;
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(pw); sp.Children.Add(new TextBlock { Text = " x ", VerticalAlignment = VerticalAlignment.Center }); sp.Children.Add(ph);
        body.Children.Add(sp);
        Content = g;
        var (ok, _) = WindowBuilder.OkCancel(this, footer);
        ok.Click += (_, _) => { int.TryParse(pw.Text, out var iw); int.TryParse(ph.Text, out var ih); W = iw; H = ih; };
    }
}

public class LoopWindow : Window
{
    public int Times { get; private set; } = 2;
    public LoopWindow()
    {
        Title = "Loop Video"; Width = 300; Height = 180;
        Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x1F));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var (g, body, footer) = WindowBuilder.Layout();
        body.Children.Add(WindowBuilder.Lbl("Repeat count"));
        var pt = WindowBuilder.Tb("2"); body.Children.Add(pt);
        Content = g;
        var (ok, _) = WindowBuilder.OkCancel(this, footer);
        ok.Click += (_, _) => { int.TryParse(pt.Text, out var t); Times = Math.Max(2, t); };
    }
}

public class VolumeWindow : Window
{
    public double Volume { get; private set; } = 1.0;
    public VolumeWindow()
    {
        Title = "Change Volume"; Width = 340; Height = 200;
        Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x1F));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var (g, body, footer) = WindowBuilder.Layout();
        body.Children.Add(WindowBuilder.Lbl("Volume multiplier (1.0 = original, 0.5 = half, 2.0 = double)"));
        var sl = new Slider { Minimum = 0, Maximum = 4, Value = 1, TickFrequency = 0.1, IsSnapToTickEnabled = false };
        var lbl = new TextBlock { Text = "1.0", Foreground = Brushes.White };
        sl.ValueChanged += (_, e) => lbl.Text = e.NewValue.ToString("0.00");
        body.Children.Add(sl); body.Children.Add(lbl);
        Content = g;
        var (ok, _) = WindowBuilder.OkCancel(this, footer);
        ok.Click += (_, _) => Volume = sl.Value;
    }
}

public class SpeedWindow : Window
{
    public double Speed { get; private set; } = 1.0;
    public SpeedWindow()
    {
        Title = "Change Speed"; Width = 340; Height = 200;
        Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x1F));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var (g, body, footer) = WindowBuilder.Layout();
        body.Children.Add(WindowBuilder.Lbl("Speed multiplier"));
        var sl = new Slider { Minimum = 0.25, Maximum = 4, Value = 1 };
        var lbl = new TextBlock { Text = "1.00x", Foreground = Brushes.White };
        sl.ValueChanged += (_, e) => lbl.Text = e.NewValue.ToString("0.00") + "x";
        body.Children.Add(sl); body.Children.Add(lbl);
        Content = g;
        var (ok, _) = WindowBuilder.OkCancel(this, footer);
        ok.Click += (_, _) => Speed = sl.Value;
    }
}

public class SplitNWindow : Window
{
    public int Parts { get; private set; } = 2;
    public SplitNWindow()
    {
        Title = "Split into N Parts"; Width = 360; Height = 220;
        Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x1F));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var (g, body, footer) = WindowBuilder.Layout();
        body.Children.Add(WindowBuilder.Lbl("Split the selected clip into how many equal parts?"));
        var partsBox = WindowBuilder.Tb("2");
        partsBox.FontSize = 18;
        partsBox.Padding = new Thickness(8, 4, 8, 4);
        body.Children.Add(partsBox);
        body.Children.Add(WindowBuilder.Lbl("(Enter a number between 2 and 20)"));
        Content = g;
        var (ok, _) = WindowBuilder.OkCancel(this, footer);
        ok.Content = "Split";
        ok.Click += (_, _) =>
        {
            if (int.TryParse(partsBox.Text, out var n) && n >= 2 && n <= 20)
                Parts = n;
            else
                Parts = 2;
        };
    }
}

public class ConfirmWindow : Window
{
    public ConfirmWindow(string title, string msg)
    {
        Title = title; Width = 340; Height = 160;
        Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x1F));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var (g, body, footer) = WindowBuilder.Layout();
        body.Children.Add(new TextBlock { Text = msg, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
        Content = g;
        WindowBuilder.OkCancel(this, footer);
    }
}
