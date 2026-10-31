using System.Runtime.InteropServices;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>
/// Filter › Volumetric Light: a light placed on the image, its beams through the air, and the haze it lights. The
/// light's place is a share of the layer's width and height (0–100, rows top-down), which the panel opens on the
/// layer's biggest bright area. The rest are 0–100: how bright the beams are, how far from the light they reach, how
/// thick the haze is, how big the light is, how much the beams break up into shafts, where the haze begins and how far
/// bunched depths are spread.
/// </summary>
public sealed record VolumetricLightSettings
{
    public double CenterX { get; set; } = 50;
    public double CenterY { get; set; } = 30;
    public double Red { get; set; } = 1;
    public double Green { get; set; } = .86;
    public double Blue { get; set; } = .68;
    public double Intensity { get; set; } = 30;
    public double Length { get; set; }
    public double Haze { get; set; } = 60;
    public double Size { get; set; } = 20;
    public double Shafts { get; set; }
    /// <summary>How far into the scene the haze begins, by the photo's own spread of depths: at 0 it reaches up to the
    /// camera; at 50 about the nearer half of the scene stays clear; at 100 only the farthest parts are hazy.</summary>
    public double Distance { get; set; } = 30;
    /// <summary>How far the depth is spread out (<see cref="DepthMap.Equalized"/>), so depths bunched together, like a
    /// distant city, get haze in steps rather than all alike. 0 keeps the model's own spacing.</summary>
    public double DepthContrast { get; set; }

    public bool IsValid => double.IsFinite(CenterX) && double.IsFinite(CenterY) && CenterX is >= -100 and <= 200 && CenterY is >= -100 and <= 200
        && new[] { Red, Green, Blue }.All(v => double.IsFinite(v) && v is >= 0 and <= 1)
        && new[] { Intensity, Length, Haze, Size, Shafts, Distance, DepthContrast }.All(v => double.IsFinite(v) && v is >= 0 and <= 100);
    public bool DoesAnything => Intensity > 0 || Haze > 0;
}

/// <summary>
/// Volumetric Light worked out the way games draw light shafts after the fact, as the Mac build does on the GPU: what
/// gives off light is the light itself and the photo's bright openings near it, with the subject and the photo's dark
/// shapes blocking it; each pixel then gathers that light along the line back toward the light, so whatever blocks it
/// casts shafts of shadow through the beams. The haze is thicker the further off a pixel is: from the depth model once
/// it's here (<see cref="DepthModel"/>), the subject held near by its mask, otherwise guessed from where it sits and
/// the subject, which stands nearer than what's behind it.
/// </summary>
public static class VolumetricLighting
{
    /// <summary>How bright the beams are at Intensity 100, over the photo.</summary>
    public const double FullIntensity = 5;
    /// <summary>How thick the haze is at Haze 100.</summary>
    public const double FullHaze = 1;
    /// <summary>The longest side the beams are gathered at: they're soft, and each one gathers over fewer pixels.</summary>
    public const int BeamLimit = 1024;
    /// <summary>The longest side the haze's wide blurs are worked out at.</summary>
    public const int FieldLimit = 1024;
    /// <summary>The largest image whose pieces are kept between calls, as a preview is; the full-size pass on OK runs
    /// once, and its pieces would take a lot of memory.</summary>
    public const long KeptPixels = 2048L * 2048;

