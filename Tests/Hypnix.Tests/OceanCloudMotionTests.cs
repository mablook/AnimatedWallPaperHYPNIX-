using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class OceanCloudMotionTests
{
    [Fact]
    public void WindEditPreservesAlreadyDisplayedEndpointsAndIntegratesTheRamp()
    {
        var motion=new OceanCloudMotion(9); var old=motion.Trajectory;
        motion.SetWind(10.25,18); var next=motion.Trajectory;
        foreach(var t in new[] {0d,10,10.25,10.5,11}) Assert.Equal(old.Evaluate(t),next.Evaluate(t));
        Assert.Equal(99,next.Evaluate(11).Distance,9);
        Assert.Equal(139.5,next.Evaluate(14).Distance,9);
        Assert.Equal(18,next.Evaluate(14).Speed,9);
        Assert.Equal(247.5,next.Evaluate(20).Distance,9);
        Assert.Equal(180,old.Evaluate(20).Distance,9); // published history is immutable
    }
    [Fact]
    public void PendingEditsCoalesceAndStoppingNeverRepositionsTheField()
    {
        var motion=new OceanCloudMotion(9);
        motion.SetWind(10.1,18); motion.SetWind(10.8,0);
        Assert.Equal(112.5,motion.Trajectory.Evaluate(14).Distance,9);
        Assert.Equal(112.5,motion.Trajectory.Evaluate(999).Distance,9);
        Assert.Equal(0,motion.Trajectory.Evaluate(999).Speed);
    }
    [Fact]
    public void MidRampEditStartsFromActualVelocityNotPreviousTarget()
    {
        var motion=new OceanCloudMotion(9); motion.SetWind(10.1,18);
        var previous=motion.Trajectory; motion.SetWind(11.1,0);
        Assert.Equal(previous.Evaluate(12),motion.Trajectory.Evaluate(12));
        Assert.Equal(12,motion.Trajectory.Evaluate(12).Speed);
        Assert.Equal(previous.Evaluate(12).Distance+18,motion.Trajectory.Evaluate(15).Distance,9);
    }
    [Fact]
    public void FrameRateAndEvaluationOrderCannotChangeTrajectory()
    {
        var motion=new OceanCloudMotion(9); motion.SetWind(20.2,18); motion.SetWind(40.4,0);
        var expected=motion.Trajectory.Constants(50,OceanCloudType.Cumulus);
        for(var i=0;i<1000;i++) motion.Trajectory.Evaluate(i/30d);
        Assert.Equal(expected,motion.Trajectory.Constants(50,OceanCloudType.Cumulus));
        Assert.Equal(expected,motion.Trajectory.Constants(50,OceanCloudType.Cumulus));
        Assert.Equal(new OceanCloudTrajectory(9).Evaluate(10),motion.Trajectory.Evaluate(10));
    }
    [Fact]
    public void CalmStopsAdvectionButNotIntrinsicEvolution()
    {
        var calm=new OceanCloudTrajectory(0);
        var a=calm.Constants(0,OceanCloudType.Cumulus); var b=calm.Constants(120,OceanCloudType.Cumulus);
        Assert.Equal(a.Travel,b.Travel); Assert.NotEqual(a.Evolution,b.Evolution);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void LongDurationConstantsAreBoundedAndSeekable(int kind)
    {
        var type=(OceanCloudType)kind;
        var motion=new OceanCloudTrajectory(18);
        foreach(var t in new[] {0d,60,3600,86400,604800,1e12})
        {
            var sample=motion.Constants(t,type);
            Assert.InRange(sample.Travel.X,0,1600); Assert.InRange(sample.Travel.Y,0,1600);
            Assert.InRange(sample.Travel.Z,0,1);
            Assert.InRange(sample.Evolution.X,-1.8f,1.8f);
            Assert.InRange(sample.Evolution.Y,-.65f,.65f);
            Assert.InRange(sample.Evolution.Z,-1.3f,1.3f);
        }
    }
    [Fact]
    public void ReferenceSpeedIsMetresPerSecondNotAnUnnormalizedVector()
    {
        var travel=new OceanCloudTrajectory(9).Constants(100,OceanCloudType.Cumulus).Travel;
        Assert.InRange(Math.Sqrt(travel.X*travel.X+travel.Y*travel.Y),.899999,.900001);
    }
}
