# HYPNIX volumetric fire research log

## Purpose

This document records the complete fire-visualizer investigation so future work does not repeat failed approaches.
The goal is an audio-reactive, realistic line of campfire flames across the bottom of each monitor. The approved visual
target is [`assets/volumetric-fire-approved-target.png`](assets/volumetric-fire-approved-target.png): a narrow shared
fuel bed, independently shaped flame tongues, irregular heights, bright internal combustion, sparse embers, restrained
smoke, and large clean black space above.

This target is not a ring, a glowing rectangle, a collection of sparks, an explosion, or a continuously visible set of
radial jets.

## Product invariants that already work

- The original audio visualizer is approved and must remain unchanged.
- `Assets/Effects/Milestones/FireRingV1` is an immutable approved procedural/Effekseer milestone.
- Wallpapers run behind desktop icons, not above them.
- The Direct3D host supports the virtual desktop and different monitor resolutions.
- Each monitor has an independent renderer/effect state so foreground-app pause can freeze one display or all displays.
- WASAPI loopback and the 64-band FFT work with the user's wireless headset at 48 kHz.
- The visualizer settings panel can change sensitivity, intensity, glow, and color without affecting other wallpapers.
- 15, 30, and 60 FPS modes work; 60 FPS is required for fire.
- Startup uses a prepared first frame, avoiding the historical white flash.

Do not trade away these behaviors while researching fire quality.

## Investigation timeline

### 1. Procedural circular fire shader

The first successful milestone used a lightweight HLSL ring with audio-driven deformation. It became smooth and
reactive after tuning bass compression, asymmetry, glow, and 60 FPS. It is useful as a performance and integration
baseline, but it does not contain physically plausible flames.

Learning: a signed-distance ring plus noise can produce an attractive abstract visualizer, not realistic combustion.

### 2. Particle and sprite experiments

Multiple particle-based variants were tested: sparks, smoke puffs, detached radial fragments, and Effekseer emitters.
They often looked like grinding sparks, expanding circles, polygonal smoke, or unrelated effects layered together.
Adding more emitters made the image busier rather than more realistic.

Learning: visual complexity is not physical coherence. Fire needs a connected body, internal temperature structure,
curling sheets, breakup, and a credible transition to smoke.

### 3. Effekseer bridge

Effekseer 1.80 was integrated through `Native/EffekseerBridge` and shares the existing Direct3D 11 device/context.
Important integration findings:

- The native bridge must be rebuilt before the managed app so the new DLL reaches the output directory.
- Every viewport needs its own bridge instance; sharing one state broke per-monitor pause.
- Rendering the GPU frame inside a nested GDI monitor loop updated effects more than once and broke pause semantics.
- A paused viewport must skip effect updates while still drawing the last state.
- Effect dependencies must travel together. Loading a smoke `.efkefc` without its textures produced dark squares.
- A fixed number of permanently looping handles makes flames appear pasted onto the scene.
- Birth/growth/decay envelopes improved timing but could not turn flipbooks into a live fluid.

Effekseer remains useful for authored particles, embers, magic and abstract visualizers. It is not the chosen core for
realistic volumetric fire.

### 4. EmberGen workflow

EmberGen 1.2.9 was installed and the `fire_jet_3` preset became the strongest authored fire reference. The project and
exports live under `Assets/Effects/FlamethrowerRingV2`.

A 4096x4096 RGBA flipbook was exported as an 8x8 atlas with 64 frames (512x512 per frame). The source looked excellent
inside EmberGen, but runtime experiments exposed several limits:

- The atlas is a prerecorded view, not a live simulation.
- Radial scaling distorted the horizontal jet into thin streaks.
- Keeping every atlas player alive produced a static ring of jets.
- Restarting atlas players on beats produced visible appearances rather than natural ignition.
- The texture already contained smoke; adding legacy smoke created dependency and composition artifacts.
- Flipbook playback can be audio-triggered, but the fluid itself cannot react to new FFT input.

EmberGen is excellent for concept work, baked VFX, fallback loops and texture/VDB authoring. It is not currently a
redistributable runtime solver for HYPNIX. Commercial use also requires appropriate JangaFX licensing.

### 5. Procedural ray-marched ring

`HypnixVolumetricFire.hlsl` first attempted a stateless procedural 3D density field ray-marched in the pixel shader.
The result was a bright circular halo. Changing the source from a torus to a horizontal line created a rectangular
wall of illuminated noise rather than a campfire.

Learning: ray marching a procedural function gives depth cues, but without persistent state it has no transported heat,
fuel history, pressure or coherent flame lifecycle.

### 6. Persistent 2D GPU field

The next implementation used ping-pong `R16G16B16A16_FLOAT` textures per monitor, with velocity, temperature and density.
A compute shader performed semi-Lagrangian advection, buoyancy-like motion, curl/noise, dissipation and fuel injection.

Confirmed technical findings:

- New GPU textures must be explicitly cleared. Undefined VRAM propagated invalid values and initially rendered black.
- GPU readback proved the compute shader was running: maximum temperature was 0.4619, density 0.3005, with active cells
  on both monitor grids.
- The first advection formula used the empty destination cell's zero velocity, so fuel stayed behind the taskbar. The
  taskbar changed color while the desktop appeared black.
