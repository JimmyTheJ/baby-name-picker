using System.Threading.Channels;

namespace BabyNamePicker.Services;

public sealed record AdminLogEntry(
    long Id,
    DateTime Utc,
    string Level,
    string Source,
    string Message);

public sealed class AdminLogHub
{
    private const int Capacity = 500;
    private readonly object _gate = new();
    private readonly Queue<AdminLogEntry> _buffer = new();
    private long _nextId = 1;
    private readonly Channel<AdminLogEntry> _live = Channel.CreateUnbounded<AdminLogEntry>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    public void Write(string level, string source, string message)
    {
        AdminLogEntry entry;
        lock (_gate)
        {
            entry = new AdminLogEntry(_nextId++, DateTime.UtcNow, level, source, message);
            _buffer.Enqueue(entry);
            while (_buffer.Count > Capacity)
            {
                _buffer.Dequeue();
            }
        }

        _live.Writer.TryWrite(entry);
    }

    public void Info(string source, string message) => Write("Info", source, message);
    public void Warning(string source, string message) => Write("Warning", source, message);
    public void Error(string source, string message) => Write("Error", source, message);

    public IReadOnlyList<AdminLogEntry> Snapshot(int? limit = null, long? afterId = null)
    {
        lock (_gate)
        {
            IEnumerable<AdminLogEntry> query = _buffer;
            if (afterId.HasValue)
            {
                query = query.Where(e => e.Id > afterId.Value);
            }

            if (limit is > 0)
            {
                query = query.TakeLast(limit.Value);
            }

            return query.ToList();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _buffer.Clear();
        }
    }

    public ChannelReader<AdminLogEntry> Reader => _live.Reader;
}
