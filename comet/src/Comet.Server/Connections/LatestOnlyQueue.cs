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

    /// <summary>Drops the queued message for <paramref name="key"/>, if any, e.g. when its entity is despawned.</summary>
    public void Remove(uint key)
    {
        if (!_indexByKey.Remove(key, out var index))
        {
            return;
        }

        _items.RemoveAt(index);
        for (var i = index; i < _items.Count; i++)
        {
            _indexByKey[_items[i].Key] = i;
        }
    }

    /// <summary>
    /// Hands queued messages to <paramref name="write"/> in the order first queued, until it returns false
    /// (it had no room for that one). The messages written leave the queue; the rest stay, in order.
    /// </summary>
    public void Drain<TState>(TState state, Func<TState, T, bool> write)
    {
        var written = 0;
        while (written < _items.Count && write(state, _items[written].Value))
        {
            written++;
        }

        if (written == _items.Count)
        {
            _items.Clear();
            _indexByKey.Clear();
            return;
        }

        for (var i = 0; i < written; i++)
        {
            _indexByKey.Remove(_items[i].Key);
        }
        _items.RemoveRange(0, written);
        for (var i = 0; i < _items.Count; i++)
        {
            _indexByKey[_items[i].Key] = i;
        }
    }
}
