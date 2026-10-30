using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>
/// One of Rim Light's lights: where it comes from, in degrees counterclockwise from the right, its color, how strong
/// it is, how far it wraps around the edges, and how hard its inner edge is rather than feathered, each 0–100.
/// </summary>
public sealed record RimLightSource
{
    public double Angle { get; set; } = 135;
    public double Red { get; set; } = 1;
    public double Green { get; set; } = .86;
    public double Blue { get; set; } = .7;
    public double Strength { get; set; } = 50;
    public double Width { get; set; } = 30;
    public double Choke { get; set; }

    public bool IsValid => double.IsFinite(Angle) && Angle is >= -180 and <= 180
        && new[] { Red, Green, Blue }.All(v => double.IsFinite(v) && v is >= 0 and <= 1)
        && new[] { Strength, Width, Choke }.All(v => double.IsFinite(v) && v is >= 0 and <= 100);
}

/// <summary>Filter › Rim Light: up to three lights behind the subject that catch its edges, and how much of their
/// light glows in the air around it, 0–100.</summary>
public sealed class RimLightSettings
{
    public const int MaximumLights = 3;
    public List<RimLightSource> Lights { get; set; } = [new()];
    public double Glow { get; set; } = 10;

    public bool IsValid => Lights.Count is >= 1 and <= MaximumLights && Lights.All(light => light.IsValid)
        && double.IsFinite(Glow) && Glow is >= 0 and <= 100;
    public bool DoesAnything => Lights.Any(light => light.Strength > 0);

    /// <summary>What the next added light starts as: from the side across from the ones there, in a color that
    /// sets them off — a cool one across from the first, then white.</summary>
    public RimLightSource NextLight
    {
        get
        {
            var last = Lights.LastOrDefault();
            var angle = last is null ? 135 : last.Angle > 0 ? last.Angle - 180 : last.Angle + 180;
            var (red, green, blue) = Math.Clamp(Lights.Count - 1, 0, 1) == 0 ? (.7, .84, 1.0) : (1.0, 1.0, 1.0);
            return new RimLightSource { Angle = angle, Red = red, Green = green, Blue = blue, Strength = 50, Width = last?.Width ?? 30, Choke = last?.Choke ?? 0 };
        }
    }

    public RimLightSettings Copy() => new() { Glow = Glow, Lights = [.. Lights.Select(light => light with { })] };
}

/// <summary>
/// The subject the lighting filters light, found once per layer image: the bundled U²-Net model's mask, kept while
/// the image lives so a slider only relights. Nothing found (or no model) reads as no subject.
/// </summary>
public static class SubjectCache
{
    private static readonly ConditionalWeakTable<SKBitmap, Holder> Masks = new();
    private sealed class Holder(SKBitmap? mask) { public SKBitmap? Mask { get; } = mask; }

    /// <summary>For tests and callers that already have one: the mask to use for <paramref name="image"/>.</summary>
    public static void Remember(SKBitmap image, SKBitmap? mask) => Masks.AddOrUpdate(image, new Holder(mask));
    public static bool HasLooked(SKBitmap image) => Masks.TryGetValue(image, out _);

    /// <summary>A Gray8 mask at the image's size, or null when the model finds nothing or cannot run.</summary>
    public static SKBitmap? Subject(SKBitmap image, CancellationToken cancellation = default)
    {
        if (Masks.TryGetValue(image, out var known)) return known.Mask;
        SKBitmap? mask;
        try
        {
            mask = SubjectSegmentation.PredictShared(image, cancellation);
            var bytes = mask.GetPixelSpan(); var any = false;
            for (var i = 0; i < bytes.Length && !any; i++) any = bytes[i] > 128;
            if (!any) { mask.Dispose(); mask = null; }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or Microsoft.ML.OnnxRuntime.OnnxRuntimeException or InvalidOperationException)
        {
            mask = null;
        }
        Masks.AddOrUpdate(image, new Holder(mask));
        return mask;
    }
}

