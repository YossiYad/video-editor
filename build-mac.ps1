# Build a quick macOS app bundle from the cross-platform Avalonia desktop host.
#
# Output:
#   publish\mac\VideoEditor-osx-arm64.app
#   publish\mac\VideoEditor-osx-arm64.zip (with -Zip)
#   publish\mac\VideoEditor-osx-arm64.app.tar.gz (with -TarGz)
#   publish\mac\VideoEditor-osx-arm64.dmg (with -Dmg, macOS only)
#
# Usage:
#   pwsh build-mac.ps1
#   pwsh build-mac.ps1 -Runtime osx-x64 -Zip -TarGz
#   pwsh build-mac.ps1 -Dmg
#
# Notes:
#   This is an unsigned MVP bundle. On a Mac, first launch may require:
#     xattr -dr com.apple.quarantine VideoEditor-osx-arm64.app
#     chmod +x VideoEditor-osx-arm64.app/Contents/MacOS/VideoEditor.Desktop

[CmdletBinding()]
param(
    [ValidateSet("osx-arm64", "osx-x64")]
    [string]$Runtime = "osx-arm64",
    [string]$OutputDir = "",
    [switch]$Zip,
    [switch]$TarGz,
    [switch]$Dmg
)

$ErrorActionPreference = "Stop"
$scriptRoot = if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) { $PSScriptRoot } else { (Get-Location).Path }
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $scriptRoot "publish\mac"
}

$projectFile = Join-Path $scriptRoot "VideoEditor.Desktop\VideoEditor.Desktop.csproj"
if (-not (Test-Path $projectFile)) {
    throw "Could not find VideoEditor.Desktop.csproj at $projectFile."
}

$publishDir = Join-Path $OutputDir "$Runtime-publish"
$appPath = Join-Path $OutputDir "VideoEditor-$Runtime.app"
$contentsDir = Join-Path $appPath "Contents"
$macosDir = Join-Path $contentsDir "MacOS"
$resourcesDir = Join-Path $contentsDir "Resources"

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (Test-Path $appPath) { Remove-Item $appPath -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publishDir, $macosDir, $resourcesDir | Out-Null

Write-Host "Publishing VideoEditor.Desktop for $Runtime ..." -ForegroundColor Green
& dotnet publish $projectFile `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=embedded `
    --output $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$exePath = Join-Path $publishDir "VideoEditor.Desktop"
if (-not (Test-Path $exePath)) {
    throw "Build finished but the macOS executable is missing: $exePath"
}

Copy-Item -Path (Join-Path $publishDir "*") -Destination $macosDir -Recurse -Force

$infoPlist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>VideoEditor</string>
  <key>CFBundleDisplayName</key>
  <string>VideoEditor</string>
  <key>CFBundleIdentifier</key>
  <string>com.videoeditor.desktop</string>
  <key>CFBundleVersion</key>
  <string>0.1.0</string>
  <key>CFBundleShortVersionString</key>
  <string>0.1.0</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleExecutable</key>
  <string>VideoEditor.Desktop</string>
  <key>LSMinimumSystemVersion</key>
  <string>11.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
"@
Set-Content -Path (Join-Path $contentsDir "Info.plist") -Value $infoPlist -Encoding UTF8

$firstRun = @"
#!/bin/sh
cd "`$(dirname "`$0")"
xattr -dr com.apple.quarantine "VideoEditor-$Runtime.app" 2>/dev/null || true
chmod +x "VideoEditor-$Runtime.app/Contents/MacOS/VideoEditor.Desktop"
open "VideoEditor-$Runtime.app"
"@
Set-Content -Path (Join-Path $OutputDir "first-run-$Runtime.command") -Value $firstRun -Encoding UTF8

Write-Host ""
Write-Host "macOS app bundle created:" -ForegroundColor Green
Write-Host "  APP: $appPath"
Write-Host "  First-run helper: $(Join-Path $OutputDir "first-run-$Runtime.command")"
Write-Host ""
Write-Host "This is unsigned. On macOS, run the first-run helper or clear quarantine/chmod manually."

if ($Zip) {
    $zipPath = Join-Path $OutputDir "VideoEditor-$Runtime.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Write-Host ""
    Write-Host "Packing ZIP ..." -ForegroundColor Cyan
    Compress-Archive -Path $appPath, (Join-Path $OutputDir "first-run-$Runtime.command") -DestinationPath $zipPath
    Write-Host "  ZIP: $zipPath" -ForegroundColor Green
}

if ($TarGz) {
    $tarGzPath = Join-Path $OutputDir "VideoEditor-$Runtime.app.tar.gz"
    if (Test-Path $tarGzPath) { Remove-Item $tarGzPath -Force }
    Write-Host ""
    Write-Host "Packing TAR.GZ ..." -ForegroundColor Cyan
    & tar -czf $tarGzPath -C $OutputDir "VideoEditor-$Runtime.app" "first-run-$Runtime.command"
    if ($LASTEXITCODE -ne 0) {
        throw "tar failed with exit code $LASTEXITCODE."
    }
    Write-Host "  TAR.GZ: $tarGzPath" -ForegroundColor Green
}

if ($Dmg) {
    $hdiutil = Get-Command hdiutil -ErrorAction SilentlyContinue
    if ($null -eq $hdiutil) {
        throw "DMG creation requires macOS hdiutil. Run this script with -Dmg on a Mac."
    }

    $dmgPath = Join-Path $OutputDir "VideoEditor-$Runtime.dmg"
    if (Test-Path $dmgPath) { Remove-Item $dmgPath -Force }

    $dmgRoot = Join-Path $OutputDir "$Runtime-dmg-root"
    if (Test-Path $dmgRoot) { Remove-Item $dmgRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $dmgRoot | Out-Null
    Copy-Item -Path $appPath -Destination $dmgRoot -Recurse -Force
    New-Item -ItemType SymbolicLink -Path (Join-Path $dmgRoot "Applications") -Target "/Applications" | Out-Null

    Write-Host ""
    Write-Host "Packing DMG ..." -ForegroundColor Cyan
    & hdiutil create -volname "VideoEditor" -srcfolder $dmgRoot -ov -format UDZO $dmgPath
    if ($LASTEXITCODE -ne 0) {
        throw "hdiutil failed with exit code $LASTEXITCODE."
    }
    Remove-Item $dmgRoot -Recurse -Force
    Write-Host "  DMG: $dmgPath" -ForegroundColor Green
}
