using AnimatedWallPaper.Services;
namespace Hypnix.Tests;

public sealed class FireFrequencyBandsTests
{
    [Theory]
    [InlineData(6)] [InlineData(9)] [InlineData(15)] [InlineData(19)] [InlineData(29)] [InlineData(48)]
    public void EveryFftBinDrivesExactlyOneFlameInReverseFrequencyOrder(int count)
    {
        for(int bin=0;bin<64;bin++)
        {
            var input=new float[64];input[bin]=1;
            var output=FireFrequencyBands.Analyze(input,4,count);
            int group=Enumerable.Range(0,count).Single(g=>bin>=g*64/count&&bin<(g+1)*64/count);
            Assert.Equal(count,output.Length);
            Assert.Single(output.Where(v=>v>0));Assert.True(output[count-1-group]>0);
        }
    }

    [Theory]
    [InlineData(1920,1080,15)] [InlineData(3840,2160,15)]
    [InlineData(3440,1440,20)] [InlineData(5120,1440,29)]
    [InlineData(1080,1920,6)] [InlineData(16000,1000,48)]
    public void MonitorAspectControlsFlameDensity(int width,int height,int expected)
        =>Assert.Equal(expected,FireEmitterLayout.CountForViewport(width,height));
    [Fact] public void TrebleIsLeftAndBassIsRight()
    {
        var low=new float[64];low[0]=1;
        var high=new float[64];high[^1]=1;
        Assert.True(FireFrequencyBands.Analyze(low,4)[^1]>0);
        Assert.True(FireFrequencyBands.Analyze(high,4)[0]>0);
    }
    [Fact] public void SilenceSensitivityZeroAndNonfiniteSamplesDoNotIgniteBands()
    {
        Assert.All(FireFrequencyBands.Analyze([],4),v=>Assert.Equal(0,v));
        Assert.All(FireFrequencyBands.Analyze(Enumerable.Repeat(1f,64).ToArray(),0),v=>Assert.Equal(0,v));
        Assert.All(FireFrequencyBands.Analyze([float.NaN,float.PositiveInfinity,float.NegativeInfinity],4),v=>Assert.Equal(0,v));
    }
    [Fact] public void DistinctFrequenciesRemainDistinctWhenGroupedProfilesWouldMatch()
    {
        var first=new float[64];first[0]=1;
        var second=new float[64];second[8]=1;
        Assert.Equal(AethelisAudioProfile.Analyze(first,4),AethelisAudioProfile.Analyze(second,4));
        Assert.False(FireFrequencyBands.Analyze(first,4).SequenceEqual(FireFrequencyBands.Analyze(second,4)));
    }
}
