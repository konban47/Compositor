using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private readonly TabControl _panelTabs = new();
    private readonly TextBox _layerSearch = new() { PlaceholderText = Localize.Text("Find layers"), Width = 138 };
    private readonly ComboBox _layerKind = new() { Width = 115, SelectedIndex = 0 };
    private readonly NumericUpDown _fill = new() { Minimum = 0, Maximum = 100, Value = 100, Width = 70, ShowButtonSpinner = false, FormatString = "0'%'", Increment = 1 };
    private readonly Dictionary<LayerLocks, ToggleButton> _lockButtons = [];
    private readonly List<LayerThumbnail> _layerThumbnails = [];
    private readonly MenuItem _lockLayer = new();

    private Control BuildPanels()
    {
        _layersSide.Width = 340;
        var layers = new DockPanel { LastChildFill = true };
        var top = new StackPanel { Spacing = 6, Margin = new Thickness(8) };
        foreach (var name in new[] { "All kinds", "Pixels", "Adjustment", "Text", "Shape", "Groups", "Locked" })
            _layerKind.Items.Add(Localize.Text(name));
        _layerKind.SelectedIndex = 0;
        _layerSearch.TextChanged += (_, _) => { if (_document is { } doc) ShowLayers(doc); };
        _layerKind.SelectionChanged += (_, _) => { if (_document is { } doc) ShowLayers(doc); };
        top.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _layerKind, _layerSearch } });
        top.Children.Add(Appearance());
        var locks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        locks.Children.Add(new TextBlock { Text = Localize.Text("Lock:"), VerticalAlignment = VerticalAlignment.Center });
        foreach (var (flag, icon, label) in new[]
        {
            (LayerLocks.Transparency, "transparency", "Lock Transparent Pixels"),
            (LayerLocks.Pixels, "brush", "Lock Image Pixels"),
            (LayerLocks.Position, "move", "Lock Position"), (LayerLocks.All, "lock", "Lock Layer"),
        })
        {
            var button = new ToggleButton { Content = new PanelGlyph(icon), Width = 26, Height = 28, Padding = new Thickness(3) };
            ToolTip.SetTip(button, Localize.Text(label));
            Avalonia.Automation.AutomationProperties.SetName(button, Localize.Text(label));
            button.Click += (_, _) => { if (!_showingAppearance) ToggleLockFlag(flag); };
            _lockButtons[flag] = button; locks.Children.Add(button);
        }
        locks.Children.Add(new TextBlock { Text = Localize.Text("Fill:"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        _fill.ValueChanged += (_, _) =>
        {
            if (_showingAppearance || _document is not { } doc) return;
            Edit("Fill Opacity", () =>
            {
                foreach (var layer in doc.Layers.Where(layer => SelectedLayers.Contains(layer.ID))) layer.FillOpacity = (double)(_fill.Value ?? 100) / 100;
                return true;
            });
        };
        locks.Children.Add(_fill); top.Children.Add(locks);
        DockPanel.SetDock(top, Dock.Top); layers.Children.Add(top);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(8) };
        footer.Children.Add(PanelButton("link", "Link / Unlink Layers", LinkLayers));
        footer.Children.Add(PanelButton("fx", "Layer Effects", () => ShowEffectsMenu(footer)));
        footer.Children.Add(PanelButton("mask", "Add Layer Mask", () => AddMask(true)));
        footer.Children.Add(PanelButton("adjust", "New Adjustment Layer", () => ShowAdjustmentMenu(footer)));
        footer.Children.Add(PanelButton("folder", "New Group (Ctrl+G)", () => { if (SelectedLayers.Count > 0) GroupSelected(); else NewFolder(); }));
        footer.Children.Add(PanelButton("add", "New Layer (Ctrl+Shift+N)", NewBlankLayer));
        footer.Children.Add(PanelButton("delete", "Delete Layer or Selected Mask", DeletePanelTarget));
        DockPanel.SetDock(footer, Dock.Bottom); layers.Children.Add(footer); layers.Children.Add(_layers);
        _panelTabs.Items.Add(new TabItem { Header = Localize.Text("Layers"), FontSize = 13, Content = layers });
        _panelTabs.Items.Add(new TabItem { Header = Localize.Text("Channels"), FontSize = 13, Content = BuildChannelsPanel() });
        _panelTabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.Source, _panelTabs) && _panelTabs.SelectedIndex == 1) RefreshChannels(); };
        _panelTabs.SelectedIndex = 0;
        return BuildInspector(_panelTabs);
    }

    private static Button PanelButton(string icon, string label, Action action)
    {
        var button = new Button { Content = new PanelGlyph(icon), Width = 32, Height = 28, Padding = new Thickness(4), Background = Brushes.Transparent };
        ToolTip.SetTip(button, Localize.Text(label));
        Avalonia.Automation.AutomationProperties.SetName(button, Localize.Text(label));
        button.Click += (_, _) => action(); return button;
    }

    private void ShowEffectsMenu(Control owner)
    {
        var menu = new ContextMenu();
        foreach (var kind in Enum.GetValues<EffectKind>())
        {
            var value = kind; menu.Items.Add(Command(EffectDialog.TitleFor(kind) + "…", () => _ = EditEffect(value)));
        }
        menu.Items.Add(Command("Clear Effects", ClearEffects)); menu.Open(owner);
    }

    private void ShowAdjustmentMenu(Control owner)
    {
        var menu = new ContextMenu();
        foreach (var kind in Enum.GetValues<AdjustmentKind>())
        {
            var value = kind; menu.Items.Add(Command(LayerPlacement.Name(kind), () => _ = NewAdjustment(value)));
        }
        menu.Open(owner);
    }

    private bool MatchesLayerFilter(ImageLayer layer)
    {
        if (!string.IsNullOrWhiteSpace(_layerSearch.Text) && !layer.Name.Contains(_layerSearch.Text.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        return _layerKind.SelectedIndex switch
        {
            1 => !layer.IsGroup && layer.LiveText is null && layer.LiveShape is null && layer.Adjustment is null,
            2 => layer.Adjustment is not null, 3 => layer.LiveText is not null, 4 => layer.LiveShape is not null,
            5 => layer.IsGroup, 6 => layer.Locks != LayerLocks.None, _ => true,
        };
    }

    private void ToggleLayerLock() => ToggleLockFlag(LayerLocks.All);

    private void ToggleLockFlag(LayerLocks flag)
    {
        if (_document is not { } document) return;
        var selected = document.Layers.Where(layer => SelectedLayers.Contains(layer.ID)).ToList();
        if (selected.Count == 0) return;
        var remove = selected.All(layer => layer.Locks.HasFlag(flag));
        Edit("Layer Locks", () =>
        {
            foreach (var layer in selected) layer.Locks = remove ? layer.Locks & ~flag : layer.Locks | flag;
            return true;
        });
        ShowLayers(document);
    }

    private void LinkLayers()
    {
        if (_document is not { } document) return;
        var selected = document.Layers.Where(layer => SelectedLayers.Contains(layer.ID)).ToList();
        var existing = selected.Select(layer => layer.LinkID).Where(id => id is not null).ToHashSet();
        if (selected.Count < 2 && existing.Count == 0) return;
        Edit("Link Layers", () =>
        {
            if (existing.Count > 0)
            {
                foreach (var layer in document.Layers.Where(layer => existing.Contains(layer.LinkID))) layer.LinkID = null;
            }
            else { var link = Guid.NewGuid(); foreach (var layer in selected) layer.LinkID = link; }
            return true;
        });
        ShowLayers(document);
    }

    private void UngroupSelected()
    {
        if (_document is not { } doc) return;
        var groups = doc.Layers.Where(layer => SelectedLayers.Contains(layer.ID) && layer.IsGroup).ToList();
        Edit("Ungroup Layers", () =>
        {
            foreach (var group in groups)
            {
                foreach (var child in doc.Layers.Where(layer => layer.ParentID == group.ID)) child.ParentID = group.ParentID;
                doc.Layers.Remove(group);
            }
            return groups.Count > 0;
        });
        ShowLayers(doc);
    }

    private void WireLayerContext(ListBoxItem row, ImageLayer layer)
    {
        row.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(row).Properties.IsRightButtonPressed) return;
            if (!SelectedLayers.Contains(layer.ID)) SelectLayerRow(layer.ID);
        }, RoutingStrategies.Tunnel);
        var menu = new ContextMenu();
        menu.Opening += (_, _) =>
        {
            foreach (var item in menu.Items.OfType<MenuItem>()) _keyRows.RemoveAll(row => ReferenceEquals(row.Item, item));
            menu.Items.Clear();
            var current = _document?.Layers.FirstOrDefault(item => item.ID == layer.ID);
            menu.Items.Add(Command(current?.Locks.HasFlag(LayerLocks.All) == true ? "Unlock Layer" : "Lock Layer", ToggleLayerLock, "Lock Layer"));
            menu.Items.Add(Command("New Group", GroupSelected, "Group Layers"));
            menu.Items.Add(Command("Ungroup Layers", UngroupSelected, "Ungroup Layers"));
            menu.Items.Add(Command("Link / Unlink Layers", LinkLayers));
            menu.Items.Add(new Separator());
            menu.Items.Add(Command("Rename Layer…", () => _ = RenameLayer(), "Rename Layer"));
            menu.Items.Add(Command("Duplicate Layer", DuplicateLayer, "Duplicate Layer"));
            menu.Items.Add(Command("Delete Layer", DeleteLayer, "Delete Layer"));
            ShowKeys();
            _keyRows.RemoveAll(row => menu.Items.Contains(row.Item));
        };
        row.ContextMenu = menu;
    }

    private void ShowPanelState(ImageLayer? layer)
    {
        _showingAppearance = true;
        foreach (var (flag, button) in _lockButtons)
        {
            button.IsEnabled = layer is not null; button.IsChecked = layer?.Locks.HasFlag(flag) == true;
        }
        _fill.Value = (decimal)((layer?.FillOpacity ?? 1) * 100);
        _fill.IsEnabled = layer is not null && !layer.Locks.HasFlag(LayerLocks.All);
        _lockLayer.Header = Localize.Text(layer?.Locks.HasFlag(LayerLocks.All) == true ? "Unlock Layer" : "Lock Layer");
        _lockLayer.IsEnabled = layer is not null;
        _showingAppearance = false;
        foreach (var thumbnail in _layerThumbnails) thumbnail.InvalidateVisual();
        if (_panelTabs.SelectedIndex == 1) RefreshChannels();
    }
}

