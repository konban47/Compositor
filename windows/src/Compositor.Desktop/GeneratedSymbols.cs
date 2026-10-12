using Avalonia;
using Avalonia.Media;

namespace Compositor.Desktop;

/// <summary>Scalable adaptations of the built-in imagegen sheet in windows/design, not Apple font assets.</summary>
internal static class GeneratedSymbols
{
    internal static bool Draw(DrawingContext context, string kind, Size size)
    {
        if (!Paths.TryGetValue(kind, out var path)) return false;
        var side = Math.Min(size.Width, size.Height);
        using var fit = context.PushTransform(Matrix.CreateScale(side / 28, side / 28)
            * Matrix.CreateTranslation((size.Width - side) / 2 + side / 14, (size.Height - side) / 2 + side / 14));
        var solid = kind is "Wand" or "Brush" or "brush" or "Pan" or "Blur" or "Type" or "Shape" or "Heal" or "Eyedropper" or "plugin";
        context.DrawGeometry(solid ? Skin.LabelBrush : null, solid ? null : new Pen(Skin.LabelBrush, 1.8)
            { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, StreamGeometry.Parse(path));
        return true;
    }
    private const string Brush = "M11,13 L19.5,2.5 Q22,0 23,2.5 Q23,4 13,15 Z M10,14 L12,16 L10.7,17.3 L8.8,15.3 Z M8,16 C11,16 12,21 7,22 Q3,24 1,21 C5,22 3,16 8,16 Z";
    private static readonly Dictionary<string, string> Paths = new(StringComparer.Ordinal)
    {
        ["Wand"] = "M2,20 L15,7 Q17,5 18.5,7 Q20,8.5 18,10 L5,23 Q2,25 1,22 Z M6,0 Q7,5 11,6 Q7,7 6,11 Q5,7 1,6 Q5,5 6,0 Z M20,1 L21,4 L24,5 L21,6 L20,9 L19,6 L16,5 L19,4 Z M18,14 L19,17 L22,18 L19,19 L18,22 L17,19 L14,18 L17,17 Z",
        ["Brush"] = Brush, ["brush"] = Brush,
        ["Lasso"] = "M11,18 C-2,18 -1,5 11,4 C26,2 24,17 11,18 Z M11,18 Q5,21 8,24",
        ["Pan"] = "M6,13 L5,5 Q5,3 7,3 Q8,3 8,5 L9,11 L9,3 Q9,1 11,1 Q12,1 12,3 L12,11 L13,4 Q13,2 15,3 L15,12 L17,6 Q18,4 19,6 L18,17 Q17,23 11,23 Q7,23 4,18 L1,12 Q0,10 2,10 Q4,10 6,13 Z",
        ["Move"] = "M3,3 L10,3 M3,3 L3,10 M3,3 L10,10 M21,21 L14,21 M21,21 L21,14 M21,21 L14,14",
        ["Zoom"] = "M18,10 A8,8 0 1 1 2,10 A8,8 0 1 1 18,10 M16,16 L22,22",
        ["Blur"] = "M12,1 C8,7 4,12 4,16 C4,26 20,26 20,16 C20,12 16,7 12,1 Z",
        ["Type"] = "M3,2 L21,2 Q22,2 22,3 L22,5 Q22,6 21,6 L14,6 L14,22 Q14,23 12,23 Q10,23 10,22 L10,6 L3,6 Q2,6 2,5 L2,3 Q2,2 3,2 Z",
        ["Shape"] = "M2,2 L13,2 Q14,2 14,3 L14,7 C8,7 7,9 7,14 L2,14 Q1,14 1,13 L1,3 Q1,2 2,2 Z M23,16 A7,7 0 1 1 9,16 A7,7 0 1 1 23,16 Z",
        ["Crop"] = "M5,1 L5,19 L23,19 M1,5 L14,5 M19,10 L19,23 M8,16 L21,3",
        ["Eyedropper"] = "M15,4 L18,1 Q23,-1 24,4 Q24,6 21,9 Z M13,4 Q12,3 11,5 L19,13 Q21,12 20,11 Z M11,7 L3,15 L2,19 L0,21 Q0,24 3,23 L5,21 L9,20 L17,12 Z",
        ["Heal"] = "F0 M3,14 L14,3 C21,-4 29,4 22,11 L11,22 C4,29 -4,21 3,14 Z M8,15 L15,8 L18,11 L11,18 Z M14,5 A.8,.8 0 1 1 12.4,5 A.8,.8 0 1 1 14,5 Z M18,7 A.8,.8 0 1 1 16.4,7 A.8,.8 0 1 1 18,7 Z M7,14 A.8,.8 0 1 1 5.4,14 A.8,.8 0 1 1 7,14 Z M10,18 A.8,.8 0 1 1 8.4,18 A.8,.8 0 1 1 10,18 Z",
        ["quick-mask"] = "M3,4 L21,4 Q22,4 22,5 L22,20 L2,20 L2,5 Q2,4 3,4 Z M16,12 A4,4 0 1 1 8,12 A4,4 0 1 1 16,12 Z",
        ["screen"] = "M17,6 L17,3 L2,3 L2,17 L5,17 M7,8 L22,8 L22,21 L7,21 Z",
        ["generate"] = "M20,3 L3,3 Q2,3 2,4 L2,21 L22,21 L22,4 Q22,3 20,3 Z M3,19 L8,13 L14,19 L17,16 L21,20 M17,6 L18,9 L21,10 L18,11 L17,14 L16,11 L13,10 L16,9 Z",
        ["plugin"] = "M8,7 L8,4 C4,-2 19,-2 15,4 L15,7 L21,7 L21,12 C28,9 28,22 21,19 L21,24 L15,24 C18,17 5,17 8,24 L2,24 L2,18 C9,21 9,8 2,11 L2,7 Z",
    };
}
