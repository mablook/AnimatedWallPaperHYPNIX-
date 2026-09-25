# HYPNIX testing and regression guide

This guide records the failure modes discovered while building HYPNIX and the automated or manual gates that
prevent them from returning.

## Running the automated suite

```powershell
dotnet test Tests\Hypnix.Tests\Hypnix.Tests.csproj -c Release
```

Release is recommended while the Debug HYPNIX executable is open because Windows locks the running app host.
If Release is also running, keep its files separate from validation output:

```powershell
dotnet test Tests/Hypnix.Tests/Hypnix.Tests.csproj -c Release "-p:OutDir=$PWD/artifacts/validation/unit/"
```

If feeds are unavailable but packages already exist locally:

```powershell
$env:NUGET_PACKAGES = "$env:USERPROFILE\.nuget\packages"
dotnet restore Tests\Hypnix.Tests\Hypnix.Tests.csproj --ignore-failed-sources
dotnet test Tests\Hypnix.Tests\Hypnix.Tests.csproj -c Release --no-restore
```

`NU1900` warnings mean NuGet could not retrieve vulnerability data from a configured feed. They are separate
from compilation and test results; always check the final passed/failed count.

## Continuous integration

The [Windows build and tests workflow](../.github/workflows/ci.yml) runs on pushes to `main` and `codex/**`,
pull requests targeting `main`, and manual dispatch. It uses Windows Server 2022 and selects the .NET 8 SDK
inside the CI checkout so newer preinstalled SDKs do not silently change the compiler used for validation.

The job restores and builds `Hypnix.Tests` and `Hypnix.NativeSmoke` with their application reference in Release,
then executes `Hypnix.Tests`. A restore, build or test failure fails the job. TRX reports are uploaded even after
a test failure, when available, as `windows-regression-results` with a 14-day retention period.
It uses the committed native DLL and assets; it does not rebuild the Effekseer runtime.

The job compiles the native smoke project but does not execute GPU rendering, WASAPI capture or desktop
attachment. Run those gates locally on an interactive Windows machine with Direct3D 11 hardware.
A green CI run does not certify visual quality, multi-monitor desktop integration or video playback.
The workflow must be committed and pushed before GitHub can execute it. Requiring its check before merging
is a separate repository branch-protection setting; adding this file does not enable that setting.

## Automated regression coverage

### Optional microphone reaction

The microphone remains off for new and legacy preferences until explicitly enabled. Router tests cover
shared desktop/preview subscriptions, independent silent or unavailable inputs, mixing frequency bands
without doubling them, clearing the microphone contribution on disable, rejecting late callbacks,
expiring stale input and restoring fresh streams after all consumers pause or stop.

Focused native checks:

```powershell
dotnet run --project Tests/Hypnix.NativeSmoke -c Release -- artifacts/microphone-ui --microphone-ui
dotnet run --project Tests/Hypnix.NativeSmoke -c Release -- artifacts/microphone-lifecycle --microphone-lifecycle
dotnet run --project Tests/Hypnix.NativeSmoke -c Release -- artifacts/microphone-live --microphone-probe
```

The UI fixture checks actual Sound/tray controls and preference persistence without opening an audio
device. The lifecycle fixture verifies that re-enabling audio on a paused native session keeps capture
closed until resume, and that disabling/disposal releases its subscription. Its microphone remains off.
The explicit live probe uses the microphone for five seconds and retains only frame counts and
peak band values. It saves no audio and waits for the endpoint worker to close.

Verified on 2026-09-23: Release build had zero warnings/errors; 353 unit tests passed; the microphone UI
and lifecycle fixtures passed, and the rendered Sound section was inspected. The local microphone opened and produced
230 silent FFT frames, then closed successfully. Voice reaction on physical hardware remains unverified
because this probe received silence. Build validation used `UseSharedCompilation=false` and isolated
intermediate/output directories after the shared compiler could not write its output.

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

The suite protects the separate classic/fire visualizer choices and the current control ceilings:
intensity `8`, audio sensitivity `12` and glow `3`. Preferences also preserve independent wallpaper settings
and the global Audio reactive toggle across restarts.
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

- Switch through all thirteen built-in wallpapers and a local MP4 without manually stopping first.
- Confirm every audio-reactive wallpaper reacts to real system audio on both displays.
- Toggle Audio reactive in both the window and tray; verify desktop and preview return to calm animation,
  resume reacting when enabled, and retain the preference after restart.
- On a fresh profile, confirm **React to microphone** is off and the app does not open an input endpoint.
  Enable it in Sound, then in the tray, with **Audio reactive** on; confirm the two controls stay synchronized,
  the choice survives restart, and speaking into an available microphone animates the wallpaper and preview
  while system playback is silent. Confirm system playback still drives reaction independently.
- Turn **React to microphone** off and verify the microphone is released while system-audio reaction remains
  active. Turn **Audio reactive** off and verify both sources stop. Stop desktop playback and close/minimize
  all previews; confirm microphone access ends when no audio consumer remains.
- Test no microphone, denied Windows microphone access, unplug/reconnect and a changed default input.
  Verify no blocking prompt or crash, system-output reaction continues, and an enabled microphone source
  recovers when input becomes available. Verify audio is not saved to files, logged as samples, sent over the
  network or monitored through the speakers.
