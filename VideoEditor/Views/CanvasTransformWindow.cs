using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VideoEditor.Models;

namespace VideoEditor.Views;

public class CanvasTransformWindow : Window
{
    private readonly TextBox _posX;
    private readonly TextBox _posY;
    private readonly TextBox _scale;
    private readonly TextBox _scaleX;
    private readonly TextBox _scaleY;
    private readonly TextBox _rotation;
    private readonly TextBox _cropLeft;
    private readonly TextBox _cropTop;
    private readonly TextBox _cropRight;
    private readonly TextBox _cropBottom;

    public double CanvasOffsetX { get; private set; }
    public double CanvasOffsetY { get; private set; }
    public double CanvasScale { get; private set; }
    public double CanvasScaleX { get; private set; }
    public double CanvasScaleY { get; private set; }
    public double RotateDegrees { get; private set; }
    public double CropLeft { get; private set; }
    public double CropTop { get; private set; }
    public double CropRight { get; private set; }
    public double CropBottom { get; private set; }

    public CanvasTransformWindow(VideoClip clip, double canvasWidth, double canvasHeight)
    {
        var chrome = WindowBuilder.Build(this, "#", "Edit Transform",
            "Numerically edit the selected clip transform on the preview canvas.", 560, 620, "Apply");

        CanvasOffsetX = clip.CanvasOffsetX;
        CanvasOffsetY = clip.CanvasOffsetY;
        CanvasScale = clip.CanvasScale;
        CanvasScaleX = clip.CanvasScaleX;
        CanvasScaleY = clip.CanvasScaleY;
        RotateDegrees = clip.RotateDegrees;
        CropLeft = clip.CanvasCropLeft;
        CropTop = clip.CanvasCropTop;
        CropRight = clip.CanvasCropRight;
        CropBottom = clip.CanvasCropBottom;

        _posX = Box((clip.CanvasOffsetX * Math.Max(1, canvasWidth)).ToString("0.##", CultureInfo.InvariantCulture));
        _posY = Box((clip.CanvasOffsetY * Math.Max(1, canvasHeight)).ToString("0.##", CultureInfo.InvariantCulture));
        _scale = Box((clip.CanvasScale * 100).ToString("0.##", CultureInfo.InvariantCulture));
        _scaleX = Box((clip.CanvasScaleX * 100).ToString("0.##", CultureInfo.InvariantCulture));
        _scaleY = Box((clip.CanvasScaleY * 100).ToString("0.##", CultureInfo.InvariantCulture));
        _rotation = Box(clip.RotateDegrees.ToString("0.##", CultureInfo.InvariantCulture));
        _cropLeft = Box(clip.CanvasCropLeft.ToString("0.##", CultureInfo.InvariantCulture));
        _cropTop = Box(clip.CanvasCropTop.ToString("0.##", CultureInfo.InvariantCulture));
        _cropRight = Box(clip.CanvasCropRight.ToString("0.##", CultureInfo.InvariantCulture));
        _cropBottom = Box(clip.CanvasCropBottom.ToString("0.##", CultureInfo.InvariantCulture));

        chrome.Body.Children.Add(Section("Position", "Pixels from canvas center", Row(("X", _posX), ("Y", _posY))));
        chrome.Body.Children.Add(Section("Scale", "100% is the current fit-to-canvas baseline", Row(("Uniform %", _scale), ("X %", _scaleX), ("Y %", _scaleY))));
        chrome.Body.Children.Add(Section("Rotation", "Degrees clockwise", Row(("Degrees", _rotation))));
        chrome.Body.Children.Add(Section("Crop", "Source pixels, matching OBS crop fields", Row(("Left", _cropLeft), ("Top", _cropTop), ("Right", _cropRight), ("Bottom", _cropBottom))));

        chrome.Footer.Children.Add(WindowBuilder.FooterInfo("Tip: use the preview handles for rough placement, then refine here."));
        chrome.Primary.Click += (_, _) =>
        {
            try
            {
                CanvasOffsetX = Parse(_posX, -canvasWidth * 2, canvasWidth * 2) / Math.Max(1, canvasWidth);
                CanvasOffsetY = Parse(_posY, -canvasHeight * 2, canvasHeight * 2) / Math.Max(1, canvasHeight);
                CanvasScale = Parse(_scale, 5, 800) / 100.0;
                CanvasScaleX = Parse(_scaleX, 5, 800) / 100.0;
                CanvasScaleY = Parse(_scaleY, 5, 800) / 100.0;
                RotateDegrees = ((Parse(_rotation, -3600, 3600) % 360) + 360) % 360;
                CropLeft = Parse(_cropLeft, 0, 100000);
                CropTop = Parse(_cropTop, 0, 100000);
                CropRight = Parse(_cropRight, 0, 100000);
                CropBottom = Parse(_cropBottom, 0, 100000);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Edit Transform", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        };
    }

    private static Border Section(string title, string subtitle, UIElement content)
    {
        var box = new Border
        {
            Background = WindowBuilder.Bg1,
            BorderBrush = WindowBuilder.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = WindowBuilder.TextBr,
            FontWeight = FontWeights.Bold,
            FontSize = 12.5
        });
        stack.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = WindowBuilder.TextDim,
            FontSize = 10.5,
            Margin = new Thickness(0, 1, 0, 8)
        });
        stack.Children.Add(content);
        box.Child = stack;
        return box;
    }

    private static Grid Row(params (string Label, TextBox Box)[] fields)
    {
        var grid = new Grid();
        for (int i = 0; i < fields.Length; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (int i = 0; i < fields.Length; i++)
        {
            var cell = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0) };
            cell.Children.Add(new TextBlock
            {
                Text = fields[i].Label.ToUpperInvariant(),
                Foreground = WindowBuilder.TextDim,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 3)
            });
            cell.Children.Add(fields[i].Box);
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        return grid;
    }

    private static TextBox Box(string value)
    {
        var tb = WindowBuilder.Tb(value);
        tb.FontFamily = new FontFamily("Consolas");
        tb.HorizontalContentAlignment = HorizontalAlignment.Right;
        return tb;
    }

    private static double Parse(TextBox box, double min, double max)
    {
        var text = (box.Text ?? "").Trim();
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
            !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            throw new InvalidOperationException($"Invalid number: {text}");

        return Math.Max(min, Math.Min(max, value));
    }
}
