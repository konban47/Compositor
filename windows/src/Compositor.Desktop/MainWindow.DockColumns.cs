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
    }
}
