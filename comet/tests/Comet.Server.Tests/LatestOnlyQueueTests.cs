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
        queue.Drain(drained, static (list, value) => { list.Add(value); return true; });

        Assert.Equal([11, 20], drained);
        Assert.Equal(1, queue.TakeReplacedCount());
        Assert.Equal(0, queue.TakeReplacedCount());
    }

    [Fact]
    public void DrainEmptiesTheQueue()
    {
        var queue = new LatestOnlyQueue<int>();
        queue.Set(key: 1, 10);
        queue.Drain(0, static (_, _) => true);

        queue.Set(key: 1, 12);

        Assert.Equal(1, queue.Count);
        Assert.Equal(0, queue.TakeReplacedCount());
    }

    [Fact]
    public void RemoveDropsOneKeyAndKeepsTheOthersInOrder()
    {
        var queue = new LatestOnlyQueue<int>();
        queue.Set(key: 1, 10);
        queue.Set(key: 2, 20);
        queue.Set(key: 3, 30);
        queue.Remove(2);
        queue.Remove(9);
        queue.Set(key: 3, 31);

        var drained = new List<int>();
        queue.Drain(drained, static (list, value) => { list.Add(value); return true; });

        Assert.Equal([10, 31], drained);
    }

    [Fact]
    public void DrainStopsWhenWriteHasNoRoomAndKeepsTheRest()
    {
        var queue = new LatestOnlyQueue<int>();
        queue.Set(key: 1, 10);
        queue.Set(key: 2, 20);
        queue.Set(key: 3, 30);

        var drained = new List<int>();
        queue.Drain(drained, static (list, value) =>
        {
            if (list.Count == 1)
            {
                return false;
            }
            list.Add(value);
            return true;
        });
        queue.Set(key: 3, 31);
        queue.Set(key: 1, 12);
        queue.Drain(drained, static (list, value) => { list.Add(value); return true; });

        Assert.Equal([10, 20, 31, 12], drained);
        Assert.Equal(1, queue.TakeReplacedCount());
    }
}
