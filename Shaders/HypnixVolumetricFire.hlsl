cbuffer FrameConstants : register(b0)
{
    float2 Resolution;
    float Time;
    float Bass;
    float Mids;
    float Highs;
    float Intensity;
    float Glow;
    float2 Origin;
    float2 Padding;
};

Texture3D<float4> VolumeState : register(t0); // velocity.xyz, combustion density
RWTexture3D<float4> NextVolume : register(u0);

float hash31(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}

float4 sampleVolume(float3 p, uint3 size)
{
    p = clamp(p, 0.0, float3(size) - 1.001);
    int3 i = int3(floor(p));
    float3 f = frac(p);
    int3 hi = int3(size) - 1;
    float4 c000 = VolumeState.Load(int4(i, 0));
    float4 c100 = VolumeState.Load(int4(min(i + int3(1,0,0), hi), 0));
    float4 c010 = VolumeState.Load(int4(min(i + int3(0,1,0), hi), 0));
    float4 c110 = VolumeState.Load(int4(min(i + int3(1,1,0), hi), 0));
    float4 c001 = VolumeState.Load(int4(min(i + int3(0,0,1), hi), 0));
    float4 c101 = VolumeState.Load(int4(min(i + int3(1,0,1), hi), 0));
    float4 c011 = VolumeState.Load(int4(min(i + int3(0,1,1), hi), 0));
    float4 c111 = VolumeState.Load(int4(min(i + 1, hi), 0));
    return lerp(lerp(lerp(c000,c100,f.x),lerp(c010,c110,f.x),f.y),
                lerp(lerp(c001,c101,f.x),lerp(c011,c111,f.x),f.y),f.z);
}

[numthreads(8, 4, 4)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    uint width, height, depth;
    NextVolume.GetDimensions(width, height, depth);
    if (any(id >= uint3(width,height,depth))) return;
    uint3 size = uint3(width,height,depth);
    float3 cell = float3(id);
    float4 current = VolumeState.Load(int4(id,0));
    float drive = saturate((Bass * 0.70 + Mids * 0.22 + Highs * 0.08) * (0.85 + Intensity * 1.4));

    float rise = 0.34 + current.w * 0.85 + drive * 0.72;
    float3 backtrace = cell + float3(-current.x * 0.62, rise, -current.z * 0.62);
    float4 advected = sampleVolume(backtrace, size);
    float density = advected.w * 0.968;
    float3 velocity = advected.xyz * 0.986;

    float left = VolumeState.Load(int4(max(int(id.x)-1,0),id.y,id.z,0)).w;
    float right = VolumeState.Load(int4(min(id.x+1,width-1),id.y,id.z,0)).w;
    float front = VolumeState.Load(int4(id.x,id.y,max(int(id.z)-1,0),0)).w;
    float back = VolumeState.Load(int4(id.x,id.y,min(id.z+1,depth-1),0)).w;
    float n = hash31(cell * 0.079 + float3(Time*0.61,-Time*0.43,Time*0.37)) - 0.5;
    velocity.x += (front-back) * (0.16 + Mids*0.26) + n * 0.055;
    velocity.z += (right-left) * (0.16 + Mids*0.26) - n * 0.055;
    velocity.y -= density * (0.18 + drive * 0.36);
    velocity = clamp(velocity, -2.6, 2.6);

    float fromBottom = (height-1)-id.y;
    if (fromBottom < 5.0)
    {
        float3 seed = float3(id.x*0.19, floor(Time*20.0), id.z*0.31);
        float randomFuel = hash31(seed);
        float waves = 0.5 + 0.5*sin(id.x*0.16 + sin(id.z*0.25+Time)*1.7);
        float fuel = smoothstep(0.18,0.86,waves*0.62+randomFuel*0.48) * (0.46+drive*0.88);
        density = max(density, fuel);
        velocity.y = min(velocity.y, -fuel*(0.48+Bass*1.15));
        velocity.xz += (float2(hash31(seed+4.2),hash31(seed+8.7))-0.5)*0.28;
    }

    float boundary = smoothstep(0.0,2.0,min(min(id.x,width-1-id.x),min(id.z,depth-1-id.z)));
    NextVolume[id] = float4(velocity, saturate(density)*boundary);
}

struct VSOutput { float4 Position:SV_Position; float2 UV:TEXCOORD0; };
VSOutput VSMain(uint id:SV_VertexID)
{
    VSOutput o; float2 p=float2((id<<1)&2,id&2); o.UV=p;
    o.Position=float4(p*float2(2,-2)+float2(-1,1),0,1); return o;
}

float3 fireColor(float heat)
{
    float3 c=lerp(float3(0.32,0.002,0),float3(1,0.075,0.002),smoothstep(0.04,0.30,heat));
    c=lerp(c,float3(1,0.48,0.025),smoothstep(0.28,0.66,heat));
    return lerp(c,float3(1,0.94,0.70),smoothstep(0.68,1.0,heat));
}

float4 PSMain(VSOutput input):SV_Target
{
    uint width,height,depth; VolumeState.GetDimensions(width,height,depth);
    float2 uv=saturate(input.UV);
    float3 accumulated=0; float transmittance=1.0;
    float jitter=hash31(float3(input.Position.xy,frac(Time)));
    [loop] for(uint z=0;z<depth;z++)
    {
        float slice=frac((z+jitter)/depth);
        float3 pos=float3(uv.x*(width-1),uv.y*(height-1),slice*(depth-1));
        float density=sampleVolume(pos,uint3(width,height,depth)).w;
        if(density>0.008)
        {
            float alpha=1-exp(-density*0.085);
            float3 emission=fireColor(density)*(0.65+density*1.9)*(1+Glow*0.65);
            accumulated+=transmittance*emission*alpha;
            transmittance*=1-alpha;
            if(transmittance<0.025) break;
        }
    }
    accumulated=1-exp(-accumulated*1.6);
    return float4(pow(saturate(accumulated),1/2.2),1);
}
