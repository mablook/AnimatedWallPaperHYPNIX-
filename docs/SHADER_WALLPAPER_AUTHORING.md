# Authoring a shader wallpaper

This is the practical recipe for adding a new GPU wallpaper to HYPNIX. Follow it and a new
audio-reactive, multi-monitor, theme-aware wallpaper drops into the gallery with a rendered
preview and automated coverage. It captures the conventions used by the shipping shaders
(Neon Ribbons, Liquid Orbs, Event Horizon, Fractal Pyramid, Kaleidoscope, Lotus).

For the *creative* side (what makes an animation relaxing) see
[Relaxing animation design](RELAXING_ANIMATION_DESIGN.md). This document is the *how*.

## 0. Policy: the code must be ours

HYPNIX is a commercial product, so every shipped shader must be **our own clean-room code**
under a commercial-compatible license. Enforced by `Tests/Hypnix.Tests/AssetContractTests.cs`
(each `wallpaper.json` needs an `author` and a license on the commercial allowlist, e.g.
`LicenseRef-HYPNIX-Proprietary`).

- You may **study** a public technique or effect, but never copy or paste third-party shader
  code, and never ship a "modified version" of code whose license forbids commercial use.
  Re-implement the *concept* with our own structure, parameters and naming.
- Techniques themselves (raymarching, domain folding, kaleidoscope folds, metaballs, FBM
  noise, ACES tonemap) are generic and fine to use.
- Document the origin honestly in the wallpaper's `CREDITS.txt` (see the existing ones).

## 1. The 30-second recipe

To add a shader called `Foo`:

1. `Shaders/Foo.hlsl` — the shader (template in section 4).
2. `Services/WallpaperKind.cs` — add `Foo` to the enum.
3. `Services/NativeWallpaperHost.cs` — add `Foo` to the `NativeRenderMode` enum, to the four
   GPU-mode conditions, and to the shader-name switch.
4. `Services/WallpaperRequest.cs` — map `WallpaperKind.Foo => NativeRenderMode.Foo`.
5. `Services/WallpaperSession.cs` — add `NativeRenderMode.Foo` to the non-layered list.
6. `Services/AethelisGpuRenderer.cs` — only if the shader reads the 64-band spectrum: add a
   `_usesFoo` flag and include it in the spectrum-fill condition.
7. `Assets/Wallpapers/foo/` — `wallpaper.json`, `CREDITS.txt`, and a `preview.png`.
8. `Tests/Hypnix.NativeSmoke/Program.cs` — add a render check, the mode and the kind.
9. Build, run the native smoke (it validates the shader and writes the preview), copy the
   preview, run the tests.

Sections 5-9 give the exact edits.

## 2. How a frame is produced

- Each wallpaper runs on a dedicated background render thread (`NativeWallpaperHost`), so the
  shader never blocks the UI. See `docs/DESKTOP_INTEGRATION_FINDINGS.md` for the hosting model.
- `AethelisGpuRenderer` compiles the shader at runtime (`VSMain` as `vs_5_0`, `PSMain` as
  `ps_5_0`), then for every monitor calls `RenderViewport(x, y, w, h, ...)` which sets the
  viewport, updates the shared constant buffer and draws a **full-screen triangle** (3 verts).
- One shader instance serves all displays. The scene must be **self-contained per viewport**
  (use `Resolution` for aspect, ignore `Origin`) so each monitor shows the same wallpaper and
  a paused monitor can be frozen independently.

## 3. The render contracts

`Tests/Hypnix.NativeSmoke` renders your shader headlessly on real D3D11 and enforces these.
A generic, reusable check (`NeonRibbonsRenderChecks.Run`) covers most shaders:

1. **Animates in silence** — a frame at t=8 must differ from t=9 with no audio. Drive motion
   from `Time`, not only from audio.
2. **Audio response** — a silent frame vs a frame with test audio must differ by a mean of
   at least ~4/255 over the frame. Map audio to something visible (brightness is easiest).
3. **Intensity is the master gain** — `Intensity = 0` must resolve to **pure black**. End the
   pixel with `col *= sqrt(clamp(Intensity,0,8) / 3);` before the final tonemap so 0 → black,
   and the default (3) → 1.0.
4. **Glow changes the image** — `Glow` (0..3) must visibly alter output (a halo/brightness
   multiplier is enough).
5. **Viewport-origin independence** — rendering the same content at two x-offsets must be
   pixel-identical. Use only `input.UV` and `Resolution`; never read `Origin`.

Event Horizon has stricter, bespoke checks (dark shadow vs lensed arc, per-band response
radii, silence reset); a normal shader only needs the five above.

## 4. Shader template

Copy this, rename, and implement `PSMain`. It already satisfies every contract in section 3.

