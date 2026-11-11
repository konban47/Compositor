using System.Text.Json;
using Compositor.Core.IO;

namespace Compositor.Desktop;

internal sealed class EditorPreferences
{
    internal static string PathName => Path.Combine(AppPaths.SettingsDirectory, "preferences.json");
    internal static EditorPreferences Current { get; private set; } = Load();
    /// <summary>General preferences' Image Interpolation as the Resample lists offer it: Bicubic Automatic is plain Bicubic there.</summary>
    internal static int DefaultResample => Current.Interpolation == 5 ? 2 : Current.Interpolation;
    internal double PrintResolutionPerInch => Math.Round(PrintResolutionUnit == 1 ? PrintResolution * 2.54 : PrintResolution, 2);
    internal double ScreenResolutionPerInch => Math.Round(ScreenResolutionUnit == 1 ? NewResolution * 2.54 : NewResolution, 2);
    /// <summary>Undo memory: the History Memory ceiling, held inside the share of the machine's memory Performance preferences allow.</summary>
    internal int HistoryBytes => (int)Math.Min(HistoryMB, Math.Max(16, MachineInfo.MemoryMB * MemoryPercent / 100)) * 1024 * 1024;
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
    public Dictionary<string, string> MenuColors { get; set; } = [];
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
    // Photoshop's own preference pages: General.
    public int ColorPicker { get; set; }
    public int Interpolation { get; set; } = 5;
    public bool BeepWhenDone { get; set; }
    public bool SaveToOriginalFolder { get; set; } = true;
    public bool NoCopySuffix { get; set; }
    public bool ResetOnQuit { get; set; }
    // Interface: the pasteboard and the edge around the picture in each screen mode. Standard Screen Mode keeps PasteboardPreset.
    public string FullMenusPreset { get; set; } = "Default";
    public string FullMenusCustom { get; set; } = "#282828";
    public string FullPreset { get; set; } = "Black";
    public string FullCustom { get; set; } = "#000000";
    public string ArtboardPreset { get; set; } = "Default";
    public string ArtboardCustom { get; set; } = "#282828";
    /// <summary>0 Line, 1 Drop Shadow, 2 None, for Standard, Full Screen with Menus, Full Screen and Artboards.</summary>
    public int[] ScreenBorders { get; set; } = [1, 1, 2, 0];
    public bool ShowMenuColors { get; set; } = true;
    // Tools.
    public bool ZoomToCenter { get; set; }
    // History.
    public int HistoryLogTarget { get; set; } = 1;
    public string HistoryLogPath { get; set; } = "";
    public int HistoryLogDetail { get; set; } = 1;
    // File handling and performance.
    public int MemoryPercent { get; set; } = 70;
    public List<string> ScratchDisks { get; set; } = [];
    // Cursors.
    /// <summary>0 Standard, 1 Precise, 2 Normal Brush Tip, 3 Full Size Brush Tip.</summary>
    public int PaintingCursor { get; set; } = 3;
    public bool CrosshairWhilePainting { get; set; }
    // Transparency and gamut.
    public string CheckerPreset { get; set; } = "Custom";
    // Units and rulers.
    public int TypeUnit { get; set; }
    public double ColumnWidth { get; set; } = 180;
    public int ColumnUnit { get; set; } = 3;
    public double ColumnGutter { get; set; } = 12;
    public int GutterUnit { get; set; } = 3;
    public double PrintResolution { get; set; } = 300;
    public int PrintResolutionUnit { get; set; }
    public int ScreenResolutionUnit { get; set; }
    public bool TraditionalPoints { get; set; }
    // Guides, grid and slices.
    public string ArtboardGuideColor { get; set; } = "#00A3FF";
    public int ArtboardGuideStyle { get; set; }
    public string SmartGuideColor { get; set; } = "#FF00FF";
    public string SliceColor { get; set; } = "#4A9EFF";
    public int GridStyle { get; set; }
    public double GridEvery { get; set; } = 25;
    public int GridUnit { get; set; }
    public int GridSubdivisions { get; set; } = 4;
    // Type.
    public bool SmartQuotes { get; set; } = true;
    public int RecentFontCount { get; set; } = 10;
    public string GamutColor { get; set; } = "#808080";
    public double GamutOpacity { get; set; } = 100;
    public CameraRawPreferences CameraRaw { get; set; } = new();
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
        ExportQuality = Math.Clamp(ExportQuality, 1, 100);
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
        HiddenMenus ??= []; MenuColors = (MenuColors ?? []).Where(p => p.Key.Length <= 1000 && p.Value != "None" && MenuLook.Palette.Contains(p.Value)).Take(2000).ToDictionary(p => p.Key, p => p.Value); FavoriteFonts ??= []; FavoriteFonts = FavoriteFonts.Where(f => !string.IsNullOrWhiteSpace(f) && f.Length <= 200).Take(500).ToHashSet(); FontPreviewSize = Math.Clamp(FontPreviewSize, 0, 5);
        LayerThumbnailSize = LayerThumbnailSize is 0 or 32 or 48 or 64 ? LayerThumbnailSize : 32;
        MaskOverlayColor = Color(MaskOverlayColor, "#FF0000"); MaskOverlayOpacity = Finite(MaskOverlayOpacity, 0, 100, 50);
        ColorPicker = Math.Clamp(ColorPicker, 0, 1); Interpolation = Math.Clamp(Interpolation, 0, 5);
        static string Preset(string? value, string fallback) => value is "Default" or "Black" or "Dark Gray" or "Medium Gray" or "Light Gray" or "Custom" ? value : fallback;
        FullMenusPreset = Preset(FullMenusPreset, "Default"); FullPreset = Preset(FullPreset, "Black"); ArtboardPreset = Preset(ArtboardPreset, "Default");
        FullMenusCustom = Color(FullMenusCustom, "#282828"); FullCustom = Color(FullCustom, "#000000"); ArtboardCustom = Color(ArtboardCustom, "#282828");
        ScreenBorders = ScreenBorders is { Length: 4 } borders && borders.All(b => b is >= 0 and <= 2) ? borders : [1, 1, 2, 0];
        HistoryLogTarget = Math.Clamp(HistoryLogTarget, 0, 2); HistoryLogDetail = Math.Clamp(HistoryLogDetail, 0, 2);
        HistoryLogPath = HistoryLogPath is { Length: <= 1000 } path && (path.Length == 0 || Path.IsPathFullyQualified(path)) ? path : "";
        MemoryPercent = Math.Clamp(MemoryPercent, 10, 100);
        ScratchDisks = (ScratchDisks ?? []).Where(d => !string.IsNullOrWhiteSpace(d) && d.Length <= 10).Distinct().Take(26).ToList();
        PaintingCursor = Math.Clamp(PaintingCursor, 0, 3); PreciseCursor = PaintingCursor == 1;
        CheckerPreset = CheckerPresets.Any(p => p.Name == CheckerPreset) ? CheckerPreset : "Custom";
        RulerUnit = Math.Clamp(RulerUnit, 0, 6); TypeUnit = Math.Clamp(TypeUnit, 0, 2);
        ColumnWidth = Finite(ColumnWidth, .04, 5000, 180); ColumnGutter = Finite(ColumnGutter, 0, 5000, 12);
        ColumnUnit = Math.Clamp(ColumnUnit, 0, 4); GutterUnit = Math.Clamp(GutterUnit, 0, 4);
        PrintResolution = Finite(PrintResolution, 1, 9600, 300); PrintResolutionUnit = Math.Clamp(PrintResolutionUnit, 0, 1); ScreenResolutionUnit = Math.Clamp(ScreenResolutionUnit, 0, 1);
        ArtboardGuideColor = Color(ArtboardGuideColor, "#00A3FF"); SmartGuideColor = Color(SmartGuideColor, "#FF00FF"); SliceColor = Color(SliceColor, "#4A9EFF");
        ArtboardGuideStyle = Math.Clamp(ArtboardGuideStyle, 0, 1); GridStyle = Math.Clamp(GridStyle, 0, 2);
        GridEvery = Finite(GridEvery, .01, 10000, 25); GridUnit = Math.Clamp(GridUnit, 0, 6); GridSubdivisions = Math.Clamp(GridSubdivisions, 1, 100);
        RecentFontCount = Math.Clamp(RecentFontCount, 0, 100); GamutColor = Color(GamutColor, "#808080"); GamutOpacity = Finite(GamutOpacity, 1, 100, 100);
        CameraRaw ??= new(); CameraRaw.Normalize();
    }
    /// <summary>Photoshop's Transparency grid colors: each a light and a dark square.</summary>
    /// <remarks>Kept in a class of its own: <see cref="Current"/> loads and normalizes during this class's static
    /// initialization, before any static field declared below it is set.</remarks>
    internal static (string Name, string Light, string Dark)[] CheckerPresets => CheckerTable.Presets;
    private static class CheckerTable
    {
        internal static readonly (string Name, string Light, string Dark)[] Presets =
        [
            ("Light", "#FFFFFF", "#CCCCCC"), ("Medium", "#CCCCCC", "#999999"), ("Dark", "#666666", "#333333"), ("Red", "#FFFFFF", "#FFCCCC"),
            ("Orange", "#FFFFFF", "#FFE0CC"), ("Green", "#FFFFFF", "#CCFFCC"), ("Blue", "#FFFFFF", "#CCE5FF"), ("Purple", "#FFFFFF", "#E6CCFF"), ("Custom", "", ""),
        ];
    }
    /// <summary>The pasteboard each of Photoshop's named shades stands for.</summary>
    internal static string ShadeColor(string preset, string custom) => preset switch
    { "Black" => "#000000", "Dark Gray" => "#282828", "Medium Gray" => "#808080", "Light Gray" => "#BFBFBF", "Custom" => custom, _ => "#282828" };
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
            if (!fields.RootElement.TryGetProperty(nameof(PaintingCursor), out _) && p.PreciseCursor) p.PaintingCursor = 1;
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

