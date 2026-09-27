// HYPNIX Ocean P3 lighting over the approved P2 surface; metres, seconds, Y up.
// HDR linear scene -> bounded optical bloom -> one tone map -> one sRGB conversion.
cbuffer OceanFrame : register(b0)
{
    float4 Resolution;       // internal width/height, aspect, tan(vertical FOV / 2)
    float4 Camera;           // xyz, pitch down in radians
    float4 Light;            // unit direction, angular radius
    float4 Radiance;         // pre-exposed RGB radiance, exposure
    float4 SkyTop;
    float4 SkyHorizon;
    float4 Water;            // low volume return, choppiness
    float4 Grid;             // segments X/Z, lighting preset, analytic comparison flag
    float4 Waves[32];        // kx,kz,amplitude,phase evaluated in double on CPU
    float4 Bands[3];         // physical length, amplitude gain, unused
    float4 Refinement;       // enable, cloud cache interpolation, gibbous phase, bloom strength
    float4 ApparentBody;     // visual angular radius, lunar display contrast, unused
};
Texture2D Scene : register(t0);
Texture2D<float4> Displacements[3] : register(t1);
Texture2D<float4> SlopeA[3] : register(t4);
Texture2D<float4> SlopeB[3] : register(t7);
Texture2D<float4> SkyEnvironment : register(t10);
Texture2D<float4> MoonAlbedo : register(t11);
Texture2D<float4> CloudPrevious : register(t12);
Texture2D<float4> CloudNext : register(t13);
Texture2D<float4> OpticalBloom : register(t14);
SamplerState LinearClamp : register(s0);
SamplerState SurfaceSampler : register(s1);
SamplerState SkySampler : register(s2);
static const float PI = 3.14159265359;

float3 Forward() { return float3(0, -sin(Camera.w), cos(Camera.w)); }
float3 Up() { return float3(0, cos(Camera.w), sin(Camera.w)); }

struct ScreenVertex { float4 position : SV_Position; float2 uv : TEXCOORD0; };
ScreenVertex ScreenVS(uint id : SV_VertexID)
{
    ScreenVertex o;
    o.uv = float2((id << 1) & 2, id & 2);
    o.position = float4(o.uv * float2(2, -2) + float2(-1, 1), 0, 1);
    return o;
}

float2 SkyUv(float3 direction)
{
    float angle=asin(clamp(direction.y,-1,1));
    return float2(atan2(direction.x,direction.z)/(2*PI)+.5,.5-.5*sign(angle)*sqrt(abs(angle)/(PI*.5)));
}
float4 CloudField(float3 direction, float lod)
{
    float2 uv=SkyUv(direction);
    return lerp(CloudPrevious.SampleLevel(SkySampler,uv,lod),CloudNext.SampleLevel(SkySampler,uv,lod),Refinement.y);
}
float CelestialTransmission(float3 direction)
{
    if (Refinement.x>.5) return CloudField(direction,0).a;
    return SkyTop.w > .5 ? SkyEnvironment.SampleLevel(SkySampler,SkyUv(Light.xyz),0).a : 1;
}
float3 MoonMaterial(float3 direction, float radius, bool displayDetail)
{
    float3 axisX=normalize(cross(float3(0,1,0),Light.xyz));
    float3 axisY=cross(Light.xyz,axisX);
    float2 q=float2(dot(direction,axisX),dot(direction,axisY))/radius;
    q*=min(1,.9999/max(length(q),.0001));
    float3 normal=float3(q,sqrt(max(0,1-dot(q,q))));
    float2 uv=float2(.5+atan2(normal.x,normal.z)/(2*PI),acos(normal.y)/PI);
    // Filter to the resolved disk diameter, including the requested perceptual enlargement.
    float lod=max(0,log2(2048*Resolution.w/(Resolution.y*radius*PI)));
    float3 albedo=MoonAlbedo.SampleLevel(SkySampler,uv,lod).rgb;
    float phaseAngle=Refinement.z*.85;
    float cosine=saturate(dot(normal,float3(sin(phaseAngle),0,cos(phaseAngle))));
    // Lommel-Seeliger + small Lambert term: preserves maria at full moon.
    float lighting=(.8*2*cosine/max(cosine+normal.z,.0001)+.2*cosine)/.933333;
    float3 material=albedo/.32;
    if (displayDetail) material=pow(max(material,.0001),ApparentBody.y);
    return material*lighting;
}
// The distributed environment deliberately excludes the solar/lunar disk.
float3 EnvironmentFiltered(float3 direction, float lod)
{
    if (SkyTop.w > .5)
    {
        float3 sky=SkyEnvironment.SampleLevel(SkySampler,SkyUv(direction),lod).rgb;
        if (Refinement.x>.5) { float4 cloud=CloudField(direction,lod); sky=sky*cloud.a+cloud.rgb; }
        return sky;
    }
    float elevation = saturate(direction.y);
    float3 sky = lerp(SkyHorizon.rgb, SkyTop.rgb, pow(saturate(elevation / .28), .55));
    float haze = pow(saturate(dot(direction, Light.xyz)), 18) * exp(-elevation * 4);
    if (Grid.z < .5) sky += float3(.24, .055, .008) * haze;
    // A broad, dim cloud veil; no high frequency textures pretending to be waves.
    return sky * lerp(.55, 1, smoothstep(-.08, .04, direction.y));
}
float3 Environment(float3 direction) { return EnvironmentFiltered(direction,0); }

