# macOS port study

> **Status: STUDY / DESIGN ONLY — not implemented.** This document captures a plan for a future
> macOS version of HYPNIX. No macOS code exists in the repository yet and nothing here changes the
> current Windows behavior. It is a starting point for a future implementation.

## Verdict

A macOS port is **feasible**. The animated-wallpaper-behind-desktop-icons behavior is achievable with a
borderless `NSWindow` at the desktop window level (the approach used by Plash, ScreenPlay and similar apps).
The heavy work is re-implementing the four Windows-coupled layers: **desktop hosting, GPU rendering, audio
capture and UI**. The *content* (shader techniques, `wallpaper.json` manifests, business logic) ports easily.

Because desktop-level windows and system-audio capture are incompatible with the App Sandbox, distribution
must be **Developer ID + notarization, outside the Mac App Store** (same model as Wallpaper Engine/Plash).

## What is Windows-locked today

| Piece | Where | Portable? |
|---|---|---|
| `net8.0-windows`, `WinExe`, **WPF + WinForms** | `AnimatedWallPaper.csproj` | No — no WPF/WinForms on macOS |
| **WorkerW/Progman + `SetParent`** (wallpaper behind icons) | `Services/DesktopWorker.cs` (msg `0x052C`, `SHELLDLL_DefView`) | No — no equivalent; use `NSWindow` |
| **Direct3D 11** (Vortice) + swapchain + Win32 windows | `Services/NativeWallpaperHost.cs`, `Services/AethelisGpuRenderer.cs` | No — macOS uses **Metal** |
| **WASAPI loopback** (NAudio) | `Services/AudioSpectrumService.cs` | No — different system-audio capture on macOS |
| Tray `NotifyIcon`, `Screen.AllScreens`, `SystemEvents`, foreground/fullscreen | several `Services/*` | No — replace with AppKit/`NSWorkspace` |
| **Effekseer** native bridge (C++/D3D11 DLL) | `Native/EffekseerBridge` | Partial — Effekseer has a Metal backend (defer) |
| **HLSL** shaders | `Shaders/*.hlsl` | Partial — translate to **MSL** (mechanical) |
| FFT/bands, settings JSON, catalog, playback policy, package validation | `AudioSpectrumService.Analyze`, `AppSettingsStore`, `WallpaperCatalog`, `PlaybackPolicy`, ... | Yes — pure logic, reusable |

## The hard part: wallpaper behind desktop icons

There is no WorkerW trick on macOS. The proven approach:

- One **borderless, non-activating `NSWindow` per `NSScreen`**, at level
  `NSWindow.Level(rawValue: Int(CGWindowLevelForKey(.desktopIconWindow)) - 1)` (behind icons, above the
  static wallpaper), with `ignoresMouseEvents = true` and
  `collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle, .fullScreenNone]`.
- Render into a `CAMetalLayer`/`MTKView` inside that window.
- Other apps going fullscreen become their own Space that covers the desktop, so the wallpaper hides itself
  (the equivalent of our per-monitor pause).
- Rebuild windows on `NSApplication.didChangeScreenParametersNotification` (analog of our
  `DesktopEnvironmentMonitor.LayoutChanged`).

This is sandbox-incompatible → Developer ID / notarization, not App Store.

## Subsystem mapping

- **Render:** D3D11 → **Metal** (`MTLDevice`, `CAMetalLayer`, fullscreen pipeline). The `FrameData` constant
  buffer becomes an MSL `struct`.
- **Shaders:** HLSL → **MSL**. Manual (they are fullscreen fragment shaders) or via DXC → SPIR-V →
  SPIRV-Cross → MSL (the same toolchain used earlier, in reverse).
- **System audio:** **ScreenCaptureKit** (`SCStream` with `capturesAudio`, macOS 13+) or **Core Audio process
  taps** (`AudioHardwareCreateProcessTap`, macOS 14.2+). Requires a TCC permission (Screen Recording). FFT via
  **vDSP/Accelerate**. The audio on/off toggle maps 1:1 (`stopCapture` = off).
