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
public enum LayerLabel { None, Red, Orange, Yellow, Green, Blue, Violet, Gray }
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

    [JsonIgnore] public bool IsValid => Enum.IsDefined(Kind) && Enum.IsDefined(BlendMode)
        && Enum.IsDefined(Contour) && Enum.IsDefined(Position) && Enum.IsDefined(Gradient) && Enum.IsDefined(Pattern)
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
}

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
