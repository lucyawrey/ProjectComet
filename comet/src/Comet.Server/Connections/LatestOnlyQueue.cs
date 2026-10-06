namespace Comet.Server.Connections;

/// <summary>
/// Pending state messages for one connection, keyed by entity. A newer message for the same key
/// replaces an unsent older one, so a stalled connection catches up with the latest state instead of
/// replaying stale updates. Not thread-safe: used from the tick thread only.
/// </summary>
public sealed class LatestOnlyQueue<T>
    where T : struct
{
    private readonly Dictionary<uint, int> _indexByKey = [];
    private readonly List<(uint Key, T Value)> _items = [];
    private int _replaced;

    public int Count => _items.Count;

    /// <summary>How many queued messages were replaced by newer ones since the last call.</summary>
    public int TakeReplacedCount()
    {
        var replaced = _replaced;
        _replaced = 0;
        return replaced;
    }

    public void Set(uint key, in T value)
    {
        if (_indexByKey.TryGetValue(key, out var index))
        {
            _items[index] = (key, value);
            _replaced++;
        }
        else
        {
            _indexByKey.Add(key, _items.Count);
            _items.Add((key, value));
        }
    }

    /// <summary>Hands every queued message to <paramref name="write"/> in the order first queued, then empties the queue.</summary>
    public void Drain<TState>(TState state, Action<TState, T> write)
    {
        foreach (var (_, value) in _items)
        {
            write(state, value);
        }
        _items.Clear();
        _indexByKey.Clear();
    }
}
