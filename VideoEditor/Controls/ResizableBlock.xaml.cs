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
        // Hide the dashed outline entirely when not selected - only show on selection
        fill.StrokeThickness = sel ? 3 : 0;
        fill.Stroke = sel ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)) : Brushes.Transparent;
        tlThumb.Visibility = trThumb.Visibility = blThumb.Visibility = brThumb.Visibility = sel ? Visibility.Visible : Visibility.Hidden;
    }

    private void ApplyModel()
    {
        Canvas.SetLeft(this, Model.X);
        Canvas.SetTop(this, Model.Y);
        Width = Model.Width;
        Height = Model.Height;
        var brush = new SolidColorBrush(Model.Color);
        fill.Fill = brush;
        fill.Opacity = Model.Mode == BlockMode.Solid ? 0.95 : 0.5;
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
