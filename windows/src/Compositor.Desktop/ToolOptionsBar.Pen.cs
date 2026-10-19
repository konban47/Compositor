using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Compositor.Core.Document;

namespace Compositor.Desktop;

internal sealed partial class ToolOptionsBar
{
    internal event Action<string, Control>? PenAsked;
    internal event Action? PenModeChanging;
    internal Func<bool>? HasPenPath, CanPenMask;
    private readonly List<Button> _penPathButtons = [];
    private Button? _penMaskButton;
    private void BuildPenOptions()
    {
        var p = _options.Pen;
        var mode = new ComboBox { Width = 105, Height = 26, Tag = "pen-mode", ItemsSource = new[] {
            new ComboBoxItem { Content = Localize.Text("Shape") }, new ComboBoxItem { Content = Localize.Text("Path") },
            new ComboBoxItem { Content = Localize.Text("Pixels"), IsEnabled = false } } };
        StyleOptionCombo(mode); Cell("penMode", mode);
        mode.SelectionChanged += (_, _) => { if (!_loading && mode.SelectedIndex is 0 or 1 && (int)p.Output != mode.SelectedIndex) { PenModeChanging?.Invoke(); p.Output = (PenOutput)mode.SelectedIndex; Changed?.Invoke(); } };
        _optionRefreshers.Add(() => mode.SelectedIndex = (int)p.Output);
        Button Action(string group, string title, string? icon = null)
        {
            var button = icon is null ? new Button { Content = Localize.Text(title), Height = 26, Padding = new Thickness(8, 2) } : IconButton(icon, title, null);
            button.Tag = title; button.Click += (_, _) => PenAsked?.Invoke(title, button); ToolTip.SetTip(button, Localize.Text(title)); Cell(group, button); return button;
        }
        OptionLabel("penCreate", "Make:");
        _penPathButtons.Add(Action("penCreate", "Selection…"));
        _penMaskButton = Action("penCreate", "Mask"); _penPathButtons.Add(_penMaskButton);
        _penPathButtons.Add(Action("penCreate", "Shape"));
        Action("penShape", "Fill Color"); OptionNumber("penShape", "Stroke:", () => p.Stroke, v => p.Stroke = v, 0, 1000);
        var operations = Action("penOperations", "Path Operations", "selection"); operations.Content = new SelectionGlyph(1) { Width = 23, Height = 23 };
        Action("penOperations", "Path Alignment", "align-left"); var arrangement = Action("penOperations", "Path Arrangement", "layers"); arrangement.Content = new SliceOrderIcon("front") { Width = 23, Height = 23 }; Action("penOperations", "Path Options", "settings");
        OptionCheck("penAuto", "Auto Add/Delete", () => p.AutoAddDelete, v => p.AutoAddDelete = v);
        OptionCheck("penMagnetic", "Magnetic", () => p.Magnetic, v => p.Magnetic = v);
        var edges = OptionCheck("penEdges", "Align Edges", () => p.AlignEdges, v => p.AlignEdges = v);
        _optionRefreshers.Add(() => edges.IsEnabled = p.Output == PenOutput.Shape);
    }
    private void ShowPenOptions(Tool tool, bool hasDocument)
    {
        var pen = ToolCatalog.IsPen(tool);
        On("pen", false); if (pen) On("linewidth", false);
        On("penMode", pen); On("penCreate", pen && _options.Pen.Output == PenOutput.Path);
        On("penShape", pen && _options.Pen.Output == PenOutput.Shape);
        On("penOperations", pen); On("penAuto", tool == Tool.Pen); On("penMagnetic", tool == Tool.FreeformPen); On("penEdges", pen);
        RefreshPenAvailability();
    }
    internal void RefreshPenAvailability()
    {
        foreach (var button in _penPathButtons) button.IsEnabled = HasPenPath?.Invoke() == true;
        if (_penMaskButton is not null) _penMaskButton.IsEnabled &= CanPenMask?.Invoke() == true;
    }
}
