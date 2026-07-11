# Desktop integration findings

This document records the behavior observed while implementing and debugging the animated desktop host. Read it before changing `DesktopWorker`, `NativeWallpaperHost`, or the wallpaper lifecycle.

## Tested environment

- Windows 11 Pro 25H2, build `26200.8655`.
  - The registry still reports the legacy product name `Windows 10 Pro`.
- Local console session, normal user, medium integrity.
- The application manifest uses `asInvoker`; administrator permission is not required.
- DWM composition is enabled.
- High contrast is disabled.
- Desktop transparency and animations are enabled.
- Two-monitor, mixed-GPU setup:
  - NVIDIA GeForce RTX 5070 Ti at 3840 x 2160.
  - Intel Graphics at 1920 x 1080.
- Applied desktop DPI is 144 (150%).
- The physical virtual desktop is 5760 x 2160.
- WPF reports the same desktop in logical units as 3840 x 1440, starting at x = -1280.

The physical/logical coordinate difference is important. Win32 desktop windows use physical pixels, while WPF sizes and positions use device-independent units.

## Permissions and installed wallpaper software

- Desktop hosting works as a normal user. Elevation is neither needed nor desirable.
- Wallpaper Engine (Steam App ID 431960) was found and uninstalled during debugging.
- No active Lively, WhitePeacock, Rainmeter, or other wallpaper process was found.
- A former WhitePeacock wallpaper path remained in the Windows desktop registry, but its Store package was not installed.
- NVIDIA Overlay processes were running, but no evidence showed that they blocked desktop composition.

## Windows 11 raised desktop

This Windows build uses the modern "raised desktop" layout:

- `Progman` has `WS_EX_NOREDIRECTIONBITMAP`.
- `SHELLDLL_DefView` is a layered child of `Progman` and renders the icons.
- A visible `WorkerW` is also a child of `Progman` and renders the static wallpaper.
- The animated host must be a layered child of `Progman`.
- Its Z-order must be below `SHELLDLL_DefView` and above `WorkerW`.
- `WorkerW` must remain the bottom child.

The desktop initialization message that works for this layout is `0x052C` with `wParam = 0xD` and `lParam = 0x1`.

The classic algorithm that searches for a top-level `WorkerW` after the window containing `SHELLDLL_DefView` is not sufficient on this build. Most top-level `WorkerW` candidates observed here were invisible utility windows around 198 x 56 pixels.

## Bugs found during debugging

### Startup crash

The initial `Checked` event in `MainWindow.xaml` fired while XAML was still creating the remaining controls. `ApplyPlaybackPolicy` accessed a checkbox that did not yet exist and raised `NullReferenceException`.

Fix: ignore policy events until `InitializeComponent` has completed.

### Wallpaper resized to zero

The first `SetWindowPos` call passed width and height as zero without `SWP_NOSIZE`. Clicking Start created the wallpaper window and immediately resized it to 0 x 0.

Rule: if width/height are zero, include `SWP_NOSIZE`; otherwise always pass the real client dimensions.

### Wrong desktop host

Parenting directly to an arbitrary or invisible top-level `WorkerW` made the wallpaper disappear. Parenting to `Progman` without correct Z-order placed it below the static wallpaper.

Fix: detect the raised desktop and place the child relative to `SHELLDLL_DefView`, then force the real child `WorkerW` to the bottom.

### Missing child style

`SetParent` appeared to succeed, but the WPF window returned to `parent = 0` because it was still a top-level window.

Fix: add `WS_CHILD` and remove `WS_POPUP` before attaching.

### Direct WPF rendering after reparenting

A normal WPF window does not reliably present its own rendered surface after being converted into a layered child of `Progman`.

Observed behavior:

- Win32 calls succeeded.
- The window was visible, correctly sized, and correctly parented.
- The animation remained invisible.
- WPF removed an externally requested `WS_EX_LAYERED` style unless the window used `AllowsTransparency=True`.
- Even with the layered style retained, the WPF content was not reliably shown after reparenting.