```hlsl
// HYPNIX Foo - original clean-room implementation. (c) HYPNIX.
// Generic technique (no third-party shader code): <describe your technique here>.
// Motion is time-based and calm; the 64 audio bands modulate brightness by frequency.
cbuffer FrameData : register(b0)
{
    float2 Resolution; float Time; float Bass;
    float Mids; float Highs; float Intensity; float Glow;
    float2 Origin; float2 Padding;
    float3 StartColor; float ColorPadA;
    float3 EndColor; float ColorPadB;
    float4 Spectrum[16]; // 64 bands; omit this line if you only use Bass/Mids/Highs.
};

struct VertexOutput { float4 Position : SV_Position; float2 UV : TEXCOORD0; };
VertexOutput VSMain(uint id : SV_VertexID)
{
    VertexOutput o;
    float2 uv = float2((id << 1) & 2, id & 2);
    o.UV = uv;
    o.Position = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
    return o;
}

float3 Palette(float d) { return lerp(StartColor, EndColor, saturate(d)); }

// One of the 64 bands; f in [0,1] maps low -> high frequency. (Needs Spectrum[16].)
float Band(float f)
{
    int i = clamp((int)(f * 64.0), 0, 63);
    return Spectrum[i >> 2][i & 3];
}

float4 PSMain(VertexOutput input) : SV_Target
{
    // Aspect-correct, centered coordinates. Independent of the monitor origin.
    float2 uv = float2(input.UV.x, 1.0 - input.UV.y) * 2.0 - 1.0;
    uv.x *= Resolution.x / max(Resolution.y, 1.0);

    float intensity = clamp(Intensity, 0.0, 8.0);
    float glow = max(Glow, 0.0);
    float t = Time;

    // ---- your scene here (drive motion from t; keep it calm) ----
    float3 col = Palette(0.5 + 0.5 * sin(length(uv) * 6.0 - t));
    col *= 0.25 + 1.6 * Band(saturate(length(uv) * 0.6)); // audio -> brightness by frequency
    // ------------------------------------------------------------

    col *= 0.7 + 0.6 * glow;                 // glow control
    col *= sqrt(intensity / 3.0);            // master gain; Intensity = 0 -> black
    col = saturate((col * (2.51 * col + 0.03)) / (col * (2.43 * col + 0.59) + 0.14)); // ACES
    return float4(col, 1.0);
}
```

## 5. Audio

The constant buffer always carries `Bass`, `Mids`, `Highs` (0..1, smoothed by capture). For
finer control, opt into the **64 logarithmic bands** (`Spectrum[16]` as `float4`, i.e. 64
floats, low → high). The bands are only filled for shaders that opt in (section 6.6).

Read a band with the `Band(f)` helper. Conventions we follow:

- **Audio drives brightness, not the motion.** Keep rotation/camera/geometry on `Time`; let
  the spectrum change how bright things are. (Event Horizon and the flowers work this way.)
- **Map frequency to space.** Common choices: by radius (inner = lows, outer = highs) as in
  Fractal Pyramid, or by angle (petals = bands) as in Lotus. This makes it read as a
  spectrum, not a global flash.
- **Stay calm at silence.** Add a small floor so the scene is faintly visible with no audio
  (e.g. `0.25 + 1.6 * Band(...)`), and let audio brighten from there.
- Sensitivity is already applied to the bands before they reach the shader; don't re-scale by
  a magic number.

If your shader is purely ambient, you can ignore audio entirely — but it still must clear the
"audio response" gate, so at least let one band nudge brightness.

### Size and position (optional)

The user can zoom and move a wallpaper from the settings window. Three extra cbuffer fields
carry this: `Scale` (0.3..3, default 1), `OffsetX` and `OffsetY` (-1..1, default 0). They sit
in previously-unused padding slots, so declaring them does not change the layout:

```hlsl
    float2 Origin; float PaddingX; float OffsetY;   // was: float2 Origin; float2 Padding;
    float3 StartColor; float Scale;                 // was: ... float ColorPadA;
    float3 EndColor; float OffsetX;                 // was: ... float ColorPadB;
```

Apply them right after your centered, aspect-corrected uv — at the defaults it is the identity,
so the render contracts are unaffected:

```hlsl
uv = (uv - float2(OffsetX, OffsetY) * H) / max(Scale, 0.05); // H = your vertical half-extent
```

Support it wherever it makes sense (the abstract shaders do); leave it out of shaders with a
fixed framing (Event Horizon keeps a fixed lens so its shadow/arc stays put).

## 6. The wiring, file by file

### 6.1 `Services/WallpaperKind.cs`
Add the kind to the enum (append at the end):
```csharp
    EventHorizon,
    FractalPyramid,
    Kaleidoscope,
    Lotus,
    Foo
```

### 6.2 `Services/NativeWallpaperHost.cs`
Add to the `NativeRenderMode` enum, then to the **four** GPU-mode conditions (window style,
alpha, `InitializeGpuRenderer`, `RenderFrame`) which all share the suffix
`... or NativeRenderMode.Lotus` — append `or NativeRenderMode.Foo` to each. Finally add the
shader-name case:
```csharp
    NativeRenderMode.Lotus => "Lotus.hlsl",
    NativeRenderMode.Foo => "Foo.hlsl",
```

