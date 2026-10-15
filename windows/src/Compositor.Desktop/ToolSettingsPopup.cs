using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace Compositor.Desktop;

internal sealed class ToolSettingsPopup : StackPanel
{
    internal Action? Changed;
    internal ToolSettingsPopup() { Spacing = 8; Margin = new Thickness(12); MinWidth = 245; }
    internal void Heading(string text) => Children.Add(new TextBlock { Text = Localize.Text(text), Foreground = Skin.SecondaryBrush });
    internal void Check(string text, bool value, Action<bool> set)
    {
        var check = new CheckBox { Content = Localize.Text(text), IsChecked = value, MinHeight = 24 };
        check.IsCheckedChanged += (_, _) => { set(check.IsChecked == true); Changed?.Invoke(); }; Children.Add(check);
    }
    internal void Number(string label, double value, double min, double max, Action<double> set, bool slider = false)
    {
        var field = new NumericUpDown { Value = (decimal)value, Minimum = (decimal)min, Maximum = (decimal)max, ShowButtonSpinner = false, Width = 90, FormatString = "0.##" };
        var row = new DockPanel(); row.Children.Add(new TextBlock { Text = Localize.Text(label), VerticalAlignment = VerticalAlignment.Center, MinWidth = 140 }); row.Children.Add(field); Children.Add(row);
        field.ValueChanged += (_, _) => { if (field.Value is { } v) { set((double)v); Changed?.Invoke(); } };
        if (slider) { var range = new Slider { Minimum = min, Maximum = max, Value = value }; range.ValueChanged += (_, _) => field.Value = (decimal)range.Value; field.ValueChanged += (_, _) => { if (field.Value is { } v) range.Value = (double)v; }; Children.Add(range); }
    }
    internal void Choice(string text, string[] values, int value, Action<int> set)
    {
        var combo = new ComboBox { ItemsSource = values.Select(Localize.Text).ToArray(), SelectedIndex = value, HorizontalAlignment = HorizontalAlignment.Stretch };
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) { set(combo.SelectedIndex); Changed?.Invoke(); } }; Heading(text); Children.Add(combo);
    }
    internal void Show(Control anchor) => new Flyout { Content = this, Placement = PlacementMode.BottomEdgeAlignedLeft }.ShowAt(anchor);
}
