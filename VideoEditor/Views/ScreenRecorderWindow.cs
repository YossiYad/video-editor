using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
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
        Width = 480; Height = 320;
        Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x1F));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var (g, body, footer) = WindowBuilder.Layout();
        body.Children.Add(WindowBuilder.Lbl(webcam ? "Captures webcam via dshow." : "Captures the primary screen using gdigrab."));
        body.Children.Add(WindowBuilder.Lbl("Output file"));
        var path = new TextBox { Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), webcam ? "webcam.mp4" : "screen.mp4") };
        body.Children.Add(path);

        var fps = new TextBox { Text = "30", Width = 80 };
        body.Children.Add(WindowBuilder.Lbl("FPS"));
        body.Children.Add(fps);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var startBtn = new Button { Content = "● Start Recording", Width = 160, Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(4) };
        var stopBtn = new Button { Content = "■ Stop", Width = 100, Style = (Style)FindResource("ToolButton"), Margin = new Thickness(4), IsEnabled = false };
        btnRow.Children.Add(startBtn); btnRow.Children.Add(stopBtn);
        body.Children.Add(btnRow);

        startBtn.Click += (_, _) =>
        {
            try
            {
                var args = webcam
                    ? $"-y -f dshow -framerate {fps.Text} -i video=\"USB Video Device\" \"{path.Text}\""
                    : $"-y -f gdigrab -framerate {fps.Text} -i desktop -c:v libx264 -preset ultrafast -pix_fmt yuv420p \"{path.Text}\"";
                _proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _ff.FFmpegExe,
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardInput = true,
                        RedirectStandardError = true
                    }
                };
                _proc.Start();
                startBtn.IsEnabled = false; stopBtn.IsEnabled = true;
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        };
        stopBtn.Click += (_, _) =>
        {
            try
            {
                if (_proc != null && !_proc.HasExited)
                {
                    _proc.StandardInput.WriteLine("q");
                    _proc.WaitForExit(4000);
                    if (!_proc.HasExited) _proc.Kill();
                }
                MessageBox.Show("Saved: " + path.Text);
                startBtn.IsEnabled = true; stopBtn.IsEnabled = false;
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        };

        Content = g;
        WindowBuilder.OkCancel(this, footer);
    }
}
