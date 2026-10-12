using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.IO;

namespace Compositor.Desktop;

/// <summary>Every editable field writes only to a draft; the owner commits it after OK.</summary>
internal sealed class PreferencesDialog : DialogWindow
{
    internal static readonly string[] Categories = ["General", "Interface", "Workspace", "Notifications", "Tools", "History", "File Handling", "Export", "Performance", "Image Processing", "Scratch Disks", "Cursors", "Transparency and Gamut", "Units and Rulers", "Guides, Grid and Slices", "Plugins", "Type", "Technology Previews", "Camera Raw"];
    internal EditorPreferences Draft { get; }
    internal string? RelatedAction { get; private set; }
    private readonly ListBox _navigation = new() { MinWidth = 174 };
    private readonly TextBox _search = new() { PlaceholderText = Localize.Text("Search Preferences"), Tag = "Search Preferences" };
    private readonly StackPanel _pages = new() { Spacing = 16 };
    private readonly TextBlock _error = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
    private readonly Dictionary<string, PreferencePage> _sections = [];
    internal string Category => (_navigation.SelectedItem as ListBoxItem)?.Tag as string ?? Categories[0];
    internal PreferencesDialog(string category, EditorPreferences source)
    {
        Draft = source.Copy(); Draft.Language = Localize.Language; Title = Localize.Text("Preferences"); Width = 1080; Height = 760; MinWidth = 780; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var grid = new Grid { ColumnDefinitions = new("190,*,120"), RowDefinitions = new("Auto,*,Auto"), Margin = new Thickness(16), ColumnSpacing = 16, RowSpacing = 12 };
        Grid.SetColumn(_search, 1); grid.Children.Add(_search);
        _navigation.ItemsSource = Categories.Select(c => new ListBoxItem { Content = Localize.Text(c), Tag = c }).ToArray();
        Grid.SetRow(_navigation, 1); grid.Children.Add(_navigation);
        var scroll = new ScrollViewer { Content = _pages, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(scroll, 1); Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
        var buttons = new StackPanel { Spacing = 12 };
        buttons.Children.Add(FormDialog.ActionButton("OK", () => Accept(null)));
        buttons.Children.Add(FormDialog.ActionButton("Cancel", () => Close(false)));
        buttons.Children.Add(FormDialog.ActionButton("Previous", () => Move(-1)));
        buttons.Children.Add(FormDialog.ActionButton("Next", () => Move(1)));
        buttons.Children.Add(FormDialog.ActionButton("Reset Preferences", () => { DraftFromDefaults(); }));
        Grid.SetColumn(buttons, 2); Grid.SetRow(buttons, 1); grid.Children.Add(buttons);
        Grid.SetColumn(_error, 1); Grid.SetRow(_error, 2); grid.Children.Add(_error); Content = grid;
        BuildPages();
        _navigation.SelectionChanged += (_, _) => { if (_search.Text?.Length > 0) _search.Text = ""; Filter(); scroll.Offset = default; };
        _search.TextChanged += (_, _) => { Filter(); scroll.Offset = default; };
        _navigation.SelectedIndex = Math.Max(0, Array.IndexOf(Categories, category)); Filter();
    }
    private void DraftFromDefaults()
    {
        var defaults = new EditorPreferences();
        foreach (var property in typeof(EditorPreferences).GetProperties()) property.SetValue(Draft, property.GetValue(defaults));
        BuildPages(); Filter();
    }
    private void Move(int direction) => _navigation.SelectedIndex = Math.Clamp(_navigation.SelectedIndex + direction, 0, Categories.Length - 1);
    private void Filter()
    {
        var query = _search.Text?.Trim() ?? "";
        var selected = (_navigation.SelectedItem as ListBoxItem)?.Tag as string ?? Categories[0];
        foreach (var (category, page) in _sections) page.IsVisible = query.Length == 0 ? category == selected : page.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase);
        _error.Text = _sections.Values.Any(p => p.IsVisible) ? "" : Localize.Text("No matching preferences.");
    }
    private void Accept(string? relatedAction)
    {
        foreach (var (category, page) in _sections)
        {
            if (page.Valid()) continue;
            _search.Text = ""; _navigation.SelectedIndex = Array.IndexOf(Categories, category);
            _error.Text = Localize.Text("Check the highlighted color or number fields before saving."); return;
        }
        Draft.Normalize(); RelatedAction = relatedAction; Close(true);
    }
    private PreferencePage Page(string category)
    {
        var page = new PreferencePage(category); _sections[category] = page; _pages.Children.Add(page); return page;
    }
    private void Link(PreferencePage page, string action)
    { page.Action(Localize.Text("Save and Continue") + ": " + Localize.Text(action), () => Accept(action)); }
    private async Task AdvancedGraphics()
    {
        var form = new FormDialog("Advanced Graphics Settings", 540, 390);
        var openCl = form.Check("Use OpenCL", false); openCl.IsEnabled = false;
        form.Description("The renderer uses Skia through the platform GPU backend; no OpenCL kernels are installed.");
        var antialias = form.Check("Antialias Guides and Paths", Draft.AntialiasOverlays);
        var deep = form.Check("30-bit Display", false); deep.IsEnabled = false;
        form.Description("The window surface is 8-bit per channel. 16/32-bit document data is preserved separately.");
        var composite = form.Check("GPU Compositing", Draft.AcceleratedCanvas);
        if (await form.ShowDialog<bool>(this)) { Draft.AntialiasOverlays = antialias.IsChecked == true; Draft.AcceleratedCanvas = composite.IsChecked == true; BuildPages(); Filter(); }
    }
    private void BuildPages()
    {
        _sections.Clear(); _pages.Children.Clear(); var p = Draft;
        var page = Page("General");
        page.Check("Resize Images During Placement", p.ResizeOnPlace, v => p.ResizeOnPlace = v);
        page.Check("Create Smart Objects During Placement", p.SmartOnPlace, v => p.SmartOnPlace = v);
        page.Check("Skip Transform During Placement", p.SkipPlacement, v => p.SkipPlacement = v);
        page.Description("Placement defaults apply to newly placed raster images. Existing layers keep their transforms and sampling settings.");
        page.Description("Reset Preferences restores this draft. Cancel discards all changes.");
        page = Page("Interface");
        page.Choice("Color Theme", ["Darkest", "Dark Gray", "Light Gray", "Lightest"], p.Theme, v => p.Theme = v);
        page.Choice("Language", ["English", "简体中文"], (p.Language == "zh-CN" || p.Language == "" && Localize.IsChinese) ? 1 : 0, v => p.Language = v == 1 ? "zh-CN" : "en");
        page.Number("UI Font Size", p.UiFontSize, 10, 18, v => p.UiFontSize = (int)v);
        page.Color("Pasteboard Color", p.Pasteboard, v => p.Pasteboard = v);
        page.Description("Theme, UI font size and language take effect after restarting Compositor.");
        page = Page("Workspace");
        page.Check("Large Document Tabs", p.LargeTabs, v => p.LargeTabs = v);
        page.Description("Documents use tabs. Panel layout and toolbar visibility are stored with the workspace configuration.");
        Link(page, "Customize Toolbar…"); Link(page, "Reset Workspace");
        page = Page("Notifications");
        page.Check("Tooltips", p.Tooltips, v => p.Tooltips = v);
        page.Description("Tooltips apply after restarting. Errors and file conversion reports remain visible. This editor has no advertising, onboarding popups or background notification service.");
        page = Page("Tools");
        page.Check("Zoom with Scroll Wheel", p.WheelZoom, v => p.WheelZoom = v);
        page.Check("Zoom at Pointer", p.ZoomAtPointer, v => p.ZoomAtPointer = v);
        page.Number("Brush Smoothing (%)", p.BrushSmoothing * 100, 0, 100, v => p.BrushSmoothing = v / 100);
        page.Check("Eyedropper Sample Ring", p.SampleRing, v => p.SampleRing = v);
        page.Description("With wheel zoom disabled, the wheel pans; Ctrl-wheel zooms. Keyboard shortcuts can be customized independently.");
        Link(page, "Keyboard Shortcuts…");
        page = Page("History");
        page.Number("History States", p.HistorySteps, 1, 1000, v => p.HistorySteps = (int)v);
        page.Number("History Memory (MB)", p.HistoryMB, 16, 1536, v => p.HistoryMB = (int)v);
        page.Check("Non-linear History", p.NonLinearHistory, v => p.NonLinearHistory = v);
        page.Check("Write Edit History Log", p.HistoryLog, v => p.HistoryLog = v);
        page.Description("The edit log stores timestamps and command names in history.log in the settings folder. It does not store document pixels or provide undo recovery.");
        page = Page("File Handling");
        page.Check("Reload Unmodified Projects after External Changes", p.WatchFiles, v => p.WatchFiles = v);
        page.Number("Recent File Count", p.RecentFiles, 0, 100, v => p.RecentFiles = (int)v);
        page.Check("Save Recovery Copies", p.RecoveryEnabled, v => p.RecoveryEnabled = v);
        page.Number("Recovery Interval (minutes)", p.RecoveryMinutes, 1, 60, v => p.RecoveryMinutes = (int)v);
        page.Description("Recovery copies are background .comp packages in the settings recovery folder. They never replace the original. Open a recovery folder with File > Open; copies remain until you remove them.");
        Link(page, "Open Recovery Folder");
        page.Description("Project saves run in the background. Camera Raw, PSD compression and Adobe compatibility switches do not apply to the .comp writer.");
        page = Page("Export");
        page.Number("JPEG Quality", p.ExportQuality, 1, 100, v => p.ExportQuality = (int)v);
        page.Number("PDF Resolution (pixels/inch)", p.PdfResolution, 1, 9600, v => p.PdfResolution = v);
        page.Check("Export Transparency", p.ExportTransparency, v => p.ExportTransparency = v);
        page.Description("These are defaults for Export As and layer PNG export. JPEG is opaque. Export asks for a destination; documents are not silently overwritten.");
        page = Page("Performance");
        page.Check("Use Graphics Processor", p.UseGpu, v => p.UseGpu = v);
        page.Number("GPU Cache (MB)", p.GpuCacheMB, 128, 2048, v => p.GpuCacheMB = (int)v);
        page.Action("Advanced Graphics Settings…", () => _ = AdvancedGraphics());
        page.Description("GPU backend and cache changes require a restart. Cache is a ceiling, not a memory reservation. Complex effects use the reference compositor.");
        using (var process = System.Diagnostics.Process.GetCurrentProcess()) page.Description($"Process Memory: {process.WorkingSet64 / 1048576:N0} MB");
        page.Description($"Managed Memory: {GC.GetTotalMemory(false) / 1048576:N0} MB");
        page.Description(CanvasView.LastFrameUsedGpu ? "Last accelerated frame: GPU" : "No GPU frame has been observed in this session.");
        page.Action("Large Image Preset", () => { p.GpuCacheMB = 1024; p.HistorySteps = 50; p.HistoryMB = 512; p.UseGpu = true; p.AcceleratedCanvas = true; BuildPages(); Filter(); });
        page.Action("Low Memory Preset", () => { p.GpuCacheMB = 256; p.HistorySteps = 30; p.HistoryMB = 128; BuildPages(); Filter(); });
        page = Page("Image Processing");
        page.Description("PSD decoding and saving run on background tasks. Selection, removal and filters are processed on this device; there is no automatic cloud upload.");
        Link(page, "Color Settings…"); Link(page, "OpenColorIO Settings…");
        page = Page("Scratch Disks");
        page.Description("Layer pixels currently reside in memory. Disk-backed tile eviction is not implemented, so selecting scratch drives is unavailable. Recovery packages use the settings volume.");
        page.Description(AppPaths.SettingsDirectory);
        try { var drive = new DriveInfo(Path.GetPathRoot(AppPaths.SettingsDirectory)!); page.Description($"{drive.Name} — {drive.AvailableFreeSpace / 1073741824d:N1} GB free"); } catch (IOException) { }
        page = Page("Cursors");
        page.Check("Pen-shaped Cursor", p.PenCursor, v => p.PenCursor = v);
        page.Check("Precise Brush Crosshair", p.PreciseCursor, v => p.PreciseCursor = v);
        page.Check("Crosshair in Brush Tip", p.BrushCrosshair, v => p.BrushCrosshair = v);
        page.Check("Hide Cursor While Painting", p.HidePaintingCursor, v => p.HidePaintingCursor = v);
        page.Number("Brush Outline Width", p.BrushOutlineWidth, 1, 3, v => p.BrushOutlineWidth = (int)v);
        page = Page("Transparency and Gamut");
        page.Check("Show Transparency", p.ShowTransparency, v => p.ShowTransparency = v);
        page.Number("Checker Size", p.CheckerSize, 2, 32, v => p.CheckerSize = (int)v);
        page.Color("Checker Light Color", p.CheckerLight, v => p.CheckerLight = v);
        page.Color("Checker Dark Color", p.CheckerDark, v => p.CheckerDark = v);
        page.Description("Checker colors affect the display only. Gamut warnings require an output ICC proofing transform and are not available.");
        page = Page("Units and Rulers");
        page.Choice("Ruler Units", ["Pixels", "Inches", "Centimeters", "Points"], p.RulerUnit, v => p.RulerUnit = v);
        page.Number("New Document Resolution", p.NewResolution, 1, 9600, v => p.NewResolution = v);
        page.Description("Existing document resolution is unchanged. Type size is stored in document pixels; ruler conversion uses each document's resolution.");
        page = Page("Guides, Grid and Slices");
        page.Color("Guide Color", p.GuideColor, v => p.GuideColor = v);
        page.Choice("Guide Style", ["Solid", "Dashed", "Dotted"], p.GuideStyle, v => p.GuideStyle = v);
        page.Color("Grid Color", p.GridColor, v => p.GridColor = v);
        page.Color("Path and Control Color", p.PathColor, v => p.PathColor = v);
        page.Number("Path and Control Width", p.PathWidth, 1, 3, v => p.PathWidth = (int)v);
        page.Check("Show Slice Numbers", p.ShowSliceNumbers, v => p.ShowSliceNumbers = v);
        Link(page, "Grid Settings…");
        page = Page("Plugins");
        page.Description("Compositor plugins use the documented local plugin host. Adobe generators, CEP extensions and remote Photoshop servers are not compatible.");
        Link(page, "Manage Plugins…");
        page = Page("Type");
        page.Text("Default Font", p.DefaultFont, v => p.DefaultFont = v);
        page.Number("Default Type Size (pixels)", p.DefaultFontSize, 1, 1000, v => p.DefaultFontSize = v);
        page.Check("Use Escape to Commit Text", p.EscapeCommitsText, v => p.EscapeCommitsText = v);
        page.Check("Fill New Type Layers with Placeholder Text", p.PlaceholderText, v => p.PlaceholderText = v);
        page.Description("Font fallback is provided by the text renderer. IME composition retains priority over Escape and Enter shortcuts.");
        page = Page("Technology Previews");
        page.Check("Accelerated Layer Display", p.AcceleratedCanvas, v => p.AcceleratedCanvas = v);
        page.Description("Replays supported layers as cached Skia commands. Turn off to use the reference compositor. Saving and export always use the reference renderer.");
        page.Description("HDR monitor output, Adobe Preserve Details 2.0 and legacy GPU modes are not implemented. Deep color data is retained by the existing 16/32-bit document pipeline.");
        page = Page("Camera Raw");
        page.Description("Camera Raw parameters belong to each layer. Open the layer adjustment panel to edit them; raw import development remains per file.");
        Link(page, "Camera Raw Filter…");
    }
}

