# HYPNIX

Windows desktop app for local animated and audio-reactive wallpapers, built with WPF/.NET 8,
Win32 desktop hosting, WASAPI and Direct3D 11.

## Current behavior

- Manifest-driven gallery for ambient, classic audio visualizer, Aethelis, Fire Burst and Flamethrower Ring V2.
- Live preview using the same renderer as the wallpaper. Desktop and preview share one WASAPI capture.
- Preview stops when the window is hidden/minimized, the session is locked, or the battery pause option applies.
- Safe replacement: prepare the next wallpaper and its first frame before showing it and disposing the previous one.
  Preparation failures keep the current wallpaper; Stop also cancels pending preparation.
- Live 15, 30 and 60 FPS presentation caps. Native CPU modes reuse a persistent back buffer.
- Fullscreen/foreground pause, all-display or active-display scope, and optional battery pause.
  Battery and session-lock policies take priority over per-display app pause.
- Settings, selected wallpaper, local video references and per-wallpaper visualizer controls persist in
  `%LocalAppData%\HYPNIX\settings.json`. Playback starts explicitly with Start.
- Explorer/host failure detection, display-change recovery and resume handling recreate active sessions.
  Display identity, geometry and DPI snapshots are retained, including disconnected displays.
- Closing the window hides it to the tray. Tray Stop stops desktop playback; Quit releases the application.
- Diagnostics are kept under `%LocalAppData%\HYPNIX\logs`, with a 2 MB active log and three rotated files.

## Local videos

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

## Engineering notes

- Read [desktop integration findings](docs/DESKTOP_INTEGRATION_FINDINGS.md) before changing Win32 hosting.
- [Testing guide](docs/TESTING_AND_REGRESSION_GUIDE.md) covers automated and manual regression gates.
- [Product direction](docs/PRODUCT_DIRECTION.md) and [UI architecture](docs/UI_ARCHITECTURE.md) distinguish future work.
- The approved FireRingV1 assets remain unchanged. The rejected volumetric prototype remains excluded from the gallery.
- No microphone capture, stored raw audio, telemetry or administrator requirement in normal operation.
- MSIX, Store certification, arbitrary per-display assignments and hardware-accelerated video decode remain future work.
