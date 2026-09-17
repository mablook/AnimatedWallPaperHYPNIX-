// Shared background pass for the shader visualizers. Draws a solid color or a cover-fit image
// behind the effect; the effect is then screen-blended on top, so its dark areas reveal this
// background while its bright areas add over it. Mirrors Living Fire's cover math for consistency.
cbuffer BackgroundData : register(b0)
{
    float4 Color;      // rgb = solid color; w = mode (1 = solid, 2 = image)
    float4 ImageSize;  // xy = image pixels; zw = viewport pixels
};

Texture2D BackgroundImage : register(t0);
SamplerState LinearClamp : register(s0);

struct VOut { float4 pos : SV_Position; float2 uv : TEXCOORD; };

VOut VSMain(uint id : SV_VertexID)
{
    VOut o;
    o.uv = float2((id << 1) & 2, id & 2);
    o.pos = float4(o.uv * float2(2, -2) + float2(-1, 1), 0, 1);
    return o;
}

float4 PSMain(VOut i) : SV_Target
{
    if (Color.w > 1.5) // image, cover fit (centered crop, no distortion)
    {
        float imageAspect = ImageSize.x / max(ImageSize.y, 1);
        float screenAspect = ImageSize.z / max(ImageSize.w, 1);
        float2 crop = float2(min(1, screenAspect / imageAspect), min(1, imageAspect / screenAspect));
        float4 s = BackgroundImage.SampleLevel(LinearClamp, (i.uv - 0.5) * crop + 0.5, 0);
        return float4(s.rgb * s.a, 1);
    }
    return float4(Color.rgb, 1); // solid color
}
