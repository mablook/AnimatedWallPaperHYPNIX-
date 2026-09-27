using System.Numerics;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace AnimatedWallPaper.Services;

// Shared spherical optical-depth LUT. Stores density integrals, so weather can change
// coefficients without rebuilding. CPU construction occurs once, never per frame.
internal sealed class OceanOpticalDepth : IDisposable
{
    public const int Width = 256, Height = 64;
    public const float RadiusKm = 6360, TopKm = 6460;
    private static readonly Lazy<Vector4[]> Data = new(CreateData);
    private readonly ID3D11Texture2D _texture;
    public ID3D11ShaderResourceView Read { get; }
    public static float Aerosol(OceanAir air) => air switch { OceanAir.Clear => .002f, OceanAir.Hazy => .010f, _ => .0044f };
    public OceanOpticalDepth(ID3D11Device device, ID3D11DeviceContext context)
    {
        _texture = device.CreateTexture2D(new Texture2DDescription(Format.R32G32B32A32_Float, Width, Height, 1, 1, BindFlags.ShaderResource));
        try
        {
            context.UpdateSubresource(Data.Value.AsSpan(), _texture, 0, Width*16);
            Read = device.CreateShaderResourceView(_texture);
        }
        catch { _texture.Dispose(); throw; }
    }
    public static Vector4 Integrate(double height, double mu)
    {
        var r = RadiusKm+height;
        var b = r*mu;
        var groundDiscriminant = b*b-r*r+RadiusKm*RadiusKm;
        // Opaque neighbors must never interpolate into a transparent beam along the terminator.
        if (mu < 0 && groundDiscriminant > 0) return new(10000,10000,10000,0);
        var distance = Math.Max(0,-b+Math.Sqrt(Math.Max(0,b*b-r*r+TopKm*TopKm)));
        double rayleigh=0,mie=0,ozone=0;
        for (var i=0;i<64;i++)
        {
            var a=distance*Math.Pow(i/64d,2); var end=distance*Math.Pow((i+1)/64d,2); var t=(a+end)*.5;
            var altitude=Math.Max(0,Math.Sqrt(r*r+t*t+2*r*t*mu)-RadiusKm);
            rayleigh+=Math.Exp(-altitude/8)*(end-a); mie+=Math.Exp(-altitude/1.2)*(end-a);
            ozone+=Math.Max(0,1-Math.Abs(altitude-25)/15)*(end-a);
        }
        return new((float)rayleigh,(float)mie,(float)ozone,1);
    }
    public static Vector3 Transmission(double apparentDegrees, OceanAir air)
    {
        var d=Integrate(.0028,Math.Sin(apparentDegrees*OceanCelestialModel.Deg));
        return new Vector3(MathF.Exp(-.0058f*d.X-Aerosol(air)*d.Y-.00065f*d.Z),
            MathF.Exp(-.0135f*d.X-Aerosol(air)*d.Y-.001881f*d.Z),
            MathF.Exp(-.0331f*d.X-Aerosol(air)*d.Y-.000085f*d.Z))*d.W;
    }
    private static Vector4[] CreateData()
    {
        var data=new Vector4[Width*Height];
        for(var y=0;y<Height;y++)
        for(var x=0;x<Width;x++)
        {
            var signed=x/(double)(Width-1)*2-1;
            data[y*Width+x]=Integrate(100*Math.Pow(y/(double)(Height-1),2)+.0028,signed*Math.Abs(signed));
        }
        return data;
    }
    public void Dispose() { Read.Dispose(); _texture.Dispose(); }
}
