# Event Horizon: visual study and first replacement

## Objective

Produce a convincing animated black hole while preserving the existing wallpaper ID,
settings, audio controls, per-monitor pause and the other eight built-ins. The first
replacement is local to `codex/event-horizon-v2` for visual approval. The validation and
packaging PR remains on its own branch.

## References inspected

- The earlier Kerr-Newman GLSL files in `artifacts/black-hole-source`: a large metric
  solver with volumetric disk integration, differential rotation, emission/absorption,
  relativistic frequency shifts and several bloom passes. Its complexity goes well
  beyond what this wallpaper needs.
- [Gargantua 2, AIandDesign](https://www.shadertoy.com/view/NfVXDd): viewed running and
  inspected its exposed Buffer A and Image code. Useful visual targets are the broad
  rear-disk arc, narrow foreground band, warm filaments, dark shadow and soft HDR halo.
  It separates ray tracing from final bloom and tone mapping.
- [NASA / Jeremy Schnittman's disk visualization](https://svs.gsfc.nasa.gov/13326/):
  the apparent upper and lower disk images arise from bent light paths; orbital motion
  changes brightness across the disk. Inclination changes the apparent silhouette.

These are study references. The new implementation does not include their shader code,
textures or postprocessing implementation. Since reference code was inspected, the
previous claim of a clean-room process has been replaced with an accurate description
of independent implementation. The model is a Schwarzschild exterior with artistic
plasma emission, not a complete Kerr-Newman simulation or a scientific prediction.

## Findings in the previous implementation

- It added photon-sphere emission after capture, lighting what should be a dark shadow.
- First-order ray integration, a zero-thickness crossing model and angular noise made
  the thin disk appear flat or unstable in compressed regions.
- It resolved directly into the display buffer without spatial HDR bloom.
- Audio changed the camera and multiplied absolute time in the rotation phase, which
  could displace the scene abruptly when the audio level changed.
- The stored gallery preview did not represent the image produced by current defaults.

## Implemented

- Midpoint integration of bent rays, with background sampled only after escape.
- A thin Gaussian emitting layer integrated over each ray segment; continuous opacity
  avoids skipped crossings without random temporal jitter.
- Periodic 3D cloud coordinates with differential rotation and radial detail for plasma
  filaments. No angular seam or explicit uniformly spaced radial stripe pattern.
- A stable, slightly tilted camera and restrained audio-driven emission. Audio does not
  alter accumulated rotation phase or move the camera.
- Separate HDR scene, horizontal/vertical scattering and final tone mapping, using the
  existing renderer's per-viewport resource allocation and disposal.
- Event Horizon has no temporal feedback. A newly created frozen viewport is initialized
  once; subsequent frozen frames reuse its image and bloom. Spectral Bloom retains its
  existing feedback path.
- Updated gallery preview, renderer revision and implementation credits.

## Verification on 2026-09-15

- Build: zero errors and warnings.
- 108 regression tests passed, none failed or skipped.
- Full native smoke passed all nine lifecycle checks, shell/gallery construction and
  shutdown, existing Neon Ribbons/Liquid Orbs/Spectral Bloom checks, and Event Horizon.
- Event Horizon checks cover animation, audio response, intensity/glow/colors, exact
  silence reset, viewport origin and independent frozen/moving displays. Added an
  image check for a dark shadow beneath a visibly luminous rear-disk arc.
- Captured 1280x720, 1920x1080, 3440x1440 and portrait 720x1280 output. The focused mode
  also writes 48 motion frames at 24 FPS for a two-second visual review clip.
- Measured approximately 2.48, 4.93, 10.93 and 2.46 ms/frame, respectively, on this
  machine. These measurements include synchronous GPU readback; they are not desktop
  presentation FPS or a performance guarantee for other GPUs.

Artifacts are under `artifacts/event-horizon-study`: `before`, `review`, `full-smoke`,
`test-results`, `event-horizon-v2.mp4` and `event-horizon-v2.gif`. The previous shader
is also saved as `EventHorizon-before.hlsl`; the previous committed implementation
remains available at `52cf34b:Shaders/EventHorizon.hlsl`.

To reproduce the focused visual test without attaching to Explorer:

```powershell
dotnet build Tests/Hypnix.NativeSmoke/Hypnix.NativeSmoke.csproj -c Release -p:OutDir="$PWD/artifacts/event-horizon-study/bin/"
./artifacts/event-horizon-study/bin/Hypnix.NativeSmoke.exe artifacts/event-horizon-study/review --event-horizon-only
```

## Review limits

The bright inner filaments and highly compressed secondary ring still warrant visual
review in motion. HDR buffers increase memory usage compared with the original single
pass. Live desktop, 4K/mixed-DPI operation, prolonged GPU use and user visual acceptance
remain pending. The running application and its preferences were not replaced or
modified during this study. Existing README/macOS study edits were preserved.

## Follow-up: remove the detached inner ring

At the user's request, attenuate disk emission from rays after their closest approach
falls between 2.15 and 2.6 horizon radii. This is an artistic weighting of higher-order
images; the capture boundary, foreground disk and primary ray geometry are unchanged.
A smooth transition avoids the visible notches produced by terminating rays abruptly.
The Event Horizon native checks passed again, including audio, controls, silence reset,
dark shadow and independent monitor freeze. Captures and the previous shader are in
`artifacts/event-horizon-study/remove-inner-ring`. The source and test-app gallery
preview were refreshed and the test app reopened with the user's saved settings.

The transition was subsequently extended to suppress the two remaining pointed
junction tails, following user review. At 1920x1080, the central foreground patch
is pixel-identical to the preceding version; pixels outside a 260-pixel radius
change by at most one channel level (out of 255). Native Event Horizon checks
passed again. Before/after captures and settings backup are under
`artifacts/event-horizon-study/smooth-junctions`.

## Follow-up: spatial audio response

The previous response mostly multiplied global emission, which was difficult to see
at the user's maximum intensity/glow. Event Horizon now receives the existing 64
logarithmic FFT bands, with sensitivity applied once. The capture service already
smooths the input; the renderer adds no temporal audio history.

Frequencies map continuously from the inner emitting disk (bass) to its middle and
outer material (mids/highs). Lensed images follow the same material-space mapping.
Local audio shears plasma filaments and changes their ridge/trough contrast and warm
emission, making the response visible even with high exposure. Camera, lens geometry,
shadow and removed inner ring remain unchanged. The silence capture is byte-identical
to the approved silhouette revision.

Validation: 108 regression tests and all nine native lifecycle checks passed.
Synthetic low/mid/high bands produced distinct response radii of 82.9/132.8/174.7
pixels at 640x360. Tests also verify different individual bands with identical grouped
profiles, zero-spectrum precedence over stale grouped audio, exact silence recovery,
nonuniform-spectrum monitor freeze, and visible response at maximum controls.
Maximum-control mean RGB change was 11.10 out of 255 for the test audio fixture.

An optional six-second `--audio-probe` in the native harness observed 276 live FFT
frames, with nonzero low/mid/high peaks. It plays and records no audio; it writes only
aggregate levels. This confirms system output reaches capture, not subjective musical
acceptance. Test results, captured frames and a settings backup are in
`artifacts/event-horizon-study/spectral-response`. Its `bin/HYPNIX.exe` is the new
review application; the preceding test executable is preserved in the parent `bin`.

## Follow-up: brighter default was toned down, audio strengthened

User review found the default too bright and the audio response too weak. Two shader-only
changes, kept inside the existing native checks: base exposure in the composite lowered
(`color *= 0.65` to `0.48`) so the disk no longer starts blown out at default intensity; and
the audio made more assertive by raising the disk-frequency response (`1 - exp(-5*a)` to
`-9*a`), the filament shear (`0.22` to `0.35`) and the ridge push (`0.12 + 3.4*ridges` to
`0.10 + 5.5*ridges`). Camera, shadow geometry and silence reset are unchanged. Native Event
Horizon checks pass with the audio response roughly doubled (overall change 11.4 -> 19.3;
bass/mids/highs 0.76/1.18/2.87 -> 1.38/2.26/5.38; max-controls 11.1 -> 17.3) while the dark
shadow and lensed arc contract still holds.
