using System.Numerics;

namespace Comet.Client.Tests;

public class PositionReporterTests
{
    private static readonly Vector3 East = new(5, 0, 0);

    [Fact]
    public void ReportsOnStartingThenAtTheIntervalThenOnceOnStopping()
    {
        var reporter = new PositionReporter(interval: 0.1);

        Assert.False(reporter.ShouldReport(Vector3.Zero, 0));
        Assert.True(reporter.ShouldReport(East, 0.01));
        Assert.False(reporter.ShouldReport(East, 0.05));
        Assert.True(reporter.ShouldReport(East, 0.12));
        Assert.True(reporter.ShouldReport(Vector3.Zero, 0.13));
        Assert.False(reporter.ShouldReport(Vector3.Zero, 0.5));
    }

    [Fact]
    public void FallingStraightDownCountsAsMoving()
    {
        var reporter = new PositionReporter(interval: 0.1);
        Assert.True(reporter.ShouldReport(new Vector3(0, -3, 0), 0));
        Assert.True(reporter.ShouldReport(new Vector3(0, -4, 0), 0.1));
    }

    [Fact]
    public void ReportsASharpTurnAtOnce()
    {
        var reporter = new PositionReporter(interval: 0.1, sharpTurnDegrees: 45);
        Assert.True(reporter.ShouldReport(East, 0));
        Assert.False(reporter.ShouldReport(new Vector3(5, 0, 1), 0.02)); // ~11°
        Assert.True(reporter.ShouldReport(new Vector3(0, 0, 5), 0.04));
    }

    [Fact]
    public void ResetReportsTheNextMoveAtOnce()
    {
        var reporter = new PositionReporter(interval: 0.1);
        Assert.True(reporter.ShouldReport(East, 0));
        reporter.Reset();
        Assert.True(reporter.ShouldReport(East, 0.01));
    }
}