Rule: do not use a WPF window as the final desktop rendering surface.

### DWM thumbnail projection

The source-window-to-host DWM projection was tested.

Lessons:

- `DwmRegisterThumbnail` must run while the destination is still a top-level window. Registering after `SetParent` returned `0x80070057`.
- Registration and update can return success even when the result is visually unusable.
- A completely off-screen WPF source was not composed and produced only the host background.
- Keeping the source fully on-screen made the animation work, but the source covered all applications and desktop icons.
- Leaving only one source pixel on-screen again produced a white host.
- A WPF layered destination can overwrite the DWM thumbnail through its own `UpdateLayeredWindow` lifecycle.

Rule: the DWM thumbnail path is not suitable for this built-in WPF animation on this machine.

### Native host background appeared white

A native `Static` host paints its default white background before any renderer has drawn a frame. The same white background is visible whenever the selected rendering path produces no content.

Creating the host hidden and painting before `ShowWindow` reduced but did not eliminate the flash. The remaining white frame came from the default Win32 `Static` class processing its own erase/paint when revealed.

The host now uses a registered application-specific window class with no background brush. Its window procedure consumes `WM_ERASEBKGND` and validates `WM_PAINT` without inserting a system-colored frame. The renderer owns every visible pixel.

Do not treat a white screen as proof that desktop attachment failed. It means the host is visible but has not received useful rendered content.

## Current architecture

The working built-in animation path is:

1. `WallpaperController` creates a `WallpaperSession`.
2. `WallpaperSession` creates `NativeWallpaperHost`.
3. `DesktopWorker` detects the raised desktop and attaches the native host to `Progman`.
4. The host is placed below `SHELLDLL_DefView` and above `WorkerW`.
5. `NativeWallpaperHost` renders the animation directly with GDI/GDI+ into its own HWND.
6. A dispatcher timer respects the configured 15 or 30 FPS cap.
7. Pause stops the render timer; Resume restarts it.
8. Stop destroys only the native host and restores the static desktop underneath.

This avoids WPF reparenting, off-screen source windows, and DWM thumbnail projection.

## First-frame readiness

The native `Static` host originally appeared white between being attached and receiving its first rendered frame. The animation then started normally. This was a readiness/lifecycle bug, not an animation failure.

The corrected lifecycle is:

1. Show `Preparing wallpaper...` in the control window.
2. Create the native host without `WS_VISIBLE`.
3. Attach and size it in the correct desktop Z-order while hidden.
4. Render a complete first frame.
5. Reveal the host without activation.
6. Render once more synchronously to replace any default `Static` paint.
7. Start the frame timer and report `Running`.

Do not make the host visible earlier in this sequence.

The native host must not use the built-in `Static` class. Use the custom no-background class so `ShowWindow` cannot introduce a white erase between the prepared frame and the first timed frame.

## Foreground pause policy

Fullscreen detection and background detection are related, mutually exclusive policy modes. Independent checkboxes were initially used, but that was misleading because "any active app" already includes fullscreen applications.

The UI now exposes one `Pause animation for apps` selector:

- `Never` ignores application foreground state.
- `Fullscreen apps only` pauses only when a foreground window covers its monitor.
- `Any active app` pauses whenever a visible non-shell window owned by another process is foreground.

Only one mode can be active at a time.

Foreground eligibility rules:

- `Progman`, `WorkerW`, `Shell_TrayWnd`, and windows owned by this application are excluded.
- Minimized (`IsIconic`) windows are excluded. Some Windows applications remain foreground after being minimized; without this check, playback resumed only after the user clicked the desktop.
- DWM-cloaked windows are excluded because they are not visually active even when Win32 still reports them as visible.
- The monitor refreshes once per second and emits a state change only when values change.
- Start forces a synchronous foreground refresh before applying policy, avoiding a stale foreground state that could pause and resume immediately.
- Logs must contain `OtherAppActive=True`, `Wallpaper paused`, then `OtherAppActive=False`, `Wallpaper resumed` for a complete validation.

