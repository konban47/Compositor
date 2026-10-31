using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Media;

namespace Compositor.Desktop;

/// <summary>
/// Every button icon, tool icon and panel mark in one system: a 24-unit grid with 2 units of margin, 1.5-unit lines with
/// round ends and joins, and small solid parts where Photoshop's tools have them (a brush's bristles, a path arrow). The
/// fifteen tools upstream has follow the SF Symbols it names (ToolRail.swift) in shape and weight; Apple's symbols
/// themselves are not shipped. Tools only Photoshop has follow Photoshop's pictures for them, in the same line. Upstream's
/// own drawings (Clone Stamp, Gradient, Polygonal Lasso, Object Selection) are drawn by <see cref="UpstreamArtwork"/>.
/// A badge, where a tool adds one to a shared shape (a plus, a sparkle), sits in the top-left 6 × 6 corner.
/// </summary>
internal static class IconSet
{
    internal const double Grid = 24, Stroke = 1.5;

    /// <summary>One icon: lines, solid parts, and dashed lines for selections.</summary>
    internal sealed record Glyph(string Outline, string Fill = "", string Dashed = "");

    internal static bool Has(string kind) => Glyphs.ContainsKey(kind);
    internal static IEnumerable<string> Kinds => Glyphs.Keys;
    /// <summary>The lines alone, for a cursor drawn from the icon.</summary>
    internal static string? Outline(string kind) => Glyphs.TryGetValue(kind, out var glyph) && glyph.Outline.Length > 0 ? glyph.Outline : null;

    private static readonly ConcurrentDictionary<string, Geometry> Parsed = new(StringComparer.Ordinal);
    private static Geometry Parse(string data) => Parsed.GetOrAdd(data, StreamGeometry.Parse);

