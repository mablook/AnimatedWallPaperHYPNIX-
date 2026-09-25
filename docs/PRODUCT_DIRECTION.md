# Product direction

This document turns product-owner direction and useful ideas from reference research into independent requirements for HYPNIX. It includes intended behavior as well as implemented milestones; it is not a list of shipped features or a dependency on another wallpaper product or API.

## Product purpose

**Relax. Have fun. Be cool.** HYPNIX gives people an enjoyable desktop atmosphere,
real-time visual reactions to their music, and a way to express their personal
style. Both quiet breaks and lively music sessions belong in the product.

Recorded product-owner direction, 2026-09-21: sell the experience of relaxing,
having fun and making the desktop look great. Use the
[positioning and sales strategy](MARKETING_STRATEGY.md) for customer-facing copy,
demos and channel messaging. Calm-animation guidance applies to the Relax pillar;
it does not require every effect or music session to have the same intensity.

## Product principles

- Local-first and usable without an account.
- Normal-user process; no administrator requirement.
- The library owns installed packages; renderers consume validated packages.
- UI, library/catalog, playback policy, display layout, and rendering are separate components.
- Downloads are staged, validated, and installed atomically.
- Active content such as HTML and interactive wallpapers runs with explicit restrictions.
- Features must work without Steam or another wallpaper application.

## Multi-monitor

Multi-monitor support should be a first-class model, not a single panoramic rectangle hidden inside the renderer.

### Layout modes

- `Per display`: each monitor can use a different wallpaper and settings.
- `Duplicate`: the same wallpaper runs independently on every selected monitor.
- `Span`: one wallpaper spans the complete virtual desktop.
- `Selected displays`: apply to one monitor or an explicit subset, including both monitors.

### Display identity

Store a stable display identity when Windows provides one, plus a recoverable snapshot:

```json
{
  "deviceId": "...",
  "deviceName": "\\\\.\\DISPLAY1",
  "friendlyName": "Primary monitor",
  "bounds": { "x": 0, "y": 0, "width": 3840, "height": 2160 },
  "dpiScale": 1.5,
  "isPrimary": true
}
```

Do not identify a display only by index. Indices can change after docking, driver updates, sleep, or cable changes.

### Rendering rules

- Use physical pixels for Win32 host placement.
- Preserve per-monitor DPI and refresh-rate information.
- Create one host per display for `Per display` and `Duplicate`.
- Use one virtual-desktop host only for `Span`.
- Pause policy should be evaluated per display when possible.
- Reconcile saved layouts when a display disconnects or reconnects.
- Never lose a package or playlist because a display is temporarily missing.

### UI

The display picker should show monitor thumbnails with number, name, resolution, primary state, and current wallpaper. Applying a wallpaper should offer:

- This display.
- Both/all displays.
- Choose displays.
- Span across displays.

## Interactive wallpapers

Interactivity is a capability with trust implications, not merely a wallpaper type.

### Input modes

- `Off`: no input reaches the wallpaper.
- `Mouse on desktop`: forward pointer movement/clicks only while the desktop is the foreground surface.
- `Always mouse`: optional advanced mode with a clear warning.
- `Mouse and keyboard`: future advanced mode; never default.

### Input architecture

- Collect input in the desktop core rather than each UI page.
- Convert physical desktop coordinates into display/package-local coordinates.
- Forward only to the wallpaper assigned to the display under the pointer.
- Stop forwarding while another app owns foreground, unless an explicit advanced mode permits it.
- Never synthesize input into arbitrary third-party processes.

### Web interactivity

- Run local HTML in a dedicated web renderer.
- Block shell, process, registry, unrestricted filesystem, clipboard, camera, and microphone access by default.
- Expose only a small versioned wallpaper API if needed.
- Block remote navigation and network access unless the package declares it and the user permits it.
- Keep origin/data isolation between unrelated packages.

Manifest capabilities should include:

```json
{
  "interactive": true,
  "input": ["mouse"],
  "requiresNetwork": false
}
```

## Playlist mode

Playlists reference installed wallpaper IDs; they do not duplicate package files.

```json
{
  "schemaVersion": 1,
  "id": "playlist-id",
  "title": "Evening rotation",
  "entries": [
    { "wallpaperId": "first-id", "durationSeconds": 900 },
    { "wallpaperId": "second-id", "durationSeconds": 1200 }
  ],
  "order": "shuffle",
  "repeat": true,
  "transition": "fade",
  "displayTargets": ["display-device-id"]
}
```

Required behaviors:

