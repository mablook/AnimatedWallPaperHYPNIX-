# Fire Ring V1 - Powerful Reactive

Status: approved visual milestone on 2026-07-11.

This snapshot preserves the first HYPNIX Effekseer fire ring that the product owner approved as powerful, smooth and worth keeping. New fire experiments must use another package/profile and must not overwrite these files.

## Contents

- `HypnixFireRing.efkefc`: compiled fire-only effect.
- `HypnixFireRingSmoke.efkefc`: compiled smoke-only effect.
- `HypnixFireOnly.efkproj`: editable fire source.
- `HypnixSmokeOnly.efkproj`: editable smoke source.
- `Texture/`: source textures required when editing/exporting.

## Native playback profile

The matching runtime profile is the `PlaceFlame` / `PlaceSmoke` implementation in `Native/EffekseerBridge/HypnixEffekseerBridge.cpp` at this milestone:

- 48 flame emitters and 12 smoke emitters.
- Fire radius `4.42`.
- Intensity drive `0.55 + intensity * 0.65`, capped at `2.45`.
- Fire gate `(energy - 0.012) / 0.16`.
- Bass impact `pow(bass * intensityDrive * 2.10, 0.48)`.
- Fire X scale `0.018 + gate * (0.34 + bass * 0.27)`.
- Fire Y scale `0.018 + gate * (0.88 + bass * 1.52 + breathing * 0.38)`.
- Per-monitor Effekseer instances; paused viewports receive no runtime update.

## Integrity

- Fire SHA-256: `5FCA0CA663074DE7F2DFFA364A68A734B36CA6205CBDE60B0D949C9656BFB437`
- Smoke SHA-256: `685082792181F4149E86BCBA7F9A1E5714679FD32FF9709A415C6BC6B30E057E`
- Native bridge SHA-256 at approval: `A7A76D31BEA53A7655270726FE0BC3436895F842E670568CB015CD55EB47E696`

## Lessons captured

- Do not arrange complete bonfire/explosion effects around a circle; they appear as separate expanding blobs.
- Fire and smoke must be authored and controlled as separate layers.
- The UI intensity value must reach the native runtime; sensitivity alone is insufficient.
- Preserve a real silence gate, then compress real-world audio above it.
- Visual continuity requires narrow overlapping tongues, staggered phases and enough emitters.
- The current texture source is a useful prototype, but a realistic campfire requires a purpose-built fluid flipbook.
