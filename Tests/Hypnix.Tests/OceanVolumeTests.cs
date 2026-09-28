using System.Numerics;
using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class OceanVolumeTests
{
    [Theory]
    [InlineData(0)] [InlineData(.01)] [InlineData(.1)] [InlineData(1)] [InlineData(5)] [InlineData(20)]
    public void SplittingHomogeneousTransportPreservesRadianceAndTransmission(double tau)
    {
        const double distance=4000;
        var sigma=tau/distance;
        var source=new Vector3(.3f,.8f,1.2f);
        var near=(source*(float)OceanVolumeMath.SegmentWeight(sigma,distance*.37),new Vector3((float)OceanVolumeMath.Transmission(sigma,distance*.37)));
        var far=(source*(float)OceanVolumeMath.SegmentWeight(sigma,distance*.63),new Vector3((float)OceanVolumeMath.Transmission(sigma,distance*.63)));
        var combined=OceanVolumeMath.Compose(near,far);
        var exact=source*(float)OceanVolumeMath.SegmentWeight(sigma,distance);
        Assert.InRange(Vector3.Distance(combined.S,exact),0,Math.Max(.001,exact.Length()*1e-6));
        Assert.InRange(Math.Abs(combined.T.X-Math.Exp(-tau)),0,1e-6);
    }
    [Theory]
    [InlineData(-.2)] [InlineData(0)] [InlineData(.65)] [InlineData(.78)]
    public void PhaseIntegratesToOneWithTheForwardPeakTowardTheLight(double g)
    {
        double integral=0;
        for(var i=0;i<100000;i++) integral+=OceanVolumeMath.Phase(-1+(i+.5)*2/100000,g)*4*Math.PI/100000;
        Assert.InRange(Math.Abs(integral-1),0,1e-6);
        if(g>0) Assert.True(OceanVolumeMath.Phase(1,g)>OceanVolumeMath.Phase(-1,g));
    }
    [Fact]
    public void WaveSpeedCannotAdvanceTheIndependentWeatherClock()
    {
        var waves=new OceanFrameClock(); var weather=new OceanFrameClock();
        waves.Advance(0); weather.Advance(0);
        waves.SetSpeed(1.5,10);
        Assert.Equal(25,waves.Advance(20)); Assert.Equal(20,weather.Advance(20));
        weather.SetPaused(true,20); weather.Advance(200); Assert.Equal(20,weather.Time);
        weather.SetPaused(false,200); Assert.Equal(21,weather.Advance(201));
    }
    [Fact]
    public void WeatherHasFiniteIndependentControlsAndFogWithinIntegratedShell()
    {
        var clean=new OceanWeatherSettings(Coverage:float.NaN,WindMetresPerSecond:float.PositiveInfinity,Seed:-1).Normalize();
        Assert.Equal(.25f,clean.Coverage); Assert.Equal(9,clean.WindMetresPerSecond); Assert.Equal(0,clean.Seed);
        foreach(var kind in Enum.GetValues<OceanCloudType>()) foreach(var fog in Enum.GetValues<OceanFog>())
        {
            var state=new OceanWeatherSettings(Clouds:kind,Fog:fog);
            // Maritime haze can overlap the stratus base; both media share transport.
            Assert.True(state.FogProfile.Height*5<=state.Layer.Top);
            Assert.InRange(state.FogProfile.Extinction,0,2);
        }
        Assert.Equal(0,new OceanWeatherSettings(Fog:OceanFog.Clear).FogProfile.Extinction);
        Assert.Equal(new OceanWeatherSettings().Layer,(new OceanWeatherSettings(Fog:OceanFog.Banks)).Layer);
    }
}