- Ordered and shuffled playback.
- Per-entry or playlist-default duration.
- Repeat on/off.
- Next/previous controls.
- Restore current position after app restart.
- Skip missing, invalid, or unsupported packages and log the reason.
- Per-display playlists and one playlist spanning selected displays.
- Optional future schedules such as time of day, battery state, or light/dark theme.

Playlist transitions must prepare the next renderer before replacing the current host, avoiding white/blank frames.

## Audio

Audio playback and audio-reactive visuals are separate capabilities.

### Wallpaper audio

- Packages can declare and be verified as `hasAudio=true`.
- Default wallpaper audio should be muted unless the user enables it.
- Provide global volume, per-wallpaper volume, mute, and `audio only when desktop is active`.
- Pause/mute policies should be independent where useful.
- Duplicate mode must not play the same audio track once per monitor.

### Audio visualizer

An audio visualizer consumes system output through Windows audio loopback capture, with optional microphone
input enabled explicitly by the user, then provides normalized spectrum data to trusted renderers.

Proposed pipeline:

1. WASAPI loopback capture receives system output samples; an independent microphone capture is added only
   when **React to microphone** is enabled and a usable input endpoint exists.
2. Samples are mixed/downsampled into a bounded analysis buffer.
3. FFT analysis produces normalized frequency bands, peak, and RMS values.
4. Renderers receive only numeric analysis data.
5. No audio samples are stored, uploaded, or exposed to wallpaper packages.

### Implemented audio-reactive prototype

The visualizer uses the default Windows render endpoint through WASAPI loopback instead of simulated
wave values. The optional microphone source uses the default Windows capture endpoint. NAudio 2.3.0 (MIT)
is used only as the maintained .NET wrapper around WASAPI and sample formats;
FFT band analysis, smoothing, lifecycle, rendering, and privacy behavior are implemented in this project.

Current analysis contract:

- Capture the shared-mode mix format reported by each active endpoint.
- Mix each source's channels to mono for analysis only.
- Analyze overlapping 2048-sample windows with a Hamming window and FFT.
- Produce 64 logarithmic bands from approximately 35 Hz to the endpoint Nyquist limit, capped at 18 kHz.
- Apply faster attack and slower release smoothing before publishing normalized values to the renderer.
- Mirror the bands around the circular visualizer while sharing each source's capture across every display
  and preview.

### Premium VFX renderer gate

Premium audio-reactive wallpapers use the validated WASAPI/FFT source but own an independent Direct3D 11 renderer.
The Effekseer runtime is available for authored 3D effects through the HYPNIX native bridge; official Effekseer
samples are never presented as finished HYPNIX wallpapers.

Visual development proceeds from one readable effect to controlled complexity. The first accepted gate is a single
fire circle: quiet ember motion at silence, bass-driven thickness and impact, mid-driven turbulence, and restrained
heat response to highs. A feature is not added merely because the renderer supports it.
- Capture starts only while **Audio reactive** is enabled and an active wallpaper or preview needs it,
  and stops when there are no remaining consumers.
- Endpoint failure/disconnection schedules a reopen of the current default endpoint. The microphone and
  system-output sources recover independently; unavailable or denied microphone access does not interrupt
  system-audio reaction or display a blocking prompt.

**React to microphone** is off by default and persists as an explicit preference in Sound and the tray.
When enabled with **Audio reactive**, an available microphone contributes live analysis alongside system
playback. Audio samples are used transiently in memory and are never written to disk, sent over the network,
or played back through the speakers. Turning off microphone reaction releases its input capture; turning
off **Audio reactive** stops both sources.

Safety and UX:

- Clearly indicate when audio-reactive mode is active.
- Never capture microphone input implicitly, including as a fallback for unavailable system output.
- Stop capture when no active wallpaper or preview needs it.
- Rate-limit analysis updates independently from display FPS.
- Use a fixed, documented band format so native, shader, and web renderers behave consistently.
- Allow users to disable all audio analysis globally and microphone analysis independently.

### Wireless and Bluetooth output

Do not require `24-bit, 44100 Hz`. WASAPI shared mode exposes the audio engine's actual mix format, and that
format can legitimately be 44.1 kHz or 48 kHz, float or PCM. The analyzer must accept the reported format and
derive FFT frequencies from its real sample rate.

The `44.1 kHz` workaround mentioned by some wallpaper projects can help with a faulty device/driver combination,
but it is troubleshooting, not an application contract. For Bluetooth headsets, Windows may expose separate
stereo playback and hands-free communications profiles. Logs must record endpoint name, channels, sample rate,
bit depth, and capture restart errors so profile switches can be diagnosed. If a wireless endpoint disappears,
the app retries the current default render endpoint instead of asking for administrator permission.

Official references:

