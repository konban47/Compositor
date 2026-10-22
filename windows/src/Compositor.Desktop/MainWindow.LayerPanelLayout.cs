using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private bool _layerFiltering = true, _syncingLayerFilter;
    private readonly HashSet<int> _layerFilterKinds = [];
    private readonly Dictionary<int, ToggleButton> _layerFilterButtons = [];
    private readonly NumericUpDown _opacityValue = new() { Minimum = 0, Maximum = 100, Value = 100, ShowButtonSpinner = false, FormatString = "0'%'", MinWidth = 0, Height = 26, FontSize = 11, Padding = new Thickness(5, 2), Tag = "layer-opacity-value" };
    private Popup? _opacityPopup;
    private bool LayerFiltering => _layerFiltering && (_layerFilterKinds.Count > 0 || _layerKind.SelectedIndex is > 0 and not 7 || !string.IsNullOrWhiteSpace(_layerSearch.Text));

    private void ConfigureLayerList()
    {
        _layers.Background = Skin.ChromeBrush; _layers.BorderThickness = new Thickness(0); _layers.Padding = new Thickness(0);
        _layers.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        _layers.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Visible);
        _layers.SetValue(ScrollViewer.AllowAutoHideProperty, false);
        _layers.Styles.Add(new Style(s => s.OfType<ListBoxItem>()) { Setters = { new Setter(TemplatedControl.BackgroundProperty, Skin.ChromeBrush) } });
        _layers.Styles.Add(new Style(s => s.OfType<ListBoxItem>().Class(":selected")) { Setters = { new Setter(TemplatedControl.BackgroundProperty, new SolidColorBrush(Color.Parse("#4D6595"))) } });
    }
    private Control BuildLayerFilters()
    {
        var row = new Grid { ColumnDefinitions = new("94,*,24"), Margin = new Thickness(6, 4), Tag = "layer-filter-row" };
        _layerKind.Width = double.NaN; _layerKind.MinWidth = 0; _layerKind.Height = 26; _layerKind.Padding = new Thickness(5, 1); _layerKind.CornerRadius = new CornerRadius(3);
        foreach (var name in new[] { "Kind", "Pixels", "Adjustment", "Text", "Shape", "Groups", "Locked", "Name", "Smart Objects", "Selected Layers" }) _layerKind.Items.Add(Localize.Text(name));
        _layerKind.SelectedIndex = 0; row.Children.Add(_layerKind);
        var filterArea = new Grid { Margin = new Thickness(4, 0) }; Grid.SetColumn(filterArea, 1); row.Children.Add(filterArea);
        var icons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var (kind, icon, title) in new[] { (1, "transparency", "Pixels"), (2, "adjust", "Adjustment"), (3, "Type", "Text"), (4, "Shape", "Shape"), (8, "add", "Smart Objects") })
        {
            var button = new ToggleButton { Content = new PanelGlyph(icon) { Width = 18, Height = 18 }, Width = 25, Height = 26, MinWidth = 0, MinHeight = 0, Padding = new Thickness(3), Background = Brushes.Transparent, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(2), Tag = "filter-kind-" + kind };
            ToolTip.SetTip(button, Localize.Text(title)); Avalonia.Automation.AutomationProperties.SetName(button, Localize.Text(title));
            button.Click += (_, _) =>
            {
                if (_layerKind.SelectedIndex is > 0 and not 7) _layerFilterKinds.Add(_layerKind.SelectedIndex);
                _syncingLayerFilter = true; _layerKind.SelectedIndex = 0; _syncingLayerFilter = false;
                if (button.IsChecked == true) _layerFilterKinds.Add(kind); else _layerFilterKinds.Remove(kind);
                foreach (var (id, toggle) in _layerFilterButtons) toggle.IsChecked = _layerFilterKinds.Contains(id);
                if (_document is { } doc) ShowLayers(doc);
            };
            _layerFilterButtons[kind] = button; icons.Children.Add(button);
        }
        filterArea.Children.Add(icons); _layerSearch.Width = double.NaN; _layerSearch.MinWidth = 0; _layerSearch.IsVisible = false; filterArea.Children.Add(_layerSearch);
        var enabled = new ToggleButton { Content = new FilterSwitch(), IsChecked = true, Width = 24, Height = 26, MinWidth = 0, Padding = new Thickness(4), BorderThickness = new Thickness(0), Background = Brushes.Transparent, Tag = "layer-filter-enabled" };
        enabled.Template = new Avalonia.Controls.Templates.FuncControlTemplate<ToggleButton>((owner, _) => new Avalonia.Controls.Presenters.ContentPresenter { Content = owner.Content, Padding = owner.Padding });
        ToolTip.SetTip(enabled, Localize.Text("Layer Filtering"));
        enabled.Click += (_, _) => { _layerFiltering = enabled.IsChecked == true; filterArea.IsEnabled = _layerKind.IsEnabled = _layerFiltering; ((FilterSwitch)enabled.Content!).Enabled = _layerFiltering; ((Control)enabled.Content!).InvalidateVisual(); if (_document is { } doc) ShowLayers(doc); };
        Grid.SetColumn(enabled, 2); row.Children.Add(enabled);
        _layerSearch.TextChanged += (_, _) => { if (_document is { } doc) ShowLayers(doc); };
        _layerKind.SelectionChanged += (_, _) =>
        {
            if (_syncingLayerFilter) return;
            _layerFilterKinds.Clear(); foreach (var (kind, button) in _layerFilterButtons) button.IsChecked = kind == _layerKind.SelectedIndex;
            icons.IsVisible = _layerKind.SelectedIndex != 7; _layerSearch.IsVisible = !icons.IsVisible;
            if (_layerKind.SelectedIndex != 7) _layerSearch.Text = "";
            if (_document is { } doc) ShowLayers(doc);
        };
        return row;
    }
    private Control BuildLayerOpacityRow()
    {
        var row = new Grid { ColumnDefinitions = new("*,Auto,86"), Margin = new Thickness(6, 4), Tag = "layer-appearance-row" };
        _blend.Width = double.NaN; _blend.HorizontalAlignment = HorizontalAlignment.Stretch; _blend.MinWidth = 70; _blend.Height = 26; _blend.CornerRadius = new CornerRadius(3); row.Children.Add(_blend);
        var label = new TextBlock { Text = Localize.Text("Opacity") + ":", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) }; Grid.SetColumn(label, 1); row.Children.Add(label);
        var value = new Grid { ColumnDefinitions = new("*,20") }; value.Children.Add(_opacityValue);
        var arrow = new Button { Content = "⌄", Width = 20, Height = 26, MinWidth = 0, Padding = new Thickness(0), CornerRadius = new CornerRadius(2), Tag = "layer-opacity-slider" };
        ToolTip.SetTip(arrow, Localize.Text("Opacity")); Grid.SetColumn(arrow, 1); value.Children.Add(arrow);
        _opacity.Width = 150; _opacityPopup = new Popup { PlacementTarget = arrow, Placement = PlacementMode.BottomEdgeAlignedRight, IsLightDismissEnabled = true, Child = new Border { Background = Skin.ChromeBrush, BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Padding = new Thickness(10), Child = _opacity } };
        value.Children.Add(_opacityPopup); arrow.Click += (_, _) => _opacityPopup.IsOpen = !_opacityPopup.IsOpen;
        _opacityValue.ValueChanged += (_, _) => { if (!_showingAppearance && _opacityValue.Value is { } v) _opacity.Value = (double)v; };
        _opacity.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) _opacityValue.Value = (decimal)_opacity.Value; };
        Grid.SetColumn(value, 2); row.Children.Add(value); return row;
    }
    private sealed class FilterSwitch : Control
    {
        internal bool Enabled = true;
        public override void Render(DrawingContext context)
        {
            context.DrawRectangle(Skin.SurfaceControlHoverBrush, null, new Rect(4, 1, 7, 20), 4, 4);
            context.DrawEllipse(Enabled ? Skin.LabelBrush : Skin.SecondaryBrush, null, new Point(7.5, Enabled ? 6 : 16), 4, 4);
        }
    }
}