    /// <summary><paramref name="source"/> lit, as RgbaF32. <paramref name="subject"/> is a Gray8 mask of what stands in
    /// front of the light (any size), or null for nothing; <paramref name="depth"/> is the photo's depth, or null to
    /// guess it. What doesn't change as the sliders move is worked out once and kept for the same source: the scene's
    /// glows, air and subject, its depth for Depth Contrast, and the beams for the light's place, Size, Length and
    /// Shafts. So moving Haze, Distance, Intensity or the color only lays them over the photo again.</summary>
    public static SKBitmap Apply(SKBitmap source, SKBitmap? subject, VolumetricLightSettings settings, DepthMap? depth = null)
    {
        if (!settings.IsValid) throw new ArgumentException("Invalid Volumetric Light settings.", nameof(settings));
        var result = DeepPixels.From(source);
        if (!settings.DoesAnything) return result;
        var width = source.Width; var height = source.Height; double side = Math.Max(width, height);
        var lightX = (float)(settings.CenterX / 100 * width); var lightY = (float)(settings.CenterY / 100 * height);
        float Lin(double v) => LightPlanes.Linear((float)v);
        var tint = new[] { Lin(settings.Red), Lin(settings.Green), Lin(settings.Blue) };
        var power = (float)(settings.Intensity / 100 * FullIntensity);
        var haze = (float)(settings.Haze / 100 * FullHaze);
        var keeps = (long)width * height <= KeptPixels;
        var prepared = keeps ? Kept.For(source, subject, depth) : new Prepared();

        // The beams, gathered on a small grid.
        var (bw, bh, bs) = LightPlanes.Grid(width, height, Math.Min(BeamLimit, (int)Math.Ceiling(side / 2)));
        var beamsKey = (settings.CenterX, settings.CenterY, settings.Size, settings.Length, settings.Shafts);
        float[] beams;
        lock (prepared)
        {
            if (prepared.Beams is { } kept && prepared.BeamsKey == beamsKey) beams = kept;
            else
            {
                beams = LightPlanes.Blur(Beams(source, subject, bw, bh, side * bs, lightX * (float)bs, lightY * (float)bs, settings), bw, bh, 1);
                if (keeps) (prepared.Beams, prepared.BeamsKey) = (beams, beamsKey);
            }
        }

        // The haze's fields: how near the subject, the photo's own lights bloomed, and its light spread wide.
        var (fw, fh, fs) = LightPlanes.Grid(width, height, FieldLimit);
        Scene scene;
        lock (prepared) scene = prepared.Scene ??= MakeScene(source, subject, fw, fh, side * fs);
        (float[] Plane, float[] Distances)? far = null;
        if (depth is not null)
            lock (prepared)
            {
                if (prepared.Depth is { } known && prepared.DepthContrast == settings.DepthContrast) far = known;
                else
                {
                    var spread = depth.Equalized((float)(settings.DepthContrast / 100));
                    var distances = spread.Values.Select(v => 1 - v).ToArray(); Array.Sort(distances);
                    var composed = ComposedDepth(spread, scene.Mask, fw, fh, side * fs);
                    for (var i = 0; i < composed.Length; i++) composed[i] = 1 - Math.Clamp(composed[i], 0, 1);
                    far = (composed, distances);
                    if (keeps) (prepared.Depth, prepared.DepthContrast) = (far, settings.DepthContrast);
                }
            }
        var (start, end) = Ramp(settings.Distance, far?.Distances);
        end = Math.Max(end, start + .01f);
        var near = scene.Near; var glows = scene.Glows; var ambient = scene.Ambient;

        var pixels = MemoryMarshal.Cast<byte, float>(result.GetPixelSpan()).ToArray();
        var stride = result.RowBytes / 4;
        Parallel.For(0, height, y =>
        {
            var down = (y + .5f) / Math.Max(1, height);
            for (var x = 0; x < width; x++)
            {
                var i = y * stride + x * 4;
                var a = pixels[i + 3];
                if (a <= 0) continue;
                var gathered = LightPlanes.Up(beams, bw, bh, bs, x, y);
                var front = Math.Clamp(LightPlanes.Up(near, fw, fh, fs, x, y), 0, 1);
                // The depth map, which already has the subject in it; without it, guessed from where a pixel sits (the
                // foot of the frame is the ground near the camera) and the subject.
                var distanceOff = far is { } map ? LightPlanes.Up(map.Plane, fw, fh, fs, x, y)
                    : (1 - LightPlanes.Smoothstep(.45f, 1.05f, down)) * (1 - .8f * front);
                var distance = MathF.Sqrt((x + .5f - lightX) * (x + .5f - lightX) + (y + .5f - lightY) * (y + .5f - lightY));
                var glow = MathF.Exp(-distance / (float)(side * .35));
                // Clear nearer than where Distance starts the haze, building to full over a gentle stretch beyond, with a
                // little haze even in front: there's air between the camera and the nearest thing too.
                var ramp = LightPlanes.Smoothstep(start, end, distanceOff);
                var fog = 1 - MathF.Exp(-3f * haze * (.12f + .88f * ramp));
                for (var c = 0; c < 3; c++)
                {
                    var air = Math.Max(0, LightPlanes.Up(ambient[c], fw, fh, fs, x, y));
                    var bloom = Math.Max(0, LightPlanes.Up(glows[c], fw, fh, fs, x, y));
                    var fogColor = air * (1 + (tint[c] - 1) * .6f) * .9f + tint[c] * glow * .45f;
                    var basis = Math.Max(0, LightPlanes.Linear(pixels[i + c]));
                    var veiled = basis + (fogColor - basis) * fog + bloom * haze * 1.6f * (.3f + .7f * ramp);
                    var added = tint[c] * power * gathered * .5f * (1 - .92f * front);
                    var lit = 1 - (1 - Math.Min(veiled, 1)) * MathF.Exp(-added);
                    // Light past white in an HDR layer stays past it.
                    lit = Math.Max(lit, veiled + (lit - Math.Min(veiled, 1)));
                    pixels[i + c] = LightPlanes.Gamma(lit);
                }
            }
        });
        pixels.CopyTo(MemoryMarshal.Cast<byte, float>(result.GetPixelSpan()));
        return result;
    }

