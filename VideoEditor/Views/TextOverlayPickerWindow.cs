using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VideoEditor.Views;

// TextOverlayOptions moved to VideoEditor.Core (Models/TextOverlayOptions.cs), kept in
// this same namespace so references resolve unchanged.

public class TextOverlayPickerWindow : Window
{
    public TextOverlayOptions Result { get; } = new();

    private readonly int _videoWidth, _videoHeight;
    private readonly Image _bgImage;
    private readonly Canvas _overlayCanvas;
    private readonly Border _textBg;
    private readonly TextBlock _textBlock;

    private double _scale = 1.0;
    private double _previewX = 60, _previewY = 60;
    private bool _layoutReady;
    private bool _dragging;
    private Point _dragOffset;

    private static readonly (string label, string hex, Color brush)[] TextSwatches =
    {
        ("White",  "white",   Colors.White),
        ("Black",  "black",   Colors.Black),
        ("Yellow", "yellow",  Color.FromRgb(0xFF, 0xD4, 0x3B)),
        ("Red",    "red",     Color.FromRgb(0xFF, 0x4B, 0x4B)),
        ("Pink",   "magenta", Color.FromRgb(0xFF, 0x4B, 0xA8)),
        ("Cyan",   "cyan",    Color.FromRgb(0x4B, 0xE0, 0xFF)),
        ("Green",  "0x51CF66",Color.FromRgb(0x51, 0xCF, 0x66)),
        ("Purple", "0x8B5CFF",Color.FromRgb(0x8B, 0x5C, 0xFF)),
    };

    private static readonly (string label, string hex, Color brush, double opacity)[] BgSwatches =
    {
        ("Black",       "black",   Colors.Black,                       0.55),
        ("White",       "white",   Colors.White,                       0.55),
        ("Purple",      "0x8B5CFF",Color.FromRgb(0x8B, 0x5C, 0xFF),    0.65),
        ("Yellow",      "yellow",  Color.FromRgb(0xFF, 0xD4, 0x3B),    0.75),
        ("Red",         "red",     Color.FromRgb(0xFF, 0x4B, 0x4B),    0.65),
        ("Transparent", "black",   Colors.Black,                       0.0),
    };

