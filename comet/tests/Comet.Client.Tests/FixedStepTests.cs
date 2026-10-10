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
    public void TimeGoingBackwardsRunsNothingUntilItPassesTheLastStep()
    {
        var step = new FixedStep(4);
        step.Advance(1);
        Assert.Equal(0, step.Advance(0.5));
        Assert.Equal(0, step.Advance(0.75));
        Assert.Equal(1, step.Advance(1.25));
        Assert.Equal(5, step.LastStep);
    }

    [Fact]
    public void StepsFallOnTheGrid()
    {
        var step = new FixedStep(4);
        step.Advance(1.1);
        Assert.Equal(4, step.LastStep);
        // Steps at 1.25 and 1.5 s, wherever the frames fall.
        Assert.Equal(2, step.Advance(1.6));
        Assert.Equal(6, step.LastStep);
        Assert.Equal(0.4f, step.Alpha, 4);
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
