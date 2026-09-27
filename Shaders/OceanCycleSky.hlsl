#include "OceanOptics.hlsl"
cbuffer CycleSkyFrame : register(b0)
{
    float4 SolarDirection;
    float4 SolarEnergy;
    float4 LunarDirection;
    float4 LunarEnergy;
    float4 Dimensions; // width, height, view steps, aerosol
    float4 Ambient; // daylight, unused
};
RWTexture2D<float4> SkyOutput : register(u0);
static const float PI=3.14159265359;
float PhaseM(float mu) { const float g=.76; return (1-g*g)/(4*PI*pow(max(.001,1+g*g-2*g*mu),1.5)); }
[numthreads(8,8,1)]
void BuildCycleSky(uint3 id : SV_DispatchThreadID)
{
    float2 uv=(id.xy+.5)/Dimensions.xy;
    float vertical=1-2*uv.y, angle=sign(vertical)*vertical*vertical*PI*.5;
    float elevation=sin(angle), azimuth=(uv.x-.5)*2*PI;
    float3 d=normalize(float3(sin(azimuth)*cos(angle),max(elevation,.0001),cos(azimuth)*cos(angle)));
    float3 origin=float3(0,6360.0028,0);
    float b=dot(origin,d), distance=-b+sqrt(max(0,b*b-dot(origin,origin)+6460.0*6460.0));
    float3 optical=0, color=0;
    float muS=dot(d,SolarDirection.xyz), muM=dot(d,LunarDirection.xyz);
    float3 rayleigh=float3(.0058,.0135,.0331), ozone=float3(.00065,.001881,.000085);
    float3 phaseS=rayleigh*3*(1+muS*muS)/(16*PI), phaseL=rayleigh*3*(1+muM*muM)/(16*PI);
    float mieS=Dimensions.w*.9*PhaseM(muS), mieL=Dimensions.w*.9*PhaseM(muM);
    [loop] for(int i=0;i<(int)Dimensions.z;i++)
    {
        float a=distance*pow(i/Dimensions.z,2), end=distance*pow((i+1)/Dimensions.z,2), step=end-a;
        float3 p=origin+d*((a+end)*.5);
        float h=max(0,length(p)-6360);
        float3 density=float3(exp(-h/float2(8,1.2)),saturate(1-abs(h-25)/15));
        float3 mid=optical+density*step*.5;
        float3 viewT=exp(-rayleigh*mid.x-Dimensions.w*mid.y-ozone*mid.z);
        float3 solar=SolarEnergy.rgb*OpticalTransmission(p,SolarDirection.xyz,Dimensions.w);
        float3 lunar=LunarEnergy.rgb*OpticalTransmission(p,LunarDirection.xyz,Dimensions.w);
        color+=viewT*(solar*(phaseS*density.x+mieS*density.y)+lunar*(phaseL*density.x+mieL*density.y))*step;
        optical+=density*step;
    }
    // Bounded low-order multiple-scattering fill, explicitly an approximation, continuous through twilight.
    float sunsetFill=smoothstep(-.21,.025,SolarDirection.y);
    color+=SolarEnergy.rgb*float3(.0007,.0012,.0021)*sunsetFill*(.7+.3*d.y);
    color+=float3(.00000004,.000000065,.00000012); // dim night background, before exposure
    color*=lerp(.24,1,smoothstep(-.15,.001,elevation));
    SkyOutput[id.xy]=float4(max(color,0),1);
}
