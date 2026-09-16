// HYPNIX Event Horizon - independent Schwarzschild visualization.
// References and implementation boundaries: docs/EVENT_HORIZON_STUDY.md.
// Dimensionless horizon radius = 1. Art-directed plasma; not a Kerr metric solver.
cbuffer FrameData : register(b0)
{
    float2 Resolution; float Time; float Bass;
    float Mids; float Highs; float Intensity; float Glow;
    float2 Origin; float2 Padding;
    float3 StartColor; float ColorPadA;
    float3 EndColor; float ColorPadB;
    float4 Spectrum[16];
};
Texture2D<float4> Scene : register(t0);
Texture2D<float4> Halo : register(t1);
SamplerState LinearClamp : register(s0);
struct VertexOutput { float4 Position : SV_Position; float2 UV : TEXCOORD0; };
VertexOutput VSMain(uint id : SV_VertexID)
{
    VertexOutput o;
    o.UV = float2((id << 1) & 2, id & 2);
    o.Position = float4(o.UV * float2(2, -2) + float2(-1, 1), 0, 1);
    return o;
}
float RandomCell(float3 p)
{
    uint3 q = asuint(int3(p));
    uint h = q.x * 1597334677u ^ q.y * 3812015801u ^ q.z * 2798796415u;
    h = (h ^ (h >> 16)) * 2246822519u;
    h = (h ^ (h >> 13)) * 3266489917u;
    return float(h ^ (h >> 16)) / 4294967295.0;
}
float Cloud(float3 p)
{
    float3 cell = floor(p), s = frac(p);
    s = s * s * s * (s * (s * 6 - 15) + 10);
    float value = 0;
    [unroll] for (int z = 0; z < 2; z++)
    [unroll] for (int y = 0; y < 2; y++)
    [unroll] for (int x = 0; x < 2; x++)
    {
        float3 corner = float3(x, y, z);
        float3 w = lerp(1 - s, s, corner);
        value += RandomCell(cell + corner) * w.x * w.y * w.z;
    }
    return value;
}
float Plasma(float radius, float angle, float height, float clock)
{
    // Seamless periodic coordinates, differential rotation and no random time jitter.
    float phase = angle - clock * 2.8 / pow(radius, 1.5);
    float2 orbit = float2(cos(phase), sin(phase));
    float3 p = float3(orbit * 2.6, radius * 5.5 + height * 1.8);
    return saturate(0.12 + Cloud(p) * 0.72
        + Cloud(p * 2.1 + float3(9, 17, 3)) * 0.30
        + Cloud(p * 4.3 + float3(21, 5, 13)) * 0.13);
}
float3 Gravity(float3 p, float angularMomentumSquared)
{
    float rr = max(dot(p, p), 0.1);
    return -1.5 * angularMomentumSquared * p / (rr * rr * sqrt(rr));
}
float3 Sky(float3 direction)
{
    float3 p = normalize(direction) * 170;
    float seed = RandomCell(floor(p));
    float3 local = frac(p) - 0.5;
    float stars = step(0.982, seed) * exp(-dot(local, local) * 65) * 0.48;
    return float3(0.0008, 0.0013, 0.0025) + stars * float3(0.75, 0.84, 1);
}
float GaussianIntegral(float x)
{
    float xx = x * x;
    return sign(x) * sqrt(max(0, 1 - exp(-xx * (1.27323954 + 0.147 * xx) / (1 + 0.147 * xx))));
}
float DiskFrequency(float radius)
{
    // Map the 64 logarithmic FFT bands onto the emitting disk, not screen pixels.
    float index = saturate((radius - 3.0) / 8.5) * 63;
    int lo = (int)floor(index);
    int hi = min(lo + 1, 63);
    float amplitude = lerp(Spectrum[lo / 4][lo % 4], Spectrum[hi / 4][hi % 4], frac(index));
    return 1 - exp(-13 * max(amplitude, 0));
}
float3 Trace(float2 pixel)
{
    float2 p = (pixel * 2 - 1) * float2(Resolution.x / max(Resolution.y, 1), -1);
    float roll = -0.12;
    p = float2(cos(roll) * p.x - sin(roll) * p.y, sin(roll) * p.x + cos(roll) * p.y);
    float azimuth = Time * 0.008;
    float3 eye = float3(18 * sin(azimuth), 1.45, 18 * cos(azimuth));
    float3 forward = normalize(-eye);
    float3 right = normalize(cross(forward, float3(0, 1, 0)));
    float3 up = cross(right, forward);
    float3 pos = eye;
    float3 velocity = normalize(forward * 2.55 + right * p.x + up * p.y);
    float3 momentum = cross(pos, velocity);
    float h2 = dot(momentum, momentum);
    float3 radiance = 0;
    float transmission = 1;
    float closestApproach = length(eye);
    bool escaped = false;
    float clock = Time * 1.25; // Audio affects light, never accumulated phase.
    [loop] for (int stepIndex = 0; stepIndex < 360; stepIndex++)
    {
        float distance = length(pos);
        closestApproach = min(closestApproach, distance);
        if (distance < 1.015 || transmission < 0.004) break;
        if (distance > 35 && dot(pos, velocity) > 0) { escaped = true; break; }
        float stepSize = clamp(distance * 0.075, 0.035, 1.25);
        if (abs(pos.y) < 0.5 && distance > 2.7 && distance < 12)
            stepSize = min(stepSize, 0.28);
        // Explicit midpoint integration evaluates curvature at the half step.
        float3 halfVelocity = velocity + Gravity(pos, h2) * (stepSize * 0.5);
        float3 midpoint = pos + velocity * (stepSize * 0.5);
        float3 next = pos + halfVelocity * stepSize;
        velocity += Gravity(midpoint, h2) * stepSize;
        float deltaY = next.y - pos.y;
        float fraction = pos.y * next.y < 0 ? saturate(-pos.y / deltaY) : 0.5;
        float3 samplePos = lerp(pos, next, fraction);
        float radius = length(samplePos.xz);
        if (radius > 3.0 && radius < 11.5 && abs(samplePos.y) < 0.65)
        {
            float envelope = smoothstep(3.0, 3.65, radius) * (1 - smoothstep(8.5, 11.5, radius));
            float localAudio = DiskFrequency(radius);
            // Shear local filaments without moving the camera or the shadow.
            float plasma = Plasma(radius, atan2(samplePos.z, samplePos.x) + localAudio * 0.5, samplePos.y, clock);
            float thickness = 0.035 + 0.003 * radius;
            // Integrate a Gaussian vertical profile across the segment so even
            // grazing rays resolve a thin disk without stochastic sampling.
            float column = abs(deltaY) > 0.0001
                ? abs(GaussianIntegral(next.y / thickness) - GaussianIntegral(pos.y / thickness))
                    * thickness * 0.8862269 / abs(deltaY)
                : exp(-pow(samplePos.y / thickness, 2));
            float structure = pow(saturate((plasma - 0.18) * 1.5), 3);
            float density = envelope * (0.06 + 8 * structure);
            float opacity = 1 - exp(-density * column * length(next - pos) * 7);
            float heat = pow(3.5 / radius, 1.4);
            float3 orbit = normalize(float3(-samplePos.z, 0, samplePos.x));
            float speed = sqrt(0.5 / max(radius - 1, 1));
            float shift = sqrt(1 - 1 / radius) * sqrt(1 - speed * speed)
                        / max(0.3, 1 - speed * dot(orbit, -normalize(velocity)));
            float3 warm = lerp(float3(1.0, 0.32, 0.08), float3(1.0, 0.89, 0.72), saturate(heat * shift));
            float3 tint = lerp(StartColor, EndColor, saturate((radius - 3) / 8.5));
            warm *= lerp(float3(1, 1, 1), tint * 1.4, 0.16);
            float energy = (0.4 + 3.2 * heat) * (0.25 + structure * 1.8) * pow(shift, 3);
            // Bright ridges and dark troughs remain readable at high exposure. Audio pushes
            // the ridges harder, and adds an overall lift, so the disk clearly answers the sound.
            float ridges = smoothstep(0.09, 0.44, structure);
            energy *= lerp(1, 0.10 + 7.5 * ridges, localAudio);
            energy *= 1 + 1.4 * localAudio;
            warm = lerp(warm, float3(1.0, 0.67, 0.32), localAudio * (1 - ridges) * 0.4);
            // Fade higher-order light paths after they wind close to the horizon.
            // This removes the detached hairline ring without masking foreground
            // material, altering capture geometry or clipping the primary arcs.
            // Include the ring's junction tails so no bright teeth remain where
            // the secondary image met the foreground disk.
            float primaryVisibility = smoothstep(2.15, 2.6, closestApproach);
            radiance += transmission * opacity * warm * energy * primaryVisibility;
            transmission *= 1 - opacity;
        }
        pos = next;
    }
    // Captured and budget-exhausted rays cannot leak stars through the shadow.
    if (escaped) radiance += transmission * Sky(velocity);
    return radiance;
}
float4 PSMain(VertexOutput input) : SV_Target
{
    float2 offset = float2(0.25, 0.25) / max(Resolution, 1);
    float3 color = (Trace(input.UV - offset) + Trace(input.UV + offset)) * 0.5;
    return float4(min(color * (0.3 + 0.7 * sqrt(clamp(Intensity, 0, 8) / 3)), 24), 1);
}
// Separable HDR scattering local to each monitor; tone mapping happens once.
float4 Blur(VertexOutput input, float2 axis)
{
    float3 sum = 0;
    float weightSum = 0;
    [unroll] for (int tap = -8; tap <= 8; tap++)
    {
        float weight = exp(-float(tap * tap) / 22);
        sum += Scene.SampleLevel(LinearClamp, input.UV + axis * tap, 0).rgb * weight;
        weightSum += weight;
    }
    return float4(sum / weightSum, 1);
}
float4 PSBloomHorizontal(VertexOutput input) : SV_Target
{
    return Blur(input, float2(0.004 * Resolution.y / max(Resolution.x, 1), 0));
}
float4 PSBloomVertical(VertexOutput input) : SV_Target
{
    return Blur(input, float2(0, 0.004));
}
float4 PSComposite(VertexOutput input) : SV_Target
{
    float3 color = Scene.SampleLevel(LinearClamp, input.UV, 0).rgb;
    color += Halo.SampleLevel(LinearClamp, input.UV, 0).rgb * clamp(Glow, 0, 3) * 0.18;
    // Lower base exposure so the disk no longer starts blown out at default intensity.
    color *= 0.48;
    color = saturate((color * (2.51 * color + 0.03)) / (color * (2.43 * color + 0.59) + 0.14));
    return float4(pow(color, 1.0 / 2.2), 1);
}
