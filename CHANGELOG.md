# Changelog

All notable changes to Video Editor are documented in this file.
The latest signed Windows build is always available at
[Releases · Latest](https://github.com/YossiYad/video-editor/releases/latest).

---

## [Unreleased]

---

## [v1.8.4] - 2026-05-28

### Added
- **AUDIO section in the Clip tab** with the four audio actions
  (Add Audio, Change Volume, Extract Audio, Mute / Remove). Sits
  alongside the other per-clip controls so audio editing happens in
  the same panel as everything else for a selected clip.

### Changed
- **Sidebar AUDIO group slimmed down to just AI Captions.** The four
  audio editing buttons (Add Audio, Change Volume, Extract Audio,
  Mute / Remove Audio) moved into the new AUDIO section in the Clip
  tab. AI Captions stays in the sidebar because it acts on the whole
  project, not a single clip.
- The duplicate "Add Audio" button under EXPORT-TIME EFFECTS in the
  Clip tab moved into the new AUDIO group; Resize now spans the full
  width of its row.

---

## [v1.8.3] - 2026-05-28

### Fixed
- **Camera Recorder no longer flashes the desktop before showing the
  webcam.** When opening Camera Recorder after a previous Screen
  Recorder session, the inline preview area used to render one frame
  of the stale screen capture from the prior session before the
  webcam stream took over (~300ms of "I see my desktop" then the
  camera). The preview source is now reset to the picker's last camera
  frame (or null) *before* the panel becomes visible, and the
  recorder's Close handler also clears the leftover frame so future
  opens start clean.

---

## [v1.8.2] - 2026-05-28

### Changed
- **Sidebar "TRANSFORM & TRIM" group removed.** All eight per-clip
  editing actions (Trim, Crop, Rotate, Flip, Resize, Loop, Change
  Speed, Stabilize) already exist as native controls inside the right
  Clip tab (IN/OUT inputs, Speed slider, 90° rotate buttons, Flip H/V,
  Crop, Resize, Loop, Stabilize), so duplicating them in the sidebar
  was just visual noise. The sidebar now keeps only "Merge Videos" in
  that area, since merging is a project-level action and has no
  counterpart in the per-clip tab.

---

## [v1.8.1] - 2026-05-28

### Changed
- **yt-dlp.exe is now bundled inside the release ZIP** instead of being
  downloaded from GitHub on first URL import. This fixes "Failed to
  download" errors on machines where antivirus blocked the runtime
  download or where the GitHub release endpoint was unreachable.

### Added
- **Persistent yt-dlp log file** at
  `%LOCALAPPDATA%\VideoEditor\yt-dlp.log`. Every URL import run is
  appended (with command line, stdout, stderr, exit code) so failures
  can be diagnosed after the fact instead of disappearing with the
  status bar text. The "Download Error" dialog now points to this log.
- **Friendlier error messages** when yt-dlp.exe cannot be downloaded
  from GitHub (firewall / antivirus / no network) or when the bundled
  yt-dlp.exe cannot be launched (quarantined by antivirus). The user
  gets a clear next step instead of a raw stack trace.

---

## [v1.8.0] - 2026-05-27

### Added
- **Multi-select on the timeline** - hold Ctrl / Shift to add clips,
  blocks, audio bars, and text-overlay bars to a selection set, or
  rubber-band a marquee across any of those tracks to grab everything
  inside the box. Drag any one selected item and the whole group
  follows. Backspace deletes the entire selection. A new
  "N items selected" inspector tab shows up in the right column so the
  multi-selection state is visible.
- **Inline Text-to-Speech tab** - the old TTS modal dialog is gone.
  TTS now lives as a tab in the right inspector with text, voice, and
  rate fields, just like the recorder tab.
- **Hebrew TTS (and any other installed language)** - the engine
  switched from `System.Speech` (SAPI5 only) to WinRT
  `Windows.Media.SpeechSynthesis`, which sees every voice installed in
  Windows including the OneCore voices like Microsoft Hadas (he-IL),
  Microsoft Asaf, and the modern English voices. Voices appear with
  their language tag (e.g. "Microsoft Hadas (he-IL)") so a Hebrew voice
  is easy to find.
- **TTS Preview button** - synth straight to the speakers via NAudio
  before saving. Click again to stop mid-playback. Switching the
  inspector away (e.g. clicking a clip on the timeline) also stops the
  preview.
- **TTS - Add to Timeline button** - generates the audio and inserts
  it as an audio-only clip on the A1 lane at the current playhead,
  without opening a save dialog. The WAV is auto-stored inside the
  app folder (`tts_audio/`). "Save to disk" still opens a normal save
  dialog for users who want the file outside the app.
- **Screen Recorder sub-tabs promoted to top-level** - while the
  recorder is open, Recording / Camera / per-Block items show as
  top-level tabs in the inspector (replacing the old monolithic
  Recorder tab). Each sub-tab now opens just the fields relevant to
  that thing instead of stuffing everything into one panel.
- **Click-anywhere-on-the-preview reopens the recorder controls** -
  whenever the recorder is in use (before, during, or after the actual
  recording), clicking the centre preview brings the inspector tabs
  back if they were closed.
- **Camera picker preview frame** - the last preview frame from the
  Camera Recorder picker dialog is now kept and shown on the camera
  block in the canvas as soon as the dialog closes, so there is no
  blank gap before the live feed starts.

### Changed
- **Clip tab is now conditional** - the Clip tab in the inspector
  only shows when a clip is actually selected on the timeline, instead
  of always taking up a column. Selecting a clip also snaps the
  preview to its first frame.
- **Minimum Windows version is now Windows 10 19041 (May 2020)** -
  the target framework moved to `net8.0-windows10.0.19041.0` so the
  app can access WinRT speech synthesis. Earlier Windows 10 builds
  are no longer supported.

---

## [v1.7.1] - 2026-05-26

### Added
- **In-app updater** - Settings -> About -> "Check for updates now"
  now actually does something. The app queries the GitHub releases
  API for the latest tag, compares it against the assembly version,
  downloads the win-x64 ZIP if a newer version is available, and
  launches a PowerShell installer that waits for the running process
  to exit, copies the new files over the install dir, and relaunches
  the app. The "Release channel" dropdown and "Auto-check on startup"
  toggle remain visible but are explicitly disabled with a "Coming
  soon" tooltip until they are wired up.
- The displayed version in Settings -> About is now read from the
  assembly version (was a hardcoded `1.4.0` string), and the csproj
  carries explicit `Version` / `AssemblyVersion` / `FileVersion` /
  `InformationalVersion` properties so the assembly reports the real
  version to the updater.
- **`RUN.cmd` smart launcher** for users who downloaded the source
  code (git clone or "Code -> Download ZIP") instead of a packaged
  release. If the machine has the .NET 8 SDK the launcher builds and
  runs from source; otherwise it pulls the latest pre-built release
  ZIP from GitHub Releases and runs that. So even users without
  .NET installed can now run the app straight from the source repo
  by double-clicking `RUN.cmd`.
- **`build-release.ps1`** helper - one command produces a portable,
  self-contained `VideoEditor.exe` (single-file, includes the .NET 8
  runtime, ready to drop on any Windows 10 / 11 x64 machine).
  Optional `-Zip` flag packs it into a redistributable archive.
- README rewrite (both English and Hebrew) - now states explicitly
  that the release ZIP needs no .NET install, documents the new
  `RUN.cmd` launcher path for source downloads, and lists the MODNet
  AI background model alongside FFmpeg / Whisper as another asset
  that auto-downloads on first use.

### Fixed
- **App icon visible size** - the desktop / taskbar / Explorer icon was
  rendering noticeably smaller than other apps because the source PNG
  had ~17 % transparent padding on each side. The icon-build script
  now auto-trims the transparent border, keeps a Windows-standard
  ~4 % padding, and rescales the logo so it fills the icon canvas
  the same way most other Windows app icons do.

### Housekeeping
- `.gitignore` now also ignores `models/` (where the MODNet AI
  background ONNX is auto-downloaded on first use) and
  `.launcher-cache/` (where `RUN.cmd` stashes the release ZIP it
  pulls from GitHub when there is no local .NET 8 SDK). Both are
  per-machine runtime caches that re-create themselves on demand.

---

## [v1.7.0] - 2026-05-26

Major recording-experience release: AI camera background now works in
the Screen Recorder + Camera PIP combo too, and the inline recorder
left column is reorganised into per-item tabs.

### Added
- **AI camera background in Screen Recorder + Camera PIP** - the same
  four modes that already worked in standalone Camera Recorder
  (Keep original, Blur, Transparent, Replace with colour) are now
  available when you add a camera on top of a screen recording. The
  background is applied both to the floating camera preview while you
  record and to the final saved file (post-processed at Stop).
- **Per-item tabs in the inline Screen Recorder** - the left column
  now starts with a fixed "Recording" tab holding the screen source,
  FPS and output path. Each thing you add to the scene gets its own
  tab next to it: "Camera" for the webcam layer (with the AI
  background picker, device name, and Remove Camera button), and one
  "Block N" tab per hide block (showing the block colour swatch and a
  Delete Block button). Switching tabs swaps the left column without
  ever hiding the live preview on the right.
- **Live AI diagnostics + log file** - a hidden
  `CameraDebugDiagnostics` flag in `AppSettings` enables an on-screen
  panel that shows camera FPS, AI inference time, dropped frame
  counters and FFmpeg-preview error tail. A persistent
  `camera-diag.log` next to the EXE captures every camera-pipeline
  event for the current session even when the on-screen panel is off,
  so freezes can be diagnosed without rebuilding.

### Changed
- **DirectML GPU acceleration for the AI background** - the MODNet
  portrait-matting runs on the GPU via DirectML when available,
  falling back to CPU on machines without a compatible GPU. On a
  recent integrated GPU per-frame inference is ~20 ms, fast enough
  for a fully live preview.
- **Sharper mask edges around the person** - the per-frame alpha mask
  from MODNet is now post-processed with a small foreground bias and
  a smoothstep curve, so the blur / colour background no longer
  bleeds into the face and shoulders.
- **Mirrored camera previews everywhere** - the picker, the inline
  recorder webcam block, and the camera-only preview all show a
  mirror-image view, matching what most webcam apps do.
- **Higher-resolution preview pipe** - the MJPEG preview pipe between
  FFmpeg and the app is now 640 px wide at 12 fps (was 320 px at 8
  fps), so the AI gets a better source frame and the mask is less
  blocky.
- **DirectShow release timing fix** - when stopping the live camera
  preview to start recording (or switching between background modes),
  the app now waits for the previous FFmpeg process to actually exit
  and gives the USB camera ~300 ms to release its DirectShow handle
  before re-opening it. Eliminates the "Could not run graph - device
  already in use" failure that used to freeze the camera on every
  background switch.
- **Safer preview retry** - if FFmpeg fails to open the camera, the
  app schedules at most four retries with increasing back-off, and
  ignores stale errors from processes that have already been replaced
  by a working one (no more endless retry loops between green and
  orange status).

### Fixed
- Camera Recorder no longer freezes its preview when you start a
  recording: the recorder process now feeds the same on-screen image
  during recording as the preview did before recording, with the AI
  background still applied frame by frame on the live preview.
- The screen recording's camera PIP overlay no longer falls back to
  raw camera when you pick an AI background - the live overlay,
  the floating preview block and the final exported file all use the
  same processed camera.
- Recording at 60 fps no longer crashes on cameras whose default
  resolution does not support 60 fps - the app now lets the camera
  use its native rate and resamples to the requested FPS via FFmpeg's
  `fps` filter.

### Notes
- The MODNet ONNX model now downloads from a Hugging Face mirror
  (was the OpenVINO Open Model Zoo). The file is still ~25 MB and
  is verified with a minimum-size check before use.
- The recording pipeline for screen + camera with AI uses two
  temporary files (screen-only + raw camera) during capture, then
  composites them with the AI-processed camera at Stop. They are
  cleaned up automatically; if a composite fails the error is shown
  with the FFmpeg tail.

---

## [v1.6.2] - 2026-05-26

Polish + release-prep pass on top of v1.6.1.

### Changed
- Codebase-wide text consistency sweep across UI strings, in-app User
  Guide content, comments, log messages, and documentation (README +
  CHANGELOG). Single style for separators throughout the project.
- README and CHANGELOG version badges bumped to v1.6.2.

### Notes
- No functional change versus v1.6.1.
- Localization map preserved; English-key / Hebrew-value pairs still
  match.

---

## [v1.6.1] - 2026-05-26

Polish on top of v1.6.0 - Screen Recorder fit, the new Export
Destination picker, in-app User Guide refresh, and MainWindow layout
tweaks.

### Added
- New `ExportDestinationWindow` - pre-export step that lets the user
  pick where the finished video goes (save / share / both).

### Changed
- Screen Recorder dialog layout tightened so every section fits on
  default-sized windows.
- MainWindow sidebar / topbar minor reshuffles.
- User Guide content updated to reflect the recorder + share-dialog
  flows.

---

## [v1.6.0] - 2026-05-26

Big upgrade to the screen-recording experience and post-export
sharing.

### Added - Screen Recorder
- **Monitor picker**: dropdown listing every attached display via
  Win32 EnumDisplayMonitors. Pick "Entire desktop (all monitors)" or
  one specific monitor. Persists across runs.
- **Live preview** in the recorder dialog, updating ~6×/sec via GDI
  screen capture. Independent of ffmpeg's own capture so it can't
  affect the recording.
- **🔍 Enlarge button** opens the live preview in its own resizable
  window - drag any edge to make it as big as your monitor.
- **HiDPI-aware recording** via ffmpeg's `ddagrab` (Direct3D Desktop
  Duplication). A 1920×1080 monitor at 150% Windows scaling now
  records at its full 1920×1080 instead of the scaled 1280×720 that
  legacy `gdigrab` saw. "Entire desktop" still uses gdigrab (it spans
  multiple monitors).
