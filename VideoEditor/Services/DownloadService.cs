using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace VideoEditor.Services;

/// <summary>
/// Downloads videos from URLs. Supports direct HTTP file links via HttpClient and
/// uses yt-dlp.exe (auto-downloaded on first use) for streaming sites like YouTube/Vimeo.
/// </summary>
public class DownloadService
{
    private static readonly string YtDlpFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");
    public static string YtDlpExePath => Path.Combine(YtDlpFolder, "yt-dlp.exe");
    private const string YtDlpDownloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

    public event Action<string>? Log;
    public event Action<double>? Progress;

    public static bool IsYtDlpInstalled() => File.Exists(YtDlpExePath);

    public static bool LooksLikeStreamingSite(string url)
    {
        url = url.ToLowerInvariant();
        var hosts = new[] { "youtube.com", "youtu.be", "vimeo.com", "twitch.tv",
            "facebook.com", "instagram.com", "twitter.com", "x.com", "tiktok.com",
            "dailymotion.com", "reddit.com", "soundcloud.com" };
        foreach (var h in hosts) if (url.Contains(h)) return true;
        return false;
    }

    public static bool LooksLikeDirectFile(string url)
    {
        try
        {
            var u = new Uri(url);
            var ext = Path.GetExtension(u.LocalPath).ToLowerInvariant();
            return ext is ".mp4" or ".mov" or ".mkv" or ".webm" or ".avi" or ".m4v" or ".wmv" or ".flv";
        }
        catch { return false; }
    }

    public async Task EnsureYtDlpAsync(IProgress<double>? progress = null)
    {
        if (File.Exists(YtDlpExePath)) return;
        Directory.CreateDirectory(YtDlpFolder);
        Log?.Invoke("Downloading yt-dlp.exe (~12 MB) — first run only...");
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        using var response = await client.GetAsync(YtDlpDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;
        await using var src = await response.Content.ReadAsStreamAsync();
        await using var dst = File.Create(YtDlpExePath);
        var buf = new byte[81920];
        long got = 0;
        int read;
        while ((read = await src.ReadAsync(buf)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, read));
            got += read;
            if (total.HasValue) progress?.Report((double)got / total.Value);
        }
    }

    public async Task<string> DownloadDirectAsync(string url, string outputPath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 VideoEditor/1.0");
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;
        Log?.Invoke($"Starting download · {(total.HasValue ? FormatSize(total.Value) : "size unknown")}");
        await using var src = await response.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(outputPath);
        var buf = new byte[81920];
        long got = 0;
        int read;
        while ((read = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, read), ct);
            got += read;
            if (total.HasValue) { var p = (double)got / total.Value; progress?.Report(p); Progress?.Invoke(p); }
        }
        progress?.Report(1.0);
        Log?.Invoke($"Downloaded {FormatSize(got)} → {outputPath}");
        return outputPath;
    }

    public async Task<string> DownloadViaYtDlpAsync(string url, string outputFolder, string ffmpegPath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!File.Exists(YtDlpExePath))
        {
            Log?.Invoke("yt-dlp.exe not found · downloading now...");
            await EnsureYtDlpAsync(progress);
        }

        Directory.CreateDirectory(outputFolder);
        var outputTemplate = Path.Combine(outputFolder, "%(title)s.%(ext)s");

        var psi = new ProcessStartInfo
        {
            FileName = YtDlpExePath,
            Arguments = $"-f \"bv*[ext=mp4][vcodec^=avc1][height<=1080]+ba[ext=m4a]/b[ext=mp4][height<=1080]/bv*[height<=1080]+ba/b[height<=1080]\" " +
                        $"--merge-output-format mp4 --remux-video mp4 " +
                        $"--ffmpeg-location \"{ffmpegPath}\" " +
                        $"--no-playlist --no-warnings --newline " +
                        $"-o \"{outputTemplate}\" \"{url}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        Log?.Invoke($"yt-dlp · {psi.Arguments}");
        string? finalPath = null;

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            Log?.Invoke(e.Data);
            // Parse progress: "[download]  42.3% of 123.45MiB at ..."
            var idx = e.Data.IndexOf("[download]", StringComparison.Ordinal);
            if (idx >= 0)
            {
                var pct = e.Data.IndexOf('%');
                if (pct > 0)
                {
                    int start = pct - 1;
                    while (start > 0 && (char.IsDigit(e.Data[start]) || e.Data[start] == '.')) start--;
                    var token = e.Data.Substring(start + 1, pct - start - 1);
                    if (double.TryParse(token, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                    {
                        progress?.Report(v / 100.0);
                        Progress?.Invoke(v / 100.0);
                    }
                }
            }
            // Detect final destination: "[download] Destination: <path>" or "Merging formats into <path>"
            if (e.Data.Contains("Destination:"))
            {
                var i = e.Data.IndexOf("Destination:", StringComparison.Ordinal);
                finalPath = e.Data[(i + "Destination:".Length)..].Trim();
            }
            else if (e.Data.Contains("Merging formats into"))
            {
                var i = e.Data.IndexOf("Merging formats into", StringComparison.Ordinal);
                var rest = e.Data[(i + "Merging formats into".Length)..].Trim().Trim('"');
                finalPath = rest;
            }
        };
        p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Log?.Invoke("ERR: " + e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        try
        {
            await p.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // User cancelled — make sure we don't leave yt-dlp running in the background.
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        if (p.ExitCode != 0) throw new Exception($"yt-dlp exited with code {p.ExitCode}. See log for details.");

        // If we didn't capture a final path, scan the folder for the newest video file.
        if (string.IsNullOrEmpty(finalPath) || !File.Exists(finalPath))
        {
            var newest = new DirectoryInfo(outputFolder).EnumerateFiles()
                .Where(f => f.Extension is ".mp4" or ".mkv" or ".webm" or ".mov")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            finalPath = newest?.FullName;
        }
        if (string.IsNullOrEmpty(finalPath) || !File.Exists(finalPath))
            throw new Exception("Download finished but file path not detected.");
        Log?.Invoke($"yt-dlp done → {finalPath}");
        return finalPath;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024 / 1024:0.##} GB";
        if (bytes >= 1024 * 1024) return $"{bytes / 1024.0 / 1024:0.##} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:0.##} KB";
        return $"{bytes} B";
    }
}
