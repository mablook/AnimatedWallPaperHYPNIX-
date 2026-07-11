using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class FrameRatePolicyTests
{
    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(60)]
    public void SupportedFrameRatesArePreserved(int fps)
    {
        Assert.Equal(fps, FrameRatePolicy.Normalize(fps));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(24)]
    [InlineData(144)]
    public void UnsupportedFrameRatesUseSafeDefault(int fps)
    {
        Assert.Equal(30, FrameRatePolicy.Normalize(fps));
    }
}