Resume is idempotent: if the wallpaper is already running, repeated foreground notifications must not restart the timer or render redundant immediate frames.

Important semantic detail: minimizing the current app does not guarantee that the desktop becomes foreground. Windows may activate another application underneath it. In `Any active app` mode, playback correctly remains paused if that newly focused window belongs to another process. Playback resumes automatically only when foreground becomes the desktop shell, this application's control window, or no eligible app. Use `Fullscreen apps only` when normal foreground windows should not pause playback.

Automated validation completed:

- Opening another app produced `OtherAppActive=True` and `Wallpaper paused`.
- Closing it produced `OtherAppActive=False` and `Wallpaper resumed`.
- A minimized window is now rejected through `IsIconic`, but automation can transfer focus to its executor; therefore final minimize behavior must also be verified manually in the real desktop workflow.

## Diagnostic logging

Each application run resets and writes:

`bin/Debug/net8.0-windows/logs/wallpaper.log`

The log records:

- OS, user, session, elevation state, and base directory.
- Start and Stop events.
- `Progman`, `SHELLDLL_DefView`, and `WorkerW` handles.
- Raised-desktop detection.
- Window styles, parent, visibility, dimensions, and Z-order operations.
- Win32 error codes.
- Renderer/session lifecycle.
- Unhandled UI exceptions.

Never report success from process existence alone. Verify the relevant handles, parent, rectangle, renderer start, and visual result.

## Multi-monitor video and wallpaper switching

The current machine exposes one `1920x1080` display and one `3840x2160` display inside a
`5760x2160` virtual desktop. Monitor rectangles are captured in physical Win32 pixels and translated
to coordinates relative to the desktop host. WPF logical dimensions must not participate in this calculation.

Important results from the MP4 prototype:

- Embedding an `ffplay`/SDL window is unreliable on the Windows 11 raised desktop. The window can show
  its first frame, but adding the layered child style required by this desktop hierarchy can stop subsequent
  SDL presentations even while the process remains alive.
- Process health is therefore not proof of video playback. A visible first frame followed by a frozen image
  is a presentation-path failure, not necessarily a decoder failure.
- The working direction decodes the local MP4 with `ffmpeg` to BGRA frames and presents those frames through
  our proven native wallpaper host. It does not use Lively APIs or binaries.
- The decoder currently produces a bounded `1280x720` working frame. The renderer scales that frame for the
  desktop, keeping decode bandwidth independent from the combined monitor resolution.
- A single fit operation over `5760x2160` gives the wrong result for monitors with different sizes. Each physical
  monitor needs its own destination rectangle and its own `cover` source crop.
- `cover` preserves aspect ratio: wide destinations crop vertically; narrow destinations crop horizontally.
  Never stretch the full frame independently in X and Y.
- The current video is duplicated across displays. Future display modes should explicitly distinguish
  `Duplicate`, `Span`, and `Per display`; they are different compositions, not just different window sizes.

The visualizer follows the same display model but renders one centered instance per monitor. A single visualizer
centered across the virtual desktop can land mostly on one display or across a bezel, which is not a valid
two-monitor duplicate layout. Visualizer size is calculated from each monitor's shorter axis.

Wallpaper selection behavior:

- When a wallpaper is already active, changing the wallpaper dropdown stops and disposes the existing session,
  then prepares and starts the newly selected renderer automatically.
- Stop-before-start prevents multiple wallpaper hosts, orphaned decoder processes, and stale pause state.
- Renderer replacement must continue to preserve immediate Stop, first-frame readiness, foreground policy,
  and cleanup on application exit.

## Per-monitor foreground pause

The pause policy now has two scopes while retaining `All displays` as the safe default:

