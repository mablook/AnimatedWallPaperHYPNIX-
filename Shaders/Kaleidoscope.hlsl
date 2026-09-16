// HYPNIX Kaleidoscope - original clean-room implementation. (c) HYPNIX.
// Generic technique (no third-party shader code): a radial kaleidoscope fold (polar
// mirror into N wedges) applied to our own layered ring/petal mandala field, given a
// subtle chromatic split, tinted with the shared HYPNIX palette, vignetted and resolved
// with an ACES-style filmic tonemap. The concept - an evolving kaleidoscopic mandala - is
// a general visual idea implemented independently; the field, fold, motion and audio
// mapping are all our own. Motion is slow and continuous (time only); the 64 audio bands
// modulate the BRIGHTNESS of the mandala rings by frequency (inner rings follow the lows,
// outer rings the highs). It stays calm and coherent at silence.
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

float2 Rotate(float2 p, float a)
{
    float c = cos(a), s = sin(a);
    return float2(c * p.x - s * p.y, s * p.x + c * p.y);
}

float3 Palette(float d) { return lerp(StartColor, EndColor, saturate(d)); }

// Read one of the 64 bands. f in [0,1] maps low -> high frequency.
float Band(float f)
{
    int i = clamp((int)(f * 64.0), 0, 63);
    return Spectrum[i >> 2][i & 3];
}

// Our own layered mandala: a few rotated octaves of concentric rings times angular petals.
// Each octave rotates and zooms, so the pattern reads as an evolving mandala rather than a
// flat texture. Rates and petal counts are our own; motion depends only on time.
float Mandala(float2 p, float t)
{
    float v = 0.0;
    float amp = 1.0;
    [unroll] for (int i = 0; i < 4; i++)
    {
        p = Rotate(p, 0.6 + t * 0.05);
        float r = length(p);
        float a = atan2(p.y, p.x);
        float ring = 0.5 + 0.5 * sin(r * 9.0 - t * 0.7 + float(i) * 1.3);
        float petal = 0.5 + 0.5 * cos(a * (5.0 + 2.0 * float(i)) + t * 0.25);
        v += amp * ring * petal;
        p *= 1.7;
        amp *= 0.6;
    }
    return v;
}

float4 PSMain(VertexOutput input) : SV_Target
{
    // Aspect-correct, centered coordinates. Independent of the monitor origin.
    float2 uv = float2(input.UV.x, 1.0 - input.UV.y) * 2.0 - 1.0;
    uv.x *= Resolution.x / max(Resolution.y, 1.0);
    uv = (uv - float2(OffsetX, OffsetY)) / max(Scale, 0.05); // user size/position

    float intensity = clamp(Intensity, 0.0, 8.0);
    float glow = max(Glow, 0.0);
    float t = Time;

    // Kaleidoscope fold: mirror the plane into N wedges around a slowly rotating axis.
    const float segments = 6.0;
    const float period = 6.2831853 / segments;
    float radius = length(uv);
    float angle = atan2(uv.y, uv.x) + t * 0.05;
    angle -= period * floor(angle / period);   // robust fmod into [0, period)
    angle = abs(angle - period * 0.5);          // mirror -> single wedge
    float2 kal = float2(cos(angle), sin(angle)) * radius;

    // Mandala field with a subtle radial chromatic split (generic aberration) for richness.
    float mR = Mandala(kal * 1.02, t);
    float mG = Mandala(kal, t);
    float mB = Mandala(kal * 0.98, t);
    float3 field = float3(mR, mG, mB);

    // Frequency-mapped brightness: inner rings follow the lows, outer rings the highs.
    // A small floor keeps the mandala calmly visible in silence.
    float react = 0.25 + 1.7 * Band(saturate(radius * 0.6));
    float3 col = Palette(mG * 0.5) * field * react;

    // Glow lifts the accumulated tones; a gentle vignette frames the mandala.
    col *= 0.7 + 0.6 * glow;
    col *= 1.0 - smoothstep(0.7, 1.7, radius);

    // Intensity is the master gain, so Intensity = 0 resolves to pure black (renderer contract).
    col *= sqrt(intensity / 3.0);
    col = saturate((col * (2.51 * col + 0.03)) / (col * (2.43 * col + 0.59) + 0.14)); // ACES-style filmic
    return float4(col, 1.0);
}
