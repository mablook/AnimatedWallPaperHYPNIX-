// HYPNIX Fractal Pyramid - original clean-room implementation. (c) HYPNIX.
// Generic technique (no third-party shader code): a kaleidoscopic space-folding
// distance field (iterated rotate + absolute-fold + translate) raymarched under a
// slowly orbiting camera and accumulated as inverse-distance volumetric glow, tinted
// with the shared HYPNIX palette and resolved with an ACES-style filmic tonemap.
// The rotation rates, fold offset, emission and audio mapping are our own parameters.
// The scene stays calm and coherent in silence; audio only gently nudges it.
cbuffer FrameData : register(b0)
{
    float2 Resolution; float Time; float Bass;
    float Mids; float Highs; float Intensity; float Glow;
    float2 Origin; float2 Padding;
    float3 StartColor; float ColorPadA;
    float3 EndColor; float ColorPadB;
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

// Kaleidoscopic space-folding distance estimate. Each pass rotates the point on two
// planes, mirror-folds it with abs() and pulls it toward the origin; the folded point's
// L1 (octahedral) norm is a safe raymarch bound. All rates/offsets are our own.
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

    float intensity = clamp(Intensity, 0.0, 8.0);
    float glow = max(Glow, 0.0);
    // Soft-knee bands with a small noise floor so the scene is quiet and calm at silence.
    float bass = 1.0 - exp(-3.0 * max(Bass - 0.01, 0.0));
    float mids = 1.0 - exp(-3.0 * max(Mids - 0.01, 0.0));
    float highs = 1.0 - exp(-3.0 * max(Highs - 0.01, 0.0));

    // Calm baseline motion; audio only nudges the fold rate and fold offset.
    float spin = Time * 0.20 + mids * 0.18;
    float fold = 0.5 + bass * 0.06;

    // Slowly orbiting camera. The view basis is rebuilt each frame from the eye position.
    float3 ro = float3(0.0, 0.0, -50.0);
    ro.xz = Spin(ro.xz, Time * 0.12);
    float3 cf = normalize(-ro);
    float3 cs = normalize(cross(cf, float3(0.0, 1.0, 0.0)));
    float3 cu = normalize(cross(cf, cs));
    float3 rd = normalize(cf * 3.0 + uv.x * cs + uv.y * cu);

    // Emission breathes with bass and sparkles slightly with highs; = 1 at silence.
    float emission = 1.0 + 0.9 * bass + 0.5 * highs;

    float3 col = float3(0.0, 0.0, 0.0);
    float t = 0.0;
    float d = 1.0;
    [loop] for (int step = 0; step < 64; step++)
    {
        float3 p = ro + rd * t;
        d = MapScene(p, spin, fold) * 0.5;
        if (d < 0.02 || t > 100.0) break;
        col += Palette(length(p) * 0.1) * emission / (400.0 * d);
        t += d;
    }

    // Glow widens the accumulated halo; highs add a restrained shimmer in the theme color.
    col *= 0.7 + 0.6 * glow;
    col += EndColor * highs * glow * 0.05;

    // Intensity is the master gain, so Intensity = 0 resolves to pure black (renderer contract).
    col *= sqrt(intensity / 3.0);
    col = saturate((col * (2.51 * col + 0.03)) / (col * (2.43 * col + 0.59) + 0.14)); // ACES-style filmic
    return float4(col, 1.0);
}