- `All displays`: pauses the shared renderer/timer exactly as before.
- `Active display only`: keeps the shared timer and video decoder running, but freezes presentation on the
  monitor containing the eligible foreground window.

The foreground window is obtained with `GetForegroundWindow`. Windows `MonitorFromWindow` semantics select
the monitor with the largest intersection with that window, which gives deterministic behavior for windows
that cross display boundaries. The app maps that monitor to the same `Screen.AllScreens` ordering used when
the renderer targets are created. Shell windows, this app, minimized windows, DWM-cloaked windows, and the
video decoder remain excluded.

Renderer-specific freeze behavior:

- Ambient and visualizer render the paused monitor using the captured animation timestamp while other monitor
  targets use current time.
- Video captures the most recent decoded BGRA frame when the monitor enters the paused state and reuses it only
  for that display. The other display keeps consuming current frames from the single decoder.
- Battery pause remains global because its purpose is resource saving, not foreground visibility.
- Clearing the policy, moving foreground to another display, Stop, or switching wallpapers clears/replaces the
  per-monitor state without creating extra decoder processes.

Official API references used for this design:

- `GetForegroundWindow`: https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getforegroundwindow
- Multiple-monitor functions and `MonitorFromWindow` largest-intersection behavior:
  https://learn.microsoft.com/windows/win32/gdi/multiple-display-monitors-functions
- Monitor rectangles in virtual-screen coordinates:
  https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-monitorinfo

## Reference projects

Persistent shallow clones are stored under `.research/`, which is excluded by `.gitignore`:

- `.research/lively`
  - Studied commit: `c1036feb664960722e34bf4309042c247d6a909d`
  - License: GPL-3.0.
  - Use only for architectural study unless the application adopts compatible licensing.
- `.research/weebp`
  - Studied commit: `3f9e2d5ab7f5f1f9215c700a6dfba8173fc18344`
  - License: Unlicense/public domain.
- `.research/livepaper`
  - Studied commit: `93324400a7dfb65304821061efe53b6a0c409495`
  - License: MIT.
- `.research/wallpaper-engine-download-free`
  - Studied commit: `475ae69d8ba277ac709d68a4d12b4daaa724302c`
  - No usable source code or trustworthy license was present.
  - Static research only; do not execute or follow its external download link.
- `.research/lively-audio-visualizer`
  - Studied commit: `947663e85935ee2e61415d062cd4c929cc5da52b`.
  - No repository license file; package metadata declares no license. Static research only, with no copied code or assets.

Useful Lively ideas:

- Raised-desktop detection.
- Separate UI, desktop core, and wallpaper player processes/components.
- Rebuild the desktop host after Explorer restarts.
- Per-monitor layout models.
- Library cards, previews, drag-and-drop import, active-wallpaper controls, and diagnostic UI.

Useful weebp ideas:

- Try both modern `0x052C` message variants and retain an old fallback.
- Embed arbitrary native player windows.
- Remove borders and convert external windows to `WS_CHILD`.
- Map coordinates from screen space to wallpaper space.
- Provide separate current-monitor and panoramic placement.
- Restore window styles and invalidate the desktop when removing a wallpaper.

Useful LivePaper ideas:

- Create the SDL/OpenGL window hidden, attach it, and only then show it.
- Use an accelerated renderer with VSync instead of painting through a framework-owned layered surface.
- Clear the render target to a deliberate color before presenting content.
- Separate platform integration, video decoding, and presentation.
- Decode video frames according to timestamps instead of assuming a fixed FPS.
- Keep remembered wallpaper selection in per-user local application data.

LivePaper currently supports only one display and uses the classic WorkerW path, so its desktop discovery cannot replace raised-desktop handling.

## Rules for future work

