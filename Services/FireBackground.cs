using System.IO;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace AnimatedWallPaper.Services;

internal sealed class FireBackground(ID3D11Device device) : IDisposable
{
    string? loadedPath;
    ID3D11Texture2D? texture;
    public ID3D11ShaderResourceView? Image { get; private set; }
    public Vector4 Color { get; private set; }
    public Vector4 Size { get; private set; }
    public unsafe void Update(VisualizerBackground? preference)
    {
        var bg=preference?.Normalize()??new();
        var color=System.Drawing.ColorTranslator.FromHtml(bg.Color);
        Color=new(color.R/255f,color.G/255f,color.B/255f,bg.Mode=="solid"?1:0);
        var path=bg.Mode=="image"?bg.ImagePath:null;
        if(path!=loadedPath) {
            Dispose();loadedPath=path;
            if(path is not null)try {
                if(new FileInfo(path).Length>32*1024*1024)throw new InvalidDataException("Background image exceeds size limit.");
                using var stream=File.OpenRead(path);
                var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnDemand);
                var frame=decoder.Frames[0];
                if(frame.PixelWidth>2048||frame.PixelHeight>2048)throw new InvalidDataException("Background image must be imported into the library first.");
                var converted=new FormatConvertedBitmap(frame,PixelFormats.Bgra32,null,0);
                var pixels=new byte[frame.PixelWidth*frame.PixelHeight*4];converted.CopyPixels(pixels,frame.PixelWidth*4,0);
                fixed(byte* data=pixels)texture=device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm,(uint)frame.PixelWidth,(uint)frame.PixelHeight,1,1,BindFlags.ShaderResource),new SubresourceData((IntPtr)data,(uint)frame.PixelWidth*4));
                Image=device.CreateShaderResourceView(texture);Size=new(frame.PixelWidth,frame.PixelHeight,0,0);
            } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Runtime.InteropServices.COMException or ArgumentException or FileFormatException) {
                Dispose();AppLog.WriteException("Background unavailable; using original",ex);
            }
        }
        if(bg.Mode=="image"&&Image is not null)Color=Color with {W=2};
    }
    public void Dispose(){Image?.Dispose();texture?.Dispose();Image=null;texture=null;Size=Vector4.Zero;}
}
