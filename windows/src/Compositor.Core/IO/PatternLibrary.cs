using Compositor.Core.Document;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;
namespace Compositor.Core.IO;

/// <summary>Original, procedural, seamless tiles shipped with Compositor; no third-party brush assets.</summary>
public static class PatternLibrary
{
    public static IReadOnlyList<EditorPreset> Defaults()
    {
        var result = new List<EditorPreset>();
        for (var group = 0; group < 3; group++) for (var variant = 0; variant < 3; variant++)
        {
            using var bitmap = Bitmaps.Allocate(Bitmaps.ColorInfo(64, 64));
            for (var y = 0; y < 64; y++) for (var x = 0; x < 64; x++)
            {
                var wave = Math.Sin(x * Math.PI / 16 + Math.Sin(y * Math.PI / 16) * (variant + 1));
                var grain = BrushDabs.Random((uint)(group * 3 + variant + 12), x + y * 64, 2);
                SKColor color = group switch
                {
                    0 => new SKColor((byte)(15 + grain * 20), (byte)(35 + (wave + 1) * 35 + grain * 35), (byte)(20 + grain * 20)),
                    1 => new SKColor((byte)(35 + grain * 35), (byte)(65 + (wave + 1) * 30), (byte)(20 + grain * 25)),
                    _ => new SKColor((byte)(35 + (wave + 1) * 25), (byte)(85 + (wave + 1) * 35), (byte)(125 + (wave + 1) * 45)),
                }; bitmap.SetPixel(x, y, color);
            }
            var id = Guid.Parse($"C09D89E0-0262-4C28-A883-{group * 3 + variant + 1:000000000000}");
            result.Add(new EditorPreset(id, new[] { "Trees", "Grass", "Water" }[group] + " / " + (variant + 1), "Pattern", Image: Convert.ToBase64String(PngCodec.Encode(bitmap))));
        }
        return result;
    }
}
