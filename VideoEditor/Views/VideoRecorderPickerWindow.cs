using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VideoEditor.Services;

namespace VideoEditor.Views;

public class VideoRecorderPickerWindow : Window
{
    private readonly FFmpegService _ff;
    private readonly ComboBox _cameraBox;
    private readonly Image _previewImage;
    private readonly TextBlock _statusText;
    private Process? _previewProc;
    private System.Threading.CancellationTokenSource? _previewCts;

    public string SelectedCameraName { get; private set; } = "USB Video Device";

    public VideoRecorderPickerWindow(FFmpegService ff, string? initialCamera = null)
    {
        _ff = ff;
        Title = "Choose Camera";
        var ch = WindowBuilder.Build(this, "🎥", "Choose Camera", "Select the camera to add to the screen recording scene.", 720, 560, "Add Camera");

        ch.Body.Children.Add(WindowBuilder.Lbl("Camera"));
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _cameraBox = new ComboBox
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
        _cameraBox.Text = string.IsNullOrWhiteSpace(initialCamera) ? SelectedCameraName : initialCamera!;
        _cameraBox.SelectionChanged += (_, _) => RestartPreview();
        Grid.SetColumn(_cameraBox, 0);
        row.Children.Add(_cameraBox);

        var refreshBtn = new Button
        {
            Content = "Refresh",
            MinWidth = 88,
            Height = 32,
            Margin = new Thickness(8, 0, 0, 0)
        };
        refreshBtn.Style = (Style)FindResource("ToolButton");
        refreshBtn.Click += async (_, _) => await LoadDevicesAsync();
        Grid.SetColumn(refreshBtn, 1);
        row.Children.Add(refreshBtn);
        ch.Body.Children.Add(row);

        ch.Body.Children.Add(WindowBuilder.Lbl("Preview"));
        var previewFrame = new Border
        {
            Height = 300,
            Background = Brushes.Black,
            BorderBrush = WindowBuilder.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true
        };
        _previewImage = new Image { Stretch = Stretch.Uniform };
        previewFrame.Child = _previewImage;
        ch.Body.Children.Add(previewFrame);

        _statusText = new TextBlock
        {
            Text = "Choose a camera to preview it.",
            FontSize = 11,
            Foreground = WindowBuilder.TextDim,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        ch.Body.Children.Add(_statusText);

        ch.Primary.Click += (_, _) =>
        {
            var name = _cameraBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Choose a camera first.", "Camera", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            SelectedCameraName = name;
            DialogResult = true;
            Close();
        };

        Loaded += async (_, _) => await LoadDevicesAsync();
        Closed += (_, _) => StopPreview();
    }

    private async System.Threading.Tasks.Task LoadDevicesAsync()
    {
        _statusText.Text = "Scanning cameras...";
        var current = string.IsNullOrWhiteSpace(_cameraBox.Text) ? SelectedCameraName : _cameraBox.Text.Trim();
        var devices = await DetectDshowVideoDevicesAsync();
        _cameraBox.Items.Clear();
        if (devices.Count == 0)
        {
            _cameraBox.Items.Add(current);
            _cameraBox.Text = current;
            _statusText.Text = "No cameras were detected automatically. You can type the exact DirectShow device name.";
        }
        else
        {
            foreach (var device in devices) _cameraBox.Items.Add(device);
            _cameraBox.Text = devices.Contains(current) ? current : devices[0];
            _statusText.Text = $"Found {devices.Count} camera device(s).";
        }
        RestartPreview();
    }

    private async System.Threading.Tasks.Task<List<string>> DetectDshowVideoDevicesAsync()
    {
        try
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
        catch
        {
            return new List<string>();
        }
    }

    private static List<string> ParseDshowVideoDevices(string ffmpegOutput)
    {
        var devices = new List<string>();
        var inVideoSection = false;
        foreach (var raw in ffmpegOutput.Split('\n'))
        {
            var line = raw.Trim();
            AddQuotedDevice(line, devices, requireVideoSuffix: true);

            if (line.Contains("DirectShow video devices", StringComparison.OrdinalIgnoreCase))
            {
                inVideoSection = true;
                continue;
            }
            if (line.Contains("DirectShow audio devices", StringComparison.OrdinalIgnoreCase))
                inVideoSection = false;
            if (inVideoSection) AddQuotedDevice(line, devices, requireVideoSuffix: false);
        }
        return devices;
    }

    private static void AddQuotedDevice(string line, List<string> devices, bool requireVideoSuffix)
    {
        if (requireVideoSuffix && !line.Contains("(video)", StringComparison.OrdinalIgnoreCase)) return;
        var first = line.IndexOf('"');
        var last = line.LastIndexOf('"');
        if (first < 0 || last <= first) return;
        var name = line.Substring(first + 1, last - first - 1);
        if (!name.StartsWith("@device_", StringComparison.OrdinalIgnoreCase) && !devices.Contains(name))
            devices.Add(name);
    }

    private void RestartPreview()
    {
        var name = _cameraBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        StopPreview();
        _statusText.Text = "Starting preview...";
        try
        {
            _previewCts = new System.Threading.CancellationTokenSource();
            _previewProc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _ff.FFmpegExe,
                    Arguments = $"-hide_banner -loglevel error -f dshow -i video=\"{EscapeRecorderArg(name)}\" -vf fps=8,hflip,scale=520:-1 -f image2pipe -vcodec mjpeg pipe:1",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };
            _previewProc.Start();
            _ = ReadMjpegPreviewAsync(_previewProc.StandardOutput.BaseStream, _previewCts.Token);
        }
        catch (Exception ex)
        {
            _statusText.Text = "Preview failed: " + ex.Message;
        }
    }

