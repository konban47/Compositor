using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Pixels;

public static partial class EffectRasterizer
{
    private static EffectRaster? RenderStyle(SKBitmap pixels, LayerEffects effects, double fillOpacity, bool transparency = true)
    {
        if (!effects.IsValid) throw new ProjectException(ProjectError.Invalid);
        var items = effects.Items!.Where(e => e.Enabled && e.Opacity > 0).ToArray();
        if (items.Length == 0) return null;
        var margin = items.Max(e => e.Kind switch
        {
            StyleEffectKind.DropShadow => e.Distance + e.Size * 3,
            StyleEffectKind.OuterGlow => e.Size * 3,
            StyleEffectKind.Stroke => e.Position == StrokePosition.Inside ? 0 : e.Size,
            StyleEffectKind.BevelEmboss => e.Size + e.Soften * 3,
            _ => 0,
        });
        var inset = (int)Math.Ceiling(margin) + 2;
        var width = checked(pixels.Width + inset * 2); var height = checked(pixels.Height + inset * 2);
        if ((long)width * height > DocumentLimits.MaxSurfacePixels) throw new ProjectException(ProjectError.TooLarge);
        var count = checked(width * height);
        var shape = new float[count]; var work = new float[count]; var temp = new float[count];
        var canvas = new byte[checked(count * 4)];
        var source = pixels.GetPixelSpan();
        for (var y = 0; y < pixels.Height; y++)
        for (var x = 0; x < pixels.Width; x++)
            shape[(y + inset) * width + x + inset] = transparency ? source[y * pixels.RowBytes + x * 4 + 3] / 255f : 1;

        bool Behind(StyleEffect e) => e.Kind is StyleEffectKind.DropShadow or StyleEffectKind.OuterGlow
            || e.Kind == StyleEffectKind.Stroke && e.Position == StrokePosition.Outside;
        bool Interior(StyleEffect e) => e.Kind is StyleEffectKind.InnerShadow or StyleEffectKind.InnerGlow or StyleEffectKind.Satin
            or StyleEffectKind.ColorOverlay or StyleEffectKind.GradientOverlay or StyleEffectKind.PatternOverlay;
        var interiorPass = false;
        foreach (var effect in items.Where(Behind)) Draw(effect);
        var behind = canvas; canvas = new byte[behind.Length];
        // Blend interiors in the layer's coverage space, then apply its alpha once. This keeps translucent edges translucent.
        for (var y = 0; y < pixels.Height; y++) for (var x = 0; x < pixels.Width; x++)
        {
            var at = (y + inset) * width + x + inset; var from = y * pixels.RowBytes + x * 4;
            if (shape[at] == 0) continue;
            for (var c = 0; c < 4; c++) canvas[at * 4 + c] = (byte)Math.Clamp(Math.Round(source[from + c] * Math.Clamp(fillOpacity, 0, 1) / shape[at]), 0, 255);
        }
        interiorPass = true;
        foreach (var effect in items.Where(Interior)) Draw(effect);
        interiorPass = false;
        for (var i = 0; i < count; i++) for (var c = 0; c < 4; c++) canvas[i * 4 + c] = (byte)Math.Round(canvas[i * 4 + c] * shape[i]);
        var foreground = canvas; canvas = behind;
        Over(canvas, foreground, width * 4, width, height, 0, width);
        foreach (var effect in items.Where(e => !Behind(e) && !Interior(e))) Draw(effect);
        var bitmap = Bitmaps.Allocate(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()));
        for (var y = 0; y < height; y++) canvas.AsSpan(y * width * 4, width * 4).CopyTo(bitmap.GetPixelSpan().Slice(y * bitmap.RowBytes, width * 4));
        return new EffectRaster(bitmap, -inset, -inset);

