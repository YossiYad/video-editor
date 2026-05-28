# Plan: macOS & Linux support

## Goal

Let users download and run Video Editor on macOS and Linux, not just Windows. Today the app ships as a single Windows `.exe`; the ask is a downloadable, runnable build on the other two desktop platforms.

## The core problem

The app is built on **WPF**, which only runs on Windows. The target framework is `net8.0-windows10.0.19041.0`, `UseWPF=true`, and several features lean on Windows-only APIs (WinRT speech, DirectShow/gdigrab capture, DirectML, NAudio, Win32 P/Invoke). There is no quick flag to flip - making this cross-platform is a real port, not a repackage.

An audit of the codebase found two genuinely large efforts and a tail of small/medium mechanical changes:

| Area | Size | What it is |
|---|---|---|
| WPF UI (~2,300 lines XAML + ~14,500 lines WPF C#) | **LARGE** | Whole UI: `MainWindow`, `Timeline`, `ResizableBlock`, 13 programmatic `Views/*.cs`, theme brushes in `App.xaml.cs` |
| Capture backends (`gdigrab` / `dshow` / `ddagrab`) | **LARGE** | Screen + camera recording hardcode the Windows ffmpeg backend; macOS/Linux need `avfoundation` / `x11grab` / `v4l2`, new device-enumeration parsers, and macOS permission prompts |
| TTS (WinRT `Windows.Media.SpeechSynthesis`) | MEDIUM | `MainWindow.xaml.cs` L3940-3988 |
| `System.Drawing` + `Graphics.CopyFromScreen` | MEDIUM | Region-picker thumbnails + AI background compositing (`BackgroundRemovalService.cs`) |
| DirectML / OnnxRuntime | MEDIUM | AI background model; a CPU fallback already exists |
| FFmpeg / yt-dlp / whisper `.exe` handling | MEDIUM | Every consumer hardcodes `.exe` and Windows download URLs |
| Build / packaging (`win-x64` PowerShell) | MEDIUM | `build-release.ps1`, `RUN.ps1`, `UpdateService.cs` |
| Win32 P/Invoke (`MonitorInfo.cs`), registry theme, fonts path, NAudio, `explorer.exe` shells | SMALL each | Localized, mechanical |

## Recommended approach: replatform the UI to Avalonia, phase the features

**Avalonia UI** is the right target. It is the closest analog to WPF (XAML + MVVM + styles + a `Window`/`Grid`/`StackPanel` object model), runs on Windows/macOS/Linux from one codebase, and supports .NET 8 single-file self-contained publish per-RID. Porting WPF XAML to Avalonia AXAML is largely a syntax-and-namespace exercise rather than a from-scratch rewrite, which matters a lot given ~17k lines of UI.

Rejected alternatives:
- **MAUI** - mobile-first, weaker desktop story, further from WPF; bigger rewrite.
- **Electron / Tauri + web UI** - total rewrite in a different language/stack; throws away all the existing C# editing logic.
- **Uno Platform** - viable but heavier and more WinUI-shaped than WPF; Avalonia is a smoother XAML port.
- **Keep WPF, run under Wine** - fragile, no camera/screen capture, not a real product.

The non-UI services (`FFmpegService`, `DownloadService`, `WhisperService`, timeline model, export pipeline) are mostly portable already and stay as-is, with platform shims where they touch the OS.

## Phased roadmap

Each phase is independently shippable / reviewable. Phases 0-2 produce a runnable Linux build with the core editor; capture and polish come after.

### Phase 0 - De-Windows the foundation (unblocks everything)
- Split the solution into **`VideoEditor.Core`** (platform-neutral services + models, `net8.0`) and **`VideoEditor.App`** (the UI). Move `Services/`, `Models/` into Core; only genuinely Windows-only services (`MonitorInfo`, the WinRT TTS, NAudio preview) get platform-specific implementations behind interfaces.
- Introduce an **`IPlatform` abstraction** in Core for the OS-specific bits: executable extension (`.exe` only on Windows), reveal-in-folder, fonts directory, dark-mode detection, special folders. One Windows impl today; macOS/Linux impls added in later phases.
- Centralize binary naming: a single `ExecutableName("ffmpeg")` helper instead of hardcoded `ffmpeg.exe` / `ffprobe.exe` / `yt-dlp.exe` / `whisper-cli.exe` scattered across `FFmpegService.cs`, `DownloadService.cs`, `WhisperService.cs`, `App.xaml.cs`.
- Per-OS binary fetch: `FFmpegDownloader` / yt-dlp / whisper download URLs chosen by RID (`win-x64`, `osx-arm64`, `osx-x64`, `linux-x64`) instead of the hardcoded Windows assets.

### Phase 1 - Avalonia UI shell + the core editor (no capture)
- New `VideoEditor.App` as an Avalonia project (`net8.0`, multi-RID). Port `App.xaml`/theme brushes, `MainWindow`, `Timeline`, `ResizableBlock`, and the dialog `Views/*` to Avalonia AXAML. This is the bulk of the work.
- Swap `Microsoft.Win32.OpenFileDialog`/`SaveFileDialog` for Avalonia's `StorageProvider` pickers.
- Swap `System.Windows.Media` brushes/`BitmapImage` for Avalonia's `IBrush` / `Bitmap`.
- Replace `System.Drawing` pixel work (region-picker thumbnails, AI compositing in `BackgroundRemovalService.cs`) with **SkiaSharp** (Avalonia already uses Skia) or **ImageSharp**.
- Result: open / trim / split / arrange / overlays / export all work on Linux (the easiest OS to validate first). Capture features temporarily hidden on non-Windows.

### Phase 2 - Cross-platform media playback & TTS
- Replace **NAudio** TTS preview with a portable player (Avalonia + a small audio lib, or shell out to `ffplay`).
- Replace **WinRT speech** with a per-OS TTS provider behind an `ITtsService`: macOS `say` / `AVSpeechSynthesizer`, Linux `espeak-ng` or `piper`, Windows keeps WinRT. Hebrew voice availability differs per OS - document the per-OS voice story.
- Preview playback: confirm Avalonia's video surface or an ffmpeg-pipe-to-bitmap path works on all three OSes (the app already pipes MJPEG frames from ffmpeg in places - reuse that pattern if Avalonia has no native video element).

### Phase 3 - Capture backends (the risky one)
- Abstract capture behind `IScreenCaptureBackend` / `ICameraBackend` with three ffmpeg implementations:
  - Windows: existing `gdigrab` / `dshow` / `ddagrab`
  - macOS: `avfoundation` (`-f avfoundation -i "1:0"` etc.) + the **TCC permission prompts** for screen recording and camera (the app must request and handle these or it silently captures black frames)
  - Linux: `x11grab` for screen, `v4l2` (`/dev/videoN`) for camera
- New per-OS **device-enumeration parsers** - the current `ParseDshowVideoDevices` (`VideoRecorderPickerWindow.cs` L154-184) only understands DirectShow's stderr format; `avfoundation -list_devices` and `v4l2-ctl` output differ.
- Replace Win32 `MonitorInfo.cs` monitor enumeration with CoreGraphics (macOS) / XRandR (Linux), or lean on ffmpeg's device listing. Accept loss of the DPI-aware `ddagrab` fast path off Windows.

### Phase 4 - Packaging, updater, distribution
- Per-RID publish profiles: `osx-arm64`, `osx-x64` (or a universal binary), `linux-x64`. Bash equivalents of `build-release.ps1` / `RUN.ps1`.
- macOS: bundle as a `.app`, **codesign + notarize** (otherwise Gatekeeper blocks it the same way SmartScreen blocks the unsigned Windows exe today). Optionally a `.dmg`.
- Linux: ship an **AppImage** (most portable) and/or a `.tar.gz`; consider Flatpak later.
- Rewrite the in-app updater (`UpdateService.cs` generates a Windows `.ps1`) per-OS, or disable auto-update off Windows and rely on the platform package manager / manual re-download.
- Extend the GitHub release to attach `osx-arm64`, `osx-x64`, `linux-x64` archives next to the existing `win-x64` ZIP, and update `RUN.*` / `UpdateService` asset-name regexes (`^VideoEditor-v[\d.]+-win-x64\.zip$`) to match per-OS names.

## Risks & open questions

- **Effort is dominated by the UI port.** ~17k lines of WPF. Avalonia keeps it a port not a rewrite, but it is still the single biggest chunk and the schedule driver.
- **macOS capture permissions (TCC).** Screen-recording and camera access need explicit user grants; without handling them the recorder silently produces black/empty output. This is the highest-risk feature area.
- **AI background (MODNet).** Portable on CPU today (ORT CPU fallback already exists). GPU acceleration off Windows would need CoreML (macOS) / other EPs - treat as a nice-to-have, ship CPU first.
- **TTS parity.** Hebrew voice quality/availability varies a lot by OS. May need a bundled engine (piper) for a consistent experience.
- **Drawtext fonts.** `FFmpegService.ResolveDrawtextFont()` hardcodes Windows font paths; needs per-OS font discovery so burned-in text and overlays render.
- **Code signing on both new OSes** is a real cost (Apple Developer account for notarization; the Windows build is already unsigned and hits SmartScreen, so this is a known theme).

## Suggested first step

Do **Phase 0** as a standalone PR: extract `VideoEditor.Core` and introduce the `IPlatform` / `ExecutableName` abstractions with Windows implementations only. It is low-risk (Windows behavior unchanged), it shrinks every later phase, and it makes the true size of the UI port measurable before committing to Avalonia. Validate the Linux editor (Phase 1) before tackling the capture backends (Phase 3), since Linux is the cheapest OS to iterate on and capture is the riskiest area.

## Scope note

This document is a roadmap, not an approved implementation. It deliberately does not pick exact library versions or write code - the next step is to green-light Phase 0 and Avalonia as the UI target, then plan Phase 1 in detail against the real Avalonia API.
