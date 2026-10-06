using Comet.Server.Connections;

namespace Comet.Server.Tests;

public class LatestOnlyQueueTests
{
    [Fact]
    public void NewerValueReplacesOlderInPlace()
    {
        var queue = new LatestOnlyQueue<int>();
        queue.Set(key: 1, 10);
        queue.Set(key: 2, 20);
        queue.Set(key: 1, 11);

        var drained = new List<int>();
        queue.Drain(drained, static (list, value) => list.Add(value));

        Assert.Equal([11, 20], drained);
        Assert.Equal(1, queue.TakeReplacedCount());
        Assert.Equal(0, queue.TakeReplacedCount());
    }

    [Fact]
    public void DrainEmptiesTheQueue()
    {
        var queue = new LatestOnlyQueue<int>();
        queue.Set(key: 1, 10);
        queue.Drain(0, static (_, _) => { });

        queue.Set(key: 1, 12);

        Assert.Equal(1, queue.Count);
        Assert.Equal(0, queue.TakeReplacedCount());
    }
}
