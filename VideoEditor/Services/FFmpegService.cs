using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VideoEditor.Models;

namespace VideoEditor.Services;

public class FFmpegService
{
    public string FFmpegExe => Path.Combine(App.FFmpegPath, "ffmpeg.exe");
    public string FFprobeExe => Path.Combine(App.FFmpegPath, "ffprobe.exe");

    public event Action<string>? Log;
    public event Action<double>? Progress;

    public async Task<(int width, int height, double durationSeconds)> ProbeAsync(string input)
    {
        var args = $"-v error -select_streams v:0 -show_entries stream=width,height -show_entries format=duration -of default=nw=1 \"{input}\"";
        var output = await RunAndCaptureAsync(FFprobeExe, args);
        int w = 0, h = 0;
        double duration = 0;
        foreach (var line in output.Split('\n'))
        {
            var l = line.Trim();
            if (l.StartsWith("width=")) int.TryParse(l[6..], out w);
            else if (l.StartsWith("height=")) int.TryParse(l[7..], out h);
            else if (l.StartsWith("duration=")) double.TryParse(l[9..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out duration);
        }
        return (w, h, duration);
    }

    public Task TrimAsync(string input, string output, TimeSpan start, TimeSpan end, IProgress<double>? progress = null)
    {
        var dur = end - start;
        var args = $"-y -ss {start.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} -i \"{input}\" -t {dur.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} -c copy \"{output}\"";
        return RunAsync(args, dur.TotalSeconds, progress);
    }

    public async Task MergeAsync(IEnumerable<string> inputs, string output, IProgress<double>? progress = null)
    {
        var list = Path.Combine(Path.GetTempPath(), $"merge_{Guid.NewGuid()}.txt");
        var sb = new StringBuilder();
        foreach (var f in inputs) sb.AppendLine($"file '{EscapeConcatPath(f)}'");
        File.WriteAllText(list, sb.ToString());
        try
        {
            var args = $"-y -f concat -safe 0 -i \"{list}\" -c copy \"{output}\"";
            await RunAsync(args, 0, progress);
        }
        finally
        {
            try { File.Delete(list); } catch { }
        }
    }

    public Task CropAsync(string input, string output, int x, int y, int w, int h, double duration, IProgress<double>? progress = null)
    {
        var args = $"-y -i \"{input}\" -vf \"crop={w}:{h}:{x}:{y}\" -c:a copy \"{output}\"";
        return RunAsync(args, duration, progress);
    }

    public Task ResizeAsync(string input, string output, int w, int h, double duration, IProgress<double>? progress = null)
    {
        var args = $"-y -i \"{input}\" -vf \"scale={w}:{h}\" -c:a copy \"{output}\"";
        return RunAsync(args, duration, progress);
    }

    public Task RotateAsync(string input, string output, int degrees, double duration, IProgress<double>? progress = null)
    {
        string filter = degrees switch
        {
            90 => "transpose=1",
            180 => "transpose=1,transpose=1",
            270 => "transpose=2",
            _ => $"rotate={degrees}*PI/180"
        };
        var args = $"-y -i \"{input}\" -vf \"{filter}\" -c:a copy \"{output}\"";
        return RunAsync(args, duration, progress);
    }

    public Task FlipAsync(string input, string output, bool horizontal, double duration, IProgress<double>? progress = null)
    {
        var filter = horizontal ? "hflip" : "vflip";
        var args = $"-y -i \"{input}\" -vf \"{filter}\" -c:a copy \"{output}\"";
        return RunAsync(args, duration, progress);
    }

    public Task SpeedAsync(string input, string output, double speed, double duration, IProgress<double>? progress = null)
    {
        // Guard against div/0 and absurdly slow values that would produce Infinity in the filter.
        var safeSpeed = Math.Max(0.01, speed);
        var atempo = BuildAtempoFilter(safeSpeed);
        var args = $"-y -i \"{input}\" -filter_complex \"[0:v]setpts={(1.0/safeSpeed).ToString(System.Globalization.CultureInfo.InvariantCulture)}*PTS[v];[0:a]{atempo}[a]\" -map \"[v]\" -map \"[a]\" \"{output}\"";
        return RunAsync(args, duration / safeSpeed, progress);
    }

    private static string BuildAtempoFilter(double speed)
    {
        if (speed >= 0.5 && speed <= 2.0) return $"atempo={speed.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var parts = new List<string>();
        double remaining = speed;
        while (remaining > 2.0) { parts.Add("atempo=2.0"); remaining /= 2.0; }
        while (remaining < 0.5) { parts.Add("atempo=0.5"); remaining /= 0.5; }
        parts.Add($"atempo={remaining.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        return string.Join(",", parts);
    }

    public Task VolumeAsync(string input, string output, double volume, double duration, IProgress<double>? progress = null)
    {
        var args = $"-y -i \"{input}\" -filter:a \"volume={volume.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" -c:v copy \"{output}\"";
        return RunAsync(args, duration, progress);
    }

    public async Task LoopAsync(string input, string output, int times, double duration, IProgress<double>? progress = null)
    {
        var list = Path.Combine(Path.GetTempPath(), $"loop_{Guid.NewGuid()}.txt");
        var sb = new StringBuilder();
        var escaped = EscapeConcatPath(input);
        for (int i = 0; i < times; i++) sb.AppendLine($"file '{escaped}'");
        File.WriteAllText(list, sb.ToString());
        try
        {
            var args = $"-y -f concat -safe 0 -i \"{list}\" -c copy \"{output}\"";
            await RunAsync(args, duration * times, progress);
        }
        finally
        {
            try { File.Delete(list); } catch { }
        }
    }

    // ffmpeg's concat demuxer treats both ' (string delimiter) and \ (escape char) specially
    // inside file '...'. Escape \ first, then '.
    private static string EscapeConcatPath(string path)
        => path.Replace("\\", "\\\\").Replace("'", "'\\''");

    public Task StabilizeAsync(string input, string output, double duration, IProgress<double>? progress = null)
    {
        var trf = Path.Combine(Path.GetTempPath(), $"trf_{Guid.NewGuid()}.trf");
        var pass1 = $"-y -i \"{input}\" -vf \"vidstabdetect=result={trf}\" -f null -";
        var pass2 = $"-y -i \"{input}\" -vf \"vidstabtransform=input={trf}:smoothing=30\" -c:a copy \"{output}\"";
        return Task.Run(async () =>
        {
            try { await RunAsync(pass1, duration, null); } catch { }
            await RunAsync(pass2, duration, progress);
            try { File.Delete(trf); } catch { }
        });
    }

    public Task AddAudioAsync(string videoIn, string audioIn, string output, double duration, IProgress<double>? progress = null)
    {
        var args = $"-y -i \"{videoIn}\" -i \"{audioIn}\" -c:v copy -map 0:v:0 -map 1:a:0 -shortest \"{output}\"";
        return RunAsync(args, duration, progress);
    }

    public Task ExtractAudioAsync(string input, string output, double startSec, double durSec, string format, IProgress<double>? progress = null)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string codec = format.ToLowerInvariant() switch
        {
            "mp3" => "-codec:a libmp3lame -qscale:a 2",
            "wav" => "-codec:a pcm_s16le",
            "aac" or "m4a" => "-codec:a aac -b:a 192k",
            "ogg" => "-codec:a libvorbis -qscale:a 5",
            "flac" => "-codec:a flac",
            _ => "-codec:a libmp3lame -qscale:a 2"
        };
        var ssArg = startSec > 0.001 ? $"-ss {startSec.ToString(ci)} " : "";
        var tArg = durSec > 0.001 ? $"-t {durSec.ToString(ci)} " : "";
        var args = $"-y {ssArg}-i \"{input}\" {tArg}-vn {codec} \"{output}\"";
        return RunAsync(args, durSec, progress);
    }

    public Task RemoveAudioTrackAsync(string input, string output, double duration, IProgress<double>? progress = null)
    {
        var args = $"-y -i \"{input}\" -an -c:v copy \"{output}\"";
        return RunAsync(args, duration, progress);
    }

    public Task AddImageAsync(string videoIn, string imageIn, string output, int x, int y, double duration, IProgress<double>? progress = null)
    {
        var args = $"-y -i \"{videoIn}\" -i \"{imageIn}\" -filter_complex \"[0:v][1:v]overlay={x}:{y}\" -c:a copy \"{output}\"";
        return RunAsync(args, duration, progress);
    }

    public Task AddTextAsync(string videoIn, string output, VideoEditor.Views.TextOverlayOptions opt, double duration, IProgress<double>? progress = null)
    {
        var fontFile = ResolveDrawtextFont(opt.Bold, opt.Italic);
        var parts = new List<string>
        {
            "drawtext=text=" + EscapeDrawtextValue(opt.Text ?? "", isText: true),
            "fontfile=" + EscapeDrawtextValue(fontFile.Replace('\\', '/'), isText: false),
            "x=" + opt.X,
            "y=" + opt.Y,
            "fontsize=" + opt.FontSize,
            "fontcolor=" + opt.FontColor
        };
        if (opt.BackgroundEnabled && opt.BackgroundOpacity > 0)
        {
            parts.Add("box=1");
            parts.Add($"boxcolor={opt.BackgroundColor}@{opt.BackgroundOpacity.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}");
            parts.Add("boxborderw=" + opt.BackgroundPadding);
        }
        var args = $"-y -i \"{videoIn}\" -vf \"{string.Join(":", parts)}\" -c:a copy \"{output}\"";
        return RunAsync(args, duration, progress);
    }

    // Escape a value for the drawtext filter (libavfilter level-1 escaping). Backslash first,
    // then the filter separator ':', the quote char ''', and drawtext's expansion-trigger '%'.
    // For the text= arg specifically, real newlines become the literal two-char sequence "\n" so
    // drawtext renders multi-line. Newline escape happens AFTER backslash escape so the
    // backslash we add isn't itself doubled.
    private static string EscapeDrawtextValue(string s, bool isText)
    {
        var t = s ?? "";
        t = t.Replace("\\", "\\\\");
        t = t.Replace(":", "\\:");
        t = t.Replace("'", "\\'");
        if (isText)
        {
            t = t.Replace("%", "\\%");
            t = t.Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");
        }
        return t;
    }

    private static string ResolveDrawtextFont(bool bold, bool italic)
    {
        string winFonts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
        var candidates = (bold, italic) switch
        {
            (true,  true)  => new[] { "segoeuiz.ttf", "arialbi.ttf", "segoeuib.ttf", "arialbd.ttf", "segoeui.ttf", "arial.ttf" },
            (true,  false) => new[] { "segoeuib.ttf", "arialbd.ttf", "segoeui.ttf",  "arial.ttf" },
            (false, true)  => new[] { "segoeuii.ttf", "ariali.ttf",  "segoeui.ttf",  "arial.ttf" },
            _              => new[] { "segoeui.ttf",  "arial.ttf",   "tahoma.ttf" },
        };
        foreach (var name in candidates)
        {
            var p = Path.Combine(winFonts, name);
            if (File.Exists(p)) return p;
        }
        return Path.Combine(winFonts, "arial.ttf");
    }

    public Task RemoveLogoAsync(string videoIn, string output, int x, int y, int w, int h, double duration, IProgress<double>? progress = null)
    {
        var args = $"-y -i \"{videoIn}\" -vf \"delogo=x={x}:y={y}:w={w}:h={h}\" -c:a copy \"{output}\"";
        return RunAsync(args, duration, progress);
    }

    public Task ApplyBlocksAsync(string videoIn, string output, IList<VideoBlock> blocks, int videoW, int videoH, double canvasW, double canvasH, double duration, IProgress<double>? progress = null)
    {
        if (blocks.Count == 0)
        {
            return RunAsync($"-y -i \"{videoIn}\" -c copy \"{output}\"", duration, progress);
        }

        var ci = System.Globalization.CultureInfo.InvariantCulture;
        if (canvasW <= 0 || double.IsNaN(canvasW) || double.IsInfinity(canvasW)) canvasW = videoW;
        if (canvasH <= 0 || double.IsNaN(canvasH) || double.IsInfinity(canvasH)) canvasH = videoH;
        var sx = videoW / canvasW;
        var sy = videoH / canvasH;

        // Pre-process every block once so we don't carry strings of doubles in mixed cultures.
        var processed = new List<(VideoBlock b, int bx, int by, int bw, int bh)>(blocks.Count);
        foreach (var b in blocks)
        {
            int bx = (int)Math.Round(b.X * sx);
            int by = (int)Math.Round(b.Y * sy);
            int bw = (int)Math.Round(b.Width * sx);
            int bh = (int)Math.Round(b.Height * sy);
            bx = Math.Clamp(bx, 0, Math.Max(0, videoW - 2));
            by = Math.Clamp(by, 0, Math.Max(0, videoH - 2));
            bw = Math.Clamp(bw, 2, Math.Max(2, videoW - bx));
            bh = Math.Clamp(bh, 2, Math.Max(2, videoH - by));
            processed.Add((b, bx, by, bw, bh));
        }

        // All solid drawboxes can be joined with commas into a single filter chain.
        var solidParts = new List<string>();
        foreach (var (b, bx, by, bw, bh) in processed)
        {
            if (b.Mode != BlockMode.Solid) continue;
            string colorHex = $"{b.Color.R:X2}{b.Color.G:X2}{b.Color.B:X2}";
            string enable = b.CoversWholeVideo
                ? ""
                : $":enable='between(t,{b.StartSeconds.ToString(ci)},{b.EndSeconds.ToString(ci)})'";
            solidParts.Add($"drawbox=x={bx}:y={by}:w={bw}:h={bh}:color=0x{colorHex}@1.0:t=fill{enable}");
        }
        string solidChain = string.Join(",", solidParts);  // no trailing comma, joined cleanly

        var blurBlocks = processed.Where(p => p.b.Mode == BlockMode.Blur || p.b.Mode == BlockMode.Pixelate).ToList();
        if (blurBlocks.Count == 0)
        {
            var argsSolid = $"-y -i \"{videoIn}\" -vf \"{solidChain}\" -c:a copy \"{output}\"";
            return RunAsync(argsSolid, duration, progress);
        }

        // Build filter_complex. The base stream goes through any solid drawboxes first,
        // then each blur/pixelate block crops + blurs + overlays back. Because each
        // intermediate output pad can only be consumed ONCE in filter_complex, we use
        // `split` to fork the base into (crop_source, overlay_base) for every blur layer.
        var fullFilter = new StringBuilder();
        string prev;
        if (solidChain.Length > 0)
        {
            fullFilter.Append("[0:v]").Append(solidChain).Append("[base];");
            prev = "[base]";
        }
        else
        {
            prev = "[0:v]";
        }

        int idx = 0;
        foreach (var (b, bx, by, bw, bh) in blurBlocks)
        {
            string region = $"[r{idx}]";
            string blurred = $"[bl{idx}]";
            string baseFork = $"[fb{idx}]";
            string cropFork = $"[fc{idx}]";
            string next = $"[o{idx}]";

            // split prev into two copies: one to crop+blur, one as overlay base
            fullFilter.Append($"{prev}split[a{idx}][a{idx}b];");
            // rename pads for clarity in the chain
            fullFilter.Append($"[a{idx}]null{baseFork};");
            fullFilter.Append($"[a{idx}b]null{cropFork};");

            fullFilter.Append($"{cropFork}crop={bw}:{bh}:{bx}:{by}{region};");
            if (b.Mode == BlockMode.Blur)
                fullFilter.Append($"{region}boxblur={b.BlurStrength}:1{blurred};");
            else
                fullFilter.Append($"{region}scale=iw/{Math.Max(2, b.BlurStrength / 4)}:ih/{Math.Max(2, b.BlurStrength / 4)},scale={bw}:{bh}:flags=neighbor{blurred};");

            string enable = b.CoversWholeVideo
                ? ""
                : $":enable='between(t,{b.StartSeconds.ToString(ci)},{b.EndSeconds.ToString(ci)})'";
            fullFilter.Append($"{baseFork}{blurred}overlay={bx}:{by}{enable}{next};");
            prev = next;
            idx++;
        }

        var finalFilter = fullFilter.ToString().TrimEnd(';');
        var args = $"-y -i \"{videoIn}\" -filter_complex \"{finalFilter}\" -map \"{prev}\" -map 0:a? -c:a copy \"{output}\"";
        return RunAsync(args, duration, progress);
    }

    public async Task ExportProjectAsync(IList<VideoClip> clips, IList<VideoBlock> blocks,
        int canvasVideoW, int canvasVideoH, double canvasUiW, double canvasUiH,
        double totalDuration, string output, string fitMode = "contain",
        IList<VideoEditor.Models.TextOverlay>? textOverlays = null,
        IProgress<double>? progress = null)
    {
        if (clips.Count == 0) throw new Exception("No clips.");
        var temps = new List<string>();
        try
        {
            int targetW = canvasVideoW > 0 ? canvasVideoW : 1920;
            int targetH = canvasVideoH > 0 ? canvasVideoH : 1080;
            if (targetW % 2 != 0) targetW++;
            if (targetH % 2 != 0) targetH++;

            var videoClips = clips.Where(c => !c.IsAudioOnly).OrderBy(c => c.TimelineStart).ToList();
            if (videoClips.Count == 0) throw new Exception("No video clips to export.");

            const double gapEpsilon = 0.1;
            var renderQueue = new List<string>();
            double prevEnd = 0;
            for (int i = 0; i < videoClips.Count; i++)
            {
                var c = videoClips[i];
                if (c.TimelineStart > prevEnd + gapEpsilon)
                {
                    var gapDur = c.TimelineStart - prevEnd;
                    var blackTmp = Path.Combine(Path.GetTempPath(), $"ve_black_{Guid.NewGuid():N}.mp4");
                    temps.Add(blackTmp);
                    await RenderBlackClipAsync(blackTmp, gapDur, targetW, targetH);
                    renderQueue.Add(blackTmp);
                }
                var tmp = Path.Combine(Path.GetTempPath(), $"ve_clip_{Guid.NewGuid():N}.mp4");
                temps.Add(tmp);
                // Stream ffmpeg's time= output back as fractional progress so the bar
                // moves smoothly even within a single very long clip. The whole
                // per-clip render covers slot (i / N → (i+1) / N) of the 0..0.7 range.
                int slot = i;
                int nClips = videoClips.Count;
                var clipProgress = new Progress<double>(p =>
                    progress?.Report((slot + Math.Max(0, Math.Min(1, p))) * 0.7 / nClips));
                await RenderClipAsync(c, tmp, targetW, targetH, fitMode, clipProgress);
                renderQueue.Add(tmp);
                prevEnd = c.TimelineStart + c.EffectiveDuration;
                progress?.Report((i + 1) * 0.7 / videoClips.Count);
            }

            string concatList = Path.Combine(Path.GetTempPath(), $"ve_concat_{Guid.NewGuid():N}.txt");
            File.WriteAllText(concatList, string.Join("\n", renderQueue.Select(t => $"file '{EscapeConcatPath(t)}'")));

            var audioOnlyClips = clips
                .Where(c => c.IsAudioOnly)
                .OrderBy(c => c.TimelineStart)
                .ToList();
            bool hasOverlays = textOverlays != null && textOverlays.Count > 0;
            bool hasBlocks = blocks.Count > 0;
            bool hasAudioOnly = audioOnlyClips.Count > 0;
            bool extraStages = hasOverlays || hasBlocks || hasAudioOnly;

            string concatOut = extraStages
                ? Path.Combine(Path.GetTempPath(), $"ve_concat_{Guid.NewGuid():N}.mp4")
                : output;
            if (extraStages) temps.Add(concatOut);

            var concatArgs = $"-y -f concat -safe 0 -i \"{concatList}\" -c copy \"{concatOut}\"";
            await RunAsync(concatArgs, totalDuration, null);
            progress?.Report(0.78);

            try { File.Delete(concatList); } catch { }

            string stageIn = concatOut;

            if (hasOverlays)
            {
                string textOut = hasBlocks
                    ? Path.Combine(Path.GetTempPath(), $"ve_texts_{Guid.NewGuid():N}.mp4")
                    : output;
                if (textOut != output) temps.Add(textOut);

                await BurnTextOverlaysAsync(stageIn, textOut, textOverlays!, totalDuration,
                    new Progress<double>(p => progress?.Report(0.78 + p * 0.12)));
                stageIn = textOut;
                progress?.Report(0.90);
            }

            if (hasBlocks)
            {
                string blockOut = hasAudioOnly
                    ? Path.Combine(Path.GetTempPath(), $"ve_blocks_{Guid.NewGuid():N}.mp4")
                    : output;
                if (blockOut != output) temps.Add(blockOut);

                await ApplyBlocksAsync(stageIn, blockOut, blocks, targetW, targetH, canvasUiW, canvasUiH, totalDuration,
                    new Progress<double>(p => progress?.Report(hasAudioOnly ? 0.90 + p * 0.05 : 0.90 + p * 0.10)));
                stageIn = blockOut;
            }

            if (hasAudioOnly)
            {
                await MixAudioOnlyClipsAsync(stageIn, output, audioOnlyClips, totalDuration,
                    new Progress<double>(p => progress?.Report((hasBlocks ? 0.95 : 0.90) + p * (hasBlocks ? 0.05 : 0.10))));
            }
            else if (!hasBlocks)
            {
                progress?.Report(1.0);
            }
        }
        finally
        {
            foreach (var t in temps) try { File.Delete(t); } catch { }
        }
    }

    private async Task MixAudioOnlyClipsAsync(string videoIn, string output,
        IList<VideoClip> audioClips, double totalDuration, IProgress<double>? progress)
    {
        if (audioClips.Count == 0)
        {
            await RunAsync($"-y -i \"{videoIn}\" -c copy \"{output}\"", totalDuration, progress);
            return;
        }

        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var args = new StringBuilder($"-y -i \"{videoIn}\" ");
        args.Append($"-f lavfi -t {totalDuration.ToString(ci)} -i anullsrc=channel_layout=stereo:sample_rate=44100 ");
        foreach (var clip in audioClips)
            args.Append($"-i \"{clip.SourceFile}\" ");

        bool hasBaseAudio = await HasAudioStreamAsync(videoIn);
        var filters = new StringBuilder();
        var mixInputs = new List<string>();

        filters.Append($"[1:a]atrim=duration={totalDuration.ToString(ci)},asetpts=PTS-STARTPTS[silence];");
        mixInputs.Add("[silence]");

        if (hasBaseAudio)
        {
            filters.Append("[0:a]aresample=async=1:first_pts=0,asetpts=PTS-STARTPTS[basea];");
            mixInputs.Add("[basea]");
        }

        for (int i = 0; i < audioClips.Count; i++)
        {
            var clip = audioClips[i];
            int inputIndex = i + 2;
            double start = Math.Max(0, clip.InPoint);
            double end = Math.Max(start + 0.01, clip.OutPoint);
            int delayMs = Math.Max(0, (int)Math.Round(clip.TimelineStart * 1000.0));

            var chain = new List<string>
            {
                $"atrim=start={start.ToString(ci)}:end={end.ToString(ci)}",
                "asetpts=PTS-STARTPTS"
            };
            if (Math.Abs(clip.Speed - 1.0) > 0.01)
                chain.Add(BuildAtempoFilter(Math.Max(0.01, clip.Speed)));
            if (Math.Abs(clip.Volume - 1.0) > 0.01)
                chain.Add($"volume={clip.Volume.ToString(ci)}");
            chain.Add($"adelay={delayMs}:all=1");
            chain.Add("aresample=async=1:first_pts=0");

            string pad = $"[aud{i}]";
            filters.Append($"[{inputIndex}:a]{string.Join(",", chain)}{pad};");
            mixInputs.Add(pad);
        }

        filters.Append(string.Concat(mixInputs));
        filters.Append($"amix=inputs={mixInputs.Count}:duration=first:dropout_transition=0,");
        filters.Append("volume=1,aresample=async=1:first_pts=0[aout]");

        int abps = AppSettings.ExportAudioBitrate;
        args.Append($"-filter_complex \"{filters}\" -map 0:v:0 -map \"[aout]\" ");
        args.Append($"-c:v copy -c:a aac -ar 44100 -ac 2 -b:a {abps}k ");
        args.Append($"\"{output}\"");

        await RunAsync(args.ToString(), totalDuration, progress);
    }

    private async Task<bool> HasAudioStreamAsync(string input)
    {
        try
        {
            var output = await RunAndCaptureAsync(FFprobeExe,
                $"-v error -select_streams a:0 -show_entries stream=index -of csv=p=0 \"{input}\"");
            return !string.IsNullOrWhiteSpace(output);
        }
        catch
        {
            return false;
        }
    }

    private async Task BurnTextOverlaysAsync(string videoIn, string output,
        IList<VideoEditor.Models.TextOverlay> overlays, double duration, IProgress<double>? progress)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var filterParts = new List<string>();
        foreach (var ov in overlays)
        {
            if (string.IsNullOrWhiteSpace(ov.Text)) continue;
            if (ov.EndSeconds <= ov.StartSeconds) continue;
            var fontFile = ResolveDrawtextFont(ov.Bold, ov.Italic).Replace('\\', '/');
            var bits = new List<string>
            {
                "drawtext=text=" + EscapeDrawtextValue(ov.Text, isText: true),
                "fontfile=" + EscapeDrawtextValue(fontFile, isText: false),
                "x=" + ov.X,
                "y=" + ov.Y,
                "fontsize=" + ov.FontSize,
                "fontcolor=" + ov.FontColor
            };
            if (ov.BackgroundEnabled && ov.BackgroundOpacity > 0)
            {
                bits.Add("box=1");
                bits.Add($"boxcolor={ov.BackgroundColor}@{ov.BackgroundOpacity.ToString("0.##", ci)}");
                bits.Add("boxborderw=" + ov.BackgroundPadding);
            }
            // enable= must be quoted so the comma inside between(...) isn't read as a filter separator.
            bits.Add($"enable='between(t,{ov.StartSeconds.ToString("0.###", ci)},{ov.EndSeconds.ToString("0.###", ci)})'");
            filterParts.Add(string.Join(":", bits));
        }
        if (filterParts.Count == 0)
        {
            // No usable overlays - copy through unchanged.
            await RunAsync($"-y -i \"{videoIn}\" -c copy \"{output}\"", duration, progress);
            return;
        }
        var filter = string.Join(",", filterParts);
        var args = $"-y -i \"{videoIn}\" -vf \"{filter}\" -c:a copy \"{output}\"";
        await RunAsync(args, duration, progress);
    }

    private async Task RenderClipAsync(VideoClip c, string output, int targetW, int targetH, string fitMode = "contain", IProgress<double>? progress = null)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        int fps = AppSettings.ExportFps;
        int crf = AppSettings.ExportCrf;
        int abps = AppSettings.ExportAudioBitrate;

        bool manualCanvasTransform = c.HasManualCanvasTransform && c.VideoWidth > 0 && c.VideoHeight > 0;
        var vfPre = new List<string>();
        double rotate = NormalizeDegrees(c.RotateDegrees);
        if (!manualCanvasTransform)
        {
            if (IsAngle(rotate, 90)) vfPre.Add("transpose=1");
            else if (IsAngle(rotate, 180)) vfPre.Add("transpose=1,transpose=1");
            else if (IsAngle(rotate, 270)) vfPre.Add("transpose=2");
            else if (!IsAngle(rotate, 0)) vfPre.Add(BuildArbitraryRotateFilter(rotate, ci));
        }
        if (c.FlipH) vfPre.Add("hflip");
        if (c.FlipV) vfPre.Add("vflip");
        if (Math.Abs(c.Speed - 1.0) > 0.01)
            vfPre.Add($"setpts={(1.0 / c.Speed).ToString(ci)}*PTS");

        var vfTail = new List<string> { "setsar=1", $"fps={fps}", "setpts=PTS-STARTPTS" };

        string videoFilterArg;
        // Per-clip manual canvas transform overrides the global fit mode.
        // Scale source by (contain-fit * CanvasScale * per-axis stretch) and lay it on a black canvas at the
        // user's offset; overlay clips anything beyond the target rectangle automatically.
        if (manualCanvasTransform)
        {
            double baseScale = Math.Min((double)targetW / c.VideoWidth, (double)targetH / c.VideoHeight);
            var crop = NormalizedCanvasCrop(c);
            int cropW = Math.Max(2, c.VideoWidth - crop.left - crop.right);
            int cropH = Math.Max(2, c.VideoHeight - crop.top - crop.bottom);
            int finalW = Math.Max(2, (int)Math.Round(cropW * baseScale * c.CanvasScale * c.CanvasScaleX));
            int finalH = Math.Max(2, (int)Math.Round(cropH * baseScale * c.CanvasScale * c.CanvasScaleY));
            int cropShiftX = (int)Math.Round(((crop.left - crop.right) * baseScale * c.CanvasScale * c.CanvasScaleX) / 2);
            int cropShiftY = (int)Math.Round(((crop.top - crop.bottom) * baseScale * c.CanvasScale * c.CanvasScaleY) / 2);
            int posX = (targetW - finalW) / 2 + (int)Math.Round(c.CanvasOffsetX * targetW) + cropShiftX;
            int posY = (targetH - finalH) / 2 + (int)Math.Round(c.CanvasOffsetY * targetH) + cropShiftY;

            var preChain = vfPre.Count > 0 ? string.Join(",", vfPre) + "," : "";
            var tailChain = string.Join(",", vfTail);
            string cropFilter = c.HasManualCanvasCrop
                ? $"crop={cropW}:{cropH}:{crop.left}:{crop.top},"
                : "";
            string rotateFilter = BuildManualCanvasRotateFilter(rotate, finalW, finalH, ci, out int rotatedW, out int rotatedH);
            if (rotateFilter.Length > 0)
            {
                posX -= (rotatedW - finalW) / 2;
                posY -= (rotatedH - finalH) / 2;
            }

            string complex =
                $"[0:v]{preChain}{cropFilter}scale={finalW}:{finalH}{rotateFilter}[fg];" +
                $"color=c=black:s={targetW}x{targetH}:r={fps}[bg];" +
                $"[bg][fg]overlay={posX}:{posY}:shortest=1,{tailChain}[v]";
            videoFilterArg = $"-filter_complex \"{complex}\" -map \"[v]\" -map 0:a?";
        }
        else if (fitMode == "blur")
        {
            int blur = Math.Clamp(AppSettings.BlurredBgStrength, 0, 60);
            var preChain = vfPre.Count > 0 ? string.Join(",", vfPre) + "," : "";
            var tailChain = string.Join(",", vfTail);
            string complex =
                $"[0:v]{preChain}split=2[fgsrc][bgsrc];" +
                $"[bgsrc]scale={targetW}:{targetH}:force_original_aspect_ratio=increase," +
                $"crop={targetW}:{targetH}:(iw-{targetW})/2:(ih-{targetH})/2," +
                $"boxblur={blur}:2[bg];" +
                $"[fgsrc]scale={targetW}:{targetH}:force_original_aspect_ratio=decrease[fg];" +
                $"[bg][fg]overlay=(W-w)/2:(H-h)/2,{tailChain}[v]";
            videoFilterArg = $"-filter_complex \"{complex}\" -map \"[v]\" -map 0:a?";
        }
        else
        {
            string fit = fitMode == "cover"
                ? $"scale={targetW}:{targetH}:force_original_aspect_ratio=increase," +
                  $"crop={targetW}:{targetH}:(iw-{targetW})/2:(ih-{targetH})/2"
                : $"scale={targetW}:{targetH}:force_original_aspect_ratio=decrease," +
                  $"pad={targetW}:{targetH}:(ow-iw)/2:(oh-ih)/2:black";

            var allVf = new List<string>(vfPre) { fit };
            allVf.AddRange(vfTail);
            videoFilterArg = $"-vf \"{string.Join(",", allVf)}\"";
        }

        var af = new List<string>();
        if (Math.Abs(c.Speed - 1.0) > 0.01) af.Add(BuildAtempoFilter(c.Speed));
        if (Math.Abs(c.Volume - 1.0) > 0.01) af.Add($"volume={c.Volume.ToString(ci)}");
        af.Add("aresample=async=1:first_pts=0");
        af.Add("asetpts=PTS-STARTPTS");

        var dur = c.OutPoint - c.InPoint;
        var inputArg = c.LoopCount > 1
            ? $"-stream_loop {c.LoopCount - 1} -ss {c.InPoint.ToString(ci)} -i \"{c.SourceFile}\" -t {(dur * c.LoopCount).ToString(ci)}"
            : $"-ss {c.InPoint.ToString(ci)} -i \"{c.SourceFile}\" -t {dur.ToString(ci)}";

        if (AppSettings.AudioLoudnorm) af.Add("loudnorm=I=-16:TP=-1.5:LRA=11");

        var args = $"-y {inputArg} {videoFilterArg}";
        if (af.Count > 0) args += $" -af \"{string.Join(",", af)}\"";
        args += $" {ResolveVideoEncoderArgs(crf)} -pix_fmt yuv420p";
        args += $" -c:a aac -ar 44100 -ac 2 -b:a {abps}k";
        args += $" -r {fps} -video_track_timescale {fps * 1000}";
        args += $" \"{output}\"";

        await RunAsync(args, c.EffectiveDuration, progress);
    }

    private static double NormalizeDegrees(double degrees)
    {
        double d = degrees % 360;
        if (d < 0) d += 360;
        return d;
    }

    private static string BuildArbitraryRotateFilter(double degrees, System.Globalization.CultureInfo ci)
    {
        double radians = degrees * Math.PI / 180.0;
        return $"rotate={radians.ToString("0.########", ci)}:ow=rotw(iw):oh=roth(ih):c=black";
    }

    private static string BuildManualCanvasRotateFilter(double degrees, int width, int height, System.Globalization.CultureInfo ci, out int rotatedW, out int rotatedH)
    {
        rotatedW = width;
        rotatedH = height;
        degrees = NormalizeDegrees(degrees);
        if (IsAngle(degrees, 0))
            return "";

        double radians = degrees * Math.PI / 180.0;
        double cos = Math.Abs(Math.Cos(radians));
        double sin = Math.Abs(Math.Sin(radians));
        rotatedW = Math.Max(2, (int)Math.Ceiling(width * cos + height * sin));
        rotatedH = Math.Max(2, (int)Math.Ceiling(width * sin + height * cos));
        return $",rotate={radians.ToString("0.########", ci)}:ow={rotatedW}:oh={rotatedH}:c=black";
    }

    private static bool IsAngle(double actual, double expected)
    {
        double delta = Math.Abs(NormalizeAngleDelta(actual - expected));
        return delta < 0.001;
    }

    private static double NormalizeAngleDelta(double value)
    {
        value = ((value + 180) % 360 + 360) % 360 - 180;
        return value;
    }

    private static (int left, int top, int right, int bottom) NormalizedCanvasCrop(VideoClip c)
    {
        int maxW = Math.Max(0, c.VideoWidth - 2);
        int maxH = Math.Max(0, c.VideoHeight - 2);
        int left = Math.Clamp((int)Math.Round(c.CanvasCropLeft), 0, maxW);
        int right = Math.Clamp((int)Math.Round(c.CanvasCropRight), 0, Math.Max(0, maxW - left));
        int top = Math.Clamp((int)Math.Round(c.CanvasCropTop), 0, maxH);
        int bottom = Math.Clamp((int)Math.Round(c.CanvasCropBottom), 0, Math.Max(0, maxH - top));
        return (left, top, right, bottom);
    }

    /// <summary>
    /// Build the video encoder args string based on AppSettings.HardwareAccel + ExportCodec.
    /// Falls back to libx264 if hardware encoder isn't available.
    /// </summary>
    private static string ResolveVideoEncoderArgs(int crf)
    {
        var accel = AppSettings.HardwareAccel;
        var codec = AppSettings.ExportCodec;
        // Hardware encoders override the codec choice
        switch (accel)
        {
            case "nvenc":
                if (codec == "libx265") return $"-c:v hevc_nvenc -preset p4 -rc vbr -cq {crf}";
                return $"-c:v h264_nvenc -preset p4 -rc vbr -cq {crf}";
            case "qsv":
                if (codec == "libx265") return $"-c:v hevc_qsv -global_quality {crf}";
                return $"-c:v h264_qsv -global_quality {crf}";
            case "amf":
                if (codec == "libx265") return $"-c:v hevc_amf -quality quality -rc cqp -qp_i {crf} -qp_p {crf}";
                return $"-c:v h264_amf -quality quality -rc cqp -qp_i {crf} -qp_p {crf}";
            default: // cpu
                switch (codec)
                {
                    case "libx265": return $"-c:v libx265 -preset veryfast -crf {crf}";
                    case "libaom-av1": return $"-c:v libaom-av1 -crf {crf} -b:v 0";
                    case "prores_ks": return "-c:v prores_ks -profile:v 3"; // ProRes 422 HQ - CRF ignored
                    default: return $"-c:v libx264 -preset veryfast -crf {crf}";
                }
        }
    }

    private async Task RenderBlackClipAsync(string output, double durationSec, int targetW, int targetH)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        int fps = AppSettings.ExportFps;
        int crf = AppSettings.ExportCrf;
        int abps = AppSettings.ExportAudioBitrate;
        var args =
            $"-y -f lavfi -i color=c=black:s={targetW}x{targetH}:r={fps}:d={durationSec.ToString(ci)} " +
            $"-f lavfi -i anullsrc=channel_layout=stereo:sample_rate=44100 " +
            $"-shortest {ResolveVideoEncoderArgs(crf)} -pix_fmt yuv420p " +
            $"-c:a aac -ar 44100 -ac 2 -b:a {abps}k -r {fps} -video_track_timescale {fps * 1000} " +
            $"\"{output}\"";
        await RunAsync(args, durationSec, null);
    }

