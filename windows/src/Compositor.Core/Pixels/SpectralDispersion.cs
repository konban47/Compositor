using System.Numerics;
using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;
namespace Compositor.Core.Pixels;
/// <summary>CPU counterpart of upstream MetalDispersion: spectral weights, bilinear taps and directional streaks.</summary>
public static class SpectralDispersion
{
    public static SKBitmap Apply(SKBitmap image, FilterSettings settings, long originX = 0, long originY = 0, int canvasWidth = 0, int canvasHeight = 0, SKRectI? clampBounds = null, int sides = 0)
    {
        if (!settings.IsValid(FilterKind.RgbSplit)) throw new ArgumentOutOfRangeException(nameof(settings));
        var width = image.Width; var height = image.Height;
        using var source = Bitmaps.Allocate(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(source)) canvas.DrawBitmap(image, new SKPoint(), new SKSamplingOptions(SKFilterMode.Nearest));
        var result = Bitmaps.Allocate(source.Info);
        try
        {
            Render(source.GetPixelSpan(), result.GetPixelSpan(), width, height, source.RowBytes, settings, originX, originY, canvasWidth, canvasHeight, clampBounds, sides); return result;
        }
        catch { result.Dispose(); throw; }
    }
    private static void Render(ReadOnlySpan<byte> source, Span<byte> result, int width, int height, int stride, FilterSettings s, long ox, long oy, int canvasWidth, int canvasHeight, SKRectI? clampBounds, int sides)
    {
        if (s.RgbSplitDistance == 0 && s.RgbSplitBlur == 0) { source.CopyTo(result); return; }
        var n = s.RgbSplitDistance > 0 ? Math.Clamp((int)(s.RgbSplitDistance * (s.RgbSplitRadial ? 2 : 1)), 3, 32) : 1;
        var m = s.RgbSplitBlur > 0 ? Math.Clamp((int)(s.RgbSplitBlur * (s.RgbSplitRadial ? 2 : 1) / 2), 2, 24) : 1;
        var angle = s.RgbSplitAngle * Math.PI / 180; var dx = (float)Math.Cos(angle); var dy = (float)-Math.Sin(angle);
        var cx = (float)(s.RgbSplitCenterX / 100 * width); var cy = (float)(s.RgbSplitCenterY / 100 * height);
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var at = new Vector2(x + .5f, y + .5f);
            var axis = s.RgbSplitRadial ? (at - new Vector2(cx, cy)) / (float)Math.Max(1, s.RgbSplitReach) : new Vector2(dx, dy);
            var spread = axis * (float)s.RgbSplitDistance; var streak = axis * (float)s.RgbSplitBlur;
            var hash = Math.Sin((x + ox) * 12.9898 + (y + oy) * 78.233) * 43758.5453; var jitter = (float)(hash - Math.Floor(hash));
            var color = Vector3.Zero; var alpha = Vector3.Zero; var total = Vector3.Zero;
            for (var i = 0; i < n; i++)
            {
                var t = n == 1 ? 0 : (i + jitter) / n * 2 - 1;
                var weight = new Vector3(Smooth(-.3f, 1, t), 1 - Smooth(0, 1, Math.Abs(t)) + .05f, Smooth(-.3f, 1, -t));
                for (var j = 0; j < m; j++)
                {
                    var u = m == 1 ? 0 : (j + jitter) / m - .5f; var q = at - spread * t - streak * u;
                    if (canvasWidth > 0) { q.X = Math.Clamp(q.X + ox, .5f, canvasWidth - .5f) - ox; q.Y = Math.Clamp(q.Y + oy, .5f, canvasHeight - .5f) - oy; }
                    if (clampBounds is { } edge) { if ((sides & 1) != 0) q.X = Math.Max(q.X, edge.Left + .5f); if ((sides & 2) != 0) q.X = Math.Min(q.X, edge.Right - .5f); if ((sides & 4) != 0) q.Y = Math.Max(q.Y, edge.Top + .5f); if ((sides & 8) != 0) q.Y = Math.Min(q.Y, edge.Bottom - .5f); }
                    var c = Tap(source, width, height, stride, q.X - .5f, q.Y - .5f);
                    color += weight * new Vector3(c.X, c.Y, c.Z); alpha += weight * c.W; total += weight;
                }
            }
            color /= total; alpha /= total; var a = Math.Max(alpha.X, Math.Max(alpha.Y, alpha.Z)); var index = y * stride + x * 4;
            result[index] = Byte(Math.Min(a, color.X)); result[index + 1] = Byte(Math.Min(a, color.Y)); result[index + 2] = Byte(Math.Min(a, color.Z)); result[index + 3] = Byte(a);
        }
    }
    private static byte Byte(float v) => (byte)Math.Clamp(MathF.Round(v), 0, 255);
    private static float Smooth(float low, float high, float x) { var t = Math.Clamp((x - low) / (high - low), 0, 1); return t * t * (3 - 2 * t); }
    private static Vector4 Tap(ReadOnlySpan<byte> pixels, int width, int height, int stride, float x, float y)
    {
        x = Math.Clamp(x, 0, width - 1); y = Math.Clamp(y, 0, height - 1); var left = (int)x; var top = (int)y;
        var right = Math.Min(width - 1, left + 1); var bottom = Math.Min(height - 1, top + 1);
        return Vector4.Lerp(Vector4.Lerp(Pixel(pixels, top * stride + left * 4), Pixel(pixels, top * stride + right * 4), x - left), Vector4.Lerp(Pixel(pixels, bottom * stride + left * 4), Pixel(pixels, bottom * stride + right * 4), x - left), y - top);
    }
    private static Vector4 Pixel(ReadOnlySpan<byte> pixels, int at) => new(pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]);
}
