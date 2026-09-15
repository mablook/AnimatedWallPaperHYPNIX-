// Spectral Bloom: flowing spectral silk, linear HDR feedback and display-only bloom.
cbuffer FrameData : register(b0)
{
    float2 Resolution;
    float Time;
    float Bass;
    float Mids;
    float Highs;
    float Intensity;
    float Glow;
    float2 Origin;
    float DeltaTime;
    float Padding;
    float3 StartColor;
    float ColorPadA;
    float3 EndColor;
    float ColorPadB;
    float4 Spectrum[16];
};

Texture2D History : register(t0);
Texture2D Bloom : register(t1);
SamplerState LinearClamp : register(s0);

struct VertexOutput
{
    float4 Position : SV_Position;
    float2 UV : TEXCOORD0;
};

VertexOutput VSMain(uint id : SV_VertexID)
{
    VertexOutput output;
    float2 uv = float2((id << 1) & 2, id & 2);
    output.UV = uv;
    output.Position = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
    return output;
}

float band(int index)
{
    index = clamp(index, 0, 63);
    return Spectrum[index / 4][index % 4];
}

// These are the capture service's logarithmic FFT bands, not PCM waveform samples.
float spectrumAt(float position)
{
    float index = saturate(position) * 63;
    int first = (int)floor(index);
    return lerp(band(first), band(first + 1), index - first);
}

float2 rotate(float2 p, float angle)
{
    float s, c;
    sincos(angle, s, c);
    return float2(c * p.x - s * p.y, s * p.x + c * p.y);
}

float3 silkColor(float t)
{
    float blend = 0.5 + 0.5 * sin(t * 3.1 + Time * 0.13);
    float3 color = lerp(StartColor, EndColor, blend);
    return color / max(0.2, max(color.r, max(color.g, color.b)));
}

float4 PSMain(VertexOutput input) : SV_Target
{
    float2 uv = input.UV;
    float aspect = Resolution.x / max(Resolution.y, 1);
    float2 p = (uv * 2 - 1) * float2(aspect, 1);
    float dt = clamp(DeltaTime, 0, 0.1);
    float bass = sqrt(saturate(Bass));
    float energy = saturate(bass * 0.55 + Mids * 0.3 + Highs * 0.15);

    // Smooth advection with two drifting vortices. All rates are per second.
    float2 center = float2(0.55 * sin(Time * 0.17), 0.25 * cos(Time * 0.21));
    float2 q = p - center;
    float2 q2 = p + center + float2(0.5, 0);
    float2 velocity = float2(-q.y, q.x) * (0.10 + 0.12 * bass) / (0.55 + dot(q, q));
    velocity -= float2(-q2.y, q2.x) * 0.065 / (0.4 + dot(q2, q2));
    velocity += p * (0.035 + 0.035 * bass);
    velocity += float2(sin(p.y * 3 + Time * 0.22), cos(p.x * 2 - Time * 0.19)) * 0.023;
    float2 oldUv = ((p - velocity * dt) / float2(aspect, 1)) * 0.5 + 0.5;
    float edge = smoothstep(0, 0.025, oldUv.x) * smoothstep(0, 0.025, oldUv.y)
               * (1 - smoothstep(0.975, 1, oldUv.x)) * (1 - smoothstep(0.975, 1, oldUv.y));
    float decayRate = 2.8 / (1 + max(Glow, 0) * 1.5);
    float3 history = History.SampleLevel(LinearClamp, oldUv, 0).rgb * exp(-decayRate * dt) * edge;

    // Five braided ribbons: large curves breathe slowly, fine folds follow FFT detail.
    float2 silk = rotate(p, 0.22 * sin(Time * 0.12));
    float x = silk.x;
    float3 emission = 0;
    float pixel = 2 / max(Resolution.y, 1);
    [unroll]
    for (int layer = 0; layer < 5; layer++)
    {
        float l = (float)layer;
        float phase = Time * (0.22 + l * 0.012) + l * 0.72;
        float spectral = spectrumAt(saturate(x / (2 * aspect) + 0.5));
        float detail = spectrumAt(0.5 + 0.47 * sin(x * 0.65 + l * 0.4));
        float curve = sin(x * 1.65 + phase) * (0.24 + bass * 0.13)
                    + sin(x * 2.8 - phase * 0.73 + l * 0.6) * 0.12
                    + (l - 2) * 0.075;
        curve += (spectral - 0.25) * 0.17 * sin(x * 4.2 + l + Time * 0.3);
        float distance = silk.y - curve;
        float width = 0.023 + 0.018 * (0.5 + 0.5 * sin(x * 2.1 + phase)) + detail * 0.028;
        float envelope = exp(-distance * distance / (width * width));
        float threads = pow(0.5 + 0.5 * cos(distance * 340 + x * 3 - phase * 3), 10);
        float rim = exp(-abs(distance - width * 0.65) / max(pixel, 0.0025));
        float taper = exp(-pow(abs(x) / max(0.5, aspect * 0.87), 6));
        float pulse = 0.6 + 0.4 * sin(x * 2.5 - Time * 0.7 + l);
        float strength = (envelope * (0.08 + threads * 0.28) + rim * 0.42) * taper * pulse;
        emission += silkColor(l * 0.48 + x * 0.45) * strength * (0.28 + energy * 1.15);
    }

    // Stable integration; no display tone mapping in the history.
    float injection = (1 - exp(-decayRate * dt)) * 6.5;
    float3 color = history + emission * injection * max(0, Intensity);
    return float4(min(color, 12), 1);
}

