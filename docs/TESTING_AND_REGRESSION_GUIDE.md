# HYPNIX testing and regression guide

This guide records the failure modes discovered while building HYPNIX and the automated or manual gates that
prevent them from returning.

## Running the automated suite

```powershell
dotnet test Tests\Hypnix.Tests\Hypnix.Tests.csproj -c Release
```

Release is recommended while the Debug HYPNIX executable is open because Windows locks the running app host.
If feeds are unavailable but packages already exist locally:

```powershell
$env:NUGET_PACKAGES = "$env:USERPROFILE\.nuget\packages"
dotnet restore Tests\Hypnix.Tests\Hypnix.Tests.csproj --ignore-failed-sources
dotnet test Tests\Hypnix.Tests\Hypnix.Tests.csproj -c Release --no-restore
```

`NU1900` warnings mean NuGet could not retrieve vulnerability data from a configured feed. They are separate
from compilation and test results; always check the final passed/failed count.

## Automated regression coverage

### Per-monitor visualizer pause

The original defect froze animation time while continuing to feed current FFT values to the paused monitor.
The visualizer therefore still reacted to music. Tests now require:

- paused display: frozen time and a cloned FFT snapshot;
- other display: current time and current FFT values;
- clearing pause: immediate return to live values;
- ownership isolation: callers cannot mutate the snapshot through an array reference.

Rule: clone mutable audio arrays at every frozen-state boundary.

### Per-monitor background fitting

A background fitted once to the combined virtual desktop is wrong when monitors have different aspect ratios.
Tests cover matching, portrait, and ultrawide targets and require centered `cover` cropping calculated inside
each monitor rectangle.

### Wallpaper package contracts

Both visualizer variants must declare a valid local manifest, preview, background, WASAPI source, and
independent multi-monitor fitting. Missing or renamed assets fail before runtime.

### Tray artwork contracts

The notification-area asset is intentionally different from the application icon. Tests require transparent
corners, a white high-contrast mark, at least 85% horizontal and 75% vertical canvas use, sufficient visible
pixels at small sizes, and application code referencing the dedicated tray ICO.

### UI contracts

The suite protects the separate classic/flame visualizer choices and the approved `2.7x` intensity ceiling.
These checks intentionally fail if a future layout rewrite silently removes existing product behavior.

### Downloaded preset safety

Tests require valid internal-renderer presets to register, future renderers to remain recognized but disabled,
and unknown renderers, executable files, and paths escaping the package root to be rejected. Discovery never
authorizes code execution.

### Frame-rate policy

The supported live caps are 15, 30, and 60 FPS. Unsupported values fall back to 30 FPS. Sixty FPS improves
temporal smoothness on capable hardware, but does not turn the current CPU renderer into a GPU renderer.

## Common pitfalls and safeguards

1. **Freezing only a clock**
   - Symptom: audio-reactive content still moves on a paused display.
   - Safeguard: snapshot every live frame input, including FFT bands.
2. **Sharing mutable arrays**
   - Symptom: frozen state changes after later capture callbacks.
   - Safeguard: clone when storing and when returning a snapshot.
3. **Using the virtual-desktop aspect ratio**
   - Symptom: backgrounds or videos fit one monitor but stretch on another.
   - Safeguard: cover-fit independently for each `WallpaperTarget`.
4. **Reusing a padded application icon in the tray**
   - Symptom: tiny mark inside a dark square at 16 px.
   - Safeguard: transparent monochrome artwork with tight bounds and a multi-resolution ICO.
5. **Combining UI and renderer lifecycles**
   - Symptom: closing the UI stops the wallpaper or leaks resources.
   - Safeguard: X hides; only explicit tray Quit disposes.
6. **Relying only on unit tests for native integration**
   - Symptom: tests pass while Explorer parenting, DWM, Z-order, DPI, or tray output is visually wrong.
   - Safeguard: run the manual Windows matrix below.
7. **Building over a running executable**
   - Symptom: `MSB3021`/`MSB3027` because HYPNIX locks its app host.
   - Safeguard: test Release/validation output; use tray Quit before replacing Debug.
