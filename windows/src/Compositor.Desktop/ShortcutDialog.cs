using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Compositor.Core.IO;

namespace Compositor.Desktop;

/// <summary>One row of the application menus as the Menus tab shows it: where it is, what it says, what it holds and which key row it is.</summary>
internal sealed record MenuNode(string Path, string Title, IReadOnlyList<MenuNode> Children, string? ShortcutId, bool CanHide);

/// <summary>Which menu rows are hidden and which are colored, as Edit ▸ Menus… leaves them.</summary>
internal sealed record MenuLook(HashSet<string> Hidden, Dictionary<string, string> Colors)
{
    internal static readonly string[] Palette = ["None", "Red", "Orange", "Yellow", "Green", "Blue", "Violet", "Gray"];
    internal static Color? Tint(string name) => name switch
    {
        "Red" => Color.FromRgb(0xC8, 0x3C, 0x3C), "Orange" => Color.FromRgb(0xD8, 0x7A, 0x28), "Yellow" => Color.FromRgb(0xC8, 0xB0, 0x28), "Green" => Color.FromRgb(0x3C, 0x9A, 0x50),
        "Blue" => Color.FromRgb(0x3C, 0x6E, 0xC8), "Violet" => Color.FromRgb(0x8A, 0x50, 0xC0), "Gray" => Color.FromRgb(0x80, 0x80, 0x80), _ => null,
    };
}

