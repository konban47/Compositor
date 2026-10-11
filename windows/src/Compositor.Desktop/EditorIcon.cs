using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Compositor.Desktop;

/// <summary>Upstream drawings first, then portable counterparts for system symbols and Windows-only tools.</summary>
internal sealed class EditorIcon(string kind) : Control
{
    protected override Size MeasureOverride(Size availableSize) => new(22, 22);
    public override void Render(DrawingContext context) => Draw(context, kind, Bounds.Size);
    internal static bool Draw(DrawingContext context, string kind, Size size)
    {
        if (UpstreamArtwork.Draw(context, kind, size)) return true;
        if (!Paths.TryGetValue(kind, out var data)) return false;
        var side = Math.Max(1, Math.Min(size.Width, size.Height)); var scale = side / 24;
        using var fit = context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation((size.Width - side) / 2, (size.Height - side) / 2));
        var pen = new Pen(Skin.LabelBrush, 1.65) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (kind is "Marquee" or "Ellipse" or "selection" or "TypeMask" or "VerticalTypeMask") pen.DashStyle = new DashStyle([2, 2], 0);
        context.DrawGeometry(null, pen, StreamGeometry.Parse(data)); return true;
    }
    private const string Brush = "M9,14 L17,3 Q19,1 21,3 Q23,5 20,7 L12,17 Z M9,14 C4,12 8,20 2,21 C10,23 13,18 9,14";
    private const string Stamp = "M6,14 L9,13 L9,10 C4,2 20,2 15,10 L15,13 L18,14 L20,18 L4,18 Z M4,21 L20,21";
    private const string Nib = "M12,2 L20,13 L17,21 L7,21 L4,13 Z M12,2 L12,11 M14,13 A2,2 0 1 1 10,13 A2,2 0 1 1 14,13";
    private const string Type = "M4,5 L20,5 M4,5 L4,8 M20,5 L20,8 M12,5 L12,21 M8,21 L16,21";
    private const string Eraser = "M3,14 L12,4 Q13,3 14,4 L21,10 Q22,11 21,12 L12,21 L9,21 Z M7,10 L16,17 M12,21 L22,21";
    private const string Eye = "M2,12 Q12,0 22,12 Q12,24 2,12 Z M15,12 A3,3 0 1 1 9,12 A3,3 0 1 1 15,12";
    private static readonly Dictionary<string, string> Paths = new(StringComparer.Ordinal)
    {
        ["SelectionBrush"] = "M3,10 A7,7 0 1 1 12,19 M3,14 L3,15 M6,18 L7,19 M10,17 L18,6 Q20,4 22,6 L14,18 Q7,24 7,20 Z",
        ["QuickSelection"] = "M3,8 L3,3 L8,3 M13,3 L14,3 M3,12 L3,13 M4,17 L5,18 M10,17 L18,6 Q20,4 22,6 L14,18 Q7,24 7,20 Z",
        ["MagneticLasso"] = "M3,12 L6,3 L21,5 L19,14 L8,18 Z M8,18 L5,22 M10,10 L10,14 Q10,20 15,20 Q20,20 20,14 L20,10 L17,10 L17,14 Q17,17 15,17 Q13,17 13,14 L13,10 Z",
        ["PerspectiveCrop"] = "M4,2 L4,20 L21,20 M1,6 L19,6 L19,23 M7,8 L16,10 L16,17 L7,18 Z M11,9 L11,18 M7,13 L16,14",
        ["Slice"] = "M3,20 L9,11 L13,15 Z M9,11 L19,3 L23,7 L13,15 M3,3 L7,3 M3,6 L5,6",
        ["SliceSelect"] = "M2,18 L8,9 L12,13 Z M8,9 L18,1 L22,5 L12,13 M15,13 L22,19 L18,19 L17,23 Z",
        ["Frame"] = "M3,4 L21,4 L21,20 L3,20 Z M3,4 L21,20 M21,4 L3,20",
        ["ColorSampler"] = "M13,8 L18,3 Q20,1 22,3 Q24,5 21,7 L17,11 M12,6 L19,13 M14,9 L6,17 L3,19 L5,15 L13,7 M3,3 L3,9 M0,6 L6,6",
        ["Ruler"] = "M2,7 L22,7 L22,18 L2,18 Z M6,7 L6,12 M10,7 L10,15 M14,7 L14,12 M18,7 L18,15",
        ["Note"] = "M3,3 L21,3 L21,16 L15,22 L3,22 Z M15,22 L15,16 L21,16 M7,7 L17,7 M7,11 L17,11",
        ["Count"] = "M2,5 L5,3 L5,14 M2,14 L8,14 M10,12 Q10,8 14,8 Q19,9 14,14 L10,18 L18,18 M19,3 Q24,1 23,5 L20,7 Q26,7 23,11",
        ["Remove"] = Brush + " M3,1 L3,7 M0,4 L6,4 M19,16 L19,22 M16,19 L22,19",
        ["HealingBrush"] = "M4,14 L14,4 Q20,-1 23,6 L10,20 Q3,24 1,18 Z M8,9 L15,16 M11,7 L18,14 M10,11 L10,12 M13,12 L13,13",
        ["Patch"] = "M5,5 L19,5 L19,19 L5,19 Z M2,8 L7,8 M2,12 L7,12 M2,16 L7,16 M17,8 L22,8 M17,12 L22,12 M17,16 L22,16 M8,2 L8,7 M12,2 L12,7 M16,2 L16,7 M8,17 L8,22 M12,17 L12,22 M16,17 L16,22",
        ["ContentMove"] = "M2,8 L18,8 M14,3 L19,8 L14,13 M22,16 L6,16 M10,11 L5,16 L10,21",
        ["RedEye"] = Eye + " M3,1 L3,7 M0,4 L6,4",
        ["Pencil"] = "M3,21 L5,14 L17,2 L22,7 L10,19 Z M5,14 L10,19 M3,21 L6,20 M14,5 L19,10",
        ["ColorReplacement"] = Brush + " M1,2 L8,2 L8,9 L1,9 Z M5,5 L10,5 L10,11",
        ["MixerBrush"] = Brush + " M4,1 C2,5 0,7 1,9 C3,12 7,10 7,7 Z",
        ["Brush"] = Brush, ["brush"] = Brush, ["PatternStamp"] = Stamp + " M2,2 L2,5 M1,3.5 L4,3.5", ["Clone"] = Stamp,
        ["Move"] = "M12,2 L12,22 M2,12 L22,12 M8,6 L12,2 L16,6 M8,18 L12,22 L16,18 M6,8 L2,12 L6,16 M18,8 L22,12 L18,16",
        ["Marquee"] = "M3,5 L21,5 L21,19 L3,19 Z", ["Ellipse"] = "M21,12 A9,7 0 1 1 3,12 A9,7 0 1 1 21,12",
        ["Lasso"] = "M10,4 C-2,4 1,17 12,17 C25,17 25,5 14,4 C5,2 7,12 10,16 Q14,24 5,22",
        ["Polygon"] = "M4,14 L6,4 L19,5 L22,15 L10,20 Z M10,20 Q5,24 3,20",
        ["Wand"] = "M4,21 L17,8 M15,5 L15,1 M20,7 L23,7 M7,8 L3,8 M10,4 L8,2 M19,3 L21,1",
        ["Object"] = "M3,9 L3,3 L9,3 M15,3 L21,3 L21,9 M3,15 L3,21 L9,21 M15,21 L21,21 L21,15 M8,11 A4,4 0 1 1 16,11 L17,17 L7,17 Z",
        ["Pan"] = "M7,12 L7,5 Q9,2 10,5 L10,11 L10,3 Q12,1 13,4 L13,11 L13,5 Q15,3 16,6 L16,12 L16,8 Q19,6 19,10 L19,16 Q17,22 11,22 L7,20 L3,14 Q1,10 4,11 Z",
        ["Blur"] = "M12,2 C9,8 4,12 5,16 C6,23 18,23 19,16 C20,12 15,8 12,2 Z M8,16 Q8,19 11,19",
        ["Sharpen"] = "M12,3 L22,21 L2,21 Z M12,3 L12,21",
        ["Smudge"] = "M6,21 L5,15 L10,7 Q12,4 14,7 L11,12 L17,7 Q20,6 20,9 L16,17 L12,21",
        ["Liquify"] = "M2,8 C6,1 8,15 12,8 C16,1 18,15 22,8 M2,16 C6,9 8,23 12,16 C16,9 18,23 22,16",
        ["Heal"] = "M4,14 L14,4 Q20,-1 23,6 L10,20 Q3,24 1,18 Z M8,9 L15,16 M11,7 L18,14",
        ["Eyedropper"] = "M14,8 L18,3 Q20,1 22,3 Q24,5 21,7 L17,11 M12,6 L19,13 M14,9 L4,19 L2,22 L6,21 L16,11",
        ["Type"] = Type, ["TypeMask"] = Type, ["VerticalType"] = Type + " M2,11 L2,20 M1,18 L2,21 L4,18",
        ["VerticalTypeMask"] = Type + " M2,11 L2,20 M1,18 L2,21 L4,18",
        ["Crop"] = "M6,2 L6,18 L22,18 M2,6 L18,6 L18,22 M8,16 L21,3",
        ["Shape"] = "M3,4 L21,4 L21,20 L3,20 Z", ["ShapeEllipse"] = "M21,12 A9,8 0 1 1 3,12 A9,8 0 1 1 21,12",
        ["Triangle"] = "M12,3 L22,21 L2,21 Z", ["PolygonShape"] = "M7,3 L17,3 L23,12 L17,21 L7,21 L1,12 Z",
        ["Star"] = "M12,2 L15,9 L23,9 L17,14 L19,22 L12,17 L5,22 L7,14 L1,9 L9,9 Z", ["Line"] = "M3,21 L21,3",
        ["CustomShape"] = "M12,21 C-8,8 4,-4 12,6 C20,-4 32,8 12,21 Z",
        ["Gradient"] = "M2,5 L22,5 L22,19 L2,19 Z M5,6 L5,18 M8,6 L8,18 M11,6 L11,18 M14,6 L14,18",
        ["Bucket"] = "M4,11 L11,4 L20,13 L12,21 Z M7,2 L12,8 M4,11 L18,11 M21,15 Q17,20 21,22 Q25,20 21,15",
        ["HistoryBrush"] = Brush + " M2,6 A6,6 0 0 1 11,2 M2,2 L2,6 L6,6", ["ArtHistory"] = Brush + " M2,3 C10,-1 12,9 5,10 C-1,9 1,5 5,5",
        ["Eraser"] = Eraser, ["BackgroundEraser"] = Eraser + " M1,3 L5,7 M5,3 L1,7", ["MagicEraser"] = Eraser + " M3,1 L3,7 M0,4 L6,4",
        ["Dodge"] = "M3,22 L10,14 M10,14 C0,6 16,-2 21,5 C26,13 13,20 10,14 Z", ["Burn"] = "M2,16 Q5,11 10,13 L16,8 Q19,7 19,10 L16,14 L21,14 L20,21 L9,22 Z",
        ["Sponge"] = "M4,5 Q12,0 21,5 L21,19 Q12,23 3,19 Z M7,7 L8,7 M14,6 L15,6 M6,13 L7,13 M14,12 L15,12 M18,17 L19,17 M10,19 L11,19",
        ["AdjustmentBrush"] = Brush + " M1,5 A4,4 0 1 1 9,5 A4,4 0 1 1 1,5 M5,1 L5,9",
        ["Pen"] = Nib, ["FreeformPen"] = Nib + " M1,4 Q4,8 1,12", ["CurvaturePen"] = Nib + " M1,4 Q6,8 1,12",
        ["AddAnchor"] = Nib + " M1,3 L7,3 M4,0 L4,6", ["DeleteAnchor"] = Nib + " M1,3 L7,3", ["ConvertAnchor"] = "M5,21 L12,3 L21,21",
        ["Path"] = "M5,2 L20,13 L13,14 L9,22 Z", ["PathSelection"] = "M5,2 L20,13 L13,14 L9,22 Z M8,7 L8,15 L11,12 Z",
        ["RotateView"] = "M4,7 A9,9 0 1 1 3,16 M4,2 L4,7 L9,7 M8,14 L13,6 L17,17 Z",
        ["Zoom"] = "M17,10 A7,7 0 1 1 3,10 A7,7 0 1 1 17,10 M15,15 L22,22 M7,10 L13,10 M10,7 L10,13",
        ["home"] = "M2,11 L12,2 L22,11 M5,9 L5,22 L10,22 L10,15 L15,15 L15,22 L19,22 L19,9",
        ["link"] = "M9,7 L11,5 C18,-2 26,7 19,13 L17,15 M7,9 L5,11 C-2,18 7,26 13,19 L15,17 M8,16 L16,8",
        ["warp"] = "M4,3 Q12,7 20,3 L22,21 Q12,17 2,21 Z M3,12 Q12,9 21,12 M12,5 L12,19",
        ["cancel"] = "M21,12 A9,9 0 1 1 3,12 A9,9 0 1 1 21,12 M6,6 L18,18", ["check"] = "M3,12 L9,19 L22,4",
        ["close"] = "M5,5 L19,19 M5,19 L19,5", ["plus"] = "M4,12 L20,12 M12,4 L12,20", ["minus"] = "M4,12 L20,12",
        ["lock"] = "M7,10 L7,6 C7,0 17,0 17,6 L17,10 M5,10 L19,10 L19,22 L5,22 Z M12,14 L12,18",
        ["folder"] = "M2,5 L9,5 L12,8 L22,8 L22,21 L2,21 Z", ["delete"] = "M9,2 L15,2 M3,5 L21,5 M6,5 L7,22 L17,22 L18,5 M10,9 L10,18 M14,9 L14,18",
        ["mask"] = "M2,4 L22,4 L22,20 L2,20 Z M16,12 A4,4 0 1 1 8,12 A4,4 0 1 1 16,12",
        ["eye"] = Eye, ["eye-closed"] = Eye + " M2,2 L22,22", ["more"] = "M3,12 L4,12 M11,12 L12,12 M19,12 L20,12",
        ["history"] = "M5,3 L20,3 L20,21 L5,21 Z M8,7 L17,7 M8,11 L17,11 M8,15 L17,15",
        ["snapshot"] = "M2,7 L7,7 L9,3 L15,3 L17,7 L22,7 L22,21 L2,21 Z M17,14 A5,5 0 1 1 7,14 A5,5 0 1 1 17,14",
        ["new-document"] = "M5,2 L14,2 L20,8 L20,22 L5,22 Z M14,2 L14,8 L20,8 M8,14 L17,14 M12,10 L12,19",
        ["add"] = "M4,3 L20,3 L20,21 L4,21 Z M8,12 L16,12 M12,8 L12,16",
    };
}
