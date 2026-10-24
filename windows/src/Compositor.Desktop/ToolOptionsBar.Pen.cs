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
    internal Func<Compositor.Core.Format.LayerShapeStyle?>? SelectedPathStyle;
    internal Func<(double Width, double Height)>? SelectedPathSize;
    internal event Action<bool, double>? PathSizeRequested;
    internal event Action<double, string?>? PathStrokeRequested;
    private void BuildPenOptions()
    {
        var p = _options.Pen;
        OptionChoice("pathSelectScope", "Select:", ["Active Layers", "All Layers"], () => p.AllLayers ? 1 : 0, n => p.AllLayers = n == 1, 130);
        Button PathAction(string group, string title, string? icon = null) { var button = icon is null ? new Button { Content = Localize.Text(title), Height = 26 } : IconButton(icon, title, null); button.Tag = title; button.Click += (_, _) => PenAsked?.Invoke(title, button); Cell(group, button); return button; }
        OptionLabel("pathAppearance", "Fill:"); var fill = PathAction("pathAppearance", "Path Fill Color"); fill.Width = 38; fill.Padding = new Thickness(4);
        OptionLabel("pathAppearance", "Stroke:"); var stroke = PathAction("pathAppearance", "Path Stroke Color"); stroke.Width = 38; stroke.Padding = new Thickness(4);
        _optionRefreshers.Add(() => { var s = SelectedPathStyle?.Invoke(); fill.Content = new Border { Height = 16, Width = 24, Background = s?.FillEnabled == false ? Avalonia.Media.Brushes.Transparent : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb((byte)((s?.Red ?? 0) * 255), (byte)((s?.Green ?? 0) * 255), (byte)((s?.Blue ?? 0) * 255))), BorderBrush = Skin.LabelBrush, BorderThickness = new Thickness(1) }; stroke.Content = new Border { Height = 10, Width = 24, Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromUInt32(s?.StrokeColor ?? 0xff000000)), BorderBrush = Skin.LabelBrush, BorderThickness = new Thickness(1) }; });
        OptionNumber("pathAppearance", "", () => SelectedPathStyle?.Invoke()?.StrokeWidth ?? SelectedPathStyle?.Invoke()?.LineWidth ?? 0, n => PathStrokeRequested?.Invoke(n, null), 0, 1000, 64);
        var styles = new[] { "Solid", "Dotted", "Dashed", "Long Dash" };
        OptionChoice("pathAppearance", "", styles, () => Math.Max(0, Array.IndexOf(styles, SelectedPathStyle?.Invoke()?.StrokePattern ?? "Solid")), i => PathStrokeRequested?.Invoke(SelectedPathStyle?.Invoke()?.StrokeWidth ?? 1, styles[i]), 100);
        OptionNumber("pathDimensions", "W:", () => SelectedPathSize?.Invoke().Width ?? 0, n => PathSizeRequested?.Invoke(true, n), 1, 30000, 64);
        var link = PathAction("pathDimensions", "Constrain Proportions", "link"); link.Click += (_, _) => { p.LinkDimensions = !p.LinkDimensions; Changed?.Invoke(); }; _optionRefreshers.Add(() => link.Background = p.LinkDimensions ? Skin.SurfaceDarkBrush : Avalonia.Media.Brushes.Transparent);
        OptionNumber("pathDimensions", "H:", () => SelectedPathSize?.Invoke().Height ?? 0, n => PathSizeRequested?.Invoke(false, n), 1, 30000, 64);
        PathAction("pathMenus", "Path Operations", "selection").Content = new SelectionGlyph(1) { Width=23, Height=23 }; PathAction("pathMenus", "Path Alignment", "align-left"); PathAction("pathMenus", "Path Arrangement", "layers").Content = new SliceOrderIcon("front") { Width=23, Height=23 };
        OptionCheck("pathEdges", "Align Edges", () => p.AlignEdges, v => p.AlignEdges = v);
        PathAction("pathOptions", "Path Options", "settings"); OptionCheck("pathOptions", "Constrain Path Dragging", () => p.ConstrainDragging, v => p.ConstrainDragging = v);
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
        var path = tool is Tool.Path or Tool.PathSelection;
        foreach (var group in new[] { "pathSelectScope", "pathAppearance", "pathDimensions", "pathMenus", "pathEdges", "pathOptions" }) On(group, path);
        foreach (var group in new[] { "pathAppearance", "pathDimensions", "pathEdges" }) if (_named.TryGetValue(group, out var cells)) foreach (var cell in cells) cell.IsEnabled = hasDocument && (group == "pathDimensions" ? HasPenPath?.Invoke() == true : SelectedPathStyle?.Invoke() is not null);
        if (path) On("path", false);
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