    public Task ExtractFrameAsync(string input, string output, double timeSeconds)
    {
        var args = $"-y -ss {timeSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} -i \"{input}\" -frames:v 1 -q:v 2 \"{output}\"";
        return RunAsync(args, 0, null);
    }

    public async Task<string?> GenerateWaveformAsync(string sourceFile, int width, int height, string cacheKey)
    {
        var root = Path.Combine(Path.GetTempPath(), "video_editor_waveforms");
        Directory.CreateDirectory(root);
        var safeKey = string.Concat(cacheKey.Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-'));
        var path = Path.Combine(root, safeKey + ".png");
        if (File.Exists(path)) return path;
        try
        {
            // showwavespic draws a single still image of the entire audio waveform.
            // colors=A0B4FF gives the cool blue tone matching the design.
            var args = $"-y -i \"{sourceFile}\" -filter_complex \"showwavespic=s={width}x{height}:colors=A0B4FF|7AA0FF:split_channels=0\" -frames:v 1 -update 1 \"{path}\"";
            await RunAsync(args, 0, null);
        }
        catch { }
        return File.Exists(path) ? path : null;
    }

    public async Task ExtractAudioAsync(string input, string output, double duration, IProgress<double>? progress = null)
    {
        // Choose codec based on output extension
        var ext = Path.GetExtension(output).ToLowerInvariant();
        string codec = ext switch
        {
            ".mp3" => "libmp3lame -q:a 2",
            ".aac" or ".m4a" => "aac -b:a 192k",
            ".wav" => "pcm_s16le",
            ".ogg" => "libvorbis -q:a 4",
            ".flac" => "flac",
            _ => "libmp3lame -q:a 2"
        };
        var args = $"-y -i \"{input}\" -vn -c:a {codec} \"{output}\"";
        await RunAsync(args, duration, progress);
    }

    public async Task RemoveAudioAsync(string input, string output, double duration, IProgress<double>? progress = null)
    {
        // -an strips audio entirely. Stream copy video for speed.
        var args = $"-y -i \"{input}\" -c:v copy -an \"{output}\"";
        await RunAsync(args, duration, progress);
    }

    public async Task<List<string>> ExtractThumbnailStripAsync(string input, double inSec, double outSec, int count, int width, string cacheKey)
    {
        var dur = outSec - inSec;
        if (dur < 0.05 || count < 1) return new List<string>();
        var root = Path.Combine(Path.GetTempPath(), "video_editor_thumbs");
        Directory.CreateDirectory(root);
        var safeKey = string.Concat(cacheKey.Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-'));
        var folder = Path.Combine(root, safeKey);
        Directory.CreateDirectory(folder);

        var paths = new List<string>();
        bool allExist = true;
        for (int i = 1; i <= count; i++)
        {
            var p = Path.Combine(folder, $"thumb_{i:D3}.jpg");
            paths.Add(p);
            if (!File.Exists(p)) allExist = false;
        }
        if (allExist) return paths;

        try
        {
            foreach (var p in paths) try { if (File.Exists(p)) File.Delete(p); } catch { }
        }
        catch { }

        var ci = System.Globalization.CultureInfo.InvariantCulture;
        // Use select filter to grab N frames evenly spaced; falls back to fps filter
        var fps = (count / dur).ToString(ci);
        var pattern = Path.Combine(folder, "thumb_%03d.jpg");
        var args = $"-y -ss {inSec.ToString(ci)} -i \"{input}\" -t {dur.ToString(ci)} -vf \"fps={fps},scale={width}:-1\" -vsync vfr -frames:v {count} \"{pattern}\"";
        try
        {
            await RunAsync(args, 0, null);
        }
        catch { }
        return paths.Where(File.Exists).ToList();
    }

    private async Task RunAsync(string arguments, double totalSeconds, IProgress<double>? progress)
    {
        var psi = new ProcessStartInfo
        {
            FileName = FFmpegExe,
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var tail = new Queue<string>();
        const int tailKeep = 12;
        p.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            Log?.Invoke(e.Data);
            lock (tail)
            {
                tail.Enqueue(e.Data);
                while (tail.Count > tailKeep) tail.Dequeue();
            }
            if (totalSeconds > 0 && progress != null)
            {
                var idx = e.Data.IndexOf("time=", StringComparison.Ordinal);
                if (idx >= 0)
                {
                    var ts = e.Data.Substring(idx + 5, Math.Min(11, e.Data.Length - idx - 5));
                    if (TimeSpan.TryParse(ts, System.Globalization.CultureInfo.InvariantCulture, out var t))
                    {
                        var pct = Math.Min(1.0, t.TotalSeconds / totalSeconds);
                        progress.Report(pct);
                        Progress?.Invoke(pct);
                    }
                }
            }
        };
        p.OutputDataReceived += (_, _) => { /* discard */ };
        p.Start();
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0)
        {
            string detail;
            lock (tail)
            {
                var diag = tail
                    .Where(l => !l.Contains("frame=") && !l.Contains("size=") && !l.Contains("bitrate="))
                    .ToList();
                if (diag.Count == 0) diag = tail.ToList();
                detail = string.Join("\n", diag.TakeLast(6));
            }
            throw new Exception($"FFmpeg exited with code {p.ExitCode}.\n\n{detail}");
        }
    }

    private async Task<string> RunAndCaptureAsync(string exe, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi) ?? throw new Exception($"Failed to start {exe}");
        // Read both streams concurrently - otherwise filling one pipe can block the other.
        var outputTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        await Task.WhenAll(outputTask, errTask);
        await p.WaitForExitAsync();
        if (p.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(errTask.Result)
                ? $"{Path.GetFileName(exe)} exited with code {p.ExitCode}."
                : errTask.Result.Trim();
            throw new Exception(message);
        }
        return outputTask.Result + "\n" + errTask.Result;
    }
}
