using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private Border DockTool(string name, Control body)
    {
        var slot = new Border(); var entry = new DockPanelEntry(name, body, null, null, slot); _dockPanels[name] = entry;
        slot.Child = ToolChrome(entry); return slot;
    }
    private Control ToolChrome(DockPanelEntry entry)
    {
        Detach(entry.Body);
        if (entry.Name == "Options") return OptionsChrome(entry);
        if (entry.Name == "Tools") _rail.SetColumns(entry.ToolCollapsed);
        var dock = new DockPanel { Background = Skin.ChromeBrush };
        var header = new DockPanel { Height = 11, Background = Skin.TabStripBrush };
        if (entry.Name != "Tools" || entry.ToolWindow is not null)
        { var close = DockTitleButton("×", "Close Panel", () => HidePanel(entry), "close-panel-" + entry.Name); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close); }
        var menu = new ContextMenu();
        menu.Items.Add(Command("Float Panel", () => FloatPanel(entry))); menu.Items.Add(Command("Close Panel", () => HidePanel(entry)));
        if (entry.Name == "Contextual Task Bar")
        {
            menu.Items.Add(new Separator()); menu.Items.Add(Command("More Properties", () => OpenInspector(0)));
            menu.Items.Add(Command("Reset Bar Position", ResetContextBar));
            var pin = Command("Pin Bar Position", () => _contextPinned = !_contextPinned); pin.ToggleType = MenuItemToggleType.CheckBox;
            menu.Opening += (_, _) => pin.IsChecked = _contextPinned; menu.Items.Add(pin);
        }
        menu.Items.Add(Command("Collapse or Expand Panels", () => ToggleToolCollapse(entry)));
        { var collapse = DockTitleButton(entry.ToolCollapsed ? "«" : "»", "Collapse or Expand Panels", () => ToggleToolCollapse(entry), "collapse-tool-" + entry.Name); DockPanel.SetDock(collapse, Dock.Right); header.Children.Add(collapse); }
        header.ContextMenu = menu;
        if (entry.Name != "Tools") { var options = DockTitleButton("•••", "Panel Options", () => menu.Open(header)); DockPanel.SetDock(options, Dock.Right); header.Children.Add(options); }
        var title = new TextBlock { Text = entry.Name == "Tools" || entry.Name == "Contextual Task Bar" && entry.ToolWindow is null ? "" : Localize.Text(entry.Name), MinWidth = 0, FontSize = 9, Foreground = Skin.LabelBrush, Background = Brushes.Transparent,
            Margin = new Thickness(2, 0), TextTrimming = TextTrimming.CharacterEllipsis, Tag = "panel-handle-" + entry.Name };
        header.Children.Add(title); title.AddHandler(PointerPressedEvent, (_, e) => StartDockDrag(entry, null, title, e), RoutingStrategies.Tunnel);
        DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header);
        if (entry.Name == "Tools")
        {
            var grip = new Border { Height = 5, Background = Skin.ChromeBrush, Cursor = new Cursor(StandardCursorType.SizeAll), Tag = "tool-drag-grip" };
            grip.PointerPressed += (_, e) => StartDockDrag(entry, null, grip, e); DockPanel.SetDock(grip, Dock.Top); dock.Children.Add(grip);
        }
        dock.Children.Add(entry.ToolCollapsed && entry.Name != "Tools" ? PanelIcon(entry, () => ToggleToolCollapse(entry), "panel-icon-" + entry.Name) : entry.Body);
        return new Border { Child = dock, BorderBrush = Skin.BorderSubtleBrush, BorderThickness = new Thickness(entry.ToolWindow is null ? 0 : 1) };
    }
    private Control OptionsChrome(DockPanelEntry entry)
    {
        entry.ToolCollapsed = false;
        var row = new DockPanel { Background = Skin.ChromeBrush };
        var grip = new Border { Width = entry.ToolWindow is null ? 0 : 10, Background = Skin.TabStripBrush,
            Cursor = new Cursor(StandardCursorType.SizeAll), Tag = "panel-handle-Options" };
        grip.PointerPressed += (_, e) => StartDockDrag(entry, null, grip, e);
        DockPanel.SetDock(grip, Dock.Left); row.Children.Add(grip); row.Children.Add(entry.Body);
        var menu = new ContextMenu(); menu.Items.Add(Command("Float Panel", () => FloatPanel(entry))); row.ContextMenu = menu;
        return row;
    }
    private void FloatTool(DockPanelEntry entry, PixelPoint? at)
    {
        if (entry.ToolWindow is not null) { entry.ToolWindow.Activate(); return; }
        entry.Visible = true; entry.Slot!.Child = null; entry.Slot.IsVisible = false;
        var context = entry.Name == "Contextual Task Bar";
        var window = NewFloatingWindow(entry.Name == "Tools" ? 110 : context ? 470 : 760, entry.Name == "Tools" ? 620 : context ? 62 : 120, at ?? new(Position.X + 80, Position.Y + 100));
        window.MinHeight = entry.ToolCollapsed || context ? 52 : 80;
        if (entry.Name == "Tools") { window.MinWidth = (entry.ToolCollapsed ? 80 : 44) + 2; window.MaxWidth = window.MinWidth; window.Width = window.MinWidth; window.Height = entry.ToolCollapsed ? 610 : 850; }
        else if (entry.Name == "Options") { window.MinHeight = window.MaxHeight = window.Height = 36; window.MinWidth = 420; window.Width = Math.Max(760, Bounds.Width - 30); }
        else if (entry.ToolCollapsed) { window.Width = 120; window.Height = 56; }
        entry.ToolWindow = window; window.Title = Localize.Text(entry.Name); window.Content = ToolChrome(entry);
        AddFloatingResize((Control)window.Content, window);
        window.Closed += (_, _) =>
        {
            if (!ReferenceEquals(entry.ToolWindow, window)) return;
            Detach(entry.Body); window.Content = null; entry.ToolWindow = null;
            if (!_closingPanels) { entry.Visible = false; entry.Slot.Child = ToolChrome(entry); entry.Slot.IsVisible = false; }
        };
        window.Show(this);
    }
    private void DockToolBack(DockPanelEntry entry)
    {
        if (EditorPreferences.Current.LockWorkspace || entry.Name == "Contextual Task Bar" && _contextPinned) return;
        Detach(entry.Body); var window = entry.ToolWindow; entry.ToolWindow = null; if (window is not null) { window.Content = null; window.Close(); }
        entry.Visible = true; entry.Slot!.Child = ToolChrome(entry); entry.Slot.IsVisible = true;
        if (entry.Name == "Tools") Grid.SetColumn(entry.Slot, entry.ToolDockRight ? 2 : 0);
    }
    private void ToggleToolCollapse(DockPanelEntry entry)
    {
        if (entry.Name == "Options") return;
        entry.ToolCollapsed = !entry.ToolCollapsed;
        if (entry.Name == "Tools")
        {
            if (entry.ToolWindow is { } toolsWindow)
            {
                toolsWindow.MinWidth = 0; toolsWindow.MaxWidth = double.PositiveInfinity;
                toolsWindow.Width = (entry.ToolCollapsed ? 80 : 44) + 2;
                toolsWindow.MinWidth = toolsWindow.MaxWidth = toolsWindow.Width;
                toolsWindow.Height = entry.ToolCollapsed ? 610 : 850;
                toolsWindow.Content = ToolChrome(entry); AddFloatingResize((Control)toolsWindow.Content, toolsWindow);
            }
            else entry.Slot!.Child = ToolChrome(entry);
            return;
        }
        if (entry.ToolWindow is { } window)
        {
            if (entry.ToolCollapsed) { entry.ToolWidth = window.Width; entry.ToolHeight = window.Height; window.MinHeight = 52; window.Width = 120; window.Height = 56; }
            else { window.Width = entry.ToolWidth; window.Height = entry.ToolHeight; }
            window.Content = ToolChrome(entry); AddFloatingResize((Control)window.Content, window);
        }
        else entry.Slot!.Child = ToolChrome(entry);
    }
    private void ResetContextBar()
    {
        _contextPinned = false; var entry = _dockPanels["Contextual Task Bar"]; if (entry.ToolCollapsed) ToggleToolCollapse(entry); FloatTool(entry, null);
        var point = _canvas.PointToScreen(new Point(Math.Max(0, (_canvas.Bounds.Width - 470) / 2), Math.Max(0, _canvas.Bounds.Height - 70)));
        entry.ToolWindow!.Position = point;
    }
}
