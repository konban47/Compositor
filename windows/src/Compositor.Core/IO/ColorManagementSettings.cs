using System.Text.Json;
using Compositor.Core.Format;

namespace Compositor.Core.IO;

public enum ColorPolicy { Off, Preserve, Convert }
public enum ColorEngine { LittleCms, MicrosoftIcm, AceCompatible }
public enum ColorIntent { Perceptual, Saturation, Relative, Absolute }
public sealed record ColorManagementSettings
{
    public string Preset { get; init; } = "Custom";
    public string Rgb { get; init; } = "sRGB IEC61966-2.1";
    public string Cmyk { get; init; } = "U.S. Web Coated (SWOP) v2";
    public string Gray { get; init; } = "Gray Gamma 2.2";
    public string Spot { get; init; } = "Dot Gain 15%";
    public ColorPolicy RgbPolicy { get; init; } = ColorPolicy.Preserve;
    public ColorPolicy CmykPolicy { get; init; } = ColorPolicy.Preserve;
    public ColorPolicy GrayPolicy { get; init; } = ColorPolicy.Preserve;
    public bool AskMismatchOpen { get; init; }
    public bool AskMismatchPaste { get; init; }
    public bool AskMissingOpen { get; init; }
    public ColorEngine Engine { get; init; } = ColorEngine.AceCompatible;
    public ColorIntent Intent { get; init; } = ColorIntent.Perceptual;
    public bool BlackPointCompensation { get; init; } = true;
    public bool Dither { get; init; } = true;
    public bool CompensateSceneProfiles { get; init; }
    public bool DesaturateMonitor { get; init; }
    public double MonitorDesaturation { get; init; } = 20;
    public bool BlendRgbGamma { get; init; }
    public double RgbGamma { get; init; } = 1;
    public bool BlendTextGamma { get; init; } = true;
    public double TextGamma { get; init; } = 1.45;
    public Dictionary<string, byte[]> CustomProfiles { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => Enum.IsDefined(Engine) && Enum.IsDefined(Intent) && Enum.IsDefined(RgbPolicy) && Enum.IsDefined(CmykPolicy) && Enum.IsDefined(GrayPolicy)
        && Preset is { Length: <= 512 } && new[] { Rgb, Cmyk, Gray, Spot }.All(n => n is { Length: > 0 and <= 512 })
        && double.IsFinite(MonitorDesaturation) && MonitorDesaturation is >= 0 and <= 100
        && double.IsFinite(RgbGamma) && RgbGamma is >= .1 and <= 10 && double.IsFinite(TextGamma) && TextGamma is >= .1 and <= 10
        && CustomProfiles is { Count: <= 64 } && CustomProfiles.Sum(p => (long)(p.Value?.Length ?? 0)) <= 16 * 1024 * 1024 && CustomProfiles.All(p => p.Key.Length is > 0 and <= 512 && IccProfiles.Valid(p.Value));
    public ColorPolicy Policy(ImageMode mode) => mode == ImageMode.CMYK ? CmykPolicy : mode == ImageMode.Grayscale ? GrayPolicy : RgbPolicy;
    public string ProfileName(ImageMode mode) => mode == ImageMode.CMYK ? Cmyk : mode == ImageMode.Grayscale ? Gray : Rgb;
    public ColorManagementSettings Copy() => this with { CustomProfiles = CustomProfiles.ToDictionary(p => p.Key, p => p.Value.ToArray()) };
    public static ColorManagementSettings Load(string path)
    {
        if (new FileInfo(path).Length > 24 * 1024 * 1024) throw new InvalidDataException("Color settings file is too large.");
        var value = JsonSerializer.Deserialize<ColorManagementSettings>(File.ReadAllText(path));
        if (value?.IsValid != true) throw new InvalidDataException("Invalid color settings."); return value;
    }
    public void Save(string path)
    {
        if (!IsValid) throw new InvalidDataException("Invalid color settings.");
        AtomicFile.Write(path, stream => JsonSerializer.Serialize(stream, this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public static class ColorDefaults
{
    private static ColorManagementSettings _settings = new();
    private static OcioSettings _ocio = new();
    public static ColorManagementSettings Settings { get => _settings; set => _settings = value.IsValid ? value.Copy() : throw new ArgumentException("Invalid color settings."); }
    public static OcioSettings Ocio { get => _ocio; set => _ocio = value.IsValid ? value : throw new ArgumentException("Invalid OCIO settings."); }
}
