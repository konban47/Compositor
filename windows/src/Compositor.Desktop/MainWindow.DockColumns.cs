using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private void RememberDockSizes()
    {
        if (_restoringDock) return;
        foreach (var group in _dockGroups.Where(g => g.Window is null && g.Column >= 0 && !g.ColumnCollapsed))
        {
            if (group.Frame.IsAttachedToVisualTree() && group.Frame.Bounds.Width > 90) group.ColumnWeight = group.Frame.Bounds.Width;
            if (!group.Collapsed && group.Frame.Bounds.Height > 65) group.Weight = group.Frame.Bounds.Height;
        }
    }
    private void SetColumnCollapsed(int column, bool collapsed)
    {
        RememberDockSizes();
        foreach (var group in _dockGroups.Where(g => g.Column == column && g.Window is null)) group.ColumnCollapsed = collapsed;
        RenderDockArea();
    }
    // Kept for old saved workspaces and workspace reset; UI arrows always address one column.
    private void SetDockCollapsed(bool collapsed)
    {
        RememberDockSizes();
        foreach (var group in _dockGroups.Where(g => g.Column >= 0 && g.Window is null)) group.ColumnCollapsed = collapsed;
        RenderDockArea();
    }
    private void RenderDockArea()
    {
        if (_dockGroups.Count == 0) return;
        _dockPeek?.Hide(); RememberDockSizes();
        foreach (var group in _dockGroups.Where(g => g.Window is null)) Detach(group.Frame);
        _dockRoot.Children.Clear(); _dockRoot.ColumnDefinitions.Clear(); _dockRoot.RowDefinitions.Clear(); _dockColumnFrames.Clear();
        var docked = _dockGroups.Where(g => g.Window is null && g.Column >= 0 && (Members(g).Any(p => p.Visible) || _draggingDock && ReferenceEquals(g, _dragOriginGroup))).OrderBy(g => g.Column).ThenBy(g => g.Order).ToArray();
        var columns = docked.GroupBy(g => g.Column).ToArray();
        _dockCollapsed = columns.Length > 0 && columns.All(c => c.First().ColumnCollapsed);
        _dockRoot.ColumnDefinitions.Add(new ColumnDefinition(5, GridUnitType.Pixel));
        var resize = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeWestEast), Tag = "dock-width-grip" };
        resize.PointerPressed += (_, e) =>
        {
            var first = columns.FirstOrDefault(c => !c.First().ColumnCollapsed);
            if (first is null || !e.GetCurrentPoint(resize).Properties.IsLeftButtonPressed || EditorPreferences.Current.LockWorkspace) return;
            var definition = _dockRoot.ColumnDefinitions[1 + Array.IndexOf(columns, first) * 2];
            var start = e.GetPosition(this).X; var width = definition.ActualWidth; var sideWidth = _layersSide.Width;
            void Move(object? s, PointerEventArgs a)
            {
                var value = Math.Clamp(width + start - a.GetPosition(this).X, 100, Math.Max(100, Bounds.Width * .7 - sideWidth + width));
                definition.Width = new GridLength(value); _layersSide.Width = sideWidth + value - width;
            }
            void Release(object? s, PointerReleasedEventArgs a) { resize.PointerMoved -= Move; resize.PointerReleased -= Release; a.Pointer.Capture(null); RememberDockSizes(); }
            resize.PointerMoved += Move; resize.PointerReleased += Release; e.Pointer.Capture(resize); e.Handled = true;
        };
        _dockRoot.Children.Add(resize);
        double total = 5, expanded = 5;
        for (var c = 0; c < columns.Length; c++)
        {
            var groups = columns[c]; var id = groups.Key; var collapsed = groups.First().ColumnCollapsed;
            if (c > 0)
            {
                _dockRoot.ColumnDefinitions.Add(new ColumnDefinition(4, GridUnitType.Pixel)); total += 4; expanded += 4;
                Control separator = collapsed || columns[c - 1].First().ColumnCollapsed
                    ? new Border { Background = Skin.BorderSubtleBrush }
                    : new GridSplitter { ResizeDirection = GridResizeDirection.Columns, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Skin.BorderSubtleBrush };
                Grid.SetColumn(separator, _dockRoot.ColumnDefinitions.Count - 1); _dockRoot.Children.Add(separator);
            }
            var savedWidth = groups.First().ColumnWeight;
            var width = savedWidth > 90 ? savedWidth : Math.Max(100, (_dockExpandedWidth - 5 - (columns.Length - 1) * 4) / Math.Max(1, columns.Length));
            foreach (var group in groups) group.ColumnWeight = width;
            var actual = collapsed ? 36 : width; total += actual; expanded += width;
            _dockRoot.ColumnDefinitions.Add(new ColumnDefinition(actual, GridUnitType.Pixel) { MinWidth = collapsed ? 36 : 100 });
            var column = new Grid { RowDefinitions = new RowDefinitions("11,*"), Background = Skin.ChromeBrush, Tag = "dock-column-" + id };
            _dockColumnFrames[id] = column;
            var toggle = DockTitleButton(collapsed ? "«" : "»", collapsed ? "Expand Panels" : "Collapse Panels to Icons", () => SetColumnCollapsed(id, !collapsed), (collapsed ? "expand-dock-" : "collapse-dock-") + id);
            toggle.HorizontalAlignment = HorizontalAlignment.Right; column.Children.Add(toggle);
            if (collapsed)
            {
                var icons = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
                foreach (var group in groups)
                {
                    if (icons.Children.Count > 0) icons.Children.Add(new Border { Height = 1, Margin = new Thickness(2, 3), Background = Skin.BorderSubtleBrush });
                    foreach (var entry in Members(group).Where(p => p.Visible))
                    {
                        Button? button = null; button = PanelIcon(entry, () => PeekDockPanel(entry, button!), "panel-icon-" + entry.Name);
                        button.AddHandler(PointerPressedEvent, (_, e) => StartDockDrag(entry, null, button, e), Avalonia.Interactivity.RoutingStrategies.Tunnel);
                        icons.Children.Add(button);
                    }
                }
                var scroll = new ScrollViewer { Content = icons, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                Grid.SetRow(scroll, 1); column.Children.Add(scroll);
            }
            else
            {
                var rows = new Grid(); Grid.SetRow(rows, 1); column.Children.Add(rows);
                foreach (var group in groups)
                {
                    if (rows.RowDefinitions.Count > 0)
                    {
                        rows.RowDefinitions.Add(new RowDefinition(4, GridUnitType.Pixel));
                        var split = new GridSplitter { ResizeDirection = GridResizeDirection.Rows, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Skin.BorderSubtleBrush };
                        Grid.SetRow(split, rows.RowDefinitions.Count - 1); rows.Children.Add(split);
                    }
                    rows.RowDefinitions.Add(group.Collapsed ? new RowDefinition(GridLength.Auto) : new RowDefinition(Math.Max(1, group.Weight), GridUnitType.Star) { MinHeight = 65 });
                    Grid.SetRow(group.Frame, rows.RowDefinitions.Count - 1); rows.Children.Add(group.Frame);
                }
            }
            Grid.SetColumn(column, _dockRoot.ColumnDefinitions.Count - 1); _dockRoot.Children.Add(column);
        }
        _dockExpandedWidth = Math.Max(220, expanded);
        _layersSide.Width = columns.Length == 0 ? 30 : total;
        RenderBottomDock();
    }

    /// <summary>The group docked under the canvas: its height is kept while it's dragged, and it folds to a strip of its
    /// panels' names when collapsed.</summary>
    private void RenderBottomDock()
    {
        if (_bottomDockRow is null) return;
        var group = _dockGroups.FirstOrDefault(g => g.Bottom && g.Window is null && Members(g).Any(p => p.Visible));
        if (!ReferenceEquals(_bottomDock.Child, group?.Frame)) { if (group is not null) Detach(group.Frame); _bottomDock.Child = group?.Frame; }
        var shown = group is not null;
        _bottomDock.IsVisible = shown; _bottomDockSplitter.IsVisible = shown && !group!.Collapsed;
        _bottomDockRow.MinHeight = shown ? group!.Collapsed ? 0 : 120 : 0;
        _bottomDockRow.Height = shown ? group!.Collapsed ? GridLength.Auto : new GridLength(_bottomDockHeight) : new GridLength(0);
    }
    private readonly Border _bottomDock = new() { IsVisible = false, Tag = "bottom-dock" };
    private readonly GridSplitter _bottomDockSplitter = new() { IsVisible = false, ResizeDirection = GridResizeDirection.Rows, HorizontalAlignment = HorizontalAlignment.Stretch, Height = 4, Background = Skin.BorderSubtleBrush };
    private RowDefinition? _bottomDockRow;
    private double _bottomDockHeight = 300;

    /// <summary>The height the tool rail has, from its top down to the status bar.</summary>
    internal double RailRoom()
    {
        var top = _dockEditorBody is { } body ? _rail.TranslatePoint(default, body) : null;
        return top is { } at && _dockEditorBody!.Bounds.Height > 0 ? Math.Max(100, _dockEditorBody.Bounds.Height - at.Y - 2) : Math.Max(100, ClientSize.Height - 150);
    }

    /// <summary>The panels Photoshop docks under the document, across the window.</summary>
    private static readonly string[] BottomPanels = ["Timeline", "Measurement Log"];
    /// <summary>The panels that start in the workspace, which never need placing.</summary>
    private static readonly string[] CorePanels = ["Properties", "History", "Layers", "Channels"];
    /// <summary>Panels Photoshop keeps in one tab group: opening one joins the others when they're showing.</summary>
    private static readonly string[][] PanelFamilies =
    [
        ["Character", "Paragraph"], ["Character Styles", "Paragraph Styles"], ["Brush Settings", "Brushes"], ["Navigator", "Histogram", "Info"],
        ["Color", "Swatches", "Gradients", "Patterns"], ["Shapes", "Styles"], ["Actions", "Tool Presets"], ["Notes", "Clone Source"], ["Materials", "Libraries"],
    ];
    /// <summary>How big a panel opens when it floats on its own; others take the default.</summary>
    private static Size? PanelSize(string name) => name switch
    {
        "Timeline" => new Size(1160, 540), "Character" => new Size(350, 480), "Paragraph" => new Size(350, 450), "Layer Comps" => new Size(400, 300),
        "Brush Settings" => new Size(500, 810), "Navigator" or "Histogram" or "Info" => new Size(300, 320), "Glyphs" => new Size(420, 360),
        "Brushes" => new Size(380, 520), "Actions" or "Tool Presets" or "Clone Source" => new Size(320, 400), "Notes" => new Size(320, 300), _ => null,
    };

    /// <summary>
    /// The first time a panel opens, it goes where Photoshop puts it: the Timeline and Measurement Log under the canvas;
    /// Paths with Layers; Adjustments in the Properties group; the color panels in a group of their own at the top of the
    /// dock; a panel of a tab group with its fellows; anything else floating beside the dock.
    /// </summary>
    private void PlacePanel(DockPanelEntry entry)
    {
        if (entry.Placed || entry.Tab is null) return;
        entry.Placed = true;
        if (_restoringDock || CorePanels.Contains(entry.Name)) return;
        if (BottomPanels.Contains(entry.Name)) { DockToBottom(entry); return; }
        if (entry.Name == "Paths") { if (_dockGroups.FirstOrDefault(g => g.ID == "layers" && g.Column >= 0) is { } layers) { MovePanel(entry, layers); return; } }
        if (entry.Name is "Adjustments" or "Plugins") { if (_dockGroups.FirstOrDefault(g => g.ID == "inspector" && g.Column >= 0) is { } inspector) { MovePanel(entry, inspector); return; } }
        var family = PanelFamilies.FirstOrDefault(f => f.Contains(entry.Name));
        if (family?.Select(n => _dockPanels.GetValueOrDefault(n)).FirstOrDefault(p => p is { Visible: true, Placed: true, Group: not null } && !ReferenceEquals(p, entry)) is { Group: { } fellow })
        { MovePanel(entry, fellow); if (fellow.Window is not null) BuildGroupChrome(fellow); return; }
        if (entry.Name is "Color" or "Swatches" or "Gradients" or "Patterns")
        {
            var top = _dockGroups.FirstOrDefault(g => g.ID == "color") ?? MakeDockGroup("color", null, 0, -1);
            top.Column = 0; top.Order = -1; top.Weight = 3; top.ColumnCollapsed = _dockGroups.FirstOrDefault(g => g.ID == "inspector")?.ColumnCollapsed ?? false; MovePanel(entry, top); BuildGroupChrome(top); return;
        }
        if (_workspacePlacing)
        {
            // The built-in workspaces dock what they show, as a group at the top of the dock.
            var id = family?[0] ?? entry.Name;
            var group = _dockGroups.FirstOrDefault(g => g.ID == id) ?? MakeDockGroup(id, null, 0, -1);
            group.Column = 0; group.Order = -1; group.Weight = 3; MovePanel(entry, group); BuildGroupChrome(group); return;
        }
        FloatPanel(entry, NextFloatPosition());
    }
    private bool _workspacePlacing;

    /// <summary>Where a panel floats: just left of the dock, under the bars, each new one stepped down and left.</summary>
    private PixelPoint NextFloatPosition()
    {
        var step = _dockGroups.Count(g => g.Window is not null) % 8 * 28;
        var size = PanelFloatWidth * RenderScaling;
        if (_dockEditorBody is { } body && body.Bounds.Width > 0)
        {
            var corner = body.PointToScreen(new Point(body.Bounds.Width, 12));
            return new PixelPoint((int)(corner.X - size - 12 * RenderScaling - step * RenderScaling), (int)(corner.Y + step * RenderScaling));
        }
        return new PixelPoint(Position.X + 80 + step, Position.Y + 100 + step);
    }
    private const double PanelFloatWidth = 340;

    /// <summary>Docks a panel under the canvas, with whatever is docked there already as tabs beside it.</summary>
    private void DockToBottom(DockPanelEntry entry)
    {
        if (EditorPreferences.Current.LockWorkspace && !_restoringDock || entry.Tab is null) return;
        var bottom = _dockGroups.FirstOrDefault(g => g.Bottom && g.Window is null) ?? MakeDockGroup("bottom-" + Guid.NewGuid().ToString("N"));
        bottom.Bottom = true; bottom.Column = -1; bottom.Collapsed = false; entry.Visible = true; entry.Tab.IsVisible = true;
        var was = entry.Group; MovePanel(entry, bottom); BuildGroupChrome(bottom);
        if (was is { Window: { } window } && !ReferenceEquals(was, bottom) && !Members(was).Any(p => p.Visible)) window.Close();
        CloseEmptyGroups(); RenderDockArea();
    }

    /// <summary>What a panel's own ≡ offers, as Photoshop's panel menus do; a panel with no commands of its own has only
    /// the closing and floating ones.</summary>
    private IEnumerable<Control> PanelOwnItems(DockPanelEntry entry)
    {
        switch (entry.Name)
        {
            case "Layers":
                yield return Command("New Layer…", NewBlankLayer); yield return Command("Duplicate Layer…", () => _ = DuplicateNamedLayer());
                yield return Command("Delete Layer", DeletePanelTarget); yield return Command("New Group", NewFolder);
                yield return new Separator();
                yield return Command("Merge Visible", () => MergeAll(false)); yield return Command("Flatten Image", () => _ = FlattenImage());
                break;
            case "Channels":
                yield return Command("Split Channels", SplitChannels); yield return Command("Channel Pack…", () => _ = ExportChannelPack());
                break;
            case "History":
                yield return Command("Create Snapshot", MakeSnapshot); yield return Command("New Document from State", NewDocumentFromHistory);
                break;
            case "Timeline": case "Actions": case "Layer Comps": break;
            case "Character" or "Paragraph" when _text is not null || CurrentType is not null:
                var flyout = BuildTypographyMenu(CurrentType ?? TypeDefaults, entry.Name == "Paragraph");
                var items = flyout.Items.OfType<Control>().ToArray(); flyout.Items.Clear();
                foreach (var item in items) yield return item;
                break;
        }
    }
}
