using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VideoEditor.Services;

namespace VideoEditor.Views;

public class ScreenRecorderWindow : Window
{
    private readonly FFmpegService _ff;
    private readonly bool _webcam;
    private Process? _proc;
    private DispatcherTimer? _previewTimer;
    private System.Windows.Controls.Image? _previewImage;

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
        // Wider + taller window so the live preview has room to breathe. WindowBuilder
        // defaults to NoResize; we override below to CanResize so the user can drag
        // the dialog larger and the preview grows with it.
        var ch = WindowBuilder.Build(this, icon, Title, sub, 880, webcam ? 460 : 760);
        if (!webcam)
        {
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 560;
            MinHeight = 540;
        }

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

        // Live preview surface — shows the chosen monitor (or whole desktop) every
        // ~150 ms via GDI screen capture. Independent of ffmpeg's own capture so it
        // can't affect the recording. Hidden for webcam mode.
        Border? previewFrame = null;
        if (!webcam)
        {
            ch.Body.Children.Add(WindowBuilder.Lbl("Live preview"));
            previewFrame = new Border
            {
                Background = System.Windows.Media.Brushes.Black,
                BorderBrush = WindowBuilder.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Height = 380,
                MinHeight = 200,
                Margin = new Thickness(0, 0, 0, 4),
                ClipToBounds = true
            };
            _previewImage = new System.Windows.Controls.Image
            {
                Stretch = Stretch.Uniform,
                Source = null
            };
            previewFrame.Child = _previewImage;
            ch.Body.Children.Add(previewFrame);
            ch.Body.Children.Add(new TextBlock
            {
                Text = "Preview updates 6×/sec. Drag the window corner to make it bigger. The recording itself captures at the FPS above.",
                FontSize = 10.5,
                Foreground = WindowBuilder.TextDim,
                Margin = new Thickness(0, 0, 0, 4),
                TextWrapping = TextWrapping.Wrap
            });

            // Grow the preview surface vertically with the window so dragging the
            // bottom-right corner makes the live preview bigger (and shrinking the
            // window makes it smaller, down to the MinHeight floor).
            SizeChanged += (_, _) =>
            {
                if (previewFrame == null) return;
                // ~360 px of fixed chrome above the preview (titlebar + output path
                // + monitor combo + FPS + caption + status row + buttons + footer).
                // Anything left over goes to the preview itself.
                double available = ActualHeight - 360;
                if (available > previewFrame.MinHeight)
                    previewFrame.Height = available;
            };
            // Start the preview timer immediately (before recording too — so the
            // user can confirm the picker selected the right monitor).
            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
            _previewTimer.Tick += (_, _) => CaptureMonitorPreview(monitorBox, monitors);
            _previewTimer.Start();
            // Refresh once whenever the user changes the chosen monitor.
            if (monitorBox != null)
                monitorBox.SelectionChanged += (_, _) => CaptureMonitorPreview(monitorBox, monitors);
        }

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

        Closed += (_, _) =>
        {
            try { _previewTimer?.Stop(); } catch { }
            try { StopRecording(); } catch { }
        };
    }

    /// <summary>
    /// Capture the currently-selected monitor (or the whole virtual desktop) into
    /// a low-res BitmapSource and assign it to the preview Image. Runs ~6×/sec —
    /// fine for "is the camera pointed at the right thing" feedback, not a real
    /// playback.
    /// </summary>
    private void CaptureMonitorPreview(ComboBox? monitorBox, List<MonitorInfo.Display> monitors)
    {
        if (_previewImage == null) return;
        try
        {
            int x, y, w, h;
            int idx = monitorBox?.SelectedIndex ?? 0;
            if (idx > 0 && idx - 1 < monitors.Count)
            {
                var m = monitors[idx - 1];
                x = m.X; y = m.Y; w = m.Width; h = m.Height;
            }
            else
            {
                // Whole virtual desktop — use SystemParameters which already reports
                // the bounding rectangle of every attached monitor.
                x = (int)SystemParameters.VirtualScreenLeft;
                y = (int)SystemParameters.VirtualScreenTop;
                w = (int)SystemParameters.VirtualScreenWidth;
                h = (int)SystemParameters.VirtualScreenHeight;
            }
            if (w <= 0 || h <= 0) return;
            // Capture at full source resolution into a Bitmap, then convert to
            // BitmapSource. Stretch="Uniform" on the Image control handles the
            // downscale to the preview area at render time — keeps the math simple
            // and the source bitmap pixel-perfect.
            using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h),
                    System.Drawing.CopyPixelOperation.SourceCopy);
            }
            _previewImage.Source = BitmapToBitmapSource(bmp);
        }
        catch
        {
            // Screen capture can transiently fail (UAC prompt, secure desktop,
            // locked screen) — silently skip this tick.
        }
    }

    private static BitmapSource BitmapToBitmapSource(System.Drawing.Bitmap bmp)
    {
        var data = bmp.LockBits(
            new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var src = BitmapSource.Create(
                bmp.Width, bmp.Height, 96, 96,
                PixelFormats.Bgra32, null,
                data.Scan0, data.Stride * bmp.Height, data.Stride);
            src.Freeze();
            return src;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
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