internal sealed class LayerThumbnail(Func<SKBitmap?> source, string kind = "") : Control
{
    public Func<bool>? Active { get; init; }
    public override void Render(DrawingContext context)
    {
        if (Active?.Invoke() == true) context.DrawRectangle(null, new Pen(Skin.AccentBrush, 2), new Rect(1, 1, Math.Max(0, Bounds.Width - 2), Math.Max(0, Bounds.Height - 2)));
        if (kind == "folder")
        {
            using (context.PushTransform(Matrix.CreateTranslation(4, 6)))
                context.DrawGeometry(Skin.SecondaryBrush, new Pen(Skin.LabelBrush, 1), StreamGeometry.Parse("M1,2 L10,2 L13,6 L25,6 L25,21 L1,21 Z"));
            return;
        }
        var box = new Rect(0, 0, Bounds.Width, Bounds.Height);
        context.DrawRectangle(Skin.Checker, new Pen(Skin.SecondaryBrush, 1), box);
        if (source() is { } pixels)
        {
            using var image = CanvasView.ToImage(pixels);
            var scale = Math.Min(Bounds.Width / pixels.Width, Bounds.Height / pixels.Height);
            context.DrawImage(image, new Rect((Bounds.Width - pixels.Width * scale) / 2,
                (Bounds.Height - pixels.Height * scale) / 2, pixels.Width * scale, pixels.Height * scale));
        }
        else if (kind.Length > 0)
        {
            var text = new FormattedText(kind, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 20, Skin.LabelBrush);
            context.DrawText(text, new Point((Bounds.Width - text.Width) / 2, (Bounds.Height - text.Height) / 2));
        }
        if (Active?.Invoke() == true) context.DrawRectangle(null, new Pen(Skin.AccentBrush, 2), new Rect(1, 1, Math.Max(0, Bounds.Width - 2), Math.Max(0, Bounds.Height - 2)));
    }
}

