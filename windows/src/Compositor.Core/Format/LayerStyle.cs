using System.Text.Json;
using System.Text.Json.Serialization;

namespace Compositor.Core.Format;

public enum StyleEffectKind { BevelEmboss, Stroke, InnerShadow, InnerGlow, Satin, ColorOverlay, GradientOverlay, PatternOverlay, OuterGlow, DropShadow }
public enum StyleContour { Linear, Round, Cone, InvertedCone, Ring, DoubleRing }
public enum StyleGradient { Linear, Radial, Angle, Reflected, Diamond }
public enum StylePattern { Checkerboard, Stripes, Dots, Image }
public enum BevelKind { InnerBevel, OuterBevel, Emboss, PillowEmboss, StrokeEmboss }
public enum StrokePosition { Outside, Inside, Center }
public enum KnockoutKind { None, Shallow, Deep }
public enum BlendIfChannel { Gray, Red, Green, Blue }
public enum LayerLabel { None, Red, Orange, Yellow, Green, Blue, Violet, Gray, Teal, Indigo, Magenta, Purple }
public enum LayerContainer { Group, Frame, Artboard }

/// <summary>One editable instance. Version 15 leaves the original six effect records readable.</summary>
public sealed class StyleEffect
{
    public StyleEffectKind Kind { get; set; }
    public bool Enabled { get; set; } = true;
    public LayerBlendMode BlendMode { get; set; } = LayerBlendMode.Normal;
    public double Opacity { get; set; } = 1;
    public double Red { get; set; }
    public double Green { get; set; }
    public double Blue { get; set; }
    public double Red2 { get; set; } = 1;
    public double Green2 { get; set; } = 1;
    public double Blue2 { get; set; } = 1;
    public double Size { get; set; } = 8;
    public double Distance { get; set; } = 10;
    public double Angle { get; set; } = 120;
    public bool UseGlobalLight { get; set; }
    public double Spread { get; set; }
    public double Noise { get; set; }
    public StyleContour Contour { get; set; }
    public bool ContourEnabled { get; set; }
    public double[]? ContourSamples { get; set; }
    public bool Invert { get; set; }
    public bool CenterSource { get; set; }
    public StrokePosition Position { get; set; }
    public int FillType { get; set; }
    public StyleGradient Gradient { get; set; }
    public double Scale { get; set; } = 100;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public bool AlignWithLayer { get; set; } = true;
    public StylePattern Pattern { get; set; }
    public string? PatternPng { get; set; }
    public bool TextureEnabled { get; set; }
    public double TextureDepth { get; set; } = 25;
    public BevelKind Bevel { get; set; }
    public int Technique { get; set; }
    public double Depth { get; set; } = 100;
    public double Soften { get; set; }
    public double Altitude { get; set; } = 30;
    public double HighlightOpacity { get; set; } = 0.75;
    public double ShadowOpacity { get; set; } = 0.75;
    public LayerBlendMode HighlightMode { get; set; } = LayerBlendMode.Screen;
    public LayerBlendMode ShadowMode { get; set; } = LayerBlendMode.Multiply;
    // Version 34: the rest of Photoshop's Layer Style options. Missing means the behavior older files were drawn with.
    /// <summary>Smooths the contour's corners.</summary>
    public bool? AntiAlias { get; set; }
    /// <summary>Glows: which part of the glow the contour is aimed at, 1–100 (50 leaves it as it is).</summary>
    public double? Range { get; set; }
    /// <summary>Glows drawn with a gradient: how far each pixel's place along it is shaken, 0–100.</summary>
    public double? Jitter { get; set; }
    /// <summary>Gradient Overlay: noise added so the gradient does not band.</summary>
    public bool? Dither { get; set; }
    /// <summary>0 Perceptual, 1 Linear, 2 Classic, 3 Smooth, 4 Stripes: how a gradient's stops are run into each other.</summary>
    public int? GradientMethod { get; set; }
    /// <summary>The gradient's stops, colors as 0xAARRGGBB; missing means the two colors above, fully opaque.</summary>
    public List<StyleGradientStop>? GradientStops { get; set; }
    /// <summary>Stroke: blended into the layer with its blend mode; off, it covers the layer normally.</summary>
    public bool? Overprint { get; set; }
    /// <summary>Drop Shadow: the shadow is cut away under the layer, so a translucent fill does not show it.</summary>
    public bool? KnocksOut { get; set; }
    /// <summary>Bevel and Emboss: the gloss contour that shapes its shading; with it set, Contour shapes the bevel's edge instead.</summary>
    public double[]? GlossSamples { get; set; }
    /// <summary>Bevel and Emboss texture: the pattern's light and dark swapped.</summary>
    public bool? TextureInvert { get; set; }
    [JsonIgnore] public bool NeedsVersion34 => BlendMode >= LayerBlendMode.Dissolve || HighlightMode >= LayerBlendMode.Dissolve || ShadowMode >= LayerBlendMode.Dissolve || AntiAlias is not null || Range is not null || Jitter is not null || Dither is not null || GradientMethod is not null
        || GradientStops is not null || Overprint is not null || KnocksOut is not null || GlossSamples is not null || TextureInvert is not null;

