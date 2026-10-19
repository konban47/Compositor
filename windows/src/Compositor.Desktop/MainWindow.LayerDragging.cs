using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.Model;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private readonly LayerDragOverlay _layerDragOverlay = new() { IsHitTestVisible = false, IsVisible = false };
    private DispatcherTimer? _layerDragTimer;
    private ScrollViewer? _layerDragScroll;
    private ListBoxItem? _layerDragSource;
    private Guid[] _layerDragIds = [];
    private Point _layerDragAt;
    private long _layerPressTime;
    private IPointer? _layerPointer;

    private void WireLayerDrag(ListBoxItem row)
    {
        row.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed || e.GetPosition(row).X < 32 || row.Tag is not Guid id) return;
            if (e.Source is Visual source && (source is Button || source.GetVisualAncestors().TakeWhile(item => item != row).Any(item => item is Button))) return;
            var thumbnail = (e.Source as Visual)?.GetSelfAndVisualAncestors().TakeWhile(item => item != row).OfType<LayerThumbnail>().FirstOrDefault();
            if (Equals(thumbnail?.Tag, "mask-thumbnail")) return;
            // Ctrl-click on a thumbnail keeps its existing load-transparency behavior.
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Source is Visual thumb && (thumb is LayerThumbnail || thumb.GetVisualAncestors().TakeWhile(item => item != row).Any(item => item is LayerThumbnail))) return;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && _layers.SelectedItems?.Contains(row) == true) { _layers.SelectedItems.Remove(row); e.Handled = true; return; }
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _layers.SelectedIndex >= 0)
            {
                var from = _layers.SelectedIndex; var to = _layers.Items.IndexOf(row);
                for (var index = Math.Min(from, to); index <= Math.Max(from, to); index++) if (_layers.SelectedItems?.Contains(_layers.Items[index]) != true) _layers.SelectedItems?.Add(_layers.Items[index]);
            }
            else if (!SelectedLayers.Contains(id)) SelectLayerRow(id, e.KeyModifiers.HasFlag(KeyModifiers.Control));
            if (thumbnail is not null && MaskTarget) SetPaintingMask(false);
            if (_document is not { } doc) return;
            _layerDragIds = DragLayerRoots(doc, SelectedLayers).ToArray();
            if (_layerDragIds.Length == 0 || _layerDragIds.Any(root => doc.Descendants(root).Append(root).Any(member => !LayerProtection.CanMove(doc, member)))) { e.Handled = true; return; }
            _dragLayer = id; _layerDragSource = row; _layerPress = _layerDragAt = e.GetPosition(_layers); _layerDragging = false;
            _layerPressTime = Environment.TickCount64; _layerPointer = e.Pointer;
            _layerDragScroll = _layers.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            _layerDragTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => LayerDragTick());
            _layerDragTimer.Start(); row.Focus(); e.Pointer.Capture(row); e.Handled = true;
        }, RoutingStrategies.Tunnel);
        row.PointerMoved += (_, e) =>
        {
            if (_dragLayer is null) return;
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) { EndLayerDrag(); return; }
            _layerDragAt = e.GetPosition(_layers);
            if (!_layerDragging && Math.Abs(_layerDragAt.X - _layerPress.X) + Math.Abs(_layerDragAt.Y - _layerPress.Y) >= 5) BeginLayerPreview();
            if (_layerDragging) UpdateLayerDrop(); e.Handled = true;
        };
        row.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (_dragLayer is null) return;
            var from = _dragLayer; var target = _dropRow?.Tag as Guid?; var above = _dropAbove; var ids = _layerDragIds; var moved = _layerDragging;
            var before = _layers.GetRealizedContainers().OfType<ListBoxItem>().Where(r => r.Tag is Guid).ToDictionary(r => (Guid)r.Tag!, r => r.TranslatePoint(default, _layers)?.Y ?? 0);
            EndLayerDrag();
            if (moved && DropLayersOnTabs(e.GetPosition(this))) { e.Handled = true; return; }
            if (moved && from is { } sourceID && target is { } to && _document is { } doc)
            {
                var changed = false;
                Edit("Reorder Layers", () =>
                {
                    if (!CanDropLayerRoots(doc, ids, to)) return false;
                    foreach (var id in above ? ids.Reverse() : ids) changed |= ReorderWithArtboardPosition(doc, id, to, above);
                    return changed;
                });
                if (changed)
                {
                    _layers.SelectedItems?.Clear(); foreach (var item in _layers.Items.OfType<ListBoxItem>().Where(r => r.Tag is Guid id && ids.Contains(id))) _layers.SelectedItems?.Add(item);
                    _layers.UpdateLayout();
                    foreach (var item in _layers.GetRealizedContainers().OfType<ListBoxItem>()) if (item.Tag is Guid id && before.TryGetValue(id, out var oldY) && item.TranslatePoint(default, _layers) is { } at)
                    {
                        var transform = new TranslateTransform(0, Math.Clamp(oldY - at.Y, -100, 100)); item.RenderTransform = transform;
                        transform.Transitions = new Transitions { new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(150) } }; transform.Y = 0;
                    }
                }
            }
            e.Handled = moved;
        }, RoutingStrategies.Tunnel);
        row.KeyDown += (_, e) => { if (e.Key == Key.Escape && _dragLayer is not null) { EndLayerDrag(); e.Handled = true; } };
        row.PointerCaptureLost += (_, _) => { if (ReferenceEquals(row, _layerDragSource)) EndLayerDrag(); };
    }

    private static IEnumerable<Guid> DragLayerRoots(CanvasDocument doc, IReadOnlyCollection<Guid> ids)
    {
        var descendants = ids.SelectMany(doc.Descendants).ToHashSet();
        return doc.Layers.Where(l => ids.Contains(l.ID) && !descendants.Contains(l.ID)).Select(l => l.ID);
    }
    private static bool CanDropLayerRoots(CanvasDocument doc, IReadOnlyCollection<Guid> ids, Guid target)
    {
        if (ids.Count == 0 || ids.Contains(target) || ids.Any(id => doc.Descendants(id).Contains(target))) return false;
        var destination = doc.Layers.FirstOrDefault(l => l.ID == target);
        return destination is not null && (destination.ParentID is not { } parent || LayerProtection.CanMove(doc, parent))
            && ids.All(id => doc.Descendants(id).Append(id).All(member => LayerProtection.CanMove(doc, member)));
    }
    private void BeginLayerPreview()
    {
        if (_document is null || _layerDragSource is null) return;
        _layerDragging = true; _moveHoverBox = null; _canvas.InvalidateOverlay();
        _layerDragOverlay.Label = _layerDragIds.Length > 1 ? Localize.Format($"{_layerDragIds.Length} layers") : _document.Layers.First(l => l.ID == _dragLayer).Name;
        _layerDragOverlay.Pointer = _layerDragAt; _layerDragOverlay.IsVisible = true;
        _layerDragSource.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(100) } }; _layerDragSource.Opacity = .4;
        _layers.Cursor = new Cursor(StandardCursorType.SizeAll); UpdateLayerDrop();
    }
    private void LayerDragTick()
    {
        if (_dragLayer is null) { _layerDragTimer?.Stop(); return; }
        if (!_layerDragging && Environment.TickCount64 - _layerPressTime >= 220) BeginLayerPreview();
        if (!_layerDragging) return;
        if (_layerDragScroll is { } scroll && _layerDragAt.X >= 0 && _layerDragAt.X < _layers.Bounds.Width)
        {
            const double edge = 42; var height = _layers.Bounds.Height;
            var speed = _layerDragAt.Y < edge ? -Math.Clamp((edge - _layerDragAt.Y) / edge, 0, 1) : _layerDragAt.Y > height - edge ? Math.Clamp((_layerDragAt.Y - height + edge) / edge, 0, 1) : 0;
            if (speed != 0) { scroll.Offset = new Vector(scroll.Offset.X, Math.Clamp(scroll.Offset.Y + speed * 10, 0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height))); _layers.UpdateLayout(); }
        }
        _layerDragOverlay.Pointer += (_layerDragAt - _layerDragOverlay.Pointer) * .55;
        UpdateLayerDrop(); _layerDragOverlay.InvalidateVisual();
    }
    private void UpdateLayerDrop()
    {
        _dropRow = null; _layerDragOverlay.LineY = null;
        if (_document is not { } doc || _layerDragAt.X < 0 || _layerDragAt.X >= _layers.Bounds.Width - 16) return;
        var y = Math.Clamp(_layerDragAt.Y, 1, Math.Max(1, _layers.Bounds.Height - 1));
        foreach (var candidate in _layers.GetRealizedContainers().OfType<ListBoxItem>())
        {
            if (candidate.Tag is not Guid id || candidate.TranslatePoint(default, _layers) is not { } top || y < top.Y || y > top.Y + candidate.Bounds.Height) continue;
            if (!CanDropLayerRoots(doc, _layerDragIds, id)) break;
            _dropRow = candidate; _dropAbove = y < top.Y + candidate.Bounds.Height / 2;
            _layerDragOverlay.LineY = top.Y + (_dropAbove ? 0 : candidate.Bounds.Height); break;
        }
        _layerDragOverlay.InvalidateVisual();
    }
    private void EndLayerDrag()
    {
        _layerDragTimer?.Stop(); _dragLayer = null; _layerDragging = false; _dropRow = null;
        _layerDragOverlay.IsVisible = false; _layerDragOverlay.LineY = null;
        if (_layerDragSource is { } source) source.Opacity = 1; _layerDragSource = null;
        _layers.Cursor = null; var pointer = _layerPointer; _layerPointer = null; pointer?.Capture(null);
    }
}

internal sealed class LayerDragOverlay : Control
{
    internal Point Pointer;
    internal double? LineY;
    internal string Label = "";
    public override void Render(DrawingContext context)
    {
        var blue = new SolidColorBrush(Color.Parse("#4D9DFF"));
        if (LineY is { } y) { context.DrawLine(new Pen(blue, 2), new Point(30, y), new Point(Math.Max(30, Bounds.Width - 18), y)); context.DrawEllipse(blue, null, new Point(30, y), 3, 3); }
        var width = Math.Min(220, Math.Max(30, Bounds.Width - 40)); var x = Math.Clamp(Pointer.X + 12, 4, Math.Max(4, Bounds.Width - width - 20)); var top = Math.Clamp(Pointer.Y + 15, 2, Math.Max(2, Bounds.Height - 32));
        var box = new Rect(x, top, width, 28); context.DrawRectangle(Skin.SurfaceDarkBrush, new Pen(blue, 1), box, 4, 4);
        var text = new FormattedText(Label, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, Skin.LabelBrush) { MaxTextWidth = Math.Max(1, width - 16), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        context.DrawText(text, new Point(x + 8, top + 5));
    }
}
