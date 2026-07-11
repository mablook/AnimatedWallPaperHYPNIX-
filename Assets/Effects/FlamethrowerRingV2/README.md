# Flamethrower Ring V2

Status: EmberGen authoring pipeline prepared; source flipbooks pending.

This package is deliberately separate from `Milestones/FireRingV1`. It targets volumetric flamethrower pulses rather than additive spark-like sprites.

## Required authored clips

1. `idle-loop`: restrained continuous combustion.
2. `jet-pulse`: narrow fuel origin, expanding turbulent body and broad detached head.
3. `impact-head`: short high-pressure combustion burst for bass transients.
4. `decay-smoke`: cooling smoke and fragmented flame after a pulse.
5. `embers`: sparse crackles triggered by high frequencies.

## EmberGen export contract

- Transparent RGBA PNG flipbooks.
- 8 x 8 frames (64 frames) for initial integration.
- 60 Hz simulation/playback reference.
- Identical framing, pivot and camera for all clips.
- Linear color where possible; avoid baking an opaque black background.
- Fire, smoke and embers exported separately so HYPNIX can mix them from FFT bands.

## Audio mapping

- Bass/onset: jet-pulse emission, pressure and impact-head opacity.
- Mids: playback rate, turbulence mix and sustained flame length.
- Highs: ember count and short spikes.
- Silence: idle-loop only, below the visualizer noise gate.

## Runtime

The production app will continue using Direct3D 11 and Effekseer. EmberGen is an authoring dependency only and will not be shipped with HYPNIX.
