namespace Compositor.Core.Format;

public sealed partial class LayerEffects
{
    /// <summary>Upgrade a copy when edited. Merely opening old projects does not change their appearance.</summary>
    public LayerEffects EditableCopy()
    {
        var copy = Scaled(1);
        if (copy.Items is not null) return copy;
        copy.Items = [];
        void Add(StyleEffectKind kind, bool enabled, double r, double g, double b, double opacity,
            double size = 0, double distance = 0, double angle = 90)
        {
            copy.Items.Add(new StyleEffect { Kind = kind, Enabled = enabled, Red = r, Green = g, Blue = b,
                Opacity = opacity, Size = size, Distance = distance, Angle = angle });
        }
        if (Shadow is { } shadow) Add(StyleEffectKind.DropShadow, shadow.IsEnabled, shadow.Red, shadow.Green, shadow.Blue, shadow.Opacity, shadow.Blur, shadow.Distance, shadow.Angle);
        if (OuterGlow is { } outer) Add(StyleEffectKind.OuterGlow, outer.IsEnabled, outer.Red, outer.Green, outer.Blue, outer.Opacity, outer.Size);
        if (ColorOverlay is { } color) Add(StyleEffectKind.ColorOverlay, color.IsEnabled, color.Red, color.Green, color.Blue, color.Opacity);
        if (InnerGlow is { } glow) Add(StyleEffectKind.InnerGlow, glow.IsEnabled, glow.Red, glow.Green, glow.Blue, glow.Opacity, glow.Size);
        if (InnerShadow is { } inner) Add(StyleEffectKind.InnerShadow, inner.IsEnabled, inner.Red, inner.Green, inner.Blue, inner.Opacity, inner.Blur, inner.Distance, inner.Angle);
        if (Stroke is { } stroke)
        {
            Add(StyleEffectKind.Stroke, stroke.IsEnabled, stroke.Red, stroke.Green, stroke.Blue, stroke.Opacity, stroke.Size);
            copy.Items[^1].Position = stroke.Inside ? StrokePosition.Inside : StrokePosition.Outside;
        }
        copy.Stroke = null; copy.Shadow = null; copy.ColorOverlay = null;
        copy.InnerShadow = null; copy.OuterGlow = null; copy.InnerGlow = null;
        return copy;
    }
}