- **UI + "tray":** **SwiftUI/AppKit** with `NSStatusItem` (menu bar). (Avalonia is the alternative if maximum
  C# reuse is desired — see strategies.)
- **Pause/fullscreen/lock/battery:** `NSWorkspace` notifications, Spaces/`CGWindowList`,
  `DistributedNotificationCenter` (`com.apple.screenIsLocked`), IOKit power source.
- **Effekseer effects:** use Effekseer's Metal renderer or defer (start with the pure-GPU shaders).

## Strategy options

- **A) Cross-platform .NET (Avalonia + Metal):** reuses almost all C# logic, replaces WPF/WinForms with
  **Avalonia** (has a tray icon). Sensitive point: Metal-in-.NET is immature; the route would be MoltenVK
  (Vulkan→Metal) via Silk.NET plus native interop for the desktop window and audio. Maximum reuse, higher
  technical risk.
- **B) Native Swift + Metal (recommended):** rewrite the shell/host/render/audio in Swift/AppKit; reuse the
  shader algorithms (HLSL→MSL) and the `wallpaper.json` format. Best performance, integration and
  distribution; the business logic is small and easy to re-implement. Lowest risk, separate codebase.
- **C) Shared C# core + native shell:** extract the platform-agnostic logic to a library, but Swift does not
  consume .NET well, so the reuse effectively becomes "re-implement the logic" (what B already assumes). Only
  worthwhile if **both** shells are .NET (then it is A).

**Recommendation:** **B (Swift + Metal)** for the shell/host/render/audio, reusing the (translated) shaders
and the manifests. If the team is strongly .NET and accepts interop risk, **A (Avalonia + MoltenVK)**.

## MVP scope (cheapest to port first)

The **pure-GPU** wallpapers are the easiest (single/multi-pass fragment shaders with the same `FrameData` +
audio bands): **Ambient, Spectral Bloom, Neon Ribbons, Liquid Orbs, Event Horizon**. The Effekseer/GDI ones
(Fire Burst, Flamethrower Ring, Volumetric Fire, Aethelis) come later.

## Architecture (mirror of the Windows app)

| Windows (current) | macOS (proposed) | Notes |
|---|---|---|
| `MainWindow` (WPF) | `SettingsWindow` (SwiftUI) + `AppDelegate` + `StatusItemController` | agent app (`LSUIElement`) |
| `DesktopWorker` | `DesktopWindowFactory` → `DesktopWindow: NSWindow` per `NSScreen` | level `desktopIconWindow-1`, click-through |
| `NativeWallpaperHost` (D3D11/Win32) | `ScreenRenderer` (MTKView + CAMetalLayer) per screen | one per `NSScreen` |
| `AethelisGpuRenderer` | `MetalRenderer` + `ShaderLibrary` (pipeline cache) | pipelines shared via `MTLDevice` |
| `WallpaperController` | `WallpaperController` (Swift) | start/stop/pause/resume + `setAudioEnabled` |
| `WallpaperSession`/`IWallpaperSession` | `WallpaperSession` (aggregates windows+renderers) | same role |
| `AudioSpectrumService` (WASAPI/NAudio) | `SystemAudioService` (ScreenCaptureKit) + `FFTAnalyzer` (vDSP) | |
| `AudioSpectrumSource` (ref-count) | `AudioHub` (ref-count, `startCapture/stopCapture`) | off truly stops capture |
| `ForegroundAppMonitor`/`DesktopEnvironmentMonitor` | `EnvironmentMonitor` (NSWorkspace/DistributedNC/IOKit/didChangeScreenParameters) | fullscreen/lock/battery/layout |
| `PlaybackPolicy` | `PlaybackPolicy` (straight port) | same logic and tests |
| `WallpaperCatalog`/`WallpaperPackageManifest`/validator | same in Swift (`Codable`) | **same `wallpaper.json`** |
| `AppSettingsStore` | `SettingsStore` (`Codable`) in `~/Library/Application Support/HYPNIX/settings.json` | **same schema** |
| `LivePreview` (HwndHost) | `PreviewView` (MTKView in SwiftUI) | reuses `MetalRenderer` |
| Effekseer bridge (D3D11) | `EffekseerMetal` (phase 7) | defer |

