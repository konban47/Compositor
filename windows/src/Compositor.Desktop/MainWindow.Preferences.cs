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
        ColorDefaults.Settings = p.ColorSettings; ColorDefaults.Ocio = p.Ocio; _canvas.ColorSettingsChanged();
        if (Avalonia.Application.Current is DesktopApp app) app.RefreshAppearance();
        foreach (var visual in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(this)) visual.InvalidateVisual();
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
    private const string ShowAllTag = "show-all-menu-items";
    /// <summary>Top-level menus showing their hidden rows after Show All Menu Items, until they close.</summary>
    private readonly HashSet<MenuItem> _showingAll = [];
    private readonly HashSet<MenuItem> _watchedMenus = [];
    private static string MenuPath(MenuItem item, string prefix) => prefix + "/" + (item.Tag as string ?? Localize.English((item.Header?.ToString() ?? "").Replace("_", "")));
    /// <summary>The application menus as the Menus tab lists them, each row with the key row it fires when it has one.</summary>
    private IReadOnlyList<MenuNode> MenuTree()
    {
        var keys = new Dictionary<MenuItem, string>(); foreach (var (item, id) in _keyRows) keys.TryAdd(item, id);
        MenuNode Node(MenuItem item, string prefix)
        {
            var path = MenuPath(item, prefix);
            var children = item.Items.OfType<MenuItem>().Where(c => !Equals(c.Tag, ShowAllTag)).Select(c => Node(c, path)).ToArray();
            return new MenuNode(path, item.Header?.ToString() ?? "", children, keys.GetValueOrDefault(item), !Equals(item.Tag, "Menus…") && item.Header?.ToString() != Localize.Text("Menus…"));
        }
        return _mainMenu?.Items.OfType<MenuItem>().Select(m => Node(m, "")).ToArray() ?? [];
    }
    private static MenuLook CurrentMenuLook() => new(EditorPreferences.Current.HiddenMenus.ToHashSet(), EditorPreferences.Current.MenuColors.ToDictionary());
    private void KeepMenuLook(MenuLook? look)
    {
        if (look is null) return;
        EditorPreferences.Current.HiddenMenus = look.Hidden; EditorPreferences.Current.MenuColors = look.Colors;
        try { EditorPreferences.Current.Save(); } catch (Exception error) { Say(error.Message); }
        ApplyHiddenMenus();
    }
    /// <summary>Hides the rows Edit ▸ Menus… hid, colors the ones it colored, and ends each menu with hidden rows with Show All Menu Items.</summary>
    private void ApplyHiddenMenus()
    {
        if (_mainMenu is null) return;
        var preferences = EditorPreferences.Current;
        foreach (var top in _mainMenu.Items.OfType<MenuItem>())
        {
            var any = false; var showing = _showingAll.Contains(top);
            void Walk(MenuItem item, string prefix)
            {
                var path = MenuPath(item, prefix); var hidden = preferences.HiddenMenus.Contains(path) && !Equals(item.Tag, "Menus…") && item.Header?.ToString() != Localize.Text("Menus…");
                any |= hidden; item.IsVisible = !hidden || showing;
                if (preferences.MenuColors.TryGetValue(path, out var color) && MenuLook.Tint(color) is { } tint) item.Background = new Avalonia.Media.SolidColorBrush(tint, .45);
                else item.ClearValue(MenuItem.BackgroundProperty);
                foreach (var child in item.Items.OfType<MenuItem>().Where(c => !Equals(c.Tag, ShowAllTag)).ToArray()) Walk(child, path);
            }
            var topPath = MenuPath(top, "");
            foreach (var child in top.Items.OfType<MenuItem>().Where(c => !Equals(c.Tag, ShowAllTag)).ToArray()) Walk(child, topPath);
            var showAll = top.Items.OfType<MenuItem>().FirstOrDefault(i => Equals(i.Tag, ShowAllTag));
            if (any && showAll is null)
            {
                showAll = new MenuItem { Header = Localize.Text("Show All Menu Items"), Tag = ShowAllTag, StaysOpenOnClick = true };
                var menu = top; showAll.Click += (_, _) => { _showingAll.Add(menu); ApplyHiddenMenus(); };
                top.Items.Add(showAll);
            }
            if (showAll is not null) showAll.IsVisible = any && !showing;
            if (_watchedMenus.Add(top))
            {
                var menu = top;
                top.PropertyChanged += (_, e) => { if (e.Property == MenuItem.IsSubMenuOpenProperty && !menu.IsSubMenuOpen && _showingAll.Remove(menu)) ApplyHiddenMenus(); };
            }
        }
    }
    private async Task CustomizeMenus()
    {
        var (picked, look) = await ShortcutDialog.Show(this, _shortcutSettings.Overrides, MenuTree(), CurrentMenuLook(), 1);
        if (picked is not { } chosen) return;
        KeepMenuLook(look);
        _shortcutSettings.Overrides = chosen; _shortcutSettings.Save(ShortcutDefaults.DefaultPath); ShowKeys();
    }
}