internal sealed class PreferencePage : StackPanel
{
    private readonly List<Func<bool>> _validators = [];
    internal string SearchText { get; private set; }
    internal PreferencePage(string title)
    {
        Spacing = 14; SearchText = title + " " + Localize.Text(title);
        Children.Add(new TextBlock { Text = Localize.Text(title), FontSize = 19, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
    }
    internal bool Valid() => _validators.Select(check => check()).ToArray().All(valid => valid);
    private void Index(string label) => SearchText += " " + label + " " + Localize.Text(label);
    internal void Description(string text) { Index(text); Children.Add(new TextBlock { Text = Localize.Text(text), TextWrapping = TextWrapping.Wrap, Foreground = Skin.SecondaryBrush }); }
    internal void Action(string text, Action action) { Index(text); Children.Add(FormDialog.ActionButton(text, action)); }
    private void Row(string label, Control value)
    {
        Index(label); var grid = new Grid { ColumnDefinitions = new("220,*"), ColumnSpacing = 12 };
        grid.Children.Add(new TextBlock { Text = Localize.Text(label), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(value, 1); value.Tag = label; value.MinHeight = 30; grid.Children.Add(value); Children.Add(grid);
    }
    internal void Check(string text, bool value, Action<bool> changed)
    {
        Index(text); var box = new CheckBox { Content = new TextBlock { Text = Localize.Text(text), TextWrapping = TextWrapping.Wrap }, IsChecked = value, Tag = text };
        box.IsCheckedChanged += (_, _) => changed(box.IsChecked == true); Children.Add(box);
    }
    internal void Number(string text, double value, double min, double max, Action<double> changed)
    {
        var input = new NumericUpDown { Value = (decimal)value, Minimum = (decimal)min, Maximum = (decimal)max, ShowButtonSpinner = false };
        input.ValueChanged += (_, _) => { if (input.Value is { } n) changed((double)n); }; Row(text, input);
        _validators.Add(() => { var valid = input.Value is { } n && n >= (decimal)min && n <= (decimal)max; input.BorderBrush = valid ? Skin.BorderControlBrush : Brushes.OrangeRed; return valid; });
    }
    internal void Choice(string text, string[] options, int selected, Action<int> changed)
    { var input = new ComboBox { ItemsSource = options.Select(Localize.Text).ToArray(), SelectedIndex = selected }; input.SelectionChanged += (_, _) => { if (input.SelectedIndex >= 0) changed(input.SelectedIndex); }; Row(text, input); }
    internal void Text(string text, string value, Action<string> changed)
    { var input = new TextBox { Text = value, MaxLength = 200 }; input.TextChanged += (_, _) => changed(input.Text ?? ""); Row(text, input); _validators.Add(() => !string.IsNullOrWhiteSpace(input.Text)); }
    internal void Color(string text, string value, Action<string> changed)
    {
        var input = new TextBox { Text = value, MaxLength = 7 }; var swatch = new Border { Width = 30, Height = 30, Background = new SolidColorBrush(Avalonia.Media.Color.Parse(value)) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }; input.Width = 125; input.Tag = text; row.Children.Add(input); row.Children.Add(swatch);
        input.TextChanged += (_, _) => { var valid = EditorPreferences.ValidColor(input.Text); input.BorderBrush = valid ? Skin.BorderControlBrush : Brushes.OrangeRed; if (valid) { changed(input.Text!); swatch.Background = new SolidColorBrush(Avalonia.Media.Color.Parse(input.Text!)); } };
        Row(text, row); _validators.Add(() => EditorPreferences.ValidColor(input.Text));
    }
}