float4 SkyPS(ScreenVertex input) : SV_Target
{
    float2 xy = (input.uv * float2(2, -2) + float2(-1, 1)) * Resolution.w;
    xy.x *= Resolution.z;
    float3 ray = normalize(Forward() + float3(xy.x, 0, 0) + Up() * xy.y);
    float3 color = Environment(ray);
    float angle = acos(clamp(dot(ray, Light.xyz), -1, 1));
    float pixelAngle = max(length(fwidth(ray))*(Refinement.x>.5 ? .5 : 1), .0001);
    float disk = 1 - smoothstep(ApparentBody.x - pixelAngle, ApparentBody.x + pixelAngle, angle);
    float3 source=Radiance.rgb;
    if(Refinement.x>.5 && Grid.z>1.5 && Grid.z<2.5)
    {
        // Local photographic exposure for the resolved lunar disk only. Reflection uses
        // the original illuminant energy below. This is an explicit SDR art direction.
        source=float3(1.35,1.38,1.42)*MoonMaterial(ray,ApparentBody.x,true);
    }
    color += source * disk * CelestialTransmission(ray);
    return float4(color, 1);
}

struct WaterVertex
{
    float4 position : SV_Position;
    float3 world : TEXCOORD0;
    float2 parameter : TEXCOORD1;
};

WaterVertex WaterVS(uint id : SV_VertexID)
{
    uint columns = (uint)Grid.x + 1;
    float2 uv = float2(id % columns / Grid.x, id / columns / Grid.y);
    // Project grid rows onto the mean sea plane: spend vertices on visible pixels,
    // rather than on logarithmic rows mostly outside the view. Extend beyond the
    // frustum so displaced crests never reveal the edge of the grid.
    float horizon = tan(Camera.w) / Resolution.w;
    float top = min(1.5, horizon - Camera.y / (4000 * Resolution.w));
    float ndcY = lerp(-1.65, top, uv.y);
    float3 ray = Forward() + Up() * ndcY * Resolution.w;
    float rayDistance = Camera.y / max(-ray.y, .0001);
    float2 q = float2((uv.x * 2 - 1) * 1.35 * rayDistance * Resolution.w * Resolution.z,
        Camera.z + ray.z * rayDistance);
    float3 p = float3(q.x, 0, q.y);
    float spacing = max(Camera.y * Resolution.w * (top + 1.65) / (Grid.y * ray.y * ray.y),
        2 * 1.35 * rayDistance * Resolution.w * Resolution.z / Grid.x);
    if (Grid.w < .5)
    {
        [unroll] for (int band = 0; band < 3; band++)
        {
            float lod = max(0, log2(spacing * 256 / Bands[band].x));
            float3 d = Displacements[band].SampleLevel(SurfaceSampler, q / Bands[band].x + 0.5 / 256.0, lod).xyz * Bands[band].y;
            p += d * float3(Water.w, 1, Water.w);
        }
    }
    else
    {
        [unroll] for (int i = 0; i < 16; i++)
        {
            float4 w = Waves[i];
            float k = length(w.xy);
            float resolved = exp(-.10 * k * k * spacing * spacing);
            float s, c; sincos(dot(w.xy, q) + w.w, s, c);
            p += w.z * resolved * float3(-Water.w * w.x / k * c, s, -Water.w * w.y / k * c);
        }
    }
    float3 relative = p - Camera.xyz;
    float z = dot(relative, Forward());
    WaterVertex output;
    output.position = float4(relative.x / (Resolution.w * Resolution.z),
        dot(relative, Up()) / Resolution.w, z * (5000.0 / 4999.9) - .100002, z);
    output.world = p;
    output.parameter = q;
    return output;
}

// Beckmann NDF: alpha^2 = twice the per-axis slope variance, with compatible
// Gaussian footprint filtering. This is not a GGX roughness/variance shortcut.
float Beckmann(float NoH, float alpha2)
{
    float cosine2 = max(NoH * NoH, .00001);
    return exp((cosine2 - 1) / (alpha2 * cosine2)) / (PI * alpha2 * cosine2 * cosine2);
}
float SmithG1(float NoV, float alpha2)
{
    float a = NoV / sqrt(max(alpha2 * (1 - NoV * NoV), .0000001));
    if (a >= 1.6) return 1;
    return (3.535 * a + 2.181 * a * a) / (1 + 2.276 * a + 2.577 * a * a);
}
float Fresnel(float cosine) { return .02037 + .97963 * pow(1 - saturate(cosine), 5); }