    public TextOverlayPickerWindow(string framePath, int videoWidth, int videoHeight,
                                   TextOverlayOptions? initial = null)
    {
        _videoWidth = Math.Max(1, videoWidth);
        _videoHeight = Math.Max(1, videoHeight);
        if (initial != null)
        {
            Result.Text = initial.Text;
            Result.X = initial.X;
            Result.Y = initial.Y;
            Result.FontSize = initial.FontSize;
            Result.FontColor = initial.FontColor;
            Result.Bold = initial.Bold;
            Result.Italic = initial.Italic;
            Result.BackgroundEnabled = initial.BackgroundEnabled;
            Result.BackgroundColor = initial.BackgroundColor;
            Result.BackgroundOpacity = initial.BackgroundOpacity;
        }
        if (string.IsNullOrEmpty(Result.Text)) Result.Text = "Sample Text";

        Title = "Add Text";
        Width = 1180;
        Height = 760;
        MinWidth = 900;
        MinHeight = 560;
        Background = new SolidColorBrush(Color.FromRgb(0x07, 0x08, 0x0D));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI, Heebo");

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ===== Sidebar (left) =====
        var sidebar = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x11, 0x19)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(16)
        };
        var ctrls = new StackPanel();
        sidebar.Content = ctrls;
        Grid.SetColumn(sidebar, 0); Grid.SetRow(sidebar, 0);
        root.Children.Add(sidebar);

        ctrls.Children.Add(MakeHeader("📝", "Add Text", "Drag the text on the video to position it. The result burns into the clip on export."));

        // Text input
        ctrls.Children.Add(MakeLabel("TEXT"));
        var textInput = new TextBox
        {
            Text = Result.Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 84,
            Background = new SolidColorBrush(Color.FromRgb(0x07, 0x08, 0x0D)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xF2)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 13,
            CaretBrush = Brushes.White,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        textInput.TextChanged += (_, _) => { Result.Text = textInput.Text; UpdatePreviewText(); };
        ctrls.Children.Add(textInput);

        // Font size
        ctrls.Children.Add(MakeLabel("SIZE"));
        var sizeRow = new Grid();
        sizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        var sizeSlider = new Slider { Minimum = 16, Maximum = 200, Value = Result.FontSize, VerticalAlignment = VerticalAlignment.Center };
        var sizeLabel = new TextBlock
        {
            Text = Result.FontSize.ToString(),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xF2)),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right
        };
        sizeSlider.ValueChanged += (_, _) =>
        {
            Result.FontSize = (int)sizeSlider.Value;
            sizeLabel.Text = Result.FontSize.ToString();
            UpdatePreviewSize();
        };
        Grid.SetColumn(sizeSlider, 0); sizeRow.Children.Add(sizeSlider);
        Grid.SetColumn(sizeLabel, 1); sizeRow.Children.Add(sizeLabel);
        ctrls.Children.Add(sizeRow);

        // Style - Bold / Italic
        ctrls.Children.Add(MakeLabel("STYLE"));
        var styleRow = new StackPanel { Orientation = Orientation.Horizontal };
        var boldBtn = MakeToggle("B", Result.Bold, isOn =>
        {
            Result.Bold = isOn;
            _textBlock!.FontWeight = isOn ? FontWeights.Bold : FontWeights.Normal;
        });
        boldBtn.FontWeight = FontWeights.Bold;
        var italicBtn = MakeToggle("I", Result.Italic, isOn =>
        {
            Result.Italic = isOn;
            _textBlock!.FontStyle = isOn ? FontStyles.Italic : FontStyles.Normal;
        });
        italicBtn.FontStyle = FontStyles.Italic;
        styleRow.Children.Add(boldBtn);
        styleRow.Children.Add(italicBtn);
        ctrls.Children.Add(styleRow);

        // Text color
        ctrls.Children.Add(MakeLabel("TEXT COLOR"));
        var colorGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var (label, hex, color) in TextSwatches)
        {
            var swatch = MakeSwatch(color, () =>
            {
                Result.FontColor = hex;
                _textBlock!.Foreground = new SolidColorBrush(color);
            }, hex == Result.FontColor);
            swatch.ToolTip = label;
            colorGrid.Children.Add(swatch);
        }
        ctrls.Children.Add(colorGrid);

        // Background
        ctrls.Children.Add(MakeLabel("BACKGROUND HIGHLIGHT"));
        var bgToggleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        var bgToggle = MakeToggle(Result.BackgroundEnabled ? "ON" : "OFF", Result.BackgroundEnabled, isOn =>
        {
            Result.BackgroundEnabled = isOn;
            UpdatePreviewBg();
        });
        bgToggle.MinWidth = 56;
        bgToggle.FontWeight = FontWeights.SemiBold;
        bgToggleRow.Children.Add(bgToggle);
        ctrls.Children.Add(bgToggleRow);

        var bgGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var (label, hex, color, opacity) in BgSwatches)
        {
            var swatch = MakeSwatch(opacity > 0 ? color : Colors.Transparent, () =>
            {
                Result.BackgroundColor = hex;
                Result.BackgroundOpacity = opacity;
                UpdatePreviewBg();
            }, hex == Result.BackgroundColor && Math.Abs(opacity - Result.BackgroundOpacity) < 0.01);
            swatch.ToolTip = label;
            if (opacity == 0)
            {
                // Transparent swatch: show diagonal stripe to distinguish from black at opacity 0
                swatch.Background = MakeTransparentChecker();
            }
            bgGrid.Children.Add(swatch);
        }
        ctrls.Children.Add(bgGrid);

        // ===== Canvas (right) =====
        var canvasHost = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x05, 0x06, 0x09)) };
        Grid.SetColumn(canvasHost, 1); Grid.SetRow(canvasHost, 0);
        root.Children.Add(canvasHost);

        var canvasOuter = new Grid
        {
            Margin = new Thickness(20),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        canvasHost.Children.Add(canvasOuter);

        var canvasBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Black
        };
        canvasOuter.Children.Add(canvasBorder);

        var imgAndOverlay = new Grid();
        _bgImage = new Image { Stretch = Stretch.Uniform };
        try
        {
            if (File.Exists(framePath))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(framePath);
                bmp.EndInit();
                bmp.Freeze();
                _bgImage.Source = bmp;
            }
        }
        catch { }
        imgAndOverlay.Children.Add(_bgImage);

        _overlayCanvas = new Canvas { Background = Brushes.Transparent };
        imgAndOverlay.Children.Add(_overlayCanvas);
        canvasBorder.Child = imgAndOverlay;

        _textBg = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb((byte)(Result.BackgroundOpacity * 255), 0, 0, 0)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(0),
            IsHitTestVisible = false
        };
        _textBlock = new TextBlock
        {
            Text = Result.Text,
            Foreground = new SolidColorBrush(SwatchColorForHex(Result.FontColor)),
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = Result.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = Result.Italic ? FontStyles.Italic : FontStyles.Normal,
            TextWrapping = TextWrapping.Wrap
        };
        _textBg.Child = _textBlock;

        Canvas.SetLeft(_textBg, _previewX);
        Canvas.SetTop(_textBg, _previewY);
        _overlayCanvas.Children.Add(_textBg);

        _textBg.IsHitTestVisible = true;
        _textBg.Cursor = Cursors.SizeAll;
        _textBg.MouseLeftButtonDown += TextBg_MouseDown;
        _textBg.MouseLeftButtonUp += TextBg_MouseUp;
        _textBg.MouseMove += TextBg_MouseMove;

        _overlayCanvas.SizeChanged += (_, _) => RecomputeScale();
        Loaded += (_, _) => RecomputeScale();

        // ===== Footer =====
        var footer = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x11, 0x19)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(16, 10, 16, 10)
        };
        var footerGrid = new Grid();
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var hint = new TextBlock
        {
            Text = "Drag the text on the video to position it.",
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x91, 0xA8)),
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(hint, 0); footerGrid.Children.Add(hint);

        var btns = new StackPanel { Orientation = Orientation.Horizontal };
        var cancel = new Button
        {
            Content = "Cancel",
            MinWidth = 96, Height = 34,
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x1D, 0x22, 0x31)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xF2)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            FontSize = 12.5
        };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        btns.Children.Add(cancel);

        var ok = new Button
        {
            Content = "Add text",
            MinWidth = 130, Height = 34,
            Background = new LinearGradientBrush(Color.FromRgb(0xA0, 0x7C, 0xFF), Color.FromRgb(0x8B, 0x5C, 0xFF), 90),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x6E, 0x44, 0xD6)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            FontSize = 13,
            FontWeight = FontWeights.Bold
        };
        ok.Click += (_, _) =>
        {
            CommitVideoCoords();
            Result.Text = textInput.Text ?? "";
            DialogResult = true;
            Close();
        };
        btns.Children.Add(ok);

        Grid.SetColumn(btns, 1); footerGrid.Children.Add(btns);
        footer.Child = footerGrid;
        Grid.SetColumnSpan(footer, 2); Grid.SetRow(footer, 1);
        root.Children.Add(footer);

        Content = root;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; Close(); } };

        FlowDirection = VideoEditor.Services.Localization.IsHebrew ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        VideoEditor.Services.Localization.TranslateTree(this);

        UpdatePreviewBg();
        UpdatePreviewSize();
    }

    private void RecomputeScale()
    {
        if (_overlayCanvas.ActualWidth <= 0 || _overlayCanvas.ActualHeight <= 0) return;
        var scaleX = _overlayCanvas.ActualWidth / _videoWidth;
        var scaleY = _overlayCanvas.ActualHeight / _videoHeight;
        _scale = Math.Min(scaleX, scaleY);
        if (_scale <= 0) _scale = 1;

        if (!_layoutReady)
        {
            // Default position: top-left with a margin proportional to the video.
            _previewX = Result.X * _scale;
            _previewY = Result.Y * _scale;
            if (Result.X == 0 && Result.Y == 0)
            {
                _previewX = Math.Max(20, _overlayCanvas.ActualWidth * 0.06);
                _previewY = Math.Max(20, _overlayCanvas.ActualHeight * 0.08);
            }
            _layoutReady = true;
        }
        Canvas.SetLeft(_textBg, _previewX);
        Canvas.SetTop(_textBg, _previewY);
        UpdatePreviewSize();
    }

    private void UpdatePreviewSize()
    {
        if (_textBlock == null) return;
        // Map video-space font size to preview-space pixels.
        _textBlock.FontSize = Math.Max(6, Result.FontSize * _scale);
        var padding = Math.Max(2, Result.BackgroundPadding * _scale);
        _textBg.Padding = new Thickness(padding, padding * 0.55, padding, padding * 0.55);
    }

    private void UpdatePreviewText()
    {
        if (_textBlock != null) _textBlock.Text = string.IsNullOrEmpty(Result.Text) ? " " : Result.Text;
    }

    private void UpdatePreviewBg()
    {
        if (_textBg == null) return;
        if (!Result.BackgroundEnabled || Result.BackgroundOpacity <= 0)
        {
            _textBg.Background = Brushes.Transparent;
            return;
        }
        var c = SwatchColorForHex(Result.BackgroundColor);
        _textBg.Background = new SolidColorBrush(Color.FromArgb((byte)(Result.BackgroundOpacity * 255), c.R, c.G, c.B));
    }

    private void TextBg_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragOffset = e.GetPosition(_textBg);
        _textBg.CaptureMouse();
        e.Handled = true;
    }
    private void TextBg_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(_overlayCanvas);
        var maxX = Math.Max(0, _overlayCanvas.ActualWidth - _textBg.ActualWidth);
        var maxY = Math.Max(0, _overlayCanvas.ActualHeight - _textBg.ActualHeight);
        _previewX = Math.Clamp(p.X - _dragOffset.X, 0, maxX);
        _previewY = Math.Clamp(p.Y - _dragOffset.Y, 0, maxY);
        Canvas.SetLeft(_textBg, _previewX);
        Canvas.SetTop(_textBg, _previewY);
    }
    private void TextBg_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        _textBg.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void CommitVideoCoords()
    {
        if (_scale <= 0) _scale = 1;
        Result.X = (int)Math.Round(_previewX / _scale);
        Result.Y = (int)Math.Round(_previewY / _scale);
    }

    // ===== UI helpers =====
    private static StackPanel MakeHeader(string emoji, string title, string sub)
    {
        var s = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        titleRow.Children.Add(new TextBlock { Text = emoji, FontSize = 22, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xF2)), VerticalAlignment = VerticalAlignment.Center });
        s.Children.Add(titleRow);
        s.Children.Add(new TextBlock
        {
            Text = sub,
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x91, 0xA8)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });
        return s;
    }

    private static TextBlock MakeLabel(string t) => new()
    {
        Text = t,
        FontSize = 10.5,
        FontWeight = FontWeights.Bold,
        Foreground = new SolidColorBrush(Color.FromRgb(0x6E, 0x76, 0x8A)),
        Margin = new Thickness(0, 14, 0, 6)
    };

    private static Border MakeSwatch(Color c, Action onClick, bool selected)
    {
        var border = new Border
        {
            Width = 50, Height = 30,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(c),
            BorderBrush = new SolidColorBrush(selected
                ? Color.FromRgb(0xA0, 0x7C, 0xFF)
                : Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(selected ? 2 : 1),
            Margin = new Thickness(2),
            Cursor = Cursors.Hand
        };
        border.MouseLeftButtonUp += (_, _) => { onClick(); HighlightSwatch(border); };
        return border;
    }

    private static void HighlightSwatch(Border self)
    {
        if (self.Parent is Panel parent)
        {
            foreach (var child in parent.Children)
            {
                if (child is Border b)
                {
                    b.BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
                    b.BorderThickness = new Thickness(1);
                }
            }
        }
        self.BorderBrush = new SolidColorBrush(Color.FromRgb(0xA0, 0x7C, 0xFF));
        self.BorderThickness = new Thickness(2);
    }

    private static Button MakeToggle(string label, bool initial, Action<bool> onChange)
    {
        var btn = new Button
        {
            Content = label,
            MinWidth = 38, Height = 32,
            Margin = new Thickness(0, 0, 6, 0),
            Background = new SolidColorBrush(initial ? Color.FromRgb(0x8B, 0x5C, 0xFF) : Color.FromRgb(0x1D, 0x22, 0x31)),
            Foreground = new SolidColorBrush(initial ? Colors.White : Color.FromRgb(0xE8, 0xEA, 0xF2)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            FontSize = 13
        };
        bool state = initial;
        btn.Click += (_, _) =>
        {
            state = !state;
            btn.Background = new SolidColorBrush(state ? Color.FromRgb(0x8B, 0x5C, 0xFF) : Color.FromRgb(0x1D, 0x22, 0x31));
            btn.Foreground = new SolidColorBrush(state ? Colors.White : Color.FromRgb(0xE8, 0xEA, 0xF2));
            onChange(state);
        };
        return btn;
    }

    private static Brush MakeTransparentChecker()
    {
        var brush = new DrawingBrush
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 8, 8),
            ViewportUnits = BrushMappingMode.Absolute
        };
        var dg = new DrawingGroup();
        dg.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x40, 0x46, 0x55)),
            null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        dg.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x3D)),
            null, new RectangleGeometry(new Rect(0, 0, 4, 4))));
        dg.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x3D)),
            null, new RectangleGeometry(new Rect(4, 4, 4, 4))));
        brush.Drawing = dg;
        return brush;
    }

    private static Color SwatchColorForHex(string h)
    {
        // Resolve ffmpeg-style color names + 0xRRGGBB + #RRGGBB to a WPF Color for the preview.
        if (string.IsNullOrEmpty(h)) return Colors.White;
        var name = h.Trim();
        if (name.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return Colors.Transparent;
        if (name.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) name = "#" + name.Substring(2);
        try
        {
            if (name.StartsWith("#"))
            {
                var c = (Color)ColorConverter.ConvertFromString(name);
                return c;
            }
            return (Color)ColorConverter.ConvertFromString(name);
        }
        catch { return Colors.White; }
    }
}
