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
    private readonly TextBox _layerSearch = new()
    {
        PlaceholderText = Localize.Text("Find layers"),
        Width = 145,
        Height = 24,
        FontSize = 11,
        CornerRadius = new CornerRadius(2),
        Background = Skin.SurfaceDarkBrush,
        BorderBrush = Skin.BorderControlBrush,
        Foreground = Skin.LabelBrush,
        VerticalContentAlignment = VerticalAlignment.Center,
    };
    private readonly ComboBox _layerKind = new()
    {
        Width = 115,
        Height = 24,
        FontSize = 11,
        CornerRadius = new CornerRadius(2),
        Background = Skin.SurfaceControlBrush,
        BorderBrush = Skin.BorderControlBrush,
        SelectedIndex = 0,
        VerticalContentAlignment = VerticalAlignment.Center,
    };
    private readonly NumericUpDown _fill = new()
    {
        Minimum = 0,
        Maximum = 100,
        Value = 100,
        Width = 70,
        Height = 24,
        FontSize = 11,
        CornerRadius = new CornerRadius(2),
        Background = Skin.SurfaceDarkBrush,
        BorderBrush = Skin.BorderControlBrush,
        Foreground = Skin.LabelBrush,
        ShowButtonSpinner = false,
        FormatString = "0'%'",
        Increment = 1,
        VerticalContentAlignment = VerticalAlignment.Center,
    };
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
        locks.Children.Add(new TextBlock { Text = Localize.Text("Lock:"), FontSize = 11, Foreground = Skin.SecondaryBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        foreach (var (flag, icon, label) in new[]
        {
            (LayerLocks.Transparency, "transparency", "Lock Transparent Pixels"),
            (LayerLocks.Pixels, "brush", "Lock Image Pixels"),
            (LayerLocks.Position, "move", "Lock Position"), (LayerLocks.All, "lock", "Lock Layer"),
        })
        {
            var button = new ToggleButton
            {
                Content = new PanelGlyph(icon),
                Width = 26,
                Height = 24,
                Padding = new Thickness(2),
                CornerRadius = new CornerRadius(2),
                Background = Brushes.Transparent,
                BorderBrush = Skin.BorderControlBrush,
            };
            ToolTip.SetTip(button, Localize.Text(label));
            Avalonia.Automation.AutomationProperties.SetName(button, Localize.Text(label));
            button.Click += (_, _) => { if (!_showingAppearance) ToggleLockFlag(flag); };
            _lockButtons[flag] = button; locks.Children.Add(button);
        }
        locks.Children.Add(new TextBlock { Text = Localize.Text("Fill:"), FontSize = 11, Foreground = Skin.SecondaryBrush, Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
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
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(8, 4, 8, 6) };
        footer.Children.Add(PanelButton("link", "Link / Unlink Layers", LinkLayers));
        footer.Children.Add(PanelButton("fx", "Layer Effects", () => ShowEffectsMenu(footer)));
        footer.Children.Add(PanelButton("mask", "Add Layer Mask", () => AddMask(true)));
        footer.Children.Add(PanelButton("adjust", "New Adjustment Layer", () => ShowAdjustmentMenu(footer)));
        footer.Children.Add(PanelButton("folder", "New Group (Ctrl+G)", () => { if (SelectedLayers.Count > 0) GroupSelected(); else NewFolder(); }));
        footer.Children.Add(PanelButton("add", "New Layer (Ctrl+Shift+N)", NewBlankLayer));
        footer.Children.Add(PanelButton("delete", "Delete Layer or Selected Mask", DeletePanelTarget));
        DockPanel.SetDock(footer, Dock.Bottom); layers.Children.Add(footer); layers.Children.Add(_layers);
        _panelTabs.Items.Add(new TabItem { Header = Localize.Text("Layers"), FontSize = 11.5, FontWeight = FontWeight.SemiBold, Content = layers });
        _panelTabs.Items.Add(new TabItem { Header = Localize.Text("Channels"), FontSize = 11.5, FontWeight = FontWeight.SemiBold, Content = BuildChannelsPanel() });
        _panelTabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.Source, _panelTabs) && _panelTabs.SelectedIndex == 1) RefreshChannels(); };
        _panelTabs.SelectedIndex = 0;
        return BuildInspector(_panelTabs);
    }

    private static Button PanelButton(string icon, string label, Action action)
    {
        var button = new Button
        {
            Content = new PanelGlyph(icon),
            Width = 32,
            Height = 26,
            Padding = new Thickness(2),
            CornerRadius = new CornerRadius(2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
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
            "selection" => "M10,2 A8,8 0 0 1 18,10 M18,10 A8,8 0 0 1 10,18 M10,18 A8,8 0 0 1 2,10 M2,10 A8,8 0 0 1 10,2",
            "lock" => "M6,8 L6,5 C6,1.5 14,1.5 14,5 L14,8 M4,8 L16,8 L16,18 L4,18 Z M10,11 L10,14",
            "folder" => "M2,4 L7,4 L9,7 L18,7 L18,17 L2,17 Z",
            "add" => "M4,2 L16,2 L16,12 L12,16 L4,16 Z M12,12 L16,12 L12,16 Z",
            "delete" => "M8,2 L12,2 M3,4 L17,4 M5,6 L15,6 L14,18 L6,18 Z M8,8 L8,15 M12,8 L12,15",
            "mask" => "M2,3 L18,3 L18,17 L2,17 Z M14,10 A4,4 0 1 1 6,10 A4,4 0 1 1 14,10",
            "move" => "M10,2 L10,18 M2,10 L18,10 M7,5 L10,2 L13,5 M7,15 L10,18 L13,15 M5,7 L2,10 L5,13 M15,7 L18,10 L15,13",
            "link" => "M8,5 L6,5 C2.5,5 2.5,15 6,15 L8,15 M12,5 L14,5 C17.5,5 17.5,15 14,15 L12,15 M6,10 L14,10",
            "transparency" => "M3,3 L17,3 L17,17 L3,17 Z M3,7 L17,7 M3,11 L17,11 M3,15 L17,15 M7,3 L7,17 M11,3 L11,17 M15,3 L15,17",
            "brush" => "M13,3 L17,7 L10,14 L7,14 L7,11 Z M7,14 C4,14 3,16 3,17 C5,17 7,16 7,14",
            "adjust" => "M10,2 A8,8 0 1 1 10,18 A8,8 0 1 1 10,2 Z M10,2 L10,18",
            "fx" => "M7,4 C5,4 4,5.5 4,8 L4,16 M2,9 L7,9 M10,8 L16,16 M16,8 L10,16",
            _ => "M7,4 C5,4 4,5.5 4,8 L4,16 M2,9 L7,9 M10,8 L16,16 M16,8 L10,16",
        };
        var pen = new Pen(Skin.LabelBrush, 1.3) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (kind == "selection")
            pen = new Pen(Skin.LabelBrush, 1.2) { DashStyle = new DashStyle([2.5, 2], 0) };
        context.DrawGeometry(null, pen, StreamGeometry.Parse(path));
        if (kind == "adjust")
        {
            // Fill the left half of the adjustment layer icon (classic Photoshop half-filled circle)
            var halfCircle = StreamGeometry.Parse("M10,2 A8,8 0 0 0 10,18 Z");
            context.DrawGeometry(Skin.LabelBrush, null, halfCircle);
        }
    }
}
