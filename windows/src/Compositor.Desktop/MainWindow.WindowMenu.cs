using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Compositor.Desktop;

internal sealed record WorkspacePanelState(string Name, bool Visible, bool Floating, int X = 0, int Y = 0, double Width = 340, double Height = 510);
internal sealed class WorkspaceLayout { public List<WorkspacePanelState> Panels { get; set; } = []; }

public sealed partial class MainWindow
{
    private static readonly string[] WorkspaceNames = ["Essentials", "Graphic and Web", "Motion", "Painting", "Photography"];
    private static readonly string[] WindowPanelNames = ["Materials", "Measurement Log", "Navigator", "Adjustments", "Actions", "Paragraph", "Paragraph Styles", "Clone Source", "Tool Presets", "Brushes", "Brush Settings", "Gradients", "Libraries", "History", "Paths", "Swatches", "Timeline", "Properties", "Channels", "Patterns", "Layers", "Layer Comps", "Info", "Shapes", "Color", "Styles", "Histogram", "Notes", "Character", "Character Styles", "Glyphs"];
    private MenuItem BuildWindowMenu()
    {
        var root = new MenuItem { Header = Localize.Text("_Window"), Tag = "_Window" };
        void Populate()
        {
            root.Items.Clear();
            var arrange = new MenuItem { Header = Localize.Text("Arrange"), Tag = "Arrange" };
            foreach (var mode in new[] { "Tile All Vertically", "Tile All Horizontally", "2-up Horizontal", "2-up Vertical", "3-up Horizontal", "3-up Vertical", "3-up Stacked", "4-up", "6-up" })
                arrange.Items.Add(Command(mode, () => ArrangeDocuments(mode)));
            arrange.Items.Add(Command("Consolidate All to Tabs", ConsolidateDocuments)); arrange.Items.Add(new Separator());
            arrange.Items.Add(Command("Cascade", () => ArrangeDocuments("Cascade"))); arrange.Items.Add(Command("Tile", () => ArrangeDocuments("4-up")));
            var floating = Command("Float in Window", () => DocumentWindow(_open, true)); floating.IsEnabled = _document is not null; arrange.Items.Add(floating);
            arrange.Items.Add(Command("Float All in Windows", FloatDocuments)); arrange.Items.Add(new Separator());
            foreach (var name in new[] { "Match Zoom", "Match Location", "Match Rotation", "Match All" }) arrange.Items.Add(Command(name, () => MatchDocumentViews(name)));
            arrange.Items.Add(new Separator()); var newView = Command("New Window for Current Document", () => DocumentWindow(_open, false)); newView.IsEnabled = _document is not null; arrange.Items.Add(newView); root.Items.Add(arrange);
            var workspace = new MenuItem { Header = Localize.Text("Workspace"), Tag = "Workspace" };
            foreach (var name in WorkspaceNames.Concat(EditorPreferences.Current.Workspaces.Keys).Distinct())
            {
                var item = Command(name, () => ApplyWorkspace(name)); item.ToggleType = MenuItemToggleType.CheckBox;
                item.IsChecked = EditorPreferences.Current.ActiveWorkspace == name; workspace.Items.Add(item);
            }
            workspace.Items.Add(new Separator()); workspace.Items.Add(Command("Reset Current Workspace", () => ApplyWorkspace(EditorPreferences.Current.ActiveWorkspace)));
            workspace.Items.Add(Command("New Workspace…", () => _ = SaveWorkspace())); workspace.Items.Add(Command("Delete Workspace…", () => _ = DeleteWorkspace())); workspace.Items.Add(new Separator());
            var customize = new MenuItem { Header = Localize.Text("Keyboard Shortcuts and Menus…") }; customize.Items.Add(Command("Keyboard Shortcuts…", () => _ = KeyboardShortcuts())); customize.Items.Add(Command("Menus…", () => _ = CustomizeMenus())); workspace.Items.Add(customize);
            var locked = Command("Lock Workspace", () => { EditorPreferences.Current.LockWorkspace = !EditorPreferences.Current.LockWorkspace; SavePreferenceChanges(); });
            locked.ToggleType = MenuItemToggleType.CheckBox; locked.IsChecked = EditorPreferences.Current.LockWorkspace; workspace.Items.Add(locked); root.Items.Add(workspace);
            root.Items.Add(new Separator());
            root.Items.Add(Command("Find Extensions on Exchange…", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://exchange.adobe.com/") { UseShellExecute = true })));
            root.Items.Add(Command("Extensions", () => _ = ManagePlugins())); root.Items.Add(new Separator());
            foreach (var name in WindowPanelNames)
            {
                var item = Command(name, () => ToggleWindowPanel(name));
                if (_keys.TryGetValue(MenuKey(name + " Panel"), out var key)) item.InputGesture = ShortcutKeys.Gesture(key);
                item.ToggleType = MenuItemToggleType.CheckBox;
                item.IsChecked = _dockPanels.GetValueOrDefault(name)?.Visible == true; root.Items.Add(item);
            }
            root.Items.Add(new Separator());
            foreach (var name in new[] { "Options", "Tools" })
            {
                var item = Command(name, () => { if (_dockPanels.TryGetValue(name, out var entry) && entry.Visible) HidePanel(entry); else if (name == "Plugins") OpenPluginsPanel();
        else ShowDockPanel(name); });
                item.ToggleType = MenuItemToggleType.CheckBox; item.IsChecked = _dockPanels.GetValueOrDefault(name)?.Visible == true; root.Items.Add(item);
            }
            var context = Command("Contextual Task Bar", () => { var panel = _dockPanels["Contextual Task Bar"]; if (panel.Visible) HidePanel(panel); else ShowDockPanel(panel.Name); });
            context.ToggleType = MenuItemToggleType.CheckBox; context.IsChecked = _dockPanels.GetValueOrDefault("Contextual Task Bar")?.Visible == true; root.Items.Add(context);
            root.Items.Add(new Separator());
            foreach (var editor in Editors.ToArray()) foreach (var tab in editor._tabs.Where(t => t.Document is not null))
            {
                var item = Command(tab.Name, () => { editor.Bring(tab); editor.Activate(); }); item.ToggleType = MenuItemToggleType.CheckBox;
                item.IsChecked = ReferenceEquals(editor, this) && ReferenceEquals(tab, _open); root.Items.Add(item);
            }
        }
        root.SubmenuOpened += (_, e) => { if (ReferenceEquals(e.Source, root)) Populate(); }; Populate(); return root;
    }
    private void ToggleWindowPanel(string name) { if (_dockPanels.GetValueOrDefault(name) is { Visible: true } shown) HidePanel(shown); else OpenWindowPanel(name); }
    private void OpenWindowPanel(string name)
    {
        if (name is "Character" or "Paragraph" or "Glyphs" or "Character Styles" or "Paragraph Styles") OpenTypePanel(name);
        else if (name == "Plugins") OpenPluginsPanel();
        else ShowDockPanel(name);
    }
    private Border BuildContextualBar()
    {
        var actions = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        actions.Children.Add(PropertyAction("Select Subject", () => _ = DetectSubject(false)));
        actions.Children.Add(PropertyAction("Deselect", Deselect));
        actions.Children.Add(PropertyAction("Add Layer Mask", () => AddMask(true)));
        return DockTool("Contextual Task Bar", actions);
    }
    private async Task SaveWorkspace()
    {
        var name = await TextPrompt.Ask(this, "New Workspace", "Name", "My Workspace"); if (string.IsNullOrWhiteSpace(name)) return;
        EditorPreferences.Current.Workspaces[name.Trim()] = new() { Panels = _dockPanels.Values.Select(p => new WorkspacePanelState(p.Name, p.Visible, p.Floating is not null, p.Floating?.Position.X ?? 0, p.Floating?.Position.Y ?? 0, p.Floating?.Width ?? 340, p.Floating?.Height ?? 510)).ToList() };
        EditorPreferences.Current.ActiveWorkspace = name.Trim(); SavePreferenceChanges();
    }
    private async Task DeleteWorkspace()
    {
        var names = EditorPreferences.Current.Workspaces.Keys.ToArray(); if (names.Length == 0) return;
        var form = new FormDialog("Delete Workspace", 420, 320); var choice = form.Choice("Workspace", names, 0); if (!await form.ShowDialog<bool>(this)) return;
        EditorPreferences.Current.Workspaces.Remove(names[choice.SelectedIndex]); ApplyWorkspace("Essentials"); SavePreferenceChanges();
    }
    private void ApplyWorkspace(string name)
    {
        var locked = EditorPreferences.Current.LockWorkspace; EditorPreferences.Current.LockWorkspace = false; ResetDockPanels();
        if (EditorPreferences.Current.Workspaces.TryGetValue(name, out var layout))
        {
            foreach (var saved in layout.Panels.Take(80))
            {
                if (!WindowPanelNames.Contains(saved.Name) && saved.Name is not ("Tools" or "Options" or "Contextual Task Bar" or "Plugins")) continue;
                OpenWindowPanel(saved.Name); if (!_dockPanels.TryGetValue(saved.Name, out var panel)) continue;
                if (!saved.Visible) { HidePanel(panel); continue; }
                if (saved.Floating)
                {
                    var position = new Avalonia.PixelPoint(saved.X, saved.Y);
                    if (!Screens.All.Any(s => s.WorkingArea.Contains(position))) position = new(Position.X + 80, Position.Y + 100);
                    FloatPanel(panel, position);
                    if (panel.Floating is { } window) { window.Width = Math.Clamp(saved.Width, 76, 2000); window.Height = Math.Clamp(saved.Height, 100, 1600); }
                }
            }
        }
        else foreach (var panel in name switch
        {
            "Graphic and Web" => new[] { "Character", "Paragraph", "Shapes" }, "Motion" => new[] { "Timeline", "Actions" },
            "Painting" => new[] { "Brushes", "Color", "Swatches" }, "Photography" => new[] { "Histogram", "Navigator", "Adjustments" }, _ => Array.Empty<string>()
        }) OpenWindowPanel(panel);
        EditorPreferences.Current.LockWorkspace = locked; EditorPreferences.Current.ActiveWorkspace = name; SavePreferenceChanges();
    }
}
