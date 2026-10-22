using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Rendering;

/// <summary>Bounded exact CPU tiles for one immutable document/display revision. Leases protect
/// in-flight draws from eviction and from replacement of the revision on the UI thread.</summary>
public sealed class ViewportTileCache : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<SKRectI, LinkedListNode<Entry>> _tiles = [];
    private readonly LinkedList<Entry> _recent = [];
    private readonly long _limit;
    private long _bytes, _hits, _rendered;
    private bool _disposed;
    public ViewportTileCache(long? limit = null) => _limit = Math.Max(0, limit ?? Math.Min(128L << 20, MemoryBudget.Current.Limit / 8));
    public long RetainedBytes { get { lock (_gate) return _bytes; } }
    public long Hits => Interlocked.Read(ref _hits);
    public long RenderedTiles => Interlocked.Read(ref _rendered);

    private sealed class Entry(SKRectI region, SKBitmap pixels)
    {
        private int _references = 1;
        public SKRectI Region { get; } = region;
        public SKBitmap Pixels { get; } = pixels;
        public long Bytes => (long)Pixels.RowBytes * Pixels.Height;
        public void Retain() => Interlocked.Increment(ref _references);
        public void Release() { if (Interlocked.Decrement(ref _references) == 0) Pixels.Dispose(); }
    }
    public sealed class Lease : IDisposable
    {
        private Action? _release;
        public SKBitmap Pixels { get; }
        internal Lease(SKBitmap pixels, Action release) { Pixels = pixels; _release = release; }
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
    public Lease? TryAcquire(SKRectI region)
    {
        lock (_gate)
        {
            if (!_tiles.TryGetValue(region, out var found)) return null;
            _recent.Remove(found); _recent.AddLast(found); found.Value.Retain(); Interlocked.Increment(ref _hits);
            return new Lease(found.Value.Pixels, found.Value.Release);
        }
    }
    public Lease Acquire(SKRectI region, Func<SKBitmap> render)
    {
        if (TryAcquire(region) is { } cached) return cached;
        // Rendering never holds the cache lock: replacing a revision must not block the UI.
        var pixels = render(); pixels.SetImmutable();
        var made = new Entry(region, pixels); var chosen = made;
        Interlocked.Increment(ref _rendered);
        List<Entry> evicted = [];
        lock (_gate)
        {
            if (!_disposed && _tiles.TryGetValue(region, out var found))
            {
                chosen = found.Value; chosen.Retain(); evicted.Add(made);
                _recent.Remove(found); _recent.AddLast(found); Interlocked.Increment(ref _hits);
            }
            else if (!_disposed && made.Bytes <= _limit)
            {
                while (_bytes + made.Bytes > _limit && _recent.First is { } oldest)
                {
                    _recent.RemoveFirst(); _tiles.Remove(oldest.Value.Region);
                    _bytes -= oldest.Value.Bytes; evicted.Add(oldest.Value);
                }
                made.Retain(); _tiles.Add(region, _recent.AddLast(made)); _bytes += made.Bytes;
            }
        }
        foreach (var entry in evicted) entry.Release();
        return new Lease(chosen.Pixels, chosen.Release);
    }
    /// <summary>A new revision borrows only proven unchanged tiles. Workers on the old revision
    /// cannot publish into the new one, even if cancellation races a native render.</summary>
    public ViewportTileCache Fork(SKRectI? damaged)
    {
        var next = new ViewportTileCache(_limit);
        if (damaged is null) return next;
        lock (_gate)
        {
            foreach (var entry in _recent)
            {
                if (!damaged.Value.IsEmpty && entry.Region.IntersectsWith(damaged.Value)) continue;
                entry.Retain(); next._tiles.Add(entry.Region, next._recent.AddLast(entry)); next._bytes += entry.Bytes;
            }
        }
        return next;
    }
    public void Dispose()
    {
        Entry[] retired;
        lock (_gate)
        {
            _disposed = true; retired = _recent.ToArray(); _recent.Clear(); _tiles.Clear(); _bytes = 0;
        }
        foreach (var entry in retired) entry.Release();
    }
}
