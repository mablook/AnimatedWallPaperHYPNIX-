#include "OceanOptics.hlsl"
// All distances in this include are kilometres; linear radiance before exposure.
cbuffer VolumeFrame : register(b1)
{
    float4 VSun;
    float4 VSunEnergy; // w: aerosol extinction per km at sea level
    float4 VMoon;
    float4 VMoonEnergy;
    float4 VLayer; // base, top, coverage, type
    float4 VFog; // extra extinction/km, scale height, bank amount, cloud extinction/km
    float4 VWind; // x/z offset in km, seed offset, unused
    float4 VCamera; // km, daylight
    float4 VBudget; // sky width, sky height, cloud steps, light steps
    float4 VGrid; // air steps, fog steps, AP slices, shadow volume horizontal size
    float4 VConfig; // AP extent, shadow half extent, shadow height, AP substeps
    float4 VReserved;
};
Texture3D<float2> VolumeNoise : register(t23);
SamplerState VolumeWrap : register(s4);
Texture3D<float2> LightField : register(t24);
SamplerState VolumeClamp : register(s5);
static const float VP=3.14159265359;
static const float VR=6360.0;

float VHeight(float3 p)
{
    // Rationalized radius difference retains metre-scale height in FP32.
    float difference=dot(p,p)+2*VR*p.y;
    return max(0,difference/(sqrt(VR*VR+difference)+VR));
}
float VPhase(float mu,float g)
{
    return (1-g*g)/(4*VP*pow(max(.001,1+g*g-2*g*mu),1.5));
}
float3 VWeight(float3 sigma,float ds)
{
    float3 x=sigma*ds;
    float3 stable=ds*(1-x*.5+x*x/6-x*x*x/24+x*x*x*x/120);
    return float3(x.x<.05 ? stable.x : (1-exp(-x.x))/sigma.x,
                  x.y<.05 ? stable.y : (1-exp(-x.y))/sigma.y,
                  x.z<.05 ? stable.z : (1-exp(-x.z))/sigma.z);
}
float2 VN(float3 p,float lod) { return VolumeNoise.SampleLevel(VolumeWrap,p/16,lod); }
float3 VCoordinates(float3 p)
{
    return float3(p.x+VWind.x+VWind.z,VHeight(p),p.z+VWind.y+VWind.z*.713);
}
float VCloud(float3 p,float lod)
{
    float h=(VHeight(p)-VLayer.x)/(VLayer.y-VLayer.x);
    if(h<=0 || h>=1 || VLayer.z<=0) return 0;
    float3 q=VCoordinates(p);
    q.xz+=float2(VN(q*3+19,lod).x-.5,VN(q*3+43,lod).x-.5)*(.16+.16*h);
    float weather=VN(float3(q.x*.22,11.3,q.z*.22),0).x;
    float coverage=smoothstep(1-VLayer.z-.17,1-VLayer.z+.13,weather);
    float2 low=VN(q*1.15, max(0,lod-1));
    float shape=.40*low.x+.36*(1-low.y)+.16*(1-VN(q*2.3+17,lod).y)+.08*VN(q*4.6+37,lod).x;
    float profile;
    if(VLayer.w<.5) profile=smoothstep(0,.09,h)*(1-smoothstep(.40,1,h));
    else if(VLayer.w<1.5) profile=smoothstep(0,.06,h)*(1-smoothstep(.58,1,h));
    else { profile=smoothstep(0,.16,h)*(1-smoothstep(.75,1,h)); shape=lerp(shape,.76,.72); }
    float base=saturate((shape+coverage*.56-.66)*3.4)*coverage*profile;
    float erosion=VN(q*7.5+37,lod).y*.22;
    return max(0,base-erosion*(1-base)) * VFog.w;
}
float VFogDensity(float3 p)
{
    float h=VHeight(p);
    float profile=exp(-h/max(VFog.y,.001))*(1-smoothstep(VFog.y*3,VFog.y*5,h));
    float3 q=VCoordinates(p);
    float bank=smoothstep(.28,.70,VN(float3(q.x*.7,2.1,q.z*.7),1).x);
    return VFog.x*profile*lerp(1,.06+1.5*bank,VFog.z);
}
float VExit(float3 p,float3 d,float altitude)
{
    float b=dot(p,d)+VR*d.y;
    float c=dot(p,p)+2*VR*p.y-altitude*(2*VR+altitude);
    float root=sqrt(max(0,b*b-c));
    return max(0,b>=0 ? -c/max(b+root,.000001) : -b+root);
}
float VLightTau(float3 p,float3 d)
{
    // Full shell extent with a distribution concentrating samples near the entry.
    float height=VHeight(p), tau=0;
    float entry=height<VLayer.x ? VExit(p,d,VLayer.x) : 0;
    float end=VExit(p,d,VLayer.y);
    if(height>=VLayer.x)
    {
        float b=dot(p,d)+VR*d.y;
        float disc=b*b-(dot(p,p)+2*VR*p.y-VLayer.x*(2*VR+VLayer.x));
        if(b<0 && disc>0) end=min(end,max(0,-b-sqrt(disc)));
    }
    if(height<VLayer.y && VLayer.z>0 && end>entry)
    {
        [loop] for(int k=0;k<(int)VBudget.w;k++)
        {
            float a=pow(k/VBudget.w,1.6), b=pow((k+1)/VBudget.w,1.6);
            float ds=(end-entry)*(b-a);
            float t=entry+(end-entry)*(a+b)*.5;
            tau+=VCloud(p+d*t,clamp(log2(max(ds,.03)*4),0,3))*ds;
        }
    }
    float fogEnd=VExit(p,d,VFog.y*5);
    if(height<VFog.y*5 && VFog.x>0)
    {
        [unroll] for(int k=0;k<8;k++)
        {
            float a=pow(k/8.0,2), b=pow((k+1)/8.0,2);
            tau+=VFogDensity(p+d*(fogEnd*(a+b)*.5))*fogEnd*(b-a);
        }
    }
    return min(60,max(tau,0));
}
float2 VLocalLight(float3 p)
{
    float3 uv=float3(p.xz/(2*VConfig.y)+.5,sqrt(saturate(VHeight(p)/VConfig.z)));
    float3 size=float3(VGrid.w,VGrid.w,16);
    uv=(uv*(size-1)+.5)/size;
    return LightField.SampleLevel(VolumeClamp,uv,0);
}
void VMedium(float3 p,out float3 extinction,out float rayleigh,out float mie,out float fog,out float cloud,float lod)
{
    float h=VHeight(p);
    rayleigh=exp(-h/8); mie=VSunEnergy.w*exp(-h/1.2);
    fog=VFogDensity(p); cloud=VCloud(p,lod);
    float ozone=saturate(1-abs(h-25)/15);
    extinction=float3(.0058,.0135,.0331)*rayleigh+mie+float3(.00065,.001881,.000085)*ozone+fog+cloud;
}
float3 VSource(float3 p,float3 d,float rayleigh,float mie,float fog,float cloud)
{
    float3 planetPosition=p+float3(0,VR,0);
    float3 solar=VSunEnergy.rgb*OpticalTransmission(planetPosition,VSun.xyz,VSunEnergy.w);
    float3 lunar=VMoonEnergy.rgb*OpticalTransmission(planetPosition,VMoon.xyz,VSunEnergy.w);
    float2 transmission=1;
    if(VHeight(p)<VConfig.z && max(abs(p.x),abs(p.z))<VConfig.y)
        transmission=VLocalLight(p);
    else if(cloud>.0001)
    {
        transmission.x=exp(-VLightTau(p,VSun.xyz));
        transmission.y=dot(lunar,1)>.000000001 ? exp(-VLightTau(p,VMoon.xyz)) : 1;
    }
    else if(VHeight(p)<VLayer.x) transmission=VLocalLight(p);
    float muS=dot(d,VSun.xyz), muM=dot(d,VMoon.xyz);
    float3 scatteringS=float3(.0058,.0135,.0331)*rayleigh*3*(1+muS*muS)/(16*VP)+mie*.9*VPhase(muS,.76)+fog*.995*VPhase(muS,.78);
    float3 scatteringM=float3(.0058,.0135,.0331)*rayleigh*3*(1+muM*muM)/(16*VP)+mie*.9*VPhase(muM,.76)+fog*.995*VPhase(muM,.78);
    float cloudS=.85*VPhase(muS,.65)+.15*VPhase(muS,-.2);
    float cloudM=.85*VPhase(muM,.65)+.15*VPhase(muM,-.2);
    float3 direct=solar*transmission.x*(scatteringS+cloud*.99*cloudS)+lunar*transmission.y*(scatteringM+cloud*.99*cloudM);
    // Bounded diffuse approximation; no feedback from the displayed image/history.
    float2 multi=.075*(1-transmission)*pow(max(transmission,.000001),.15);
    float3 diffuse=solar*multi.x+lunar*multi.y;
    float twilight=smoothstep(-.21,.025,VSun.y);
    // The same low-frequency twilight field used at the far sky boundary also
    // illuminates droplets. Otherwise twilight produces unlit black silhouettes.
    float3 ambient=(solar+lunar)*float3(.006,.009,.015)+
        .6*(VSunEnergy.rgb*float3(.0007,.0012,.0021)*twilight+float3(4e-8,6.5e-8,12e-8));
    return direct+cloud*.99*(diffuse+ambient)+fog*.995*ambient;
}
void VAccumulate(float3 sigma,float3 source,float ds,inout float3 S,inout float3 T)
{
    S+=T*source*VWeight(sigma,ds);
    T*=exp(-sigma*ds);
}
void VIntegrate(float3 origin,float3 d,float start,float end,int steps,inout float3 S,inout float3 T,inout float extraTau)
{
    if(end<=start) return;
    [loop] for(int i=0;i<steps;i++)
    {
        if(max(T.r,max(T.g,T.b))<.000001) break;
        // Resolve the dense near part of grazing rays. Fixed quadrature avoids
        // screen-space grain and remains identical after pause, seek or recreation.
        float a=pow(i/(float)steps,1.5), b=pow((i+1)/(float)steps,1.5);
        float ds=(end-start)*(b-a);
        float3 p=origin+d*(start+(a+b)*.5*(end-start));
        float3 sigma; float rayleigh,mie,fog,cloud;
        VMedium(p,sigma,rayleigh,mie,fog,cloud,clamp(log2(max(ds,.03)*4),0,3));
        VAccumulate(sigma,VSource(p,d,rayleigh,mie,fog,cloud),ds,S,T);
        extraTau+=(fog+cloud)*ds;
    }
}
