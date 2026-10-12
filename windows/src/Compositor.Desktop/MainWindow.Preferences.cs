using Avalonia.Controls;
using Compositor.Core.IO;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private MenuItem BuildPreferencesMenu()
    {
        var menu = new MenuItem { Header = Localize.Text("Preferences") };
        foreach (var name in PreferencesDialog.Categories)
            menu.Items.Add(Command(name + "…", () => _ = EditPreferences(name), name == "General" ? "Preferences" : null));
        return menu;
    }
    private async Task EditPreferences(string category)
    {
        var dialog = new PreferencesDialog(category, EditorPreferences.Current);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try { EditorPreferences.Commit(dialog.Draft); if (dialog.Draft.Language.Length > 0) Localize.SaveLanguage(dialog.Draft.Language); }
        catch (Exception error) { Say(error.Message); return; }
        ApplyPreferences(); Refresh();
        switch (dialog.RelatedAction)
        {
            case "Customize Toolbar…": await CustomizeToolbar(); break;
            case "Grid Settings…": await GridSettings(); break;
            case "Manage Plugins…": await ManagePlugins(); break;
            case "Color Settings…": await ColorSettings(); break;
            case "OpenColorIO Settings…": await OpenColorIoSettings(); break;
            case "Camera Raw Filter…": CameraRawFilter(); break;
            case "Keyboard Shortcuts…": await KeyboardShortcuts(); break;
            case "Reset Workspace": ResetWorkspace(); break;
            case "Open Recovery Folder": OpenRecoveryFolder(); break;
        }
    }
    private bool _preferencesReady;
    private void ApplyPreferences()
    {
        var p = EditorPreferences.Current;
        foreach (var tab in _tabs) { tab.History.Configure(p.HistorySteps, p.HistoryMB * 1024 * 1024, tab.Document); tab.History.AllowNonLinear = p.NonLinearHistory; }
        _canvas.PreciseBrushCursor = p.PreciseCursor; _canvas.ShowsTransparency = p.ShowTransparency; _canvas.UpdateToolCursor();
        _options.ShowsSampleRing = p.SampleRing; _options.Brush = _options.Brush with { Smoothing = p.BrushSmoothing };
        _propertyRulerUnit = p.RulerUnit; UpdateRulers(); ApplyHiddenMenus();
        _canvas.GuideLineStyle = p.GuideStyle; PushBrush(); _preferencesReady = true;
        Skin.ResetChecker(); _canvas.InvalidateVisual(); RefreshTabs(); RefreshRecent(); ConfigureRecovery();
        Avalonia.Media.RenderOptions.SetEdgeMode(_canvas, p.AntialiasOverlays ? Avalonia.Media.EdgeMode.Unspecified : Avalonia.Media.EdgeMode.Aliased);
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
