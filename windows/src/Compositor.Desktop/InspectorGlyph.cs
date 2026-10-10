using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Compositor.Desktop;

/// <summary>Small vector icons keep the inspector legible at every display scale.</summary>
internal sealed class InspectorGlyph(string kind) : Control
{
    protected override Size MeasureOverride(Size availableSize) => new(22, 22);
    public override void Render(DrawingContext context)
    {
        var pen = new Pen(Skin.LabelBrush, 1.4);
        void Line(double a, double b, double c, double d) => context.DrawLine(pen, new Point(a, b), new Point(c, d));
        if (kind.StartsWith("Align") || kind.StartsWith("Distribute"))
        {
            var vertical = kind.Contains("Top") || kind.Contains("Bottom") || kind.Contains("V Center") || kind.Contains("Vertic");
            using var turn = context.PushTransform(vertical ? Matrix.CreateTranslation(-11, -11) * Matrix.CreateRotation(Math.PI / 2) * Matrix.CreateTranslation(11, 11) : Matrix.Identity);
            var middle = kind.Contains("Center") || kind is "Distribute Horizontally" or "Distribute Vertically";
            var end = kind.Contains("Right") || kind.Contains("Bottom");
            if (kind.StartsWith("Align"))
            {
                var axis = middle ? 11 : end ? 19 : 3; Line(axis, 2, axis, 20);
                for (var i = 0; i < 2; i++)
                {
                    var width = i == 0 ? 11 : 7;
                    var x = middle ? axis - width / 2.0 : end ? axis - width - 2 : axis + 2;
                    context.FillRectangle(Skin.LabelBrush, new Rect(x, 5 + i * 8, width, 4));
                }
            }
            else if (kind.Contains("Spacing"))
            {
                Line(3, 2, 3, 20); Line(19, 2, 19, 20);
                context.FillRectangle(Skin.LabelBrush, new Rect(7, 4, 3, 14));
                context.FillRectangle(Skin.LabelBrush, new Rect(13, 4, 3, 14));
            }
            else
            {
                foreach (var x in new[] { 4, 11, 18 })
                {
                    Line(x, 2, x, 20);
                    context.FillRectangle(Skin.LabelBrush, new Rect(middle ? x - 2 : end ? x - 4 : x, 7, 4, 8));
                }
            }
            return;
        }
        var data = kind switch
        {
            "flip-x" => "M10,2 L10,20 M7,6 L2,11 L7,16 Z M13,6 L18,11 L13,16 Z",
            "flip-y" => "M2,11 L20,11 M6,8 L11,3 L16,8 Z M6,14 L11,19 L16,14 Z",
            "rotation" => "M3,17 L18,17 M3,17 L13,5 M9,17 A6,6 0 0 0 6,12",
            "reset" => "M5,6 A8,8 0 1 1 4,16 M5,1 L5,6 L10,6",
            "snapshot" => "M2,6 L7,6 L8,3 L14,3 L15,6 L20,6 L20,19 L2,19 Z M15,12 A4,4 0 1 1 7,12 A4,4 0 1 1 15,12",
            "new-document" => "M4,2 L13,2 L18,7 L18,20 L4,20 Z M13,2 L13,7 L18,7 M7,13 L15,13 M11,9 L11,17",
            "history" => "M5,3 L19,3 L19,20 L5,20 Z M8,7 L16,7 M8,11 L16,11 M8,15 L16,15",
            "up" => "M4,13 L11,6 L18,13 M11,6 L11,20",
            "down" => "M4,9 L11,16 L18,9 M11,2 L11,16",
            "delete" => "M8,2 L14,2 M3,5 L19,5 M5,7 L6,20 L16,20 L17,7 M9,9 L9,17 M13,9 L13,17",
            "more" => "M3,11 L5,11 M10,11 L12,11 M17,11 L19,11",
            "colors" => "M2,2 L14,2 L14,14 L2,14 Z M8,16 L8,20 L20,20 L20,8 L16,8",
            "screen" => "M2,3 L20,3 L20,16 L2,16 Z M8,20 L14,20 M11,16 L11,20",
            "generate" => "M2,4 L20,4 L20,19 L2,19 Z M3,17 L8,10 L12,15 L15,12 L19,17 M15,1 L15,7 M12,4 L18,4",
            _ => "M8,5 L5,5 C0,5 0,17 5,17 L8,17 M14,5 L17,5 C22,5 22,17 17,17 L14,17 M7,11 L15,11",
        };
        context.DrawGeometry(null, pen, StreamGeometry.Parse(data));
    }
}
