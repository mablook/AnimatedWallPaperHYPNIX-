using System.Text.Json.Serialization;

namespace AnimatedWallPaper.Services;

internal sealed record WallpaperPackageManifest(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("rendererId")] string? RendererId,
    [property: JsonPropertyName("entrypoint")] string? Entrypoint,
    [property: JsonPropertyName("preview")] string? Preview,
    [property: JsonPropertyName("background")] string? Background);

internal enum WallpaperPackageStatus
{
    Ready,
    UnsupportedRenderer,
    Invalid
}

internal sealed record WallpaperPackageRegistration(
    string PackageDirectory,
    WallpaperPackageStatus Status,
    WallpaperPackageManifest? Manifest,
    string? Error);
