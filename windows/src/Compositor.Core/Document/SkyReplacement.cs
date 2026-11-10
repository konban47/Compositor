using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Document;

public enum SkyLighting { Multiply, Screen }

/// <summary>Edit ▸ Sky Replacement's settings, as its sliders hold them.</summary>
public sealed record SkySettings
{
    public double ShiftEdge { get; init; }
    public double FadeEdge { get; init; } = 50;
    public double Brightness { get; init; }
    public double Temperature { get; init; }
    public double Scale { get; init; } = 100;
    public bool Flip { get; init; }
    public SkyLighting Lighting { get; init; } = SkyLighting.Screen;
    public double ForegroundLighting { get; init; } = 24;
    public double EdgeLighting { get; init; } = 70;
    public double ColorAdjustment { get; init; } = 35;
    /// <summary>Where the Sky Move tool has dragged the sky, in document pixels.</summary>
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }
}

/// <summary>The pixels Sky Replacement adds: the placed sky, its mask, and the light and color the new sky casts on the foreground.</summary>
public sealed class SkyLayers(SKBitmap sky, SKBitmap mask, SKBitmap lighting, SKBitmap color) : IDisposable
{
    public SKBitmap Sky { get; } = sky;
    /// <summary>One alpha byte a pixel: where the new sky shows.</summary>
    public SKBitmap Mask { get; } = mask;
    public SKBitmap Lighting { get; } = lighting;
    public SKBitmap Color { get; } = color;
    public void Dispose() { Sky.Dispose(); Mask.Dispose(); Lighting.Dispose(); Color.Dispose(); }
}

/// <summary>
/// Finds the sky of a photograph and puts another one in its place. The sky is what is bright, smooth and connected to the top
/// edge; the new sky is scaled to cover the frame, moved and flipped as asked, brightened and warmed; and the foreground takes
/// on the new sky's light through a lighting layer and a color layer, strongest at the horizon.
/// </summary>
public static class SkyReplacement
{
    /// <summary>How likely each pixel is to be sky, 0 to 255, worked out on a copy at most 512 pixels across and scaled back.</summary>
    public static byte[] Detect(SKBitmap image)
    {
        var scale = Math.Min(1, 512d / Math.Max(image.Width, image.Height));
        int w = Math.Max(1, (int)Math.Round(image.Width * scale)), h = Math.Max(1, (int)Math.Round(image.Height * scale));
        using var small = image.Resize(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            ?? throw new InvalidOperationException("The image could not be measured.");
        var pixels = small.GetPixelSpan();
        var luma = new float[w * h]; var likely = new float[w * h];
        for (var i = 0; i < w * h; i++)
        {
            float r = pixels[i * 4] / 255f, g = pixels[i * 4 + 1] / 255f, b = pixels[i * 4 + 2] / 255f;
            luma[i] = .2126f * r + .7152f * g + .0722f * b;
            var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var saturation = max <= 0 ? 0 : (max - min) / max;
            // Blue sky, bright overcast and warm sunsets all count; dark, saturated greens and browns do not.
            var blue = b >= r && b >= g * .9f ? .45f : 0; var bright = luma[i] > .55f ? .35f + (luma[i] - .55f) : 0; var warm = r > g && r > b && luma[i] > .45f && saturation > .25f ? .3f : 0;
            var green = g > r * 1.08f && g > b * 1.05f ? -.5f : 0;
            likely[i] = Math.Clamp(blue + bright + warm + green + .15f, 0, 1);
        }
        // Grow from the top edge through smooth, likely pixels; a sharp edge or an unlikely pixel stops the growth.
        var sky = new byte[w * h]; var queue = new Queue<int>();
        for (var x = 0; x < w; x++) if (likely[x] > .5f) { sky[x] = 1; queue.Enqueue(x); }
        while (queue.Count > 0)
        {
            var p = queue.Dequeue(); int px = p % w, py = p / w;
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int x = px + dx, y = py + dy; if (x < 0 || y < 0 || x >= w || y >= h) continue;
                var q = y * w + x; if (sky[q] != 0 || likely[q] < .4f) continue;
                if (Math.Abs(luma[q] - luma[p]) > .06f) continue;
                sky[q] = 1; queue.Enqueue(q);
            }
        }
        // Small foreground islands inside the sky (birds, wires) become sky; the result is softened before it is scaled up.
        var holes = FillSmallHoles(sky, w, h, w * h / 400);
        var soft = new float[w * h]; for (var i = 0; i < soft.Length; i++) soft[i] = holes[i];
        soft = Blur(soft, w, h, 1);
        using var mask = new SKBitmap(new SKImageInfo(w, h, SKColorType.Alpha8, SKAlphaType.Premul));
        var span = mask.GetPixelSpan(); for (var i = 0; i < soft.Length; i++) span[i] = (byte)Math.Round(Math.Clamp(soft[i], 0, 1) * 255);
        using var large = mask.Resize(new SKImageInfo(image.Width, image.Height, SKColorType.Alpha8, SKAlphaType.Premul), new SKSamplingOptions(SKFilterMode.Linear))
            ?? throw new InvalidOperationException("The sky mask could not be scaled.");
        return large.GetPixelSpan().ToArray();
    }