8. **Treating feed warnings as test failures**
   - Symptom: a passing suite is reported as broken due to unavailable NuGet audit feeds.
   - Safeguard: report feed warnings separately from the final test result.
9. **Installing arbitrary renderer code from a wallpaper package**
   - Symptom: a downloaded DLL, EXE, or script becomes a code-execution path.
   - Safeguard: packages select only allowlisted renderers compiled into HYPNIX and contain declarative assets.
10. **Assuming higher FPS fixes rendering cost**
   - Symptom: 60 FPS is selected but CPU frame time cannot stay below 16.67 ms.
   - Safeguard: retain 15/30 fallbacks, measure frame time, and move fluid effects to Direct3D/HLSL.

## Manual Windows regression matrix

Run before releases and Store submissions:

- Switch among ambient, classic visualizer, flame visualizer, and MP4 without manually stopping first.
- Confirm both visualizers react to real system audio on both displays.
- Enable `Pause only the display in use` + set `Maximized or fullscreen apps`; maximize an app on display 1 and confirm only display 1 freezes while display 2 keeps animating.
- Maximize an app on display 2 as well and confirm both displays freeze; minimize one and confirm that display resumes while the other stays frozen.
- Confirm a transparent/tool overlay (e.g. the NVIDIA GeForce overlay) does not pause a monitor that has no real app on it.
- Or run the automated multi-monitor attestation: `powershell -ExecutionPolicy Bypass -File scripts\e2e-permonitor-pause.ps1` (requires two displays).
- Confirm backgrounds crop independently without stretching on mixed-resolution monitors.
- Close the UI: wallpaper remains active and the transparent white tray icon appears.
- Restore by double-click, test Stop, restart, then Quit and confirm renderer and process disappear.
- Test 100%, 125%, 150%, and mixed-DPI layouts with the title bar reachable.
- Test speakers, Bluetooth, USB, and wireless headsets without forcing 44.1 kHz.
- Restart Explorer during playback once Explorer-restart recovery is implemented.

## Rules for future tests

- Extract calculations and state transitions into internal pure classes; keep Win32 handles out of unit tests.
- Add a failing reproduction test before fixing a defect.
- Test multiple monitor shapes, not only current physical resolutions.
- Extend asset-contract coverage whenever a wallpaper type gains required files.
- Assert the state or geometry that prevents the visible defect, not merely that a method returned successfully.
# Volumetric fire research gate

The rejected volumetric prototypes are documented in
[`VOLUMETRIC_FIRE_RESEARCH.md`](VOLUMETRIC_FIRE_RESEARCH.md). The experimental
card must remain collapsed in production-facing UI until a new implementation
passes visual review against `docs/assets/volumetric-fire-approved-target.png`.
Compilation, nonzero GPU buffers and smooth animation are necessary but are not
visual acceptance criteria.

## Reliability and library implementation (September 2026)

New behavioral coverage exercises repeated audio retry failures and cancellation, invalid audio samples,
battery precedence, locked-session pause, per-display transitions, failed preparation/show, stop-during-prepare,
late selection completion, video reader pause/cancellation, persisted independent settings, malformed settings,
aspect-preserving video decode dimensions, incremental package limits and revalidation before playback.
Catalog assertions now inspect loaded wallpaper entries instead of depending on hardcoded XAML labels.

`Tests/Hypnix.NativeSmoke` separately checks real native CPU and GPU hosts, pause/resume and destruction,
manifest packaging and shell construction at two sizes. Optional arguments run a real ffprobe/ffmpeg first-frame
and pause/resume check. Run it from the repository root on Windows with Direct3D 11 hardware.
The check uses hidden parent windows and does not attach to Explorer. Layout PNGs exclude HWND-hosted preview content.

## Desktop end-to-end smoke test (`scripts/e2e-desktop-smoke.ps1`)

`Tests/Hypnix.NativeSmoke` deliberately never touches Explorer. The desktop smoke closes that gap: it drives the
**real, built application** through Windows UI Automation, attaches each wallpaper to the **live Windows desktop**
exactly as a user would, plays audio into the default render endpoint so the WASAPI-loopback visualizers react,
and screenshots the desktop for visual attestation. It then presses Stop, restores the user's `settings.json`
from a backup, and terminates the process.

### Requirements