    internal static bool Draw(DrawingContext context, string kind, Size size, IBrush? ink = null)
    {
        if (!Glyphs.TryGetValue(kind, out var glyph)) return false;
        var side = Math.Max(1, Math.Min(size.Width, size.Height)); var scale = side / Grid;
        using var fit = context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation((size.Width - side) / 2, (size.Height - side) / 2));
        ink ??= Skin.LabelBrush;
        if (glyph.Fill.Length > 0) context.DrawGeometry(ink, null, Parse(glyph.Fill));
        if (glyph.Outline.Length > 0)
            context.DrawGeometry(null, new Pen(ink, Stroke) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, Parse(glyph.Outline));
        if (glyph.Dashed.Length > 0)
            context.DrawGeometry(null, new Pen(ink, Stroke) { LineCap = PenLineCap.Flat, LineJoin = PenLineJoin.Round, DashStyle = new DashStyle([1.7, 1.3], 0) }, Parse(glyph.Dashed));
        return true;
    }

    // ---- Shared shapes ----

    /// <summary>paintbrush.pointed: the handle up to the right, the bristles solid, coming to a point at the bottom left.</summary>
    private const string BrushHandle = "M12.4,9.4 L18.9,2.9 Q20,1.8 21.1,2.9 Q22.2,4 21.1,5.1 L14.6,11.6 Z";
    private const string BrushTip = "M12.4,9.4 L14.6,11.6 Q14.9,15.5 11.4,17.4 Q7.7,19.4 2.8,21.2 Q4.6,16.4 6.6,12.6 Q8.4,9.2 12.4,9.4 Z";
    private static Glyph Brush(string badge = "", string badgeFill = "", string dashed = "") => new(BrushHandle + " " + BrushTip + " " + badge, BrushTip + " " + badgeFill, dashed);

    /// <summary>A pen nib, smaller and to the right where a badge or stroke goes beside it.</summary>
    private const string Nib = "M12,2.5 L18.5,11 L16,18 L8,18 L5.5,11 Z M12,2.5 L12,9.6 M13.5,11.1 A1.5,1.5 0 1 1 10.5,11.1 A1.5,1.5 0 1 1 13.5,11.1 M8,21.2 L16,21.2";
    private const string NibRight = "M15.5,2.5 L20.8,9.4 L18.8,15 L12.2,15 L10.2,9.4 Z M15.5,2.5 L15.5,8.3 M16.7,9.5 A1.2,1.2 0 1 1 14.3,9.5 A1.2,1.2 0 1 1 16.7,9.5 M12.2,17.6 L18.8,17.6";

    /// <summary>A slanted eraser, its rubber end solid.</summary>
    private const string Eraser = "M3.6,15.4 L12.6,6.4 Q14,5 15.4,6.4 L19.9,10.9 Q21.3,12.3 19.9,13.7 L13.1,20.5 L8.7,20.5 Z M7.8,11.2 L15.1,18.5 M13.1,20.5 L21,20.5";
    private const string EraserEnd = "M3.6,15.4 L7.8,11.2 L15.1,18.5 L13.1,20.5 L8.7,20.5 Z";

    /// <summary>eyedropper: the bulb up to the right, the glass tube down to its tip.</summary>
    private const string Dropper = "M14.8,4.8 L17.2,2.4 Q18.9,0.9 20.8,3.2 Q23.1,5.1 21.6,6.8 L19.2,9.2 M12.6,7 L17,11.4 M14,8.4 L4.6,17.8 L3.4,20.6 L6.2,19.4 L15.6,10";

    /// <summary>bandage, from bottom left to top right, with its pad.</summary>
    private const string Bandage = "M4.6,14.6 L14.6,4.6 Q17.6,1.6 20.4,3.6 Q22.4,6.4 19.4,9.4 L9.4,19.4 Q6.4,22.4 3.6,20.4 Q1.6,17.6 4.6,14.6 Z M9.5,12 L12,9.5 L14.5,12 L12,14.5 Z";

    /// <summary>The Aa of textformat, and the same letters stacked for vertical type.</summary>
    private const string Letters = "M2.5,19 L8,5 L13.5,19 M4.6,14 L11.4,14 M21,11.5 L21,19 M21,15.25 A3,3.75 0 1 1 15,15.25 A3,3.75 0 1 1 21,15.25";
    private const string Stacked = "M4.5,11 L8,2.5 L11.5,11 M5.8,8 L10.2,8 M12,14.5 L12,21 M12,17.75 A2.6,3.25 0 1 1 6.8,17.75 A2.6,3.25 0 1 1 12,17.75 M18,3.5 L18,20.5 M15.5,18 L18,20.5 L20.5,18";

    private const string Eye = "M2.5,12.5 Q12,2.5 21.5,12.5 Q12,22.5 2.5,12.5 Z M15,12.5 A3,3 0 1 1 9,12.5 A3,3 0 1 1 15,12.5";
    private const string Lasso = "M12,3.5 C17.2,3.5 21,6 21,9.2 C21,12.4 17.2,14.8 12,14.8 C6.8,14.8 3,12.4 3,9.2 C3,6 6.8,3.5 12,3.5 Z M7.4,14 C6.4,15.8 7.4,17.2 8.5,18 C9.7,19 9.3,20.6 7.5,21";
    private const string Arrow = "M6,3 L6,19 L10,15.2 L12.8,21 L15.2,19.9 L12.5,14.2 L18,14.2 Z";
    private const string Knife = "M2.5,21.5 L13.6,8.2 L15.8,10.4 Z M14.4,7.4 L18.9,2.9 Q20.3,1.5 21.6,2.8 Q22.9,4.1 21.5,5.5 L17,10";
    private const string Bucket = "M4,12 L11,5 L18.5,12.5 L11.5,19.5 Z M7.8,2.5 L11,5.7 M4,12 L18.5,12.5";
    private const string Stamp = "M17.2,5.2 A3.2,2.8 0 1 1 10.8,5.2 A3.2,2.8 0 1 1 17.2,5.2 Z M12.5,7.8 L15.5,7.8 L15.5,12 L12.5,12 Z M7.6,12 L20.4,12 Q21.5,12 21.5,13.1 L21.5,15.2 Q21.5,16.3 20.4,16.3 L7.6,16.3 Q6.5,16.3 6.5,15.2 L6.5,13.1 Q6.5,12 7.6,12 Z M5.5,18.2 L22.5,18.2 L22.5,20.6 L5.5,20.6 Z";

    /// <summary>A solid dot centered on (<paramref name="x"/>, <paramref name="y"/>).</summary>
    private static string Dot(double x, double y, double r) => FormattableString.Invariant($"M{x + r},{y} A{r},{r} 0 1 1 {x - r},{y} A{r},{r} 0 1 1 {x + r},{y} Z");
    private static string Dots(double r, params (double X, double Y)[] at) => string.Join(" ", at.Select(p => Dot(p.X, p.Y, r)));

    // ---- Badges, in the top-left corner ----
    private const string Plus = "M5,2.2 L5,8.2 M2,5.2 L8,5.2";
    private const string Minus = "M2,5.2 L8,5.2";
    private const string Sparkle = "M5,1.6 Q5.4,4.6 8.4,5 Q5.4,5.4 5,8.4 Q4.6,5.4 1.6,5 Q4.6,4.6 5,1.6 Z";
    private const string Back = "M2.4,6 A3.2,3.2 0 1 1 5.4,8.8 M2.4,3.2 L2.4,6 L5.2,6";

    internal static readonly IReadOnlyDictionary<string, Glyph> Glyphs = new Dictionary<string, Glyph>(StringComparer.Ordinal)
    {
        // ---- The tools upstream has, after the SF Symbols it names ----
        ["Move"] = new("M4,4 L10.5,10.5 M4,4 L4,9.5 M4,4 L9.5,4 M20,20 L13.5,13.5 M20,20 L20,14.5 M20,20 L14.5,20"),
        ["Marquee"] = new("", "", "M3.5,5.5 L20.5,5.5 L20.5,18.5 L3.5,18.5 Z"),
        ["Ellipse"] = new("", "", "M20.5,12 A8.5,8.5 0 1 1 3.5,12 A8.5,8.5 0 1 1 20.5,12"),
        ["Lasso"] = new(Lasso),
        ["Wand"] = new("M3.1,19.4 L13.1,9.4 L14.6,10.9 L4.6,20.9 Q3.85,21.65 3.1,20.9 Q2.35,20.15 3.1,19.4 Z M9.6,2.8 L9.6,6 M8,4.4 L11.2,4.4 M20,13 L20,16.2 M18.4,14.6 L21.6,14.6",
            "M17,2.5 Q17.5,5.9 21,6.5 Q17.5,7.1 17,10.5 Q16.5,7.1 13,6.5 Q16.5,5.9 17,2.5 Z"),
        ["Brush"] = Brush(), ["brush"] = Brush(),
        ["Heal"] = new(Bandage),
        ["Blur"] = new("M12,3 C12,3 5.5,10.2 5.5,14.5 C5.5,18.4 8.4,21 12,21 C15.6,21 18.5,18.4 18.5,14.5 C18.5,10.2 12,3 12,3 Z"),
        ["Shape"] = new("M3.5,3.5 L13.5,3.5 L13.5,13.5 L3.5,13.5 Z M13.5,9.7 A5.5,5.5 0 1 1 9.7,13.5"),
        ["Crop"] = new("M6.5,2.5 L6.5,17.5 L21.5,17.5 M2.5,6.5 L17.5,6.5 L17.5,21.5"),
        // hand.raised, turned so its thumb is on the left, as a left hand grabbing the canvas.
        ["Pan"] = new("M7,13 L7,6.6 Q7,5.2 8.4,5.2 Q9.8,5.2 9.8,6.6 L9.8,11 L9.8,4 Q9.8,2.6 11.2,2.6 Q12.6,2.6 12.6,4 L12.6,11 L12.6,4.8 Q12.6,3.4 14,3.4 Q15.4,3.4 15.4,4.8 L15.4,11.4 L15.4,7.2 Q15.4,5.8 16.8,5.8 Q18.2,5.8 18.2,7.2 L18.2,15 Q18.2,21.4 12.4,21.4 Q8.6,21.4 6.6,18.2 L3.9,13.7 Q3.2,12.4 4.3,11.8 Q5.5,11.2 6.4,12.3 Z"),
        ["Zoom"] = new("M16.5,10 A6.5,6.5 0 1 1 3.5,10 A6.5,6.5 0 1 1 16.5,10 M14.8,14.8 L20.5,20.5"),
        ["Type"] = new(Letters),
        ["Eyedropper"] = new(Dropper),

        // ---- Tools only Photoshop has, after its own pictures ----
        ["Path"] = new(Arrow),
        ["PathSelection"] = new(Arrow, Arrow),
        ["Pen"] = new(Nib),
        ["FreeformPen"] = new(NibRight + " M2.8,21 C4.6,13.6 7.6,23.6 9.6,16.8"),
        ["CurvaturePen"] = new(NibRight + " M2.8,21 Q5.8,12.4 9.6,21", Dot(6.2, 16.7, 1.1)),
        ["AddAnchor"] = new(NibRight + " M5,4 L5,10 M2,7 L8,7"),
        ["DeleteAnchor"] = new(NibRight + " M2,7 L8,7"),
        ["ConvertAnchor"] = new("M3.5,20.5 L12,4 L20.5,20.5 M10.5,2.5 L13.5,2.5 L13.5,5.5 L10.5,5.5 Z"),
        ["Eraser"] = new(Eraser, EraserEnd),
        ["BackgroundEraser"] = new(Eraser + " M2.4,2.4 L7.6,7.6 M7.6,2.4 L2.4,7.6", EraserEnd),
        ["MagicEraser"] = new(Eraser, EraserEnd + " " + Sparkle),
        ["Bucket"] = new(Bucket, "M4,12 L18.5,12.5 L11.5,19.5 Z M20.5,15 Q18.4,18.6 20.5,20.4 Q22.6,18.6 20.5,15 Z"),
        ["Sharpen"] = new("M12,3.5 L20.5,20 L3.5,20 Z"),
        ["Smudge"] = new("M7.2,21 L5.8,15 Q5.4,13.2 6.7,12 L13.4,4.8 Q14.7,3.5 15.9,4.6 Q17.1,5.8 15.8,7.1 L12.4,10.8 L18.3,6.2 Q19.7,5.2 20.7,6.3 Q21.6,7.5 20.4,8.7 L15.4,14.6 Q13.6,16.8 13.2,21"),
        ["Liquify"] = new("M3,8.5 C6,3.5 8,13.5 12,8.5 C16,3.5 18,13.5 21,8.5 M3,15.5 C6,10.5 8,20.5 12,15.5 C16,10.5 18,20.5 21,15.5"),
        ["Dodge"] = new("M20,9 A5.5,5.5 0 1 1 9,9 A5.5,5.5 0 1 1 20,9 M10.6,12.9 L3.5,20.5"),
        ["Burn"] = new("M4,21 L5.6,14.6 Q6.2,12.4 8.4,12.4 L11.4,12.4 L11.4,9.6 Q11.4,6.6 14.2,6.6 Q17,6.6 17,9.6 Q17,12.4 14.4,12.4 L13.4,12.4 M17,9.8 Q19.8,10.6 20,13.6 L20,21 M8.4,12.4 L8.4,8.6 Q8.4,7.4 9.6,7.4"),
        ["Sponge"] = new("M5,6 Q12,2.5 19,6 Q21,12 19,18 Q12,21.5 5,18 Q3,12 5,6 Z",
            Dots(1, (9, 9), (14.5, 8.4), (8, 14), (13, 13.4)) + " " + Dots(.8, (16.6, 16), (10.6, 17.4))),
        ["HistoryBrush"] = Brush(Back),
        ["ArtHistory"] = Brush("M2.4,4.6 C6.2,1.2 8.8,6.2 5.8,7.6 C3.4,8.8 2.8,5.6 5.4,5.2"),
        ["PatternStamp"] = new("M2.5,2.5 L8.5,2.5 L8.5,8.5 L2.5,8.5 Z", Stamp + " M2.5,2.5 L5.5,2.5 L5.5,5.5 L2.5,5.5 Z M5.5,5.5 L8.5,5.5 L8.5,8.5 L5.5,8.5 Z"),
        ["ColorReplacement"] = Brush("M2.2,2.2 L6.2,2.2 L6.2,6.2 L2.2,6.2 Z M4.4,8.4 L8.4,8.4 L8.4,4.4", "M2.2,2.2 L6.2,2.2 L6.2,6.2 L2.2,6.2 Z"),
        ["MixerBrush"] = Brush("", "M5,1.8 Q2.4,5.6 5,8 Q7.6,5.6 5,1.8 Z"),
        ["Pencil"] = new("M3.5,20.5 L4.8,15.6 L16.4,4 Q17.6,2.8 18.8,4 L20,5.2 Q21.2,6.4 20,7.6 L8.4,19.2 Z M14.4,6 L18,9.6 M4.8,15.6 L8.4,19.2", "M3.5,20.5 L4.2,17.9 L6.1,19.8 Z"),
        ["AdjustmentBrush"] = Brush("M8.4,5 A3.4,3.4 0 1 1 1.6,5 A3.4,3.4 0 1 1 8.4,5", "M5,1.6 A3.4,3.4 0 0 0 5,8.4 Z"),
        ["SelectionBrush"] = Brush("", "", "M2.5,2.5 L8.5,2.5 L8.5,8.5 L2.5,8.5 Z"),
        ["QuickSelection"] = Brush("", "", "M14,15.5 A5.5,5.5 0 1 1 3,15.5 A5.5,5.5 0 1 1 14,15.5"),
        ["Remove"] = Brush("", Sparkle),
        ["HealingBrush"] = new(Bandage + " " + Plus),
        ["Patch"] = new("M6,4 L18,4 Q20,4 20,6 L20,18 Q20,20 18,20 L6,20 Q4,20 4,18 L4,6 Q4,4 6,4 Z", "", "M7.6,7.6 L16.4,7.6 L16.4,16.4 L7.6,16.4 Z"),
        ["ContentMove"] = new("M3,8.5 L17.5,8.5 M14,5 L17.5,8.5 L14,12 M21,15.5 L6.5,15.5 M10,12 L6.5,15.5 L10,19"),
        ["RedEye"] = new(Eye + " " + Plus, Dot(12, 12.5, 1.6)),
        ["MagneticLasso"] = new("M11,3 C16,3 19.5,5 19.5,7.8 C19.5,10.6 16,12.6 11,12.6 C6,12.6 2.5,10.6 2.5,7.8 C2.5,5 6,3 11,3 Z M6.6,12 Q5.4,14.6 7.8,16 M11.5,14 L14.2,14 L14.2,18 Q14.2,19 15.25,19 Q16.3,19 16.3,18 L16.3,14 L19,14 L19,18 Q19,21.8 15.25,21.8 Q11.5,21.8 11.5,18 Z",
            "M11.5,14 L14.2,14 L14.2,15.8 L11.5,15.8 Z M16.3,14 L19,14 L19,15.8 L16.3,15.8 Z"),
        ["PerspectiveCrop"] = new("M6.5,2.5 L6.5,17.5 L21.5,17.5 M2.5,6.5 L17.5,6.5 L17.5,21.5 M9.5,9.5 L14.5,9.5 L15,14.8 L9,14.8 Z"),
        ["Slice"] = new(Knife),
        ["SliceSelect"] = new("M2.5,17 L10.8,7 L12.6,8.8 Z M11.6,6.2 L15,2.8 Q16.1,1.7 17.2,2.8 Q18.3,3.9 17.2,5 L13.8,8.4 M13.5,11.5 L13.5,21.5 L16,19 L17.6,22.2 L19.4,21.4 L17.8,18.2 L21,18.2 Z"),
        ["Frame"] = new("M3.5,4.5 L20.5,4.5 L20.5,19.5 L3.5,19.5 Z M3.5,4.5 L20.5,19.5 M20.5,4.5 L3.5,19.5"),
        ["ColorSampler"] = new(Dropper + " M5,2.2 L5,8.2 M2,5.2 L8,5.2"),
        ["Ruler"] = new("M2.5,15.5 L15.5,2.5 L21.5,8.5 L8.5,21.5 Z M6,12 L8.3,14.3 M9,9 L10.6,10.6 M12,6 L14.3,8.3 M15,13 L16.6,14.6 M12,16 L13.6,17.6"),
        ["Note"] = new("M4,3.5 L20,3.5 L20,15 L14.5,20.5 L4,20.5 Z M14.5,20.5 L14.5,15 L20,15 M7.5,8 L16.5,8 M7.5,11.5 L13,11.5"),
        ["Count"] = new("M3,9.5 L5.2,8 L5.2,16 M3.2,16 L7,16 M9.2,9.6 Q9.4,8 11.3,8 Q13.4,8 13.4,9.8 Q13.4,11.2 9.2,16 L13.8,16 M16,8 L20.6,8 L18.1,11.1 Q21,11.1 21,13.6 Q21,16.1 18.3,16.1 Q16.6,16.1 15.9,15.1"),
        ["TypeMask"] = new("", "", Letters),
        ["VerticalType"] = new(Stacked),
        ["VerticalTypeMask"] = new("M18,3.5 L18,20.5 M15.5,18 L18,20.5 L20.5,18", "", "M4.5,11 L8,2.5 L11.5,11 M5.8,8 L10.2,8 M12,14.5 L12,21 M12,17.75 A2.6,3.25 0 1 1 6.8,17.75 A2.6,3.25 0 1 1 12,17.75"),
        ["Artboard"] = new("M6.5,2.5 L6.5,21.5 M2.5,6.5 L21.5,6.5 M9.5,9.5 L19.5,9.5 L19.5,19.5 L9.5,19.5 Z"),
        ["SingleRow"] = new("", "", "M2.5,10.5 L21.5,10.5 L21.5,13.5 L2.5,13.5 Z"),
        ["SingleColumn"] = new("", "", "M10.5,2.5 L13.5,2.5 L13.5,21.5 L10.5,21.5 Z"),
        ["ShapeEllipse"] = new("M20.5,12 A8.5,8.5 0 1 1 3.5,12 A8.5,8.5 0 1 1 20.5,12"),
        ["Triangle"] = new("M12,3.5 L20.8,19.5 L3.2,19.5 Z"),
        ["PolygonShape"] = new("M7.5,3.8 L16.5,3.8 L21,12 L16.5,20.2 L7.5,20.2 L3,12 Z"),
        ["Star"] = new("M12,2.8 L14.8,8.7 L21.2,9.4 L16.4,13.8 L17.7,20.2 L12,17 L6.3,20.2 L7.6,13.8 L2.8,9.4 L9.2,8.7 Z"),
        ["Line"] = new("M4,20 L20,4"),
        ["CustomShape"] = new("M12,20 C5,15 2.5,11.5 2.5,8.5 C2.5,5.5 4.8,3.5 7.5,3.5 C9.6,3.5 11,4.8 12,6.4 C13,4.8 14.4,3.5 16.5,3.5 C19.2,3.5 21.5,5.5 21.5,8.5 C21.5,11.5 19,15 12,20 Z"),
        ["RotateView"] = new("M20.5,12 A8.5,8.5 0 1 1 16.3,4.6 M16.3,4.6 L16.6,1.4 M16.3,4.6 L19.5,5.1 M12,7.5 L16.5,12 L12,16.5 L7.5,12 Z"),

        // ---- Buttons and controls ----
        ["image"] = new("M3,4.5 L21,4.5 L21,19.5 L3,19.5 Z M3,17 L9,10.5 L13,14.5 L15.8,11.8 L21,17 M17.2,8.2 A1.4,1.4 0 1 1 14.4,8.2 A1.4,1.4 0 1 1 17.2,8.2"),
        ["document"] = new("M5,2.5 L14,2.5 L19,7.5 L19,21.5 L5,21.5 Z M14,2.5 L14,7.5 L19,7.5"),
        ["new-document"] = new("M5,2.5 L14,2.5 L19,7.5 L19,21.5 L5,21.5 Z M14,2.5 L14,7.5 L19,7.5 M8.5,14.5 L15.5,14.5 M12,11 L12,18"),
        ["add"] = new("M4.5,4.5 L19.5,4.5 L19.5,19.5 L4.5,19.5 Z M8.5,12 L15.5,12 M12,8.5 L12,15.5"),
        // Paint Symmetry's butterfly, as Photoshop has it.
        ["symmetry"] = new("M12,6.5 L12,19 M11,8.4 C7.6,3 2.6,3.6 3,7.8 C3.4,11 6.6,11.8 10.2,12 C6.4,12.8 4.6,16.4 7,18.6 C9,20.4 11,17.4 11.4,14.2 M13,8.4 C16.4,3 21.4,3.6 21,7.8 C20.6,11 17.4,11.8 13.8,12 C17.6,12.8 19.4,16.4 17,18.6 C15,20.4 13,17.4 12.6,14.2"),
        ["sampling-continuous"] = new(Dropper + " M2.5,2.5 Q6,2.5 6,5.5 Q6,8.5 9,8.5"),
        ["sampling-once"] = new(Dropper, Dot(5, 5, 2.4)),
        ["sampling-background"] = new(Dropper + " M2.2,2.2 L7.8,2.2 L7.8,7.8 L2.2,7.8 Z"),
        ["pressure"] = new("M20.5,12 A8.5,8.5 0 1 1 12,3.5 M16.5,12 A4.5,4.5 0 1 1 12,7.5 M11.2,12.8 L20.5,3.5", "M20.5,3.5 L21.5,6.8 L17.2,2.5 Z"),
        ["undo"] = new("M4,9.5 L9,4.5 M4,9.5 L9,14.5 M4,9.5 L14.5,9.5 Q20.5,9.5 20.5,15 Q20.5,20 14.5,20 L11,20"),
        ["swap"] = new("M4,8 L20,8 M16.5,4.5 L20,8 L16.5,11.5 M20,16 L4,16 M7.5,12.5 L4,16 L7.5,19.5"),
        ["chevron"] = new("M6.5,9.5 L12,15 L17.5,9.5"),
        ["reset"] = new("M5.2,8.5 A8,8 0 1 1 4,13 M5.2,3.5 L5.2,8.5 L10.2,8.5"),
        ["grid"] = new("M3.5,3.5 L20.5,3.5 L20.5,20.5 L3.5,20.5 Z M9.2,3.5 L9.2,20.5 M14.8,3.5 L14.8,20.5 M3.5,9.2 L20.5,9.2 M3.5,14.8 L20.5,14.8"),
        ["guides"] = new("M7.5,2.5 L7.5,21.5 M16.5,2.5 L16.5,21.5 M2.5,8 L21.5,8 M2.5,16 L21.5,16"),
        ["guides-lock"] = new("M7,2.5 L7,21.5 M2.5,7.5 L21.5,7.5 M11.5,14.5 L21,14.5 L21,21.5 L11.5,21.5 Z M13.8,14.5 L13.8,12.4 Q13.8,9.8 16.25,9.8 Q18.7,9.8 18.7,12.4 L18.7,14.5"),
        ["snap"] = new("M3.5,2.5 L3.5,20.5 L21.5,20.5 M5.5,16.5 L13,9 M9,9 L13,9 L13,13", "M17.5,3 L13.8,9.6 L17,9.6 L15.4,15 L20.4,7.6 L17.2,7.6 L18.6,3 Z"),
        ["info"] = new("M21,12 A9,9 0 1 1 3,12 A9,9 0 1 1 21,12 M12,10.5 L12,17", Dot(12, 7.4, 1.1)),
        ["properties"] = new("M3.5,6.5 L7,6.5 M11,6.5 L20.5,6.5 M3.5,12 L14,12 M18,12 L20.5,12 M3.5,17.5 L9,17.5 M13,17.5 L20.5,17.5 M7,4.5 L11,4.5 L11,8.5 L7,8.5 Z M14,10 L18,10 L18,14 L14,14 Z M9,15.5 L13,15.5 L13,19.5 L9,19.5 Z"),
        ["up"] = new("M5.5,11 L12,4.5 L18.5,11 M12,4.5 L12,20.5"),
        ["down"] = new("M5.5,13 L12,19.5 L18.5,13 M12,19.5 L12,3.5"),
        ["portrait"] = new("M6,2.5 L18,2.5 L18,21.5 L6,21.5 Z"),
        ["landscape"] = new("M2.5,6 L21.5,6 L21.5,18 L2.5,18 Z"),
        ["new-artboard"] = new("M6.5,2.5 L6.5,21.5 M2.5,6.5 L21.5,6.5 M9.5,9.5 L19.5,9.5 L19.5,19.5 L9.5,19.5 Z M11.8,14.5 L17.2,14.5 M14.5,11.8 L14.5,17.2"),
        ["settings"] = new("M10.3,2.5 L13.7,2.5 L14.4,5.4 L16.9,6.8 L19.7,5.9 L21.4,8.9 L19.2,10.9 L19.2,13.1 L21.4,15.1 L19.7,18.1 L16.9,17.2 L14.4,18.6 L13.7,21.5 L10.3,21.5 L9.6,18.6 L7.1,17.2 L4.3,18.1 L2.6,15.1 L4.8,13.1 L4.8,10.9 L2.6,8.9 L4.3,5.9 L7.1,6.8 L9.6,5.4 Z M15,12 A3,3 0 1 1 9,12 A3,3 0 1 1 15,12"),
        ["home"] = new("M3,11.5 L12,3.5 L21,11.5 M5.5,9.5 L5.5,20.5 L10,20.5 L10,15 L14,15 L14,20.5 L18.5,20.5 L18.5,9.5"),
        ["workspace"] = new("M3,4 L21,4 L21,20 L3,20 Z M3,8 L21,8 M15.5,8 L15.5,20 M5.2,6 L5.3,6 M7.4,6 L7.5,6"),
        ["link"] = new("M10.2,13.8 Q8.8,12.4 10.2,11 L13.6,7.6 Q16,5.2 18.4,7.6 Q20.8,10 18.4,12.4 L16.8,14 M13.8,10.2 Q15.2,11.6 13.8,13 L10.4,16.4 Q8,18.8 5.6,16.4 Q3.2,14 5.6,11.6 L7.2,10"),
        ["warp"] = new("M4,4 Q12,7.5 20,4 L20,20 Q12,16.5 4,20 Z M4,12 Q12,9 20,12 M12,5.8 L12,18.2"),
        ["cancel"] = new("M21,12 A9,9 0 1 1 3,12 A9,9 0 1 1 21,12 M5.6,5.6 L18.4,18.4"),
        ["check"] = new("M4,12.5 L9.5,18 L20,6.5"),
        ["close"] = new("M6,6 L18,18 M6,18 L18,6"),
        ["plus"] = new("M5,12 L19,12 M12,5 L12,19"),
        ["minus"] = new("M5,12 L19,12"),
        ["lock"] = new("M8,10.5 L8,7.5 Q8,3.5 12,3.5 Q16,3.5 16,7.5 L16,10.5 M5.5,10.5 L18.5,10.5 L18.5,20.5 L5.5,20.5 Z", Dot(12, 15.5, 1.3)),
        ["unlock"] = new("M8,10.5 L8,7.5 Q8,3.5 12,3.5 Q15.4,3.5 15.9,6.5 M5.5,10.5 L18.5,10.5 L18.5,20.5 L5.5,20.5 Z", Dot(12, 15.5, 1.3)),
        ["folder"] = new("M2.5,6 Q2.5,4.5 4,4.5 L9,4.5 L11,7 L20,7 Q21.5,7 21.5,8.5 L21.5,18.5 Q21.5,20 20,20 L4,20 Q2.5,20 2.5,18.5 Z M2.5,10 L21.5,10"),
        ["delete"] = new("M9.5,3 L14.5,3 M3.5,6 L20.5,6 M6,6 L7,21 L17,21 L18,6 M10,10 L10,17 M14,10 L14,17"),
        ["mask"] = new("M3,4.5 L21,4.5 L21,19.5 L3,19.5 Z M16,12 A4,4 0 1 1 8,12 A4,4 0 1 1 16,12", "M3,4.5 L21,4.5 L21,19.5 L3,19.5 Z M16,12 A4,4 0 1 0 8,12 A4,4 0 1 0 16,12 Z"),
        ["eye"] = new(Eye),
        ["eye-closed"] = new(Eye + " M3.5,3.5 L20.5,20.5"),
        ["history"] = new("M4.6,8 A8.5,8.5 0 1 1 3.5,12.8 M4.6,3.6 L4.6,8 L9,8 M12,7 L12,12 L15.5,14.5"),
        ["snapshot"] = new("M2.5,7.5 L7,7.5 L8.8,4.5 L15.2,4.5 L17,7.5 L21.5,7.5 L21.5,19.5 L2.5,19.5 Z M16,13.3 A4,4 0 1 1 8,13.3 A4,4 0 1 1 16,13.3"),
        ["quick-mask"] = new("M3,4.5 L21,4.5 L21,19.5 L3,19.5 Z", "", "M16.2,12 A4.2,4.2 0 1 1 7.8,12 A4.2,4.2 0 1 1 16.2,12"),
        ["screen"] = new("M2.5,4 L21.5,4 L21.5,16.5 L2.5,16.5 Z M8,20.5 L16,20.5 M12,16.5 L12,20.5"),
        ["generate"] = new("M2.5,6.5 L14,6.5 M21.5,11 L21.5,20 L2.5,20 L2.5,6.5 M3,17.5 L8.5,12 L13,16.5 L15.5,14 L21,19",
            "M18,1.8 Q18.5,5 21.8,5.5 Q18.5,6 18,9.2 Q17.5,6 14.2,5.5 Q17.5,5 18,1.8 Z"),
        ["plugin"] = new("M3.5,8 L8.5,8 L8.5,6 Q8.5,3 11.25,3 Q14,3 14,6 L14,8 L19,8 L19,12.5 L20.5,12.5 Q23,12.5 23,15.25 Q23,18 20.5,18 L19,18 L19,21.5 L3.5,21.5 Z"),
        ["flip-x"] = new("M12,2.5 L12,21.5 M9,6.5 L3.5,12 L9,17.5 Z M15,6.5 L20.5,12 L15,17.5 Z"),
        ["flip-y"] = new("M2.5,12 L21.5,12 M6.5,9 L12,3.5 L17.5,9 Z M6.5,15 L12,20.5 L17.5,15 Z"),
        ["rotation"] = new("M3.5,19 L20.5,19 M3.5,19 L15,6.5 M10.5,19 Q10.5,14.5 7.4,14.7"),
        ["bullets"] = new("M9,6 L21,6 M9,12 L21,12 M9,18 L21,18", Dots(1.5, (4.5, 6), (4.5, 12), (4.5, 18))),
        ["numbered"] = new("M3,4.6 L4.8,3.5 L4.8,9 M3,13.4 Q3.4,12 4.8,12 Q6.4,12 6.4,13.4 Q6.4,14.6 3,18 L6.6,18 M10,5.5 L21,5.5 M10,12 L21,12 M10,18.5 L21,18.5"),
        ["colors"] = new("M3,3 L14,3 L14,14 L3,14 Z M10,17.5 L10,21 L21,21 L21,10 L17.5,10", "M3,3 L14,3 L14,14 L3,14 Z"),
        ["selection"] = new("", "", "M20.5,12 A8.5,8.5 0 1 1 3.5,12 A8.5,8.5 0 1 1 20.5,12"),
        ["move"] = new("M12,2.5 L12,21.5 M2.5,12 L21.5,12 M9,5.5 L12,2.5 L15,5.5 M9,18.5 L12,21.5 L15,18.5 M5.5,9 L2.5,12 L5.5,15 M18.5,9 L21.5,12 L18.5,15"),
        ["transparency"] = new("M3.5,3.5 L20.5,3.5 L20.5,20.5 L3.5,20.5 Z", "M3.5,3.5 L9.2,3.5 L9.2,9.2 L3.5,9.2 Z M14.8,3.5 L20.5,3.5 L20.5,9.2 L14.8,9.2 Z M9.2,9.2 L14.8,9.2 L14.8,14.8 L9.2,14.8 Z M3.5,14.8 L9.2,14.8 L9.2,20.5 L3.5,20.5 Z M14.8,14.8 L20.5,14.8 L20.5,20.5 L14.8,20.5 Z"),
        ["adjust"] = new("M20.5,12 A8.5,8.5 0 1 1 3.5,12 A8.5,8.5 0 1 1 20.5,12", "M12,3.5 A8.5,8.5 0 0 0 12,20.5 Z"),
        ["fx"] = new("M10.5,4 Q7.5,3.4 7.2,6.6 L5.8,20 M3.5,10 L10,10 M12.5,10 L19.5,19.5 M19.5,10 L12.5,19.5"),
        // Arrange: the square being moved solid, over or under the other; Forward and Backward add one step's arrow.
        ["slice-front"] = new("M9,9 L4,9 L4,20 L13,20 L13,15", "M9,4 L20,4 L20,15 L9,15 Z"),
        ["slice-forward"] = new("M7.5,7.5 L3.5,7.5 L3.5,17 L11,17 L11,13.5 M18.5,22 L18.5,16.5 M16,19 L18.5,16.5 L21,19", "M7.5,3 L16.5,3 L16.5,13.5 L7.5,13.5 Z"),
        ["slice-backward"] = new("M7.5,3 L16.5,3 L16.5,13.5 L7.5,13.5 Z M18.5,16.5 L18.5,22 M16,19.5 L18.5,22 L21,19.5", "M3.5,7.5 L7.5,7.5 L7.5,13.5 L11,13.5 L11,17 L3.5,17 Z"),
        ["slice-back"] = new("M9,4 L20,4 L20,15 L9,15 Z", "M4,9 L9,9 L9,15 L13,15 L13,20 L4,20 Z"),
        // ---- Panel marks, after Photoshop's panel meanings ----
        ["panel:Properties"] = new("M3.5,6.5 L7,6.5 M11,6.5 L20.5,6.5 M3.5,12 L14,12 M18,12 L20.5,12 M3.5,17.5 L9,17.5 M13,17.5 L20.5,17.5 M7,4.5 L11,4.5 L11,8.5 L7,8.5 Z M14,10 L18,10 L18,14 L14,14 Z M9,15.5 L13,15.5 L13,19.5 L9,19.5 Z"),
        ["panel:History"] = new("M4.6,8 A8.5,8.5 0 1 1 3.5,12.8 M4.6,3.6 L4.6,8 L9,8 M12,7 L12,12 L15.5,14.5"),
        ["panel:Layers"] = new("M3,7.5 L12,3 L21,7.5 L12,12 Z M3,12 L12,16.5 L21,12 M3,16.5 L12,21 L21,16.5"),
        ["panel:Channels"] = new("M3.5,13.5 L3.5,3.5 L13.5,3.5 L13.5,6 M6.5,16.5 L6.5,6.5 L16.5,6.5 L16.5,9 M9.5,9.5 L20.5,9.5 L20.5,20.5 L9.5,20.5 Z"),
        ["panel:Materials"] = new("M3.5,3.5 L20.5,3.5 L20.5,15 L15,20.5 L3.5,20.5 Z M15,20.5 L15,15 L20.5,15 M6.5,10 L10,6.5 M6.5,16 L16,6.5 M10.5,17.5 L17.5,10.5"),
        ["panel:Measurement Log"] = new("M8.5,5 L4.5,5 L4.5,21 L19.5,21 L19.5,5 L15.5,5 M8.5,3 L15.5,3 L15.5,7 L8.5,7 Z M7.5,11 L9,11 M11.5,11 L16.5,11 M7.5,14.5 L9,14.5 M11.5,14.5 L16.5,14.5 M7.5,18 L16.5,18"),
        ["panel:Navigator"] = new("M3,4.5 L21,4.5 L21,19.5 L3,19.5 Z M6,7.5 L13.5,7.5 L13.5,13.5 L6,13.5 Z"),
        ["panel:Adjustments"] = new("M20.5,12 A8.5,8.5 0 1 1 3.5,12 A8.5,8.5 0 1 1 20.5,12", "M12,3.5 A8.5,8.5 0 0 0 12,20.5 Z"),
        ["panel:Actions"] = new("M3.5,9 L8,9 M3.5,15 L8,15 M11.5,5 L20.5,12 L11.5,19 Z"),
        ["panel:Paragraph"] = new("M14,4 L14,21 M18.5,4 L18.5,21 M9,4 L21,4", "M12.5,4 L12.5,13 L9.5,13 C3,13 3,4 9.5,4 Z"),
        ["panel:Paragraph Styles"] = new("M3.5,3.5 L20.5,3.5 L20.5,20.5 L3.5,20.5 Z M12.5,7 L17,7 M13.5,7 L13.5,17 M16.5,7 L16.5,17", "M12,7 L12,12 L9.8,12 C6,12 6,7 9.8,7 Z"),
        ["panel:Clone Source"] = new("M13,5.1 A3,2.6 0 1 1 7,5.1 A3,2.6 0 1 1 13,5.1 Z M8.6,7.7 L11.4,7.7 L11.4,11.2 L8.6,11.2 Z M3.5,11.2 L16.5,11.2 L16.5,15 L3.5,15 Z M2.5,17.5 L17.5,17.5 M19.5,14.5 L19.5,21 M16.25,17.75 L22.75,17.75"),
        ["panel:Tool Presets"] = new("M12.4,9.4 L18.9,2.9 Q20,1.8 21.1,2.9 Q22.2,4 21.1,5.1 L14.6,11.6 Z M12.4,9.4 L14.6,11.6 Q14.4,14.4 11.4,15.4 L4.5,16.5 L7,10.6 Q8.6,9.2 12.4,9.4 Z M3.5,19 L10.5,19 L10.5,21.5 L3.5,21.5 Z"),
        ["panel:Brushes"] = new("M4,16 L13,3.5 L15.8,5.4 L7,18 M7,18 Q7,21.8 2.5,21 Q4.4,19.2 4,16 M12,17 L19.8,6.5 L21.8,8.2 L15,19 M15,19 Q15,21.8 10.5,21 Q12.3,19.8 12,17"),
        ["panel:Brush Settings"] = new("M4,10.5 L11,3.5 L14,6.5 L7,13.5 Q3,15.5 3,12.5 Z M3,18.5 L10,18.5 M14,18.5 L21,18.5 M10,16.5 L14,16.5 L14,20.5 L10,20.5 Z M17.5,5 L21.5,5 M19.5,3 L19.5,7"),
        ["panel:Gradients"] = new("M3,5 L21,5 L21,19 L3,19 Z", "M8,6.5 L9.5,6.5 L9.5,17.5 L8,17.5 Z M11.5,6.5 L13.5,6.5 L13.5,17.5 L11.5,17.5 Z M15.5,6.5 L19.5,6.5 L19.5,17.5 L15.5,17.5 Z"),
        ["panel:Libraries"] = new("M2.5,21 L21.5,21 M4,4 L8,4 L8,18 L4,18 Z M10,3 L14,3 L14,18 L10,18 Z M16,6 L20,5 L22.4,17.6 L18.4,18.6 Z"),
        ["panel:Paths"] = new("M5,17 C5,5 19,19 19,7 M3.5,15.5 L6.5,15.5 L6.5,18.5 L3.5,18.5 Z M17.5,5.5 L20.5,5.5 L20.5,8.5 L17.5,8.5 Z M5,17 L3,9 M19,7 L21,15"),
        ["panel:Swatches"] = new("M3.5,3.5 L10,3.5 L10,10 L3.5,10 Z M14,3.5 L20.5,3.5 L20.5,10 L14,10 Z M3.5,14 L10,14 L10,20.5 L3.5,20.5 Z M14,14 L20.5,14 L20.5,20.5 L14,20.5 Z", "M3.5,3.5 L10,3.5 L10,10 L3.5,10 Z M14,14 L20.5,14 L20.5,20.5 L14,20.5 Z"),
        ["panel:Timeline"] = new("M2.5,8 L21.5,8 M2.5,13 L8,13 M14.5,13 L21.5,13 M2.5,18.5 L21.5,18.5 M7,5.5 L7,21 M11.25,10 L14,13 L11.25,16 L8.5,13 Z", "M4.8,2.5 L9.2,2.5 L7,5.5 Z"),
        ["panel:Patterns"] = new("M7,2.5 L11.5,7 L7,11.5 L2.5,7 Z M17,2.5 L21.5,7 L17,11.5 L12.5,7 Z M7,12.5 L11.5,17 L7,21.5 L2.5,17 Z M17,12.5 L21.5,17 L17,21.5 L12.5,17 Z"),
        ["panel:Layer Comps"] = new("M3.5,16 L3.5,3.5 L16,3.5 M7.5,7.5 L20.5,7.5 L20.5,20.5 L7.5,20.5 Z M10.5,14 L13,16.5 L17.5,11.5"),
        ["panel:Info"] = new("M21,12 A9,9 0 1 1 3,12 A9,9 0 1 1 21,12 M12,10.5 L12,17", Dot(12, 7.4, 1.1)),
        ["panel:Shapes"] = new("M3.5,3.5 L13.5,3.5 L13.5,13.5 L3.5,13.5 Z M13.5,9.7 A5.5,5.5 0 1 1 9.7,13.5"),
        ["panel:Color"] = new("M12,3 C6.5,3 2.5,7 2.5,12 C2.5,17 6.5,21 11.5,21 Q13.6,21 13.6,19.2 Q13.6,18 12.8,17.2 Q12,16.2 12.8,15.4 Q13.4,14.8 14.6,14.8 L17,14.8 Q21.5,14.8 21.5,10.8 C21.5,6.4 17.2,3 12,3 Z",
            Dots(1.2, (7.6, 9), (12, 6.6), (16.4, 8.6), (6.4, 13.4))),
        ["panel:Styles"] = new("M12,5 L3.5,5 L3.5,20.5 L19,20.5 L19,13", "M17.5,1.8 Q18,6 22.2,6.6 Q18,7.2 17.5,11.4 Q17,7.2 12.8,6.6 Q17,6 17.5,1.8 Z"),
        ["panel:Histogram"] = new("M2.5,20.5 L21.5,20.5", "M3.5,15.5 L6,15.5 L6,19 L3.5,19 Z M7.5,10 L10,10 L10,19 L7.5,19 Z M11.5,4 L14,4 L14,19 L11.5,19 Z M15.5,8 L18,8 L18,19 L15.5,19 Z M19.5,13 L21.5,13 L21.5,19 L19.5,19 Z"),
        ["panel:Notes"] = new("M4,3.5 L20,3.5 L20,15 L14.5,20.5 L4,20.5 Z M14.5,20.5 L14.5,15 L20,15 M7.5,8 L16.5,8 M7.5,11.5 L13,11.5"),
        ["panel:Character"] = new("M3,4.5 L13,4.5 M8,4.5 L8,20 M5.5,20 L10.5,20 M3,4.5 L3,7 M13,4.5 L13,7 M17.5,9 L17.5,18 Q17.5,20.5 21,19.4 M14.5,12 L21,12"),
        ["panel:Character Styles"] = new("M3.5,3.5 L20.5,3.5 L20.5,20.5 L3.5,20.5 Z M7.5,8 L16.5,8 M12,8 L12,17 M9.5,17 L14.5,17"),
        ["panel:Glyphs"] = new("M3,10.5 L6,3.5 L9,10.5 M4.2,8 L7.8,8 M17,3.5 L17,9.5 M14,6.5 L20,6.5 M4,15 Q4,12.6 6.8,13.4 Q9.4,14.6 4,20.5 L9.5,20.5 M14.5,20.5 L16.5,20.5 Q12,15 17,13.4 Q22,15 17.5,20.5 L20.5,20.5"),
        ["panel:Plugins"] = new("M3.5,8 L8.5,8 L8.5,6 Q8.5,3 11.25,3 Q14,3 14,6 L14,8 L19,8 L19,12.5 L20.5,12.5 Q23,12.5 23,15.25 Q23,18 20.5,18 L19,18 L19,21.5 L3.5,21.5 Z"),
        ["panel:Tools"] = new("M4.5,3 L3,7 L9,13 M7.5,3 L11,6.5 M14,13 L20,19 L18,21 L12,15 M5,19 L16.5,3.5 L20.5,6.5 L9,21 Q4.5,22.6 3.5,20.6 Q3.5,19.6 5,19 Z"),
        ["panel:Options"] = new("M3,7 L6,7 M12,7 L21,7 M3,17 L12,17 M18,17 L21,17 M12,7 A3,3 0 1 1 6,7 A3,3 0 1 1 12,7 M18,17 A3,3 0 1 1 12,17 A3,3 0 1 1 18,17"),
        ["panel:Contextual Task Bar"] = new("M3,4.5 L21,4.5 Q22,4.5 22,5.5 L22,12 Q22,13 21,13 L3,13 Q2,13 2,12 L2,5.5 Q2,4.5 3,4.5 Z M12,15.5 L12,22 L13.8,20.2 L15,22.4 L16.4,21.8 L15.2,19.6 L17.6,19.6 Z", Dots(1, (6.7, 8.75), (12, 8.75), (17.3, 8.75))),
    };
}
