using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Compositor.Core.Document;
namespace Compositor.Desktop;
internal sealed partial class ToolOptionsBar
{
    internal event Action<string, Control>? RetouchAsked;
    private readonly Dictionary<string, Button> _retouchButtons = [];
    private Button RetouchButton(string group, string action, string? icon = null)
    {
        var button = icon is null ? new Button { Content = Localize.Text(action), Height = 26, Padding = new Thickness(7, 2) } : IconButton(icon, action, null);
        button.Tag = action; button.Click += (_, _) => RetouchAsked?.Invoke(action, button); _retouchButtons[action] = button; Cell(group, button); return button;
    }
    private void BuildRetouchOptions()
    {
        BuildRepairOptions();
        var p = Professional;
        var tip = RetouchButton("spotOptions", "Healing Brush Settings"); tip.Width = 60;
        _optionRefreshers.Add(() => tip.Content = new BrushTipPreview(_options.Brush.Diameter) { Width = 45, Height = 26 });
        OptionChoice("spotOptions", "Mode:", Enum.GetNames<HealingBlend>(), () => (int)_options.Brush.HealingBlend, i => _options.Brush = _options.Brush with { HealingBlend = (HealingBlend)i }, 125);
        OptionLabel("spotOptions", "Type:");
        foreach (var mode in Enum.GetValues<HealingMode>())
        {
            var name = mode == HealingMode.ContentAware ? "Content-Aware" : mode == HealingMode.CreateTexture ? "Create Texture" : "Proximity Match";
            var button = new Button { Content = Localize.Text(name), Height = 26, Padding = new Thickness(7, 2) };
            button.Click += (_, _) => { _options.Brush = _options.Brush with { Healing = mode }; Changed?.Invoke(); Refresh(); };
            _optionRefreshers.Add(() => button.Background = _options.Brush.Healing == mode ? Skin.SurfaceDarkBrush : Brushes.Transparent); Cell("spotOptions", button);
        }
        OptionCheck("spotOptions", "Sample All Layers", () => _options.Brush.CloneAllLayers, v => _options.Brush = _options.Brush with { CloneAllLayers = v });
        OptionNumber("spotOptions", "Angle:", () => _options.Brush.Angle, v => _options.Brush = _options.Brush with { Angle = v }, -180, 180);
        var pressure = RetouchButton("spotOptions", "Healing Size Pressure", "pressure");
        _optionRefreshers.Add(() => pressure.Background = _options.Brush.PressureSize ? Skin.SurfaceDarkBrush : Brushes.Transparent);
        foreach (var subtract in new[] { false, true })
        {
            var button = RetouchButton("removeOptions", subtract ? "Subtract Removal Area" : "Add Removal Area"); button.Content = subtract ? "⊖" : "⊕"; button.Width = 30;
            _optionRefreshers.Add(() => button.Background = p.RemoveSubtract == subtract ? Skin.SurfaceDarkBrush : Brushes.Transparent);
        }
        OptionNumber("removeOptions", "Size:", () => _options.Brush.Diameter, v => _options.Brush = _options.Brush with { Diameter = v }, 1, 5000);
        RetouchButton("removeOptions", "Removal Brush Size", "chevron"); var removePressure = RetouchButton("removeOptions", "Removal Size Pressure", "pressure");
        _optionRefreshers.Add(() => removePressure.Background = _options.Brush.PressureSize ? Skin.SurfaceDarkBrush : Brushes.Transparent);
        RetouchButton("removeOptions", "Removal Overlay Options", "settings"); RetouchButton("removeOptions", "Find Distractions");
        OptionChoice("removeOptions", "", ["Auto (May Use Generative AI)", "Generative AI On", "Generative AI Off"], () => p.RemoveMode, v => p.RemoveMode = v, 175);
        OptionCheck("removeOptions", "Sample All Layers", () => p.RemoveAllLayers, v => p.RemoveAllLayers = v);
        OptionCheck("removeOptions", "Remove After Each Stroke", () => p.RemoveEachStroke, v => p.RemoveEachStroke = v);
        OptionCheck("removeOptions", "Create New Layer", () => p.RemoveNewLayer, v => p.RemoveNewLayer = v);
        RetouchButton("removeOptions", "Removal Feedback", "Note"); RetouchButton("removeOptions", "Undo Removal Stroke", "undo");
        RetouchButton("removeOptions", "Cancel Removal", "cancel"); RetouchButton("removeOptions", "Apply Removal", "check");
    }
    private void ShowRetouchOptions(Tool tool)
    {
        ShowRepairOptions(tool);
        On("spotOptions", tool == Tool.Heal); On("removeOptions", tool == Tool.Remove);
        if (tool is Tool.Heal or Tool.Remove) foreach (var group in new[] { "brush", "pressure", "opacity", "heal", "extendedBrush" }) On(group, false);
    }
    internal void RemovalState(bool pending, bool undo, bool busy)
    {
        _retouchButtons["Apply Removal"].IsEnabled = pending && !busy;
        _retouchButtons["Undo Removal Stroke"].IsEnabled = undo && !busy;
        _retouchButtons["Cancel Removal"].IsEnabled = pending && !busy;
    }
}