### Flow

1. Launch (agent) → `SettingsStore.load` → `EnvironmentMonitor.start`.
2. `WallpaperController.start(entry)` → create a `DesktopWindow` per `NSScreen` → `MetalRenderer` with the
   shader for `entry.kind` → `AudioHub.subscribe` (if `audioReactive`) → start `CVDisplayLink`/`MTKView`.
3. Per-screen loop: build `FrameData` (time + bands + settings) → encode fullscreen draw → present.
4. `EnvironmentMonitor` → `PlaybackPolicy` → pause/resume per screen (`MTKView.isPaused`); per-monitor freeze =
   do not advance that screen's time/history.
5. Audio: `SystemAudioService` → `FFTAnalyzer` → 64 bands → renderer. Toggle = `stopCapture`.
6. Settings (SwiftUI bindings) → `SettingsStore.save` (debounced) + apply to controller/renderer/preview.
7. `didChangeScreenParameters` → rebuild windows/renderers (our "recovery").

### Suggested layout

```
HypnixMac/
  App/        AppDelegate.swift, StatusItemController.swift, HypnixApp.swift
  Settings/   SettingsWindow.swift, SettingsStore.swift, VisualizerPreferences.swift
  Desktop/    DesktopWindow.swift, DesktopWindowFactory.swift, EnvironmentMonitor.swift, PlaybackPolicy.swift
  Render/     MetalRenderer.swift, ScreenRenderer.swift, ShaderLibrary.swift, FrameData.swift, Shaders/*.metal
  Audio/      SystemAudioService.swift, FFTAnalyzer.swift, AudioHub.swift
  Catalog/    WallpaperCatalog.swift, WallpaperManifest.swift  (reads the existing wallpaper.json)
  Resources/  Assets/Wallpapers/** (reused manifests + previews), Info.plist, HYPNIX.entitlements
```

### Threading

- **Render:** `CVDisplayLink`/`MTKView` per screen; per-thread command buffers (Metal is fine).
- **Audio:** its own queue (`SCStream` handler) → publishes bands thread-safely (lock/atomic), as we already do.
- **UI:** main thread. `EnvironmentMonitor` posts on main.

## Proof-of-concept plan and checkpoints

Order (lowest risk first):

0. `NSWindow` at the desktop level (single screen) + `CAMetalLayer` drawing a fullscreen triangle.
1. Metal pipeline + `FrameData` + **Event Horizon** in MSL, time/FPS loop.
2. Multi-monitor + rebuild on layout change + pause per Space/fullscreen/lock/battery.
3. Audio (ScreenCaptureKit) → vDSP → 64 bands → reaction + toggle.
4. Port the remaining GPU shaders.
5. Settings UI (SwiftUI) + menu bar (`NSStatusItem`) + persistence (same `settings.json`).
6. Package `.app`, Developer ID + notarization, universal arm64/x64.
7. (Optional) Effekseer-Metal effects.

Validation checkpoints: (1) window truly behind icons and present across Spaces; (2) click-through works;
(3) survives sleep/wake, resolution change, monitor connect/disconnect; (4) acceptable GPU/energy cost (pause
when occluded); (5) audio permission + latency acceptable.

## FrameData interop (Swift ↔ MSL)

On macOS we own both producer (Swift) and consumer (MSL), so we do **not** need to match the Windows/HLSL
constant-buffer layout. Swift SIMD types share Metal's alignment, so mirror field-by-field and drop the
HLSL-only pads (`ColorPadA/B`). Put `Spectrum`/`Spheres` in a **separate** buffer to avoid embedding 64 floats.

