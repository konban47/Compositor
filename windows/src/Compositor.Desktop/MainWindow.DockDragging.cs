using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private enum DockPlacement { Tabs, Above, Below, Left, Right, Sidebar }
    private sealed record DockDrop(DockGroup Group, DockPlacement Placement, int Index);
    private DockDrop? _dockDrop;
    private static Rect ScreenBounds(Control control)
    {
        var a = control.PointToScreen(default); var b = control.PointToScreen(new Point(control.Bounds.Width, control.Bounds.Height));
        return new Rect(a.ToPoint(1), b.ToPoint(1));
    }
    private DockDrop? FindDockDrop(PixelPoint at, DockGroup? moving)
    {
        var point = at.ToPoint(1);
        foreach (var target in _dockGroups.Where(g => !ReferenceEquals(g, moving) && (g.Window is not null || g.Column >= 0) && g.Frame.IsEffectivelyVisible && TopLevel.GetTopLevel(g.Frame) is not null)
                     .OrderByDescending(g => g.Window is not null))
        {
            var bounds = ScreenBounds(target.Frame); if (!bounds.Contains(point)) continue;
            var scale = (target.Window ?? this).RenderScaling; var p = (point - bounds.TopLeft) / scale;
            var width = bounds.Width / scale; var height = bounds.Height / scale;
            var placement = p.Y is >= 21 and <= 57 ? DockPlacement.Tabs : p.Y < 12 ? DockPlacement.Above : p.Y > height - 18 ? DockPlacement.Below : p.X < 16 ? DockPlacement.Left : p.X > width - 16 ? DockPlacement.Right : DockPlacement.Tabs;
            var index = target.Tabs.Items.Count;
            if (placement == DockPlacement.Tabs)
                foreach (var entry in Members(target).Where(e => e.Visible))
                {
                    if (entry.Tab?.Header is Control header && header.IsEffectivelyVisible && point.X < ScreenBounds(header).Center.X) { index = target.Tabs.Items.IndexOf(entry.Tab); break; }
                }
            return new(target, placement, index);
        }
        // Collapsed columns retain their own targets; the left/right boundary creates a neighbouring column.
        foreach (var (column, frame) in _dockColumnFrames)
        {
            var bounds = ScreenBounds(frame); var scale = RenderScaling;
            if (point.Y < bounds.Top || point.Y > bounds.Bottom || point.X < bounds.Left - 12 * scale || point.X > bounds.Right + 12 * scale) continue;
            var target = _dockGroups.FirstOrDefault(g => g.Column == column && !ReferenceEquals(g, moving) && g.Window is null);
            if (target is null) continue;
            var placement = point.X < bounds.Left + 5 * scale ? DockPlacement.Left : point.X > bounds.Right - 5 * scale ? DockPlacement.Right : DockPlacement.Sidebar;
            return new(target, placement, target.Tabs.Items.Count);
        }
        var side = ScreenBounds(_layersSide);
        if (_layersSide.IsVisible && side.Inflate(12 * RenderScaling).Contains(point) && !_dockGroups.Any(g => g.Column >= 0 && Members(g).Any(p => p.Visible)))
        {
            var target = _dockGroups.First(g => g.ID == "inspector");
            return new(target, DockPlacement.Sidebar, target.Tabs.Items.Count);
        }
        return null;
    }
    private void ShowDockHint(Control target, DockPlacement placement)
    {
        if (target.TranslatePoint(default, _dockHintOverlay) is not { } at) return;
        var rect = new Rect(at, target.Bounds.Size);
        rect = placement switch
        {
            DockPlacement.Left => new Rect(rect.Left - 1, rect.Top, 2, rect.Height),
            DockPlacement.Right => new Rect(rect.Right - 1, rect.Top, 2, rect.Height),
            DockPlacement.Above => new Rect(rect.Left, rect.Top - 1, rect.Width, 2),
            DockPlacement.Below => new Rect(rect.Left, rect.Bottom - 1, rect.Width, 2),
            _ => rect.Deflate(2)
        };
        Canvas.SetLeft(_dockSidePreview, rect.X); Canvas.SetTop(_dockSidePreview, rect.Y);
        var outlined = placement is DockPlacement.Tabs or DockPlacement.Sidebar;
        _dockSidePreview.BorderThickness = new Thickness(outlined ? 2 : 0);
        _dockSidePreview.Background = outlined ? Brushes.Transparent : _dockSidePreview.BorderBrush;
        _dockSidePreview.Width = rect.Width; _dockSidePreview.Height = rect.Height; _dockSidePreview.IsVisible = true;
    }
    private void ShowDockDrop(DockDrop? drop)
    {
        _dockDrop = drop; _dockSidePreview.IsVisible = false; foreach (var group in _dockGroups) group.Preview.IsVisible = false;
        if (drop is null) return;
        var target = drop.Group.Frame;
        if (drop.Group.Window is not null)
        {
            // A native floating window has its own visual root.
            var preview = drop.Group.Preview; preview.BoxShadow = _dockSidePreview.BoxShadow; preview.IsVisible = true; return;
        }
        if ((drop.Placement is DockPlacement.Left or DockPlacement.Right or DockPlacement.Sidebar) && _dockColumnFrames.TryGetValue(drop.Group.Column, out var column))
            ShowDockHint(column, drop.Placement);
        else ShowDockHint(target.IsEffectivelyVisible && target.Parent is not null ? target : _layersSide, drop.Placement);
    }
    private DockPlacement? FindToolDrop(DockPanelEntry entry, PixelPoint at)
    {
        if (_dockEditorBody is null) return null;
        var p = _dockEditorBody.PointToClient(at); var size = _dockEditorBody.Bounds.Size;
        if (p.X < -24 || p.X > size.Width + 24 || p.Y < -24 || p.Y > size.Height + 24) return null;
        if (entry.Name == "Tools")
        {
            if (p.Y < 0 || p.Y > size.Height) return null;
            if (p.X < 28) return DockPlacement.Left;
            if (p.X > size.Width - 28) return DockPlacement.Right;
        }
        else if (entry.Name == "Options" && p.Y < 24) return DockPlacement.Above;
        else if (entry.Name == "Contextual Task Bar" && p.Y > size.Height - 28) return DockPlacement.Below;
        return null;
    }
    private void ShowToolDrop(DockPlacement? placement)
    {
        ShowDockDrop(null);
        if (placement is { } side && _dockEditorBody is { } body) ShowDockHint(body, side);
    }
    private void ApplyDockDrop(DockDrop drop, IReadOnlyList<DockPanelEntry> entries)
    {
        var target = drop.Group; var active = entries.FirstOrDefault(p => ReferenceEquals(p.Host?.SelectedItem, p.Tab));
        if (drop.Placement == DockPlacement.Sidebar)
        {
            target = drop.Group.Column >= 0 && drop.Group.Window is null ? drop.Group : _dockGroups.FirstOrDefault(g => g.Column >= 0 && g.Window is null) ?? MakeDockGroup(Guid.NewGuid().ToString("N"), column: 0);
        }
        else if (drop.Placement != DockPlacement.Tabs)
        {
            // Edges in the editor create a new row/column; floating palettes merge as tabs.
            if (target.Window is null)
            {
                var column = target.Column; var order = target.Order + (drop.Placement == DockPlacement.Above ? -.5 : .5);
                if (drop.Placement is DockPlacement.Left or DockPlacement.Right)
                {
                    column += drop.Placement == DockPlacement.Right ? 1 : 0;
                    foreach (var group in _dockGroups.Where(g => g.Column >= column && g.Window is null)) group.Column++;
                    _dockExpandedWidth = Math.Min(Math.Max(440, _dockExpandedWidth + 200), Math.Max(440, Bounds.Width * .7)); order = 0;
                }
                target = MakeDockGroup(Guid.NewGuid().ToString("N"), column: column, order: order); target.Weight = Math.Max(65, drop.Group.Weight / 2); target.ColumnWeight = drop.Group.ColumnWeight;
            }
        }
        target.Collapsed = false;
        foreach (var group in _dockGroups.Where(g => g.Column == target.Column && g.Window is null)) group.ColumnCollapsed = false;
        var index = drop.Placement == DockPlacement.Tabs ? drop.Index : -1;
        foreach (var entry in entries) { MovePanel(entry, target, index); if (index >= 0) index++; }
        if (active is { Visible: true }) SelectPanel(active); else SelectVisiblePanel(target.Tabs);
        foreach (var column in _dockGroups.Where(g => g.Column >= 0).GroupBy(g => g.Column))
        { var i = 0; foreach (var group in column.OrderBy(g => g.Order)) group.Order = i++; }
        BuildGroupChrome(target); _dockCollapsed = false; CloseEmptyGroups(); RenderDockArea();
    }
    private void StartDockDrag(DockPanelEntry? entry, DockGroup? wholeGroup, Control handle, PointerPressedEventArgs e)
    {
        if (e.Handled || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed || TopLevel.GetTopLevel(handle) is not { } top) return;
        var surface = top as Window ?? this;
        // Selection happens on press even if the user never crosses the drag threshold.
        if (entry is not null) SelectPanel(entry);
        if (EditorPreferences.Current.LockWorkspace || entry?.Name == "Contextual Task Bar" && _contextPinned)
        {
            if (entry?.Tab is not null && handle.Tag is string iconTag && iconTag.StartsWith("panel-icon-", StringComparison.Ordinal)) PeekDockPanel(entry, handle);
            e.Handled = true; return;
        }
        if (e.ClickCount == 2 && entry?.Slot is not null) { ToggleToolCollapse(entry); e.Handled = true; return; }
        if (e.ClickCount == 2 && wholeGroup is { } collapse) { ToggleGroupCollapse(collapse); e.Handled = true; return; }
        var before = CaptureDockWorkspace(); var origin = handle.PointToScreen(e.GetPosition(handle));
        var entries = wholeGroup is null ? new[] { entry! } : Members(wholeGroup).ToArray();
        var originalGroup = wholeGroup ?? entry?.Group; var movingGroup = originalGroup; var originalWindow = entry?.Floating ?? wholeGroup?.Window;
        var offset = originalWindow is null ? new PixelVector((int)(20 * surface.RenderScaling), (int)(32 * surface.RenderScaling)) : origin - originalWindow.Position;
        var started = false; Window? movingWindow = null;
        void Cleanup()
        {
            surface.RemoveHandler(PointerMovedEvent, Move); surface.RemoveHandler(PointerReleasedEvent, Release); surface.PointerCaptureLost -= Lost;
            surface.RemoveHandler(KeyDownEvent, DragKey); _cancelDockDrag = null;
        }
        void Stop(bool cancel)
        {
            Cleanup(); ShowDockDrop(null); if (movingWindow is not null) movingWindow.Opacity = 1;
            _draggingDock = false; _dragOriginGroup = null;
            if (cancel && started) RestoreDockWorkspace(before); else if (started) { CloseEmptyGroups(); RenderDockArea(); }
        }
        void DragKey(object? sender, KeyEventArgs args) { if (args.Key == Key.Escape) { Stop(true); e.Pointer.Capture(null); args.Handled = true; } }
        void Lost(object? sender, PointerCaptureLostEventArgs args) => Stop(false);
        void Move(object? sender, PointerEventArgs args)
        {
            var at = surface.PointToScreen(args.GetPosition(surface));
            if (!started && Math.Abs(at.X - origin.X) + Math.Abs(at.Y - origin.Y) < 7 * surface.RenderScaling) return;
            if (!started)
            {
                started = true; _draggingDock = true; _dragOriginGroup = originalGroup;
                if (wholeGroup is not null) FloatGroup(wholeGroup, at - offset); else FloatPanel(entry!, at - offset);
                movingGroup = wholeGroup ?? entry?.Group; movingWindow = entry?.Floating ?? wholeGroup?.Window;
                if (movingWindow is not null) movingWindow.Opacity = .72;
            }
            if (movingWindow is not null) movingWindow.Position = at - offset;
            UpdateLayout(); foreach (var floating in _dockGroups.Select(g => g.Window).OfType<Window>()) floating.UpdateLayout();
            if (entry?.Slot is not null) ShowToolDrop(FindToolDrop(entry, at)); else ShowDockDrop(FindDockDrop(at, movingGroup));
            args.Handled = true;
        }
        void Release(object? sender, PointerReleasedEventArgs args)
        {
            var at = surface.PointToScreen(args.GetPosition(surface)); Cleanup(); args.Pointer.Capture(null);
            if (!started) { if (entry?.Tab is not null && handle.Tag is string tag && tag.StartsWith("panel-icon-", StringComparison.Ordinal)) PeekDockPanel(entry, handle); args.Handled = true; return; }
            if (started)
            {
                if (movingWindow is not null) movingWindow.Opacity = 1;
                if (entry?.Slot is not null)
                {
                    if (FindToolDrop(entry, at) is { } placement)
                    { if (entry.Name == "Tools") entry.ToolDockRight = placement == DockPlacement.Right; DockToolBack(entry); }
                }
                else if (FindDockDrop(at, movingGroup) is { } drop) ApplyDockDrop(drop, entries);
            }
            ShowDockDrop(null); _draggingDock = false; _dragOriginGroup = null; CloseEmptyGroups(); RenderDockArea(); args.Handled = true;
        }
        surface.AddHandler(PointerMovedEvent, Move, RoutingStrategies.Tunnel, true); surface.AddHandler(PointerReleasedEvent, Release, RoutingStrategies.Tunnel, true);
        surface.AddHandler(KeyDownEvent, DragKey, RoutingStrategies.Tunnel, true); surface.PointerCaptureLost += Lost;
        _cancelDockDrag = () => { Stop(true); e.Pointer.Capture(null); }; e.Pointer.Capture(surface); e.Handled = true;
    }
}
