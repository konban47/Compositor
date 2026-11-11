using System.Text.Json.Serialization;

namespace Compositor.Core.Format;

public enum ImageMode { Bitmap, Grayscale, Duotone, Indexed, RGB, CMYK, Lab, Multichannel }

public sealed record ImageSettings
{
    public byte[]? CmykProfile { get; init; }
    public byte[]? ColorProfile { get; init; }
    // The original document encoding remains fixed when another profile is assigned.
    public byte[]? ProfileEncoding { get; init; }
    public string? OcioConfiguration { get; init; }
    public string? OcioWorkingSpace { get; init; }
    public uint? Background { get; init; }
    public ImageMode Mode { get; init; } = ImageMode.RGB;
    public int Depth { get; init; } = 8;
    public uint[]? Palette { get; init; }
    public uint Ink { get; init; } = 0xFF352818;
    [JsonIgnore] public bool IsDefault => ProfileEncoding is null && ColorProfile is null && OcioConfiguration is null && CmykProfile is null && Mode == ImageMode.RGB && Depth == 8 && Palette is null && Background is null;
    [JsonIgnore] public bool IsValid => (ColorProfile is null || IO.IccProfiles.Valid(ColorProfile) && IO.IccProfiles.Model(ColorProfile) is "RGB" or "GRAY")
        && (ProfileEncoding is null || IO.IccProfiles.Valid(ProfileEncoding) && IO.IccProfiles.Model(ProfileEncoding) == (Mode == ImageMode.CMYK ? "CMYK" : Mode == ImageMode.Grayscale ? "GRAY" : "RGB"))
        && (OcioConfiguration is null && OcioWorkingSpace is null || OcioConfiguration is { Length: > 0 and < 32768 } && OcioWorkingSpace is { Length: > 0 and < 1024 })
        && (CmykProfile is null || IO.CmykColor.IsProfile(CmykProfile)) && Enum.IsDefined(Mode) && Depth is 8 or 16 or 32
        && (Mode is not (ImageMode.Bitmap or ImageMode.Indexed or ImageMode.Duotone) || Depth == 8)
        && (Palette is null || Palette.Length is >= 2 and <= 256);
}

public enum VariableKind { Visibility, Text, PixelReplacement }
/// <summary>How a pixel replacement fills its layer's box: Photoshop's Fit, Fill, As Is and Conform (stretch).</summary>
public enum PixelMethod { Fit, Fill, AsIs, Conform }
/// <summary>A variable. Method, Alignment (0–8, top-left to bottom-right) and Clip are Windows extension 35; without them a
/// replacement image is stretched to the layer's box, as before.</summary>
public sealed record ImageVariable(string Name, Guid LayerID, VariableKind Kind, PixelMethod? Method = null, int? Alignment = null, bool? Clip = null)
{
    [JsonIgnore] public bool NeedsVersion35 => Method is not null || Alignment is not null || Clip is not null;
    [JsonIgnore] public bool IsValid => Enum.IsDefined(Kind) && (Method is null || Enum.IsDefined(Method.Value)) && Alignment is null or (>= 0 and <= 8);
}
public sealed record ImageDataSet(string Name, Dictionary<string, string> Values);
public sealed record MeasurementScale(double Pixels = 1, double Units = 1, string Unit = "px")
{
    [JsonIgnore] public bool IsValid => double.IsFinite(Pixels) && double.IsFinite(Units) && Pixels > 0 && Units > 0
        && Pixels <= 1000000 && Units <= 1000000 && !string.IsNullOrWhiteSpace(Unit) && Unit.Length <= 64;
}
/// <summary>One row of the Measurement Log. The gray range, median, histogram, source and document were added in Windows
/// extension 35, for Photoshop's full list of data points; older rows leave them out.</summary>
public sealed record MeasurementRecord(string Name, DateTimeOffset Time, double Area, double Perimeter,
    double Width, double Height, double Length, double Angle, int Count, double Mean, string Unit,
    double? GrayMinimum = null, double? GrayMaximum = null, double? GrayMedian = null, int[]? Histogram = null,
    string? Source = null, string? Document = null)
{
    [JsonIgnore] public bool NeedsVersion35 => GrayMinimum is not null || GrayMaximum is not null || GrayMedian is not null || Histogram is not null
        || Source is not null || Document is not null;
    [JsonIgnore] public bool IsValid => Name is { Length: <= 1024 } && Unit is { Length: <= 64 }
        && new[] { Area, Perimeter, Width, Height, Length, Angle, Mean }.All(double.IsFinite)
        && new[] { GrayMinimum, GrayMaximum, GrayMedian }.All(v => v is null || double.IsFinite(v.Value))
        && (Histogram is null || Histogram.Length == 256 && Histogram.All(v => v >= 0)) && Source is null or { Length: <= 64 } && Document is null or { Length: <= 1024 };
}
