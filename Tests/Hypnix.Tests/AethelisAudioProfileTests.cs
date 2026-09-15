using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class AethelisAudioProfileTests
{
    [Fact]
    public void SilenceProducesLatentCoreState()
    {
        var profile = AethelisAudioProfile.Analyze(new float[64], 1f);

        Assert.Equal(0, profile.Bass);
        Assert.Equal(0, profile.Mids);
        Assert.Equal(0, profile.Highs);
        Assert.Equal(0, profile.Energy);
    }

    [Fact]
    public void LowBandsDriveBassWithoutFakingHighs()
    {
        var bands = new float[64];
        Array.Fill(bands, 0.9f, 0, 16);

        var profile = AethelisAudioProfile.Analyze(bands, 1f);

        Assert.True(profile.Bass > 0.85f);
        Assert.Equal(0, profile.Highs);
        Assert.True(profile.Energy > 0.4f);
    }

    [Fact]
    public void HighBandsDriveParticlesWithoutFakingBass()
    {
        var bands = new float[64];
        Array.Fill(bands, 0.8f, 48, 16);

        var profile = AethelisAudioProfile.Analyze(bands, 1f);

        Assert.True(profile.Highs > 0.75f);
        Assert.Equal(0, profile.Bass);
    }

    [Fact]
    public void SensitivityRemainsBounded()
    {
        var bands = Enumerable.Repeat(1f, 64).ToArray();
        var profile = AethelisAudioProfile.Analyze(bands, 2.7f);

        Assert.InRange(profile.Bass, 0, 1);
        Assert.InRange(profile.Mids, 0, 1);
        Assert.InRange(profile.Highs, 0, 1);
        Assert.InRange(profile.Complexity, 0, 1);
    }

    [Fact]
    public void HighGainRetainsDynamicsAndZeroSensitivityDisablesReaction()
    {
        var low = AethelisAudioProfile.ApplyGain(0.04f, 4);
        var high = AethelisAudioProfile.ApplyGain(0.04f, 12);
        Assert.True(high > low + 0.15f);
        Assert.True(AethelisAudioProfile.ApplyGain(0.1f, 12) < AethelisAudioProfile.ApplyGain(0.4f, 12));
        Assert.Equal(0, AethelisAudioProfile.ApplyGain(1, 0));
        Assert.Equal(0, AethelisAudioProfile.ApplyGain(0, 12));
        Assert.Equal(0, AethelisAudioProfile.ApplyGain(float.NaN, 12));
    }
}
