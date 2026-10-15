using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Compositor.Desktop;

internal sealed class ChromeGlyph(string kind) : Control
{
    public override void Render(DrawingContext context)
    {
        var cx = Bounds.Width / 2; var cy = Bounds.Height / 2;
        var ink = Skin.LabelBrush; var pen = new Pen(ink, 1.25) { LineCap = PenLineCap.Square };
        if (kind is "•••" or "more")
        { foreach (var dx in new[] { -7d, 0, 7 }) context.DrawEllipse(ink, null, new Point(cx + dx, cy), 2.1, 2.1); }
        else if (kind == "×")
        { context.DrawLine(pen, new(cx - 4, cy - 4), new(cx + 4, cy + 4)); context.DrawLine(pen, new(cx - 4, cy + 4), new(cx + 4, cy - 4)); }
        else
        {
            var direction = kind == "«" ? -1 : 1;
            foreach (var dx in new[] { -2.5, 2.5 })
            { context.DrawLine(pen, new(cx + dx - 2 * direction, cy - 4), new(cx + dx + 2 * direction, cy)); context.DrawLine(pen, new(cx + dx + 2 * direction, cy), new(cx + dx - 2 * direction, cy + 4)); }
        }
    }
}
