using Avalonia;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private WorkspaceLayout CaptureDockWorkspace() => new()
    {
        SidebarWidth = _dockExpandedWidth, SidebarCollapsed = _dockCollapsed, ContextBarPinned = _contextPinned,
        Panels = _dockPanels.Values.Select(p =>
        {
            var g = p.Group; var w = p.Floating;
            return new WorkspacePanelState(p.Name, p.Visible, w is not null || g?.Column < 0, w?.Position.X ?? 0, w?.Position.Y ?? 0,
                w?.Width ?? 340, w?.Height ?? 510, g?.ID, g?.Tabs.Items.IndexOf(p.Tab) ?? 0, g?.Column ?? 0, (int)(g?.Order ?? 0),
                g?.Collapsed ?? p.ToolCollapsed, p.Tab is not null && ReferenceEquals(g?.Tabs.SelectedItem, p.Tab), p.LastDockGroup,
                g is { Collapsed: false } && g.Window is null && g.Frame.Bounds.Height > 65 ? g.Frame.Bounds.Height : g?.Weight ?? 1,
                g is { Window: null, ColumnCollapsed: false } && g.Frame.Bounds.Width > 90 ? g.Frame.Bounds.Width : g?.ColumnWeight ?? 1, g?.ExpandedHeight ?? p.ToolHeight, g?.ExpandedWidth ?? p.ToolWidth, g?.ColumnCollapsed ?? false, p.ToolDockRight);
        }).ToList()
    };
    private void RestoreDockWorkspace(WorkspaceLayout layout)
    {
        var wasLocked = EditorPreferences.Current.LockWorkspace; EditorPreferences.Current.LockWorkspace = false;
        _restoringDock = true;
        try
        {
            ResetDockPanels();
            var savedPanels = layout.Panels.Take(80).Where(p => p is not null && (WindowPanelNames.Contains(p.Name) || p.Name is "Tools" or "Options" or "Contextual Task Bar" or "Plugins")).DistinctBy(p => p.Name).ToArray();
            foreach (var saved in savedPanels) OpenWindowPanel(saved.Name);
            foreach (var saved in savedPanels.OrderBy(p => p.Order))
            {
                if (!_dockPanels.TryGetValue(saved.Name, out var panel)) continue;
                if (panel.Tab is not null && saved.Group is { Length: > 0 and <= 80 } groupID)
                {
                    var group = _dockGroups.FirstOrDefault(g => g.ID == groupID) ?? MakeDockGroup(groupID);
                    group.Column = saved.Floating ? -1 : Math.Clamp(saved.Column, 0, 7); group.Order = Math.Clamp(saved.Row, 0, 80);
                    group.Weight = double.IsFinite(saved.Weight) ? Math.Clamp(saved.Weight, 1, 3000) : 1;
                    group.ColumnWeight = WorkspaceSize(saved.ColumnWeight, 1, 3000, 1); group.ExpandedHeight = WorkspaceSize(saved.ExpandedHeight, 100, 1600, 510);
                    group.ExpandedWidth = WorkspaceSize(saved.ExpandedWidth, 90, 2000, 340); group.Collapsed = saved.Collapsed; group.ColumnCollapsed = saved.ColumnCollapsed || layout.SidebarCollapsed; MovePanel(panel, group); panel.LastDockGroup = saved.ReturnGroup ?? HomeGroup(panel).ID;
                }
                if (panel.Slot is not null)
                { panel.ToolDockRight = saved.ToolDockRight; if (panel.Name == "Tools") Avalonia.Controls.Grid.SetColumn(panel.Slot, panel.ToolDockRight ? 2 : 0); panel.ToolCollapsed = saved.Collapsed; panel.ToolWidth = WorkspaceSize(saved.ExpandedWidth, 90, 2000, 470); panel.ToolHeight = WorkspaceSize(saved.ExpandedHeight, 52, 1600, 120); panel.Slot.Child = ToolChrome(panel); }
                panel.Visible = saved.Visible;
                if (panel.Tab is not null) panel.Tab.IsVisible = saved.Visible; else panel.Slot!.IsVisible = saved.Visible;
            }
            foreach (var group in _dockGroups.ToArray())
            {
                var saved = savedPanels.FirstOrDefault(p => p.Group == group.ID);
                if (saved is not null && saved.Floating && Members(group).Any(p => p.Visible))
                {
                    var position = WorkspacePosition(saved); FloatGroup(group, position);
                    if (group.Window is { } window)
                    { window.Width = WorkspaceSize(saved.Width, 90, 2000, 340); window.MinHeight = group.Collapsed ? 46 : 100; window.Height = WorkspaceSize(saved.Height, 46, 1600, 510); }
                }
                BuildGroupChrome(group); SelectVisiblePanel(group.Tabs);
            }
            foreach (var saved in savedPanels)
            {
                if (!_dockPanels.TryGetValue(saved.Name, out var panel)) continue;
                // Workspaces saved by 1.5.1.2 have no group identity.
                if (saved.Visible && saved.Floating && (saved.Group is null || panel.Slot is not null))
                {
                    FloatPanel(panel, WorkspacePosition(saved));
                    if (panel.Floating is { } window)
                    {
                        if (panel.Name != "Tools") window.Width = WorkspaceSize(saved.Width, panel.Name == "Options" ? 420 : 90, 3000, 760);
                        window.Height = panel.Name == "Options" ? 36 : WorkspaceSize(saved.Height, 52, 1600, 510);
                    }
                }
                if (saved.Active && saved.Visible) SelectPanel(panel);
            }
            _dockExpandedWidth = WorkspaceSize(layout.SidebarWidth, 220, 1200, 340); _dockCollapsed = layout.SidebarCollapsed; _contextPinned = layout.ContextBarPinned;
            CloseEmptyGroups(); RenderDockArea();
        }
        finally { _restoringDock = false; EditorPreferences.Current.LockWorkspace = wasLocked; }
    }
    private PixelPoint WorkspacePosition(WorkspacePanelState saved)
    {
        var point = new PixelPoint(saved.X, saved.Y);
        return Screens.All.Any(s => s.WorkingArea.Contains(point)) ? point : new(Position.X + 80, Position.Y + 100);
    }
    private static double WorkspaceSize(double value, double minimum, double maximum, double fallback) => double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
}
