// Original single-scattering RGB atmosphere, kilometre units. Cached per light preset.
// Deliberately excludes the celestial disk; alpha is cloud transmission.
cbuffer SkyFrame : register(b0)
{
    float4 Sun;       // direction, cloud coverage
    float4 Energy;    // pre-exposed irradiance, night flag
    float4 Options;  // overcast flag, unused
};
RWTexture2D<float4> SkyOutput : register(u0);
static const float PI = 3.14159265359;
static const float R = 6360, TOP = 6460;
static const float3 BR = float3(.0058,.0135,.0331);
static const float BM = .0044;
static const float3 BO = float3(.00065,.001881,.000085);

float ExitDistance(float3 p, float3 d)
{
    float b = dot(p,d);
    return -b + sqrt(max(0,b*b-dot(p,p)+TOP*TOP));
}
float3 Density(float3 p)
{
    float height = max(0, length(p)-R);
    return float3(exp(-height / float2(8,1.2)),saturate(1-abs(height-25)/15));
}
float3 SunTransmission(float3 p)
{
    float distance = ExitDistance(p,Sun.xyz);
    float3 depth = 0;
    [unroll] for(int i=0;i<12;i++)
    {
        float a = distance * pow(i/12.0,2), b = distance * pow((i+1)/12.0,2);
        depth += Density(p + Sun.xyz * ((a+b)*.5)) * (b-a);
    }
    return exp(-BR*depth.x-BM*depth.y-BO*depth.z);
}
float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
float Noise(float2 p)
{
    float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
    return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
}
float Clouds(float3 d)
{
    float2 p = d.xz / (.18+max(d.y,0)) * float2(2.0,5.5) + float2(18.7,3.2);
    float n=0, weight=.51;
    [unroll] for(int i=0;i<7;i++) { n+=Noise(p)*weight; p=mul(float2x2(1.72,-.87,.87,1.72),p)+7.3; weight*=.49; }
    return smoothstep(.64-Sun.w*.25,.98-Sun.w*.25,n);
}
[numthreads(8,8,1)]
void BuildSky(uint3 id : SV_DispatchThreadID)
{
    float2 uv=(id.xy+.5)/float2(2048,512);
    float vertical=1-2*uv.y;
    float elevation = sin(sign(vertical)*vertical*vertical*PI*.5), azimuth=(uv.x-.5)*2*PI;
    // Lower hemisphere is a dim continuation of the horizon, not a bright ground plane.
    float3 d=normalize(float3(sin(azimuth)*sqrt(1-elevation*elevation),max(elevation,.001),cos(azimuth)*sqrt(1-elevation*elevation)));
    float3 origin=float3(0,R+.002,0);
    float distance=ExitDistance(origin,d);
    float3 depth=0;
    float3 sumR=0,sumM=0;
    [loop] for(int i=0;i<32;i++)
    {
        float a=distance*pow(i/32.0,2), b=distance*pow((i+1)/32.0,2), step=b-a;
        float3 p=origin+d*((a+b)*.5);
        float3 density=Density(p), midpointDepth=depth+density*step*.5;
        float3 transmission=exp(-BR*midpointDepth.x-BM*midpointDepth.y-BO*midpointDepth.z)*SunTransmission(p);
        sumR+=transmission*density.x*step; sumM+=transmission*density.y*step;
        depth+=density*step;
    }
    float mu=dot(d,Sun.xyz), g=.76;
    float phaseR=3*(1+mu*mu)/(16*PI);
    float phaseM=(1-g*g)/(4*PI*pow(max(.001,1+g*g-2*g*mu),1.5));
    float3 sky=Energy.rgb*(BR*sumR*phaseR+BM*.9*sumM*phaseM);
    // Small artistic ambient term stands in for omitted multiple scattering/airglow.
    sky+=lerp(float3(.008,.014,.024),float3(.001,.0016,.0028),Energy.w)*(1-.6*d.y);
    if (Options.y>.5)
    {
        sky*=lerp(.24,1,smoothstep(-.15,.001,elevation));
        SkyOutput[id.xy]=float4(max(sky,0),1);
        return;
    }
    float density=Clouds(d);
    float transmission=exp(-density*4);
    float3 cloudAmbient=Energy.rgb*.023 + sky*.35;
    float3 cloudLight=Energy.rgb*SunTransmission(origin+float3(0,2,0))*(.035+.16*pow(saturate(mu),8));
    sky=sky*transmission+(cloudAmbient+cloudLight)*(1-transmission);
    if(Options.x>.5)
    {
        float luminance=dot(Energy.rgb,float3(.2126,.7152,.0722));
        sky=lerp(float3(.24,.27,.31),float3(.095,.11,.13),d.y)*luminance*(.8+.2*density);
        transmission=0;
    }
    sky*=lerp(.24,1,smoothstep(-.15,.001,elevation));
    SkyOutput[id.xy]=float4(max(sky,0),transmission);
}
