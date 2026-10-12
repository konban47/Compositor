using System.Text.Json;
using Compositor.Core.IO;

namespace Compositor.Desktop;

internal sealed class EditorPreferences
{
    internal static string PathName => Path.Combine(AppPaths.SettingsDirectory, "preferences.json");
    internal static EditorPreferences Current { get; } = Load();
    public int HistorySteps { get; set; } = 100;
    public int HistoryMB { get; set; } = 256;
    public bool NonLinearHistory { get; set; }
    public bool PreciseCursor { get; set; }
    public bool SampleRing { get; set; } = true;
    public double BrushSmoothing { get; set; }
    public bool ShowTransparency { get; set; } = true;
    public bool WatchFiles { get; set; } = true;
    public int RulerUnit { get; set; }
    public int ExportQuality { get; set; } = 90;
    public double PdfResolution { get; set; } = 300;
    public string DefaultFont { get; set; } = "Arial";
    public string OcioExecutable { get; set; } = "";
    public string OcioConfiguration { get; set; } = "";
    public string OcioSource { get; set; } = "srgb_texture";
    public string OcioDestination { get; set; } = "srgb_texture";
    public HashSet<string> HiddenMenus { get; set; } = [];
    private static EditorPreferences Load()
    {
        try
        {
            if (!File.Exists(PathName) || new FileInfo(PathName).Length > 1024 * 1024) return new();
            var p = JsonSerializer.Deserialize<EditorPreferences>(File.ReadAllText(PathName)) ?? new();
            p.HistorySteps = Math.Clamp(p.HistorySteps, 1, 1000); p.HistoryMB = Math.Clamp(p.HistoryMB, 16, 1536);
            p.BrushSmoothing = double.IsFinite(p.BrushSmoothing) ? Math.Clamp(p.BrushSmoothing, 0, 1) : 0;
            p.ExportQuality = Math.Clamp(p.ExportQuality, 1, 100); p.RulerUnit = Math.Clamp(p.RulerUnit, 0, 3);
            p.PdfResolution = double.IsFinite(p.PdfResolution) ? Math.Clamp(p.PdfResolution, 1, 9600) : 300;
            p.DefaultFont = string.IsNullOrWhiteSpace(p.DefaultFont) ? "Arial" : p.DefaultFont[..Math.Min(200, p.DefaultFont.Length)];
            p.HiddenMenus ??= []; return p;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    internal void Save()
    {
        Directory.CreateDirectory(AppPaths.SettingsDirectory);
        var temporary = PathName + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); File.Move(temporary, PathName, true);
    }
}
