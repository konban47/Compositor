using Avalonia;
using Avalonia.Controls;
using Compositor.Core.Document;

namespace Compositor.Desktop;

internal sealed partial class ToolOptionsBar
{
    internal event Action? PatternAsked, FinishPathAsked, CancelPathAsked, NewAdjustmentAsked;
    internal event Action<string, double>? AdjustmentBrushChanged;
    private string _adjustKind = "Exposure";
    private double _adjustAmount = 1;
    private void BuildExtended()
    {
        void Number(string group, string title, decimal initial, decimal min, decimal max, Action<double> changed)
        {
            var value = new NumericUpDown { Width = 80, Height = 28, ShowButtonSpinner = false, Minimum = min, Maximum = max, Value = initial, Increment = 1, FormatString = "0.##" };
            ToolTip.SetTip(value, Localize.Text(title));
            Cell(group, new TextBlock { Text = Localize.Text(title), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }); Cell(group, value);
            value.ValueChanged += (_, _) => { if (!_loading && value.Value is { } n) { changed((double)n); Changed?.Invoke(); } };
        }
        void Choice(string group, string title, string[] labels, Action<int> changed)
        {
            var choice = new ComboBox { ItemsSource = labels.Select(Localize.Text).ToArray(), SelectedIndex = 0, Height = 28, MinWidth = 115 };
            ToolTip.SetTip(choice, Localize.Text(title)); Cell(group, choice);
            choice.SelectionChanged += (_, _) => { if (!_loading && choice.SelectedIndex >= 0) { changed(choice.SelectedIndex); Changed?.Invoke(); } };
        }
        void Action(string group, string label, System.Action callback)
        { var b = new Button { Content = Localize.Text(label), Height = 28, Padding = new Thickness(8, 2) }; b.Click += (_, _) => callback(); Cell(group, b); }
        Number("retouch", "Strength", 50, 0, 100, n => _options.Brush = _options.Brush with { Strength = n / 100 });
        Choice("tone", "Range", ["Midtones", "Shadows", "Highlights"], i => _options.Brush = _options.Brush with { ToneRange = i == 0 ? 1 : i == 1 ? 0 : 2 });
        Choice("sponge", "Mode", ["Desaturate", "Saturate"], i => _options.Brush = _options.Brush with { Saturate = i == 1 });
        Number("eraseColor", "Tolerance", 32, 0, 255, n => { _options.Brush = _options.Brush with { Tolerance = n }; _options.Wand = _options.Wand with { Tolerance = (int)n }; });
        Choice("backgroundErase", "Sampling", ["Sampling: Once", "Sampling: Continuous"], i => _options.Brush = _options.Brush with { ContinuousSampling = i == 1 });
        var protect = new CheckBox { Content = Localize.Text("Protect Foreground Color") }; protect.IsCheckedChanged += (_, _) => { _options.Brush = _options.Brush with { ProtectForeground = protect.IsChecked == true }; Changed?.Invoke(); }; Cell("backgroundErase", protect);
        Choice("bucket", "Limits", ["Contiguous", "Discontiguous"], i => _options.Wand = _options.Wand with { Contiguous = i == 0 });
        Choice("bucket", "Sample", ["Sample: This Layer", "Sample: All Layers"], i => _options.WandAllLayers = i == 1);
        Action("pattern", "Load Pattern…", () => PatternAsked?.Invoke());
        Action("pattern", "Reset Pattern", () => { _options.Brush.Pattern?.Dispose(); _options.Brush = _options.Brush with { Pattern = null }; Changed?.Invoke(); });
        Number("pattern", "Pattern Scale %", 100, 1, 1000, n => _options.Brush = _options.Brush with { PatternScale = n / 100 });
        Choice("art", "Style", ["Dab", "Loose Curl", "Tight Curl"], i => _options.Brush = _options.Brush with { ArtStyle = i });
        Choice("adjustmentBrush", "Adjustment", ["Exposure", "Saturation"], i => { _adjustKind = i == 0 ? "Exposure" : "Saturation"; AdjustmentBrushChanged?.Invoke(_adjustKind, _adjustAmount); });
        Number("adjustmentBrush", "Amount", 1, -4, 4, n => { _adjustAmount = n; AdjustmentBrushChanged?.Invoke(_adjustKind, _adjustAmount); });
        Action("adjustmentBrush", "New Adjustment", () => NewAdjustmentAsked?.Invoke());
        Action("pen", "Finish Path (Enter)", () => FinishPathAsked?.Invoke()); Action("pen", "Cancel Path (Esc)", () => CancelPathAsked?.Invoke());
    }
    private void ShowExtended(Tool tool)
    {
        On("retouch", tool is Tool.Sharpen or Tool.Dodge or Tool.Burn or Tool.Sponge);
        On("tone", tool is Tool.Dodge or Tool.Burn); On("sponge", tool == Tool.Sponge);
        On("eraseColor", tool is Tool.BackgroundEraser or Tool.MagicEraser or Tool.Bucket);
        On("backgroundErase", tool == Tool.BackgroundEraser); On("bucket", tool is Tool.Bucket or Tool.MagicEraser);
        On("pattern", tool == Tool.PatternStamp); On("art", tool == Tool.ArtHistory); On("adjustmentBrush", tool == Tool.AdjustmentBrush);
        On("pen", ToolCatalog.IsPen(tool));
    }
}
