using System.Text.Json;
using Compositor.Core.IO;

namespace Compositor.Desktop;

internal sealed class EditorPreferences
{
    internal static string PathName => Path.Combine(AppPaths.SettingsDirectory, "preferences.json");
    internal static EditorPreferences Current { get; private set; } = Load();
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
    public string Language { get; set; } = "";
    public int Theme { get; set; }
    public int UiFontSize { get; set; } = 12;
    public bool Tooltips { get; set; } = true;
    public bool WheelZoom { get; set; } = true;
    public bool ZoomAtPointer { get; set; } = true;
    public bool PenCursor { get; set; } = true;
    public bool BrushCrosshair { get; set; }
    public bool HidePaintingCursor { get; set; }
    public int BrushOutlineWidth { get; set; } = 1;
    public int CheckerSize { get; set; } = 10;
    public string CheckerLight { get; set; } = "#454545";
    public string CheckerDark { get; set; } = "#383838";
    public string Pasteboard { get; set; } = "#282828";
    public int GuideStyle { get; set; }
    public string GuideColor { get; set; } = "#00FFFF";
    public string GridColor { get; set; } = "#B3B3B3";
    public string PathColor { get; set; } = "#1473E6";
    public int PathWidth { get; set; } = 1;
    public bool AntialiasOverlays { get; set; } = true;
    public bool UseGpu { get; set; } = true;
    public bool AcceleratedCanvas { get; set; } = true;
    public int GpuCacheMB { get; set; } = 1024;
    public int RecentFiles { get; set; } = 10;
    public bool RecoveryEnabled { get; set; }
    public int RecoveryMinutes { get; set; } = 10;
    public bool HistoryLog { get; set; }
    public bool LargeTabs { get; set; }
    public bool ResizeOnPlace { get; set; } = true;
    public bool SmartOnPlace { get; set; }
    public bool SkipPlacement { get; set; }
    public bool ExportTransparency { get; set; } = true;
    public bool EscapeCommitsText { get; set; }
    public bool PlaceholderText { get; set; }
    public double DefaultFontSize { get; set; } = 48;
    public double NewResolution { get; set; } = 72;
    public bool ShowSliceNumbers { get; set; } = true;
    internal EditorPreferences Copy() => JsonSerializer.Deserialize<EditorPreferences>(JsonSerializer.Serialize(this))!;
    internal static void Commit(EditorPreferences draft) { draft.Normalize(); draft.Save(); Current = draft.Copy(); }
    internal void Normalize()
    {
        HistorySteps = Math.Clamp(HistorySteps, 1, 1000); HistoryMB = Math.Clamp(HistoryMB, 16, 1536);
        BrushSmoothing = Finite(BrushSmoothing, 0, 1, 0);
        ExportQuality = Math.Clamp(ExportQuality, 1, 100); RulerUnit = Math.Clamp(RulerUnit, 0, 3);
        PdfResolution = Finite(PdfResolution, 1, 9600, 300);
        DefaultFontSize = Finite(DefaultFontSize, 1, 1000, 48); NewResolution = Finite(NewResolution, 1, 9600, 72);
        DefaultFont = string.IsNullOrWhiteSpace(DefaultFont) ? "Arial" : DefaultFont[..Math.Min(200, DefaultFont.Length)];
        GuideStyle = Math.Clamp(GuideStyle, 0, 2); Theme = Math.Clamp(Theme, 0, 3); UiFontSize = Math.Clamp(UiFontSize, 10, 18);
        BrushOutlineWidth = Math.Clamp(BrushOutlineWidth, 1, 3); PathWidth = Math.Clamp(PathWidth, 1, 3);
        CheckerSize = Math.Clamp(CheckerSize, 2, 32); GpuCacheMB = Math.Clamp(GpuCacheMB, 128, 2048);
        RecentFiles = Math.Clamp(RecentFiles, 0, 100); RecoveryMinutes = Math.Clamp(RecoveryMinutes, 1, 60);
        CheckerLight = Color(CheckerLight, "#454545"); CheckerDark = Color(CheckerDark, "#383838");
        Pasteboard = Color(Pasteboard, "#282828"); GuideColor = Color(GuideColor, "#00FFFF");
        GridColor = Color(GridColor, "#B3B3B3"); PathColor = Color(PathColor, "#1473E6");
        Language = Language is "en" or "zh-CN" ? Language : "";
        HiddenMenus ??= [];
    }
    private static double Finite(double n, double min, double max, double fallback) => double.IsFinite(n) ? Math.Clamp(n, min, max) : fallback;
    internal static bool ValidColor(string? value) => value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);
    private static string Color(string value, string fallback) => ValidColor(value) ? value : fallback;
    internal static EditorPreferences Load()
    {
        try
        {
            if (!File.Exists(PathName) || new FileInfo(PathName).Length > 1024 * 1024) return new();
            var p = JsonSerializer.Deserialize<EditorPreferences>(File.ReadAllText(PathName)) ?? new();
            p.Normalize(); return p;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    internal void Save()
    {
        Normalize(); Directory.CreateDirectory(AppPaths.SettingsDirectory);
        var temporary = PathName + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); File.Move(temporary, PathName, true);
    }
}
