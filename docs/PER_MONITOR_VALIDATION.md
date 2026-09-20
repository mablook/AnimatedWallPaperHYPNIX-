# Independent wallpapers per monitor

## Behavior

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

- Unit suite: 262 tests passed on 2026-09-20. New tests cover independent sessions,
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