/// <summary>
/// Rim Light worked out the way games light flat sprites, as the Mac build does on the GPU: the subject's outline (the
/// subject model's mask, pulled onto the photo's edges) is rounded into a surface that faces sideways at the edge and
/// turns to face the camera a light's Width in, the photo's fine detail is pressed into it as bumps, and each light,
/// from a little behind the subject, brightens what faces it. Loose strands past the edge catch light only where they
/// stand out from the backdrop by more than that backdrop varies on its own.
/// </summary>
public static class RimLighting
{
    /// <summary>The rim's width as a share of the image's longer side, from Width 0 to 100.</summary>
    public static double WidthShare(double width) => .002 + width / 100 * .03;
    /// <summary>How bright a light at Strength 100 is, over the surface's own color.</summary>
    public const double FullStrength = 10;
    /// <summary>How much of the rims' light glows around the subject at Glow 100.</summary>
    public const double FullGlow = 4;
    /// <summary>The longest side the lighting is worked out at; the light is then laid over every pixel.</summary>
    public const int WorkingLimit = 2048;

    /// <summary><paramref name="source"/> with its rim lights added, as RgbaF32; <paramref name="subject"/> is a Gray8
    /// mask of the subject (any size). Without a subject there is no edge to light and the pixels come back as they
    /// were.</summary>
    public static SKBitmap Apply(SKBitmap source, SKBitmap? subject, RimLightSettings settings)
    {
        if (!settings.IsValid) throw new ArgumentException("Invalid Rim Light settings.", nameof(settings));
        var result = DeepPixels.From(source);
        if (!settings.DoesAnything || subject is null) return result;
        var (w, h, scale) = LightPlanes.Grid(source.Width, source.Height, WorkingLimit);
        var light = Light(LightPlanes.Rgba(source, w, h), LightPlanes.Mask(subject, w, h), w, h, settings);
        var pixels = MemoryMarshal.Cast<byte, float>(result.GetPixelSpan()).ToArray();
        var stride = result.RowBytes / 4;
        Parallel.For(0, result.Height, y =>
        {
            for (var x = 0; x < result.Width; x++)
            {
                var i = y * stride + x * 4;
                if (pixels[i + 3] <= 0) continue;
                // A little of the light adds almost as it is; a lot rolls off into the light's color and white.
                for (var c = 0; c < 3; c++)
                {
                    var added = Math.Max(0, LightPlanes.Up(light[c], w, h, scale, x, y));
                    if (added <= 0) continue;
                    var linear = LightPlanes.Linear(pixels[i + c]);
                    var screened = 1 - (1 - Math.Clamp(linear, 0, 1)) * MathF.Exp(-added);
                    pixels[i + c] = LightPlanes.Gamma(Math.Max(linear, screened + Math.Max(0, linear - 1)));
                }
            }
        });
        pixels.CopyTo(MemoryMarshal.Cast<byte, float>(result.GetPixelSpan()));
        return result;
    }

