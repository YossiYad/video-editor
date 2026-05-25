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
        Title = webcam ? "Camera Recorder" : "Screen Recorder";
        var icon = webcam ? "🎥" : "🖥";
        var sub = webcam ? "Record directly from a camera" : "Capture a monitor — or the whole virtual desktop — using gdigrab";
        // Default sized so every section (output path / monitor / FPS / preview /
        // buttons) fits without the ScrollViewer needing to scroll. WindowBuilder
        // defaults to NoResize; for the screen recorder we override to CanResize
        // so the user can drag the dialog wider for a roomier preview.
        var ch = WindowBuilder.Build(this, icon, Title, sub, 920, webcam ? 560 : 860);
        if (!webcam)
        {
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 560;
            MinHeight = 700;
        }

        ch.Body.Children.Add(WindowBuilder.Lbl("Output file"));
        var path = WindowBuilder.Tb(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            webcam ? "webcam.mp4" : "screen.mp4"));
        ch.Body.Children.Add(path);

        ComboBox? cameraBox = null;
        if (webcam)
        {
            ch.Body.Children.Add(WindowBuilder.Lbl("Camera device"));
            var cameraRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            cameraRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cameraRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            cameraBox = new ComboBox
            {
                IsEditable = true,
                Background = WindowBuilder.Bg2,
                Foreground = WindowBuilder.TextBr,
                BorderBrush = WindowBuilder.Line,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 6, 9, 6),
                FontSize = 12.5,
                MinHeight = 32
            };
            cameraBox.Items.Add("USB Video Device");
            cameraBox.Text = "USB Video Device";
            Grid.SetColumn(cameraBox, 0);
            cameraRow.Children.Add(cameraBox);

            var refreshCamerasBtn = new Button
            {
                Content = "Refresh",
                MinWidth = 86,
                Height = 32,
                Margin = new Thickness(8, 0, 0, 0)
            };
            refreshCamerasBtn.Style = (Style)FindResource("ToolButton");
            Grid.SetColumn(refreshCamerasBtn, 1);
            cameraRow.Children.Add(refreshCamerasBtn);
            ch.Body.Children.Add(cameraRow);

            var cameraHint = new TextBlock
            {
                Text = "Pick a camera, or type the exact DirectShow device name if it is not listed.",
                FontSize = 10.5,
                Foreground = WindowBuilder.TextDim,
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };
            ch.Body.Children.Add(cameraHint);

            async void RefreshCameras()
            {
                refreshCamerasBtn.IsEnabled = false;
                refreshCamerasBtn.Content = "Scanning...";
                try
                {
                    var current = string.IsNullOrWhiteSpace(cameraBox.Text) ? "USB Video Device" : cameraBox.Text;
                    var devices = await DetectDshowVideoDevicesAsync();
                    cameraBox.Items.Clear();
                    if (devices.Count == 0)
                    {
                        cameraBox.Items.Add(current);
                        cameraBox.Text = current;
                        cameraHint.Text = "No cameras were detected automatically. Type the DirectShow device name manually.";
                    }
                    else
                    {
                        foreach (var d in devices) cameraBox.Items.Add(d);
                        cameraBox.Text = devices.Contains(current) ? current : devices[0];
                        cameraHint.Text = $"Found {devices.Count} camera device(s). Pick one before recording.";
                    }
                }
                catch
                {
                    cameraHint.Text = "Could not scan cameras. You can still type the DirectShow device name manually.";
                }
                finally
                {
                    refreshCamerasBtn.Content = "Refresh";
                    refreshCamerasBtn.IsEnabled = true;
                }
            }
            refreshCamerasBtn.Click += (_, _) => RefreshCameras();
            Loaded += (_, _) => RefreshCameras();
        }

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
            // Preview header row: "LIVE PREVIEW" label + "🔍 Enlarge" button on the right.
            var previewHeader = new Grid { Margin = new Thickness(0, 12, 0, 5) };
            previewHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            previewHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var previewLbl = new TextBlock
            {
                Text = "LIVE PREVIEW",
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = WindowBuilder.TextDim,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(previewLbl, 0);
            previewHeader.Children.Add(previewLbl);
            var enlargeBtn = new Button
            {
                Content = "🔍 Enlarge",
                MinWidth = 96,
                Height = 26,
                FontSize = 11,
                Padding = new Thickness(8, 2, 8, 2)
            };
            enlargeBtn.Style = (Style)FindResource("ToolButton");
            Grid.SetColumn(enlargeBtn, 1);
            previewHeader.Children.Add(enlargeBtn);
            ch.Body.Children.Add(previewHeader);

            previewFrame = new Border
            {
                Background = System.Windows.Media.Brushes.Black,
                BorderBrush = WindowBuilder.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Height = 360,
                MinHeight = 180,
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
                Text = "Preview updates 6×/sec. Click 🔍 Enlarge for a full-window preview, or drag the dialog wider. The recording itself captures at the FPS above.",
                FontSize = 10.5,
                Foreground = WindowBuilder.TextDim,
                Margin = new Thickness(0, 0, 0, 4),
                TextWrapping = TextWrapping.Wrap
            });

            // Diagnostic line — shows exactly which rectangle of the screen we're
            // capturing. If the user reports "I don't see the whole monitor", we can
            // compare these numbers to what Windows Display Settings actually reports.
            var diagText = new TextBlock
            {
                Text = "",
                FontSize = 10,
                FontFamily = new FontFamily("Consolas"),
                Foreground = WindowBuilder.TextMute,
                Margin = new Thickness(0, 0, 0, 4),
                TextWrapping = TextWrapping.Wrap
            };
            ch.Body.Children.Add(diagText);
            Action refreshDiag = () =>
            {
                int idx = monitorBox?.SelectedIndex ?? 0;
                if (idx > 0 && idx - 1 < monitors.Count)
                {
                    var m = monitors[idx - 1];
                    diagText.Text = m.HasDpiScaling
                        ? $"Recording at full physical {m.PhysicalWidth}×{m.PhysicalHeight} via ddagrab (DXGI Desktop Duplication) · preview is the DPI-scaled view"
                        : $"Recording at {m.Width}×{m.Height} via ddagrab (output_idx={m.Index})";
                }
                else
                {
                    diagText.Text = $"Recording entire virtual desktop via gdigrab · " +
                        $"x={(int)SystemParameters.VirtualScreenLeft}, " +
                        $"y={(int)SystemParameters.VirtualScreenTop}, " +
                        $"w={(int)SystemParameters.VirtualScreenWidth}, " +
                        $"h={(int)SystemParameters.VirtualScreenHeight}";
                }
            };
            refreshDiag();
            if (monitorBox != null) monitorBox.SelectionChanged += (_, _) => refreshDiag();

            // Start the preview timer immediately (before recording too — so the
            // user can confirm the picker selected the right monitor).
            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
            _previewTimer.Tick += (_, _) => CaptureMonitorPreview(monitorBox, monitors);
            _previewTimer.Start();
            // Refresh once whenever the user changes the chosen monitor.
            if (monitorBox != null)
                monitorBox.SelectionChanged += (_, _) => CaptureMonitorPreview(monitorBox, monitors);

            // Pop the live preview out into a separate full-window viewer that the
            // user can maximise / resize freely. The same _previewImage.Source is
            // pushed by the same DispatcherTimer, so both Images mirror in real time.
            enlargeBtn.Click += (_, _) =>
            {
                var bigWin = new Window
                {
                    Title = "Live Preview - " + (monitorBox?.SelectedItem?.ToString() ?? "Desktop"),
                    Width = 1280,
                    Height = 760,
                    MinWidth = 320,
                    MinHeight = 200,
                    Background = System.Windows.Media.Brushes.Black,
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                var bigImage = new System.Windows.Controls.Image { Stretch = Stretch.Uniform };
                bigWin.Content = bigImage;
                // Drive the second image from the same timer tick so both views are
                // always in sync. Unsubscribe when the popped-out window closes so
                // we don't leak the handler.
                EventHandler bigTick = (_, _) =>
                {
                    if (_previewImage?.Source is BitmapSource s) bigImage.Source = s;
                };
                if (_previewTimer != null) _previewTimer.Tick += bigTick;
                bigWin.Closed += (_, _) =>
                {
                    if (_previewTimer != null) _previewTimer.Tick -= bigTick;
                };
                bigWin.Show();
            };
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

                var outputPath = path.Text.Trim();
                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    MessageBox.Show("Choose an output file first.");
                    return;
                }
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(outputDir)) Directory.CreateDirectory(outputDir);
                path.Text = outputPath;

                string args;
                if (webcam)
                {
                    var cameraName = string.IsNullOrWhiteSpace(cameraBox?.Text) ? "USB Video Device" : cameraBox.Text.Trim();
                    args = $"-y -f dshow -framerate {fpsValue} -i video=\"{EscapeRecorderArg(cameraName)}\" -c:v libx264 -preset ultrafast -pix_fmt yuv420p \"{outputPath}\"";
                }
                else
                {
                    // For specific-monitor capture, use ddagrab — ffmpeg's modern Desktop
                    // Duplication API (DXGI) source. Unlike the legacy gdigrab, ddagrab is
                    // DPI-aware and captures at PHYSICAL pixel resolution, so a 1920×1080
                    // monitor displayed via 150% Windows scaling is recorded at the full
                    // 1920×1080 — not the 1280×720 logical size gdigrab would see.
                    //
                    // For "Entire desktop (all monitors)" we still use gdigrab because
                    // ddagrab is per-monitor only.
                    int monIdx = monitorBox?.SelectedIndex ?? 0;
                    if (monIdx > 0 && monIdx - 1 < monitors.Count)
                    {
                        var m = monitors[monIdx - 1];
                        AppSettings.LastScreenRecorderMonitor = m.Index;
                        AppSettings.Save();
                        // ddagrab uses lavfi virtual input; output_idx is the DXGI output
                        // index which maps to monitors in attach order (matches our
                        // MonitorInfo.Index for typical multi-monitor setups). Add a
                        // pixel-format conversion afterwards so libx264 doesn't choke on
                        // ddagrab's native BGRA output.
                        args = $"-y -filter_complex \"ddagrab=output_idx={m.Index}:framerate={fpsValue},hwdownload,format=bgra,format=yuv420p[v]\" " +
                               $"-map \"[v]\" -c:v libx264 -preset ultrafast \"{outputPath}\"";
                    }
                    else
                    {
                        AppSettings.LastScreenRecorderMonitor = -1;
                        AppSettings.Save();
                        // Entire virtual desktop: stick with gdigrab (multi-monitor span).
                        args = $"-y -f gdigrab -framerate {fpsValue} -i desktop -c:v libx264 -preset ultrafast -pix_fmt yuv420p \"{outputPath}\"";
                    }
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

    private async System.Threading.Tasks.Task<List<string>> DetectDshowVideoDevicesAsync()
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ff.FFmpegExe,
            Arguments = "-hide_banner -list_devices true -f dshow -i dummy",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        using var proc = Process.Start(psi);
        if (proc == null) return new List<string>();
        var stderr = await proc.StandardError.ReadToEndAsync();
        var stdout = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return ParseDshowVideoDevices(stderr + "\n" + stdout);
    }

    private static List<string> ParseDshowVideoDevices(string ffmpegOutput)
    {
        var devices = new List<string>();
        var inVideoSection = false;
        foreach (var raw in ffmpegOutput.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Contains("DirectShow video devices", StringComparison.OrdinalIgnoreCase))
            {
                inVideoSection = true;
                continue;
            }
            if (line.Contains("DirectShow audio devices", StringComparison.OrdinalIgnoreCase))
                inVideoSection = false;
            if (!inVideoSection) continue;

            var first = line.IndexOf('"');
            var last = line.LastIndexOf('"');
            if (first >= 0 && last > first)
            {
                var name = line.Substring(first + 1, last - first - 1);
                if (!name.StartsWith("@device_", StringComparison.OrdinalIgnoreCase) && !devices.Contains(name))
                    devices.Add(name);
            }
        }
        return devices;
    }

    private static string EscapeRecorderArg(string value) => (value ?? "").Replace("\"", "\\\"");

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
                // Use the physical size when DPI scaling is enabled so the preview
                // matches the full area ddagrab records for that monitor.
                x = m.X;
                y = m.Y;
                w = m.HasDpiScaling ? m.PhysicalWidth : m.Width;
                h = m.HasDpiScaling ? m.PhysicalHeight : m.Height;
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
        if (_proc == null) return;
        if (!_proc.HasExited)
        {
            try { _proc.StandardInput.WriteLine("q"); } catch { }
            if (!_proc.WaitForExit(4000))
            {
                try { _proc.Kill(); } catch { }
                _proc.WaitForExit(2000);
            }
        }
        try { _proc.Dispose(); } catch { }
        _proc = null;
    }
}
