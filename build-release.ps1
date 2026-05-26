# Build a portable self-contained release of VideoEditor.
#
# Output: publish\portable\VideoEditor.exe (single-file, includes the .NET 8
# runtime, no installation required on the target machine).
#
# Usage:   pwsh build-release.ps1
# Needs:   .NET 8 SDK (https://dotnet.microsoft.com/download)

[CmdletBinding()]
param(
    [string]$OutputDir = (Join-Path $PSScriptRoot "publish\portable"),
    [string]$Runtime = "win-x64",
    [switch]$Zip
)

$ErrorActionPreference = "Stop"
$projectFile = Join-Path $PSScriptRoot "VideoEditor\VideoEditor.csproj"

if (-not (Test-Path $projectFile)) {
    throw "Could not find VideoEditor.csproj at $projectFile."
}

try {
    $sdks = & dotnet --list-sdks 2>$null
    $hasUsableSdk = [bool]($sdks | Where-Object {
        if ($_ -match '^(\d+)\.') { [int]$Matches[1] -ge 8 } else { $false }
    })
    if (-not $hasUsableSdk) {
        throw ".NET 8 SDK (or newer) is required. Install from https://dotnet.microsoft.com/download/dotnet/8.0"
    }
} catch [System.Management.Automation.CommandNotFoundException] {
    throw "dotnet CLI not found in PATH. Install .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0"
}

if (Test-Path $OutputDir) {
    Write-Host "Cleaning $OutputDir ..." -ForegroundColor Cyan
    Remove-Item $OutputDir -Recurse -Force
}

Write-Host "Publishing self-contained build for $Runtime ..." -ForegroundColor Green

& dotnet publish $projectFile `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:DebugType=embedded `
    --output $OutputDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$exe = Join-Path $OutputDir "VideoEditor.exe"
if (-not (Test-Path $exe)) {
    throw "Build finished but VideoEditor.exe is missing from $OutputDir."
}

$sizeMB = [Math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "Build complete." -ForegroundColor Green
Write-Host "  EXE:  $exe  ($sizeMB MB)" -ForegroundColor Green
Write-Host "  This folder is fully portable - copy it to any Windows 10/11 x64 machine."
Write-Host "  FFmpeg, Whisper models, and the AI background model auto-download on first launch."

if ($Zip) {
    $zipName = "VideoEditor-$Runtime-portable.zip"
    $zipPath = Join-Path (Split-Path $OutputDir) $zipName
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Write-Host ""
    Write-Host "Packing $zipName ..." -ForegroundColor Cyan
    Compress-Archive -Path (Join-Path $OutputDir '*') -DestinationPath $zipPath
    $zipMB = [Math]::Round((Get-Item $zipPath).Length / 1MB, 1)
    Write-Host "  ZIP:  $zipPath  ($zipMB MB)" -ForegroundColor Green
}
