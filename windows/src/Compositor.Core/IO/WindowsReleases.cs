using System.Text.Json;

namespace Compositor.Core.IO;

public sealed record WindowsRelease(Version Version, string Page, bool Prerelease);

/// <summary>The Windows release channel never offers upstream's macOS installer.</summary>
public static class WindowsReleases
{
    public const string Repository = "konban47/Compositor";
    public const string Page = "https://github.com/" + Repository + "/releases";
    public const string Api = "https://api.github.com/repos/" + Repository + "/releases?per_page=20";

    public static WindowsRelease? Newest(string json)
    {
        using var data = JsonDocument.Parse(json);
        if (data.RootElement.ValueKind != JsonValueKind.Array) return null;
        var releases = new List<WindowsRelease>();
        foreach (var release in data.RootElement.EnumerateArray())
        {
            if (!release.TryGetProperty("tag_name", out var tagValue)) continue;
            var tag = tagValue.GetString() ?? "";
            if (!tag.StartsWith("windows-v", StringComparison.Ordinal)
                || !Version.TryParse(tag[9..], out var version)) continue;
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            var page = release.TryGetProperty("html_url", out var url) ? url.GetString() : null;
            if (page is null || !page.StartsWith(Page + "/tag/windows-v", StringComparison.Ordinal)) continue;
            releases.Add(new(version, page, release.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()));
        }
        return releases.OrderByDescending(release => release.Version).FirstOrDefault();
    }
}
