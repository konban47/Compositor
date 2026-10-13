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
        // An empty/collapsed sidebar remains a drop target.
        var side = ScreenBounds(_layersSide);
        if (_layersSide.IsVisible && side.Inflate(12).Contains(point) && (_dockCollapsed || !_dockGroups.Any(g => g.Column >= 0 && Members(g).Any(p => p.Visible))))
        {
            var target = _dockGroups.First(g => g.ID == "inspector");
            return new(target, DockPlacement.Sidebar, target.Tabs.Items.Count);
        }
        return null;
    }
    private void ShowDockDrop(DockDrop? drop)
    {
        _dockDrop = drop; _dockSidePreview.IsVisible = false; foreach (var group in _dockGroups) group.Preview.IsVisible = false;
        if (drop is null) return;
        if (drop.Placement == DockPlacement.Sidebar) { _dockSidePreview.IsVisible = true; return; }
        var preview = drop.Group.Preview; preview.HorizontalAlignment = HorizontalAlignment.Stretch; preview.VerticalAlignment = VerticalAlignment.Stretch;
        preview.Width = double.NaN; preview.Height = double.NaN; preview.Margin = new Thickness(0);
        switch (drop.Placement)
        {
            case DockPlacement.Above: preview.VerticalAlignment = VerticalAlignment.Top; preview.Height = 5; break;
            case DockPlacement.Below: preview.VerticalAlignment = VerticalAlignment.Bottom; preview.Height = 5; break;
            case DockPlacement.Left: preview.HorizontalAlignment = HorizontalAlignment.Left; preview.Width = 5; break;
            case DockPlacement.Right: preview.HorizontalAlignment = HorizontalAlignment.Right; preview.Width = 5; break;
        }
        preview.IsVisible = true;
    }
    private void ApplyDockDrop(DockDrop drop, IReadOnlyList<DockPanelEntry> entries)
    {
        var target = drop.Group; var active = entries.FirstOrDefault(p => ReferenceEquals(p.Host?.SelectedItem, p.Tab));
        if (drop.Placement == DockPlacement.Sidebar)
        {
            target = _dockGroups.FirstOrDefault(g => g.Column >= 0 && g.Window is null) ?? MakeDockGroup(Guid.NewGuid().ToString("N"), column: 0);
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
        if (EditorPreferences.Current.LockWorkspace || entry?.Name == "Contextual Task Bar" && _contextPinned) { e.Handled = true; return; }
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
            if (entry?.Slot is null) ShowDockDrop(FindDockDrop(at, movingGroup));
            args.Handled = true;
        }
        void Release(object? sender, PointerReleasedEventArgs args)
        {
            var at = surface.PointToScreen(args.GetPosition(surface)); Cleanup(); args.Pointer.Capture(null);
            if (!started) { args.Handled = true; return; }
            if (started)
            {
                if (movingWindow is not null) movingWindow.Opacity = 1;
                if (entry?.Slot is { } slot)
                {
                    var bounds = ScreenBounds(slot); var local = this.PointToClient(at);
                    var back = entry.Name == "Tools" ? local.X < 70 : entry.Name == "Options" ? local.Y is > 50 and < 120 : local.Y > Bounds.Height - 75;
                    if (bounds.Contains(at.ToPoint(1)) || back) DockToolBack(entry);
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
