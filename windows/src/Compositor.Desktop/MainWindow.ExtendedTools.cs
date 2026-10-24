using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private PathNodes? _pen;
    private SKPoint _penLast;
    private bool _penDrawing;
    private bool _typeMaskSession;
    private Guid? _typeMaskPreviousLayer;
    private Guid? _adjustmentBrushLayer;
    private string _adjustmentBrushKind = "Exposure";
    private double _adjustmentAmount = 1;
    private (int Subpath, int Node)? _convertDrag;

    private bool CanvasActionPressed(SKPoint at, KeyModifiers modifiers, int clicks)
    {
        if (_placementTab is not null) return PlacementPressed(at, modifiers);
        if (RemovePressed(at, modifiers)) return true;
        if (ArtboardPressed(at, modifiers)) return true;
        if (ProfessionalPressed(at, modifiers, clicks)) return true;
        if (PathEditorPressed(at, modifiers)) return true;
        if (_tool == Tool.AdjustmentBrush && _adjustmentObjectMode) { _adjustmentObjectMode = false; _ = ApplyAdjustmentObject(at); return true; }
        if (_tool is Tool.Bucket or Tool.MagicEraser) { PaintBucket(at, _tool == Tool.MagicEraser); return true; }
        if (ToolCatalog.IsPen(_tool)) return PenPressed(at, modifiers, clicks);
        if (_tool is Tool.AddAnchor or Tool.DeleteAnchor or Tool.ConvertAnchor && _pathNodes is { } path && _canvas.DocumentToPath is { } map)
        {
            var point = map(at); var grab = path.Grabs().Where(g => !g.Handle).MinBy(g => SKPoint.Distance(g.Point, point));
            if (path.NodeCount == 0) return true;
            if (_tool == Tool.ConvertAnchor)
            {
                _history.Begin("Convert Anchor Point", _document, Selected); _convertDrag = (grab.Subpath, grab.Node);
                var node = path.Subpaths[grab.Subpath].Nodes[grab.Node];
                Edit("Convert Anchor Point", () =>
                {
                    if (node.In is not null || node.Out is not null) { node.In = null; node.Out = null; }
                    else
                    {
                        var nodes = path.Subpaths[grab.Subpath].Nodes; var before = nodes[(grab.Node + nodes.Count - 1) % nodes.Count].Point;
                        var after = nodes[(grab.Node + 1) % nodes.Count].Point; var delta = PlacementMesh.Scale(after - before, 1f / 6);
                        node.In = node.Point - delta; node.Out = node.Point + delta;
                    }
                    return SaveEditablePath(path.ToSvg());
                }); SyncPathNodes();
            }
            else if (_tool == Tool.AddAnchor)
            {
                Edit("Add Anchor Point", () => path.InsertNearest(point) && SaveEditablePath(path.ToSvg())); SyncPathNodes();
            }
            else EditPathNode(grab.Subpath, grab.Node, false);
            return true;
        }
        return false;
    }
    private void CanvasActionMoved(SKPoint at, KeyModifiers modifiers)
    {
        if (PathEditorMoved(at, modifiers)) return;
        if (_placementTab is not null) { PlacementMoved(at, modifiers); return; }
        if (RemoveMoved(at)) return;
        if (ArtboardMoved(at, modifiers)) return;
        if (ProfessionalMoved(at, modifiers)) return;
        if (_convertDrag is { } convert && _pathNodes is { } editable && _canvas.DocumentToPath is { } map)
        {
            var node = editable.Subpaths[convert.Subpath].Nodes[convert.Node]; var point = map(at);
            node.Out = point; node.In = modifiers.HasFlag(KeyModifiers.Alt) ? node.In : node.Point - (point - node.Point);
            _canvas.InvalidateOverlay(); return;
        }
        PenMoved(at, modifiers);
    }
    private void CanvasActionReleased()
    {
        if (PathEditorReleased()) return;
        if (RemoveReleased()) return;
        if (ArtboardReleased()) return;
        if (ProfessionalReleased()) return;
        if (_convertDrag is not null)
        {
            _convertDrag = null;
            if (_pathNodes is { } path) SaveEditablePath(path.ToSvg());
            _history.End(_document, Selected); Refresh();
        }
        PenReleased();
    }
    private void SmoothPen()
    {
        if (_pen?.Subpaths.FirstOrDefault() is not { } contour) return;
        PenPaths.Smooth(contour);
        foreach (var index in _penCorners.Where(i => i < contour.Nodes.Count)) { contour.Nodes[index].In = null; contour.Nodes[index].Out = null; }
    }
    private bool FinishPen()
    {
        if (_pen is not { } pen || _document is null) return false;
        if (pen.NodeCount < 2) return CancelPen();
        var svg = pen.ToSvg(); _madePenShape = null;
        Edit("Draw Path", () => CommitPenPath(svg));
        ClearPenDraft(); if (_madePenShape is { } id) Reselect(id); SyncPathNodes(); _canvas.InvalidateOverlay(); return true;
    }
    private bool CancelPen()
    { if (_pen is null) return false; ClearPenDraft(); return true; }
    private void DrawEditingOverlay(DrawingContext context, Func<SKPoint, Point> screen)
    {
        DrawAdjustmentBrushOverlay(context, screen);
        DrawProfessionalOverlay(context, screen);
        DrawRemoveOverlay(context, screen);
        DrawCloneOverlay(context, screen);
        DrawArtboardDraft(context, screen);
        DrawMoveHover(context, screen);
        var pen = new Pen(new SolidColorBrush(Color.Parse(EditorPreferences.Current.PathColor)), EditorPreferences.Current.PathWidth) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (_placementTab is not null && _placePivotVisible && PropertyLayer is { } layer)
        {
            var at = screen(PlacementPivot(layer.Transform)); context.DrawEllipse(null, pen, at, 5, 5);
            context.DrawLine(pen, at - new Vector(8, 0), at + new Vector(8, 0)); context.DrawLine(pen, at - new Vector(0, 8), at + new Vector(0, 8));
        }
        if (_placeMesh is { } mesh)
        {
            for (var y = 0; y < mesh.Rows.Count; y++) for (var x = 0; x < mesh.Columns.Count; x++)
            {
                var at = screen(mesh.Points[y * mesh.Columns.Count + x]);
                if (x + 1 < mesh.Columns.Count) context.DrawLine(pen, at, screen(mesh.Points[y * mesh.Columns.Count + x + 1]));
                if (y + 1 < mesh.Rows.Count) context.DrawLine(pen, at, screen(mesh.Points[(y + 1) * mesh.Columns.Count + x]));
                context.DrawEllipse(Skin.ChromeBrush, pen, at, 4, 4);
            }
        }
        DrawPuppetPins(context, screen);
        DrawWorkPath(context, screen, pen);
        if (_pen is { } draft)
        {
            var geometry = new StreamGeometry();
            using (var drawing = geometry.Open()) foreach (var contour in draft.Subpaths)
            {
                if (contour.Nodes.Count == 0) continue;
                drawing.BeginFigure(screen(contour.Nodes[0].Point), false);
                for (var i = 1; i < contour.Nodes.Count; i++) drawing.CubicBezierTo(screen(contour.Nodes[i - 1].Out ?? contour.Nodes[i - 1].Point), screen(contour.Nodes[i].In ?? contour.Nodes[i].Point), screen(contour.Nodes[i].Point));
                drawing.EndFigure(contour.Closed);
            }
            context.DrawGeometry(null, pen, geometry);
            foreach (var contour in draft.Subpaths)
            foreach (var node in contour.Nodes)
            {
                if (node.In is { } incoming) context.DrawLine(pen, screen(node.Point), screen(incoming));
                if (node.Out is { } outgoing) context.DrawLine(pen, screen(node.Point), screen(outgoing));
            }
            foreach (var grab in draft.Grabs())
            {
                var blue = new SolidColorBrush(Color.Parse("#1473E6"));
                var selected = !grab.Handle && (grab.Node == _penSelected || grab.Node == 0 && _canvas.ClosePathHint);
                context.DrawEllipse(selected ? blue : Skin.ChromeBrush, pen, screen(grab.Point), 3.5, 3.5);
            }
            if (_options.Pen.RubberBand && !_penDrawing && _penHover is { } hover && draft.Subpaths[0].Nodes.LastOrDefault() is { } last)
            {
                var preview = new StreamGeometry(); using (var g = preview.Open()) { g.BeginFigure(screen(last.Point), false); g.CubicBezierTo(screen(last.Out ?? last.Point), screen(hover), screen(hover)); g.EndFigure(false); }
                context.DrawGeometry(null, pen, preview);
            }
        }
    }
    private void BuildCanvasMenu(ContextMenu menu)
    {
        if (BuildGuideAndPasteboardMenu(menu)) return;
        foreach (var (label, action) in new (string, Action)[] { ("Copy", Copy), ("Paste", Paste), ("Finish Path", () => FinishPen()), ("Cancel Path", () => CancelPen()) })
        { var item = new MenuItem { Header = Localize.Text(label), IsEnabled = label.Contains("Path") ? _pen is not null : _document is not null }; item.Click += (_, _) => action(); menu.Items.Add(item); }
    }
    private void PaintBucket(SKPoint point, bool erase)
    {
        if (EditActiveAlpha(erase ? "Magic Eraser" : "Paint Bucket", (target, channel) => BucketEdits.Apply(target, channel, point, _options.Wand with { Antialias = erase ? _options.SelectionAntialiased : _options.BucketAntialias }, BrushColour(), erase, _options.Brush.Opacity, false, _options.Brush.Pattern, PaintingOptions.Blend, _options.BucketPattern))) return;
        if (_document is not { } doc || Selected is not { } id || !LayerProtection.CanPaint(doc, id)) { Say("The layer is locked."); return; }
        if (!erase && _options.PaintOnMask) { Edit("Paint Bucket Mask", () => BucketEdits.ApplyMask(doc, id, point, _options.Wand with { Antialias=_options.BucketAntialias }, BrushColour(), _options.Brush.Opacity, _options.Brush.Pattern, PaintingOptions.Blend, _options.BucketPattern)); return; }
        Edit(erase ? "Magic Eraser" : "Paint Bucket", () => BucketEdits.Apply(doc, id, point, _options.Wand with { Antialias = erase ? _options.SelectionAntialiased : _options.BucketAntialias }, BrushColour(), erase, _options.Brush.Opacity, _options.WandAllLayers, _options.Brush.Pattern, PaintingOptions.Blend, _options.BucketPattern));
    }
    private int _paintedAdjustmentKind = -1;
    private Guid? AdjustmentTarget(CanvasDocument doc, Guid selected)
    {
        var wanted = AdjustmentBrushPresets.Create(_options.AdjustmentBrushKind);
        var chosen = doc.Layers.FirstOrDefault(l => l.ID == selected && l.Adjustment is { } a && a.Kind == wanted.Kind && a.Extended?.Kind == wanted.Extended?.Kind);
        if (chosen is not null) { _adjustmentBrushLayer = chosen.ID; _paintedAdjustmentKind = _options.AdjustmentBrushKind; }
        var layer = doc.Layers.FirstOrDefault(l => l.ID == _adjustmentBrushLayer && l.Adjustment is not null);
        if (_paintedAdjustmentKind != _options.AdjustmentBrushKind || layer is null || selected != layer.ID)
        {
            if (_options.AdjustmentSubtract) return null;
            var settings = AdjustmentBrushPresets.Create(_options.AdjustmentBrushKind);
            var id = LayerPlacement.AddAdjustment(doc, settings.Kind, selected);
            layer = doc.Layers.FirstOrDefault(l => l.ID == id); if (layer is null) return null;
            layer.Adjustment = settings; layer.Name = Localize.Text(ToolOptionsBar.AdjustmentNames[_options.AdjustmentBrushKind]);
            var mask = Bitmaps.Allocate(Bitmaps.MaskInfo(doc.Width, doc.Height)); mask.Erase(SKColors.Black); layer.Mask = Compositor.Core.Model.LayerMask.AssetFrom(mask);
            _adjustmentBrushLayer = layer.ID; _paintedAdjustmentKind = _options.AdjustmentBrushKind;
        }
        return layer.ID;
    }
    private void PaintAdjustment(IReadOnlyList<SKPoint> stroke)
    {
        if (_document is not { } doc || Selected is not { } selected || !LayerProtection.CanPaint(doc, selected)) return;
        Guid? target = null;
        Edit("Adjustment Brush", () =>
        {
            target = AdjustmentTarget(doc, selected); if (target is not { } id) return false;
            var value = _options.AdjustmentSubtract ? 0 : 1;
            return BrushEdits.PaintMask(doc, id, stroke, _options.Brush with { Red = value, Green = value, Blue = value, Erasing = false, Mode = BrushMode.Paint,
                Pressures = _canvas.CompletedStrokePressures, Painting = PaintingOptions with { Samples = _canvas.CompletedPenSamples, Blend = PaintBlend.Normal, Symmetry = BrushSymmetry.Off } });
        });
        if (target is { } newID) Reselect(newID);
    }
    private bool _adjustmentObjectMode;
    private Task ApplyAdjustmentObject() { _adjustmentObjectMode = !_adjustmentObjectMode; Say(_adjustmentObjectMode ? "Click an object to apply the adjustment." : "Paint to apply the adjustment."); return Task.CompletedTask; }
    private async Task ApplyAdjustmentObject(SKPoint at)
    {
        if (_document is not { } doc || Selected is not { } selected) return;
        var revision = _history.CurrentRevision; var all = _options.WandAllLayers;
        try { _options.WandAllLayers = true; await DetectSubject(false, at); } finally { _options.WandAllLayers = all; }
        if (_document != doc || _history.CurrentRevision == revision) return;
        Guid? target = null;
        Edit("Apply Adjustment to Object", () => { target = AdjustmentTarget(doc, selected); return target is { } id && FillEdits.FillMask(doc, id, _options.AdjustmentSubtract ? (byte)0 : (byte)255); });
        if (target is { } newID) Reselect(newID);
    }
    private async Task ChoosePattern()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Load Pattern"), AllowMultiple = false });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        try { using var imported = Compositor.Core.IO.ImageImporter.Decode(path); var image = imported.Image.Copy(); _options.Brush.Pattern?.Dispose(); _options.Brush = _options.Brush with { Pattern = image }; OptionsChanged(); }
        catch (Exception error) { Say($"Could not import that image: {error.Message}"); }
    }
}
