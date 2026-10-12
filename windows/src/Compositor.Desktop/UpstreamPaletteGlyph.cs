using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Compositor.Desktop;

/// <summary>Independent vectors matching ColorPaletteControls.swift's system-symbol roles; no Apple font assets.</summary>
internal sealed class UpstreamPaletteGlyph(string kind) : Control
{
    protected override Size MeasureOverride(Size availableSize) => new(24, 24);
    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        using var fit = context.PushTransform(Matrix.CreateScale(size / 24, size / 24)
            * Matrix.CreateTranslation((Bounds.Width - size) / 2, (Bounds.Height - size) / 2));
        if (kind == "more")
        {
            foreach (var x in new[] { 4d, 12d, 20d }) context.DrawEllipse(Skin.LabelBrush, null, new Point(x, 12), 2.1, 2.1);
            return;
        }
        var pen = new Pen(Skin.SecondaryBrush, 1.8) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (kind == "swap")
        {
            using var turn = context.PushTransform(Matrix.CreateTranslation(-12, -12) * Matrix.CreateRotation(Math.PI / 4) * Matrix.CreateTranslation(12, 12));
            context.DrawGeometry(null, pen, StreamGeometry.Parse("M3,8 L21,8 M17,4 L21,8 L17,12 M21,16 L3,16 M7,12 L3,16 L7,20"));
        }
        else context.DrawGeometry(null, pen, StreamGeometry.Parse("M5,10 A8,8 0 1 1 5,17 M5,4 L5,10 L11,10"));
    }
}