    /// <summary>The mask with its edge moved out (positive) or in (negative) and faded over a width set by Fade Edge.</summary>
    public static byte[] Refine(byte[] mask, int width, int height, double shift, double fade)
    {
        var size = Math.Max(width, height); var values = new float[mask.Length];
        for (var i = 0; i < mask.Length; i++) values[i] = mask[i] / 255f;
        var reach = (int)Math.Round(Math.Abs(shift) / 100 * size * .03);
        if (reach > 0) { values = Blur(values, width, height, reach); var threshold = shift > 0 ? .15f : .85f; for (var i = 0; i < values.Length; i++) values[i] = values[i] >= threshold ? 1 : values[i] / threshold * .5f; }
        var feather = (int)Math.Round(fade / 100 * size * .02);
        if (feather > 0) values = Blur(values, width, height, feather);
        var result = new byte[mask.Length]; for (var i = 0; i < result.Length; i++) result[i] = (byte)Math.Round(Math.Clamp(values[i], 0, 1) * 255);
        return result;
    }

    /// <summary>The replacement sky covering a width × height frame: scaled to cover it, then scaled again, moved, flipped, brightened and warmed.</summary>
    public static SKBitmap PlaceSky(SKBitmap sky, int width, int height, SkySettings settings)
    {
        var placed = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(placed);
        var cover = Math.Max(width / (double)sky.Width, height / (double)sky.Height) * Math.Clamp(settings.Scale, 25, 400) / 100;
        var w = (float)(sky.Width * cover); var h = (float)(sky.Height * cover);
        var left = (float)((width - w) / 2 + settings.OffsetX); var top = (float)Math.Min(0, settings.OffsetY);
        canvas.Save();
        if (settings.Flip) { canvas.Translate(width, 0); canvas.Scale(-1, 1); left = width - left - w; }
        using var image = SKImage.FromBitmap(sky);
        canvas.DrawImage(image, SKRect.Create(left, top, w, h), new SKSamplingOptions(SKCubicResampler.Mitchell));
        canvas.Restore();
        // Brightness lifts or lowers every channel; temperature trades blue for red, the warm-cool axis of the slider.
        var brightness = (float)(settings.Brightness / 100 * .5); var temperature = (float)(settings.Temperature / 100 * .25);
        if (brightness != 0 || temperature != 0)
        {
            using var copy = placed.Copy(); using var paint = new SKPaint();
            paint.ColorFilter = SKColorFilter.CreateColorMatrix([1 + brightness + temperature, 0, 0, 0, 0, 0, 1 + brightness, 0, 0, 0, 0, 0, 1 + brightness - temperature, 0, 0, 0, 0, 0, 1, 0]);
            canvas.Clear(SKColors.Transparent); Draw(canvas, copy, paint);
        }
        return placed;
    }

    /// <summary>Everything Sky Replacement adds over <paramref name="image"/> for the given mask, sky and settings.</summary>
    public static SkyLayers Build(SKBitmap image, byte[] detected, SKBitmap sky, SkySettings settings)
    {
        int width = image.Width, height = image.Height;
        var refined = Refine(detected, width, height, settings.ShiftEdge, settings.FadeEdge);
        var placed = PlaceSky(sky, width, height, settings);
        var mask = new SKBitmap(new SKImageInfo(width, height, SKColorType.Alpha8, SKAlphaType.Premul)); refined.CopyTo(mask.GetPixelSpan());
        // The tone the new sky lights the scene with: its average color where it shows.
        double r = 0, g = 0, b = 0, weight = 0; var skyPixels = placed.GetPixelSpan();
        for (var i = 0; i < refined.Length; i += 7) { var a = refined[i] / 255d; if (a <= 0) continue; r += skyPixels[i * 4] * a; g += skyPixels[i * 4 + 1] * a; b += skyPixels[i * 4 + 2] * a; weight += a; }
        var tone = weight > 0 ? new SKColor((byte)(r / weight), (byte)(g / weight), (byte)(b / weight)) : new SKColor(128, 160, 200);
        // How close each foreground pixel is to the horizon: the sky mask spread down and kept off the sky itself.
        var near = new float[refined.Length]; for (var i = 0; i < near.Length; i++) near[i] = refined[i] / 255f;
        near = Blur(near, width, height, Math.Max(2, (int)(Math.Max(width, height) * .04)));
        var lighting = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var color = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var light = lighting.GetPixelSpan(); var tint = color.GetPixelSpan();
        for (var i = 0; i < refined.Length; i++)
        {
            var foreground = 1 - refined[i] / 255f; var edge = Math.Clamp(near[i] * 2, 0, 1) * foreground;
            var a = Math.Clamp(foreground * (float)settings.ForegroundLighting / 100 * .6f + edge * (float)settings.EdgeLighting / 100 * .5f, 0, 1);
            light[i * 4] = tone.Red; light[i * 4 + 1] = tone.Green; light[i * 4 + 2] = tone.Blue; light[i * 4 + 3] = (byte)Math.Round(a * 255);
            tint[i * 4] = tone.Red; tint[i * 4 + 1] = tone.Green; tint[i * 4 + 2] = tone.Blue; tint[i * 4 + 3] = (byte)Math.Round(foreground * settings.ColorAdjustment / 100 * .5 * 255);
        }
        lighting.NotifyPixelsChanged(); color.NotifyPixelsChanged();
        return new SkyLayers(placed, mask, lighting, color);
    }

