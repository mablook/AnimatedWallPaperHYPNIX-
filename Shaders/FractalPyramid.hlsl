// HYPNIX Fractal Pyramid - original clean-room implementation. (c) HYPNIX.
// Generic technique (no third-party shader code): a kaleidoscopic space-folding
// distance field (iterated rotate + absolute-fold + translate) raymarched under a
// slowly orbiting camera and accumulated as inverse-distance volumetric glow, tinted
// with the shared HYPNIX palette and resolved with an ACES-style filmic tonemap.
// The motion is purely time-based and stays calm; audio does NOT drive the animation.
// Instead the 64 logarithmic bands modulate the BRIGHTNESS of the fractal lines by
// frequency (inner detail follows the lows, outer detail the highs), so the structure
// lights up to sound while its movement remains constant. Quiet and coherent at silence.
cbuffer FrameData : register(b0)
{
    float2 Resolution; float Time; float Bass;
    float Mids; float Highs; float Intensity; float Glow;
    float2 Origin; float PaddingX; float OffsetY;
    float3 StartColor; float Scale;
    float3 EndColor; float OffsetX;
    float4 Spectrum[16]; // 64 normalized logarithmic bands (low -> high).
};

struct VertexOutput { float4 Position : SV_Position; float2 UV : TEXCOORD0; };
VertexOutput VSMain(uint id : SV_VertexID)
{
    VertexOutput output;
    float2 uv = float2((id << 1) & 2, id & 2);
    output.UV = uv;
    output.Position = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
    return output;
}

float2 Spin(float2 p, float a)
{
    float c = cos(a), s = sin(a);
    return float2(c * p.x + s * p.y, -s * p.x + c * p.y);
}

// HYPNIX theme palette: blend the two configured colors across the accumulated depth.
float3 Palette(float d) { return lerp(StartColor, EndColor, saturate(d)); }

// Read one of the 64 bands. f in [0,1] maps low -> high frequency.
float Band(float f)
{
    int i = clamp((int)(f * 64.0), 0, 63);
    return Spectrum[i >> 2][i & 3];
}

// Kaleidoscopic space-folding distance estimate. Each pass rotates the point on two
// planes, mirror-folds it with abs() and pulls it toward the origin; the folded point's
// L1 (octahedral) norm is a safe raymarch bound. Rotation and fold offset are constant
// (time only), so audio never changes the geometry or motion.
float MapScene(float3 p, float spin, float fold)
{
    [unroll] for (int i = 0; i < 8; i++)
    {
        p.xz = Spin(p.xz, spin);
        p.xy = Spin(p.xy, spin * 1.89);
        p.xz = abs(p.xz) - fold;
    }
    return (abs(p.x) + abs(p.y) + abs(p.z)) * 0.2;
}

float4 PSMain(VertexOutput input) : SV_Target
{
    // Aspect-correct, centered screen coordinates. Independent of the monitor origin,
    // so every display shows the same self-contained scene.
    float2 uv = float2(input.UV.x, 1.0 - input.UV.y) - 0.5;
    uv.x *= Resolution.x / max(Resolution.y, 1.0);
    uv = (uv - float2(OffsetX, OffsetY) * 0.5) / max(Scale, 0.05); // user size/position

    float intensity = clamp(Intensity, 0.0, 8.0);
    float glow = max(Glow, 0.0);

    // Purely time-based, calm motion. Audio is deliberately absent here.
    float spin = Time * 0.20;
    float fold = 0.5;

    // Slowly orbiting camera. The view basis is rebuilt each frame from the eye position.
    float3 ro = float3(0.0, 0.0, -50.0);
    ro.xz = Spin(ro.xz, Time * 0.12);
    float3 cf = normalize(-ro);
    float3 cs = normalize(cross(cf, float3(0.0, 1.0, 0.0)));
    float3 cu = normalize(cross(cf, cs));
    float3 rd = normalize(cf * 3.0 + uv.x * cs + uv.y * cu);

    float3 col = float3(0.0, 0.0, 0.0);
    float t = 0.0;
    float d = 1.0;
    [loop] for (int step = 0; step < 64; step++)
    {
        float3 p = ro + rd * t;
        d = MapScene(p, spin, fold) * 0.5;
        if (d < 0.02 || t > 100.0) break;
        // Frequency-mapped line brightness: a sample's distance from the origin selects a
        // band (inner detail = lows, outer = highs). A small floor keeps the lines faintly
        // visible in silence; louder bands make their lines glow brighter.
        float react = 0.22 + 1.8 * Band(saturate(length(p) * 0.11));
        col += Palette(length(p) * 0.1) * react / (400.0 * d);
        t += d;
    }

    // Glow widens the accumulated halo.
    col *= 0.7 + 0.6 * glow;

    // Intensity is the master gain, so Intensity = 0 resolves to pure black (renderer contract).
    col *= sqrt(intensity / 3.0);
    col = saturate((col * (2.51 * col + 0.03)) / (col * (2.43 * col + 0.59) + 0.14)); // ACES-style filmic
    return float4(col, 1.0);
}