float4 WaterPS(WaterVertex input) : SV_Target
{
    float2 dx = ddx(input.parameter), dy = ddy(input.parameter);
    float3 tangentX = float3(1, 0, 0), tangentZ = float3(0, 0, 1);
    float missingSlope = 0;
    if (Grid.w < .5)
    {
        [unroll] for (int band = 0; band < 3; band++)
        {
            float invLength = 1 / Bands[band].x;
            float gain = Bands[band].y;
            // DFT samples live at i/N, whereas texture samples live at (i+.5)/N.
            float2 uv = input.parameter * invLength + .5 / 256;
            float4 a = SlopeA[band].SampleGrad(SurfaceSampler, uv, dx * invLength, dy * invLength);
            float4 b = SlopeB[band].SampleGrad(SurfaceSampler, uv, dx * invLength, dy * invLength);
            tangentX += gain * float3(Water.w * a.z, a.x, Water.w * b.x);
            tangentZ += gain * float3(Water.w * a.w, a.y, Water.w * b.y);
            missingSlope += max(0, b.z + b.w - dot(a.xy, a.xy)) * gain * gain;
        }
    }
    else
    {
        [unroll] for (int i = 0; i < 32; i++)
        {
            float4 w = Waves[i];
            float k = length(w.xy);
            float phaseVariance = (pow(dot(w.xy, dx), 2) + pow(dot(w.xy, dy), 2)) / 12;
            float weight = exp(-.5 * phaseVariance);
            float s, c; sincos(dot(w.xy, input.parameter) + w.w, s, c);
            float chop = i < 16 ? Water.w : 0;
            float3 derivative = w.z * weight * float3(chop * w.x / k * s, c, chop * w.y / k * s);
            tangentX += derivative * w.x;
            tangentZ += derivative * w.y;
            missingSlope += .5 * w.z * w.z * k * k * (1 - weight * weight);
        }
    }
    float3 n = normalize(cross(tangentZ, tangentX));
    float3 v = normalize(Camera.xyz - input.world);
    float NoV = max(dot(n, v), .015);
    float alpha2 = .0012 + missingSlope;
    // Isotropic approximation to unresolved directional slope covariance.
    float3 reflected = reflect(-v, n);
    float envLod=Refinement.x>.5 ? clamp(log2(max(length(fwidth(reflected)),sqrt(alpha2)*.18)*256),0,6) : 0;
    float3 env = lerp(EnvironmentFiltered(reflected,envLod), Environment(float3(0, .35, 1)), saturate(alpha2 * 2));
    float3 color = Fresnel(NoV) * env + Water.rgb * (1 - Fresnel(NoV));

    float3 axisX = normalize(cross(float3(0, 1, 0), Light.xyz));
    float3 axisY = cross(Light.xyz, axisX);
    float3 direct = 0;
    // Deterministic equal-area quadrature of a finite distant disk, shared with sky.
    [unroll] for (int j = 0; j < 8; j++)
    {
        float radius = Light.w * sqrt((j + .5) / 8);
        float angle = j * 2.39996323;
        float3 l = normalize(Light.xyz + radius * (axisX * cos(angle) + axisY * sin(angle)));
        float3 h = normalize(l + v);
        float NoL = saturate(dot(n, l));
        float NoH = saturate(dot(n, h));
        float D = Beckmann(NoH, alpha2);
        float G = SmithG1(NoV, alpha2) * SmithG1(NoL, alpha2);
        float3 profile=1;
        if(Refinement.x>.5 && Grid.z>1.5 && Grid.z<2.5) profile=MoonMaterial(l,Light.w,false);
        direct += D * G * Fresnel(dot(v, h)) / (4 * NoV) * profile * CelestialTransmission(l);
    }
    color += Radiance.rgb * (PI * Light.w * Light.w / 8) * direct;
    float distance = length(input.world - Camera.xyz);
    float fog = 1 - exp(-distance * .0007);
    color = lerp(color, Environment(normalize(input.world - Camera.xyz)), fog);
    return float4(max(color, 0), 1);
}

float3 ToSrgb(float3 value)
{
    return lerp(12.92 * value, 1.055 * pow(max(value, 0), 1 / 2.4) - .055, step(.0031308, value));
}
float4 CompositePS(ScreenVertex input) : SV_Target
{
    float3 color = Scene.SampleLevel(LinearClamp, input.uv, 0).rgb * Radiance.w;
    if(Refinement.x>.5) color += OpticalBloom.SampleLevel(LinearClamp,input.uv,0).rgb * Refinement.w;
    // Luminance compression keeps the golden/silver hue instead of clipping channels.
    float luminance = dot(color, float3(.2126, .7152, .0722));
    color /= 1 + luminance;
    if (SkyTop.w > .5)
    {
        // Compress out-of-gamut chroma toward the mapped luminance, instead of
        // independently clipping the red/green channels of bright warm reflections.
        float mappedLuminance = luminance / (1 + luminance);
        float peak = max(color.r,max(color.g,color.b));
        float compression = saturate((peak-1)/max(peak-mappedLuminance,.00001));
        color = lerp(color,mappedLuminance.xxx,compression);
    }
    return float4(saturate(ToSrgb(color)), 1);
}
