using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VideoEditor.Services;

namespace VideoEditor.Views;

public class ScreenRecorderWindow : Window
{
    private readonly FFmpegService _ff;
    private readonly bool _webcam;
    private Process? _proc;

    /// <summary>Path of the last successful recording. Set by Stop, read by the
    /// parent MainWindow when "Open in editor" was clicked. Empty when the user
    /// closed without recording or chose only "Save".</summary>
    public string? OpenInEditorPath { get; private set; }

    public ScreenRecorderWindow(FFmpegService ff, bool webcam = false)
    {
        _ff = ff;
        _webcam = webcam;
        Title = webcam ? "Video Recorder (Webcam)" : "Screen Recorder";
        var icon = webcam ? "🎥" : "🖥";
        var sub = webcam ? "Capture webcam via dshow" : "Capture a monitor — or the whole virtual desktop — using gdigrab";
        var ch = WindowBuilder.Build(this, icon, Title, sub, 560, 440);

        ch.Body.Children.Add(WindowBuilder.Lbl("Output file"));
        var path = WindowBuilder.Tb(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            webcam ? "webcam.mp4" : "screen.mp4"));
        ch.Body.Children.Add(path);

        // Monitor picker — only shown for screen recording, not webcam.
        // Enumerates every attached display via Win32 EnumDisplayMonitors and offers
        // an "Entire desktop" option that captures the full virtual screen (current default).
        ComboBox? monitorBox = null;
        List<MonitorInfo.Display> monitors = new();
        if (!webcam)
        {
            monitors = MonitorInfo.EnumerateAll();
            ch.Body.Children.Add(WindowBuilder.Lbl("Recording source"));
            monitorBox = new ComboBox
            {
                Background = WindowBuilder.Bg2,
                Foreground = WindowBuilder.TextBr,
                BorderBrush = WindowBuilder.Line,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 6, 9, 6),
                FontSize = 12.5
            };
            monitorBox.Items.Add("Entire desktop (all monitors)");
            foreach (var m in monitors) monitorBox.Items.Add(m.FriendlyName);
            // Restore last choice if still valid; otherwise default to "Entire desktop" (0).
            int saved = AppSettings.LastScreenRecorderMonitor;
            monitorBox.SelectedIndex = (saved >= 0 && saved < monitors.Count) ? saved + 1 : 0;
            ch.Body.Children.Add(monitorBox);
        }

        ch.Body.Children.Add(WindowBuilder.Lbl("Frame rate (FPS)"));
        var fps = WindowBuilder.Tb("30");
        fps.HorizontalAlignment = HorizontalAlignment.Left;
        fps.Width = 100;
        ch.Body.Children.Add(fps);

        // Recording status indicator
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var statusDot = new System.Windows.Shapes.Ellipse
        {
            Width = 9, Height = 9, Margin = new Thickness(0, 0, 8, 0),
            Fill = WindowBuilder.TextDim,
            VerticalAlignment = VerticalAlignment.Center
        };
        var statusText = new TextBlock
        {
            Text = "Idle",
            FontSize = 11.5,
            Foreground = WindowBuilder.TextMute,
            VerticalAlignment = VerticalAlignment.Center
        };
        statusRow.Children.Add(statusDot);
        statusRow.Children.Add(statusText);
        ch.Body.Children.Add(statusRow);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var startBtn = new Button { Content = "● Start Recording", MinWidth = 168, Height = 32, Margin = new Thickness(0, 0, 6, 0) };
        startBtn.Style = (Style)FindResource("PrimaryButton");
        var stopBtn = new Button { Content = "■ Stop", MinWidth = 100, Height = 32, IsEnabled = false };
        stopBtn.Style = (Style)FindResource("ToolButton");
        btnRow.Children.Add(startBtn); btnRow.Children.Add(stopBtn);
        ch.Body.Children.Add(btnRow);

        startBtn.Click += (_, _) =>
        {
            try
            {
                if (!int.TryParse(fps.Text, out var fpsValue) || fpsValue < 1 || fpsValue > 120)
                {
                    MessageBox.Show("FPS must be between 1 and 120.");
                    return;
                }
                string args;
                if (webcam)
                {
                    args = $"-y -f dshow -framerate {fpsValue} -i video=\"USB Video Device\" \"{path.Text}\"";
                }
                else
                {
                    // gdigrab captures the virtual screen. When the user picks a specific
                    // monitor we add -offset_x / -offset_y / -video_size to crop to that
                    // monitor's rectangle inside the virtual screen (offsets can be negative
                    // for monitors to the left of the primary).
                    string sourceArgs;
                    int monIdx = monitorBox?.SelectedIndex ?? 0;
                    if (monIdx > 0 && monIdx - 1 < monitors.Count)
                    {
                        var m = monitors[monIdx - 1];
                        AppSettings.LastScreenRecorderMonitor = m.Index;
                        sourceArgs = $"-offset_x {m.X} -offset_y {m.Y} -video_size {m.Width}x{m.Height} -i desktop";
                    }
                    else
                    {
                        AppSettings.LastScreenRecorderMonitor = -1;
                        sourceArgs = "-i desktop";
                    }
                    AppSettings.Save();
                    args = $"-y -f gdigrab -framerate {fpsValue} {sourceArgs} -c:v libx264 -preset ultrafast -pix_fmt yuv420p \"{path.Text}\"";
                }
                _proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _ff.FFmpegExe,
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardInput = true,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true
                    },
                    EnableRaisingEvents = true
                };
                _proc.Start();
                _proc.BeginErrorReadLine();
                _proc.BeginOutputReadLine();
                _proc.ErrorDataReceived += (_, _) => { };
                _proc.OutputDataReceived += (_, _) => { };
                startBtn.IsEnabled = false; stopBtn.IsEnabled = true;
                statusDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
                statusText.Text = "Recording…";
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        };
        stopBtn.Click += (_, _) =>
        {
            try
            {
                StopRecording();
                statusDot.Fill = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                statusText.Text = "Saved: " + Path.GetFileName(path.Text);
                startBtn.IsEnabled = true; stopBtn.IsEnabled = false;
                if (!File.Exists(path.Text))
                {
                    MessageBox.Show("Recording finished but the file wasn't created: " + path.Text);
                    return;
                }
                // Show the shared post-creation dialog. If the user picks "Open in editor",
                // ShareDialog returns true and OpenInEditor is set — propagate that to our
                // own OpenInEditorPath so the parent MainWindow can pick the clip up.
                var share = new ShareDialog(path.Text,
                    title: webcam ? "Recording saved" : "Screen recording saved",
                    subtitle: "Edit it in the timeline, share it, or just keep it on disk.")
                { Owner = this };
                share.ShowDialog();
                if (share.OpenInEditor)
                {
                    OpenInEditorPath = path.Text;
                    DialogResult = true;
                    Close();
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        };

        // Primary CTA closes the window (recording is started/stopped via the buttons above)
        ch.Primary.Content = "Done";
        ch.Primary.Click += (_, _) => { DialogResult = true; Close(); };

        Closed += (_, _) => { try { StopRecording(); } catch { } };
    }

    private void StopRecording()
    {
        if (_proc == null || _proc.HasExited) return;
        try { _proc.StandardInput.WriteLine("q"); } catch { }
        if (!_proc.WaitForExit(4000))
        {
            try { _proc.Kill(); } catch { }
            _proc.WaitForExit(2000);
        }
        try { _proc.Dispose(); } catch { }
        _proc = null;
    }
}