1. Do not request administrator permission for normal wallpaper hosting.
2. Do not assume registry `ProductName` identifies Windows 11 correctly; use build/version too.
3. Keep WPF logical dimensions separate from Win32 physical dimensions.
4. Do not select a `WorkerW` only by class name; verify hierarchy, visibility, and rectangle.
5. Preserve `SHELLDLL_DefView > animated host > WorkerW` Z-order on the raised desktop.
6. Do not reparent a WPF renderer directly into the desktop.
7. Treat successful Win32/DWM return codes as necessary but not sufficient; visual verification is required.
8. Create the desktop host hidden and reveal it only after a first valid frame is ready.
9. Use a custom window class with no background brush; never use the default `Static` paint lifecycle for the wallpaper surface.
10. Stop must always destroy the host immediately and leave Explorer intact.
11. Keep reference repositories under `.research/` and never compile or ship their code accidentally.
12. Keep production code independent of Lively APIs and binaries.
13. Add Explorer restart and display/DPI change recovery before packaging.
14. Model overlapping user choices as one explicit mode rather than independent checkboxes.
15. Treat minimized and DWM-cloaked foreground windows as inactive for background-pause behavior.
16. Size external player windows before their first frame; resizing a reparented SDL window alone may leave its video viewport at the media's original dimensions.
17. Compute visual geometry from the selected monitor's shorter axis, not the full virtual-desktop width.
18. For mixed-resolution displays, apply video fit/crop independently inside every monitor rectangle. A single crop calculated from the combined virtual desktop produces incorrect composition on each display.
19. Per-monitor pause must freeze presentation state, not start a second decoder or audio pipeline. Battery pause remains global.
20. Audio-reactive wallpapers capture only the default render endpoint through WASAPI loopback. Never fall back to the microphone implicitly.
21. Treat the endpoint mix format as authoritative. Do not hardcode 44.1 kHz for wireless devices; log the real format and convert during analysis.
22. Endpoint changes and disconnects are recoverable runtime events. Retry the current default render endpoint and keep Stop deterministic.
23. A Bluetooth endpoint was validated at 32-bit IEEE float, 48 kHz, stereo. Real FFT reaction worked, proving that forcing 44.1 kHz is unnecessary when the endpoint mix format is healthy.
24. Audio-reactive validation requires a visual response to real playback plus a logged endpoint format. A successful capture start alone is insufficient.
25. Renderer-specific settings must be contextual and no-op for unrelated wallpaper types. Updating visualizer appearance must not restart capture, recreate the desktop host, or alter ambient/video defaults.
26. The compact contextual settings popup was manually approved. Preserve this pattern: hidden outside its renderer, safe defaults, live bounded updates, and no main-layout clutter.
27. Do not rely on `CenterScreen` in a mixed-monitor setup. Place the control window explicitly inside the primary `SystemParameters.WorkArea`, using WPF logical pixels and a safe title-bar margin.
28. Treat the control window and wallpaper session as separate lifecycles. The window close button hides the UI
    to the notification area; only an explicit tray `Quit` disposes the wallpaper engine and exits the process.
29. Keep tray commands on the WPF dispatcher. Notification-area callbacks may originate outside the UI event
    flow and must not mutate controls or controller state directly.
30. Visualizer intensity uses `1.0x` as the stable default and allows up to `2.7x`. Raising the ceiling must remain
    a live renderer update: it must not restart WASAPI capture or alter non-visualizer wallpapers.
31. Freezing an audio visualizer per monitor requires freezing both animation time and a snapshot of its FFT
    bands. Freezing time alone still allows current audio levels to animate the supposedly paused display.
32. Visualizer backgrounds are wallpaper-package assets rendered with cover fitting inside each monitor target.
    Never stretch one image across the combined virtual desktop when monitor aspect ratios differ.
33. Renderer variants may share one WASAPI/FFT pipeline while remaining separate wallpaper choices. The classic
    bars and reactive flames have independent render modes, backgrounds, previews, and package manifests.