    /// <summary>What the haze is laid over the photo with that's the same whatever the sliders say, on the field grid.</summary>
    private sealed record Scene(float[]? Mask, float[] Near, float[][] Glows, float[][] Ambient);

    /// <summary>The last source's scene, depth and beams, so a slider moving on the same image reuses them.</summary>
    private sealed class Prepared
    {
        public Scene? Scene;
        public float[]? Beams;
        public (double, double, double, double, double) BeamsKey;
        public (float[] Plane, float[] Distances)? Depth;
        public double DepthContrast;
    }

    /// <summary>One kept <see cref="Prepared"/>, for the source, subject and depth it was worked out from; held weakly,
    /// so a source that's gone takes its pieces with it.</summary>
    private static class Kept
    {
        private static readonly Lock Gate = new();
        private static WeakReference<SKBitmap>? _source;
        private static object? _subject, _depth;
        private static Prepared _prepared = new();
        public static Prepared For(SKBitmap source, SKBitmap? subject, DepthMap? depth)
        {
            lock (Gate)
            {
                if (_source is null || !_source.TryGetTarget(out var held) || !ReferenceEquals(held, source)
                    || !ReferenceEquals(_subject, subject) || !ReferenceEquals(_depth, depth))
                {
                    _source = new WeakReference<SKBitmap>(source); _subject = subject; _depth = depth; _prepared = new Prepared();
                }
                return _prepared;
            }
        }
    }

    private static Scene MakeScene(SKBitmap source, SKBitmap? subject, int fw, int fh, double fieldSide)
    {
        var rgba = LightPlanes.Rgba(source, fw, fh);
        var mask = subject is null ? null : LightPlanes.Mask(subject, fw, fh);
        var near = mask is null ? new float[fw * fh] : LightPlanes.Blur(mask, fw, fh, Math.Max(1, fieldSide * .003));
        var glows = new float[3][]; var ambient = new float[3][];
        for (var c = 0; c < 3; c++)
        {
            var linear = new float[fw * fh];
            for (var i = 0; i < linear.Length; i++) linear[i] = LightPlanes.Linear(rgba[i * 4 + c]) * rgba[i * 4 + 3];
            // The photo's own lights (headlights, windows, signs), each glowing into the haze in its own color: a halo
            // and a wider bloom. A light is what's brighter than half white and than half again the light around it, so
            // in a dark scene every lamp counts, while in a bright one only what stands out from the surfaces around it
            // does, rather than the whole scene glowing.
            var around = LightPlanes.Blur(linear, fw, fh, fieldSide * .04);
            var lights = new float[linear.Length];
            for (var i = 0; i < lights.Length; i++) lights[i] = Math.Clamp(linear[i] - Math.Clamp(around[i] * 1.5f, .5f, 100), 0, 100);
            var halo = LightPlanes.Blur(lights, fw, fh, fieldSide * .012); var bloom = LightPlanes.Blur(lights, fw, fh, fieldSide * .05);
            for (var i = 0; i < halo.Length; i++) halo[i] += bloom[i] * 2;
            glows[c] = halo;
            // The air lit by the scene itself: its light spread wide, which the haze takes its color and brightness from.
            ambient[c] = LightPlanes.Blur(linear, fw, fh, fieldSide * .12);
        }
        // The subject's outline for the depth, made crisp so the haze meets it cleanly rather than creeping onto it.
        // The Mac build outlines people with Vision's person segmentation and finds hair from their poses; Windows has
        // the general subject mask only.
        float[]? outline = null;
        if (mask is not null)
        {
            outline = LightPlanes.Blur(mask, fw, fh, Math.Max(1, fieldSide * .0015));
            for (var i = 0; i < outline.Length; i++) outline[i] = Math.Clamp(outline[i] * 2.5f - 1.1f, 0, 1);
        }
        return new Scene(outline, near, glows, ambient);
    }

