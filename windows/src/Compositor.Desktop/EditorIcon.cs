using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Compositor.Desktop;

/// <summary>Upstream drawings first, then the app's one icon system (<see cref="IconSet"/>).</summary>
internal sealed class EditorIcon(string kind) : Control
{
    internal static string? CursorPath(string kind) => IconSet.Outline(kind);
    protected override Size MeasureOverride(Size availableSize) => new(22, 22);
    public override void Render(DrawingContext context) => Draw(context, kind, Bounds.Size);
    internal static bool Draw(DrawingContext context, string kind, Size size)
    {
        var align = kind switch { "align-left" => "Align Left", "align-center" => "Align H Center", "align-right" => "Align Right", "align-top" => "Align Top", "align-middle" => "Align V Center", "align-bottom" => "Align Bottom", _ => kind };
        if (AlignmentGlyph.Draw(context, align, size)) return true;
        if (kind is "more" or "ellipsis")
        {
            var center = new Point(size.Width / 2, size.Height / 2);
            foreach (var dx in new[] { -7, 0, 7 }) context.DrawEllipse(Skin.LabelBrush, null, center + new Vector(dx, 0), 2.1, 2.1);
            return true;
        }
        if (UpstreamArtwork.Draw(context, kind, size)) return true;
        return IconSet.Draw(context, kind, size);
    }
}