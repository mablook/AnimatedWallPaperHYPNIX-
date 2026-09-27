# HYPNIX

**Relax. Have fun. Be cool.**

Give your Windows desktop a mood of its own. Put on music in your usual player,
watch audio-reactive animations respond in real time, and choose a look for
relaxing, enjoying the beat or making your setup feel like you.
See the [positioning and sales strategy](docs/MARKETING_STRATEGY.md).

Licensing preparation: Microsoft Store distribution with Lemon Squeezy purchases.
Lemon Squeezy manages the commercial operation; HYPNIX development focuses on the
application and licence integration. See [responsibilities](docs/LEMON_SQUEEZY.md#responsibility-boundary--owner-direction-2026-09-21).
See [commerce setup and release gates](docs/LEMON_SQUEEZY.md). Version 1.1.1 uses
the verified Live product configuration. The final MSIX's licensing classes passed
Live activation, validation, encrypted persistence, restoration and deactivation.
Store distribution remains pending; see [the build record](docs/STORE_LIVE_BUILD_20260923.md).
The publication guard still rejects Test or malformed configurations.
See the [validation report](docs/LEMON_SQUEEZY_TEST_REPORT.md) and
[historical development handoff](docs/DEVELOPMENT_STATUS_20260921.md).

Windows desktop app for local animated and audio-reactive wallpapers, built with WPF/.NET 8,
Win32 desktop hosting, WASAPI and Direct3D 11.

Support: [hello@mablook.com](mailto:hello@mablook.com).

Current development version: **1.2.2**. See the [release record](docs/DEVELOPMENT_RELEASE_1.2.2.md)
and [build, logging and manual-test workflow](docs/DEVELOPMENT_RELEASE_WORKFLOW.md).

## Current behavior

- Thirteen built-in wallpapers in a manifest-driven gallery: ambient, classic audio visualizer, Aethelis,
  Fire Burst, Flamethrower Ring V2, Spectral Bloom, Neon Ribbons, Liquid Orbs, Event Horizon,
  Fractal Pyramid, Kaleidoscope, Lotus and Living Fire.
- Live preview uses the wallpaper renderer with a separate presentation path: GPU previews render offscreen
  and copy pixels to GDI, without a window DXGI swap chain. Desktop and preview share audio analysis.
- **Audio reactive** in the window and tray toggles audio reaction for both desktop and preview;
  the preference persists across restarts. It does not mute other applications or stop animation.
- **Audio source** in Sound and the tray selects system audio, microphone, or both. Playback and microphone
  devices can be selected independently or follow Windows defaults. Microphone input is optional;
  new profiles use system audio. The settings meter and explicit microphone Test show input activity
  without saving audio or playing it through the speakers. See the [privacy policy](docs/PRIVACY_POLICY.md).
- Per-wallpaper intensity (0–8), audio sensitivity (0–12), glow (0–3) and four color themes.
  Every control the settings window shows actually affects its wallpaper: the color themes recolor
  every visualizer (the classic visualizer, Aethelis and all shader wallpapers), except the two
  Effekseer fire effects (Fire Burst, Flamethrower Ring V2) whose colors live in the authored effect
  and therefore hide the palette and size/position controls instead of showing ones that do nothing.
- One neutral black/white theme across both windows, with a single blue accent used only on the
  primary buttons and interactive states (hover, selection, focus). Title bars match the native
  Windows caption: minimize/maximize hover gray and close hovers red.
- Visualizer settings with **View / Effects / More** tabs and a matching custom title bar.
  View combines a circular position pad, numeric values and zoom; Effects groups palettes, glow,
  intensity and audio; More manages named presets, wallpaper defaults and undoing the last reset.
  Presets are independent per wallpaper and persist across restarts.
- A solid-color or local-image background can sit behind every shader visualizer (Neon Ribbons,
  Liquid Orbs, Event Horizon, Fractal Pyramid, Kaleidoscope, Lotus, Spectral Bloom, Aethelis) and
  the classic visualizer, composited (screen blend) so the effect's dark areas reveal it. The two
  Effekseer fire effects and video (itself the background) opt out. Imported images are copied into
  the local library and stay fixed when you move or zoom the effect.
- Living Fire runs at four times its original simulation speed, distributes sources across the full
  monitor width according to aspect ratio, and routes treble left through mids to bass right.
  Its settings support an original, solid-color or local-image background and a sparks toggle.
  Imported backgrounds are copied into the local library; moving the fire leaves its background fixed.
- A dedicated settings window exposes size (0.3–3) and position X/Y (-1–1) for the classic visualizer,
  Aethelis, Spectral Bloom, Neon Ribbons, Liquid Orbs, Event Horizon, Fractal Pyramid, Kaleidoscope,
  Lotus and Living Fire. Size/position uses one convention everywhere (X+ right, Y+ up, measured in
  half-heights so it feels identical at any aspect ratio). The controls are hidden for renderers
  without layout support (the two Effekseer fire effects); preferences persist across restarts.
- Preview stops when the window is hidden/minimized, the session is locked, or the battery pause option applies.
- Safe replacement: prepare the next wallpaper and its first frame before showing it and disposing the previous one.
  Preparation failures, including a 15-second first-frame timeout, keep the current wallpaper.
  Stop also cancels pending preparation. A slow render thread retains its resources until it exits;
  late completion cannot reveal a failed replacement or signal disposed synchronization objects.
- Session creation and the first-frame wait run asynchronously without
  blocking the UI thread, which also lets the shell service the swap-chain hand-off instead of stalling
  behind a blocked owner. On the Windows 11 raised desktop, a failed GPU-composited GDI present is treated
  as an unhealthy session and rebuilt by the recovery loop rather than silently leaving a black monitor.
- Live 15, 30 and 60 FPS presentation caps. Native CPU modes reuse a persistent back buffer.
- Per-monitor pause for maximized/fullscreen apps, all-display or active-display scope, and optional battery
  pause. In active-display scope each covered monitor freezes independently while clean monitors keep animating;
  transparent/tool overlays (e.g. the NVIDIA GeForce overlay) are ignored. Battery and session-lock policies
  take priority over per-display app pause.
- Settings, selected wallpaper, local video references and per-wallpaper visualizer controls persist in
  `%LocalAppData%\HYPNIX\settings.json`. Playback starts explicitly with Start.
- Explorer/host failure detection, display-change recovery and resume handling recreate active sessions.
  Display identity, geometry and DPI snapshots are retained, including disconnected displays.
- **Start with Windows** (App settings ▸ Startup, and the tray) launches HYPNIX at sign-in and restores the
  saved wallpapers. Under MSIX it uses the packaged StartupTask (Windows lets the user override it under
  Task Manager ▸ Startup, which the app reflects); direct/Velopack installs use a per-user `Run` key. An
  auto-started launch comes up minimized to the notification area.
- The tray shows the current version and a **Check for updates** entry (also under App settings ▸ About &
  updates). On the Store build it opens the HYPNIX Store page (the Store applies updates automatically);
  the website installer runs the in-app Velopack check and reports whether an update was downloaded.
- Closing the window hides it to the tray. Tray Stop stops desktop playback; Quit releases the application.
- Single instance per user session: launching HYPNIX again surfaces the running window (restoring it from the
  tray) and exits the new process, so copies never pile up in the notification area.
- Diagnostics are kept under `%LocalAppData%\HYPNIX\logs`, with a 2 MB active log and three rotated files.
  Development packages can additionally retain a separate log per session, with build/version identity;
  About & updates shows the active diagnostics directory. See the development workflow above.

## Local videos

Implementation details and validation: [visual settings](docs/VISUAL_SETTINGS_DESIGN_PLAN.md),
[Living Fire](docs/LIVING_FIRE.md), and [regression guide](docs/TESTING_AND_REGRESSION_GUIDE.md).

Experimental real-time ocean: [spectral surface and comparison preview](docs/OCEAN_P2_IMPLEMENTATION.md),
[atmosphere, sun/moon and lighting comparison](docs/OCEAN_P3_IMPLEMENTATION.md),
[next realism plan for sun, moon and clouds](docs/OCEAN_SKY_REALISM_PLAN.md),
[first native animation](docs/OCEAN_P1_IMPLEMENTATION.md),
[technical review](docs/OCEAN_TECHNICAL_REVIEW.md),
[implementation plan](docs/OCEAN_WALLPAPER_PLAN.md), and
[rendering research and decisions](docs/OCEAN_RENDERING_RESEARCH.md).

Use **Media tools…** to select an existing `ffmpeg.exe` with `ffprobe.exe` in the same folder.
HYPNIX also checks `MediaTools` beside the application and absolute PATH directories. Nothing is downloaded automatically.
Then choose **Add local video…**. The app validates the video stream before adding it to the gallery.

Videos are referenced in their original location, not copied. Keep the source file available.
Playback is muted. Frames preserve the source aspect ratio before cover fitting separately per monitor.
Decode resolution is bounded by 1280×720; presentation follows the selected FPS cap, while the decoder follows the source cadence.
Global pause stops reading frames, allowing the bounded pipe to backpressure the decoder. Per-display pause
keeps decoding for the other display. Startup has bounded probe/first-frame waits and cleans up failed decoders.

## Preset library

**Open library folder** opens `%LocalAppData%\HYPNIX\Library\packages`.
Place complete validated `native-preset` directories there; changes refresh the gallery automatically.
Only allowlisted renderers compiled into HYPNIX are selected. Packages never execute scripts or DLLs.
See [package format](docs/WALLPAPER_PACKAGE_FORMAT.md) for the manifest and supported preset properties.
Invalid/unavailable packages are counted in the UI and detailed in diagnostics.

## Build and validation

```powershell
dotnet build AnimatedWallPaper.csproj -c Release
dotnet test Tests/Hypnix.Tests/Hypnix.Tests.csproj -c Release
```

The [Windows build and tests workflow](.github/workflows/ci.yml) runs on pushes to `main` and `codex/**`,
pull requests targeting `main`, and manual dispatch. It uses .NET 8 on Windows, builds the application and both
test projects, runs the regression suite, and retains TRX test reports for 14 days. GPU/native and live-desktop
execution remain separate local checks. The workflow becomes available on GitHub after it is committed and pushed;
a successful local run does not attest a hosted CI run.

If the Release app is running, use an isolated output directory to avoid replacing its executable:

```powershell
dotnet test Tests/Hypnix.Tests/Hypnix.Tests.csproj -c Release "-p:OutDir=$PWD/artifacts/validation/unit/"
dotnet build Tests/Hypnix.NativeSmoke/Hypnix.NativeSmoke.csproj -c Release "-p:OutDir=$PWD/artifacts/validation/native/"
dotnet artifacts/validation/native/Hypnix.NativeSmoke.dll artifacts/validation/captures
```

The committed native runtime supports ordinary .NET builds. To rebuild it from pinned source with
PowerShell 7, CMake and Visual Studio C++ Build Tools:

```powershell
./scripts/build-native.ps1 -FetchEffekseer
# Or use a clean local checkout of the documented pinned commit:
./scripts/build-native.ps1 -EffekseerSource C:\src\Effekseer
# Add -UpdateRuntime to replace the committed NativeBin DLL after validation.
```

The output is `artifacts/native/bin/Hypnix.EffekseerBridge.dll`. The build no longer depends on `.research`
or prebuilt absolute-path libraries. Only `-FetchEffekseer` permits source fetching.

For a hidden-surface native smoke check (requires a Windows interactive session and Direct3D 11 hardware):

```powershell
dotnet run --project Tests/Hypnix.NativeSmoke/Hypnix.NativeSmoke.csproj -c Release -- artifacts/native-smoke
# Optional real video check:
dotnet run --project Tests/Hypnix.NativeSmoke/Hypnix.NativeSmoke.csproj -c Release -- artifacts/native-smoke C:\tools\ffmpeg C:\videos\test.mp4
```

Run from the repository root. The smoke check does not attach wallpapers to Explorer; it exercises hidden
native surfaces, shell construction/layout and disposal. Its WPF layout PNGs omit the native preview surface.
Settings and library fixtures stay under its output directory.
The default run checks all thirteen built-in hosts and gallery entries, plus image checks for Neon Ribbons,
Liquid Orbs, Fractal Pyramid, Kaleidoscope, Lotus, Spectral Bloom, Event Horizon and Living Fire. These cover animation/audio and renderer-specific controls,
viewport positioning, FPS exposure or independent freeze. It also verifies animated GPU-to-GDI preview pixels. See the [testing guide](docs/TESTING_AND_REGRESSION_GUIDE.md)
for the exact coverage and remaining desktop checks.

For a full desktop end-to-end check that drives the real app through UI Automation and attaches each wallpaper
to the live desktop (Windows PowerShell 5.1, interactive session, Direct3D 11, audio endpoint, ffmpeg):

```powershell
powershell -ExecutionPolicy Bypass -File scripts/e2e-desktop-smoke.ps1 -Configuration Release
```

It switches wallpapers live, plays audio so the visualizers react, captures the desktop to
`artifacts/e2e-desktop/`, then presses Stop and restores your settings. See the
[testing guide](docs/TESTING_AND_REGRESSION_GUIDE.md#desktop-end-to-end-smoke-test-scriptse2e-desktop-smokeps1)
for the per-capture attestation checklist.

## Install and updates

Two independent channels from one codebase (the GitHub repo is private, so it is not the end-user feed):

- **Website installer** — a per-user [Velopack](https://velopack.io) installer
  (`HypnixWallpaper-win-Setup.exe`, no admin) with in-app auto-update served from a public release
  website. An installed build checks on launch, downloads deltas in the background, and offers
  **Restart to update** in the tray. Install/update/uninstall keep user data in `%LocalAppData%\HYPNIX`
  untouched (the install lives in a separate `%LocalAppData%\HypnixWallpaper`). Point the app at your
  host via `UpdateFeedUrl` in `Services/UpdateService.cs`.

  ```powershell
  dotnet tool install -g vpk --version 1.2.0
  ./scripts/package-release.ps1 -Version 1.2.3     # -> artifacts/releases, upload to your host
  ```

- **Microsoft Store** — a separate MSIX package (`scripts/package-msix.ps1`, needs the Windows SDK)
  with Store-managed updates. The in-app auto-updater self-disables inside MSIX, so **Check for updates**
  opens the Store listing instead; the Store keeps the app current and provides the Start-menu entry and
  uninstall. The manifest declares a `windows.startupTask` (`TaskId="HypnixStartup"`, disabled until the
  user enables **Start with Windows**). The wallpaper hosting needs the `runFullTrust` capability,
  reviewed at certification.

  ```powershell
  ./scripts/package-msix.ps1 -Version 1.2.3 -SelfSign   # sideload test; unsigned for Partner Center
  ```

Both are detailed in [install and updates](docs/INSTALL_AND_UPDATES.md) (feed hosting, local
install→update test via `HYPNIX_UPDATE_FEED`, Store submission). Builds are not yet code-signed, so
SmartScreen warns until a certificate is configured; the Store signs its own MSIX.

## Portable validation package

Run `./scripts/package-portable.ps1` in PowerShell 7 to create a Windows x64 ZIP with the .NET runtime,
all twelve wallpapers, dependency notices, file hashes and source-build metadata. Output goes to a new
timestamped folder under `artifacts/distribution`. See [distribution instructions](docs/DISTRIBUTION.md)
for extraction, shared settings, integrity checks and the remaining release validation.

## Engineering notes

- [Release pending items](docs/RELEASE_PENDING.md) tracks application reliability, licence integration, sales materials, final-package validation and distribution requirements per channel.
- Read [desktop integration findings](docs/DESKTOP_INTEGRATION_FINDINGS.md) before changing Win32 hosting.
- [Testing guide](docs/TESTING_AND_REGRESSION_GUIDE.md) covers automated and manual regression gates.
- [Product direction](docs/PRODUCT_DIRECTION.md) and [UI architecture](docs/UI_ARCHITECTURE.md) distinguish future work.
- [Relaxing animation design](docs/RELAXING_ANIMATION_DESIGN.md) turns the neuroscience of relaxation into authoring rules for calm wallpapers.
- [Shader wallpaper authoring](docs/SHADER_WALLPAPER_AUTHORING.md) is the step-by-step recipe for adding a new GPU wallpaper (contracts, template, audio, wiring, preview, tests).
- [Realistic fire study](docs/FIRE_RENDERING_STUDY.md) analyzes the three supplied Shadertoy examples and proposes a volumetric fire pipeline, GPU budgets and visual validation gates (Portuguese; design only).
- [Standalone fire preview](Tests/Hypnix.FirePreview/README.md) exercises the shared Living Fire renderer with system-audio response, limited MacCormack transport, flow-driven embers and GPU state checks.
- The approved FireRingV1 assets remain unchanged. The rejected volumetric prototype remains excluded from the gallery.
- Optional microphone reaction uses an **Audio source** that includes the microphone. No stored raw audio, audio uploads,
  telemetry or administrator requirement in normal operation.
- Code signing for direct downloads (SmartScreen trust), final MSIX/Store certification and hardware-accelerated video decode remain pending. Independent per-display assignments are implemented; see [validation](docs/PER_MONITOR_VALIDATION.md).
- A [macOS port study](docs/MACOS_PORT_STUDY.md) documents a future, not-yet-started plan (NSWindow desktop hosting, Metal, ScreenCaptureKit audio, notarization). It is design-only and changes no Windows behavior.

- [Living Fire integration](docs/LIVING_FIRE.md) documents the gallery effect, per-monitor simulation, controls and measured GPU cost.
