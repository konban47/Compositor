using Avalonia.Controls;
using Compositor.Core.IO;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private MenuItem BuildPreferencesMenu()
    {
        var menu = new MenuItem { Header = Localize.Text("Preferences") };
        foreach (var name in new[] { "General", "Interface", "Workspace", "Notifications", "Tools", "History", "File Handling", "Export", "Performance", "Image Processing", "Scratch Disks", "Cursors", "Transparency and Gamut", "Units and Rulers", "Guides, Grid and Slices", "Plugins", "Type", "Technology Previews", "Camera Raw" })
        {
            var item = Command(name + "…", () => _ = EditPreferences(name));
            if (name is "Scratch Disks" or "Technology Previews" or "Notifications")
            { item.IsEnabled = false; ToolTip.SetTip(item, Localize.Text("This editor uses memory-backed images and in-window status messages; this Photoshop preference is not available.")); }
            menu.Items.Add(item);
        }
        return menu;
    }
    private async Task EditPreferences(string category)
    {
        if (category == "Guides, Grid and Slices") { await GridSettings(); return; }
        if (category == "Plugins") { await ManagePlugins(); return; }
        if (category == "Workspace") { await CustomizeToolbar(); return; }
        if (category == "Image Processing") { await ColorSettings(); return; }
        if (category == "Camera Raw") { CameraRawFilter(); return; }
        var p = EditorPreferences.Current; var form = new FormDialog(category, 540, 480); var changes = new List<Action>();
        if (category is "General" or "Interface")
        {
            var language = form.Choice("Language", ["English", "简体中文"], Localize.IsChinese ? 1 : 0);
            form.Description("Language takes effect after restarting Compositor.");
            changes.Add(() => Localize.SaveLanguage(language.SelectedIndex == 1 ? "zh-CN" : "en"));
            var transparency = form.Check("Show Transparency", p.ShowTransparency); changes.Add(() => p.ShowTransparency = transparency.IsChecked == true);
        }
        if (category is "History" or "Performance")
        {
            var count = form.Number("History States", p.HistorySteps, 1, 1000);
            var memory = form.Number("History Memory (MB)", p.HistoryMB, 16, 1536);
            var branch = form.Check("Non-linear History", p.NonLinearHistory);
            changes.Add(() => { p.HistorySteps = (int)(count.Value ?? 100); p.HistoryMB = (int)(memory.Value ?? 256); p.NonLinearHistory = branch.IsChecked == true; });
        }
        if (category == "File Handling") { var watch = form.Check("Reload Unmodified Projects after External Changes", p.WatchFiles); changes.Add(() => p.WatchFiles = watch.IsChecked == true); }
        if (category is "Cursors" or "Tools")
        {
            var precise = form.Check("Precise Brush Crosshair", p.PreciseCursor); changes.Add(() => p.PreciseCursor = precise.IsChecked == true);
            var ring = form.Check("Eyedropper Sample Ring", _options.ShowsSampleRing); changes.Add(() => p.SampleRing = ring.IsChecked == true);
            var smooth = form.Number("Brush Smoothing (%)", _options.Brush.Smoothing * 100, 0, 100); changes.Add(() => p.BrushSmoothing = (double)(smooth.Value ?? 0) / 100);
        }
        if (category == "Export")
        {
            var quality = form.Number("JPEG Quality", p.ExportQuality, 1, 100); var dpi = form.Number("PDF Resolution (pixels/inch)", p.PdfResolution, 1, 9600);
            form.Description("PDF exports a flattened page. These defaults apply to the next Export As dialog.");
            changes.Add(() => { p.ExportQuality = (int)(quality.Value ?? 90); p.PdfResolution = (double)(dpi.Value ?? 300); });
        }
        if (category == "Transparency and Gamut") { var transparent = form.Check("Show Transparency", p.ShowTransparency); changes.Add(() => p.ShowTransparency = transparent.IsChecked == true); }
        if (category == "Units and Rulers") { var unit = form.Choice("Ruler Units", ["Pixels", "Inches", "Centimeters", "Points"], p.RulerUnit); changes.Add(() => p.RulerUnit = unit.SelectedIndex); }
        if (category == "Type") { var font = form.Text("Default Font", p.DefaultFont); form.Validate = () => string.IsNullOrWhiteSpace(font.Text) ? "Enter a font name." : null; changes.Add(() => p.DefaultFont = font.Text!.Trim()); }
        if (!await form.ShowDialog<bool>(this)) return;
        foreach (var change in changes) change();
        try { p.Save(); } catch (Exception error) { Say(error.Message); }
        ApplyPreferences(); Refresh();
    }
    private void ApplyPreferences()
    {
        var p = EditorPreferences.Current;
        foreach (var tab in _tabs) { tab.History.Configure(p.HistorySteps, p.HistoryMB * 1024 * 1024, tab.Document); tab.History.AllowNonLinear = p.NonLinearHistory; }
        _canvas.PreciseBrushCursor = p.PreciseCursor; _canvas.ShowsTransparency = p.ShowTransparency; _canvas.UpdateToolCursor();
        _options.ShowsSampleRing = p.SampleRing; _options.Brush = _options.Brush with { Smoothing = p.BrushSmoothing };
        _propertyRulerUnit = p.RulerUnit; UpdateRulers(); ApplyHiddenMenus();
    }
    private IEnumerable<(string Path, MenuItem Item)> MenuLeaves()
    {
        IEnumerable<(string, MenuItem)> Walk(MenuItem item, string prefix)
        {
            var path = prefix + "/" + (item.Tag as string ?? Localize.English((item.Header?.ToString() ?? "").Replace("_", "")));
            if (item.Items.OfType<MenuItem>().Any()) { foreach (var child in item.Items.OfType<MenuItem>()) foreach (var leaf in Walk(child, path)) yield return leaf; }
            else yield return (path, item);
        }
        if (_mainMenu is null) yield break;
        foreach (var menu in _mainMenu.Items.OfType<MenuItem>()) foreach (var item in Walk(menu, "")) yield return item;
    }
    private void ApplyHiddenMenus()
    { foreach (var (path, item) in MenuLeaves()) item.IsVisible = !EditorPreferences.Current.HiddenMenus.Contains(path) || item.Header?.ToString() == Localize.Text("Menus…"); }
    private async Task CustomizeMenus()
    {
        var form = new FormDialog("Menus", 620, 650); var checks = new Dictionary<string, CheckBox>();
        foreach (var (path, item) in MenuLeaves())
        {
            if (item.Header?.ToString() == Localize.Text("Menus…")) continue;
            checks[path] = form.Check(string.Join(" › ", path.TrimStart('/').Split('/').Select(part => Localize.Text(part).Replace("_", ""))), !EditorPreferences.Current.HiddenMenus.Contains(path));
        }
        form.Fields.Children.Add(FormDialog.ActionButton("Restore Defaults", () => { foreach (var check in checks.Values) check.IsChecked = true; }));
        if (!await form.ShowDialog<bool>(this)) return;
        EditorPreferences.Current.HiddenMenus = checks.Where(p => p.Value.IsChecked != true).Select(p => p.Key).ToHashSet();
        try { EditorPreferences.Current.Save(); } catch (Exception error) { Say(error.Message); }
        ApplyHiddenMenus();
    }
}
