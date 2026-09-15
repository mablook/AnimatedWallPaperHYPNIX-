// HYPNIX Event Horizon - original clean-room black hole. (c) Marcelo Bossle (HYPNIX).
// Physics-based technique only (no third-party shader code): a Schwarzschild
// photon geodesic is integrated (light bending via a = -1.5 * h^2 * r / |r|^5)
// to lens an emissive accretion disk, a photon ring near 1.5 r_s and a procedural
// starfield. Disk color uses a blackbody-style temperature ramp with Doppler
// beaming; the frame is HDR and resolved with an ACES filmic tonemap. Audio
// drives accretion brightness, disk rotation and photon-ring energy.
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

static const float PI = 3.14159265;

float Hash21(float2 p) { p = frac(p * float2(123.34, 345.45)); p += dot(p, p + 34.345); return frac(p.x * p.y); }
float Hash31(float3 p) { p = frac(p * 0.1031); p += dot(p, p.yzx + 33.33); return frac((p.x + p.y) * p.z); }

float Noise3(float3 p)
{
    float3 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float n000 = Hash31(i + float3(0, 0, 0)), n100 = Hash31(i + float3(1, 0, 0));
    float n010 = Hash31(i + float3(0, 1, 0)), n110 = Hash31(i + float3(1, 1, 0));
    float n001 = Hash31(i + float3(0, 0, 1)), n101 = Hash31(i + float3(1, 0, 1));
    float n011 = Hash31(i + float3(0, 1, 1)), n111 = Hash31(i + float3(1, 1, 1));
    float x00 = lerp(n000, n100, f.x), x10 = lerp(n010, n110, f.x);
    float x01 = lerp(n001, n101, f.x), x11 = lerp(n011, n111, f.x);
    return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z);
}

// Fewer octaves = smoother disk with less grain.
float Fbm(float3 p)
{
    float s = 0.0, a = 0.5;
    [unroll] for (int i = 0; i < 3; i++) { s += a * Noise3(p); p *= 2.02; a *= 0.5; }
    return s;
}

float3 Blackbody(float t) // t: 0 = cooler outer (amber), 1 = hotter inner (blue-white)
{
    float3 cool = float3(1.0, 0.42, 0.12);
    float3 mid  = float3(1.0, 0.85, 0.55);
    float3 hot  = float3(0.75, 0.85, 1.0);
    return t < 0.5 ? lerp(cool, mid, t * 2.0) : lerp(mid, hot, (t - 0.5) * 2.0);
}

// Anti-aliased starfield. 'aa' fades stars where gravitational lensing makes the
// ray direction change quickly between pixels, which would otherwise sparkle as
// grain around the hole. Stars are soft, jittered and firefly-clamped.
float3 Starfield(float3 d, float aa)
{
    float2 uv = float2(atan2(d.z, d.x) / (2.0 * PI) + 0.5, acos(clamp(d.y, -1.0, 1.0)) / PI);
    float3 col = float3(0, 0, 0);
    [unroll] for (int layer = 0; layer < 2; layer++)
    {
        float scale = 150.0 + layer * 210.0;
        float2 g = uv * scale;
        float2 cell = floor(g);
        float h = Hash21(cell + layer * 41.7);
        if (h > 0.955)
        {
            float2 jit = 0.5 + 0.4 * (float2(Hash21(cell + 3.1), Hash21(cell + 7.7)) - 0.5);
            float dstar = length(frac(g) - jit);
            float soft = smoothstep(0.34, 0.0, dstar);
            float star = min(soft * soft * (h - 0.955) / 0.045, 1.1);
            col += star * lerp(float3(0.65, 0.75, 1.0), float3(1.0, 0.9, 0.8), Hash21(cell + 5.5));
        }
    }
    col += Fbm(d * 2.5 + 11.0) * Fbm(d * 1.7 + 5.0) * float3(0.012, 0.016, 0.03); // faint nebula
    return col * aa * 0.7; // 'aa' fades stars AND nebula where the lens magnifies the background
}

float3 Aces(float3 x) { return saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14)); }

