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
    private readonly Border _detectionBox;
    private readonly Ellipse _detectionDot;

    public DownloadUrlWindow(string defaultFolder)
    {
        Title = "Download Video from URL";
        Width = 640; Height = 440;
        MinWidth = 480;
        Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x1F));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Header
        var headerPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal };
        headerRow.Children.Add(new TextBlock
        {
            Text = "🌐",
            FontSize = 26,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center
        });
        headerRow.Children.Add(new TextBlock
        {
            Text = "Download Video from URL",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        });
        headerPanel.Children.Add(headerRow);
        headerPanel.Children.Add(new TextBlock
        {
            Text = "Paste a video URL — YouTube, Vimeo, Twitter, TikTok, or a direct .mp4 link. The video will be downloaded and added to your timeline.",
            Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB0)),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        });
        Grid.SetRow(headerPanel, 0);
        grid.Children.Add(headerPanel);

        // Body
        var body = new StackPanel();
        body.Children.Add(MakeLabel("URL"));
        _urlBox = MakeTextBox();
        _urlBox.FontSize = 13;
        _urlBox.Height = 32;
        _urlBox.TextChanged += (_, _) => UpdateDetection();
        body.Children.Add(_urlBox);

        // Detection chip
        _detectionBox = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x2A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x28, 0x34)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 8, 0, 0)
        };
        var detRow = new StackPanel { Orientation = Orientation.Horizontal };
        _detectionDot = new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(Color.FromRgb(0x5A, 0x61, 0x78)), VerticalAlignment = VerticalAlignment.Center };
        detRow.Children.Add(_detectionDot);
        _detectionText = new TextBlock
        {
            Text = "Awaiting URL…",
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x91, 0xA8)),
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        detRow.Children.Add(_detectionText);
        _detectionBox.Child = detRow;
        body.Children.Add(_detectionBox);

        body.Children.Add(MakeLabel("Download to"));
        var folderRow = new Grid();
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _folderBox = MakeTextBox(defaultFolder);
        _folderBox.FontSize = 12;
        Grid.SetColumn(_folderBox, 0);
        folderRow.Children.Add(_folderBox);
        var browseBtn = MakeButton("📁 Browse", false);
        browseBtn.Margin = new Thickness(8, 0, 0, 0);
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
        body.Children.Add(folderRow);

        // Helpful tip
        var tip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x18, 0x8B, 0x5C, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x8B, 0x5C, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 14, 0, 0)
        };
        tip.Child = new TextBlock
        {
            Text = "💡 For YouTube / Vimeo / TikTok etc, yt-dlp will be auto-downloaded (~12 MB, first run only). For a direct .mp4 link, a simple HTTP download is used.",
            Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xD0)),
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap
        };
        body.Children.Add(tip);

        var bodyScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
        Grid.SetRow(bodyScroll, 1);
        grid.Children.Add(bodyScroll);

        // Footer
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var cancelBtn = MakeButton("Cancel", false);
        cancelBtn.Margin = new Thickness(4);
        cancelBtn.Click += (_, _) => { DialogResult = false; Close(); };
        var downloadBtn = MakeButton("📥 Download & Add to Timeline", true);
        downloadBtn.Margin = new Thickness(4);
        downloadBtn.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_urlBox.Text)) { MessageBox.Show("Please enter a URL."); return; }
            Url = _urlBox.Text.Trim();
            OutputFolder = _folderBox.Text.Trim();
            UseStreamingDownloader = !DownloadService.LooksLikeDirectFile(Url);
            DialogResult = true;
            Close();
        };
        footer.Children.Add(cancelBtn);
        footer.Children.Add(downloadBtn);
        Grid.SetRow(footer, 2);
        grid.Children.Add(footer);

        Content = grid;
        UpdateDetection();
    }

    private void UpdateDetection()
    {
        var url = _urlBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(url))
        {
            _detectionText.Text = "Awaiting URL…";
            _detectionDot.Fill = new SolidColorBrush(Color.FromRgb(0x5A, 0x61, 0x78));
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

    private TextBlock MakeLabel(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x61, 0x78)),
        FontSize = 10.5,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 12, 0, 5)
    };

    private TextBox MakeTextBox(string text = "")
    {
        return new TextBox
        {
            Text = text,
            Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x11, 0x19)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x28, 0x34)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 7, 9, 7),
            FontFamily = new FontFamily("Consolas")
        };
    }

    private Button MakeButton(string text, bool primary)
    {
        var b = new Button
        {
            Content = text,
            Padding = new Thickness(14, 7, 14, 7),
            Foreground = Brushes.White,
            BorderBrush = primary ? new SolidColorBrush(Color.FromRgb(0x6E, 0x44, 0xD6)) : new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x52)),
            Background = primary
                ? new LinearGradientBrush(Color.FromRgb(0xA0, 0x7C, 0xFF), Color.FromRgb(0x8B, 0x5C, 0xFF), 90)
                : (Brush)new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x3E)),
            FontWeight = primary ? FontWeights.Bold : FontWeights.SemiBold,
            FontSize = 12.5,
            Cursor = Cursors.Hand,
            Height = 34,
            BorderThickness = new Thickness(1)
        };
        return b;
    }
}
