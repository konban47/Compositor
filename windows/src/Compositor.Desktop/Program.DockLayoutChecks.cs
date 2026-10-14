using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private void DockLayoutSelfCheck(string output)
    {
        static void Check(bool condition, string why) { if (!condition) throw new InvalidOperationException(why); }
        void Pump() { Dispatcher.UIThread.RunJobs(); UpdateLayout(); foreach (var w in OwnedWindows) w.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        TextBlock Handle(string name) => ((Control)_dockPanels[name].Tab!.Header!).GetVisualDescendants().OfType<TextBlock>().Single(t => Equals(t.Tag, "panel-handle-" + name));
        void Tap(string name)
        {
            Pump(); var entry = _dockPanels[name]; var window = (Window)TopLevel.GetTopLevel(Handle(name))!; var point = Handle(name).TranslatePoint(new Point(7, 7), window) ?? throw new InvalidOperationException("Tab header detached: " + name);
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
            Check(ReferenceEquals(entry.Host!.SelectedItem, entry.Tab) && entry.Body.IsEffectivelyVisible, "Tab click failed: " + name);
        }
        void Drag(Control handle, PixelPoint target, bool expectHint)
        {
            Pump(); var source = (Window)TopLevel.GetTopLevel(handle)!; var down = handle.TranslatePoint(new Point(7, 7), source)!.Value;
            source.MouseDown(down, MouseButton.Left); source.MouseMove(source.PointToClient(target), RawInputModifiers.LeftMouseButton); Pump();
            if (expectHint) Check(_dockDrop is not null && (_dockDrop.Group.Preview.IsVisible || _dockSidePreview.IsVisible), "No docking preview at " + target + ": " + handle.Tag + "; drop=" + _dockDrop?.Placement);
            source.MouseUp(source.PointToClient(target), MouseButton.Left); Pump();
            Check(_dockDrop is null && !_draggingDock, "Drag did not clean up preview/capture state.");
        }
        ResetDockPanels(); Pump();
        foreach (var name in new[] { "Channels", "Layers", "History", "Properties", "Channels", "Layers" }) Tap(name);
        var channel = _dockPanels["Channels"]; var jitter = Handle("Channels").TranslatePoint(new Point(7,7), this)!.Value;
        this.MouseDown(jitter, MouseButton.Left); this.MouseMove(jitter + new Vector(1,1), RawInputModifiers.LeftMouseButton); this.MouseUp(jitter + new Vector(1,1), MouseButton.Left); Pump();
        Check(channel.Floating is null && ReferenceEquals(channel.Host!.SelectedItem, channel.Tab), "A click with pointer jitter detached the panel.");
        OpenTypePanel("Character"); Pump(); var character = _dockPanels["Character"]; FloatPanel(character); Pump();
        var layers = _dockPanels["Layers"].Group!; var target = layers.Frame.PointToScreen(new Point(130, 36));
        Drag(Handle("Character"), target, true);
        Check(character.Floating is null && ReferenceEquals(character.Group, layers) && layers.Tabs.Items.Contains(character.Tab), "Cross-group merge lost the panel.");
        Tap("Channels"); Tap("Character"); Tap("Layers");
        var first = ((Control)_dockPanels["Layers"].Tab!.Header!).PointToScreen(new Point(2, 8));
        Drag(Handle("Character"), first, true);
        Check(ReferenceEquals(character.Group, layers) && layers.Tabs.Items.IndexOf(character.Tab) < layers.Tabs.Items.IndexOf(_dockPanels["Layers"].Tab), "Tab insertion order was not applied.");
        ShowDockPanel("Properties"); Pump(); var properties = _dockPanels["Properties"];
        Drag(Handle("Properties"), layers.Frame.PointToScreen(new Point(100, layers.Frame.Bounds.Height - 5)), true);
        Check(!ReferenceEquals(properties.Group, layers) && properties.Group!.Column == layers.Column && properties.Group.Order > layers.Order, "Bottom edge did not make a new stacked group.");
        OpenTypePanel("Paragraph"); Pump(); var paragraph = _dockPanels["Paragraph"];
        Drag(Handle("Paragraph"), layers.Frame.PointToScreen(new Point(3, 70)), true);
        Check(paragraph.Group!.Column < layers.Column, "Left edge did not make a new column.");
        Save("grouped-dock");
        Tap("Channels");
        var columnWidth = channel.Group!.Frame.Bounds.Width; var rowHeight = channel.Group.Frame.Bounds.Height;
        var layout = CaptureDockWorkspace(); EditorPreferences.Current.Workspaces["Video Dock Test"] = layout; EditorPreferences.Current.Save();
        var loaded = EditorPreferences.Load().Workspaces["Video Dock Test"]; RestoreDockWorkspace(loaded); Pump();
        Check(ReferenceEquals(_dockPanels["Character"].Group, channel.Group) && channel.Host!.SelectedItem == channel.Tab, "Workspace lost tab grouping or active selection.");
        Check(Math.Abs(channel.Group!.Frame.Bounds.Width - columnWidth) < 3 && Math.Abs(channel.Group.Frame.Bounds.Height - rowHeight) < 3, "Workspace lost column widths or group heights.");
        Check(paragraph.Group!.Column < channel.Group!.Column && properties.Group!.Order > channel.Group.Order, "Workspace lost split layout.");
        var rightWidth = channel.Group!.Frame.Bounds.Width;
        SetColumnCollapsed(paragraph.Group!.Column, true); Pump();
        Check(paragraph.Group.ColumnCollapsed && !channel.Group.ColumnCollapsed && Math.Abs(channel.Group.Frame.Bounds.Width - rightWidth) < 1, "Collapsing left column changed right column.");
        var oneCollapsed = CaptureDockWorkspace(); RestoreDockWorkspace(oneCollapsed); Pump();
        Check(paragraph.Group.ColumnCollapsed && !channel.Group.ColumnCollapsed, "Workspace lost independent column states."); Save("left-column-collapsed");
        SetColumnCollapsed(paragraph.Group.Column, false); Pump(); var leftWidth = paragraph.Group.Frame.Bounds.Width;
        SetColumnCollapsed(channel.Group.Column, true); Pump();
        Check(!paragraph.Group.ColumnCollapsed && channel.Group.ColumnCollapsed && Math.Abs(paragraph.Group.Frame.Bounds.Width - leftWidth) < 1, "Collapsing right column changed left column."); Save("right-column-collapsed");
        SetDockCollapsed(true); Pump(); Check(_layersSide.Width == 81, "Dock did not collapse to an icon strip.");
        var icon = _dockRoot.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "panel-icon-Channels"));
        icon.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Pump(); Check(_dockPeek is not null, "Collapsed icon did not open its panel.");
        _dockPeek!.Hide(); Pump();
        EditorPreferences.Current.LockWorkspace = true;
        var iconAt = icon.TranslatePoint(new Point(15,14), this)!.Value; this.MouseDown(iconAt, MouseButton.Left); this.MouseUp(iconAt, MouseButton.Left); Pump();
        Check(_dockPeek is not null, "Locked workspace blocked opening an icon panel."); _dockPeek!.Hide(); EditorPreferences.Current.LockWorkspace = false; Pump(); Save("two-icon-columns"); SetDockCollapsed(false); Pump(); Tap("Channels");
        FloatGroup(channel.Group!); Pump(); Check(character.Floating is not null && ReferenceEquals(character.Floating, channel.Floating), "Floating a group split its tabs.");
        ToggleGroupCollapse(channel.Group!); Pump(); Check(channel.Group!.Collapsed && channel.Floating!.Height < 180, "Floating group did not collapse.");
        ToggleGroupCollapse(channel.Group); Pump(); Tap("Character"); Tap("Channels"); Save("floating-group", channel.Floating!);
        var floatingLayout = CaptureDockWorkspace(); RestoreDockWorkspace(floatingLayout); Pump();
        Check(channel.Floating is not null && ReferenceEquals(channel.Floating, character.Floating), "Workspace did not restore a grouped floating window.");
        var closeWindow = channel.Floating!; closeWindow.Close(); Pump(); Check(!channel.Visible && !character.Visible, "Closing the group did not hide its tabs.");
        ShowDockPanel("Channels"); ShowDockPanel("Character"); Pump(); Tap("Channels");
        HidePanel(_dockPanels["Layers"]); Pump();
        var groupHandle = channel.Group!.Frame.GetVisualDescendants().OfType<TextBlock>().Single(t => Equals(t.Tag, "group-title-" + channel.Group.ID));
        Drag(groupHandle, _dockPanels["History"].Group!.Frame.PointToScreen(new Point(100, 37)), true);
        Check(ReferenceEquals(channel.Group, _dockPanels["History"].Group) && ReferenceEquals(channel.Group, _dockPanels["Layers"].Group) && !_dockPanels["Layers"].Visible, "Whole-group docking lost hidden members or changed their visibility.");
        SetDockCollapsed(true); FloatPanel(properties); Pump();
        Drag(Handle("Properties"), _layersSide.PointToScreen(new Point(16, 100)), true);
        Check(properties.Floating is null && !_dockCollapsed, "Collapsed sidebar did not accept docking.");
        ResetDockPanels(); Pump();
        var tools = _dockPanels["Tools"];
        void DragTools(bool right)
        {
            FloatPanel(tools, new PixelPoint(Position.X + 200, Position.Y + 150)); Pump();
            var buttons = tools.Floating!.GetVisualDescendants().OfType<DockPanel>().First(p => p.Height == 11).Children.OfType<Button>().ToArray();
            Check(buttons.Length == 2 && buttons.All(b => Equals(b.Tag, "collapse-tool-Tools") || Equals(b.Tag, "close-panel-Tools")), "Tools floating header must have just collapse and close.");
            var source = tools.Floating!; var handle = source.GetVisualDescendants().OfType<Border>().Single(b => Equals(b.Tag, "tool-drag-grip")); var down = handle.TranslatePoint(new Point(12,2), source)!.Value;
            var point = _dockEditorBody!.PointToScreen(new Point(right ? _dockEditorBody!.Bounds.Width - 3 : 3, 140));
            source.MouseDown(down, MouseButton.Left); source.MouseMove(source.PointToClient(point), RawInputModifiers.LeftMouseButton); Pump();
            Check(_dockSidePreview.IsVisible && _dockSidePreview.Height > _dockEditorBody!.Bounds.Height - 2 && _dockSidePreview.BoxShadow.Count > 0 && _dockSidePreview.BoxShadow[0].Blur >= 6, "Tool edge preview/glow missing.");
            using (var hintBitmap = new RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height)))
            {
                hintBitmap.Render(this); using var encoded = new MemoryStream(); hintBitmap.Save(encoded, new PngBitmapEncoderOptions()); encoded.Position = 0;
                using var pixels = SkiaSharp.SKBitmap.Decode(encoded);
                var x = Math.Clamp((int)(_dockSidePreview.Bounds.X + 1), 0, pixels.Width - 1); var y = (int)_dockSidePreview.Bounds.Center.Y;
                var line = pixels.GetPixel(x, y); var halo = pixels.GetPixel(Math.Clamp(x + (right ? -5 : 5), 0, pixels.Width - 1), y);
                Check(line.Blue > line.Red + 60 && halo.Blue > halo.Red + 8 && halo.Blue < line.Blue, "Dock line or soft blue halo was not rendered.");
            }
            Save(right ? "tools-right-hint" : "tools-left-hint");
            source.MouseUp(source.PointToClient(point), MouseButton.Left); Pump();
            Check(tools.Floating is null && tools.ToolDockRight == right && Grid.GetColumn(tools.Slot!) == (right ? 2 : 0), "Toolbar docked on wrong side.");
            var saved = CaptureDockWorkspace(); RestoreDockWorkspace(saved); Pump();
            Check(tools.ToolDockRight == right && Grid.GetColumn(tools.Slot!) == (right ? 2 : 0), "Workspace lost toolbar side.");
        }
        DragTools(true); DragTools(false);
        Check(FindToolDrop(tools, _dockEditorBody!.PointToScreen(new Point(3, -100))) is null, "Tool dock target leaked above the editing area.");
        var before = _dockGroups.Count; var handle = Handle("History"); var at = handle.TranslatePoint(new Point(7,7), this)!.Value;
        this.MouseDown(at, MouseButton.Left); this.MouseMove(at + new Vector(-250,100), RawInputModifiers.LeftMouseButton); Pump();
        this.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); this.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump();
        Check(_dockPanels["History"].Floating is null && _dockGroups.Count == before, "Escape did not restore the pre-drag layout.");
        var context = _dockPanels["Contextual Task Bar"]; ResetContextBar(); Pump(); Check(context.Floating is { Height: < 100, Width: > 400 }, "Contextual bar was not a compact floating toolbar.");
        var fixedPosition = context.Floating!.Position; _contextPinned = true;
        var barHandle = context.Floating.GetVisualDescendants().OfType<TextBlock>().Single(t => Equals(t.Tag, "panel-handle-Contextual Task Bar"));
        Drag(barHandle, new(fixedPosition.X + 200, fixedPosition.Y - 100), false); Check(context.Floating.Position == fixedPosition, "Pinned contextual bar moved.");
        _contextPinned = false; DockToolBack(context); EditorPreferences.Current.Workspaces.Remove("Video Dock Test"); ResetDockPanels(); Pump();
        void Save(string suffix, Window? window = null) { Pump(); var view = window ?? this; using var frame = new RenderTargetBitmap(new PixelSize((int)view.Bounds.Width, (int)view.Bounds.Height)); frame.Render(view); frame.Save(Path.ChangeExtension(output, suffix + ".png"), new PngBitmapEncoderOptions()); }
    }
}
