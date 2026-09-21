using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class MonitorPreviewSimulationTests
{
    private static readonly WallpaperEntry[] Catalog =
    [
        new("living-fire", "Living Fire", WallpaperKind.LivingFire, null),
        new("kaleidoscope", "Kaleidoscope", WallpaperKind.Kaleidoscope, null),
        new("spectral-bloom", "Spectral Bloom", WallpaperKind.SpectralBloom, null),
        new("neon-ribbons", "Neon Ribbons", WallpaperKind.NeonRibbons, null),
        new("event-horizon", "Event Horizon", WallpaperKind.EventHorizon, null)
    ];

    [Fact]
    public void MonitorFormatIsIndependentAndSurvivesCountAndWallpaperChanges()
    {
        var simulation = new MonitorPreviewSimulation();
        simulation.SetCount(4, Catalog);
        var before = simulation.Displays;
        Assert.True(simulation.SelectAspect("preview-sim-4", 32d / 9));
        Assert.Equal(before.Take(3), simulation.Displays.Take(3));
        Assert.True(simulation.SelectWallpaper("preview-sim-4", "living-fire", Catalog));
        simulation.SetCount(3, Catalog);
        Assert.False(simulation.SelectAspect("preview-sim-4", 16d / 9));
        simulation.SetCount(4, Catalog);
        Assert.Equal(32d / 9, simulation.Displays[3].Aspect);
        Assert.Equal("living-fire", simulation.Displays[3].WallpaperId);
        Assert.Equal(21d / 9, before[3].Aspect);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100)]
    public void InvalidMonitorFormatPreservesThePreview(double aspect)
    {
        var simulation = new MonitorPreviewSimulation();
        simulation.SetCount(4, Catalog);
        var before = simulation.Displays;
        Assert.False(simulation.SelectAspect("preview-sim-4", aspect));
        Assert.False(simulation.SelectAspect("physical-monitor", 32d / 9));
        Assert.Same(before, simulation.Displays);
    }

    [Fact]
    public void FourDisplaysStartWithDifferentWallpapersAndIncludePortraitAndUltrawide()
    {
        var simulation = new MonitorPreviewSimulation();
        simulation.SetCount(4, Catalog);

        Assert.Equal(new[] { "preview-sim-1", "preview-sim-2", "preview-sim-3", "preview-sim-4" },
            simulation.Displays.Select(display => display.Id));
        Assert.Equal(new[] { "living-fire", "kaleidoscope", "spectral-bloom", "neon-ribbons" },
            simulation.Displays.Select(display => display.WallpaperId));
        Assert.Equal(new[] { 16d / 9, 16d / 9, 9d / 16, 21d / 9 },
            simulation.Displays.Select(display => display.Aspect));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(int.MaxValue)]
    public void UnsupportedCountsAreRejectedWithoutChangingTheCurrentPreview(int count)
    {
        var simulation = new MonitorPreviewSimulation();
        simulation.SetCount(3, Catalog);
        var before = simulation.Displays;

        Assert.Throws<ArgumentOutOfRangeException>(() => simulation.SetCount(count, Catalog));
        Assert.Same(before, simulation.Displays);
    }

    [Fact]
    public void EditingOneDisplayDoesNotChangeTheOthersOrAnotherSimulation()
    {
        var first = new MonitorPreviewSimulation();
        var second = new MonitorPreviewSimulation();
        first.SetCount(4, Catalog);
        second.SetCount(4, Catalog);
        var before = first.Displays;

        Assert.True(first.SelectWallpaper("preview-sim-2", "event-horizon", Catalog));

        Assert.Equal("event-horizon", first.Displays[1].WallpaperId);
        Assert.Equal(before.Where((_, index) => index != 1),
            first.Displays.Where((_, index) => index != 1));
        Assert.Equal(before, second.Displays);
        Assert.Equal("kaleidoscope", before[1].WallpaperId);
    }

    [Fact]
    public void SwitchingBetweenThreeAndFourKeepsIdsAndEverySelection()
    {
        var simulation = new MonitorPreviewSimulation();
        simulation.SetCount(4, Catalog);
        Assert.True(simulation.SelectWallpaper("preview-sim-1", "neon-ribbons", Catalog));
        Assert.True(simulation.SelectWallpaper("preview-sim-4", "event-horizon", Catalog));
        var allFour = simulation.Displays;

        simulation.SetCount(3, Catalog);
        Assert.Equal(allFour.Take(3), simulation.Displays);
        Assert.False(simulation.SelectWallpaper("preview-sim-4", "living-fire", Catalog));
        simulation.SetCount(4, Catalog);

        Assert.Equal(allFour, simulation.Displays);
    }

    [Theory]
    [InlineData("unknown-display", "living-fire")]
    [InlineData("preview-sim-1", "missing-wallpaper")]
    [InlineData("preview-sim-1", "")]
    [InlineData("preview-sim-1", " ")]
    [InlineData("", "living-fire")]
    public void InvalidSelectionDoesNotModifyAnyPreview(string displayId, string wallpaperId)
    {
        var simulation = new MonitorPreviewSimulation();
        simulation.SetCount(3, Catalog);
        var before = simulation.Displays;

        Assert.False(simulation.SelectWallpaper(displayId, wallpaperId, Catalog));
        Assert.Same(before, simulation.Displays);
    }

    [Fact]
    public void MissingPreferredEntriesUseDistinctAvailableWallpapersBeforeRepeating()
    {
        var simulation = new MonitorPreviewSimulation();
        var available = new[] { Catalog[4], Catalog[3], Catalog[2] };

        simulation.SetCount(4, available);

        Assert.Equal(3, simulation.Displays.Take(3).Select(display => display.WallpaperId).Distinct().Count());
        Assert.All(simulation.Displays, display =>
            Assert.Contains(available, entry => entry.Id == display.WallpaperId));
    }

    [Fact]
    public void RemovedCatalogEntriesFallBackWithoutResettingValidSelections()
    {
        var simulation = new MonitorPreviewSimulation();
        simulation.SetCount(4, Catalog);
        Assert.True(simulation.SelectWallpaper("preview-sim-2", "event-horizon", Catalog));
        var available = Catalog.Where(entry => entry.Id is not ("living-fire" or "neon-ribbons")).ToArray();

        simulation.SetCount(3, available);
        simulation.SetCount(4, available);

        Assert.Equal("event-horizon", simulation.Displays[1].WallpaperId);
        Assert.Equal("spectral-bloom", simulation.Displays[2].WallpaperId);
        Assert.All(simulation.Displays, display =>
            Assert.Contains(available, entry => entry.Id == display.WallpaperId));
    }

    [Fact]
    public void TheSameWallpaperCanBeChosenForMultipleDisplays()
    {
        var simulation = new MonitorPreviewSimulation();
        simulation.SetCount(3, Catalog);
        Assert.True(simulation.SelectWallpaper("preview-sim-2", "living-fire", Catalog));

        simulation.SetCount(4, Catalog);

        Assert.Equal("living-fire", simulation.Displays[0].WallpaperId);
        Assert.Equal("living-fire", simulation.Displays[1].WallpaperId);
    }

    [Fact]
    public void EmptyCatalogLeavesEmptyPreviewsAndRecoversWhenWallpapersBecomeAvailable()
    {
        var simulation = new MonitorPreviewSimulation();
        simulation.SetCount(4, Array.Empty<WallpaperEntry>());

        Assert.Equal(4, simulation.Displays.Count);
        Assert.All(simulation.Displays, display => Assert.Null(display.WallpaperId));
        Assert.False(simulation.SelectWallpaper("preview-sim-1", "living-fire", Array.Empty<WallpaperEntry>()));

        simulation.SetCount(3, Catalog);

        Assert.Equal(3, simulation.Displays.Count);
        Assert.Equal(new[] { "living-fire", "kaleidoscope", "spectral-bloom" },
            simulation.Displays.Select(display => display.WallpaperId));
    }
}
