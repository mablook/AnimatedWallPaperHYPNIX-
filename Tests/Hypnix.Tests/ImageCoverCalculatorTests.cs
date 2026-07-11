using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class ImageCoverCalculatorTests
{
    [Fact]
    public void MatchingAspectRatioUsesEntireImage()
    {
        var source = ImageCoverCalculator.CalculateSourceRectangle(1920, 1080, 2560, 1440);

        Assert.Equal(0, source.X, 3);
        Assert.Equal(0, source.Y, 3);
        Assert.Equal(1920, source.Width, 3);
        Assert.Equal(1080, source.Height, 3);
    }

    [Fact]
    public void PortraitMonitorCropsImageHorizontallyAndCentersIt()
    {
        var source = ImageCoverCalculator.CalculateSourceRectangle(1920, 1080, 1080, 1920);

        Assert.True(source.X > 0);
        Assert.Equal(0, source.Y, 3);
        Assert.Equal(1080, source.Height, 3);
        Assert.Equal((1920 - source.Width) / 2, source.X, 3);
    }

    [Fact]
    public void UltrawideMonitorCropsImageVerticallyAndCentersIt()
    {
        var source = ImageCoverCalculator.CalculateSourceRectangle(1920, 1080, 3440, 1440);

        Assert.Equal(0, source.X, 3);
        Assert.True(source.Y > 0);
        Assert.Equal(1920, source.Width, 3);
        Assert.Equal((1080 - source.Height) / 2, source.Y, 3);
    }

    [Theory]
    [InlineData(0, 1080, 1920, 1080)]
    [InlineData(1920, 1080, 0, 1080)]
    public void InvalidDimensionsAreRejected(int imageWidth, int imageHeight, int targetWidth, int targetHeight)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ImageCoverCalculator.CalculateSourceRectangle(imageWidth, imageHeight, targetWidth, targetHeight));
    }
}
