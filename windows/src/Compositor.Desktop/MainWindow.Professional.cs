using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;
using SelectionMode = Compositor.Core.Document.SelectionMode;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private readonly List<SKPoint> _proPoints = [];
    private SKPoint[]? _perspectiveQuad;
    private SKBitmap? _magneticSample;
    private SKPoint _proStart, _proEnd;
    private int _proGrab = -1;
    private bool _proGesture, _proMovingSelection, _markHistory;
    private DocumentMark? _markBefore;
    private Guid? _selectedMark;
    private SelectionPaintMode _proMode;
    private ProfessionalOptions Pro => _optionsBar.Professional;
    private MarkKind? MarkToolKind => _tool switch { Tool.Slice or Tool.SliceSelect => MarkKind.Slice, Tool.Note => MarkKind.Note, Tool.Count => MarkKind.Count, Tool.Ruler => MarkKind.Measure, Tool.ColorSampler => MarkKind.Sampler, _ => null };
    private SelectionPaintMode ProMode(KeyModifiers keys) => keys.HasFlag(KeyModifiers.Alt) ? keys.HasFlag(KeyModifiers.Shift) ? SelectionPaintMode.Intersect : SelectionPaintMode.Subtract : keys.HasFlag(KeyModifiers.Shift) ? SelectionPaintMode.Add : Pro.SelectionMode;
    private bool ProfessionalPressed(SKPoint at, KeyModifiers keys, int clicks)
    {
        if (_document is not { } doc) return false;
        if (_tool is Tool.SelectionBrush or Tool.QuickSelection or Tool.MagneticLasso or Tool.PerspectiveCrop or Tool.Slice or Tool.SliceSelect or Tool.Frame or Tool.ColorSampler or Tool.Ruler or Tool.Note or Tool.Count or Tool.Patch or Tool.ContentMove or Tool.RedEye)
        {
            if (_open.ActiveAlpha is not null) { Say("Select RGB to use this command."); return true; }
            _proStart = _proEnd = at; _proGesture = true; _proGrab = -1;
            if (_tool == Tool.MagneticLasso)
            {
                if (_proPoints.Count > 2 && (clicks > 1 || SKPoint.Distance(at, _proPoints[0]) < 10 / _canvas.Zoom)) { FinishProfessionalDraft(); return true; }
                if (_proPoints.Count == 0) { _proMode = ProMode(keys); _magneticSample = SelectionEdits.Sample(doc, Pro.SampleAll ? null : Selected); }
                _proPoints.Add(at); _canvas.InvalidateVisual(); return true;
            }
            if (_tool is Tool.SelectionBrush or Tool.QuickSelection) { _proPoints.Clear(); _proPoints.Add(at); _proMode = ProMode(keys); return true; }
            if (_tool == Tool.PerspectiveCrop)
            {
                if (_perspectiveQuad is { } quad)
                { var closest = quad.Select((p, i) => (i, distance: SKPoint.Distance(p, at))).MinBy(p => p.distance); if (closest.distance < 14 / _canvas.Zoom) _proGrab = closest.i; }
                if (_proGrab < 0) _perspectiveQuad = [at, at, at, at]; return true;
            }
            if (_tool is Tool.Patch or Tool.ContentMove)
            { _proMovingSelection = doc.Selection.Path is { IsEmpty: false } path && path.Contains(at.X, at.Y); _proPoints.Clear(); _proPoints.Add(at); return true; }
            if (MarkToolKind is { } kind)
            {
                _markBefore = doc.Marks.LastOrDefault(m => m.Kind == kind && m.Visible && (kind == MarkKind.Slice ? new SKRect((float)m.X, (float)m.Y, (float)(m.X + m.Width), (float)(m.Y + m.Height)).Contains(at)
                    : SKPoint.Distance(at, new SKPoint((float)m.X, (float)m.Y)) < 12 / _canvas.Zoom));
                if (_tool == Tool.Slice) _markBefore = null;
                if (_markBefore is { } found)
                {
                    _selectedMark = found.ID;
                    if (keys.HasFlag(KeyModifiers.Alt)) { DeleteToolAnnotation(); _proGesture = false; return true; }
                    if (_tool == Tool.Count && !keys.HasFlag(KeyModifiers.Shift)) _markBefore = null;
                    else if (clicks > 1 && _tool is Tool.Note or Tool.SliceSelect) { _ = EditAnnotation(); _proGesture = false; return true; }
                    else
                    {
                        _history.Begin("Move Annotation", doc, Selected); _markHistory = true;
                        if (kind == MarkKind.Slice && SKPoint.Distance(at, new SKPoint((float)(found.X + found.Width), (float)(found.Y + found.Height))) < 14 / _canvas.Zoom) _proGrab = 2;
                    }
                }
                if (_markBefore is null && _tool is Tool.ColorSampler or Tool.Count or Tool.Note)
                {
                    if (kind == MarkKind.Sampler && doc.Marks.Count(m => m.Kind == kind) >= 10) { Say("At most 10 color samplers per document."); _proGesture = false; return true; }
                    AddMark(new DocumentMark { Kind = kind, X = at.X, Y = at.Y, Group = Pro.CountGroup, Name = Localize.Text(kind.ToString()) });
                    _proGesture = false; if (_tool == Tool.Note) _ = EditAnnotation();
                }
                ShowAnnotationReadout(); return true;
            }
            return true;
        }
        return false;
    }
    private bool ProfessionalMoved(SKPoint at, KeyModifiers keys)
    {
        if (_tool == Tool.MagneticLasso && _proPoints.Count > 0)
        {
            var snapped = keys.HasFlag(KeyModifiers.Alt) || _magneticSample is null ? at : SelectionBrushEdits.SnapEdge(_magneticSample, at, Pro.MagneticWidth, Pro.MagneticContrast);
            if (SKPoint.Distance(snapped, _proPoints[^1]) >= Pro.MagneticFrequency && _proPoints.Count < 10000) _proPoints.Add(snapped);
            _proEnd = snapped; _canvas.InvalidateVisual(); return true;
        }
        if (!_proGesture) return false;
        _proEnd = at;
        if (_tool is Tool.SelectionBrush or Tool.QuickSelection or Tool.Patch or Tool.ContentMove && _proPoints.Count < 10000) _proPoints.Add(at);
        if (_tool == Tool.PerspectiveCrop && _perspectiveQuad is { } quad)
        {
            if (_proGrab >= 0) quad[_proGrab] = at;
            else { var box = ProBox(); _perspectiveQuad = [new(box.Left, box.Top), new(box.Right, box.Top), new(box.Right, box.Bottom), new(box.Left, box.Bottom)]; }
        }
        if (_markBefore is { } mark && _document is { } doc && _markHistory)
        {
            var delta = at - _proStart;
            var next = _proGrab == 2 ? mark with { Width = Math.Max(1, mark.Width + delta.X), Height = Math.Max(1, mark.Height + delta.Y) } : mark with { X = mark.X + delta.X, Y = mark.Y + delta.Y };
            var index = doc.Marks.FindIndex(m => m.ID == mark.ID); if (index >= 0 && next.IsValid) doc.Marks[index] = next;
            ShowAnnotationReadout();
        }
        _canvas.InvalidateVisual(); return true;
    }
    private bool ProfessionalReleased()
    {
        if (!_proGesture) return false;
        _proGesture = false;
        if (_document is not { } doc) return true;
        if (_markHistory) { _markHistory = false; _history.End(doc, Selected); _markBefore = null; Refresh(); return true; }
        if (_tool is Tool.SelectionBrush or Tool.QuickSelection)
        {
            Edit(ToolCatalog.Title(_tool), () => _tool == Tool.SelectionBrush
                ? SelectionBrushEdits.Paint(doc, _proPoints, _options.Brush.Diameter, _options.Brush.Opacity, _proMode)
                : SelectionBrushEdits.Quick(doc, Pro.SampleAll ? null : Selected, _proPoints, _options.Brush.Diameter, _options.Wand.Tolerance, _proMode));
            _proPoints.Clear();
        }
        else if (_tool == Tool.PerspectiveCrop) Say("Drag the four corners, then press Enter to crop. Zero dimensions use automatic size.");
        else if (_tool == Tool.Frame && ProBox() is { Width: > 1, Height: > 1 } frame)
        {
            Guid? made = null; Edit("Frame Tool", () => (made = LayerWorkflow.MakeContainer(doc, SelectedLayers, LayerContainer.Frame, Localize.Text("Frame"), frame, Pro.EllipseFrame)) is not null); Reselect(made);
        }
        else if (_tool == Tool.Slice && ProBox() is { Width: > 1, Height: > 1 } slice)
            AddMark(new DocumentMark { Kind = MarkKind.Slice, X = slice.Left, Y = slice.Top, Width = slice.Width, Height = slice.Height, Name = "slice-" + (doc.Marks.Count(m => m.Kind == MarkKind.Slice) + 1) });
        else if (_tool == Tool.Ruler)
        {
            Edit("Ruler Tool", () => { doc.Marks.RemoveAll(m => m.Kind == MarkKind.Measure); var measure = new DocumentMark { Kind = MarkKind.Measure, X = _proStart.X, Y = _proStart.Y, Width = _proEnd.X - _proStart.X, Height = _proEnd.Y - _proStart.Y }; doc.Marks.Add(measure); _selectedMark = measure.ID; return true; }); ShowAnnotationReadout();
        }
        else if (_tool == Tool.RedEye && Selected is { } eye)
        {
            var box = ProBox(); if (box.Width < 2 || box.Height < 2) { var r = (float)Math.Max(4, _options.Brush.Diameter / 2); box = new SKRect(_proStart.X - r, _proStart.Y - r, _proStart.X + r, _proStart.Y + r); }
            Edit("Red Eye Tool", () => RepairEdits.RedEye(doc, eye, box, Pro.Pupil, Pro.Darken));
        }
        else if (_tool is Tool.Patch or Tool.ContentMove && Selected is { } id)
        {
            if (!_proMovingSelection)
                Edit("Lasso", () => SelectionEdits.SelectLasso(doc, _proPoints));
            else
            {
                var delta = _proEnd - _proStart;
                Edit(ToolCatalog.Title(_tool), () => _tool == Tool.ContentMove ? RepairEdits.MoveContent(doc, id, (int)delta.X, (int)delta.Y, Pro.ExtendMove)
                    : PatchSelection(doc, id, delta));
            }
            _proPoints.Clear();
        }
        _canvas.InvalidateVisual(); return true;
    }
    private bool PatchSelection(CanvasDocument doc, Guid id, SKPoint delta)
    {
        if (!Pro.PatchDestination) return RepairEdits.Patch(doc, id, delta, true, Pro.SampleAll);
        var previous = doc.Selection; doc.Selection = previous.Translated(delta.X, delta.Y);
        if (RepairEdits.Patch(doc, id, PlacementMesh.Scale(delta, -1), true, Pro.SampleAll)) return true;
        doc.Selection = previous; return false;
    }
    private SKRect ProBox() => new(Math.Min(_proStart.X, _proEnd.X), Math.Min(_proStart.Y, _proEnd.Y), Math.Max(_proStart.X, _proEnd.X), Math.Max(_proStart.Y, _proEnd.Y));
    private bool FinishProfessionalDraft()
    {
        if (_document is not { } doc) return false;
        if (_tool == Tool.MagneticLasso && _proPoints.Count > 0)
        {
            using var shape = new SKPathBuilder(); shape.MoveTo(_proPoints[0]); foreach (var p in _proPoints.Skip(1)) shape.LineTo(p); shape.Close(); using var path = shape.Detach();
            Edit("Magnetic Lasso Tool", () =>
            {
                if (_proMode == SelectionPaintMode.Intersect && doc.Selection.Path is { } previous)
                { var intersection = previous.Op(path, SKPathOp.Intersect); if (intersection is null) return false; doc.Selection = DocumentSelection.FromPath(intersection); return true; }
                return SelectionEdits.Apply(doc, path, _proMode switch { SelectionPaintMode.Add => SelectionMode.Add, SelectionPaintMode.Subtract => SelectionMode.Subtract, _ => SelectionMode.Replace });
            }); CancelProfessionalDraft(); return true;
        }
        if (_tool == Tool.PerspectiveCrop && _perspectiveQuad is { } quad)
        {
            var width = Pro.CropWidth > 0 ? Pro.CropWidth : (int)Math.Round((SKPoint.Distance(quad[0], quad[1]) + SKPoint.Distance(quad[3], quad[2])) / 2);
            var height = Pro.CropHeight > 0 ? Pro.CropHeight : (int)Math.Round((SKPoint.Distance(quad[0], quad[3]) + SKPoint.Distance(quad[1], quad[2])) / 2);
            if (Edit("Perspective Crop Tool", () => PerspectiveCropEdits.Apply(doc, quad, width, height))) { CancelProfessionalDraft(); _canvas.Fit(); }
            else Say("The crop must have four convex corners and fit within document limits."); return true;
        }
        return false;
    }
    private bool CancelProfessionalDraft()
    {
        var had = _proPoints.Count > 0 || _perspectiveQuad is not null || _proGesture;
        if (_markHistory && _document is { } doc)
        {
            if (_markBefore is { } before) { var index = doc.Marks.FindIndex(m => m.ID == before.ID); if (index >= 0) doc.Marks[index] = before; }
            _history.End(doc, Selected); _markHistory = false;
        }
        _proPoints.Clear(); _perspectiveQuad = null; _magneticSample?.Dispose(); _magneticSample = null; _proGesture = false; _markBefore = null; _canvas.InvalidateVisual(); return had;
    }
    private bool ProfessionalKey(KeyEventArgs e)
    {
        if (e.Key is Key.Delete or Key.Back && e.KeyModifiers == KeyModifiers.None)
        {
            if (_tool == Tool.MagneticLasso && _proPoints.Count > 0) { _proPoints.RemoveAt(_proPoints.Count - 1); _canvas.InvalidateVisual(); return true; }
            if (ToolCatalog.IsAnnotation(_tool) && _selectedMark is not null) { DeleteToolAnnotation(); return true; }
        }
        return false;
    }
    private void AddMark(DocumentMark mark)
    {
        if (_document is not { } doc || doc.Marks.Count >= 10000 || !mark.IsValid) return;
        Edit("Add Annotation", () => { doc.Marks.Add(mark); return true; }); _selectedMark = mark.ID; ShowAnnotationReadout();
    }
    private void DeleteToolAnnotation()
    { if (_document is { } doc && _selectedMark is { } id) Edit("Delete Annotation", () => doc.Marks.RemoveAll(m => m.ID == id) > 0); _selectedMark = null; ShowAnnotationReadout(); }
    private async Task EditAnnotation()
    {
        var doc = _document; if (doc?.Marks.FirstOrDefault(m => m.ID == _selectedMark) is not { } mark) return;
        var changed = await MarkDialog.Edit(this, mark); if (changed is null || !ReferenceEquals(doc, _document)) return;
        Edit("Edit Annotation", () => { var i = doc.Marks.FindIndex(m => m.ID == mark.ID); if (i < 0 || doc.Marks[i] == changed) return false; doc.Marks[i] = changed; return true; }); ShowAnnotationReadout();
    }
    private void ShowAnnotationReadout()
    {
        if (_document is not { } doc) return;
        if (doc.Marks.FirstOrDefault(m => m.ID == _selectedMark) is not { } mark) { _optionsBar.ProfessionalReadout(""); return; }
        var text = $"X {mark.X:0.##}  Y {mark.Y:0.##}";
        if (mark.Kind == MarkKind.Measure) text += $"  {Localize.Text("Length")} {Math.Sqrt(mark.Width * mark.Width + mark.Height * mark.Height):0.##} px  ∠ {Math.Atan2(mark.Height, mark.Width) * 180 / Math.PI:0.##}°";
        if (mark.Kind == MarkKind.Slice) text += $"  W {mark.Width:0.##}  H {mark.Height:0.##}";
        if (mark.Kind == MarkKind.Count) text += $"  {Localize.Text("Count")} {doc.Marks.Count(m => m.Kind == MarkKind.Count && m.Group == mark.Group)}";
        if (mark.Kind == MarkKind.Note) text += "  " + mark.Text.Replace('\n', ' ');
        if (mark.Kind == MarkKind.Sampler)
        {
            using var image = SelectionEdits.Sample(doc, Pro.SampleAll ? null : Selected);
            if (image is not null && mark.X >= 0 && mark.Y >= 0 && mark.X < image.Width && mark.Y < image.Height)
            { var c = image.GetPixel((int)mark.X, (int)mark.Y); text += $"  R {c.Red}  G {c.Green}  B {c.Blue}  #{c.Red:X2}{c.Green:X2}{c.Blue:X2}"; }
        }
        _optionsBar.ProfessionalReadout(text);
    }
    private async Task ProfessionalAction(string action)
    {
        var doc = _document; if (doc is null) return;
        switch (action)
        {
            case "Apply (Enter)": FinishProfessionalDraft(); break;
            case "Cancel (Esc)": CancelProfessionalDraft(); break;
            case "Edit Annotation…": await EditAnnotation(); break;
            case "Delete Annotation": DeleteToolAnnotation(); break;
            case "Clear Tool Annotations": Edit(action, () => doc.Marks.RemoveAll(m => m.Kind == MarkToolKind && (m.Kind != MarkKind.Count || m.Group == Pro.CountGroup)) > 0); break;
            case "Toggle Count Group": Edit(action, () => { var group = doc.Marks.Where(m => m.Kind == MarkKind.Count && m.Group == Pro.CountGroup).ToArray(); if (group.Length == 0) return false; var visible = !group.Any(m => m.Visible); for (var i = 0; i < doc.Marks.Count; i++) if (group.Contains(doc.Marks[i])) doc.Marks[i] = doc.Marks[i] with { Visible = visible }; return true; }); break;
            case "Straighten Layer":
                if (doc.Marks.LastOrDefault(m => m.Kind == MarkKind.Measure) is { } line && Selected is { } id && LayerProtection.CanMove(doc, id))
                    Edit(action, () => { var layer = doc.Layers.Single(l => l.ID == id); TransformEdits.SetPlacement(layer, layer.Transform with { Rotation = layer.Transform.Rotation - Math.Atan2(line.Height, line.Width) * 180 / Math.PI }); return true; }); break;
            case "Load Brush": _options.Brush = _options.Brush with { MixerLoad = 1 }; PushBrush(); break;
            case "Clean Brush": _options.Brush = _options.Brush with { MixerLoad = 0, MixerMix = 1 }; PushBrush(); break;
            case "Place Image in Frame…":
                if (PropertyLayer?.Container != LayerContainer.Frame) { Say("Select a frame layer first."); break; }
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text(action), AllowMultiple = false });
                if (files.FirstOrDefault()?.TryGetLocalPath() is { } file && ReferenceEquals(doc, _document)) await ImportDroppedPaths([file]); break;
            case "Slices from Guides":
                var xs = doc.Guides.Where(g => g.Axis == GuideAxis.Vertical && g.Position > 0 && g.Position < doc.Width).Select(g => g.Position).Append(0).Append(doc.Width).Distinct().Order().ToArray();
                var ys = doc.Guides.Where(g => g.Axis == GuideAxis.Horizontal && g.Position > 0 && g.Position < doc.Height).Select(g => g.Position).Append(0).Append(doc.Height).Distinct().Order().ToArray();
                if ((xs.Length - 1) * (ys.Length - 1) + doc.Marks.Count > 10000) break;
                Edit(action, () => { for (var y = 0; y < ys.Length - 1; y++) for (var x = 0; x < xs.Length - 1; x++) doc.Marks.Add(new DocumentMark { Kind = MarkKind.Slice, X = xs[x], Y = ys[y], Width = xs[x + 1] - xs[x], Height = ys[y + 1] - ys[y], Name = $"slice-{y + 1}-{x + 1}" }); return true; }); break;
            case "Divide Slice…":
                if (doc.Marks.FirstOrDefault(m => m.ID == _selectedMark && m.Kind == MarkKind.Slice) is not { } slice) break;
                var answer = await TextPrompt.Ask(this, action, "Columns, rows (1–100)", "2,2"); var parts = answer?.Split(',');
                if (parts?.Length == 2 && int.TryParse(parts[0], out var columns) && int.TryParse(parts[1], out var rows) && columns is > 0 and <= 100 && rows is > 0 and <= 100 && columns * rows + doc.Marks.Count <= 10000 && ReferenceEquals(doc, _document))
                    Edit(action, () => { doc.Marks.Remove(slice); for (var y = 0; y < rows; y++) for (var x = 0; x < columns; x++) doc.Marks.Add(slice with { ID = Guid.NewGuid(), X = slice.X + x * slice.Width / columns, Y = slice.Y + y * slice.Height / rows, Width = slice.Width / columns, Height = slice.Height / rows, Name = $"{slice.Name}-{y + 1}-{x + 1}" }); return true; }); break;
            case "Export Slices…": await ExportSlices(); break;
        }
        _optionsBar.RefreshProfessional(); ShowAnnotationReadout(); _canvas.InvalidateVisual();
    }
    private async Task ExportSlices()
    {
        var doc = _document; if (doc is null) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Localize.Text("Export Slices…"), AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } folder || !ReferenceEquals(doc, _document)) return;
        try
        {
            var exported = new List<object>();
            foreach (var slice in doc.Marks.Where(m => m.Kind == MarkKind.Slice))
            {
                var box = SKRectI.Intersect(new SKRectI((int)Math.Floor(slice.X), (int)Math.Floor(slice.Y), (int)Math.Ceiling(slice.X + slice.Width), (int)Math.Ceiling(slice.Y + slice.Height)), SKRectI.Create(doc.Width, doc.Height));
                if (box.Width <= 0 || box.Height <= 0) continue;
                var name = string.Concat(slice.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)); if (string.IsNullOrWhiteSpace(name)) name = "slice";
                name = name.Trim().TrimEnd('.') + "-" + slice.ID.ToString("N")[..8] + ".png"; var path = Path.Combine(folder, name);
                if (File.Exists(path)) { name = Guid.NewGuid().ToString("N") + ".png"; path = Path.Combine(folder, name); }
                using var image = DocumentRenderer.RenderRegion(doc, box); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100); File.WriteAllBytes(path, encoded.ToArray());
                exported.Add(new { file = name, x = box.Left, y = box.Top, width = box.Width, height = box.Height, slice.Url });
            }
            File.WriteAllText(Path.Combine(folder, "slices-" + Guid.NewGuid().ToString("N")[..8] + ".json"), System.Text.Json.JsonSerializer.Serialize(exported, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })); Say("Slices exported.");
        }
        catch (Exception error) { Say($"Could not export: {error.Message}"); }
    }
    private void DrawProfessionalOverlay(DrawingContext context, Func<SKPoint, Point> screen)
    {
        var pen = new Pen(Skin.AccentBrush, 1.5);
        if (_document is { } doc)
        {
            var counts = new Dictionary<string, int>();
            foreach (var mark in doc.Marks)
            {
                var key = mark.Kind + ":" + (mark.Kind == MarkKind.Count ? mark.Group : "");
                var number = counts[key] = counts.GetValueOrDefault(key) + 1;
                if (!mark.Visible) continue;
                var at = screen(new SKPoint((float)mark.X, (float)mark.Y)); var end = screen(new SKPoint((float)(mark.X + mark.Width), (float)(mark.Y + mark.Height)));
                var brush = new SolidColorBrush(Color.FromUInt32(mark.Color)); var stroke = new Pen(brush, mark.ID == _selectedMark ? 2 : 1);
                if (mark.Kind == MarkKind.Slice)
                {
                    var points = new[] { at, screen(new SKPoint((float)(mark.X + mark.Width), (float)mark.Y)), end, screen(new SKPoint((float)mark.X, (float)(mark.Y + mark.Height))) };
                    for (var i = 0; i < 4; i++) context.DrawLine(stroke, points[i], points[(i + 1) % 4]);
                }
                else if (mark.Kind == MarkKind.Measure) context.DrawLine(stroke, at, end);
                else context.DrawEllipse(Skin.ChromeBrush, stroke, at, 5, 5);
                var label = mark.Kind == MarkKind.Note ? "▤" : number.ToString();
                var text = new FormattedText(label, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, brush);
                context.DrawRectangle(Skin.ChromeBrush, null, new Rect(at + new Vector(7, -8), new Size(text.Width + 4, text.Height))); context.DrawText(text, at + new Vector(9, -8));
            }
            if (_tool == Tool.SelectionBrush && doc.Selection.Path is { } selected)
            {
                // The same document-to-screen matrix the scene uses; no transform is needed for vertices drawn below.
                using var path = new SKPath(selected); var matrix = new SKMatrix((float)_canvas.Zoom, 0, (float)screen(default).X, 0, (float)_canvas.Zoom, (float)screen(default).Y, 0, 0, 1); path.Transform(matrix);
                context.DrawGeometry(new SolidColorBrush(Color.FromArgb(48, 200, 80, 180)), null, StreamGeometry.Parse(path.ToSvgPathData()));
            }
        }
        if (_perspectiveQuad is { } quad)
        {
            for (var i = 0; i < 4; i++) { context.DrawLine(pen, screen(quad[i]), screen(quad[(i + 1) % 4])); context.DrawEllipse(Skin.ChromeBrush, pen, screen(quad[i]), 5, 5); }
            if (PerspectiveCropEdits.UnitToQuad(quad) is { } map) for (var i = 1; i < 3; i++)
            { var t = i / 3f; context.DrawLine(pen, screen(map.MapPoint(t, 0)), screen(map.MapPoint(t, 1))); context.DrawLine(pen, screen(map.MapPoint(0, t)), screen(map.MapPoint(1, t))); }
        }
        if (_proPoints.Count > 0)
        {
            var stroke = _tool is Tool.SelectionBrush or Tool.QuickSelection ? new Pen(new SolidColorBrush(Color.FromArgb(100, 200, 80, 180)), _options.Brush.Diameter * _canvas.Zoom) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round } : pen;
            for (var i = 1; i < _proPoints.Count; i++) context.DrawLine(stroke, screen(_proPoints[i - 1]), screen(_proPoints[i]));
            if (_tool == Tool.MagneticLasso) foreach (var p in _proPoints.Where((_, i) => i % 4 == 0)) context.DrawEllipse(Skin.LabelBrush, pen, screen(p), 2, 2);
        }
        if (_proGesture && _tool is Tool.Slice or Tool.Frame or Tool.RedEye or Tool.Ruler)
        {
            if (_tool == Tool.Ruler) context.DrawLine(pen, screen(_proStart), screen(_proEnd));
            else { var box = ProBox(); var corners = new[] { new SKPoint(box.Left, box.Top), new SKPoint(box.Right, box.Top), new SKPoint(box.Right, box.Bottom), new SKPoint(box.Left, box.Bottom) }; for (var i = 0; i < 4; i++) context.DrawLine(pen, screen(corners[i]), screen(corners[(i + 1) % 4])); }
        }
    }
}