/// <summary>Camera Raw Preferences: how raw (and, when asked, JPEG and TIFF) files are developed and opened.</summary>
internal sealed class CameraRawPreferences
{
    public int Theme { get; set; }
    public int PanelBehavior { get; set; }
    public bool CompactLayout { get; set; }
    public bool RichTooltips { get; set; } = true;
    public int ClickZoom { get; set; } = 6;
    public int Sidecar { get; set; }
    public int JpegHandling { get; set; } = 1;
    public int TiffHandling { get; set; } = 1;
    public bool PreferForRaw { get; set; } = true;
    public int GpuMode { get; set; }
    public double CacheGB { get; set; } = 5;
    public string CacheFolder { get; set; } = "";
    public int GlobalDefault { get; set; }
    public bool HdrDefault { get; set; }
    public List<CameraRawWorkflow> WorkflowPresets { get; set; } = [];
    public CameraRawWorkflow Workflow { get; set; } = new();
    internal void Normalize()
    {
        Theme = Math.Clamp(Theme, 0, 4); PanelBehavior = Math.Clamp(PanelBehavior, 0, 2); ClickZoom = Math.Clamp(ClickZoom, 0, 11); Sidecar = Math.Clamp(Sidecar, 0, 2);
        JpegHandling = Math.Clamp(JpegHandling, 0, 2); TiffHandling = Math.Clamp(TiffHandling, 0, 2); GpuMode = Math.Clamp(GpuMode, 0, 1); GlobalDefault = Math.Clamp(GlobalDefault, 0, 1);
        CacheGB = double.IsFinite(CacheGB) ? Math.Clamp(CacheGB, 1, 200) : 5;
        CacheFolder = CacheFolder is { Length: <= 1000 } folder && (folder.Length == 0 || Path.IsPathFullyQualified(folder)) ? folder : "";
        Workflow ??= new(); Workflow.Normalize();
        WorkflowPresets = (WorkflowPresets ?? []).Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Name) && p.Name.Length <= 100).Take(100).ToList();
        foreach (var preset in WorkflowPresets) preset.Normalize();
    }
    /// <summary>Where Camera Raw keeps what it caches.</summary>
    internal string CachePath => CacheFolder.Length > 0 ? CacheFolder : Path.Combine(AppPaths.SettingsDirectory, "CameraRaw", "Cache");
}