### 6.3 `Services/WallpaperRequest.cs`
```csharp
    WallpaperKind.Foo => NativeRenderMode.Foo,
```

### 6.4 `Services/WallpaperSession.cs`
Append `or NativeRenderMode.Foo` to the `useLayeredWindow: mode is not (...)` list, so the GPU
path uses an opaque (non-layered) window like the other shaders.

### 6.5 `Services/AethelisGpuRenderer.cs` (only if you read `Spectrum`)
Add a flag and include it where the 64 bands are written:
```csharp
private readonly bool _usesFoo;
// in the constructor:
_usesFoo = string.Equals(shaderFileName, "Foo.hlsl", StringComparison.OrdinalIgnoreCase);
// in RenderViewport, the spectrum-fill condition:
if (_usesEventHorizon || _usesFractalPyramid || _usesKaleidoscope || _usesLotus || _usesFoo)
```
Plain shaders (no feedback/fluid/effekseer) render through the default `Draw(3, 0)` path — no
other renderer change is needed.

### 6.6 The wallpaper package `Assets/Wallpapers/foo/`
`wallpaper.json` (the gallery reads this; `kind` must match the enum name):
```json
{
  "id": "foo",
  "title": "Foo",
  "type": "native-audio-visualizer",
  "renderer": "foo-hlsl-v1",
  "preview": "preview.png",
  "previewKind": "rendered-shader",
  "audio": "wasapi-loopback",
  "multiMonitor": "independent-cover",
  "kind": "Foo",
  "author": "Your Name",
  "license": "LicenseRef-HYPNIX-Proprietary"
}
```
`CREDITS.txt` — state it is an original clean-room HYPNIX implementation and which generic
technique/concept it uses (copy the style of a neighbour). `preview.png` is generated in
section 7.

The `.csproj` copies `Shaders/**/*.hlsl` and `Assets/Wallpapers/**/{wallpaper.json,CREDITS.txt,
preview.png}` to the output automatically — no project edits needed.

## 7. Native smoke coverage and the preview

In `Tests/Hypnix.NativeSmoke/Program.cs`:

- Add the reusable render check (also writes `foo.png` and `foo-audio.png` to the output):
  ```csharp
  NeonRibbonsRenderChecks.Run(parent, output, "Foo.hlsl", "foo");
  ```
- Add `NativeRenderMode.Foo` to the mode-lifecycle loop and `WallpaperKind.Foo` to the gallery
  kind assertion.

Then build and run the smoke; it renders on the real GPU, enforces the contracts, and emits
the preview frames:

```powershell
dotnet build Tests/Hypnix.NativeSmoke/Hypnix.NativeSmoke.csproj -c Debug
Tests/Hypnix.NativeSmoke/bin/Debug/net8.0-windows/Hypnix.NativeSmoke.exe artifacts/native-smoke
Copy-Item artifacts/native-smoke/foo-audio.png Assets/Wallpapers/foo/preview.png
```

Use `foo-audio.png` (the scene lit by audio) for a vibrant card, or `foo.png` (silent
baseline) for a calmer one. Requires a machine with a D3D11 **hardware** adapter; the smoke
does not run on GPU-less CI runners (it is built there, not executed).

## 8. Build, test, run

```powershell
# Unit tests (includes the wallpaper manifest/licensing contract)
dotnet test Tests/Hypnix.Tests/Hypnix.Tests.csproj -c Debug

# Full app (auto-starts the last selected wallpaper; pick your card in the gallery)
dotnet build AnimatedWallPaper.csproj -c Release
bin/Release/net8.0-windows/HYPNIX.exe
```

The app writes `%LocalAppData%\HYPNIX\logs\wallpaper.log`; a successful GPU wallpaper logs
`Aethelis GPU renderer initialized. Shader=Foo.hlsl; ...` and `Native host revealed`.

## 9. Checklist before you commit

- [ ] Shader is our own; `CREDITS.txt` documents the origin; license is on the commercial allowlist.
- [ ] All five render contracts pass in the native smoke (animation, audio, black-at-0, glow, origin).
- [ ] `Intensity = 0` is pure black; default looks right at Intensity 3, Glow 1.2.
- [ ] Motion is calm and time-based; audio maps to brightness by frequency; calm at silence.
- [ ] Wired in all files in section 6; gallery card count increased by one.
- [ ] `preview.png` present and representative.
- [ ] 108 unit tests pass; native smoke exits 0.

## Tuning knobs (what to expose to users)

`Intensity` (master gain), `Sensitivity` (audio gain, applied before the shader), `Glow`
(halo/brightness) and `ColorTheme` (drives `StartColor`/`EndColor`) are the live controls.
Design so their defaults (Intensity 3, Sensitivity 4, Glow 1.2, theme 0) already look good,
and keep every value bounded so extreme settings cannot blow out or destabilise the frame.
