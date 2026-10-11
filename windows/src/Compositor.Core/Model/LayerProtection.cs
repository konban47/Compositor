using Compositor.Core.IO;
using SkiaSharp;

namespace Compositor.Core.Model;

[Flags]
public enum LayerLocks { None = 0, Transparency = 1, Pixels = 2, Position = 4, All = 8 }

/// <summary>Shared by pointer editing, menus and history, so a lock cannot be bypassed by another tool.</summary>
public static class LayerProtection
{
    public static LayerLocks Effective(CanvasDocument document, Guid id)
        => Effective(document.Layers.ToDictionary(layer => layer.ID), id);

    private static LayerLocks Effective(IReadOnlyDictionary<Guid, ImageLayer> layers, Guid id)
    {
        var result = LayerLocks.None;
        var seen = new HashSet<Guid>();
        while (layers.TryGetValue(id, out var layer) && seen.Add(id))
        {
            result |= layer.Locks;
            if (layer.ParentID is not { } parent) break;
            id = parent;
        }
        return result;
    }

    public static bool CanMove(CanvasDocument document, Guid id) =>
        (Effective(document, id) & (LayerLocks.All | LayerLocks.Position)) == 0;

    public static bool CanPaint(CanvasDocument document, Guid id) =>
        (Effective(document, id) & (LayerLocks.All | LayerLocks.Pixels)) == 0
        && document.Layers.FirstOrDefault(layer => layer.ID == id)?.SmartObject is null;

    public static bool Set(CanvasDocument document, IEnumerable<Guid> ids, LayerLocks locks)
    {
        var wanted = ids.ToHashSet();
        var changed = false;
        foreach (var layer in document.Layers.Where(layer => wanted.Contains(layer.ID)))
        {
            changed |= layer.Locks != locks;
            layer.Locks = locks;
        }
        return changed;
    }

    /// <summary>Enforce protection before recording an edit. Snapshots share immutable pixel assets.</summary>
    public static void Enforce(CanvasDocument before, CanvasDocument after, string operation)
    {
        if (operation is "Layer Locks" or "Channel Edit" or "Restore Snapshot") return;
        // Canvas geometry operations move the document coordinate system, including protected layers.
        if (before.Width != after.Width || before.Height != after.Height
            || operation is "Flip Canvas" or "Rotate Canvas" or "Perspective Crop Tool") return;
        var originals = before.Layers.ToDictionary(layer => layer.ID);
        var current = after.Layers.ToDictionary(layer => layer.ID);
        // Adding or reparenting into a protected group is a layer edit too.
        if (after.Layers.Any(layer => (!originals.TryGetValue(layer.ID, out var old) || old.ParentID != layer.ParentID)
            && layer.ParentID is { } parent && Effective(originals, parent).HasFlag(LayerLocks.All)))
        { after.Adopt(before); return; }
        foreach (var old in before.Layers)
        {
            var protection = Effective(originals, old.ID);
            var layer = current.GetValueOrDefault(old.ID);
            if (layer is null && protection != LayerLocks.None)
            {
                after.Adopt(before); // Deleting a protected group/merge is atomic.
                return;
            }
            if (layer is null) continue;
            if (protection.HasFlag(LayerLocks.All))
            {
                var visible = layer.IsVisible;
                after.Layers[after.Layers.IndexOf(layer)] = old.Clone();
                after.Layers.First(item => item.ID == old.ID).IsVisible = visible;
                continue;
            }
            if (protection.HasFlag(LayerLocks.Position))
            {
                layer.Transform = old.Transform;
                layer.ParentID = old.ParentID;
            }
            if (old.SmartObject is not null && layer.SmartObject is not null && operation != "Update Smart Object")
            { layer.Asset = old.Asset; layer.Shape = old.Shape; layer.Text = old.Text; }
            if (protection.HasFlag(LayerLocks.Pixels))
            {
                layer.Asset = old.Asset; layer.Shape = old.Shape; layer.Text = old.Text;
                layer.Adjustment = old.Adjustment; layer.Effects = old.Effects; layer.Mask = old.Mask;
            }
            else if (!ReferenceEquals(old.Asset, layer.Asset) && layer.Asset is not null
                && (protection.HasFlag(LayerLocks.Transparency) || after.EditChannels != ColorChannels.RGB))
            {
                RestrictPixels(old, layer, protection.HasFlag(LayerLocks.Transparency), after.EditChannels);
            }
        }
        // A protected row stays in its sibling order, even when another row is dragged around it.
        foreach (var old in before.Layers.Where(layer => Effective(originals, layer.ID).HasFlag(LayerLocks.All)))
        {
            var layer = after.Layers.FirstOrDefault(item => item.ID == old.ID);
            if (layer is null) continue;
            var oldSiblings = before.Layers.Where(item => item.ParentID == old.ParentID).Select(item => item.ID).ToList();
            var newSiblings = after.Layers.Where(item => item.ParentID == old.ParentID && originals.ContainsKey(item.ID)).Select(item => item.ID).ToList();
            if (oldSiblings.Count == newSiblings.Count && oldSiblings.IndexOf(old.ID) != newSiblings.IndexOf(old.ID))
            {
                after.Adopt(before); return;
            }
        }
    }

    private static void RestrictPixels(ImageLayer old, ImageLayer layer, bool alphaLocked, ColorChannels channels)
    {
        if (old.Asset is null && alphaLocked) { layer.Asset = null; return; }
        var bitmap = layer.Asset!.Image.Copy();
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var color = bitmap.GetPixel(x, y);
            var original = SKColors.Transparent;
            if (old.Asset is { } asset)
            {
                // Same raster geometry is the common case; placement changes map through document space.
                var point = layer.Transform.Point((x + .5) / bitmap.Width, (y + .5) / bitmap.Height);
                if (old.Transform.InBox(point) is { } local)
                {
                    var ox = Math.Clamp((int)(local.X / old.Transform.Width * asset.Width), 0, asset.Width - 1);
                    var oy = Math.Clamp((int)(local.Y / old.Transform.Height * asset.Height), 0, asset.Height - 1);
                    original = asset.Image.GetPixel(ox, oy);
                }
            }
            bitmap.SetPixel(x, y, new SKColor(channels.HasFlag(ColorChannels.Red) ? color.Red : original.Red,
                channels.HasFlag(ColorChannels.Green) ? color.Green : original.Green,
                channels.HasFlag(ColorChannels.Blue) ? color.Blue : original.Blue,
                alphaLocked || channels != ColorChannels.RGB ? original.Alpha : color.Alpha));
        }
        layer.Asset = ImportedImage.Create(bitmap, layer.Name);
    }
}
