using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class FireEmitterLayoutTests
{
    [Theory]
    [InlineData(1920, 1080, 1f, 15)]
    [InlineData(3440, 1440, 1f, 20)]
    [InlineData(5120, 1440, 1f, 29)]
    [InlineData(1920, 1080, .5f, 29)]
    [InlineData(3440, 1440, .5f, 39)]
    [InlineData(5120, 1440, .5f, 48)]
    [InlineData(1920, 1080, 2f, 8)]
    public void SmallerFlamesAddSourcesInsteadOfShrinkingTheBed(int width, int height, float size, int count)
        => Assert.Equal(count, FireEmitterLayout.CountForViewport(width, height, size));

    [Theory]
    [InlineData(.3f)] [InlineData(.5f)] [InlineData(1f)] [InlineData(2f)]
    public void PreviewAndDesktopHaveTheSameDensityAtTheSameAspect(float size)
        => Assert.Equal(FireEmitterLayout.CountForViewport(1280, 360, size),
            FireEmitterLayout.CountForViewport(5120, 1440, size));

    [Fact]
    public void InvalidSizesUseTheDefaultAndExtremeRatiosStayWithinTheGpuBandBudget()
    {
        foreach (var size in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Assert.Equal(15, FireEmitterLayout.CountForViewport(1920, 1080, size));
        Assert.Equal(FireEmitterLayout.MaxCount, FireEmitterLayout.CountForViewport(int.MaxValue, 1));
        Assert.Equal(6, FireEmitterLayout.CountForViewport(1, int.MaxValue));
    }
}
