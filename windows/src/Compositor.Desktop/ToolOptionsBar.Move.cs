using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Compositor.Desktop;

internal sealed partial class ToolOptionsBar
{
    internal event Action<bool>? TransformVisibilityAsked;
    internal event Action<string>? AlignmentAsked;
    private readonly CheckBox _autoSelect = new() { Content = Localize.Text("Auto-Select:") };
    private readonly CheckBox _transformVisible = new() { Content = Localize.Text("Show Transform Controls") };
    private readonly ComboBox _autoSelectKind = new() { Width = 78 };
    internal static Button IconButton(string icon, string title, Action? action)
    {
        var b = new Button { Content = new EditorIcon(icon) { Width = 20, Height = 20 }, Width = 30, Height = 28, MinWidth = 0, MinHeight = 0,
            Padding = new Thickness(3), Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Tag = title };
        ToolTip.SetTip(b, Localize.Text(title)); if (action is not null) b.Click += (_, _) => action(); return b;
    }
    private void BuildMoveOptions()
    {
        StyleOptionCheck(_autoSelect); StyleOptionCheck(_transformVisible); StyleOptionCombo(_autoSelectKind);
        _autoSelectKind.ItemsSource = new[] { Localize.Text("Layer"), Localize.Text("Group") };
        Cell("moveOptions", _autoSelect); Cell("moveOptions", _autoSelectKind); Cell("moveOptions", _transformVisible);
        _autoSelect.IsCheckedChanged += (_, _) => { if (!_loading) { EditorPreferences.Current.MoveAutoSelect = _autoSelect.IsChecked == true; Changed?.Invoke(); } };
        _autoSelectKind.SelectionChanged += (_, _) => { if (!_loading) { EditorPreferences.Current.MoveSelectGroup = _autoSelectKind.SelectedIndex == 1; Changed?.Invoke(); } };
        _transformVisible.IsCheckedChanged += (_, _) => { if (!_loading) TransformVisibilityAsked?.Invoke(_transformVisible.IsChecked == true); };
        foreach (var operation in new[] { "Align Left", "Align H Center", "Align Right", "Distribute Vertical Spacing", "Align Top", "Align V Center", "Align Bottom", "Distribute Horizontal Spacing" })
        {
            var b = IconButton("", operation, () => AlignmentAsked?.Invoke(operation)); b.Content = new InspectorGlyph(operation) { Width = 20, Height = 20 }; Cell("moveOptions", b);
        }
        var more = IconButton("", "Align and Distribute", null); more.Content = new ChromeGlyph("more") { Width = 24, Height = 24 }; more.Click += (_, _) => MenuAsked?.Invoke("Alignment", more); Cell("moveOptions", more);
        var settings = IconButton("settings", "Move Tool Settings", null); settings.Click += (_, _) => MenuAsked?.Invoke("Move Settings", settings); Cell("moveOptions", settings);
    }
    private void RefreshMoveOptions()
    {
        _autoSelect.IsChecked = EditorPreferences.Current.MoveAutoSelect;
        _autoSelectKind.SelectedIndex = EditorPreferences.Current.MoveSelectGroup ? 1 : 0;
    }
    internal void ShowTransformVisibility(bool value)
    { var loading = _loading; _loading = true; _transformVisible.IsChecked = value; _loading = loading; }
}
