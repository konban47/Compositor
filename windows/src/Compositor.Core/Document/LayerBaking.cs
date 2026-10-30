using System.Runtime.InteropServices;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>
/// What File › Scripts › Flatten All Masks and Flatten All Layer Effects do to groups and animated layers. A group's
/// mask that is all black or white is multiplied into every layer inside, which keeps the layers; a soft one, or a
/// group's effects, can only be kept by drawing the group as one layer, so the group becomes that layer. An animated
/// (GIF) layer's effects are drawn into every frame, within the animation's own frame.
/// </summary>
public static class LayerBaking
{
    /// <summary>A group's mask applied; false when there is none or the group is locked.</summary>
    public static bool FlattenGroupMask(CanvasDocument document, Guid groupID, List<string> notes)
    {
        if (document.Layers.FirstOrDefault(l => l.ID == groupID) is not { IsGroup: true, Mask: { } mask } group) return false;
        if (!LayerWorkflow.Editable(document, [groupID])) return false;
        if (!mask.IsEnabled) { group.Mask = null; return true; }
        using var coverage = Coverage(document, group, mask);
        var inside = document.Descendants(groupID).Select(id => document.Layers.First(l => l.ID == id)).Where(l => !l.IsGroup).ToArray();
        var hard = IsHard(coverage) && mask.Feather <= 0 && mask.Density >= 1;
        if (hard && inside.All(l => l.Asset is not null && l.Adjustment is null && l.Gif is null && l.Effects is null && !Transformed(l)))
        {
            foreach (var layer in inside)
            {
                var asset = layer.Asset!;
                using var image = DeepPixels.From(asset.WorkingImage);
                var rgba = MemoryMarshal.Cast<byte, float>(image.GetPixelSpan());
                var (ox, oy) = ((int)Math.Round(layer.Transform.X), (int)Math.Round(layer.Transform.Y));
                for (var y = 0; y < image.Height; y++) for (var x = 0; x < image.Width; x++)
                {
                    var dx = x + ox; var dy = y + oy;
                    var keep = dx >= 0 && dy >= 0 && dx < coverage.Width && dy < coverage.Height ? coverage.GetPixelSpan()[dy * coverage.RowBytes + dx] / 255f : 0;
                    rgba[y * image.RowBytes / 4 + x * 4 + 3] *= keep;
                }
                layer.Asset = asset.Deep is null ? ImportedImage.Create(DeepPixels.Preview(image), layer.Name) : ImportedImage.CreateDeep(image.Copy(), layer.Name, asset.BitDepth);
            }
            group.Mask = null;
            return true;
        }
        notes.Add($"{group.Name}: merged into one layer to keep its soft mask.");
        return Merge(document, group, keepEffects: true);
    }

    /// <summary>A group's effects drawn in: the group, its mask and its effects become one layer.</summary>
    public static bool FlattenGroupEffects(CanvasDocument document, Guid groupID, List<string> notes)
    {
        if (document.Layers.FirstOrDefault(l => l.ID == groupID) is not { IsGroup: true, Effects: not null } group) return false;
        if (!LayerWorkflow.Editable(document, [groupID])) return false;
        notes.Add($"{group.Name}: merged into one layer to draw in its effects.");
        return Merge(document, group, keepEffects: false);
    }

    /// <summary>An animated layer's effects drawn into each of its frames, cut to the animation's frame.</summary>
    public static bool FlattenGifEffects(CanvasDocument document, Guid layerID, List<string> notes)
    {
        if (document.Layers.FirstOrDefault(l => l.ID == layerID) is not { Gif: { } gif, Effects: not null, Asset: { } asset } layer) return false;
        if (!LayerWorkflow.Editable(document, [layerID])) return false;
        var t = layer.Transform;
        if (t.Rotation != 0 || t.FlipX || t.FlipY) { notes.Add($"{layer.Name}: a turned or flipped animation keeps its effects live."); return false; }
        var frames = gif.Frames;
        MemoryBudget.Current.Check(checked((long)asset.Width * asset.Height * frames.Count * 8));
        var region = SKRectI.Round(SKRect.Create((float)t.X, (float)t.Y, (float)t.Width, (float)t.Height));
        if (region.Width < 1 || region.Height < 1) return false;
        var edited = gif.Edited.ToBuilder(); var created = new List<ImportedImage>();
        var clipped = false;
        try
        {
            using var isolated = LayerWorkflow.Subset(document, [layerID]);
            isolated.Image = isolated.Image with { Background = null };
            var copy = isolated.Layers.Single(l => l.ID == layerID);
            copy.IsVisible = true; copy.Opacity = 1; copy.BlendMode = Format.LayerBlendMode.Normal; copy.ParentID = null;
            foreach (var frame in frames)
            {
                var decoded = gif.Decode(frame.Index, layer.Name);
                try
                {
                    copy.ShowGifFrame(frame.Index, decoded);
                    using var drawn = DocumentRenderer.RenderRegion(isolated, region);
                    if (!clipped) { using var whole = DocumentRenderer.Render(isolated); clipped = Outside(whole, region); }
                    using var sized = drawn.Width == asset.Width && drawn.Height == asset.Height ? null
                        : drawn.Resize(Bitmaps.ColorInfo(asset.Width, asset.Height), new SKSamplingOptions(SKFilterMode.Linear));
                    var baked = ImportedImage.Create((sized ?? drawn).Copy(), layer.Name);
                    created.Add(baked); edited[frame.Index] = baked;
                }
                finally { if (!gif.Edited.ContainsKey(frame.Index)) decoded.Dispose(); }
            }
            var selected = layer.GifFrameIndex;
            layer.Gif = gif with { Edited = edited.ToImmutable(), SelectedFrameIndex = selected };
            layer.ShowGifFrame(selected, edited[selected]);
            layer.Effects = null; layer.Mask = null; layer.FillOpacity = 1;
            if (clipped) notes.Add($"{layer.Name}: effects past the animation's frame were cut to it.");
            return true;
        }
        catch { foreach (var image in created) image.Dispose(); throw; }
    }

