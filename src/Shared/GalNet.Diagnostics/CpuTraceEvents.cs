#if BENCHMARK
using System.Diagnostics.Tracing;

namespace GalNet.Diagnostics;

/// <summary>Optional EventPipe mirror for correlating CPU phases with runtime stacks.</summary>
[EventSource(Name = "GalNet-CPU")]
internal sealed class CpuTraceEvents : EventSource
{
    public static CpuTraceEvents Log { get; } = new();

    [NonEvent]
    internal void Emit(CpuEvent item)
    {
        if (IsEnabled()) Record(item.Name, item.Category, item.TimestampMicroseconds,
            item.DurationMicroseconds ?? -1, item.ThreadId);
    }

    [Event(1, Level = EventLevel.Informational)]
    public void Record(string name, string category, double timestampMicroseconds,
        double durationMicroseconds, int threadId) =>
        WriteEvent(1, name, category, timestampMicroseconds, durationMicroseconds, threadId);
}
#endif
