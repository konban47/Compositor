using System.Text.Json;
using Compositor.Core.Document;

namespace Compositor.Core.IO;

public sealed record EditorPreset(Guid ID, string Name, string Kind, double Diameter = 40, double Hardness = 1,
    double Opacity = 1, string? Image = null, string? Path = null, BrushSettings? Brush = null,
    bool CaptureSize = true, bool IncludeTool = false, bool IncludeColor = false, string Tool = "Brush", double[]? Contour = null);

public static class EditorPresets
{
    public static string DefaultPath => System.IO.Path.Combine(AppPaths.SettingsDirectory, "presets.json");
    public static IReadOnlyList<EditorPreset> Load(string path)
    {
        if (!File.Exists(path)) return string.Equals(System.IO.Path.GetFullPath(path), System.IO.Path.GetFullPath(DefaultPath), StringComparison.OrdinalIgnoreCase)
            ? PresetCatalog.Defaults("Brush").Concat(PresetCatalog.Defaults("Pattern")).Select(p => p.Preset!).ToArray() : [];
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("Preset file is too large.");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var legacy = json.RootElement.ValueKind == JsonValueKind.Array;
        if (!legacy && (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("Version", out var version) || !version.TryGetInt32(out var number) || number != 2 || !json.RootElement.TryGetProperty("Presets", out var entries) || entries.ValueKind != JsonValueKind.Array)) throw new InvalidDataException("Invalid preset file.");
        var presets = (legacy ? json.RootElement : json.RootElement.GetProperty("Presets")).Deserialize<EditorPreset[]>() ?? [];
        if (presets.Length > 500 || presets.Any(p => p is null || !Valid(p)) || presets.Select(p => p.ID).Distinct().Count() != presets.Length) throw new InvalidDataException("Invalid preset file.");
        if (legacy && string.Equals(System.IO.Path.GetFullPath(path), System.IO.Path.GetFullPath(DefaultPath), StringComparison.OrdinalIgnoreCase))
        {
            var existing = presets.Select(p => p.ID).ToHashSet();
            return presets.Concat(PresetCatalog.Defaults("Brush").Concat(PresetCatalog.Defaults("Pattern")).Select(p => p.Preset!).Where(p => !existing.Contains(p.ID))).ToArray();
        }
        return presets;
    }
    private static bool Valid(EditorPreset preset)
    {
        if (preset.ID == Guid.Empty || string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > 200 || preset.Kind is not ("Brush" or "Pattern" or "Shape" or "Contour")
            || !double.IsFinite(preset.Diameter + preset.Hardness + preset.Opacity) || preset.Diameter is < 1 or > 5000 || preset.Hardness is < 0 or > 1 || preset.Opacity is < 0 or > 1) return false;
        if (preset.Brush is { } b && (b.Painting is { IsValid: false } || b.Diameter is < 1 or > 5000 || b.Hardness is < 0 or > 1 || b.Opacity is < 0 or > 1
            || b.Angle is < -180 or > 180 || b.Roundness is < .01 or > 1 || b.SpacingFraction is < 0 or > 10
            || typeof(BrushSettings).GetProperties().Where(p => p.PropertyType == typeof(double)).Any(p => !double.IsFinite((double)p.GetValue(b)!)))) return false;
        if (preset.Tool is not ("Brush" or "Pencil" or "ColorReplacement" or "MixerBrush" or "Clone" or "PatternStamp" or "HistoryBrush" or "ArtHistory" or "Eraser" or "BackgroundEraser")) return false;
        try
        {
            if (preset.Kind == "Contour") return ContourLibrary.Valid(preset.Contour);
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
        var presets = items.ToArray(); if (presets.Length > 500 || presets.Any(p => p is null || !Valid(p)) || presets.Select(p => p.ID).Distinct().Count() != presets.Length) throw new InvalidDataException("Invalid preset file.");
        var local = string.Equals(System.IO.Path.GetFullPath(path), System.IO.Path.GetFullPath(DefaultPath), StringComparison.OrdinalIgnoreCase);
        var json = local ? JsonSerializer.Serialize(new { Version = 2, Presets = presets }, new JsonSerializerOptions { WriteIndented = true })
            : JsonSerializer.Serialize(presets, new JsonSerializerOptions { WriteIndented = true });
        if (json.Length > 32 * 1024 * 1024) throw new InvalidDataException("Preset file is too large.");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, json); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
