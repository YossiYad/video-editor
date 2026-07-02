using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace VideoEditor.Services;

public class LoginRequiredDownloadException : Exception
{
    public string Url { get; }
    public string SiteName { get; }

    public LoginRequiredDownloadException(string url, string siteName, string message, Exception innerException)
        : base(message, innerException)
    {
        Url = url;
        SiteName = siteName;
    }
}

/// <summary>
/// Downloads videos from URLs. Supports direct HTTP file links via HttpClient and
/// uses yt-dlp.exe for any site that yt-dlp can extract.
/// </summary>
public class DownloadService
{
    private static readonly string YtDlpFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");
    public static string YtDlpExePath => Path.Combine(YtDlpFolder, "yt-dlp.exe");
    private const string YtDlpDownloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

    public static string DownloadLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VideoEditor", "yt-dlp.log");

    public event Action<string>? Log;
    public event Action<double>? Progress;

    public static bool IsYtDlpInstalled() => File.Exists(YtDlpExePath);

    private static readonly object _logFileLock = new();
    private void WriteLogFile(string line)
    {
        try
        {
            var dir = Path.GetDirectoryName(DownloadLogPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            lock (_logFileLock)
            {
                File.AppendAllText(DownloadLogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}{Environment.NewLine}");
            }
        }
        catch { /* logging must never crash the download */ }
    }

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

    public async Task<string> DownloadFromUrlAsync(string url, string outputFolder, string ffmpegPath, IProgress<double>? progress = null, CancellationToken ct = default, bool allowBrowserLogin = false, int maxHeight = 1080)
    {
        Directory.CreateDirectory(outputFolder);

        if (LooksLikeDirectFile(url))
        {
            try
            {
                return await DownloadDirectAsync(url, BuildDirectOutputPath(url, outputFolder), progress, ct);
            }
            catch (Exception directEx) when (directEx is not OperationCanceledException)
            {
                Log?.Invoke("Direct HTTP failed; trying yt-dlp...");
                WriteLogFile($"Direct HTTP failed, falling back to yt-dlp: {directEx.GetType().Name}: {directEx.Message}");
                try
                {
                    return await DownloadViaYtDlpWithBrowserLoginPromptAsync(url, outputFolder, ffmpegPath, allowBrowserLogin, maxHeight, progress, ct);
                }
                catch (Exception ytDlpEx) when (ytDlpEx is not OperationCanceledException)
                {
                    throw new Exception(
                        $"Direct HTTP failed: {directEx.Message}\n" +
                        $"yt-dlp fallback also failed: {ytDlpEx.Message}",
                        ytDlpEx);
                }
            }
        }

        try
        {
            return await DownloadViaYtDlpWithBrowserLoginPromptAsync(url, outputFolder, ffmpegPath, allowBrowserLogin, maxHeight, progress, ct);
        }
        catch (Exception ytDlpEx) when (ytDlpEx is not OperationCanceledException && !LooksLikeDirectFile(url))
        {
            if (LooksLikeStreamingSite(url))
                throw;

            Log?.Invoke("yt-dlp failed; trying direct HTTP fallback...");
            WriteLogFile($"yt-dlp failed, falling back to direct HTTP: {ytDlpEx.GetType().Name}: {ytDlpEx.Message}");
            try
            {
                return await DownloadDirectAsync(url, BuildDirectOutputPath(url, outputFolder), progress, ct);
            }
            catch (Exception directEx) when (directEx is not OperationCanceledException)
            {
                throw new Exception(
                    $"yt-dlp failed: {ytDlpEx.Message}\n" +
                    $"Direct HTTP fallback also failed: {directEx.Message}",
                    directEx);
            }
        }
    }

    private async Task<string> DownloadViaYtDlpWithBrowserLoginPromptAsync(string url, string outputFolder, string ffmpegPath, bool allowBrowserLogin, int maxHeight, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        try
        {
            return await DownloadViaYtDlpAsync(url, outputFolder, ffmpegPath, null, maxHeight, progress, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ShouldRetryWithBrowserLogin(ex.Message))
        {
            if (!allowBrowserLogin)
            {
                throw new LoginRequiredDownloadException(
                    url,
                    GetSiteName(url),
                    $"Login is required before downloading from {GetSiteName(url)}.",
                    ex);
            }

            Log?.Invoke("Trying browser login cookies...");
            WriteLogFile($"Retrying with browser cookies after login prompt: {ex.GetType().Name}: {ex.Message}");
        }

        var failures = new List<(string Browser, Exception Error)>();
        foreach (var browser in new[] { "firefox", "edge", "chrome" })
        {
            try
            {
                Log?.Invoke($"Trying yt-dlp with {browser} login cookies...");
                return await DownloadViaYtDlpAsync(url, outputFolder, ffmpegPath, browser, maxHeight, progress, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add((browser, ex));
                WriteLogFile($"yt-dlp with {browser} cookies failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        throw new LoginRequiredDownloadException(
            url,
            GetSiteName(url),
            BuildBrowserLoginFailureMessage(failures),
            failures.LastOrDefault().Error ?? new Exception("Browser login failed."));
    }

    public async Task EnsureYtDlpAsync(IProgress<double>? progress = null)
    {
        if (File.Exists(YtDlpExePath)) return;
        Directory.CreateDirectory(YtDlpFolder);
        Log?.Invoke("Downloading yt-dlp.exe (~12 MB) - first run only...");
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
        EnsureDirectResponseLooksDownloadable(response, url);
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

    public async Task<string> DownloadViaYtDlpAsync(string url, string outputFolder, string ffmpegPath, string? browserForCookies = null, int maxHeight = 1080, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        WriteLogFile($"===== yt-dlp run started for URL: {url} =====");
        WriteLogFile($"yt-dlp.exe path: {YtDlpExePath}");
        WriteLogFile($"yt-dlp.exe exists before run: {File.Exists(YtDlpExePath)}");

        if (!File.Exists(YtDlpExePath))
        {
            Log?.Invoke("yt-dlp.exe not found · downloading now...");
            WriteLogFile("yt-dlp.exe missing - attempting download from GitHub...");
            try
            {
                await EnsureYtDlpAsync(progress);
                WriteLogFile($"yt-dlp.exe download succeeded. File exists: {File.Exists(YtDlpExePath)}");
            }
            catch (Exception ex)
            {
                WriteLogFile($"yt-dlp.exe download FAILED: {ex.GetType().Name}: {ex.Message}");
                throw new Exception(
                    "Could not download yt-dlp.exe from GitHub. This usually means antivirus " +
                    "or a firewall is blocking it. Please allow GitHub releases and the " +
                    $"file {YtDlpExePath}, then try again. (Details: {ex.Message})", ex);
            }
        }

        Directory.CreateDirectory(outputFolder);
        var outputTemplate = Path.Combine(outputFolder, "%(title)s.%(ext)s");

        var psi = new ProcessStartInfo
        {
            FileName = YtDlpExePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add(BuildFormatSelector(maxHeight));
        psi.ArgumentList.Add("--merge-output-format");
        psi.ArgumentList.Add("mp4");
        psi.ArgumentList.Add("--remux-video");
        psi.ArgumentList.Add("mp4");
        psi.ArgumentList.Add("--ffmpeg-location");
        psi.ArgumentList.Add(ffmpegPath);
        psi.ArgumentList.Add("--no-playlist");
        psi.ArgumentList.Add("--no-warnings");
        psi.ArgumentList.Add("--newline");
        if (!string.IsNullOrWhiteSpace(browserForCookies))
        {
            psi.ArgumentList.Add("--cookies-from-browser");
            psi.ArgumentList.Add(browserForCookies);
        }
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(outputTemplate);
        psi.ArgumentList.Add(url);

        Log?.Invoke("yt-dlp · " + string.Join(" ", psi.ArgumentList.Select(QuoteArg)));
        WriteLogFile($"Running: {YtDlpExePath} {string.Join(" ", psi.ArgumentList.Select(QuoteArg))}");
        string? finalPath = null;
        var recentErrors = new Queue<string>();

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            Log?.Invoke(e.Data);
            WriteLogFile($"OUT: {e.Data}");
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
        p.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            Log?.Invoke("ERR: " + e.Data);
            WriteLogFile($"ERR: {e.Data}");
            recentErrors.Enqueue(e.Data);
            while (recentErrors.Count > 5) recentErrors.Dequeue();
        };
        try
        {
            p.Start();
        }
        catch (System.ComponentModel.Win32Exception wex)
        {
            WriteLogFile($"Failed to launch yt-dlp.exe: {wex.Message} (NativeErrorCode={wex.NativeErrorCode})");
            throw new Exception(
                $"Could not launch yt-dlp.exe at {YtDlpExePath}. The file may have been " +
                $"quarantined by antivirus or is corrupted. Try deleting it and re-running. " +
                $"(System error: {wex.Message})", wex);
        }
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        try
        {
            await p.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // User cancelled - make sure we don't leave yt-dlp running in the background.
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
            WriteLogFile("yt-dlp run cancelled by user.");
            throw;
        }
        WriteLogFile($"yt-dlp exited with code {p.ExitCode}");
        if (p.ExitCode != 0)
        {
            var detail = string.Join(Environment.NewLine, recentErrors);
            if (string.IsNullOrWhiteSpace(detail))
                detail = "No detailed error was reported by yt-dlp.";
            throw new Exception(
                $"yt-dlp exited with code {p.ExitCode}.{Environment.NewLine}" +
                $"{detail}{Environment.NewLine}" +
                $"Common causes: site login/private content, anti-bot block, age/audience restriction, or geo-restricted video.{Environment.NewLine}" +
                $"Full log: {DownloadLogPath}");
        }

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
        WriteLogFile($"yt-dlp done → {finalPath}");
        return finalPath;
    }

    private static string BuildDirectOutputPath(string url, string outputFolder)
    {
        string fileName;
        try
        {
            var uri = new Uri(url);
            fileName = Path.GetFileName(uri.LocalPath);
        }
        catch
        {
            fileName = "";
        }

        if (string.IsNullOrWhiteSpace(fileName) || !fileName.Contains('.'))
            fileName = $"downloaded_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";

        foreach (var c in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(c, '_');

        return Path.Combine(outputFolder, fileName);
    }

    private static void EnsureDirectResponseLooksDownloadable(HttpResponseMessage response, string url)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(mediaType)) return;

        var looksLikePage =
            mediaType.StartsWith("text/") ||
            mediaType is "application/json" or "application/xml" or "application/xhtml+xml";

        if (looksLikePage && !LooksLikeDirectFile(url))
            throw new InvalidOperationException($"The URL returned {mediaType}, not a downloadable video file.");
    }

    private static string QuoteArg(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        return value.Any(char.IsWhiteSpace) || value.Contains('"')
            ? "\"" + value.Replace("\"", "\\\"") + "\""
            : value;
    }

    private static string BuildFormatSelector(int maxHeight)
    {
        if (maxHeight <= 0)
            return "bv*+ba/b";

        maxHeight = Math.Clamp(maxHeight, 144, 4320);
        return $"bv*[height<={maxHeight}]+ba/b[height<={maxHeight}]/bv*+ba/b";
    }

    private static string BuildBrowserLoginFailureMessage(List<(string Browser, Exception Error)> failures)
    {
        var usableCookieFailures = failures
            .Where(f => !IsBrowserCookieDecryptFailure(f.Error.Message))
            .ToList();

        var primary = usableCookieFailures.FirstOrDefault();
        if (primary.Error != null)
        {
            var blockedBrowsers = failures
                .Where(f => IsBrowserCookieDecryptFailure(f.Error.Message))
                .Select(f => f.Browser)
                .ToArray();

            var msg =
                $"yt-dlp could read browser login cookies, but the site still refused the download.{Environment.NewLine}" +
                $"{primary.Browser}: {primary.Error.Message}";

            if (blockedBrowsers.Length > 0)
            {
                msg += Environment.NewLine + Environment.NewLine +
                       "Could not read cookies from: " + string.Join(", ", blockedBrowsers) + "." + Environment.NewLine +
                       "On recent Chrome/Edge versions this can happen because Windows protects browser cookies with DPAPI/App-Bound encryption.";
            }

            return msg;
        }

        return
            $"yt-dlp could not read browser login cookies from Firefox, Edge, or Chrome.{Environment.NewLine}" +
            "Chrome/Edge may be blocked by Windows DPAPI/App-Bound cookie encryption. Try logging into the site in Firefox, then run the download again.";
    }

    private static bool ShouldRetryWithBrowserLogin(string message)
    {
        var needles = new[]
        {
            "login",
            "private",
            "not available to everyone",
            "not available",
            "age",
            "audience",
            "restricted",
            "cookies",
            "sign in",
            "this content isn't available"
        };

        return needles.Any(n => message.Contains(n, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetSiteName(string url)
    {
        try
        {
            var host = new Uri(url).Host.ToLowerInvariant();
            if (host.Contains("instagram")) return "Instagram";
            if (host.Contains("tiktok")) return "TikTok";
            if (host.Contains("facebook") || host == "fb.watch") return "Facebook";
            if (host.Contains("youtube") || host == "youtu.be") return "YouTube";
            if (host.Contains("x.com") || host.Contains("twitter")) return "X";
            if (host.Contains("vimeo")) return "Vimeo";
            return host.StartsWith("www.") ? host[4..] : host;
        }
        catch
        {
            return "this site";
        }
    }

    private static bool IsBrowserCookieDecryptFailure(string message)
    {
        return message.Contains("Failed to decrypt", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("DPAPI", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("could not decrypt", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024 / 1024:0.##} GB";
        if (bytes >= 1024 * 1024) return $"{bytes / 1024.0 / 1024:0.##} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:0.##} KB";
        return $"{bytes} B";
    }
}
