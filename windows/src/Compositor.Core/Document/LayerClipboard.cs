using Compositor.Core.IO;
using Compositor.Core.Model;

namespace Compositor.Core.Document;

/// <summary>A self-contained layer transfer. Encoding at copy time keeps it alive after the source tab closes.</summary>
public sealed class LayerClipboard
{
    private readonly SmartObjectData _contents;
    public int Count { get; }
    private LayerClipboard(SmartObjectData contents, int count) { _contents = contents; Count = count; }

    public static LayerClipboard? Copy(CanvasDocument document, IEnumerable<Guid> selected)
    {
        using var subset = LayerWorkflow.Subset(document, selected);
        if (subset.Layers.Count == 0) return null;
        subset.Channels.Clear(); subset.Guides.Clear(); subset.Selection = DocumentSelection.All;
        // Links that leave the copied set must never acquire a meaning in the destination document.
        var links = subset.Layers.Where(l => l.LinkID is not null).GroupBy(l => l.LinkID).ToDictionary(g => g.Key!.Value, g => g.Count());
        foreach (var layer in subset.Layers)
            if (layer.LinkID is { } link && links[link] < 2) layer.LinkID = null;
        return new LayerClipboard(SmartObjectData.FromDocument(subset), subset.Layers.Count);
    }

    public IReadOnlyList<Guid> Paste(CanvasDocument document, Guid? activeID)
    {
        if (document.Layers.Count + Count > LayerPlacement.MaxLayers) return [];
        using var snapshot = _contents.Open();
        var layers = snapshot.RuntimeLayers();
        var pixels = document.Layers.Concat(layers).Sum(l => (long)(l.Asset?.Width ?? 0) * (l.Asset?.Height ?? 0));
        var masks = document.Layers.Concat(layers).Sum(l => (long)(l.Mask?.Asset.Width ?? 0) * (l.Mask?.Asset.Height ?? 0));
        if (pixels > DocumentLimits.DocumentPixelBudget || masks > DocumentLimits.DocumentPixelBudget) return [];
        var active = document.Layers.FirstOrDefault(l => l.ID == activeID);
        var parent = active is { IsGroup: true } ? active.ID : active?.ParentID;
        if (parent is { } p && LayerProtection.Effective(document, p).HasFlag(LayerLocks.All)) return [];
        var ids = layers.ToDictionary(l => l.ID, _ => Guid.NewGuid());
        var links = layers.Where(l => l.LinkID is not null).Select(l => l.LinkID!.Value).Distinct().ToDictionary(id => id, _ => Guid.NewGuid());
        var roots = new List<Guid>(); var copies = new List<ImageLayer>();
        foreach (var layer in layers)
        {
            var root = layer.ParentID is null || !ids.ContainsKey(layer.ParentID.Value);
            var copy = layer.Copy(ids[layer.ID], layer.Name, root ? parent : ids[layer.ParentID!.Value],
                layer.MaskSourceID is { } clip && ids.TryGetValue(clip, out var mapped) ? mapped : null);
            copy.LinkID = layer.LinkID is { } link ? links[link] : null;
            copies.Add(copy); if (root) roots.Add(copy.ID);
        }
        var index = active is null ? document.Layers.Count : document.Layers.IndexOf(active) + 1;
        document.Layers.InsertRange(index, copies);
        // Ownership of freshly decoded assets passes to the destination. Later pastes decode independent assets.
        snapshot.Images.Clear(); snapshot.Masks.Clear();
        return roots;
    }
}
