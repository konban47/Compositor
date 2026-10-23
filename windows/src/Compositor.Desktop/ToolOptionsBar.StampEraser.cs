using Compositor.Core.Document;
namespace Compositor.Desktop;
internal sealed partial class ToolOptionsBar
{
    internal static readonly string[] ArtStyleNames = ["Tight Short", "Tight Medium", "Tight Long", "Loose Medium", "Loose Long", "Dab", "Tight Curl", "Tight Long Curl", "Loose Curl", "Loose Long Curl"];
    private void BuildStampEraserOptions()
    {
        var stampModes = Enum.GetValues<PaintBlend>().Where(m => m != PaintBlend.Clear).ToArray();
        OptionChoice("paintStampBlend", "Mode:", stampModes.Select(m => PaintModeNames[(int)m]).ToArray(), () => Math.Max(0, Array.IndexOf(stampModes, PaintOptions.Blend)), i => SetPaint(p => p with { Blend = stampModes[i] }), 150);
        var artModes = new[] { PaintBlend.Normal, PaintBlend.Darken, PaintBlend.Lighten, PaintBlend.Hue, PaintBlend.Saturation, PaintBlend.Color, PaintBlend.Luminosity };
        OptionChoice("paintArtBlend", "Mode:", artModes.Select(m => PaintModeNames[(int)m]).ToArray(), () => Math.Max(0, Array.IndexOf(artModes, PaintOptions.Blend)), i => SetPaint(p => p with { Blend = artModes[i] }), 140);
        PaintingButton("paintCloneSource", "Clone Source", "Clone");
        OptionCheck("paintClone", "Aligned", () => _options.Brush.CloneAligned, v => _options.Brush = _options.Brush with { CloneAligned = v });
        OptionChoice("paintClone", "Sample:", ["Current Layer", "Current & Below", "All Layers"], () => _options.Brush.CloneAllLayers ? 2 : _options.Brush.CloneScope, i => _options.Brush = _options.Brush with { CloneScope = i, CloneAllLayers = false }, 145);
        PaintToggle("paintClone", "Ignore Adjustment Layers", "eye-closed", () => _options.Brush.CloneIgnoreAdjustments, v => _options.Brush = _options.Brush with { CloneIgnoreAdjustments = v });
        var pattern = PaintingButton("paintPattern", "Pattern Presets"); pattern.Width = 44;
        _optionRefreshers.Add(() => pattern.Content = new PatternSwatch(() => _options.Brush.Pattern) { Width = 30, Height = 24 });
        OptionCheck("paintPattern", "Aligned", () => PaintOptions.PatternAligned, v => SetPaint(p => p with { PatternAligned = v }));
        OptionCheck("paintPattern", "Impressionist", () => PaintOptions.PatternImpressionist, v => SetPaint(p => p with { PatternImpressionist = v }));
        OptionChoice("paintArt", "Style:", ArtStyleNames, () => PaintOptions.ArtStyle, i => SetPaint(p => p with { ArtStyle = i }), 135);
        OptionNumber("paintArt", "Area:", () => PaintOptions.ArtArea, n => SetPaint(p => p with { ArtArea = n }), 1, 500, 65);
        PaintSlider("paintArt", "Tolerance:", "Art Tolerance Slider", () => PaintOptions.ArtTolerance * 100, n => SetPaint(p => p with { ArtTolerance = n / 100 }));
        OptionChoice("paintEraseMode", "Mode:", ["Brush", "Pencil", "Block"], () => PaintOptions.EraserMode, i => SetPaint(p => p with { EraserMode = i }), 95);
        OptionCheck("paintEraseHistory", "Erase to History", () => PaintOptions.EraseToHistory, v => SetPaint(p => p with { EraseToHistory = v }));
        OptionCheck("paintProtect", "Protect Foreground Color", () => _options.Brush.ProtectForeground, v => _options.Brush = _options.Brush with { ProtectForeground = v });
        PaintSlider("paintMagic", "Tolerance:", "Magic Tolerance Slider", () => _options.Wand.Tolerance, n => _options.Wand = _options.Wand with { Tolerance = (int)n }, 0, 255);
        OptionCheck("paintMagic", "Anti-alias", () => _options.SelectionAntialiased, v => _options.SelectionAntialiased = v);
        OptionCheck("paintMagic", "Contiguous", () => _options.Wand.Contiguous, v => _options.Wand = _options.Wand with { Contiguous = v });
        OptionCheck("paintMagic", "Sample All Layers", () => _options.WandAllLayers, v => _options.WandAllLayers = v);
        PaintSlider("paintMagic", "Opacity:", "Paint Opacity Slider", () => _options.Brush.Opacity * 100, n => _options.Brush = _options.Brush with { Opacity = n / 100 });
    }
}
