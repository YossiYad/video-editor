using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using VideoEditor.Models;

namespace VideoEditor.Controls;

public partial class ResizableBlock : UserControl
{
    public VideoBlock Model { get; }
    public event Action<ResizableBlock>? Selected;
    public event Action<ResizableBlock>? Changed;

    private bool _selected;

    public ResizableBlock(VideoBlock model)
    {
        InitializeComponent();
        Model = model;
        ApplyModel();

        dragThumb.DragDelta += OnDrag;
        dragThumb.DragStarted += (_, _) => RaiseSelected();
        dragThumb.PreviewMouseLeftButtonDown += (_, _) => RaiseSelected();

        tlThumb.DragDelta += (s, e) => Resize(e.HorizontalChange, e.VerticalChange, true, true);
        trThumb.DragDelta += (s, e) => Resize(e.HorizontalChange, e.VerticalChange, false, true);
        blThumb.DragDelta += (s, e) => Resize(e.HorizontalChange, e.VerticalChange, true, false);
        brThumb.DragDelta += (s, e) => Resize(e.HorizontalChange, e.VerticalChange, false, false);

        tlThumb.DragStarted += (_, _) => RaiseSelected();
        trThumb.DragStarted += (_, _) => RaiseSelected();
        blThumb.DragStarted += (_, _) => RaiseSelected();
        brThumb.DragStarted += (_, _) => RaiseSelected();

        PreviewMouseLeftButtonDown += (_, _) => RaiseSelected();

        model.PropertyChanged += (_, _) => ApplyModel();
    }

    private void RaiseSelected()
    {
        Selected?.Invoke(this);
        SetSelected(true);
        if (Parent is Canvas c)
        {
            Panel.SetZIndex(this, 100);
            foreach (var child in c.Children)
            {
                if (child is ResizableBlock rb && rb != this)
                    Panel.SetZIndex(rb, 1);
            }
        }
    }

    public void SetSelected(bool sel)
    {
        _selected = sel;
        // Yellow outline (--warn) only when selected; hidden otherwise so the block sits clean on the video.
        fill.StrokeThickness = sel ? 2 : 0;
        fill.Stroke = sel ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD4, 0x3B)) : Brushes.Transparent;
        tlThumb.Visibility = trThumb.Visibility = blThumb.Visibility = brThumb.Visibility = sel ? Visibility.Visible : Visibility.Hidden;
    }

    private void ApplyModel()
    {
        Canvas.SetLeft(this, Model.X);
        Canvas.SetTop(this, Model.Y);
        Width = Model.Width;
        Height = Model.Height;

        // All modes are now FULLY OPAQUE in preview to match the exported result.
        // Each mode also gets a distinct visual pattern + badge so the user knows what will be applied.
        switch (Model.Mode)
        {
            case BlockMode.Solid:
                fill.Fill = new SolidColorBrush(Model.Color);
                fill.Opacity = 1.0;
                patternFill.Visibility = Visibility.Collapsed;
                modeBadgeText.Text = "SOLID";
                break;

            case BlockMode.Blur:
                // Dark frosted-glass look (we can't actually blur the underlying video in WPF preview,
                // so we show a stylized indicator that conveys "this region will be blurred on export").
                fill.Fill = new LinearGradientBrush(
                    Color.FromArgb(0xE6, 0x1A, 0x1F, 0x2C),
                    Color.FromArgb(0xE6, 0x0B, 0x10, 0x1A),
                    90);
                fill.Opacity = 1.0;
                patternFill.Fill = new DrawingBrush
                {
                    TileMode = TileMode.Tile,
                    Viewport = new Rect(0, 0, 24, 24),
                    ViewportUnits = BrushMappingMode.Absolute,
                    Drawing = new GeometryDrawing
                    {
                        Brush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                        Geometry = Geometry.Parse("M0,0 m6,6 a6,6 0 1,0 12,0 a6,6 0 1,0 -12,0")
                    }
                };
                patternFill.Visibility = Visibility.Visible;
                modeBadgeText.Text = "BLUR";
                break;

            case BlockMode.Pixelate:
                // Checkered pattern indicating pixelation
                fill.Fill = new SolidColorBrush(Color.FromArgb(0xE6, 0x2A, 0x20, 0x18));
                fill.Opacity = 1.0;
                patternFill.Fill = new DrawingBrush
                {
                    TileMode = TileMode.Tile,
                    Viewport = new Rect(0, 0, 14, 14),
                    ViewportUnits = BrushMappingMode.Absolute,
                    Drawing = new GeometryDrawing
                    {
                        Brush = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)),
                        Geometry = Geometry.Parse("M0,0 L7,0 L7,7 L0,7 Z M7,7 L14,7 L14,14 L7,14 Z")
                    }
                };
                patternFill.Visibility = Visibility.Visible;
                modeBadgeText.Text = "PIXELATE";
                break;
        }
    }

    private void OnDrag(object sender, DragDeltaEventArgs e)
    {
        var parent = Parent as Canvas;
        if (parent == null) return;
        var newX = Math.Max(0, Math.Min(parent.ActualWidth - Width, Model.X + e.HorizontalChange));
        var newY = Math.Max(0, Math.Min(parent.ActualHeight - Height, Model.Y + e.VerticalChange));
        Model.X = newX;
        Model.Y = newY;
        Changed?.Invoke(this);
    }

    private void Resize(double dx, double dy, bool fromLeft, bool fromTop)
    {
        var parent = Parent as Canvas;
        if (parent == null) return;
        double newX = Model.X, newY = Model.Y, newW = Model.Width, newH = Model.Height;
        if (fromLeft) { newX += dx; newW -= dx; }
        else { newW += dx; }
        if (fromTop) { newY += dy; newH -= dy; }
        else { newH += dy; }
        if (newW < 20) { newW = 20; if (fromLeft) newX = Model.X + Model.Width - 20; }
        if (newH < 20) { newH = 20; if (fromTop) newY = Model.Y + Model.Height - 20; }
        if (newX < 0) { newW += newX; newX = 0; }
        if (newY < 0) { newH += newY; newY = 0; }
        if (newX + newW > parent.ActualWidth) newW = parent.ActualWidth - newX;
        if (newY + newH > parent.ActualHeight) newH = parent.ActualHeight - newY;
        Model.X = newX; Model.Y = newY; Model.Width = newW; Model.Height = newH;
        Changed?.Invoke(this);
    }
}
