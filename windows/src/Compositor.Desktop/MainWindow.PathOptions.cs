using Avalonia.Controls;
using Avalonia.Input;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;
namespace Compositor.Desktop;
public sealed partial class MainWindow
{
    private void ApplyPathOperation(PathOperation operation)
    {
        EditPenComponents(nodes =>
        {
            if (nodes.Subpaths.Count < 2) return null;
            string? combined = null;
            foreach (var contour in nodes.Subpaths)
            {
                var single = new PathNodes(); single.Subpaths.Add(contour);
                combined = PenPaths.Combine(combined, single.ToSvg(), operation);
                if (combined is null) return null;
            }
            return PathNodes.Parse(combined!);
        });
        _canvas.SelectedPathComponents.Clear();
    }
    private (int Subpath, int Node, float T)? _segmentDrag;
    private SKPoint _segmentLast;
    private void InitializePathToolOptions()
    {
        _optionsBar.SelectedPathStyle = () => PropertyLayer?.LiveShape;
        _optionsBar.SelectedPathSize = () => { if (CurrentPenSvg() is not { } svg) return (1, 1); using var path = SKPath.ParseSvgPathData(svg); return path is null ? (1, 1) : (Math.Max(1, path.TightBounds.Width), Math.Max(1, path.TightBounds.Height)); };
        _optionsBar.PathSizeRequested += ResizePath;
        _optionsBar.PathStrokeRequested += (width, pattern) => ChangePathStyle(style => { style.StrokeWidth = width; style.StrokeColor ??= 0xff000000; if (pattern is not null) style.StrokePattern = pattern; });
    }
    private void ChangePathStyle(Action<LayerShapeStyle> change)
    {
        if (_document is not { } doc || PropertyLayer is not { LiveShape: { } current } layer || !LayerProtection.CanPaint(doc, layer.ID)) return;
        var style = System.Text.Json.JsonSerializer.Deserialize<LayerShapeStyle>(System.Text.Json.JsonSerializer.Serialize(current))!; change(style);
        Edit("Path Appearance", () =>
        {
            if (!style.IsValid) return false;
            if (style.Kind == ShapeKind.Path && style.Path is { } path && style.StrokeWidth != current.StrokeWidth)
                return ShapeEdits.SetPath(doc, layer.ID, path, style);
            if (ShapeEdits.Image(style, layer.Asset!.Width, layer.Asset.Height) is not { } image) return false;
            layer.Asset = ImportedImage.Create(image, layer.Name); layer.Shape = new(style, image); return true;
        });
    }
    private async Task PathColor(bool stroke)
    {
        if (PropertyLayer?.LiveShape is not { } style) return;
        var c = stroke ? new SKColor(style.StrokeColor ?? 0xff000000) : new SKColor((byte)(style.Red * 255), (byte)(style.Green * 255), (byte)(style.Blue * 255));
        var form = new FormDialog(stroke ? "Path Stroke Color" : "Path Fill Color", 410, 320); var none = form.Check("None", stroke ? (style.StrokeWidth ?? style.LineWidth ?? 0) <= 0 : style.FillEnabled == false);
        var chosen = (Red: c.Red / 255d, Green: c.Green / 255d, Blue: c.Blue / 255d);
        var color = FormDialog.ActionButton("Choose Color…", () => { }); color.Click += async (_, _) => await ColorPickerDialog.Pick(form, "Color", chosen, value => chosen = value); form.Fields.Children.Add(color);
        if (await form.ShowDialog<bool>(this)) ChangePathStyle(s => { if (stroke) { s.StrokeColor = (uint)new SKColor((byte)(chosen.Red * 255), (byte)(chosen.Green * 255), (byte)(chosen.Blue * 255)); s.StrokeWidth = none.IsChecked == true ? 0 : Math.Max(1, s.StrokeWidth ?? s.LineWidth ?? 1); } else { s.Red = chosen.Red; s.Green = chosen.Green; s.Blue = chosen.Blue; s.FillEnabled = none.IsChecked != true; } });
    }
    private void ResizePath(bool horizontal, double value)
    {
        EditPenComponents(nodes => { using var path = SKPath.ParseSvgPathData(nodes.ToSvg()); if (path is null) return null; var b = path.TightBounds; if (b.Width <= 0 || b.Height <= 0) return null; var scale = (float)(value / (horizontal ? b.Width : b.Height)); var sx = horizontal || _options.Pen.LinkDimensions ? scale : 1; var sy = !horizontal || _options.Pen.LinkDimensions ? scale : 1; path.Transform(new SKMatrix(sx, 0, b.Left * (1 - sx), 0, sy, b.Top * (1 - sy), 0, 0, 1)); return PathNodes.Parse(path.ToSvgPathData()); });
    }
    private bool PathEditorPressed(SKPoint at, KeyModifiers keys)
    {
        if (_tool is not (Tool.Path or Tool.PathSelection) || _document is not { } doc) return false;
        if (_options.Pen.AllLayers)
        {
            foreach (var layer in doc.Layers.AsEnumerable().Reverse().Where(l => l.IsVisible && LayerProtection.CanPaint(doc, l.ID)))
            {
                using var outline = layer.LiveShape is { } shape ? ShapeOutline(layer, shape) : layer.Mask?.VectorPath is { } svg ? SKPath.ParseSvgPathData(svg) : null;
                if (outline is null) continue;
                outline.Transform(layer.LiveShape is not null ? BrushEdits.PixelToDocument(layer.Transform, 1, 1) : BrushEdits.PixelToDocument(layer.MaskTransform, layer.Mask!.Asset.Width, layer.Mask.Asset.Height));
                var nodes = PathNodes.Parse(outline.ToSvgPathData());
                if (!outline.Contains(at.X, at.Y) && (nodes is null || PathSegments.Find(nodes, p => p, at, (float)(10 / _canvas.Zoom)) is null)) continue;
                _options.Pen.Output = PenOutput.Shape; if (Selected != layer.ID) Reselect(layer.ID); SyncPathNodes(); break;
            }
        }
        if (_tool != Tool.Path || _pathNodes is not { } editable || _canvas.PathToDocument is not { } map || _canvas.DocumentToPath is not { } inverse) return false;
        if (editable.Grabs().Any(g => SKPoint.Distance(map(g.Point), at) * _canvas.Zoom < 9)) return false;
        if (PathSegments.Find(editable, map, at, (float)(8 / _canvas.Zoom)) is not { } segment) return false;
        _segmentDrag = segment; _segmentLast = inverse(at); _history.Begin("Reshape Path Segment", doc, Selected); _canvas.SelectedNode = (segment.Subpath, segment.Node); return true;
    }
    private bool PathEditorMoved(SKPoint at, KeyModifiers keys)
    {
        if (_segmentDrag is not { } segment || _pathNodes is not { } path || _canvas.DocumentToPath is not { } inverse) return false;
        var point = inverse(at); var delta = point - _segmentLast;
        if (keys.HasFlag(KeyModifiers.Shift)) { var angle = Math.Round(Math.Atan2(delta.Y, delta.X) / (Math.PI / 4)) * Math.PI / 4; var length = SKPoint.Distance(point, _segmentLast); delta = new((float)(Math.Cos(angle) * length), (float)(Math.Sin(angle) * length)); }
        PathSegments.Move(path, segment.Subpath, segment.Node, segment.T, delta, _options.Pen.ConstrainDragging); _segmentLast += delta; _canvas.InvalidateOverlay(); return true;
    }
    private bool PathEditorReleased()
    {
        if (_segmentDrag is null) return false; _segmentDrag = null; if (_pathNodes is { } nodes) SaveEditablePath(nodes.ToSvg()); _history.End(_document, Selected); Refresh(); return true;
    }
}