- WASAPI loopback recording: https://learn.microsoft.com/windows/win32/coreaudio/loopback-recording
- Audio engine mix format: https://learn.microsoft.com/windows/win32/api/audioclient/nf-audioclient-iaudioclient-getmixformat
- NAudio source/license: https://github.com/naudio/NAudio

### Validated result on the development machine

Manual visual validation completed successfully on 2026-07-11:

- Default output: Bluetooth Powerbeats Pro headphones.
- WASAPI mix format: 32-bit IEEE float, 48,000 Hz, stereo.
- The visualizer reacted to real system audio on both monitor targets.
- The `24-bit, 44,100 Hz` workaround was not needed; the endpoint's native 48 kHz mix format worked correctly.
- Per-monitor foreground pause continued to freeze only the selected display while the other visualizer instance continued.
- No microphone capture, raw-audio persistence, network transfer, or administrator permission was involved.

This confirms the rule: never instruct all wireless users to force 44.1 kHz. First use the endpoint's reported
mix format and inspect diagnostics. Changing the Windows device format is a last-resort driver workaround only
when loopback fails or produces unusable data for that specific endpoint.

### Contextual visualizer controls

Visualizer customization is intentionally scoped to the audio-reactive wallpaper. A small gear button is shown
beside the header only while a visualizer wallpaper is selected; selecting ambient or video hides it. The button
opens a **dedicated, resizable settings window** (`VisualizerSettingsWindow`) scoped to the selected wallpaper,
replacing the earlier popup that clipped its lower controls. Live controls are size (zoom), on-screen position
(X/Y), color theme, glow, intensity, and audio sensitivity.

Rules:

- Defaults reproduce the previously validated visualizer before the user changes anything (size 1, centered).
- Settings update the active visualizer host and the preview without restarting WASAPI capture or the session.
- Ambient and video sessions accept no visualizer side effects.
- Use bounded sliders and curated themes so invalid colors, extreme size/position, or extreme FFT amplification
  cannot destabilize rendering.
- Size and position ride previously unused constant-buffer padding slots (Scale/OffsetX/OffsetY), so the shared
  buffer layout is unchanged; shaders apply `uv = (uv - offset) / scale`. One convention holds everywhere
  (X+ right, Y+ up, in half-heights); renderers whose screen Y grows downward negate OffsetY. The classic GDI
  visualizer applies the same transform through the graphics matrix.
- Every advertised control must actually change its wallpaper. Size/position and color are standardized across
  all visualizers that can honor them; the two Effekseer fire effects (Fire Burst, Flamethrower Ring V2) author
  color and motion in the effect, so they hide those controls (via `SupportsColorTheme`/`SupportsLayoutControls`)
  rather than presenting inert ones, until the effect exposes a transform and recolor path.
- Color themes drive each visualizer's look (not just tint it), so switching theme visibly recolors it. Aethelis
  tints its flame from the palette with a white-hot core preserved, and defaults to the warm theme so the
  approved orange look is unchanged out of the box.
- Persistence lives in the visualizer wallpaper's own settings, not global performance settings.

Manual UX validation completed on 2026-07-11:

- The compact settings button and popup were approved as simple and visually clean.
- Controls are understandable without adding permanent complexity to the main screen.
- Live intensity, sensitivity, glow, and theme changes preserve audio reaction and desktop playback.
- Contextual visibility works as intended: the control belongs to the visualizer and does not appear for
  ambient or video wallpapers.
- This interaction pattern should be reused for future renderer-specific options: a small contextual entrypoint,
  safe defaults, bounded live controls, and no unrelated settings in the global panel.

Manifest example:

```json
{
  "capabilities": {
    "hasVideo": false,
    "hasAudio": false,
    "audioReactive": true,
    "interactive": false,
    "requiresNetwork": false
  }
}
```

## Renderer model

Recommended renderer interfaces:

- `NativeRenderer`: built-in procedural scenes.
- `VideoRenderer`: MP4/WebM/GIF and optional audio track.
- `WebRenderer`: local HTML/CSS/JavaScript under restricted policy.
- `ShaderRenderer`: future GPU-native visual scenes.

Every renderer should support the common lifecycle:

- Prepare while hidden.
- Report Ready only after a valid first frame exists.
- Attach/show atomically.
- Play, Pause, Resume, Stop.
- Update target bounds/DPI.
- Report capabilities and errors.
- Capture a preview frame when allowed.

Current prototype finding: external SDL player-window embedding freezes after its first frame on this machine's
raised desktop when the required layered child style is applied. The safer independent pipeline is
`media decoder -> bounded BGRA frames -> native host`. Multi-monitor presentation then applies a separate fit
and crop inside each display rectangle. This also creates a clean seam for future `Duplicate`, `Span`, and
`Per display` assignments.

