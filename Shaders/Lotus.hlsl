// HYPNIX Lotus - original clean-room implementation. (c) HYPNIX.
// Generic technique (no third-party shader code): a radial "spectrum flower". The angle maps
// to a frequency band (mirrored for symmetry), so the petals ARE the frequency bands arranged
// in a circle; each petal grows/shrinks and brightens with its band. A slow time-based breeze,
// drifting/zooming/rolling camera and rim flutter keep it alive and calm in silence, while the
// silhouette becomes a circular equaliser to sound. Colors come from the theme palette (the
// pink/amber theme yields a warm rose). The concept and every parameter are our own.
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

static const float TAU = 6.2831853;

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

// One flower layer: a carved petal ring whose reach at each angle is set by that angle's
// frequency band, so the petals grow and shrink with the spectrum. Returns rim+body coverage.
float FlowerLayer(float2 p, float petals, float t, float phase, out float freqOut, out float levelOut)
{
    float r = length(p);
    float a = atan2(p.y, p.x);
    // Rim flutter + slow heave keep the outline organic even at silence.
    r += sin(a * 7.0 + t * 1.6 + phase) * 0.015;
    r += sin(t * 1.1 + phase + a * 3.0) * 0.020;

    // Angle -> mirrored frequency coordinate: petals are the frequency bands.
    float ang01 = a / TAU + 0.5;
    float fm = abs(frac(ang01) * 2.0 - 1.0);
    float level = Band(fm);
    freqOut = fm;
    levelOut = level;

    // Petals grow/shrink strongly with their band, plus a gentle time breathing. The base is
    // large so the bloom fills the frame and its silhouette reads clearly as a circular spectrum.
    float breathe = 0.9 + 0.1 * sin(t * 0.8 + phase);
    float reach = (0.5 + 1.0 * level) * breathe;

    float carve = pow(0.5 + 0.5 * sin(petals * a + phase), 1.5); // petal separation
    float body = smoothstep(reach, reach - 0.18, r) * carve;     // filled petal
    float rim = smoothstep(0.06, 0.0, abs(r - reach)) * carve;   // bright rim
    return body * 0.5 + rim * 1.2;
}

float4 PSMain(VertexOutput input) : SV_Target
{
    // Aspect-correct, centered coordinates. Independent of the monitor origin.
    float2 uv = float2(input.UV.x, 1.0 - input.UV.y) * 2.0 - 1.0;
    uv.x *= Resolution.x / max(Resolution.y, 1.0);
    float2 screenUv = uv;                                    // screen-centered, for the vignette
    uv = (uv - float2(OffsetX, OffsetY)) / max(Scale, 0.05); // user size/position

    float intensity = clamp(Intensity, 0.0, 8.0);
    float glow = max(Glow, 0.0);
    float t = Time;

    // Moves more across the screen: larger drift, breathing zoom and a slow continuous roll.
    float2 drift = float2(sin(t * 0.5) * 0.18 + cos(t * 0.31) * 0.10,
                          cos(t * 0.43) * 0.16 + sin(t * 0.27) * 0.09);
    float zoom = 1.0 + sin(t * 0.3) * 0.20;
    float roll = t * 0.06;
    float2 pos = Rotate((uv - drift) * zoom, roll);

    // Two layers for depth: a main bloom and a counter-rotated inner echo.
    float f0, l0, f1, l1;
    float main = FlowerLayer(pos, 24.0, t, 0.0, f0, l0);
    float echo = FlowerLayer(Rotate(pos, -0.4 - t * 0.03) * 1.6, 16.0, t, 1.7, f1, l1);

    // Brightness is dim in silence and swings strongly with each petal's band, so the flower
    // reads as a circular equaliser (and clears the audio-response gate) while staying calm quiet.
    float3 col = Palette(f0) * main * (0.18 + 3.0 * l0);
    col += Palette(f1) * echo * (0.12 + 2.2 * l1) * 0.5;
    col = tanh(col * 0.9);

    // Glowing center that pulses softly with the lows, in a bright lift of the palette.
    float rc = length(pos);
    float centerMask = smoothstep(0.14, 0.0, rc);
    float3 centerColor = lerp(EndColor, float3(1.0, 1.0, 1.0), 0.6) * (1.1 + 0.7 * Band(0.02));
    col = lerp(col, centerColor, centerMask);

    // Soft lens: desaturate toward the edges and frame with a vignette (screen-centered).
    float dc = length(screenUv);
    float lens = smoothstep(0.5, 1.0, dc);
    float lum = dot(col, float3(0.299, 0.587, 0.114));
    col = lerp(col, lum.xxx, lens * 0.25);
    col *= smoothstep(1.15, 0.15, dc * 0.78);

    // Glow lifts the bloom; Intensity is the master gain, so Intensity = 0 -> black.
    col *= 0.8 + 0.5 * glow;
    col *= sqrt(intensity / 3.0);
    return float4(saturate(col), 1.0);
}
