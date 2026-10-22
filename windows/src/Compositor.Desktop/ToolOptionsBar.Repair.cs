using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Compositor.Core.Document;
namespace Compositor.Desktop;
internal sealed partial class ToolOptionsBar
{
    private void BuildRepairOptions()
    {
        var p = Professional;
        var tip = RetouchButton("healingOptions", "Sampled Healing Brush Settings"); tip.Width = 55;
        _optionRefreshers.Add(() => tip.Content = new BrushTipPreview(_options.Brush.Diameter) { Width = 40, Height = 26 });
        RetouchButton("healingOptions", "Clone Source", "Clone");
        OptionChoice("healingOptions", "Mode:", Enum.GetNames<HealingBlend>(), () => (int)_options.Brush.HealingBlend, i => _options.Brush = _options.Brush with { HealingBlend = (HealingBlend)i }, 110);
        OptionLabel("healingOptions", "Source:");
        foreach (var pattern in new[] { false, true })
        {
            var button = new Button { Content = Localize.Text(pattern ? "Pattern" : "Sampled"), Height = 26, Padding = new Thickness(6, 2) };
            button.Click += (_, _) => { _options.Brush = _options.Brush with { HealingPattern = pattern }; Changed?.Invoke(); Refresh(); };
            _optionRefreshers.Add(() => button.Background = _options.Brush.HealingPattern == pattern ? Skin.SurfaceDarkBrush : Brushes.Transparent); Cell("healingOptions", button);
        }
        var choose = RetouchButton("healingOptions", "Healing Pattern…", "image");
        _optionRefreshers.Add(() => choose.IsEnabled = _options.Brush.HealingPattern);
        OptionCheck("healingOptions", "Aligned", () => _options.Brush.CloneAligned, v => _options.Brush = _options.Brush with { CloneAligned = v });
        OptionCheck("healingOptions", "Use Legacy", () => _options.Brush.HealingLegacy, v => _options.Brush = _options.Brush with { HealingLegacy = v });
        OptionChoice("healingOptions", "Sample:", ["Current Layer", "Current & Below", "All Layers"], () => _options.Brush.CloneAllLayers ? 2 : _options.Brush.CloneScope, v => _options.Brush = _options.Brush with { CloneScope = v, CloneAllLayers = v == 2 }, 140);
        var ignore = RetouchButton("healingOptions", "Ignore Adjustment Layers", "eye");
        _optionRefreshers.Add(() => ignore.Background = _options.Brush.CloneIgnoreAdjustments ? Skin.SurfaceDarkBrush : Brushes.Transparent);
        OptionNumber("healingOptions", "Angle:", () => _options.Brush.Angle, v => _options.Brush = _options.Brush with { Angle = v }, -180, 180);
        var pressure = RetouchButton("healingOptions", "Sampled Healing Size Pressure", "pressure");
        _optionRefreshers.Add(() => pressure.Background = _options.Brush.PressureSize ? Skin.SurfaceDarkBrush : Brushes.Transparent);
        OptionChoice("healingOptions", "Diffusion:", ["1", "2", "3", "4", "5", "6", "7"], () => _options.Brush.HealingDiffusion - 1, v => _options.Brush = _options.Brush with { HealingDiffusion = v + 1 }, 55);
        foreach (var group in new[] { "patchOptions", "moveContentOptions" })
        foreach (var mode in Enum.GetValues<Compositor.Core.Document.SelectionMode>())
        {
            var button = new Button { Content = new SelectionGlyph((int)mode) { Width = 23, Height = 23 }, Width = 29, Height = 28, Padding = new Thickness(2), Tag = group + "-" + mode };
            ToolTip.SetTip(button, Localize.Text(new[] { "New Selection", "Add to Selection", "Subtract from Selection", "Intersect with Selection" }[(int)mode]));
            button.Click += (_, _) => { _options.Selection.Mode = mode; Changed?.Invoke(); Refresh(); };
            _optionRefreshers.Add(() => button.Background = _options.Selection.Mode == mode ? Skin.SurfaceDarkBrush : Brushes.Transparent); Cell(group, button);
        }
        OptionChoice("patchOptions", "Patch:", ["Normal", "Content-Aware"], () => p.PatchContentAware ? 1 : 0, v => p.PatchContentAware = v == 1, 125);
        OptionChoice("patchOptions", "", ["Source", "Destination"], () => p.PatchDestination ? 1 : 0, v => p.PatchDestination = v == 1);
        OptionCheck("patchOptions", "Transparent", () => p.PatchTransparent, v => p.PatchTransparent = v);
        RetouchButton("patchOptions", "Use Pattern"); RetouchButton("patchOptions", "Patch Pattern…", "image");
        OptionChoice("patchOptions", "Diffusion:", ["1", "2", "3", "4", "5", "6", "7"], () => p.PatchDiffusion - 1, v => p.PatchDiffusion = v + 1, 55);
        OptionChoice("moveContentOptions", "Mode:", ["Move", "Extend"], () => p.ExtendMove ? 1 : 0, v => p.ExtendMove = v == 1, 85);
        SliderField("moveContentOptions", "Structure:", "Content Move Structure", () => p.MoveStructure, n => p.MoveStructure = (int)n, 1, 7);
        SliderField("moveContentOptions", "Color:", "Content Move Color", () => p.MoveColor, n => p.MoveColor = (int)n, 0, 10);
        OptionCheck("moveContentOptions", "Sample All Layers", () => p.MoveAllLayers, v => p.MoveAllLayers = v);
        OptionCheck("moveContentOptions", "Transform On Drop", () => p.MoveTransform, v => p.MoveTransform = v);
        SliderField("redEyeOptions", "Pupil Size %", "Pupil Size Slider", () => p.Pupil * 100, n => p.Pupil = n / 100, 5, 100);
        SliderField("redEyeOptions", "Darken %", "Darken Slider", () => p.Darken * 100, n => p.Darken = n / 100, 0, 100);
    }
    private void SliderField(string group, string label, string action, Func<double> get, Action<double> set, double min, double max)
    {
        OptionNumber(group, label, get, set, min, max, 60); var button = RetouchButton(group, action); button.Content = "⌄"; button.Width = 23;
    }
    private void ShowRepairOptions(Tool tool)
    {
        On("healingOptions", tool == Tool.HealingBrush); On("patchOptions", tool == Tool.Patch); On("moveContentOptions", tool == Tool.ContentMove); On("redEyeOptions", tool == Tool.RedEye);
        if (tool == Tool.HealingBrush) foreach (var group in new[] { "brush", "pressure", "opacity", "clone", "extendedBrush" }) On(group, false);
        if (tool is Tool.Patch or Tool.ContentMove or Tool.RedEye) foreach (var group in new[] { "patch", "contentMove", "redEye", "proSample", "proReadout" }) On(group, false);
    }
}
