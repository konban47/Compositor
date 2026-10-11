using Avalonia;
using Avalonia.Controls;
using Compositor.Core.Document;

namespace Compositor.Desktop;

internal sealed class ProfessionalOptions
{
    internal SelectionPaintMode SelectionMode = SelectionPaintMode.Add;
    internal double MagneticWidth = 12, MagneticContrast = 20, MagneticFrequency = 4;
    internal bool SampleAll, EllipseFrame, ExtendMove, PatchDestination, CleanAfterStroke;
    internal int CropWidth, CropHeight;
    internal double Pupil = .8, Darken = .5;
    internal string CountGroup = "1";
}
internal sealed partial class ToolOptionsBar
{
    internal ProfessionalOptions Professional { get; } = new();
    internal event Action<string>? ProfessionalAsked;
    private readonly TextBlock _professionalReadout = new() { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, MaxWidth = 600 };
    private readonly Dictionary<string, NumericUpDown> _professionalNumbers = [];
    internal void RefreshProfessional()
    {
        var loading = _loading; _loading = true;
        if (_professionalNumbers.TryGetValue("Load %", out var load)) load.Value = (decimal)(_options.Brush.MixerLoad * 100);
        if (_professionalNumbers.TryGetValue("Mix %", out var mix)) mix.Value = (decimal)(_options.Brush.MixerMix * 100);
        _loading = loading;
    }
    internal void ProfessionalReadout(string value) { _professionalReadout.Text = value; ToolTip.SetTip(_professionalReadout, value); }
    private void BuildProfessional()
    {
        void Number(string group, string title, double value, double min, double max, Action<double> set)
        {
            var field = new NumericUpDown { Value = (decimal)value, Minimum = (decimal)min, Maximum = (decimal)max, Width = 72, Height = 30, ShowButtonSpinner = false, FormatString = "0.##" };
            _professionalNumbers[title] = field;
            ToolTip.SetTip(field, Localize.Text(title)); Cell(group, new TextBlock { Text = Localize.Text(title), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }); Cell(group, field);
            field.ValueChanged += (_, _) => { if (!_loading && field.Value is { } n) { set((double)n); Changed?.Invoke(); } };
        }
        void Choice(string group, string title, string[] values, Action<int> set, int initial = 0)
        {
            var field = new ComboBox { ItemsSource = values.Select(Localize.Text).ToArray(), SelectedIndex = initial, MinWidth = 110, Height = 30 };
            ToolTip.SetTip(field, Localize.Text(title)); Cell(group, field); field.SelectionChanged += (_, _) => { if (!_loading && field.SelectedIndex >= 0) { set(field.SelectedIndex); Changed?.Invoke(); } };
        }
        void Action(string group, string title) { var button = new Button { Content = Localize.Text(title), Height = 30, Padding = new Thickness(8, 3) }; ToolTip.SetTip(button, Localize.Text(title)); button.Click += (_, _) => ProfessionalAsked?.Invoke(title); Cell(group, button); }
        Choice("selectPaint", "Selection Mode", ["New Selection", "Add to Selection", "Subtract from Selection", "Intersect with Selection"], n => Professional.SelectionMode = (SelectionPaintMode)n, 1);
        Choice("proSample", "Sample", ["Sample: This Layer", "Sample: All Layers"], n => Professional.SampleAll = n == 1);
        Number("magnetic", "Width", 12, 1, 64, n => Professional.MagneticWidth = n);
        Number("magnetic", "Edge Contrast", 20, 0, 255, n => Professional.MagneticContrast = n);
        Number("magnetic", "Frequency", 4, 1, 30, n => Professional.MagneticFrequency = n);
        Number("perspective", "Width", 0, 0, 30000, n => Professional.CropWidth = (int)n);
        Number("perspective", "Height", 0, 0, 30000, n => Professional.CropHeight = (int)n);
        Action("proApply", "Apply (Enter)"); Action("proApply", "Cancel (Esc)");
        Choice("frame", "Frame Shape", ["Rectangle", "Ellipse"], n => Professional.EllipseFrame = n == 1);
        Action("frame", "Place Image in Frame…");
        Action("slice", "Slices from Guides"); Action("slice", "Divide Slice…"); Action("slice", "Export Slices…");
        Action("marks", "Edit Annotation…"); Action("marks", "Delete Annotation"); Action("marks", "Clear Tool Annotations");
        Action("ruler", "Straighten Layer");
        var group = new TextBox { Text = "1", Width = 85, Height = 30 }; ToolTip.SetTip(group, Localize.Text("Count Group")); group.TextChanged += (_, _) => Professional.CountGroup = string.IsNullOrWhiteSpace(group.Text) ? "1" : group.Text[..Math.Min(group.Text.Length, 256)]; Cell("count", group);
        Action("count", "Toggle Count Group");
        Choice("patch", "Patch Mode", ["Source", "Destination"], n => Professional.PatchDestination = n == 1);
        Choice("contentMove", "Mode", ["Move", "Extend"], n => Professional.ExtendMove = n == 1);
        Number("redEye", "Pupil Size %", 80, 5, 100, n => Professional.Pupil = n / 100); Number("redEye", "Darken %", 50, 0, 100, n => Professional.Darken = n / 100);
        Choice("replace", "Mode", ["Color", "Hue", "Saturation", "Luminosity"], n => _options.Brush = _options.Brush with { ReplaceMode = n });
        Number("replace", "Tolerance", 32, 0, 255, n => _options.Brush = _options.Brush with { Tolerance = n });
        Number("mixer", "Wet %", 50, 0, 100, n => _options.Brush = _options.Brush with { MixerWet = n / 100 });
        Number("mixer", "Load %", 100, 0, 100, n => _options.Brush = _options.Brush with { MixerLoad = n / 100 });
        Number("mixer", "Mix %", 50, 0, 100, n => _options.Brush = _options.Brush with { MixerMix = n / 100 });
        Number("mixer", "Flow %", 100, 0, 100, n => _options.Brush = _options.Brush with { MixerFlow = n / 100 });
        Action("mixer", "Load Brush"); Action("mixer", "Clean Brush");
        Choice("mixer", "After Each Stroke", ["Keep Brush Load", "Clean After Each Stroke"], n => Professional.CleanAfterStroke = n == 1);
        Cell("proReadout", _professionalReadout);
    }
    private void ShowProfessional(Tool tool)
    {
        On("selectPaint", tool is Tool.SelectionBrush or Tool.QuickSelection or Tool.MagneticLasso);
        On("proSample", tool is Tool.QuickSelection or Tool.MagneticLasso or Tool.ColorSampler or Tool.Patch);
        On("magnetic", tool == Tool.MagneticLasso); On("perspective", tool == Tool.PerspectiveCrop);
        On("proApply", tool is Tool.MagneticLasso or Tool.PerspectiveCrop);
        On("frame", tool == Tool.Frame); On("slice", tool is Tool.Slice or Tool.SliceSelect);
        On("marks", ToolCatalog.IsAnnotation(tool)); On("ruler", tool == Tool.Ruler); On("count", tool == Tool.Count);
        On("patch", tool == Tool.Patch); On("contentMove", tool == Tool.ContentMove); On("redEye", tool == Tool.RedEye);
        On("replace", tool == Tool.ColorReplacement); On("mixer", tool == Tool.MixerBrush);
        On("proReadout", ToolCatalog.IsAnnotation(tool));
    }
}