    [JsonIgnore] public bool IsValid => Enum.IsDefined(Kind) && Enum.IsDefined(BlendMode)
        && (Range is null || In(Range.Value, 1, 100)) && (Jitter is null || In(Jitter.Value, 0, 100)) && GradientMethod is null or (>= 0 and <= 4)
        && (GradientStops is null || GradientStops.Count is >= 2 and <= 64 && GradientStops.All(s => s is not null && In(s.Position, 0, 1)))
        && (GlossSamples is null || IO.ContourLibrary.Valid(GlossSamples))
        && (ContourSamples is null || IO.ContourLibrary.Valid(ContourSamples)) && Enum.IsDefined(Contour) && Enum.IsDefined(Position) && Enum.IsDefined(Gradient) && Enum.IsDefined(Pattern)
        && Enum.IsDefined(Bevel) && Enum.IsDefined(HighlightMode) && Enum.IsDefined(ShadowMode)
        && In(Opacity, 0, 1) && Color.IsValid(Red, Green, Blue) && Color.IsValid(Red2, Green2, Blue2)
        && In(Size, 0, 500) && In(Distance, 0, 5000) && In(Angle, -360, 360)
        && In(Spread, 0, 100) && In(Noise, 0, 100) && FillType is >= 0 and <= 2
        && In(Scale, 1, 1000) && In(OffsetX, -100000, 100000) && In(OffsetY, -100000, 100000)
        && In(TextureDepth, -1000, 1000) && Technique is >= 0 and <= 2 && In(Depth, 0, 1000)
        && In(Soften, 0, 100) && In(Altitude, 0, 90) && In(HighlightOpacity, 0, 1) && In(ShadowOpacity, 0, 1)
        && ValidPattern();
    private string? _checkedPattern;
    private bool _patternValid;
    private bool ValidPattern()
    {
        if (PatternPng is null) return Pattern != StylePattern.Image;
        if (ReferenceEquals(PatternPng, _checkedPattern)) return _patternValid;
        _checkedPattern = PatternPng; _patternValid = false;
        try { using var image = Pixels.EffectRasterizer.DecodePattern(PatternPng); _patternValid = true; }
        catch (Exception error) when (error is FormatException or IO.ProjectException or ArgumentException or InvalidOperationException) { }
        return _patternValid;
    }
    internal static bool In(double v, double min, double max) => double.IsFinite(v) && v >= min && v <= max;
    public StyleEffect Copy() => JsonSerializer.Deserialize<StyleEffect>(JsonSerializer.Serialize(this))!;
    public static StyleEffect Default(StyleEffectKind kind) => new()
    {
        Kind = kind,
        BlendMode = kind is StyleEffectKind.DropShadow or StyleEffectKind.InnerShadow or StyleEffectKind.Satin ? LayerBlendMode.Multiply
            : kind is StyleEffectKind.InnerGlow or StyleEffectKind.OuterGlow ? LayerBlendMode.Screen : LayerBlendMode.Normal,
        Red = kind is StyleEffectKind.InnerGlow or StyleEffectKind.OuterGlow ? 1 : 0,
        Green = kind is StyleEffectKind.InnerGlow or StyleEffectKind.OuterGlow ? 1 : 0,
        Blue = kind is StyleEffectKind.InnerGlow or StyleEffectKind.OuterGlow ? 1 : 0,
        Opacity = kind is StyleEffectKind.DropShadow or StyleEffectKind.InnerShadow or StyleEffectKind.Satin ? 0.5 : 1,
        UseGlobalLight = kind is StyleEffectKind.BevelEmboss or StyleEffectKind.DropShadow or StyleEffectKind.InnerShadow,
    };
    /// <summary>Photoshop's own starting values for a new effect, as Reset to Default restores them.</summary>
    public static StyleEffect PhotoshopDefault(StyleEffectKind kind)
    {
        var e = Default(kind);
        switch (kind)
        {
            case StyleEffectKind.BevelEmboss: e.Size = 7; e.HighlightOpacity = .5; e.ShadowOpacity = .5; e.Red2 = e.Green2 = e.Blue2 = 1; e.Altitude = 30; break;
            case StyleEffectKind.Stroke: e.Size = 1; e.Position = StrokePosition.Inside; e.Overprint = false; break;
            case StyleEffectKind.InnerShadow: e.Opacity = .35; e.Distance = 3; e.Size = 7; e.Angle = 30; break;
            case StyleEffectKind.DropShadow: e.Opacity = .35; e.Distance = 3; e.Size = 7; e.Angle = 30; e.KnocksOut = true; break;
            case StyleEffectKind.InnerGlow or StyleEffectKind.OuterGlow:
                e.Opacity = .35; e.Size = 7; e.Range = 50; e.Jitter = 0; e.Red = e.Green = e.Blue = 1;
                e.GradientStops = [new(0, 0xFFFFFFFF), new(1, 0x00FFFFFF)]; break;
            case StyleEffectKind.Satin: e.Opacity = .5; e.Angle = 90; e.Distance = 50; e.Size = 80; e.Invert = true; e.AntiAlias = true; break;
            case StyleEffectKind.ColorOverlay: e.Red = e.Green = e.Blue = 0.506; break;
            case StyleEffectKind.GradientOverlay: e.Angle = 90; e.Red = e.Green = e.Blue = 0.8; e.GradientMethod = 3; e.Dither = false; break;
        }
        return e;
    }
    /// <summary>The gradient's color and alpha at <paramref name="t"/> (0–1), its stops run as <see cref="GradientMethod"/> says.</summary>
    public (double Red, double Green, double Blue, double Alpha) GradientAt(double t)
    {
        t = Math.Clamp(t, 0, 1);
        static (double, double, double, double) Split(uint c) => (((c >> 16) & 255) / 255.0, ((c >> 8) & 255) / 255.0, (c & 255) / 255.0, (c >> 24) / 255.0);
        if (GradientStops is not { Count: >= 2 } stops) return (Red + (Red2 - Red) * t, Green + (Green2 - Green) * t, Blue + (Blue2 - Blue) * t, 1);
        var ordered = stops.OrderBy(s => s.Position).ToList();
        if (t <= ordered[0].Position) return Split(ordered[0].Color);
        if (t >= ordered[^1].Position) return Split(ordered[^1].Color);
        var right = ordered.FindIndex(s => s.Position >= t); var a = ordered[right - 1]; var b = ordered[right];
        var u = b.Position - a.Position <= 1e-9 ? 1 : (t - a.Position) / (b.Position - a.Position);
        u = GradientMethod switch { 3 => u * u * (3 - 2 * u), 4 => u < .5 ? 0 : 1, _ => u };
        var (r1, g1, b1, a1) = Split(a.Color); var (r2, g2, b2, a2) = Split(b.Color);
        if (GradientMethod == 1)
        {
            static double Lin(double v) => v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4);
            static double Gam(double v) => v <= .0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - .055;
            return (Gam(Lin(r1) + (Lin(r2) - Lin(r1)) * u), Gam(Lin(g1) + (Lin(g2) - Lin(g1)) * u), Gam(Lin(b1) + (Lin(b2) - Lin(b1)) * u), a1 + (a2 - a1) * u);
        }
        return (r1 + (r2 - r1) * u, g1 + (g2 - g1) * u, b1 + (b2 - b1) * u, a1 + (a2 - a1) * u);
    }
}

