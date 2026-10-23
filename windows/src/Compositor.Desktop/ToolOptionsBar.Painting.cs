using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Compositor.Core.Document;
namespace Compositor.Desktop;
internal sealed partial class ToolOptionsBar
{
    internal event Action<string, Control>? PaintingAsked;
    private PaintBrushOptions PaintOptions => _options.Brush.Painting ?? new();
    private void SetPaint(Func<PaintBrushOptions, PaintBrushOptions> change) => _options.Brush = _options.Brush with { Painting = change(PaintOptions) };
    internal static readonly string[] PaintModeNames = ["Normal", "Dissolve", "Behind", "Clear", "Darken", "Multiply", "Color Burn", "Linear Burn", "Darker Color", "Lighten", "Screen", "Color Dodge", "Linear Dodge (Add)", "Lighter Color", "Overlay", "Soft Light", "Hard Light", "Vivid Light", "Linear Light", "Pin Light", "Hard Mix", "Difference", "Exclusion", "Subtract", "Divide", "Hue", "Saturation", "Color", "Luminosity"];
    internal static readonly string[] MixerPresetNames = ["Custom", "Dry", "Dry, Light Load", "Dry, Heavy Load", "Moist", "Moist, Light Mix", "Moist, Heavy Mix", "Wet", "Wet, Light Mix", "Wet, Heavy Mix", "Very Wet", "Very Wet, Light Mix", "Very Wet, Heavy Mix"];
    private Button PaintingButton(string group, string title, string? icon = null)
    {
        var button = icon is null ? new Button { Content = Localize.Text(title), Height = 26, Padding = new Thickness(6, 2) } : IconButton(icon, title, null);
        button.Tag = title; button.Click += (_, _) => PaintingAsked?.Invoke(title, button); Cell(group, button); return button;
    }
    private void PaintToggle(string group, string title, string icon, Func<bool> get, Action<bool> set)
    {
        var button = IconButton(icon, title, () => { set(!get()); Changed?.Invoke(); }); button.Tag = title;
        _optionRefreshers.Add(() => button.Background = get() ? Skin.SurfaceDarkBrush : Brushes.Transparent); Cell(group, button);
    }
    private void PaintSlider(string group, string title, string action, Func<double> get, Action<double> set, double min = 0, double max = 100)
    { OptionNumber(group, title, get, set, min, max, 58); var drop = PaintingButton(group, action); drop.Content = "⌄"; drop.Width = 22; }
    private void BuildPaintingOptions()
    {
        var tip = PaintingButton("paintTip", "Brush Tip Presets"); tip.Width = 58;
        _optionRefreshers.Add(() => tip.Content = new BrushTipPreview(_options.Brush.Diameter) { Width = 42, Height = 26 });
        PaintingButton("paintSettings", "Brush Settings", "Brush");
        OptionChoice("paintBlend", "Mode:", PaintModeNames, () => (int)PaintOptions.Blend, i => SetPaint(p => p with { Blend = (PaintBlend)i }), 150);
        PaintSlider("paintOpacity", "Opacity:", "Paint Opacity Slider", () => _options.Brush.Opacity * 100, n => _options.Brush = _options.Brush with { Opacity = n / 100 });
        PaintToggle("paintOpacity", "Always Use Pressure for Opacity", "pressure", () => _options.Brush.PressureOpacity, v => _options.Brush = _options.Brush with { PressureOpacity = v });
        PaintSlider("paintFlow", "Flow:", "Paint Flow Slider", () => PaintOptions.Flow * 100, n => SetPaint(p => p with { Flow = n / 100 }));
        var load = PaintingButton("paintMixer", "Mixer Load Menu"); load.Width = 40;
        _optionRefreshers.Add(() => { load.Content = "⌄"; load.Background = PaintOptions.MixerLoaded ? new SolidColorBrush(Color.FromUInt32(PaintOptions.MixerColor)) : Skin.SurfaceControlBrush; });
        PaintToggle("paintMixer", "Load After Each Stroke", "Brush", () => PaintOptions.MixerAutoLoad, v => SetPaint(p => p with { MixerAutoLoad = v }));
        PaintToggle("paintMixer", "Clean After Each Stroke", "delete", () => PaintOptions.MixerAutoClean, v => SetPaint(p => p with { MixerAutoClean = v }));
        OptionChoice("paintMixer", "", MixerPresetNames, () => PaintOptions.MixerPreset, SetMixerPreset, 145);
        PaintSlider("paintMixer", "Wet:", "Mixer Wet Slider", () => _options.Brush.MixerWet * 100, n => { _options.Brush = _options.Brush with { MixerWet = n / 100 }; SetPaint(p => p with { MixerPreset = 0 }); });
        PaintSlider("paintMixer", "Load:", "Mixer Load Slider", () => _options.Brush.MixerLoad * 100, n => { _options.Brush = _options.Brush with { MixerLoad = n / 100 }; SetPaint(p => p with { MixerPreset = 0 }); });
        PaintSlider("paintMixer", "Mix:", "Mixer Mix Slider", () => _options.Brush.MixerMix * 100, n => { _options.Brush = _options.Brush with { MixerMix = n / 100 }; SetPaint(p => p with { MixerPreset = 0 }); });
        PaintSlider("paintMixer", "Flow:", "Mixer Flow Slider", () => _options.Brush.MixerFlow * 100, n => _options.Brush = _options.Brush with { MixerFlow = n / 100 });
        PaintToggle("paintAirbrush", "Airbrush Build-up", "Brush", () => PaintOptions.Airbrush, v => SetPaint(p => p with { Airbrush = v }));
        PaintSlider("paintSmooth", "Smoothing:", "Paint Smoothing Slider", () => _options.Brush.Smoothing * 100, n => _options.Brush = _options.Brush with { Smoothing = n / 100 });
        PaintingButton("paintSmooth", "Smoothing Options", "settings");
        OptionChoice("paintReplace", "Mode:", ["Hue", "Saturation", "Color", "Luminosity"], () => _options.Brush.ReplaceMode switch { 1 => 0, 2 => 1, 3 => 3, _ => 2 }, i => _options.Brush = _options.Brush with { ReplaceMode = i switch { 0 => 1, 1 => 2, 3 => 3, _ => 0 } }, 110);
        foreach (var (title, value) in new[] { ("Sampling: Continuous", 0), ("Sampling: Once", 1), ("Sampling: Background Swatch", 2) })
        { var n = value; var button = IconButton(n == 2 ? "Swatches" : "Eyedropper", title, () => { SetPaint(p => p with { ReplaceSampling = n }); Changed?.Invoke(); }); button.Tag = title; _optionRefreshers.Add(() => button.Background = PaintOptions.ReplaceSampling == n ? Skin.SurfaceDarkBrush : Brushes.Transparent); Cell("paintSampling", button); }
        OptionChoice("paintLimits", "Limits:", ["Discontiguous", "Contiguous", "Find Edges"], () => PaintOptions.ReplaceLimits, i => SetPaint(p => p with { ReplaceLimits = i }), 120);
        PaintSlider("paintTolerance", "Tolerance:", "Replace Tolerance Slider", () => _options.Brush.Tolerance / 2.55, n => _options.Brush = _options.Brush with { Tolerance = n * 2.55 });
        OptionCheck("paintAntialias", "Anti-alias", () => PaintOptions.ReplaceAntialias, v => SetPaint(p => p with { ReplaceAntialias = v }));
        OptionNumber("paintAngle", "Angle:", () => _options.Brush.Angle, n => _options.Brush = _options.Brush with { Angle = n }, -180, 180, 60);
        OptionCheck("paintPencil", "Auto Erase", () => PaintOptions.AutoErase, v => SetPaint(p => p with { AutoErase = v }));
        OptionCheck("paintMixerAll", "Sample All Layers", () => PaintOptions.MixerAllLayers, v => SetPaint(p => p with { MixerAllLayers = v }));
        PaintToggle("paintPressure", "Always Use Pressure for Size", "pressure", () => _options.Brush.PressureSize, v => _options.Brush = _options.Brush with { PressureSize = v });
        PaintingButton("paintSymmetry", "Paint Symmetry", "symmetry");
        BuildStampEraserOptions();
        // A shared order keeps each tool's visible controls in the same order as its brush workflow.
        foreach (var name in new[] { "paintTip", "paintSettings", "paintCloneSource", "paintBlend", "paintStampBlend", "paintArtBlend", "paintEraseMode", "paintReplace", "paintOpacity", "paintMixer", "paintFlow", "paintAirbrush", "paintSmooth", "paintArt", "paintSampling", "paintLimits", "paintTolerance", "paintAntialias", "paintAngle", "paintClone", "paintPattern", "paintEraseHistory", "paintProtect", "paintPencil", "paintMixerAll", "paintPressure", "paintSymmetry", "paintMagic" })
            if (_named.TryGetValue(name, out var controls)) foreach (var control in controls) { _cells.Children.Remove(control); _cells.Children.Add(control); }
    }
    internal void SetMixerPreset(int i)
    {
        SetPaint(p => p with { MixerPreset = i }); if (i == 0) return;
        var group = (i - 1) / 3; var strength = (i - 1) % 3;
        _options.Brush = _options.Brush with { MixerWet = new[] { 0d, .1, .5, 1 }[group], MixerLoad = group == 0 ? new[] { .5, .1, 1d }[strength] : .5, MixerMix = new[] { .5, .1, .9 }[strength] };
    }
    private void ShowPaintingOptions(Tool tool, bool hasDocument)
    {
        var paint = tool is Tool.Brush or Tool.Pencil or Tool.ColorReplacement or Tool.MixerBrush or Tool.Clone or Tool.PatternStamp or Tool.HistoryBrush or Tool.ArtHistory or Tool.Eraser or Tool.BackgroundEraser;
        var stamp = tool is Tool.Clone or Tool.PatternStamp or Tool.HistoryBrush;
        var sampled = tool is Tool.ColorReplacement or Tool.BackgroundEraser;
        foreach (var group in new[] { "paintTip", "paintAngle", "paintPressure" }) On(group, paint);
        On("paintSettings", paint && !sampled);
        On("paintBlend", tool is Tool.Brush or Tool.Pencil); On("paintStampBlend", stamp); On("paintArtBlend", tool == Tool.ArtHistory);
        On("paintOpacity", tool is Tool.Brush or Tool.Pencil or Tool.Eraser or Tool.ArtHistory || stamp);
        On("paintFlow", tool is Tool.Brush or Tool.Eraser || stamp); On("paintReplace", tool == Tool.ColorReplacement);
        foreach (var group in new[] { "paintSampling", "paintLimits", "paintTolerance" }) On(group, sampled);
        On("paintAntialias", tool == Tool.ColorReplacement);
        On("paintMixer", tool == Tool.MixerBrush); On("paintMixerAll", tool == Tool.MixerBrush);
        On("paintAirbrush", tool is Tool.Brush or Tool.MixerBrush or Tool.Eraser || stamp);
        On("paintSmooth", tool is Tool.Brush or Tool.Pencil or Tool.MixerBrush or Tool.Eraser);
        On("paintPencil", tool == Tool.Pencil); On("paintSymmetry", tool is Tool.Brush or Tool.Pencil or Tool.Eraser);
        On("paintClone", tool == Tool.Clone); On("paintCloneSource", tool == Tool.Clone); On("paintPattern", tool == Tool.PatternStamp);
        On("paintArt", tool == Tool.ArtHistory); On("paintEraseMode", tool == Tool.Eraser); On("paintEraseHistory", tool == Tool.Eraser);
        On("paintProtect", tool == Tool.BackgroundEraser); On("paintMagic", tool == Tool.MagicEraser);
        if (paint || tool == Tool.MagicEraser) foreach (var old in new[] { "brush", "pressure", "opacity", "mode", "extendedBrush", "replace", "mixer", "clone", "cloneScope", "pattern", "art", "eraseColor", "backgroundErase", "bucket" }) On(old, false);
        foreach (var group in _named.Where(n => n.Key.StartsWith("paint", StringComparison.Ordinal))) foreach (var cell in group.Value) cell.IsEnabled = hasDocument || group.Key is "paintTip" or "paintSettings";
    }
}
