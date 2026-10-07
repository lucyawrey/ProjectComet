namespace Comet.Client.Tests;

public class FixedStepTests
{
    [Fact]
    public void TheFirstFrameStartsTheClock()
    {
        var step = new FixedStep(4);
        Assert.Equal(0, step.Advance(10));
        Assert.Equal(0f, step.Alpha);
    }

    [Fact]
    public void RunsTheWholeStepsDueAndCarriesTheRest()
    {
        var step = new FixedStep(4);
        step.Advance(0);

        Assert.Equal(0, step.Advance(0.125));
        Assert.Equal(0.5f, step.Alpha);
        Assert.Equal(1, step.Advance(0.375));
        Assert.Equal(0.5f, step.Alpha);
        Assert.Equal(2, step.Advance(0.875));
        Assert.Equal(0.5f, step.Alpha);
    }

    [Fact]
    public void ALongHitchRunsAtMostMaxStepsAndDropsTheRest()
    {
        var step = new FixedStep(4, maxSteps: 3);
        step.Advance(0);

        Assert.Equal(3, step.Advance(5));
        Assert.Equal(0f, step.Alpha);
        Assert.Equal(1, step.Advance(5.25));
    }

    [Fact]
    public void TimeGoingBackwardsRunsNothing()
    {
        var step = new FixedStep(4);
        step.Advance(1);
        Assert.Equal(0, step.Advance(0.5));
        Assert.Equal(1, step.Advance(0.75));
    }

    [Fact]
    public void ResetStartsTheClockAgain()
    {
        var step = new FixedStep(4);
        step.Advance(0);
        step.Advance(0.125);
        step.Reset();

        Assert.Equal(0, step.Advance(7));
        Assert.Equal(1, step.Advance(7.25));
        Assert.Equal(0f, step.Alpha);
    }
}
