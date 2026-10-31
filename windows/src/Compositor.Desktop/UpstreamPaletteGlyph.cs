using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Compositor.Desktop;

/// <summary>ColorPaletteControls.swift's swap and reset buttons, in secondary gray as upstream has them, from the app's
/// icon system; the swap arrows turned a quarter, so they point from one swatch to the other.</summary>
internal sealed class UpstreamPaletteGlyph(string kind) : Control
{
    protected override Size MeasureOverride(Size availableSize) => new(24, 24);
    public override void Render(DrawingContext context)
    {
        if (kind == "more") { EditorIcon.Draw(context, "more", Bounds.Size); return; }
        if (kind != "swap") { IconSet.Draw(context, "reset", Bounds.Size, Skin.SecondaryBrush); return; }
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        using var turn = context.PushTransform(Matrix.CreateTranslation(-center.X, -center.Y) * Matrix.CreateRotation(Math.PI / 4) * Matrix.CreateTranslation(center.X, center.Y));
        IconSet.Draw(context, "swap", Bounds.Size, Skin.SecondaryBrush);
    }
}
