using Compositor.Core.Document;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;
namespace Compositor.Core.IO;
public readonly record struct PackedChannelSource(Guid? Layer = null, bool WhiteValue = false)
{
    public static PackedChannelSource Black => new();
    public static PackedChannelSource White => new(null, true);
}
public static class ChannelPacking
{
    public static PackedChannelSource Default(CanvasDocument doc, int channel)
    {
        var names = new[] { "Red", "Green", "Blue", "Alpha" }; if (channel is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(channel));
        var layer = doc.Layers.LastOrDefault(l => l.Name.Trim().Equals(names[channel], StringComparison.OrdinalIgnoreCase) || l.Name.Trim().Equals(names[channel][..1], StringComparison.OrdinalIgnoreCase));
        return layer is null ? channel == 3 ? PackedChannelSource.White : PackedChannelSource.Black : new(layer.ID);
    }
    public static SKBitmap Pack(CanvasDocument doc, IReadOnlyList<PackedChannelSource> sources, int width, int height)
    {
        if (sources.Count != 4) throw new ArgumentException("Four channel sources are required.");
        var output = Bitmaps.Allocate(Bitmaps.ColorInfo(width, height));
        try
        {
            for (var channel = 0; channel < 4; channel++)
            {
                var choice = sources[channel];
                if (choice.Layer is not { } id)
                { var bytes = output.GetPixelSpan(); for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) bytes[y * output.RowBytes + x * 4 + channel] = choice.WhiteValue ? (byte)255 : (byte)0; continue; }
                using var view = doc.Clone(); var chosen = view.Layers.FirstOrDefault(l => l.ID == id) ?? throw new ArgumentException("Channel source layer is missing.");
                var ancestors = new HashSet<Guid> { id }; var parent = chosen.ParentID;
                while (parent is { } p && ancestors.Add(p)) parent = view.Layers.FirstOrDefault(l => l.ID == p)?.ParentID;
                var children = view.Descendants(id);
                foreach (var l in view.Layers) l.IsVisible = ancestors.Contains(l.ID) || children.Contains(l.ID) && l.IsVisible;
                view.Image = view.Image with { Background = null };
                using var rendered = ViewportRenderer.Render(view, SKRectI.Create(view.Width, view.Height), Math.Max(width, height));
                using var resized = rendered.Resize(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKCubicResampler.Mitchell)) ?? throw new OutOfMemoryException();
                var pixels = resized.GetPixelSpan(); var target = output.GetPixelSpan();
                for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                { var at = y * resized.RowBytes + x * 4; target[y * output.RowBytes + x * 4 + channel] = (byte)Math.Clamp(Math.Round(.2126 * pixels[at] + .7152 * pixels[at + 1] + .0722 * pixels[at + 2]), 0, 255); }
            }
            return output;
        }
        catch { output.Dispose(); throw; }
    }
    public static Guid? Split(CanvasDocument doc, Guid id)
    {
        if (doc.Layers.Count > LayerPlacement.MaxLayers - 4 || doc.Layers.FirstOrDefault(l => l.ID == id) is not { Asset: { } asset, IsGroup: false, Adjustment: null } layer || !LayerProtection.CanPaint(doc, id)) return null;
        var children = new List<ImageLayer>();
        try
        {
            for (var channel = 3; channel >= 0; channel--)
            {
                var plane = Bitmaps.Allocate(Bitmaps.ColorInfo(asset.Width, asset.Height)); var pixels = plane.GetPixelSpan(); var source = asset.Image.GetPixelSpan();
                for (var y = 0; y < plane.Height; y++) for (var x = 0; x < plane.Width; x++)
                { var value = source[y * asset.Image.RowBytes + x * 4 + channel]; var at = y * plane.RowBytes + x * 4; pixels[at] = pixels[at + 1] = pixels[at + 2] = value; pixels[at + 3] = 255; }
                var name = new[] { "Red", "Green", "Blue", "Alpha" }[channel];
                children.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(plane, name), layer.Transform, name) { ParentID = id });
            }
            var index = doc.Layers.IndexOf(layer);
            doc.Layers[index] = new ImageLayer(id, null, new Model.LayerTransform(0, 0, doc.Width, doc.Height), layer.Name) { IsGroup = true, ParentID = layer.ParentID };
            doc.Layers.InsertRange(index + 1, children); return children[^1].ID;
        }
        catch { foreach (var child in children) child.Asset?.Dispose(); throw; }
    }
}
