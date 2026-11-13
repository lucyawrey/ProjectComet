namespace Comet.Server.Connections;

public sealed class LatestOnlyQueue<T>
    where T : struct
{
    private readonly Dictionary<uint, int> _indexByKey = [];
    private readonly List<(uint Key, T Value)> _items = [];

    public int Count => _items.Count;

    public void Set(uint key, in T value)
    {
        if (_indexByKey.TryGetValue(key, out var index))
        {
            _items[index] = (key, value);
        }
        else
        {
            _indexByKey.Add(key, _items.Count);
            _items.Add((key, value));
        }
    }

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
