#include "OceanVolumeCommon.hlsl"
RWTexture2D<float4> SkyVolumeOutput : register(u0);
RWTexture3D<float4> AerialScatterOutput : register(u1);
RWTexture3D<float4> AerialTransmitOutput : register(u2);
RWTexture3D<float2> LightVolumeOutput : register(u3);
RWTexture3D<float2> NoiseOutput : register(u4);
RWTexture2DArray<float4> ReflectionOutput : register(u5);

// Native test-only entry: sample the actual cloud/fog fields, without light,
// projection, exposure or cached image interpolation hiding density changes.
cbuffer DensityProbe : register(b2) { float4 ProbeArea; }; // origin x/z, span km, size
[numthreads(8,8,1)]
void CloudDensityReference(uint3 id:SV_DispatchThreadID)
{
    if(id.x>=ProbeArea.w || id.y>=ProbeArea.w) return;
    float2 xz=ProbeArea.xy+(id.xy/(ProbeArea.w-1)-.5)*ProbeArea.z;
    float density=0, peak=0;
    [unroll] for(int k=0;k<8;k++)
    {
        float3 p=float3(xz.x,lerp(VLayer.x,VLayer.y,(k+.5)/8),xz.y);
        // Align samples with the spherical height convention used in production.
        p.y-=dot(xz,xz)/(2*VR);
        float d=VCloud(p,0); density+=d/8; peak=max(peak,d);
    }
    float3 fogPosition=float3(xz.x,.04-dot(xz,xz)/(2*VR),xz.y);
    SkyVolumeOutput[id.xy]=float4(density,VFogDensity(fogPosition),peak,1);
}

// Native validation executes the production transport step against a double-precision oracle.
[numthreads(8,1,1)]
void TransportReference(uint3 id:SV_DispatchThreadID)
{
    if(id.x>=13) return;
    float cases[6]={0,1e-7,.01,.1,1,10};
    float3 sigma=cases[id.x%6]*float3(1,2,.5),S=0,T=1;
    int steps=id.x<6 ? 1 : 17;
    float3 source=id.x==12 ? 0 : float3(.3,.8,1.2);
    [loop] for(int i=0;i<steps;i++) VAccumulate(sigma,source,7.3/steps,S,T);
    SkyVolumeOutput[uint2(id.x,0)]=float4(S,1);
    SkyVolumeOutput[uint2(id.x,1)]=float4(T,1);
}

