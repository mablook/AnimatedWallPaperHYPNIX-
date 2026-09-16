// HYPNIX Lotus - original clean-room implementation. (c) HYPNIX.
// Generic technique (no third-party shader code): a stack of rotated, scaled petal layers
// (a blooming lotus) swaying under a slow time-based breeze, over a glowing center, tinted
// with the shared HYPNIX palette, edge-desaturated and vignetted, and softened with a tanh
// curve. The bloom concept and every parameter are our own. Motion is time-based and calm;
// the 64 audio bands modulate petal BRIGHTNESS by frequency (inner petals follow the lows,
// outer petals the highs). It stays calm and coherent at silence. The theme palette drives
// the colors, so the pink/amber theme yields a warm rose and the default theme a cool lotus.
cbuffer FrameData : register(b0)
{
    float2 Resolution; float Time; float Bass;
    float Mids; float Highs; float Intensity; float Glow;
    float2 Origin; float2 Padding;
    float3 StartColor; float ColorPadA;
    float3 EndColor; float ColorPadB;
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

// One petal layer's thin rim value at a position, swaying in a slow time-based breeze.
// Outer petals flex more than the core, so the bloom breathes like a real flower in wind.
float PetalLayer(float2 p, float layer, float t)
{
    float r = length(p);
    float flex = pow(r, 1.7);
    float gust = 0.8 + 0.45 * sin(t * 0.9) + 0.25 * cos(t * 1.7);
    p -= float2(0.7, 0.45) * gust * flex * 0.10;

    r = length(p);
    float a = atan2(p.y, p.x);
    r += sin(a * 7.0 + t * 1.6 + layer) * 0.030 * flex;   // rim flutter
    r += sin(t * 1.2 + layer * 0.4 + a * 3.0) * 0.045 * flex; // slow heave

    float petals = floor(4.0 + fmod(layer, 4.0));
    float shape = sin(petals * a) * (1.0 - r);
    return smoothstep(0.22, 0.16, abs(shape - 0.5));
}

float4 PSMain(VertexOutput input) : SV_Target
{
    // Aspect-correct, centered coordinates. Independent of the monitor origin.
    float2 uv = float2(input.UV.x, 1.0 - input.UV.y) * 2.0 - 1.0;
    uv.x *= Resolution.x / max(Resolution.y, 1.0);

    float intensity = clamp(Intensity, 0.0, 8.0);
    float glow = max(Glow, 0.0);
    float t = Time;

    // Calm, time-based camera: a gentle breathing zoom, slow drift and a slight roll.
    float zoom = 1.0 + sin(t * 0.3) * 0.15;
    float2 drift = float2(sin(t * 0.4) * 0.04 + cos(t * 0.25) * 0.02,
                          cos(t * 0.35) * 0.04 + sin(t * 0.18) * 0.02);
    float roll = sin(t * 0.2) * 0.05;
    float2 uvn = Rotate((uv - drift) * zoom, roll);

    // Accumulate the petal layers, brightening each by the frequency mapped to its radius.
    float3 col = float3(0.0, 0.0, 0.0);
    [loop] for (int layer = 0; layer < 24; layer++)
    {
        float scale = pow(0.97, (float)layer);
        float2 pos = Rotate(uvn / scale, (float)layer * 0.18);
        float r = length(pos);
        float petal = PetalLayer(pos, (float)layer, t);
        float react = 0.28 + 1.6 * Band(saturate(r * 0.5));
        float3 layerColor = Palette(0.5 + 0.5 * sin((float)layer * 0.5 + r * 6.0));
        col += petal * layerColor * react * pow(0.97, (float)layer) * 1.4;
    }
    col = tanh(col * 0.3);

    // Glowing center that pulses softly with the lows, in a bright lift of the palette.
    float rc = length(uvn);
    float centerMask = smoothstep(0.16, 0.01, rc);
    float3 centerColor = lerp(EndColor, float3(1.0, 1.0, 1.0), 0.6) * (1.1 + 0.7 * Band(0.02));
    col = lerp(col, centerColor, centerMask);

    // Soft lens: desaturate toward the edges and frame with a vignette.
    float dc = length(uv);
    float lens = smoothstep(0.35, 0.7, dc);
    float lum = dot(col, float3(0.299, 0.587, 0.114));
    col = lerp(col, lum.xxx, lens * 0.25);
    col *= smoothstep(0.85, 0.2, dc * 0.9);

    // Glow lifts the bloom; Intensity is the master gain, so Intensity = 0 -> black.
    col *= 0.8 + 0.5 * glow;
    col *= sqrt(intensity / 3.0);
    return float4(saturate(col), 1.0);
}