    /// <summary>The group drawn as one pixel layer in its place, keeping its name, opacity, blend mode, visibility and
    /// (when asked) its effects.</summary>
    private static bool Merge(CanvasDocument document, ImageLayer group, bool keepEffects)
    {
        if ((long)document.Width * document.Height > DocumentLimits.DocumentPixelBudget) return false;
        using var isolated = LayerWorkflow.Subset(document, [group.ID]);
        isolated.Image = isolated.Image with { Background = null };
        var copy = isolated.Layers.Single(l => l.ID == group.ID);
        copy.IsVisible = true; copy.Opacity = 1; copy.BlendMode = Format.LayerBlendMode.Normal; copy.ParentID = null;
        if (keepEffects) copy.Effects = null;
        var pixels = DocumentRenderer.Render(isolated);
        var members = document.Descendants(group.ID).ToHashSet();
        var merged = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, group.Name), new LayerTransform(0, 0, document.Width, document.Height), group.Name)
        {
            ParentID = group.ParentID, IsVisible = group.IsVisible, Opacity = group.Opacity,
            BlendMode = group.BlendMode, Effects = keepEffects ? group.Effects : null, Locks = group.Locks, Label = group.Label,
        };
        document.Layers.RemoveAll(l => members.Contains(l.ID));
        document.Layers[document.Layers.IndexOf(group)] = merged;
        foreach (var layer in document.Layers) if (layer.MaskSourceID is { } source && (source == group.ID || members.Contains(source))) layer.MaskSourceID = merged.ID;
        return true;
    }

    /// <summary>The group mask over the document, 0–255.</summary>
    private static SKBitmap Coverage(CanvasDocument document, ImageLayer group, LayerMask mask)
    {
        var coverage = Bitmaps.Allocate(Bitmaps.MaskInfo(document.Width, document.Height));
        using var canvas = new SKCanvas(coverage);
        canvas.Clear(SKColors.Black);
        canvas.SetMatrix(BrushEdits.PixelToDocument(group.MaskTransform, mask.Asset.Width, mask.Asset.Height));
        canvas.DrawBitmap(mask.Asset.Image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        return coverage;
    }

    private static bool IsHard(SKBitmap coverage)
    {
        var bytes = coverage.GetPixelSpan();
        foreach (var b in bytes) if (b is not (0 or 255)) return false;
        return true;
    }

    private static bool Transformed(ImageLayer layer) =>
        layer.Asset is not { } asset || layer.Transform.Rotation != 0 || layer.Transform.FlipX || layer.Transform.FlipY
        || Math.Abs(layer.Transform.Width - asset.Width) > .001 || Math.Abs(layer.Transform.Height - asset.Height) > .001
        || layer.Transform.X != Math.Round(layer.Transform.X) || layer.Transform.Y != Math.Round(layer.Transform.Y);

    /// <summary>Whether anything was drawn outside <paramref name="region"/>.</summary>
    private static bool Outside(SKBitmap whole, SKRectI region)
    {
        var bytes = whole.GetPixelSpan();
        for (var y = 0; y < whole.Height; y++) for (var x = 0; x < whole.Width; x++)
            if (!region.Contains(x, y) && bytes[y * whole.RowBytes + x * 4 + 3] > 8) return true;
        return false;
    }
}