internal sealed class PanelGlyph(string kind) : Control
{
    protected override Size MeasureOverride(Size availableSize) => new(20, 20);
    public override void Render(DrawingContext context)
    {
        var path = kind switch
        {
            "selection" => "M1,1 L6,1 M10,1 L15,1 M19,1 L19,6 M19,10 L19,15 M19,19 L14,19 M10,19 L5,19 M1,19 L1,14 M1,10 L1,5",
            "lock" => "M5,9 L5,6 C5,0 15,0 15,6 L15,9 M3,9 L17,9 L17,19 L3,19 Z M10,12 L10,16",
            "folder" => "M1,5 L8,5 L10,8 L19,8 L19,18 L1,18 Z",
            "add" => "M2,2 L18,2 L18,18 L2,18 Z M10,5 L10,15 M5,10 L15,10",
            "delete" => "M4,6 L16,6 L15,19 L5,19 Z M2,3 L18,3 M7,1 L13,1 M8,9 L8,16 M12,9 L12,16",
            "mask" => "M1,3 L19,3 L19,17 L1,17 Z M14,10 A4,4 0 1 1 6,10 A4,4 0 1 1 14,10",
            "move" => "M10,1 L10,19 M1,10 L19,10 M7,4 L10,1 L13,4 M7,16 L10,19 L13,16 M4,7 L1,10 L4,13 M16,7 L19,10 L16,13",
            "link" => "M8,5 L5,5 C0,5 0,15 5,15 L8,15 M12,5 L15,5 C20,5 20,15 15,15 L12,15 M6,10 L14,10",
            "transparency" => "M2,2 L18,2 L18,18 L2,18 Z M2,7 L18,7 M2,13 L18,13 M7,2 L7,18 M13,2 L13,18",
            "brush" => "M7,14 L15,2 L19,5 L10,16 Z M7,13 Q1,12 2,19 Q9,20 10,16",
            "adjust" => "M10,1 A9,9 0 1 1 10,19 A9,9 0 1 1 10,1 M10,1 L10,19",
            _ => "M4,19 L8,2 L15,2 M2,8 L12,8 M12,10 L19,18 M19,10 L12,18",
        };
        context.DrawGeometry(null, new Pen(Skin.LabelBrush, 1.5), StreamGeometry.Parse(path));
    }
}
