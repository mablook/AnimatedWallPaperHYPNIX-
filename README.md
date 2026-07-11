# HYPNIX

Windows desktop app for lightweight animated and audio-reactive wallpapers.

Before changing desktop hosting or rendering, read
[`docs/DESKTOP_INTEGRATION_FINDINGS.md`](docs/DESKTOP_INTEGRATION_FINDINGS.md).

The proposed downloadable/importable wallpaper library format is documented in
[`docs/WALLPAPER_PACKAGE_FORMAT.md`](docs/WALLPAPER_PACKAGE_FORMAT.md).

The independent product roadmap for multi-monitor layouts, playlists,
interactivity, audio visualization, and future downloads is documented in
[`docs/PRODUCT_DIRECTION.md`](docs/PRODUCT_DIRECTION.md).

The WPF/WPF UI decision, centered preview layout, theme system, accessibility,
and Microsoft Store shell requirements are documented in
[`docs/UI_ARCHITECTURE.md`](docs/UI_ARCHITECTURE.md).

Automated regression tests, known pitfalls, and the manual Windows validation matrix are documented in
[`docs/TESTING_AND_REGRESSION_GUIDE.md`](docs/TESTING_AND_REGRESSION_GUIDE.md).

## Current scope

HYPNIX supports live 15, 30, and 60 FPS caps. The per-user package registry watches
`%LocalAppData%\HYPNIX\Library\packages` and safely registers validated `native-preset` packages without
executing downloaded code.

- WPF/.NET 8 desktop app.
- Built-in local ambient animation.
- Native circular system-audio visualizer duplicated per monitor, using WASAPI loopback and local FFT analysis.
- Local MP4 prototype decoded to frames and fitted independently to each monitor.
- No internet access, downloads, telemetry, services, or admin elevation.
- Start/Stop controls and automatic wallpaper replacement when the selection changes.
- Contextual visualizer settings for intensity, sensitivity, glow, and color theme.
  The compact popup has been manually validated as clear and visually unobtrusive.
- Pause policy for fullscreen apps.
- Pause scope can target all displays or only the display containing the foreground app.
- Optional pause while running on battery.
- Frame cap: 15 FPS or 30 FPS.

## Safety rules

- The wallpaper host runs as a normal user process.
- The app does not change the configured static Windows wallpaper.
- Audio-reactive mode captures only the local output mix; it never opens the microphone, stores samples, or uploads audio.

## Validated audio setup

The real-audio visualizer has been manually validated with Bluetooth headphones using the Windows endpoint's
native 32-bit float, 48 kHz stereo mix. No forced 44.1 kHz setting was required.
- The desktop integration is isolated in `Services/DesktopWorker.cs`.
- `Stop` closes the wallpaper host window immediately.
- App shutdown also closes the wallpaper host.

## Next steps

1. Add tray icon and minimize-to-tray behavior.
2. Replace the prototype video frame transport with a persistent accelerated texture/back buffer.
3. Add app settings persistence.
4. Create MSIX packaging project.
5. Run Windows App Certification Kit before Store submission.
