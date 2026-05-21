using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using VideoEditor.Controls;
using VideoEditor.Models;
using VideoEditor.Services;
using VideoEditor.Views;

namespace VideoEditor;

public partial class MainWindow : Window
{
    private readonly FFmpegService _ff = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(80) };

    private readonly Dictionary<VideoBlock, ResizableBlock> _blockControls = new();
    private VideoBlock? _selectedBlock;
    private VideoClip? _selectedClip;
    private VideoClip? _playingClip;
    private bool _suppress;
    private bool _isPlaying;
    private double _masterVolume = 0.8;

    // Clipboard for Ctrl+C / Ctrl+V
    private VideoClip? _clipboardClip;
    private VideoBlock? _clipboardBlock;
    private double? _clipboardAudioVolume;

    // The audio bar of a clip currently selected on the timeline (separate from clip body selection)
    private VideoClip? _selectedAudio;

    public MainWindow()
    {
        InitializeComponent();

        // Apply Language preference (RTL for Hebrew)
        ApplyLanguage();
        // Apply default master volume from settings
        _masterVolume = AppSettings.DefaultMasterVolume / 100.0;
        volumeSlider.Value = AppSettings.DefaultMasterVolume;
        videoView.Volume = _masterVolume;
        videoView.ScrubbingEnabled = AppSettings.ScrubbingQuality != "smooth";
        timeline.FFmpeg = _ff;
        // Re-apply on settings change at runtime
        AppSettings.Changed += () =>
        {
            Dispatcher.Invoke(() =>
            {
                ApplyLanguage();
                if (_playingClip == null) volumeSlider.Value = AppSettings.DefaultMasterVolume;
                videoView.ScrubbingEnabled = AppSettings.ScrubbingQuality != "smooth";
            });
        };

        _tick.Tick += Tick_OnTick;
        _tick.Start();

        PreviewKeyDown += MainWindow_PreviewKeyDown;

        timeline.Seek += sec => SeekTo(sec);
        timeline.BlockSelected += b => SelectBlock(b);
        timeline.ClipSelected += c => SelectClip(c);
        timeline.ClipDoubleClicked += c => { SeekTo(timeline.GetClipStart(c)); videoView.Play(); _isPlaying = true; };
        timeline.ClipContextAction += OnClipContextAction;
        timeline.FilesDropped += (files, sec) => AddFiles(files, sec);
        timeline.ClipScrubPreview += ScrubToClipFrame;
        timeline.ClipEdgeDragEnded += c => { /* leave preview at the trim point */ };
        timeline.AudioSelected += OnAudioSelected;
        timeline.AudioContextAction += OnAudioContextAction;
        timeline.ClipsChanged += () =>
        {
            if (timeline.Clips.Count > 0 && _playingClip == null) LoadClipForPreview(timeline.Clips[0], 0);
            UpdateStats();
            UpdateTimeDisplays();
        };
        timeline.BlocksChanged += () => UpdateStats();

        overlayCanvas.SizeChanged += (_, _) => RepositionOverlay();
        overlayCanvas.MouseLeftButtonDown += OverlayCanvas_BackgroundClick;
    }

    protected override void OnClosed(EventArgs e)
    {
        _tick.Stop();
        try { videoView.Stop(); videoView.Close(); } catch { }
        base.OnClosed(e);
    }

    // ===== Drag & Drop =====

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            AddFiles(files);
        }
        e.Handled = true;
    }

    private async void AddFiles(string[] files, double? insertAtSec = null)
    {
        var exts = new[] { ".mp4", ".mov", ".mkv", ".avi", ".webm", ".wmv", ".flv", ".m4v" };
        foreach (var f in files)
        {
            if (!File.Exists(f)) continue;
            if (!exts.Contains(Path.GetExtension(f).ToLowerInvariant())) continue;
            await AddClipAsync(f, insertAtSec);
            if (insertAtSec.HasValue) insertAtSec = insertAtSec.Value + 0.001; // next file goes after this one
        }
    }

    private async System.Threading.Tasks.Task AddClipAsync(string path, double? insertAtSec = null)
    {
        status.Text = "Probing " + Path.GetFileName(path) + "...";
        try
        {
            var (w, h, d) = await _ff.ProbeAsync(path);
            var clip = new VideoClip
            {
                SourceFile = path,
                OriginalDuration = d > 0 ? d : 5,
                InPoint = 0,
                OutPoint = d > 0 ? d : 5,
                VideoWidth = w,
                VideoHeight = h,
                AccentColor = VideoClip.NextColor()
            };
            if (insertAtSec.HasValue)
            {
                timeline.ReorderClipToPosition(clip, insertAtSec.Value);
            }
            else
            {
                timeline.Clips.Add(clip);
            }
            status.Text = $"Added: {Path.GetFileName(path)} · {w}×{h} · {Timeline.FormatTime(d)}";
            projectName.Text = Path.GetFileNameWithoutExtension(path);
            projDims.Text = $"{w}×{h}";
            UpdateStats();
            if (timeline.Clips.Count == 1)
            {
                LoadClipForPreview(clip, 0);
                timeline.FitToView();
            }
        }
        catch (Exception ex)
        {
            status.Text = "Failed to add: " + ex.Message;
        }
    }

    private void UpdateStats()
    {
        statClips.Text = timeline.Clips.Count.ToString();
        statBlocks.Text = timeline.Blocks.Count.ToString();
        statusCache.Text = $"Cache: {timeline.Clips.Count * 8} thumbnails";
        hudBlocks.Text = $"{timeline.Blocks.Count} blocks";
    }

    private async void OpenBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Multiselect = true, Filter = "Video files|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.wmv;*.flv|All files|*.*" };
        if (dlg.ShowDialog() != true) return;
        AddFiles(dlg.FileNames);
        await System.Threading.Tasks.Task.CompletedTask;
    }

    // ===== Playback (multi-clip) =====

    private readonly DispatcherTimer _scrubTimer = new() { Interval = TimeSpan.FromMilliseconds(35) };
    private VideoClip? _pendingScrubClip;
    private double _pendingScrubSourceTime;
    private bool _scrubInited;

    private void EnsureScrubTimer()
    {
        if (_scrubInited) return;
        _scrubInited = true;
        _scrubTimer.Tick += (_, _) =>
        {
            _scrubTimer.Stop();
            if (_pendingScrubClip != null) DoScrub(_pendingScrubClip, _pendingScrubSourceTime);
        };
    }

    private void ScrubToClipFrame(VideoClip clip, double sourceTime)
    {
        EnsureScrubTimer();
        _pendingScrubClip = clip;
        _pendingScrubSourceTime = sourceTime;
        if (!_scrubTimer.IsEnabled) _scrubTimer.Start();
    }

    private void DoScrub(VideoClip clip, double sourceTime)
    {
        sourceTime = Math.Max(0, Math.Min(clip.OriginalDuration - 0.01, sourceTime));
        try
        {
            var srcUri = new Uri(clip.SourceFile);
            if (videoView.Source == null || !string.Equals(videoView.Source.LocalPath, srcUri.LocalPath, StringComparison.OrdinalIgnoreCase))
            {
                videoView.Source = srcUri;
                _playingClip = clip;
                ApplyClipTransform(clip);
                videoView.SpeedRatio = 1.0;
            }
            videoView.Pause();
            _isPlaying = false;
            videoView.Position = TimeSpan.FromSeconds(sourceTime);
        }
        catch { }
    }

    private void LoadClipForPreview(VideoClip clip, double offsetWithinClip)
    {
        try
        {
            _playingClip = clip;
            videoView.Source = new Uri(clip.SourceFile);
            videoView.Position = TimeSpan.FromSeconds(clip.InPoint + Math.Max(0, offsetWithinClip));
            videoView.SpeedRatio = clip.Speed;
            videoView.Volume = _masterVolume * clip.Volume;
            ApplyClipTransform(clip);
            if (!_isPlaying)
            {
                videoView.Play();
                System.Threading.Tasks.Task.Delay(80).ContinueWith(_ => Dispatcher.Invoke(() => { if (!_isPlaying) videoView.Pause(); }));
            }
        }
        catch (Exception ex)
        {
            status.Text = "Preview failed: " + ex.Message;
        }
    }

    private void ApplyClipTransform(VideoClip clip)
    {
        var tg = new TransformGroup();
        tg.Children.Add(new ScaleTransform(clip.FlipH ? -1 : 1, clip.FlipV ? -1 : 1, videoView.ActualWidth / 2, videoView.ActualHeight / 2));
        if (clip.RotateDegrees != 0)
            tg.Children.Add(new RotateTransform(clip.RotateDegrees, videoView.ActualWidth / 2, videoView.ActualHeight / 2));
        videoView.RenderTransform = tg;
    }

    private void SeekTo(double seconds)
    {
        var clip = timeline.GetClipAt(seconds);
        if (clip == null) { timeline.SetCurrent(seconds); return; }
        var withinClip = Math.Max(0, seconds - clip.TimelineStart) * clip.Speed;
        if (clip != _playingClip)
        {
            LoadClipForPreview(clip, withinClip);
        }
        else
        {
            try { videoView.Position = TimeSpan.FromSeconds(clip.InPoint + withinClip); } catch { }
        }
        timeline.SetCurrent(seconds);
    }

    private VideoClip? NextClipAfter(VideoClip c)
    {
        return timeline.Clips
            .Where(x => x.TimelineStart > c.TimelineStart - 0.001 && x != c)
            .OrderBy(x => x.TimelineStart)
            .FirstOrDefault();
    }

    private VideoClip? PrevClipBefore(VideoClip c)
    {
        return timeline.Clips
            .Where(x => x.TimelineStart < c.TimelineStart && x != c)
            .OrderByDescending(x => x.TimelineStart)
            .FirstOrDefault();
    }

    private void Tick_OnTick(object? sender, EventArgs e)
    {
        if (!_isPlaying || _playingClip == null) return;
        try
        {
            var mediaPos = videoView.Position.TotalSeconds;
            if (mediaPos >= _playingClip.OutPoint - 0.05)
            {
                var next = NextClipAfter(_playingClip);
                if (next != null)
                {
                    timeline.SetCurrent(next.TimelineStart);
                    LoadClipForPreview(next, 0);
                    if (_isPlaying) videoView.Play();
                }
                else
                {
                    if (AppSettings.LoopOnEnd && timeline.Clips.Count > 0)
                    {
                        // Loop: restart from beginning
                        var first = timeline.Clips.OrderBy(c => c.TimelineStart).First();
                        timeline.SetCurrent(first.TimelineStart);
                        LoadClipForPreview(first, 0);
                        if (_isPlaying) videoView.Play();
                    }
                    else
                    {
                        _isPlaying = false;
                        videoView.Pause();
                    }
                }
                return;
            }
            var clipStart = _playingClip.TimelineStart;
            var withinClip = (mediaPos - _playingClip.InPoint) / Math.Max(0.01, _playingClip.Speed);
            timeline.SetCurrent(clipStart + withinClip);
            UpdateBlockVisibility();
            UpdateTimeDisplays();
        }
        catch (Exception ex)
        {
            // The tick fires 12×/sec; surface any unexpected failure in the debugger and the
            // status bar so problems don't silently snowball.
            System.Diagnostics.Debug.WriteLine($"Tick error: {ex}");
            status.Text = "Playback tick error: " + ex.Message;
        }
    }

    private void UpdateTimeDisplays()
    {
        curTimeText.Text = Timeline.FormatTime(timeline.CurrentSeconds);
        totTimeText.Text = Timeline.FormatTime(timeline.TotalSeconds);
        if (_playingClip != null)
        {
            hudClipName.Text = Path.GetFileName(_playingClip.SourceFile);
            var pos = videoView.Position.TotalSeconds;
            hudTimeWithin.Text = Timeline.FormatTime(pos) + " in source";
        }
        else
        {
            hudClipName.Text = "No clip loaded";
            hudTimeWithin.Text = "--:--";
        }
    }

    private void VideoView_MediaOpened(object sender, RoutedEventArgs e) { }
    private void VideoView_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (_playingClip == null) return;
        var next = NextClipAfter(_playingClip);
        if (next != null)
        {
            LoadClipForPreview(next, 0);
            timeline.SetCurrent(next.TimelineStart);
            if (_isPlaying) videoView.Play();
        }
        else
        {
            _isPlaying = false;
        }
    }

    private void PlayBtn_Click(object sender, RoutedEventArgs e)
    {
        if (timeline.Clips.Count == 0) return;
        if (_playingClip == null) LoadClipForPreview(timeline.Clips[0], 0);
        videoView.Play(); _isPlaying = true;
        UpdateBlockVisibility();
    }
    private void PauseBtn_Click(object sender, RoutedEventArgs e) { videoView.Pause(); _isPlaying = false; UpdateBlockVisibility(); }
    private void StopBtn_Click(object sender, RoutedEventArgs e)
    {
        videoView.Pause(); _isPlaying = false;
        if (timeline.Clips.Count > 0)
        {
            LoadClipForPreview(timeline.Clips[0], 0);
            timeline.SetCurrent(0);
            videoView.Pause();
        }
        UpdateBlockVisibility();
    }
    private void Back5_Click(object sender, RoutedEventArgs e) => SeekTo(Math.Max(0, timeline.CurrentSeconds - AppSettings.BackForwardSeconds));
    private void Fwd5_Click(object sender, RoutedEventArgs e) => SeekTo(Math.Min(timeline.TotalSeconds, timeline.CurrentSeconds + AppSettings.BackForwardSeconds));
    private void PrevClip_Click(object sender, RoutedEventArgs e)
    {
        if (_playingClip == null) return;
        var prev = PrevClipBefore(_playingClip);
        if (prev != null) SeekTo(prev.TimelineStart);
    }
    private void NextClip_Click(object sender, RoutedEventArgs e)
    {
        if (_playingClip == null) return;
        var next = NextClipAfter(_playingClip);
        if (next != null) SeekTo(next.TimelineStart);
    }
    private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _masterVolume = e.NewValue / 100.0;
        if (videoView != null)
        {
            if (_playingClip != null) videoView.Volume = _masterVolume * _playingClip.Volume;
            else videoView.Volume = _masterVolume;
        }
        if (volText != null) volText.Text = ((int)Math.Round(e.NewValue)).ToString();
    }

    private async void ExtractAudio_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip();
        if (c == null) { MessageBox.Show("Select a clip first."); return; }
        var sfd = new SaveFileDialog
        {
            FileName = Path.GetFileNameWithoutExtension(c.SourceFile) + "_audio.mp3",
            Filter = "MP3|*.mp3|AAC|*.aac|WAV|*.wav|OGG|*.ogg|FLAC|*.flac"
        };
        if (sfd.ShowDialog() != true) return;
        status.Text = "Extracting audio...";
        progress.Value = 0;
        var prog = new Progress<double>(v => Dispatcher.Invoke(() => progress.Value = v));
        try
        {
            await _ff.ExtractAudioAsync(c.SourceFile, sfd.FileName, c.OriginalDuration, prog);
            status.Text = "Audio extracted: " + Path.GetFileName(sfd.FileName);
            progress.Value = 1;
        }
        catch (Exception ex)
        {
            status.Text = "Extract failed: " + ex.Message;
            MessageBox.Show(ex.Message, "Error");
        }
    }

    private async void MuteAudio_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip();
        if (c == null) { MessageBox.Show("Select a clip first."); return; }
        var result = MessageBox.Show(
            "Mute audio on this clip (live, no re-encode), or remove the audio track entirely (re-encode)?\n\nYes = Mute  |  No = Remove track  |  Cancel = nothing",
            "Mute / Remove Audio", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel) return;
        if (result == MessageBoxResult.Yes)
        {
            c.Volume = 0;
            status.Text = "Clip muted (export will silence it).";
            return;
        }
        // Remove audio track via FFmpeg
        await ApplyDestructiveOpAsync(c, async (input, output, prog) =>
            await _ff.RemoveAudioAsync(input, output, c.OriginalDuration, prog));
        status.Text = "Audio track removed from clip.";
    }

    // ===== Block management =====

    private void AddBlock_Click(object sender, RoutedEventArgs e)
    {
        if (timeline.Clips.Count == 0) { MessageBox.Show("Add a video first."); return; }
        var b = new VideoBlock
        {
            X = Math.Max(0, overlayCanvas.ActualWidth / 2 - 100),
            Y = Math.Max(0, overlayCanvas.ActualHeight / 2 - 60),
            Width = 200, Height = 120,
            StartSeconds = 0, EndSeconds = timeline.TotalSeconds, CoversWholeVideo = true,
            Color = Colors.Black, Mode = BlockMode.Solid,
            Label = $"Block {timeline.Blocks.Count + 1}"
        };
        timeline.Blocks.Add(b);
        var ctl = new ResizableBlock(b);
        ctl.Selected += rb => SelectBlock(rb.Model);
        ctl.Changed += _ => SyncBlockInspector();
        overlayCanvas.Children.Add(ctl);
        _blockControls[b] = ctl;
        SelectBlock(b);
    }

    private void OverlayCanvas_BackgroundClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == overlayCanvas) SelectBlock(null);
    }

    private void DeleteBlock_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedBlock == null) return;
        if (_blockControls.TryGetValue(_selectedBlock, out var ctl))
        {
            overlayCanvas.Children.Remove(ctl);
            _blockControls.Remove(_selectedBlock);
        }
        timeline.Blocks.Remove(_selectedBlock);
        _selectedBlock = null;
        blockPanel.Visibility = Visibility.Collapsed;
    }

    // ===== Selection =====

    private void SelectBlock(VideoBlock? b)
    {
        _selectedBlock = b;
        if (b != null) _selectedClip = null;
        foreach (var kv in _blockControls) kv.Value.SetSelected(kv.Key == b);
        timeline.SelectBlock(b);
        blockPanel.Visibility = b != null ? Visibility.Visible : Visibility.Collapsed;
        clipPanel.Visibility = Visibility.Collapsed;
        emptyInspector.Visibility = (b == null && _selectedClip == null) ? Visibility.Visible : Visibility.Collapsed;
        if (b == null) return;
        _suppress = true;
        lblBox.Text = b.Label;
        modeBox.SelectedIndex = (int)b.Mode;
        strengthSlider.Value = b.BlurStrength;
        wholeCheck.IsChecked = b.CoversWholeVideo;
        startBox.Text = b.StartSeconds.ToString("0.###");
        endBox.Text = b.EndSeconds.ToString("0.###");
        _suppress = false;
    }

    private void SelectClip(VideoClip? c)
    {
        _selectedClip = c;
        if (c != null) _selectedBlock = null;
        timeline.SelectClip(c);
        clipPanel.Visibility = c != null ? Visibility.Visible : Visibility.Collapsed;
        blockPanel.Visibility = Visibility.Collapsed;
        emptyInspector.Visibility = (c == null && _selectedBlock == null) ? Visibility.Visible : Visibility.Collapsed;
        if (c == null) return;
        _suppress = true;
        clipNameText.Text = Path.GetFileName(c.SourceFile);
        clipInBox.Text = c.InPoint.ToString("0.###");
        clipOutBox.Text = c.OutPoint.ToString("0.###");
        clipSpeedSlider.Value = c.Speed;
        clipSpeedLabel.Text = c.Speed.ToString("0.00") + "x";
        clipVolSlider.Value = c.Volume;
        clipVolLabel.Text = (c.Volume * 100).ToString("0") + "%";
        _suppress = false;
    }

    private void SyncBlockInspector()
    {
        if (_selectedBlock == null) return;
        _suppress = true;
        startBox.Text = _selectedBlock.StartSeconds.ToString("0.###");
        endBox.Text = _selectedBlock.EndSeconds.ToString("0.###");
        wholeCheck.IsChecked = _selectedBlock.CoversWholeVideo;
        _suppress = false;
    }

    private void RepositionOverlay()
    {
        foreach (var kv in _blockControls)
        {
            Canvas.SetLeft(kv.Value, kv.Key.X);
            Canvas.SetTop(kv.Value, kv.Key.Y);
            kv.Value.Width = kv.Key.Width;
            kv.Value.Height = kv.Key.Height;
        }
    }

    // ===== Inspector handlers =====

    private void LblBox_Changed(object sender, TextChangedEventArgs e) { if (!_suppress && _selectedBlock != null) _selectedBlock.Label = lblBox.Text; }
    private void Mode_Changed(object sender, SelectionChangedEventArgs e) { if (!_suppress && _selectedBlock != null) _selectedBlock.Mode = (BlockMode)modeBox.SelectedIndex; }
    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedBlock == null) return;
        if (sender is Button b && b.Tag is string name) _selectedBlock.Color = (Color)ColorConverter.ConvertFromString(name);
    }
    private void Strength_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (!_suppress && _selectedBlock != null) _selectedBlock.BlurStrength = (int)e.NewValue; }
    private void Whole_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress || _selectedBlock == null) return;
        _selectedBlock.CoversWholeVideo = wholeCheck.IsChecked == true;
        if (_selectedBlock.CoversWholeVideo) { _selectedBlock.StartSeconds = 0; _selectedBlock.EndSeconds = timeline.TotalSeconds; }
    }
    private void StartEnd_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppress || _selectedBlock == null) return;
        if (double.TryParse(startBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s)) _selectedBlock.StartSeconds = Math.Max(0, s);
        if (double.TryParse(endBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var en)) _selectedBlock.EndSeconds = Math.Min(timeline.TotalSeconds, Math.Max(_selectedBlock.StartSeconds + 0.1, en));
        if (_selectedBlock.EndSeconds < timeline.TotalSeconds || _selectedBlock.StartSeconds > 0) _selectedBlock.CoversWholeVideo = false;
    }

    private void ClipInOut_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppress || _selectedClip == null) return;
        // Parse both first, then clamp against each other — otherwise we'd clamp In against
        // an outdated Out (or vice versa) when the user is editing the second field.
        var inOk = double.TryParse(clipInBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var newIn);
        var outOk = double.TryParse(clipOutBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var newOut);
        if (!inOk) newIn = _selectedClip.InPoint;
        if (!outOk) newOut = _selectedClip.OutPoint;
        newIn = Math.Max(0, newIn);
        newOut = Math.Min(_selectedClip.OriginalDuration, newOut);
        if (newOut < newIn + 0.1) newOut = newIn + 0.1;
        _selectedClip.InPoint = newIn;
        _selectedClip.OutPoint = newOut;
    }
    private void ClipSpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress || _selectedClip == null) return;
        _selectedClip.Speed = e.NewValue;
        clipSpeedLabel.Text = e.NewValue.ToString("0.00") + "x";
        if (_playingClip == _selectedClip) videoView.SpeedRatio = e.NewValue;
    }
    private void ClipVol_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress || _selectedClip == null) return;
        _selectedClip.Volume = e.NewValue;
        clipVolLabel.Text = (e.NewValue * 100).ToString("0") + "%";
        if (_playingClip == _selectedClip) videoView.Volume = _masterVolume * e.NewValue;
    }
    private void ClipRotate_Click(object s, RoutedEventArgs e) { if (_selectedClip != null) { _selectedClip.RotateDegrees = (_selectedClip.RotateDegrees + 90) % 360; ApplyClipTransform(_selectedClip); } }
    private void ClipRotateLeft_Click(object s, RoutedEventArgs e) { if (_selectedClip != null) { _selectedClip.RotateDegrees = (_selectedClip.RotateDegrees + 270) % 360; ApplyClipTransform(_selectedClip); } }
    private void ClipFlipH_Click(object s, RoutedEventArgs e) { if (_selectedClip != null) { _selectedClip.FlipH = !_selectedClip.FlipH; ApplyClipTransform(_selectedClip); } }
    private void ClipFlipV_Click(object s, RoutedEventArgs e) { if (_selectedClip != null) { _selectedClip.FlipV = !_selectedClip.FlipV; ApplyClipTransform(_selectedClip); } }
    private void ClipDelete_Click(object s, RoutedEventArgs e)
    {
        if (_selectedClip == null) return;
        var c = _selectedClip;
        timeline.Clips.Remove(c);
        if (_playingClip == c) { _playingClip = null; videoView.Stop(); }
        SelectClip(null);
    }

    // ===== Per-clip context actions =====

    private void OnClipContextAction(VideoClip clip, string action)
    {
        _selectedClip = clip;
        SelectClip(clip);
        switch (action)
        {
            case "trim": Trim_Click(this, new RoutedEventArgs()); break;
            case "speed": Speed_Click(this, new RoutedEventArgs()); break;
            case "volume": Volume_Click(this, new RoutedEventArgs()); break;
            case "rotate90": clip.RotateDegrees = (clip.RotateDegrees + 90) % 360; ApplyClipTransform(clip); break;
            case "flipH": clip.FlipH = !clip.FlipH; ApplyClipTransform(clip); break;
            case "flipV": clip.FlipV = !clip.FlipV; ApplyClipTransform(clip); break;
            case "loop":
                clip.LoopCount += 1;
                status.Text = $"Clip will loop {clip.LoopCount}x on export";
                break;
            case "crop": Crop_Click(this, new RoutedEventArgs()); break;
            case "removeLogo": RemoveLogo_Click(this, new RoutedEventArgs()); break;
            case "addImage": AddImage_Click(this, new RoutedEventArgs()); break;
            case "addText": AddText_Click(this, new RoutedEventArgs()); break;
            case "addAudio": AddAudio_Click(this, new RoutedEventArgs()); break;
            case "stabilize": Stabilize_Click(this, new RoutedEventArgs()); break;
            case "split": SplitAtPlayhead(clip); break;
            case "splitHere":
                SplitClipAtSourceTime(clip, timeline.LastRequestedSplitSourceSec);
                status.Text = "Clip split at click position.";
                break;
            case "splitN":
                {
                    var dlg = new SplitNWindow() { Owner = this };
                    if (dlg.ShowDialog() == true) { SplitIntoNParts(clip, dlg.Parts); status.Text = $"Clip split into {dlg.Parts} parts."; }
                }
                break;
            case "duplicate": DuplicateClip(clip); break;
            case "delete": timeline.Clips.Remove(clip); if (_playingClip == clip) { _playingClip = null; videoView.Stop(); } SelectClip(null); break;
            case "extractAudio": ExtractAudioClip_Click(this, new RoutedEventArgs()); break;
            case "mute": MuteClip_Click(this, new RoutedEventArgs()); break;
            case "unmute": UnmuteClip_Click(this, new RoutedEventArgs()); break;
            case "removeAudioTrack": RemoveAudioTrack_Click(this, new RoutedEventArgs()); break;
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Skip shortcuts if a text box has focus (so typing works normally)
        if (Keyboard.FocusedElement is TextBoxBase) return;

        if (e.Key == Key.F1 || (e.Key == Key.OemQuestion && Keyboard.Modifiers == ModifierKeys.None))
        {
            Help_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        if (e.Key == Key.OemComma && Keyboard.Modifiers == ModifierKeys.None)
        {
            Settings_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        if (e.Key == Key.U && Keyboard.Modifiers == ModifierKeys.None)
        {
            DownloadUrl_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SaveBtn_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenBtn_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            HandleCopy();
            e.Handled = true;
        }
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            HandlePaste();
            e.Handled = true;
        }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.None)
        {
            Split_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Space)
        {
            if (_isPlaying) PauseBtn_Click(this, new RoutedEventArgs());
            else PlayBtn_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            if (_selectedAudio != null && _selectedAudio.IsAudioOnly)
            {
                // Audio-only clip: full delete - it's an independent entity, not just a mute target.
                var ac = _selectedAudio;
                timeline.Clips.Remove(ac);
                _selectedAudio = null;
                _selectedClip = null;
                status.Text = $"Deleted audio clip: {ac.DisplayName}";
                e.Handled = true;
            }
            else if (_selectedAudio != null)
            {
                // Attached audio (sub-bar of a video clip): mute, don't destroy the clip.
                _selectedAudio.Volume = 0;
                status.Text = $"Muted: {_selectedAudio.DisplayName}. Right-click for Remove track / Restore.";
                e.Handled = true;
            }
            else if (_selectedClip != null)
            {
                ClipDelete_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (_selectedBlock != null)
            {
                DeleteBlock_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
    }

    private void OnAudioSelected(VideoClip c)
    {
        _selectedAudio = c;
        _selectedBlock = null;
        // For audio-only clips: treat as a full clip selection so every timeline shortcut works.
        // For attached audio (sub-bar of a video clip): keep selection focused on the audio,
        // not the clip itself.
        SelectClip(c);
        _selectedAudio = c; // SelectClip clears it; restore
        if (!c.IsAudioOnly) _selectedClip = null; // attached audio: don't claim the clip
        timeline.SelectAudio(c);
        if (c.IsAudioOnly)
            status.Text = $"Audio clip: {c.DisplayName} · S = Split · Backspace = Delete · Ctrl+C/V · drag edges to trim · drag body to move";
        else
            status.Text = $"Audio: {c.DisplayName} · Vol {(int)(c.Volume * 100)}% · Backspace = Mute · Ctrl+C/V = copy/paste volume";
    }

    private async void OnAudioContextAction(VideoClip c, string action)
    {
        _selectedClip = c;
        _selectedAudio = c;
        switch (action)
        {
            case "volume":
                {
                    var dlg = new VolumeWindow() { Owner = this };
                    if (dlg.ShowDialog() == true) { c.Volume = dlg.Volume; status.Text = $"Volume → {(int)(dlg.Volume * 100)}%"; }
                }
                break;
            case "mute":
                c.Volume = 0;
                status.Text = "Audio muted (export will silence).";
                break;
            case "unmute":
                c.Volume = 1;
                status.Text = "Audio restored.";
                break;
            case "removeAudioTrack":
                await ApplyDestructiveOpAsync(c, async (input, output, prog) =>
                    await _ff.RemoveAudioAsync(input, output, c.OriginalDuration, prog));
                status.Text = "Audio track removed from clip.";
                break;
            case "extractAudio":
                ExtractAudio_Click(this, new RoutedEventArgs());
                break;
            case "addAudio":
                AddAudio_Click(this, new RoutedEventArgs());
                break;
            case "detachAudio":
                await DetachAudioFromClipAsync(c);
                break;
            case "deleteAudioClip":
                timeline.Clips.Remove(c);
                _selectedAudio = null;
                status.Text = "Detached audio clip deleted.";
                break;
        }
    }

    private async System.Threading.Tasks.Task DetachAudioFromClipAsync(VideoClip parent)
    {
        if (parent.IsAudioOnly) { status.Text = "Already an audio-only clip."; return; }

        status.Text = "Detaching audio from " + parent.DisplayName + "...";
        progress.Value = 0;
        var prog = new Progress<double>(v => Dispatcher.Invoke(() => progress.Value = v));

        try
        {
            // Extract to a stable cache folder next to the EXE
            var detachedDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "detached_audio");
            Directory.CreateDirectory(detachedDir);
            var outputPath = Path.Combine(detachedDir,
                $"{Path.GetFileNameWithoutExtension(parent.SourceFile)}_{Guid.NewGuid():N}.m4a");

            await _ff.ExtractAudioAsync(parent.SourceFile, outputPath, parent.OriginalDuration, prog);
            var (w, h, d) = await _ff.ProbeAsync(outputPath);

            // Create a new audio-only clip with the same in/out points as the parent.
            var audioClip = new VideoClip
            {
                SourceFile = outputPath,
                OriginalDuration = d > 0 ? d : parent.OriginalDuration,
                InPoint = parent.InPoint,
                OutPoint = Math.Min(parent.OutPoint, d > 0 ? d : parent.OutPoint),
                Speed = parent.Speed,
                Volume = parent.Volume,
                VideoWidth = 0, VideoHeight = 0,
                AccentColor = System.Windows.Media.Color.FromRgb(0x6E, 0x44, 0xD6),
                IsAudioOnly = true,
                TimelineStart = parent.TimelineStart
            };
            timeline.Clips.Add(audioClip);

            progress.Value = 1;
            status.Text = $"✓ Audio detached as independent clip. Drag the purple bar in A1, trim its edges, split with S, copy with Ctrl+C, delete with Backspace.";
            // Select the new audio clip so the user can immediately work with it.
            timeline.SelectAudio(audioClip);
        }
        catch (Exception ex)
        {
            status.Text = "Detach failed: " + ex.Message;
            MessageBox.Show("Failed to detach audio:\n" + ex.Message, "Detach Audio");
        }
    }

    private void HandleCopy()
    {
        if (_selectedAudio != null)
        {
            _clipboardAudioVolume = _selectedAudio.Volume;
            _clipboardClip = null;
            _clipboardBlock = null;
            status.Text = $"Copied audio (volume {(int)(_selectedAudio.Volume * 100)}%). Select another clip's audio and Ctrl+V to paste.";
        }
        else if (_selectedClip != null)
        {
            _clipboardClip = _selectedClip;
            _clipboardBlock = null;
            _clipboardAudioVolume = null;
            status.Text = "Copied clip: " + _selectedClip.DisplayName;
        }
        else if (_selectedBlock != null)
        {
            _clipboardBlock = _selectedBlock;
            _clipboardClip = null;
            _clipboardAudioVolume = null;
            status.Text = "Copied block: " + _selectedBlock.Label;
        }
        else
        {
            status.Text = "Nothing selected to copy. Click a clip / audio bar / block first.";
        }
    }

    private void HandlePaste()
    {
        if (_clipboardAudioVolume.HasValue && _selectedAudio != null)
        {
            _selectedAudio.Volume = _clipboardAudioVolume.Value;
            status.Text = $"Pasted audio volume ({(int)(_clipboardAudioVolume.Value * 100)}%) to {_selectedAudio.DisplayName}";
            return;
        }
        if (_clipboardClip != null)
        {
            var c = _clipboardClip;
            var newClip = new VideoClip
            {
                SourceFile = c.SourceFile,
                OriginalDuration = c.OriginalDuration,
                InPoint = c.InPoint,
                OutPoint = c.OutPoint,
                Speed = c.Speed,
                Volume = c.Volume,
                RotateDegrees = c.RotateDegrees,
                FlipH = c.FlipH,
                FlipV = c.FlipV,
                VideoWidth = c.VideoWidth,
                VideoHeight = c.VideoHeight,
                AccentColor = c.IsAudioOnly ? c.AccentColor : VideoClip.NextColor(),
                LoopCount = c.LoopCount,
                IsAudioOnly = c.IsAudioOnly
            };
            // Insert right after the source clip (audio-only: free position; video: ripple-abut)
            timeline.ReorderClipToPosition(newClip, c.TimelineStart + c.EffectiveDuration + 0.001);
            status.Text = c.IsAudioOnly ? "Pasted audio clip." : "Pasted clip after original.";
        }
        else if (_clipboardBlock != null)
        {
            var b = _clipboardBlock;
            var newBlock = new VideoBlock
            {
                X = Math.Min(overlayCanvas.ActualWidth - b.Width - 1, b.X + 20),
                Y = Math.Min(overlayCanvas.ActualHeight - b.Height - 1, b.Y + 20),
                Width = b.Width,
                Height = b.Height,
                StartSeconds = b.StartSeconds,
                EndSeconds = b.EndSeconds,
                CoversWholeVideo = b.CoversWholeVideo,
                Color = b.Color,
                Mode = b.Mode,
                BlurStrength = b.BlurStrength,
                Label = $"Block {timeline.Blocks.Count + 1}"
            };
            timeline.Blocks.Add(newBlock);
            var ctl = new ResizableBlock(newBlock);
            ctl.Selected += rb => SelectBlock(rb.Model);
            ctl.Changed += _ => SyncBlockInspector();
            overlayCanvas.Children.Add(ctl);
            _blockControls[newBlock] = ctl;
            SelectBlock(newBlock);
            status.Text = "Pasted block.";
        }
        else
        {
            status.Text = "Clipboard is empty - copy something first with Ctrl+C.";
        }
    }

    private void UpdateBlockVisibility()
    {
        var t = timeline.CurrentSeconds;
        foreach (var kv in _blockControls)
        {
            var block = kv.Key;
            var ctl = kv.Value;
            bool shouldShow;
            if (!_isPlaying)
            {
                // When paused, always show blocks so user can edit/position them
                shouldShow = true;
            }
            else
            {
                // When playing, only show blocks within their active time range (matches export)
                shouldShow = block.CoversWholeVideo || (t >= block.StartSeconds && t <= block.EndSeconds);
            }
            ctl.Visibility = shouldShow ? Visibility.Visible : Visibility.Hidden;
        }
    }

    private void Split_Click(object sender, RoutedEventArgs e)
    {
        var clip = _selectedClip ?? timeline.GetClipAt(timeline.CurrentSeconds);
        if (clip == null) { status.Text = "Move playhead onto a clip first to split."; return; }
        if (timeline.CurrentSeconds <= clip.TimelineStart + 0.05 ||
            timeline.CurrentSeconds >= clip.TimelineStart + clip.EffectiveDuration - 0.05)
        {
            status.Text = "Move the red playhead INSIDE the clip first, then press Split.";
            return;
        }
        SplitAtPlayhead(clip);
        status.Text = "Clip split at playhead.";
    }

    private void SplitN_Click(object sender, RoutedEventArgs e)
    {
        var clip = _selectedClip ?? timeline.GetClipAt(timeline.CurrentSeconds);
        if (clip == null) { status.Text = "Select a clip first."; return; }
        var dlg = new SplitNWindow() { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            SplitIntoNParts(clip, dlg.Parts);
            status.Text = $"Clip split into {dlg.Parts} parts.";
        }
    }

    private void SplitAtPlayhead(VideoClip clip)
    {
        var withinClip = (timeline.CurrentSeconds - clip.TimelineStart) * clip.Speed;
        var splitAt = clip.InPoint + withinClip;
        SplitClipAtSourceTime(clip, splitAt);
    }

    private void SplitClipAtSourceTime(VideoClip clip, double splitAt)
    {
        if (splitAt <= clip.InPoint + 0.1 || splitAt >= clip.OutPoint - 0.1)
        {
            status.Text = "Split point too close to clip edge - try further inside.";
            return;
        }
        // When the clip is looped, both halves should also loop the same number of times so
        // the total timeline length stays consistent (the previous code only kept LoopCount on
        // the left, producing a left half that loops and a right half that doesn't).
        var loopCount = Math.Max(1, clip.LoopCount);
        var leftDur = (splitAt - clip.InPoint) / Math.Max(0.01, clip.Speed) * loopCount;
        var newClip = new VideoClip
        {
            SourceFile = clip.SourceFile,
            OriginalDuration = clip.OriginalDuration,
            InPoint = splitAt,
            OutPoint = clip.OutPoint,
            Speed = clip.Speed,
            Volume = clip.Volume,
            RotateDegrees = clip.RotateDegrees,
            FlipH = clip.FlipH,
            FlipV = clip.FlipV,
            VideoWidth = clip.VideoWidth,
            VideoHeight = clip.VideoHeight,
            AccentColor = clip.IsAudioOnly ? clip.AccentColor : VideoClip.NextColor(),
            LoopCount = loopCount,
            IsAudioOnly = clip.IsAudioOnly,  // Preserve type when splitting audio-only clips
            TimelineStart = clip.TimelineStart + leftDur
        };
        clip.OutPoint = splitAt;
        timeline.Clips.Add(newClip);
    }

    private void SplitIntoNParts(VideoClip clip, int n)
    {
        if (n < 2) return;
        var totalSrc = clip.OutPoint - clip.InPoint;
        var partSrc = totalSrc / n;
        var origIn = clip.InPoint;
        var origOut = clip.OutPoint;
        var origTLStart = clip.TimelineStart;
        var origSpeed = Math.Max(0.01, clip.Speed);
        var partTL = partSrc / origSpeed;

        // Shrink the original to be the first part
        clip.OutPoint = origIn + partSrc;

        // Add N-1 new parts
        var newClips = new List<VideoClip>();
        for (int i = 1; i < n; i++)
        {
            var newIn = origIn + partSrc * i;
            var newOut = (i == n - 1) ? origOut : (origIn + partSrc * (i + 1));
            var nc = new VideoClip
            {
                SourceFile = clip.SourceFile,
                OriginalDuration = clip.OriginalDuration,
                InPoint = newIn,
                OutPoint = newOut,
                Speed = origSpeed,
                Volume = clip.Volume,
                RotateDegrees = clip.RotateDegrees,
                FlipH = clip.FlipH,
                FlipV = clip.FlipV,
                VideoWidth = clip.VideoWidth,
                VideoHeight = clip.VideoHeight,
                AccentColor = VideoClip.NextColor(),
                LoopCount = 1,
                TimelineStart = origTLStart + partTL * i
            };
            newClips.Add(nc);
        }
        foreach (var nc in newClips) timeline.Clips.Add(nc);
    }

    private void DuplicateClip(VideoClip clip)
    {
        var d = new VideoClip
        {
            SourceFile = clip.SourceFile,
            OriginalDuration = clip.OriginalDuration,
            InPoint = clip.InPoint,
            OutPoint = clip.OutPoint,
            Speed = clip.Speed,
            Volume = clip.Volume,
            RotateDegrees = clip.RotateDegrees,
            FlipH = clip.FlipH,
            FlipV = clip.FlipV,
            VideoWidth = clip.VideoWidth,
            VideoHeight = clip.VideoHeight,
            AccentColor = clip.IsAudioOnly ? clip.AccentColor : VideoClip.NextColor(),
            LoopCount = clip.LoopCount,
            IsAudioOnly = clip.IsAudioOnly,
            TimelineStart = clip.TimelineStart + clip.EffectiveDuration
        };
        timeline.Clips.Add(d);
    }

    // ===== Export =====

    private async void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (timeline.Clips.Count == 0) { MessageBox.Show("Add at least one video clip."); return; }
        var ext = AppSettings.ExportContainer;
        var sfd = new SaveFileDialog
        {
            FileName = "project_export." + ext,
            DefaultExt = ext,
            Filter = ext switch
            {
                "mov" => "MOV|*.mov|MP4|*.mp4|MKV|*.mkv|WebM|*.webm",
                "mkv" => "MKV|*.mkv|MP4|*.mp4|MOV|*.mov|WebM|*.webm",
                "webm" => "WebM|*.webm|MP4|*.mp4|MOV|*.mov|MKV|*.mkv",
                _ => "MP4|*.mp4|MOV|*.mov|MKV|*.mkv|WebM|*.webm"
            }
        };
        if (sfd.ShowDialog() != true) return;

        status.Text = "Exporting...";
        progress.Value = 0;
        var prog = new Progress<double>(v => Dispatcher.Invoke(() => progress.Value = v));
        try
        {
            // Export in timeline order
            var orderedClips = timeline.Clips.OrderBy(c => c.TimelineStart).ToList();
            var first = orderedClips[0];
            await _ff.ExportProjectAsync(orderedClips, timeline.Blocks.ToList(),
                first.VideoWidth, first.VideoHeight,
                overlayCanvas.ActualWidth, overlayCanvas.ActualHeight,
                timeline.TotalSeconds, sfd.FileName, prog);
            status.Text = "Exported: " + sfd.FileName;
            progress.Value = 1;
            if (MessageBox.Show("Open output folder?", "Done", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                RevealInExplorer(sfd.FileName);
        }
        catch (Exception ex)
        {
            status.Text = "Export failed: " + ex.Message;
            MessageBox.Show(ex.Message, "Export Error");
        }
    }

    // ===== Quick Tool buttons (operate on selected clip; default to first if none) =====

    private VideoClip? CurrentClip() => _selectedClip ?? _playingClip ?? (timeline.Clips.Count > 0 ? timeline.Clips[0] : null);

    private void ScreenRec_Click(object s, RoutedEventArgs e) => new ScreenRecorderWindow(_ff) { Owner = this }.ShowDialog();
    private void Tts_Click(object s, RoutedEventArgs e) => new TextToSpeechWindow() { Owner = this }.ShowDialog();
    private async void Merge_Click(object s, RoutedEventArgs e)
    {
        if (timeline.Clips.Count < 2) { MessageBox.Show("Need at least 2 clips."); return; }
        SaveBtn_Click(s, e);
        await System.Threading.Tasks.Task.CompletedTask;
    }
    private void Record_Click(object s, RoutedEventArgs e) => new ScreenRecorderWindow(_ff, true) { Owner = this }.ShowDialog();

    private void Settings_Click(object s, RoutedEventArgs e) => new SettingsWindow() { Owner = this }.ShowDialog();

    private void ApplyLanguage()
    {
        var lang = AppSettings.Language;
        bool isHe = lang == "he"
            || (lang == "auto" && System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "he");
        FlowDirection = isHe ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight;
        if (isHe)
        {
            // Translate top-bar text labels and a few key strings
            openBtn.ToolTip = "פתח קבצי וידאו";
            saveBtn.ToolTip = "ייצוא הפרויקט";
            settingsBtn.ToolTip = "הגדרות · ,";
            helpBtn.ToolTip = "מדריך למשתמש · ?";
            status.Text = "מוכן · גרור קבצי וידאו כדי להוסיף";
        }
        else
        {
            openBtn.ToolTip = "Open video files";
            saveBtn.ToolTip = "Export project";
            settingsBtn.ToolTip = "Settings · ,";
            helpBtn.ToolTip = "User Guide · ?";
            // Don't overwrite the status if there's already a message.
        }
    }
    private void Help_Click(object s, RoutedEventArgs e) => new UserGuideWindow() { Owner = this }.ShowDialog();

    private async void DownloadUrl_Click(object s, RoutedEventArgs e)
    {
        var defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "VideoEditorDownloads");
        var dlg = new DownloadUrlWindow(defaultFolder) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var url = dlg.Url;
        var folder = dlg.OutputFolder;
        bool useStreaming = dlg.UseStreamingDownloader;
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Cannot create destination folder: " + ex.Message);
            return;
        }

        var dl = new DownloadService();
        dl.Log += msg => Dispatcher.Invoke(() => status.Text = msg.Length > 80 ? msg[..80] + "…" : msg);
        var prog = new Progress<double>(v => Dispatcher.Invoke(() => progress.Value = v));

        status.Text = "Starting download...";
        progress.Value = 0;
        try
        {
            string finalPath;
            if (useStreaming)
            {
                finalPath = await dl.DownloadViaYtDlpAsync(url, folder, App.FFmpegPath, prog);
            }
            else
            {
                var uri = new Uri(url);
                var fileName = Path.GetFileName(uri.LocalPath);
                if (string.IsNullOrWhiteSpace(fileName) || !fileName.Contains('.')) fileName = "downloaded.mp4";
                var outputPath = Path.Combine(folder, fileName);
                finalPath = await dl.DownloadDirectAsync(url, outputPath, prog);
            }
            status.Text = $"Downloaded → adding to timeline: {Path.GetFileName(finalPath)}";
            await AddClipAsync(finalPath);
            progress.Value = 1;
            status.Text = $"Added downloaded clip: {Path.GetFileName(finalPath)}";
        }
        catch (Exception ex)
        {
            status.Text = "Download failed: " + ex.Message;
            MessageBox.Show("Failed to download:\n\n" + ex.Message, "Download Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Trim_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip(); if (c == null) return;
        var dlg = new TrimWindow(c.OriginalDuration) { Owner = this };
        if (dlg.ShowDialog() == true) { c.InPoint = dlg.StartSec; c.OutPoint = dlg.EndSec; SelectClip(c); }
    }
    private void Speed_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip(); if (c == null) return;
        var dlg = new SpeedWindow() { Owner = this };
        if (dlg.ShowDialog() == true) { c.Speed = dlg.Speed; SelectClip(c); }
    }
    private void Volume_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip(); if (c == null) return;
        var dlg = new VolumeWindow() { Owner = this };
        if (dlg.ShowDialog() == true) { c.Volume = dlg.Volume; SelectClip(c); }
    }
    private void Rotate_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip(); if (c == null) return;
        var dlg = new RotateWindow() { Owner = this };
        if (dlg.ShowDialog() == true) { c.RotateDegrees = dlg.Degrees; ApplyClipTransform(c); }
    }
    private void Flip_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip(); if (c == null) return;
        var dlg = new FlipWindow() { Owner = this };
        if (dlg.ShowDialog() == true) { if (dlg.Horizontal) c.FlipH = !c.FlipH; else c.FlipV = !c.FlipV; ApplyClipTransform(c); }
    }
    private void Loop_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip(); if (c == null) return;
        var dlg = new LoopWindow() { Owner = this };
        if (dlg.ShowDialog() == true) { c.LoopCount = dlg.Times; status.Text = $"Clip will loop {c.LoopCount}x on export"; }
    }
    private async void Crop_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip();
        if (c == null) { MessageBox.Show("Select a clip first."); return; }

        // Extract a preview frame so the user can see what they're cropping
        var tempFrame = Path.Combine(Path.GetTempPath(), $"crop_{Guid.NewGuid():N}.jpg");
        try
        {
            double frameTime;
            if (timeline.CurrentSeconds >= c.TimelineStart && timeline.CurrentSeconds <= c.TimelineStart + c.EffectiveDuration)
            {
                var withinClip = (timeline.CurrentSeconds - c.TimelineStart) * c.Speed;
                frameTime = c.InPoint + withinClip;
            }
            else
            {
                frameTime = c.InPoint + (c.OutPoint - c.InPoint) / 2;
            }
            status.Text = "Extracting preview frame...";
            await _ff.ExtractFrameAsync(c.SourceFile, tempFrame, frameTime);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to extract preview: " + ex.Message);
            return;
        }

        var picker = new VisualRegionPickerWindow(tempFrame,
            c.VideoWidth > 0 ? c.VideoWidth : 1920,
            c.VideoHeight > 0 ? c.VideoHeight : 1080,
            "Crop Video - Mark Area to Keep",
            "Drag the green rectangle to mark the area you want to KEEP. The darkened area outside will be cropped out. Drag corners to resize.",
            darkenOutside: true,
            selectionLabel: "✄ KEEP",
            accentColor: System.Windows.Media.Color.FromRgb(0x4D, 0xFF, 0x88))
        { Owner = this };

        var ok = picker.ShowDialog() == true;
        try { File.Delete(tempFrame); } catch { }
        if (!ok) { status.Text = "Crop cancelled."; return; }

        await ApplyDestructiveOpAsync(c, async (input, output, prog) =>
            await _ff.CropAsync(input, output, picker.X, picker.Y, picker.W, picker.H, c.OriginalDuration, prog));
        status.Text = "Clip cropped.";
    }
    private async void Resize_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip(); if (c == null) return;
        var dlg = new ResizeWindow(c.VideoWidth, c.VideoHeight) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        await ApplyDestructiveOpAsync(c, async (input, output, prog) =>
            await _ff.ResizeAsync(input, output, dlg.W, dlg.H, c.OriginalDuration, prog));
    }
    private async void Stabilize_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip(); if (c == null) return;
        if (MessageBox.Show("Run 2-pass stabilization on this clip? Replaces source.", "Stabilize", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        await ApplyDestructiveOpAsync(c, async (input, output, prog) =>
            await _ff.StabilizeAsync(input, output, c.OriginalDuration, prog));
    }
    private async void AddImage_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip(); if (c == null) return;
        var dlg = new AddImageWindow() { Owner = this };
        if (dlg.ShowDialog() != true) return;
        await ApplyDestructiveOpAsync(c, async (input, output, prog) =>
            await _ff.AddImageAsync(input, dlg.ImageFile, output, dlg.X, dlg.Y, c.OriginalDuration, prog));
    }
    private async void AddText_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip(); if (c == null) return;
        var dlg = new AddTextWindow() { Owner = this };
        if (dlg.ShowDialog() != true) return;
        await ApplyDestructiveOpAsync(c, async (input, output, prog) =>
            await _ff.AddTextAsync(input, output, dlg.TextValue, dlg.X, dlg.Y, dlg.FontSize, dlg.ColorHex, c.OriginalDuration, prog));
    }
    private async void RemoveLogo_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip();
        if (c == null) { MessageBox.Show("Select a clip first."); return; }

        // Extract a preview frame so the user can see what they're selecting
        var tempFrame = Path.Combine(Path.GetTempPath(), $"removelogo_{Guid.NewGuid():N}.jpg");
        try
        {
            double frameTime;
            if (timeline.CurrentSeconds >= c.TimelineStart && timeline.CurrentSeconds <= c.TimelineStart + c.EffectiveDuration)
            {
                var withinClip = (timeline.CurrentSeconds - c.TimelineStart) * c.Speed;
                frameTime = c.InPoint + withinClip;
            }
            else
            {
                frameTime = c.InPoint + (c.OutPoint - c.InPoint) / 2;
            }
            status.Text = "Extracting preview frame...";
            await _ff.ExtractFrameAsync(c.SourceFile, tempFrame, frameTime);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to extract preview frame: " + ex.Message);
            return;
        }

        var picker = new VisualRegionPickerWindow(tempFrame,
            c.VideoWidth > 0 ? c.VideoWidth : 1920,
            c.VideoHeight > 0 ? c.VideoHeight : 1080,
            "Remove Logo - Mark the Logo Area",
            "Drag the yellow rectangle over the logo, and resize it using the corners. The marked area will be blurred out across the entire clip.")
        { Owner = this };

        var ok = picker.ShowDialog() == true;
        try { File.Delete(tempFrame); } catch { }
        if (!ok) { status.Text = "Remove Logo cancelled."; return; }

        await ApplyDestructiveOpAsync(c, async (input, output, prog) =>
            await _ff.RemoveLogoAsync(input, output, picker.X, picker.Y, picker.W, picker.H, c.OriginalDuration, prog));
    }
    private async void AddAudio_Click(object s, RoutedEventArgs e)
    {
        var c = CurrentClip(); if (c == null) return;
        var dlg = new AddAudioWindow() { Owner = this };
        if (dlg.ShowDialog() != true) return;
        await ApplyDestructiveOpAsync(c, async (input, output, prog) =>
            await _ff.AddAudioAsync(input, dlg.AudioFile, output, c.OriginalDuration, prog));
    }

    // ===== Audio operations =====

    private async void ExtractAudioClip_Click(object sender, RoutedEventArgs e)
    {
        var c = CurrentClip();
        if (c == null) { status.Text = "Select a clip first."; return; }
        var sfd = new SaveFileDialog
        {
            FileName = Path.GetFileNameWithoutExtension(c.SourceFile) + "_audio.mp3",
            Filter = "MP3|*.mp3|WAV|*.wav|M4A|*.m4a|AAC|*.aac|OGG|*.ogg|FLAC|*.flac"
        };
        if (sfd.ShowDialog() != true) return;
        var fmt = Path.GetExtension(sfd.FileName).TrimStart('.').ToLowerInvariant();
        var dur = c.OutPoint - c.InPoint;
        status.Text = "Extracting audio...";
        progress.Value = 0;
        var prog = new Progress<double>(v => Dispatcher.Invoke(() => progress.Value = v));
        try
        {
            await _ff.ExtractAudioAsync(c.SourceFile, sfd.FileName, c.InPoint, dur, fmt, prog);
            status.Text = "Audio saved: " + sfd.FileName;
            progress.Value = 1;
            if (MessageBox.Show("Open output folder?", "Done", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                RevealInExplorer(sfd.FileName);
        }
        catch (Exception ex)
        {
            status.Text = "Extract failed: " + ex.Message;
            MessageBox.Show(ex.Message, "Error");
        }
    }

    private async void ExtractAudioProject_Click(object sender, RoutedEventArgs e)
    {
        if (timeline.Clips.Count == 0) { MessageBox.Show("Add clips first."); return; }
        var sfd = new SaveFileDialog
        {
            FileName = "project_audio.mp3",
            Filter = "MP3|*.mp3|WAV|*.wav|M4A|*.m4a|AAC|*.aac|OGG|*.ogg|FLAC|*.flac"
        };
        if (sfd.ShowDialog() != true) return;

        var tempVideo = Path.Combine(Path.GetTempPath(), $"ve_audio_export_{Guid.NewGuid():N}.mp4");
        var fmt = Path.GetExtension(sfd.FileName).TrimStart('.').ToLowerInvariant();
        status.Text = "Rendering project for audio extraction...";
        progress.Value = 0;
        try
        {
            var orderedClips = timeline.Clips.OrderBy(x => x.TimelineStart).ToList();
            var first = orderedClips[0];
            var prog1 = new Progress<double>(v => Dispatcher.Invoke(() => progress.Value = v * 0.8));
            await _ff.ExportProjectAsync(orderedClips, new List<VideoBlock>(),
                first.VideoWidth > 0 ? first.VideoWidth : 1920,
                first.VideoHeight > 0 ? first.VideoHeight : 1080,
                overlayCanvas.ActualWidth, overlayCanvas.ActualHeight,
                timeline.TotalSeconds, tempVideo, prog1);

            status.Text = "Extracting audio...";
            var prog2 = new Progress<double>(v => Dispatcher.Invoke(() => progress.Value = 0.8 + v * 0.2));
            await _ff.ExtractAudioAsync(tempVideo, sfd.FileName, 0, timeline.TotalSeconds, fmt, prog2);

            status.Text = "Audio saved: " + sfd.FileName;
            progress.Value = 1;
            if (MessageBox.Show("Open output folder?", "Done", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                RevealInExplorer(sfd.FileName);
        }
        catch (Exception ex)
        {
            status.Text = "Audio export failed: " + ex.Message;
            MessageBox.Show(ex.Message, "Error");
        }
        finally
        {
            try { File.Delete(tempVideo); } catch { }
        }
    }

    private void MuteClip_Click(object sender, RoutedEventArgs e)
    {
        var c = CurrentClip();
        if (c == null) { status.Text = "Select a clip first."; return; }
        c.Volume = 0;
        SelectClip(c);
        status.Text = "Clip muted (volume = 0). Use Restore Audio to undo.";
    }

    private void UnmuteClip_Click(object sender, RoutedEventArgs e)
    {
        var c = CurrentClip();
        if (c == null) return;
        c.Volume = 1.0;
        SelectClip(c);
        status.Text = "Clip audio restored (volume = 100%).";
    }

    private async void RemoveAudioTrack_Click(object sender, RoutedEventArgs e)
    {
        var c = CurrentClip();
        if (c == null) { status.Text = "Select a clip first."; return; }
        if (MessageBox.Show(
            "Permanently strip the audio track from this clip? The clip's source file will be replaced with a silent version (the original file on disk is not modified).",
            "Remove Audio Track", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

        await ApplyDestructiveOpAsync(c, async (input, output, prog) =>
            await _ff.RemoveAudioTrackAsync(input, output, c.OriginalDuration, prog));
        status.Text = "Audio track removed.";
    }

    private async System.Threading.Tasks.Task ApplyDestructiveOpAsync(VideoClip clip, Func<string, string, IProgress<double>, System.Threading.Tasks.Task> op)
    {
        if (AppSettings.ConfirmDestructive)
        {
            var ok = MessageBox.Show(
                "This operation re-encodes the clip and replaces its source file. Continue?",
                "Confirm destructive operation",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (ok != MessageBoxResult.OK) return;
        }
        var tempOut = Path.Combine(Path.GetTempPath(), $"ve_{Guid.NewGuid()}.mp4");
        status.Text = "Processing clip...";
        progress.Value = 0;
        var prog = new Progress<double>(v => Dispatcher.Invoke(() => progress.Value = v));
        var previousSource = clip.SourceFile;
        bool succeeded = false;
        try
        {
            await op(previousSource, tempOut, prog);
            var (w, h, d) = await _ff.ProbeAsync(tempOut);
            // Release the previous file from the MediaElement before we swap and try to delete it.
            if (_playingClip == clip) { try { videoView.Stop(); videoView.Close(); } catch { } _playingClip = null; }
            clip.SourceFile = tempOut;
            clip.VideoWidth = w; clip.VideoHeight = h;
            clip.OriginalDuration = d;
            if (clip.OutPoint > d) clip.OutPoint = d;
            if (clip.InPoint >= d) clip.InPoint = 0;
            status.Text = "Clip updated.";
            LoadClipForPreview(clip, 0);
            SelectClip(clip);
            succeeded = true;
        }
        catch (Exception ex)
        {
            status.Text = "Op failed: " + ex.Message;
            MessageBox.Show(ex.Message, "Error");
        }
        finally
        {
            if (succeeded)
            {
                // Replace the prior temp output (if any) — first destructive op uses the user's
                // original file (don't touch), subsequent ones supersede our own temp files.
                if (IsAppTempFile(previousSource))
                {
                    try { File.Delete(previousSource); } catch { }
                }
            }
            else
            {
                // Operation failed — clean up the half-written temp if it exists.
                try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
            }
        }
    }

    // Use the ProcessStartInfo argument list so the path is quoted by the runtime — avoids
    // breaking when the file name contains characters that interact with the shell parser.
    private static void RevealInExplorer(string filePath)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = true
            };
            psi.ArgumentList.Add("/select,");
            psi.ArgumentList.Add(filePath);
            System.Diagnostics.Process.Start(psi);
        }
        catch { }
    }

    private static bool IsAppTempFile(string path)
    {
        try
        {
            var tempDir = Path.GetFullPath(Path.GetTempPath());
            var full = Path.GetFullPath(path);
            var name = Path.GetFileName(full);
            return full.StartsWith(tempDir, StringComparison.OrdinalIgnoreCase)
                   && name.StartsWith("ve_", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
