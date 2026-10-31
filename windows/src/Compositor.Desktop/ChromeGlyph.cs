using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Compositor.Desktop;

/// <summary>
/// The small marks on a panel group's bar, as Photoshop draws them: « and » to collapse a column to icons and expand it
/// again, × to close, and ≡ for the panel menu. One-pixel lines on whole pixels, so they stay crisp at 100%.
/// </summary>
internal sealed class ChromeGlyph(string kind) : Control
{
    public override void Render(DrawingContext context)
    {
        var cx = Math.Round(Bounds.Width / 2); var cy = Math.Round(Bounds.Height / 2);
        var ink = IsPointerOver ? Skin.LabelBrush : Skin.SecondaryBrush;
        var pen = new Pen(ink, 1.2) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        switch (kind)
        {
            case "more" or "•••":
                foreach (var dx in new[] { -6d, 0, 6 }) context.DrawEllipse(Skin.LabelBrush, null, new Point(cx + dx, cy), 1.8, 1.8);
                break;
            case "×":
                context.DrawLine(pen, new(cx - 3.5, cy - 3.5), new(cx + 3.5, cy + 3.5));
                context.DrawLine(pen, new(cx - 3.5, cy + 3.5), new(cx + 3.5, cy - 3.5));
                break;
            case "≡":
                foreach (var dy in new[] { -3.5, -.5, 2.5 }) context.DrawLine(pen, new(cx - 4.5, cy + dy), new(cx + 4.5, cy + dy));
                break;
            default:
                // « collapses toward the edge, » opens back out: two chevrons, three pixels apart.
                var direction = kind == "«" ? -1 : 1;
                foreach (var dx in new[] { -2, 2d })
                {
                    context.DrawLine(pen, new(cx + dx - 1.75 * direction, cy - 3.5), new(cx + dx + 1.75 * direction, cy));
                    context.DrawLine(pen, new(cx + dx + 1.75 * direction, cy), new(cx + dx - 1.75 * direction, cy + 3.5));
                }
                break;
        }
    }

    protected override void OnPointerEntered(Avalonia.Input.PointerEventArgs e) { base.OnPointerEntered(e); InvalidateVisual(); }
    protected override void OnPointerExited(Avalonia.Input.PointerEventArgs e) { base.OnPointerExited(e); InvalidateVisual(); }
}
