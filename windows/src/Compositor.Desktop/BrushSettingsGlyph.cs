using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Compositor.Desktop;

internal sealed class BrushSettingsGlyph(bool locked, bool add = false) : Control
{
    internal bool Locked { get; set; } = locked;
    protected override Size MeasureOverride(Size availableSize) => new(16, 16);
    public override void Render(DrawingContext context)
    {
        var pen = new Pen(Skin.LabelBrush, 1.3);
        using var scale = context.PushTransform(Matrix.CreateScale(Bounds.Width / 16, Bounds.Height / 16));
        if (add)
        {
            context.DrawRectangle(null, pen, new Rect(2, 2, 12, 12));
            context.DrawLine(pen, new(5, 8), new(11, 8)); context.DrawLine(pen, new(8, 5), new(8, 11));
        }
        else
        {
            context.DrawRectangle(null, pen, new Rect(3, 7, 10, 7), 1, 1);
            var shackle = new StreamGeometry(); using (var path = shackle.Open())
            { path.BeginFigure(new(6, 7), false); path.LineTo(new(6, 4)); path.CubicBezierTo(new(6, 0), new(12, 0), new(12, 4)); if (Locked) path.LineTo(new(12, 7)); path.EndFigure(false); }
            context.DrawGeometry(null, pen, shackle); context.DrawEllipse(Skin.LabelBrush, null, new(8, 10.5), 1, 1);
        }
    }
}
