# UI architecture and layout direction

## Visual refinement — 20 September 2026

Use a maximum 3 DIP corner radius for cards, buttons, inputs and purchase surfaces.
The neutral charcoal palette shares a restrained blue accent across the library,
license screens and wallpaper editor. Outer spacing is 24 DIPs; control gaps use
8/12 DIPs, and sections use 16/24 DIPs. Button templates must honor Padding and
retain distinct hover, pressed, disabled and keyboard-focus states.

Library headings align with card edges. A thin divider separates the live preview,
and the selected wallpaper shares a compact action row with Preview, Customize and
Apply. App settings cards stretch consistently instead of sizing to their text.
Visual captures and passing layout/license smoke checks: `artifacts/design-refinement`.

## Implemented adaptive library — 20 September 2026

The library now uses a vertical card grid, with separate selected and on-desktop
states. Selecting a card updates the preview; **Apply wallpaper** explicitly starts
that wallpaper. **Stop** and the running/paused/stopped status remain global.

Preview layout uses available WPF device-independent units, so monitor resolution
and Windows scaling are not confused with usable window space. At 1100 × 480 DIPs
of shell content or above, a collapsible right preview leaves room for three or
more gallery columns. Otherwise, Preview opens a dedicated page inside the same
window. Returning retains selection and scroll position. The wide-pane preference
is saved; the compact back action does not change it.

A diagram and selector show the connected displays. Their arrangement follows
physical monitor bounds; the live render surface preserves the selected display's
aspect ratio, including portrait and ultrawide. The always-visible display selector
and preview diagram select the display to preview, customize and apply to. Apply
changes only that display; a separate Apply to all copies the selected wallpaper
and current customization to each connected display. Subsequent edits stay independent.
Display refresh preserves selection by device identity or falls back to an available
display when the selected one is disconnected.

`DisplayWallpaperController` owns one production session per stable DeviceId. Each
host attaches to that physical monitor rectangle and renders into a local (0,0)
viewport. Audio capture is shared, while GPU resources and video decoders belong to
individual sessions. Global FPS/audio/stop apply to all sessions; covered-display
pause maps current monitor indices to stable IDs and pauses only the matching session.

`DisplayWallpapers` saves wallpaper, enabled state and per-wallpaper preferences for
each display, using the old global preferences as fallback. Startup restores enabled
assignments after license verification. Unplugging retains assignments and reconnecting
restores them with the new geometry/DPI. Failed replacement preserves the old session;
recovery failures on one display do not stop the others. See [per-monitor validation](PER_MONITOR_VALIDATION.md).

**App settings** contains pause, battery, FPS, global audio, library and media tools.
**Customize** contains supported wallpaper controls, presets and reset/undo. Its
live preview docks beside the controls in a wide window and stays above them in a
compact one. Only the visible preview renders; hidden/minimized previews stop,
and playback resumes when returning to them. Native placeholders are black.

All 13 visible built-ins now have their own 960 × 540 capture of the production
preview renderer. The gallery preserves the entire image. Fire Burst and
Flamethrower no longer reuse the Aethelis image. These static captures never replace
the animated preview. See [layout validation](LAYOUT_VALIDATION.md).

The historical proposals below remain design background; their horizontal filmstrip
and fixed panel suggestions are superseded by this implementation. No additional
UI framework dependency was introduced for this change.


## Visual settings implementation — 17 September 2026

The requested View / Effects / More controls, graphite styling, matching title-bar
color, background support and presets are described in
[Visual settings — study and strategy](VISUAL_SETTINGS_DESIGN_PLAN.md).
The settings redesign is implemented. It uses a scoped graphite resource dictionary,
three tabs, a circular position pad, live controls, per-wallpaper presets and reset undo.
Living Fire also supports solid/image backgrounds and a sparks toggle. Other renderers
only expose supported controls. Settings additions are optional and preserve schema-1 files.

The settings window uses WPF WindowChrome with a solid custom caption from the start,
so the caption and content share the same background even when inactive. This replaces
the proposed DWM-first approach for this window and the older best-effort title-bar
fallback guidance below. MainWindow retains its existing DWM treatment. High contrast
uses system brushes. The wider WPF UI/Mica redesign remains a separate future project.

## Decision

Keep the existing .NET 8 WPF application and adopt **WPF UI 4.3.x** for the future visual-shell redesign.
Do not migrate the working application to WinUI 3 only to obtain Fluent styling or Mica.

Reasons:

- The desktop host, video pipeline, WASAPI capture, pause policy, and lifecycle are already validated in WPF.
- Microsoft Store accepts WPF/Win32 applications and recommends MSIX when Store package features are desired.
- WPF UI is actively maintained, MIT-licensed, supports Fluent controls, themes, navigation, and Mica, and can
  be introduced at the presentation layer without replacing renderer services.
