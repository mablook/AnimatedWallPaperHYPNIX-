using AnimatedWallPaper.Services;
using Xunit;

namespace Hypnix.Tests;

public sealed class LibraryLayoutTests
{
    [Theory]
    [InlineData(700, 600, false, false, 0)]
    [InlineData(700, 600, true, false, 2)]
    [InlineData(1400, 750, false, false, 1)]
    [InlineData(1400, 750, true, true, 0)]
    [InlineData(1400, 450, true, false, 2)]
    [InlineData(1099, 750, false, false, 0)]
    [InlineData(1100, 750, false, false, 1)]
    public void WindowSpaceDeterminesPreview(double width, double height, bool requested, bool collapsed, int expected)
        => Assert.Equal((LibraryPreviewMode)expected, LibraryLayout.Resolve(width, height, requested, collapsed));

    [Theory]
    [InlineData(400, 300, 16d / 9)]
    [InlineData(400, 300, 9d / 16)]
    [InlineData(400, 300, 32d / 9)]
    [InlineData(120, 400, 1)]
    public void FramePreservesDisplayAspectWithoutOverflow(double width, double height, double aspect)
    {
        var fit = LibraryLayout.Fit(width, height, aspect);
        Assert.InRange(fit.Width, 0, width);
        Assert.InRange(fit.Height, 0, height);
        Assert.Equal(aspect, fit.Width / fit.Height, 8);
    }
}