/// <summary>Camera Raw's Workflow options: what an opened raw image is sized, sharpened and opened as.</summary>
internal sealed class CameraRawWorkflow
{
    public string Name { get; set; } = "";
    public int ColorSpace { get; set; } = 3;
    public int Depth { get; set; } = 1;
    public bool Resize { get; set; }
    /// <summary>0 Width and Height, 1 Dimensions, 2 Long Side, 3 Short Side, 4 Megapixels, 5 Percentage.</summary>
    public int FitMode { get; set; }
    public bool DontEnlarge { get; set; }
    public double Width { get; set; } = 1000;
    public double Height { get; set; } = 1000;
    public int SizeUnit { get; set; }
    public double Resolution { get; set; } = 300;
    public int ResolutionUnit { get; set; }
    public bool Sharpen { get; set; }
    public int SharpenFor { get; set; }
    public int SharpenAmount { get; set; } = 1;
    public bool SmartObject { get; set; }
    internal void Normalize()
    {
        ColorSpace = Math.Clamp(ColorSpace, 0, 5); Depth = Math.Clamp(Depth, 0, 1); FitMode = Math.Clamp(FitMode, 0, 5); SizeUnit = Math.Clamp(SizeUnit, 0, 2);
        static double Clean(double n, double min, double max, double fallback) => double.IsFinite(n) ? Math.Clamp(n, min, max) : fallback;
        Width = Clean(Width, .01, 65000, 1000); Height = Clean(Height, .01, 65000, 1000); Resolution = Clean(Resolution, 1, 9600, 300);
        ResolutionUnit = Math.Clamp(ResolutionUnit, 0, 1); SharpenFor = Math.Clamp(SharpenFor, 0, 2); SharpenAmount = Math.Clamp(SharpenAmount, 0, 2);
    }
    internal CameraRawWorkflow Copy() => (CameraRawWorkflow)MemberwiseClone();
    /// <summary>The pixel size an image of <paramref name="width"/> by <paramref name="height"/> is opened at.</summary>
    internal (int Width, int Height) Fit(int width, int height)
    {
        if (!Resize) return (width, height);
        var perPixel = SizeUnit switch { 1 => ResolutionPerInch, 2 => ResolutionPerInch / 2.54, _ => 1 };
        double w = Width * perPixel, h = Height * perPixel;
        var scale = FitMode switch
        {
            0 => Math.Min(w / width, h / height),
            1 => Math.Min(Math.Max(w, h) / Math.Max(width, height), Math.Min(w, h) / Math.Min(width, height)),
            2 => w / Math.Max(width, height),
            3 => w / Math.Min(width, height),
            4 => Math.Sqrt(Width * 1_000_000 / ((double)width * height)),
            _ => Width / 100,
        };
        if (DontEnlarge) scale = Math.Min(1, scale);
        if (!double.IsFinite(scale) || scale <= 0) return (width, height);
        return (Math.Clamp((int)Math.Round(width * scale), 1, 30000), Math.Clamp((int)Math.Round(height * scale), 1, 30000));
    }
    internal double ResolutionPerInch => ResolutionUnit == 1 ? Resolution * 2.54 : Resolution;
}
