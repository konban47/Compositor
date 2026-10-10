using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Compositor.Core.IO;

namespace Compositor.Desktop;

internal sealed class ToolbarDialog : DialogWindow
{
    internal ToolbarLayout Draft { get; private set; }
    private readonly ListBox _main = new(), _extra = new();
    private readonly Dictionary<string, ShortcutChord> _keys = Shortcuts.Effective(ShortcutDefaults.Load(ShortcutDefaults.DefaultPath).Overrides);
    private readonly TextBlock _error = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly WrapPanel _show = new() { Orientation = Orientation.Horizontal };
    private sealed record Entry(string? ID, int Group, bool Extra);
    private Entry? _pressed;
    private Point _press;
    private bool _dragging;
    internal ToolbarDialog(ToolbarLayout current)
    {
        Draft = current.Copy();
        Title = Localize.Text("Customize Toolbar"); Width = 1000; Height = 740; MinWidth = 760; MinHeight = 500;
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,200"), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(18) };
        var explanation = new TextBlock { Text = Localize.Text("Drag tools or group headings to reorder. Drop onto a group to join it; drop below the list to create a group. Extra tools remain available in the … menu."), TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 14) };
        Grid.SetColumnSpan(explanation, 2); layout.Children.Add(explanation);
        Control List(string title, ListBox list)
        {
            var panel = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
            var label = new TextBlock { Text = Localize.Text(title), Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(label, Dock.Top); panel.Children.Add(label);
            panel.Children.Add(list); return panel;
        }
        var first = List("Toolbar", _main); Grid.SetRow(first, 1); layout.Children.Add(first);
        var second = List("Extra Tools", _extra); Grid.SetRow(second, 1); Grid.SetColumn(second, 1); layout.Children.Add(second);
        var actions = new StackPanel { Spacing = 9 };
        void Button(string title, Action action)
        {
            var button = new Button { Content = Localize.Text(title), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            button.Click += (_, _) => action(); actions.Children.Add(button);
        }
        Button("Done", () => Close(Draft));
        Button("Cancel", () => Close(null));
        Button("Restore Defaults", () => { Draft = ToolCatalog.Defaults(); Refresh(); });
        Button("Clear Tools", () => { Draft.Extras = Draft.Groups.SelectMany(g => g).Concat(Draft.Extras).ToList(); Draft.Groups.Clear(); Draft.ShowExtras = true; Refresh(); });
        Button("Save Preset…", () => _ = SavePreset());
        Button("Load Preset…", () => _ = LoadPreset());
        actions.Children.Add(new Separator());
        Button("Move Up", () => Step(-1)); Button("Move Down", () => Step(1));
        Button("Move to Extra Tools", () => Transfer(true));
        Button("Move to Toolbar", () => Transfer(false));
        Button("Separate into New Group", Separate);
        Grid.SetColumn(actions, 2); Grid.SetRowSpan(actions, 2); layout.Children.Add(actions);
        Grid.SetRow(_show, 2); Grid.SetColumnSpan(_show, 3); _show.Margin = new Thickness(0, 14, 0, 6); layout.Children.Add(_show);
        Grid.SetRow(_error, 3); Grid.SetColumnSpan(_error, 3); layout.Children.Add(_error); Content = layout;
        AddHandler(PointerPressedEvent, Pressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, Moved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, Released, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Refresh();
    }
    private Entry? Selected => (_main.SelectedItem as ListBoxItem)?.Tag as Entry ?? (_extra.SelectedItem as ListBoxItem)?.Tag as Entry;
    private void Refresh()
    {
        _main.Items.Clear(); _extra.Items.Clear();
        ListBoxItem Row(Entry entry)
        {
            Control content;
            if (entry.ID is { } id)
            {
                var tool = Enum.Parse<Tool>(id);
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("30,*,42"), HorizontalAlignment = HorizontalAlignment.Stretch };
                row.Children.Add(ToolRail.Icon(tool));
                var title = new TextBlock { Text = Localize.Text(ToolCatalog.Title(tool)), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(title, 1); row.Children.Add(title);
                var key = _keys.GetValueOrDefault(Shortcuts.Canvas + ":" + ToolCatalog.Shortcut(tool), ShortcutChord.Unbound);
                var hint = new TextBlock { Text = key.Label, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
                Grid.SetColumn(hint, 2); row.Children.Add(hint); content = row;
            }
            else content = new TextBlock { Text = Localize.Text("Tool Group") + " " + (entry.Group + 1), FontWeight = Avalonia.Media.FontWeight.SemiBold };
            return new ListBoxItem { Tag = entry, Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 34, Margin = new Thickness(entry.ID is null ? 0 : 12, entry.ID is null ? 8 : 0, 0, 0) };
        }
        for (var i = 0; i < Draft.Groups.Count; i++)
        {
            _main.Items.Add(Row(new(null, i, false)));
            foreach (var id in Draft.Groups[i]) _main.Items.Add(Row(new(id, i, false)));
        }
        foreach (var id in Draft.Extras) _extra.Items.Add(Row(new(id, -1, true)));
        _show.Children.Clear(); _show.Children.Add(new TextBlock { Text = Localize.Text("Show:"), VerticalAlignment = VerticalAlignment.Center });
        void Toggle(string icon, string label, bool value, Action<bool> set)
        {
            var button = new Avalonia.Controls.Primitives.ToggleButton { Content = icon == "mask" ? new PanelGlyph("mask") : new InspectorGlyph(icon), IsChecked = value, Width = 36, Height = 34, Padding = new Thickness(6) };
            ToolTip.SetTip(button, Localize.Text(label)); button.Click += (_, _) => set(button.IsChecked == true); _show.Children.Add(button);
        }
        Toggle("more", "Show Extra Tools", Draft.ShowExtras, v => Draft.ShowExtras = v);
        Toggle("colors", "Show Foreground / Background Colors", Draft.ShowColors, v => Draft.ShowColors = v);
        Toggle("mask", "Show Quick Mask", Draft.ShowQuickMask, v => Draft.ShowQuickMask = v);
        Toggle("screen", "Show Screen Mode", Draft.ShowScreenMode, v => Draft.ShowScreenMode = v);
        Toggle("generate", "Show Generative Workspace", Draft.ShowGenerative, v => Draft.ShowGenerative = v);
        var disable = new CheckBox { Content = Localize.Text("Disable shortcuts for hidden extra tools"), IsChecked = Draft.DisableExtraShortcuts, FontSize = 12 };
        disable.IsCheckedChanged += (_, _) => Draft.DisableExtraShortcuts = disable.IsChecked == true; _show.Children.Add(disable);
    }
    private void Transfer(bool extra)
    {
        if (Selected is not { } entry) return;
        Move(entry, extra, -1, null, false); Refresh();
    }
    private void Separate()
    {
        if (Selected is { ID: not null } entry) { Move(entry, false, -1, null, false); Refresh(); }
    }
    private void Step(int direction)
    {
        if (Selected is not { } entry) return;
        if (entry.ID is null)
        {
            var to = Math.Clamp(entry.Group + direction, 0, Draft.Groups.Count - 1);
            (Draft.Groups[entry.Group], Draft.Groups[to]) = (Draft.Groups[to], Draft.Groups[entry.Group]);
        }
        else
        {
            var list = entry.Extra ? Draft.Extras : Draft.Groups[entry.Group];
            var at = list.IndexOf(entry.ID); var to = Math.Clamp(at + direction, 0, list.Count - 1);
            (list[at], list[to]) = (list[to], list[at]);
        }
        Refresh();
        var selected = (entry.Extra ? _extra : _main).Items.OfType<ListBoxItem>().FirstOrDefault(row => row.Tag is Entry value && (entry.ID is not null ? value.ID == entry.ID : value.ID is null && value.Group == Math.Clamp(entry.Group + direction, 0, Draft.Groups.Count - 1)));
        if (selected is not null) { (entry.Extra ? _extra : _main).SelectedItem = selected; selected.BringIntoView(); }
    }
    private void Move(Entry source, bool extra, int group, string? before, bool groupInsert)
    {
        var moving = source.ID is { } id ? new List<string> { id } : Draft.Groups[source.Group].ToList();
        if (source.ID == before && source.ID is not null || source.ID is null && !extra && source.Group == group) return;
        var target = !extra && group >= 0 && group < Draft.Groups.Count ? Draft.Groups[group] : null;
        foreach (var item in moving) { Draft.Extras.Remove(item); foreach (var members in Draft.Groups) members.Remove(item); }
        if (extra)
        {
            var at = before is null ? Draft.Extras.Count : Draft.Extras.IndexOf(before);
            Draft.Extras.InsertRange(Math.Max(0, at), moving);
        }
        else if (target is null || groupInsert || source.ID is null)
        {
            var at = target is null ? Draft.Groups.Count : Draft.Groups.IndexOf(target);
            Draft.Groups.Insert(at, moving);
        }
        else
        {
            var at = before is null ? target.Count : target.IndexOf(before);
            target.InsertRange(Math.Max(0, at), moving);
        }
        Draft.Groups.RemoveAll(g => g.Count == 0);
    }
    private Entry? EntryAt(Point point, out ListBoxItem? row)
    {
        row = _main.Items.Concat(_extra.Items).OfType<ListBoxItem>().FirstOrDefault(item =>
            item.TranslatePoint(default, this) is { } p && new Rect(p, item.Bounds.Size).Contains(point));
        return row?.Tag as Entry;
    }
    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _press = e.GetPosition(this); _pressed = EntryAt(_press, out _); _dragging = false;
        if (_pressed is { Extra: true }) _main.SelectedItem = null; else if (_pressed is not null) _extra.SelectedItem = null;
    }
    private void Moved(object? sender, PointerEventArgs e)
    {
        if (_pressed is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (Math.Abs(_press.X - e.GetPosition(this).X) + Math.Abs(_press.Y - e.GetPosition(this).Y) < 6) return;
        _dragging = true; e.Pointer.Capture(this); Cursor = new Cursor(StandardCursorType.DragMove);
        foreach (var list in new[] { _main, _extra })
        {
            var at = e.GetPosition(list);
            if (at.X < 0 || at.X > list.Bounds.Width || at.Y < 0 || at.Y > list.Bounds.Height) continue;
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (scroll is not null && (at.Y < 35 || at.Y > list.Bounds.Height - 35))
                scroll.Offset = new Vector(scroll.Offset.X, Math.Clamp(scroll.Offset.Y + (at.Y < 35 ? -20 : 20), 0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)));
        }
    }
    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging && _pressed is { } source)
        {
            var point = e.GetPosition(this); var target = EntryAt(point, out var row);
            var mainAt = _main.TranslatePoint(default, this); var extraAt = _extra.TranslatePoint(default, this);
            var extra = extraAt is { } a && new Rect(a, _extra.Bounds.Size).Contains(point);
            if (extra || mainAt is { } b && new Rect(b, _main.Bounds.Size).Contains(point))
            {
                var top = row?.TranslatePoint(default, this)?.Y ?? 0;
                Move(source, extra, target?.Group ?? -1, target?.ID, target?.ID is null && point.Y - top < 10);
                Refresh();
            }
            e.Handled = true;
        }
        _dragging = false; _pressed = null; Cursor = null; if (ReferenceEquals(e.Pointer.Captured, this)) e.Pointer.Capture(null);
    }
    private async Task SavePreset()
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new() { Title = Localize.Text("Save Toolbar Preset"), SuggestedFileName = "toolbar.json", DefaultExtension = "json" });
            if (file?.TryGetLocalPath() is { } path) Draft.Save(path, ToolCatalog.IDs);
        }
        catch (Exception ex) { _error.Text = Localize.Text("Could not save toolbar preset") + ": " + ex.Message; }
    }
    private async Task LoadPreset()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = Localize.Text("Load Toolbar Preset"), AllowMultiple = false });
            if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) { Draft = ToolbarLayout.Load(path, ToolCatalog.IDs); Refresh(); }
        }
        catch (Exception ex) { _error.Text = Localize.Text("Invalid toolbar preset") + ": " + ex.Message; }
    }
}
