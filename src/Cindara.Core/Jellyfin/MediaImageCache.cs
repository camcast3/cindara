namespace Cindara.Core.Jellyfin;

internal sealed class MediaImageCache(TimeProvider? timeProvider = null)
{
    internal const int MaximumBytes = 32 * 1024 * 1024;
    internal const int MaximumEntries = 128;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, byte[] Data)>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, byte[] Data)> _recent = new();
    private int _bytes;
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private DateTimeOffset _expiresAt = (timeProvider ?? TimeProvider.System).GetUtcNow().AddMinutes(5);

    internal int Bytes { get { lock (_gate) { return _bytes; } } }
    internal int Count { get { lock (_gate) { return _entries.Count; } } }

    public byte[]? Get(string key)
    {
        lock (_gate)
        {
            if (_clock.GetUtcNow() >= _expiresAt)
            {
                _entries.Clear();
                _recent.Clear();
                _bytes = 0;
                _expiresAt = _clock.GetUtcNow().AddMinutes(5);
            }

            if (!_entries.TryGetValue(key, out var entry))
            {
                return null;
            }

            _recent.Remove(entry);
            _recent.AddFirst(entry);
            return entry.Value.Data;
        }
    }

    public void Add(string key, byte[] data)
    {
        lock (_gate)
        {
            if (data.Length > MaximumBytes || _entries.ContainsKey(key))
            {
                return;
            }

            while (_recent.Last is { } oldest
                && (_entries.Count >= MaximumEntries || _bytes + data.Length > MaximumBytes))
            {
                _recent.RemoveLast();
                _entries.Remove(oldest.Value.Key);
                _bytes -= oldest.Value.Data.Length;
            }

            _entries.Add(key, _recent.AddFirst((key, data)));
            _bytes += data.Length;
        }
    }
}
