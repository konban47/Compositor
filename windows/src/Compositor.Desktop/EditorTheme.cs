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
        // Upstream uses the operating system's sans-serif UI face, not the document font.
        // Windows supplies Segoe UI and Microsoft YaHei UI; SF is not a redistributable repository asset.
        var windows = new Style(s => s.OfType<Window>());
        windows.Setters.Add(new Setter(TemplatedControl.FontFamilyProperty, new FontFamily("Segoe UI, Microsoft YaHei UI, Noto Sans SC")));
        windows.Setters.Add(new Setter(TemplatedControl.FontSizeProperty, (double)EditorPreferences.Current.UiFontSize));
        windows.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, Skin.LabelBrush));
        styles.Add(windows);
        var tips = new Style(s => s.Is<Control>());
        tips.Setters.Add(new Setter(ToolTip.IsEnabledProperty, EditorPreferences.Current.Tooltips)); styles.Add(tips);
        // Upstream ContentView: 7-point tool corners, translucent white selection and a quiet hairline.
        var buttons = new Style(s => s.OfType<Button>());
        buttons.Setters.Add(new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(UpstreamArtwork.ButtonRadius)));
        buttons.Setters.Add(new Setter(TemplatedControl.FontSizeProperty, (double)EditorPreferences.Current.UiFontSize));
        buttons.Setters.Add(new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)));
        buttons.Setters.Add(new Setter(TemplatedControl.BorderBrushProperty, Skin.BorderControlBrush));
        buttons.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Skin.SurfaceControlBrush));
        buttons.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, Skin.LabelBrush));
        styles.Add(buttons);
        foreach (var (state, fill) in new[] { (":pointerover", Skin.SurfaceControlHoverBrush), (":pressed", Skin.SurfaceControlPressedBrush) })
        {
            var style = new Style(s => s.OfType<Button>().Class(state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));
            style.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, fill));
            style.Setters.Add(new Setter(ContentPresenter.BorderBrushProperty, Skin.BorderControlBrush)); styles.Add(style);
        }
        var disabled = new Style(s => s.OfType<Button>().Class(":disabled"));
        disabled.Setters.Add(new Setter(Visual.OpacityProperty, .45d)); styles.Add(disabled);
    }
}