```swift
// Swift
struct FrameData {
    var resolution = SIMD2<Float>(0, 0)
    var time: Float = 0, bass: Float = 0, mids: Float = 0, highs: Float = 0
    var intensity: Float = 3, glow: Float = 1.2
    var startColor = SIMD3<Float>(0.35, 0.80, 1.0)
    var endColor   = SIMD3<Float>(0.41, 0.31, 1.0)
} // setFragmentBytes(&frame, length: MemoryLayout<FrameData>.stride, index: 0)
```
```metal
// MSL
struct FrameData {
    float2 resolution; float time, bass, mids, highs, intensity, glow;
    float3 startColor; float3 endColor;
};
```

### HLSL → MSL cheat sheet

| HLSL | MSL |
|---|---|
| `lerp` | `mix` |
| `frac` | `fract` |
| `saturate` | `saturate` |
| `fwidth` | `fwidth` (fragment only) |
| `atan2(y,x)` | `atan2(y,x)` |
| `[unroll] for` | `for` |
| `static const float k[6]={…}` | `constant float k[6]={…}` (file scope) |
| `min(float3, scalar)` (broadcasts) | `min(v, float3(scalar))` (no broadcast) |
| `pow(x,n)` with x possibly < 0 | avoid (we already use `dd*dd`, etc.) |
| `Texture.Sample(...)` | `texture.sample(sampler, uv)` |

## Reference: Event Horizon fully ported to MSL

Faithful translation of `Shaders/EventHorizon.hlsl` (post anti-grain fixes). It does not use
`Spectrum/Spheres`, so the small `FrameData` above is enough. Compile check: `xcrun -sdk macosx metal -c eh.metal`.