    /// <summary>The picture with the new sky and its light laid over it, as the layers would show it: for the preview and for Duplicate Layer.</summary>
    public static SKBitmap Composite(SKBitmap image, SkyLayers layers, SkySettings settings)
    {
        var result = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(result); Draw(canvas, image);
        using (var masked = new SKBitmap(result.Info))
        {
            using (var skyCanvas = new SKCanvas(masked)) { Draw(skyCanvas, layers.Sky); using var keep = new SKPaint { BlendMode = SKBlendMode.DstIn }; Draw(skyCanvas, layers.Mask, keep); }
            Draw(canvas, masked);
        }
        using (var light = new SKPaint { BlendMode = settings.Lighting == SkyLighting.Multiply ? SKBlendMode.Multiply : SKBlendMode.Screen }) Draw(canvas, layers.Lighting, light);
        using (var tint = new SKPaint { BlendMode = SKBlendMode.Color }) Draw(canvas, layers.Color, tint);
        return result;
    }

    private static void Draw(SKCanvas canvas, SKBitmap bitmap, SKPaint? paint = null) { using var image = SKImage.FromBitmap(bitmap); canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), paint); }

    private static byte[] FillSmallHoles(byte[] sky, int w, int h, int limit)
    {
        var result = (byte[])sky.Clone(); var seen = new bool[sky.Length]; var region = new List<int>(); var queue = new Queue<int>();
        for (var start = 0; start < sky.Length; start++)
        {
            if (sky[start] != 0 || seen[start]) continue;
            region.Clear(); queue.Enqueue(start); seen[start] = true; var touchesBottom = false;
            while (queue.Count > 0)
            {
                var p = queue.Dequeue(); region.Add(p); int px = p % w, py = p / w; if (py == h - 1 || px == 0 || px == w - 1) touchesBottom |= py == h - 1;
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int x = px + dx, y = py + dy; if (x < 0 || y < 0 || x >= w || y >= h) continue;
                    var q = y * w + x; if (sky[q] != 0 || seen[q]) continue; seen[q] = true; queue.Enqueue(q);
                }
            }
            if (!touchesBottom && region.Count <= limit) foreach (var p in region) result[p] = 1;
        }
        return result;
    }

    /// <summary>A separable box blur, run three times, which is close enough to a Gaussian for masks.</summary>
    internal static float[] Blur(float[] values, int w, int h, int radius)
    {
        if (radius <= 0) return values;
        var a = (float[])values.Clone(); var b = new float[a.Length];
        for (var pass = 0; pass < 3; pass++)
        {
            for (var y = 0; y < h; y++)
            {
                float sum = 0; var row = y * w;
                for (var x = -radius; x <= radius; x++) sum += a[row + Math.Clamp(x, 0, w - 1)];
                for (var x = 0; x < w; x++) { b[row + x] = sum / (2 * radius + 1); sum += a[row + Math.Min(w - 1, x + radius + 1)] - a[row + Math.Max(0, x - radius)]; }
            }
            for (var x = 0; x < w; x++)
            {
                float sum = 0;
                for (var y = -radius; y <= radius; y++) sum += b[Math.Clamp(y, 0, h - 1) * w + x];
                for (var y = 0; y < h; y++) { a[y * w + x] = sum / (2 * radius + 1); sum += b[Math.Min(h - 1, y + radius + 1) * w + x] - b[Math.Max(0, y - radius) * w + x]; }
            }
        }
        return a;
    }
}