    private void StopPreview()
    {
        try { _previewCts?.Cancel(); } catch { }
        if (_previewProc != null)
        {
            try
            {
                if (!_previewProc.HasExited) _previewProc.Kill();
            }
            catch { }
            try { _previewProc.Dispose(); } catch { }
        }
        _previewProc = null;
        _previewCts = null;
    }

    private async System.Threading.Tasks.Task ReadMjpegPreviewAsync(Stream stream, System.Threading.CancellationToken token)
    {
        var buffer = new byte[8192];
        var bytes = new List<byte>(256 * 1024);
        try
        {
            while (!token.IsCancellationRequested)
            {
                int read = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                if (read <= 0) break;
                for (int i = 0; i < read; i++) bytes.Add(buffer[i]);
                while (TryExtractJpeg(bytes, out var jpg))
                {
                    var image = BitmapSourceFromJpeg(jpg);
                    Dispatcher.Invoke(() =>
                    {
                        _previewImage.Source = image;
                        _statusText.Text = "Preview is live. Click Add Camera when ready.";
                    });
                }
            }
        }
        catch
        {
            Dispatcher.Invoke(() => _statusText.Text = "Preview stopped. Try Refresh or choose another camera.");
        }
    }

    private static bool TryExtractJpeg(List<byte> bytes, out byte[] jpg)
    {
        jpg = Array.Empty<byte>();
        int start = -1;
        for (int i = 0; i < bytes.Count - 1; i++)
        {
            if (bytes[i] == 0xFF && bytes[i + 1] == 0xD8) { start = i; break; }
        }
        if (start < 0)
        {
            if (bytes.Count > 4096) bytes.RemoveRange(0, bytes.Count - 2);
            return false;
        }

        int end = -1;
        for (int i = start + 2; i < bytes.Count - 1; i++)
        {
            if (bytes[i] == 0xFF && bytes[i + 1] == 0xD9) { end = i + 2; break; }
        }
        if (end < 0)
        {
            if (start > 0) bytes.RemoveRange(0, start);
            return false;
        }

        jpg = bytes.GetRange(start, end - start).ToArray();
        bytes.RemoveRange(0, end);
        return true;
    }

    private static BitmapSource BitmapSourceFromJpeg(byte[] jpg)
    {
        using var ms = new MemoryStream(jpg);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static string EscapeRecorderArg(string value) => (value ?? "").Replace("\"", "\\\"");
}
