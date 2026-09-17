using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Point = System.Windows.Point;

namespace AnimatedWallPaper.Services;

internal sealed record VisualizerBackground(string Mode = "original", string Color = "#222222", string? ImagePath = null)
{
    public VisualizerBackground Normalize() => new(
        Mode is "solid" or "image" ? Mode : "original",
        Color is { Length: 7 } && Color[0]=='#' && uint.TryParse(Color.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out _) ? Color.ToUpperInvariant() : "#222222",
        ImagePath is not null && Path.IsPathFullyQualified(ImagePath) ? ImagePath : null);
}

internal sealed record VisualizerPreset(string Id, string WallpaperId, string Name, VisualizerPreferences Values, int Version = 1);

internal static class VisualizerPresetLibrary
{
    public static List<VisualizerPreset> Normalize(IEnumerable<VisualizerPreset>? presets) => (presets ?? [])
        .Where(p => p is not null && p.Version == 1 && !string.IsNullOrWhiteSpace(p.Id)
            && !string.IsNullOrWhiteSpace(p.WallpaperId) && !string.IsNullOrWhiteSpace(p.Name) && p.Values is not null)
        .DistinctBy(p => p.Id).Take(200)
        .Select(p => p with { Name = p.Name.Trim()[..Math.Min(p.Name.Trim().Length, 60)], Values = p.Values.Normalize() }).ToList();
}

internal static class BackgroundImageLibrary
{
    public static string Import(string source, string? directory = null)
    {
        if (new FileInfo(source).Length > 32 * 1024 * 1024) throw new InvalidDataException("Choose an image smaller than 32 MB.");
        using var stream = File.OpenRead(source);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
        var frame = decoder.Frames[0];
        if (frame.PixelWidth > 16384 || frame.PixelHeight > 16384 || (long)frame.PixelWidth * frame.PixelHeight > 64000000)
            throw new InvalidDataException("Choose an image up to 64 megapixels and 16,384 pixels per side.");
        double scale = Math.Min(1, 2048d / Math.Max(frame.PixelWidth, frame.PixelHeight));
        BitmapSource image = scale < 1 ? new TransformedBitmap(frame, new ScaleTransform(scale, scale)) : frame;
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HYPNIX", "Backgrounds");
        Directory.CreateDirectory(directory);
        var name=Path.GetFileNameWithoutExtension(source);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + "-" + name[..Math.Min(name.Length,60)] + ".png");
        using var output = File.Create(path); encoder.Save(output);
        return path;
    }
}

// Radial square/disk mapping preserves the complete independent X/Y domain.
internal static class PositionPadMapping
{
    public static Point ToDisk(double x, double y)
    {
        double length = Math.Sqrt(x*x+y*y);
        return length == 0 ? new() : new(x*Math.Max(Math.Abs(x),Math.Abs(y))/length, y*Math.Max(Math.Abs(x),Math.Abs(y))/length);
    }
    public static Point FromDisk(double x, double y)
    {
        double length = Math.Sqrt(x*x+y*y), largest = Math.Max(Math.Abs(x),Math.Abs(y));
        if (largest == 0) return new();
        double scale = Math.Min(1,length)/largest;
        return new(Math.Clamp(x*scale,-1,1),Math.Clamp(y*scale,-1,1));
    }
}
