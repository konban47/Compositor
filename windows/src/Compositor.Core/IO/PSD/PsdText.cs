using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;
using static Compositor.Core.IO.PSD.PsdMetadata;

namespace Compositor.Core.IO.PSD;

/// <summary>Type Tool Object Setting (TySh), including UTF-16 content and engine style runs.</summary>
internal static class PsdText
{
    internal sealed record Source(LayerTextStyle Style, double X, double Y, double Rotation, bool FlipY, bool Frame, List<string> Notes);

    internal static PsdRect? Artboard(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > 1000000) return null;
        try { var reader = new Descriptor(bytes); var rect = Rect(reader.Read(), "artboardRect");
            if (rect is not { Width: >= 1, Height: >= 1 } r || r.Width > DocumentLimits.MaxSide || r.Height > DocumentLimits.MaxSide || Math.Abs(r.Left) > 1000000 || Math.Abs(r.Top) > 1000000) return null;
            var bounds = SKRectI.Ceiling(r); return new(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom); }
        catch (Exception error) when (error is InvalidDataException or ImportException or OverflowException) { return null; }
    }
    public static Source? Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 60 or > 8_000_000) return null;
        try
        {
            var reader = new Descriptor(bytes);
            if (reader.Cursor.U16() != 1) return null;
            var a = reader.Double(); var b = reader.Double(); var c = reader.Double(); var d = reader.Double();
            var tx = reader.Double(); var ty = reader.Double();
            if (new[] { a, b, c, d, tx, ty }.Any(n => !double.IsFinite(n)) || Math.Abs(tx) > 1e6 || Math.Abs(ty) > 1e6) return null;
            var sx = Math.Sqrt(a * a + b * b); var determinant = a * d - b * c;
            if (sx < 1e-6 || Math.Abs(determinant) < 1e-9 || sx > 10000) return null;
            var sy = Math.Abs(determinant) / sx;
            if (reader.Cursor.U16() != 50) return null;
            var descriptor = reader.Read(); var notes = new List<string>();
            object? engine = Get(descriptor, "EngineData") is byte[] data ? new Engine(data).Read() : null;
            var raw = Get(descriptor, "Txt ") as string ?? Get(engine, "EngineDict", "Editor", "Text") as string;
            if (raw is null || raw.Length > 100000) return null;
            // The engine adds one terminating CR; interior CRs are editable paragraph breaks.
            raw = raw.TrimEnd('\0'); if (raw.EndsWith('\r')) raw = raw[..^1];
            var style = new LayerTextStyle { Content = raw.Replace("\r\n", "\n").Replace('\r', '\n'), FontSize = Math.Clamp(12 * sx, 1, 2000),
                Vertical = Get(descriptor, "Ornt") as string == "Vrtc" ? true : null };
            var runs = Array(Get(engine, "EngineDict", "StyleRun", "RunArray"));
            var lengths = Array(Get(engine, "EngineDict", "StyleRun", "RunLengthArray"));
            var fonts = Array(Get(engine, "ResourceDict", "FontSet"));
            if (runs.Count > 0) Apply(style, Get(runs[0], "StyleSheet", "StyleSheetData"), fonts, sx, sy);
            var styleRuns = new List<LayerTextStyleRun>(); var colorRuns = new List<LayerTextColorRun>(); var fontRuns = new List<LayerTextFontRun>(); var offset = 0;
            for (var i = 0; i < runs.Count && i < lengths.Count && offset < style.Content.Length; i++)
            {
                var length = (int)Math.Clamp(Number(lengths[i]), 0, style.Content.Length - offset);
                if (length == 0) continue;
                var dataRun = Get(runs[i], "StyleSheet", "StyleSheetData");
                var runStyle = TextEdits.Copy(style); Apply(runStyle, dataRun, fonts, sx, sy);
                styleRuns.Add(new() { Location = offset, Length = length, Style = TextRanges.Flat(runStyle) });
                colorRuns.Add(new() { Location = offset, Length = length, Red = runStyle.Red, Green = runStyle.Green, Blue = runStyle.Blue });
                fontRuns.Add(new() { Location = offset, Length = length, FontName = runStyle.FontName });
                offset += length;
            }
            style.StyleRuns = styleRuns.Count > 0 ? styleRuns : null;
            var paragraphs = Array(Get(engine, "EngineDict", "ParagraphRun", "RunArray"));
            var paragraph = Get(paragraphs.FirstOrDefault(), "ParagraphSheet", "Properties");
            var justification = (int)Number(Get(paragraph, "Justification"));
            style.Alignment = justification switch { 1 => TextAlignment.Right, 2 => TextAlignment.Center, _ => TextAlignment.Left };
            style.Layout = (style.Layout ?? new()) with {
                LeftIndent = Math.Clamp(Number(Get(paragraph, "StartIndent")) * sx, 0, 10000),
                RightIndent = Math.Clamp(Number(Get(paragraph, "EndIndent")) * sx, 0, 10000),
                FirstLineIndent = Math.Clamp(Number(Get(paragraph, "FirstLineIndent")) * sx, -10000, 10000),
                SpaceBefore = Math.Clamp(Number(Get(paragraph, "SpaceBefore")) * sx, 0, 10000),
                SpaceAfter = Math.Clamp(Number(Get(paragraph, "SpaceAfter")) * sx, 0, 10000),
                Justification = justification switch { 3 => TextJustification.LastLeft, 4 => TextJustification.LastRight, 5 => TextJustification.LastCenter, 6 => TextJustification.All, _ => TextJustification.None } };
            var frame = false;
            if (Rect(descriptor, "bounds") is { } bounds && Rect(descriptor, "boundingBox") is { } glyphs
                && bounds.Width > glyphs.Width + 4 && bounds.Height > glyphs.Height + 4)
            {
                var w = bounds.Width * sx + TextEdits.Padding * 2; var h = bounds.Height * sy + TextEdits.Padding * 2;
                if (w is >= 16 and <= DocumentLimits.MaxSide && h is >= 16 and <= DocumentLimits.MaxSide && w * h <= DocumentLimits.MaxSurfacePixels)
                {
                    style.BoxSize = new JsonSize(w, h); frame = true;
                    tx += a * bounds.Left + c * bounds.Top; ty += b * bounds.Left + d * bounds.Top;
                }
            }
            if (Math.Abs(a * c + b * d) > 0.02 * sx * sy) notes.Add("The text shear was approximated; text remains editable.");
            if (reader.Cursor.Remaining >= 6 && reader.Cursor.U16() == 1)
            {
                var warp = reader.Read();
                if (Get(warp, "warpStyle") is string warpStyle && warpStyle is not ("warpNone" or "none")) notes.Add("The Photoshop text warp was omitted; text remains editable.");
            }
            if (!style.IsValid) return null;
            return new(style, tx, ty, Math.Atan2(b, a) * 180 / Math.PI, determinant < 0, frame, notes);
        }
        catch (Exception error) when (error is InvalidDataException or FormatException or ImportException or ArgumentException or OverflowException) { return null; }
    }

    private static void Apply(LayerTextStyle style, object? sheet, List<object?> fonts, double sx, double sy)
    {
        style.FontSize = Math.Clamp(Number(Get(sheet, "FontSize"), 12) * sx, 1, 2000);
        var font = (int)Number(Get(sheet, "Font"));
        if (font >= 0 && font < fonts.Count && Get(fonts[font], "Name") is string name && name.Length is > 0 and <= 200) style.FontName = name.TrimEnd('\0');
        var color = Array(Get(sheet, "FillColor", "Values"));
        double Channel(int at) { var value = Number(color[at]); return Math.Clamp(value > 1 ? value / 255 : value, 0, 1); }
        if (color.Count >= 4) { style.Red = Channel(1); style.Green = Channel(2); style.Blue = Channel(3); }
        else if (color.Count == 3) { style.Red = Channel(0); style.Green = Channel(1); style.Blue = Channel(2); }
        style.Tracking = Math.Clamp(Number(Get(sheet, "Tracking")) * style.FontSize / 1000, -100, 1000);
        style.Leading = Get(sheet, "AutoLeading") is false ? Math.Clamp(Number(Get(sheet, "Leading")) * sx, 0, 5000) : 0;
        style.Bold = Get(sheet, "FauxBold") as bool?; style.Italic = Get(sheet, "FauxItalic") as bool?;
        style.Underline = Get(sheet, "Underline") as bool?; style.Strikethrough = Get(sheet, "Strikethrough") as bool?;
        style.Ligatures = Get(sheet, "Ligatures") as bool?;
        style.Layout = new() { HorizontalScale = Math.Clamp(Number(Get(sheet, "HorizontalScale"), 1) * 100, 1, 1000),
            VerticalScale = Math.Clamp(Number(Get(sheet, "VerticalScale"), 1) * sy / sx * 100, 1, 1000),
            BaselineShift = Math.Clamp(Number(Get(sheet, "BaselineShift")) * sx, -10000, 10000) };
    }

    private static SKRect? Rect(object descriptor, string key)
    {
        var value = Get(descriptor, key);
        if (value is not Dictionary<string, object?> map) return null;
        double Side(string name) => Number(map.GetValueOrDefault(name) ?? map.GetValueOrDefault(name.Trim()), double.NaN);
        var l = Side("Left"); var t = Side("Top "); var r = Side("Rght"); var b = Side("Btom");
        return new[] { l, t, r, b }.All(double.IsFinite) ? new SKRect((float)l, (float)t, (float)r, (float)b) : null;
    }

    public static (SKBitmap Image, Format.LayerTransform Transform)? Render(Source source)
    {
        if (TextEdits.Image(source.Style) is not { } image) return null;
        var anchor = source.Frame ? new SKPoint((float)TextEdits.Padding, (float)TextEdits.Padding) : TextEdits.Caret(source.Style, 0);
        if (!source.Frame && source.Style.Alignment != TextAlignment.Left)
            anchor.X = source.Style.Alignment == TextAlignment.Center ? image.Width / 2f : image.Width - (float)TextEdits.Padding;
        var dx = anchor.X - image.Width / 2.0; var dy = (anchor.Y - image.Height / 2.0) * (source.FlipY ? -1 : 1);
        var angle = source.Rotation * Math.PI / 180;
        var x = source.X - (Math.Cos(angle) * dx - Math.Sin(angle) * dy) - image.Width / 2.0;
        var y = source.Y - (Math.Sin(angle) * dx + Math.Cos(angle) * dy) - image.Height / 2.0;
        return (image, new() { Origin = new(x, y), Size = new(image.Width, image.Height), Rotation = source.Rotation, FlipY = source.FlipY, Sampling = LayerSampling.HighQuality });
    }
}
