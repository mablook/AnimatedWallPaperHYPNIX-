cbuffer FrameData : register(b0)
{
    float2 Resolution; float Time; float Bass; float Mids; float Highs;
    float Intensity; float Glow; float2 Origin; float2 Padding;
};
struct VertexOutput { float4 Position : SV_Position; float2 UV : TEXCOORD0; };
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
    float ember = saturate(Bass) * 0.003;
    return float4(0.0008 + ember, 0.0005, 0.0004, 1.0);
}
