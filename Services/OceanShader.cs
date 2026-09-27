using System.IO;
using Vortice.D3DCompiler;

namespace AnimatedWallPaper.Services;

internal static class OceanShader
{
    // Resolve the one shared, bundled include explicitly: no working-directory dependence.
    internal static ReadOnlyMemory<byte> Compile(string path,string entryPoint,string profile)
    {
        var shared=File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!,"OceanOptics.hlsl"));
        var source=File.ReadAllText(path).Replace("#include \"OceanOptics.hlsl\"",shared,StringComparison.Ordinal);
        return Compiler.Compile(source,entryPoint,path,profile);
    }
}
