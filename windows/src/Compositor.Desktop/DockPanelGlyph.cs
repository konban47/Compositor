using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Compositor.Desktop;

/// <summary>Small-size vector adaptations of the individually generated panel designs in windows/design/panel-icons.</summary>
internal sealed class DockPanelGlyph(string name) : Control
{
    internal sealed record Symbol(string Outline, string Fill = "");
    internal static IReadOnlyDictionary<string, Symbol> Symbols { get; } = new Dictionary<string, Symbol>(StringComparer.Ordinal)
    {
        ["Properties"] = new("M3,6 L6,6 M9,6 L21,6 M6,3.5 L9,3.5 L9,8.5 L6,8.5 Z M3,12 L15,12 M18,12 L21,12 M15,9.5 L18,9.5 L18,14.5 L15,14.5 Z M3,18 L10,18 M13,18 L21,18 M10,15.5 L13,15.5 L13,20.5 L10,20.5 Z"),
        ["History"] = new("M4,7 A9,9 0 1 1 3,13 M4,3 L4,7 L8,7 M12,6 L12,12 L17,15"),
        ["Layers"] = new("M3,7 L12,2.5 L21,7 L12,11.5 Z M3,12 L12,16.5 L21,12 M3,17 L12,21.5 L21,17"),
        ["Channels"] = new("M3,14 L3,3 L14,3 L14,6 M6,17 L6,6 L17,6 L17,9 M9,9 L21,9 L21,21 L9,21 Z M3,14 L6,14 M6,17 L9,17"),
        ["Materials"] = new("M3,3 L21,3 L21,15 L15,21 L3,21 Z M15,21 L15,15 L21,15 M6,10 L10,6 M6,16 L16,6 M10,18 L18,10"),
        ["Measurement Log"] = new("M8,5 L4,5 L4,21 L20,21 L20,5 L16,5 M8,3 L16,3 L16,7 L8,7 Z M7,10 L9,10 M12,10 L17,10 M7,13 L9,13 M12,13 L17,13 M7,18 L17,18 M7,18 L7,16 M12,18 L12,16 M17,18 L17,16"),
        ["Navigator"] = new("M3,4 L21,4 L21,20 L3,20 Z M5.5,6.5 L13.5,6.5 L13.5,13.5 L5.5,13.5 Z M9.5,8.5 L9.5,11.5 M8,10 L11,10"),
        ["Adjustments"] = new("M21,12 A9,9 0 1 1 3,12 A9,9 0 1 1 21,12 M15,9 L18,9 M15,15 L18,15", "M12,3 A9,9 0 0 0 12,21 Z"),
        ["Actions"] = new("M3,9 L8,9 M3,15 L8,15 M12,5 L21,12 L12,19 Z"),
        ["Paragraph"] = new("M14,4 L14,21 M19,4 L19,21 M9,4 L21,4", "M12,4 L12,13 L9,13 C2,13 2,4 9,4 Z"),
        ["Paragraph Styles"] = new("M3,3 L21,3 L21,21 L3,21 Z M12,7 L17,7 M13,7 L13,17 M17,7 L17,17", "M11,7 L11,12 L9,12 C5,12 5,7 9,7 Z"),
        ["Clone Source"] = new("M4,17 L4,14 L7,12 L7,9 C3,2 16,2 12,9 L12,12 L15,14 L15,17 Z M3,20 L12,20 M18,15 L18,21 M15,18 L21,18"),
        ["Tool Presets"] = new("M13,4 C8,1 5,4 6,8 L3,17 Q2,20 5,20 L14,11 C19,11 21,7 19,4 L16,7 L13,7 Z M17,14 L21,14 L21,21 L19,19 L17,21 Z"),
        ["Brushes"] = new("M4,16 L13,3 L16,5 L7,18 M7,18 Q7,22 2,21 Q4,19 4,16 M12,17 L20,6 L22,8 L15,19 M15,19 Q15,22 10,21 Q12,20 12,17"),
        ["Brush Settings"] = new("M4,10 L11,3 L14,6 L7,13 Q3,15 3,12 Z M3,17 L10,17 M14,17 L21,17 M10,15 L14,15 L14,19 L10,19 Z M17,5 L21,5 M19,3 L19,7"),
        ["Gradients"] = new("M3,5 L21,5 L21,19 L3,19 Z M6,8 L6,16", "M9,7 L10,7 L10,17 L9,17 Z M12,7 L14,7 L14,17 L12,17 Z M16,7 L19,7 L19,17 L16,17 Z"),
        ["Libraries"] = new("M3,21 L22,21 M4,4 L8,4 L8,18 L4,18 Z M10,3 L14,3 L14,18 L10,18 Z M16,6 L20,5 L23,18 L19,19 Z"),
        ["Paths"] = new("M5,17 C5,5 19,19 19,7 M5,17 L3,8 M19,7 L21,16 M3,15 L7,15 L7,19 L3,19 Z M17,5 L21,5 L21,9 L17,9 Z M4.2,7 A1.2,1.2 0 1 1 1.8,7 A1.2,1.2 0 1 1 4.2,7 M22.2,17 A1.2,1.2 0 1 1 19.8,17 A1.2,1.2 0 1 1 22.2,17"),
        ["Swatches"] = new("", "M3,3 L10,3 L10,10 L3,10 Z M14,3 L21,3 L21,10 L14,10 Z M3,14 L10,14 L10,21 L3,21 Z M14,14 L21,14 L21,21 L14,21 Z"),
        ["Timeline"] = new("M3,7 L21,7 M3,12 L9,12 M15,12 L21,12 M3,18 L21,18 M7,3 L7,21 M12,9 L15,12 L12,15 L9,12 Z", "M5,2 L9,2 L7,5 Z"),
        ["Patterns"] = new("M7,2 L12,7 L7,12 L2,7 Z M17,2 L22,7 L17,12 L12,7 Z M7,12 L12,17 L7,22 L2,17 Z M17,12 L22,17 L17,22 L12,17 Z"),
        ["Layer Comps"] = new("M3,16 L3,3 L16,3 M7,7 L21,7 L21,21 L7,21 Z M10,14 L13,17 L18,11"),
        ["Info"] = new("M21,12 A9,9 0 1 1 3,12 A9,9 0 1 1 21,12 M10,11 L12,11 L12,17 M10,17 L14,17", "M13,7 A1,1 0 1 1 11,7 A1,1 0 1 1 13,7"),
        ["Shapes"] = new("M3,3 L16,3 L16,8 M3,3 L3,16 L8,16 M21,15 A6,6 0 1 1 9,15 A6,6 0 1 1 21,15"),
        ["Color"] = new("M21,12 C21,2 10,0 4,6 C-1,12 3,21 10,21 L13,21 Q16,21 14,18 Q12,15 17,15 Q21,15 21,12 M8,7 A1,1 0 1 1 6,7 A1,1 0 1 1 8,7 M13,5 A1,1 0 1 1 11,5 A1,1 0 1 1 13,5 M18,8 A1,1 0 1 1 16,8 A1,1 0 1 1 18,8 M7,13 A1,1 0 1 1 5,13 A1,1 0 1 1 7,13"),
        ["Styles"] = new("M12,5 L3,5 L3,21 L19,21 L19,14 M18,2 Q18,7 23,8 Q18,9 18,14 Q17,9 12,8 Q17,7 18,2 Z"),
        ["Histogram"] = new("M2,21 L22,21", "M3,16 L6,16 L6,20 L3,20 Z M7,10 L10,10 L10,20 L7,20 Z M11,3 L14,3 L14,20 L11,20 Z M15,7 L18,7 L18,20 L15,20 Z M19,13 L22,13 L22,20 L19,20 Z"),
        ["Notes"] = new("M3,3 L21,3 L21,15 L15,21 L3,21 Z M15,21 L15,15 L21,15 M7,8 L17,8 M7,12 L13,12"),
        ["Character"] = new("M3,4 L13,4 M8,4 L8,20 M5,20 L11,20 M3,4 L3,7 M13,4 L13,7 M17,9 L17,18 Q17,21 21,19 M14,12 L21,12"),
        ["Character Styles"] = new("M3,3 L21,3 L21,21 L3,21 Z M7,8 L17,8 M12,8 L12,17 M9,17 L15,17"),
        ["Glyphs"] = new("M3,10 L6,3 L9,10 M4,8 L8,8 M17,3 L17,9 M14,6 L20,6 M4,15 Q4,12 8,14 Q10,16 4,20 L10,20 M14,20 L16,20 Q11,14 17,13 Q23,14 18,20 L21,20"),
        ["Plugins"] = new("M3,8 L8,8 L8,5 C5,0 18,0 15,5 L15,8 L21,8 L21,13 C15,10 15,22 21,19 L21,22 L3,22 Z"),
        ["Tools"] = new("M4,3 L3,7 L9,13 M7,3 L11,7 M14,13 L20,19 L18,21 L12,15 M5,19 L17,3 L21,6 L9,21 Q4,23 3,21 Z"),
        ["Options"] = new("M3,7 L6,7 M12,7 L21,7 M3,17 L12,17 M18,17 L21,17 M12,7 A3,3 0 1 1 6,7 A3,3 0 1 1 12,7 M18,17 A3,3 0 1 1 12,17 A3,3 0 1 1 18,17"),
        ["Contextual Task Bar"] = new("M3,4 L21,4 L21,13 L3,13 Z M6,8 L7,8 M11,8 L12,8 M16,8 L17,8 M11,16 L17,20 L14,20 L13,23 Z")
    };
    internal static bool HasSymbol(string name) => Symbols.ContainsKey(name);
    protected override Size MeasureOverride(Size availableSize) => new(22, 22);
    public override void Render(DrawingContext context)
    {
        if (!Symbols.TryGetValue(name, out var symbol)) return;
        var size = Math.Min(Bounds.Width, Bounds.Height); var scale = size / 24;
        using var fit = context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation((Bounds.Width - size) / 2, (Bounds.Height - size) / 2));
        if (symbol.Outline.Length > 0) context.DrawGeometry(null, new Pen(Skin.LabelBrush, 1.65) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, StreamGeometry.Parse(symbol.Outline));
        if (symbol.Fill.Length > 0) context.DrawGeometry(Skin.LabelBrush, null, StreamGeometry.Parse(symbol.Fill));
    }
}