float4 PSMain(VertexOutput input) : SV_Target
{
    float aspect = Resolution.x / max(Resolution.y, 1.0);
    float2 uv = float2(input.UV.x, 1.0 - input.UV.y);
    float2 p = (uv * 2.0 - 1.0) * float2(aspect, 1.0);

    float intensity = clamp(Intensity, 0.0, 8.0);
    float glow = max(Glow, 0.0);
    float bassE = 1.0 - exp(-3.2 * max(Bass - 0.01, 0.0));
    float midsE = 1.0 - exp(-3.2 * max(Mids - 0.01, 0.0));
    float highsE = 1.0 - exp(-3.2 * max(Highs - 0.01, 0.0));

    // Geometry in Schwarzschild radii (r_s = 1).
    const float rs = 1.0, rPhoton = 1.5, rIn = 3.0, rOut = 13.0, rEscape = 42.0;

    // Slowly orbiting camera; mids nudge the orbit, a slow bob adds life.
    float ct = Time * 0.04 + midsE * 0.25;
    float3 camPos = float3(sin(ct) * 15.0, 2.6 + sin(Time * 0.05) * 0.5, cos(ct) * 15.0);
    float3 fwd = normalize(-camPos);
    float3 right = normalize(cross(float3(0, 1, 0), fwd));
    float3 up = cross(fwd, right);
    float3 pos = camPos;
    float3 dir = normalize(p.x * right + p.y * up + fwd * 1.6);

    float3 hc = cross(pos, dir);
    float h2 = dot(hc, hc);
    float rot = Time * (0.55 + midsE * 0.6);

    float3 col = float3(0, 0, 0);
    float trans = 1.0;   // transparency remaining toward the background
    float ring = 0.0;    // photon-ring accumulator

    [loop] for (int i = 0; i < 300; i++)
    {
        float r = length(pos);
        if (r < rs * 1.02) { trans = 0.0; break; }  // captured by the horizon
        if (r > rEscape) break;                      // escaped: sample stars below

        float dt = clamp(r * 0.10, 0.02, 0.5);
        float3 oldPos = pos;
        float3 accel = -1.5 * h2 * pos / pow(r, 5.0);
        dir += accel * dt;
        pos += dir * dt;

        float dd = (r - rPhoton) / 0.10;
        ring += exp(-dd * dd) * dt;

        // Accretion disk in the equatorial plane (y = 0); accumulate every crossing
        // so the lensed disk arcs over and under the hole.
        if (oldPos.y * pos.y < 0.0)
        {
            float f = oldPos.y / (oldPos.y - pos.y);
            float3 hp = lerp(oldPos, pos, f);
            float rad = length(hp.xz);
            if (rad > rIn && rad < rOut)
            {
                float tR = saturate((rOut - rad) / (rOut - rIn));
                float ang = atan2(hp.z, hp.x);
                float turb = Fbm(float3(log(rad) * 3.0, ang * 2.0 - rot, rad * 0.2));
                turb *= smoothstep(rIn, rIn + 2.5, rad); // calm the strongly-magnified inner edge
                float bright = pow(tR, 1.5) * (0.6 + 0.7 * turb);
                float3 vdir = normalize(cross(float3(0, 1, 0), hp)); // orbital velocity
                float dop = clamp(1.0 + 0.7 * dot(vdir, normalize(camPos - hp)), 0.4, 2.2);
                float3 emit = lerp(Blackbody(tR), lerp(StartColor, EndColor, tR), 0.45);
                emit = min(emit * bright * (dop * dop) * (0.8 + 1.3 * bassE), 8.0);
                float dens = saturate(bright * 1.2);
                col += trans * emit;
                trans *= 1.0 - dens * 0.85;
            }
        }
        if (trans < 0.01) break;
    }

    // Fade the starfield where the lens aliases (screen-space derivative of the ray).
    float starAA = saturate(1.0 - length(fwidth(dir)) * 14.0);
    if (trans > 0.001) col += trans * Starfield(dir, starAA);
    ring = min(ring, 2.5); // tame chaotic photon-sphere spikes into a clean, steady ring
    col += float3(1.0, 0.9, 0.75) * ring * (0.12 + 0.6 * glow) * (0.6 + 0.9 * highsE);

    col = min(col, 12.0);                       // suppress fireflies before tonemapping
    col *= 0.6 + 0.9 * sqrt(intensity / 3.0);
    return float4(Aces(col), 1.0);
}