    /// <summary>The light added over the photo, per channel, premultiplied by its alpha, at the working size.</summary>
    private static float[][] Light(float[] rgba, float[] mask, int w, int h, RimLightSettings settings)
    {
        var n = w * h; double side = Math.Max(w, h);
        float[] Blur(float[] plane, double sigma) => LightPlanes.Blur(plane, w, h, sigma);
        // As stored, not linearized: the photo's gray as it's seen, for its detail; the rest is lit in linear light.
        var gray = new float[n]; var linear = new float[3][]; for (var c = 0; c < 3; c++) linear[c] = new float[n];
        var alpha = new float[n];
        for (var i = 0; i < n; i++)
        {
            var a = rgba[i * 4 + 3]; alpha[i] = a;
            gray[i] = (.2126f * rgba[i * 4] + .7152f * rgba[i * 4 + 1] + .0722f * rgba[i * 4 + 2]) * a + (1 - a);
            for (var c = 0; c < 3; c++) linear[c][i] = LightPlanes.Linear(rgba[i * 4 + c]) * a;
        }
        // Where the light lands: the mask pulled onto the photo's own edges, its soft grays kept, so thin strands
        // catch their share; and the outline the form is rounded from, those grays pushed apart.
        var coverage = LightPlanes.Guided(mask, gray, w, h, (int)Math.Max(2, side * .008));
        var outline = new float[n];
        for (var i = 0; i < n; i++) outline[i] = Math.Clamp(coverage[i] * 1.6f - .3f, 0, 1);
        var fine = Blur(gray, 1); var coarse = Blur(gray, Math.Max(2, side / 400));
        // The backdrop behind the subject, worked out from around it, and how busy it is there: a strand has to stand
        // out from it further than it varies on its own to count.
        var open = new float[n]; for (var i = 0; i < n; i++) open[i] = 1 - outline[i];
        var spread = side * .01; var openBlurred = Blur(open, spread);
        var apart = new float[n]; var varies = new float[n];
        for (var c = 0; c < 3; c++)
        {
            var weighted = new float[n]; var squares = new float[n];
            for (var i = 0; i < n; i++) { weighted[i] = linear[c][i] * open[i]; squares[i] = linear[c][i] * linear[c][i] * open[i]; }
            weighted = Blur(weighted, spread); squares = Blur(squares, spread);
            for (var i = 0; i < n; i++)
            {
                var behind = weighted[i] / (openBlurred[i] + .001f);
                apart[i] = Math.Max(apart[i], Math.Abs(linear[c][i] - behind));
                varies[i] = Math.Max(varies[i], Math.Abs(squares[i] / (openBlurred[i] + .001f) - behind * behind));
            }
        }
        var near = Blur(outline, side * .015);
        var light = new[] { new float[n], new float[n], new float[n] };
        foreach (var source in settings.Lights.Where(l => l.Strength > 0))
        {
            var width = (float)(side * WidthShare(source.Width)); var reach = Math.Max(1f, width / 1.5f);
            var edge = Blur(outline, reach);
            var angle = source.Angle * Math.PI / 180;
            // From a little behind the subject, so it reaches only what turns away from the camera.
            var (dx, dy, dz) = Normalize((float)Math.Cos(angle), (float)Math.Sin(angle), -.2f);
            var strength = (float)(source.Strength / 100 * FullStrength);
            var color = new[] { LightPlanes.Linear((float)source.Red) * strength, LightPlanes.Linear((float)source.Green) * strength, LightPlanes.Linear((float)source.Blue) * strength };
            var detailScale = (float)(side / 250); var choke = (float)(source.Choke / 100);
            Parallel.For(0, h, y =>
            {
                float Read(float[] plane, int x, int yy) => plane[Math.Clamp(yy, 0, h - 1) * w + Math.Clamp(x, 0, w - 1)];
                (float X, float Y) Inwards(float[] plane, int x, int yy)
                {
                    var ix = Read(plane, x + 1, yy) - Read(plane, x - 1, yy); var iy = Read(plane, x, yy - 1) - Read(plane, x, yy + 1);
                    var length = MathF.Sqrt(ix * ix + iy * iy);
                    return length > 1e-6f ? (ix / length, iy / length) : (0, 0);
                }
                float Detail(int x, int yy) => Read(fine, x, yy) - Read(coarse, x, yy);
                for (var x = 0; x < w; x++)
                {
                    var i = y * w + x;
                    var covered = coverage[i]; var closeness = near[i];
                    if (covered <= .002f && closeness <= .002f) continue;
                    var a = alpha[i];
                    var tint = new float[3];
                    for (var c = 0; c < 3; c++)
                    {
                        var albedo = a > 0 ? Math.Max(0, linear[c][i] / a) : 0;
                        tint[c] = color[c] * (albedo * .7f + .45f);
                    }
                    // How far in from the edge: a hard edge blurred by `reach` is at half where the edge is, and its
                    // level tells the distance from it.
                    var level = Math.Clamp(edge[i], .001f, .999f);
                    var depth = reach * MathF.Sqrt(2) * InverseError(2 * level - 1);
                    var inwards = Inwards(edge, x, y);
                    var bumpX = (Detail(x + 1, y) - Detail(x - 1, y)) / 2; var bumpY = (Detail(x, y - 1) - Detail(x, y + 1)) / 2;
                    var surface = Normalize(-inwards.X * 2.75f - bumpX * detailScale, -inwards.Y * 2.75f - bumpY * detailScale, 1);
                    var lit = MathF.Pow(Math.Max(0, surface.X * dx + surface.Y * dy + surface.Z * dz), 1.6f);
                    // At Choke 0 it tails off softly with no end, as light does; more holds it bright and ends it
                    // crisply at Width. The distance is only told out to twice Width.
                    var t = Math.Max(depth, 0) / width;
                    var held = 1 - LightPlanes.Smoothstep(Math.Max(0, (choke - .3f) / .7f) * .95f, 1, t);
                    var tail = MathF.Exp(-2.2f * t);
                    var reaches = (choke < .3f ? tail + (held - tail) * (choke / .3f) : held) * (1 - LightPlanes.Smoothstep(1.5f, 1.9f, t));
                    var catches = Math.Clamp(1 + Detail(x, y) * 6, .3f, 1.8f);
                    var inside = LightPlanes.Smoothstep(-1.5f, 1.5f, depth);
                    var amount = lit * reaches * catches * covered * inside;
                    // Past the edge, loose strands: thin, they're all edge, lit by how they face the light from the subject.
                    var strand = Math.Clamp((apart[i] - .03f - 2.5f * MathF.Sqrt(Math.Max(0, varies[i]))) / .1f, 0, 1)
                        * LightPlanes.Smoothstep(0, .1f, closeness) * (1 - inside);
                    var faces = 0f;
                    if (strand > 0)
                    {
                        var away = Inwards(near, x, y);
                        var n2 = Normalize(-away.X * 2.75f, -away.Y * 2.75f, 1);
                        faces = MathF.Pow(Math.Max(0, n2.X * dx + n2.Y * dy + n2.Z * dz), 1.6f) * strand;
                    }
                    for (var c = 0; c < 3; c++) light[c][i] += tint[c] * (amount + faces) * a;
                }
            });
        }
        if (settings.Glow > 0)
        {
            // Glow: the rims' light in the air behind the subject, a halo and a wider haze, wrapping a little way in
            // across the edge so the subject sits in it rather than being cut out against it.
            var amount = (float)(settings.Glow / 100 * FullGlow);
            var around = Blur(outline, side * .008);
            for (var c = 0; c < 3; c++)
            {
                var halo = Blur(light[c], side * .012); var haze = Blur(light[c], side * .035);
                for (var i = 0; i < n; i++)
                    light[c][i] += (halo[i] * amount * .6f + haze[i] * amount) * Math.Clamp(2 - 2 * around[i], 0, 1);
            }
        }
        return light;
    }

