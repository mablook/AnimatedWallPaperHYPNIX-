# Independent wallpapers per monitor

## Simultaneous previews and simulated displays

The Preview mode selector offers **Selected display**, **All connected displays**,
**Simulate 3 displays**, and **Simulate 4 displays**. The multiple-display views use
the full preview workspace and adapt their rows and columns to the available window
size. Each card runs its own live renderer, with the display's proportions preserved.

In the connected-display view, choose a wallpaper in a card, then use **Apply to
display N** to change that display's desktop. Preview choices for the other cards
stay independent. Selecting a new wallpaper does not apply it automatically.

Simulation uses temporary, in-memory choices and never creates Windows monitors or
desktop wallpaper targets. Three displays include a portrait screen; four add an
ultrawide screen. Switching between three and four preserves each simulated choice.
Desktop Apply and display customization controls are unavailable in simulation;
the global **Stop all** action still explicitly stops real desktop playback.

Each preview is capped at 30 FPS (or the lower configured cap). Hidden previews,
minimized windows, license expiry, and closing the preview release their rendering
sessions. Changing one preview reuses the other cards' renderer windows.

Run the isolated native preview checks with:

```powershell
dotnet run --project Tests/Hypnix.NativeSmoke -c Release -- artifacts/multi-preview/gpu --multi-preview
```

The preview checks use production GPU renderers and recorded desktop-session test
doubles, so simulating monitors does not touch real wallpaper assignments. The real
desktop E2E below separately verifies applying to physically connected monitors.

Validated on 2026-09-20: 284 unit tests, the existing display/layout/license WPF
suites, and the new GPU preview suite passed. The preview suite exercises simultaneous
frames, independent draft choices, Apply all, portrait/ultrawide proportions at
1280/760/640 window widths, simulation isolation, and cleanup after hide/mode changes
and close. The real desktop E2E also verified two actual wallpapers plus both previews
rendering concurrently, without replacing either desktop window.
Reports: `artifacts/multi-preview/gpu/multi-preview-report.json` and
`artifacts/multi-preview/desktop/display-desktop-report.json`; both passed with no
remaining tracked native windows or video decoders.

## Behavior

Living Fire fits its source bed to each viewport independently of the Size control.
Emitter count follows viewport aspect divided by flame size (6–48 sources), while
vertical flame size follows viewport height. Reducing Size adds sources instead of
shrinking the entire band; increasing monitor width adds sources without making the
flames taller. Position controls still intentionally move the effect. Source-count
edits preserve the running fluid/particle simulation and remap smoothed FFT bands.

The ultrawide regression reproduced 571 unlit columns at 1280x360, Size 0.5 before
the correction, then zero unlit columns across 12 landscape/portrait/size cases.
At normal size, typical flame height was 110/107/109 pixels across 16:9/21:9/32:9
with equal viewport height. All 296 unit tests and the existing native fire checks
(audio response, settings, background, sparks, pause/resume, and 4K) passed.
Evidence: `artifacts/fire-ultrawide-after/living-fire-ultrawide-checks.json` and
`artifacts/fire-ultrawide/regression.trx`. Reproduce the focused GPU checks with
`dotnet run --project Tests/Hypnix.NativeSmoke -- artifacts/fire-ultrawide --fire-ultrawide`.

Choose a display in the selector above the library or in the preview diagram, choose
a wallpaper, then use **Apply to display N**. Browsing changes only the preview.
The active card badge and Playing/Paused label refer to the selected display.
**Apply to all displays** copies the selected wallpaper and its current customization
to the connected displays. Later customization remains independent, including when
several displays use the same wallpaper.

**Stop this display** affects only the selected display. **Stop all** also disables
saved assignments for currently disconnected displays. Closing HYPNIX releases its
native windows; the next launch restores enabled assignments after license checking.
Monitor disconnect does not erase assignments/preferences. IDs, not screen numbers,
associate saved wallpapers with reconnected/reordered displays. Unavailable files or
renderer failures preserve other working displays and surface an error.

## Automated coverage

- Unit suite: 284 tests passed on 2026-09-20. New tests cover independent sessions,
  failed replacement rollback, competing starts, stopping during preparation,
  disconnect during preparation, pause routing, global FPS/audio, settings isolation,
  stable-ID reconnection/reordering/geometry recovery, cancellation and persistence.
- WPF integration: real selector/gallery/button events with synthetic display topology
  and instrumented sessions. Covers different/same wallpapers with independent
  preferences, Apply all, selective/global Stop, startup restore, disconnect/reconnect,
  reversed display order and DPI/size changes. Controls remain visible at 1280x780,
  760x620 and 640x520. Also added to Windows CI.
- Existing layout/editor and Store-license WPF checks passed after this integration.
- Real desktop E2E: passed on two connected displays. Drives MainWindow buttons through
  production controller/session/renderers, verifies separate Explorer-owned parents,
  exact physical monitor geometry and one local viewport per host. DXGI presentation
  counts prove continuing render output and that pausing one host leaves the other
  advancing. Replacing/stopping one display preserves the other HWND; Apply all,
  persistence/startup restoration and complete test-window cleanup are checked.
