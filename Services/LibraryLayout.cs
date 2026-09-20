namespace AnimatedWallPaper.Services;

internal enum LibraryPreviewMode { Library, Docked, Preview }

// WPF supplies device-independent units here, so display scaling is already accounted for.
internal static class LibraryLayout
{
    public static LibraryPreviewMode Resolve(double width, double height, bool previewRequested, bool paneCollapsed)
        => width >= 1100 && height >= 480
            ? paneCollapsed ? LibraryPreviewMode.Library : LibraryPreviewMode.Docked
            : previewRequested ? LibraryPreviewMode.Preview : LibraryPreviewMode.Library;

    public static (double Width, double Height) Fit(double width, double height, double aspect)
    {
        if (!double.IsFinite(aspect) || aspect <= 0) aspect = 16d / 9;
        width = Math.Max(0, width); height = Math.Max(0, height);
        return width / Math.Max(height, 1) > aspect ? (height * aspect, height) : (width, width / aspect);
    }
}