    /// <summary>
    /// Where the haze begins and where it's full, as distance (0 nearest, 1 farthest), for Distance: with depth, read off
    /// the photo's own spread of <paramref name="distances"/> (sorted), so the same Distance clears the same share of any
    /// scene; without it, as the guess runs. The haze always builds over a gentle stretch from the one to the other, so
    /// it can't turn hard or inside out.
    /// </summary>
    public static (float Start, float End) Ramp(double distance, float[]? distances)
    {
        var share = (float)(distance / 100);
        if (distances is not { Length: > 0 }) return (share * .6f, share * .6f + .5f);
        float At(float s) => distances[Math.Min(distances.Length - 1, (int)(Math.Max(0, s) * (distances.Length - 1)))];
        // A wide stretch however close the scene's depths lie, so a soft edge in the depth stays a soft edge in the haze.
        var start = Math.Min(At(share), .9f);
        return (start, Math.Max(At(Math.Min(1, share + .45f)), start + .45f));
    }

    /// <summary>
    /// The depth the haze goes by, on a <paramref name="width"/> × <paramref name="height"/> grid: 1 near, 0 far. The
    /// model's map is soft at the subject's edge, where its nearness bleeds out into a band around it, so there the
    /// subject's <paramref name="outline"/> decides instead: inside it the subject is near, and the band around it takes
    /// the depth of the backdrop just beyond it, worked out from around it; the two meet through the outline's own edge.
    /// Further off, the model's map as it is.
    /// </summary>
    internal static float[] ComposedDepth(DepthMap predicted, float[]? outline, int width, int height, double side)
    {
        var depth = LightPlanes.Blur(DepthModel.Resample(predicted, width, height), width, height, side * .0015);
        if (outline is null) return depth;
        var reach = side * .02;
        float[] Blurred(float[] plane, double sigma) => LightPlanes.Blur(plane, width, height, sigma);
        // The backdrop is read from beyond the band the model's nearness bleeds into (the outline grown past it), then
        // spread back in under the subject and the band, each place an average of the backdrop near it.
        var beyond = Blurred(outline, reach);
        for (var i = 0; i < beyond.Length; i++) beyond[i] = Math.Clamp(1 - 6 * beyond[i], 0, 1);
        var product = new float[depth.Length];
        for (var i = 0; i < product.Length; i++) product[i] = depth[i] * beyond[i];
        var weighted = Blurred(product, reach * 2.5); var coverage = Blurred(beyond, reach * 2.5);
        var closeness = Blurred(outline, reach * 1.5);
        var result = new float[depth.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var behind = weighted[i] / Math.Clamp(coverage[i] + .001f, .001f, 1);
            var close = Math.Clamp(closeness[i] * 4, 0, 1);
            // Near the subject, the backdrop's depth; further out, the model's; on the subject, near.
            var around = depth[i] + (behind - depth[i]) * close;
            var near = Math.Max(depth[i], .9f);
            result[i] = around + (near - around) * outline[i];
        }
        return result;
    }

