using System.Text.Json;
using Compositor.Core.IO;

namespace Compositor.Desktop;

internal sealed class EditorPreferences
{
    internal static string PathName => Path.Combine(AppPaths.SettingsDirectory, "preferences.json");
    internal static EditorPreferences Current { get; private set; } = Load();
    public List<CropPreset> CropPresets { get; set; } = [];
    public int HistorySteps { get; set; } = 100;
    public bool MoveAutoSelect { get; set; } = true;
    public bool PresetCurrentOnly { get; set; } = true;
    public bool PresetSortByTool { get; set; } = true;
    public int PresetListSize { get; set; } = 1;
    public bool MoveSelectGroup { get; set; }
    public bool MoveHoverBounds { get; set; } = true;
    public bool MoveExpandGroup { get; set; } = true;
    public bool MoveLayerHoverBounds { get; set; } = true;
    public bool ArtboardAutoNest { get; set; } = true;
    public bool ArtboardAutoCanvas { get; set; } = true;
    public bool ArtboardKeepPosition { get; set; } = true;
    public bool ArtboardShrinkOnSave { get; set; } = true;
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
    public ColorManagementSettings ColorSettings { get; set; } = new();
    public OcioSettings Ocio { get; set; } = new();
    public HashSet<string> HiddenMenus { get; set; } = [];
    public string Language { get; set; } = "";
    public bool LockWorkspace { get; set; }
    public string ActiveWorkspace { get; set; } = "Essentials";
    public Dictionary<string, List<string>> Actions { get; set; } = [];
    public Dictionary<string, WorkspaceLayout> Workspaces { get; set; } = [];
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
    public string PasteboardPreset { get; set; } = "Default";
    public string CustomPasteboard { get; set; } = "#282828";
    public string Pasteboard { get; set; } = "#282828";
    public int GuideStyle { get; set; }
    public string GuideColor { get; set; } = "#00FFFF";
    public string GridColor { get; set; } = "#B3B3B3";
    public string PathColor { get; set; } = "#1473E6";
    public double PathWidth { get; set; } = 1;
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
    public bool EscapeCommitsText { get; set; } = true;
    public bool PlaceholderText { get; set; }
    public double DefaultFontSize { get; set; } = 48;
    public double NewResolution { get; set; } = 72;
    public Compositor.Core.Document.MaskRefinementSettings? MaskRefinementPreset { get; set; }
    public Compositor.Core.Format.LayerTextStyle? DefaultTypeStyle { get; set; }
    public Dictionary<string, Compositor.Core.Format.LayerTextStyle> CharacterStyles { get; set; } = [];
    public Dictionary<string, Compositor.Core.Format.LayerTextStyle> ParagraphStyles { get; set; } = [];
    public HashSet<string> FavoriteFonts { get; set; } = [];
    public int FontPreviewSize { get; set; } = 3;
    public int LayerThumbnailSize { get; set; } = 32;
    public bool ThumbnailLayerBounds { get; set; } = true;
    public string MaskOverlayColor { get; set; } = "#FF0000";
    public double MaskOverlayOpacity { get; set; } = 50;
    public bool ShowSliceNumbers { get; set; } = true;
    internal EditorPreferences Copy() => JsonSerializer.Deserialize<EditorPreferences>(JsonSerializer.Serialize(this))!;
    internal static void Commit(EditorPreferences draft) { draft.Normalize(); draft.Save(); Current = draft.Copy(); }
    internal void Normalize()
    {
        if (ColorSettings?.IsValid != true) ColorSettings = new();
        if (Ocio?.IsValid != true) Ocio = new();
        CropPresets = (CropPresets ?? []).Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Name) && p.Name.Length <= 200 && p.Mode is >= 0 and <= 1 && double.IsFinite(p.Width) && p.Width is >= 0 and <= 30000 && double.IsFinite(p.Height) && p.Height is >= 0 and <= 30000 && double.IsFinite(p.Resolution) && p.Resolution is >= 0 and <= 9600).Take(200).ToList();
        CropPresets = (CropPresets ?? []).Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Name) && p.Name.Length <= 200 && p.Mode is >= 0 and <= 1 && double.IsFinite(p.Width) && p.Width is >= 0 and <= 30000 && double.IsFinite(p.Height) && p.Height is >= 0 and <= 30000 && double.IsFinite(p.Resolution) && p.Resolution is >= 0 and <= 9600).Take(200).ToList();
        HistorySteps = Math.Clamp(HistorySteps, 1, 1000); HistoryMB = Math.Clamp(HistoryMB, 16, 1536);
        BrushSmoothing = Finite(BrushSmoothing, 0, 1, 0);
        ExportQuality = Math.Clamp(ExportQuality, 1, 100); RulerUnit = Math.Clamp(RulerUnit, 0, 3);
        PdfResolution = Finite(PdfResolution, 1, 9600, 300);
        DefaultFontSize = Finite(DefaultFontSize, 1, 1000, 48); NewResolution = Finite(NewResolution, 1, 9600, 72);
        DefaultFont = string.IsNullOrWhiteSpace(DefaultFont) ? "Arial" : DefaultFont[..Math.Min(200, DefaultFont.Length)];
        GuideStyle = Math.Clamp(GuideStyle, 0, 2); Theme = Math.Clamp(Theme, 0, 3); UiFontSize = Math.Clamp(UiFontSize, 10, 18);
        BrushOutlineWidth = Math.Clamp(BrushOutlineWidth, 1, 3); PathWidth = double.IsFinite(PathWidth) ? Math.Clamp(PathWidth, .25, 10) : 1;
        CheckerSize = Math.Clamp(CheckerSize, 2, 32); GpuCacheMB = Math.Clamp(GpuCacheMB, 128, 2048);
        RecentFiles = Math.Clamp(RecentFiles, 0, 100); RecoveryMinutes = Math.Clamp(RecoveryMinutes, 1, 60);
        CheckerLight = Color(CheckerLight, "#454545"); CheckerDark = Color(CheckerDark, "#383838");
        CustomPasteboard = Color(CustomPasteboard, "#282828");
        Actions ??= []; foreach (var key in Actions.Keys.ToArray()) if (key.Length > 100 || Actions[key] is null || Actions[key].Count > 200 || Actions.Count > 100) Actions.Remove(key);
        Workspaces ??= []; foreach (var key in Workspaces.Keys.ToArray()) if (string.IsNullOrWhiteSpace(key) || key.Length > 100 || Workspaces[key] is null || Workspaces[key].Panels is null || Workspaces.Count > 50) Workspaces.Remove(key);
        ActiveWorkspace ??= "Essentials";
        PasteboardPreset = PasteboardPreset is "Default" or "Black" or "Dark Gray" or "Medium Gray" or "Light Gray" or "Custom" ? PasteboardPreset : "Default";
        Pasteboard = Color(Pasteboard, "#282828"); GuideColor = Color(GuideColor, "#00FFFF");
        GridColor = Color(GridColor, "#B3B3B3"); PathColor = Color(PathColor, "#1473E6");
        Language = Language is "en" or "zh-CN" ? Language : "";
        CharacterStyles ??= []; ParagraphStyles ??= [];
        foreach (var styles in new[] { CharacterStyles, ParagraphStyles }) foreach (var key in styles.Keys.ToArray())
            if (styles[key] is not { IsValid: true } || key.Length > 100 || styles.Count > 200) styles.Remove(key);
        if (DefaultTypeStyle is not null && !DefaultTypeStyle.IsValid) DefaultTypeStyle = null;
        HiddenMenus ??= []; FavoriteFonts ??= []; FavoriteFonts = FavoriteFonts.Where(f => !string.IsNullOrWhiteSpace(f) && f.Length <= 200).Take(500).ToHashSet(); FontPreviewSize = Math.Clamp(FontPreviewSize, 0, 5);
        LayerThumbnailSize = LayerThumbnailSize is 0 or 32 or 48 or 64 ? LayerThumbnailSize : 32;
        MaskOverlayColor = Color(MaskOverlayColor, "#FF0000"); MaskOverlayOpacity = Finite(MaskOverlayOpacity, 0, 100, 50);
    }
    private static double Finite(double n, double min, double max, double fallback) => double.IsFinite(n) ? Math.Clamp(n, min, max) : fallback;
    internal static bool ValidColor(string? value) => value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);
    private static string Color(string value, string fallback) => ValidColor(value) ? value : fallback;
    internal static EditorPreferences Load()
    {
        try
        {
            if (!File.Exists(PathName) || new FileInfo(PathName).Length > 32 * 1024 * 1024) return new();
            var json = File.ReadAllText(PathName); var p = JsonSerializer.Deserialize<EditorPreferences>(json) ?? new();
            using var fields = JsonDocument.Parse(json);
            if (!fields.RootElement.TryGetProperty(nameof(PasteboardPreset), out _) && p.Pasteboard != "#282828") { p.PasteboardPreset = "Custom"; p.CustomPasteboard = p.Pasteboard; }
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
