using System;
using System.Diagnostics;
using System.IO;
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

    public ScreenRecorderWindow(FFmpegService ff, bool webcam = false)
    {
        _ff = ff;
        _webcam = webcam;
        Title = webcam ? "Video Recorder (Webcam)" : "Screen Recorder";
        var icon = webcam ? "🎥" : "🖥";
        var sub = webcam ? "Capture webcam via dshow" : "Capture the primary screen using gdigrab";
        var ch = WindowBuilder.Build(this, icon, Title, sub, 540, 380);

        ch.Body.Children.Add(WindowBuilder.Lbl("Output file"));
        var path = WindowBuilder.Tb(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            webcam ? "webcam.mp4" : "screen.mp4"));
        ch.Body.Children.Add(path);

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
                var args = webcam
                    ? $"-y -f dshow -framerate {fpsValue} -i video=\"USB Video Device\" \"{path.Text}\""
                    : $"-y -f gdigrab -framerate {fpsValue} -i desktop -c:v libx264 -preset ultrafast -pix_fmt yuv420p \"{path.Text}\"";
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
                MessageBox.Show("Saved: " + path.Text);
                startBtn.IsEnabled = true; stopBtn.IsEnabled = false;
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
