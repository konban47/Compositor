using System.Runtime.InteropServices;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using ImageMagick;
using SkiaSharp;

namespace Compositor.Core.Document;

public static class ImageModeEdits
{
    public static bool Convert(CanvasDocument document, ImageSettings settings, int colors = 256, bool dither = true, double threshold = .5)
    {
        if (!settings.IsValid || colors is < 2 or > 256 || threshold is < 0 or > 1) return false;
        var replacements = new List<(ImageLayer Layer, ImportedImage Asset)>();
        var flatten = settings.Mode is ImageMode.Bitmap or ImageMode.Indexed or ImageMode.Duotone or ImageMode.Multichannel;
        if (flatten)
        {
            using var composite = DocumentRenderer.RenderDeep(document);
            var layer = new ImageLayer(Guid.NewGuid(), null, new Model.LayerTransform(0, 0, document.Width, document.Height), "Background");
            using var source = DeepImageCodec.Magick(composite);
            if (settings.Mode == ImageMode.Indexed && settings.Palette is null)
            {
                source.Quantize(new QuantizeSettings { Colors = (uint)colors, DitherMethod = dither ? DitherMethod.FloydSteinberg : DitherMethod.No });
                using var pixels = source.GetPixels(); var entries = pixels.ToByteArray(PixelMapping.RGBA)!;
                var palette = new HashSet<uint>();
                for (var i = 0; i < entries.Length; i += 4) if (entries[i + 3] != 0) palette.Add(0xFF000000u | (uint)(entries[i] << 16 | entries[i + 1] << 8 | entries[i + 2]));
                var ordered = palette.Take(colors).ToList(); while (ordered.Count < 2) ordered.Add(0xFFFFFFFF);
                settings = settings with { Palette = ordered.ToArray() };
            }
            replacements.Add((layer, Converted(composite, layer.Name, settings, dither, threshold)));
        }
        else foreach (var layer in document.Layers.Where(l => l.Asset is not null))
            replacements.Add((layer, Converted(layer.Asset!.WorkingImage, layer.Name, settings, dither, threshold)));
        if (flatten)
        {
            document.Layers.Clear(); document.Layers.Add(replacements[0].Layer); document.Variables.Clear(); document.DataSets.Clear();
        }
        foreach (var (layer, asset) in replacements) { layer.Asset = asset; layer.Text = null; layer.Shape = null; layer.SmartObject = null; }
        document.Image = settings with { Background = flatten ? null : document.Image.Background };
        if (settings.Mode == ImageMode.Multichannel)
        {
            document.Channels.Clear(); using var rendered = DocumentRenderer.RenderDeep(document);
            for (var channel = 0; channel < 3; channel++)
            {
                var gray = Bitmaps.Allocate(Bitmaps.MaskInfo(document.Width, document.Height));
                for (var y = 0; y < gray.Height; y++) for (var x = 0; x < gray.Width; x++)
                { var c = rendered.GetPixel(x, y); gray.GetPixelSpan()[y * gray.RowBytes + x] = channel == 0 ? c.Red : channel == 1 ? c.Green : c.Blue; }
                document.Channels.Add(new AlphaChannel(Guid.NewGuid(), new[] { "Red", "Green", "Blue" }[channel], ImportedImage.Create(gray, "Channel")));
            }
        }
        return true;
    }

    public static ImportedImage Converted(SKBitmap source, string name, ImageSettings settings, bool dither = false, double threshold = .5)
    {
        var deep = DeepPixels.From(source); var p = MemoryMarshal.Cast<byte, float>(deep.GetPixelSpan());
        var palette = settings.Palette?.Select(Color3.From).ToArray(); var ink = Color3.From(settings.Ink);
        var error = dither && settings.Mode is ImageMode.Bitmap or ImageMode.Indexed ? new Color3[deep.Width + 2] : null;
        var nextError = error is null ? null : new Color3[deep.Width + 2];
        for (var y = 0; y < deep.Height; y++)
        {
            for (var x = 0; x < deep.Width; x++)
            {
                var i = y * deep.RowBytes / 4 + x * 4; var original = new Color3(p[i], p[i + 1], p[i + 2]);
                var c = error is null ? original : (original + error[x + 1]).Clamp();
                Color3 result = settings.Mode switch
                {
                    ImageMode.Grayscale => new(c.Luma, c.Luma, c.Luma),
                    ImageMode.Bitmap => c.Luma >= threshold ? new(1, 1, 1) : new(0, 0, 0),
                    ImageMode.Duotone => Color3.Mix(ink, new(1, 1, 1), c.Luma),
                    ImageMode.Indexed when palette is { Length: > 0 } => Closest(c, palette),
                    _ => c,
                };
                p[i] = (float)result.R; p[i + 1] = (float)result.G; p[i + 2] = (float)result.B;
                if (error is not null)
                {
                    var e = c - result; error[x + 2] += e * (7.0 / 16); nextError![x] += e * (3.0 / 16);
                    nextError[x + 1] += e * (5.0 / 16); nextError[x + 2] += e * (1.0 / 16);
                }
            }
            if (error is not null) { (error, nextError) = (nextError!, error); Array.Clear(nextError!); }
        }
        if (settings.Depth > 8) return ImportedImage.CreateDeep(deep, name, settings.Depth);
        using (deep) return ImportedImage.Create(DeepPixels.Preview(deep), name);
    }

    private static Color3 Closest(Color3 c, Color3[] palette)
    {
        double distance = double.MaxValue; var best = palette[0];
        foreach (var p in palette) { var d = p - c; var n = d.R * d.R + d.G * d.G + d.B * d.B; if (n < distance) { distance = n; best = p; } }
        return best;
    }

    /// <summary>Keep a document's editing mode consistent when paint, paste or a plugin supplies RGB pixels.</summary>
    public static void Enforce(CanvasDocument before, CanvasDocument after)
    {
        if (before.Image != after.Image) return;
        var previous = before.Layers.ToDictionary(l => l.ID);
        foreach (var layer in after.Layers.Where(l => l.Asset is not null))
        {
            var old = previous.GetValueOrDefault(layer.ID)?.Asset; if (ReferenceEquals(old, layer.Asset)) continue;
            if (old?.Deep is not null && layer.Asset!.Deep is null) layer.Asset = DeepPixels.Painted(old, layer.Asset);
            if (after.Image.Mode is ImageMode.Bitmap or ImageMode.Grayscale or ImageMode.Indexed
                || after.Image.Depth > layer.Asset!.BitDepth)
                layer.Asset = Converted(layer.Asset!.WorkingImage, layer.Name, after.Image);
        }
    }
}
