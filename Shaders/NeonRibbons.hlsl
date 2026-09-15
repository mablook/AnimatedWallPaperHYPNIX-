// HYPNIX Neon Ribbons - original clean-room implementation. (c) HYPNIX.
// Generic technique (no third-party shader code): an aspect-correct radial
// domain warp, N audio-reactive sinusoidal ribbons drawn as anti-aliased
// inverse-distance neon cores with analytic halos, accumulated in linear light
// and resolved with an ACES-style filmic tonemap. All parameters are our own.
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

static const int RIBBONS = 6;
// Our own composition. Per ribbon: base X, temporal freq, vertical freq, amplitude, phase.
static const float kBaseX[6] = { -1.05, -0.62, -0.20, 0.22, 0.64, 1.08 };
static const float kFreqT[6] = {  0.90,  1.35,  0.55, 1.70, 1.10, 0.75 };
static const float kFreqY[6] = {  1.30,  2.10,  3.40, 1.70, 2.60, 0.90 };
static const float kAmp[6]   = {  0.34,  0.22,  0.16, 0.20, 0.26, 0.38 };
static const float kPhase[6] = {  0.00,  1.90,  3.60, 5.10, 2.40, 4.30 };
// Neon palette (our selection spanning the spectrum).
static const float3 kPal[6] = {
    float3(0.35, 0.10, 1.00), // violet
    float3(1.00, 0.12, 0.42), // magenta
    float3(1.00, 0.55, 0.05), // amber
    float3(0.15, 1.00, 0.55), // emerald
    float3(0.10, 0.70, 1.00), // cyan
    float3(1.00, 0.90, 0.20)  // gold
};

float3 Tonemap(float3 x) // ACES-style filmic curve
{
    return saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14));
}

float4 PSMain(VertexOutput input) : SV_Target
{
    float aspect = Resolution.x / max(Resolution.y, 1.0);
    float2 u = (float2(input.UV.x, 1.0 - input.UV.y) * 2.0 - 1.0) * float2(aspect, 1.0);

    float intensity = clamp(Intensity, 0.0, 8.0);
    float drive = intensity / (1.0 + 0.12 * intensity);
    float glow = max(Glow, 0.0);
    // Soft-knee bands with a small noise floor (our knee = 3.2).
    float bass = 1.0 - exp(-3.2 * max(Bass - 0.01, 0.0));
    float mids = 1.0 - exp(-3.2 * max(Mids - 0.01, 0.0));
    float highs = 1.0 - exp(-3.2 * max(Highs - 0.01, 0.0));
    float band3[3] = { bass, mids, highs };

    float t = Time * 0.18;
    // Radial swirl: rotation angle grows with radius and breathes with mids.
    float ang = 0.6 * length(u) + 0.25 * mids * drive + 0.15 * t;
    float ca = cos(ang), sa = sin(ang);
    u = float2(u.x * ca - u.y * sa, u.x * sa + u.y * ca);

    float width = 1.0 + bass * drive * 0.35;
    float3 accum = float3(0.0, 0.0, 0.0);
    [unroll] for (int i = 0; i < RIBBONS; i++)
    {
        float level = band3[i % 3];
        float wave = sin(kFreqT[i] * Time + u.y * kFreqY[i] + kPhase[i]) * kAmp[i] * width;
        float d = abs((u.x - kBaseX[i] * 0.9) + wave);
        float aa = max(fwidth(d), 1e-4);
        float core = (1.0 - smoothstep(0.0, aa * 2.5 + 0.004, d)) * 1.4; // AA-aware core
        core += 0.02 / (d * (0.6 + i * 0.05) + 0.004);                   // inverse-distance sheen
        float halo = glow * 0.03 / (0.05 + d);                           // analytic halo
        float react = 0.55 + 0.75 * level;                              // per-band brightness
        accum += kPal[i] * (core + halo) * react;
    }

    // Clean palette tint: blend StartColor..EndColor along y (linear), no divide hacks.
    float3 tint = lerp(StartColor, EndColor, 0.5 + 0.5 * sin(u.y * 1.3 + t));
    accum *= (0.35 + 1.30 * tint);
    accum *= 0.18 * sqrt(intensity / 3.0) * (1.0 + highs * drive * 0.30);

    return float4(Tonemap(accum), 1.0);
}