- Backtracing into a lower source cell made the field rise.
- A 384x216 grid upscaled to 4K created visible 10x10 blocks because the pixel shader used point `Load` sampling.
- Increasing to 768x432 and using bilinear sampling removed the hard blocks but not the large smooth columns.
- Higher resolution and filtering cannot create combustion structure that is absent from the solver.
- Uniform injection along the bottom naturally becomes a wall. Source modulation alone does not create independent,
  internally detailed flame bodies.

Visual result: pixelated or blurred vertical columns and a large brown/orange block. Rejected.

### 7. Minimal 3D voxel experiment

The 2D state was replaced by two 3D volumes per monitor (`192 x aspect-adjusted-height x 64`) and a 64-slice ray marcher.
This proved that Direct3D 11 3D textures, compute dispatch and multi-monitor volume state can compile and run inside the
existing host. It did not produce realistic fire.

Why it failed:

- The state packed only velocity and one generic density channel.
- There was no independent fuel, oxygen, temperature, soot or reaction-progress field.
- There was no pressure solve or divergence projection, so the flow was not incompressible.
- Buoyancy and curl approximations moved density but did not model combustion.
- A full-width floor injector filled most voxels uniformly.
- The ray marcher mapped density directly to fire color, producing a smooth rectangular gradient/block.
- Depth alone is not realism. A 3D texture without a physically meaningful solver remains a 3D block.

Visual result: a large red/orange rectangular volume with a slightly uneven top. Rejected.

## Repeated failure patterns

Do not repeat these approaches as candidate product wallpapers:

1. Adding more particles to compensate for missing fluid behavior.
2. Stretching or rotating a single fire flipbook into many radial copies.
3. Keeping looping flame instances alive and scaling them with volume.
4. Calling stateless noise or a ray-marched SDF a fluid simulation.
5. Using one density channel as velocity, fuel, heat, flame and smoke simultaneously.
6. Injecting fuel uniformly across an entire edge without localized emitters and oxygen mixing.
7. Increasing grid resolution before the governing model is correct.
8. Judging only from compilation or nonzero GPU buffers; visual acceptance is a separate gate.
9. Exposing experimental fire cards as if they were usable wallpapers.
10. Describing a technical prototype as “realistic” before comparing it with the approved target.

## Required architecture for the next credible prototype

A new attempt is only justified if it includes these minimum elements:

### Simulation fields

- 3D velocity field.
- Separate fuel field.
- Separate temperature field.
- Smoke/soot density.
- Pressure and divergence buffers.
- Optional reaction-progress and oxygen fields.

### Solver passes

1. Inject localized fuel/temperature sources.
2. Advect velocity and scalar fields.
3. Apply buoyancy from temperature and smoke.
4. Apply vorticity confinement at multiple scales.
5. Compute divergence.
6. Solve pressure iteratively (Jacobi or a better GPU solver).
7. Project velocity to remove divergence.
8. Burn fuel according to temperature/oxygen, generating heat and soot.
9. Cool temperature and dissipate smoke at different rates.
10. Render with physically meaningful emission/absorption.

### Rendering

- Ray-marched blackbody emission based on temperature, not density alone.
- Separate smoke absorption/scattering.
- Blue/white core only where temperature supports it.
- Empty-space skipping or a conservative bounding region.
- Temporal jitter plus stable accumulation to avoid banding.
- Tone mapping that preserves internal detail instead of clipping to white.
- Quality levels and GPU timing budgets.

### Audio mapping

- Silence: low, localized pilot flames only; no explosions or continuous maximum output.
- Bass: short increases in fuel pressure/source velocity.
- Mids: vorticity/turbulence, not global brightness.
- Highs: sparse ember emission and fine breakup.
- Use attack/release envelopes and onset detection; raw FFT values must not directly scale the whole volume.

### Product constraints

- Preserve independent state and pause per monitor.
- Provide 15/30/60 FPS choices, with an adaptive simulation step.
- Detect feature level and VRAM budget; fall back safely.
- Keep normal-user permissions and Windows Store packaging viability.
- Avoid vendor lock-in unless there is a portable fallback.
- Any third-party engine/runtime must have reviewed redistribution and commercial licensing.
- Hide experimental entries from production builds.

## Tool/engine conclusions

- **Effekseer:** already integrated and useful for particles; insufficient as the core fluid solver.
- **EmberGen:** best current authoring/reference tool; exports flipbooks, sequences and VDB, not a HYPNIX runtime solver.
- **Unreal Niagara Fluids:** technically capable and likely the fastest route to the approved look, but Unreal is not
  installed and would add significant binary size, startup, packaging and integration work.
- **Custom DirectCompute solver:** best long-term fit for HYPNIX size/control/Store goals, but it is a substantial graphics
  engineering project and must implement the required solver passes above before another visual review.
- **NVIDIA-only solutions:** may accelerate research but are unsuitable as the only production renderer.

## Current disposition

- Preserve the original visualizer.
- Preserve `FireRingV1` unchanged.
- Preserve EmberGen sources/exports for research.
- Treat `Flamethrower Ring V2`, the 2D fluid field and the minimal 3D voxel field as failed experiments.
- Hide `Volumetric Fire 3D Prototype` from the product gallery.
- Do not continue parameter tuning on the rejected solvers.
- Next work begins with an architecture/spike for either Niagara integration or a complete multi-pass DirectCompute
  combustion solver; it does not begin with another shader color/noise adjustment.
