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

    public Task MergeAsync(IEnumerable<string> inputs, string output, IProgress<double>? progress = null)
    {
        var list = Path.Combine(Path.GetTempPath(), $"merge_{Guid.NewGuid()}.txt");
        var sb = new StringBuilder();
        foreach (var f in inputs) sb.AppendLine($"file '{f.Replace("'", "'\\''")}'");
        File.WriteAllText(list, sb.ToString());
        var args = $"-y -f concat -safe 0 -i \"{list}\" -c copy \"{output}\"";
        return RunAsync(args, 0, progress).ContinueWith(_ => { try { File.Delete(list); } catch { } });
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
        var atempo = BuildAtempoFilter(speed);
        var args = $"-y -i \"{input}\" -filter_complex \"[0:v]setpts={(1.0/speed).ToString(System.Globalization.CultureInfo.InvariantCulture)}*PTS[v];[0:a]{atempo}[a]\" -map \"[v]\" -map \"[a]\" \"{output}\"";
        return RunAsync(args, duration / speed, progress);
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

    public Task LoopAsync(string input, string output, int times, double duration, IProgress<double>? progress = null)
    {
        var list = Path.Combine(Path.GetTempPath(), $"loop_{Guid.NewGuid()}.txt");
        var sb = new StringBuilder();
        for (int i = 0; i < times; i++) sb.AppendLine($"file '{input.Replace("'", "'\\''")}'");
        File.WriteAllText(list, sb.ToString());
        var args = $"-y -f concat -safe 0 -i \"{list}\" -c copy \"{output}\"";
        return RunAsync(args, duration * times, progress).ContinueWith(_ => { try { File.Delete(list); } catch { } });
    }

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
        string codec = format.ToLower() switch
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

    public Task AddTextAsync(string videoIn, string output, string text, int x, int y, int fontSize, string colorHex, double duration, IProgress<double>? progress = null)
    {
        var safeText = text.Replace("'", "\\'").Replace(":", "\\:").Replace("\\", "\\\\");
        var args = $"-y -i \"{videoIn}\" -vf \"drawtext=text='{safeText}':x={x}:y={y}:fontsize={fontSize}:fontcolor={colorHex}\" -c:a copy \"{output}\"";
        return RunAsync(args, duration, progress);
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

        var sx = videoW / canvasW;
        var sy = videoH / canvasH;
        var sb = new StringBuilder();
        sb.Append("[0:v]");

        var blurInputs = new List<string>();
        for (int i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            int bx = (int)(b.X * sx);
            int by = (int)(b.Y * sy);
            int bw = (int)(b.Width * sx);
            int bh = (int)(b.Height * sy);
            if (bw < 2) bw = 2;
            if (bh < 2) bh = 2;

            if (b.Mode == BlockMode.Solid)
            {
                string colorHex = $"{b.Color.R:X2}{b.Color.G:X2}{b.Color.B:X2}";
                string enable = b.CoversWholeVideo ? "" : $":enable='between(t,{b.StartSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)},{b.EndSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)})'";
                sb.Append($"drawbox=x={bx}:y={by}:w={bw}:h={bh}:color=0x{colorHex}@1.0:t=fill{enable}");
                if (i < blocks.Count - 1) sb.Append(',');
            }
            else if (b.Mode == BlockMode.Blur || b.Mode == BlockMode.Pixelate)
            {
                blurInputs.Add($"BLOCK{i}|{bx}|{by}|{bw}|{bh}|{b.Mode}|{b.BlurStrength}|{b.CoversWholeVideo}|{b.StartSeconds}|{b.EndSeconds}");
            }
        }

        var solidFilter = sb.ToString();
        if (solidFilter.EndsWith("[0:v]")) solidFilter = "";

        if (blurInputs.Count == 0)
        {
            var argsSolid = $"-y -i \"{videoIn}\" -vf \"{solidFilter.Substring("[0:v]".Length)}\" -c:a copy \"{output}\"";
            return RunAsync(argsSolid, duration, progress);
        }

        var fullFilter = new StringBuilder();
        string prev = "[0:v]";
        if (solidFilter.Length > "[0:v]".Length)
        {
            fullFilter.Append(solidFilter).Append("[base];");
            prev = "[base]";
        }

        int idx = 0;
        foreach (var blk in blurInputs)
        {
            var parts = blk.Split('|');
            int bx = int.Parse(parts[1]);
            int by = int.Parse(parts[2]);
            int bw = int.Parse(parts[3]);
            int bh = int.Parse(parts[4]);
            var mode = parts[5];
            int strength = int.Parse(parts[6]);
            bool whole = bool.Parse(parts[7]);
            double s = double.Parse(parts[8], System.Globalization.CultureInfo.InvariantCulture);
            double e = double.Parse(parts[9], System.Globalization.CultureInfo.InvariantCulture);

            string region = $"[r{idx}]";
            string blurred = $"[b{idx}]";
            string next = $"[o{idx}]";

            fullFilter.Append($"{prev}crop={bw}:{bh}:{bx}:{by}{region};");
            if (mode == "Blur")
                fullFilter.Append($"{region}boxblur={strength}:1{blurred};");
            else
                fullFilter.Append($"{region}scale=iw/{Math.Max(2, strength / 4)}:ih/{Math.Max(2, strength / 4)},scale={bw}:{bh}:flags=neighbor{blurred};");

            string enable = whole ? "" : $":enable='between(t,{s.ToString(System.Globalization.CultureInfo.InvariantCulture)},{e.ToString(System.Globalization.CultureInfo.InvariantCulture)})'";
            fullFilter.Append($"{prev}{blurred}overlay={bx}:{by}{enable}{next};");
            prev = next;
            idx++;
        }

        var finalFilter = fullFilter.ToString().TrimEnd(';');
        var args = $"-y -i \"{videoIn}\" -filter_complex \"{finalFilter}\" -map \"{prev}\" -map 0:a? -c:a copy \"{output}\"";
        return RunAsync(args, duration, progress);
    }

    public async Task ExportProjectAsync(IList<VideoClip> clips, IList<VideoBlock> blocks,
        int canvasVideoW, int canvasVideoH, double canvasUiW, double canvasUiH,
        double totalDuration, string output, IProgress<double>? progress = null)
    {
        if (clips.Count == 0) throw new Exception("No clips.");
        var temps = new List<string>();
        try
        {
            int targetW = canvasVideoW > 0 ? canvasVideoW : 1920;
            int targetH = canvasVideoH > 0 ? canvasVideoH : 1080;
            if (targetW % 2 != 0) targetW++;
            if (targetH % 2 != 0) targetH++;

            // Pass 1: render each clip with its effects (trim, speed, rotate, flip, volume, loop)
            for (int i = 0; i < clips.Count; i++)
            {
                var c = clips[i];
                var tmp = Path.Combine(Path.GetTempPath(), $"ve_clip_{Guid.NewGuid():N}.mp4");
                temps.Add(tmp);
                await RenderClipAsync(c, tmp, targetW, targetH);
                progress?.Report((i + 1) * 0.7 / clips.Count);
            }

            string concatList = Path.Combine(Path.GetTempPath(), $"ve_concat_{Guid.NewGuid():N}.txt");
            File.WriteAllText(concatList, string.Join("\n", temps.Select(t => $"file '{t.Replace("'", "'\\''")}'")));

            string concatOut = blocks.Count > 0
                ? Path.Combine(Path.GetTempPath(), $"ve_concat_{Guid.NewGuid():N}.mp4")
                : output;

            var concatArgs = $"-y -f concat -safe 0 -i \"{concatList}\" -c copy \"{concatOut}\"";
            await RunAsync(concatArgs, totalDuration, null);
            progress?.Report(0.85);

            try { File.Delete(concatList); } catch { }

            if (blocks.Count > 0)
            {
                await ApplyBlocksAsync(concatOut, output, blocks, targetW, targetH, canvasUiW, canvasUiH, totalDuration,
                    new Progress<double>(p => progress?.Report(0.85 + p * 0.15)));
                try { File.Delete(concatOut); } catch { }
            }
            else
            {
                progress?.Report(1.0);
            }
        }
        finally
        {
            foreach (var t in temps) try { File.Delete(t); } catch { }
        }
    }

    private async Task RenderClipAsync(VideoClip c, string output, int targetW, int targetH)
    {
        var vf = new List<string>();
        if (c.RotateDegrees == 90) vf.Add("transpose=1");
        else if (c.RotateDegrees == 180) vf.Add("transpose=1,transpose=1");
        else if (c.RotateDegrees == 270) vf.Add("transpose=2");
        if (c.FlipH) vf.Add("hflip");
        if (c.FlipV) vf.Add("vflip");
        if (Math.Abs(c.Speed - 1.0) > 0.01)
            vf.Add($"setpts={(1.0 / c.Speed).ToString(System.Globalization.CultureInfo.InvariantCulture)}*PTS");
        vf.Add($"scale={targetW}:{targetH}:force_original_aspect_ratio=decrease");
        vf.Add($"pad={targetW}:{targetH}:(ow-iw)/2:(oh-ih)/2:black");
        vf.Add("setsar=1");

        var af = new List<string>();
        if (Math.Abs(c.Speed - 1.0) > 0.01) af.Add(BuildAtempoFilter(c.Speed));
        if (Math.Abs(c.Volume - 1.0) > 0.01) af.Add($"volume={c.Volume.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

        var dur = c.OutPoint - c.InPoint;
        var inputArg = c.LoopCount > 1
            ? $"-stream_loop {c.LoopCount - 1} -ss {c.InPoint.ToString(System.Globalization.CultureInfo.InvariantCulture)} -i \"{c.SourceFile}\" -t {(dur * c.LoopCount).ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : $"-ss {c.InPoint.ToString(System.Globalization.CultureInfo.InvariantCulture)} -i \"{c.SourceFile}\" -t {dur.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

        var args = $"-y {inputArg} -vf \"{string.Join(",", vf)}\"";
        if (af.Count > 0) args += $" -af \"{string.Join(",", af)}\"";
        args += " -c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -c:a aac -ar 44100 -ac 2";
        args += $" \"{output}\"";

        await RunAsync(args, c.EffectiveDuration, null);
    }

    public Task ExtractFrameAsync(string input, string output, double timeSeconds)
    {
        var args = $"-y -ss {timeSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} -i \"{input}\" -frames:v 1 -q:v 2 \"{output}\"";
        return RunAsync(args, 0, null);
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
        p.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            Log?.Invoke(e.Data);
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
        p.Start();
        p.BeginErrorReadLine();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0) throw new Exception($"FFmpeg exited with code {p.ExitCode}. See log for details.");
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
        using var p = Process.Start(psi)!;
        var output = await p.StandardOutput.ReadToEndAsync();
        var err = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return output + "\n" + err;
    }
}
