using System.Runtime.CompilerServices;
using Compositor.Core.Format;
using HarfBuzzSharp;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace Compositor.Core.Document;

/// <summary>
/// OpenType text shaping through HarfBuzz: standard and discretionary ligatures, kerning, contextual and
/// cursive forms, and the joined forms Arabic and the Indic scripts need. Skia draws the glyphs; this
/// chooses them. A face that cannot be shaped falls back to the character-by-character layout.
/// </summary>
internal static class TextShaping
{
    private static readonly ConditionalWeakTable<SKTypeface, ShapedFont> Fonts = new();
    private static readonly object Gate = new();

    /// <summary>One shaped run: the glyphs, where each sits, and how wide the run came out.</summary>
    internal readonly record struct Run(ushort[] Glyphs, SKPoint[] Positions, float Width);

    /// <summary>A HarfBuzz face and font kept alive, with the stream its blob borrows from.</summary>
    private sealed class ShapedFont : IDisposable
    {
        public ShapedFont(SKStreamAsset stream, Blob blob, Face face, Font font)
        {
            Stream = stream;
            Blob = blob;
            Face = face;
            Font = font;
        }

        public SKStreamAsset Stream { get; }
        public Blob Blob { get; }
        public Face Face { get; }
        public Font Font { get; }

        public void Dispose()
        {
            Font.Dispose();
            Face.Dispose();
            Blob.Dispose();
            Stream.Dispose();
        }
    }

    /// <summary>Whether a character is written in a script that needs shaping to look right.</summary>
    public static bool Complex(char character) => character switch
    {
        >= '\u0590' and <= '\u05FF' => true,   // Hebrew
        >= '\u0600' and <= '\u06FF' => true,   // Arabic
        >= '\u0700' and <= '\u074F' => true,   // Syriac
        >= '\u0750' and <= '\u077F' => true,   // Arabic supplement
        >= '\u07C0' and <= '\u07FF' => true,   // NKo, Samaritan
        >= '\u0900' and <= '\u0DFF' => true,   // Indic
        >= '\u0E00' and <= '\u0E7F' => true,   // Thai, Lao
        >= '\u0F00' and <= '\u0FFF' => true,   // Tibetan
        >= '\u1000' and <= '\u109F' => true,   // Myanmar
        >= '\u1780' and <= '\u17FF' => true,   // Khmer
        >= '\u1200' and <= '\u137F' => true,   // Ethiopic
        >= '\u2D30' and <= '\u2D7F' => true,   // Tifinagh
        >= '\uFB1D' and <= '\uFDFF' => true,   // Hebrew/Arabic presentation forms
        >= '\uFE70' and <= '\uFEFF' => true,   // Arabic presentation forms
        _ => false,
    };

    /// <summary>Whether any character of the words is written in such a script.</summary>
    public static bool Complex(string text)
    {
        foreach (var character in text)
        {
            if (Complex(character)) return true;
        }
        return false;
    }

    /// <summary>Whether a character reads right to left.</summary>
    public static bool RightToLeft(char character) =>
        character is >= '\u0590' and <= '\u05FF' or >= '\u0600' and <= '\u06FF' or >= '\u0700' and <= '\u074F'
            or >= '\u0750' and <= '\u077F' or >= '\u07C0' and <= '\u07FF' or >= '\uFB1D' and <= '\uFDFF'
            or >= '\uFE70' and <= '\uFEFF';

    /// <summary>
    /// Shapes one run. Falls back to a glyph-per-character run when the face cannot be shaped, so a broken
    /// font shows something rather than nothing.
    /// </summary>
    public static Run Shape(SKTypeface typeface, double size, string text, TextDirection direction,
        bool ligatures, bool kerning, IReadOnlyList<string>? extra, string? language)
    {
        var shaped = For(typeface);
        if (shaped is null) return Fallback(typeface, size, text);
        try
        {
            var scale = (int)Math.Round(Math.Max(1, size) * 64);
            lock (Gate)
            {
                shaped.Font.SetScale(scale, scale);
                using var buffer = new HarfBuzzSharp.Buffer();
                buffer.AddUtf16(text);
                buffer.Direction = direction switch
                {
                    TextDirection.RightToLeft => HarfBuzzSharp.Direction.RightToLeft,
                    TextDirection.LeftToRight => HarfBuzzSharp.Direction.LeftToRight,
                    _ => HarfBuzzSharp.Direction.Invalid,
                };
                if (language is { Length: > 0 }) buffer.Language = new Language(language);
                buffer.GuessSegmentProperties();
                shaped.Font.Shape(buffer, Features(ligatures, kerning, extra));
                var infos = buffer.GlyphInfos;
                var positions = buffer.GlyphPositions;
                var glyphs = new ushort[infos.Length];
                var points = new SKPoint[infos.Length];
                var x = 0f;
                for (var index = 0; index < infos.Length; index++)
                {
                    glyphs[index] = (ushort)infos[index].Codepoint;
                    points[index] = new SKPoint(x + positions[index].XOffset / 64f, -positions[index].YOffset / 64f);
                    x += positions[index].XAdvance / 64f;
                }
                return new Run(glyphs, points, x);
            }
        }
        catch (Exception)
        {
            return Fallback(typeface, size, text);
        }
    }

    /// <summary>A run of one glyph per character, for a face HarfBuzz will not take.</summary>
    private static Run Fallback(SKTypeface typeface, double size, string text)
    {
        using var font = new SKFont(typeface, (float)Math.Max(1, size));
        var glyphs = new ushort[text.Length];
        var points = new SKPoint[text.Length];
        var x = 0f;
        for (var index = 0; index < text.Length; index++)
        {
            var element = char.ConvertFromUtf32(char.ConvertToUtf32(text, index));
            glyphs[index] = font.GetGlyph(char.ConvertToUtf32(text, index));
            points[index] = new SKPoint(x, 0);
            x += font.MeasureText(element);
            if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length) index++;
        }
        return new Run(glyphs, points, x);
    }

    private static Feature[] Features(bool ligatures, bool kerning, IReadOnlyList<string>? extra)
    {
        var features = new List<Feature>(2 + (extra?.Count ?? 0))
        {
            new(new Tag('l', 'i', 'g', 'a'), ligatures ? 1u : 0u),
            new(new Tag('k', 'e', 'r', 'n'), kerning ? 1u : 0u),
        };
        foreach (var tag in extra ?? [])
        {
            if (Feature.TryParse(tag, out var feature)) features.Add(feature);
        }
        return [.. features];
    }

    private static ShapedFont? For(SKTypeface typeface)
    {
        // GetValue adds atomically, so two threads shaping at once build one face, not two.
        try
        {
            return Fonts.GetValue(typeface, Build);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static ShapedFont Build(SKTypeface typeface)
    {
        var stream = typeface.OpenStream(out var ttcIndex);
        var blob = stream.ToHarfBuzzBlob();
        var face = new Face(blob, ttcIndex);
        var font = new Font(face);
        return new ShapedFont(stream, blob, face, font);
    }
}
