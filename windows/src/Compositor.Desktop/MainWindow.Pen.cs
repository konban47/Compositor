using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private SKPoint? _penHover;
    private int _penSelected = -1;
    private bool _penNodeDrag, _editingWorkPath;
    private readonly HashSet<int> _penCorners = [];
    private SKBitmap? _penSample;
    private ContextMenu? _penMenu;
    private Flyout? _penFlyout;
    private bool PenClosing(SKPoint at) => _pen?.Subpaths.FirstOrDefault() is { Nodes.Count: > 2 } contour
        && SKPoint.Distance(contour.Nodes[0].Point, at) * _canvas.Zoom <= 10;

    private void PenHover(SKPoint at, KeyModifiers keys)
    {
        _penHover = ToolCatalog.IsPen(_tool) ? at : null;
        _canvas.ClosePathHint = ToolCatalog.IsPen(_tool) && PenClosing(at) && (!_penDrawing || _tool == Tool.FreeformPen);
        if (ToolCatalog.IsPen(_tool)) _canvas.InvalidateOverlay();
    }

    private bool PenPressed(SKPoint at, KeyModifiers keys, int clicks)
    {
        if (_document is not { } doc || _open.ActiveAlpha is not null) return true;
        if (_options.Pen.AlignEdges && _options.Pen.Output == PenOutput.Shape) at = new(MathF.Round(at.X), MathF.Round(at.Y));
        if (_pen is null && _tool != Tool.FreeformPen && _pathNodes is { } existing && _canvas.DocumentToPath is { } unmap && _canvas.PathToDocument is { } map)
        {
            if (existing.Grabs().Any(g => g.Handle && SKPoint.Distance(map(g.Point), at) * _canvas.Zoom <= 7)) return false;
            var nearest = existing.Grabs().Where(g => !g.Handle).MinBy(g => SKPoint.Distance(map(g.Point), at));
            var hit = existing.NodeCount > 0 && SKPoint.Distance(map(nearest.Point), at) * _canvas.Zoom <= 8;
            if (hit && _tool == Tool.CurvaturePen && clicks > 1)
            {
                Edit("Convert Anchor Point", () =>
                {
                    var sub = existing.Subpaths[nearest.Subpath]; var node = sub.Nodes[nearest.Node];
                    if (node.In is not null || node.Out is not null) node.In = node.Out = null;
                    else
                    {
                        var before = sub.Nodes[(nearest.Node + sub.Nodes.Count - 1) % sub.Nodes.Count].Point;
                        var after = sub.Nodes[(nearest.Node + 1) % sub.Nodes.Count].Point;
                        var tangent = PlacementMesh.Scale(after - before, 1f / 6);
                        node.In = node.Point - tangent; node.Out = node.Point + tangent;
                    }
                    return SaveEditablePath(existing.ToSvg());
                }); SyncPathNodes(); return true;
            }
            if (hit && (_tool != Tool.Pen || !_options.Pen.AutoAddDelete || keys.HasFlag(KeyModifiers.Control))) return false;
            if (_tool == Tool.Pen && _options.Pen.AutoAddDelete && !keys.HasFlag(KeyModifiers.Control))
            {
                if (hit) { EditPathNode(nearest.Subpath, nearest.Node, false); return true; }
                // Measure the distance in screen pixels even when the selected shape is transformed.
                var screenPath = PathNodes.Parse(existing.ToSvg())!;
                foreach (var n in screenPath.Subpaths.SelectMany(s => s.Nodes)) { n.Point = map(n.Point); if (n.In is { } i) n.In = map(i); if (n.Out is { } o) n.Out = map(o); }
                if (PenPaths.DistanceToPath(screenPath, at) * _canvas.Zoom <= 6)
                {
                    Edit("Add Anchor Point", () => existing.InsertNearest(unmap(at)) && SaveEditablePath(existing.ToSvg())); SyncPathNodes(); return true;
                }
            }
        }
        _pen ??= new PathNodes();
        if (_pen.Subpaths.Count == 0) { _pen.Subpaths.Add(new PathNodes.Subpath()); _penCorners.Clear(); }
        var contour = _pen.Subpaths[0]; var nodes = contour.Nodes;
        if (PenClosing(at)) { contour.Closed = true; if (_tool == Tool.CurvaturePen) SmoothPen(); FinishPen(); return true; }
        var index = nodes.FindIndex(n => SKPoint.Distance(n.Point, at) * _canvas.Zoom <= 7);
        if (index >= 0 && _tool != Tool.FreeformPen)
        {
            _penSelected = index;
            if (_tool == Tool.CurvaturePen && clicks > 1) { if (!_penCorners.Add(index)) _penCorners.Remove(index); SmoothPen(); }
            _penNodeDrag = true; _penDrawing = true; _penLast = at; _canvas.InvalidateOverlay(); return true;
        }
        if (clicks > 1 && _tool == Tool.Pen && nodes.Count > 1) { FinishPen(); return true; }
        nodes.Add(new PathNodes.Node { Point = at }); _penSelected = nodes.Count - 1; _penLast = at; _penDrawing = true; _penNodeDrag = false;
        if (_tool == Tool.FreeformPen && _options.Pen.Magnetic) { _penSample?.Dispose(); _penSample = SelectionEdits.Sample(doc, null); }
        if (_tool == Tool.CurvaturePen) SmoothPen();
        _optionsBar.RefreshPenAvailability(); _canvas.InvalidateOverlay(); return true;
    }

    private void PenMoved(SKPoint at, KeyModifiers keys)
    {
        if (!_penDrawing || _pen?.Subpaths.FirstOrDefault() is not { } contour) return;
        if (_options.Pen.AlignEdges && _options.Pen.Output == PenOutput.Shape) at = new(MathF.Round(at.X), MathF.Round(at.Y));
        if (_penNodeDrag && _penSelected >= 0)
        {
            _pen.MoveNode(0, _penSelected, at - _penLast); _penLast = at;
            if (_tool == Tool.CurvaturePen) SmoothPen();
        }
        else if (_tool == Tool.FreeformPen)
        {
            var p = _options.Pen;
            if (p.Magnetic && _penSample is { } sample && !keys.HasFlag(KeyModifiers.Alt))
                at = SelectionBrushEdits.SnapEdge(sample, at, p.MagneticWidth * (p.Pressure ? _canvas.PointerPressure : 1), p.MagneticContrast * 2.55);
            var spacing = p.Magnetic ? Math.Clamp(10 - p.MagneticFrequency / 11, 1, 10) : 2;
            if (SKPoint.Distance(at, _penLast) * _canvas.Zoom < spacing || contour.Nodes.Count >= 4000) { PenHover(at, keys); return; }
            contour.Nodes.Add(new PathNodes.Node { Point = at }); _penLast = at; _penSelected = contour.Nodes.Count - 1;
        }
        else if (_tool == Tool.Pen)
        {
            var node = contour.Nodes[^1]; var delta = at - node.Point;
            if (SKPoint.Distance(at, node.Point) * _canvas.Zoom > 2) { node.Out = at; node.In = keys.HasFlag(KeyModifiers.Alt) ? null : node.Point - delta; }
        }
        PenHover(at, keys); _canvas.InvalidateOverlay();
    }

    private void PenReleased()
    {
        _penDrawing = false; _penNodeDrag = false;
        if (_tool == Tool.FreeformPen && _pen?.Subpaths.FirstOrDefault() is { } contour)
        {
            var closed = contour.Nodes.Count > 3 && PenClosing(_penLast);
            var points = contour.Nodes.Select(n => n.Point).ToList(); if (closed) points.RemoveAt(points.Count - 1);
            _pen = PenPaths.Fit(points, _options.Pen.CurveFit, closed); FinishPen();
        }
        _optionsBar.RefreshPenAvailability();
    }

    private string? CurrentPenSvg()
    {
        if (_pen is { NodeCount: > 1 } draft) return draft.ToSvg();
        if (_document is not { } doc) return null;
        if (_options.Pen.Output == PenOutput.Path && doc.WorkPath is { } work) return work;
        if (PropertyLayer is { LiveShape: { } shape } layer)
        {
            using var path = ShapeOutline(layer, shape); path.Transform(BrushEdits.PixelToDocument(layer.Transform, 1, 1)); return path.ToSvgPathData();
        }
        if (PropertyLayer is { Mask: { VectorPath: { } svg } mask } masked)
        {
            using var path = SKPath.ParseSvgPathData(svg); if (path is null) return null;
            path.FillType = mask.VectorEvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
            path.Transform(BrushEdits.PixelToDocument(masked.MaskTransform, mask.Asset.Width, mask.Asset.Height)); return PenPaths.WindingSvg(path);
        }
        return doc.WorkPath;
    }
    private static SKPath ShapeOutline(ImageLayer layer, LayerShapeStyle shape)
    {
        if (shape.Path is { } svg && SKPath.ParseSvgPathData(svg) is { } path) return path;
        using var builder = new SKPathBuilder();
        if (shape.Kind == ShapeKind.Ellipse) builder.AddOval(SKRect.Create(1, 1));
        else { using var rectangle = SmoothCorners.Rectangle(SKRect.Create((float)layer.Transform.Width, (float)layer.Transform.Height), shape.CornerRadius, shape.Smoothing == true); rectangle.Transform(SKMatrix.CreateScale((float)(1 / layer.Transform.Width), (float)(1 / layer.Transform.Height))); builder.AddPath(rectangle); }
        return builder.Detach();
    }

    private bool CommitPenPath(string svg)
    {
        if (_document is not { } doc) return false;
        if (_options.Pen.Output == PenOutput.Path)
        {
            var next = PenPaths.Combine(doc.WorkPath, svg, _options.Pen.Operation);
            if (next is null || next == doc.WorkPath) return false;
            doc.WorkPath = next.Length == 0 ? null : next; return true;
        }
        return CreatePenShape(svg, _options.Pen.Operation);
    }

    private Guid? _madePenShape;
    private bool CreatePenShape(string svg, PathOperation operation = PathOperation.New)
    {
        if (_document is not { } doc) return false;
        if (operation != PathOperation.New && PropertyLayer is { LiveShape: { } style } layer)
        {
            if (!LayerProtection.CanPaint(doc, layer.ID) || !LayerProtection.CanMove(doc, layer.ID)) return false;
            using var old = ShapeOutline(layer, style); var transform = BrushEdits.PixelToDocument(layer.Transform, 1, 1); old.Transform(transform);
            var combined = PenPaths.Combine(old.ToSvgPathData(), svg, operation); if (combined is null) return false;
            using var result = SKPath.ParseSvgPathData(combined); if (result is null) return false;
            if (result.IsEmpty)
            {
                var empty = Bitmaps.Allocate(Bitmaps.ColorInfo(1, 1)); empty.Erase(SKColors.Transparent);
                var replacement = new LayerShapeStyle { Kind = ShapeKind.Path, Path = "M0 0", Red = style.Red, Green = style.Green, Blue = style.Blue };
                layer.Asset = ImportedImage.Create(empty, layer.Name); layer.Shape = new LayerShape(replacement, empty); return true;
            }
            if (!transform.TryInvert(out var inverse)) return false; result.Transform(inverse);
            return ShapeEdits.SetPath(doc, layer.ID, result.ToSvgPathData());
        }
        using var path = SKPath.ParseSvgPathData(svg); if (path is null || path.IsEmpty) return false;
        var bounds = path.TightBounds; var box = SKRectI.Ceiling(bounds); var closed = PathNodes.Parse(svg)?.Subpaths.All(s => s.Closed) == true;
        var stroke = closed ? _options.Pen.Stroke : Math.Max(1, _options.Pen.Stroke);
        if (stroke > 0) box.Inflate((int)Math.Ceiling(stroke / 2), (int)Math.Ceiling(stroke / 2));
        box.Right = Math.Max(box.Right, box.Left + 1); box.Bottom = Math.Max(box.Bottom, box.Top + 1);
        path.Transform(new SKMatrix(1f / box.Width, 0, -box.Left / (float)box.Width, 0, 1f / box.Height, -box.Top / (float)box.Height, 0, 0, 1));
        _madePenShape = ShapeEdits.Add(doc, new LayerShapeStyle { Kind = ShapeKind.Path, Path = path.ToSvgPathData(), Red = _options.Brush.Red, Green = _options.Brush.Green, Blue = _options.Brush.Blue, LineWidth = stroke > 0 ? stroke : null }, box, Selected);
        return _madePenShape is not null;
    }

    private bool PathToSelection(double feather = 0, bool antialias = true, Compositor.Core.Document.SelectionMode mode = Compositor.Core.Document.SelectionMode.Replace)
    {
        if (_canvas.TextEditing || _document is not { } doc || CurrentPenSvg() is not { } svg) return false;
        using var path = SKPath.ParseSvgPathData(svg); if (path is null) return false;
        Edit("Path to Selection", () =>
        {
            if (_pen is not null) doc.WorkPath = svg;
            if (feather <= 0) return SelectionEdits.Apply(doc, path, mode, antialias);
            var selection = DocumentSelection.FromPath(new SKPath(path), antialias, feather);
            // Combine transfers the coverage bitmap to the persistent selection/history.
            var coverage = selection.Coverage(SKRectI.Create(doc.Width, doc.Height));
            return coverage is not null && SelectionBrushEdits.Combine(doc, coverage, 1, (SelectionPaintMode)(int)mode);
        });
        ClearPenDraft(); SyncPathNodes(); return true;
    }

    private bool SaveEditablePath(string svg)
    {
        if (_document is not { } doc) return false;
        if (_editingWorkPath) { if (doc.WorkPath == svg) return false; doc.WorkPath = svg; return true; }
        return _pathLayerID is { } id && ApplyEditedPath(doc, id, svg);
    }
    private void ClearPenDraft()
    {
        _pen = null; _penDrawing = _penNodeDrag = false; _penSelected = -1; _penCorners.Clear(); _canvas.ClosePathHint = false;
        _penSample?.Dispose(); _penSample = null; _optionsBar.RefreshPenAvailability(); _canvas.InvalidateOverlay();
    }

    private void DrawWorkPath(DrawingContext context, Func<SKPoint, Point> screen, Pen pen)
    {
        if (!ToolCatalog.IsPen(_tool) && !ToolCatalog.IsPathEditor(_tool) || !_editingWorkPath || _pathNodes is not { } nodes) return;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open()) foreach (var contour in nodes.Subpaths)
        {
            if (contour.Nodes.Count == 0) continue;
            g.BeginFigure(screen(contour.Nodes[0].Point), false);
            for (var i = 1; i < contour.Nodes.Count; i++) g.CubicBezierTo(screen(contour.Nodes[i - 1].Out ?? contour.Nodes[i - 1].Point), screen(contour.Nodes[i].In ?? contour.Nodes[i].Point), screen(contour.Nodes[i].Point));
            if (contour.Closed && (contour.Nodes[^1].Out is not null || contour.Nodes[0].In is not null)) g.CubicBezierTo(screen(contour.Nodes[^1].Out ?? contour.Nodes[^1].Point), screen(contour.Nodes[0].In ?? contour.Nodes[0].Point), screen(contour.Nodes[0].Point));
            g.EndFigure(contour.Closed);
        }
        context.DrawGeometry(null, pen, geometry);
    }

    private async Task PenAction(string action, Control anchor)
    {
        _penMenu?.Close(); _penFlyout?.Hide();
        var p = _options.Pen;
        if (action == "Fill Color") { ChooseColour(true); return; }
        if (action == "Selection…")
        {
            if (CurrentPenSvg() is null) return;
            var form = new FormDialog("Make Selection", 400, 300); double feather = 0; bool aa = true; int mode = 0;
            form.Number("Feather Radius:", 0, 0, 250, v => feather = v); form.Check("Anti-alias", aa, v => aa = v);
            form.Choice("Operation", ["New Selection", "Add to Selection", "Subtract from Selection", "Intersect with Selection"], 0, v => mode = v);
            if (await form.ShowDialog<bool>(this)) PathToSelection(feather, aa, (Compositor.Core.Document.SelectionMode)mode); return;
        }
        if (action is "Mask" or "Shape")
        {
            if (_document is not { } doc || CurrentPenSvg() is not { } svg) return;
            _madePenShape = null;
            Edit(action == "Mask" ? "Vector Mask from Path" : "Shape from Path", () =>
            {
                if (_pen is not null) doc.WorkPath = svg;
                if (action == "Shape") return CreatePenShape(svg);
                if (Selected is not { } id || !LayerProtection.CanPaint(doc, id)) return false;
                using var path = SKPath.ParseSvgPathData(svg); if (path is null) return false;
                var before = doc.Selection; doc.Selection = DocumentSelection.FromPath(new SKPath(path));
                try { return MaskProperties.FromSelection(doc, id, true); } finally { doc.Selection = before; }
            });
            ClearPenDraft(); if (_madePenShape is { } made) Reselect(made); SyncPathNodes(); return;
        }
        if (action == "Path Operations")
        {
            var menu = new ContextMenu(); var labels = new[] { "New Layer", "Combine Shapes", "Subtract Front Shape", "Intersect Shape Areas", "Exclude Overlapping Shapes" };
            for (var i = 0; i < labels.Length; i++) { var op = (PathOperation)i; var item = new MenuItem { Header = Localize.Text(labels[i]), ToggleType = MenuItemToggleType.Radio, IsChecked = p.Operation == op }; item.Click += (_, _) => { p.Operation = op; OptionsChanged(); }; menu.Items.Add(item); }
            menu.Items.Add(new Separator()); var merge = new MenuItem { Header = Localize.Text("Merge Shape Components"), IsEnabled = CurrentPenSvg() is { } svg && PathNodes.Parse(svg)?.Subpaths.Count > 1 };
            merge.Click += (_, _) => EditPenComponents(nodes => { var merged = PenPaths.Merge(nodes.ToSvg()); return merged is null ? null : PathNodes.Parse(merged); }); menu.Items.Add(merge); _penMenu = menu; menu.Open(anchor); return;
        }
        if (action == "Path Arrangement")
        {
            var menu = new ContextMenu(); var labels = new[] { "Bring Shape to Front", "Bring Shape Forward", "Send Shape Backward", "Send Shape to Back" };
            for (var i = 0; i < labels.Length; i++) { var command = i; var item = new MenuItem { Header = Localize.Text(labels[i]), IsEnabled = CurrentPenSvg() is { } svg && PathNodes.Parse(svg)?.Subpaths.Count > 1 };
                item.Click += (_, _) => EditPenComponents(nodes => { var index = Math.Clamp(_canvas.SelectedNode?.Subpath ?? nodes.Subpaths.Count - 1, 0, nodes.Subpaths.Count - 1); var destination = command switch { 0 => nodes.Subpaths.Count - 1, 1 => Math.Min(index + 1, nodes.Subpaths.Count - 1), 2 => Math.Max(index - 1, 0), _ => 0 }; var sub = nodes.Subpaths[index]; nodes.Subpaths.RemoveAt(index); nodes.Subpaths.Insert(destination, sub); _canvas.SelectedNode = (destination, 0); return nodes; }); menu.Items.Add(item); }
            _penMenu = menu; menu.Open(anchor); return;
        }
        var popup = new ToolSettingsPopup { Changed = () => { EditorPreferences.Current.Save(); OptionsChanged(); } };
        if (action == "Path Alignment")
        {
            popup.Heading("Align:");
            void Row(params string[] labels)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                foreach (var label in labels) { var button = new Button { Content = new InspectorGlyph(label) { Width = 22, Height = 22 }, Width = 34, Height = 30, Tag = label, IsEnabled = CurrentPenSvg() is not null }; ToolTip.SetTip(button, Localize.Text(label)); button.Click += (_, _) => AlignPenComponents(label); row.Children.Add(button); } popup.Children.Add(row);
            }
            Row("Align Left", "Align H Center", "Align Right", "Align Top", "Align V Center", "Align Bottom"); popup.Heading("Distribute:");
            Row("Distribute Top", "Distribute Vertically", "Distribute Bottom", "Distribute Left", "Distribute Horizontally", "Distribute Right");
            popup.Heading("Distribute Spacing:"); Row("Distribute Vertical Spacing", "Distribute Horizontal Spacing");
            popup.Choice("Align To:", ["Selection", "Canvas"], p.AlignmentTarget, v => p.AlignmentTarget = v);
        }
        else if (action == "Path Options")
        {
            popup.Heading("Path Options");
            var colors = new[] { "#1473E6", "#DF48A5", "#E85353", "#59B858", "#FFCF43", "#FFFFFF" };
            var names = new[] { "Default Blue", "Magenta", "Red", "Green", "Yellow", "White" };
            var line = new Border { Height = EditorPreferences.Current.PathWidth, Background = new SolidColorBrush(Color.Parse(EditorPreferences.Current.PathColor)), Margin = new Thickness(3, 0), VerticalAlignment = VerticalAlignment.Center };
            var style = new Grid { ColumnDefinitions = new ColumnDefinitions("60,Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 8, RowSpacing = 5 };
            var preview = new Border { Width = 60, Height = 60, Background = Skin.SurfaceDarkBrush, BorderBrush = Skin.BorderSubtleBrush, BorderThickness = new Thickness(1), Child = line }; Grid.SetRowSpan(preview, 2); style.Children.Add(preview);
            var thickness = new ComboBox { ItemsSource = Enumerable.Range(1, 3).Select(i => Localize.IsChinese ? $"{i} 像素" : $"{i} px").ToArray(), SelectedIndex = Math.Clamp(EditorPreferences.Current.PathWidth - 1, 0, 2), MinWidth = 105 };
            var color = new ComboBox { MinWidth = 105, ItemsSource = colors.Select((value, i) => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { new Border { Width = 13, Height = 13, Background = new SolidColorBrush(Color.Parse(value)), VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = Localize.Text(names[i]), Foreground = Skin.LabelBrush, VerticalAlignment = VerticalAlignment.Center } } }).ToArray(), SelectedIndex = Math.Max(0, Array.IndexOf(colors, EditorPreferences.Current.PathColor)) };
            foreach (var (label, field, row) in new (string, Control, int)[] { ("Thickness:", thickness, 0), ("Color:", color, 1) })
            {
                var text = new TextBlock { Text = Localize.Text(label), VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(text, 1); Grid.SetRow(text, row); style.Children.Add(text);
                Grid.SetColumn(field, 2); Grid.SetRow(field, row); style.Children.Add(field);
            }
            thickness.SelectionChanged += (_, _) => { if (thickness.SelectedIndex < 0) return; EditorPreferences.Current.PathWidth = thickness.SelectedIndex + 1; line.Height = EditorPreferences.Current.PathWidth; popup.Changed?.Invoke(); };
            color.SelectionChanged += (_, _) => { if (color.SelectedIndex < 0) return; EditorPreferences.Current.PathColor = colors[color.SelectedIndex]; line.Background = new SolidColorBrush(Color.Parse(EditorPreferences.Current.PathColor)); popup.Changed?.Invoke(); };
            popup.Children.Add(style);
            if (_tool == Tool.FreeformPen)
            {
                popup.Number("Curve Fit:", p.CurveFit, .5, 10, v => p.CurveFit = v);
                popup.Check("Magnetic", p.Magnetic, v => p.Magnetic = v);
                var magnetic = new ToolSettingsPopup { Margin = default, Changed = popup.Changed, IsEnabled = p.Magnetic };
                magnetic.Number("Width:", p.MagneticWidth, 1, 64, v => p.MagneticWidth = v); magnetic.Number("Contrast:", p.MagneticContrast, 0, 100, v => p.MagneticContrast = v);
                magnetic.Number("Frequency:", p.MagneticFrequency, 1, 100, v => p.MagneticFrequency = v); magnetic.Check("Pen Pressure", p.Pressure, v => p.Pressure = v); popup.Children.Add(magnetic);
                var previous = popup.Changed; popup.Changed = () => { magnetic.IsEnabled = p.Magnetic; previous?.Invoke(); };
            }
            else popup.Check("Rubber Band", p.RubberBand, v => p.RubberBand = v);
        }
        else return;
        _penFlyout = popup.Show(anchor);
    }

    private void EditPenComponents(Func<PathNodes, PathNodes?> edit)
    {
        if (_pen is not null) FinishPen();
        if (CurrentPenSvg() is not { } svg || PathNodes.Parse(svg) is not { } nodes) return;
        Edit("Edit Path Components", () =>
        {
            if (edit(nodes) is not { } result || result.ToSvg() == svg) return false;
            if (_options.Pen.Output == PenOutput.Path) { _document!.WorkPath = result.ToSvg(); return true; }
            if (PropertyLayer is not { LiveShape: not null } layer) return false;
            using var path = SKPath.ParseSvgPathData(result.ToSvg()); if (path is null || !BrushEdits.PixelToDocument(layer.Transform, 1, 1).TryInvert(out var inverse)) return false;
            path.Transform(inverse); return ShapeEdits.SetPath(_document!, layer.ID, path.ToSvgPathData());
        }); SyncPathNodes();
    }

    private void AlignPenComponents(string operation)
    {
        EditPenComponents(nodes =>
        {
            using var temporary = new CanvasDocument(Guid.NewGuid(), _document!.Width, _document.Height);
            var boxes = new List<SKRect>();
            foreach (var contour in nodes.Subpaths)
            {
                var single = new PathNodes(); single.Subpaths.Add(contour); using var path = SKPath.ParseSvgPathData(single.ToSvg()); if (path is null) return null;
                var b = path.TightBounds; boxes.Add(b);
                var pixel = Bitmaps.Allocate(Bitmaps.ColorInfo(1, 1)); pixel.Erase(SKColors.Transparent);
                temporary.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixel, "Path component"), new(b.Left, b.Top, Math.Max(.001, b.Width), Math.Max(.001, b.Height)), "Path component"));
            }
            if (!LayerAlignment.Apply(temporary, temporary.Layers.Select(l => l.ID).ToArray(), operation, _options.Pen.AlignmentTarget == 1 ? AlignmentTarget.Canvas : AlignmentTarget.SelectedLayers)) return null;
            for (var i = 0; i < nodes.Subpaths.Count; i++) { var delta = new SKPoint((float)temporary.Layers[i].Transform.X - boxes[i].Left, (float)temporary.Layers[i].Transform.Y - boxes[i].Top); for (var j = 0; j < nodes.Subpaths[i].Nodes.Count; j++) nodes.MoveNode(i, j, delta); }
            return nodes;
        });
    }
}
