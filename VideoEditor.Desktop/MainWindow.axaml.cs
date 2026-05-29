using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using VideoEditor.Services;

namespace VideoEditor.Desktop;

public partial class MainWindow : Window
{
    // The shared, platform-neutral export/probe engine from VideoEditor.Core. The ffmpeg
    // binaries live in an "ffmpeg" folder next to the app data dir; they auto-download on
    // first use just like the Windows app (per-OS asset names via Platform.ExeName).
    private readonly FFmpegService _ff;

    private static readonly string[] VideoExtensions =
        { ".mp4", ".mov", ".mkv", ".avi", ".webm", ".wmv", ".flv", ".m4v", ".ts", ".mpg", ".mpeg" };

    public MainWindow()
    {
        InitializeComponent();
        var ffmpegDir = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
        Directory.CreateDirectory(ffmpegDir);
        _ff = new FFmpegService(ffmpegDir);
        platformText.Text = $"{Platform.RuntimeId}  ·  ffmpeg: {Platform.ExeName("ffmpeg")}";
    }

    private async void OpenFiles_Click(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open video files",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Video files")
                {
                    Patterns = VideoExtensions.Select(x => "*" + x).ToList()
                },
                FilePickerFileTypes.All
            }
        });

        if (files is null || files.Count == 0) return;

        int added = 0;
        foreach (var f in files)
        {
            var path = f.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) continue;
            clipList.Items.Add(Path.GetFileName(path));
            added++;
        }
        previewHint.IsVisible = clipList.Items.Count == 0;
        statusText.Text = added > 0
            ? $"Added {added} file(s). (Stage C will wire these onto the timeline.)"
            : "No files added.";
    }
}
