using Compositor.Core.Model;
using Compositor.Core.Pixels;

namespace Compositor.Core.Rendering;

/// <summary>Bounded effects reuse within one immutable render snapshot, never across document edits.</summary>
public sealed class RenderCache : IDisposable
{
    private static readonly AsyncLocal<RenderCache?> Active = new();
    internal static RenderCache? Current => Active.Value;
    private readonly RenderCache? _previous = Active.Value;
    private readonly Dictionary<ImageLayer, EffectRaster> _effects = [];
    private readonly long _limit;
    public long RetainedBytes { get; private set; }
    public RenderCache(long? limit = null)
    {
        _limit = limit ?? Math.Min(128L << 20, MemoryBudget.Current.Limit / 16);
        Active.Value = this;
    }
    internal bool TryGet(ImageLayer layer, out EffectRaster? raster) => _effects.TryGetValue(layer, out raster);
    internal bool Store(ImageLayer layer, EffectRaster raster)
    {
        var bytes = (long)raster.Pixels.RowBytes * raster.Pixels.Height;
        if (bytes > _limit - RetainedBytes) return false;
        _effects.Add(layer, raster); RetainedBytes += bytes; return true;
    }
    public void Dispose()
    {
        foreach (var raster in _effects.Values) raster.Dispose();
        _effects.Clear(); RetainedBytes = 0; Active.Value = _previous;
    }
}
