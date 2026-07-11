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
- Select `Active display only`; foreground an app on display 1 and confirm only display 1 freezes completely.
- Move the foreground app to display 2 and confirm the frozen visualizer follows it.
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