float3 highlight(float3 color)
{
    float brightness = max(color.r, max(color.g, color.b));
    return color * max(0, brightness - 0.35) / max(brightness, 0.0001);
}

// Half-resolution separable bloom. Threshold only before the first blur.
float4 PSBloomHorizontal(VertexOutput input) : SV_Target
{
    float2 stepUV = float2(3 / max(Resolution.x, 1), 0);
    float3 result = highlight(History.SampleLevel(LinearClamp, input.UV, 0).rgb) * 0.227027;
    [unroll] for (int i = 1; i <= 4; i++)
    {
        float weight = exp(-float(i * i) / 8) * 0.1946;
        result += (highlight(History.SampleLevel(LinearClamp, input.UV + stepUV * i, 0).rgb)
                 + highlight(History.SampleLevel(LinearClamp, input.UV - stepUV * i, 0).rgb)) * weight;
    }
    return float4(result, 1);
}

float4 PSBloomVertical(VertexOutput input) : SV_Target
{
    float2 stepUV = float2(0, 3 / max(Resolution.y, 1));
    float3 result = History.SampleLevel(LinearClamp, input.UV, 0).rgb * 0.227027;
    [unroll] for (int i = 1; i <= 4; i++)
    {
        float weight = exp(-float(i * i) / 8) * 0.1946;
        result += (History.SampleLevel(LinearClamp, input.UV + stepUV * i, 0).rgb
                 + History.SampleLevel(LinearClamp, input.UV - stepUV * i, 0).rgb) * weight;
    }
    return float4(result, 1);
}

float4 PSComposite(VertexOutput input) : SV_Target
{
    float2 uv = input.UV;
    float3 color = History.SampleLevel(LinearClamp, uv, 0).rgb;
    color += Bloom.SampleLevel(LinearClamp, uv, 0).rgb * max(Glow, 0) * 1.4;
    // Scale RGB together so blue/cyan highlights retain their hue.
    float peak = max(color.r, max(color.g, color.b));
    color = color * (1 - exp(-peak * 1.7)) / max(peak, 0.0001);
    float vignette = 1 - 0.35 * smoothstep(0.25, 0.75, length(uv - 0.5));
    color = pow(saturate(color), 0.85) * vignette;
    color += float3(0.0015, 0.002, 0.006) * vignette;
    return float4(saturate(color), 1);
}
