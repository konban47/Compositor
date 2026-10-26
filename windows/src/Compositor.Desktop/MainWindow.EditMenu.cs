using Avalonia.Controls;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private MenuItem BuildEditMenu()
    {
        MenuItem Group(string name, params Control[] items)
        { var group = new MenuItem { Header = Localize.Text(name), Tag = name }; foreach (var item in items) group.Items.Add(item); return group; }
        MenuItem Run(string title, System.Func<System.Threading.Tasks.Task> action, string? shortcut = null) => Command(title, () => _ = action(), shortcut);
        var transform = Group("Transform", Command("Again", RepeatTransform, "Transform Again"), new Separator());
        foreach (var mode in new[] { "Scale", "Rotate", "Skew", "Distort", "Perspective", "Warp" })
        { var selected = mode; transform.Items.Add(Command(mode, () => BeginEditTransform(selected))); }
        transform.Items.Add(new Separator());
        transform.Items.Add(Command("Split Warp Horizontally", () => SplitPlaced(true, false)));
        transform.Items.Add(Command("Split Warp Vertically", () => SplitPlaced(false, true)));
        transform.Items.Add(Command("Split Warp Crosswise", () => SplitPlaced(true, true)));
        transform.Items.Add(Command("Remove Warp Split", () => { if (_placeMesh?.RemoveSplit(_placeSplit.Column, _placeSplit.Row) == true) RenderPlacementMesh(); }));
        transform.Items.Add(Command("Toggle Transform Anchor", () => { _placePivotVisible = !_placePivotVisible; _canvas.InvalidateVisual(); }));
        transform.Items.Add(Command("Toggle Guides", ShowGuides)); transform.Items.Add(new Separator());
        foreach (var (title, angle) in new[] { ("Rotate 180°", 180), ("Rotate 90° Clockwise", 90), ("Rotate 90° Counterclockwise", -90) })
            transform.Items.Add(Command(title, () => EditRotate(angle)));
        transform.Items.Add(Command("Flip Layer _Horizontal", () => Flip(horizontal: true, canvas: false)));
        transform.Items.Add(Command("Flip Layer _Vertical", () => Flip(horizontal: false, canvas: false)));
        var menu = Group("_Edit",
            Command("_Undo", Undo, "Undo"), Command("_Redo", Redo, "Redo"), Command("Toggle Last State", ToggleLastState, "Toggle Last State"),
            new Separator(), Run("Fade…", FadeLastEdit, "Fade"), new Separator(),
            Command("Cu_t", Cut, "Cut"), Command("_Copy", Copy, "Copy"), Command("Copy _Merged", CopyMerged, "Copy Merged"), Command("_Paste", Paste, "Paste"),
            Group("Paste Special", Run("Paste without Formatting", PastePlainText), Command("Paste in Place", Paste, "Paste in Place"),
                Command("Paste Into", () => PasteMasked(false), "Paste Into"), Command("Paste Outside", () => PasteMasked(true))),
            Command("Clear", ClearPixels), Command("Layer via Cop_y", LayerViaCopy, "Layer via Copy"), new Separator(),
            Run("_Find Command…", FindCommand, "Find Command"), Run("Check Spelling…", CheckSpelling), Run("Find and Replace Text…", FindReplaceText),
            new Separator(), Run("Fill…", EditFill, "Fill Dialog"), Run("Stroke…", EditStroke),
            Command("_Content-Aware Fill", ContentAwareFill, "Content-Aware Fill"), Run("Generative Fill…", GenerativeFill), Run("Generate Image…", OpenGenerativeWorkspace),
            Run("Remove Reflections…", RemoveReflections), new Separator(),
            Run("Content-Aware Scale…", ContentAwareScale, "Content-Aware Scale"), Command("Puppet Warp", BeginPuppetWarp), Command("Perspective Warp", () => BeginEditTransform("Perspective")),
            Command("Free Transform", () => BeginEditTransform("Scale"), "Free Transform"), transform, Command("Rotate Object", () => BeginEditTransform("Rotate")),
            Run("Auto-Align Layers…", AutoAlignLayers), Run("Auto-Blend Layers…", AutoBlendLayers), Run("Sky Replacement…", SkyReplacement), new Separator(),
            Run("Define Brush Preset…", () => DefinePreset("Brush")), Run("Define Pattern…", () => DefinePreset("Pattern")), Run("Define Custom Shape…", () => DefinePreset("Shape")),
            new Separator(), Group("Purge", Run("Clipboard", () => Purge("Clipboard")), Run("History", () => Purge("History")), Run("All", () => Purge("All")), Run("Video Cache", () => Purge("Video Cache"))),
            new Separator(), Run("PDF Presets…", PdfPresets), Group("Presets", Run("Preset Manager…", PresetManager), Run("Migrate Presets…", MigratePresets)),
            Run("Remote Connections…", RemoteConnections), new Separator(), Run("Color Settings…", ColorSettings, "Color Settings"), Run("OpenColorIO Settings…", OpenColorIoSettings),
            Run("Assign Profile…", AssignColorProfile), Run("Convert to Profile…", ConvertColorProfile), Run("OpenColorIO Conversion…", ConvertOpenColorIo), new Separator(),
            Run("_Keyboard Shortcuts…", KeyboardShortcuts, "Keyboard Shortcuts"), Run("Menus…", CustomizeMenus, "Menus"), Run("Customize Toolbar…", CustomizeToolbar), BuildPreferencesMenu());
        menu.PropertyChanged += (_, e) => { if (e.Property == MenuItem.IsSubMenuOpenProperty && menu.IsSubMenuOpen) RefreshEditAvailability(menu); };
        return menu;
    }
    private void RefreshEditAvailability(MenuItem root)
    {
        void Visit(MenuItem item)
        {
            if (item.Tag is string title)
            {
                if (title is "_Undo" or "_Redo" or "Toggle Last State") item.IsEnabled = title == "_Redo" ? _history.CanRedo : _history.CanUndo || title == "Toggle Last State" && _history.CanRedo;
                else if (title == "Again") item.IsEnabled = _repeatTransform is not null && PropertyLayer is not null;
                else if (title == "Video Cache") { item.IsEnabled = false; ToolTip.SetTip(item, Localize.Text("This editor has no video cache.")); }
                else if (title.StartsWith("Split Warp") || title == "Remove Warp Split") item.IsEnabled = _placeMode == "Warp" && _placeMesh is not null;
                else if (title is "Auto-Align Layers…" or "Auto-Blend Layers…") item.IsEnabled = SelectedLayers.Count >= 2;
                else if (title is "Check Spelling…" or "Find and Replace Text…") item.IsEnabled = _document?.Layers.Exists(l => l.LiveText is not null) == true;
                else if (title is "Fade…" or "Cu_t" or "_Copy" or "Copy _Merged" or "Clear" or "Fill…" or "Stroke…" or "Content-Aware Scale…" or "Puppet Warp" or "Perspective Warp" or "Free Transform" or "Rotate Object") item.IsEnabled = PropertyLayer is not null;
                else if (title is "Generative Fill…" or "Paste Into" or "Paste Outside" or "Sky Replacement…") item.IsEnabled = _document?.Selection.Path is not null && _document.Selection.IsEmpty == false;
            }
            foreach (var child in item.Items.OfType<MenuItem>()) Visit(child);
        }
        Visit(root);
    }
}
