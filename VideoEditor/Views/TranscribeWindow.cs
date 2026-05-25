using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VideoEditor.Models;
using VideoEditor.Services;

namespace VideoEditor.Views;

public class TranscribeWindow : Window
{
    public List<SubtitleSegment> Segments { get; private set; } = new();

    private readonly ComboBox _sourceBox;
    private readonly ComboBox _languageBox;
    private readonly ComboBox _modelBox;
    private readonly TextBlock _statusText;
    private readonly ProgressBar _progress;
    private readonly Button _startBtn;
    private readonly Button _cancelBtn;

    private readonly VideoClip? _selectedClip;
    private readonly List<VideoClip> _videoClips;
    private CancellationTokenSource? _cts;

    private static readonly string[] LanguageItems = { "Auto-detect", "Hebrew", "English" };
    private static readonly string[] LanguageKeys  = { "auto",        "he",     "en"      };
    private static readonly string[] ModelItems = { "Tiny — fastest (75 MB)", "Base — balanced (140 MB)", "Small — most accurate (470 MB)" };
    private static readonly string[] ModelKeys  = { "tiny",                    "base",                      "small" };
    private static readonly string[] SourceItems = { "Selected clip", "All video clips" };
    private static readonly string[] SourceKeys  = { "clip",          "all" };

    public TranscribeWindow(VideoClip? selectedClip, IEnumerable<VideoClip> allVideoClips)
    {
        _selectedClip = selectedClip;
        _videoClips = new List<VideoClip>();
        foreach (var c in allVideoClips) if (!c.IsAudioOnly) _videoClips.Add(c);

        var ch = WindowBuilder.Build(this, "📝",
            "Transcribe video",
            "Auto-generate subtitles with whisper.cpp",
            560, 460, "Start", primarySuccess: true);

        _startBtn = ch.Primary;
        _cancelBtn = ch.Cancel;

        ch.Body.Children.Add(WindowBuilder.Lbl("Source"));
        _sourceBox = MakeCombo(SourceItems);
        int srcIdx = Array.IndexOf(SourceKeys, AppSettings.LastTranscribeSource);
        if (srcIdx < 0) srcIdx = 0;
        if (_selectedClip == null || _selectedClip.IsAudioOnly) srcIdx = 1;
        _sourceBox.SelectedIndex = srcIdx;
        ch.Body.Children.Add(_sourceBox);

        ch.Body.Children.Add(WindowBuilder.Lbl("Language"));
        _languageBox = MakeCombo(LanguageItems);
        int langIdx = Array.IndexOf(LanguageKeys, AppSettings.LastTranscribeLanguage);
        if (langIdx < 0) langIdx = 0;
        _languageBox.SelectedIndex = langIdx;
        ch.Body.Children.Add(_languageBox);

        ch.Body.Children.Add(WindowBuilder.Lbl("Model"));
        _modelBox = MakeCombo(ModelItems);
        int modelIdx = Array.IndexOf(ModelKeys, AppSettings.LastTranscribeModel);
        if (modelIdx < 0) modelIdx = 1;
        _modelBox.SelectedIndex = modelIdx;
        ch.Body.Children.Add(_modelBox);

        _statusText = new TextBlock
        {
            Text = "",
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x91, 0xA8)),
            FontSize = 11.5,
            Margin = new Thickness(0, 16, 0, 4),
            TextWrapping = TextWrapping.Wrap
        };
        ch.Body.Children.Add(_statusText);

        _progress = new ProgressBar
        {
            Minimum = 0, Maximum = 1, Value = 0,
            Height = 6,
            Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x11, 0x19)),
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xFF))
        };
        ch.Body.Children.Add(_progress);

        _startBtn.Click += async (_, _) => await StartAsync();

        VideoEditor.Services.Localization.TranslateTree(this);
        FlowDirection = VideoEditor.Services.Localization.IsHebrew
            ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    private async Task StartAsync()
    {
        if (_videoClips.Count == 0)
        {
            _statusText.Text = "No video clip selected";
            return;
        }

        var sourceKey = SourceKeys[_sourceBox.SelectedIndex];
        var langKey   = LanguageKeys[_languageBox.SelectedIndex];
        var modelKey  = ModelKeys[_modelBox.SelectedIndex];

        AppSettings.LastTranscribeSource   = sourceKey;
        AppSettings.LastTranscribeLanguage = langKey;
        AppSettings.LastTranscribeModel    = modelKey;
        AppSettings.Save();

        var clips = sourceKey == "clip" && _selectedClip != null && !_selectedClip.IsAudioOnly
            ? new List<VideoClip> { _selectedClip }
            : _videoClips;

        _startBtn.IsEnabled = false;
        _cancelBtn.IsEnabled = true;
        _sourceBox.IsEnabled = false;
        _languageBox.IsEnabled = false;
        _modelBox.IsEnabled = false;

        _cts = new CancellationTokenSource();
        var svc = new WhisperService();
        svc.Log += line => Dispatcher.Invoke(() =>
        {
            if (line.Contains("ggml-")) _statusText.Text = "Downloading model…";
            else if (line.Contains("Extracting audio")) _statusText.Text = "Extracting audio…";
            else if (line.Contains("Running whisper")) _statusText.Text = "Transcribing…";
            else if (line.Contains("whisper-cli") || line.Contains("whisper.cpp")) _statusText.Text = "Downloading whisper.cpp…";
        });

        try
        {
            var all = new List<SubtitleSegment>();
            int total = clips.Count;
            for (int i = 0; i < total; i++)
            {
                var c = clips[i];
                var clipProgress = new Progress<double>(p =>
                {
                    var globalP = (i + p) / total;
                    Dispatcher.Invoke(() => _progress.Value = globalP);
                });

                var segs = await svc.TranscribeAsync(
                    c.SourceFile,
                    c.InPoint,
                    c.OutPoint - c.InPoint,
                    langKey,
                    modelKey,
                    clipProgress,
                    _cts.Token);

                // Shift parsed times by (TimelineStart - InPoint) to land on project time.
                // (Parsed times start at 0 because we passed -ss/-t to ffmpeg.)
                double offset = c.TimelineStart;
                foreach (var s in segs)
                {
                    all.Add(new SubtitleSegment
                    {
                        StartSeconds = s.StartSeconds + offset,
                        EndSeconds   = s.EndSeconds + offset,
                        Text = s.Text
                    });
                }
            }

            Segments = all;
            _statusText.Text = $"Done — {all.Count} segments";
            DialogResult = true;
            Close();
        }
        catch (OperationCanceledException)
        {
            _statusText.Text = "Cancelled";
            _startBtn.IsEnabled = true;
            _sourceBox.IsEnabled = true;
            _languageBox.IsEnabled = true;
            _modelBox.IsEnabled = true;
        }
        catch (Exception ex)
        {
            _statusText.Text = "Transcription failed: " + ex.Message;
            _startBtn.IsEnabled = true;
            _sourceBox.IsEnabled = true;
            _languageBox.IsEnabled = true;
            _modelBox.IsEnabled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        try { _cts?.Cancel(); } catch { }
        base.OnClosed(e);
    }

    private static ComboBox MakeCombo(IEnumerable<string> items)
    {
        var cb = new ComboBox
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x11, 0x19)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xF2)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 6, 9, 6),
            FontSize = 12.5
        };
        foreach (var s in items) cb.Items.Add(s);
        return cb;
    }
}
