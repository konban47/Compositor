using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
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
        var eye = new Button
        {
            Content = new LayerEye(layer.IsVisible), Width = 28, Height = 28,
            Padding = new Thickness(3), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Focusable = false, Tag = "visibility",
        };
        ToolTip.SetTip(eye, Localize.Text(layer.IsVisible ? "Hide Layer" : "Show Layer"));
        Avalonia.Automation.AutomationProperties.SetName(eye, Localize.Text(layer.IsVisible ? "Hide Layer" : "Show Layer"));
        eye.Click += (_, _) =>
        {
            if (_document is not { } document) return;
            Edit("Layer Visibility", () => LayerEdits.SetVisible(document, layer.ID, !layer.IsVisible));
            ShowLayers(document);
        };
        var panel = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*") };
        var name = new TextBlock
        {
            Text = layer.Name + notes, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4 + depth * 14, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(name, 1);
        panel.Children.Add(eye); panel.Children.Add(name);
        return panel;
    }

    private void WireLayerDrag(ListBoxItem row)
    {
        row.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed || e.GetPosition(row).X < 32) return;
            if (row.Tag is not Guid id) return;
            SelectLayerRow(id, e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift));
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