```metal
#include <metal_stdlib>
using namespace metal;

struct FrameData {
    float2 resolution; float time, bass, mids, highs, intensity, glow;
    float3 startColor; float3 endColor;
};
struct VOut { float4 pos [[position]]; float2 uv; };

vertex VOut eh_vertex(uint id [[vertex_id]]) {
    float2 uv = float2((id << 1) & 2, id & 2);
    VOut o; o.uv = uv; o.pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1); return o;
}

constant float PI = 3.14159265;
inline float hash21(float2 p){ p = fract(p*float2(123.34,345.45)); p += dot(p, p+34.345); return fract(p.x*p.y); }
inline float hash31(float3 p){ p = fract(p*0.1031); p += dot(p, p.yzx+33.33); return fract((p.x+p.y)*p.z); }

inline float noise3(float3 p){
    float3 i = floor(p), f = fract(p); f = f*f*(3.0-2.0*f);
    float n000=hash31(i), n100=hash31(i+float3(1,0,0));
    float n010=hash31(i+float3(0,1,0)), n110=hash31(i+float3(1,1,0));
    float n001=hash31(i+float3(0,0,1)), n101=hash31(i+float3(1,0,1));
    float n011=hash31(i+float3(0,1,1)), n111=hash31(i+float3(1,1,1));
    float x00=mix(n000,n100,f.x), x10=mix(n010,n110,f.x);
    float x01=mix(n001,n101,f.x), x11=mix(n011,n111,f.x);
    return mix(mix(x00,x10,f.y), mix(x01,x11,f.y), f.z);
}
inline float fbm(float3 p){ float s=0.0,a=0.5; for(int i=0;i<3;i++){ s+=a*noise3(p); p*=2.02; a*=0.5; } return s; }
inline float3 blackbody(float t){
    float3 cool=float3(1.0,0.42,0.12), mid=float3(1.0,0.85,0.55), hot=float3(0.75,0.85,1.0);
    return t<0.5 ? mix(cool,mid,t*2.0) : mix(mid,hot,(t-0.5)*2.0);
}
inline float3 starfield(float3 d, float aa){
    float2 uv = float2(atan2(d.z,d.x)/(2.0*PI)+0.5, acos(clamp(d.y,-1.0,1.0))/PI);
    float3 col = float3(0.0);
    for (int layer=0; layer<2; layer++){
        float scale = 150.0 + float(layer)*210.0;
        float2 g = uv*scale; float2 cell = floor(g);
        float h = hash21(cell + float(layer)*41.7);
        if (h > 0.955){
            float2 jit = 0.5 + 0.4*(float2(hash21(cell+3.1),hash21(cell+7.7))-0.5);
            float dstar = length(fract(g)-jit);
            float soft = smoothstep(0.34,0.0,dstar);
            float star = min(soft*soft*(h-0.955)/0.045, 1.1);
            col += star * mix(float3(0.65,0.75,1.0), float3(1.0,0.9,0.8), hash21(cell+5.5));
        }
    }
    col += fbm(d*2.5+11.0)*fbm(d*1.7+5.0)*float3(0.012,0.016,0.03);
    return col*aa*0.7;
}
inline float3 aces(float3 x){ return saturate((x*(2.51*x+0.03))/(x*(2.43*x+0.59)+0.14)); }

fragment float4 eh_fragment(VOut in [[stage_in]], constant FrameData& f [[buffer(0)]]) {
    float aspect = f.resolution.x/max(f.resolution.y,1.0);
    float2 uv = float2(in.uv.x, 1.0 - in.uv.y);
    float2 p = (uv*2.0-1.0)*float2(aspect,1.0);
    float intensity = clamp(f.intensity,0.0,8.0);
    float glow = max(f.glow,0.0);
    float bassE =1.0-exp(-3.2*max(f.bass -0.01,0.0));
    float midsE =1.0-exp(-3.2*max(f.mids -0.01,0.0));
    float highsE=1.0-exp(-3.2*max(f.highs-0.01,0.0));
    const float rs=1.0, rPhoton=1.5, rIn=3.0, rOut=13.0, rEscape=42.0;
    float ct = f.time*0.04 + midsE*0.25;
    float3 camPos = float3(sin(ct)*15.0, 2.6+sin(f.time*0.05)*0.5, cos(ct)*15.0);
    float3 fwd = normalize(-camPos);
    float3 right = normalize(cross(float3(0,1,0), fwd));
    float3 up = cross(fwd, right);
    float3 pos = camPos;
    float3 dir = normalize(p.x*right + p.y*up + fwd*1.6);
    float3 hc = cross(pos,dir); float h2 = dot(hc,hc);
    float rot = f.time*(0.55 + midsE*0.6);
    float3 col = float3(0.0); float trans = 1.0; float ring = 0.0;
    for (int i=0;i<300;i++){
        float r = length(pos);
        if (r < rs*1.02){ trans=0.0; break; }
        if (r > rEscape) break;
        float dt = clamp(r*0.10, 0.02, 0.5);
        float3 oldPos = pos;
        dir += (-1.5*h2*pos/pow(r,5.0))*dt;
        pos += dir*dt;
        float dd = (r-rPhoton)/0.10; ring += exp(-dd*dd)*dt;
        if (oldPos.y*pos.y < 0.0){
            float fcr = oldPos.y/(oldPos.y-pos.y);
            float3 hp = mix(oldPos,pos,fcr);
            float rad = length(hp.xz);
            if (rad>rIn && rad<rOut){
                float tR = saturate((rOut-rad)/(rOut-rIn));
                float ang = atan2(hp.z, hp.x);
                float turb = fbm(float3(log(rad)*3.0, ang*2.0 - rot, rad*0.2));
                turb *= smoothstep(rIn, rIn+2.5, rad);
                float bright = pow(tR,1.5)*(0.6+0.7*turb);
                float3 vdir = normalize(cross(float3(0,1,0), hp));
                float dop = clamp(1.0 + 0.7*dot(vdir, normalize(camPos-hp)), 0.4, 2.2);
                float3 emit = mix(blackbody(tR), mix(f.startColor,f.endColor,tR), 0.45);
                emit = min(emit*bright*(dop*dop)*(0.8+1.3*bassE), float3(8.0));
                col += trans*emit;
                trans *= 1.0 - saturate(bright*1.2)*0.85;
            }
        }
        if (trans < 0.01) break;
    }
    float starAA = saturate(1.0 - length(fwidth(dir))*14.0);
    if (trans > 0.001) col += trans*starfield(dir, starAA);
    ring = min(ring, 2.5);
    col += float3(1.0,0.9,0.75)*ring*(0.12+0.6*glow)*(0.6+0.9*highsE);
    col = min(col, float3(12.0));
    col *= 0.6 + 0.9*sqrt(intensity/3.0);
    return float4(aces(col), 1.0);
}
```

