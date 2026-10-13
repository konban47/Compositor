using Avalonia;
using Avalonia.Media;

namespace Compositor.Desktop;

/// <summary>One geometry system for the inspector's fourteen alignment and distribution commands.</summary>
internal static class AlignmentGlyph
{
    // All reference lines, including equal-spacing guides, share this logical-pixel width.
    internal const double GuideThickness = 1.5;

    public static bool Draw(DrawingContext context, string kind, Size size)
    {
        if (!kind.StartsWith("Align ") && !kind.StartsWith("Distribute ")) return false;
        var side = Math.Min(size.Width, size.Height);
        using var fit = context.PushTransform(Matrix.CreateScale(side / 22, side / 22)
            * Matrix.CreateTranslation((size.Width - side) / 2, (size.Height - side) / 2));
        var vertical = kind.Contains("Top") || kind.Contains("Bottom") || kind.Contains("V Center") || kind.Contains("Vertic");
        // Transposition preserves the exact guide thickness and object dimensions in both orientations.
        Point Map(double x, double y) => vertical ? new(y, x) : new(x, y);
        var pen = new Pen(Skin.LabelBrush, GuideThickness) { LineCap = PenLineCap.Flat };
        void Guide(double x) => context.DrawLine(pen, Map(x, 2), Map(x, 20));
        void Object(double x, double y, double w, double h) => context.FillRectangle(Skin.LabelBrush,
            vertical ? new Rect(y, x, h, w) : new Rect(x, y, w, h));
        var center = kind.Contains("Center") || kind is "Distribute Horizontally" or "Distribute Vertically";
        var end = kind.Contains("Right") || kind.Contains("Bottom");
        if (kind.StartsWith("Align "))
        {
            var axis = center ? 11 : end ? 19 : 3;
            Guide(axis);
            for (var i = 0; i < 2; i++)
            {
                var width = i == 0 ? 7 : 12;
                Object(center ? axis - width / 2.0 : end ? axis - width - 2 : axis + 2, 5 + i * 8, width, 4);
            }
        }
        else if (kind.Contains("Spacing"))
        {
            Guide(3); Guide(19);
            Object(9, 4, 4, 14);
        }
        else
        {
            // The reference uses two unequal objects and their own edge/center guides.
            // Three equally sized bars would make the edge and center commands indistinguishable.
            for (var i = 0; i < 2; i++)
            {
                var axis = 6 + i * 10;
                const double width = 4;
                var height = i == 0 ? 7 : 12;
                Guide(axis);
                Object(center ? axis - width / 2.0 : end ? axis - width - 1.5 : axis + 1.5, (22 - height) / 2.0, width, height);
            }
        }
        return true;
    }
}
