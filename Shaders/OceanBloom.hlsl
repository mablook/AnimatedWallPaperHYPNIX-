cbuffer BloomFrame : register(b1) { float4 Filter; };
Texture2D<float4> Source : register(t0);
SamplerState LinearClamp : register(s0);
struct ScreenVertex { float4 position : SV_Position; float2 uv : TEXCOORD0; };
float3 Bright(float2 uv)
{
    float3 c=Source.SampleLevel(LinearClamp,uv,0).rgb*Filter.z;
    float l=dot(c,float3(.2126,.7152,.0722));
    // Soft knee and bounded energy keep the tiny solar disk from flooding the frame.
    float knee=saturate((l-.7)/.6);
    return c*(knee*min(max(l-.7,0),18)/max(l,.00001));
}
float4 ExtractPS(ScreenVertex input):SV_Target
{
    float3 c=0;
    [unroll] for(int y=0;y<4;y++) [unroll] for(int x=0;x<4;x++)
        c+=Bright(input.uv+((float2(x,y)+.5)/4-.5)*Filter.xy);
    return float4(c/16,1);
}
float4 BlurPS(ScreenVertex input):SV_Target
{
    float3 c=0; float weight=0;
    [unroll] for(int i=-6;i<=6;i++)
    {
        float w=exp(-i*i/12.0); weight+=w;
        c+=Source.SampleLevel(LinearClamp,input.uv+Filter.xy*i,0).rgb*w;
    }
    return float4(c/weight,1);
}