## Audio pipeline (ScreenCaptureKit → vDSP → 64 bands)

Match `AudioSpectrumService.Analyze`: FFT 2048, Hamming window, 64 **log** bands from 35 Hz to
`min(18000, sr/2)`, value `clamp(log10(1 + peak*18)/1.15, 0, 1)`, attack **0.62** / decay **0.16**, 50% hop.

```swift
// 1) Capture (macOS 13+)
let cfg = SCStreamConfiguration(); cfg.capturesAudio = true
cfg.sampleRate = 48000; cfg.channelCount = 2
// SCStream(...).addStreamOutput(self, type: .audio, sampleHandlerQueue: q)

// 2) Callback: CMSampleBuffer -> Float32 mono -> ring buffer (2048, 1024 overlap)
func stream(_ s: SCStream, didOutputSampleBuffer sb: CMSampleBuffer, of type: SCStreamOutputType) {
    guard type == .audio else { return }
    // extract Float32 via withAudioBufferList, downmix to mono, push into the ring; at 2048 -> analyze()
}

// 3) Analyze with Accelerate (equivalent to ours)
import Accelerate
// vDSP_hamm_window + vDSP_vmul (window) -> vDSP_ctoz -> FFT (radix-2, log2n=11) -> vDSP_zvabs (magnitude)
// log bands 35..min(18k, sr/2): peak per band -> log10(1 + peak*18)/1.15 -> clamp
// smoothing: band += (target - band) * (target > band ? 0.62 : 0.16)
```

- vDSP is native (fast) and replaces `NAudio.Dsp`; the **band math is identical** (copy from `Analyze`).
- Requires the Screen Recording TCC permission. The audio toggle = `startCapture`/`stopCapture`.
- macOS 14.2+ alternative: a Core Audio process tap (`AudioHardwareCreateProcessTap`).

## Effort plan (rough)

| Phase | Scope | Effort | Risk |
|---|---|---|---|
| 0 | PoC: desktop `NSWindow` (1 screen) + `CAMetalLayer` + triangle | 1–2 d | Low (validates "behind icons") |
| 1 | Metal pipeline + `FrameData` + **Event Horizon** MSL, time/FPS loop | 2–4 d | Medium (translation/tonemap) |
| 2 | Multi-monitor + rebuild on layout change + pause per Space/fullscreen/lock/battery | 2–3 d | Medium |
| 3 | Audio (SCK) → vDSP → 64 bands → reaction + toggle | 3–4 d | Medium (TCC/latency) |
| 4 | Port remaining GPU shaders (Ambient, Spectral Bloom, Neon Ribbons, Liquid Orbs) | 2–3 d | Low (mechanical) |
| 5 | Settings UI (SwiftUI) + menu bar + persistence (same `settings.json`) | 3–5 d | Medium |
| 6 | Package `.app`, Developer ID + notarization, universal arm64/x64 | 1–2 d | Low–Medium |
| 7 (opt.) | Effekseer-Metal effects | 5–10 d | High (native integration) |

