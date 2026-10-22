using System.Text.Json.Serialization;
using Compositor.Core.Document;
namespace Compositor.Core.Format;
/// <summary>Upstream v16 filter layer fields, with positions in document pixels.</summary>
public sealed record FilterLayerSettings
{
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
        && new[] { ProgressiveStart, ProgressiveEnd, RgbSplitCenter }.All(p => p is null || double.IsFinite(p.Value.X) && double.IsFinite(p.Value.Y));
    public static FilterLayerSettings From(FilterSettings s, int width, int height) => new() {
        ProgressiveFrom = s.ProgressiveFrom, ProgressiveTo = s.ProgressiveTo, ProgressiveStart = new(s.ProgressiveStartX / 100 * width, s.ProgressiveStartY / 100 * height),
        ProgressiveEnd = new(s.ProgressiveEndX / 100 * width, s.ProgressiveEndY / 100 * height), ProgressiveRadial = s.ProgressiveRadial, ProgressiveInverted = s.ProgressiveInverted,
        BloomAmount = s.BloomAmount, BloomRadius = s.BloomRadius, RgbSplitDistance = s.RgbSplitDistance, RgbSplitBlur = s.RgbSplitBlur, RgbSplitAngle = s.RgbSplitAngle,
        RgbSplitRadial = s.RgbSplitRadial, RgbSplitCenter = new(s.RgbSplitCenterX / 100 * width, s.RgbSplitCenterY / 100 * height), RgbSplitReach = s.RgbSplitReach };
    public FilterSettings Local(int width, int height, double originX = 0, double originY = 0) => new() {
        ProgressiveFrom = ProgressiveFrom, ProgressiveTo = ProgressiveTo, ProgressiveRadial = ProgressiveRadial, ProgressiveInverted = ProgressiveInverted,
        ProgressiveStartX = ((ProgressiveStart?.X ?? width * .5) - originX) / width * 100, ProgressiveStartY = ((ProgressiveStart?.Y ?? height * .35) - originY) / height * 100,
        ProgressiveEndX = ((ProgressiveEnd?.X ?? width * .5) - originX) / width * 100, ProgressiveEndY = ((ProgressiveEnd?.Y ?? height) - originY) / height * 100,
        BloomAmount = BloomAmount, BloomRadius = BloomRadius, RgbSplitDistance = RgbSplitDistance, RgbSplitBlur = RgbSplitBlur, RgbSplitAngle = RgbSplitAngle,
        RgbSplitRadial = RgbSplitRadial, RgbSplitCenterX = ((RgbSplitCenter?.X ?? width * .5) - originX) / width * 100, RgbSplitCenterY = ((RgbSplitCenter?.Y ?? height * .5) - originY) / height * 100, RgbSplitReach = RgbSplitReach };
}
