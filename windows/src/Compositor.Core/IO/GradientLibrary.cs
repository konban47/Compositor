using System.Text.Json;
using Compositor.Core.Document;
namespace Compositor.Core.IO;
public sealed record GradientPreset(Guid ID, string Name, string Category, GradientStopValue[] Stops);
public static class GradientLibrary
{
    public static string DefaultPath => Path.Combine(AppPaths.SettingsDirectory, "gradients.json");
    public static List<GradientPreset> Defaults()
    {
        var colors = new (string Name, string Category, uint[] Colors)[] {
            ("Black, White", "Basics", [0xff000000, 0xffffffff]), ("Black, Transparent", "Basics", [0xff000000, 0x00000000]),
            ("Ocean", "Blues", [0xff071b4f, 0xff218ede, 0xffbfefff]), ("Sky", "Blues", [0xff224bd1, 0xffbde9ff]),
            ("Violet", "Purples", [0xff291b59, 0xff9457c4, 0xffedccff]), ("Twilight", "Purples", [0xff10235a, 0xffab6fbd, 0xffffc69e]),
            ("Rose", "Pinks", [0xff8d2159, 0xffed8fb0, 0xffffeadc]), ("Sunset", "Oranges", [0xff622060, 0xffdf674b, 0xffffd184]),
            ("Forest", "Greens", [0xff103d32, 0xff58ad70, 0xffe0f5a7]) };
        return colors.Select((c, i) => new GradientPreset(new Guid(i + 1, 148, 158, new byte[8]), c.Name, c.Category, c.Colors.Select((v, j) => new GradientStopValue(j / (double)(c.Colors.Length - 1), v)).ToArray())).ToList();
    }
    public static List<GradientPreset> Load(string path)
    {
        if (!File.Exists(path)) return Defaults();
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Gradient presets exceed 1 MB.");
        var values = JsonSerializer.Deserialize<List<GradientPreset>>(File.ReadAllText(path)) ?? [];
        Validate(values); return values;
    }
    public static void Save(string path, IEnumerable<GradientPreset> presets)
    {
        var values = presets.ToList(); Validate(values); var text = JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllText(path + ".tmp", text); File.Move(path + ".tmp", path, true);
    }
    private static void Validate(List<GradientPreset> values)
    {
        if (values.Count > 512 || values.Any(v => v is null) || values.Select(v => v.ID).Distinct().Count() != values.Count || values.Any(v => v is null || string.IsNullOrWhiteSpace(v.Name) || v.Name.Length > 200 || string.IsNullOrWhiteSpace(v.Category) || v.Category.Length > 200 || !(new GradientPaintSettings { Stops = v.Stops }).IsValid)) throw new InvalidDataException("Invalid gradient presets.");
    }
}