- Mixed video/shader E2E also passed with the local FFmpeg tools: changing decoded
  video frames on one monitor, continuing GPU presentations on the other, then
  replacing the video without affecting the shader and terminating its decoder.
- A recovery regression explicitly verifies that failure on an existing display does
  not prevent a newly reconnected saved display from restoring independently.

Evidence: `artifacts/per-monitor/*.png` and
`artifacts/per-monitor-desktop/display-desktop-report.json`.
The desktop test uses isolated settings/library directories and does not stop existing
HYPNIX processes or capture screenshots of other applications.

## Reproduce

```powershell
dotnet test Tests/Hypnix.Tests/Hypnix.Tests.csproj
dotnet run --project Tests/Hypnix.NativeSmoke -- artifacts/per-monitor --displays-ui
pwsh -File scripts/e2e-display-wallpapers.ps1
# Include a real local video when FFmpeg is installed:
pwsh -File scripts/e2e-display-wallpapers.ps1 -MediaToolsDirectory C:/Tools/ffmpeg/bin -VideoPath C:/Videos/test.mp4
```

Desktop E2E requires an interactive Windows session with at least two physical displays
and D3D11; it fails explicitly if that prerequisite is absent. Run the desktop tests
locally, not on a headless hosted CI runner. Their wallpapers are temporary and cleaned
up on exit. Hardware unplug/replug is simulated in automated tests; physical cable,
docking-station and real Explorer restart checks remain part of manual release validation.
The GPU pause E2E exercises coordinator-to-renderer behavior; foreground-app coverage
detection has separate policy tests and a legacy interactive smoke script.

Independent effects allocate separate GPU resources; independent videos use separate
FFmpeg decoders. Performance should be checked on target customer hardware, especially
with several 4K displays.

The test executable uses the same PerMonitorV2 manifest as production. This matters
across asynchronous video loading and Explorer parenting; a thread-only DPI override
did not reproduce production correctly and was replaced by the shared manifest.

## Crash regression: foreground polling before the WPF message loop

The Windows .NET Runtime event from 2026-09-20 at 14:47:50 identified a cross-thread
`InvalidOperationException` in `MainWindow.Decision`, reached from the foreground
monitor's timer. The saved assignments were Living Fire on the 3840x2160 display
and Kaleidoscope on the 1920x1080 display, with the live preview enabled.

The interactive demo constructed `ForegroundAppMonitor` before `Application.Run`. Without a WPF
synchronization context, its fallback posted state changes to the thread pool,
where reading the window's ComboBox caused the process to terminate. Earlier desktop
checks installed a WPF synchronization context before constructing the window and
therefore missed that startup condition.

The service now captures its owning WPF Dispatcher directly, posts timer results
there, and ignores results after disposal or dispatcher shutdown. `RefreshNow`
continues to apply synchronously on the owner thread. The demo also preserves its
saved monitor choices when reopened.

Validation after the correction: all 266 unit tests passed, including four new
foreground-monitor tests. Two of the new tests failed before the correction,
demonstrating callbacks on a worker thread for both null and generic ambient contexts.
The WPF display interaction suite also passed.

Real desktop E2E passed on the same two physical displays with Living Fire and
Kaleidoscope, a visible preview with advancing GPU frames, repeated Apply on both
displays, and two actual timer callbacks recorded on the UI thread. Mixed video/shader,
selective pause, replacement, startup restoration and cleanup also passed, with no
remaining test windows or decoder processes. Evidence is saved in
`artifacts/crash-regression/desktop/display-desktop-report.json` and
`artifacts/crash-regression/regression.trx`.

## Ultrawide simulation preview regression

The multi-monitor preview previously painted the entire spare card area black,
making its letterbox margins look like part of the simulated screen. Cards now
outline the actual screen and use the card color outside it. Each simulated
display has its own 16:9, 21:9, 32:9 or portrait format selector. These choices
are temporary and survive switching between three and four simulated displays.

The previous multi-preview test assigned Living Fire only to its first, 16:9
card. The expanded `--multi-preview` test selects Living Fire on the fourth card
and changes its format through the actual WPF selector. At window widths
1280/760/640 and flame scales 1/.5, it verifies the native client dimensions,
the active GPU simulation's source count and continuing time/presentation.
Default-size source counts are 15/19/29 for 16:9/21:9/32:9; reduced-size counts
are 29/38/48. It also checks that preferences and desktop sessions stay unchanged.

Validation: 302 unit tests and the complete multi-preview E2E suite passed.
Evidence: `artifacts/fire-preview-e2e/regression.trx` and
`artifacts/fire-preview-e2e/multi-preview-report.json`. WPF layout PNGs exclude
native GPU content; the JSON records actual native renderer measurements.