    /// <summary>
    /// The depth the haze goes by, as an opaque gray picture the size of <paramref name="source"/>: white near, black far.
    /// Volumetric Light's Extract Depth adds it as a layer. Null without the model.
    /// </summary>
    public static SKBitmap? DepthImage(SKBitmap source, SKBitmap? subject, VolumetricLightSettings settings)
    {
        if (DepthModel.Depth(source) is not { } depth) return null;
        var spread = depth.Equalized((float)(Math.Clamp(settings.DepthContrast, 0, 100) / 100));
        var width = source.Width; var height = source.Height; double side = Math.Max(width, height);
        var (fw, fh, fs) = LightPlanes.Grid(width, height, FieldLimit);
        var scene = MakeScene(source, subject, fw, fh, side * fs);
        var composed = ComposedDepth(spread, scene.Mask, fw, fh, side * fs);
        var image = Bitmaps.Allocate(Bitmaps.ColorInfo(width, height));
        var bytes = image.GetPixelSpan();
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var v = (byte)Math.Clamp((int)MathF.Round(Math.Clamp(LightPlanes.Up(composed, fw, fh, fs, x, y), 0, 1) * 255), 0, 255);
                var i = y * image.RowBytes + x * 4;
                bytes[i] = v; bytes[i + 1] = v; bytes[i + 2] = v; bytes[i + 3] = 255;
            }
        return image;
    }

    /// <summary>
    /// Extract Depth's layer: <paramref name="depth"/> (at the layer's pixel size) added just above the layer, in its
    /// place and folder, named <paramref name="name"/>. Null when the layer is gone or the document is full.
    /// </summary>
    public static Guid? AddDepthLayer(CanvasDocument document, Guid layerID, SKBitmap depth, string name)
    {
        var index = document.Layers.FindIndex(layer => layer.ID == layerID);
        if (index < 0 || document.Layers[index] is not { IsGroup: false, Asset: { } asset } source
            || depth.Width != asset.Width || depth.Height != asset.Height || document.Layers.Count >= LayerPlacement.MaxLayers) return null;
        var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(depth, name), source.Transform, name) { ParentID = source.ParentID };
        document.Layers.Insert(index + 1, layer);
        return layer.ID;
    }

    /// <summary>The beams on the small grid, gray: at each pixel, the light gathered along the line back toward it.</summary>
    private static float[] Beams(SKBitmap source, SKBitmap? subject, int w, int h, double side, float lightX, float lightY,
        VolumetricLightSettings settings)
    {
        var rgba = LightPlanes.Rgba(source, w, h);
        var luma = new float[w * h];
        for (var i = 0; i < luma.Length; i++)
            luma[i] = (.2126f * LightPlanes.Linear(rgba[i * 4]) + .7152f * LightPlanes.Linear(rgba[i * 4 + 1]) + .0722f * LightPlanes.Linear(rgba[i * 4 + 2])) * rgba[i * 4 + 3];
        var blocked = subject is null ? new float[w * h] : LightPlanes.Mask(subject, w, h);
        // The photo's brightest, lightly blurred so one hot pixel doesn't set it, to tell its openings by.
        var green = new float[w * h];
        for (var i = 0; i < green.Length; i++) green[i] = LightPlanes.Linear(rgba[i * 4 + 1]) * rgba[i * 4 + 3];
        var peak = Math.Max(.05f, LightPlanes.Blur(green, w, h, Math.Max(.5, 2 * w / (double)source.Width)).Max());
        var size = (float)Math.Max(1, side * (.02 + settings.Size / 100 * .4));
        var length = (float)(settings.Length / 100); var shafts = (float)(settings.Shafts / 100);
        var openingsReach = (float)(side * .5);
        // What gives off light at a point: the light itself, a soft disc, mostly where the photo is light enough to
        // let it through; and the photo's bright openings, more near the light. The subject blocks both, and the
        // shafts break them up around the light, as dust and haze break a beam.
        float Emission(float x, float y)
        {
            var bright = Math.Clamp(LightPlanes.Sample(luma, w, h, x, y) / peak, 0, 1);
            var block = LightPlanes.Sample(blocked, w, h, x, y);
            var ax = x - lightX; var ay = y - lightY;
            var distance = MathF.Sqrt(ax * ax + ay * ay);
            var open = Math.Clamp((bright - .08f) / .3f, 0, 1);
            var disc = MathF.Exp(-2 * (distance / size) * (distance / size));
            var openings = Math.Clamp((bright - .45f) / .45f, 0, 1) * MathF.Exp(-distance / openingsReach);
            var broken = 1f;
            if (shafts > 0)
            {
                // Measured with y up, as the Mac's kernel does, so the same shafts fall in the same places.
                var angle = MathF.Atan2(-ay, ax);
                var wave = .6f * Around(angle, 97, 3.1f) + .4f * Around(angle, 251, 7.7f);
                broken = 1 - shafts + shafts * wave * wave * 1.8f;
            }
            return (disc * (.25f + .75f * open) + openings) * (1 - block) * broken;
        }
        const int samples = 96;
        var beams = new float[w * h];
        Parallel.For(0, h, py =>
        {
            for (var px = 0; px < w; px++)
            {
                var hx = px + .5f; var hy = py + .5f;
                var sx = (lightX - hx) * length / samples; var sy = (lightY - hy) * length / samples;
                var jitter = Hash(px * .618f + py * 1.414f);
                float sum = 0, weight = 1;
                for (var i = 0; i < samples; i++)
                {
                    sum += Emission(hx + sx * (i + jitter), hy + sy * (i + jitter)) * weight;
                    weight *= .985f;
                }
                beams[py * w + px] = sum / samples * 4;
            }
        });
        return beams;
    }

    private static float Hash(float n) { var v = MathF.Sin(n * 12.9898f) * 43758.5453f; return v - MathF.Floor(v); }

    /// <summary>Smooth noise around the light, one value per <paramref name="cells"/> around the circle.</summary>
    private static float Around(float angle, float cells, float seed)
    {
        var u = (angle / 6.2831853f + .5f) * cells;
        var i = MathF.Floor(u); var f = u - i;
        f = f * f * (3 - 2 * f);
        var a = Hash(i % cells + seed); var b = Hash((i + 1) % cells + seed);
        return a + (b - a) * f;
    }

    /// <summary>
    /// Where the light starts: in the biggest bright area of <paramref name="image"/> (a window, the sky, a beam), at its
    /// brightest, in its pixels (rows top-down). Areas are the connected parts of a small copy near its top brightness,
    /// each weighed by its size and how bright it is, so a wide opening wins over a smaller, hotter patch.
    /// </summary>
    public static SKPoint BrightestPoint(SKBitmap image)
    {
        const int limit = 64;
        var width = Math.Max(1, image.Width >= image.Height ? limit : limit * image.Width / Math.Max(1, image.Height));
        var height = Math.Max(1, image.Height >= image.Width ? limit : limit * image.Height / Math.Max(1, image.Width));
        var center = new SKPoint(image.Width / 2f, image.Height / 2f);
        var rgba = LightPlanes.Rgba(image, width, height);
        var gray = new double[width * height];
        for (var i = 0; i < gray.Length; i++)
            gray[i] = 255 * (.2126 * rgba[i * 4] + .7152 * rgba[i * 4 + 1] + .0722 * rgba[i * 4 + 2]) * rgba[i * 4 + 3];
        // Lightly smoothed, so a speck doesn't count as an area.
        var bright = new double[gray.Length];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                double sum = 0, count = 0;
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var sx = x + dx; var sy = y + dy;
                        if (sx < 0 || sy < 0 || sx >= width || sy >= height) continue;
                        sum += gray[sy * width + sx]; count++;
                    }
                bright[y * width + x] = sum / count;
            }
        var peak = bright.Max();
        if (!(peak > 0)) return center;
        var threshold = peak * .75;
        var area = Enumerable.Repeat(-1, bright.Length).ToArray();
        (double Score, int At) best = (-1, 0);
        for (var start = 0; start < bright.Length; start++)
        {
            if (bright[start] < threshold || area[start] >= 0) continue;
            var stack = new Stack<int>([start]); double score = 0; var brightest = start;
            area[start] = start;
            while (stack.TryPop(out var index))
            {
                score += bright[index];
                if (bright[index] > bright[brightest]) brightest = index;
                var x = index % width; var y = index / width;
                foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
                {
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    var next = ny * width + nx;
                    if (area[next] < 0 && bright[next] >= threshold) { area[next] = start; stack.Push(next); }
                }
            }
            if (score > best.Score) best = (score, brightest);
        }
        return new SKPoint((float)((best.At % width + .5) * image.Width / width), (float)((best.At / width + .5) * image.Height / height));
    }
}
