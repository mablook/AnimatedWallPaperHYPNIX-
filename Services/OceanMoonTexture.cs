using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace AnimatedWallPaper.Services;

internal sealed class OceanMoonTexture : IDisposable
{
    private readonly ID3D11Texture2D _texture;
    public ID3D11ShaderResourceView Read { get; }
    internal ID3D11Texture2D Texture => _texture;
    public const long EstimatedBytes = 11184812;
    public unsafe OceanMoonTexture(ID3D11Device device, ID3D11DeviceContext context)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Assets", "Effects", "Ocean", "lroc_color_2k.jpg"));
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        if (frame.PixelWidth != 2048 || frame.PixelHeight != 1024) throw new InvalidDataException("Unexpected lunar map dimensions.");
        var pixels = new byte[frame.PixelWidth * frame.PixelHeight * 4];
        frame.CopyPixels(pixels, frame.PixelWidth * 4, 0);
        _texture = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm_SRgb, 2048, 1024, 1, 0,
            BindFlags.ShaderResource | BindFlags.RenderTarget, miscFlags: ResourceOptionFlags.GenerateMips));
        try
        {
            fixed (byte* p = pixels) context.UpdateSubresource(_texture, 0, null, (IntPtr)p, 2048 * 4, 0);
            Read = device.CreateShaderResourceView(_texture);
            try { context.GenerateMips(Read); }
            catch { Read.Dispose(); throw; }
        }
        catch { _texture.Dispose(); throw; }
    }
    public void Dispose() { Read.Dispose(); _texture.Dispose(); }
}
