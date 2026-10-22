using System.Text.Json.Serialization;
using SkiaSharp;

namespace Compositor.Core.Format;
public sealed record FrameStyle
{
    public int Shape { get; init; }
    public uint? Stroke { get; init; }
    public double StrokeWidth { get; init; } = 1;
    public int StrokeAlignment { get; init; } = 1;
    public double CornerRadius { get; init; }
    public string? CustomPath { get; init; }
    [JsonIgnore] public bool IsValid => Shape is >= 0 and <= 4 && StrokeAlignment is >= 0 and <= 2
        && double.IsFinite(StrokeWidth + CornerRadius) && StrokeWidth is >= 0 and <= 1000 && CornerRadius is >= 0 and <= 30000
        && (Shape != 4 || ValidPath(CustomPath));
    private static bool ValidPath(string? value)
    { if (value is not { Length: > 0 and <= 100000 }) return false; using var path = SKPath.ParseSvgPathData(value); return path is { IsEmpty: false } && path.Points.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y)) && path.Bounds.Left >= 0 && path.Bounds.Top >= 0 && path.Bounds.Right <= 1 && path.Bounds.Bottom <= 1; }
}
public sealed record CountGroup(string Name = "1", uint Color = 0xFF9AFFFF, bool Visible = true, double MarkerSize = 2, double LabelSize = 8)
{
    [JsonIgnore] public bool IsValid => Name is { Length: > 0 and <= 256 } && double.IsFinite(MarkerSize + LabelSize)
        && MarkerSize is >= 1 and <= 10 && LabelSize is >= 8 and <= 72;
}
