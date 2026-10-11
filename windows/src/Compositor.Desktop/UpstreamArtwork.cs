using Avalonia;
using Avalonia.Media;

namespace Compositor.Desktop;

/// <summary>
/// Direct ports of MIT-licensed Compositor Canvas drawings at b4bfdea87f9d.
/// Coordinates, fills, stroke weights and the gradient error-diffusion algorithm are unchanged.
/// Sources: Compositor/UI/{BrushControls,GradientControls,LassoControls}.swift.
/// SwiftUI and SF Symbols are platform services; their Windows counterparts live in EditorIcon.
/// </summary>
internal static class UpstreamArtwork
{
    internal const double ButtonSide = 36, ButtonRadius = 7, RailWidth = 56, RailSpacing = 10;
    internal const byte SelectedFillAlpha = 31, SelectedBorderAlpha = 36;
    private static readonly bool[,] Gradient = MakeGradient();
    private static bool[,] MakeGradient()
    {
        const int size = 16;
        var ramp = new double[size, size]; var result = new bool[size, size];
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++) ramp[y, x] = x / (double)(size - 1);
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
        {
            var on = ramp[y, x] >= .5; result[y, x] = on; var error = ramp[y, x] - (on ? 1 : 0);
            if (x + 1 < size) ramp[y, x + 1] += error * 7 / 16;
            if (y + 1 >= size) continue;
            if (x > 0) ramp[y + 1, x - 1] += error * 3 / 16;
            ramp[y + 1, x] += error * 5 / 16;
            if (x + 1 < size) ramp[y + 1, x + 1] += error / 16;
        }
        return result;
    }
    internal static bool Draw(DrawingContext context, string kind, Size size)
    {
        if (kind is not ("Clone" or "Gradient" or "Polygon" or "Object")) return false;
        var side = Math.Max(1, Math.Min(size.Width, size.Height));
        using var fit = context.PushTransform(Matrix.CreateScale(side / 18, side / 18) * Matrix.CreateTranslation((size.Width - side) / 2, (size.Height - side) / 2));
        var ink = Skin.LabelBrush;
        if (kind == "Clone")
        {
            context.DrawEllipse(ink, null, new Rect(18 * .33, 18 * .02, 18 * .34, 18 * .30));
            context.DrawRectangle(ink, null, new Rect(18 * .43, 18 * .28, 18 * .14, 18 * .28));
            context.DrawRectangle(ink, null, new Rect(18 * .12, 18 * .54, 18 * .76, 18 * .22), 18 * .08, 18 * .08);
            context.DrawRectangle(ink, null, new Rect(18 * .06, 18 * .82, 18 * .88, 18 * .12));
        }
        else if (kind == "Gradient")
        {
            var frame = new Rect(1, 1, 16, 16); var shape = new RectangleGeometry(frame, 3.5, 3.5);
            using var clip = context.PushGeometryClip(shape);
            for (var y = 0; y < 16; y++) for (var x = 0; x < 16; x++) if (Gradient[y, x]) context.DrawRectangle(ink, null, new Rect(1 + x, 1 + y, 1, 1));
            context.DrawGeometry(null, new Pen(ink, 1.4), shape);
        }
        else if (kind == "Polygon")
            context.DrawGeometry(null, new Pen(ink, 1.4) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, StreamGeometry.Parse("M1.2,7 L4,2.4 L11.8,1.8 L16.8,5.2 L15.6,10.4 L7,11.6 Z M8.9,10.9 L13.3,10.5 L11.6,14.5 Z M11.6,14.5 L12.9,17.3"));
        else
        {
            context.DrawGeometry(null, new Pen(ink, 1.6) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, StreamGeometry.Parse("M2,6 L2,2 L6,2 M12,2 L16,2 L16,6 M16,12 L16,16 L12,16 M6,16 L2,16 L2,12"));
            context.DrawGeometry(ink, null, StreamGeometry.Parse("M7,5 L7,14 L9.6,11.7 L11.3,15.3 L13.2,14.4 L11.5,10.9 L14.5,10.9 Z"));
        }
        return true;
    }
}