- **DirectShow camera scan**: webcam mode auto-detects USB cameras
  and shows them in a dropdown with a Refresh button.
- **Diagnostic** under the preview shows the exact rectangle being
  captured plus physical resolution if DPI scaling is in play.

### Added - ShareDialog (used after every recording + export)
- "🎬 Open in editor" loads the file straight onto the timeline.
- "📁 Open folder" reveals it in Explorer.
- "Copy path" copies the path for pasting into web upload dialogs.
- One-click upload-page openers for YouTube / TikTok / X / Instagram,
  brand-coloured. Pre-copies the path + reveals the file so you can
  drag it onto the page that opens.
- Replaces the old "Open output folder?" MessageBox after export.

### Added - UI polish
- **Branded application icon** (purple gradient scissors-and-film-
  strip "Video Editor Pro") visible in Explorer, taskbar, Alt+Tab and
  the window titlebar.
- **Self-contained single-file EXE** - bundles the native WPF DLLs
  (D3DCompiler / PresentationNative / wpfgfx / PenImc / vcruntime)
  inside the .exe so it runs from any folder with no companion files.

### Fixed
- Hide blocks now hide / show only when the playhead is in their
  range - matches export, no more "block over the whole video at
  every paused position".
- ScreenRecorderWindow layout: previous SizeChanged growth handler
  pushed Output file + Recording source out of the ScrollViewer's
  view. Replaced with a fixed-height preview at a roomier default
  window size.
