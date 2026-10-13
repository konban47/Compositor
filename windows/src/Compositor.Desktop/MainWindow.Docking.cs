using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private sealed class DockPanelEntry(string name, Control body, TabControl? host, TabItem? tab, Border? slot)
    {
        public string Name { get; } = name;
        public Control Body { get; } = body;
        public TabControl? Host { get; } = host;
        public TabItem? Tab { get; } = tab;
        public Border? Slot { get; } = slot;
        public Window? Floating { get; set; }
        public bool Visible { get; set; } = true;
    }
    private readonly Dictionary<string, DockPanelEntry> _dockPanels = [];
    private bool _movingDock, _closingPanels;
    private Border? _toolDockSlot, _optionDockSlot;
    private void InitializePanelDocking()
    {
        foreach (var (host, names) in new[] { (_inspectorTabs, new[] { "Properties", "History" }), (_panelTabs, new[] { "Layers", "Channels" }) })
            for (var i = 0; i < names.Length; i++) RegisterPanel(names[i], host, (TabItem)host.Items[i]!);
        Opened += (_, _) => { if (EditorPreferences.Current.ActiveWorkspace != "Essentials") ApplyWorkspace(EditorPreferences.Current.ActiveWorkspace); };
        Closed += (_, _) => { _closingPanels = true; foreach (var panel in _dockPanels.Values) panel.Floating?.Close(); };
    }
    private void RegisterPanel(string name, TabControl host, TabItem tab)
    {
        if (_dockPanels.ContainsKey(name) || tab.Content is not Control body) return;
        var entry = new DockPanelEntry(name, body, host, tab, null); _dockPanels[name] = entry;
        tab.FontSize = 12; tab.Padding = new Thickness(4, 2); tab.MinHeight = 26; tab.Header = PanelHeader(entry, false);
    }
    private Border DockTool(string name, Control body)
    {
        var slot = new Border(); var entry = new DockPanelEntry(name, body, null, null, slot); _dockPanels[name] = entry;
        slot.Child = PanelWithHeader(entry); return slot;
    }
    private Control PanelWithHeader(DockPanelEntry entry)
    {
        var dock = new DockPanel { Background = Skin.ChromeBrush };
        var header = PanelHeader(entry, entry.Floating is not null); DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header); dock.Children.Add(entry.Body); return dock;
    }
    private Control PanelHeader(DockPanelEntry entry, bool floating)
    {
        var header = new DockPanel { LastChildFill = true, Background = Brushes.Transparent, MinHeight = 22 };
        var close = new Button { Content = "×", FontSize = 14, Width = 18, Height = 20, MinHeight = 0, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, Tag = "close-panel-" + entry.Name };
        ToolTip.SetTip(close, Localize.Text("Close Panel")); close.Click += (_, _) => HidePanel(entry); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        if (floating)
        {
            var dock = new Button { Content = "↙", FontSize = 13, Width = 22, Height = 20, MinHeight = 0, Padding = new Thickness(0), Tag = "dock-panel-" + entry.Name };
            ToolTip.SetTip(dock, Localize.Text("Dock Panel")); dock.Click += (_, _) => DockPanelBack(entry); DockPanel.SetDock(dock, Dock.Right); header.Children.Add(dock);
        }
        var title = new TextBlock { Text = Localize.Text(entry.Name), FontSize = 12, Foreground = Skin.LabelBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3,0,6,0), Tag = "panel-handle-" + entry.Name };
        header.Children.Add(title);
        title.Background = Brushes.Transparent;
        title.AddHandler(InputElement.PointerPressedEvent, (_, e) => StartPanelDrag(entry, title, e), RoutingStrategies.Tunnel);
        var menu = new ContextMenu(); menu.Items.Add(Command("Float Panel", () => FloatPanel(entry))); menu.Items.Add(Command("Dock Panel", () => DockPanelBack(entry))); menu.Items.Add(Command("Close Panel", () => HidePanel(entry))); header.ContextMenu = menu;
        return header;
    }
    private void StartPanelDrag(DockPanelEntry entry, Control title, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(title).Properties.IsLeftButtonPressed || EditorPreferences.Current.LockWorkspace || TopLevel.GetTopLevel(title) is not Window surface) return;
        if (e.ClickCount == 2) { if (entry.Floating is null) FloatPanel(entry); else DockPanelBack(entry); e.Handled = true; return; }
        var origin = surface.PointToScreen(e.GetPosition(surface)); var windowOrigin = entry.Floating?.Position ?? default;
        void Move(object? sender, PointerEventArgs args)
        {
            var at = surface.PointToScreen(args.GetPosition(surface));
            if (entry.Floating is null && Math.Abs(at.X-origin.X)+Math.Abs(at.Y-origin.Y) > 12)
            { FloatPanel(entry, at); if (entry.Floating is { } created) windowOrigin = created.Position; origin = at; }
            else if (entry.Floating is { } window) window.Position = new(windowOrigin.X+at.X-origin.X, windowOrigin.Y+at.Y-origin.Y);
            args.Handled = true;
        }
        void Cleanup()
        { surface.RemoveHandler(InputElement.PointerMovedEvent, Move); surface.RemoveHandler(InputElement.PointerReleasedEvent, Release); surface.PointerCaptureLost -= Lost; }
        void Lost(object? sender, PointerCaptureLostEventArgs args) => Cleanup();
        void Release(object? sender, PointerReleasedEventArgs args)
        {
            var at = surface.PointToScreen(args.GetPosition(surface)); Cleanup(); args.Pointer.Capture(null);
            var target = entry.Slot ?? (Control?)entry.Host;
            var bounds = target is null ? default : new Rect(target.PointToScreen(default).ToPoint(1), target.PointToScreen(new Point(target.Bounds.Width, target.Bounds.Height)).ToPoint(1));
            if (entry.Slot is not null && (bounds.Width < 4 || bounds.Height < 4))
            {
                var corner = this.PointToScreen(default).ToPoint(1); var scale = RenderScaling;
                bounds = entry.Name == "Tools" ? new Rect(corner.X, corner.Y + 100 * scale, 80 * scale, Math.Max(0, Bounds.Height - 100) * scale)
                    : new Rect(corner.X, corner.Y + (entry.Name == "Options" ? 60 : Math.Max(0, Bounds.Height - 90)) * scale, Bounds.Width * scale, 70 * scale);
            }
            if (entry.Floating is not null && target is not null && bounds.Contains(at.ToPoint(1))) DockPanelBack(entry);
            args.Handled = true;
        }
        surface.AddHandler(InputElement.PointerMovedEvent, Move, RoutingStrategies.Tunnel, true);
        surface.AddHandler(InputElement.PointerReleasedEvent, Release, RoutingStrategies.Tunnel, true);
        surface.PointerCaptureLost += Lost; e.Pointer.Capture(surface); e.Handled = true;
    }
    private void DetachPanelBody(DockPanelEntry entry)
    {
        if (entry.Tab is not null) { entry.Tab.Content = null; entry.Tab.IsVisible = false; }
        if (entry.Slot is not null) { if (entry.Slot.Child is DockPanel parent) parent.Children.Clear(); entry.Slot.Child = null; }
        if (entry.Floating?.Content is DockPanel dock) dock.Children.Clear();
    }
    private void FloatPanel(DockPanelEntry entry, PixelPoint? at = null)
    {
        if (EditorPreferences.Current.LockWorkspace || entry.Floating is not null) { entry.Floating?.Activate(); return; }
        DetachPanelBody(entry); entry.Visible = true;
        var window = new Window { Title = Localize.Text(entry.Name), Width = entry.Name == "Tools" ? 96 : entry.Name == "Options" ? 760 : 340,
            Height = entry.Name == "Options" ? 130 : 510, MinWidth = entry.Name == "Tools" ? 76 : 220, MinHeight = 100, Background = Skin.ChromeBrush, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Position = at ?? new PixelPoint(Position.X+80, Position.Y+100) };
        entry.Floating = window; window.Content = PanelWithHeader(entry);
        window.Closed += (_, _) => { if (_movingDock || _closingPanels) return; DetachPanelBody(entry); entry.Floating = null; entry.Visible = false; if (entry.Tab is not null) entry.Tab.Content = entry.Body; SelectVisiblePanel(entry.Host); };
        window.Show(this); SelectVisiblePanel(entry.Host);
    }
    private static void SelectVisiblePanel(TabControl? host)
    {
        if (host is null) return;
        if (host.SelectedItem is not TabItem { IsVisible: true }) host.SelectedItem = host.Items.OfType<TabItem>().FirstOrDefault(t => t.IsVisible);
    }
    private void DockPanelBack(DockPanelEntry entry)
    {
        if (EditorPreferences.Current.LockWorkspace) return;
        var window = entry.Floating; DetachPanelBody(entry); entry.Floating = null; _movingDock = true; window?.Close(); _movingDock = false;
        entry.Visible = true; entry.Body.IsVisible = true;
        if (entry.Tab is not null) { entry.Tab.Content = entry.Body; entry.Tab.IsVisible = true; entry.Host!.SelectedItem = entry.Tab; }
        if (entry.Slot is not null) { entry.Slot.IsVisible = true; entry.Slot.Child = PanelWithHeader(entry); }
    }
    private void HidePanel(DockPanelEntry entry)
    {
        entry.Visible = false;
        if (entry.Floating is { } window) window.Close();
        if (entry.Tab is not null) entry.Tab.IsVisible = false;
        if (entry.Slot is not null) entry.Slot.IsVisible = false;
        SelectVisiblePanel(entry.Host);
    }
    private void ShowDockPanel(string name)
    {
        if (!_dockPanels.TryGetValue(name, out var entry)) { OpenUtilityPanel(name); return; }
        entry.Visible = true; entry.Body.IsVisible = true;
        if (entry.Floating is { } window) { window.Show(); window.Activate(); return; }
        if (entry.Tab is not null) { entry.Tab.IsVisible = true; entry.Host!.SelectedItem = entry.Tab; _layersSide.IsVisible = true; }
        if (entry.Slot is not null) entry.Slot.IsVisible = true;
    }
    private void ResetDockPanels()
    {
        var wasLocked = EditorPreferences.Current.LockWorkspace; EditorPreferences.Current.LockWorkspace = false;
        foreach (var entry in _dockPanels.Values) { DockPanelBack(entry); if (entry.Name is not ("Properties" or "History" or "Layers" or "Channels" or "Tools" or "Options" or "Contextual Task Bar")) HidePanel(entry); }
        EditorPreferences.Current.LockWorkspace = wasLocked; _inspectorTabs.SelectedIndex = _panelTabs.SelectedIndex = 0;
    }
}