- Enable `Pause only the display in use` + set `Maximized or fullscreen apps`; maximize an app on display 1 and confirm only display 1 freezes while display 2 keeps animating.
- Maximize an app on display 2 as well and confirm both displays freeze; minimize one and confirm that display resumes while the other stays frozen.
- Confirm a transparent/tool overlay (e.g. the NVIDIA GeForce overlay) does not pause a monitor that has no real app on it.
- Or run the automated multi-monitor attestation: `powershell -ExecutionPolicy Bypass -File scripts\e2e-permonitor-pause.ps1` (requires two displays).
- Confirm backgrounds crop independently without stretching on mixed-resolution monitors.
- Close the UI: wallpaper remains active and the transparent white tray icon appears.
- Restore by double-click, test Stop, restart, then Quit and confirm renderer and process disappear.
- Test 100%, 125%, 150%, and mixed-DPI layouts with the title bar reachable.
- Test speakers, Bluetooth, USB, and wireless headsets without forcing 44.1 kHz.
- Restart Explorer during playback and confirm the active wallpaper is reconstructed.

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

The default native smoke run retains lifecycle checks for ambient, classic visualizer, Aethelis, Fire Burst
and Flamethrower Ring V2, and includes Spectral Bloom, Neon Ribbons, Liquid Orbs, Event Horizon,
Fractal Pyramid, Kaleidoscope, Lotus and Living Fire.
It asserts all thirteen entries are present in the built application's gallery. Additional GPU image checks cover:

| Wallpaper | Checks |
| --- | --- |
| Neon Ribbons / Liquid Orbs / Fractal Pyramid / Kaleidoscope / Lotus | Animation in silence, quiet-audio response, intensity/glow range, black output at zero intensity, size/position controls and viewport origin. |
| Aethelis | Animation in silence, plus the standardized size/position and color-theme controls each visibly change the output (warm default vs. ice-blue palette). |
| Spectral Bloom | Exposure at 15/30/60 FPS, audio response, individual FFT-band influence, size/position effect on the emitted silk, and pixel-exact independent viewport freeze. |
| Event Horizon | Non-flat image, animation in silence, audio response, intensity/glow/color/size/position controls, return to silence and independent viewport freeze with saved time/audio inputs. |
| Living Fire | Shared fluid solver, controls and zero intensity, no settings-only time advancement, separate monitor state, frozen pixels, resume without catch-up, and D3D timestamp samples at 640 × 360 and 4K. The study harness separately checks material/particle invariants and source response to audio. |

PNG artifacts are saved under the requested output directory. The `--spectral-only` option still runs the
Spectral Bloom image checks alone. Its independent-freeze renderer is disposed before another swap chain is
created for the same HWND. These checks validate renderer behavior; they do not replace visual review or the
real desktop pause script. The desktop smoke script below covers its listed subset; manually exercise the
remaining gallery entries before release.

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

## First-frame timeout and deferred render cleanup

### Layered child compatibility for the two GDI wallpapers

```powershell
dotnet build Tests/Hypnix.NativeSmoke -c Release -p:UseSharedCompilation=false
& ./Tests/Hypnix.NativeSmoke/bin/Release/net8.0-windows10.0.19041.0/Hypnix.NativeSmoke.exe artifacts/layered-child --layered-child
```

Launch the executable, not `dotnet Hypnix.NativeSmoke.dll`: the apphost's embedded Windows
compatibility manifest is part of this regression. The harness shares production `app.manifest`.
`LayeredChildWindowChecks` creates only owned, hidden HWNDs. It verifies direct layered-child
creation, then the Ambient and Audio Visualizer desktop constructors followed by child-style
conversion, reparenting, alpha preservation, geometry, GDI frames, paused reveal and disposal.
No user preferences, Explorer windows or audio devices are changed.

On 2026-09-25, the same code built with the old manifest failed direct layered-child creation;
adding the Windows 10/11 `supportedOS` declaration passed that check and both GDI lifecycles.
Both Release builds had zero warnings/errors. `layered-child-windows.json` captures native
states and failures. Source, shipped 1.1.1 and rebuilt EXE manifests were compared. These
checks establish runtime compatibility; final Store/Explorer visual validation is separate.

### Applying a wallpaper while automatic pause is active

`PausedWallpaperRevealChecks` reproduces hidden preparation followed by `Pause()` and `Show()`
for the first three gallery entries: Built-in ambient, Audio Visualizer and Aethelis Audio Reactive.
It requires one completed presentation after reveal, a stopped animation clock and no ongoing
rendering until resume. The two GDI modes also require identical prepared, revealed and paused
pixels. Aethelis checks render/present counters and time; it does not compare GPU pixels.

```powershell
dotnet run --project Tests/Hypnix.NativeSmoke -c Release -- artifacts/paused-reveal --paused-reveal
```