uint VHash(uint3 p)
{
    p&=63;
    uint n=p.x*1597334677u ^ p.y*3812015801u ^ p.z*2798796415u;
    n^=n>>16; n*=2246822519u; n^=n>>13; return n;
}
float VRandom(uint3 p) { return (VHash(p)&0xffffff)/16777216.0; }
[numthreads(4,4,4)]
void BuildNoise(uint3 id:SV_DispatchThreadID)
{
    // Own periodic value + cellular field; no downloaded noise assets.
    float3 p=(id+.5)*.25, cell=floor(p), f=frac(p);
    float3 smooth=f*f*(3-2*f); float value=0, nearest=10;
    [unroll] for(int z=-1;z<=1;z++) [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++)
    {
        int3 c=(int3)cell+int3(x,y,z);
        // Cell period is 16, so the sampled 64-texel texture tiles continuously.
        uint3 wrapped=(uint3)(c&15);
        float3 feature=float3(VRandom(wrapped),VRandom(wrapped+17),VRandom(wrapped+31));
        nearest=min(nearest,length(float3(x,y,z)+feature-f));
    }
    // Make value noise share the same period too.
    value=0;
    [unroll] for(int z=0;z<2;z++) [unroll] for(int y=0;y<2;y++) [unroll] for(int x=0;x<2;x++)
    {
        float3 weight=lerp(1-smooth,smooth,float3(x,y,z));
        value+=VRandom((uint3)(((int3)cell+int3(x,y,z))&15))*weight.x*weight.y*weight.z;
    }
    NoiseOutput[id]=float2(value,saturate(nearest));
}
float3 VDirection(float2 uv)
{
    float v=1-2*uv.y, angle=sign(v)*v*v*VP*.5;
    float az=(uv.x-.5)*2*VP;
    return float3(sin(az)*cos(angle),sin(angle),cos(az)*cos(angle));
}
[numthreads(8,8,1)]
void BuildLight(uint3 id:SV_DispatchThreadID)
{
    float3 uv=id/float3(VGrid.w-1,VGrid.w-1,15);
    float3 p=float3((uv.x*2-1)*VConfig.y,uv.z*uv.z*VConfig.z,(uv.y*2-1)*VConfig.y);
    p.y-=dot(p.xz,p.xz)/(2*VR);
    LightVolumeOutput[id]=exp(-float2(VLightTau(p,VSun.xyz),VLightTau(p,VMoon.xyz)));
}
float4 VEnvironment(float3 origin,float3 d)
{
    float below=d.y; d=normalize(float3(d.x,max(d.y,.0001),d.z));
    float3 S=0,T=1; float tau=0;
    float fogEnd=VExit(origin,d,VFog.y*5);
    float cloudStart=VExit(origin,d,VLayer.x), cloudEnd=VExit(origin,d,VLayer.y);
    // Split at cloud entry; any fog/cloud overlap is integrated jointly there.
    fogEnd=min(fogEnd,cloudStart);
    VIntegrate(origin,d,0,fogEnd,(int)VGrid.y,S,T,tau);
    VIntegrate(origin,d,fogEnd,cloudStart,max(4,(int)VGrid.x/2),S,T,tau);
    VIntegrate(origin,d,cloudStart,cloudEnd,(int)VBudget.z,S,T,tau);
    float top=VExit(origin,d,100);
    // Quadratic intervals resolve lower atmosphere without adding many far samples.
    [loop] for(int i=0;i<(int)VGrid.x;i++)
    {
        float a=cloudEnd+(top-cloudEnd)*pow(i/VGrid.x,2);
        float b=cloudEnd+(top-cloudEnd)*pow((i+1)/VGrid.x,2);
        VIntegrate(origin,d,a,b,1,S,T,tau);
    }
    // Approximate higher-order atmospheric background; attenuated by foreground media.
    float twilight=smoothstep(-.21,.025,VSun.y);
    S+=exp(-min(tau,60))*(VSunEnergy.rgb*float3(.0007,.0012,.0021)*twilight*(.7+.3*d.y)+float3(4e-8,6.5e-8,12e-8));
    S*=lerp(.24,1,smoothstep(-.15,.001,below));
    return float4(max(S,0),below<0 ? 0 : exp(-min(tau,60)));
}
[numthreads(8,8,1)]
void BuildEnvironment(uint3 id:SV_DispatchThreadID)
{
    if(id.x>=VBudget.x || id.y>=VBudget.y) return;
    SkyVolumeOutput[id.xy]=VEnvironment(VCamera.xyz,VDirection((id.xy+.5)/VBudget.xy));
}
[numthreads(8,8,1)]
void BuildReflections(uint3 id:SV_DispatchThreadID)
{
    uint w,h,depth; ReflectionOutput.GetDimensions(w,h,depth);
    if(id.x>=w || id.y>=h) return;
    uint x=id.z%3, z=id.z/3;
    float3 origin=float3((int(x)-1)*2,.0005,z==2 ? 8 : z*2);
    ReflectionOutput[id]=VEnvironment(origin,VDirection((id.xy+.5)/float2(w,h)));
}
[numthreads(8,8,1)]
void BuildAerial(uint3 id:SV_DispatchThreadID)
{
    uint w,h,depth; AerialScatterOutput.GetDimensions(w,h,depth);
    if(id.x>=w || id.y>=h) return;
    float3 d=VDirection(float2(id.xy)/float2(w-1,h-1));
    float3 S=0,T=1; float tau=0;
    AerialScatterOutput[uint3(id.xy,0)]=0;
    AerialTransmitOutput[uint3(id.xy,0)]=1;
    [loop] for(uint slice=1;slice<depth;slice++)
    {
        float start=VConfig.x*pow((slice-1.0)/(depth-1),2);
        float end=VConfig.x*pow(slice/(float)(depth-1),2);
        VIntegrate(VCamera.xyz,d,start,end,(int)VConfig.w,S,T,tau);
        AerialScatterOutput[uint3(id.xy,slice)]=float4(S,1);
        AerialTransmitOutput[uint3(id.xy,slice)]=float4(T,1);
    }
}
