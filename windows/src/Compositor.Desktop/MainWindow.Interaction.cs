using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private bool _importingDrop;
    private Guid? _dragLayer;
    private Point _layerPress;
    private bool _layerDragging;
    private ListBoxItem? _dropRow;
    private bool _dropAbove;

    private void InitializeInteraction()
    {
        _canvas.MoveTargetPressed = SelectMoveTarget;
        DragDrop.SetAllowDrop(_canvas, true);
        _canvas.AddHandler(DragDrop.DragOverEvent, CanvasDragOver);
        _canvas.AddHandler(DragDrop.DropEvent, CanvasDrop);
    }

    private static void NameNewLayer(CanvasDocument document, Guid? id, string prefix)
    {
        if (!Localize.IsChinese || document.Layers.FirstOrDefault(layer => layer.ID == id) is not { } made) return;
        var translated = Localize.Text(prefix);
        var names = document.Layers.Where(layer => layer.ID != id).Select(layer => layer.Name).ToHashSet();
        var name = translated;
        for (var index = 2; names.Contains(name); index++) name = translated + " " + index;
        made.Name = name;
    }

    private static string[] DroppedPaths(DragEventArgs e) => e.DataTransfer.TryGetFiles()?
        .Select(file => file.TryGetLocalPath()).OfType<string>().Where(CanDropPath).ToArray() ?? [];

    private static bool CanDropPath(string path) => ImageImporter.LooksImportable(path)
        || path.EndsWith(".psd", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".psb", StringComparison.OrdinalIgnoreCase)
        || Directory.Exists(path) && File.Exists(Path.Combine(path, "manifest.json"));

    private void CanvasDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = !_importingDrop && DroppedPaths(e).Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void CanvasDrop(object? sender, DragEventArgs e)
    {
        var paths = DroppedPaths(e);
        e.Handled = true;
        e.DragEffects = paths.Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        await ImportDroppedPaths(paths);
    }

    internal async Task ImportDroppedPaths(IEnumerable<string> paths)
    {
        if (_importingDrop) return;
        _importingDrop = true;
        CommitText();
        var target = _open;
        try
        {
            foreach (var path in paths.Where(CanDropPath))
            {
                // Keep a multi-file drop with its originating tab even if the user switches tabs while decoding.
                await OpenPath(path, newTab: false, importTarget: target);
                if (target.Document is null) target = _open;
            }
        }
        catch (Exception error) { Say($"Could not import that image: {error.Message}"); }
        finally { _importingDrop = false; }
    }

    private void SelectMoveTarget(SKPoint point, KeyModifiers modifiers)
    {
        if (_document is not { } document) return;
        if (MaskTarget && PropertyLayer?.Mask?.IsLinked == false && PropertyLayer.MaskTransform.Contains(point)) return;
        // Handles retain the current transform. Interior clicks pick the top visible pixel, including overlapping layers.
        if (_canvas.TransformBox is { } box && _transformShown &&
            TransformEdits.HandleAt(box, point, TransformEdits.Grab / _canvas.Zoom,
                TransformEdits.RotateGrip / _canvas.Zoom) is not null) return;
        var selectedMembers = TransformEdits.GroupMembers(document, SelectedLayers).Select(layer => layer.ID).ToHashSet();
        var layers = document.Layers.ToDictionary(layer => layer.ID);
        foreach (var entry in document.HierarchyEntries(topFirst: true))
        {
            var layer = layers[entry.Layer.ID];
            if (!entry.Visible || layer.IsGroup || layer.Opacity <= 0 || layer.Asset is not { } asset
                || layer.Transform.InBox(point) is not { } at) continue;
            var x = Math.Clamp((int)(at.X / layer.Transform.Width * asset.Width), 0, asset.Width - 1);
            var y = Math.Clamp((int)(at.Y / layer.Transform.Height * asset.Height), 0, asset.Height - 1);
            if (asset.Image.GetPixel(x, y).Alpha == 0) continue;
            if (selectedMembers.Contains(layer.ID)) return;
            SelectLayerRow(layer.ID, modifiers.HasFlag(KeyModifiers.Shift));
            return;
        }
    }

    private void SelectLayerRow(Guid id, bool extend = false)
    {
        var row = _layers.Items.OfType<ListBoxItem>().FirstOrDefault(item => Equals(item.Tag, id));
        if (row is null) return;
        if (!extend) _layers.SelectedItems?.Clear();
        if (extend) { if (_layers.SelectedItems?.Contains(row) != true) _layers.SelectedItems?.Add(row); }
        else _layers.SelectedItem = row;
        _layers.ScrollIntoView(row);
        ShowTransformBox();
        _canvas.InvalidateVisual();
    }

    private Control LayerRow(ImageLayer layer, int depth, string notes)
    {
        ImageLayer? Current() => _document?.Layers.FirstOrDefault(item => item.ID == layer.ID);
        var eye = new Button { Content = new LayerEye(layer.IsVisible) { Width = 20, Height = 20 }, Width = 28, Height = 32,
            Padding = new Thickness(3), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Focusable = false, Tag = "visibility" };
        ToolTip.SetTip(eye, Localize.Text(layer.IsVisible ? "Hide Layer" : "Show Layer"));
        Avalonia.Automation.AutomationProperties.SetName(eye, Localize.Text(layer.IsVisible ? "Hide Layer" : "Show Layer"));
        eye.Click += (_, _) =>
        {
            if (_document is not { } document || Current() is not { } current) return;
            Edit("Layer Visibility", () => LayerEdits.SetVisible(document, current.ID, !current.IsVisible));
            ShowLayers(document);
        };
        var panel = new Grid { ColumnDefinitions = new ColumnDefinitions("28,Auto,Auto,Auto,Auto,*,24"), MinHeight = 42 };
        panel.Children.Add(eye);
        var arrow = new Button { Content = layer.IsGroup ? (_open.Collapsed.Contains(layer.ID) ? "▸" : "▾") : "",
            Width = 18, Height = 28, Margin = new Thickness(depth * 10, 0, 0, 0), Padding = new Thickness(0),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), IsVisible = layer.IsGroup, Focusable = false };
        ToolTip.SetTip(arrow, Localize.Text("Expand / Collapse Group"));
        arrow.Click += (_, _) =>
        {
            if (!_open.Collapsed.Add(layer.ID)) _open.Collapsed.Remove(layer.ID);
            if (_document is { } doc) { ShowLayers(doc); SelectLayerRow(layer.ID); }
        };
        Grid.SetColumn(arrow, 1); panel.Children.Add(arrow);
        var thumbnail = new LayerThumbnail(() => Current()?.LiveText is not null ? null : Current()?.Asset?.Thumbnail,
            layer.IsGroup ? "folder" : layer.LiveText is not null ? "T" : layer.Adjustment is not null ? "◐" : "")
            { Width = 32, Height = 32, Margin = new Thickness(layer.IsGroup ? 0 : depth * 12, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, Active = () => Selected == layer.ID && !MaskTarget };
        _layerThumbnails.Add(thumbnail);
        thumbnail.PointerPressed += (_, e) =>
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            { SelectLayerRow(layer.ID); SelectLayerPixels(); e.Handled = true; }
            else { SelectLayerRow(layer.ID); SetPaintingMask(false); e.Handled = true; }
        };
        Grid.SetColumn(thumbnail, 2); panel.Children.Add(thumbnail);
        if (layer.Mask is not null)
        {
            var link = PanelButton("link", layer.Mask.IsLinked ? "Unlink Layer Mask" : "Link Layer Mask", () => { SelectLayerRow(layer.ID); ToggleMaskLink(); });
            link.Width = 21; link.Padding = new Thickness(1); link.Tag = "mask-link";
            if (!layer.Mask.IsLinked) link.Content = null;
            Grid.SetColumn(link, 3); panel.Children.Add(link);
            var mask = new LayerThumbnail(() => Current()?.Mask?.Asset.Thumbnail) { Width = 30, Height = 30, Margin = new Thickness(3), Active = () => Selected == layer.ID && MaskTarget };
            _layerThumbnails.Add(mask); ToolTip.SetTip(mask, Localize.Text("Edit Layer Mask"));
            mask.PointerPressed += (_, e) => { SelectLayerRow(layer.ID); SetPaintingMask(true); e.Handled = true; };
            Grid.SetColumn(mask, 4); panel.Children.Add(mask);
        }
        var name = new TextBlock { Text = layer.Name + (layer.LinkID is not null ? "  ↔" : ""), Foreground = Ink,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 2, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(name, layer.Name + notes); Grid.SetColumn(name, 5); panel.Children.Add(name);
        if (layer.Locks != LayerLocks.None || _document is { } doc && LayerProtection.Effective(doc, layer.ID) != LayerLocks.None)
        {
            var unlock = PanelButton("lock", "Unlock Layer", () =>
            {
                if (_document is not { } current) return;
                Edit("Layer Locks", () => LayerProtection.Set(current, [layer.ID], LayerLocks.None));
                ShowLayers(current);
            });
            unlock.Width = 24; unlock.IsEnabled = layer.Locks != LayerLocks.None;
            Grid.SetColumn(unlock, 6); panel.Children.Add(unlock);
        }
        return panel;
    }

    private void WireLayerDrag(ListBoxItem row)
    {
        row.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed || e.GetPosition(row).X < 32) return;
            if (e.Source is Visual source && (source is Button || source.GetVisualAncestors().TakeWhile(item => item != row).Any(item => item is Button || item is LayerThumbnail))) return;
            if (row.Tag is not Guid id) return;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && _layers.SelectedItems?.Contains(row) == true)
                _layers.SelectedItems.Remove(row);
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _layers.SelectedIndex >= 0)
            {
                var from = _layers.SelectedIndex; var to = _layers.Items.IndexOf(row);
                for (var index = Math.Min(from, to); index <= Math.Max(from, to); index++)
                    if (_layers.SelectedItems?.Contains(_layers.Items[index]) != true) _layers.SelectedItems?.Add(_layers.Items[index]);
            }
            else if (!SelectedLayers.Contains(id))
                SelectLayerRow(id, e.KeyModifiers.HasFlag(KeyModifiers.Control));
            if (_document is { } doc && !LayerProtection.CanMove(doc, id)) { e.Handled = true; return; }
            _dragLayer = id; _layerPress = e.GetPosition(_layers); _layerDragging = false;
            e.Pointer.Capture(row); e.Handled = true;
        }, RoutingStrategies.Tunnel);
        row.PointerMoved += (_, e) =>
        {
            if (_dragLayer is null || !e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
            var at = e.GetPosition(_layers);
            if (!_layerDragging && Math.Abs(at.Y - _layerPress.Y) < 5) return;
            _layerDragging = true;
            ClearDropIndicator();
            foreach (var candidate in _layers.Items.OfType<ListBoxItem>())
            {
                var top = candidate.TranslatePoint(default, _layers);
                if (top is null || at.Y < top.Value.Y || at.Y > top.Value.Y + candidate.Bounds.Height) continue;
                _dropRow = candidate; _dropAbove = at.Y < top.Value.Y + candidate.Bounds.Height / 2;
                candidate.BorderBrush = Skin.LabelBrush;
                candidate.BorderThickness = _dropAbove ? new Thickness(0, 2, 0, 0) : new Thickness(0, 0, 0, 2);
                break;
            }
            if (_dropRow is { } target && (at.Y < 30 || at.Y > _layers.Bounds.Height - 30))
            {
                var index = _layers.Items.IndexOf(target) + (at.Y < 30 ? -1 : 1);
                if (index >= 0 && index < _layers.ItemCount) _layers.ScrollIntoView(_layers.Items[index]!);
            }
            e.Handled = true;
        };
        row.PointerReleased += (_, e) =>
        {
            var from = _dragLayer; var to = _dropRow?.Tag as Guid?; var above = _dropAbove;
            var moved = _layerDragging;
            _dragLayer = null; _layerDragging = false; ClearDropIndicator(); e.Pointer.Capture(null);
            if (moved && from is { } source && to is { } target && _document is { } document)
            {
                Edit("Reorder Layers", () => LayerEdits.MoveRelative(document, source, target, above));
                Reselect(source);
            }
        };
        row.PointerCaptureLost += (_, _) => { _dragLayer = null; _layerDragging = false; ClearDropIndicator(); };
    }

    private void ClearDropIndicator()
    {
        if (_dropRow is { } row) row.BorderThickness = new Thickness(0);
        _dropRow = null;
    }
}

internal sealed class LayerEye(bool visible) : Control
{
    public override void Render(DrawingContext context)
    {
        var pen = new Pen(Skin.LabelBrush, 1.5);
        var shape = StreamGeometry.Parse(visible ? "M1,10 Q10,0 19,10 Q10,20 1,10 Z" : "M1,8 Q10,19 19,8 M3,11 L1,15 M10,14 L10,18 M17,11 L19,15");
        context.DrawGeometry(null, pen, shape);
        if (visible) context.DrawEllipse(Skin.LabelBrush, null, new Point(10, 10), 2.5, 2.5);
    }
}