## Library and future catalog

The local Library remains authoritative even when a remote catalog is added.

- Browsing/downloading is optional.
- Downloaded packages use the same manifest and validation pipeline as local imports.
- Catalog metadata is untrusted until content validation succeeds.
- Show package size, type, audio, interactivity, network requirement, author, and source before installation.
- Keep creator/license/source attribution in the manifest.
- Do not integrate unofficial Workshop scraping or bypass ownership controls.

## UI direction

Suggested navigation:

- `Library`: installed wallpapers in preview cards.
- `Discover`: future catalog/download page.
- `Playlists`: create and schedule rotations.
- `Displays`: monitor layout and active assignments.
- `Settings`: performance, pause policies, audio, startup, and safety.
- `Diagnostics`: logs, desktop handles, renderers, displays, and recovery actions.

Wallpaper cards should expose type, audio, interactive, network, and audio-reactive badges. Applying a card should open the display/layout picker when multiple monitors exist.

## Implementation order

1. Library scanner, manifest models, validation, and local import.
2. Display service and `Per display`/`Duplicate`/`Span` host models.
3. Video renderer without audio, then safe audio playback.
4. Playlist service and atomic transitions.
5. Restricted local web renderer.
6. Desktop-only mouse interactivity.
7. WASAPI/FFT audio analysis and native visualizer.
8. Optional catalog/download service.

Each phase must preserve the current guarantees: immediate Stop, normal-user execution, no white first frame, Explorer safety, and clear logs.

## Untrusted feature-list reference

## Circular visualizer research

The `eliasfloreteng/lively-audio-visualizer` repository was inspected statically at commit
`947663e85935ee2e61415d062cd4c929cc5da52b`; none of its code was executed or added as a dependency.
It has no repository license file and declares a null license in its package metadata, so its code and assets
must not be copied. Independently useful product ideas are a circular spectrum sized from the shorter display
axis, percentage-based position and size, frequency compensation, smoothing, glow, center artwork,
configurable backgrounds, and optional particles. Its Lively-specific audio callback will not be used;
our design remains an independent WASAPI/FFT pipeline.

The repository `karisardog/wallpaper-engine-download-free` was inspected only because its feature list mentioned multi-monitor, playlists, interactivity, web wallpapers, and audio visualizers.

Static findings at commit `475ae69d8ba277ac709d68a4d12b4daaa724302c`:

- No source code, build manifest, tests, or implementation is present.
- The repository contains only `.gitignore` and three byte-identical Markdown files.
- Files named `license.md`, `security.md`, and `contributing.md` contain the same promotional content rather than meaningful license/security/contribution material.
- The documents link to an external download site and claim a free unlocked copy of a paid product.
- Claims such as multi-monitor, playlists, interactivity, and audio visualization cannot be verified from the repository.

The external download was not opened. Nothing from this repository may be executed, installed, copied, or used as a technical dependency. The feature names are treated only as general product ideas implemented independently.
# Approved effect milestone: Fire Ring V1

`Assets/Effects/Milestones/FireRingV1` is the immutable snapshot of the first approved Effekseer-powered audio-reactive fire ring. It is a fallback and comparison baseline for future fire work. New variants must receive distinct package IDs/assets and must never replace this snapshot.

The next target is a realistic campfire ring: a continuous annular fuel bed using a purpose-built fluid/fire flipbook, with independent flame, smoke, ember and crackle/spike emitters. Bass affects flame height and pressure, mids affect turbulence, highs trigger short-lived sparks. The effect must remain quiet at silence and retain per-monitor pause behavior.
# Volumetric Fire GPU direction

`Hypnix Volumetric Fire` is a separate renderer and must not replace the approved
classic visualizer or the FireRingV1 milestone. The approved visual target is
[`docs/assets/volumetric-fire-approved-target.png`](assets/volumetric-fire-approved-target.png).
The rejected experimental card is hidden from the product gallery. The current
foundation maintains two persistent 3D voxel volumes per monitor and advances
velocity and combustion density with a Direct3D compute shader using advection,
buoyancy, curl and dissipation. A 64-slice ray marcher composites the result.
It deliberately has no flipbook, PNG atlas, EmberGen runtime dependency, or
Effekseer emitter. Audio changes live fuel injection and forces instead of
selecting or scaling prerecorded frames.

The discarded 2D version proved that higher resolution and bilinear filtering
cannot replace pressure, combustion and volumetric depth. Do not restore it as a
product wallpaper. The next quality gates are pressure projection, separate fuel
and temperature fields, blackbody emission, smoke conversion and temporal jitter.
