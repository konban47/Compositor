using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private sealed class DockGroup(string id, TabControl tabs, int column, double order)
    {
        public string ID { get; } = id;
        public TabControl Tabs { get; } = tabs;
        public Border Frame { get; } = new() { Background = Skin.ChromeBrush, BorderBrush = Skin.BorderSubtleBrush, BorderThickness = new Thickness(1) };
        public Border Preview { get; set; } = new() { IsVisible = false, IsHitTestVisible = false };
        public Window? Window { get; set; }
        public int Column { get; set; } = column;
        public double Order { get; set; } = order;
        public double Weight { get; set; } = 5;
        public bool Collapsed { get; set; }
        public bool ColumnCollapsed { get; set; }
        public double ColumnWeight { get; set; } = 1;
        public double ExpandedWidth { get; set; } = 340;
        public double ExpandedHeight { get; set; } = 510;
    }
    private sealed class DockPanelEntry(string name, Control body, TabControl? home, TabItem? tab, Border? slot)
    {
        public string Name { get; } = name;
        public Control Body { get; } = body;
        public TabControl? HomeHost { get; } = home;
        public TabControl? Host => Group?.Tabs ?? HomeHost;
        public DockGroup? Group { get; set; }
        public string? LastDockGroup { get; set; }
        public TabItem? Tab { get; } = tab;
        public Border? Slot { get; } = slot;
        public Window? ToolWindow { get; set; }
        public bool ToolCollapsed { get; set; }
        public bool ToolDockRight { get; set; }
        public double ToolWidth { get; set; } = 470;
        public double ToolHeight { get; set; } = 120;
        public Window? Floating => Group?.Window ?? ToolWindow;
        public bool Visible { get; set; } = true;
    }
    private readonly Dictionary<string, DockPanelEntry> _dockPanels = [];
    private readonly List<DockGroup> _dockGroups = [];
    private readonly Grid _dockRoot = new();
    private readonly Border _dockSidePreview = new() { IsVisible = false, IsHitTestVisible = false, BorderBrush = new SolidColorBrush(Color.Parse("#4A9FFF")), BorderThickness = new Thickness(2),
        BoxShadow = new BoxShadows(new BoxShadow { Blur = 9, Spread = 2, Color = Color.Parse("#B0358FFF") }), Tag = "dock-glow-preview" };
    private readonly Canvas _dockHintOverlay = new() { IsHitTestVisible = false };
    private readonly Dictionary<int, Control> _dockColumnFrames = [];
    private Grid? _dockEditorBody;
    private bool _closingPanels, _draggingDock, _dockCollapsed, _restoringDock, _contextPinned;
    private DockGroup? _dragOriginGroup;
    private Flyout? _dockPeek;
    private Action? _cancelDockDrag;
    private double _dockExpandedWidth = 340;
    private Border? _toolDockSlot, _optionDockSlot;

    private void InitializePanelDocking()
    {
        MakeDockGroup("inspector", _inspectorTabs, 0, 0);
        MakeDockGroup("layers", _panelTabs, 0, 1);
        foreach (var (host, names) in new[] { (_inspectorTabs, new[] { "Properties", "History" }), (_panelTabs, new[] { "Layers", "Channels" }) })
            for (var i = 0; i < names.Length; i++) RegisterPanel(names[i], host, (TabItem)host.Items[i]!);
        RenderDockArea();
        Opened += (_, _) => { if (EditorPreferences.Current.ActiveWorkspace != "Essentials") ApplyWorkspace(EditorPreferences.Current.ActiveWorkspace); };
        Closed += (_, _) => { _closingPanels = true; _dockPeek?.Hide(); foreach (var group in _dockGroups.ToArray()) group.Window?.Close(); foreach (var panel in _dockPanels.Values) panel.ToolWindow?.Close(); };
    }
    private DockGroup MakeDockGroup(string id, TabControl? tabs = null, int column = -1, double order = 0)
    {
        var group = new DockGroup(id, tabs ?? new TabControl(), column, order); group.Tabs.MinHeight = 0;
        _dockGroups.Add(group);
        group.Tabs.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.Source, group.Tabs)) return;
            if (Members(group).Any(p => p.Name == "Channels" && ReferenceEquals(group.Tabs.SelectedItem, p.Tab))) RefreshChannels();
        };
        BuildGroupChrome(group); return group;
    }
    private IEnumerable<DockPanelEntry> Members(DockGroup group) => group.Tabs.Items.OfType<TabItem>().Select(t => _dockPanels.Values.FirstOrDefault(p => ReferenceEquals(p.Tab, t))).OfType<DockPanelEntry>();
    private DockGroup HomeGroup(DockPanelEntry entry) => _dockGroups.First(g => ReferenceEquals(g.Tabs, entry.HomeHost));
    private void RegisterPanel(string name, TabControl host, TabItem tab)
    {
        if (_dockPanels.ContainsKey(name) || tab.Content is not Control body) return;
        var group = _dockGroups.First(g => ReferenceEquals(g.Tabs, host));
        var entry = new DockPanelEntry(name, body, host, tab, null) { Group = group, LastDockGroup = group.ID }; _dockPanels[name] = entry;
        tab.FontSize = 12; tab.Padding = new Thickness(4, 2); tab.MinHeight = 26; tab.Header = PanelHeader(entry);
        RenderDockArea();
    }
    private static void Detach(Control control)
    {
        if (control.Parent is Panel panel) panel.Children.Remove(control);
        else if (control.Parent is Decorator decorator) decorator.Child = null;
        else if (control.Parent is ContentControl content) content.Content = null;
    }
    private Button DockButton(string text, string hint, Action action, string? tag = null)
    {
        var button = new Button { Content = text, FontSize = 13, Width = 22, Height = 21, MinWidth = 0, MinHeight = 0, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, Tag = tag };
        ToolTip.SetTip(button, Localize.Text(hint)); button.Click += (_, e) => { action(); e.Handled = true; }; return button;
    }
    private Button PanelIcon(DockPanelEntry entry, Action action, string? tag = null)
    {
        var button = DockButton("", entry.Name, action, tag); button.Width = 30; button.Height = 28;
        button.Content = new DockPanelGlyph(entry.Name) { Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false }; return button;
    }
    private Button DockTitleButton(string text, string hint, Action action, string? tag = null)
    {
        var button = DockButton(text, hint, action, tag); button.Height = 11; button.Content = new ChromeGlyph(text) { Width = 22, Height = 10, VerticalAlignment = VerticalAlignment.Center }; return button;
    }
    private Control PanelHeader(DockPanelEntry entry)
    {
        var header = new DockPanel { Background = Brushes.Transparent, MinHeight = 24 };
        var close = DockButton("×", "Close Panel", () => HidePanel(entry), "close-panel-" + entry.Name); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        var title = new TextBlock { Text = Localize.Text(entry.Name), FontSize = 12, Foreground = Skin.LabelBrush, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 5, 0), Background = Brushes.Transparent, Tag = "panel-handle-" + entry.Name };
        header.Children.Add(title);
        title.AddHandler(PointerPressedEvent, (_, e) => StartDockDrag(entry, null, title, e), RoutingStrategies.Tunnel);
        var menu = new ContextMenu(); menu.Items.Add(Command("Float Panel", () => FloatPanel(entry)));
        menu.Items.Add(Command("Close Panel", () => HidePanel(entry))); header.ContextMenu = menu; return header;
    }
    private void BuildGroupChrome(DockGroup group)
    {
        Detach(group.Tabs);
        var root = new Grid(); var content = new DockPanel();
        var grip = new DockPanel { Height = 11, Background = Skin.SurfaceDarkBrush, Tag = "group-handle-" + group.ID };
        var close = DockTitleButton("×", "Close Tab Group", () => { foreach (var entry in Members(group).ToArray()) HidePanel(entry); }); DockPanel.SetDock(close, Dock.Right); grip.Children.Add(close);
        var collapse = DockTitleButton(group.Collapsed ? "»" : "«", "Collapse or Expand Panels", () => ToggleGroupCollapse(group), "collapse-group-" + group.ID); DockPanel.SetDock(collapse, Dock.Right); grip.Children.Add(collapse);
        var title = new TextBlock { Text = group.Window is null ? "⋮⋮" : string.Join(" · ", Members(group).Where(p => p.Visible).Select(p => Localize.Text(p.Name))),
            Tag = "group-title-" + group.ID, FontSize = 9, Foreground = Skin.SecondaryBrush, Margin = new Thickness(6, 0), Background = Brushes.Transparent, TextTrimming = TextTrimming.CharacterEllipsis };
        grip.Children.Add(title); title.AddHandler(PointerPressedEvent, (_, e) => StartDockDrag(null, group, title, e), RoutingStrategies.Tunnel);
        var menu = new ContextMenu(); menu.Items.Add(Command("Float Tab Group", () => FloatGroup(group))); menu.Items.Add(Command("Collapse or Expand Panels", () => ToggleGroupCollapse(group)));
        menu.Items.Add(Command("Close Tab Group", () => { foreach (var entry in Members(group).ToArray()) HidePanel(entry); })); grip.ContextMenu = menu;
        DockPanel.SetDock(grip, Dock.Top); content.Children.Add(grip);
        if (group.Collapsed)
        {
            var icons = new StackPanel { Orientation = group.Window is null ? Orientation.Horizontal : Orientation.Vertical, Spacing = 2 };
            foreach (var entry in Members(group).Where(p => p.Visible))
            {
                var icon = PanelIcon(entry, () => { ToggleGroupCollapse(group); SelectPanel(entry); }); icons.Children.Add(icon);
            }
            content.Children.Add(new ScrollViewer { Content = icons, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = group.Window is null ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto });
        }
        else content.Children.Add(group.Tabs);
        root.Children.Add(content);
        group.Preview = new Border { IsHitTestVisible = false, IsVisible = false, BorderBrush = Skin.AccentBrush, BorderThickness = new Thickness(2), Background = new SolidColorBrush(Color.FromArgb(28, 20, 115, 230)), Tag = "dock-preview-" + group.ID };
        root.Children.Add(group.Preview); group.Frame.Child = root;
        if (group.Window is { } window) { window.Title = title.Text ?? Localize.Text("Panels"); AddFloatingResize(group.Frame, window); }
    }
    private static void AddFloatingResize(Control frame, Window window)
    {
        if (ReferenceEquals(frame.Tag, window)) return; frame.Tag = window;
        frame.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!ReferenceEquals(TopLevel.GetTopLevel(frame), window)) return;
            var p = e.GetPosition(frame); const double edge = 4;
            if (!e.GetCurrentPoint(frame).Properties.IsLeftButtonPressed) return;
            var left = p.X < edge; var right = p.X > frame.Bounds.Width - edge; var top = p.Y < edge; var bottom = p.Y > frame.Bounds.Height - edge;
            WindowEdge? side = (left, right, top, bottom) switch { (true, _, true, _) => WindowEdge.NorthWest, (_, true, true, _) => WindowEdge.NorthEast,
                (true, _, _, true) => WindowEdge.SouthWest, (_, true, _, true) => WindowEdge.SouthEast, (true, _, _, _) => WindowEdge.West,
                (_, true, _, _) => WindowEdge.East, (_, _, true, _) => WindowEdge.North, (_, _, _, true) => WindowEdge.South, _ => null };
            if (side is { } resize) { window.BeginResizeDrag(resize, e); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
    }
    private void ToggleGroupCollapse(DockGroup group)
    {
        if (group.Window is null && group.Column >= 0) { SetColumnCollapsed(group.Column, !group.ColumnCollapsed); return; }
        group.Collapsed = !group.Collapsed;
        if (group.Window is { } window)
        {
            if (group.Collapsed) { group.ExpandedHeight = window.Height; group.ExpandedWidth = window.Width; window.Width = 110; window.MinHeight = 46; window.Height = CollapsedGroupHeight(group); }
            else { window.MinHeight = 100; window.Height = group.ExpandedHeight; window.Width = group.ExpandedWidth; }
        }
        BuildGroupChrome(group); RenderDockArea();
    }
    private double CollapsedGroupHeight(DockGroup group) => Math.Clamp(23 + Members(group).Count(p => p.Visible) * 30, 53, 800);
    private void SelectPanel(DockPanelEntry entry)
    {
        if (entry.Tab is not null) entry.Host!.SelectedItem = entry.Tab;
        if (entry.Name == "Channels") RefreshChannels();
    }
    private void PeekDockPanel(DockPanelEntry entry, Control anchor)
    {
        _dockPeek?.Hide(); var group = entry.Group!; SelectPanel(entry); Detach(group.Tabs);
        var peek = new Flyout { Placement = PlacementMode.LeftEdgeAlignedTop, Content = new Border { Width = 340, Height = 480, Child = group.Tabs, Background = Skin.ChromeBrush } };
        _dockPeek = peek; peek.Closed += (_, _) => { Detach(group.Tabs); BuildGroupChrome(group); if (ReferenceEquals(_dockPeek, peek)) _dockPeek = null; }; peek.ShowAt(anchor);
    }
    private void MovePanel(DockPanelEntry entry, DockGroup group, int index = -1)
    {
        if (entry.Tab is null) return;
        var previous = entry.Group; previous?.Tabs.Items.Remove(entry.Tab);
        entry.Group = group; entry.Tab.IsVisible = entry.Visible; entry.Tab.Content = entry.Body;
        group.Tabs.Items.Insert(index < 0 ? group.Tabs.Items.Count : Math.Clamp(index, 0, group.Tabs.Items.Count), entry.Tab);
        if (group.Column >= 0 && group.Window is null) entry.LastDockGroup = group.ID;
        SelectPanel(entry); SelectVisiblePanel(previous?.Tabs);
        if (previous is not null && !ReferenceEquals(previous, group) && (previous.Window is not null || previous.Collapsed)) BuildGroupChrome(previous);
    }
    private Window NewFloatingWindow(double width, double height, PixelPoint position) => new()
    {
        Width = width, Height = height, MinWidth = 90, MinHeight = 100, Background = Skin.ChromeBrush, ShowInTaskbar = false, ShowActivated = false,
        WindowDecorations = WindowDecorations.None, WindowStartupLocation = WindowStartupLocation.Manual, Position = position
    };
    private void FloatGroup(DockGroup group, PixelPoint? position = null)
    {
        if (EditorPreferences.Current.LockWorkspace || group.Window is not null) { group.Window?.Activate(); return; }
        foreach (var entry in Members(group)) if (group.Column >= 0) entry.LastDockGroup = group.ID;
        Detach(group.Frame); group.Column = -1;
        var window = NewFloatingWindow(Math.Max(260, group.Frame.Bounds.Width), group.Collapsed ? 80 : Math.Max(250, group.Frame.Bounds.Height), position ?? new(Position.X + 80, Position.Y + 100));
        if (group.Collapsed) { window.MinHeight = 46; window.Width = 110; window.Height = CollapsedGroupHeight(group); }
        group.Window = window; BuildGroupChrome(group); window.Content = group.Frame;
        window.Closed += (_, _) =>
        {
            if (!ReferenceEquals(group.Window, window)) return;
            window.Content = null; group.Window = null;
            if (_closingPanels) return;
            foreach (var entry in Members(group)) { entry.Visible = false; entry.Tab!.IsVisible = false; }
            RenderDockArea();
        };
        window.Show(this); RenderDockArea();
    }
    private void FloatPanel(DockPanelEntry entry, PixelPoint? at = null)
    {
        if (EditorPreferences.Current.LockWorkspace || entry.Name == "Contextual Task Bar" && _contextPinned) return;
        if (entry.Slot is not null) { FloatTool(entry, at); return; }
        entry.Visible = true; entry.Tab!.IsVisible = true;
        var group = entry.Group!;
        if (group.Window is not null && Members(group).Count(p => p.Visible) == 1) { group.Window.Activate(); return; }
        if (Members(group).Count() > 1) { var separate = MakeDockGroup(Guid.NewGuid().ToString("N")); MovePanel(entry, separate); group = separate; }
        FloatGroup(group, at);
        if (entry.Name == "Timeline" && group.Window is { } timelineWindow) { timelineWindow.Width = 1160; timelineWindow.Height = 540; }
        if (entry.Name is "Character" or "Paragraph" && group.Window is { } typeWindow) { typeWindow.Width = 350; typeWindow.Height = entry.Name == "Character" ? 480 : 450; }
        if (entry.Name == "Layer Comps" && group.Window is { } compWindow) { compWindow.Width = 400; compWindow.Height = 300; }
        if (entry.Name == "Brush Settings" && group.Window is { } brushWindow)
        { brushWindow.Width = 500; brushWindow.Height = 810; }
    }
    private static void SelectVisiblePanel(TabControl? host)
    { if (host?.SelectedItem is not TabItem { IsVisible: true } && host is not null) host.SelectedItem = host.Items.OfType<TabItem>().FirstOrDefault(t => t.IsVisible); }
    private void CloseEmptyGroups()
    {
        if (_draggingDock) return;
        foreach (var group in _dockGroups.Where(g => !Members(g).Any()).ToArray())
        {
            var window = group.Window; group.Window = null; if (window is not null) { window.Content = null; window.Close(); }
            Detach(group.Frame); if (group.ID is not ("inspector" or "layers")) _dockGroups.Remove(group);
        }
    }
    private void DockPanelBack(DockPanelEntry entry)
    {
        if (EditorPreferences.Current.LockWorkspace || entry.Name == "Contextual Task Bar" && _contextPinned) return;
        entry.Visible = true;
        if (entry.Slot is not null) { DockToolBack(entry); return; }
        var target = _dockGroups.FirstOrDefault(g => g.ID == entry.LastDockGroup && g.Window is null && g.Column >= 0) ?? HomeGroup(entry);
        if (target.Window is not null || target.Column < 0)
        {
            var window = target.Window; target.Window = null; if (window is not null) { window.Content = null; window.Close(); }
            target.Column = 0; target.Order = target.ID == "layers" ? 1 : 0; BuildGroupChrome(target);
        }
        MovePanel(entry, target); CloseEmptyGroups(); _layersSide.IsVisible = true; RenderDockArea();
    }
    private void HidePanel(DockPanelEntry entry)
    {
        if (entry.Name == "Timeline") _stopDocumentTimeline?.Invoke();
        entry.Visible = false;
        if (entry.Slot is not null) { entry.ToolWindow?.Close(); entry.Slot.IsVisible = false; return; }
        entry.Tab!.IsVisible = false; SelectVisiblePanel(entry.Host);
        if (entry.Group is { } group)
        {
            if (!Members(group).Any(p => p.Visible)) group.Window?.Close(); else if (group.Window is not null || group.Collapsed) BuildGroupChrome(group);
        }
        RenderDockArea();
    }
    private void ShowDockPanel(string name)
    {
        if (!_dockPanels.TryGetValue(name, out var entry)) { OpenUtilityPanel(name); return; }
        entry.Visible = true; entry.Body.IsVisible = true;
        if (entry.Slot is not null)
        {
            if (entry.ToolWindow is { } tool) { tool.Show(); tool.Activate(); }
            else { entry.Slot.IsVisible = true; if (entry.Slot.Child is null) entry.Slot.Child = ToolChrome(entry); }
            return;
        }
        entry.Tab!.IsVisible = true; SelectPanel(entry);
        if (entry.Group is { } group)
        {
            if (group.Collapsed) ToggleGroupCollapse(group);
            if (group.Column < 0 && group.Window is null) FloatGroup(group);
            if (group.Window is { } window) { BuildGroupChrome(group); window.Activate(); }
        }
        _layersSide.IsVisible = true; if (entry.Group is { Column: >= 0 } docked) SetColumnCollapsed(docked.Column, false); else RenderDockArea();
    }
    private void ResetDockPanels()
    {
        _dockPeek?.Hide(); var restoring = _restoringDock; _restoringDock = true; var locked = EditorPreferences.Current.LockWorkspace; EditorPreferences.Current.LockWorkspace = false; _contextPinned = false; _dockCollapsed = false; _dockExpandedWidth = 340;
        foreach (var group in _dockGroups) { var window = group.Window; group.Window = null; if (window is not null) { window.Content = null; window.Close(); } group.Collapsed = false; group.ColumnCollapsed = false; }
        foreach (var group in _dockGroups.Where(g => g.ID is "inspector" or "layers"))
        { group.Column = 0; group.Order = group.ID == "inspector" ? 0 : 1; group.Weight = 5; group.ColumnWeight = 1; }
        foreach (var entry in _dockPanels.Values)
        {
            if (entry.Tab is not null) { var home = HomeGroup(entry); MovePanel(entry, home); entry.LastDockGroup = home.ID; }
            else { entry.ToolCollapsed = false; entry.ToolDockRight = false; DockToolBack(entry); }
            entry.Visible = entry.Name is "Properties" or "History" or "Layers" or "Channels" or "Tools" or "Options";
            if (entry.Tab is not null) entry.Tab.IsVisible = entry.Visible; else entry.Slot!.IsVisible = entry.Visible;
        }
        CloseEmptyGroups(); foreach (var group in _dockGroups) BuildGroupChrome(group);
        _inspectorTabs.SelectedItem = _dockPanels["Properties"].Tab; _panelTabs.SelectedItem = _dockPanels["Layers"].Tab;
        EditorPreferences.Current.LockWorkspace = locked; RenderDockArea(); _restoringDock = restoring;
    }
}
