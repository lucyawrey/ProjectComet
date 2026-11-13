namespace Comet.Data.Tests;

public class SnowflakeIdsTests
{
    private sealed class StepClock(params DateTimeOffset[] times) : TimeProvider
    {
        private readonly Queue<DateTimeOffset> _times = new(times);
        private DateTimeOffset _last = times[^1];

        public override DateTimeOffset GetUtcNow() => _times.Count > 0 ? _times.Dequeue() : _last = _last.AddMilliseconds(1);
    }

    private static readonly DateTimeOffset T = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IdsCarryTheirTimeAndNode()
    {
        var ids = new SnowflakeIds(37, new StepClock(T));
        var id = ids.Next();
        Assert.True(id > 0);
        Assert.Equal(T, SnowflakeIds.TimeOf(id));
        Assert.Equal(37, SnowflakeIds.NodeOf(id));
    }

    [Fact]
    public void IdsInTheSameMillisecondCountUp()
    {
        var ids = new SnowflakeIds(1, new StepClock(T, T, T));
        var a = ids.Next();
        var b = ids.Next();
        var c = ids.Next();
        Assert.Equal(a + 1, b);
        Assert.Equal(b + 1, c);
    }

    [Fact]
    public void AFullMillisecondWaitsForTheNext()
    {
        var times = Enumerable.Repeat(T, 2049).ToArray();
        var ids = new SnowflakeIds(1, new StepClock(times));
        var made = Enumerable.Range(0, 2049).Select(_ => ids.Next()).ToList();
        Assert.Equal(made.Count, made.Distinct().Count());
        Assert.Equal(T.AddMilliseconds(1), SnowflakeIds.TimeOf(made[^1]));
        Assert.Equal(made.Order(), made);
    }

    [Fact]
    public void ASmallStepBackWaitsInsteadOfRepeating()
    {
        var ids = new SnowflakeIds(1, new StepClock(T, T.AddMilliseconds(-5)));
        var a = ids.Next();
        var b = ids.Next();
        Assert.True(b > a);
    }

    [Fact]
    public void ABigStepBackThrows()
    {
        var ids = new SnowflakeIds(1, new StepClock(T, T.AddSeconds(-2)));
        ids.Next();
        Assert.Throws<InvalidOperationException>(() => ids.Next());
    }

    [Fact]
    public void DifferentNodesNeverCollide()
    {
        var a = new SnowflakeIds(1, new StepClock(T)).Next();
        var b = new SnowflakeIds(2, new StepClock(T)).Next();
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void NodeMustFitItsBits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SnowflakeIds(1024, TimeProvider.System));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SnowflakeIds(-1, TimeProvider.System));
    }

    [Fact]
    public void TheLayoutLastsPastTheYear2160()
    {
        var late = new DateTimeOffset(2160, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var id = new SnowflakeIds(1023, new StepClock(late)).Next();
        Assert.True(id > 0);
        Assert.Equal(late, SnowflakeIds.TimeOf(id));
    }
}
