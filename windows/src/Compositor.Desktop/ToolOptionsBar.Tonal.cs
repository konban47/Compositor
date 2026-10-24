using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Compositor.Core.Document;

namespace Compositor.Desktop;

internal sealed partial class ToolOptionsBar
{
    internal static readonly PaintBlend[] RetouchModes = [PaintBlend.Normal, PaintBlend.Darken, PaintBlend.Lighten, PaintBlend.Hue, PaintBlend.Saturation, PaintBlend.Color, PaintBlend.Luminosity];
    internal static readonly string[] AdjustmentNames = ["Color and Vibrance", "Clarity and Dehaze", "Grain", "Brightness/Contrast", "Levels", "Curves", "Exposure", "Hue/Saturation", "Color Balance", "Black & White", "Photo Filter", "Channel Mixer", "Color Lookup", "Selective Color", "Invert", "Posterize", "Threshold", "Gradient Map"];
    private void BuildTonalOptions()
    {
        OptionChoice("localAdjustment", "Adjustment:", AdjustmentNames, () => _options.AdjustmentBrushKind, n => _options.AdjustmentBrushKind = n, 190);
        foreach (var subtract in new[] { true, false })
        {
            var button = PaintingButton("localAdjustment", subtract ? "Subtract Adjustment" : "Add Adjustment", "Brush"); button.Content = subtract ? "−" : "+";
            button.Click += (_, _) => { _options.AdjustmentSubtract = subtract; Changed?.Invoke(); };
            _optionRefreshers.Add(() => button.Background = _options.AdjustmentSubtract == subtract ? Skin.SurfaceDarkBrush : Brushes.Transparent);
        }
        var tip = PaintingButton("localTip", "Brush Tip Presets"); tip.Width = 58;
        _optionRefreshers.Add(() => tip.Content = new BrushTipPreview(_options.Brush.Diameter) { Width = 42, Height = 26 });
        PaintingButton("localSettings", "Brush Settings", "Brush");
        OptionChoice("localBlend", "Mode:", RetouchModes.Select(x => x.ToString()).ToArray(), () => Math.Max(0, Array.IndexOf(RetouchModes, _options.Brush.EffectBlend)), i => _options.Brush = _options.Brush with { EffectBlend = RetouchModes[i] }, 145);
        OptionChoice("localRange", "Range:", ["Shadows", "Midtones", "Highlights"], () => _options.Brush.ToneRange, i => _options.Brush = _options.Brush with { ToneRange = i }, 110);
        OptionChoice("localSponge", "Mode:", ["Desaturate", "Saturate"], () => _options.Brush.Saturate ? 1 : 0, i => _options.Brush = _options.Brush with { Saturate = i == 1 }, 110);
        PaintSlider("localStrength", "Strength:", "Retouch Strength Slider", () => _options.Brush.Strength * 100, n => _options.Brush = _options.Brush with { Strength = n / 100 });
        PaintSlider("localExposure", "Exposure:", "Retouch Strength Slider", () => _options.Brush.Strength * 100, n => _options.Brush = _options.Brush with { Strength = n / 100 });
        PaintSlider("localFlow", "Flow:", "Retouch Strength Slider", () => _options.Brush.Strength * 100, n => _options.Brush = _options.Brush with { Strength = n / 100 });
        PaintToggle("localStrengthPressure", "Always Use Pressure for Strength", "pressure", () => _options.Brush.PressureStrength, v => _options.Brush = _options.Brush with { PressureStrength = v });
        PaintingButton("localObject", "Apply Adjustment to Object", "Object");
        PaintingButton("localObject", "Adjustment Settings", "Settings");
        OptionCheck("localObject", "Overlay", () => _options.AdjustmentOverlay, v => _options.AdjustmentOverlay = v);
        PaintSlider("localOpacity", "Opacity:", "Paint Opacity Slider", () => _options.Brush.Opacity * 100, n => _options.Brush = _options.Brush with { Opacity = n / 100 });
        PaintToggle("localOpacity", "Always Use Pressure for Opacity", "pressure", () => _options.Brush.PressureOpacity, v => _options.Brush = _options.Brush with { PressureOpacity = v });
        PaintSlider("localAdjustFlow", "Flow:", "Paint Flow Slider", () => PaintOptions.Flow * 100, n => SetPaint(p => p with { Flow = n / 100 }));
        PaintToggle("localAdjustFlow", "Airbrush Build-up", "Brush", () => PaintOptions.Airbrush, v => SetPaint(p => p with { Airbrush = v }));
        OptionNumber("localAngle", "Angle:", () => _options.Brush.Angle, n => _options.Brush = _options.Brush with { Angle = n }, -180, 180, 58);
        OptionCheck("localAll", "Sample All Layers", () => _options.Brush.SampleAll, v => _options.Brush = _options.Brush with { SampleAll = v });
        OptionCheck("localDetail", "Protect Details", () => _options.Brush.ProtectDetails, v => _options.Brush = _options.Brush with { ProtectDetails = v });
        OptionCheck("localFinger", "Finger Painting", () => _options.Brush.FingerPaint, v => _options.Brush = _options.Brush with { FingerPaint = v });
        OptionCheck("localTones", "Protect Tones", () => _options.Brush.ProtectTones, v => _options.Brush = _options.Brush with { ProtectTones = v });
        OptionCheck("localVibrance", "Vibrance", () => _options.Brush.Vibrance, v => _options.Brush = _options.Brush with { Vibrance = v });
        PaintToggle("localPressure", "Always Use Pressure for Size", "pressure", () => _options.Brush.PressureSize, v => _options.Brush = _options.Brush with { PressureSize = v });
        OptionChoice("fillBucket", "", ["Foreground", "Pattern"], () => _options.BucketPattern ? 1 : 0, n => _options.BucketPattern = n == 1, 105);
        var pattern = PaintingButton("fillBucket", "Pattern Presets"); pattern.Width = 42;
        _optionRefreshers.Add(() => { pattern.IsEnabled = _options.BucketPattern; pattern.Content = new PatternSwatch(() => _options.Brush.Pattern) { Width = 28, Height = 22 }; });
        OptionChoice("fillBucket", "Mode:", PaintModeNames, () => (int)PaintOptions.Blend, i => SetPaint(p => p with { Blend = (PaintBlend)i }), 145);
        PaintSlider("fillBucket", "Opacity:", "Paint Opacity Slider", () => _options.Brush.Opacity * 100, n => _options.Brush = _options.Brush with { Opacity = n / 100 });
        OptionNumber("fillBucket", "Tolerance:", () => _options.Wand.Tolerance, n => _options.Wand = _options.Wand with { Tolerance = (int)n }, 0, 255, 56);
        OptionCheck("fillBucket", "Anti-alias", () => _options.BucketAntialias, v => _options.BucketAntialias = v);
        OptionCheck("fillBucket", "Contiguous", () => _options.Wand.Contiguous, v => _options.Wand = _options.Wand with { Contiguous = v });
        OptionCheck("fillBucket", "All Layers", () => _options.WandAllLayers, v => _options.WandAllLayers = v);
        foreach (var zoomOut in new[] { false, true })
        {
            var b = PaintingButton("viewZoom", zoomOut ? "Zoom Out Mode" : "Zoom In Mode", "Zoom"); b.Content = zoomOut ? "⊖" : "⊕";
            b.Click += (_, _) => { _options.ZoomOut = zoomOut; Changed?.Invoke(); }; _optionRefreshers.Add(() => b.Background = _options.ZoomOut == zoomOut ? Skin.SurfaceDarkBrush : Brushes.Transparent);
        }
        OptionCheck("viewZoom", "Resize Windows to Fit", () => _options.ZoomResize, v => _options.ZoomResize = v);
        OptionCheck("viewZoom", "Zoom All Windows", () => _options.ZoomAll, v => _options.ZoomAll = v);
        OptionCheck("viewZoom", "Scrubby Zoom", () => _options.ZoomScrubby, v => _options.ZoomScrubby = v);
        PaintingButton("viewZoom", "100%"); PaintingButton("viewZoom", "Fit Screen"); PaintingButton("viewZoom", "Fill Screen");
    }
    private void ShowTonalOptions(Tool tool, bool hasDocument)
    {
        var retouch = tool is Tool.Blur or Tool.Sharpen or Tool.Smudge; var tone = tool is Tool.Dodge or Tool.Burn; var sponge = tool == Tool.Sponge; var adjustment = tool == Tool.AdjustmentBrush;
        var local = retouch || tone || sponge || adjustment;
        On("localTip", local); On("localSettings", local && !adjustment); On("localBlend", retouch); On("localRange", tone); On("localSponge", sponge);
        On("localStrength", retouch); On("localExposure", tone); On("localFlow", sponge); On("localStrengthPressure", tone || sponge);
        On("localAngle", local && !adjustment); On("localAll", retouch); On("localDetail", tool == Tool.Sharpen); On("localFinger", tool == Tool.Smudge);
        On("localTones", tone); On("localVibrance", sponge); On("localPressure", local);
        foreach (var g in new[] { "localAdjustment", "localObject", "localOpacity", "localAdjustFlow" }) On(g, adjustment);
        On("gradient", false); On("fillGradient", tool == Tool.Gradient); On("fillGradientClassic", tool == Tool.Gradient && _options.GradientClassic);
        On("fillBucket", tool == Tool.Bucket); On("viewZoom", tool == Tool.Zoom);
        On("zoom", false); On("viewRotation", false); On("viewPan", tool == Tool.Pan); On("viewRotate", tool == Tool.RotateView);
        if (local || tool == Tool.Bucket) foreach (var old in new[] { "brush", "pressure", "opacity", "mode", "blur", "retouch", "tone", "sponge", "bucket", "eraseColor", "adjustmentBrush", "extendedBrush" }) On(old, false);
        foreach (var group in _named.Where(n => n.Key.StartsWith("local", StringComparison.Ordinal) || n.Key is "fillBucket" or "viewZoom")) foreach (var c in group.Value) c.IsEnabled = hasDocument && !(c.Tag is "Pattern Presets" && !_options.BucketPattern);
    }
}
