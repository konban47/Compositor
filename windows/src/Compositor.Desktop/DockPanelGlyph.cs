using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Compositor.Desktop;

/// <summary>A panel's mark when its group is collapsed to icons, from the app's icon system (<see cref="IconSet"/>).</summary>
internal sealed class DockPanelGlyph(string name) : Control
{
    internal static IReadOnlyDictionary<string, IconSet.Glyph> Symbols { get; } = IconSet.Glyphs
        .Where(pair => pair.Key.StartsWith("panel:", StringComparison.Ordinal))
        .ToDictionary(pair => pair.Key["panel:".Length..], pair => pair.Value, StringComparer.Ordinal);
    internal static bool HasSymbol(string name) => Symbols.ContainsKey(name);
    protected override Size MeasureOverride(Size availableSize) => new(22, 22);
    public override void Render(DrawingContext context) => IconSet.Draw(context, "panel:" + name, Bounds.Size);
}
