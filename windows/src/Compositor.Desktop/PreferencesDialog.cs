using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Compositor.Core.IO;

namespace Compositor.Desktop;

/// <summary>
/// Photoshop's Preferences, page for page: the categories down the left, framed groups in the middle, OK, Cancel, Previous and
/// Next down the right and the search field above. Every editable field writes only to a draft; the owner commits it after OK.
/// A setting Compositor has no counterpart for is shown as Photoshop shows it, but disabled, its tooltip saying why.
/// </summary>
internal sealed class PreferencesDialog : DialogWindow
{
    internal static readonly string[] Categories = ["General", "Interface", "Workspace", "Notifications", "Tools", "History", "File Handling", "Export", "Performance", "Image Processing", "Scratch Disks", "Cursors", "Transparency and Gamut", "Units and Rulers", "Guides, Grid and Slices", "Plug-Ins", "Type", "Technology Previews"];
    internal EditorPreferences Draft { get; }
    internal string? RelatedAction { get; private set; }
    private readonly ListBox _navigation = new() { Background = Brushes.Transparent, Tag = "preferences-categories" };
    private readonly TextBox _search = new() { PlaceholderText = Localize.Text("Search Preferences"), Tag = "Search Preferences", Width = 280, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly StackPanel _pages = new() { Spacing = 18 };
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _error = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
    private readonly Dictionary<string, PreferencePage> _sections = [];
    internal string Category => (_navigation.SelectedItem as ListBoxItem)?.Tag as string ?? Categories[0];
    internal PreferencesDialog(string category, EditorPreferences source)
    {
        Draft = source.Copy(); Draft.Language = Localize.Language; Title = Localize.Text("Preferences"); Width = 1120; Height = 800; MinWidth = 920; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var grid = new Grid { ColumnDefinitions = new("170,*,Auto"), RowDefinitions = new("Auto,*,Auto"), Margin = new Thickness(18, 12, 18, 14), ColumnSpacing = 22, RowSpacing = 10 };
        Grid.SetColumn(_search, 1); Grid.SetColumnSpan(_search, 2); grid.Children.Add(_search);
        _navigation.ItemsSource = Categories.Select(c => new ListBoxItem { Content = Localize.Text(c), Tag = c, Padding = new Thickness(8, 5), FontSize = 13 }).ToArray();
        var navigation = new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Background = Skin.SurfaceDarkBrush, Child = new ScrollViewer { Content = _navigation, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        Grid.SetRow(navigation, 1); grid.Children.Add(navigation);
        _scroll = new ScrollViewer { Content = _pages, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 14, 0) };
        Grid.SetColumn(_scroll, 1); Grid.SetRow(_scroll, 1); grid.Children.Add(_scroll);
        var ok = FormDialog.ActionButton("OK", () => Accept(null)); ok.IsDefault = true;
        var cancel = FormDialog.ActionButton("Cancel", () => Close(false)); cancel.IsCancel = true;
        var buttons = DialogKit.ActionColumn(ok, cancel, this.Mnemonic(FormDialog.ActionButton("Previous", () => Move(-1)), 'P'), this.Mnemonic(FormDialog.ActionButton("Next", () => Move(1)), 'N'));
        buttons.Margin = default; Grid.SetColumn(buttons, 2); Grid.SetRow(buttons, 1); grid.Children.Add(buttons);
        Grid.SetColumn(_error, 1); Grid.SetRow(_error, 2); grid.Children.Add(_error); Content = grid;
        BuildPages();
        _navigation.SelectionChanged += (_, _) => { if (_search.Text?.Length > 0) _search.Text = ""; Filter(); _scroll.Offset = default; };
        _search.TextChanged += (_, _) => { Filter(); _scroll.Offset = default; };
        _navigation.SelectedIndex = Math.Max(0, Array.IndexOf(Categories, category)); Filter();
    }
    private void Rebuild() { var offset = _scroll.Offset; BuildPages(); Filter(); _scroll.Offset = offset; }
    private void Move(int direction) => _navigation.SelectedIndex = (_navigation.SelectedIndex + direction + Categories.Length) % Categories.Length;
    private void Filter()
    {
        var query = _search.Text?.Trim() ?? "";
        var selected = (_navigation.SelectedItem as ListBoxItem)?.Tag as string ?? Categories[0];
        foreach (var (category, page) in _sections) { page.IsVisible = query.Length == 0 ? category == selected : page.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase); page.Heading.IsVisible = query.Length > 0; }
        _error.Text = _sections.Values.Any(p => p.IsVisible) ? "" : Localize.Text("No matching preferences.");
    }
    private void Accept(string? relatedAction)
    {
        foreach (var (category, page) in _sections)
        {
            if (page.Valid()) continue;
            _search.Text = ""; _navigation.SelectedIndex = Array.IndexOf(Categories, category);
            _error.Text = Localize.Text("Check the highlighted fields before saving."); return;
        }
        Draft.Normalize(); RelatedAction = relatedAction; Close(true);
    }
    private PreferencePage Page(string category)
    {
        var page = new PreferencePage(category, this); _sections[category] = page; _pages.Children.Add(page); return page;
    }
    private async Task AdvancedGraphics()
    {
        var form = new FormDialog("Advanced Graphics Processor Settings", 560, 430);
        var openCl = form.Check("Use OpenCL", false); openCl.IsEnabled = false;
        form.Description("The renderer uses Skia through the platform GPU backend; no OpenCL kernels are installed.");
        var antialias = form.Check("Antialias Guides and Paths", Draft.AntialiasOverlays);
        var deep = form.Check("30-bit Display", false); deep.IsEnabled = false;
        form.Description("The window surface is 8-bit per channel. 16/32-bit document data is preserved separately.");
        var composite = form.Check("GPU Compositing", Draft.AcceleratedCanvas);
        var cache = form.Number("GPU Cache (MB)", Draft.GpuCacheMB, 128, 2048);
        if (await form.ShowDialog<bool>(this)) { Draft.AntialiasOverlays = antialias.IsChecked == true; Draft.AcceleratedCanvas = composite.IsChecked == true; Draft.GpuCacheMB = (int)(cache.Value ?? 1024); Rebuild(); }
    }
    private static readonly string[] Shades = ["Default", "Black", "Dark Gray", "Medium Gray", "Light Gray", "Custom", "Select Custom Color…"];
    private static readonly (string Name, string Color)[] LineColors = [("Custom…", ""), ("Light Blue", "#8FC0FF"), ("Light Red", "#FF8080"), ("Green", "#00C000"), ("Medium Blue", "#3366FF"), ("Yellow", "#FFFF00"), ("Magenta", "#FF00FF"), ("Cyan", "#00FFFF"), ("Light Gray", "#BFBFBF"), ("Black", "#000000")];
    /// <summary>A color list as Photoshop's guide and grid colors are, its swatch beside it; Custom… opens the Color Picker.</summary>
    private Control LineColor(PreferenceGroup group, string label, Func<string> get, Action<string> set, double width = 150)
    {
        var swatch = group.Swatch(label + " Swatch", get(), v => { set(v); Rebuild(); });
        var index = Array.FindIndex(LineColors, c => string.Equals(c.Color, get(), StringComparison.OrdinalIgnoreCase));
        var list = PreferenceGroup.Combo(label, LineColors.Select(c => c.Name).ToArray(), Math.Max(0, index), width);
        list.SelectionChanged += async (_, _) =>
        {
            if (list.SelectedIndex > 0) { set(LineColors[list.SelectedIndex].Color); Rebuild(); }
            else if (list.SelectedIndex == 0 && index != 0) await PickInto(label, get(), v => { set(v); Rebuild(); });
        };
        group.Page.Index(label); foreach (var (name, _) in LineColors) group.Page.Index(name);
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { list, swatch } };
    }
    private async Task PickInto(string title, string start, Action<string> set)
    {
        var color = SkiaSharp.SKColor.Parse(start); var chosen = start; var cancelled = false;
        if (WindowsColorDialog.Chosen) { if (WindowsColorDialog.Pick(this, (color.Red / 255d, color.Green / 255d, color.Blue / 255d)) is { } rgb) set(Hex(rgb)); return; }
        var picker = new ColorPickerDialog(Localize.Text(title), (color.Red / 255d, color.Green / 255d, color.Blue / 255d));
        picker.Moved += rgb => chosen = Hex(rgb); picker.Cancelled += () => cancelled = true;
        await picker.ShowDialog(this); if (!cancelled) set(chosen);
    }
    internal static string Hex((double Red, double Green, double Blue) rgb) => $"#{(byte)Math.Round(rgb.Red * 255):X2}{(byte)Math.Round(rgb.Green * 255):X2}{(byte)Math.Round(rgb.Blue * 255):X2}";
    /// <summary>One screen mode's row: its pasteboard shade and the edge drawn around the picture.</summary>
    private void ScreenRow(Grid table, int row, string mode, Func<string> preset, Action<string> setPreset, Func<string> custom, Action<string> setCustom, int border, PreferencePage page)
    {
        var shade = PreferenceGroup.Combo(mode + " Color", Shades, Math.Max(0, Array.IndexOf(Shades, preset())), 170);
        shade.SelectionChanged += async (_, _) =>
        {
            if (shade.SelectedIndex == 6) await PickInto("Select Custom Color", custom(), v => { setCustom(v); setPreset("Custom"); Rebuild(); });
            else if (shade.SelectedIndex >= 0) setPreset(Shades[shade.SelectedIndex]);
        };
        var edge = PreferenceGroup.Combo(mode + " Border", ["Line", "Drop Shadow", "None"], Draft.ScreenBorders[border], 140);
        edge.SelectionChanged += (_, _) => { if (edge.SelectedIndex >= 0) { var borders = Draft.ScreenBorders.ToArray(); borders[border] = edge.SelectedIndex; Draft.ScreenBorders = borders; } };
        var label = new TextBlock { Text = Localize.Text(mode) + ":", VerticalAlignment = VerticalAlignment.Center };
        foreach (var (control, column) in new (Control, int)[] { (label, 0), (shade, 1), (edge, 2) }) { Grid.SetRow(control, row); Grid.SetColumn(control, column); table.Children.Add(control); }
        page.Index(mode);
    }
    private void BuildPages()
    {
        _sections.Clear(); _pages.Children.Clear(); var p = Draft;
        // ---- General ----
        var page = Page("General"); var top = page.Plain();
        top.Choice("Color Picker", ["Compositor", "Windows"], p.ColorPicker, v => p.ColorPicker = v, 200, tip: "Windows opens the system color dialog wherever a color is picked.");
        top.Unavailable(top.Choice("HUD Color Picker", ["Hue Strip (Small)", "Hue Strip (Medium)", "Hue Strip (Large)", "Hue Wheel (Small)", "Hue Wheel (Medium)", "Hue Wheel (Large)"], 0, _ => { }, 200), "Compositor has no on-canvas HUD color picker; use the Color panel or the Color Picker.");
        top.Choice("Image Interpolation", ["Nearest Neighbor (preserve hard edges)", "Bilinear", "Bicubic (best for smooth gradients)", "Bicubic Smoother (best for enlargement)", "Bicubic Sharper (best for reduction)", "Bicubic Automatic"], p.Interpolation, v => p.Interpolation = v, 300, tip: "The Resample method Export As and Save for Web start with.");
        var (left, right) = page.Group("Options").Columns();
        left.Check("Auto-Update Open Documents", p.WatchFiles, v => p.WatchFiles = v, 'A', "Reload a project that has not been changed here when another program saves it.");
        left.Off("Show \"Home\" Screen", false, "Compositor opens straight to the workspace; it has no Home screen.");
        left.Off("Use Legacy \"New Document\" Interface", false, "File ▸ New uses one dialog, with its presets.", 'L');
        left.Check("Skip Transform when Placing", p.SkipPlacement, v => p.SkipPlacement = v, 'K');
        left.Off("Use Legacy Free Transform", false, "Free Transform scales proportionally by default and Shift frees it, as current Photoshop does.");
        right.Check("Beep When Done", p.BeepWhenDone, v => p.BeepWhenDone = v, 'D', "Play the system sound when a save or an export finishes.");
        right.Off("Export Clipboard", true, "Copied pixels are put on the Windows clipboard at once, so they stay there after Compositor quits.", 'X');
        right.Check("Resize Image During Place", p.ResizeOnPlace, v => p.ResizeOnPlace = v, 'G');
        right.Check("Always Create Smart Objects when Placing", p.SmartOnPlace, v => p.SmartOnPlace = v, 'J');
        right.Off("Create New Layer when Painting", false, "Painting goes onto the selected pixel layer.");
        var actions = page.Plain();
        actions.Line(actions.Button("Reset All Warning Dialogs", () => _ = ConfirmDialog.Tell(this, "Reset All Warning Dialogs", "Every warning will be shown again."), 'W'),
            actions.Button("Reset Preferences On Quit", async () => { if (await ConfirmDialog.Ask(this, "Reset Preferences On Quit", Localize.Text("Reset preferences to their defaults when you quit Compositor?"), "OK", "Cancel")) p.ResetOnQuit = true; }));
        // ---- Interface ----
        page = Page("Interface"); var group = page.Group("Appearance");
        var themes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        string[] themeColors = ["#1E1E1E", "#3C3C3C", "#9B9B9B", "#E8E8E8"]; string[] themeNames = ["Darkest", "Dark Gray", "Light Gray", "Lightest"];
        for (var i = 0; i < 4; i++)
        {
            var index = i; var swatch = new Button { Width = 38, Height = 26, Padding = new Thickness(2), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, Tag = "Color Theme " + i, BorderThickness = new Thickness(2), BorderBrush = p.Theme == i ? Skin.AccentBrush : Skin.BorderControlBrush, Content = new Border { Background = new SolidColorBrush(Color.Parse(themeColors[i])) } };
            ToolTip.SetTip(swatch, Localize.Text(themeNames[i])); swatch.Click += (_, _) => { p.Theme = index; Rebuild(); }; themes.Children.Add(swatch); page.Index(themeNames[i]);
        }
        group.Row("Color Theme", themes);
        group.Unavailable(group.Choice("Highlight Color", ["Default", "Blue"], 0, _ => { }, 150), "Selections in lists use the theme's accent color.");
        group.Off("Neutral Color Mode", false, "An Adobe Creative Cloud setting with no counterpart here.");
        var table = new Grid { ColumnDefinitions = new("Auto,Auto,Auto"), ColumnSpacing = 14, RowSpacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        for (var r = 0; r < 5; r++) table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        foreach (var (text, column) in new[] { ("Color", 1), ("Border", 2) }) { var head = new TextBlock { Text = Localize.Text(text), Foreground = Skin.SecondaryBrush }; Grid.SetColumn(head, column); table.Children.Add(head); }
        ScreenRow(table, 1, "Standard Screen Mode", () => p.PasteboardPreset, v => { p.PasteboardPreset = v; p.Pasteboard = EditorPreferences.ShadeColor(v, p.CustomPasteboard); }, () => p.CustomPasteboard, v => p.CustomPasteboard = v, 0, page);
        ScreenRow(table, 2, "Full Screen with Menus", () => p.FullMenusPreset, v => p.FullMenusPreset = v, () => p.FullMenusCustom, v => p.FullMenusCustom = v, 1, page);
        ScreenRow(table, 3, "Full Screen", () => p.FullPreset, v => p.FullPreset = v, () => p.FullCustom, v => p.FullCustom = v, 2, page);
        ScreenRow(table, 4, "Artboards", () => p.ArtboardPreset, v => p.ArtboardPreset = v, () => p.ArtboardCustom, v => p.ArtboardCustom = v, 3, page);
        group.Children.Add(table);
        group.Info("Artboard settings apply to documents that contain artboards.");
        group = page.Group("Interface Presentation");
        group.Choice("UI Language", ["English", "简体中文"], (p.Language == "zh-CN" || p.Language == "" && Localize.IsChinese) ? 1 : 0, v => p.Language = v == 1 ? "zh-CN" : "en", 200);
        group.Choice("UI Font Size", ["Tiny", "Small", "Medium", "Large"], p.UiFontSize <= 10 ? 0 : p.UiFontSize == 11 ? 1 : p.UiFontSize <= 13 ? 2 : 3, v => p.UiFontSize = v switch { 0 => 10, 1 => 11, 2 => 12, _ => 14 }, 200);
        group.Unavailable(group.Choice("UI Scaling", ["Auto", "100%", "200%"], 0, _ => { }, 200), "Compositor follows the Windows display scale.");
        group.Off("Scale UI To Font", false, "Compositor follows the Windows display scale.", 'S');
        group.Info("Changes will take effect the next time you start Compositor.");
        group = page.Group("Options");
        group.Off("Show Channels in Color", false, "Channel thumbnails are shown in gray.", 'C');
        group.Off("Dynamic Color Sliders", true, "The Color panel's sliders always show the colors they lead to.", 'Y');
        group.Check("Show Menu Colors", p.ShowMenuColors, v => p.ShowMenuColors = v, 'M', "Show the colors Edit ▸ Menus… gave menu rows.");
        group.Off("Show Simplified Right-Click and Popup Menus", false, "Context menus always show every command.");
        // ---- Workspace ----
        page = Page("Workspace"); group = page.Group("Options");
        group.Off("Auto-Collapse Iconic Panels", false, "Panels docked as icons stay open until you close them.", 'A');
        group.Off("Auto-Show Hidden Panels", false, "Hidden panels come back with Window ▸ Workspace or Tab.", 'H');
        group.Off("Open Documents as Tabs", true, "Documents always open as tabs.", 'O');
        group.Off("Enable Floating Document Window Docking", false, "Use Window ▸ Arrange ▸ Float in Window for a separate document window.", 'D');
        group.Check("Large Tabs", p.LargeTabs, v => p.LargeTabs = v, 'L');
        group.Off("Align UI to OS Settings", true, "Compositor always follows the Windows display scale and fonts.");
        group = page.Group("More Options");
        group.Off("Enable Narrow Options Bar", false, "The options bar keeps one height.");
        group.Info("The narrow options bar hides the text of some buttons.");
        var row = page.Plain();
        row.Line(row.Button("Restore Default Workspace", () => Accept("Reset Workspace"), 'R'), row.Button("Customize Toolbar…", () => Accept("Customize Toolbar…")));
        // ---- Notifications ----
        page = Page("Notifications"); group = page.Group("Set Notification Preferences");
        group.Off("System Alerts (always active)", true, "Errors and file conversion reports are always shown.");
        group.Note("Messages that need your attention, such as errors and file conversion reports.");
        group.Check("Tooltips", p.Tooltips, v => p.Tooltips = v, 'T', "Show short tooltips when the pointer rests on controls. Applies after restarting.");
        group.Note("Short tips that appear when the pointer rests on buttons and fields.");
        group.Off("Enable Quiet Mode", false, "Compositor has no promotional or background notifications to quiet.");
        var nested = group.Indent();
        nested.Off("Rich Tooltips", false, "Tooltips are text only."); nested.Note("Tooltips with a short video of the tool.");
        nested.Off("What's New", false, "Release notes are in the documentation folder."); nested.Note("News about features added in an update.");
        nested.Off("Feature Onboarding", false, "Compositor shows no onboarding popups."); nested.Note("Guides that introduce a feature the first time it is used.");
        // ---- Tools ----
        page = Page("Tools"); (left, right) = page.Group("Options").Columns();
        left.Off("Enable Gestures", false, "Touchpad pinch zooms; gestures are not configurable.");
        left.Off("Use Shift Key for Tool Switch", false, "Pressing a tool's letter again cycles through its group.", 'U');
        left.Off("Overscroll", true, "The canvas can always be scrolled past its edges.");
        left.Off("Enable Flick Panning", false, "Panning stops when you let go.", 'F');
        left.Off("Double Click Layer Mask Launches \"Select and Mask\" Workspace", false, "Double-clicking a mask opens the Properties panel.");
        left.Off("Vary Round Brush Hardness based on HUD vertical movement", false, "Compositor has no on-canvas brush HUD.");
        left.Off("Disable brush opacity based on HUD vertical movement", false, "Compositor has no on-canvas brush HUD.");
        left.Off("Use Arrow Keys to Rotate Brush Tip", false, "Arrow keys nudge layers and selections.");
        left.Off("Erase using brush tip when using stylus eraser", false, "The stylus eraser end selects the Eraser tool.");
        left.Off("Snap Vector Tools and Transforms to Pixel Grid", false, "Use View ▸ Snap To for snapping.");
        left.Off("Show reference point when using Transform", true, "Free Transform always shows its reference point.");
        left.Off("Spring-Loaded Tool Shortcuts", true, "Holding a tool's key uses it until the key is released.");
        left.Line(left.Indent(), new TextBlock { Text = Localize.Text("Timing Sensitivity") + ":", VerticalAlignment = VerticalAlignment.Center, Foreground = Skin.DisabledBrush }, new TextBlock { Text = Localize.Format($"{200} ms"), VerticalAlignment = VerticalAlignment.Center, Foreground = Skin.DisabledBrush });
        right.Check("Zoom with Scroll Wheel", p.WheelZoom, v => p.WheelZoom = v, 'S', "With this off, the wheel scrolls and Ctrl-wheel zooms.");
        right.Off("Animated Zoom", false, "Zoom changes in one step.", 'Z');
        right.Off("Zoom Resizes Windows", false, "Documents are tabs; zoom does not resize them.", 'R');
        right.Check("Zoom Clicked Point to Center", p.ZoomToCenter, v => p.ZoomToCenter = v, 'K', "After a zoom, the point zoomed at moves to the middle of the window.");
        group = page.Plain();
        group.Unavailable(group.Choice("Show Transformation Values", ["Never", "Top Left", "Top Right", "Bottom Left", "Bottom Right"], 2, _ => { }, 150, 'V'), "Transform values are shown in the options bar.");
        group = page.Group("More Options");
        group.Check("Zoom at Pointer", p.ZoomAtPointer, v => p.ZoomAtPointer = v, tip: "The wheel zooms at the pointer rather than the middle of the window.");
        group.Number("Brush Smoothing", p.BrushSmoothing * 100, 0, 100, v => p.BrushSmoothing = v / 100, "%");
        group.Check("Eyedropper Sample Ring", p.SampleRing, v => p.SampleRing = v);
        group.Line(group.Button("Keyboard Shortcuts…", () => Accept("Keyboard Shortcuts…")));
        // ---- History ----
        page = Page("History"); group = page.Group("History Log");
        group.Check("History Log", p.HistoryLog, v => { p.HistoryLog = v; Rebuild(); }, 'L', "Write a log of edits to a text file.");
        var logged = group.Indent(); logged.IsEnabled = p.HistoryLog;
        logged.Note("Save Log Items To:");
        var targets = logged.Radios("history-target", ["Metadata", "Text File", "Both"], p.HistoryLogTarget, v => { p.HistoryLogTarget = v; }, ['M', 'T', 'B']);
        logged.Unavailable(targets[0], "Compositor projects have no history metadata field; choose Text File.");
        logged.Unavailable(targets[2], "Compositor projects have no history metadata field; choose Text File.");
        var file = new TextBlock { Text = MainWindowHistoryFile(p), VerticalAlignment = VerticalAlignment.Center, Foreground = Skin.SecondaryBrush, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 460 };
        logged.Line(logged.Button("Choose…", async () =>
        {
            var picked = await StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions { Title = Localize.Text("History Log"), SuggestedFileName = "history.log", DefaultExtension = "log" });
            if (picked?.TryGetLocalPath() is { } path) { p.HistoryLogPath = path; p.HistoryLogTarget = 1; Rebuild(); }
        }, 'O'), file);
        logged.Choice("Edit Log Items", ["Sessions Only", "Concise", "Detailed"], p.HistoryLogDetail, v => p.HistoryLogDetail = v, 180, tip: "Sessions Only notes when Compositor starts; Concise adds each command; Detailed adds who made it.");
        group = page.Group("History Panel");
        group.Check("Allow Non-Linear History", p.NonLinearHistory, v => p.NonLinearHistory = v);
        // ---- File Handling ----
        page = Page("File Handling"); group = page.Group("File Saving Options");
        group.Unavailable(group.Choice("Image Previews", ["Never Save", "Always Save", "Ask When Saving"], 1, _ => { }, 180), "Projects always store a preview.");
        group.Unavailable(group.Choice("File Extension", ["Use Lower Case", "Use Upper Case"], 0, _ => { }, 180), "File names are suggested in lower case.");
        group.Check("Save As to Original Folder", p.SaveToOriginalFolder, v => p.SaveToOriginalFolder = v, 'S', "Save As and Save a Copy open in the folder the project is in.");
        group.Off("Save in Background", true, "Projects always save in the background.", 'B');
        var recovery = group.Check("Automatically Save Recovery Information Every", p.RecoveryEnabled, v => p.RecoveryEnabled = v, 'A', "Recovery copies never replace the original.");
        int[] minutes = [5, 10, 15, 30, 60];
        var every = PreferenceGroup.Combo("Recovery Interval", ["5 Minutes", "10 Minutes", "15 Minutes", "30 Minutes", "1 Hour"], Math.Max(0, Array.FindIndex(minutes, m => m >= p.RecoveryMinutes)), 140);
        every.SelectionChanged += (_, _) => { if (every.SelectedIndex >= 0) p.RecoveryMinutes = minutes[every.SelectedIndex]; };
        group.Children.Remove(recovery); group.Line(recovery, every);
        group.Off("Enable legacy \"Save As\"", false, "Save As keeps every format in one list.");
        group.Check("Do not append \"copy\" to filename when saving a copy", p.NoCopySuffix, v => p.NoCopySuffix = v);
        group.Line(group.Button("Open Recovery Folder", () => Accept("Open Recovery Folder")));
        group = page.Group("File Compatibility");
        group.Line(group.Button("Camera Raw Preferences…", () => Accept("Camera Raw Preferences…")));
        group.Check("Prefer Adobe Camera Raw for Supported Raw Files", p.CameraRaw.PreferForRaw, v => p.CameraRaw.PreferForRaw = v, 'C', "Develop raw files before they open; turn off to open them as decoded.");
        group.Off("Use Adobe Camera Raw to Convert Documents from 32 bit to 16/8 bit", false, "Image ▸ Mode ▸ 16 Bits/Channel converts with HDR Toning.", 'U');
        group.Off("Ignore EXIF Profile Tag", false, "Embedded color profiles are always read.", 'X');
        group.Off("Ignore Rotation Metadata", false, "Pictures are turned upright as their camera recorded.", 'O');
        group.Off("Ask Before Saving Layered TIFF Files", false, "TIFF export is flattened.", 'T');
        group.Off("Disable Compression of PSD and PSB Files", false, "PSD files are written with RLE compression.", 'D');
        group.Unavailable(group.Choice("Maximize PSD and PSB File Compatibility", ["Never", "Always", "Ask"], 1, _ => { }, 140, 'M'), "PSD files always include a composite image.");
        group = page.Plain();
        group.Number("Recent File List Contains", p.RecentFiles, 0, 100, v => p.RecentFiles = (int)v, "files", 64, key: 'R');
        // ---- Export ----
        page = Page("Export"); group = page.Group("Quick Export");
        group.Line(PreferenceGroup.Label("Quick Export Format"), group.Unavailable(PreferenceGroup.Combo("Quick Export Format", ["PNG", "JPG", "GIF"], 0, 90), "Quick Export writes PNG; use Export As for JPEG and GIF."),
            group.Check("Transparency", p.ExportTransparency, v => p.ExportTransparency = v), group.Off("Smaller File (8-bit)", false, "PNG keeps 24-bit color with alpha."));
        group.Note("Quick Export Location");
        var ask = group.Radios("quick-location", ["Ask where to export each time", "Export files to an assets folder next to the current document"], 0, _ => { });
        group.Unavailable(ask[1], "Quick Export always asks where to save.");
        group.Unavailable(group.Choice("Quick Export Metadata", ["None", "Copyright and Contact Info"], 0, _ => { }, 220), "Use Export As to include metadata.");
        group.Off("Convert to sRGB", true, "Exports are converted to sRGB.");
        group = page.Group("Export As Location");
        var places = group.Radios("export-location", ["Export assets to the document's location", "Export to the last specified location"], 1, _ => { });
        group.Unavailable(places[0], "Export As starts where you last exported.");
        group = page.Group("Options");
        group.Off("Use legacy \"Export As\"", false, "Export As is the only Export As dialog.");
        group = page.Group("Export Defaults");
        group.Number("JPEG Quality", p.ExportQuality, 1, 100, v => p.ExportQuality = (int)v, width: 72);
        group.Number("PDF Resolution", p.PdfResolution, 1, 9600, v => p.PdfResolution = v, "pixels/inch", 80);
        // ---- Performance ----
        page = Page("Performance"); var (memory, gpu) = page.Columns();
        group = memory.GroupIn("Memory Usage"); var total = MachineInfo.MemoryMB;
        group.Note(Localize.Format($"Available RAM: {total:N0} MB")); group.Note(Localize.Format($"Ideal Range: {total * 55 / 100:N0}-{total * 75 / 100:N0} MB"));
        var share = new NumericUpDown { Value = total * p.MemoryPercent / 100, Minimum = Math.Max(1, total / 10), Maximum = total, Width = 100, ShowButtonSpinner = false, Tag = "Let Compositor Use", FormatString = "0" };
        var percent = new TextBlock { Text = $"MB ({p.MemoryPercent}%)", VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider { Minimum = 10, Maximum = 100, Value = p.MemoryPercent, Width = 230, Tag = "Memory Slider" };
        void Share(int pct) { p.MemoryPercent = Math.Clamp(pct, 10, 100); percent.Text = $"MB ({p.MemoryPercent}%)"; if ((int)slider.Value != p.MemoryPercent) slider.Value = p.MemoryPercent; var mb = total * p.MemoryPercent / 100; if (share.Value != mb) share.Value = mb; }
        share.ValueChanged += (_, _) => { if (share.Value is { } v) Share((int)Math.Round((double)v * 100 / total)); };
        slider.ValueChanged += (_, _) => Share((int)Math.Round(slider.Value));
        group.Line(PreferenceGroup.Label("Let Compositor Use"), share, percent);
        group.Line(group.Button("−", () => Share(p.MemoryPercent - 5)), slider, group.Button("+", () => Share(p.MemoryPercent + 5)));
        page.Describe(slider, "Undo history and caches stay inside this share of the machine's memory.");
        page.Index("Let Compositor Use");
        group = gpu.GroupIn("Graphics Processor Settings");
        var adapter = MachineInfo.GraphicsProcessor();
        group.Note("Detected Graphics Processor:"); if (adapter.Length > 0) group.Children.Add(new TextBlock { Text = adapter, TextWrapping = TextWrapping.Wrap }); else group.Note("Not detected");
        group.Note(CanvasView.LastFrameUsedGpu ? "Last accelerated frame: GPU" : "No GPU frame has been observed in this session.");
        page.Describe(group.Check("Use Graphics Processor", p.UseGpu, v => p.UseGpu = v, 'G'), "Draw the canvas with the graphics processor. Applies after restarting.");
        group.Line(group.Button("Advanced Settings…", () => _ = AdvancedGraphics()));
        group = page.Group("History & Cache");
        group.Note("Optimize Cache Levels and Tile Size for documents that are:");
        group.Line(group.Button("Web / UI Design", () => { p.HistorySteps = 50; p.HistoryMB = 256; p.GpuCacheMB = 512; Rebuild(); }),
            group.Button("Default / Photos", () => { p.HistorySteps = 50; p.HistoryMB = 512; p.GpuCacheMB = 1024; Rebuild(); }),
            group.Button("Huge Pixel Dimensions", () => { p.HistorySteps = 50; p.HistoryMB = 1024; p.GpuCacheMB = 2048; p.AcceleratedCanvas = true; Rebuild(); }));
        page.Describe(group.Number("History States", p.HistorySteps, 1, 1000, v => p.HistorySteps = (int)v, width: 72, slider: true, key: 'H'), "How many steps the History panel keeps. More steps use more memory.");
        page.Describe(group.Number("History Memory", p.HistoryMB, 16, 1536, v => p.HistoryMB = (int)v, "MB", 72, slider: true), "The most memory undo steps may hold, within the share above.");
        group.Unavailable(group.Number("Cache Levels", 4, 1, 8, _ => { }, width: 72, key: 'C'), "Compositor draws from full-resolution layers and a GPU cache, not cache levels.");
        group.Unavailable(group.Choice("Cache Tile Size", ["128K", "132K", "1024K", "1028K"], 2, _ => { }, 110, 'Z'), "Compositor's tiles are sized by the renderer.");
        group.Info("Set Cache Levels to 2 or higher for optimum GPU performance.");
        group = page.Group("Options");
        group.Off("Multithreaded Compositing", true, "Layers are always composited on several threads.");
        page.DescriptionBox();
        // ---- Image Processing ----
        page = Page("Image Processing"); group = page.Plain();
        var device = group.Choice("Select Subject and Remove Background", ["Device", "Cloud"], 0, _ => { }, 140);
        device.ItemsSource = new[] { new ComboBoxItem { Content = Localize.Text("Device") }, new ComboBoxItem { Content = Localize.Text("Cloud"), IsEnabled = false } }; device.SelectedIndex = 0;
        page.Describe(device, "Processing runs on this device; nothing is uploaded.");
        group.Info("Processing on this device is faster and keeps the picture on your computer.");
        group.Unavailable(group.Choice("Selection Processing", ["Faster", "More Stable"], 0, _ => { }, 140), "Selections are processed one way.");
        group.Unavailable(group.Choice("Remove Tool Processing", ["Faster", "More Stable"], 0, _ => { }, 140), "The Remove tool is processed one way.");
        group.Unavailable(group.Choice("Enhance Detail Processing", ["Faster", "More Stable"], 0, _ => { }, 140), "Enhance Detail is processed one way.");
        group = page.Group("Color");
        group.Line(group.Button("Color Settings…", () => Accept("Color Settings…")), group.Button("OpenColorIO Settings…", () => Accept("OpenColorIO Settings…")));
        // ---- Scratch Disks ----
        page = Page("Scratch Disks"); group = page.Group("Scratch Disks");
        BuildScratchDisks(group, p);
        group.Info("Recovery copies are kept on the first active scratch disk; with none chosen they stay in the settings folder.");
        // ---- Cursors ----
        page = Page("Cursors"); (left, right) = page.Columns();
        group = left.GroupIn("Painting Cursors");
        var paint = new CursorPreview(() => p.PaintingCursor, () => p.BrushCrosshair, true);
        group.Children.Add(paint);
        var modes = group.Radios("painting-cursor", ["Standard", "Precise", "Normal Brush Tip", "Full Size Brush Tip"], p.PaintingCursor, v => { p.PaintingCursor = v; p.PreciseCursor = v == 1; paint.InvalidateVisual(); }, ['T', 'R', 'B', 'F']);
        group.Choice("Brush Tip Outline", ["Normal", "Bold", "Bolder"], p.BrushOutlineWidth - 1, v => { p.BrushOutlineWidth = v + 1; paint.InvalidateVisual(); }, 130);
        group.Check("Show Crosshair in Brush Tip", p.BrushCrosshair, v => { p.BrushCrosshair = v; paint.InvalidateVisual(); }, 'C');
        group.Check("Show Only Crosshair While Painting", p.CrosshairWhilePainting, v => p.CrosshairWhilePainting = v, 'W');
        group.Check("Hide Cursor While Painting", p.HidePaintingCursor, v => p.HidePaintingCursor = v);
        group.Off("Show Brush Leash While Smoothing", false, "Smoothing does not draw a leash.", 'L');
        group.Line(PreferenceGroup.Label("Brush Leash Color"), group.Unavailable(PreferenceGroup.SwatchView("#FF60A0"), "Smoothing does not draw a leash."));
        group = right.GroupIn("Other Cursors");
        var other = new CursorPreview(() => p.PenCursor ? 0 : 1, () => false, false);
        group.Children.Add(other);
        group.Radios("other-cursor", ["Standard", "Precise"], p.PenCursor ? 0 : 1, v => { p.PenCursor = v == 0; other.InvalidateVisual(); }, ['A', 'E']);
        group = right.GroupIn("Brush Preview");
        group.Line(PreferenceGroup.Label("Color"), group.Unavailable(PreferenceGroup.SwatchView("#FF0000"), "Brush previews use the brush outline."));
        // ---- Transparency and Gamut ----
        page = Page("Transparency and Gamut"); group = page.Group("Transparency Settings");
        int[] sizes = [0, 5, 10, 20];
        var size = !p.ShowTransparency ? 0 : p.CheckerSize <= 6 ? 1 : p.CheckerSize <= 14 ? 2 : 3;
        group.Choice("Grid Size", ["None", "Small", "Medium", "Large"], size, v => { p.ShowTransparency = v > 0; if (v > 0) p.CheckerSize = sizes[v]; Rebuild(); }, 150);
        var presets = EditorPreferences.CheckerPresets.Select(c => c.Name).ToArray();
        var colors = PreferenceGroup.Combo("Grid Colors", presets, Math.Max(0, Array.IndexOf(presets, p.CheckerPreset)), 150);
        colors.SelectionChanged += (_, _) =>
        {
            if (colors.SelectedIndex < 0) return; var (name, light, dark) = EditorPreferences.CheckerPresets[colors.SelectedIndex]; p.CheckerPreset = name;
            if (light.Length > 0) { p.CheckerLight = light; p.CheckerDark = dark; } Rebuild();
        };
        group.Line(PreferenceGroup.Label("Grid Colors", 'R'), colors,
            group.Swatch("Checker Light Color", p.CheckerLight, v => { p.CheckerLight = v; p.CheckerPreset = "Custom"; Rebuild(); }),
            group.Swatch("Checker Dark Color", p.CheckerDark, v => { p.CheckerDark = v; p.CheckerPreset = "Custom"; Rebuild(); }),
            new CheckerPreview(p.CheckerLight, p.CheckerDark, p.ShowTransparency ? p.CheckerSize : 0));
        page.Index("Grid Colors");
        group = page.Group("Gamut Warning");
        group.Line(PreferenceGroup.Label("Color"), group.Swatch("Gamut Warning Color", p.GamutColor, v => { p.GamutColor = v; Rebuild(); }),
            new TextBlock { Width = 24 }, PreferenceGroup.Label("Opacity", 'O'), group.NumberBox("Gamut Warning Opacity", p.GamutOpacity, 1, 100, v => p.GamutOpacity = v, 64, slider: true), new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center });
        group.Note("Shown by View ▸ Gamut Warning over colors the CMYK proof cannot print.");
        // ---- Units and Rulers ----
        page = Page("Units and Rulers"); group = page.Group("Units");
        group.Choice("Rulers", RulerUnits.Names, RulerUnits.Index(p.RulerUnit), v => p.RulerUnit = RulerUnits.Code(v), 150, tip: "The unit the rulers count in.");
        group.Unavailable(group.Choice("Type", ["Pixels", "Points", "Millimeters"], 0, _ => { }, 150), "Type sizes are set in document pixels.");
        group = page.Group("Column Size");
        string[] columnUnits = ["Inches", "Centimeters", "Millimeters", "Points", "Picas"];
        // Image ▸ Image Size can measure a width in columns of this size, with this gutter between them.
        group.Line(PreferenceGroup.Label("Width", 'W'), group.NumberBox("Column Width", p.ColumnWidth, .04, 5000, v => p.ColumnWidth = v, 80), group.Combo2("Column Width Unit", columnUnits, p.ColumnUnit, v => p.ColumnUnit = v, 120));
        group.Line(PreferenceGroup.Label("Gutter", 'T'), group.NumberBox("Column Gutter", p.ColumnGutter, 0, 5000, v => p.ColumnGutter = v, 80), group.Combo2("Column Gutter Unit", columnUnits, p.GutterUnit, v => p.GutterUnit = v, 120));
        group = page.Group("New Document Preset Resolutions");
        string[] perUnit = ["Pixels/Inch", "Pixels/Centimeter"];
        group.Line(PreferenceGroup.Label("Print Resolution", 'I'), group.NumberBox("Print Resolution", p.PrintResolution, 1, 9600, v => p.PrintResolution = v, 80),
            group.Combo2("Print Resolution Unit", perUnit, p.PrintResolutionUnit, v => p.PrintResolutionUnit = v, 140));
        group.Line(PreferenceGroup.Label("Screen Resolution", 'S'), group.NumberBox("New Document Resolution", p.NewResolution, 1, 9600, v => p.NewResolution = v, 80),
            group.Combo2("Screen Resolution Unit", perUnit, p.ScreenResolutionUnit, v => p.ScreenResolutionUnit = v, 140));
        group = page.Group("Point/Pica Size");
        group.Radios("pica", ["PostScript (72 points/inch)", "Traditional (72.27 points/inch)"], p.TraditionalPoints ? 1 : 0, v => p.TraditionalPoints = v == 1, ['O', 'D']);
        // ---- Guides, Grid and Slices ----
        page = Page("Guides, Grid and Slices"); group = page.Group("Guides");
        group.Line(PreferenceGroup.Label("Canvas", 'A'), LineColor(group, "Guide Color", () => p.GuideColor, v => p.GuideColor = v), group.Combo2("Guide Style", ["Lines", "Dashed Lines"], Math.Min(1, p.GuideStyle), v => p.GuideStyle = v, 130));
        group.Line(PreferenceGroup.Label("Artboard", 'R'), LineColor(group, "Artboard Guide Color", () => p.ArtboardGuideColor, v => p.ArtboardGuideColor = v), group.Combo2("Artboard Guide Style", ["Lines", "Dashed Lines"], p.ArtboardGuideStyle, v => p.ArtboardGuideStyle = v, 130));
        group.Off("Show Inactive Artboard Guides", true, "Every artboard's guides are shown.");
        group = page.Group("Smart Guides");
        group.Unavailable(group.Line(PreferenceGroup.Label("Color", 'S'), PreferenceGroup.SwatchView(p.SmartGuideColor)), "Compositor's snapping shows no smart guides.");
        group = page.Group("Hover Layer Bounds");
        group.Unavailable(group.Line(PreferenceGroup.Label("Line Color", 'E'), PreferenceGroup.SwatchView("#1473E6"), PreferenceGroup.Combo("Hover Width", ["0.5 px", "0.75 px", "1 px", "2 px", "3 px"], 2, 90)), "Hover bounds are drawn in the accent color.");
        group = page.Group("Grid");
        group.Line(PreferenceGroup.Label("Color", 'C'), LineColor(group, "Grid Color", () => p.GridColor, v => p.GridColor = v), group.Combo2("Grid Style", ["Lines", "Dashed Lines", "Dots"], p.GridStyle, v => p.GridStyle = v, 130));
        group.Line(PreferenceGroup.Label("Gridline Every", 'D'), group.NumberBox("Gridline Every", p.GridEvery, .01, 10000, v => p.GridEvery = v, 80), group.Combo2("Gridline Unit", RulerUnits.Names, RulerUnits.Index(p.GridUnit), v => p.GridUnit = RulerUnits.Code(v), 130),
            new TextBlock { Width = 16 }, PreferenceGroup.Label("Subdivisions", 'V'), group.NumberBox("Subdivisions", p.GridSubdivisions, 1, 64, v => p.GridSubdivisions = (int)v, 64));
        group = page.Group("Slices");
        group.Line(PreferenceGroup.Label("Line Color", 'E'), LineColor(group, "Slice Line Color", () => p.SliceColor, v => p.SliceColor = v), group.Check("Show Slice Numbers", p.ShowSliceNumbers, v => p.ShowSliceNumbers = v, 'M'));
        group = page.Group("Path");
        double[] widths = [.5, 1, 2, 3];
        group.Line(PreferenceGroup.Label("Path Options"), LineColor(group, "Path and Control Color", () => p.PathColor, v => p.PathColor = v),
            group.Combo2("Path and Control Width", ["Other", "0.5 px", "1 px", "2 px", "3 px"], Array.IndexOf(widths, p.PathWidth) + 1, v => { if (v > 0) p.PathWidth = widths[v - 1]; }, 110),
            group.NumberBox("Path Width", p.PathWidth, .25, 10, v => p.PathWidth = v, 64));
        group = page.Group("Controls");
        group.Unavailable(group.Choice("Control Color", ["Classic", "Default", "Green", "Yellow-Green", "Yellow", "Orange", "Red", "Magenta", "Purple", "Indigo", "Violet", "Navy Blue", "White", "Black"], 1, _ => { }, 150, 'L'), "Transform controls take the Path color above.");
        // ---- Plugins ----
        page = Page("Plug-Ins"); group = page.Group("Options");
        group.Off("Show all Filter Gallery groups and names", true, "Every filter is listed in the Filter menu.");
        group.Off("Enable Developer Mode", false, "Plugins are loaded from the documented local plugin host.");
        group = page.Group("Generator");
        group.Off("Enable Generator", false, "Adobe Generator is not part of Compositor.");
        group.Off("Enable Remote Connections", false, "Photoshop's remote connection protocol is not supported.", 'E');
        group.Unavailable(group.Line(PreferenceGroup.Label("Service Name", 'S'), DialogKit.Line("Service Name")), "Photoshop's remote connection protocol is not supported.");
        group.Unavailable(group.Line(PreferenceGroup.Label("Password", 'O'), DialogKit.Line("Password")), "Photoshop's remote connection protocol is not supported.");
        group.Note("Password must be 6 characters or longer.");
        group = page.Group("Legacy Extensions");
        group.Off("Allow Extensions to Connect to the Internet", false, "CEP extensions are not compatible.", 'A');
        group.Off("Load Extension Panels", false, "CEP extensions are not compatible.", 'X');
        group.Info("Changes will take effect the next time you start Compositor.");
        group = page.Group("Compositor Plugins");
        group.Line(group.Button("Manage Plugins…", () => Accept("Manage Plugins…")));
        // ---- Type ----
        page = Page("Type"); group = page.Group("Type Options");
        group.Check("Use Smart Quotes", p.SmartQuotes, v => p.SmartQuotes = v, 'Q', "Typed straight quotes become curly opening and closing quotes.");
        group.Off("Enable Missing Glyph Protection", true, "Missing glyphs always fall back to an installed font.", 'G');
        group.Off("Show Font Names in English", false, "Font names are shown as the font itself gives them.", 'F');
        group.Check("Use ESC key to commit text", p.EscapeCommitsText, v => p.EscapeCommitsText = v, tip: "With this off, Escape throws the edit away; Ctrl+Escape always does.");
        group.Off("Auto-set Default Font Size", false, "New type uses the Default Size below.");
        group.Off("Enable Type Layer Glyph Alternates", false, "Use the Glyphs panel for alternates.");
        group.Check("Fill new type layers with placeholder text", p.PlaceholderText, v => p.PlaceholderText = v);
        group.Off("Auto-detect bulleted and numbered lists while typing", false, "Lists are typed as plain text.");
        group.Unavailable(group.Number("Number of Recent Fonts to Display", p.RecentFontCount, 0, 100, v => p.RecentFontCount = (int)v, width: 64), "The font list shows favorites first; it keeps no recent fonts list.");
        group = page.Group("Default Type");
        group.Text("Default Font", p.DefaultFont, v => p.DefaultFont = v);
        group.Number("Default Type Size", p.DefaultFontSize, 1, 1000, v => p.DefaultFontSize = v, "px", 72);
        group.Info("IME composition keeps priority over the Escape and Enter shortcuts.");
        // ---- Technology Previews ----
        page = Page("Technology Previews"); group = page.Group("Technology Previews");
        page.Describe(group.Check("Accelerated Layer Display", p.AcceleratedCanvas, v => p.AcceleratedCanvas = v), "Replays supported layers as cached Skia commands. Turn off to use the reference compositor. Saving and export always use the reference renderer.");
        page.Describe(group.Off("Enable Preserve Details 2.0 Upscale", false, "Image Size offers Bicubic and Nearest Neighbor."), "Image Size offers Bicubic and Nearest Neighbor.");
        page.Describe(group.Off("Enable Content-Aware Tracing Tool", false, "Use the Pen tools to trace edges."), "Use the Pen tools to trace edges.");
        page.Describe(group.Off("Precise Color Management for HDR Display", false, "The window surface is standard dynamic range."), "The window surface is standard dynamic range.");
        page.Describe(group.Off("Precise Previews for 16-bit Documents", true, "Deep documents keep their data; the display shows an 8-bit rendering."), "Deep documents keep their data; the display shows an 8-bit rendering.");
        page.Describe(group.Off("Open JPEG documents without a background layer", true, "Opened pictures are ordinary layers."), "Opened pictures are ordinary layers.");
        page.Describe(group.Off("Legacy GPU mode (pre-2016)", false, "There is one GPU mode."), "There is one GPU mode.");
        page.Describe(group.Off("Enable Modern UI", true, "This is Compositor's only interface."), "This is Compositor's only interface.");
        group.Info("Changes will take effect the next time you start Compositor.");
        page.DescriptionBox();
        var more = page.Plain(); more.Line(more.Button("Learn More…", () => _ = ConfirmDialog.Tell(this, "Technology Previews", "Compositor's previews and their limits are described in the Windows documentation (windows/docs/README.md).")));
    }
    private static string MainWindowHistoryFile(EditorPreferences p) => p.HistoryLogPath is { Length: > 0 } path ? path : Path.Combine(AppPaths.SettingsDirectory, "history.log");
    private void BuildScratchDisks(PreferenceGroup group, EditorPreferences p)
    {
        var drives = new List<DriveInfo>();
        try { drives = DriveInfo.GetDrives().Where(d => { try { return d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable; } catch (IOException) { return false; } }).ToList(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        var ordered = p.ScratchDisks.Select(name => drives.FirstOrDefault(d => d.Name == name)).OfType<DriveInfo>().Concat(drives.Where(d => !p.ScratchDisks.Contains(d.Name))).ToList();
        var table = new Grid { ColumnDefinitions = new("70,90,140,*"), RowSpacing = 2 };
        string? selected = null; var rows = new List<(string Name, Border Back)>();
        void AddCell(Control control, int r, int c) { Grid.SetRow(control, r); Grid.SetColumn(control, c); table.Children.Add(control); }
        table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        string[] heads = ["Active?", "Drive", "Free Space", "Information"];
        for (var c = 0; c < heads.Length; c++) { AddCell(new TextBlock { Text = Localize.Text(heads[c]), Foreground = Skin.SecondaryBrush, Margin = new Thickness(6, 2) }, 0, c); group.Page.Index(heads[c]); }
        for (var i = 0; i < ordered.Count; i++)
        {
            var drive = ordered[i]; var r = i + 1; table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var back = new Border { Background = Brushes.Transparent }; Grid.SetColumnSpan(back, 4); AddCell(back, r, 0); rows.Add((drive.Name, back));
            back.PointerPressed += (_, _) => { selected = drive.Name; foreach (var (name, b) in rows) b.Background = name == selected ? Skin.AccentBrush : Brushes.Transparent; };
            var active = new CheckBox { IsChecked = p.ScratchDisks.Contains(drive.Name), Tag = "Scratch " + drive.Name, Margin = new Thickness(18, 0, 0, 0) };
            active.IsCheckedChanged += (_, _) => { var list = p.ScratchDisks.ToList(); list.Remove(drive.Name); if (active.IsChecked == true) list.Add(drive.Name); p.ScratchDisks = list; };
            AddCell(active, r, 0);
            string free; try { free = $"{drive.AvailableFreeSpace / 1073741824d:N1} GB"; } catch (IOException) { free = "—"; }
            string info; try { info = drive.VolumeLabel; } catch (IOException) { info = ""; } catch (UnauthorizedAccessException) { info = ""; }
            AddCell(new TextBlock { Text = drive.Name.TrimEnd('\\'), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 3), IsHitTestVisible = false }, r, 1);
            AddCell(new TextBlock { Text = free, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 3), IsHitTestVisible = false }, r, 2);
            AddCell(new TextBlock { Text = info, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 3), IsHitTestVisible = false }, r, 3);
        }
        void Shift(int by)
        {
            if (selected is null || !p.ScratchDisks.Contains(selected)) return;
            var list = p.ScratchDisks.ToList(); var at = list.IndexOf(selected); var to = Math.Clamp(at + by, 0, list.Count - 1);
            list.RemoveAt(at); list.Insert(to, selected); p.ScratchDisks = list; Rebuild();
        }
        var arrows = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { group.Button("▲", () => Shift(-1)), group.Button("▼", () => Shift(1)) } };
        var frame = new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), MinHeight = 160, Child = table, Background = Skin.SurfaceDarkBrush };
        group.Children.Add(new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 10, Children = { frame, arrows } }); Grid.SetColumn(arrows, 1);
    }

    /// <summary>The cursor a choice in Cursors preferences draws, over a gray patch.</summary>
    private sealed class CursorPreview(Func<int> mode, Func<bool> crosshair, bool painting) : Control
    {
        public override void Render(DrawingContext context)
        {
            var box = new Rect(0, 0, 64, 64); context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)), new Pen(Skin.BorderControlBrush, 1), box);
            var c = box.Center; var pen = new Pen(Brushes.Black, 1.2);
            void Cross(double arm) { context.DrawLine(pen, c - new Vector(arm, 0), c + new Vector(arm, 0)); context.DrawLine(pen, c - new Vector(0, arm), c + new Vector(0, arm)); }
            switch (mode())
            {
                case 0 when painting: context.DrawGeometry(Brushes.White, pen, StreamGeometry.Parse("M24,44 C25,40 27,39 29,39 L39,27 L42,29 L31,41 C31,43 29,45 24,44 Z")); break;
                case 0: context.DrawGeometry(Brushes.White, pen, StreamGeometry.Parse("M24,20 L28,37 L33,40 L44,29 L41,24 Z M24,20 L35,31")); break;
                case 1: Cross(9); break;
                default: context.DrawEllipse(null, new Pen(Brushes.Black, 1 + EditorPreferences.Current.BrushOutlineWidth * .3), c, mode() == 2 ? 14 : 22, mode() == 2 ? 14 : 22); if (crosshair()) Cross(5); break;
            }
        }
        protected override Size MeasureOverride(Size availableSize) => new(64, 64);
    }

    /// <summary>The checkerboard Transparency preferences will draw, at its square size.</summary>
    private sealed class CheckerPreview(string light, string dark, int size) : Control
    {
        public override void Render(DrawingContext context)
        {
            var step = Math.Max(4, size); var a = new SolidColorBrush(Color.Parse(light)); var b = new SolidColorBrush(Color.Parse(dark));
            for (var y = 0; y < 48; y += step) for (var x = 0; x < 48; x += step)
                context.FillRectangle(size == 0 ? Brushes.White : ((x / step + y / step) % 2 == 0 ? a : b), new Rect(x, y, Math.Min(step, 48 - x), Math.Min(step, 48 - y)));
            context.DrawRectangle(null, new Pen(Skin.BorderControlBrush, 1), new Rect(0, 0, 48, 48));
        }
        protected override Size MeasureOverride(Size availableSize) => new(48, 48);
    }
}

