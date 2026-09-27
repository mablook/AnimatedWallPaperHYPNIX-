#include "OceanOptics.hlsl"
// Distant cloud shell, kilometres. Volume radiance + Beer-Lambert transmittance.
// The angular cache deliberately omits positional parallax and local water shadows.
cbuffer CloudFrame : register(b0)
{
    float4 Sun;
    float4 DirectEnergy;
    float4 Budget;
    float4 Wind;
    float4 AmbientEnergy;
    float4 MoonDirection;
    float4 MoonIrradiance;
    float4 CycleAtmosphere;
};
RWTexture2D<float4> CloudOutput : register(u0);
static const float PI = 3.14159265359;
float Hash(float3 p)
{
    uint3 q = (uint3)((int3)p & 255);
    uint n = q.x * 1597334677u ^ q.y * 3812015801u ^ q.z * 2798796415u;
    n ^= n >> 16; n *= 2246822519u; n ^= n >> 13;
    return (n & 0x00ffffffu) / 16777216.0;
}
float Noise(float3 p)
{
    float3 i = floor(p), f = frac(p); f = f*f*(3-2*f);
    return lerp(lerp(lerp(Hash(i),Hash(i+float3(1,0,0)),f.x),
                     lerp(Hash(i+float3(0,1,0)),Hash(i+float3(1,1,0)),f.x),f.y),
                lerp(lerp(Hash(i+float3(0,0,1)),Hash(i+float3(1,0,1)),f.x),
                     lerp(Hash(i+float3(0,1,1)),Hash(i+1),f.x),f.y),f.z);
}
float Density(float3 p)
{
    float altitude = p.y + dot(p.xz,p.xz) / (2*6360.0);
    float h = (altitude-1.2)/2.0;
    if (h <= 0 || h >= 1) return 0;
    float3 q = float3(p.x + Wind.x, altitude, p.z + Wind.y);
    float weather = Noise(float3(q.x*.125, 3.7, q.z*.125));
    float coverage = smoothstep(.70-Sun.w*.42, .88-Sun.w*.40, weather);
    float shape = .62*Noise(q*.8) + .26*Noise(q*1.6+17) + .12*Noise(q*3.2+37);
    float profile = smoothstep(0,.12,h) * (1-smoothstep(.48,1,h));
    float base = saturate((shape + coverage*.55 - .60) * 3.0) * profile;
    float erosion = Noise(q*6.4+53)*.13;
    return max(0, base-erosion*(1-base)) * 1.6;
}
float ShellDistance(float elevation, float height)
{
    float r=6360.002;
    return -r*elevation + sqrt(max(0,r*r*elevation*elevation+(6360+height)*(6360+height)-r*r));
}
[numthreads(8,8,1)]
void BuildClouds(uint3 id : SV_DispatchThreadID)
{
    float2 uv=(id.xy+.5)/Budget.xy;
    float vertical=1-2*uv.y;
    float elevation=sin(sign(vertical)*vertical*vertical*PI*.5), azimuth=(uv.x-.5)*2*PI;
    if(elevation<0) { CloudOutput[id.xy]=float4(0,0,0,1); return; }
    float3 d=float3(sin(azimuth)*sqrt(1-elevation*elevation),elevation,cos(azimuth)*sqrt(1-elevation*elevation));
    float near=ShellDistance(elevation,1.2), far=ShellDistance(elevation,3.2);
    float step=(far-near)/Budget.z;
    float T=1; float3 light=0;
    float mu=dot(d,Sun.xyz);
    float g=.65;
    float phase=(1-g*g)/pow(max(.03,1+g*g-2*g*mu),1.5);
    float jitter=.25+.5*Hash(float3(id.xy,71)); // Fixed angular jitter breaks coherent march bands.
    [loop] for(int i=0;i<(int)Budget.z;i++)
    {
        float distance=near+(i+jitter)*step;
        float3 p=d*distance;
        float density=Density(p);
        if(density>.001)
        {
            float shadow=0;
            [loop] for(int s=0;s<(int)Budget.w;s++)
            {
                float a=.12*pow(2.0,s), b=.12*pow(2.0,s+1);
                shadow+=Density(p+Sun.xyz*((a+b)*.5))*(b-a);
            }
            float height=saturate((p.y+dot(p.xz,p.xz)/(2*6360.0)-1.2)/2);
            float3 ambient=AmbientEnergy.rgb * (.015+.040*height) * lerp(float3(.70,.82,1),float3(1,1,1),Wind.z);
            float3 sunlight=DirectEnergy.rgb * (.055+.07*phase) * (exp(-shadow*2.4)+.18*exp(-shadow*.4));
            if(CycleAtmosphere.x>.5)
            {
                float3 position=p+float3(0,6360.0028,0);
                float3 sunT=OpticalTransmission(position,Sun.xyz,CycleAtmosphere.y);
                float3 moonT=OpticalTransmission(position,MoonDirection.xyz,CycleAtmosphere.y);
                float moonShadow=0;
                if(dot(moonT,MoonIrradiance.rgb)>.00000001)
                {
                    [loop] for(int s=0;s<(int)Budget.w;s++)
                    {
                        float a=.12*pow(2.0,s), b=.12*pow(2.0,s+1);
                        moonShadow+=Density(p+MoonDirection.xyz*((a+b)*.5))*(b-a);
                    }
                }
                float moonMu=dot(d,MoonDirection.xyz);
                float moonPhase=(1-g*g)/pow(max(.03,1+g*g-2*g*moonMu),1.5);
                sunlight*=sunT;
                sunlight+=MoonIrradiance.rgb*moonT*(.055+.07*moonPhase)*(exp(-moonShadow*2.4)+.18*exp(-moonShadow*.4));
                ambient=(AmbientEnergy.rgb*sunT+MoonIrradiance.rgb*moonT)*(.012+.035*height)*float3(.70,.82,1);
                ambient+=AmbientEnergy.rgb*float3(.00015,.00025,.00045)*smoothstep(-.21,.025,Sun.y);
            }
            float extinction=exp(-density*step*2.4);
            float haze=exp(-distance*.04);
            light+=T*(1-extinction)*(ambient+sunlight)*haze;
            T*=extinction;
            if(T<.005) break;
        }
    }
    // Distant haze reduces silhouette contrast without creating an emissive cloud edge.
    float haze=exp(-near*.04);
    CloudOutput[id.xy]=float4(max(light,0),lerp(1,T,haze));
}
