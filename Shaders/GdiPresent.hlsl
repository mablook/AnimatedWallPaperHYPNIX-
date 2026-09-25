// Presents a CPU (GDI) frame as an opaque fullscreen image through a DXGI swap chain.
// The classic GDI wallpapers (Built-in ambient, Audio Visualizer) draw with System.Drawing and
// were blitted to their window with GDI. On the Windows 11 "raised desktop" (Progman carries
// WS_EX_NOREDIRECTIONBITMAP) a GDI child of Progman is not composited by DWM, so that blit shows
// black. Presenting the same frame through this swap chain gives the window its own composition
// surface, exactly like the shader wallpapers, so it displays on the desktop.
Texture2D Frame : register(t0);
SamplerState LinearClamp : register(s0);

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
    // The GDI scene is opaque; force alpha to 1 so the desktop shows a solid wallpaper.
    return float4(Frame.Sample(LinearClamp, input.UV).rgb, 1.0);
}