- ModernWpf is MIT but its latest release is from 2022, making it a weaker long-term choice for a new Store UI.
- WinUI 3 remains a good option for a new application or a future deliberate shell migration, not an incidental
  styling dependency.

WPF UI's MIT license notice must be included in third-party notices when packaging.

Official/reference sources:

- Microsoft Store distribution for WPF/Win32:
  https://learn.microsoft.com/windows/apps/distribute-through-store/how-to-distribute-your-win32-app-through-microsoft-store
- Microsoft system backdrop guidance:
  https://learn.microsoft.com/windows/apps/develop/ui/system-backdrops
- WPF UI source and license: https://github.com/lepoco/wpfui
- ModernWpf maintenance reference: https://github.com/Kinnara/ModernWpf

## Material rules

- Use Mica as the window foundation, with an opaque fallback color for unsupported systems, high contrast,
  transparency disabled, remote sessions, and energy-saving policies.
- Do not use Acrylic as a permanent gallery or page background. Microsoft positions Acrylic for transient
  surfaces such as flyouts and context menus; use it only for short-lived contextual panels when supported.
- Cards use theme-aware translucent/solid brushes rather than stacking blur surfaces.
- The UI must remain fully readable when Mica and transparency are unavailable.

## Proposed layout

The supplied concept is inspiration, not a screen to reproduce literally. Its visual hierarchy is strong, but
the permanent left gallery plus permanent right inspector reduces the main preview and becomes crowded at
smaller window sizes.

Recommended desktop composition:

1. **Top command area**
   - App identity and compact running status.
   - Start/Stop as one clear primary state action.
   - Theme, diagnostics, and global settings as secondary icon actions.
2. **Central stage**
   - One large, centered preview of the selected wallpaper.
   - Preserve the wallpaper aspect ratio; letterbox rather than distort.
   - Show wallpaper title, type badges, and active-display state without covering important content.
3. **Bottom library filmstrip**
   - Horizontally scrolling wallpaper preview cards.
   - Selection immediately updates the central preview and, when playback is active, atomically replaces the
     running wallpaper using the already validated lifecycle.
   - Cards show thumbnail, concise title, and small capability badges only.
4. **Contextual settings**
   - Keep the validated small settings entrypoint (the gear beside the header).
   - It opens a dedicated, resizable settings window scoped to the selected visualizer wallpaper.
   - Controls: size (zoom) and on-screen position (X/Y) where supported, color theme, glow, intensity, and audio
     sensitivity, all bounded and applied live to the running wallpaper and the preview.
5. **Responsive behavior**
   - Wide: centered preview with optional contextual drawer overlaying/adjacent without shifting the library.
   - Medium: narrower preview and horizontal filmstrip; contextual settings use a flyout.
   - Minimum width: single column, preview first, filmstrip second; no clipped permanent inspector.

This layout intentionally focuses on the user's stated first milestone: list wallpaper previews and show the
selected animation prominently. NavigationView, Discover, playlists, and complex library management should not
be added until those product areas exist.

## Theme architecture

- All colors, radii, spacing, typography, and elevation must come from semantic resources.
- Use resource names such as `WindowBackgroundBrush`, `CardBackgroundBrush`, `SurfaceStrokeBrush`,
  `TextPrimaryBrush`, `TextSecondaryBrush`, and `AccentBrush`; never scatter literal theme colors through pages.
- Support System, Light, and Dark themes from one resource layer.
- Prefer `Segoe UI Variable` with `Segoe UI` fallback.
- Minimum text contrast and focus visuals must remain valid in every theme and high contrast mode.
- Animation scale/hover effects must respect Windows reduced-motion/accessibility settings.

## Card and interaction rules

- Corner radius: 8 px for cards and thumbnails; larger radii only for major container surfaces.
- Hover: subtle border/tint change. Avoid scale effects that cause neighboring cards to move or blur.
- Selection: persistent accent border plus a non-color-only indicator for accessibility.
- Keyboard: every wallpaper card is reachable and selectable; arrow navigation follows visual order.
- Tooltips: required for icon-only buttons.
- Preview loading: show a neutral skeleton/poster; never flash white.
- Every wallpaper package owns a `preview.jpg` and declares it in `wallpaper.json`. The central stage and card
  (or a lossless `preview.png`) and declares it in `wallpaper.json`. The central stage and card must load that
  same asset when selection changes; never leave the ambient preview visible for another type.
- Generated placeholder previews must be marked `previewKind: example`. Video frames extracted from the real
  media use `previewKind: extracted-frame`. Both may be replaced later without changing UI code.
- Virtualize the filmstrip/gallery when the library grows; do not create every animated preview simultaneously.
- Only the selected central preview may animate by default. Thumbnail previews stay static unless explicitly
  hovered and performance policy allows it.

