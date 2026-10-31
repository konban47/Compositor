using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Compositor.Desktop;

/// <summary>Brush Settings' lock beside each section, and its new-preset button, from the app's icon system.</summary>
internal sealed class BrushSettingsGlyph(bool locked, bool add = false) : Control
{
    internal bool Locked { get; set; } = locked;
    protected override Size MeasureOverride(Size availableSize) => new(16, 16);
    public override void Render(DrawingContext context) => IconSet.Draw(context, add ? "add" : Locked ? "lock" : "unlock", Bounds.Size);
}
