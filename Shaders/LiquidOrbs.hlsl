// HYPNIX Liquid Orbs - original clean-room implementation. (c) HYPNIX.
// Generic technique (no third-party shader code): signed-distance metaballs
// (polynomial smooth-union) raymarched under a perspective camera and shaded in
// linear light with diffuse + Blinn-Phong specular, Schlick fresnel, a
// procedural gradient environment reflection and cheap SDF ambient occlusion.
// Sphere motion is generated in C# with our own per-orb hash.
cbuffer FrameData : register(b0)
{
    float2 Resolution; float Time; float Bass;
    float Mids; float Highs; float Intensity; float Glow;
    float2 Origin; float PaddingX; float OffsetY;
    float3 StartColor; float Scale;
    float3 EndColor; float OffsetX;
    float4 Spectrum[16]; // reserved (layout parity with the shared constant buffer)
    float4 Spheres[16];  // xyz = animated center; w = base radius.
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

float SmoothUnion(float a, float b, float k) // polynomial smooth-min
{
    float h = saturate(0.5 + 0.5 * (b - a) / k);
    return lerp(b, a, h) - k * h * (1.0 - h);
}

float MapScene(float3 p, float radiusGain, float k)
{
    float d = 1e9;
    [unroll] for (int i = 0; i < 16; i++)
    {
        float sd = length(p - Spheres[i].xyz) - Spheres[i].w * radiusGain;
        d = SmoothUnion(d, sd, k);
    }
    return d;
}

float3 NormalAt(float3 p, float rg, float k)
{
    const float h = 0.0006;
    const float2 e = float2(1, -1);
    float3 n = e.xyy * MapScene(p + e.xyy * h, rg, k)
             + e.yyx * MapScene(p + e.yyx * h, rg, k)
             + e.yxy * MapScene(p + e.yxy * h, rg, k)
             + e.xxx * MapScene(p + e.xxx * h, rg, k);
    return n / max(length(n), 1e-6);
}

float Occlusion(float3 p, float3 n, float rg, float k)
{
    float occ = 0.0, sca = 1.0;
    [unroll] for (int j = 1; j <= 3; j++)
    {
        float hd = 0.03 * j;
        occ += (hd - MapScene(p + n * hd, rg, k)) * sca;
        sca *= 0.55;
    }
    return saturate(1.0 - 1.6 * occ);
}

float4 PSMain(VertexOutput input) : SV_Target
{
    float2 uv = float2(input.UV.x, 1.0 - input.UV.y);
    float aspect = Resolution.x / max(Resolution.y, 1.0);
    float drive = clamp(Intensity, 0.0, 8.0);
    float motion = drive / (1.0 + 0.12 * drive);
    float glow = max(Glow, 0.0);
    float bass = 1.0 - exp(-3.2 * max(Bass - 0.01, 0.0));
    float mids = 1.0 - exp(-3.2 * max(Mids - 0.01, 0.0));
    float highs = 1.0 - exp(-3.2 * max(Highs - 0.01, 0.0));

    float radiusGain = 1.0 + bass * 0.14 * motion;
    float k = 0.42 + mids * 0.10 * motion;

    // Perspective camera with a slow bass-reactive dolly.
    float2 ndc = (uv - 0.5) * float2(aspect, 1.0);
    ndc = (ndc - float2(OffsetX, OffsetY) * 0.5) / max(Scale, 0.05); // user size/position
    float3 ro = float3(0.0, 0.0, 6.2 - bass * 0.6 * motion);
    float3 rd = normalize(float3(ndc * 1.15, -1.0));

    float depth = 0.0;
    bool hit = false;
    float3 p = ro;
    [loop] for (int step = 0; step < 72; step++)
    {
        p = ro + rd * depth;
        float d = MapScene(p, radiusGain, k);
        if (d < 0.0006) { hit = true; break; }
        depth += d;
        if (depth > 14.0) break;
    }
    // Shade grazing rays at their last estimate to avoid dark seams on smooth joins.
    hit = hit || depth <= 14.0;
    p = ro + rd * min(depth, 14.0);

    float3 col;
    if (!hit)
    {
        col = lerp(StartColor, EndColor, uv.y) * 0.06; // darkened palette backdrop
    }
    else
    {
        float3 n = NormalAt(p, radiusGain, k);
        float3 L = normalize(float3(0.5, 0.7, 0.6));
        float3 V = -rd;
        float3 H = normalize(L + V);
        float diff = max(dot(n, L), 0.0);
        float spec = pow(max(dot(n, H), 0.0), 48.0) * (0.4 + 0.6 * highs);
        float fres = pow(1.0 - max(dot(n, V), 0.0), 5.0);
        float occ = Occlusion(p, n, radiusGain, k);

        // Iridescent base albedo (our cosine palette) blended with the user palette.
        float3 pal = 0.5 + 0.5 * cos(Time * 1.6 + p.z * 0.6 + float3(0.0, 2.1, 4.2));
        float3 tint = lerp(StartColor, EndColor, saturate(uv.y));
        float3 albedo = lerp(pal, tint, 0.5);

        // Procedural environment reflection (cheap gradient by reflected direction).
        float3 refl = reflect(rd, n);
        float3 env = lerp(float3(0.02, 0.03, 0.06), float3(0.6, 0.8, 1.0),
                          saturate(refl.y * 0.5 + 0.5));

        col = albedo * (0.18 + 0.90 * diff) * occ;                 // diffuse + AO
        col += env * (0.15 + 0.85 * fres) * (0.6 + 0.6 * glow);    // fresnel reflection
        col += spec * float3(1.0, 1.0, 1.0) * (1.0 + glow);        // specular highlight
        col *= 1.0 + bass * 0.15 * motion;                         // bass pulse
        col *= exp(-max(depth - 4.0, 0.0) * 0.10);                 // depth fog
    }

    col *= sqrt(drive / 3.0);
    col = saturate((col * (2.51 * col + 0.03)) / (col * (2.43 * col + 0.59) + 0.14)); // filmic
    return float4(col, 1.0);
}
