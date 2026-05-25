using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VideoEditor.Models;

namespace VideoEditor.Views;

public class SubtitleEditorWindow : Window
{
    public List<SubtitleSegment> Segments { get; private set; }
    public bool Discarded { get; private set; }

    private readonly StackPanel _rows;
    private readonly List<RowControls> _rowControls = new();

    private class RowControls
    {
        public required SubtitleSegment Segment;
        public required TextBox StartBox;
        public required TextBox EndBox;
        public required TextBox TextBox;
        public required Border Container;
    }

    public SubtitleEditorWindow(IEnumerable<SubtitleSegment> input)
    {
        Segments = input.Select(s => new SubtitleSegment
        {
            StartSeconds = s.StartSeconds,
            EndSeconds = s.EndSeconds,
            Text = s.Text
        }).OrderBy(s => s.StartSeconds).ToList();

        var ch = WindowBuilder.Build(this, "📝",
            "Edit subtitles",
            "Subtitles will burn into the export",
            880, 660, "Apply to project", primarySuccess: true);

        _rows = new StackPanel();
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _rows,
            MaxHeight = 460,
            Margin = new Thickness(0, 4, 0, 0)
        };
        ch.Body.Children.Add(scroll);

        foreach (var s in Segments) AddRow(s);

        var discardBtn = new Button
        {
            Content = "Discard transcription",
            Height = 32,
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0x6B, 0x6B)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x6B, 0x6B)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold
        };
        discardBtn.Click += (_, _) =>
        {
            Discarded = true;
            Segments.Clear();
            DialogResult = true;
            Close();
        };
        ch.Footer.Children.Add(discardBtn);

        ch.Primary.Click += (_, _) =>
        {
            CommitRows();
            DialogResult = true;
            Close();
        };

        VideoEditor.Services.Localization.TranslateTree(this);
        FlowDirection = VideoEditor.Services.Localization.IsHebrew
            ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    private void AddRow(SubtitleSegment s)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x16, 0x1A, 0x25)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 6)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var startBox = MakeBox(FormatTime(s.StartSeconds));
        var endBox   = MakeBox(FormatTime(s.EndSeconds));
        var textBox  = MakeBox(s.Text);
        textBox.AcceptsReturn = false;
        textBox.TextWrapping = TextWrapping.Wrap;
        textBox.MinHeight = 30;
        textBox.HorizontalContentAlignment = HorizontalAlignment.Stretch;

        Grid.SetColumn(startBox, 0); grid.Children.Add(startBox);
        Grid.SetColumn(endBox, 1); grid.Children.Add(endBox);
        endBox.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(textBox, 2); grid.Children.Add(textBox);
        textBox.Margin = new Thickness(6, 0, 0, 0);

        var removeBtn = new Button
        {
            Content = "✕",
            Width = 30, Height = 30,
            Padding = new Thickness(0),
            Margin = new Thickness(6, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)),
            FontSize = 14,
            Cursor = Cursors.Hand,
            ToolTip = "Remove segment"
        };
        Grid.SetColumn(removeBtn, 3); grid.Children.Add(removeBtn);

        border.Child = grid;
        _rows.Children.Add(border);

        var rc = new RowControls
        {
            Segment = s,
            StartBox = startBox,
            EndBox = endBox,
            TextBox = textBox,
            Container = border
        };
        _rowControls.Add(rc);

        removeBtn.Click += (_, _) =>
        {
            _rows.Children.Remove(border);
            _rowControls.Remove(rc);
            Segments.Remove(s);
        };
    }

    private void CommitRows()
    {
        var updated = new List<SubtitleSegment>();
        foreach (var rc in _rowControls)
        {
            var s = rc.Segment;
            if (TryParseTime(rc.StartBox.Text, out var start)) s.StartSeconds = start;
            if (TryParseTime(rc.EndBox.Text, out var end))     s.EndSeconds   = end;
            s.Text = rc.TextBox.Text?.Trim() ?? "";
            if (s.EndSeconds < s.StartSeconds) s.EndSeconds = s.StartSeconds;
            if (!string.IsNullOrEmpty(s.Text)) updated.Add(s);
        }
        Segments = updated.OrderBy(x => x.StartSeconds).ToList();
    }

    private static TextBox MakeBox(string text)
    {
        return new TextBox
        {
            Text = text,
            Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x11, 0x19)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xF2)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(7, 6, 7, 6),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            CaretBrush = Brushes.White,
            VerticalContentAlignment = VerticalAlignment.Center
        };
    }

    private static string FormatTime(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}.{2:D3}",
            (int)ts.TotalMinutes, ts.Seconds, ts.Milliseconds);
    }

    private static bool TryParseTime(string s, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim().Replace(',', '.');
        // Accept "mm:ss.fff" or "hh:mm:ss.fff" or raw seconds.
        var parts = s.Split(':');
        try
        {
            if (parts.Length == 1)
            {
                return double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
            }
            if (parts.Length == 2)
            {
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m)) return false;
                if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var sec)) return false;
                seconds = m * 60 + sec;
                return true;
            }
            if (parts.Length == 3)
            {
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)) return false;
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m)) return false;
                if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var sec)) return false;
                seconds = h * 3600 + m * 60 + sec;
                return true;
            }
        }
        catch { }
        return false;
    }
}