    private static (float X, float Y, float Z) Normalize(float x, float y, float z)
    {
        var length = MathF.Sqrt(x * x + y * y + z * z);
        return length > 0 ? (x / length, y / length, z / length) : (0, 0, 1);
    }

    /// <summary>The inverse of the error function (Giles's approximation), for how far into a blurred edge a level lies.</summary>
    internal static float InverseError(float x)
    {
        var w = -MathF.Log((1 - x) * (1 + x)); float p;
        if (w < 5)
        {
            w -= 2.5f;
            p = 2.81022636e-08f; p = 3.43273939e-07f + p * w; p = -3.5233877e-06f + p * w; p = -4.39150654e-06f + p * w;
            p = 0.00021858087f + p * w; p = -0.00125372503f + p * w; p = -0.00417768164f + p * w; p = 0.246640727f + p * w;
            p = 1.50140941f + p * w;
        }
        else
        {
            w = MathF.Sqrt(w) - 3;
            p = -0.000200214257f; p = 0.000100950558f + p * w; p = 0.00134934322f + p * w; p = -0.00367342844f + p * w;
            p = 0.00573950773f + p * w; p = -0.0076224613f + p * w; p = 0.00943887047f + p * w; p = 1.00167406f + p * w;
            p = 2.83297682f + p * w;
        }
        return p * x;
    }
}
