using System.IO;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Vortice.D3DCompiler;

namespace AnimatedWallPaper.Services;

internal static class OceanShader
{
    private static readonly ConcurrentDictionary<string,Lazy<ReadOnlyMemory<byte>>> Cache=new();
    // Resolve only known bundled includes, independent of the working directory.
    internal static ReadOnlyMemory<byte> Compile(string path,string entryPoint,string profile)
    {
        var source=File.ReadAllText(path);
        for(var pass=0;pass<3;pass++)
        foreach(var name in new[] {"OceanVolumeCommon.hlsl", "OceanVolumeSampling.hlsl", "OceanOptics.hlsl"})
        {
            var include=$"#include \"{name}\"";
            if(source.Contains(include,StringComparison.Ordinal))
                source=source.Replace(include,File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!,name)),StringComparison.Ordinal);
        }
        var key=entryPoint+":"+profile+":"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        return Cache.GetOrAdd(key,_=>new Lazy<ReadOnlyMemory<byte>>(()=>Compiler.Compile(source,entryPoint,path,profile))).Value;
    }
}