MVP (phases 0–6): roughly **2–4 weeks** for a developer experienced with AppKit/Metal, delivering the GPU
wallpapers (including Event Horizon) with audio, multi-monitor, toggle and settings. Effekseer later.

## Packaging: entitlements, Info.plist, signing

Model: **Developer ID** app (outside the App Store), **Hardened Runtime ON**, **App Sandbox OFF** (desktop-level
window + system-audio capture do not fit the sandbox).

### Info.plist (essentials)

```xml
<key>CFBundleIdentifier</key><string>com.marcelobossle.hypnix</string>
<key>CFBundleName</key><string>HYPNIX</string>
<key>CFBundleShortVersionString</key><string>1.0.0</string>
<key>CFBundleVersion</key><string>1</string>
<key>LSMinimumSystemVersion</key><string>13.0</string>
<key>LSUIElement</key><true/>            <!-- agent: no Dock -->
<key>NSHighResolutionCapable</key><true/>
<!-- Only if microphone/Core Audio input is ever used: -->
<!-- <key>NSMicrophoneUsageDescription</key><string>HYPNIX reacts to system audio.</string> -->
```

- **ScreenCaptureKit (system audio):** the permission is **Screen Recording** (TCC, granted in System Settings
  › Privacy › Screen Recording) — there is no Info.plist string for it; the app must be signed and the user
  grants it.
- **Core Audio process tap (14.2+):** requires the system-audio recording TCC permission plus the
  `com.apple.security.device.audio-input` entitlement. **Verify the exact keys against current Apple docs**
  (they changed across 14.2/14.4).

### HYPNIX.entitlements (Hardened Runtime)

```xml
<key>com.apple.security.app-sandbox</key><false/>
<!-- only if using Core Audio input/tap or microphone: -->
<key>com.apple.security.device.audio-input</key><true/>
```
(ScreenCaptureKit needs no extra entitlement beyond the Hardened Runtime.)

### Sign + notarize

```bash
# 1) Sign inside-out (dylibs/frameworks/helpers before the .app), Hardened Runtime:
codesign --force --options runtime --timestamp \
  --entitlements HYPNIX.entitlements \
  --sign "Developer ID Application: Marcelo Bossle (TEAMID)" \
  HYPNIX.app/Contents/Frameworks/*   # each nested item
codesign --force --options runtime --timestamp \
  --entitlements HYPNIX.entitlements \
  --sign "Developer ID Application: Marcelo Bossle (TEAMID)" HYPNIX.app

# 2) Package and notarize (app-specific password or a stored keychain profile):
ditto -c -k --keepParent HYPNIX.app HYPNIX.zip
xcrun notarytool submit HYPNIX.zip --apple-id "id@apple" --team-id TEAMID \
  --password "app-specific-pass" --wait

# 3) Staple the ticket and verify:
xcrun stapler staple HYPNIX.app
spctl -a -vvv HYPNIX.app
codesign -dv --verbose=4 HYPNIX.app
```

Notes: avoid `--deep` (sign nested items individually); no provisioning profile needed for Developer ID (just
the **Developer ID Application** certificate); distribute via DMG/ZIP (the stapled ticket lets Gatekeeper pass
offline). Mac App Store is out of scope (sandbox forbids desktop-level windows + system-audio capture).

## Key risks to validate early

1. The `NSWindow` truly renders behind icons and stays across Spaces (level + `collectionBehavior`).
2. Click-through works (Finder icons/menus still usable).
3. Survives display sleep/wake, resolution change, external monitor connect/disconnect.
4. GPU/energy cost is acceptable (pause when occluded; respect App Nap).
5. Audio permission + latency are acceptable.

## Targets and constraints

- Minimum macOS: **13** (ScreenCaptureKit audio); Metal works on much older.
- Distribution: Developer ID + notarization; **not** the App Store.
- Universal: `arm64` + `x86_64`.
