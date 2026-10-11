using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Styling;

namespace Compositor.Desktop;

internal static class EditorTheme
{
    internal static void Apply(Styles styles)
    {
        // Upstream ContentView: 7-point tool corners, translucent white selection and a quiet hairline.
        var buttons = new Style(s => s.OfType<Button>());
        buttons.Setters.Add(new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(UpstreamArtwork.ButtonRadius)));
        buttons.Setters.Add(new Setter(TemplatedControl.FontSizeProperty, 12d));
        buttons.Setters.Add(new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)));
        buttons.Setters.Add(new Setter(TemplatedControl.BorderBrushProperty, Skin.BorderControlBrush));
        buttons.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Skin.SurfaceControlBrush));
        buttons.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, Skin.LabelBrush));
        styles.Add(buttons);
        foreach (var (state, fill) in new[] { (":pointerover", "#555559"), (":pressed", "#29292C") })
        {
            var style = new Style(s => s.OfType<Button>().Class(state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));
            style.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, new SolidColorBrush(Color.Parse(fill))));
            style.Setters.Add(new Setter(ContentPresenter.BorderBrushProperty, new SolidColorBrush(Color.Parse("#737377")))); styles.Add(style);
        }
        var disabled = new Style(s => s.OfType<Button>().Class(":disabled"));
        disabled.Setters.Add(new Setter(Visual.OpacityProperty, .45d)); styles.Add(disabled);
    }
}
