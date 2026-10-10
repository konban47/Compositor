using System.Text.Json;

namespace Compositor.Core.IO;

/// <summary>Stable tool identifiers, independent of translations and project file versions.</summary>
public sealed class ToolbarLayout
{
    public int Version { get; set; } = 1;
    public List<List<string>> Groups { get; set; } = [];
    public List<string> Extras { get; set; } = [];
    public bool ShowExtras { get; set; } = true;
    public bool ShowColors { get; set; } = true;
    public bool ShowQuickMask { get; set; } = true;
    public bool ShowScreenMode { get; set; } = true;
    public bool ShowGenerative { get; set; } = true;
    public bool DisableExtraShortcuts { get; set; }
    public ToolbarLayout Copy() => JsonSerializer.Deserialize<ToolbarLayout>(JsonSerializer.Serialize(this))!;
    public static string DefaultPath => Path.Combine(AppPaths.SettingsDirectory, "toolbar.json");
    public void Validate(IReadOnlyCollection<string> known)
    {
        if (Version != 1 || Groups is null || Extras is null || Groups.Count > 200 || Extras.Count > 200) throw new InvalidDataException("Invalid toolbar preset.");
        var all = Groups.SelectMany(g => g ?? throw new InvalidDataException("Invalid tool group.")).Concat(Extras).ToArray();
        if (all.Length > 200 || all.Any(id => !known.Contains(id)) || all.Distinct().Count() != all.Length)
            throw new InvalidDataException("Unknown or duplicate tool in preset.");
        Groups.RemoveAll(g => g.Count == 0);
        // New tools remain accessible after an application upgrade.
        Extras.AddRange(known.Except(all));
    }
    public static ToolbarLayout Load(string path, IReadOnlyCollection<string> known)
    {
        if (new FileInfo(path).Length > 128 * 1024) throw new InvalidDataException("Toolbar preset is too large.");
        var layout = JsonSerializer.Deserialize<ToolbarLayout>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid toolbar preset.");
        layout.Validate(known); return layout;
    }
    public void Save(string path, IReadOnlyCollection<string> known)
    {
        Validate(known);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
}
