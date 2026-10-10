using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Compositor.Core.IO;

namespace Compositor.Desktop;

internal sealed class GenerationSettings
{
    public string Endpoint { get; set; } = "";
    public string Model { get; set; } = "";
    public string Size { get; set; } = "1024x1024";
    public string? ProtectedKey { get; set; }
    private static string PathName => Path.Combine(AppPaths.SettingsDirectory, "image-generation.json");
    public static GenerationSettings Load()
    {
        try { return File.Exists(PathName) ? JsonSerializer.Deserialize<GenerationSettings>(File.ReadAllText(PathName)) ?? new() : new(); }
        catch { return new(); }
    }
    public string Key()
    {
        if (!OperatingSystem.IsWindows() || ProtectedKey is null) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ProtectedKey), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }
    public void Save(string key, bool remember)
    {
        ProtectedKey = null;
        if (remember && key.Length > 0 && OperatingSystem.IsWindows())
            ProtectedKey = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
        Directory.CreateDirectory(AppPaths.SettingsDirectory);
        var temp = PathName + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, PathName, true);
    }
}