- Topbar ScrollViewer prevents the Export button from being clipped
  on narrow windows.
- AI Captions HTTP timeout raised from 2 → 10 minutes; the dialog
  no longer falsely surfaces "Cancelled" when Gemini is just slow.
- Hide block START/END inputs split into separate hours / minutes /
  seconds / milliseconds fields with proper overflow rollover.

---

## [v1.5.0] - 2026-05-25

Major update: AI Captions, Canvas Transform, project formats, and lots of polish.

### Added - AI Captions ✨
- Auto-generate kinetic-typography captions from the spoken audio in a clip.
- Local Whisper.cpp transcription - free, no cloud upload for the audio
  itself. Five model sizes from Tiny (75 MB) to Large-v3-Turbo (800 MB,
  recommended for Hebrew).
- Google Gemini turns the cleaned transcript into short, punchy on-screen
  captions and **fixes obvious Whisper transcription errors using
  full-sentence context** (e.g. Hebrew lyric "שוב חוזרים ללוודה הנקודה" →
  "שוב חוזרים לאותה הנקודה").
- Caption translation: keep the source language *or* translate into
  Hebrew / English / Arabic / Spanish / French / Russian / Portuguese / German.
  Great for fan-subbing courses or shows.
- Optional **fallback API key** from a second Google account - kicks in
  automatically when the primary key hits its daily 429 quota, doubling
  the free-tier cap.
