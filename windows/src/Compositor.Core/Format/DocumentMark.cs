using System.Text.Json.Serialization;

namespace Compositor.Core.Format;

public enum MarkKind { Slice, Note, Count, Sampler, Measure }

/// <summary>Non-printing document annotations. Immutable records keep undo snapshots independent.</summary>
public sealed record DocumentMark
{
    [JsonRequired] public Guid ID { get; init; } = Guid.NewGuid();
    [JsonRequired] public MarkKind Kind { get; init; }
    [JsonRequired] public double X { get; init; }
    [JsonRequired] public double Y { get; init; }
    public double Width { get; init; } = 1;
    public double Height { get; init; } = 1;
    public string Name { get; init; } = "";
    public string Text { get; init; } = "";
    public string Author { get; init; } = "";
    public double? SecondX { get; init; }
    public double? SecondY { get; init; }
    [JsonIgnore] public bool HasUtilityOptions => Author.Length > 0 || SecondX is not null || SecondY is not null;
    public string Group { get; init; } = "1";
    public string Url { get; init; } = "";
    public bool NoImage { get; init; }
    public string Target { get; init; } = "";
    public string AltText { get; init; } = "";
    public uint? Background { get; init; }
    [JsonIgnore] public bool HasSliceOptions => NoImage || Target.Length > 0 || AltText.Length > 0 || Background is not null;
    public uint Color { get; init; } = 0xFFFFB74D;
    public bool Visible { get; init; } = true;
    [JsonIgnore] public bool IsValid => ID != Guid.Empty && Enum.IsDefined(Kind) && double.IsFinite(X + Y + Width + Height)
        && Math.Abs(X) <= 1_000_000 && Math.Abs(Y) <= 1_000_000 && Math.Abs(Width) <= 1_000_000 && Math.Abs(Height) <= 1_000_000
        && (Kind != MarkKind.Slice || Width > 0 && Height > 0) && Name is { Length: <= 256 } && Text is { Length: <= 8192 }
        && Target is { Length: <= 2048 } && AltText is { Length: <= 8192 }
        && Author is { Length: <= 256 } && (SecondX is null && SecondY is null || Kind == MarkKind.Measure && SecondX is { } sx && SecondY is { } sy && double.IsFinite(sx + sy) && Math.Abs(sx) <= 1_000_000 && Math.Abs(sy) <= 1_000_000)
        && Group is { Length: <= 256 } && Url is { Length: <= 2048 };
}
