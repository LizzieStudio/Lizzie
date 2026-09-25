using System;
using System.Collections.Generic;
using System.Diagnostics;

/// <summary>
/// The most recent durations of instrumented operations, shown in the debug console.
/// Time an operation with <c>using var _ = DebugTimings.Measure("Label");</c>.
/// </summary>
public static class DebugTimings
{
    public readonly record struct Entry(string Label, double Milliseconds, int EventCount);

    private const int Capacity = 20;

    private static readonly Queue<Entry> _recent = new();

    /// <summary>Oldest first.</summary>
    public static IReadOnlyCollection<Entry> Recent => _recent;

    /// <summary>Records the time until the returned scope is disposed.</summary>
    public static Scope Measure(string label) => new(label, Stopwatch.GetTimestamp());

    public readonly struct Scope(string label, long start) : IDisposable
    {
        public void Dispose()
        {
            if (_recent.Count == Capacity)
                _recent.Dequeue();
            _recent.Enqueue(
                new Entry(
                    label,
                    Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                    EventSynchronizer.Instance?.EventLog.Count ?? 0
                )
            );
        }
    }
}
