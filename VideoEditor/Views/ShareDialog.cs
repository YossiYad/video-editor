using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VideoEditor.Services;

namespace VideoEditor.Views;

/// <summary>
/// Post-creation share modal. Shows the freshly-saved video's path plus buttons
/// to open it in the editor, reveal the file in Explorer, copy its path, or
/// open the upload page of YouTube / TikTok / X / Instagram in the default browser.
/// Used by both the Screen Recorder (after Stop) and the main Export pipeline.
/// </summary>
public class ShareDialog : Window
{
    /// <summary>If true, the caller should load <see cref="FilePath"/> into the editor's timeline.</summary>
    public bool OpenInEditor { get; private set; }

    /// <summary>The file path the dialog was created with.</summary>
    public string FilePath { get; }

    public ShareDialog(string filePath, string? title = null, string? subtitle = null, bool offerOpenInEditor = true)
    {
        FilePath = filePath;
        var ch = WindowBuilder.Build(this, "*",
            title ?? "Saved",
            subtitle ?? "Choose what to do next with your video.",
            560, offerOpenInEditor ? 400 : 360);

        var fileCard = new Border
        {
            Background = WindowBuilder.Bg2,
            BorderBrush = WindowBuilder.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 14)
        };
        var fileStack = new StackPanel();
        fileStack.Children.Add(new TextBlock
        {
            Text = Path.GetFileName(filePath),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = WindowBuilder.TextBr,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        fileStack.Children.Add(new TextBlock
        {
            Text = Path.GetDirectoryName(filePath) ?? "",
            FontSize = 11,
            FontFamily = new FontFamily("Consolas"),
            Foreground = WindowBuilder.TextDim,
            Margin = new Thickness(0, 3, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (File.Exists(filePath))
        {
            var size = new FileInfo(filePath).Length;
            fileStack.Children.Add(new TextBlock
            {
                Text = FormatBytes(size),
                FontSize = 11,
                Foreground = WindowBuilder.TextMute,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }
        fileCard.Child = fileStack;
        ch.Body.Children.Add(fileCard);

        ch.Body.Children.Add(BuildFileActionRow(filePath, offerOpenInEditor));

        ch.Body.Children.Add(new TextBlock
        {
            Text = "Upload without app OAuth - opens the platform upload page in your browser:",
            FontSize = 11,
            Foreground = WindowBuilder.TextDim,
            Margin = new Thickness(0, 16, 0, 6),
            TextWrapping = TextWrapping.Wrap
        });

        var row2 = new StackPanel { Orientation = Orientation.Horizontal };
        var ytBtn = MakeShareButton("YouTube", Color.FromRgb(0xFF, 0x00, 0x00));
        var tkBtn = MakeShareButton("TikTok", Color.FromRgb(0x00, 0xF2, 0xEA));
        var xBtn = MakeShareButton("X (Twitter)", Color.FromRgb(0x1D, 0xA1, 0xF2));
        var igBtn = MakeShareButton("Instagram", Color.FromRgb(0xE1, 0x30, 0x6C));
        ytBtn.Click += (_, _) => OpenUploadPage("https://studio.youtube.com/channel/upload", filePath);
        tkBtn.Click += (_, _) => OpenUploadPage("https://www.tiktok.com/upload", filePath);
        xBtn.Click += (_, _) => OpenUrl("https://twitter.com/compose/post");
        igBtn.Click += (_, _) => OpenUploadPage("https://www.instagram.com/create/select/", filePath);
        row2.Children.Add(ytBtn);
        row2.Children.Add(tkBtn);
        row2.Children.Add(xBtn);
        row2.Children.Add(igBtn);
        ch.Body.Children.Add(row2);

        ch.Body.Children.Add(new TextBlock
        {
            Text = "Tip: TikTok and Instagram do not allow direct API posting without account authorization. This app does not store tokens; use the browser page and drag the file from Explorer.",
            FontSize = 10.5,
            Foreground = WindowBuilder.TextDim,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

        ch.Primary.Content = "Close";
        ch.Primary.Click += (_, _) => { DialogResult = false; Close(); };
    }

    private UIElement BuildFileActionRow(string filePath, bool offerOpenInEditor)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };

        if (offerOpenInEditor)
        {
            var editorBtn = new Button { Content = "Open in editor", MinWidth = 154, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
            editorBtn.Style = (Style)FindResource("PrimaryButton");
            editorBtn.Click += (_, _) => { OpenInEditor = true; DialogResult = true; Close(); };
            row.Children.Add(editorBtn);
        }

        var folderBtn = new Button
        {
            Content = "Open folder",
            MinWidth = offerOpenInEditor ? 118 : 154,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0)
        };
        folderBtn.Style = (Style)FindResource(offerOpenInEditor ? "ToolButton" : "PrimaryButton");
        folderBtn.Click += (_, _) => RevealInExplorer(filePath);
        row.Children.Add(folderBtn);

        var copyBtn = new Button { Content = "Copy path", MinWidth = 96, Height = 32 };
        copyBtn.Style = (Style)FindResource("ToolButton");
        copyBtn.Click += (_, _) => CopyPath(filePath);
        row.Children.Add(copyBtn);

        return row;
    }

    private Button MakeShareButton(string label, Color brandColor)
    {
        var b = new Button
        {
            Content = label,
            MinWidth = 100,
            Height = 30,
            Margin = new Thickness(0, 0, 5, 0)
        };
        b.Style = (Style)FindResource("ToolButton");
        b.BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, brandColor.R, brandColor.G, brandColor.B));
        return b;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Couldn't open the browser: " + ex.Message);
        }
    }

    private static void OpenUploadPage(string url, string filePath)
    {
        CopyPath(filePath, showConfirmation: false);
        RevealInExplorer(filePath);
        OpenUrl(url);
    }

    private static void CopyPath(string path, bool showConfirmation = true)
    {
        try
        {
            Clipboard.SetText(path);
            if (showConfirmation)
            {
                MessageBox.Show("Copied the video file path.", "Copy path",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Couldn't copy the path: " + ex.Message);
        }
    }

    private static void RevealInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            else
                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.GetDirectoryName(path) ?? "",
                    UseShellExecute = true
                });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message);
        }
    }

    private static string FormatBytes(long bytes)
    {
        const double KB = 1024, MB = KB * 1024, GB = MB * 1024;
        if (bytes >= GB) return $"{bytes / GB:0.##} GB";
        if (bytes >= MB) return $"{bytes / MB:0.##} MB";
        if (bytes >= KB) return $"{bytes / KB:0.##} KB";
        return $"{bytes} B";
    }
}
