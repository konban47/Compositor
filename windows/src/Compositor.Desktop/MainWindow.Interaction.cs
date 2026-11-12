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
        InitializeEditing();
        Point? middle = null;
        _layers.AddHandler(PointerPressedEvent, (_, e) => { if (!e.GetCurrentPoint(_layers).Properties.IsMiddleButtonPressed) return; middle = e.GetPosition(_layers); e.Pointer.Capture(_layers); _layers.Cursor = new Cursor(StandardCursorType.SizeAll); e.Handled = true; }, RoutingStrategies.Tunnel);
        _layers.AddHandler(PointerMovedEvent, (_, e) => { if (middle is not { } last) return; var at = e.GetPosition(_layers); if (_layers.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } scroll) scroll.Offset = new Vector(scroll.Offset.X, Math.Clamp(scroll.Offset.Y + last.Y - at.Y, 0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height))); middle = at; e.Handled = true; }, RoutingStrategies.Tunnel);
        _layers.AddHandler(PointerReleasedEvent, (_, e) => { if (middle is null) return; middle = null; e.Pointer.Capture(null); _layers.Cursor = null; e.Handled = true; }, RoutingStrategies.Tunnel);
        _layers.PointerCaptureLost += (_, _) => { middle = null; _layers.Cursor = null; };
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
        _cameraRawAfterImport = null;
        FinishPlacement(true);
        CommitText();
        var target = _open;
        var before = target.Document?.Clone(); var selected = Selected;
        if (before is not null) target.History.Begin("Place Images", target.Document, selected);
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
        finally
        {
            _importingDrop = false;
            if (before is not null)
            {
                var old = before.Layers.Select(l => l.ID).ToHashSet();
                var added = target.Document?.Layers.Where(l => !old.Contains(l.ID)).Select(l => l.ID).ToHashSet() ?? [];
                if (added.Count > 0 && _tabs.Contains(target) && !EditorPreferences.Current.SkipPlacement) BeginPlacement(target, before, selected, added);
                else target.History.End(target.Document, selected);
            }
            OpenImportedCameraRaw();
        }
    }

    private void SelectMoveTarget(SKPoint point, KeyModifiers modifiers)
    {
        if (_placementTab is not null) return;
        if (_document is not { } document) return;
        if (_tool == Tool.Artboard) return;
        if (MaskTarget && PropertyLayer?.Mask?.IsLinked == false && PropertyLayer.MaskTransform.Contains(point)) return;
        // Handles retain the current transform. Interior clicks pick the top visible pixel, including overlapping layers.
        if (_canvas.TransformBox is { } box && _transformShown &&
            _canvas.TransformHandleAt(box, point) is not null) return;
        if (EditorPreferences.Current.MoveAutoSelect == modifiers.HasFlag(KeyModifiers.Control)) return;
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
            var picked = layer;
            if (EditorPreferences.Current.MoveSelectGroup)
                while (picked.ParentID is { } parent && layers.TryGetValue(parent, out var owner)) picked = owner;
            if (EditorPreferences.Current.MoveExpandGroup)
            {
                var owner = layer.ParentID; var changed = false;
                while (owner is { } parent && layers.TryGetValue(parent, out var group)) { changed |= _open.Collapsed.Remove(parent); owner = group.ParentID; }
                if (changed) ShowLayers(document);
            }
            SelectLayerRow(picked.ID, modifiers.HasFlag(KeyModifiers.Shift));
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

    /// <summary>How close to a row's top or bottom edge the pointer must be to stand on the line between two layers.</summary>
    private const double ClipReach = 6;

    /// <summary>
    /// The upper of the two layers whose dividing line the pointer is on, in the panel's order: near the bottom of a row it is
    /// that row's layer, near the top the layer listed above it. Null away from a line, or at the very top or bottom.
    /// </summary>
    private Guid? ClipBoundary(Guid rowLayer, double y, double height)
    {
        var index = _rows.IndexOf(rowLayer); if (index < 0) return null;
        if (y >= height - ClipReach) return index + 1 < _rows.Count ? rowLayer : null;
        if (y <= ClipReach) return index > 0 ? _rows[index - 1] : null;
        return null;
    }

    /// <summary>Alt-click between two layers: the upper one clipped to the one under it, or released when it already is.</summary>
    internal void ClipAtBoundary(Guid upper)
    {
        if (_document is not { } document || !document.Layers.Any(l => l.ID == upper)) return;
        Edit(LayerMaskEdits.ReleasesSelected(document, [upper]) ? "Release Clipping Mask" : "Create Clipping Mask", () => LayerMaskEdits.ToggleSelected(document, [upper]));
        ShowLayers(document); Reselect(upper);
    }

    private static Cursor? _clipCursor;
    /// <summary>Photoshop's clipping cursor: an arrow bending down beside a small square.</summary>
    private static Cursor ClipCursor => _clipCursor ??= MakeClipCursor();
    private static Cursor MakeClipCursor()
    {
        using var bitmap = new SKBitmap(32, 32, SKColorType.Bgra8888, SKAlphaType.Premul); bitmap.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(bitmap))
        using (var outline = new SKPaint { Color = SKColors.White, StrokeWidth = 5, Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeCap = SKStrokeCap.Round })
        using (var ink = new SKPaint { Color = SKColors.Black, StrokeWidth = 2.4f, Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeCap = SKStrokeCap.Round })
        using (var fill = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill, IsAntialias = true })
        using (var white = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill })
        {
            using var hook = SKPath.ParseSvgPathData("M15,4 L8,4 L8,18");
            canvas.DrawPath(hook, outline); canvas.DrawPath(hook, ink);
            using var head = SKPath.ParseSvgPathData("M2,16 L14,16 L8,24 Z");
            canvas.DrawPath(head, outline); canvas.DrawPath(head, fill);
            canvas.DrawRect(17, 2, 12, 12, white); canvas.DrawRect(17, 2, 12, 12, ink);
        }
        return new Cursor(Eyedroppers.Thumbnail(bitmap), new PixelPoint(8, 24));
    }

    private Control LayerRow(ImageLayer layer, int depth, string notes)
    {
        ImageLayer? Current() => _document?.Layers.FirstOrDefault(item => item.ID == layer.ID);
        var eye = new Button { Content = new LayerEye(layer.IsVisible) { Width = 18, Height = 18 }, Width = 30, Height = double.NaN, VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(3), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Focusable = false, Tag = "visibility" };
        ToolTip.SetTip(eye, Localize.Text(layer.IsVisible ? "Hide Layer" : "Show Layer"));
        Avalonia.Automation.AutomationProperties.SetName(eye, Localize.Text(layer.IsVisible ? "Hide Layer" : "Show Layer"));
        eye.Click += (_, _) =>
        {
            if (_document is not { } document || Current() is not { } current) return;
            Edit("Layer Visibility", () => LayerEdits.SetVisible(document, current.ID, !current.IsVisible));
            ShowLayers(document);
        };
        var panel = new Grid { ColumnDefinitions = new ColumnDefinitions("30,Auto,Auto,Auto,Auto,*,Auto,Auto"), MinHeight = layer.IsGroup ? 28 : Math.Max(42, EditorPreferences.Current.LayerThumbnailSize + 10) };
        panel.PointerEntered += (_, _) => { if (!_layerDragging && EditorPreferences.Current.MoveLayerHoverBounds) { _moveHoverBox = layer.IsGroup && _document is { } doc ? TransformEdits.GroupBox(doc, [layer.ID]) : layer.Transform; _canvas.InvalidateOverlay(); } };
        panel.PointerExited += (_, _) => { _moveHoverBox = null; _canvas.InvalidateOverlay(); };
        panel.Children.Add(new Border { Child = eye, Background = Skin.ChromeBrush, BorderBrush = Skin.BorderSubtleBrush, BorderThickness = new Thickness(0, 0, 1, 0) });
        // Alt over the line between two layers shows Photoshop's clipping cursor; a click there clips the upper layer to the one
        // under it, or releases it, as Create/Release Clipping Mask does.
        panel.PointerMoved += (_, e) => panel.Cursor = e.KeyModifiers.HasFlag(KeyModifiers.Alt) && ClipBoundary(layer.ID, e.GetPosition(panel).Y, panel.Bounds.Height) is not null ? ClipCursor : null;
        panel.PointerExited += (_, _) => panel.Cursor = null;
        panel.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt) || !e.GetCurrentPoint(panel).Properties.IsLeftButtonPressed
                || ClipBoundary(layer.ID, e.GetPosition(panel).Y, panel.Bounds.Height) is not { } upper) return;
            ClipAtBoundary(upper); e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        if (layer.Label != Compositor.Core.Format.LayerLabel.None)
        {
            var color = layer.Label switch { Compositor.Core.Format.LayerLabel.Red => "#994c4c", Compositor.Core.Format.LayerLabel.Orange => "#996432", Compositor.Core.Format.LayerLabel.Yellow => "#8d853c", Compositor.Core.Format.LayerLabel.Green => "#4e7c4e", Compositor.Core.Format.LayerLabel.Blue => "#47668a", Compositor.Core.Format.LayerLabel.Violet => "#785489", Compositor.Core.Format.LayerLabel.Teal => "#19969a", Compositor.Core.Format.LayerLabel.Indigo => "#655bf0", Compositor.Core.Format.LayerLabel.Magenta => "#d62d93", Compositor.Core.Format.LayerLabel.Purple => "#ba38c8", _ => "#686868" };
            eye.Background = new SolidColorBrush(Avalonia.Media.Color.Parse(color));
        }
        // The marks at the right, left to right as Photoshop orders them: smart filters, layer style, then the arrow that shows or
        // hides the rows under the layer. They sit side by side, so none covers another.
        var marks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Tag = "layer-marks" };
        if (layer.SmartObject is not null && layer.SmartFilters is not null)
        {
            var filters = new Border { Child = new SmartFilterGlyph { Width = 18, Height = 18 }, Width = 24, Height = 24, Padding = new Thickness(3), Tag = "smart-filter-mark", Background = Brushes.Transparent };
            ToolTip.SetTip(filters, Localize.Text("Smart Filters")); marks.Children.Add(filters);
        }
        if (layer.Effects is not null || layer.Blending is { IsDefault: false })
        {
            var fx = PanelButton("fx", "Layer Style", () => { SelectLayerRow(layer.ID); _ = EditLayerStyle(); }); fx.Width = 26; fx.Opacity = layer.Effects?.Enabled == false ? 0.4 : 1;
            marks.Children.Add(fx);
        }
        if (layer.SmartObject is not null && layer.SmartFilters is not null)
        {
            var open = !_open.Collapsed.Contains(layer.ID);
            var toggle = new Button { Content = open ? "⌃" : "⌄", Width = 18, Height = 24, Padding = default, Background = Brushes.Transparent, BorderThickness = default, Focusable = false, Tag = "smart-filter-toggle", HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(toggle, Localize.Text(open ? "Hide the smart filters" : "Show the smart filters"));
            toggle.Click += (_, e) => { if (!_open.Collapsed.Add(layer.ID)) _open.Collapsed.Remove(layer.ID); if (_document is { } d) { ShowLayers(d); SelectLayerRow(layer.ID); } e.Handled = true; };
            marks.Children.Add(toggle);
        }
        Grid.SetColumn(marks, 6); panel.Children.Add(marks);
        panel.DoubleTapped += (_, e) => { if (e.Handled) return; SelectLayerRow(layer.ID); _ = EditLayerStyle(); e.Handled = true; };
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
            { Smart = layer.SmartObject is not null, Width = layer.IsGroup || EditorPreferences.Current.LayerThumbnailSize == 0 ? 20 : EditorPreferences.Current.LayerThumbnailSize, Height = layer.IsGroup || EditorPreferences.Current.LayerThumbnailSize == 0 ? 20 : EditorPreferences.Current.LayerThumbnailSize, HideImage = EditorPreferences.Current.LayerThumbnailSize == 0, LayerBounds = () => (Current()?.Transform, _document?.Width ?? 1, _document?.Height ?? 1), Margin = new Thickness(layer.IsGroup ? 0 : depth * 12, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, Active = () => Selected == layer.ID && !MaskTarget };
        if (layer.Asset?.BitDepth > 8) notes = (layer.Asset.BitDepth == 32 ? "HDR" : "16-Bit") + (string.IsNullOrEmpty(notes) ? "" : " · " + notes);
        _layerThumbnails.Add(thumbnail);
        thumbnail.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(thumbnail).Properties.IsRightButtonPressed) { OpenThumbnailContext(thumbnail, layer); e.Handled = true; return; }
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            { SelectLayerRow(layer.ID); SelectLayerPixels(); e.Handled = true; }
            else { SelectLayerRow(layer.ID); SetPaintingMask(false); e.Handled = true; }
        };
        thumbnail.DoubleTapped += (_, e) => { SelectLayerRow(layer.ID); if (layer.SmartObject is not null) EditSmartContents(); else _ = EditLayerStyle(); e.Handled = true; };
        Grid.SetColumn(thumbnail, 2); panel.Children.Add(thumbnail);
        if (layer.Mask is not null)
        {
            var link = PanelButton("link", layer.Mask.IsLinked ? "Unlink Layer Mask" : "Link Layer Mask", () => { SelectLayerRow(layer.ID); ToggleMaskLink(); });
            link.Width = 21; link.Padding = new Thickness(1); link.Tag = "mask-link";
            if (!layer.Mask.IsLinked) link.Content = null;
            Grid.SetColumn(link, 3); panel.Children.Add(link);
            var mask = new LayerThumbnail(() => Current()?.Mask?.Asset.Thumbnail) { Tag = "mask-thumbnail", Width = 30, Height = 30, Margin = new Thickness(3), Active = () => Selected == layer.ID && MaskTarget };
            _layerThumbnails.Add(mask); ToolTip.SetTip(mask, Localize.Text("Edit Layer Mask"));
            mask.DoubleTapped += (_, e) => { SelectLayerRow(layer.ID); SetPaintingMask(true); OpenInspector(0); e.Handled = true; };
            mask.PointerPressed += (_, e) => { if (e.GetCurrentPoint(mask).Properties.IsRightButtonPressed) { OpenMaskContext(mask, layer); e.Handled = true; return; } SelectLayerRow(layer.ID); SetPaintingMask(true); e.Handled = true; };
            Grid.SetColumn(mask, 4); panel.Children.Add(mask);
        }
        var name = new TextBlock { Text = layer.Name + (layer.LinkID is not null ? "  ↔" : ""), FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 2, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        name.DoubleTapped += (_, e) => { SelectLayerRow(layer.ID); _ = RenameLayer(); e.Handled = true; };
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
            Grid.SetColumn(unlock, 7); panel.Children.Add(unlock);
        }
        // A smart object's filters are listed inside its own row, so selecting, dragging or hiding the layer takes them along.
        Control content = SmartFilterRows(layer) is { } filterRows ? new StackPanel { Children = { panel, filterRows } } : panel;
        return new Border { BorderBrush = Skin.BorderSubtleBrush, BorderThickness = new Thickness(0, 0, 0, 1), Child = content };
    }


}

internal sealed class LayerEye(bool visible) : Control
{
    public override void Render(DrawingContext context)
    {
        var pen = new Pen(Skin.LabelBrush, 1.4) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (visible)
        {
            var shape = StreamGeometry.Parse("M2,10 Q10,2 18,10 Q10,18 2,10 Z");
            context.DrawGeometry(null, pen, shape);
            context.DrawEllipse(Skin.LabelBrush, null, new Point(10, 10), 2.8, 2.8);
        }
        else
        {
            var shape = StreamGeometry.Parse("M3,11 Q10,17 17,11");
            context.DrawGeometry(null, new Pen(Skin.SecondaryBrush, 1.2), shape);
            var slash = new Pen(Skin.SecondaryBrush, 1.2) { LineCap = PenLineCap.Round };
            context.DrawLine(slash, new Point(4, 16), new Point(16, 4));
        }
    }
}
