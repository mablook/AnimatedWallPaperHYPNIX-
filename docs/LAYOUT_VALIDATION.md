# Layout and thumbnail validation — 20 September 2026

## Implemented

- Responsive vertical library with distinct selection and on-desktop badges.
- Docked, collapsible preview on wide windows; dedicated preview page on compact
  windows, preserving browsing selection and scroll position.
- Real connected-display diagram and preview framing; Apply targets the selected
  display, with a separate Apply to all action. See [per-monitor validation](PER_MONITOR_VALIDATION.md).
- Global playback/tools settings separated from supported wallpaper customization.
- Wider customization with persistent live preview, shorter descriptions and tooltips.
- Explicit Apply/Customize/Stop actions and running/paused/stopped status.
- Thirteen independent 960 × 540 thumbnails captured from the production renderers,
  without window chrome, UI or desktop content. The live preview remains animated.

## Capture reproduction

From the repository root:

```powershell
dotnet run --project Tests/Hypnix.NativeSmoke -- artifacts/thumbnail-capture --capture-thumbnails
```

This command intentionally replaces the visible built-in thumbnail assets. It renders
at 30 FPS using catalog default preferences and a quiet synthetic spectrum. Capture
time is 6 seconds, or 3 seconds for Flamethrower Ring V2. PNG/JPEG formats and manifest
paths are preserved. Each manifest records the capture provenance. No external images
or generated artwork are used.

## Verification

Result: **203 unit tests passed**; WPF navigation/settings checks and the full
native/GPU regression suite passed. Release build: **0 warnings, 0 errors**.

- Unit suite covers responsive thresholds, portrait/ultrawide fitting, distinct
  thumbnail paths and dimensions, plus the existing application regressions.
- WPF harness checks explicit Apply versus browsing, active badges, compact/wide
  navigation, retained selection/scroll, portrait framing, settings navigation,
  supported controls, presets and reset/undo.
- Layout captures cover library widths 640, 760 and 1280 DIPs and customization
  widths 348, 688 and 1028 DIPs, across all three tabs.
- Live UI check used two connected displays (3840 × 2160 and 2880 × 1620): display
  selection, animated library preview, animated customization preview, immediate
  color changes and continued animation after resizing to a compact window.
- WPF bitmap captures omit native Direct3D child surfaces. They verify layout;
  the separate renderer captures and live UI inspection verify animation content.
- Tests use isolated preferences and library paths. The renderer/native regression
  suite checks playback, controls, pause/resume, monitor behavior and disposal.

Artifacts for this run: `artifacts/layout-redesign/`.
These checks validate the updated application; existing installer artifacts must be
rebuilt from this source before distributing this layout.
