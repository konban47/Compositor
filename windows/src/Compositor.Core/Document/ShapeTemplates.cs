using SkiaSharp;
using System.Globalization;

namespace Compositor.Core.Document;

public static class ShapeTemplates
{
    public static string Polygon(int sides, double innerRatio = 1)
    {
        sides = Math.Clamp(sides, 3, 100);
        innerRatio = Math.Clamp(innerRatio, .01, 1);
        using var path = new SKPathBuilder();
        var count = innerRatio < 1 ? sides * 2 : sides;
        for (var i = 0; i < count; i++)
        {
            var angle = -Math.PI / 2 + Math.PI * 2 * i / count;
            var radius = .5 * (i % 2 == 1 ? innerRatio : 1);
            var point = new SKPoint((float)(.5 + Math.Cos(angle) * radius), (float)(.5 + Math.Sin(angle) * radius));
            if (i == 0) path.MoveTo(point); else path.LineTo(point);
        }
        path.Close(); using var result = path.Detach(); return Normalize(result.ToSvgPathData());
    }
    public static string Normalize(string data)
    {
        if (data.Length > 1_000_000) throw new InvalidDataException("Path is too large.");
        using var path = SKPath.ParseSvgPathData(data) ?? throw new InvalidDataException("Invalid SVG path.");
        var box = path.TightBounds;
        if (path.IsEmpty || box.Width <= 0 || box.Height <= 0 || !float.IsFinite(box.Width + box.Height)) throw new InvalidDataException("Path must enclose a finite area.");
        path.Transform(new SKMatrix { ScaleX = 1 / box.Width, ScaleY = 1 / box.Height, TransX = -box.Left / box.Width, TransY = -box.Top / box.Height, Persp2 = 1 });
        return path.ToSvgPathData();
    }
    public static IReadOnlyDictionary<string, string> Presets { get; } = new Dictionary<string, string>
    {
        ["Heart"] = "M.5,1 C.4,.8 0,.55 0,.25 C0,-.05 .4,-.05 .5,.22 C.6,-.05 1,-.05 1,.25 C1,.55 .6,.8 .5,1 Z",
        ["Arrow"] = "M0,.3 L.6,.3 L.6,0 L1,.5 L.6,1 L.6,.7 L0,.7 Z",
        ["Cross"] = "M.3,0 L.7,0 L.7,.3 L1,.3 L1,.7 L.7,.7 L.7,1 L.3,1 L.3,.7 L0,.7 L0,.3 L.3,.3 Z",
        ["Checkmark"] = "M0,.5 L.15,.35 L.4,.65 L.9,0 L1,.15 L.4,1 Z",
        ["Diamond"] = "M.5,0 L1,.5 L.5,1 L0,.5 Z",
    };
}