34. Premium audio states must be derived from semantic frequency ranges rather than total volume: silence drives
    ember breathing, low bands drive radial impact, high bands drive cyan jets/sparks, and simultaneous spectral
    spread drives the harmonic vortex. Blend weights continuously; never switch scenes abruptly.
35. A technically valid procedural shader is not automatically premium. The rejected Aethelis prototype proved
    that bright rings, square hash particles, and analytic glow can compile and run at 60 FPS while still missing
    the approved glass/liquid/plasma art direction. Never ship or add a premium renderer to the gallery before
    side-by-side frame review against its storyboard.
36. Premium visual work must use an isolated prototype and explicit visual gates: silhouette, material response,
    particle shape, exposure, palette, motion, silence state, bass state, high-frequency state, and climax state.

## Effekseer and GPU visualizer findings

The first Direct3D 11 Aethelis experiments established several additional rules:

1. A powerful GPU does not improve an under-designed effect. The initial full-screen pixel shader was fast, but
   visually sparse because it approximated particles and fluid with analytic loops rather than an authored VFX
   system.
2. Effekseer 1.80 can share the existing Direct3D 11 device and immediate context through a small native C ABI
   bridge. The runtime initialized on the NVIDIA adapter, loaded an HDR effect, and rendered inside the existing
   desktop swap chain at 60 FPS.
3. The runtime is kept optional. Failure to load the native DLL or effect must leave the procedural renderer usable;
   it must never prevent Stop, tray exit, or the approved classic visualizer from working.
4. Combining an authored Effekseer aura, the earlier procedural vortex, two glowing rings, smoke and independent
   spark layers produced visual noise. Technical capability is not a reason to show every effect simultaneously.
5. Each premium wallpaper starts with one dominant visual idea. Additional emitters are introduced only when they
   support that idea and pass a side-by-side visual review.
6. The next Aethelis gate is intentionally minimal: one organic fire circle on a dark field. Bass controls impact and
   thickness, mids control turbulence speed, and highs may affect heat/color only. No logo, secondary ring, tornado,
   smoke cloud, orbiting particles, or unrelated sample effect is allowed in this gate.
7. Official sample effects are integration fixtures, not product artwork. They prove loaders, textures, materials,
   trails, device sharing, and packaging; they must be disabled before product visual review.
8. The single fire-circle shader reached its first approved visual milestone after bass compression, asymmetric
   turbulence, a white-hot core, extended red tails, and a 60 FPS default were tuned together. Preserve this exact
   renderer as `Aethelis reactive`; future flame experiments are separate wallpaper choices and shader files.
9. Detached flame fragments are a second visual gate, not a modification of the approved ring. They require their
   own direction, lifetime, velocity, fade, and bass threshold so they read as small explosions rather than a ring
   whose radius merely scales.

    Only after still frames and motion are manually approved may the renderer be connected to the desktop host.
37. Do not describe mathematical hash particles as fluid simulation or billions of particles. Product language
    must match the actual renderer. A future Aethelis attempt needs a genuine multipass fluid/particle pipeline,
    temporal buffers, bloom, and depth layers, or a deliberately pre-rendered high-quality alternative.

## Recommended next steps

1. Replace per-frame `BufferedGraphics` allocation with a persistent back buffer.
2. Add explicit multi-monitor models and per-monitor DPI handling.
3. Add a wallpaper library and local file import.
4. Add a native/local video renderer in a separate component.
5. Add Explorer restart recovery and display-change handling.
6. Add automated tests for style flags, first-frame readiness, host selection, lifecycle, and unaffected desktop behavior.
7. Add a diagnostics page that can export the current log.

The package/library design and the analyzed external example are documented in
[`WALLPAPER_PACKAGE_FORMAT.md`](WALLPAPER_PACKAGE_FORMAT.md).

Multi-monitor, playlists, interactive wallpapers, audio visualization, renderer boundaries, and product sequencing are documented in [`PRODUCT_DIRECTION.md`](PRODUCT_DIRECTION.md).
