using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Core.Document;

public static class ChannelEdits
{
    public static void Transform(CanvasDocument document, int width, int height, SKMatrix matrix)
    {
        var changed = new List<AlphaChannel>();
        foreach (var channel in document.Channels)
        {
            var result = Bitmaps.Allocate(Bitmaps.MaskInfo(width, height)); result.Erase(SKColors.Black);
            using (var canvas = new SKCanvas(result))
            {
                canvas.SetMatrix(matrix);
                canvas.DrawBitmap(channel.Asset.Image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear));
            }
            changed.Add(channel with { Asset = ImportedImage.Create(result, channel.Name) });
        }
        document.Channels.Clear(); document.Channels.AddRange(changed);
    }
    public static Guid? Add(CanvasDocument document, string name, bool fromSelection)
    {
        if (document.Channels.Count >= 64) return null;
        var region = SKRectI.Create(0, 0, document.Width, document.Height);
        var gray = fromSelection ? document.Selection.Coverage(region) : null;
        if (gray is null)
        {
            gray = Bitmaps.Allocate(Bitmaps.MaskInfo(document.Width, document.Height));
            gray.Erase(fromSelection ? SKColors.White : SKColors.Black);
        }
        var id = Guid.NewGuid();
        document.Channels.Add(new AlphaChannel(id, name, ImportedImage.Create(gray, name)));
        return id;
    }

    public static bool Load(CanvasDocument document, SKBitmap pixels)
    {
        document.Selection = DocumentSelection.FromCoverage(pixels.Copy());
        return true;
    }

    /// <summary>RGB channel visibility changes only the viewport; exports always retain the full color image.</summary>
    public static void View(SKBitmap pixels, ColorChannels visible, bool grayscale)
    {
        if (visible == ColorChannels.RGB) return;
        for (var y = 0; y < pixels.Height; y++)
        for (var x = 0; x < pixels.Width; x++)
        {
            var color = pixels.GetPixel(x, y);
            var red = visible.HasFlag(ColorChannels.Red) ? color.Red : (byte)0;
            var green = visible.HasFlag(ColorChannels.Green) ? color.Green : (byte)0;
            var blue = visible.HasFlag(ColorChannels.Blue) ? color.Blue : (byte)0;
            if (grayscale && visible is ColorChannels.Red or ColorChannels.Green or ColorChannels.Blue)
                red = green = blue = (byte)(red + green + blue);
            pixels.SetPixel(x, y, new SKColor(red, green, blue, color.Alpha));
        }
    }

    /// <summary>Run raster tools on an isolated grayscale proxy, without touching any image layer.</summary>
    public static bool Edit(CanvasDocument document, Guid id, Func<CanvasDocument, Guid, bool> edit)
    {
        var index = document.Channels.FindIndex(channel => channel.ID == id);
        if (index < 0) return false;
        var channel = document.Channels[index];
        var proxy = new CanvasDocument(document.ID, document.Width, document.Height) { Selection = document.Selection };
        var rgba = Bitmaps.Allocate(new SKImageInfo(document.Width, document.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(rgba)) canvas.DrawBitmap(channel.Asset.Image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        proxy.Layers.Add(new ImageLayer(id, ImportedImage.Create(rgba, channel.Name),
            new LayerTransform(0, 0, document.Width, document.Height), channel.Name));
        if (!edit(proxy, id)) { proxy.Dispose(); return false; }
        var result = Bitmaps.Allocate(Bitmaps.MaskInfo(document.Width, document.Height));
        using var flattened = DocumentRenderer.Render(proxy);
        using (var canvas = new SKCanvas(result))
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawBitmap(flattened, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        }
        document.Channels[index] = channel with { Asset = ImportedImage.Create(result, channel.Name) };
        proxy.Dispose();
        return true;
    }
}
