namespace Compositor.Core.Format;

public enum TextGeometryShape { Block, Circle, Arch, Bowtie }
public sealed record TextGeometry(TextGeometryShape Shape = TextGeometryShape.Block, double Bend = 50, double HorizontalDistortion = 0, double VerticalDistortion = 0)
{
    [System.Text.Json.Serialization.JsonIgnore] public bool IsValid => Enum.IsDefined(Shape)
        && new[] { Bend, HorizontalDistortion, VerticalDistortion }.All(v => double.IsFinite(v) && Math.Abs(v) <= 100);
}