        void Draw(StyleEffect e)
        {
            var angle = e.UseGlobalLight ? effects.GlobalLightAngle : e.Angle;
            var (dx, dy) = Offset(angle, e.Distance);
            Array.Clear(work);
            switch (e.Kind)
            {
                case StyleEffectKind.DropShadow:
                    SpreadShape(e, smallest: false); Shift(work, temp, width, height, dx, dy);
                    temp.CopyTo(work, 0); Blur(work, work, temp, width, height, e.Size * (1 - e.Spread / 100));
                    break;
                case StyleEffectKind.InnerShadow:
                    SpreadShape(e, smallest: true); Shift(work, temp, width, height, dx, dy);
                    temp.CopyTo(work, 0); Blur(work, work, temp, width, height, e.Size * (1 - e.Spread / 100));
                    for (var i = 0; i < count; i++) work[i] = shape[i] * (1 - work[i]);
                    break;
                case StyleEffectKind.OuterGlow:
                    SpreadShape(e, smallest: false); Blur(work, work, temp, width, height, e.Size * (1 - e.Spread / 100));
                    for (var i = 0; i < count; i++) work[i] *= 1 - shape[i];
                    break;
                case StyleEffectKind.InnerGlow:
                    SpreadShape(e, smallest: true); Blur(work, work, temp, width, height, e.Size * (1 - e.Spread / 100));
                    for (var i = 0; i < count; i++) work[i] = shape[i] * (e.CenterSource ? work[i] : 1 - work[i]);
                    break;
                case StyleEffectKind.Stroke:
                    var reach = Math.Max(0, (int)Math.Ceiling(e.Size * (e.Position == StrokePosition.Center ? 0.5 : 1)));
                    if (reach == 0) return;
                    Extreme(shape, work, temp, width, height, reach, e.Position == StrokePosition.Inside);
                    for (var i = 0; i < count; i++) work[i] = e.Position == StrokePosition.Inside ? shape[i] - work[i] : work[i] - shape[i];
                    if (e.Position == StrokePosition.Center)
                    {
                        var inside = new float[count]; Extreme(shape, inside, temp, width, height, reach, true);
                        for (var i = 0; i < count; i++) work[i] += shape[i] - inside[i];
                    }
                    break;
                case StyleEffectKind.Satin:
                    Shift(shape, work, width, height, dx, dy); Blur(work, work, temp, width, height, e.Size);
                    var opposite = new float[count]; Shift(shape, opposite, width, height, -dx, -dy); Blur(opposite, opposite, temp, width, height, e.Size);
                    for (var i = 0; i < count; i++) work[i] = shape[i] * (float)(e.Invert ? 1 - Math.Abs(work[i] - opposite[i]) : Math.Abs(work[i] - opposite[i]));
                    break;
                case StyleEffectKind.BevelEmboss:
                    Bevel(e, angle); return;
                default: shape.CopyTo(work, 0); break;
            }
            if (e.ContourEnabled && e.Kind != StyleEffectKind.BevelEmboss)
                for (var i = 0; i < count; i++) if (work[i] > 0) work[i] = (float)Contour(e.Contour, work[i]);
            Paint(e, work, e.BlendMode, e.Opacity);
        }
        void SpreadShape(StyleEffect e, bool smallest)
        {
            var spread = (int)Math.Round(e.Size * e.Spread / 100);
            if (spread > 0) Extreme(shape, work, temp, width, height, spread, smallest);
            else shape.CopyTo(work, 0);
        }
        void Bevel(StyleEffect e, double angle)
        {
            if (e.Size <= 0) return;
            using var texture = e.TextureEnabled && e.Pattern == StylePattern.Image && e.PatternPng is { } encodedTexture ? DecodePattern(encodedTexture) : null;
            var heights = new float[count];
            Blur(shape, heights, temp, width, height, Math.Max(1, e.Size));
            if (e.Soften > 0) Blur(heights, heights, temp, width, height, e.Soften);
            var highlights = new float[count]; var shadows = new float[count];
            var rad = angle * Math.PI / 180; var altitude = e.Altitude * Math.PI / 180;
            for (var y = 1; y < height - 1; y++)
            for (var x = 1; x < width - 1; x++)
            {
                var i = y * width + x;
                var slopeX = heights[i + 1] - heights[i - 1]; var slopeY = heights[i + width] - heights[i - width];
                var slope = (slopeX * Math.Cos(rad) - slopeY * Math.Sin(rad)) * e.Depth / 100 * Math.Sqrt(e.Size) * Math.Cos(altitude);
                if (e.Invert) slope = -slope;
                var coverage = e.Bevel switch
                {
                    BevelKind.InnerBevel => shape[i], BevelKind.OuterBevel => 1 - shape[i],
                    BevelKind.PillowEmboss => 1.0, BevelKind.StrokeEmboss => Math.Clamp(heights[i] * (1 - heights[i]) * 4, 0, 1), _ => 1.0,
                };
                if (e.Bevel == BevelKind.PillowEmboss && shape[i] < 0.5f) slope = -slope;
                if (e.Technique > 0) slope = Math.Sign(slope) * Math.Pow(Math.Abs(slope), e.Technique == 1 ? 0.45 : 0.75);
                if (e.TextureEnabled && shape[i] > 0) slope += Pattern(e, x - inset, y - inset, texture) * e.TextureDepth / 200 * shape[i];
                var amount = Math.Clamp(Math.Abs(slope), 0, 1);
                if (e.ContourEnabled) amount = Contour(e.Contour, amount);
                if (slope > 0) highlights[i] = (float)(amount * coverage); else shadows[i] = (float)(amount * coverage);
            }
            Paint(e, highlights, e.HighlightMode, e.HighlightOpacity * e.Opacity, second: true);
            Paint(e, shadows, e.ShadowMode, e.ShadowOpacity * e.Opacity);
        }
        void Paint(StyleEffect e, float[] coverage, LayerBlendMode mode, double opacity, bool second = false)
        {
            var plane = new byte[canvas.Length];
            var fill = e.Kind == StyleEffectKind.GradientOverlay ? 1 : e.Kind == StyleEffectKind.PatternOverlay ? 2 : e.Kind == StyleEffectKind.Stroke ? e.FillType : 0;
            using var pattern = fill == 2 && e.Pattern == StylePattern.Image && e.PatternPng is { } encoded ? DecodePattern(encoded) : null;
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x; var at = i * 4;
                var alpha = Math.Clamp(coverage[i] * opacity / (interiorPass && shape[i] > 0 ? shape[i] : 1), 0, 1);
                if (alpha <= 0) continue;
                if (e.Noise > 0) alpha *= 1 - e.Noise / 100 * ((unchecked((uint)(x * 73856093 ^ y * 19349663)) % 1024) / 1023.0);
                var t = second ? 1.0 : 0.0;
                if (fill == 1) t = Gradient(e, x - inset, y - inset, pixels.Width, pixels.Height);
                if (fill == 2) t = Pattern(e, x - inset, y - inset, pattern);
                var r = e.Red + (e.Red2 - e.Red) * t; var g = e.Green + (e.Green2 - e.Green) * t; var b = e.Blue + (e.Blue2 - e.Blue) * t;
                if (fill == 2 && pattern is not null)
                {
                    var p = PatternColor(e, x - inset, y - inset, pattern); r = p.Red / 255.0; g = p.Green / 255.0; b = p.Blue / 255.0; alpha *= p.Alpha / 255.0;
                }
                plane[at] = ToByte((float)(r * alpha)); plane[at + 1] = ToByte((float)(g * alpha));
                plane[at + 2] = ToByte((float)(b * alpha)); plane[at + 3] = ToByte((float)alpha);
            }
            var blend = BlendModes.From(mode);
            if (BlendModes.Skia(blend) is { } skia)
            {
                using var background = Bitmaps.Allocate(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
                using var foreground = Bitmaps.Allocate(background.Info);
                canvas.CopyTo(background.GetPixelSpan()); plane.CopyTo(foreground.GetPixelSpan());
                using (var drawing = new SKCanvas(background))
                using (var paint = new SKPaint { BlendMode = skia }) drawing.DrawBitmap(foreground, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), paint);
                background.GetPixelSpan().CopyTo(canvas);
            }
            else BlendModes.Composite(blend, plane, width * 4, canvas, width * 4, canvas, width * 4, width, height);
        }
    }

    public static double Contour(StyleContour contour, double value)
    {
        var t = Math.Clamp(value, 0, 1);
        return contour switch
        {
            StyleContour.Round => Math.Sqrt(t), StyleContour.Cone => 1 - Math.Abs(2 * t - 1),
            StyleContour.InvertedCone => Math.Abs(2 * t - 1), StyleContour.Ring => Math.Sin(Math.PI * t) * Math.Sin(Math.PI * t),
            StyleContour.DoubleRing => Math.Sin(2 * Math.PI * t) * Math.Sin(2 * Math.PI * t), _ => t,
        };
    }
    private static double Gradient(StyleEffect e, double x, double y, int width, int height)
    {
        var radians = e.Angle * Math.PI / 180;
        var dx = (x - width / 2.0 - e.OffsetX) / Math.Max(1, width * e.Scale / 100);
        var dy = (y - height / 2.0 - e.OffsetY) / Math.Max(1, height * e.Scale / 100);
        var along = dx * Math.Cos(radians) - dy * Math.Sin(radians);
        var across = dx * Math.Sin(radians) + dy * Math.Cos(radians);
        var t = e.Gradient switch
        {
            StyleGradient.Radial => Math.Sqrt(dx * dx + dy * dy) * 2,
            StyleGradient.Angle => (Math.Atan2(across, along) / (2 * Math.PI) + 1) % 1,
            StyleGradient.Reflected => Math.Abs(along) * 2, StyleGradient.Diamond => (Math.Abs(along) + Math.Abs(across)) * 2,
            _ => along + 0.5,
        };
        t = Math.Clamp(t, 0, 1); return e.Invert ? 1 - t : t;
    }
    private static double Pattern(StyleEffect e, double x, double y, SKBitmap? image)
    {
        if (image is not null) { var p = PatternColor(e, x, y, image); return (p.Red + p.Green + p.Blue) / 765.0; }
        var rad = e.Angle * Math.PI / 180; var scale = Math.Max(1, 16 * e.Scale / 100);
        var a = ((x + e.OffsetX) * Math.Cos(rad) - (y + e.OffsetY) * Math.Sin(rad)) / scale;
        var b = ((x + e.OffsetX) * Math.Sin(rad) + (y + e.OffsetY) * Math.Cos(rad)) / scale;
        var t = e.Pattern switch
        {
            StylePattern.Stripes => a - Math.Floor(a) < 0.5 ? 1.0 : 0,
            StylePattern.Dots => Math.Pow(a - Math.Floor(a) - 0.5, 2) + Math.Pow(b - Math.Floor(b) - 0.5, 2) < 0.10 ? 1.0 : 0,
            _ => (((int)Math.Floor(a) + (int)Math.Floor(b)) & 1) == 0 ? 1.0 : 0,
        };
        return e.Invert ? 1 - t : t;
    }
    private static SKColor PatternColor(StyleEffect e, double x, double y, SKBitmap image)
    {
        var rad = e.Angle * Math.PI / 180; var scale = e.Scale / 100;
        int Wrap(double v, int n) => ((int)Math.Floor(v) % n + n) % n;
        return image.GetPixel(Wrap(((x + e.OffsetX) * Math.Cos(rad) - (y + e.OffsetY) * Math.Sin(rad)) / scale, image.Width),
            Wrap(((x + e.OffsetX) * Math.Sin(rad) + (y + e.OffsetY) * Math.Cos(rad)) / scale, image.Height));
    }
    public static SKBitmap DecodePattern(string encoded)
    {
        if (encoded.Length > 2_800_000) throw new ProjectException(ProjectError.TooLarge);
        var bytes = Convert.FromBase64String(encoded); var header = PngCodec.ReadHeader(bytes);
        if (header.Width > 1024 || header.Height > 1024 || !header.IsEightBitOrLess) throw new ProjectException(ProjectError.TooLarge);
        return PngCodec.DecodeImage(header, bytes);
    }
}
