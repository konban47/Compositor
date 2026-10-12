using Compositor.Core.Format;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Document;

/// <summary>Layer menu operations, independent of UI and bracketed by the caller's undo transaction.</summary>
public static class LayerMenuEdits
{
    public static bool Arrange(CanvasDocument doc, IReadOnlyCollection<Guid> ids, string operation)
    {
        if (ids.Count == 0 || ids.Any(id => !LayerProtection.CanMove(doc, id))) return false;
        var roots = doc.Layers.Where(l => ids.Contains(l.ID) && !ids.Any(p => p != l.ID && doc.Descendants(p).Contains(l.ID))).ToArray();
        var changed = false;
        foreach (var group in roots.GroupBy(l => l.ParentID))
        {
            var siblings = doc.Layers.Where(l => l.ParentID == group.Key).ToList();
            var original = siblings.ToArray(); var selected = group.Select(l => l.ID).ToHashSet();
            if (operation is "Front" or "Back")
            {
                var picked = siblings.Where(l => selected.Contains(l.ID)).ToArray(); siblings.RemoveAll(l => selected.Contains(l.ID));
                siblings.InsertRange(operation == "Front" ? siblings.Count : 0, picked);
            }
            else if (operation == "Reverse")
            {
                var reversed = siblings.Where(l => selected.Contains(l.ID)).Reverse().ToArray(); var next = 0;
                for (var i = 0; i < siblings.Count; i++) if (selected.Contains(siblings[i].ID)) siblings[i] = reversed[next++];
            }
            else if (operation == "Forward")
            { for (var i = siblings.Count - 2; i >= 0; i--) if (selected.Contains(siblings[i].ID) && !selected.Contains(siblings[i + 1].ID)) (siblings[i], siblings[i + 1]) = (siblings[i + 1], siblings[i]); }
            else if (operation == "Backward")
            { for (var i = 1; i < siblings.Count; i++) if (selected.Contains(siblings[i].ID) && !selected.Contains(siblings[i - 1].ID)) (siblings[i], siblings[i - 1]) = (siblings[i - 1], siblings[i]); }
            if (original.SequenceEqual(siblings)) continue;
            var slots = original.Select(l => doc.Layers.IndexOf(l)).Order().ToArray();
            for (var i = 0; i < slots.Length; i++) doc.Layers[slots[i]] = siblings[i];
            changed = true;
        }
        return changed;
    }

    public static bool TransparencyMask(CanvasDocument doc, Guid id)
    {
        if (!LayerProtection.CanPaint(doc, id) || doc.Layers.FirstOrDefault(l => l.ID == id) is not { Asset: { } asset, Mask: null } layer) return false;
        var mask = Bitmaps.Allocate(Bitmaps.MaskInfo(asset.Width, asset.Height));
        var pixels = mask.GetPixelSpan();
        using var work = DeepPixels.From(asset.WorkingImage);
        var rgba = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(work.GetPixelSpan());
        for (var y = 0; y < asset.Height; y++) for (var x = 0; x < asset.Width; x++)
        { var at = y * work.RowBytes / 4 + x * 4; pixels[y * mask.RowBytes + x] = (byte)Math.Clamp(Math.Round(rgba[at + 3] * 255), 0, 255); rgba[at + 3] = 1; }
        layer.Asset = asset.Deep is null ? ImportedImage.Create(DeepPixels.Preview(work), layer.Name) : ImportedImage.CreateDeep(work.Copy(), layer.Name, asset.BitDepth);
        layer.Mask = Model.LayerMask.AssetFrom(mask); layer.Text = null; layer.Shape = null; layer.SmartObject = null; return true;
    }

