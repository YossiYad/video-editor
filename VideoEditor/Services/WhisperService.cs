using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VideoEditor.Models;

namespace VideoEditor.Services;

public sealed class WhisperService
{
    private static readonly string WhisperFolder =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "whisper");

    public static string WhisperExePath
    {
        get
        {
            var cli = Path.Combine(WhisperFolder, Platform.ExeName("whisper-cli"));
            if (File.Exists(cli)) return cli;
            var main = Path.Combine(WhisperFolder, Platform.ExeName("main"));
            if (File.Exists(main)) return main;
            return cli;
        }
    }

    public static string ModelPath(string key) =>
        Path.Combine(WhisperFolder, $"ggml-{key}.bin");

    public event Action<string>? Log;

    private const string WhisperZipUrl =
        "https://github.com/ggerganov/whisper.cpp/releases/latest/download/whisper-bin-x64.zip";

    private static string ModelUrl(string key) =>
        $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-{key}.bin";

    public static bool IsBinaryReady() =>
        File.Exists(Path.Combine(WhisperFolder, Platform.ExeName("whisper-cli"))) ||
        File.Exists(Path.Combine(WhisperFolder, Platform.ExeName("main")));

    public static bool IsModelReady(string key) => File.Exists(ModelPath(key));

    public async Task EnsureBinaryAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (IsBinaryReady()) return;
        Directory.CreateDirectory(WhisperFolder);

        // whisper.cpp publishes a prebuilt binary only for Windows (whisper-bin-x64.zip).
        // On macOS/Linux there is no official prebuilt archive, so we can't auto-fetch it.
        // Give the user a clear, actionable message instead of downloading a Windows zip
        // that won't run. (Cross-platform whisper fetch is a follow-up: build from source
        // or install via Homebrew/apt and drop the binary into the whisper/ folder.)
        if (!Platform.IsWindows)
        {
            throw new PlatformNotSupportedException(
                "AI Captions needs the whisper.cpp binary. There is no official prebuilt " +
                "build for macOS/Linux yet. Install whisper.cpp (e.g. `brew install whisper-cpp` " +
                "on macOS, or build from source on Linux) and copy the `whisper-cli` binary " +
                $"into:\n{WhisperFolder}");
        }

        Log?.Invoke("Downloading whisper.cpp (~80 MB) - first run only...");

        var tmpZip = Path.Combine(Path.GetTempPath(), $"whisper_{Guid.NewGuid():N}.zip");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("VideoEditor/1.0");
            using var response = await client.GetAsync(WhisperZipUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;
            await using var src = await response.Content.ReadAsStreamAsync(ct);
            await using (var dst = File.Create(tmpZip))
            {
                var buf = new byte[81920];
                long got = 0;
                int read;
                while ((read = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, read), ct);
                    got += read;
                    if (total.HasValue) progress?.Report((double)got / total.Value * 0.9);
                }
            }

            Log?.Invoke("Extracting whisper binaries...");
            using var archive = ZipFile.OpenRead(tmpZip);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                if (ext is not (".exe" or ".dll")) continue;
                var target = Path.Combine(WhisperFolder, entry.Name);
                entry.ExtractToFile(target, overwrite: true);
            }
            progress?.Report(1.0);

            if (!IsBinaryReady())
                throw new Exception($"whisper-cli.exe / main.exe not found in {WhisperZipUrl}. The release format may have changed.");
        }
        finally
        {
            try { File.Delete(tmpZip); } catch { }
        }
    }

    public async Task EnsureModelAsync(string modelKey, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var path = ModelPath(modelKey);
        if (File.Exists(path)) return;
        Directory.CreateDirectory(WhisperFolder);
        Log?.Invoke($"Downloading whisper model ggml-{modelKey}.bin - first run only...");

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("VideoEditor/1.0");
        using var response = await client.GetAsync(ModelUrl(modelKey), HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;
        await using var src = await response.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(path);
        var buf = new byte[81920];
        long got = 0;
        int read;
        while ((read = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, read), ct);
            got += read;
            if (total.HasValue) progress?.Report((double)got / total.Value);
        }
        progress?.Report(1.0);
    }

    /// <summary>
    /// Extract audio with ffmpeg → run whisper-cli → parse SRT → return segments in input-file time.
    /// Caller adds timeline offset.
    /// </summary>
    public async Task<List<SubtitleSegment>> TranscribeAsync(
        string mediaPath, double inSec, double durSec,
        string language, string modelKey,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!File.Exists(mediaPath)) throw new FileNotFoundException("Media file not found", mediaPath);

        await EnsureBinaryAsync(new Progress<double>(p => progress?.Report(p * 0.15)), ct);
        await EnsureModelAsync(modelKey, new Progress<double>(p => progress?.Report(0.15 + p * 0.45)), ct);

        var ci = CultureInfo.InvariantCulture;
        var tempBase = Path.Combine(Path.GetTempPath(), $"ve_whisper_{Guid.NewGuid():N}");
        var tempWav = tempBase + ".wav";
        var srtOutBase = tempBase;
        var srtOutPath = srtOutBase + ".srt";

        try
        {
            Log?.Invoke("Extracting audio to 16 kHz mono WAV...");
            var ffmpegArgs = $"-y -ss {inSec.ToString(ci)} -t {durSec.ToString(ci)} -i \"{mediaPath}\" " +
                             $"-ac 1 -ar 16000 -vn -c:a pcm_s16le \"{tempWav}\"";
            await RunProcessAsync(Path.Combine(App.FFmpegPath, Platform.ExeName("ffmpeg")), ffmpegArgs, line =>
            {
                Log?.Invoke(line);
            }, ct);
            progress?.Report(0.65);

            if (!File.Exists(tempWav))
                throw new Exception("ffmpeg failed to extract audio");

            var langArg = string.IsNullOrEmpty(language) ? "auto" : language;
            Log?.Invoke($"Running whisper.cpp (model={modelKey}, language={langArg})...");
            var whisperArgs = $"-m \"{ModelPath(modelKey)}\" -f \"{tempWav}\" -l {langArg} -osrt -of \"{srtOutBase}\"";

            await RunProcessAsync(WhisperExePath, whisperArgs, line =>
            {
                if (string.IsNullOrEmpty(line)) return;
                Log?.Invoke(line);
                // Whisper emits "[00:00.000 --> 00:02.500]" progressively; bump progress smoothly.
                if (line.Contains("-->")) progress?.Report(Math.Min(0.95, 0.65 + 0.30 * 0.5));
            }, ct);

            if (!File.Exists(srtOutPath))
                throw new Exception($"whisper did not produce {srtOutPath}");

            // whisper.cpp writes the .srt as UTF-8 (no BOM). Always read as
            // UTF-8 - File.ReadAllTextAsync without an encoding falls back to
            // the system default code page on Windows, which corrupts Hebrew.
            var srtText = await File.ReadAllTextAsync(srtOutPath, Encoding.UTF8, ct);
            progress?.Report(1.0);
            return ParseSrt(srtText);
        }
        finally
        {
            try { File.Delete(tempWav); } catch { }
            try { File.Delete(srtOutPath); } catch { }
        }
    }

    public static List<SubtitleSegment> ParseSrt(string srt)
    {
        var result = new List<SubtitleSegment>();
        if (string.IsNullOrWhiteSpace(srt)) return result;

        var lines = srt.Replace("\r\n", "\n").Split('\n');
        int i = 0;
        while (i < lines.Length)
        {
            while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;
            if (i >= lines.Length) break;
            if (int.TryParse(lines[i].Trim(), out _)) i++; // sequence number
            if (i >= lines.Length) break;

            var timeLine = lines[i].Trim();
            i++;
            var arrowIdx = timeLine.IndexOf("-->", StringComparison.Ordinal);
            if (arrowIdx < 0) continue;

            if (!TryParseSrtTime(timeLine.Substring(0, arrowIdx).Trim(), out var start)) continue;
            if (!TryParseSrtTime(timeLine.Substring(arrowIdx + 3).Trim(), out var end)) continue;

            var textBuilder = new StringBuilder();
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]))
            {
                if (textBuilder.Length > 0) textBuilder.Append(' ');
                textBuilder.Append(lines[i].Trim());
                i++;
            }
            var text = textBuilder.ToString().Trim();
            if (!string.IsNullOrEmpty(text))
                result.Add(new SubtitleSegment { StartSeconds = start, EndSeconds = end, Text = text });
        }
        return result;
    }

    private static bool TryParseSrtTime(string token, out double seconds)
    {
        seconds = 0;
        token = token.Replace(',', '.');
        var parts = token.Split(':');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hh)) return false;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mm)) return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var ss)) return false;
        seconds = hh * 3600 + mm * 60 + ss;
        return true;
    }

    public static void WriteSrt(IEnumerable<SubtitleSegment> segments, string path)
    {
        var sb = new StringBuilder();
        int idx = 1;
        foreach (var s in segments)
        {
            if (string.IsNullOrWhiteSpace(s.Text)) continue;
            sb.Append(idx++).Append('\n');
            sb.Append(FormatSrtTime(s.StartSeconds)).Append(" --> ").Append(FormatSrtTime(s.EndSeconds)).Append('\n');
            sb.Append(s.Text.Trim()).Append("\n\n");
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    private static string FormatSrtTime(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}:{2:D2},{3:D3}",
            (int)ts.TotalHours, ts.Minutes, ts.Seconds, ts.Milliseconds);
    }

    private static Task RunProcessAsync(string exe, string args, Action<string> onLine, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>();
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // whisper.cpp emits UTF-8 (Hebrew/CJK/etc); without this the
            // .NET process host decodes with the system code page and the
            // status log turns into mojibake.
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) onLine(e.Data); };
        p.ErrorDataReceived  += (_, e) => { if (e.Data != null) onLine(e.Data); };
        p.Exited += (_, _) =>
        {
            if (p.ExitCode == 0) tcs.TrySetResult(true);
            else tcs.TrySetException(new Exception($"{Path.GetFileName(exe)} exited with code {p.ExitCode}"));
            p.Dispose();
        };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        ct.Register(() =>
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
            tcs.TrySetCanceled();
        });
        return tcs.Task;
    }
}
