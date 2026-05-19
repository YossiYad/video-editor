using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace VideoEditor.Views;

public class VisualRegionPickerWindow : Window
{
    public int X { get; private set; }
    public int Y { get; private set; }
    public int W { get; private set; }
    public int H { get; private set; }

    private readonly int _videoWidth;
    private readonly int _videoHeight;
    private readonly Image _bgImage;
    private readonly Canvas _overlayCanvas;
    private Grid? _selectionBox;
    private readonly TextBlock _dimensionsLabel;
    private double _selX = 100, _selY = 100, _selW = 200, _selH = 100;
    private bool _initialized;
    private readonly bool _darkenOutside;
    private readonly string _selectionLabel;
    private readonly Color _accentColor;
    private Rectangle[]? _outsideOverlays;

    public VisualRegionPickerWindow(string framePath, int videoWidth, int videoHeight,
        string title, string instructions,
        bool darkenOutside = false, string selectionLabel = "🎯 LOGO AREA", Color? accentColor = null)
    {
        _darkenOutside = darkenOutside;
        _selectionLabel = selectionLabel;
        _accentColor = accentColor ?? (darkenOutside ? Color.FromRgb(0x4D, 0xFF, 0x88) : Color.FromRgb(0xFF, 0xD7, 0x00));
        _videoWidth = videoWidth;
        _videoHeight = videoHeight;
        Title = title;
        Width = 980;
        Height = 740;
        MinWidth = 600;
        MinHeight = 500;
        Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x1F));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var hdrPanel = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x2A)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var hdrStack = new StackPanel { Orientation = Orientation.Horizontal };
        hdrStack.Children.Add(new TextBlock
        {
            Text = "📍",
            FontSize = 22,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });
        hdrStack.Children.Add(new TextBlock
        {
            Text = instructions,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 800
        });
        hdrPanel.Child = hdrStack;
        Grid.SetRow(hdrPanel, 0);
        grid.Children.Add(hdrPanel);

        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x52)),
            BorderThickness = new Thickness(1),
            Background = Brushes.Black,
            ClipToBounds = true,
            CornerRadius = new CornerRadius(4)
        };
        var imgHost = new Grid();
        _bgImage = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(framePath);
            bmp.EndInit();
            bmp.Freeze();
            _bgImage.Source = bmp;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load preview frame: " + ex.Message);
        }
        imgHost.Children.Add(_bgImage);
        _overlayCanvas = new Canvas
        {
            Background = Brushes.Transparent,
            ClipToBounds = true
        };
        imgHost.Children.Add(_overlayCanvas);
        border.Child = imgHost;
        Grid.SetRow(border, 1);
        grid.Children.Add(border);

        _dimensionsLabel = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xC0)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Margin = new Thickness(4, 10, 0, 0),
            Text = "Drag to select an area..."
        };
        Grid.SetRow(_dimensionsLabel, 2);
        grid.Children.Add(_dimensionsLabel);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var cancelBtn = new Button
        {
            Content = "Cancel",
            Width = 100,
            Height = 34,
            Margin = new Thickness(4),
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x3E)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x52)),
            Cursor = Cursors.Hand,
            FontWeight = FontWeights.SemiBold
        };
        cancelBtn.Click += (_, _) => { DialogResult = false; Close(); };
        var okBtn = new Button
        {
            Content = "Apply",
            Width = 120,
            Height = 34,
            Margin = new Thickness(4),
            Background = new SolidColorBrush(Color.FromRgb(0x7C, 0x4D, 0xFF)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x7C, 0x4D, 0xFF)),
            Cursor = Cursors.Hand,
            FontWeight = FontWeights.Bold
        };
        okBtn.Click += (_, _) => CommitSelection();
        footer.Children.Add(cancelBtn);
        footer.Children.Add(okBtn);
        Grid.SetRow(footer, 3);
        grid.Children.Add(footer);

        Content = grid;

        _overlayCanvas.SizeChanged += (_, _) =>
        {
            if (!_initialized && _overlayCanvas.ActualWidth > 50 && _overlayCanvas.ActualHeight > 50)
            {
                InitSelection();
                _initialized = true;
            }
            else if (_initialized)
            {
                UpdateSelectionVisual();
            }
        };
    }

    private void InitSelection()
    {
        var rect = ComputeImageDisplayRect();
        if (_darkenOutside)
        {
            // Crop mode - default to most of the image (user usually crops edges)
            _selX = rect.X + rect.Width * 0.05;
            _selY = rect.Y + rect.Height * 0.05;
            _selW = rect.Width * 0.9;
            _selH = rect.Height * 0.9;
        }
        else
        {
            // Region mode (e.g., Remove Logo) - default to small area in corner
            _selX = rect.X + rect.Width * 0.1;
            _selY = rect.Y + rect.Height * 0.1;
            _selW = Math.Max(60, rect.Width * 0.3);
            _selH = Math.Max(40, rect.Height * 0.2);
        }
        CreateSelectionUI();
    }

    private Rect ComputeImageDisplayRect()
    {
        if (_bgImage.Source == null) return new Rect(0, 0, _overlayCanvas.ActualWidth, _overlayCanvas.ActualHeight);
        double natW = _bgImage.Source.Width;
        double natH = _bgImage.Source.Height;
        double contW = _overlayCanvas.ActualWidth;
        double contH = _overlayCanvas.ActualHeight;
        if (natW <= 0 || natH <= 0 || contW <= 0 || contH <= 0) return new Rect(0, 0, contW, contH);
        double scale = Math.Min(contW / natW, contH / natH);
        double w = natW * scale;
        double h = natH * scale;
        double x = (contW - w) / 2;
        double y = (contH - h) / 2;
        return new Rect(x, y, w, h);
    }

    private void CreateSelectionUI()
    {
        _overlayCanvas.Children.Clear();

        // Add dark overlays for the area OUTSIDE the selection (Crop mode)
        if (_darkenOutside)
        {
            _outsideOverlays = new Rectangle[4];
            var darkBrush = new SolidColorBrush(Color.FromArgb(0xC0, 0x00, 0x00, 0x00));
            for (int i = 0; i < 4; i++)
            {
                _outsideOverlays[i] = new Rectangle { Fill = darkBrush, IsHitTestVisible = false };
                _overlayCanvas.Children.Add(_outsideOverlays[i]);
            }
        }

        _selectionBox = new Grid { Width = _selW, Height = _selH };

        var fill = new Rectangle
        {
            Fill = _darkenOutside
                ? Brushes.Transparent
                : new SolidColorBrush(Color.FromArgb(0x55, _accentColor.R, _accentColor.G, _accentColor.B)),
            Stroke = new SolidColorBrush(_accentColor),
            StrokeThickness = 3,
            StrokeDashArray = new DoubleCollection { 6, 3 }
        };
        _selectionBox.Children.Add(fill);

        var glow = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromArgb(0x50, _accentColor.R, _accentColor.G, _accentColor.B)),
            StrokeThickness = 8,
            IsHitTestVisible = false,
            Margin = new Thickness(-4)
        };
        _selectionBox.Children.Add(glow);

        var label = new Border
        {
            Background = new SolidColorBrush(_accentColor),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2, 6, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -22, 0, 0),
            IsHitTestVisible = false
        };
        var labelText = new TextBlock
        {
            Text = _selectionLabel,
            Foreground = Brushes.Black,
            FontWeight = FontWeights.Bold,
            FontSize = 10
        };
        label.Child = labelText;
        _selectionBox.Children.Add(label);

        var dragThumb = new Thumb { Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        dragThumb.Template = MakeTransparent();
        _selectionBox.Children.Add(dragThumb);

        var tlT = MakeCornerThumb(HorizontalAlignment.Left, VerticalAlignment.Top, Cursors.SizeNWSE);
        var trT = MakeCornerThumb(HorizontalAlignment.Right, VerticalAlignment.Top, Cursors.SizeNESW);
        var blT = MakeCornerThumb(HorizontalAlignment.Left, VerticalAlignment.Bottom, Cursors.SizeNESW);
        var brT = MakeCornerThumb(HorizontalAlignment.Right, VerticalAlignment.Bottom, Cursors.SizeNWSE);
        _selectionBox.Children.Add(tlT);
        _selectionBox.Children.Add(trT);
        _selectionBox.Children.Add(blT);
        _selectionBox.Children.Add(brT);

        Canvas.SetLeft(_selectionBox, _selX);
        Canvas.SetTop(_selectionBox, _selY);
        _overlayCanvas.Children.Add(_selectionBox);

        double startX = 0, startY = 0, startW = 0, startH = 0;

        dragThumb.DragStarted += (_, _) => { startX = _selX; startY = _selY; };
        dragThumb.DragDelta += (_, e) =>
        {
            var imgRect = ComputeImageDisplayRect();
            _selX = Math.Max(imgRect.X, Math.Min(imgRect.X + imgRect.Width - _selW, startX + e.HorizontalChange));
            _selY = Math.Max(imgRect.Y, Math.Min(imgRect.Y + imgRect.Height - _selH, startY + e.VerticalChange));
            UpdateSelectionVisual();
        };

        tlT.DragStarted += (_, _) => { startX = _selX; startY = _selY; startW = _selW; startH = _selH; };
        tlT.DragDelta += (_, e) => ResizeFromCorner(true, true, startX, startY, startW, startH, e.HorizontalChange, e.VerticalChange);
        trT.DragStarted += (_, _) => { startX = _selX; startY = _selY; startW = _selW; startH = _selH; };
        trT.DragDelta += (_, e) => ResizeFromCorner(false, true, startX, startY, startW, startH, e.HorizontalChange, e.VerticalChange);
        blT.DragStarted += (_, _) => { startX = _selX; startY = _selY; startW = _selW; startH = _selH; };
        blT.DragDelta += (_, e) => ResizeFromCorner(true, false, startX, startY, startW, startH, e.HorizontalChange, e.VerticalChange);
        brT.DragStarted += (_, _) => { startX = _selX; startY = _selY; startW = _selW; startH = _selH; };
        brT.DragDelta += (_, e) => ResizeFromCorner(false, false, startX, startY, startW, startH, e.HorizontalChange, e.VerticalChange);

        UpdateSelectionVisual();
    }

    private Thumb MakeCornerThumb(HorizontalAlignment h, VerticalAlignment v, Cursor cursor)
    {
        var t = new Thumb
        {
            Width = 16,
            Height = 16,
            HorizontalAlignment = h,
            VerticalAlignment = v,
            Cursor = cursor,
            Margin = new Thickness(-8)
        };
        t.Template = MakeHandle();
        return t;
    }

    private void ResizeFromCorner(bool fromLeft, bool fromTop, double startX, double startY,
        double startW, double startH, double dx, double dy)
    {
        var imgRect = ComputeImageDisplayRect();
        double newX = startX, newY = startY, newW = startW, newH = startH;
        if (fromLeft) { newX = startX + dx; newW = startW - dx; }
        else { newW = startW + dx; }
        if (fromTop) { newY = startY + dy; newH = startH - dy; }
        else { newH = startH + dy; }
        if (newW < 20) { newW = 20; if (fromLeft) newX = startX + startW - 20; }
        if (newH < 20) { newH = 20; if (fromTop) newY = startY + startH - 20; }
        if (newX < imgRect.X) { newW += newX - imgRect.X; newX = imgRect.X; }
        if (newY < imgRect.Y) { newH += newY - imgRect.Y; newY = imgRect.Y; }
        if (newX + newW > imgRect.X + imgRect.Width) newW = imgRect.X + imgRect.Width - newX;
        if (newY + newH > imgRect.Y + imgRect.Height) newH = imgRect.Y + imgRect.Height - newY;
        _selX = newX; _selY = newY; _selW = newW; _selH = newH;
        UpdateSelectionVisual();
    }

    private void UpdateSelectionVisual()
    {
        if (_selectionBox == null) return;
        Canvas.SetLeft(_selectionBox, _selX);
        Canvas.SetTop(_selectionBox, _selY);
        _selectionBox.Width = _selW;
        _selectionBox.Height = _selH;
        var imgRect = ComputeImageDisplayRect();
        if (imgRect.Width > 0)
        {
            var scaleX = _videoWidth / imgRect.Width;
            var scaleY = _videoHeight / imgRect.Height;
            var vx = (int)((_selX - imgRect.X) * scaleX);
            var vy = (int)((_selY - imgRect.Y) * scaleY);
            var vw = (int)(_selW * scaleX);
            var vh = (int)(_selH * scaleY);
            var prefix = _darkenOutside ? "Crop area (kept)" : "Selected area";
            _dimensionsLabel.Text = $"{prefix}:  X={vx}  Y={vy}  Width={vw}  Height={vh}     (Video: {_videoWidth}×{_videoHeight})";
        }
        // Update the 4 dark overlays surrounding the selection (Crop mode)
        if (_outsideOverlays != null)
        {
            var img = ComputeImageDisplayRect();
            // Top band
            Canvas.SetLeft(_outsideOverlays[0], img.X);
            Canvas.SetTop(_outsideOverlays[0], img.Y);
            _outsideOverlays[0].Width = img.Width;
            _outsideOverlays[0].Height = Math.Max(0, _selY - img.Y);
            // Bottom band
            Canvas.SetLeft(_outsideOverlays[1], img.X);
            Canvas.SetTop(_outsideOverlays[1], _selY + _selH);
            _outsideOverlays[1].Width = img.Width;
            _outsideOverlays[1].Height = Math.Max(0, img.Y + img.Height - (_selY + _selH));
            // Left band
            Canvas.SetLeft(_outsideOverlays[2], img.X);
            Canvas.SetTop(_outsideOverlays[2], _selY);
            _outsideOverlays[2].Width = Math.Max(0, _selX - img.X);
            _outsideOverlays[2].Height = _selH;
            // Right band
            Canvas.SetLeft(_outsideOverlays[3], _selX + _selW);
            Canvas.SetTop(_outsideOverlays[3], _selY);
            _outsideOverlays[3].Width = Math.Max(0, img.X + img.Width - (_selX + _selW));
            _outsideOverlays[3].Height = _selH;
        }
    }

    private void CommitSelection()
    {
        var imgRect = ComputeImageDisplayRect();
        if (imgRect.Width > 0)
        {
            var scaleX = _videoWidth / imgRect.Width;
            var scaleY = _videoHeight / imgRect.Height;
            X = Math.Max(0, (int)((_selX - imgRect.X) * scaleX));
            Y = Math.Max(0, (int)((_selY - imgRect.Y) * scaleY));
            W = Math.Min(_videoWidth - X, (int)(_selW * scaleX));
            H = Math.Min(_videoHeight - Y, (int)(_selH * scaleY));
            if (W % 2 != 0) W = Math.Max(2, W - 1);
            if (H % 2 != 0) H = Math.Max(2, H - 1);
        }
        DialogResult = true;
        Close();
    }

    private ControlTemplate MakeHandle()
    {
        var t = new ControlTemplate(typeof(Thumb));
        var rect = new FrameworkElementFactory(typeof(Rectangle));
        rect.SetValue(Shape.FillProperty, new SolidColorBrush(_accentColor));
        rect.SetValue(Shape.StrokeProperty, Brushes.White);
        rect.SetValue(Shape.StrokeThicknessProperty, 2.0);
        t.VisualTree = rect;
        return t;
    }

    private ControlTemplate MakeTransparent()
    {
        var t = new ControlTemplate(typeof(Thumb));
        var rect = new FrameworkElementFactory(typeof(Rectangle));
        rect.SetValue(Shape.FillProperty, Brushes.Transparent);
        t.VisualTree = rect;
        return t;
    }
}
