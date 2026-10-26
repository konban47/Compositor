using System.Text.Json.Serialization;
using Compositor.Core.Document;
namespace Compositor.Core.Format;
/// <summary>Upstream v16–v19 filter layer fields, with positions in document pixels.</summary>
public sealed record FilterLayerSettings
{
    public VignetteLayerSettings? Vignette { get; init; }
    public TonalLayerSettings? Tonal { get; init; }
    public SharpenLayerSettings? Sharpen { get; init; }
    [JsonIgnore] public double VignetteAmount { get => Vignette?.Amount ?? 35; init => Vignette = (Vignette ?? new()) with { Amount = value }; }
    [JsonIgnore] public double VignetteMidpoint { get => Vignette?.Midpoint ?? 50; init => Vignette = (Vignette ?? new()) with { Midpoint = value }; }
    [JsonIgnore] public double VignetteRoundness { get => Vignette?.Roundness ?? 100; init => Vignette = (Vignette ?? new()) with { Roundness = value }; }
    [JsonIgnore] public double VignetteFeather { get => Vignette?.Feather ?? 60; init => Vignette = (Vignette ?? new()) with { Feather = value }; }
    [JsonIgnore] public double VignetteHighlights { get => Vignette?.Highlights ?? 25; init => Vignette = (Vignette ?? new()) with { Highlights = value }; }
    [JsonIgnore] public double TonalAmount { get => Tonal?.Amount ?? 50; init => Tonal = (Tonal ?? new()) with { Amount = value }; }
    [JsonIgnore] public double TonalRadius { get => Tonal?.Radius ?? 16; init => Tonal = (Tonal ?? new()) with { Radius = value }; }
    [JsonIgnore] public double TonalShadows { get => Tonal?.Shadows ?? 40; init => Tonal = (Tonal ?? new()) with { Shadows = value }; }
    [JsonIgnore] public double TonalMidtones { get => Tonal?.Midtones ?? 60; init => Tonal = (Tonal ?? new()) with { Midtones = value }; }
    [JsonIgnore] public double TonalHighlights { get => Tonal?.Highlights ?? 30; init => Tonal = (Tonal ?? new()) with { Highlights = value }; }
    [JsonIgnore] public double SharpenAmount { get => Sharpen?.Amount ?? 100; init => Sharpen = (Sharpen ?? new()) with { Amount = value }; }
    [JsonIgnore] public double SharpenRadius { get => Sharpen?.Radius ?? 1; init => Sharpen = (Sharpen ?? new()) with { Radius = value }; }
    [JsonIgnore] public double VignetteRed { get => Vignette?.Color?.Red ?? 0; init => Vignette = (Vignette ?? new()) with { Color = (Vignette?.Color ?? new()) with { Red = value } }; }
    [JsonIgnore] public double VignetteGreen { get => Vignette?.Color?.Green ?? 0; init => Vignette = (Vignette ?? new()) with { Color = (Vignette?.Color ?? new()) with { Green = value } }; }
    [JsonIgnore] public double VignetteBlue { get => Vignette?.Color?.Blue ?? 0; init => Vignette = (Vignette ?? new()) with { Color = (Vignette?.Color ?? new()) with { Blue = value } }; }
    public LensEffectSettings Lens { get; init; } = new();
    public double ProgressiveFrom { get; init; }
    public double ProgressiveTo { get; init; } = 20;
    public JsonPoint? ProgressiveStart { get; init; }
    public JsonPoint? ProgressiveEnd { get; init; }
    public bool ProgressiveRadial { get; init; }
    public bool ProgressiveInverted { get; init; }
    public double BloomAmount { get; init; } = 40;
    public double BloomRadius { get; init; } = 24;
    public double RgbSplitDistance { get; init; } = 10;
    public double RgbSplitBlur { get; init; }
    public double RgbSplitAngle { get; init; }
    public bool RgbSplitRadial { get; init; }
    public JsonPoint? RgbSplitCenter { get; init; }
    public double RgbSplitReach { get; init; } = 1000;
    [JsonIgnore] public bool IsValid => new[] { ProgressiveFrom, ProgressiveTo, BloomAmount, BloomRadius, RgbSplitDistance, RgbSplitBlur, RgbSplitAngle, RgbSplitReach }.All(double.IsFinite)
        && ProgressiveFrom is >= 0 and <= 250 && ProgressiveTo is >= 0 and <= 250 && BloomAmount is >= 0 and <= 100 && BloomRadius is >= 1 and <= 150
        && RgbSplitDistance is >= 0 and <= 200 && RgbSplitBlur is >= 0 and <= 400 && RgbSplitAngle is >= -180 and <= 180 && RgbSplitReach is >= 1 and <= 100000
        && (Vignette is null || Vignette.Color is not null) && Lens is { IsValid: true } && new[] { FilterKind.Vignette, FilterKind.TonalContrast, FilterKind.Sharpen }.All(k => Local(1, 1).IsValid(k))
        && new[] { ProgressiveStart, ProgressiveEnd, RgbSplitCenter }.All(p => p is null || double.IsFinite(p.Value.X) && double.IsFinite(p.Value.Y));
    public static FilterLayerSettings From(FilterSettings s, int width, int height) => new() {
        VignetteAmount = s.VignetteAmount, VignetteRed = s.VignetteRed, VignetteGreen = s.VignetteGreen, VignetteBlue = s.VignetteBlue, VignetteMidpoint = s.VignetteMidpoint, VignetteRoundness = s.VignetteRoundness, VignetteFeather = s.VignetteFeather, VignetteHighlights = s.VignetteHighlights, TonalAmount = s.TonalAmount, TonalRadius = s.TonalRadius, TonalShadows = s.TonalShadows, TonalMidtones = s.TonalMidtones, TonalHighlights = s.TonalHighlights, SharpenAmount = s.SharpenAmount, SharpenRadius = s.SharpenRadius, Lens = s.Lens with { },
        ProgressiveFrom = s.ProgressiveFrom, ProgressiveTo = s.ProgressiveTo, ProgressiveStart = new(s.ProgressiveStartX / 100 * width, s.ProgressiveStartY / 100 * height),
        ProgressiveEnd = new(s.ProgressiveEndX / 100 * width, s.ProgressiveEndY / 100 * height), ProgressiveRadial = s.ProgressiveRadial, ProgressiveInverted = s.ProgressiveInverted,
        BloomAmount = s.BloomAmount, BloomRadius = s.BloomRadius, RgbSplitDistance = s.RgbSplitDistance, RgbSplitBlur = s.RgbSplitBlur, RgbSplitAngle = s.RgbSplitAngle,
        RgbSplitRadial = s.RgbSplitRadial, RgbSplitCenter = new(s.RgbSplitCenterX / 100 * width, s.RgbSplitCenterY / 100 * height), RgbSplitReach = s.RgbSplitReach };
    public FilterSettings Local(int width, int height, double originX = 0, double originY = 0) => new() {
        VignetteAmount = VignetteAmount, VignetteRed = VignetteRed, VignetteGreen = VignetteGreen, VignetteBlue = VignetteBlue, VignetteMidpoint = VignetteMidpoint, VignetteRoundness = VignetteRoundness, VignetteFeather = VignetteFeather, VignetteHighlights = VignetteHighlights, TonalAmount = TonalAmount, TonalRadius = TonalRadius, TonalShadows = TonalShadows, TonalMidtones = TonalMidtones, TonalHighlights = TonalHighlights, SharpenAmount = SharpenAmount, SharpenRadius = SharpenRadius, Lens = Lens with { },
        ProgressiveFrom = ProgressiveFrom, ProgressiveTo = ProgressiveTo, ProgressiveRadial = ProgressiveRadial, ProgressiveInverted = ProgressiveInverted,
        ProgressiveStartX = ((ProgressiveStart?.X ?? width * .5) - originX) / width * 100, ProgressiveStartY = ((ProgressiveStart?.Y ?? height * .35) - originY) / height * 100,
        ProgressiveEndX = ((ProgressiveEnd?.X ?? width * .5) - originX) / width * 100, ProgressiveEndY = ((ProgressiveEnd?.Y ?? height) - originY) / height * 100,
        BloomAmount = BloomAmount, BloomRadius = BloomRadius, RgbSplitDistance = RgbSplitDistance, RgbSplitBlur = RgbSplitBlur, RgbSplitAngle = RgbSplitAngle,
        RgbSplitRadial = RgbSplitRadial, RgbSplitCenterX = ((RgbSplitCenter?.X ?? width * .5) - originX) / width * 100, RgbSplitCenterY = ((RgbSplitCenter?.Y ?? height * .5) - originY) / height * 100, RgbSplitReach = RgbSplitReach };
}

public sealed record FilterColor(double Red = 0, double Green = 0, double Blue = 0);
public sealed record VignetteLayerSettings
{
    public double Amount { get; init; } = 35;
    public FilterColor Color { get; init; } = new();
    public double Midpoint { get; init; } = 50;
    public double Roundness { get; init; } = 100;
    public double Feather { get; init; } = 60;
    public double Highlights { get; init; } = 25;
}
public sealed record TonalLayerSettings
{
    public double Amount { get; init; } = 50;
    public double Radius { get; init; } = 16;
    public double Shadows { get; init; } = 40;
    public double Midtones { get; init; } = 60;
    public double Highlights { get; init; } = 30;
}
public sealed record SharpenLayerSettings
{
    public double Amount { get; init; } = 100;
    public double Radius { get; init; } = 1;
}
