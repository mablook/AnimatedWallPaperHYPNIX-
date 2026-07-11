using System.Drawing;

namespace AnimatedWallPaper.Services;

internal static class ImageCoverCalculator
{
    public static RectangleF CalculateSourceRectangle(
        int imageWidth, int imageHeight, int targetWidth, int targetHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(imageWidth), "Image dimensions must be positive.");
        if (targetWidth <= 0 || targetHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetWidth), "Target dimensions must be positive.");

        var scale = Math.Max(targetWidth / (float)imageWidth, targetHeight / (float)imageHeight);
        var sourceWidth = targetWidth / scale;
        var sourceHeight = targetHeight / scale;
        return new RectangleF(
            (imageWidth - sourceWidth) / 2f,
            (imageHeight - sourceHeight) / 2f,
            sourceWidth,
            sourceHeight);
    }
}