## Isolation from the wallpaper engine

The layout is a presentation shell only:

- It consumes controller state and sends existing commands through a view model/facade.
- It must not call `DesktopWorker`, WASAPI, ffmpeg, or native host APIs directly.
- Wallpaper sessions continue running if the UI theme changes or a gallery is reorganized.
- Theme changes never restart the wallpaper.
- Preview selection and active wallpaper application are separate state concepts, even if the first MVP applies
  selection immediately while running.
- Existing logs, Stop guarantees, first-frame readiness, and per-monitor pause behavior are regression gates.

## Store-readiness gates for the shell

- Package with MSIX and run the Windows App Certification Kit before submission.
- Include WPF UI and NAudio MIT notices in third-party attribution.
- No dependency may download executable code or assets implicitly.
- Verify 100%, 125%, 150%, 175%, and 200% scaling, mixed-DPI monitors, keyboard-only use, Narrator labels,
  high contrast, reduced motion, light/dark/system theme, and transparency disabled.
- Verify clean layout at the declared minimum window size and after display changes.
- Open the control window inside the primary monitor work area, centered in WPF device-independent pixels,
  with a safety margin that keeps the title bar reachable at every supported DPI.
- Mica/Acrylic support is optional enhancement, never a functional requirement.
- On Windows 11, request immersive dark mode for the native title bar so the non-client header does not remain
  white above the dark shell; retain normal Windows fallback behavior if DWM rejects the attribute.

## Notification-area lifecycle

- Closing the main window hides the UI in the Windows notification area; it does not stop the wallpaper or
  dispose its renderer.
- Double-clicking the HYPNIX icon or selecting `Open HYPNIX` restores and activates the same window instance.
- `Stop wallpaper` is available independently from the UI and leaves HYPNIX running in the notification area.
- Only the explicit `Quit HYPNIX` command performs final controller, monitor, preview, and tray disposal.
- Show the background-running explanation only once per process to avoid repeated notifications.
- The tray icon and executable icon must use the current official brand asset and expose text labels for every
  command; Windows decides whether the icon appears directly on the taskbar or inside the overflow panel.

## Implementation sequence

1. Introduce semantic theme resources and a UI state/view-model boundary without changing layout.
2. Add WPF UI and the Fluent window/Mica foundation with fallback validation.
3. Build the centered stage and static bottom filmstrip using the three existing wallpapers.
4. Connect selection through the existing controller and preserve automatic safe replacement.
5. Move global performance controls into a compact settings surface.
6. Reuse the dedicated visualizer settings window with the new theme resources.
7. Run visual, accessibility, DPI, lifecycle, and Store packaging regression checks.

## Current shell implementation (September 2026)

The shell now uses a virtualized manifest-driven ListBox, per-wallpaper persisted settings, local video selection,
library/diagnostic folder actions and an HWND-hosted live preview. Selection and active playback remain distinct:
failed replacement leaves the previous wallpaper active. Preview captions sit outside the HWND surface to avoid
WPF airspace overlap. Hidden/minimized and battery/lock policies release preview work.

The live preview shares native renderer code and the audio source with desktop sessions. The unused animated
backdrop is no longer instantiated behind a static poster. WPF UI/theme adoption remains a future styling change.

Per-wallpaper visualizer controls now live in a dedicated, resizable and scrollable settings window
(`VisualizerSettingsWindow`), opened from the gear entrypoint, replacing the earlier popup that clipped its
lower controls. View groups geometry, background and quick presets; Effects groups color, glow, intensity,
sparks and audio; More groups preset management, defaults, reset undo and credits. Values are bounded,
and changes apply live to the matching running wallpaper and preview.
Size and position are carried to the GPU through previously unused constant-buffer slots (no layout change).

Size and position are shown for the classic visualizer, Aethelis, Spectral Bloom, Neon Ribbons,
Liquid Orbs, Event Horizon, Fractal Pyramid, Kaleidoscope, Lotus and Living Fire — every wallpaper
whose renderer applies Scale/OffsetX/OffsetY. One convention holds everywhere: X+ right, Y+ up,
measured in half-heights (so it feels identical at any aspect ratio). Color themes recolor every
visualizer, including Aethelis (the flame is palette-tinted, warm by default) and the classic GDI
visualizer. The two Effekseer fire effects (Fire Burst, Flamethrower Ring V2) author their color and
motion in the effect, so they hide both the palette and size/position rather than showing controls
that do nothing. The principle is that no control is shown unless it actually changes its wallpaper;
capabilities (`SupportsColorTheme`, `SupportsLayoutControls`) are the single source of truth, and the
same settings window updates its available controls when the selected wallpaper changes.
