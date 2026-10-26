using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>Maps Photoshop PostScript names to installed faces without replacing the saved font name.</summary>
public static class FontCatalog
{
    public static IReadOnlyList<string> Families { get; } = SKFontManager.Default.FontFamilies.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToArray();
    private sealed record Face(string Family, SKFontStyle Style);
    private static readonly Lazy<Dictionary<string, Face>> Names = new(() =>
    {
        var result = new Dictionary<string, Face>(StringComparer.OrdinalIgnoreCase);
        foreach (var family in Families)
        {
            result.TryAdd(family, new(family, SKFontStyle.Normal));
            using var styles = SKFontManager.Default.GetFontStyles(family);
            for (var i = 0; i < styles.Count; i++)
            {
                using var face = styles.CreateTypeface(i);
                if (face?.PostScriptName is { Length: > 0 } name) result.TryAdd(name, new(family, face.FontStyle));
            }
        }
        return result;
    });
    private static readonly Lazy<string[]> AllFaces = new(() => Names.Value.Keys.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToArray());
    public static IReadOnlyList<string> Faces => AllFaces.Value;
    public static bool IsInstalled(string name) => Names.Value.ContainsKey(name);
    public static SKTypeface Resolve(string name) => Names.Value.TryGetValue(name, out var face)
        ? SKTypeface.FromFamilyName(face.Family, face.Style) : SKTypeface.FromFamilyName(name) ?? SKTypeface.Default;
    public static string Family(string name) => Names.Value.TryGetValue(name, out var face) ? face.Family : name;

    public sealed record FontFace(string Name, string Style, int Weight, int Width, bool Italic);
    public static IReadOnlyList<FontFace> Styles(string name)
    {
        var family = Family(name); using var styles = SKFontManager.Default.GetFontStyles(family);
        var result = new List<FontFace>();
        for (var i = 0; i < styles.Count; i++)
        {
            using var face = styles.CreateTypeface(i); if (face is null) continue;
            var style = face.FontStyle; var label = styles.GetStyleName(i);
            result.Add(new(face.PostScriptName ?? family, string.IsNullOrWhiteSpace(label) ? "Regular" : label,
                style.Weight, style.Width, style.Slant != SKFontStyleSlant.Upright));
        }
        return result.Count == 0 ? [new(name, "Regular", 400, 5, false)] : result;
    }
}
