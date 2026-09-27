using System.Globalization;
using System.Numerics;
using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class OceanCelestialTests
{
    [Theory]
    [InlineData("sun",.005)]
    [InlineData("moon",.01)]
    public void TrajectoriesAgreeWithIndependentJplHorizonsSamples(string body,double tolerance)
    {
        var rows=File.ReadAllLines(Path.Combine(AppContext.BaseDirectory,"Fixtures",$"horizons-{body}-20260927-rise-2h.csv"));
        foreach(var row in rows.Skip(1))
        {
            var c=row.Split(','); double N(int index)=>double.Parse(c[index],CultureInfo.InvariantCulture);
            var utc=DateTimeOffset.ParseExact(c[0],"yyyy-MMM-dd HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal);
            var state=OceanCelestialModel.Evaluate(utc,OceanCelestialSettings.Default);
            var actual=body=="sun" ? state.Sun : state.Moon;
            Assert.InRange(Math.Abs(actual.Azimuth-N(3)),0,tolerance);
            Assert.InRange(Math.Abs(actual.AirlessElevation-N(4)),0,tolerance);
            Assert.InRange(Math.Abs(actual.Radius*2/OceanCelestialModel.Deg*3600-N(6)),0,1.5);
            Assert.InRange(Math.Abs(actual.DistanceKm/N(7)-1),0,.0002);
        }
    }

    [Fact]
    public void CelestialClockPreservesContinuityAndDoesNotChangeWaveTime()
    {
        var epoch=OceanCelestialSettings.Default.EpochUtc;
        var sky=new OceanCelestialClock(epoch); var sea=new OceanFrameClock();
        sky.Advance(0); sea.Advance(0);
        Assert.Equal(epoch.AddSeconds(240),sky.Advance(10)); Assert.Equal(10,sea.Advance(10));
        sky.SetRate(144,10); sky.SetPaused(true,12);
        Assert.Equal(epoch.AddSeconds(528),sky.Advance(112));
        sky.SetPaused(false,112); Assert.Equal(epoch.AddSeconds(672),sky.Advance(113));
        Assert.Equal(113,sea.Advance(113));
        sky.Seek(epoch,113); Assert.Equal(epoch,sky.Advance(double.NaN));
    }

    [Fact]
    public void RefractionDoesNotInvertDisksAndApproachesZeroAtZenith()
    {
        var previous=-2+OceanCelestialModel.Refraction(-2);
        for(var h=-1.99;h<=89;h+=.01)
        {
            var apparent=h+OceanCelestialModel.Refraction(h);
            Assert.True(apparent>previous); previous=apparent;
        }
        Assert.InRange(OceanCelestialModel.Refraction(0),.45,.60);
        Assert.InRange(Math.Abs(OceanCelestialModel.Refraction(90)),0,.001);
    }

    [Fact]
    public void PhaseNormalizationMatchesIndependentProjectedDiskIntegral()
    {
        foreach(var a in new[] { .1,.85,Math.PI/2,2.1,2.6 })
        {
            double total=0,full=0;
            for(var y=0;y<400;y++) for(var x=0;x<400;x++)
            {
                double u=(x+.5)/200-1,v=(y+.5)/200-1;
                if(u*u+v*v>=1) continue;
                var z=Math.Sqrt(1-u*u-v*v); var c=Math.Max(0,u*Math.Sin(a)+z*Math.Cos(a));
                total+=1.6*c/Math.Max(c+z,1e-10)+.2*c; full+=.8+.2*z;
            }
            Assert.InRange(Math.Abs(total/full-OceanCelestialModel.MaterialIntegral(a)),0,.0005);
        }
        Assert.InRange(OceanCelestialModel.PhaseFlux(Math.PI/2),.06,.10);
    }

    [Fact]
    public void MoonCrossesTheAtmosphereWithoutCancellingItsWarmColor()
    {
        var low=OceanOpticalDepth.Transmission(1,OceanAir.Maritime);
        var high=OceanOpticalDepth.Transmission(30,OceanAir.Maritime);
        Assert.True(low.X<high.X && low.Y<high.Y && low.Z<high.Z);
        Assert.True(low.Z/low.X<high.Z/high.X);
        Assert.Equal(0,OceanOpticalDepth.Integrate(.0028,-.1).W);
        Assert.Equal(1,OceanOpticalDepth.Integrate(30,-.01).W);
    }

    [Theory]
    [InlineData(0,0)] [InlineData(60,10)] [InlineData(89.9,0)]
    public void FullDayStatesStayFiniteAcrossMidnightAndPolarCases(double latitude,double longitude)
    {
        var options=OceanCelestialSettings.Default with { Latitude=latitude,Longitude=longitude };
        for(var hour=0;hour<48;hour++)
        {
            var frame=OceanCelestialModel.Evaluate(new DateTimeOffset(2026,6,21,0,0,0,TimeSpan.Zero).AddHours(hour),options);
            Assert.InRange(frame.Exposure,1,16001);
            foreach(var body in new[] {frame.Sun,frame.Moon})
            {
                Assert.InRange(body.Direction.Length(),.99999f,1.00001f);
                Assert.InRange(body.Radius,.003f,.006f);
                Assert.True(float.IsFinite(body.ApparentElevation));
            }
            Assert.InRange(Math.Abs(Vector3.Dot(frame.MoonPrime,frame.MoonNorth)),0,.00001);
        }
    }

    [Fact]
    public void OpticalDepthRemainsOpaqueAndNonnegativeAtGroundAndAtmosphereBoundaries()
    {
        foreach(var height in new[] {0,.0028,1.2,3.2,25,50,100,100.0028})
        for(var index=0;index<=200;index++)
        {
            var d=OceanOpticalDepth.Integrate(height,index/100d-1);
            Assert.True(float.IsFinite(d.X) && float.IsFinite(d.Y) && float.IsFinite(d.Z));
            Assert.True(d.X>=0 && d.Y>=0 && d.Z>=0);
            if(d.W==0) Assert.True(d.X>=1000 && d.Y>=1000 && d.Z>=1000);
        }
    }
}
