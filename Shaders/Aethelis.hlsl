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
    float2 Padding;
};

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

float hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float noise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(hash21(i), hash21(i + float2(1, 0)), f.x),
                lerp(hash21(i + float2(0, 1)), hash21(i + 1), f.x), f.y);
}

float fbm(float2 p)
{
    float value = 0;
    float amplitude = 0.5;
    [unroll] for (int i = 0; i < 5; i++)
    {
        value += amplitude * noise(p);
        p = mul(float2x2(1.62, 1.18, -1.18, 1.62), p);
        amplitude *= 0.48;
    }
    return value;
}

float3 palette(float t)
{
    return 0.52 + 0.48 * cos(6.28318 * (float3(0.02, 0.24, 0.48) * t + float3(0.00, 0.10, 0.22)));
}

float capsule(float2 p, float2 a, float2 b, float radius)
{
    float2 pa = p - a;
    float2 ba = b - a;
    float h = saturate(dot(pa, ba) / dot(ba, ba));
    return length(pa - ba * h) - radius;
}

float4 PSMain(VertexOutput input) : SV_Target
{
    float2 viewportUV = (input.Position.xy - Origin) / max(Resolution, 1.0);
    float2 p = viewportUV * 2.0 - 1.0;
    p.x *= Resolution.x / max(Resolution.y, 1.0);
    float radius = length(p);
    float angle = atan2(p.y, p.x);
    float2 direction = radius > 0.0001 ? p / radius : float2(0, 1);
    float motion = Time * (0.20 + Mids * 0.95);

    // Seamless circular turbulence: noise is sampled in Cartesian direction space,
    // so the flame never reveals a seam at -pi/pi.
    float broadNoise = fbm(direction * (3.2 + Mids * 1.8) + float2(motion, -motion * 0.73));
    float fineNoise = fbm(direction * 8.5 + float2(-motion * 1.7, motion * 1.15));
    float lickNoise = fbm(direction * 15.0 + float2(motion * 2.4, -motion * 1.9));
    float turbulence = broadNoise * 0.58 + fineNoise * 0.29 + lickNoise * 0.13;

    // The FFT bass profile is deliberately compressed so real music can reach a
    // cinematic response without requiring a normalized band value of exactly 1.
    float bassImpact = saturate(pow(max(Bass, 0.0), 0.46) * (0.78 + Intensity * 0.66));
    float intensityStage = saturate((Intensity - 0.35) / 2.35);

    float quietBreath = sin(Time * 2.094) * 0.004;
    float baseRadius = 0.40 + quietBreath + bassImpact * 0.030;
    float displacedRadius = baseRadius + (turbulence - 0.50) * (0.034 + bassImpact * 0.145);
    float radialDistance = radius - displacedRadius;

    // Flames grow primarily outward. Bass adds height and body, while mids make
    // the contour travel faster around the ring instead of scaling the whole image.
    float flameHeight = 0.042 + bassImpact * (0.245 + intensityStage * 0.165);
    float tongues = saturate((turbulence - 0.31) * (2.65 + bassImpact * 2.15));
    float dramaticLicks = pow(saturate(fineNoise * 0.72 + lickNoise * 0.70 - 0.34), 2.2);
    float localFlameHeight = flameHeight * (0.42 + tongues * 0.72 + dramaticLicks * (0.95 + bassImpact * 0.95));
    float outwardDistance = max(radialDistance, 0.0);
    float flameEnvelope = smoothstep(localFlameHeight, 0.0, outwardDistance);
    flameEnvelope *= step(0.0, radialDistance);
    float innerBody = exp(-abs(radialDistance) * (68.0 - bassImpact * 25.0));
    float hotCore = exp(-abs(radialDistance + 0.004) * 155.0);
    float emberEdge = exp(-max(outwardDistance, 0.0) * (20.0 - bassImpact * 6.0)) * step(0.0, radialDistance);

    float heat = saturate(outwardDistance / max(localFlameHeight, 0.001));
    float3 whiteHot = float3(1.00, 0.96, 0.66);
    float3 orange = float3(1.00, 0.18, 0.008);
    float3 deepRed = float3(0.52, 0.004, 0.001);
    float3 fireColor = lerp(whiteHot, orange, smoothstep(0.02, 0.42, heat));
    fireColor = lerp(fireColor, deepRed, smoothstep(0.42, 1.0, heat));

    float audioEnergy = 0.56 + bassImpact * 2.25 + Mids * 0.46 + Highs * 0.28;
    float fire = innerBody * 0.70 + flameEnvelope * (0.48 + tongues * 1.18) + emberEdge * (0.12 + bassImpact * 0.32);
    float3 color = float3(0.0015, 0.0018, 0.0035);
    color += fireColor * fire * audioEnergy;
    color += whiteHot * hotCore * (0.42 + bassImpact * 1.85);

    // Restrained bloom belongs to the same single ring; it is not another ring.
    float bloom = exp(-abs(radialDistance) * (20.0 - Glow * 6.0));
    color += float3(0.50, 0.022, 0.001) * bloom * (0.12 + Glow * 0.30 + bassImpact * 0.68);

    color = 1.0 - exp(-color * 1.12);
    return float4(saturate(color), 1.0);
}
