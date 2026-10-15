using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Document;
using SelectionMode = Compositor.Core.Document.SelectionMode;

namespace Compositor.Desktop;

internal sealed partial class ToolOptionsBar
{
    private readonly List<Action> _optionRefreshers = [];
    private Tool _shownTool;
    internal event Action<string, Control>? SelectionAsked;
    private void OptionLabel(string group, string label) => Cell(group, new TextBlock { Text = Localize.Text(label), VerticalAlignment = VerticalAlignment.Center });
    private NumericUpDown OptionNumber(string group, string label, Func<double> get, Action<double> set, double min = 0, double max = 30000, double width = 64)
    {
        if (label.Length > 0) OptionLabel(group, label);
        var field = new NumericUpDown { Width = width, Height = 26, Minimum = (decimal)min, Maximum = (decimal)max, ShowButtonSpinner = false, FormatString = "0.##", Tag = group + "/" + label };
        field.ValueChanged += (_, _) => { if (!_loading && field.Value is { } value) { set((double)value); Changed?.Invoke(); } };
        _optionRefreshers.Add(() => field.Value = (decimal)get()); Cell(group, field); return field;
    }
    private CheckBox OptionCheck(string group, string label, Func<bool> get, Action<bool> set)
    {
        var field = new CheckBox { Content = Localize.Text(label), Height = 26, MinHeight = 0, Tag = group + "/" + label };
        field.IsCheckedChanged += (_, _) => { if (!_loading) { set(field.IsChecked == true); Changed?.Invoke(); } };
        _optionRefreshers.Add(() => field.IsChecked = get()); Cell(group, field); return field;
    }
    private ComboBox OptionChoice(string group, string label, string[] values, Func<int> get, Action<int> set, double width = 110)
    {
        if (label.Length > 0) OptionLabel(group, label);
        var field = new ComboBox { ItemsSource = values.Select(Localize.Text).ToArray(), Width = width, Height = 26, Tag = group + "/" + label }; StyleOptionCombo(field);
        field.SelectionChanged += (_, _) => { if (!_loading && field.SelectedIndex >= 0) { set(field.SelectedIndex); Changed?.Invoke(); } };
        _optionRefreshers.Add(() => field.SelectedIndex = get()); Cell(group, field); return field;
    }
    private Button OptionAction(string group, string title, string? icon = null)
    {
        var button = icon is null ? new Button { Content = Localize.Text(title), Height = 26, Padding = new Thickness(8, 2) } : IconButton(icon, title, null);
        button.Tag = title; button.Click += (_, _) => SelectionAsked?.Invoke(title, button); Cell(group, button); return button;
    }
    private void BuildSelectionOptions()
    {
        var smooth = OptionCheck("shapeSmoothing", "Smooth Corners", () => _options.ShapeSmoothing, v => _options.ShapeSmoothing = v);
        _optionRefreshers.Add(() => smooth.IsEnabled = _options.ShapeCornerRadius > 0);
        var s = _options.Selection;
        foreach (var (name, mode) in new[] { ("New Selection", SelectionMode.Replace), ("Add to Selection", SelectionMode.Add), ("Subtract from Selection", SelectionMode.Subtract), ("Intersect with Selection", SelectionMode.Intersect) })
        {
            var b = IconButton("", name, () => { s.Mode = mode; Professional.SelectionMode = (SelectionPaintMode)(int)mode; Changed?.Invoke(); });
            b.Content = new SelectionGlyph((int)mode) { Width = 23, Height = 23 };
            _optionRefreshers.Add(() => { b.Background = s.Mode == mode ? Skin.SurfaceDarkBrush : Brushes.Transparent; b.IsVisible = SelectionTool(_shownTool) && _shownTool != Tool.SelectionBrush && (_shownTool != Tool.QuickSelection || mode != SelectionMode.Intersect); }); Cell("selectionModes", b);
        }
        foreach (var (title, mode) in new[] { ("Add", SelectionPaintMode.Add), ("Subtract", SelectionPaintMode.Subtract) })
        {
            var b = new Button { Content = Localize.Text(title), Height = 26, Padding = new Thickness(6, 1) }; ToolTip.SetTip(b, Localize.Text(title));
            b.Click += (_, _) => { Professional.SelectionMode = mode; Changed?.Invoke(); };
            _optionRefreshers.Add(() => b.Background = Professional.SelectionMode == mode ? Skin.SurfaceDarkBrush : Brushes.Transparent); Cell("selectionPaintModes", b);
        }
        OptionNumber("selectionFeather", "Feather:", () => s.Feather, n => s.Feather = n, 0, 250);
        var aa = OptionCheck("selectionAA", "Anti-alias", () => _options.SelectionAntialiased, v => _options.SelectionAntialiased = v);
        _optionRefreshers.Add(() => { aa.IsEnabled = _shownTool is not (Tool.Marquee or Tool.SingleRow or Tool.SingleColumn); if (!aa.IsEnabled) aa.IsChecked = false; });
        OptionChoice("selectionSize", "Style:", ["Normal", "Fixed Ratio", "Fixed Size"], () => s.Style, v => s.Style = v);
        var w = OptionNumber("selectionSize", "Width:", () => s.Width, v => s.Width = v, 1); OptionAction("selectionSize", "Swap Selection Dimensions", "swap");
        var h = OptionNumber("selectionSize", "Height:", () => s.Height, v => s.Height = v, 1);
        _optionRefreshers.Add(() => w.IsEnabled = h.IsEnabled = s.Style != 0 && _shownTool is not (Tool.SingleRow or Tool.SingleColumn));
        OptionNumber("selectionOpacity", "Opacity:", () => _options.Brush.Opacity * 100, v => _options.Brush = _options.Brush with { Opacity = v / 100 }, 0, 100);
        var brush = OptionAction("selectionBrushTip", "Selection Brush Settings", "Brush");
        _optionRefreshers.Add(() => brush.Content = new BrushTipPreview(_options.Brush.Diameter) { Width = 45, Height = 26 }); brush.Width = 55;
        OptionAction("selectionOverlay", "Selection Overlay Options", "settings");
        OptionNumber("quickAngle", "Angle:", () => s.BrushAngle, v => s.BrushAngle = v, -180, 180);
        OptionCheck("quickSample", "Sample All Layers", () => Professional.SampleAll, v => Professional.SampleAll = v);
        OptionCheck("quickSample", "Enhance Edge", () => s.EnhanceEdge, v => s.EnhanceEdge = v);
        OptionNumber("magnetOptions", "Width:", () => Professional.MagneticWidth, v => Professional.MagneticWidth = v, 1, 64);
        OptionNumber("magnetOptions", "Contrast:", () => Professional.MagneticContrast / 2.55, v => Professional.MagneticContrast = v * 2.55, 0, 100);
        OptionNumber("magnetOptions", "Frequency:", () => Professional.MagneticFrequency, v => Professional.MagneticFrequency = v, 1, 100);
        OptionCheck("magnetOptions", "Pen Pressure", () => s.MagneticPressure, v => s.MagneticPressure = v);
        var samples = new[] { 0, 1, 2, 5, 15, 25, 50 };
        OptionChoice("wandSample", "Sample Size:", ["Point Sample", "3 × 3 Average", "5 × 5 Average", "11 × 11 Average", "31 × 31 Average", "51 × 51 Average", "101 × 101 Average"], () => Math.Max(0, Array.IndexOf(samples, _options.Wand.Radius)), i => _options.Wand = _options.Wand with { Radius = samples[i] }, 135);
        OptionNumber("wandTolerance", "Tolerance:", () => _options.Wand.Tolerance, v => _options.Wand = _options.Wand with { Tolerance = (int)v }, 0, 255);
        OptionCheck("wandConnected", "Contiguous", () => _options.Wand.Contiguous, v => _options.Wand = _options.Wand with { Contiguous = v });
        OptionCheck("allSample", "Sample All Layers", () => _options.WandAllLayers, v => _options.WandAllLayers = v);
        OptionAction("objectFind", "Select People"); OptionAction("objectFind", "Refresh Object Finder", "reset"); OptionAction("objectFind", "Show All Objects", "selection"); OptionAction("objectFind", "Object Finder Settings", "settings");
        OptionChoice("objectShape", "", ["Rectangle", "Lasso"], () => s.ObjectShape, v => s.ObjectShape = v, 95);
        OptionCheck("objectHard", "Hard Edge", () => s.HardEdge, v => s.HardEdge = v);
        OptionAction("selectionSubject", "Select Subject"); OptionAction("selectionSubject", "Subject Processing", "chevron");
        OptionAction("selectionRefine", "Select and Mask…");
    }
    private static bool SelectionTool(Tool tool) => tool is Tool.Marquee or Tool.Ellipse or Tool.SingleRow or Tool.SingleColumn or Tool.SelectionBrush or Tool.Lasso or Tool.Polygon or Tool.MagneticLasso or Tool.Wand or Tool.QuickSelection or Tool.Object;
    private void ShowSelectionOptions(Tool tool)
    {
        if (_shownTool != tool)
        {
            var order = tool switch
            {
                Tool.Wand => new[] { "selectionModes", "wandSample", "wandTolerance", "selectionAA", "wandConnected", "allSample", "selectionSubject", "selectionRefine" },
                Tool.Object => ["selectionModes", "objectFind", "objectShape", "allSample", "objectHard", "selectionSubject", "selectionRefine"],
                _ => ["selectionModes", "selectionPaintModes", "selectionFeather", "selectionAA", "selectionSize", "selectionOpacity", "selectionBrushTip", "selectionOverlay", "quickAngle", "quickSample", "magnetOptions", "selectionSubject", "selectionRefine"],
            };
            foreach (var group in order) if (_named.TryGetValue(group, out var controls)) foreach (var control in controls) { _cells.Children.Remove(control); _cells.Children.Add(control); }
        }
        On("shapeSmoothing", tool == Tool.Shape);
        _shownTool = tool; var selection = SelectionTool(tool);
        if (selection) foreach (var group in new[] { "brush", "opacity", "marqueeShape", "lasso", "wand", "selectionSample", "selectPaint", "proSample", "magnetic", "proApply" }) On(group, false);
        On("selectionModes", selection && tool != Tool.SelectionBrush);
        On("selectionPaintModes", tool == Tool.SelectionBrush);
        On("selectionFeather", tool is Tool.Marquee or Tool.Ellipse or Tool.SingleRow or Tool.SingleColumn or Tool.Lasso or Tool.Polygon or Tool.MagneticLasso);
        On("selectionAA", selection && tool is not (Tool.SelectionBrush or Tool.QuickSelection or Tool.Object));
        On("selectionSize", tool is Tool.Marquee or Tool.Ellipse or Tool.SingleRow or Tool.SingleColumn);
        On("selectionOpacity", tool == Tool.SelectionBrush); On("selectionBrushTip", tool is Tool.SelectionBrush or Tool.QuickSelection);
        On("selectionOverlay", tool == Tool.SelectionBrush); On("quickAngle", tool == Tool.QuickSelection); On("quickSample", tool == Tool.QuickSelection);
        On("magnetOptions", tool == Tool.MagneticLasso); On("wandSample", tool == Tool.Wand); On("wandTolerance", tool == Tool.Wand); On("wandConnected", tool == Tool.Wand);
        On("allSample", tool is Tool.Wand or Tool.Object); On("objectFind", tool == Tool.Object); On("objectShape", tool == Tool.Object); On("objectHard", tool == Tool.Object);
        On("selectionSubject", tool is Tool.Wand or Tool.QuickSelection or Tool.Object); On("selectionRefine", selection && tool != Tool.SelectionBrush);
        foreach (var refresh in _optionRefreshers) refresh();
    }
    private sealed class SelectionGlyph(int mode) : Control
    {
        public override void Render(DrawingContext context)
        {
            var pen = new Pen(Skin.LabelBrush, 1.5); var a = new Rect(3, 3, 12, 12); var b = new Rect(9, 9, 12, 12);
            context.DrawRectangle(mode is 0 or 1 ? Skin.LabelBrush : null, pen, a);
            if (mode != 0) context.DrawRectangle(mode == 1 ? Skin.LabelBrush : Skin.ChromeBrush, pen, b);
            if (mode == 3) context.FillRectangle(Skin.LabelBrush, new Rect(9, 9, 6, 6));
        }
    }
    private sealed class BrushTipPreview(double diameter) : Control
    {
        public override void Render(DrawingContext context)
        {
            context.DrawEllipse(Skin.LabelBrush, null, new Point(16, 8), 7, 7);
            var text = new FormattedText(diameter.ToString("0"), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 10, Skin.LabelBrush);
            context.DrawText(text, new Point(16 - text.Width / 2, 15));
            var pen = new Pen(Skin.SecondaryBrush, 1); context.DrawLine(pen, new Point(34, 10), new Point(38, 14)); context.DrawLine(pen, new Point(38, 14), new Point(42, 10));
        }
    }
}
