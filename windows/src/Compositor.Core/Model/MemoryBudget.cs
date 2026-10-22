using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Compositor.Core.Model;

/// <summary>Process-wide admission control, including native memory and concurrent work reservations.</summary>
public sealed class MemoryBudget
{
    private readonly object _gate = new();
    private readonly Func<long> _committed;
    private long _reserved;
    private readonly bool _cacheMeasurements;
    private long _measured, _charged, _measuredAt;
    private static readonly AsyncLocal<MemoryBudget?> Override = new();
    public static MemoryBudget Shared { get; } = new(DefaultLimit(), () =>
    {
        using var process = Process.GetCurrentProcess();
        return process.PrivateMemorySize64;
    }, cacheMeasurements: true);
    public static MemoryBudget Current => Override.Value ?? Shared;
    public long Limit { get; }
    public long ReservedBytes { get { lock (_gate) return _reserved; } }
    public MemoryBudget(long limit, Func<long> committed, bool cacheMeasurements = false)
    {
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
        Limit = limit; _committed = committed; _cacheMeasurements = cacheMeasurements;
    }
    private static long DefaultLimit()
    {
        var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var limit = Math.Max(256L << 20, available * 3 / 5);
        return long.TryParse(Environment.GetEnvironmentVariable("COMPOSITOR_MEMORY_MB"), out var mb) && mb >= 256
            ? Math.Min(limit, checked(Math.Min(mb, long.MaxValue >> 20) << 20)) : limit;
    }
    public static long PixelBytes(int width, int height, int bytesPerPixel)
    {
        if (width <= 0 || height <= 0 || bytesPerPixel <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        return checked((long)width * height * bytesPerPixel);
    }
    public void Check(long bytes)
    {
        lock (_gate)
        {
            // Windows process accounting is expensive enough to dominate small tile allocations.
            // Between samples, charge every completed reservation in full; this is conservative
            // even when that reservation was only an estimate or its allocation was freed.
            var now = Stopwatch.GetTimestamp();
            if (!_cacheMeasurements || _measuredAt == 0 || Stopwatch.GetElapsedTime(_measuredAt, now).TotalMilliseconds >= 16)
            { _measured = Math.Max(0, _committed()); _charged = 0; _measuredAt = now; }
            if (_cacheMeasurements && bytes >= 0 && bytes > Limit - _measured - _charged - _reserved)
            { _measured = Math.Max(0, _committed()); _charged = 0; _measuredAt = now; }
            if (bytes < 0 || bytes > Limit - _measured - _charged - _reserved)
                throw new OutOfMemoryException("The operation exceeds Compositor's memory budget. Close other documents or reduce the image size.");
        }
    }
    public IDisposable Reserve(long bytes)
    {
        lock (_gate) { Check(bytes); _reserved = checked(_reserved + bytes); }
        return new Release(() => { lock (_gate) { _reserved -= bytes; if (_cacheMeasurements) _charged += Math.Min(bytes, Limit - _charged); } });
    }
    public static T[] Allocate<T>(int count) where T : unmanaged
    {
        var bytes = checked((long)count * Unsafe.SizeOf<T>());
        using var reservation = bytes >= 65536 || Override.Value is not null ? Current.Reserve(bytes) : null;
        return new T[count];
    }
    /// <summary>Scoped deterministic fault injection; async work inherits the same budget.</summary>
    public IDisposable Enter()
    {
        var previous = Override.Value; Override.Value = this;
        return new Release(() => Override.Value = previous);
    }
    private sealed class Release(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
