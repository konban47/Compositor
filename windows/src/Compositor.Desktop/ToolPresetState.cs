using System.Reflection;
using System.Text.Json;
using Compositor.Core.Document;
using Compositor.Core.IO;
using SkiaSharp;

namespace Compositor.Desktop;

internal sealed class ToolPresetState
{
    public Guid ID { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public Tool Tool { get; set; }
    public ToolOptions Options { get; set; } = new();
    public ArtboardToolOptions Artboard { get; set; } = new();
    public Dictionary<string, JsonElement> Professional { get; set; } = [];
    public bool AutoSelect { get; set; } = true;
    public bool SelectGroup { get; set; }
    public bool TransformControls { get; set; } = true;
    public string? Pattern { get; set; }
    public string? Tip { get; set; }
    internal static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true };
    internal static string FilePath => Path.Combine(AppPaths.SettingsDirectory, "tool-presets.json");
    internal static void CopyOptions(ToolOptions source, ToolOptions target)
    {
        foreach (var field in typeof(ToolOptions).GetFields())
            if (field.FieldType == typeof(SelectionToolSettings) || field.FieldType == typeof(CropToolSettings))
            { foreach (var property in field.FieldType.GetProperties()) property.SetValue(field.GetValue(target), property.GetValue(field.GetValue(source))); }
            else field.SetValue(target, field.GetValue(source));
    }
    internal static List<ToolPresetState> Read(string path)
    {
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Tool presets exceed 16 MB.");
        var values = JsonSerializer.Deserialize<List<ToolPresetState>>(File.ReadAllText(path), Json) ?? [];
        if (values.Count > 512 || values.Any(p => p is null || !Enum.IsDefined(p.Tool) || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 200 || p.Options?.Brush is null || p.Options.Wand is null || p.Options.Selection is null || p.Options.Crop is null || p.Artboard is null || p.Professional is null)) throw new InvalidDataException("Invalid tool presets.");
        foreach (var p in values)
        {
            var b = p.Options.Brush;
            if (typeof(BrushSettings).GetProperties().Where(f => f.PropertyType == typeof(double)).Any(f => !double.IsFinite((double)f.GetValue(b)!))
                || b.Diameter is <= 0 or > 5000 || b.Opacity is < 0 or > 1 || b.Hardness is < 0 or > 1
                || p.Artboard.Width is < 1 or > 30000 || p.Artboard.Height is < 1 or > 30000) throw new InvalidDataException("Invalid tool preset values.");
            var s = p.Options.Selection; var c = p.Options.Crop;
            if (new object[] { s, c }.Any(settings => settings.GetType().GetProperties().Where(f => f.PropertyType == typeof(double)).Any(f => !double.IsFinite((double)f.GetValue(settings)!)))
                || !Enum.IsDefined(s.Mode) || s.Style is < 0 or > 2 || s.Feather is < 0 or > 250 || s.Width is < 1 or > 30000 || s.Height is < 1 or > 30000
                || s.BrushAngle is < -180 or > 180 || s.ObjectShape is < 0 or > 1 || s.OverlayMode is < 0 or > 2 || s.OverlayOpacity is < 0 or > 100 || s.OverlayOutline is < 0 or > 10
                || c.Mode is < 0 or > 1 || c.Width is < 0 or > 30000 || c.Height is < 0 or > 30000 || c.Resolution is < 0 or > 9600 || c.ResolutionUnit is < 0 or > 1
                || c.Fill is < 0 or > 2 || c.Overlay is < 0 or > 5 || c.OverlayDisplay is < 0 or > 2 || c.Orientation is < 0 or > 3 || c.ShieldOpacity is < 0 or > 100
                || c.SliceStyle is < 0 or > 2 || c.SliceWidth is < 1 or > 30000 || c.SliceHeight is < 1 or > 30000) throw new InvalidDataException("Invalid selection, crop or slice preset values.");
        }
        return values;
    }
    internal static void Write(string path, IEnumerable<ToolPresetState> values)
    {
        var text = JsonSerializer.Serialize(values, Json);
        if (System.Text.Encoding.UTF8.GetByteCount(text) > 16 * 1024 * 1024) throw new InvalidDataException("Tool presets exceed 16 MB.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllText(path + ".tmp", text); File.Move(path + ".tmp", path, true);
    }
    internal static Dictionary<string, JsonElement> CaptureProfessional(ProfessionalOptions options) => typeof(ProfessionalOptions).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
        .ToDictionary(f => f.Name, f => JsonSerializer.SerializeToElement(f.GetValue(options), f.FieldType, Json));
    internal void ApplyProfessional(ProfessionalOptions target)
    {
        foreach (var field in typeof(ProfessionalOptions).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            if (Professional.TryGetValue(field.Name, out var value)) field.SetValue(target, value.Deserialize(field.FieldType, Json));
    }
    internal static string? Encode(SKBitmap? image) => image is null ? null : Convert.ToBase64String(PngCodec.Encode(image));
    internal static SKBitmap? Decode(string? text)
    {
        if (text is null) return null;
        var data = Convert.FromBase64String(text); using var stream = new SKMemoryStream(data); using var codec = SKCodec.Create(stream);
        if (codec is null || codec.Info.Width > 2048 || codec.Info.Height > 2048) throw new InvalidDataException("Preset image is too large.");
        return SKBitmap.Decode(data);
    }
}
