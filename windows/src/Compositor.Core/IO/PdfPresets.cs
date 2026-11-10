using System.Text.Json;
using System.Text.Json.Serialization;

namespace Compositor.Core.IO;

public enum PdfStandard { None, PdfA2b }
public enum PdfDownsample { None, Average, Bicubic }
public enum PdfCompression { Zip, Jpeg }
public enum PdfColorConversion { None, WorkingCmyk }

/// <summary>
/// Edit ▸ PDF Presets…: how Export As writes a PDF page. Compositor's PDFs are one flattened image per page, so a preset decides what
/// happens to that image — its resolution, its compression, its color — and whether the file follows PDF/A-2b.
/// </summary>
public sealed record PdfPreset
{
    public string Name { get; init; } = "Custom";
    public string Description { get; init; } = "";
    [JsonIgnore] public bool BuiltIn { get; init; }
    public PdfStandard Standard { get; init; }
    public PdfDownsample Downsample { get; init; } = PdfDownsample.Bicubic;
    /// <summary>The resolution an image is brought down to, when it is above <see cref="DownsampleAbove"/>.</summary>
    public double DownsampleTo { get; init; } = 300;
    public double DownsampleAbove { get; init; } = 450;
    public PdfCompression Compression { get; init; } = PdfCompression.Jpeg;
    public int ImageQuality { get; init; } = 100;
    public PdfColorConversion ColorConversion { get; init; }
    public bool IsValid => Name is { Length: > 0 and <= 120 } && Description is { Length: <= 2000 } && Enum.IsDefined(Standard) && Enum.IsDefined(Downsample) && Enum.IsDefined(Compression) && Enum.IsDefined(ColorConversion)
        && double.IsFinite(DownsampleTo) && DownsampleTo is >= 9 and <= 2400 && double.IsFinite(DownsampleAbove) && DownsampleAbove >= DownsampleTo && DownsampleAbove <= 4800 && ImageQuality is >= 1 and <= 100;
    /// <summary>The PDF quality setting Skia writes images with: 101 asks it for lossless compression.</summary>
    public int EncodingQuality => Compression == PdfCompression.Zip ? 101 : ImageQuality;
    /// <summary>The pixel size an image of the given size at the given resolution is written at under this preset.</summary>
    public (int Width, int Height) Downsampled(int width, int height, double resolution)
    {
        if (Downsample == PdfDownsample.None || resolution <= DownsampleAbove || resolution <= 0) return (width, height);
        var factor = DownsampleTo / resolution;
        return (Math.Max(1, (int)Math.Round(width * factor)), Math.Max(1, (int)Math.Round(height * factor)));
    }
    /// <summary>The settings in the order the summary lists them, under their headings.</summary>
    public IReadOnlyList<(string Heading, string Label, string Value)> Summary() =>
    [
        ("", "PDF Preset", Name), ("", "Compatibility", "PDF 1.4"), ("", "Standard", Standard == PdfStandard.PdfA2b ? "PDF/A-2b" : "None"),
        ("Compression", "Downsample", Downsample == PdfDownsample.None ? "Do Not Downsample" : Downsample == PdfDownsample.Average ? "Average Downsampling" : "Bicubic Downsampling"),
        ("Compression", "To", DownsampleTo.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " ppi"),
        ("Compression", "For Images Above", DownsampleAbove.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " ppi"),
        ("Compression", "Compression", Compression == PdfCompression.Zip ? "ZIP" : "JPEG"),
        ("Compression", "Image Quality", Compression == PdfCompression.Zip ? "Lossless" : ImageQuality + "%"),
        ("Output", "Color Conversion", ColorConversion == PdfColorConversion.None ? "No Conversion" : "Convert to Working CMYK"),
    ];
}

public static class PdfPresets
{
    public static readonly IReadOnlyList<PdfPreset> BuiltIn =
    [
        new() { Name = "[High Quality Print]", BuiltIn = true, Description = "Use these settings to create PDF documents for quality printing on desktop printers and proofers. Images above 450 ppi are brought down to 300 ppi and compressed with maximum-quality JPEG." },
        new() { Name = "[Press Quality]", BuiltIn = true, ColorConversion = PdfColorConversion.WorkingCmyk, Description = "Use these settings to create PDF documents for high-quality prepress printing. The page is converted to the working CMYK space; images above 450 ppi are brought down to 300 ppi." },
        new() { Name = "[Smallest File Size]", BuiltIn = true, DownsampleTo = 100, DownsampleAbove = 150, ImageQuality = 40, Description = "Use these settings to create PDF documents best suited for on-screen display, email and the web. Images above 150 ppi are brought down to 100 ppi and compressed with low-quality JPEG." },
        new() { Name = "[PDF/A-2b]", BuiltIn = true, Standard = PdfStandard.PdfA2b, Compression = PdfCompression.Zip, Downsample = PdfDownsample.None, Description = "Use these settings to create PDF documents that follow PDF/A-2b, the ISO standard for long-term archiving. Images are kept at full resolution with lossless compression." },
    ];
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static string DefaultPath => Path.Combine(AppPaths.SettingsDirectory, "pdf-presets.json");
    public static IReadOnlyList<PdfPreset> Load(string? path = null)
    {
        path ??= DefaultPath;
        try { return File.Exists(path) && new FileInfo(path).Length < 4 * 1024 * 1024 ? Read(path) : []; }
        catch (Exception error) when (error is IOException or JsonException or InvalidDataException or UnauthorizedAccessException) { return []; }
    }
    public static IReadOnlyList<PdfPreset> All(string? path = null) => [.. BuiltIn, .. Load(path)];
    /// <summary>The presets in a file written by <see cref="Save"/>; a file with any preset that does not pass the check is refused whole.</summary>
    public static IReadOnlyList<PdfPreset> Read(string path)
    {
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("The PDF preset file is too large.");
        var presets = JsonSerializer.Deserialize<List<PdfPreset>>(File.ReadAllText(path), Json) ?? [];
        if (presets.Count > 200 || presets.Any(p => p is null || !p.IsValid)) throw new InvalidDataException("The PDF preset file is not valid.");
        return presets.Select(p => p with { BuiltIn = false }).ToArray();
    }
    public static void Save(IEnumerable<PdfPreset> presets, string? path = null)
    {
        var list = presets.Where(p => !p.BuiltIn).ToList();
        if (list.Any(p => !p.IsValid)) throw new InvalidDataException("The PDF preset is not valid.");
        path ??= DefaultPath; Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        AtomicFile.Write(path, stream => JsonSerializer.Serialize(stream, list, Json));
    }
}
