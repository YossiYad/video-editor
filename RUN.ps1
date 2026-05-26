# Smart launcher for VideoEditor.
#
# Goal: let anyone who downloaded the source (git clone or "Download ZIP" from
# GitHub) run the app even if they do not have .NET 8 SDK on their machine.
#
#   - If .NET 8 SDK is installed -> build and run from source (latest local code).
#   - If it is not -> grab the latest ready-to-run release ZIP from
#     GitHub Releases (the EXE is self-contained, includes .NET) and launch it.
#
# Heavy assets (FFmpeg, Whisper models, MODNet AI background model) auto-download
# on first use from inside the app itself - no manual setup needed either way.

$ErrorActionPreference = "Stop"
$ScriptRoot = $PSScriptRoot
$Repo = "YossiYad/video-editor"

function Test-Dotnet8Sdk {
    try {
        $sdks = & dotnet --list-sdks 2>$null
        if (-not $sdks) { return $false }
        return [bool]($sdks | Where-Object { $_ -match '^8\.' })
    } catch {
        return $false
    }
}

function Invoke-FromSource {
    Write-Host ""
    Write-Host ".NET 8 SDK detected. Building and running from source..." -ForegroundColor Green
    Write-Host ""
    Push-Location $ScriptRoot
    try {
        & dotnet run --project VideoEditor --configuration Release
        return $LASTEXITCODE
    } finally {
        Pop-Location
    }
}

function Invoke-FromRelease {
    $cacheDir = Join-Path $ScriptRoot ".launcher-cache"
    $extractDir = Join-Path $cacheDir "app"
    $exePath = Join-Path $extractDir "VideoEditor.exe"

    if (Test-Path $exePath) {
        Write-Host "Launching cached release build..." -ForegroundColor Green
        Start-Process -FilePath $exePath -WorkingDirectory $extractDir
        return 0
    }

    Write-Host ""
    Write-Host ".NET 8 SDK was not found on this machine." -ForegroundColor Yellow
    Write-Host "Fetching the latest ready-to-run release from GitHub instead..." -ForegroundColor Yellow
    Write-Host ""

    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $headers = @{ "User-Agent" = "video-editor-launcher" }
    $api = "https://api.github.com/repos/$Repo/releases/latest"

    try {
        $release = Invoke-RestMethod -Uri $api -Headers $headers -ErrorAction Stop
    } catch {
        Write-Host "ERROR: could not reach GitHub Releases. Check your internet connection, then try again." -ForegroundColor Red
        Write-Host "Manual download: https://github.com/$Repo/releases/latest" -ForegroundColor Yellow
        return 1
    }

    $asset = $release.assets | Where-Object { $_.name -match 'win-x64.*\.zip$' } | Select-Object -First 1
    if (-not $asset) {
        Write-Host "ERROR: no Windows release ZIP found in the latest release." -ForegroundColor Red
        Write-Host "Browse the releases manually: https://github.com/$Repo/releases/latest" -ForegroundColor Yellow
        return 1
    }

    New-Item -ItemType Directory -Force -Path $cacheDir | Out-Null
    $zipPath = Join-Path $cacheDir $asset.name

    if (-not (Test-Path $zipPath)) {
        $sizeMB = [Math]::Round($asset.size / 1MB, 0)
        Write-Host "Downloading $($asset.name) ($sizeMB MB)..." -ForegroundColor Cyan
        try {
            Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zipPath -UseBasicParsing -Headers $headers
        } catch {
            if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
            Write-Host "ERROR: download failed - $($_.Exception.Message)" -ForegroundColor Red
            return 1
        }
    }

    if (Test-Path $extractDir) { Remove-Item $extractDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $extractDir | Out-Null

    Write-Host "Extracting..." -ForegroundColor Cyan
    Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force

    if (-not (Test-Path $exePath)) {
        $found = Get-ChildItem -Path $extractDir -Recurse -Filter "VideoEditor.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) {
            $extractDir = $found.Directory.FullName
            $exePath = $found.FullName
        }
    }

    if (-not (Test-Path $exePath)) {
        Write-Host "ERROR: VideoEditor.exe was not found inside the downloaded archive." -ForegroundColor Red
        return 1
    }

    Write-Host ""
    Write-Host "Launching $exePath" -ForegroundColor Green
    Write-Host "(Re-running this launcher later will reuse the cached copy in $cacheDir)" -ForegroundColor DarkGray
    Start-Process -FilePath $exePath -WorkingDirectory $extractDir
    return 0
}

if (Test-Dotnet8Sdk) {
    exit (Invoke-FromSource)
} else {
    exit (Invoke-FromRelease)
}