The fixture uses a hidden parent without changing Explorer, user settings or audio devices.
It saves counter evidence to `paused-wallpaper-reveal.json` and GDI images to the output folder.
On 2026-09-25, restoring the old paused guard reproduced an Ambient timeout after `Show()`;
the corrected build passed all three modes (1 prepared, 2 after reveal, still 2 while paused,
at least 5 after resume). The Release build had zero warnings/errors and all 353 unit tests passed.
This proves the paused-reveal regression; visual application on the desktop from a newly built
Store package remains a release check. The installed Store 1.1.1 package was not replaced.

### GDI wallpaper GPU presentation (Windows 11 raised desktop)

The deferred desktop visual check (`scripts/e2e-desktop-smoke.ps1`) found that Built-in ambient
and Audio Visualizer rendered black on a real Windows 11 raised desktop while GPU wallpapers
displayed, because a GDI child of `Progman` (which carries `WS_EX_NOREDIRECTIONBITMAP`) is not
composited by DWM. The fix presents the GDI back buffer through a DXGI swap chain
(`GdiWallpaperSwapChain`, `Shaders/GdiPresent.hlsl`); see `DESKTOP_INTEGRATION_FINDINGS.md`.

`--gdi-present` verifies the presenter headlessly: it renders a two-colour frame through the swap
chain and reads the back buffer back, asserting the presented colours. It needs Direct3D 11 but
no desktop, Explorer or audio device (owned hidden STATIC window).

```powershell
dotnet run --project Tests/Hypnix.NativeSmoke -c Release -- artifacts/gdi-present --gdi-present
```

On 2026-09-25 the presenter check passed (upper R=200,G=30,B=40; lower R=40,G=60,B=200) and the
desktop e2e was re-run against the fixed build: `01-ambient.png` shows the ambient gradient with
drifting orbs and `02-visualizer-audio.png` shows the audio-reactive ring, with the GPU wallpapers
(`03`, `04`) unchanged. Release build zero warnings/errors; 367 unit tests passed; paused-reveal,
layered-child and settings checks still pass. Screenshots are under `artifacts/e2e-desktop-fixed`.
The installed Store package was not replaced; this fix is in source only.

### Startup timeout and cleanup

`WallpaperRenderWorkerTests` holds initialization behind a gate to reproduce both successful and
failed completion after startup and shutdown timeouts. It requires that timed-out initialization
never enters the ongoing render loop, cleanup runs exactly once on the render thread, and notifications
are safe both before and after cleanup. A blocked-frame test also verifies deferred shutdown cleanup.
A controller regression also verifies that a timed-out first frame preserves the previous wallpaper
and its paused state, even after the delayed initializer finishes.

A first frame must be ready within 15 seconds. Timeout fails preparation instead of revealing an
empty host. Shutdown waits up to five seconds; if the thread is still inside native code, it retains
its resources and hidden HWND until it exits. Resource cleanup then runs on the render thread and
HWND destruction is dispatched to the owning UI thread. If the UI dispatcher has already shut down,
Windows reclaims the HWND at process exit.

The native smoke switches the same settings window across all visualizer entries and verifies that
size/position and the color palette appear exactly on the renderers that honor them (both hidden only
for the two Effekseer fire effects), driven by the `SupportsLayoutControls`/`SupportsColorTheme`
capabilities. GPU comparisons separately require size/position and color to change the image on
Aethelis and Spectral Bloom in addition to the abstract shaders. Preference tests round-trip
different transforms for separate wallpapers.

The portable inventory regression compares the packaging script's renderer list with the visible
manifest-driven gallery, so a newly added wallpaper cannot silently disappear from `build-info.json`.

## Graphite settings, presets and Living Fire backgrounds

`VisualizerCustomizationTests` exercises the reversible square/disk position mapping,
out-of-range drags, old settings, persistence of background/sparks/presets and rejection
of invalid preset records. `VisualizerCapabilityTests` locks the standardized control contract:
every visualizer's palette and size/position support matches its renderer, the two Effekseer effects
hide both, the full-screen shaders consume Scale/OffsetX/OffsetY and the palette, and the GDI
visualizer applies the shared transform. The regression suite currently contains 177 passing cases.

`SettingsWindowChecks` runs within the native smoke harness. It verifies that loading
settings and switching View/Effects/More do not emit changes; geometry reset preserves
appearance; reset undo restores the complete snapshot; presets save/apply/rename complete
preferences and remain scoped to their wallpaper. It also checks background capabilities
and captures each tab at normal and minimum window sizes.

```powershell
dotnet Tests/Hypnix.NativeSmoke/bin/Release/net8.0-windows/Hypnix.NativeSmoke.dll artifacts/settings-review --settings-only
```

The full smoke includes these checks plus all 13 wallpaper lifecycles. `--fire-only`
also exercises solid/image composition, importing an image, missing-file fallback,
background independence from the fire transform, zero intensity with a background,
sparks visibility, frequency separation, full-width coverage and independent pauses.
The shared FirePreview capture harness checks sustained physics, audio and particle state.

Local results for the redesign are in `artifacts/settings-redesign/`. WPF render captures
validate layout but do not prove native caption behavior, screen-reader usability or
physical mixed-DPI operation. Keep those manual release checks, including inactive
caption colors, maximize/restore, keyboard navigation, high contrast and display changes.
