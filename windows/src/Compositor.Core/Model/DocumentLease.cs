namespace Compositor.Core.Model;

/// <summary>A private metadata snapshot with explicit lifetime leases on shared immutable pixels.</summary>
public sealed class DocumentLease : IDisposable
{
    public CanvasDocument Document { get; }
    private readonly List<IDisposable> _leases = [];
    public DocumentLease(CanvasDocument source)
    {
        Document = source.Clone();
        try
        {
            foreach (var asset in Document.Layers.SelectMany(l => new[] { l.Asset, l.Mask?.Asset })
                .Concat(Document.Channels.Select(c => c.Asset)).OfType<ImportedImage>().Distinct())
                _leases.Add(asset.Retain());
        }
        catch { Dispose(); throw; }
    }
    public void Dispose()
    {
        foreach (var lease in _leases) lease.Dispose();
        _leases.Clear(); Document.Dispose();
    }
}
