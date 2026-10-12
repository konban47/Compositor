using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace Compositor.Desktop;

internal static class CompactSlider
{
    // Fluent's full-size thumb overflows its compact row. Keep the hit area tall and center a 12px thumb.
    internal static void Apply(Slider slider)
    {

        slider.Template = new FuncControlTemplate<Slider>((owner, scope) =>
        {
            var track = new Track { Name = "PART_Track", Minimum = owner.Minimum, Maximum = owner.Maximum,
                Orientation = Orientation.Horizontal, Height = 20, VerticalAlignment = VerticalAlignment.Center,
                Thumb = new Thumb { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center,
                    Template = new FuncControlTemplate<Thumb>((_, _) => new Border { Background = Skin.LabelBrush, CornerRadius = new CornerRadius(6) }) } };
            scope.Register("PART_Track", track);
            track.Bind(Track.ValueProperty, new Binding(nameof(Slider.Value)) { Source = owner, Mode = BindingMode.TwoWay });
            return new Grid { ClipToBounds = false, Children = {
                new Border { Height = 2, Margin = new Thickness(6, 0), Background = Skin.SecondaryBrush, VerticalAlignment = VerticalAlignment.Center }, track } };
        });
    }
}
