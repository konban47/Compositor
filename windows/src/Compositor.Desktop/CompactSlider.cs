using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;

namespace Compositor.Desktop;

internal static class CompactSlider
{
    internal static void Apply(Slider slider) => slider.Template = Template;
    // Keep the thumb inside the control even when hosted in a compact form row.
    internal static IControlTemplate Template { get; } = new FuncControlTemplate<Slider>((owner, names) =>
    {
        var thumb = new Thumb { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center,
            Template = new FuncControlTemplate<Thumb>((_, _) => new Border { CornerRadius = new CornerRadius(7), Background = Skin.LabelBrush }) };
        RepeatButton Rail(Avalonia.Media.IBrush brush) => new() { Height = 2, MinHeight = 0, VerticalAlignment = VerticalAlignment.Center, Focusable = false,
            Template = new FuncControlTemplate<RepeatButton>((_, _) => new Border { Background = brush }) };
        var track = new Track { Name = "PART_Track", Orientation = Orientation.Horizontal, Height = 20, VerticalAlignment = VerticalAlignment.Center, Thumb = thumb,
            DecreaseButton = Rail(Skin.AccentBrush), IncreaseButton = Rail(Skin.SecondaryBrush),
            [!Track.MinimumProperty] = new TemplateBinding(RangeBase.MinimumProperty),
            [!Track.MaximumProperty] = new TemplateBinding(RangeBase.MaximumProperty),
            [!Track.ValueProperty] = new TemplateBinding(RangeBase.ValueProperty) { Mode = BindingMode.TwoWay } };
        var surface = new Grid { Background = Avalonia.Media.Brushes.Transparent, Children = { new Border { Height = 2, Margin = new Thickness(7, 0), Background = Skin.SecondaryBrush, VerticalAlignment = VerticalAlignment.Center }, track } };
        surface.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, (_, e) =>
        {
            var at = e.GetPosition(track); if (!e.GetCurrentPoint(track).Properties.IsLeftButtonPressed || thumb.Bounds.Contains(at)) return;
            owner.Value = Math.Clamp(track.ValueFromPoint(at), owner.Minimum, owner.Maximum); owner.Focus(); e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        names.Register("PART_Track", track); return surface;
    });
}