/// <summary>One stop of a layer style's gradient: where it sits, 0–1, and its color as 0xAARRGGBB.</summary>
public sealed record StyleGradientStop(double Position, uint Color);

/// <summary>Split sliders: black fade-in and white fade-out, in the unpremultiplied 0–255 range.</summary>
public sealed class BlendRange
{
    public double Black { get; set; }
    public double BlackSplit { get; set; }
    public double WhiteSplit { get; set; } = 255;
    public double White { get; set; } = 255;
    [JsonIgnore] public bool IsDefault => Black == 0 && BlackSplit == 0 && WhiteSplit == 255 && White == 255;
    [JsonIgnore] public bool IsValid => StyleEffect.In(Black, 0, 255) && StyleEffect.In(White, 0, 255)
        && double.IsFinite(BlackSplit) && double.IsFinite(WhiteSplit) && Black <= BlackSplit && BlackSplit <= WhiteSplit && WhiteSplit <= White;
    public double Coverage(double value) => value < Black || value > White ? 0
        : Math.Min(BlackSplit > Black ? Math.Clamp((value - Black) / (BlackSplit - Black), 0, 1) : 1,
            White > WhiteSplit ? Math.Clamp((White - value) / (White - WhiteSplit), 0, 1) : 1);
}

public sealed class BlendIfRange
{
    public BlendIfChannel Channel { get; set; }
    public BlendRange Source { get; set; } = new();
    public BlendRange Underlying { get; set; } = new();
    [JsonIgnore] public bool IsValid => Enum.IsDefined(Channel) && Source is { IsValid: true } && Underlying is { IsValid: true };
}