- **Windows PowerShell 5.1** (the `UIAutomationClient`/`UIAutomationTypes` assemblies are .NET Framework GAC
  assemblies and are not available under PowerShell 7 / .NET Core; the script refuses to run there).
- An **interactive desktop session**, **Direct3D 11** hardware, and an **audio render endpoint**.
- A built `HYPNIX.exe` and **ffmpeg/ffprobe** (auto-detected on `PATH` or under the winget package cache, or
  passed with `-FfmpegDir`). ffmpeg is used only to generate the throwaway test video and pink-noise clip.

### Running

```powershell
dotnet build -c Release
powershell -ExecutionPolicy Bypass -File scripts\e2e-desktop-smoke.ps1 -Configuration Release
```

Useful switches: `-SkipVideo` (skip the local-video card), `-RefreshMedia` (regenerate the test clip/wav),
`-Exe` / `-FfmpegDir` / `-OutDir` to override auto-detection. The script prints `E2E-DESKTOP: PASS` and exits
`0` on success, or lists failures and exits `1`.

### What it does (each step attaches to the real desktop)

1. Backs up `%LOCALAPPDATA%\HYPNIX\settings.json`, then injects a deterministic config: `AppPauseMode = Never`
   (so captures are not paused by foreground changes) plus a temporary video card pointing at the generated clip.
2. Launches the app, locates the `HYPNIX` window, `StartButton`/`StopButton`, and the `WallpaperGallery` list.
3. Captures a baseline, clicks **Start** on *Built-in ambient*, then switches through *Audio Visualizer*,
   *Aethelis Audio Reactive*, *Fire Burst Experimental*, and the *E2E Test Clip* video — playing pink-noise into
   the default output during the audio-reactive modes. Wallpaper switches happen live, without pressing Stop.
4. For each mode it minimizes all windows (`Shell.MinimizeAll`, deterministic — no toggle drift), screenshots the
   virtual desktop, then restores the windows.
5. Presses **Stop**, captures the restored desktop, terminates the app, and **always restores** the settings
   backup in a `finally` block (even on error). It fails if any HYPNIX process survives cleanup.

### Attestation — what each artifact proves

Artifacts are written to `artifacts\e2e-desktop\`:

| Capture | Proves |
| --- | --- |
| `00-baseline.png` | Original desktop before attachment. |
| `01-ambient.png` | Native procedural wallpaper (grid + particles) rendered on the desktop behind icons. |
| `02-visualizer-audio.png` | Classic GDI visualizer, per-monitor cover-fit background, spectrum bars reacting to system audio. |
| `03-aethelis-audio.png` | Direct3D 11 GPU visualizer (corona/ring) composited on the desktop on every display. |
| `04-fireburst-audio.png` | GPU + Effekseer beat-driven burst, audio-triggered, on every display. |
| `05-video.png` | ffmpeg-decoded video wallpaper playing live (animated frame counter), cover-fit per monitor. |
| `06-stopped.png` | Desktop restored to baseline after Stop (near-identical size to `00-baseline.png`). |

A run passes attestation when `00`/`06` match the bare desktop, `01`-`05` each show their distinct animated
wallpaper across all monitors, the two audio-reactive captures show non-flat spectra, and the script exits `0`.

Additional manual release checks:

- Disconnect the default audio output through multiple retry intervals, then reconnect; verify automatic recovery.
- Change the default endpoint while the old endpoint remains connected.
- Enable battery pause plus active-display scope; on battery, every desktop target and the preview must stop.
- Rapidly select videos and press Stop during preparation; no late session may become active.
- Select a missing/invalid video while another wallpaper runs; the old wallpaper must remain active.
- Verify preview animation for every selectable renderer, and no preview processing while hidden/minimized.
- Copy a valid preset to the library, select it and adjust controls; restart and confirm settings remain independent.
- Restart Explorer, change resolution/DPI, reconnect monitors and resume from sleep; confirm session reconstruction.
- Confirm three consecutive preparation failures during recovery stop playback with an actionable error.

The native build is pinned and executable through `scripts/build-native.ps1`; `-UpdateRuntime` is an explicit opt-in
because the existing committed runtime remains the default for ordinary .NET builds.
