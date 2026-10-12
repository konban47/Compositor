using System.Text.Json;
using Compositor.Core.Document;

namespace Compositor.Core.IO;

public sealed record EditorPreset(Guid ID, string Name, string Kind, double Diameter = 40, double Hardness = 1,
    double Opacity = 1, string? Image = null, string? Path = null);

public static class EditorPresets
{
    public static string DefaultPath => System.IO.Path.Combine(AppPaths.SettingsDirectory, "presets.json");
    public static IReadOnlyList<EditorPreset> Load(string path)
    {
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("Preset file is too large.");
        var presets = JsonSerializer.Deserialize<EditorPreset[]>(File.ReadAllText(path)) ?? [];
        if (presets.Length > 500 || presets.Select(p => p.ID).Distinct().Count() != presets.Length || presets.Any(p => !Valid(p))) throw new InvalidDataException("Invalid preset file.");
        return presets;
    }
    private static bool Valid(EditorPreset preset)
    {
        if (preset.ID == Guid.Empty || string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > 200 || preset.Kind is not ("Brush" or "Pattern" or "Shape")
            || !double.IsFinite(preset.Diameter + preset.Hardness + preset.Opacity) || preset.Diameter is < 1 or > 2500 || preset.Hardness is < 0 or > 1 || preset.Opacity is < 0 or > 1) return false;
        try
        {
            if (preset.Kind == "Shape") return preset.Path is not null && ShapeTemplates.Normalize(preset.Path).Length > 0;
            if (preset.Image is null) return preset.Kind == "Brush";
            var data = Convert.FromBase64String(preset.Image); if (data.Length > 4 * 1024 * 1024) return false;
            using var codec = SkiaSharp.SKCodec.Create(new SkiaSharp.SKMemoryStream(data));
            return codec is not null && codec.Info.Width <= 2048 && codec.Info.Height <= 2048 && codec.Info.Width > 0 && codec.Info.Height > 0;
        }
        catch (Exception e) when (e is FormatException or InvalidDataException) { return false; }
    }
    public static void Save(string path, IEnumerable<EditorPreset> items)
    {
        var presets = items.ToArray(); if (presets.Length > 500 || presets.Any(p => !Valid(p))) throw new InvalidDataException("Invalid preset file.");
        var json = JsonSerializer.Serialize(presets, new JsonSerializerOptions { WriteIndented = true });
        if (json.Length > 32 * 1024 * 1024) throw new InvalidDataException("Preset file is too large.");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, json); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