    public static bool Matte(CanvasDocument doc, Guid id, bool white)
    {
        if (!LayerProtection.CanPaint(doc, id) || doc.Layers.FirstOrDefault(l => l.ID == id) is not { Asset: { } asset } layer) return false;
        using var pixels = DeepPixels.From(asset.WorkingImage);
        var rgba = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(pixels.GetPixelSpan());
        for (var y = 0; y < pixels.Height; y++) for (var x = 0; x < pixels.Width; x++)
        {
            var at = y * pixels.RowBytes / 4 + x * 4; var alpha = rgba[at + 3]; if (alpha <= 0 || alpha >= 1) continue;
            for (var c = 0; c < 3; c++) rgba[at + c] = Math.Max(0, (rgba[at + c] - (white ? 1 - alpha : 0)) / alpha);
        }
        return ImageAdjustments.SetPixels(doc, layer, pixels, selection: false);
    }

    public static bool Defringe(CanvasDocument doc, Guid id, int radius)
    {
        if (radius is < 1 or > 100 || !LayerProtection.CanPaint(doc, id)
            || doc.Layers.FirstOrDefault(l => l.ID == id) is not { Asset: { } asset } layer) return false;
        using var work = DeepPixels.From(asset.WorkingImage);
        var data = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(work.GetPixelSpan());
        var width = work.Width; var height = work.Height; var distance = new byte[width * height];
        var sources = new int[distance.Length]; Array.Fill(sources, -1);
        var queue = new Queue<int>();
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var index = y * width + x;
            if (data[y * work.RowBytes / 4 + x * 4 + 3] < .99f) continue;
            sources[index] = index;
            // Queue only the opaque boundary, avoiding one queue entry per interior pixel.
            if (x > 0 && data[y * work.RowBytes / 4 + (x - 1) * 4 + 3] < .99f || x + 1 < width && data[y * work.RowBytes / 4 + (x + 1) * 4 + 3] < .99f
                || y > 0 && data[(y - 1) * work.RowBytes / 4 + x * 4 + 3] < .99f || y + 1 < height && data[(y + 1) * work.RowBytes / 4 + x * 4 + 3] < .99f) queue.Enqueue(index);
        }
        while (queue.TryDequeue(out var index))
        {
            if (distance[index] >= radius) continue;
            var x = index % width; var y = index / width;
            foreach (var next in new[] { x > 0 ? index - 1 : -1, x + 1 < width ? index + 1 : -1, y > 0 ? index - width : -1, y + 1 < height ? index + width : -1 })
            { if (next < 0 || sources[next] >= 0) continue; sources[next] = sources[index]; distance[next] = (byte)(distance[index] + 1); queue.Enqueue(next); }
        }
        var changed = false;
        for (var i = 0; i < sources.Length; i++)
        {
            if (sources[i] < 0 || sources[i] == i) continue;
            var at = i / width * work.RowBytes / 4 + i % width * 4;
            if (data[at + 3] <= 0) continue;
            var from = sources[i] / width * work.RowBytes / 4 + sources[i] % width * 4;
            data.Slice(from, 3).CopyTo(data.Slice(at, 3)); changed = true;
        }
        return changed && ImageAdjustments.SetPixels(doc, layer, work, selection: false);
    }

    public static Guid? CombineShapes(CanvasDocument doc, IReadOnlyCollection<Guid> ids, SKPathOp operation)
    {
        if (!CanCombineShapes(doc, ids)) return null;
        var layers = doc.Layers.Where(l => ids.Contains(l.ID)).ToArray();
        using var combined = new SKPathBuilder();
        foreach (var layer in layers)
        {
            var s = layer.LiveShape!; using var builder = new SKPathBuilder();
            if (s.Kind == ShapeKind.Path) { using var parsed = SKPath.ParseSvgPathData(s.Path); if (parsed is null) return null; builder.AddPath(parsed); }
            else if (s.Kind == ShapeKind.Ellipse) builder.AddOval(SKRect.Create(1, 1));
            else if (s.Kind == ShapeKind.Rectangle) builder.AddRoundRect(SKRect.Create(1, 1), (float)(s.CornerRadius / layer.Transform.Width), (float)(s.CornerRadius / layer.Transform.Height));
            else return null;
            using var path = builder.Detach();
            path.Transform(BrushEdits.PixelToDocument(layer.Transform, 1, 1));
            if (layer == layers[0]) combined.AddPath(path);
            else { using var before = combined.Snapshot(); using var result = before.Op(path, operation); if (result is null) return null; combined.Reset(); combined.AddPath(result); }
        }
        using var finalPath = combined.Detach();
        var bounds = finalPath.TightBounds;
        if (bounds.IsEmpty) return null;
        var box = SKRectI.Create((int)Math.Floor(bounds.Left), (int)Math.Floor(bounds.Top), Math.Max(1, (int)Math.Ceiling(bounds.Right) - (int)Math.Floor(bounds.Left)), Math.Max(1, (int)Math.Ceiling(bounds.Bottom) - (int)Math.Floor(bounds.Top)));
        finalPath.Transform(new SKMatrix(1f / box.Width, 0, -box.Left / (float)box.Width, 0, 1f / box.Height, -box.Top / (float)box.Height, 0, 0, 1));
        var style = System.Text.Json.JsonSerializer.Deserialize<LayerShapeStyle>(System.Text.Json.JsonSerializer.Serialize(layers[^1].LiveShape))!;
        style.Kind = ShapeKind.Path; style.Path = finalPath.ToSvgPathData();
        var made = ShapeEdits.Add(doc, style, box, layers[^1].ID); if (made is null) return null;
        var resultLayer = doc.Layers.First(l => l.ID == made); resultLayer.Opacity = layers[^1].Opacity; resultLayer.FillOpacity = layers[^1].FillOpacity; resultLayer.BlendMode = layers[^1].BlendMode;
        doc.Layers.RemoveAll(l => ids.Contains(l.ID));
        foreach (var l in doc.Layers) if (l.MaskSourceID is { } source && ids.Contains(source)) l.MaskSourceID = made;
        return made;
    }

    public static bool CanCombineShapes(CanvasDocument doc, IReadOnlyCollection<Guid> ids)
    {
        var layers = doc.Layers.Where(l => ids.Contains(l.ID)).ToArray();
        return layers.Length >= 2 && layers.Length == ids.Count && layers.Select(l => l.ParentID).Distinct().Count() == 1
            && layers.All(l => l.LiveShape is { Kind: ShapeKind.Rectangle or ShapeKind.Ellipse or ShapeKind.Path }
                && LayerProtection.CanPaint(doc, l.ID) && l.Mask is null && l.Effects is null
                && l.Blending is not { IsDefault: false } && l.MaskSourceID is null);
    }

    public static bool UnpackSmartObject(CanvasDocument doc, Guid id)
    {
        if (!LayerWorkflow.Editable(doc, [id]) || doc.Layers.FirstOrDefault(l => l.ID == id) is not { SmartObject: { } smart } layer) return false;
        using var snapshot = smart.Open(); using var source = snapshot.ToDocument();
        if (source.Image.Background is { } color)
        {
            var pixels = Bitmaps.Allocate(Bitmaps.ColorInfo(source.Width, source.Height)); pixels.Erase((SKColor)color);
            source.Layers.Insert(0, new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "Background"),
                new LayerTransform(0, 0, source.Width, source.Height), "Background"));
        }
        var transfer = LayerClipboard.Copy(source, source.Layers.Select(l => l.ID)); if (transfer is null) return false;
        var roots = transfer.Paste(doc, id); if (roots.Count == 0) return false;
        var original = TransformEdits.Originals(doc, roots);
        TransformEdits.Carry(doc, original, new LayerTransform(0, 0, source.Width, source.Height), layer.Transform);
        foreach (var root in roots) doc.Layers.First(l => l.ID == root).ParentID = id;
        // A bounded, isolated group preserves the embedded canvas edge and applies opacity once.
        layer.IsGroup = true; layer.Container = LayerContainer.Frame; layer.Asset = null;
        layer.SmartObject = null; layer.Text = null; layer.Shape = null; return true;
    }
}
