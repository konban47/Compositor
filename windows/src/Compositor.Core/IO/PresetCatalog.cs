using Compositor.Core.Document;

namespace Compositor.Core.IO;

public sealed record PresetItem(Guid ID, string Name, EditorPreset? Preset = null, GradientPreset? Gradient = null)
{
    public PresetItem Rename(string name) => this with { Name = name, Preset = Preset is { } p ? p with { Name = name } : null, Gradient = Gradient is { } g ? g with { Name = name } : null };
}
/// <summary>The manager and tool pickers use these same files; no manager-only copies of presets.</summary>
public static class PresetCatalog
{
    public static readonly string[] Kinds = ["Brush", "Pattern", "Gradient", "Shape", "Contour"];
    public static PresetItem Item(EditorPreset p) => new(p.ID, p.Name, p);
    public static List<PresetItem> Load(string kind) => kind switch {
        "Gradient" => GradientLibrary.Load(GradientLibrary.DefaultPath).Select(g => new PresetItem(g.ID, g.Name, Gradient: g)).ToList(),
        "Contour" => ContourLibrary.Load().Select(Item).ToList(),
        _ => EditorPresets.Load(EditorPresets.DefaultPath).Where(p => p.Kind == kind).Select(Item).ToList() };
    public static List<PresetItem> Defaults(string kind) => kind switch {
        "Contour" => ContourLibrary.Defaults().Select(Item).ToList(),
        "Pattern" => PatternLibrary.Defaults().Select(Item).ToList(),
        "Gradient" => GradientLibrary.Defaults().Select(g => new PresetItem(g.ID, g.Name, Gradient: g)).ToList(),
        "Brush" => BrushPresetLibrary.All.Select((p, i) => Item(new EditorPreset(new Guid(i + 1, 406, 29, new byte[8]), p.Name, "Brush", Brush: p.Settings, CaptureSize: false, IncludeTool: true))).ToList(),
        _ => [] };
    public static void Save(string kind, IEnumerable<PresetItem> items, string? exportPath = null)
    {
        if (!Kinds.Contains(kind)) throw new InvalidDataException("Unknown preset type.");
        var list = items.ToArray();
        if (list.Any(p => p.Name.Length is < 1 or > 200 || p.Gradient is null && p.Preset?.Kind != kind)) throw new InvalidDataException("Invalid preset file.");
        if (kind == "Gradient") { if (list.Any(p => p.Gradient is null)) throw new InvalidDataException("Invalid gradient presets."); GradientLibrary.Save(exportPath ?? GradientLibrary.DefaultPath, list.Select(p => p.Gradient!)); }
        else if (exportPath is not null || kind == "Contour") EditorPresets.Save(exportPath ?? ContourLibrary.DefaultPath, list.Select(p => p.Preset!));
        else EditorPresets.Save(EditorPresets.DefaultPath, EditorPresets.Load(EditorPresets.DefaultPath).Where(p => p.Kind != kind).Concat(list.Select(p => p.Preset!)));
    }
    public static List<PresetItem> Import(string kind, string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Preset file does not exist.", path);
        var items = kind == "Gradient" ? GradientLibrary.Load(path).Select(g => new PresetItem(g.ID, g.Name, Gradient: g)).ToList()
            : EditorPresets.Load(path).Where(p => p.Kind == kind).Select(Item).ToList();
        if (items.Count == 0) throw new InvalidDataException("The file contains no presets of the selected type.");
        return items;
    }
    public static List<PresetItem> Append(IEnumerable<PresetItem> current, IEnumerable<PresetItem> imported)
    {
        var result = current.ToList(); var ids = result.Select(p => p.ID).ToHashSet();
        foreach (var item in imported) if (ids.Add(item.ID)) result.Add(item);
        return result;
    }
}