public sealed class LayerBlending
{
    public bool Red { get; set; } = true;
    public bool Green { get; set; } = true;
    public bool Blue { get; set; } = true;
    public KnockoutKind Knockout { get; set; }
    public bool BlendInteriorEffectsAsGroup { get; set; }
    public bool BlendClippedLayersAsGroup { get; set; } = true;
    public bool TransparencyShapesLayer { get; set; } = true;
    public bool LayerMaskHidesEffects { get; set; }
    public bool VectorMaskHidesEffects { get; set; }
    public List<BlendIfRange> Ranges { get; set; } = [];
    [JsonIgnore] public bool IsValid => Enum.IsDefined(Knockout) && Ranges is { Count: <= 4 }
        && Ranges.All(r => r is { IsValid: true }) && Ranges.Select(r => r.Channel).Distinct().Count() == Ranges.Count;
    [JsonIgnore] public bool IsDefault => Red && Green && Blue && Knockout == KnockoutKind.None && !BlendInteriorEffectsAsGroup
        && BlendClippedLayersAsGroup && TransparencyShapesLayer && !LayerMaskHidesEffects && !VectorMaskHidesEffects
        && Ranges.All(r => r.Source.IsDefault && r.Underlying.IsDefault);
    public LayerBlending Copy() => JsonSerializer.Deserialize<LayerBlending>(JsonSerializer.Serialize(this))!;
}

/// <summary>A reusable style contains appearance, never the layer's image, name or placement.</summary>
public sealed class LayerStylePreset
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "Style";
    public LayerEffects? Effects { get; set; }
    public LayerBlending? Blending { get; set; }
    public LayerBlendMode BlendMode { get; set; }
    public double Opacity { get; set; } = 1;
    public double FillOpacity { get; set; } = 1;
    [JsonIgnore] public bool IsValid => Version == 1 && !string.IsNullOrWhiteSpace(Name) && Name.Length <= 256
        && (Effects?.IsValid ?? true) && (Blending?.IsValid ?? true) && Enum.IsDefined(BlendMode)
        && StyleEffect.In(Opacity, 0, 1) && StyleEffect.In(FillOpacity, 0, 1);
    public LayerStylePreset Copy() => JsonSerializer.Deserialize<LayerStylePreset>(JsonSerializer.Serialize(this))!;
}