- Daily Gemini request counter visible in Settings → AI Captions and in
  the AI Captions dialog itself.
- Automatic model probing - tries 2.5-flash → 2.0-flash → 2.5-flash-lite
  → 1.5-flash, so the key works on any Google project regardless of
  which model that project has free quota for.

### Added - per-clip Canvas Transform ⛶
- Zoom + offset on the project canvas, per-clip.
- Direct manipulation: four corner handles, drag the preview to pan,
  scroll-wheel to zoom (Ctrl+Scroll = finer), double-click or right-
  click to reset.
- Sliders in the right-pane CANVAS section as a precise alternative.
- Dashed purple frame shows the editable canvas bounds.
- Handles auto-hug the actual displayed video, not the canvas - so
  portrait clips in a 16:9 canvas don't put the handles in the side
  letterboxing.

### Added - Project formats 📐
- Presets: YouTube 16:9, Reels / TikTok / Shorts 9:16, Square 1:1,
  Portrait 4:5, Cinematic 21:9, plus custom width × height.
- Fit modes: Contain (letterbox), Cover (crop edges), Blur background
  (fill bars with a blurred copy of the clip).

### Added - Hide blocks
- Dedicated milliseconds field next to seconds in the START / END
  inputs (H : M : S . MS), each in its own box.
