using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
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
        var rows = new Style(s => s.OfType<ListBoxItem>());
        rows.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Skin.SurfaceDarkBrush));
        rows.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, Skin.LabelBrush)); styles.Add(rows);
        var selected = new Style(s => s.OfType<ListBoxItem>().Class(":selected"));
        selected.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Skin.AccentBrush));
        selected.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, Brushes.White)); styles.Add(selected);
        // Explicit row template: Fluent's inner presenter otherwise retains its own state brushes.
        var rowTemplate = new Style(s => s.OfType<ListBoxItem>());
        rowTemplate.Setters.Add(new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<ListBoxItem>((owner, _) => new ContentPresenter
        {
            Name = "PART_ContentPresenter", Padding = owner.Padding,
            [!ContentPresenter.ContentProperty] = new TemplateBinding(ContentControl.ContentProperty),
            [!ContentPresenter.ContentTemplateProperty] = new TemplateBinding(ContentControl.ContentTemplateProperty),
            [!ContentPresenter.BackgroundProperty] = new TemplateBinding(TemplatedControl.BackgroundProperty),
            [!ContentPresenter.HorizontalContentAlignmentProperty] = new TemplateBinding(ContentControl.HorizontalContentAlignmentProperty),
            [!ContentPresenter.VerticalContentAlignmentProperty] = new TemplateBinding(ContentControl.VerticalContentAlignmentProperty),
        })));
        styles.Add(rowTemplate);
        var menuRows = new Style(s => s.OfType<MenuItem>());
        menuRows.Setters.Add(new Setter(Control.MinHeightProperty, 24d));
        menuRows.Setters.Add(new Setter(TemplatedControl.PaddingProperty, new Thickness(7, 2)));
        menuRows.Setters.Add(new Setter(TemplatedControl.FontSizeProperty, 12d)); styles.Add(menuRows);
        var tabs = new Style(s => s.OfType<TabItem>());
        tabs.Setters.Add(new Setter(TemplatedControl.FontSizeProperty, 12d));
        tabs.Setters.Add(new Setter(TemplatedControl.PaddingProperty, new Thickness(7, 3)));
        tabs.Setters.Add(new Setter(Control.MinHeightProperty, 28d)); styles.Add(tabs);
        // Photoshop's panel tabs: the open tab is the panel's own surface, joined to it, and the others sit back on the
        // darker strip in gray; no underline.
        var strip = new Style(s => s.OfType<TabControl>().Class("dock"));
        strip.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Skin.TabStripBrush));
        strip.Setters.Add(new Setter(TemplatedControl.PaddingProperty, new Thickness(0))); styles.Add(strip);
        var body = new Style(s => s.OfType<TabControl>().Class("dock").Template().OfType<ContentPresenter>().Name("PART_SelectedContentHost"));
        body.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, Skin.ChromeBrush)); styles.Add(body);
        var dockTab = new Style(s => s.OfType<TabItem>().Class("dock"));
        dockTab.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent));
        dockTab.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, Skin.SecondaryBrush));
        dockTab.Setters.Add(new Setter(TemplatedControl.PaddingProperty, new Thickness(10, 0)));
        dockTab.Setters.Add(new Setter(Control.MinHeightProperty, 26d));
        dockTab.Setters.Add(new Setter(Layoutable.MarginProperty, new Thickness(0)));
        styles.Add(dockTab);
        foreach (var state in new[] { ":selected", ":pointerover" })
        {
            var lit = new Style(s => s.OfType<TabItem>().Class("dock").Class(state));
            if (state == ":selected") lit.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Skin.ChromeBrush));
            lit.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, Skin.LabelBrush)); styles.Add(lit);
            var face = new Style(s => s.OfType<TabItem>().Class("dock").Class(state).Template().OfType<Border>().Name("PART_LayoutRoot"));
            if (state == ":selected") face.Setters.Add(new Setter(Border.BackgroundProperty, Skin.ChromeBrush));
            else face.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.Transparent));
            styles.Add(face);
            var text = new Style(s => s.OfType<TabItem>().Class("dock").Class(state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));
            text.Setters.Add(new Setter(ContentPresenter.ForegroundProperty, Skin.LabelBrush)); styles.Add(text);
        }
        var pipe = new Style(s => s.OfType<TabItem>().Class("dock").Template().OfType<Border>().Name("PART_SelectedPipe"));
        pipe.Setters.Add(new Setter(Visual.IsVisibleProperty, false)); styles.Add(pipe);
        var disabled = new Style(s => s.OfType<Button>().Class(":disabled"));
        disabled.Setters.Add(new Setter(Visual.OpacityProperty, .45d)); styles.Add(disabled);
        EditorScrollbars.Apply(styles);
    }
}
