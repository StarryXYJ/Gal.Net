#if BENCHMARK
using System.Diagnostics;
using System.Text.Json;

namespace GalNet.Diagnostics;

/// <summary>Opt-in, bounded CPU wall-clock events. Has no UI or logging dependency.</summary>
public sealed class CpuProfiler
{
    public static CpuProfiler Shared { get; } = new(emitEventPipe:
        Environment.GetEnvironmentVariable("GALNET_BENCHMARK_EVENTPIPE") == "1");
    private readonly object _gate = new();
    private readonly List<CpuEvent> _events = [];
    private readonly long _origin = Stopwatch.GetTimestamp();
    private readonly int _capacity;
    private readonly bool _emitEventPipe;
    private bool _recording = true;
    private int _dropped;

    public CpuProfiler(int capacity = 100_000, bool emitEventPipe = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
        _emitEventPipe = emitEventPipe;
    }

    public IDisposable Measure(string name, string category = "CPU") =>
        new Measurement(this, name, category);

    public void Mark(string name, string category = "CPU") =>
        Add(new(name, category, ElapsedMicroseconds(), null, Environment.CurrentManagedThreadId));

    public void Stop()
    {
        lock (_gate) _recording = false;
    }

    public CpuEvent[] Snapshot()
    {
        lock (_gate) return _events.OrderBy(item => item.TimestampMicroseconds).ToArray();
    }

    public void WriteTrace(Stream destination)
    {
        CpuEvent[] snapshot;
        int dropped;
        lock (_gate)
        {
            snapshot = _events.OrderBy(item => item.TimestampMicroseconds).ToArray();
            dropped = _dropped;
        }
        using var json = new Utf8JsonWriter(destination, new() { Indented = true });
        json.WriteStartObject();
        json.WriteString("displayTimeUnit", "ms");
        json.WriteNumber("droppedEvents", dropped);
        json.WriteStartArray("traceEvents");
        foreach (var item in snapshot)
        {
            json.WriteStartObject();
            json.WriteString("name", item.Name);
            json.WriteString("cat", item.Category);
            json.WriteString("ph", item.DurationMicroseconds.HasValue ? "X" : "i");
            json.WriteNumber("ts", item.TimestampMicroseconds);
            json.WriteNumber("pid", Environment.ProcessId);
            json.WriteNumber("tid", item.ThreadId);
            if (item.DurationMicroseconds is { } duration) json.WriteNumber("dur", duration);
            else json.WriteString("s", "t");
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteEndObject();
    }

    private double ElapsedMicroseconds() =>
        Stopwatch.GetElapsedTime(_origin).TotalMicroseconds;

    private void Add(CpuEvent item)
    {
        lock (_gate)
        {
            if (!_recording) return;
            if (_events.Count < _capacity) _events.Add(item);
            else
            {
                _dropped++;
                return;
            }
        }
        if (_emitEventPipe) CpuTraceEvents.Log.Emit(item);
    }

    private sealed class Measurement : IDisposable
    {
        private CpuProfiler? _owner;
        private readonly string _name;
        private readonly string _category;
        private readonly double _start;
        private readonly int _thread = Environment.CurrentManagedThreadId;

        public Measurement(CpuProfiler owner, string name, string category)
        {
            _owner = owner;
            _name = name;
            _category = category;
            _start = owner.ElapsedMicroseconds();
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null) return;
            owner.Add(new(_name, _category, _start, owner.ElapsedMicroseconds() - _start, _thread));
        }
    }
}

public sealed record CpuEvent(string Name, string Category, double TimestampMicroseconds,
    double? DurationMicroseconds, int ThreadId);
#endif
