using AnimatedWallPaper.Services;
using NAudio.Wave;

namespace Hypnix.Tests;

public sealed class AudioRecoveryTests
{
    [Fact]
    public async Task MultipleFailuresKeepRetryingUntilDeviceReturns()
    {
        var attempts = 0;
        await RetryWorker.RunAsync(() => ++attempts == 4, TimeSpan.Zero, CancellationToken.None);
        Assert.Equal(4, attempts);
    }

    [Fact]
    public async Task ShutdownCancelsPendingRetryWithoutStartingCapture()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var work = RetryWorker.RunAsync(() => { attempts++; return false; }, TimeSpan.FromMinutes(1), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
        Assert.Equal(1, attempts);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidFloatSamplesNeverReachFft(float value)
        => Assert.Equal(0, AudioSpectrumService.ReadSample(BitConverter.GetBytes(value), 0,
            WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)));

    [Fact]
    public void Pcm32IsDecodedAsInteger()
        => Assert.Equal(0.5f, AudioSpectrumService.ReadSample(BitConverter.GetBytes(1073741824), 0,
            new WaveFormat(48000, 32, 2)));
}
