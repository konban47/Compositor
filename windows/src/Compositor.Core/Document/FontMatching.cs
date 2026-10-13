using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Document;

public static class FontMatching
{
    public static IReadOnlyList<string> Find(SKBitmap sample, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 100) return [];
        var expected = Signature(sample); var scores = new List<(string Font, double Score)>();
        foreach (var name in FontCatalog.Families)
        {
            using var rendered = TextEdits.Image(new() { Content = text, FontName = name, FontSize = 36 });
            if (rendered is null) continue;
            var actual = Signature(rendered);
            var score = expected.Pixels.Zip(actual.Pixels).Sum(p => Math.Abs(p.First - p.Second)) / 1024
                + Math.Abs(Math.Log(Math.Max(.001, actual.Ratio / expected.Ratio))) * .15;
            scores.Add((name, score));
        }
        return scores.OrderBy(s => s.Score).Take(30).Select(s => s.Font).ToArray();
    }
    private static (double[] Pixels, double Ratio) Signature(SKBitmap image)
    {
        var background = image.GetPixel(0, 0);
        double Ink(int x, int y)
        {
            var c = image.GetPixel(x, y);
            return background.Alpha < 128 ? c.Alpha / 255.0
                : (Math.Abs(c.Red - background.Red) + Math.Abs(c.Green - background.Green) + Math.Abs(c.Blue - background.Blue)) / 765.0;
        }
        var l = image.Width; var t = image.Height; var r = 0; var b = 0;
        for (var y = 0; y < image.Height; y++) for (var x = 0; x < image.Width; x++)
            if (Ink(x, y) > .15) { l = Math.Min(l, x); t = Math.Min(t, y); r = Math.Max(r, x + 1); b = Math.Max(b, y + 1); }
        if (r <= l || b <= t) return (new double[1024], 1);
        var values = new double[1024];
        for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++) values[y * 32 + x] = Ink(Math.Min(r - 1, l + x * (r - l) / 32), Math.Min(b - 1, t + y * (b - t) / 32));
        return (values, (double)(r - l) / (b - t));
    }
}
