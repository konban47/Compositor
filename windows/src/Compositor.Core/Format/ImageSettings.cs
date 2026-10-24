using System.Text.Json.Serialization;

namespace Compositor.Core.Format;

public enum ImageMode { Bitmap, Grayscale, Duotone, Indexed, RGB, CMYK, Lab, Multichannel }

public sealed record ImageSettings
{
    public byte[]? CmykProfile { get; init; }
    public byte[]? ColorProfile { get; init; }
    public string? OcioConfiguration { get; init; }
    public string? OcioWorkingSpace { get; init; }
    public uint? Background { get; init; }
    public ImageMode Mode { get; init; } = ImageMode.RGB;
    public int Depth { get; init; } = 8;
    public uint[]? Palette { get; init; }
    public uint Ink { get; init; } = 0xFF352818;
    [JsonIgnore] public bool IsDefault => ColorProfile is null && OcioConfiguration is null && CmykProfile is null && Mode == ImageMode.RGB && Depth == 8 && Palette is null && Background is null;
    [JsonIgnore] public bool IsValid => (ColorProfile is null || IO.IccProfiles.Valid(ColorProfile) && IO.IccProfiles.Model(ColorProfile) is "RGB" or "GRAY")
        && (OcioConfiguration is null && OcioWorkingSpace is null || OcioConfiguration is { Length: > 0 and < 32768 } && OcioWorkingSpace is { Length: > 0 and < 1024 })
        && (CmykProfile is null || IO.CmykColor.IsProfile(CmykProfile)) && Enum.IsDefined(Mode) && Depth is 8 or 16 or 32
        && (Mode is not (ImageMode.Bitmap or ImageMode.Indexed or ImageMode.Duotone) || Depth == 8)
        && (Palette is null || Palette.Length is >= 2 and <= 256);
}

public enum VariableKind { Visibility, Text, PixelReplacement }
public sealed record ImageVariable(string Name, Guid LayerID, VariableKind Kind);
public sealed record ImageDataSet(string Name, Dictionary<string, string> Values);
public sealed record MeasurementScale(double Pixels = 1, double Units = 1, string Unit = "px")
{
    [JsonIgnore] public bool IsValid => double.IsFinite(Pixels) && double.IsFinite(Units) && Pixels > 0 && Units > 0
        && Pixels <= 1000000 && Units <= 1000000 && !string.IsNullOrWhiteSpace(Unit) && Unit.Length <= 64;
}
public sealed record MeasurementRecord(string Name, DateTimeOffset Time, double Area, double Perimeter,
    double Width, double Height, double Length, double Angle, int Count, double Mean, string Unit);