/// <summary>One category's page: framed groups, searchable by every label on it.</summary>
internal sealed class PreferencePage : StackPanel
{
    private readonly List<Func<bool>> _validators = [];
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap, Foreground = Skin.SecondaryBrush, MinHeight = 54 };
    internal Window Owner { get; }
    internal TextBlock Heading { get; }
    internal string SearchText { get; private set; }
    internal PreferencePage(string title, Window owner)
    {
        Owner = owner; Spacing = 14; SearchText = title + " " + Localize.Text(title); Tag = title;
        Heading = new TextBlock { Text = Localize.Text(title), FontSize = 16, FontWeight = FontWeight.SemiBold, IsVisible = false };
        Children.Add(Heading);
    }
    internal bool Valid() => _validators.Select(check => check()).ToArray().All(valid => valid);
    internal void Validate(Func<bool> check) => _validators.Add(check);
    internal void Index(string label) => SearchText += " " + label + " " + Localize.Text(label);
    internal PreferenceGroup Group(string title) { var group = new PreferenceGroup(this); Index(title); Children.Add(DialogKit.Group(title, group)); return group; }
    internal PreferenceGroup Plain() { var group = new PreferenceGroup(this); Children.Add(group); return group; }
    internal (PreferenceGroup Left, PreferenceGroup Right) Columns()
    {
        PreferenceGroup left = new(this) { Spacing = 14 }, right = new(this) { Spacing = 14 };
        var grid = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 18, Children = { left, right } }; Grid.SetColumn(right, 1); Children.Add(grid); return (left, right);
    }
    /// <summary>The page's Description box shows what the control under the pointer does, as Photoshop's Performance page does.</summary>
    internal void Describe(Control control, string text)
    {
        Index(text); var shown = Localize.Text(text);
        if (ToolTip.GetTip(control) is null) ToolTip.SetTip(control, shown);
        control.PointerEntered += (_, _) => _description.Text = shown;
    }
    internal void DescriptionBox() { Index("Description"); Children.Add(DialogKit.Group("Description", _description)); }
}

