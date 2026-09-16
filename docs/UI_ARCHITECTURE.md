# UI architecture and layout direction

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
   - Controls: size (zoom), on-screen position (X/Y), color theme, glow, intensity, and audio
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
lower controls. It groups Appearance (color theme, size/zoom, on-screen position X/Y, glow, intensity) and
Audio (sensitivity), all bounded, and applies every change live to the running wallpaper and the preview.
Size and position are carried to the GPU through previously unused constant-buffer slots (no layout change).