/// <summary>Named copies of the shortcut table or the menu look, kept as JSON files beside the settings, as Photoshop's sets.</summary>
internal static class NamedSets
{
    internal const string Defaults = "Compositor Defaults";
    private static string Folder(string kind) => Path.Combine(AppPaths.SettingsDirectory, kind);
    internal static IReadOnlyList<string> Names(string kind)
    {
        try { return Directory.Exists(Folder(kind)) ? Directory.EnumerateFiles(Folder(kind), "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.CurrentCultureIgnoreCase).Take(200).ToArray() : []; }
        catch (IOException) { return []; } catch (UnauthorizedAccessException) { return []; }
    }
    internal static T? Load<T>(string kind, string name) where T : class
    {
        try { var path = Path.Combine(Folder(kind), name + ".json"); return File.Exists(path) && new FileInfo(path).Length < 4 * 1024 * 1024 ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : null; }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }
    internal static void Save<T>(string kind, string name, T value)
    {
        if (!Valid(name)) throw new ArgumentException("Enter a set name without \\ / : * ? \" < > |.");
        Directory.CreateDirectory(Folder(kind)); AtomicFile.Write(Path.Combine(Folder(kind), name + ".json"), stream => JsonSerializer.Serialize(stream, value, new JsonSerializerOptions { WriteIndented = true }));
    }
    internal static void Delete(string kind, string name) { var path = Path.Combine(Folder(kind), name + ".json"); if (Valid(name) && File.Exists(path)) File.Delete(path); }
    internal static bool Valid(string name) => name.Length is > 0 and <= 100 && name != Defaults && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name.Trim() == name;
}

/// <summary>
/// Edit ▸ Keyboard Shortcuts… and Edit ▸ Menus…: one window with a tab for each, as Photoshop's. The Keyboard Shortcuts tab lists the
/// application menus' commands under their menus (or the tools) with the key each holds; clicking a key starts recording, and the next
/// key pressed is the row's new one. The Menus tab lists every menu row with its visibility and its color. Both keep named sets.
/// <para>
/// The recorder takes its keys through a tunnelling handler on the dialog itself, so Return, Escape, Tab and the arrows can be recorded
/// like any other key; while a row is recording, the dialog's own Return and Escape are held off.
/// </para>
/// </summary>
internal sealed class ShortcutDialog : DialogWindow
{
    private const string ShortcutSetKind = "Shortcut Sets", MenuSetKind = "Menu Sets";
    /// <summary>The table being edited, every row on a key, which is what the check runs on.</summary>
    private readonly Dictionary<string, ShortcutChord> _draft;
    private readonly Dictionary<string, ShortcutChord> _undo = [];
    private readonly IReadOnlyList<MenuNode> _menus;
    private readonly HashSet<string> _hidden;
    private readonly Dictionary<string, string> _colors;
    private readonly HashSet<string> _expanded = [];
    private readonly TextBox _search = Search(), _menuSearch = Search();
    private readonly CheckBox _case = new() { Content = Localize.Text("Case Sensitive") }, _menuCase = new() { Content = Localize.Text("Case Sensitive") };
    private readonly ComboBox _kind = new() { Width = 280, MinHeight = 0, Height = 26 }, _set = new() { Width = 280, MinHeight = 0, Height = 26 }, _menuSet = new() { Width = 280, MinHeight = 0, Height = 26 };
    private readonly CheckBox _legacyUndo = new() { Content = Localize.Text("Use Legacy Undo Shortcuts") }, _legacyChannels = new() { Content = Localize.Text("Use Legacy Channel Shortcuts") };
    private readonly StackPanel _list = new(), _menuList = new();
    /// <summary>What scrolls the list, kept so that the check can take the list out of it to be drawn.</summary>
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, _menuScroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly TextBlock _complaint = new() { Foreground = Brushes.Orange, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly Button _save, _accept, _revert, _useDefault, _add, _remove;
    private readonly TabControl _tabs = new();
    private readonly TextBlock _header = new() { VerticalAlignment = VerticalAlignment.Center };
    /// <summary>The row being recorded, or null when the keys are the dialog's own again; and the row picked in the list.</summary>
    private string? _recording, _selected;
    private bool _quiet;
    private Dictionary<string, ShortcutChord>? _result;
    private MenuLook? _look;

    internal ShortcutDialog(IReadOnlyDictionary<string, ShortcutChord> overrides, IReadOnlyList<MenuNode>? menus = null, MenuLook? look = null, int tab = 0)
    {
        Title = Localize.Text("Keyboard Shortcuts and Menus"); Width = 1040; Height = 820; MinWidth = 760; MinHeight = 560; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _draft = Shortcuts.Effective(overrides); _menus = menus ?? []; _hidden = look?.Hidden.ToHashSet() ?? []; _colors = look?.Colors.ToDictionary() ?? [];
        _save = FormDialog.ActionButton("OK", Keep); _save.IsDefault = true;
        var cancel = FormDialog.ActionButton("Cancel", Close); cancel.IsCancel = true;
        _accept = Side("Accept", () => StopRecording()); _revert = Side("Undo", RevertSelected); _useDefault = this.Mnemonic(Side("Use Default", UseDefault), 'D');
        _add = this.Mnemonic(Side("Add Shortcut", () => { if (_selected is { } id) { _recording = id; ShowRows(); } }), 'A'); _remove = this.Mnemonic(Side("Delete Shortcut", () => { if (_selected is { } id) Set(id, ShortcutChord.Unbound); }), 'E');
        var summarize = this.Mnemonic(Side("Summarize…", async () => await Summarize()), 'M');

        foreach (var name in new[] { "Application Menus", "Tools" }) _kind.Items.Add(new ComboBoxItem { Content = Localize.Text(name), Tag = name });
        _kind.SelectedIndex = 0; _kind.SelectionChanged += (_, _) => { _selected = null; ShowRows(); };
        FillSets(_set, ShortcutSetKind, Shortcuts.Problem(overrides) is null && overrides.Count == 0 ? NamedSets.Defaults : null);
        FillSets(_menuSet, MenuSetKind, _hidden.Count == 0 && _colors.Count == 0 ? NamedSets.Defaults : null);
        _set.SelectionChanged += (_, _) => PickShortcutSet(); _menuSet.SelectionChanged += (_, _) => PickMenuSet();
        _search.TextChanged += (_, _) => ShowRows(); _case.IsCheckedChanged += (_, _) => ShowRows();
        _menuSearch.TextChanged += (_, _) => ShowMenus(); _menuCase.IsCheckedChanged += (_, _) => ShowMenus();
        _legacyUndo.IsCheckedChanged += (_, _) => { if (!_quiet) Legacy(_legacyUndo.IsChecked == true, [("Menus:Undo", new("Z", ShortcutModifiers.Control | ShortcutModifiers.Alt)), ("Menus:Toggle Last State", new("Z", ShortcutModifiers.Control))]); };
        _legacyChannels.IsCheckedChanged += (_, _) => { if (!_quiet) Legacy(_legacyChannels.IsChecked == true, [("Menus:RGB Channel", new("OemTilde", ShortcutModifiers.Control)), ("Menus:Red Channel", new("D1", ShortcutModifiers.Control)), ("Menus:Green Channel", new("D2", ShortcutModifiers.Control)), ("Menus:Blue Channel", new("D3", ShortcutModifiers.Control)), ("Menus:Actual Pixels", new("D0", ShortcutModifiers.Control | ShortcutModifiers.Alt))]); };

        _tabs.Items.Add(new TabItem { Header = Localize.Text("Keyboard Shortcuts"), Content = ShortcutsTab(summarize) });
        _tabs.Items.Add(new TabItem { Header = Localize.Text("Menus"), Content = MenusTab() });
        _tabs.SelectedIndex = tab;
        var actions = new StackPanel { Spacing = 10, Width = DialogKit.ActionWidth, Children = { _save, cancel } };
        var root = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 16, Margin = new Thickness(16), Children = { _tabs, actions } };
        Grid.SetColumn(actions, 1); Content = root;
        if (_menus.Count > 0) _expanded.Add("menu:" + _menus[0].Path);
        ShowRows(); ShowMenus();
        // The keys come here first, wherever in the dialog they are pressed, so recording can take any of them.
        AddHandler(KeyDownEvent, Recording, RoutingStrategies.Tunnel);
    }
    private static TextBox Search() => new() { PlaceholderText = Localize.Text("Search in Application Menus"), Width = 380, MinHeight = 0, Height = 28, Padding = new Thickness(8, 3), VerticalContentAlignment = VerticalAlignment.Center };
    private static Button Side(string text, Action action) { var button = FormDialog.ActionButton(text, action); button.HorizontalAlignment = HorizontalAlignment.Stretch; return button; }
    private static Control Glyph(string data) => new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(data), Stroke = Skin.LabelBrush, StrokeThickness = 1.4, Width = 18, Height = 18, Stretch = Stretch.Uniform };
    private Button IconButton(string tip, string data, Action action) { var button = new Button { Content = Glyph(data), Width = 34, Height = 30, Padding = new Thickness(6), Background = Brushes.Transparent, Tag = tip }; ToolTip.SetTip(button, Localize.Text(tip)); button.Click += (_, _) => action(); return button; }
    private const string SaveIcon = "M3,14 V19 H21 V14 M12,3 V14 M7,9 L12,14 L17,9", NewSetIcon = "M3,14 V19 H21 V14 M12,3 V14 M7,9 L12,14 L17,9 M8,21 H9 M11.5,21 H12.5 M15,21 H16", DeleteIcon = "M5,6 H19 M9,6 V4 H15 V6 M7,6 L8,21 H16 L17,6 M10,10 V18 M14,10 V18";

    private Control ShortcutsTab(Button summarize)
    {
        var top = new Grid { ColumnDefinitions = new("Auto,Auto,*,Auto,Auto"), ColumnSpacing = 8 };
        var forLabel = this.Mnemonic(new Label { Content = Localize.Text("Shortcuts For") + ":", VerticalAlignment = VerticalAlignment.Center }, 'H');
        var setLabel = this.Mnemonic(new Label { Content = Localize.Text("Set") + ":", VerticalAlignment = VerticalAlignment.Center }, 'S');
        top.Children.Add(forLabel); Grid.SetColumn(_kind, 1); top.Children.Add(_kind); Grid.SetColumn(setLabel, 3); top.Children.Add(setLabel); Grid.SetColumn(_set, 4); top.Children.Add(_set);
        var icons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, Children = { IconButton("Save all changes to the current set of shortcuts", SaveIcon, () => SaveSet(false)), IconButton("Create a new set based on the current set of shortcuts", NewSetIcon, () => SaveSet(true)), IconButton("Delete the current set of shortcuts", DeleteIcon, () => DeleteSet(_set, ShortcutSetKind)) } };
        var legacy = new Grid { ColumnDefinitions = new("*,Auto"), Children = { new StackPanel { Spacing = 6, Children = { _legacyUndo, _legacyChannels } }, icons } }; Grid.SetColumn(icons, 1); icons.VerticalAlignment = VerticalAlignment.Bottom;
        var search = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Glyph("M10,4 A6,6 0 1 1 9.99,4 Z M14.5,14.5 L20,20"), _search, _case } };
        var head = new Grid { ColumnDefinitions = new("*,220"), Height = 34, Children = { _header, new TextBlock { Text = Localize.Text("Shortcut"), VerticalAlignment = VerticalAlignment.Center } } };
        Grid.SetColumn(head.Children[1], 1); _header.Margin = head.Children[1].Margin = new Thickness(10, 0);
        _scroller.Content = _list;
        var table = new DockPanel { Children = { head, new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Background = Skin.SurfaceDarkBrush, Child = _scroller } } }; DockPanel.SetDock(head, Dock.Top);
        var side = new StackPanel { Spacing = 10, Width = 176, Margin = new Thickness(14, 34, 0, 0), Children = { _accept, _revert, _useDefault, new Border { Height = 30 }, _add, _remove, new Border { Height = 30 }, summarize } };
        var middle = new Grid { ColumnDefinitions = new("*,Auto"), Children = { table, side } }; Grid.SetColumn(side, 1);
        var info = Info("To edit keyboard shortcuts:\n1) Click the New Set button to create a copy of the selected set, or select a set to modify.\n2) Click in the Shortcut column for a command and press the keyboard shortcut to assign.\n3) When done editing, save the set to save all your changes.");
        var page = new DockPanel { Margin = new Thickness(10), Children = { Stack(top, legacy, search), info, _complaint, middle } };
        foreach (var (control, dock) in new (Control, Dock)[] { (page.Children[0], Dock.Top), (info, Dock.Bottom), (_complaint, Dock.Bottom) }) DockPanel.SetDock(control, dock);
        return page;
    }
    private Control MenusTab()
    {
        var kind = new ComboBox { Width = 280, MinHeight = 0, Height = 26, Items = { new ComboBoxItem { Content = Localize.Text("Application Menus") } }, SelectedIndex = 0 };
        var top = new Grid { ColumnDefinitions = new("Auto,Auto,*,Auto,Auto"), ColumnSpacing = 8 };
        var forLabel = this.Mnemonic(new Label { Content = Localize.Text("Menu For") + ":", VerticalAlignment = VerticalAlignment.Center }, 'M');
        var setLabel = new Label { Content = Localize.Text("Set") + ":", VerticalAlignment = VerticalAlignment.Center };
        top.Children.Add(forLabel); Grid.SetColumn(kind, 1); top.Children.Add(kind); Grid.SetColumn(setLabel, 3); top.Children.Add(setLabel); Grid.SetColumn(_menuSet, 4); top.Children.Add(_menuSet);
        var icons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { IconButton("Save all changes to the current set of menus", SaveIcon, () => SaveMenuSet(false)), IconButton("Create a new set based on the current set of menus", NewSetIcon, () => SaveMenuSet(true)), IconButton("Delete the current set of menus", DeleteIcon, () => DeleteSet(_menuSet, MenuSetKind)) } };
        var search = new Grid { ColumnDefinitions = new("Auto,Auto,Auto,*,Auto"), ColumnSpacing = 8, Children = { Glyph("M10,4 A6,6 0 1 1 9.99,4 Z M14.5,14.5 L20,20"), _menuSearch, _menuCase, icons } };
        Grid.SetColumn(_menuSearch, 1); Grid.SetColumn(_menuCase, 2); Grid.SetColumn(icons, 4);
        var head = new Grid { ColumnDefinitions = new("*,100,180"), Height = 34 };
        foreach (var (text, column) in new[] { ("Application Menu Command", 0), ("Visibility", 1), ("Color", 2) }) { var label = new TextBlock { Text = Localize.Text(text), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0) }; Grid.SetColumn(label, column); head.Children.Add(label); }
        _menuScroller.Content = _menuList;
        var table = new DockPanel { Children = { head, new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Background = Skin.SurfaceDarkBrush, Child = _menuScroller } } }; DockPanel.SetDock(head, Dock.Top);
        var info = Info("To hide a menu item, click the Visibility button.\nShow All Menu Items is added to the bottom of a menu that contains hidden items.\nTo see hidden items once, choose Show All Menu Items.\nTo add a color to a menu item, click in the Color column.");
        var page = new DockPanel { Margin = new Thickness(10), Children = { Stack(top, search), info, table } };
        DockPanel.SetDock(page.Children[0], Dock.Top); DockPanel.SetDock(info, Dock.Bottom);
        return page;
    }
    private static StackPanel Stack(params Control[] rows) { var stack = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 0, 10) }; foreach (var row in rows) stack.Children.Add(row); return stack; }
    private static Control Info(string text)
    {
        var grid = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 16, Margin = new Thickness(0, 14, 0, 0) };
        grid.Children.Add(new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M12,2 A10,10 0 1 1 11.99,2 Z M12,10 V17 M12,6.5 V7.5"), Stroke = Skin.LabelBrush, StrokeThickness = 1.8, Width = 34, Height = 34, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top });
        var box = new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Padding = new Thickness(14, 10), MinHeight = 110, Child = new TextBlock { Text = Localize.Text(text), TextWrapping = TextWrapping.Wrap, Foreground = Skin.LabelBrush } };
        Grid.SetColumn(box, 1); grid.Children.Add(box); return grid;
    }

    /// <summary>The rows the Shortcuts For choice shows, under the menu each one sits in, in the menus' own order.</summary>
    private List<(string Group, List<ShortcutDefinition> Rows)> Groups()
    {
        if ((_kind.SelectedItem as ComboBoxItem)?.Tag as string == "Tools")
            return [("Tools", Shortcuts.Definitions.Where(d => d.Group == Shortcuts.Canvas).ToList())];
        var groups = new List<(string, List<ShortcutDefinition>)>(); var placed = new HashSet<string>();
        var byId = Shortcuts.Definitions.ToDictionary(d => d.ID);
        foreach (var menu in _menus)
        {
            var rows = new List<ShortcutDefinition>();
            void Walk(MenuNode node) { if (node.ShortcutId is { } id && byId.TryGetValue(id, out var row) && placed.Add(id)) rows.Add(row); foreach (var child in node.Children) Walk(child); }
            Walk(menu); groups.Add((menu.Title, rows));
        }
        var other = Shortcuts.Definitions.Where(d => d.Group == Shortcuts.Menus && !placed.Contains(d.ID)).ToList();
        if (other.Count > 0) groups.Add((Localize.Text("Other Commands"), other));
        return groups;
    }
    private bool Matches(string text, string search, bool caseSensitive) => search.Length == 0 || text.Contains(search, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    /// <summary>Rebuilds the shortcut list from the draft, under the groups and the search text.</summary>
    private void ShowRows()
    {
        _list.Children.Clear();
        var tools = (_kind.SelectedItem as ComboBoxItem)?.Tag as string == "Tools";
        _header.Text = Localize.Text(tools ? "Tool Command" : "Application Menu Command");
        _search.PlaceholderText = Localize.Text(tools ? "Search in Tools" : "Search in Application Menus");
        var search = _search.Text ?? ""; var caseSensitive = _case.IsChecked == true;
        foreach (var (group, all) in Groups())
        {
            var rows = all.Where(r => Matches(Localize.Text(r.Title), search, caseSensitive) || Matches(r.Title, search, caseSensitive) || Matches(_draft[r.ID].Label, search, caseSensitive)).ToList();
            if (search.Length > 0 && rows.Count == 0) continue;
            var open = search.Length > 0 || _expanded.Contains(group);
            _list.Children.Add(GroupRow(group, open, () => { if (!_expanded.Remove(group)) _expanded.Add(group); ShowRows(); }, null));
            if (open) foreach (var row in rows) _list.Children.Add(KeyRow(row));
        }
        _quiet = true;
        _legacyUndo.IsChecked = _draft["Menus:Undo"] == new ShortcutChord("Z", ShortcutModifiers.Control | ShortcutModifiers.Alt) && _draft["Menus:Toggle Last State"] == new ShortcutChord("Z", ShortcutModifiers.Control);
        _legacyChannels.IsChecked = _draft["Menus:Red Channel"] == new ShortcutChord("D1", ShortcutModifiers.Control);
        _quiet = false;
        var picked = _selected is not null;
        _accept.IsEnabled = _recording is not null; _revert.IsEnabled = picked && _undo.ContainsKey(_selected!); _useDefault.IsEnabled = picked && _draft[_selected!] != Shortcuts.Definitions.First(d => d.ID == _selected).Original;
        _add.IsEnabled = picked && _recording is null; _remove.IsEnabled = picked && _draft[_selected!].IsBound;
        Complaint();
    }
    private static Control GroupRow(string title, bool open, Action toggle, Control? trailing)
    {
        var chevron = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(open ? "M0,0 L4,4 L8,0" : "M0,0 L4,4 L0,8"), Stroke = Skin.SecondaryBrush, StrokeThickness = 1.2, Width = 10, Height = 10, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
        var grid = new Grid { ColumnDefinitions = new("*,Auto"), Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { chevron, new TextBlock { Text = System.Text.RegularExpressions.Regex.Replace(Localize.Text(title).Replace("_", ""), @"([A-Za-z])$", ""), VerticalAlignment = VerticalAlignment.Center } } } } };
        if (trailing is not null) { Grid.SetColumn(trailing, 1); grid.Children.Add(trailing); }
        var row = new Border { Background = open ? Skin.SurfaceControlBrush : Brushes.Transparent, BorderBrush = Skin.BorderSubtleBrush, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(10, 0), Height = 34, Child = grid, Cursor = new Cursor(StandardCursorType.Hand) };
        row.PointerPressed += (_, e) => { if (e.Source is not Button) toggle(); };
        return row;
    }

    /// <summary>One row: what it is called, and the key on it — click the key to record a new one.</summary>
    private Control KeyRow(ShortcutDefinition definition)
    {
        var chord = _draft[definition.ID]; var recording = _recording == definition.ID;
        var key = new Button
        {
            Content = recording ? Localize.Text("Press keys…") : chord.Label, Width = 200, Height = 28, MinHeight = 0, Padding = new Thickness(8, 2), HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = recording ? Skin.SurfaceControlPressedBrush : Brushes.Transparent, BorderBrush = recording ? Skin.AccentBrush : Brushes.Transparent, BorderThickness = new Thickness(1), Tag = "shortcut-" + definition.ID,
        };
        key.Click += (_, _) => { _selected = definition.ID; _recording = definition.ID; ShowRows(); };
        var name = new TextBlock { Text = Localize.Text(definition.Title), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(38, 4, 12, 4) };
        var grid = new Grid { ColumnDefinitions = new("*,220"), Children = { name, key } }; Grid.SetColumn(key, 1);
        var row = new Border { Child = grid, MinHeight = 34, BorderBrush = Skin.BorderSubtleBrush, BorderThickness = new Thickness(0, 0, 0, 1), Background = _selected == definition.ID ? Skin.SurfaceControlHoverBrush : Brushes.Transparent };
        row.PointerPressed += (_, _) => { if (_selected != definition.ID) { _selected = definition.ID; if (_recording != definition.ID) _recording = null; ShowRows(); } };
        return row;
    }

    /// <summary>A key while the dialog has it. While a row is recording, the key is that recording's whatever it is.</summary>
    private void Recording(object? sender, KeyEventArgs e)
    {
        if (_recording is not { } id) return;
        e.Handled = true;
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None) { StopRecording(); return; }
        if (e.Key == Key.Back && e.KeyModifiers == KeyModifiers.None) { Set(id, ShortcutChord.Unbound); return; }
        // Somebody letting go of Control is not asking for Control to be a shortcut.
        if (ShortcutKeys.Bare(e.Key) || e.Key is Key.None or Key.DeadCharProcessed or Key.ImeProcessed) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) { _complaint.Text = Localize.Text("The Windows key is reserved by Windows"); _complaint.IsVisible = true; return; }
        Set(id, new ShortcutChord(e.Key.ToString(), ShortcutKeys.Held(e.KeyModifiers)));
    }
    private void Set(string id, ShortcutChord chord)
    {
        if (_draft[id] != chord) { _undo[id] = _draft[id]; _draft[id] = chord; MarkSetChanged(_set); }
        StopRecording();
    }
    private void StopRecording() { _recording = null; ShowRows(); }
    private void RevertSelected() { if (_selected is { } id && _undo.Remove(id, out var before)) { _draft[id] = before; StopRecording(); } }
    private void UseDefault() { if (_selected is { } id) Set(id, Shortcuts.Definitions.First(d => d.ID == id).Original); }
    private void Legacy(bool on, (string Id, ShortcutChord Chord)[] rows)
    {
        foreach (var (id, chord) in rows) { if (!_draft.ContainsKey(id)) continue; _undo[id] = _draft[id]; _draft[id] = on ? chord : Shortcuts.Definitions.First(d => d.ID == id).Original; }
        MarkSetChanged(_set); ShowRows();
    }

    /// <summary>Whatever would stop the dialog being saved, said in one line, or nothing at all.</summary>
    private void Complaint()
    {
        var problem = Shortcuts.Problem(_draft);
        _complaint.Text = Localize.Text(problem); _complaint.IsVisible = problem is not null;
        _save.IsEnabled = _recording is null && problem is null;
    }

    /// <summary>The rows that have moved off their original key, which is what a saved table is made of.</summary>
    private Dictionary<string, ShortcutChord> Moved()
    {
        var moved = new Dictionary<string, ShortcutChord>();
        foreach (var definition in Shortcuts.Definitions) if (_draft[definition.ID] != definition.Original) moved[definition.ID] = _draft[definition.ID];
        return moved;
    }
    private void Keep()
    {
        if (Shortcuts.Problem(_draft) is not null || _recording is not null) return;
        _result = Moved(); _look = new MenuLook(_hidden.ToHashSet(), _colors.ToDictionary()); Close();
    }

    // ---- Sets ----
    private void FillSets(ComboBox box, string kind, string? selected)
    {
        box.Items.Clear(); box.Items.Add(new ComboBoxItem { Content = Localize.Text(NamedSets.Defaults), Tag = NamedSets.Defaults });
        foreach (var name in NamedSets.Names(kind)) box.Items.Add(new ComboBoxItem { Content = name, Tag = name });
        if (selected is null) box.Items.Add(new ComboBoxItem { Content = Localize.Text("Current (modified)"), Tag = "" });
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, selected ?? ""));
    }
    private static string? SetName(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
    private void MarkSetChanged(ComboBox box)
    {
        if (_quiet || SetName(box) == "") return;
        _quiet = true; var modified = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, ""));
        if (modified is null) { modified = new ComboBoxItem { Content = Localize.Text("Current (modified)"), Tag = "" }; box.Items.Add(modified); }
        box.SelectedItem = modified; _quiet = false;
    }
    private void PickShortcutSet()
    {
        if (_quiet || SetName(_set) is not { Length: > 0 } name) return;
        var overrides = name == NamedSets.Defaults ? [] : NamedSets.Load<Dictionary<string, ShortcutChord>>(ShortcutSetKind, name) ?? [];
        if (Shortcuts.Problem(overrides) is { } problem) { _complaint.Text = Localize.Text(problem); _complaint.IsVisible = true; return; }
        foreach (var (id, chord) in Shortcuts.Effective(overrides)) _draft[id] = chord;
        _undo.Clear(); _recording = null; ShowRows();
    }
    private void PickMenuSet()
    {
        if (_quiet || SetName(_menuSet) is not { Length: > 0 } name) return;
        var look = name == NamedSets.Defaults ? new MenuLook([], []) : NamedSets.Load<MenuLook>(MenuSetKind, name) ?? new MenuLook([], []);
        _hidden.Clear(); _hidden.UnionWith(look.Hidden ?? []); _colors.Clear(); foreach (var (path, color) in look.Colors ?? []) if (MenuLook.Palette.Contains(color)) _colors[path] = color;
        ShowMenus();
    }
    private async void SaveSet(bool asNew) => await SaveNamed(_set, ShortcutSetKind, asNew, name => NamedSets.Save(ShortcutSetKind, name, Moved()));
    private async void SaveMenuSet(bool asNew) => await SaveNamed(_menuSet, MenuSetKind, asNew, name => NamedSets.Save(MenuSetKind, name, new MenuLook(_hidden.ToHashSet(), _colors.ToDictionary())));
    private async Task SaveNamed(ComboBox box, string kind, bool asNew, Action<string> write)
    {
        var name = SetName(box);
        if (asNew || name is null or "" or NamedSets.Defaults)
        {
            var form = new FormDialog("Save Set", 440, 200); var field = form.Text("Set Name", name is { Length: > 0 } and not NamedSets.Defaults ? name + " " + Localize.Text("copy") : Localize.Text("Custom"));
            form.Validate = () => NamedSets.Valid((field.Text ?? "").Trim()) ? null : Localize.Text("Enter a set name without \\ / : * ? \" < > |.");
            if (!await form.ShowDialog<bool>(this)) return; name = (field.Text ?? "").Trim();
        }
        try { write(name); _quiet = true; FillSets(box, kind, name); _quiet = false; }
        catch (Exception error) { _complaint.Text = Localize.Text(error.Message); _complaint.IsVisible = true; }
    }
    private void DeleteSet(ComboBox box, string kind)
    {
        if (SetName(box) is not { Length: > 0 } name || name == NamedSets.Defaults) return;
        try { NamedSets.Delete(kind, name); _quiet = true; FillSets(box, kind, null); _quiet = false; }
        catch (Exception error) { _complaint.Text = Localize.Text(error.Message); _complaint.IsVisible = true; }
    }

    /// <summary>Summarize…: every shortcut in the current table, under its group, as a web page saved where the person chooses.</summary>
    private async Task Summarize()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Localize.Text("Summarize"), SuggestedFileName = "Compositor Shortcuts.htm", DefaultExtension = "htm", FileTypeChoices = [new("HTML") { Patterns = ["*.htm", "*.html"] }] });
        if (file?.TryGetLocalPath() is not { } path) return;
        var html = new System.Text.StringBuilder("<!doctype html><meta charset=\"utf-8\"><title>Compositor</title><style>body{font:14px Segoe UI,sans-serif}td{padding:2px 16px}h2{margin-top:24px}</style><h1>")
            .Append(System.Net.WebUtility.HtmlEncode(Localize.Text("Keyboard Shortcuts"))).Append("</h1>");
        var kinds = new[] { "Application Menus", "Tools" }; var current = _kind.SelectedIndex;
        foreach (var index in new[] { 0, 1 })
        {
            _quiet = true; _kind.SelectedIndex = index; _quiet = false;
            foreach (var (group, rows) in Groups())
            {
                html.Append("<h2>").Append(System.Net.WebUtility.HtmlEncode(Localize.Text(kinds[index]) + " › " + Localize.Text(group).Replace("_", ""))).Append("</h2><table>");
                foreach (var row in rows) html.Append("<tr><td>").Append(System.Net.WebUtility.HtmlEncode(Localize.Text(row.Title))).Append("</td><td>").Append(System.Net.WebUtility.HtmlEncode(_draft[row.ID].Label)).Append("</td></tr>");
                html.Append("</table>");
            }
        }
        _kind.SelectedIndex = current;
        try { AtomicFile.Write(path, stream => { var bytes = System.Text.Encoding.UTF8.GetBytes(html.ToString()); stream.Write(bytes); }); }
        catch (Exception error) { _complaint.Text = Localize.Text(error.Message); _complaint.IsVisible = true; }
    }

    // ---- Menus ----
    private void ShowMenus()
    {
        _menuList.Children.Clear();
        var search = _menuSearch.Text ?? ""; var caseSensitive = _menuCase.IsChecked == true;
        bool Has(MenuNode node) => Matches(node.Title, search, caseSensitive) || node.Children.Any(Has);
        foreach (var menu in _menus)
        {
            if (search.Length > 0 && !Has(menu)) continue;
            var open = search.Length > 0 || _expanded.Contains("menu:" + menu.Path);
            _menuList.Children.Add(GroupRow(menu.Title, open, () => { if (!_expanded.Remove("menu:" + menu.Path)) _expanded.Add("menu:" + menu.Path); ShowMenus(); }, null));
            if (!open) continue;
            void Add(MenuNode node, int depth)
            {
                if (search.Length == 0 || Has(node)) _menuList.Children.Add(MenuRow(node, depth));
                foreach (var child in node.Children) Add(child, depth + 1);
            }
            foreach (var child in menu.Children) Add(child, 0);
        }
    }
    private Control MenuRow(MenuNode node, int depth)
    {
        var hidden = _hidden.Contains(node.Path);
        var name = new TextBlock { Text = node.Title.Replace("_", "") + (node.Children.Count > 0 ? ">" : ""), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(38 + depth * 22, 4, 12, 4), Foreground = hidden ? Skin.SecondaryBrush : Skin.LabelBrush };
        var grid = new Grid { ColumnDefinitions = new("*,100,180"), Children = { name } };
        if (node.CanHide)
        {
            var eye = new Button { Width = 30, Height = 24, MinHeight = 0, Padding = new Thickness(4, 2), Tag = "visibility-" + node.Path, Background = Skin.SurfaceControlBrush, HorizontalAlignment = HorizontalAlignment.Center };
            eye.Content = hidden ? null : new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M1,8 C4,2 14,2 17,8 C14,14 4,14 1,8 Z M9,5.5 A2.5,2.5 0 1 1 8.99,5.5 Z"), Fill = Skin.LabelBrush, Width = 16, Height = 12, Stretch = Stretch.Uniform };
            ToolTip.SetTip(eye, Localize.Text(hidden ? "Show" : "Hide"));
            eye.Click += (_, _) => { if (!_hidden.Remove(node.Path)) _hidden.Add(node.Path); MarkSetChanged(_menuSet); ShowMenus(); };
            Grid.SetColumn(eye, 1); grid.Children.Add(eye);
        }
        var color = _colors.TryGetValue(node.Path, out var c) ? c : "None";
        var swatch = new Button { Content = Localize.Text(color), Width = 160, Height = 24, MinHeight = 0, Padding = new Thickness(8, 1), HorizontalContentAlignment = HorizontalAlignment.Left, Background = MenuLook.Tint(color) is { } tint ? new SolidColorBrush(tint, .55) : Brushes.Transparent, Tag = "color-" + node.Path };
        var menu = new ContextMenu();
        foreach (var option in MenuLook.Palette)
        {
            var item = new MenuItem { Header = Localize.Text(option), Icon = MenuLook.Tint(option) is { } t ? new Border { Width = 12, Height = 12, Background = new SolidColorBrush(t) } : null };
            item.Click += (_, _) => { if (option == "None") _colors.Remove(node.Path); else _colors[node.Path] = option; MarkSetChanged(_menuSet); ShowMenus(); };
            menu.Items.Add(item);
        }
        swatch.ContextMenu = menu; swatch.Click += (_, _) => menu.Open(swatch);
        Grid.SetColumn(swatch, 2); grid.Children.Add(swatch);
        return new Border { Child = grid, MinHeight = 32, BorderBrush = Skin.BorderSubtleBrush, BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    // ---- What the callers and the checks use ----
    /// <summary>The table and the menu look the dialog was told to keep, or nulls when it was dismissed.</summary>
    internal static async Task<(Dictionary<string, ShortcutChord>? Overrides, MenuLook? Look)> Show(Window owner, IReadOnlyDictionary<string, ShortcutChord> overrides, IReadOnlyList<MenuNode> menus, MenuLook look, int tab)
    {
        var dialog = new ShortcutDialog(overrides, menus, look, tab);
        await dialog.ShowDialog(owner);
        return (dialog._result, dialog._look);
    }
    /// <summary>The body this dialog is made of, handed over and let go of, for the check to drive.</summary>
    internal Control TakeBody() { var body = (Control)Content!; Content = null; return body; }
    /// <summary>What the dialog is saying would stop it being saved, for the check.</summary>
    internal string ComplaintText => _complaint.Text ?? "";
    /// <summary>Whether OK is on offer, which the complaint line gates.</summary>
    internal bool CanSave => _save.IsEnabled;
    /// <summary>The table as the dialog has it, for the check.</summary>
    internal IReadOnlyDictionary<string, ShortcutChord> Draft => _draft;
    internal IReadOnlySet<string> Hidden => _hidden;
    /// <summary>The list of rows on its own, every group open, let go of by the dialog so that it can be drawn.</summary>
    internal Control TakeRows()
    {
        foreach (var (group, _) in Groups()) _expanded.Add(group);
        ShowRows(); _scroller.Content = null; return _list;
    }
    /// <summary>The rows that have moved, which OK would keep, for the check.</summary>
    internal Dictionary<string, ShortcutChord> Changes() => Moved();
    /// <summary>Records a key on a row the way pressing it would, so the check drives the path a person drives.</summary>
    internal void Record(string id, Key key, KeyModifiers modifiers)
    {
        _selected = id; _recording = id;
        RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = key, KeyModifiers = modifiers });
    }
    /// <summary>Hides or shows a menu row the way its eye button would, for the check.</summary>
    internal void ToggleHidden(string path) { if (!_hidden.Remove(path)) _hidden.Add(path); ShowMenus(); }
    internal void ColorRow(string path, string color) { if (color == "None") _colors.Remove(path); else _colors[path] = color; ShowMenus(); }
    internal void SetLegacyUndo(bool on) => _legacyUndo.IsChecked = on;
    internal void Accept() => Keep();
}
