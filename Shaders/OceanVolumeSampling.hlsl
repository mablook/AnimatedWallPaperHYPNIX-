Texture2D<float4> VolumeEnvironmentPrevious : register(t18);
Texture2D<float4> VolumeEnvironmentNext : register(t19);
Texture3D<float4> VolumeScatterPrevious : register(t20);
Texture3D<float4> VolumeScatterNext : register(t21);
Texture3D<float4> VolumeTransmitPrevious : register(t22);
Texture3D<float4> VolumeTransmitNext : register(t23);
Texture3D<float2> VolumeLightPrevious : register(t24);
Texture3D<float2> VolumeLightNext : register(t25);
Texture2DArray<float4> VolumeReflectionPrevious : register(t26);
Texture2DArray<float4> VolumeReflectionNext : register(t27);
// Parameters are part of OceanFrame, not the compute-only VolumeFrame.
float4 VolumeEnvironment(float3 direction,float lod)
{
    float2 uv=SkyUv(direction);
    return lerp(VolumeEnvironmentPrevious.SampleLevel(SkySampler,uv,lod),VolumeEnvironmentNext.SampleLevel(SkySampler,uv,lod),VolumeState.y);
}
float2 VolumeLightAt(float3 world)
{
    float3 p=world*.001;
    float height=max(0,p.y+dot(p.xz,p.xz)/(2*6360));
    float3 uv=float3(p.xz/(2*VolumeGrid.y)+.5,sqrt(saturate(height/VolumeGrid.z)));
    float3 size=float3(VolumeGrid.w,VolumeGrid.w,16);
    uv=(uv*(size-1)+.5)/size;
    return lerp(VolumeLightPrevious.SampleLevel(LinearClamp,uv,0),VolumeLightNext.SampleLevel(LinearClamp,uv,0),VolumeState.y);
}
float3 VolumeIncident(float3 direction,float3 world,float lod)
{
    float2 position=float2(clamp(world.x/2000+1,0,2),clamp(world.z<2000 ? world.z/2000 : 1+(world.z-2000)/6000,0,2));
    float2 cell=min(floor(position),1), f=position-cell;
    float2 uv=SkyUv(direction); float3 result=0;
    uint width,height,layers; VolumeReflectionPrevious.GetDimensions(width,height,layers);
    float probeLod=max(0,lod+log2(width/2048.0));
    [unroll] for(int y=0;y<2;y++) [unroll] for(int x=0;x<2;x++)
    {
        float index=cell.x+x+3*(cell.y+y);
        float weight=(x==0 ? 1-f.x : f.x)*(y==0 ? 1-f.y : f.y);
        float3 location=float3(uv,index);
        // Probe maps have lower angular resolution than the old 2048-wide cloud field.
        result+=weight*lerp(VolumeReflectionPrevious.SampleLevel(SkySampler,location,probeLod).rgb,
            VolumeReflectionNext.SampleLevel(SkySampler,location,probeLod).rgb,VolumeState.y);
    }
    return result;
}
float3 VolumeAerial(float3 color,float3 world)
{
    float3 delta=world-Camera.xyz; float distance=length(delta)*.001;
    float2 angle=SkyUv(normalize(delta));
    uint w,h,depth; VolumeScatterPrevious.GetDimensions(w,h,depth);
    float3 size=float3(w,h,depth);
    float slice=sqrt(saturate(distance/VolumeGrid.x))*(depth-1);
    float low=min(floor(slice),depth-2);
    float nearDistance=VolumeGrid.x*pow(low/(depth-1),2);
    float farDistance=VolumeGrid.x*pow((low+1)/(depth-1),2);
    // Interpolate physical distance within the quadratic grid, especially near z=0.
    float fraction=saturate((distance-nearDistance)/max(farDistance-nearDistance,.000001));
    float3 uv=float3((angle*(size.xy-1)+.5)/size.xy,(low+fraction+.5)/depth);
    float3 S=lerp(VolumeScatterPrevious.SampleLevel(LinearClamp,uv,0).rgb,VolumeScatterNext.SampleLevel(LinearClamp,uv,0).rgb,VolumeState.y);
    float3 T=lerp(VolumeTransmitPrevious.SampleLevel(LinearClamp,uv,0).rgb,VolumeTransmitNext.SampleLevel(LinearClamp,uv,0).rgb,VolumeState.y);
    return S+T*color;
}
