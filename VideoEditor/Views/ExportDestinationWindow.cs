using System.Windows;
using System.Windows.Controls;
using VideoEditor.Services;

namespace VideoEditor.Views;

public enum ExportDestination
{
    SaveToFiles,
    Publish
}

public class ExportDestinationWindow : Window
{
    public ExportDestination Destination { get; private set; } = ExportDestination.SaveToFiles;

    public ExportDestinationWindow()
    {
        Title = "Export";
        var ch = WindowBuilder.Build(this, "↗", "Export", "Choose what you want to do with the finished video.", 560, 360);
        ch.Primary.Visibility = Visibility.Collapsed;

        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var saveBtn = MakeChoiceButton(
            "Save to files",
            "Choose a folder and filename for the exported video.");
        saveBtn.Click += (_, _) =>
        {
            Destination = ExportDestination.SaveToFiles;
            DialogResult = true;
            Close();
        };
        Grid.SetColumn(saveBtn, 0);
        grid.Children.Add(saveBtn);

        var publishBtn = MakeChoiceButton(
            "Publish / share",
            "Export first, then open upload/share options.");
        publishBtn.Click += (_, _) =>
        {
            Destination = ExportDestination.Publish;
            DialogResult = true;
            Close();
        };
        Grid.SetColumn(publishBtn, 2);
        grid.Children.Add(publishBtn);

        ch.Body.Children.Add(grid);
        ch.Body.Children.Add(new TextBlock
        {
            Text = "Publishing still creates a video file first, so you always have a local copy.",
            Foreground = WindowBuilder.TextDim,
            FontSize = 11,
            Margin = new Thickness(0, 14, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
    }

    private Button MakeChoiceButton(string title, string subtitle)
    {
        var stack = new StackPanel { Margin = new Thickness(6) };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = WindowBuilder.TextBr,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 11,
            Foreground = WindowBuilder.TextMute,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        });

        var button = new Button
        {
            Content = stack,
            Height = 128,
            Padding = new Thickness(12),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        button.Style = (Style)FindResource("ToolButton");
        return button;
    }
}
