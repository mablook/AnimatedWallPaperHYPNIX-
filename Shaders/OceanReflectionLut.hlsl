// Beckmann/Smith directional-albedo integral. Match Ocean.hlsl's alpha^2,
// masking approximation and Schlick Fresnel; a GGX DFG fit is not interchangeable.
// Mapping: NoV=(x/127)^2 and alpha^2=(y/127)^2, including both endpoints.
// RG stores A/B such that E = F0*A + (1-F0)*B for a unit white environment.
// Sampling coordinates: (sqrt(saturate(float2(NoV,alphaSquared)))*127+.5)/128.
RWTexture2D<float2> ReflectionIntegral : register(u0);
static const float ReflectionPi = 3.141592653589793;
static const uint ReflectionSamples = 1024u;

float ReflectionSmithG1(float cosine, float alphaSquared)
{
    if (cosine <= 0) return 0;
    float a = cosine / sqrt(max(alphaSquared * (1 - cosine * cosine), 1e-20));
    if (a >= 1.6) return 1;
    return (3.535 * a + 2.181 * a * a) / (1 + 2.276 * a + 2.577 * a * a);
}

float ReflectionSmithG1OverCosine(float cosine, float alphaSquared)
{
    // The finite grazing limit is 3.535/sqrt(alphaSquared), not a clamped NoV.
    float slope = sqrt(max(alphaSquared * (1 - cosine * cosine), 1e-20));
    float a = cosine / slope;
    if (a >= 1.6) return 1 / cosine;
    return (3.535 + 2.181 * a) / (slope * (1 + 2.276 * a + 2.577 * a * a));
}

[numthreads(8, 8, 1)]
void BuildReflectionLut(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= 128u || id.y >= 128u) return;
    float2 grid = id.xy / 127.0;
    float NoV = grid.x * grid.x, alphaSquared = grid.y * grid.y;
    if (id.y == 0u)
    {
        // The exact smooth-surface limit avoids the singular delta NDF.
        ReflectionIntegral[id.xy] = float2(1, pow(1 - NoV, 5));
        return;
    }
    float3 view = float3(sqrt(max(0, 1 - NoV * NoV)), 0, NoV);
    float visibilityOverView = ReflectionSmithG1OverCosine(NoV, alphaSquared);
    float2 integral = 0;
    [loop] for (uint sampleIndex = 0u; sampleIndex < ReflectionSamples; sampleIndex++)
    {
        // Hammersley samples the projected NDF: p(h)=D(h)*NoH. Midpoint radii
        // avoid both the central special sample and the logarithmic endpoint.
        float u = (sampleIndex + .5) / ReflectionSamples;
        float azimuth = 2 * ReflectionPi * (reversebits(sampleIndex) * 2.3283064365386963e-10);
        float tangentSquared = -alphaSquared * log(1 - u);
        float NoH = rsqrt(1 + tangentSquared);
        float sine = sqrt(max(0, 1 - NoH * NoH));
        float sinAzimuth, cosAzimuth;
        sincos(azimuth, sinAzimuth, cosAzimuth);
        float3 halfVector = float3(sine * cosAzimuth, sine * sinAzimuth, NoH);
        float VoH = dot(view, halfVector);
        float NoL = 2 * VoH * NoH - NoV;
        if (VoH <= 0 || NoL <= 0) continue;

        // p(l)=D(h)*NoH/(4*VoH); f*cos(theta_l)/p(l)
        // = F*G1(v)*G1(l)*VoH/(NoV*NoH). D cancels exactly.
        float weight = visibilityOverView * ReflectionSmithG1(saturate(NoL), alphaSquared) * VoH / NoH;
        integral += weight * float2(1, pow(1 - saturate(VoH), 5));
    }
    integral /= ReflectionSamples;
    // Single-scattering reflectance must be bounded. This only bounds numerical
    // quadrature/Smith-fit overshoot; it adds no multiple-scattering compensation.
    integral.x = saturate(integral.x);
    integral.y = clamp(integral.y, 0, integral.x);
    ReflectionIntegral[id.xy] = integral;
}
