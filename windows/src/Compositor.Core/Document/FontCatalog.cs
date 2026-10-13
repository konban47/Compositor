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
    public static bool IsInstalled(string name) => Names.Value.ContainsKey(name);
    public static SKTypeface Resolve(string name) => Names.Value.TryGetValue(name, out var face)
        ? SKTypeface.FromFamilyName(face.Family, face.Style) : SKTypeface.FromFamilyName(name) ?? SKTypeface.Default;
    public static string Family(string name) => Names.Value.TryGetValue(name, out var face) ? face.Family : name;
}
