using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VideoEditor.Models;
using VideoEditor.Services;
using Localization = VideoEditor.Services.Localization;

namespace VideoEditor.Views;

/// <summary>
/// Modal that orchestrates Whisper transcription → Gemini caption generation →
/// returns a list of <see cref="TextOverlay"/> placed in timeline time
/// (i.e. each overlay's StartSeconds / EndSeconds are offset by the clip's
/// position on the timeline so the caller can drop them straight into
/// <c>timeline.TextOverlays</c>).
/// </summary>
public class AiCaptionsWindow : Window
{
    public List<TextOverlay> Result { get; private set; } = new();

    private readonly ComboBox _sourceBox;
    private readonly ComboBox _languageBox;
    private readonly ComboBox _modelBox;
    private readonly TextBlock _statusText;
    private readonly TextBlock _phaseText;
    private readonly ProgressBar _progress;
    private readonly Button _startBtn;
    private readonly Button _cancelBtn;

    private readonly VideoClip? _selectedClip;
    private readonly List<VideoClip> _videoClips;
    private readonly int _videoWidth;
    private readonly int _videoHeight;
    private CancellationTokenSource? _cts;

    private static readonly string[] LanguageItems = { "Auto-detect", "Hebrew", "English" };
    private static readonly string[] LanguageKeys  = { "auto",        "he",     "en"      };
    private static readonly string[] ModelItems = { "Tiny — fastest (75 MB)", "Base — balanced (140 MB)", "Small — most accurate (470 MB)" };
    private static readonly string[] ModelKeys  = { "tiny",                    "base",                      "small" };
    private static readonly string[] SourceItems = { "Selected clip", "All video clips" };
    private static readonly string[] SourceKeys  = { "clip",          "all" };

    public AiCaptionsWindow(VideoClip? selectedClip, IEnumerable<VideoClip> allVideoClips,
                            int videoWidth, int videoHeight)
    {
        _selectedClip = selectedClip;
        _videoClips = new List<VideoClip>();
        foreach (var c in allVideoClips) if (!c.IsAudioOnly) _videoClips.Add(c);
        _videoWidth = Math.Max(320, videoWidth);
        _videoHeight = Math.Max(180, videoHeight);

        var ch = WindowBuilder.Build(this, "✨",
            "AI Captions",
            "Auto-generate short on-screen captions with Gemini",
            580, 500, "Generate", primarySuccess: true);

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

        ch.Body.Children.Add(WindowBuilder.Lbl("Whisper model"));
        _modelBox = MakeCombo(ModelItems);
        int modelIdx = Array.IndexOf(ModelKeys, AppSettings.LastTranscribeModel);
        if (modelIdx < 0) modelIdx = 1;
        _modelBox.SelectedIndex = modelIdx;
        ch.Body.Children.Add(_modelBox);

        // Phase line: "Step 1/3 — Transcribing…"
        _phaseText = new TextBlock
        {
            Text = "",
            Foreground = WindowBuilder.TextMute,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 22, 0, 4)
        };
        ch.Body.Children.Add(_phaseText);

        _progress = new ProgressBar
        {
            Minimum = 0, Maximum = 1, Value = 0,
            Height = 8,
            Background = WindowBuilder.Bg2,
            Foreground = WindowBuilder.Accent,
            BorderThickness = new Thickness(0)
        };
        ch.Body.Children.Add(_progress);

        _statusText = new TextBlock
        {
            Text = "",
            Foreground = WindowBuilder.TextDim,
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        ch.Body.Children.Add(_statusText);

        _startBtn.Click += async (_, _) => await RunAsync();

        FlowDirection = Localization.IsHebrew ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Localization.TranslateTree(this);
    }

