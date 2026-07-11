cbuffer FrameData : register(b0)
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

struct VertexOutput
{
    float4 Position : SV_Position;
    float2 UV : TEXCOORD0;
};

VertexOutput VSMain(uint id : SV_VertexID)
{
    VertexOutput output;
    float2 uv = float2((id << 1) & 2, id & 2);
    output.UV = uv;
    output.Position = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
    return output;
}

float4 PSMain(VertexOutput input) : SV_Target
{
    // The actual flames are rendered by Effekseer. This pass deliberately
    // contains no procedural ring so it cannot pollute the authored effect.
    float bassAura = saturate(Bass) * 0.004;
    return float4(0.001 + bassAura, 0.001, 0.002, 1.0);
}
