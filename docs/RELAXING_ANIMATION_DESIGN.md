# Designing relaxing animations

This document turns the neuroscience of relaxation into concrete, actionable rules for
authoring and tuning HYPNIX wallpapers. HYPNIX content runs for hours in the periphery of a
person's attention, so "relaxing" is a measurable design target, not decoration. The goal is
content that lets a tired brain rest instead of demanding it.

This is design guidance derived from public neuroaesthetics and digital-media research
(see [References](#references)). It is not a medical claim or a therapeutic promise.

## How the brain relaxes

Calm, continuous animation nudges the nervous system out of alert processing and into a
restful state. Four mechanisms matter for us:

- **Brainwave transition.** Attention shifts from **Beta** (alert, effortful focus) toward
  **Alpha** (relaxed, meditative) and eventually **Theta** (drowsiness). Content should
  invite this drift rather than repeatedly yanking attention back to Beta.
- **Soft Fascination.** Gentle, slowly evolving scenes hold *involuntary* attention with a
  light grip, which lets the brain's *executive* attention (the prefrontal cortex) recover
  from daily fatigue. This is the core idea from Attention Restoration Theory: fascinating
  but undemanding stimuli restore directed attention.
- **Reduced cognitive load / Default Mode Network (DMN) quieting.** When there is nothing to
  identify, decode, or resolve, the brain leaves "problem-solving" mode. Abstract flow in
  particular deactivates the DMN, the network active during rumination about the past or
  worry about the future, producing present-moment visual mindfulness.
- **Steady neurochemistry.** Calm loops are associated with lower cortisol (stress) and a
  **linear, stable** release of dopamine and oxytocin (comfort). This is the opposite of the
  spiky, anxious dopamine hits of fast social-media feeds. **Avoid reward spikes**: no sudden
  reveals, hard cuts, or bursty motion.

The practical rule that follows from all four: **hold attention softly and never surprise the
viewer.** Continuity and predictability are features.

## Two families of relaxing motion

The brain reaches relaxation through two slightly different neurological paths. HYPNIX should
support both intentionally.

### Real / "satisfying" motion (physics, nature, texture)

Perfect physical simulations, nature footage, soft 3D textures, seamless mechanical motion.

- **Order and closure.** The brain dislikes unfinished or chaotic tasks. Perfect, repetitive,
  fully resolved motion closes visual "loops" and produces immediate relief.
- **Tactile empathy (mirror neurons).** Slow motion over a believable texture (kinetic sand,
  soft foam, water) makes the viewer *feel* the surface without touch, which reads as comfort.
- **Visual ASMR.** Slow, close, deliberate movement activates areas associated with care and
  coziness.

Best for **reducing acute anxiety and stress peaks** through a sense of control and safety.

### Abstract / fluid motion (fractals, gradients, morphing forms)

Metamorphosing geometry, infinitely expanding fractals, slow color-field gradients.

- **Low cognitive load.** No faces, objects, or story to parse, so the interpreter shuts off.
- **DMN deactivation and trance.** Continuous abstract flow silences repetitive thought and
  induces a light meditative state.

Best for **entering meditative states and inducing sleep** through disconnection and calm.

### Comparison

| Characteristic | Real / Satisfying | Abstract / Fluid |
| --- | --- | --- |
| Primary trigger | Predictability, order, real texture | Absence of narrative, color fluidity |
| Mental effect | Control, safety, order | Disconnection, trance, peace |
| Best for | Acute anxiety, stress peaks | Meditation, falling asleep |
| HYPNIX fit | Video (nature), fire/organic renderers | Gradient, metaball, plasma, bloom renderers |

## Design principles for HYPNIX

These are the authoring rules. They are intentionally specific so they can be reviewed.

### Motion and pacing

- **Slow and continuous.** Prefer drift over travel. As a starting range, dominant features
  should take **8-30 seconds** to cross a noticeable distance; ambient fields can be slower.
- **No hard cuts or teleports.** Every change is a transition. Reveals fade; they never pop.
- **Ease, never snap.** Use smoothstep/eased interpolation; avoid linear starts/stops that
  read as mechanical jerks (except deliberate "satisfying" mechanical loops, which must be
  perfectly periodic).
- **Steady frame pacing.** A stable 30 FPS is calmer than an uneven 60. HYPNIX caps at
  15/30/60; the dedicated render thread exists so pacing stays even under desktop load. Judder
  breaks the trance more than a lower frame rate does.

### Loops and continuity

- **Seamless loops.** Any repeating element must loop without a visible seam. Match position,
  velocity, and phase at the wrap point. A perceptible "jump" re-alerts the brain (Beta).
- **Long or non-obvious periods.** Either loop long enough that the period is not consciously
  noticed, or drive motion from continuous noise so there is no fixed period at all.

### Color and light

- **Low to moderate saturation.** Muted, harmonious palettes over neon primaries. HYPNIX's
  curated color themes and bounded sliders exist so authors cannot ship destabilizing values.
- **Slow gradient transitions.** Color should evolve gradually; blend across many seconds.
- **Gentle luminance.** Avoid abrupt brightness swings and any strobing. This is both a
  relaxation rule and a **safety** rule (see [Safety](#safety-and-accessibility)).
- **Restful contrast.** Keep a soft dynamic range; a dark shadow next to a soft HDR halo
  (as in Event Horizon) reads as depth without harshness.

### Composition

- **Prefer order and symmetry** for the "satisfying/control" path: radial, mirrored, or
  regular structures close visual loops.
- **Prefer open, non-representational flow** for the "abstract/trance" path.
- **Avoid DMN triggers.** No faces, readable text, clocks, logos, or implied narrative in
  relaxation content. The moment a viewer starts *interpreting*, executive attention re-engages.

### Audio reactivity (when enabled)

Audio-reactive wallpapers must relax, not excite. The FFT pipeline already smooths input, and
the product rule is that a visualizer stays **quiet at silence**.

- **Linear, stable response.** Map audio to gentle, continuous change (shear, warmth, gentle
  swell), not to sudden scale/flash. Reproduce steady dopamine, not spikes.
- **Attack slower than a beat detector.** Fast attack with slow release is acceptable for a
  living feel, but the resting state must dominate. Never let a loud transient throw the scene.
- **Silence is a valid, calm state.** At silence the scene returns to quiet, coherent motion,
  not a frozen or dead frame.
- **No camera moves from audio.** Audio may modulate material/emission; it must not move the
  camera or accumulate rotation phase (this was a specific Event Horizon correction).

## Mapping to the built-in renderers

The existing HYPNIX built-ins already fall into the two families. Use this to place new work.

- **Abstract / fluid (trance, sleep):** Ambient gradient drift, **Neon Ribbons**, **Liquid
  Orbs** (organic metaballs), **Spectral Bloom** (audio-reactive; keep the response gentle),
  **Event Horizon** (symmetric, organic plasma with a calm dark center), **Fractal Pyramid**
  (kaleidoscopic space-folding glow that stays calm at silence and reacts gently to audio),
  **Kaleidoscope** (symmetric evolving mandala; time-based motion with audio driving ring
  brightness by frequency), **Lotus** (a layered flower blooming and swaying in a slow
  breeze; audio drives petal brightness by frequency).
- **Organic / near-real (nature texture):** the Aethelis flame ring, the fire-ring/flamethrower
  Effekseer effects, and **Volumetric Fire** read as a natural element. Fire relaxes through
  flicker and warmth; keep ember motion alive at silence and let bass raise height/pressure
  smoothly rather than punching.
- **Real (nature footage):** the video renderer is the direct path to "real/satisfying"
  content. Prefer slow nature loops (water, foliage in wind, clouds) that already loop cleanly.

New renderers should declare which family they target and be reviewed against the matching
principles above, exactly as the Product direction gates new VFX from "one readable effect to
controlled complexity."

## Authoring checklist

Before proposing a wallpaper as "relaxing", confirm:

- [ ] Dominant motion is slow, continuous, and eased; nothing pops or teleports.
- [ ] Every repeating element loops seamlessly, or motion is noise-driven with no visible period.
- [ ] Frame pacing is steady at the chosen cap; no judder under normal desktop load.
- [ ] Palette is harmonious and not over-saturated; color transitions span seconds.
- [ ] No strobing; luminance changes are gradual (also required by the safety rule).
- [ ] No faces, text, or narrative that pull the viewer into interpretation.
- [ ] If audio-reactive: quiet and coherent at silence, linear response, no camera motion from audio.
- [ ] The scene is still pleasant after watching for several minutes (it does not become tiring).

## Safety and accessibility

Relaxation and safety point the same way here. Do not ship content that flashes or strobes.
As a hard limit, avoid luminance flashes faster than **3 per second** and avoid large,
high-contrast full-field transitions, which can trigger photosensitive seizures. The bounded
sliders, curated themes, and gentle-luminance rule above keep authored content inside safe
limits. When in doubt, slower and softer is both calmer and safer.

## References

Peer-reviewed and institutional sources that inform this guidance:

- Attention Restoration Theory / Soft Fascination — *How soft fascination helps restore your
  tired brain*: https://elemental.medium.com/how-soft-fascination-helps-restore-your-tired-brain-27669cd0be9d
- ASMR, stress and relaxation (Hunter Medical Research Institute):
  https://hmri.org.au/the-science-behind-asmr-and-its-benefits-for-stress-and-relaxation/
- ASMR neuroscience review (NCBI PMC): https://www.ncbi.nlm.nih.gov/pmc/articles/PMC9204527/
- ASMR overview (Wikipedia): https://en.wikipedia.org/wiki/ASMR

Popular-media examples that illustrate the concepts (not scientific sources):

- Brainwave/relaxation explainer: https://www.youtube.com/watch?v=QxfF-14AsYo
- "Satisfying" physics/texture loops: https://www.youtube.com/playlist?list=PLQ2M2JyEObRx3QzVjbfaFnN1KrLd6zQ0W
- Abstract/fluid flow examples: https://www.youtube.com/watch?v=O191fzlSs-4