- Visibility now matches export: a block only appears when the playhead
  is in its range - paused or playing. The currently-selected block
  stays visible regardless so it remains editable.

### Added - UI
- Bilingual English / Hebrew interface with full RTL layout for Hebrew.
- Light / Dark / Auto theme with live switching (no restart needed).
- Self-pacing export progress: bar moves smoothly even within a single
  long clip, with a percent label overlay.
- Topbar wrapped in a horizontal ScrollViewer for narrow windows so the
  Export button never gets clipped.

### Changed
- Whisper output is now read as UTF-8 explicitly - Hebrew transcripts
  no longer come out as mojibake.
- Scrubbing the yellow playhead routes through a 35 ms coalescing
  timer so dragging doesn't choke MediaElement with ~100 Position
  writes/second; audio is no longer choppy during scrub.
- Text-overlay preview controls now use dirty-tracking - adding 40+
  AI Captions to a timeline no longer makes scrubbing stutter.
- Topbar icons: pause is now two hand-drawn bars (no longer rendered
  as a coloured emoji square that looked identical to Stop); settings
  is the Segoe MDL2 Assets gear (no longer rendered as a flower).
- HttpClient timeout for Gemini bumped 2 → 10 minutes; long video
  prompts no longer surface a fake "Cancelled" status.
- Topbar layout uses four explicit columns so transport buttons and
  the volume slider don't hide behind the project metadata on narrow
  screens.
- Project meta line trims to 180 px max + ellipsis so very long
  project names can't push the transport off-screen.

### Documentation
- In-app User Guide (F1) rewritten end-to-end in bilingual plain
  language. 15 sections including four new ones: Project Formats,
  Canvas Transform, Text Overlays, AI Captions.
- README restructured English-first → separator → Hebrew, with the
  full feature list and a Download-Latest badge.

### Dependencies bundled at first run
- whisper.cpp + the selected ggml model (auto-downloaded into
  `whisper/` next to the EXE).
- yt-dlp.exe (unchanged from v1.0.0, for URL imports).
- FFmpeg.exe + ffprobe.exe (unchanged, for all video processing).

### Security
- `settings.json` (which holds the LLM API keys) is now explicitly
  ignored in `.gitignore` - defence in depth on top of `publish/` and
  `bin/` already being ignored.

---

## [v1.0.0] - 2026-05-21

First public release.

- Multi-clip timeline with drag-and-drop import, ripple or free-drag,
  per-clip trim / speed / volume / rotate / flip / loop.
- Hide blocks with Solid / Blur / Pixelate modes and per-block time
  ranges (H : M : S inputs).
- Text overlays via a visual picker (drag-on-frame positioning) and
  draggable timeline bars for appearance time.
- Tool dialogs: Trim, Merge, Crop, Resize, Rotate, Flip, Loop, Speed,
  Stabilize, Remove Logo, Add Image, Add Text, Add Audio, Change Volume,
  Extract Audio, Mute / Remove Audio Track.
- Capture: Screen Recorder, Webcam Recorder, Text-to-Speech, URL Import
  (YouTube / TikTok / Vimeo / Twitter / Instagram / Facebook / Twitch /
  Reddit via yt-dlp).
- Export: H.264 (default), H.265, AV1, ProRes; MP4 / MOV / MKV / WebM
  containers; CRF / FPS / audio-bitrate controls.
- Settings: General / Player / Editor / Export / Storage / FFmpeg /
  Keyboard / Updates / About sections.
- Bilingual UI: English + Hebrew (with RTL support).
- Light / Dark theme with live switching.
- Self-contained single-file Windows EXE - no installer, no admin
  rights, no .NET install required (~150 MB).

[v1.6.2]: https://github.com/YossiYad/video-editor/releases/tag/v1.6.2
[v1.6.1]: https://github.com/YossiYad/video-editor/releases/tag/v1.6.1
[v1.6.0]: https://github.com/YossiYad/video-editor/releases/tag/v1.6.0
[v1.5.0]: https://github.com/YossiYad/video-editor/releases/tag/v1.5.0
[v1.0.0]: https://github.com/YossiYad/video-editor/releases/tag/v1.0.0
