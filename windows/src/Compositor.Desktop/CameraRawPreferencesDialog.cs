using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Compositor.Desktop;

/// <summary>
/// Camera Raw Preferences, as Photoshop lays them out: General, File Handling, Performance, Defaults, Workflow and Technology
/// Previews down the left, OK and Cancel on the right. Edits go to a draft the owner keeps after OK. What Compositor's raw
/// import has no counterpart for is shown disabled, its tooltip saying why.
/// </summary>
internal sealed class CameraRawPreferencesDialog : DialogWindow
{
    internal static readonly string[] Categories = ["General", "File Handling", "Performance", "Defaults", "Workflow", "Technology Previews"];
    internal CameraRawPreferences Draft { get; }
    private readonly ListBox _navigation = new() { Background = Brushes.Transparent, Tag = "camera-raw-categories" };
    private readonly StackPanel _pages = new() { Spacing = 18 };
    private readonly ScrollViewer _scroll;
    private readonly Dictionary<string, PreferencePage> _sections = [];
    internal string Category => (_navigation.SelectedItem as ListBoxItem)?.Tag as string ?? Categories[0];
    internal CameraRawPreferencesDialog(CameraRawPreferences source, string category = "General")
    {
        Draft = System.Text.Json.JsonSerializer.Deserialize<CameraRawPreferences>(System.Text.Json.JsonSerializer.Serialize(source))!;
        Title = Localize.Text("Camera Raw Preferences") + $" ({Localize.Format($"Version {typeof(CameraRawPreferencesDialog).Assembly.GetName().Version?.ToString(3)}")})";
        Width = 1040; Height = 760; MinWidth = 860; MinHeight = 520; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var grid = new Grid { ColumnDefinitions = new("140,*,Auto"), Margin = new Thickness(18, 16), ColumnSpacing = 22 };
        _navigation.ItemsSource = Categories.Select(c => new ListBoxItem { Content = Localize.Text(c), Tag = c, Padding = new Thickness(8, 5), FontSize = 13 }).ToArray();
        grid.Children.Add(new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Background = Skin.SurfaceDarkBrush, Child = _navigation });
        _scroll = new ScrollViewer { Content = _pages, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 14, 0) };
        Grid.SetColumn(_scroll, 1); grid.Children.Add(_scroll);
        var ok = FormDialog.ActionButton("OK", () => { Draft.Normalize(); Close(true); }); ok.IsDefault = true;
        var cancel = FormDialog.ActionButton("Cancel", () => Close(false)); cancel.IsCancel = true;
        var buttons = DialogKit.ActionColumn(ok, cancel); buttons.Margin = default; Grid.SetColumn(buttons, 2); grid.Children.Add(buttons);
        Content = grid; Build();
        _navigation.SelectionChanged += (_, _) => { ShowCategory(); _scroll.Offset = default; };
        _navigation.SelectedIndex = Math.Max(0, Array.IndexOf(Categories, category)); ShowCategory();
    }
    private void ShowCategory() { foreach (var (name, page) in _sections) page.IsVisible = name == Category; }
    private void Rebuild() { var offset = _scroll.Offset; Build(); ShowCategory(); _scroll.Offset = offset; }
    private PreferencePage Page(string category) { var page = new PreferencePage(category, this); _sections[category] = page; _pages.Children.Add(page); return page; }
    private void Build()
    {
        _sections.Clear(); _pages.Children.Clear(); var d = Draft; var w = d.Workflow;
        // ---- General ----
        var page = Page("General"); var group = page.Group("Appearance");
        group.Unavailable(group.Choice("Color Theme", ["Default", "Brightest", "Bright", "Dark", "Darkest"], d.Theme, v => d.Theme = v, 180), "The Camera Raw panel follows the Interface color theme.");
        group = page.Group("Panels");
        group.Unavailable(group.Choice("Edit Panel Behavior", ["Single (default)", "Responsive", "Multiple"], d.PanelBehavior, v => d.PanelBehavior = v, 200), "The Camera Raw panel shows every group in one scrolling column.");
        group.Info("In single panel mode, opening a panel closes the others.");
        group.Off("Use Compact Layout", d.CompactLayout, "The Camera Raw panel has one layout.");
        group.Off("Show Rich Tooltips", false, "Tooltips are text only.");
        group.Off("Select Legacy Targeted Adjustment Tool", false, "There is no targeted adjustment tool.");
        group = page.Group("Filmstrip");
        group.Unavailable(group.Choice("Orientation", ["Horizontal", "Vertical"], 0, _ => { }, 160), "Camera Raw opens one picture at a time, without a filmstrip.");
        group.Off("Show Filenames", true, "Camera Raw opens one picture at a time, without a filmstrip.");
        group.Off("Show Ratings and Color Labels", true, "Camera Raw opens one picture at a time, without a filmstrip.");
        group.Off("Show context menu hint on hover", true, "Camera Raw opens one picture at a time, without a filmstrip.");
        group = page.Group("Zoom & Pan");
        group.Unavailable(group.Choice("Default Image Click Zoom", ["6%", "12%", "25%", "33%", "50%", "66%", "100%", "200%", "300%", "400%", "800%", "1600%"], d.ClickZoom, v => d.ClickZoom = v, 120), "The picture is zoomed with the document window's own zoom.");
        group.Off("Use Lightroom-style zoom and pan", false, "The picture is zoomed with the document window's own zoom.");
        group = page.Group("Keyboard Shortcuts");
        group.Off("Use legacy undo shortcuts", false, "Ctrl+Z and Ctrl+Shift+Z undo and redo, as in the rest of Compositor.");
        // ---- File Handling ----
        page = Page("File Handling"); group = page.Group("DNG File Handling");
        group.Unavailable(group.Choice("Sidecar Files", ["Embed XMP in DNG", "Always use sidecar XMP files", "Ignore sidecar XMP files"], d.Sidecar, v => d.Sidecar = v, 260), "Raw settings are kept in the project, not in XMP files.");
        group.Off("Update embedded JPEG previews", false, "DNG files are opened, never rewritten.");
        group = page.Group("File Format Handling");
        string[] handling = ["Disable JPEG support", "Automatically open JPEGs with settings", "Automatically open all supported JPEGs"];
        group.Choice("JPEG", handling, d.JpegHandling, v => d.JpegHandling = v, 300, tip: "With all supported JPEGs, opening a JPEG develops it first, as a raw file is.");
        group.Choice("TIFF", handling.Select(h => h.Replace("JPEG", "TIFF")).ToArray(), d.TiffHandling, v => d.TiffHandling = v, 300, tip: "With all supported TIFFs, opening a TIFF develops it first, as a raw file is.");
        group.Unavailable(group.Choice("AVIF", ["Disable AVIF support", "Automatically open AVIFs with settings", "Automatically open all supported AVIFs"], 0, _ => { }, 300), "AVIF files are not read.");
        group.Unavailable(group.Choice("JPEG XL", ["Disable JPEG XL support", "Automatically open JPEG XLs with settings", "Automatically open all supported JPEG XLs"], 0, _ => { }, 300), "JPEG XL files are not read.");
        group.Check("Prefer Camera Raw for supported raw files", d.PreferForRaw, v => d.PreferForRaw = v, tip: "Develop raw files before they open; turn off to open them as decoded.");
        // ---- Performance ----
        page = Page("Performance"); group = page.Group("Performance");
        group.Unavailable(group.Choice("Use Graphics Processor", ["Auto", "Custom"], d.GpuMode, v => d.GpuMode = v, 180), "Camera Raw uses the Use Graphics Processor setting of Performance preferences.");
        var adapter = MachineInfo.GraphicsProcessor();
        group.Children.Add(new TextBlock { Text = adapter.Length > 0 ? adapter : Localize.Text("Not detected"), TextWrapping = TextWrapping.Wrap });
        group.Note(EditorPreferences.Current.UseGpu ? "Your system automatically supports full acceleration." : "The graphics processor is turned off in Performance preferences.");
        group.Line(group.Unavailable(group.Button("Reset GPU Support", () => { }), "Compositor keeps no Camera Raw GPU test results to reset."));
        group.Note("Changes will take effect the next time you start Compositor.");
        group = page.Group("Camera Raw Cache");
        group.Unavailable(group.Line(PreferenceGroup.Label("Maximum Size"), group.NumberBox("Camera Raw Cache Size", d.CacheGB, 1, 200, v => d.CacheGB = v, 64), new TextBlock { Text = "GB", VerticalAlignment = VerticalAlignment.Center }, group.Button("Purge Cache", () => { })), "Compositor decodes raw files each time they are opened; it keeps no Camera Raw cache.");
        group.Unavailable(group.Line(group.Button("Select Location…", () => { }), new TextBlock { Text = d.CachePath, VerticalAlignment = VerticalAlignment.Center }), "Compositor keeps no Camera Raw cache.");
        // ---- Defaults ----
        page = Page("Defaults"); group = page.Group("HDR Defaults");
        group.Off("Enable HDR editing for HDR photos by default", false, "Raw files are developed in standard dynamic range.");
        group = page.Group("Raw Defaults");
        var chevron = new TextBlock { Text = "⌄", HorizontalAlignment = HorizontalAlignment.Right }; Grid.SetColumn(chevron, 1);
        var global = new Button { Content = new Grid { ColumnDefinitions = new("*,Auto"), Children = { new TextBlock { Text = Localize.Text(d.GlobalDefault == 0 ? "Adobe Default" : "Camera Settings") }, chevron } }, Width = 420, HorizontalContentAlignment = HorizontalAlignment.Stretch, Tag = "Global" };
        var menu = new ContextMenu();
        foreach (var (name, index) in new[] { ("Adobe Default", 0), ("Camera Settings", 1) })
        {
            var item = new MenuItem { Header = Localize.Text(name), ToggleType = MenuItemToggleType.CheckBox, IsChecked = d.GlobalDefault == index };
            item.Click += (_, _) => { d.GlobalDefault = index; Rebuild(); }; menu.Items.Add(item); page.Index(name);
        }
        menu.Items.Add(new MenuItem { Header = Localize.Text("Preset"), IsEnabled = false }); menu.Items.Add(new Separator());
        var choose = new MenuItem { Header = Localize.Text("Choose Preset") }; choose.Items.Add(new MenuItem { Header = Localize.Text("No presets saved"), IsEnabled = false }); menu.Items.Add(choose);
        global.ContextMenu = menu; global.Click += (_, _) => menu.Open(global);
        group.Row("Global", global); group.Note("Whichever is chosen, Develop starts its sliders at zero with the white balance the camera recorded.");
        group.Off("Customize defaults for specific camera", false, "Defaults are the same for every camera.");
        var cameras = new Grid { ColumnDefinitions = new("*,*"), RowDefinitions = new("Auto,*"), IsEnabled = false, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var (text, column) in new[] { ("Camera Model", 0), ("Default", 1) }) { var head = new TextBlock { Text = Localize.Text(text), Foreground = Skin.DisabledBrush, Margin = new Thickness(60, 0, 0, 4) }; Grid.SetColumn(head, column); cameras.Children.Add(head); }
        var list = new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Background = Skin.SurfaceDarkBrush, MinHeight = 220 }; Grid.SetRow(list, 1); Grid.SetColumnSpan(list, 2); cameras.Children.Add(list);
        group.Children.Add(cameras);
        // ---- Workflow ----
        page = Page("Workflow"); var top = page.Plain();
        var presetNames = new[] { "Custom" }.Concat(d.WorkflowPresets.Select(p => p.Name)).Append("New Workflow Preset…").ToArray();
        var preset = new ComboBox { ItemsSource = presetNames.Select((n, i) => i == 0 || i == presetNames.Length - 1 ? Localize.Text(n) : n).ToArray(), SelectedIndex = Math.Max(0, d.WorkflowPresets.FindIndex(p => Same(p, w)) + 1), Width = 560, Tag = "Workflow Preset" };
        preset.SelectionChanged += async (_, _) =>
        {
            if (preset.SelectedIndex == presetNames.Length - 1)
            {
                var name = await TextPrompt.Ask(this, "New Workflow Preset…", "Name", Localize.Text("Untitled") + "-" + (d.WorkflowPresets.Count + 1));
                if (!string.IsNullOrWhiteSpace(name)) { var saved = w.Copy(); saved.Name = name.Trim(); d.WorkflowPresets.RemoveAll(p => p.Name == saved.Name); d.WorkflowPresets.Add(saved); }
                Rebuild();
            }
            else if (preset.SelectedIndex > 0) { d.Workflow = d.WorkflowPresets[preset.SelectedIndex - 1].Copy(); d.Workflow.Name = ""; Rebuild(); }
        };
        top.Row("Preset", preset); page.Index("New Workflow Preset…");
        group = page.Group("Color Space");
        var spaces = new[] { "Display P3", "ProPhoto RGB", "Rec. 2020", "sRGB IEC61966-2.1", "Lab Color", "Adobe RGB (1998)" };
        var space = new ComboBox { ItemsSource = spaces.Select(s => new ComboBoxItem { Content = s, IsEnabled = s.StartsWith("sRGB", StringComparison.Ordinal) }).ToArray(), SelectedIndex = 3, Width = 300, Tag = "Color Space" };
        ToolTip.SetTip(space, Localize.Text("Raw files are developed into sRGB; convert afterwards with Edit ▸ Convert to Profile."));
        group.Line(PreferenceGroup.Label("Color Space"), space, PreferenceGroup.Label("Color Depth"), group.Combo2("Color Depth", ["8 Bits/Channel", "16 Bits/Channel"], w.Depth, v => w.Depth = v, 170));
        foreach (var s in spaces) page.Index(s);
        group.Unavailable(group.Line(PreferenceGroup.Label("Intent"), PreferenceGroup.Combo("Intent", ["Perceptual", "Relative"], -1, 300)), "Intent applies to CMYK color spaces, which raw files are not developed into.");
        group = page.Group("Image Sizing");
        var resize = group.Check("Resize to Fit", w.Resize, v => { w.Resize = v; Rebuild(); });
        var fit = group.Combo2("Resize to Fit Mode", ["Width & Height", "Dimensions", "Long Side", "Short Side", "Megapixels", "Percentage"], w.FitMode, v => { w.FitMode = v; Rebuild(); }, 260);
        var enlarge = group.Check("Don't Enlarge", w.DontEnlarge, v => w.DontEnlarge = v);
        fit.IsEnabled = enlarge.IsEnabled = w.Resize; group.Line(resize, fit, enlarge);
        var two = w.FitMode is 0 or 1; var unitless = w.FitMode is 4 or 5;
        var size = group.Line(PreferenceGroup.Label(w.FitMode switch { 2 => "Long Side", 3 => "Short Side", 4 => "Megapixels", 5 => "Percentage", _ => "W" }), group.NumberBox("Workflow Width", w.Width, .01, 65000, v => w.Width = v, 80));
        if (two) { size.Children.Add(PreferenceGroup.Label("H")); var height = group.NumberBox("Workflow Height", w.Height, .01, 65000, v => w.Height = v, 80); size.Children.Add(height); }
        if (!unitless) size.Children.Add(group.Combo2("Workflow Size Unit", ["Pixels", "Inches", "Centimeters"], w.SizeUnit, v => w.SizeUnit = v, 130));
        else size.Children.Add(new TextBlock { Text = w.FitMode == 4 ? "MP" : "%", VerticalAlignment = VerticalAlignment.Center });
        size.IsEnabled = w.Resize; size.Margin = new Thickness(40, 0, 0, 0);
        group.Line(PreferenceGroup.Label("Resolution"), group.NumberBox("Workflow Resolution", w.Resolution, 1, 9600, v => w.Resolution = v, 80), group.Combo2("Workflow Resolution Unit", ["Pixels/Inch", "Pixels/Centimeter"], w.ResolutionUnit, v => w.ResolutionUnit = v, 160));
        group = page.Group("Output Sharpening");
        var sharpen = group.Check("Sharpen For", w.Sharpen, v => { w.Sharpen = v; Rebuild(); }, tip: "Applies when Color Depth is 8 Bits/Channel.");
        var target = group.Combo2("Sharpen Target", ["Screen", "Glossy Paper", "Matte Paper"], w.SharpenFor, v => w.SharpenFor = v, 260);
        var amount = group.Combo2("Sharpen Amount", ["Low", "Standard", "High"], w.SharpenAmount, v => w.SharpenAmount = v, 160);
        target.IsEnabled = amount.IsEnabled = w.Sharpen; group.Line(sharpen, target, PreferenceGroup.Label("Amount"), amount);
        group = page.Group("Compositor");
        group.Check("Open in Compositor as Smart Objects", w.SmartObject, v => w.SmartObject = v);
        // ---- Technology Previews ----
        page = Page("Technology Previews"); group = page.Group("Technology Previews");
        group.Off("New AI Features", false, "Generative features are not part of raw development in Compositor.");
        group.Note("Enable generative expand features. This feature is not available in mainland China.");
        group.Note("Changes will take effect the next time you start Compositor.");
    }
    private static bool Same(CameraRawWorkflow a, CameraRawWorkflow b)
    { var x = a.Copy(); var y = b.Copy(); x.Name = y.Name = ""; return System.Text.Json.JsonSerializer.Serialize(x) == System.Text.Json.JsonSerializer.Serialize(y); }
}
