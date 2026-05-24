using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VideoEditor.Services;

namespace VideoEditor.Views;

public class DownloadUrlWindow : Window
{
    public string Url { get; private set; } = "";
    public string OutputFolder { get; private set; } = "";
    public bool UseStreamingDownloader { get; private set; } = false;

    private readonly TextBox _urlBox;
    private readonly TextBox _folderBox;
    private readonly TextBlock _detectionText;
    private readonly Ellipse _detectionDot;

    public DownloadUrlWindow(string defaultFolder)
    {
        Title = "Import from URL";
        var ch = WindowBuilder.Build(this, "🌐", "Import from URL",
            "YouTube · Vimeo · TikTok · direct .mp4 · …", 720, 520, "Download & Add to Timeline", primarySuccess: true);

        // ===== URL field =====
        ch.Body.Children.Add(WindowBuilder.Lbl("URL"));
        _urlBox = WindowBuilder.Tb();
        _urlBox.FontSize = 13;
        _urlBox.Padding = new Thickness(10, 8, 10, 8);
        _urlBox.TextChanged += (_, _) => UpdateDetection();
        ch.Body.Children.Add(_urlBox);

        // ===== Detection chip =====
        var detection = new Border
        {
            Background = WindowBuilder.Bg2,
            BorderBrush = WindowBuilder.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 8, 0, 0)
        };
        var detRow = new StackPanel { Orientation = Orientation.Horizontal };
        _detectionDot = new Ellipse { Width = 8, Height = 8, Fill = WindowBuilder.TextDim, VerticalAlignment = VerticalAlignment.Center };
        detRow.Children.Add(_detectionDot);
        _detectionText = new TextBlock
        {
            Text = "Awaiting URL…",
            Foreground = WindowBuilder.TextMute,
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        detRow.Children.Add(_detectionText);
        detection.Child = detRow;
        ch.Body.Children.Add(detection);

        // ===== Supported platforms strip =====
        var platforms = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var p in new[] { "YouTube", "Vimeo", "TikTok", "Twitter / X", "Instagram", "Facebook", "Twitch", "Direct .mp4" })
        {
            var pill = new Border
            {
                Background = WindowBuilder.Bg2,
                BorderBrush = WindowBuilder.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 6, 6)
            };
            pill.Child = new TextBlock
            {
                Text = p,
                Foreground = WindowBuilder.TextMute,
                FontSize = 10.5
            };
            platforms.Children.Add(pill);
        }
        ch.Body.Children.Add(platforms);

        // ===== Download folder =====
        ch.Body.Children.Add(WindowBuilder.Lbl("Download to"));
        var folderRow = new Grid();
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _folderBox = WindowBuilder.Tb(defaultFolder);
        _folderBox.FontSize = 12;
        Grid.SetColumn(_folderBox, 0);
        folderRow.Children.Add(_folderBox);

        var browseBtn = new Button { Content = "📁 Browse", Margin = new Thickness(8, 0, 0, 0), MinWidth = 100, Padding = new Thickness(12, 6, 12, 6) };
        browseBtn.Style = (Style)FindResource("ToolButton");
        browseBtn.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Pick any file in the destination folder",
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Select folder",
                Filter = "Folder|*.folder"
            };
            if (dlg.ShowDialog() == true)
            {
                var folder = System.IO.Path.GetDirectoryName(dlg.FileName);
                if (folder != null) _folderBox.Text = folder;
            }
        };
        Grid.SetColumn(browseBtn, 1);
        folderRow.Children.Add(browseBtn);
        ch.Body.Children.Add(folderRow);

        // ===== Tip =====
        var tip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x1C, 0x8B, 0x5C, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x8B, 0x5C, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 14, 0, 0)
        };
        tip.Child = new TextBlock
        {
            Text = "💡 For YouTube / Vimeo / TikTok etc, yt-dlp will be auto-downloaded (~12 MB, first run only). For a direct .mp4 link, a simple HTTP download is used.",
            Foreground = WindowBuilder.TextBr,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap
        };
        ch.Body.Children.Add(tip);

        // ===== Wire up CTA =====
        ch.Primary.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_urlBox.Text)) { MessageBox.Show("Please enter a URL."); return; }
            Url = _urlBox.Text.Trim();
            OutputFolder = _folderBox.Text.Trim();
            UseStreamingDownloader = !DownloadService.LooksLikeDirectFile(Url);
            DialogResult = true;
            Close();
        };

        UpdateDetection();
    }

    private void UpdateDetection()
    {
        var url = _urlBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(url))
        {
            _detectionText.Text = "Awaiting URL…";
            _detectionDot.Fill = WindowBuilder.TextDim;
            return;
        }
        if (DownloadService.LooksLikeStreamingSite(url))
        {
            _detectionText.Text = "🎬 Streaming site detected — will use yt-dlp";
            _detectionDot.Fill = new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xFF));
        }
        else if (DownloadService.LooksLikeDirectFile(url))
        {
            _detectionText.Text = "📦 Direct video file — will use HTTP download";
            _detectionDot.Fill = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        }
        else
        {
            _detectionText.Text = "🤔 Unknown format — will try yt-dlp as fallback";
            _detectionDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xD4, 0x3B));
        }
    }
}