/// <summary>The rows inside one framed group, built the way Photoshop's preference groups are.</summary>
internal sealed class PreferenceGroup : StackPanel
{
    internal PreferencePage Page { get; }
    internal PreferenceGroup(PreferencePage page) { Page = page; Spacing = 9; Grid.SetIsSharedSizeScope(this, true); }
    internal PreferenceGroup GroupIn(string title) { var group = new PreferenceGroup(Page); Page.Index(title); Children.Add(DialogKit.Group(title, group)); return group; }
    internal PreferenceGroup Indent() { var group = new PreferenceGroup(Page) { Margin = new Thickness(24, 0, 0, 0) }; Children.Add(group); return group; }
    internal (PreferenceGroup Left, PreferenceGroup Right) Columns()
    {
        PreferenceGroup left = new(Page), right = new(Page);
        var grid = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 24, Children = { left, right } }; Grid.SetColumn(right, 1); Children.Add(grid); return (left, right);
    }
    /// <summary>A caption ending in a colon; in Chinese its Alt key follows it, "Rulers(R):".</summary>
    internal static TextBlock Label(string text, char key = default) => new()
    { Text = Localize.Text(text) + (key != default && Localize.IsChinese ? $"({key})" : "") + ":", VerticalAlignment = VerticalAlignment.Center };
    internal static ComboBox Combo(string tag, string[] options, int selected, double width) =>
        new() { ItemsSource = options.Select(Localize.Text).ToArray(), SelectedIndex = Math.Clamp(selected, -1, options.Length - 1), Width = width, Tag = tag, MinHeight = DialogKit.FieldHeight, VerticalAlignment = VerticalAlignment.Center };
    internal ComboBox Combo2(string tag, string[] options, int selected, Action<int> changed, double width)
    { var combo = Combo(tag, options, selected, width); combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) changed(combo.SelectedIndex); }; Page.Index(tag); foreach (var o in options) Page.Index(o); return combo; }
    internal static Border SwatchView(string color) => new() { Width = 34, Height = 20, Background = new SolidColorBrush(Color.Parse(color)), BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
    internal StackPanel Line(params Control[] controls)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var given in controls) { var control = given is NumericUpDown number ? Holder(number) : given; if (control.Parent is Panel old) old.Children.Remove(control); control.VerticalAlignment = VerticalAlignment.Center; line.Children.Add(control); }
        Children.Add(line); return line;
    }
    internal Grid Row(string label, Control control, char key = default)
    {
        // Captions in one group share a width, so their fields line up as Photoshop's do.
        Page.Index(label); var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "caption" }, new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 8 };
        grid.Children.Add(Label(label, key)); Grid.SetColumn(control, 1); control.HorizontalAlignment = HorizontalAlignment.Left; control.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(control);
        if (key != default) Mnemonic(key, control);
        Children.Add(grid); return grid;
    }
    /// <summary>Alt and the letter puts the focus on, or toggles, the control the caption belongs to.</summary>
    private void Mnemonic(char key, Control control)
    {
        Page.Owner.KeyDown += (_, e) =>
        {
            if (e.KeyModifiers != KeyModifiers.Alt || e.Key != Key.A + (char.ToUpperInvariant(key) - 'A') || !control.IsEffectivelyEnabled || !control.IsEffectivelyVisible) return;
            if (control is ComboBox combo) combo.IsDropDownOpen = true; else control.Focus();
            e.Handled = true;
        };
    }
    internal CheckBox Check(string text, bool value, Action<bool> changed, char key = default, string? tip = null)
    {
        Page.Index(text); var box = new CheckBox { Content = new TextBlock { Text = Localize.Text(text), TextWrapping = TextWrapping.Wrap }, IsChecked = value, Tag = text };
        if (key != default) Page.Owner.Mnemonic(box, key);
        if (tip is not null) Page.Describe(box, tip);
        box.IsCheckedChanged += (_, _) => changed(box.IsChecked == true); Children.Add(box); return box;
    }
    /// <summary>A Photoshop setting Compositor has no counterpart for: shown as Photoshop shows it, disabled, with the reason as its tooltip.</summary>
    internal CheckBox Off(string text, bool value, string reason, char key = default) => Unavailable(Check(text, value, _ => { }, key), reason);
    internal T Unavailable<T>(T control, string reason) where T : Control
    {
        control.IsEnabled = false; Page.Index(reason); ToolTip.SetTip(control, Localize.Text(reason)); ToolTip.SetShowOnDisabled(control, true);
        if (control is Panel or Border) control.Opacity = .5;
        if (control.Parent is Grid row) { row.Children.OfType<TextBlock>().FirstOrDefault()?.SetValue(TextBlock.ForegroundProperty, Skin.DisabledBrush); ToolTip.SetTip(row, Localize.Text(reason)); }
        return control;
    }
    internal ComboBox Choice(string label, string[] options, int selected, Action<int> changed, double width = 220, char key = default, string? tip = null)
    {
        var combo = Combo(label, options, selected, width); foreach (var option in options) Page.Index(option);
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) changed(combo.SelectedIndex); };
        Row(label, combo, key); if (tip is not null) Page.Describe(combo, tip); return combo;
    }
    internal NumericUpDown NumberBox(string tag, double value, double min, double max, Action<double> changed, double width = 72, bool slider = false)
    {
        Page.Index(tag);
        var input = new NumericUpDown { Value = (decimal)Math.Clamp(value, min, max), Minimum = (decimal)min, Maximum = (decimal)max, ShowButtonSpinner = false, Width = width, Tag = tag, MinHeight = DialogKit.FieldHeight, FormatString = "0.##" };
        input.ValueChanged += (_, _) => { if (input.Value is { } n) changed((double)n); };
        Page.Validate(() => { var valid = input.Value is { } n && n >= (decimal)min && n <= (decimal)max; input.BorderBrush = valid ? Skin.BorderControlBrush : Brushes.OrangeRed; return valid; });
        if (!slider) return input;
        // The ⌄ beside a field opens a slider under it, as Photoshop's History States and Opacity fields do.
        var track = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 180 };
        var popup = new Popup { Child = new Border { Background = Skin.ChromeBrush, BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Padding = new Thickness(10, 4), Child = track }, PlacementTarget = input, Placement = PlacementMode.Bottom, IsLightDismissEnabled = true };
        track.ValueChanged += (_, _) => input.Value = (decimal)Math.Round(track.Value);
        var arrow = new Button { Content = "⌄", Width = 22, MinHeight = DialogKit.FieldHeight, Padding = default, HorizontalContentAlignment = HorizontalAlignment.Center, Tag = tag + " Slider" };
        arrow.Click += (_, _) => { track.Value = (double)(input.Value ?? 0); popup.IsOpen = !popup.IsOpen; };
        _holders[input] = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { input, arrow, popup } }; return input;
    }
    private readonly Dictionary<NumericUpDown, Control> _holders = [];
    private Control Holder(NumericUpDown input) => _holders.GetValueOrDefault(input) ?? input;
    internal NumericUpDown Number(string label, double value, double min, double max, Action<double> changed, string? unit = null, double width = 72, bool slider = false, char key = default)
    {
        var input = NumberBox(label, value, min, max, changed, width, slider); var holder = Holder(input);
        Control shown = unit is null ? holder : new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { holder, new TextBlock { Text = Localize.Text(unit), VerticalAlignment = VerticalAlignment.Center } } };
        Row(label, shown, key); return input;
    }
    internal TextBox Text(string label, string value, Action<string> changed)
    {
        var input = DialogKit.Line(label); input.Text = value; input.MaxLength = 200; input.Width = 260;
        input.TextChanged += (_, _) => changed(input.Text ?? ""); Row(label, input);
        Page.Validate(() => { var valid = !string.IsNullOrWhiteSpace(input.Text); input.BorderBrush = valid ? Skin.BorderControlBrush : Brushes.OrangeRed; return valid; }); return input;
    }
    /// <summary>A color swatch that opens the Color Picker, as Photoshop's are.</summary>
    internal Button Swatch(string tag, string value, Action<string> changed)
    {
        Page.Index(tag);
        var swatch = new Button { Width = 36, Height = 22, Padding = new Thickness(2), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, Tag = tag, Content = new Border { Background = new SolidColorBrush(Color.Parse(value)), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch } };
        swatch.Click += async (_, _) =>
        {
            var start = SkiaSharp.SKColor.Parse(value); var picked = value; var cancelled = false;
            var rgb = (start.Red / 255d, start.Green / 255d, start.Blue / 255d);
            if (WindowsColorDialog.Chosen) { if (WindowsColorDialog.Pick(Page.Owner, rgb) is { } chosen) changed(PreferencesDialog.Hex(chosen)); return; }
            var picker = new ColorPickerDialog(Localize.Text(tag), rgb); picker.Moved += c => picked = PreferencesDialog.Hex(c); picker.Cancelled += () => cancelled = true;
            await picker.ShowDialog(Page.Owner); if (!cancelled && picked != value) changed(picked);
        };
        return swatch;
    }
    internal RadioButton[] Radios(string group, string[] options, int selected, Action<int> changed, char[]? keys = null)
    {
        var buttons = new RadioButton[options.Length]; group += "-" + GetHashCode();
        for (var i = 0; i < options.Length; i++)
        {
            var index = i; Page.Index(options[i]);
            var radio = new RadioButton { Content = new TextBlock { Text = Localize.Text(options[i]), TextWrapping = TextWrapping.Wrap }, GroupName = group, IsChecked = i == selected, Tag = options[i] };
            if (keys is { } letters && i < letters.Length) Page.Owner.Mnemonic(radio, letters[i]);
            radio.IsCheckedChanged += (_, _) => { if (radio.IsChecked == true) changed(index); };
            buttons[i] = radio; Children.Add(radio);
        }
        return buttons;
    }
    internal void Info(string text)
    {
        Page.Index(text);
        Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
        {
            new TextBlock { Text = "ⓘ", Foreground = Skin.AccentBrush, VerticalAlignment = VerticalAlignment.Top },
            new TextBlock { Text = Localize.Text(text), TextWrapping = TextWrapping.Wrap, Foreground = Skin.SecondaryBrush, MaxWidth = 640 },
        } });
    }
    internal void Note(string text) { Page.Index(text); Children.Add(new TextBlock { Text = Localize.Text(text), TextWrapping = TextWrapping.Wrap, Foreground = Skin.SecondaryBrush }); }
    internal Button Button(string text, Action action, char key = default)
    {
        Page.Index(text); var button = FormDialog.ActionButton(text, action); button.MinHeight = DialogKit.FieldHeight; button.Margin = default;
        if (text.Length <= 2) { button.MinWidth = 0; button.Width = 30; button.Padding = default; }
        if (key != default) Page.Owner.Mnemonic(button, key);
        return button;
    }
}
