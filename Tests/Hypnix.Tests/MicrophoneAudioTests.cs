using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class MicrophoneAudioTests
{
    [Fact]
    public void MicrophoneStartsOnlyAfterOptInAndDoesNotRestartSystemAudio()
    {
        var fixture = new Fixture();
        using var subscription = fixture.Router.Subscribe(_ => { });
        var system = Assert.Single(fixture.Streams);
        Assert.False(system.Microphone);
        Assert.Equal(1, system.Starts);
        Assert.False(fixture.Router.MicrophoneEnabled);

        fixture.Router.SetMicrophoneEnabled(true);
        fixture.Router.SetMicrophoneEnabled(true);

        Assert.True(fixture.Router.MicrophoneEnabled);
        Assert.Equal(2, fixture.Streams.Count);
        Assert.Equal(1, fixture.Latest(true).Starts);
        Assert.Equal(1, system.Starts);
        Assert.Equal(0, system.Disposals);
    }

    [Fact]
    public void OptInWithoutAnActiveWallpaperOrPreviewOpensNoStreams()
    {
        var fixture = new Fixture();
        fixture.Router.SetMicrophoneEnabled(true);
        Assert.True(fixture.Router.MicrophoneEnabled);
        Assert.Empty(fixture.Streams);

        fixture.Router.SetMicrophoneEnabled(false);
        Assert.False(fixture.Router.MicrophoneEnabled);
        Assert.Empty(fixture.Streams);
    }

    [Fact]
    public void DesktopAndPreviewShareStreamsUntilLastSubscriberLeaves()
    {
        var fixture = new Fixture();
        var desktopFrames = new List<float[]>();
        var previewFrames = new List<float[]>();
        fixture.Router.SetMicrophoneEnabled(true);
        using var desktop = fixture.Router.Subscribe(desktopFrames.Add);
        using var preview = fixture.Router.Subscribe(previewFrames.Add);
        Assert.Equal(2, fixture.Streams.Count);
        Assert.All(fixture.Streams, stream => Assert.Equal(1, stream.Starts));

        fixture.Latest(true).Emit(0.7f);
        Assert.Equal(0.7f, Assert.Single(desktopFrames)[0]);
        Assert.Equal(0.7f, Assert.Single(previewFrames)[0]);

        desktop.Dispose();
        Assert.All(fixture.Streams, stream => Assert.Equal(0, stream.Disposals));
        fixture.Latest(true).Emit(0.3f);
        Assert.Single(desktopFrames);
        Assert.Equal(0.3f, previewFrames[^1][0]);

        preview.Dispose();
        preview.Dispose();
        Assert.All(fixture.Streams, stream => Assert.Equal(1, stream.Disposals));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EitherSourceAnimatesWhenTheOtherIsAbsentOrSilent(bool microphoneAvailable)
    {
        var fixture = new Fixture();
        var frames = new List<float[]>();
        fixture.Router.SetMicrophoneEnabled(true);
        using var subscription = fixture.Router.Subscribe(frames.Add);

        // An unavailable endpoint emits nothing while its worker waits for a device.
        fixture.Latest(microphoneAvailable).Emit(0.6f, 0.2f);

        var bands = Assert.Single(frames);
        Assert.Equal(64, bands.Length);
        Assert.Equal(0.6f, bands[0]);
        Assert.Equal(0.2f, bands[1]);
        Assert.All(bands.Skip(2), value => Assert.Equal(0, value));
    }

    [Fact]
    public void CombinedAudioUsesTheStrongerBandWithoutDoublingSharedSound()
    {
        var fixture = new Fixture();
        var frames = new List<float[]>();
        fixture.Router.SetMicrophoneEnabled(true);
        using var subscription = fixture.Router.Subscribe(frames.Add);

        fixture.Latest(false).Emit(0.2f, 0.7f, 0.4f);
        fixture.Latest(true).Emit(0.8f, 0.1f, 0.4f);

        Assert.Equal(new[] { 0.8f, 0.7f, 0.4f }, frames[^1].Take(3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisablingMicrophoneImmediatelyClearsItsContributionAndRejectsLateCallbacks(bool systemPlaying)
    {
        var fixture = new Fixture();
        var frames = new List<float[]>();
        fixture.Router.SetMicrophoneEnabled(true);
        using var subscription = fixture.Router.Subscribe(frames.Add);
        var system = fixture.Latest(false);
        var microphone = fixture.Latest(true);
        if (systemPlaying) system.Emit(0.2f);
        microphone.Emit(0.9f);
        var beforeDisable = frames.Count;

        fixture.Router.SetMicrophoneEnabled(false);

        Assert.False(fixture.Router.MicrophoneEnabled);
        Assert.Equal(beforeDisable + 1, frames.Count);
        Assert.Equal(systemPlaying ? 0.2f : 0f, frames[^1][0]);
        Assert.Equal(1, microphone.Disposals);
        Assert.Equal(0, system.Disposals);
        microphone.Emit(1);
        Assert.Equal(beforeDisable + 1, frames.Count);
        system.Emit(0.3f);
        Assert.Equal(0.3f, frames[^1][0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnInputThatStopsRespondingDoesNotHoldTheOtherAboveItsCurrentLevel(bool continuingMicrophone)
    {
        var fixture = new Fixture();
        var frames = new List<float[]>();
        fixture.Router.SetMicrophoneEnabled(true);
        using var subscription = fixture.Router.Subscribe(frames.Add);
        fixture.Latest(!continuingMicrophone).Emit(0.9f);
        fixture.Now += 300;
        fixture.Latest(continuingMicrophone).Emit(0.1f);
        Assert.Equal(0.9f, frames[^1][0]);

        fixture.Now++;
        fixture.Latest(continuingMicrophone).Emit(0.1f);
        Assert.Equal(0.1f, frames[^1][0]);
    }

    [Fact]
    public void ResumingPreservesOptInButDiscardsOldStreamsAndTheirQueuedAudio()
    {
        var fixture = new Fixture();
        var firstFrames = new List<float[]>();
        fixture.Router.SetMicrophoneEnabled(true);
        using var first = fixture.Router.Subscribe(firstFrames.Add);
        var oldSystem = fixture.Latest(false);
        var oldMicrophone = fixture.Latest(true);
        oldSystem.Emit(0.4f);
        oldMicrophone.Emit(0.9f);
        first.Dispose();
        Assert.True(fixture.Router.MicrophoneEnabled);
        Assert.All(fixture.Streams, stream => Assert.Equal(1, stream.Disposals));

        var resumedFrames = new List<float[]>();
        using var resumed = fixture.Router.Subscribe(resumedFrames.Add);
        Assert.Equal(4, fixture.Streams.Count);
        fixture.Latest(false).Emit(0.2f);
        Assert.Equal(0.2f, Assert.Single(resumedFrames)[0]);
        oldSystem.Emit(1);
        oldMicrophone.Emit(1);
        Assert.Single(resumedFrames);
        Assert.Equal(2, firstFrames.Count);
        fixture.Latest(true).Emit(0.6f);
        Assert.Equal(0.6f, resumedFrames[^1][0]);
    }

    private sealed class Fixture
    {
        public long Now { get; set; } = 1000;
        public List<FakeStream> Streams { get; } = [];
        public AudioSpectrumRouter Router { get; }

        public Fixture() => Router = new(microphone =>
        {
            var stream = new FakeStream(microphone);
            Streams.Add(stream);
            return stream;
        }, () => Now);

        public FakeStream Latest(bool microphone) => Streams.Last(stream => stream.Microphone == microphone);
    }

    private sealed class FakeStream(bool microphone) : IAudioSpectrumStream
    {
        public bool Microphone { get; } = microphone;
        public int Starts { get; private set; }
        public int Disposals { get; private set; }
        public event Action<float[]>? BandsAvailable;
        public void Start() => Starts++;
        // Keep the callback after disposal to simulate a callback already queued by a device.
        public void Dispose() => Disposals++;
        public void Emit(params float[] bands) => BandsAvailable?.Invoke(bands);
    }
}
