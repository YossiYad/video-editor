using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace VideoEditor.Services;

public static class UpdateService
{
    private const string ReleasesApiUrl = "https://api.github.com/repos/YossiYad/video-editor/releases/latest";
    private static readonly Regex AssetNameRegex = new(@"^VideoEditor-v[\d.]+-win-x64\.zip$", RegexOptions.IgnoreCase);

    public sealed record UpdateInfo(Version Latest, string TagName, string DownloadUrl, long? SizeBytes, string ReleaseNotes);

    public static Version CurrentVersion { get; } = ResolveCurrentVersion();

    public static string CurrentVersionString => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    private static Version ResolveCurrentVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            if (plus > 0) info = info[..plus];
            if (Version.TryParse(NormalizeVersionString(info), out var v)) return v;
        }
        return asm.GetName().Version ?? new Version(0, 0, 0, 0);
    }

    private static string NormalizeVersionString(string raw)
    {
        var s = raw.Trim();
        if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s[1..];
        var parts = s.Split('.');
        while (parts.Length < 3) s += ".0";
        return s;
    }

    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var client = BuildClient(TimeSpan.FromSeconds(20));
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        using var resp = await client.GetAsync(ReleasesApiUrl, ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(tag)) return null;

        if (!Version.TryParse(NormalizeVersionString(tag), out var latest)) return null;
        if (latest <= CurrentVersion) return null;

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        string? downloadUrl = null;
        long? size = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrEmpty(name) || !AssetNameRegex.IsMatch(name)) continue;
            downloadUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            if (asset.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var s)) size = s;
            break;
        }
        if (string.IsNullOrEmpty(downloadUrl)) return null;

        var body = root.TryGetProperty("body", out var b) ? (b.GetString() ?? string.Empty) : string.Empty;
        return new UpdateInfo(latest, tag!, downloadUrl, size, body);
    }

    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct = default)
    {
        var folder = Path.Combine(Path.GetTempPath(), "VideoEditor-Update");
        Directory.CreateDirectory(folder);
        var safeTag = string.Concat(info.TagName.Split(Path.GetInvalidFileNameChars()));
        var zipPath = Path.Combine(folder, $"{safeTag}.zip");

        if (File.Exists(zipPath) && info.SizeBytes.HasValue && new FileInfo(zipPath).Length == info.SizeBytes.Value)
        {
            progress?.Report(1.0);
            return zipPath;
        }
        if (File.Exists(zipPath)) File.Delete(zipPath);

        using var client = BuildClient(TimeSpan.FromMinutes(30));
        using var resp = await client.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long? total = resp.Content.Headers.ContentLength ?? info.SizeBytes;

        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(zipPath))
        {
            var buf = new byte[81920];
            long got = 0;
            int read;
            while ((read = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, read), ct);
                got += read;
                if (total.HasValue && total.Value > 0)
                    progress?.Report(Math.Min(1.0, (double)got / total.Value));
            }
        }
        progress?.Report(1.0);
        return zipPath;
    }

    public static void LaunchInstallerAndExit(string zipPath)
    {
        var exePath = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Cannot determine current executable path.");
        var installDir = Path.GetDirectoryName(exePath)
            ?? throw new InvalidOperationException("Cannot determine install directory.");
        var pid = Environment.ProcessId;

        var folder = Path.Combine(Path.GetTempPath(), "VideoEditor-Update");
        Directory.CreateDirectory(folder);
        var scriptPath = Path.Combine(folder, "update.ps1");
        var logPath = Path.Combine(folder, "update.log");

        var script = BuildInstallerScript(pid, zipPath, installDir, logPath);
        File.WriteAllText(scriptPath, script, new UTF8Encoding(true));

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
            UseShellExecute = true,
            WorkingDirectory = folder,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        Process.Start(psi);

        if (Application.Current != null)
            Application.Current.Shutdown();
        else
            Environment.Exit(0);
    }

    private static string BuildInstallerScript(int pid, string zipPath, string installDir, string logPath)
    {
        string Q(string s) => "'" + s.Replace("'", "''") + "'";

        return $@"$ErrorActionPreference = 'Stop'
$log = {Q(logPath)}
try {{
  ""[{{0}}] updater start"" -f (Get-Date -Format o) | Out-File -FilePath $log -Encoding utf8 -Append

  try {{ Wait-Process -Id {pid} -Timeout 60 -ErrorAction Stop }} catch {{ }}
  Start-Sleep -Milliseconds 1200

  $stage = Join-Path $env:TEMP 'VideoEditor-Update\stage'
  if (Test-Path $stage) {{ Remove-Item $stage -Recurse -Force }}
  New-Item -ItemType Directory -Path $stage -Force | Out-Null

  ""[{{0}}] expanding {zipPath.Replace("\\", "\\\\")}"" -f (Get-Date -Format o) | Out-File -FilePath $log -Encoding utf8 -Append
  Expand-Archive -LiteralPath {Q(zipPath)} -DestinationPath $stage -Force

  $src = (Get-ChildItem $stage -Directory | Select-Object -First 1)
  $srcPath = if ($src) {{ $src.FullName }} else {{ $stage }}

  ""[{{0}}] copying from $srcPath to {installDir.Replace("\\", "\\\\")}"" -f (Get-Date -Format o) | Out-File -FilePath $log -Encoding utf8 -Append
  Copy-Item -Path (Join-Path $srcPath '*') -Destination {Q(installDir)} -Recurse -Force

  ""[{{0}}] relaunching"" -f (Get-Date -Format o) | Out-File -FilePath $log -Encoding utf8 -Append
  Start-Process -FilePath (Join-Path {Q(installDir)} 'VideoEditor.exe') -WorkingDirectory {Q(installDir)}

  Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
  ""[{{0}}] updater done"" -f (Get-Date -Format o) | Out-File -FilePath $log -Encoding utf8 -Append
}}
catch {{
  ""[{{0}}] ERROR: $($_.Exception.Message)"" -f (Get-Date -Format o) | Out-File -FilePath $log -Encoding utf8 -Append
  exit 1
}}
";
    }

    private static HttpClient BuildClient(TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"VideoEditor/{CurrentVersionString}");
        return client;
    }
}