    private static ComboBox MakeCombo(string[] items)
    {
        var cb = new ComboBox
        {
            Background = WindowBuilder.Bg2,
            Foreground = WindowBuilder.TextBr,
            BorderBrush = WindowBuilder.Line,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 6, 9, 6),
            FontSize = 12.5,
            Margin = new Thickness(0, 0, 0, 4)
        };
        foreach (var i in items) cb.Items.Add(i);
        return cb;
    }

    private async Task RunAsync()
    {
        if (_cts != null) return; // already running

        var sourceKey = SourceKeys[Math.Max(0, _sourceBox.SelectedIndex)];
        var languageKey = LanguageKeys[Math.Max(0, _languageBox.SelectedIndex)];
        var modelKey = ModelKeys[Math.Max(0, _modelBox.SelectedIndex)];
        AppSettings.LastTranscribeSource = sourceKey;
        AppSettings.LastTranscribeLanguage = languageKey;
        AppSettings.LastTranscribeModel = modelKey;
        AppSettings.Save();

        // Pick the clips to transcribe.
        var clipsToProcess = new List<VideoClip>();
        if (sourceKey == "clip" && _selectedClip != null && !_selectedClip.IsAudioOnly)
            clipsToProcess.Add(_selectedClip);
        else
            clipsToProcess.AddRange(_videoClips);

        if (clipsToProcess.Count == 0)
        {
            _statusText.Text = Localization.T("No video clips to transcribe.");
            _statusText.Foreground = Brushes.OrangeRed;
            return;
        }

        _startBtn.IsEnabled = false;
        _cancelBtn.Content = Localization.T("Stop");
        _cts = new CancellationTokenSource();

        var allSegments = new List<SubtitleSegment>();

        try
        {
            var whisper = new WhisperService();
            whisper.Log += line => Dispatcher.Invoke(() => { _statusText.Text = line; });

            // ---- Phase 1: transcribe (0 → 0.60) ----
            _phaseText.Text = Localization.T("Step 1/3 — Transcribing audio");
            int idx = 0;
            foreach (var clip in clipsToProcess)
            {
                _cts.Token.ThrowIfCancellationRequested();
                int slot = idx;
                double slotShare = 1.0 / clipsToProcess.Count;
                var clipProgress = new Progress<double>(p =>
                {
                    Dispatcher.Invoke(() => _progress.Value = (slot * slotShare + p * slotShare) * 0.60);
                });
                var segs = await whisper.TranscribeAsync(
                    clip.SourceFile, clip.InPoint, clip.OutPoint - clip.InPoint,
                    languageKey, modelKey, clipProgress, _cts.Token);

                // Offset to timeline time (account for speed warping).
                double speed = Math.Max(0.01, clip.Speed);
                double offset = clip.TimelineStart;
                foreach (var s in segs)
                {
                    allSegments.Add(new SubtitleSegment
                    {
                        StartSeconds = offset + s.StartSeconds / speed,
                        EndSeconds   = offset + s.EndSeconds   / speed,
                        Text = s.Text
                    });
                }
                idx++;
            }

            if (allSegments.Count == 0)
                throw new Exception(Localization.T("Whisper produced no segments — is there spoken audio?"));

            // ---- Phase 2: ask Gemini (0.60 → 0.95) ----
            _phaseText.Text = Localization.T("Step 2/3 — Generating captions with Gemini");
            _statusText.Text = Localization.T("Sending {0} segments to Gemini…").Replace("{0}", allSegments.Count.ToString());
            var llm = new LlmCaptionService();
            llm.Log += line => Dispatcher.Invoke(() => { _statusText.Text = line; });

            var llmProgress = new Progress<double>(p =>
                Dispatcher.Invoke(() => _progress.Value = 0.60 + p * 0.35));

            var overlays = await llm.GenerateOverlaysAsync(
                allSegments, _videoWidth, _videoHeight, llmProgress, _cts.Token);

            // ---- Phase 3: hand back (0.95 → 1.0) ----
            _phaseText.Text = Localization.T("Step 3/3 — Applying overlays");
            _progress.Value = 1.0;
            Result = overlays;
            _statusText.Text = Localization.T("Done · {0} overlays generated.").Replace("{0}", overlays.Count.ToString());
            _statusText.Foreground = WindowBuilder.TextBr;
            DialogResult = true;
            Close();
        }
        catch (OperationCanceledException)
        {
            _statusText.Text = Localization.T("Cancelled.");
            _statusText.Foreground = WindowBuilder.TextMute;
        }
        catch (Exception ex)
        {
            _statusText.Text = ex.Message;
            _statusText.Foreground = Brushes.OrangeRed;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _startBtn.IsEnabled = true;
            _cancelBtn.Content = Localization.T("Close");
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        try { _cts?.Cancel(); } catch { }
        base.OnClosing(e);
    }
}
