# Living Fire

Living Fire is the thirteenth built-in wallpaper. It extends the approved multi-source
fire study, with persistent flow-driven embers and system-audio response.
The rejected `VolumetricFire` experiment remains hidden. The new kind is appended
to the enums so existing numeric values retain their meaning.

## Runtime

`FireGpuRenderer` owns one D3D11 device and swap chain per host. Each monitor owns
a `FireSimulation`, clock, particles and cached render surface. The study harness
links the same engine and `Shaders/LivingFire.hlsl`; there is no separate shader copy.

Each simulation uses a 288 × 128 × 40 volume with separate fuel, heat, soot and
reaction, limited MacCormack scalar transport, buoyancy, drag, vorticity confinement,
24 pressure iterations and 64 persistent ember slots. The model is approximate;
oxygen availability, incandescent color and smoke shading are not spectral chemistry.

The fixed physics substep is 1/60 second. Each logical tick now executes four
substeps, making flame and ember motion exactly 4x faster than the original
(twice the preceding version). At 15/30/60 FPS, the frame scheduler advances
the same 240 physics substeps per playback second.
Audio smoothing uses a fast attack and a gentler release (attack tau ~36 ms, release ~143 ms;
rates are per real-time second and independent of the substep count), so beats visibly drive the
flame instead of lagging behind its 4x-faster motion, while it still settles without flicker.
Monitor pause freezes both the cached image and
simulation; resuming discards the paused interval. Global pause stops the host clock.
Large scheduling gaps do not cause unbounded catch-up. First-frame preparation
warms the source for four simulated seconds before revealing the host.

The source count is `clamp(ceil(width / height * 8), 6, 48)` per monitor. A 16:9
monitor uses 15 sources, 3440 × 1440 uses 20, and 5120 × 1440 uses 29. Equal aspect
ratios retain the same density regardless of resolution or preview size. The bed is
mapped with a small horizontal overscan (half a source spacing past each border, so it
scales with the source count for any aspect ratio): the outermost sources sit just beyond
the screen edges, so the visible left/right edges are covered by sources with neighbors on
both sides — a full-width bed rather than a lower "arch" at the far edges. By default the
base sits near the very bottom of the monitor (position Y still moves it up or down).

Volume shading is capped at 1080 × 720 pixels per monitor, preserving aspect ratio.
A 3840 × 2160 output renders the fire at 1080 × 608 before compositing into the
desktop. Simulation textures require approximately 84.4 MiB per monitor, excluding
the render surface, swap-chain buffers and driver overhead. Frozen monitors reuse
their cached surface. The new renderer uses the existing render-worker ownership
and recovery path for initialization, teardown and device errors.

## Controls

- Intensity controls source strength; zero immediately hides fire and sparks while the
  old heat dissipates internally. The original background is black; custom backgrounds remain visible.
- The complete 64-band FFT is divided into as many non-overlapping logarithmic groups
  as there are flame sources, reversed spatially: treble on the left, mids in the center,
  bass on the right. Each source's own band controls its fuel, lift and ember
  interval. Grouped bass/mids/highs no longer drive the whole fire bed together.
  Sensitivity uses the existing gain curve. Capture remains shared with the
  preview/desktop subscription.
- Audio drives the flame strongly, especially its height (lift), so beats are unmistakable rather
  than a faint warming of the bed. To keep the bands readable, `FireFrequencyBands` emphasizes each
  group's deviation above the current spectral average: because real music energizes every band at
  once (and the gain curve compresses the differences), a plain per-band level would raise the whole
  flame as one wall. With the contrast emphasis the loud frequencies rise as distinct columns — fire
  as an equalizer — while a flat spectrum reacts gently and evenly. A small level term preserves an
  overall response to volume.
- Without new audio data for 500 ms, the live target returns to silence.
- Size and position transform both the fire and the embers within each monitor.
- View offers original, solid-color and imported local-image backgrounds. Images use Cover
  independently of the fire transform. The GPU combines the background with the volume's
  transmittance; missing/invalid files fall back to the original background. Each monitor
  owns and releases its background texture along with its simulation.
- Effects offers a persistent sparks toggle; it changes visibility without advancing physics.
- The warm theme retains the approved incandescent palette. Other themes tint
  the flame and embers. Defaults use the warm theme and zero glow.
- Glow adds a restrained nine-tap light spread during composition; the base fire
  remains visible with glow zero. This is an artistic display-space spread, not HDR bloom.

Preferences remain per wallpaper. The host applies the requested settings before
first-frame preparation, so the initial frame already uses the requested palette.

## Validation and measured cost

Validation on 17 September 2026:

- 144 unit tests pass, including customization persistence, position-pad mapping,
  frame-cap equivalence, independent pause clocks,
  time discontinuities, catalog defaults, the hidden legacy experiment, and one-to-one
  reversed routing of every FFT bin at six source counts, and monitor aspect ratios.
- Full native smoke passes for all 13 wallpapers and the settings window.
- Background checks verify solid color, imported image, missing-file fallback, fixed
  background during fire transforms and sparks visibility. Settings-only redraws remain deterministic.
- Living Fire GPU checks cover size, position, palette, glow, zero intensity,
  no simulation advancement from settings-only redraws, independent monitor pixels
  and resuming without catch-up. Both viewports are exercised on a shared host.
  Isolated FFT signals must increase heat in the correct left/center/right region,
  with at least 3x the increase measured in either other region. A speed check
  confirms that 60 logical ticks advance exactly four simulated seconds. Coverage
  checks require visible fire near the base in every output column, including both
  edges, at 640 × 360, 1280 × 360 and 360 × 640.
- The shared study harness passes finite material/particle checks, projection
  reduction, nine active source regions, single-flame mode, source response to audio,
  silence recovery, invalid input, repeatable reset and 30 seconds of playback (120 simulated seconds).

On the **NVIDIA GeForce RTX 5070 Ti**, a short 12-frame D3D timestamp sample at
30 FPS measured approximately **3.25 ms median at 640 × 360** and **3.82 ms median
at 3840 × 2160**. Queries include eight physics substeps and rendering/composition,
exclude Present and CPU/UI work, and reject disjoint timestamps. These measurements
are a local smoke baseline, not a cross-device performance guarantee or a long-run
benchmark. Integrated GPUs and physical multi-monitor/DPI configurations still
need hardware validation; the automated freeze test uses two regions of one surface.

Artifacts: `artifacts/settings-redesign/full-checks/living-fire-checks.json` and
`artifacts/settings-redesign/study-checks/checks.json` (generated, not versioned).

Living Fire was created by Marcelo Bossle and is covered by the HYPNIX proprietary
license. Its manifest and credits identify Marcelo Bossle as author and copyright
holder. The application output includes `LICENSE.txt`; third-party components retain
their own existing licenses.

To run focused GPU checks after building the native smoke project:

```powershell
dotnet artifacts/fire-integration/smoke/Hypnix.NativeSmoke.dll artifacts/fire-review --fire-only
```

To review the real gallery window with Living Fire selected and separate review
preferences, without automatically applying a desktop wallpaper:

```powershell
dotnet artifacts/fire-integration/smoke/Hypnix.NativeSmoke.dll artifacts/fire-review --show-fire-gallery
```

The app's normal preview and Start actions use this same renderer. A source push does
not create a portable release or publish a Store package.
