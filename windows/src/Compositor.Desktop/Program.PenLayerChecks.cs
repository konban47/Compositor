using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

internal static partial class Program
{
    private static int PenLayerChecks(string output)
    {
        EditorPreferences.Current.Theme = 1;
        BuildHeadless().SetupWithoutStarting();
        try { var window = new MainWindow(); window.Show(); window.PenLayerSelfCheck(output); Console.WriteLine("PASS: pen/freeform/curvature options, screen-space closure, blue node selection, handles, work path/history, Ctrl+Enter selection, mask/shape conversion, component operations, layer thumbnail/multiple/hold drag, cancellation, locks and edge scrolling."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}

public sealed partial class MainWindow
{
    internal void PenLayerSelfCheck(string output)
    {
        static void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
        void Pump() { Dispatcher.UIThread.RunJobs(); UpdateLayout(); foreach (var w in OwnedWindows) w.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        Point At(SKPoint p) => _canvas.TranslatePoint(_canvas.ToScreen(p), this)!.Value;
        void Click(SKPoint p) { this.MouseDown(At(p), MouseButton.Left); this.MouseUp(At(p), MouseButton.Left); Pump(); }
        void Key(Key key, RawInputModifiers modifiers = RawInputModifiers.None) { _canvas.Focus(); this.KeyPress(key, modifiers, PhysicalKey.None, null); this.KeyRelease(key, modifiers, PhysicalKey.None, null); Pump(); }
        void ButtonClick(Window owner, Control control)
        {
            var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), owner)!.Value;
            owner.MouseDown(at, MouseButton.Left); owner.MouseUp(at, MouseButton.Left); Pump();
        }
        Button Option(string title) => _optionsBar.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, title));
        void Save(Control control, string suffix)
        {
            Pump(); using var shot = new RenderTargetBitmap(new PixelSize(Math.Max(1, (int)control.Bounds.Width), Math.Max(1, (int)control.Bounds.Height)));
            shot.Render(control); shot.Save(Path.ChangeExtension(output, suffix + ".png"), new PngBitmapEncoderOptions());
        }
        void Fresh()
        {
            CancelPen(); AdoptImported(LayerPlacement.NewDocument(500, 350)!, "Pen and layers");
            _options.Pen.Output = PenOutput.Path; _options.Pen.Operation = PathOperation.New; SetTool(Tool.Pen); _canvas.ActualSize(); Pump();
        }
        Fresh(); Check(!Option("Selection…").IsEnabled, "Selection should require a path.");
        var preset = new ToolOptions { Pen = new PenToolSettings { AutoAddDelete = false, MagneticWidth = 21 } };
        ToolPresetState.CopyOptions(preset, _options); OptionsChanged();
        var auto = _optionsBar.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, Localize.Text("Auto Add/Delete")));
        Check(auto.IsChecked == false, "Applying a pen preset left the toolbar bound to old settings.");
        auto.IsChecked = true; Check(_options.Pen.AutoAddDelete && !preset.Pen.AutoAddDelete, "Toolbar edits changed the saved preset.");
        var presetPath = Path.ChangeExtension(output, "pen-preset.json");
        ToolPresetState.Write(presetPath, [new ToolPresetState { Name = "Pen", Tool = Tool.Pen, Options = preset }]);
        Check(ToolPresetState.Read(presetPath).Single().Options.Pen.MagneticWidth == 21, "Pen preset did not round-trip.");
        preset.Pen.CurveFit = -1; ToolPresetState.Write(presetPath, [new ToolPresetState { Name = "Invalid", Tool = Tool.Pen, Options = preset }]);
        var invalid = false; try { ToolPresetState.Read(presetPath); } catch (InvalidDataException) { invalid = true; } Check(invalid, "Invalid pen preset was accepted.");
        var first = new SKPoint(90, 80); Click(first); Click(new(240, 80)); Click(new(230, 230));
        this.MouseMove(At(new(94, 84))); Pump(); Check(_canvas.ClosePathHint, "Closing circle did not appear near the first node."); Save(this, "closure");
        this.MouseMove(At(new(120, 110))); Pump(); Check(!_canvas.ClosePathHint, "Closing circle remained outside the threshold.");
        _canvas.ZoomBy(2); Pump(); this.MouseMove(At(new(94, 80))); Pump(); Check(_canvas.ClosePathHint, "Close threshold did not follow zoom.");
        this.MouseMove(At(new(97, 80))); Pump(); Check(!_canvas.ClosePathHint, "Close threshold used document pixels.");
        _canvas.ActualSize(); Click(first);
        Check(_pen is null && PathNodes.Parse(_document!.WorkPath!)!.Subpaths[0].Closed && _document.Layers.Count == 1, "Path mode created a layer or failed to close.");
        Key(Avalonia.Input.Key.Enter, RawInputModifiers.Control);
        Check(_document!.Selection.Contains(180, 130) && !_document.Selection.Contains(40, 40), "Ctrl+Enter did not use the path selection.");
        Check(_history.UndoName == "Path to Selection", "Path selection did not create one history state.");
        Undo(); Pump(); Check(_document!.Selection.Path is null && _document.WorkPath is not null, "Selection undo lost the work path."); Redo(); Pump();
        SetTool(Tool.Path); this.MouseDown(At(first), MouseButton.Left); Pump(); Check(_canvas.SelectedNode == (0, 0), "Anchor selection was not retained."); Save(this, "selected-node");
        this.MouseMove(At(new(75, 65)), RawInputModifiers.LeftMouseButton); this.MouseUp(At(new(75, 65)), MouseButton.Left); Pump();
        Check(PathNodes.Parse(_document.WorkPath!)!.Subpaths[0].Nodes[0].Point == new SKPoint(75, 65), "Work path node drag failed."); Undo(); Pump();
        SetTool(Tool.Pen); var count = PathNodes.Parse(_document.WorkPath!)!.NodeCount; Click(new(160, 80));
        Check(PathNodes.Parse(_document.WorkPath!)!.NodeCount == count + 1, "Auto-add missed the path segment."); Click(new(160, 80));
        Check(PathNodes.Parse(_document.WorkPath!)!.NodeCount == count, "Auto-delete missed the inserted anchor.");
        ButtonClick(this, Option("Mask")); Check(PropertyLayer!.Mask?.VectorPath is not null, "Make Mask did not create a vector mask."); Undo(); Pump();
        ButtonClick(this, Option("Shape")); Check(_document!.Layers.Count == 2 && PropertyLayer?.LiveShape is not null, "Make Shape did not create an editable shape."); Undo(); Pump();
        ButtonClick(this, Option("Selection…")); var form = OwnedWindows.OfType<FormDialog>().Single();
        form.GetVisualDescendants().OfType<NumericUpDown>().First().Value = 3;
        ButtonClick(form, form.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, Localize.Text("OK"))));
        using (var coverage = _document!.Selection.Coverage(SKRectI.Create(500, 350))) Check(coverage!.GetPixel(160, 79).Red is > 0 and < 255, "Make Selection ignored feather.");
        foreach (var tool in new[] { Tool.Pen, Tool.FreeformPen, Tool.CurvaturePen })
        {
            SetTool(tool); Pump(); Save(_optionsBar, tool + "-bar");
            var mode = _optionsBar.GetVisualDescendants().OfType<ComboBox>().Single(c => Equals(c.Tag, "pen-mode"));
            Check(mode.Items.Count == 3 && mode.Items[2] is ComboBoxItem { IsEnabled: false }, "Unsupported pixel mode was enabled.");
            ButtonClick(this, Option("Path Operations")); Check(_penMenu!.Items.OfType<MenuItem>().Count() == 6, "Missing boolean operations."); _penMenu.Close();
            ButtonClick(this, Option("Path Arrangement")); Check(_penMenu!.Items.Count == 4, "Missing arrangement actions."); _penMenu.Close();
            ButtonClick(this, Option("Path Alignment")); var popup = (ToolSettingsPopup)_penFlyout!.Content!;
            Check(popup.GetVisualDescendants().OfType<Button>().Count() == 14, "Missing alignment/distribution buttons."); Save(popup, tool + "-align"); _penFlyout.Hide();
            ButtonClick(this, Option("Path Options")); popup = (ToolSettingsPopup)_penFlyout!.Content!;
            if (tool == Tool.FreeformPen)
            {
                var magnet = popup.Children.OfType<CheckBox>().Single(); var fields = popup.Children.OfType<ToolSettingsPopup>().Single();
                Check(!fields.IsEnabled, "Magnetic settings enabled when magnet is off."); magnet.IsChecked = true; Pump(); Check(fields.IsEnabled && _options.Pen.Magnetic, "Magnetic toggle is not connected.");
                fields.GetVisualDescendants().OfType<NumericUpDown>().First().Value = 17; Check(_options.Pen.MagneticWidth == 17, "Magnetic width is not connected."); Save(popup, "freeform-magnetic"); magnet.IsChecked = false;
            }
            else { popup.Children.OfType<CheckBox>().Single().IsChecked = true; Check(_options.Pen.RubberBand, "Rubber band is not connected."); }
            Save(popup, tool + "-settings"); _penFlyout.Hide();
        }
        Fresh(); SetTool(Tool.FreeformPen); this.MouseDown(At(new(60, 180)), MouseButton.Left);
        for (var i = 1; i <= 45; i++) this.MouseMove(At(new(60 + i * 6, 180 + (float)Math.Sin(i / 7.0) * 50)), RawInputModifiers.LeftMouseButton);
        this.MouseUp(At(new(330, 180)), MouseButton.Left); Pump();
        var fitted = PathNodes.Parse(_document!.WorkPath!)!; Check(fitted.NodeCount is > 2 and < 45 && fitted.Subpaths[0].Nodes.Any(n => n.Out is not null), "Freeform did not fit a curve.");
        Fresh(); SetTool(Tool.CurvaturePen); Click(new(70, 80)); Click(new(250, 90)); Click(new(260, 220)); Click(new(80, 240));
        Check(_pen!.Subpaths[0].Nodes[1].Out is not null, "Curvature pen did not create handles."); Click(new(70, 80)); Check(PathNodes.Parse(_document!.WorkPath!)!.Subpaths[0].Closed, "Curvature closure failed.");
        var oldHandle = PathNodes.Parse(_document.WorkPath!)!.Subpaths[0].Nodes[0].Out;
        this.MouseDown(At(new(250, 90)), MouseButton.Left); this.MouseMove(At(new(300, 120)), RawInputModifiers.LeftMouseButton); this.MouseUp(At(new(300, 120)), MouseButton.Left); Pump();
        var curved = PathNodes.Parse(_document.WorkPath!)!.Subpaths[0];
        Check(SKPoint.Distance(curved.Nodes[1].Point, new(300, 120)) < .01 && curved.Nodes[0].Out != oldHandle, "Curvature node drag did not recompute neighboring handles.");
        SetTool(Tool.Pen); _document.WorkPath = "M50 50L90 50L90 90L50 90Z M170 120L210 120L210 160L170 160Z M300 180L340 180L340 220L300 220Z"; SyncPathNodes();
        AlignPenComponents("Align Top"); var aligned = PathNodes.Parse(_document.WorkPath!)!;
        Check(aligned.Subpaths.All(s => Math.Abs(s.Nodes[0].Point.Y - 50) < .01), "Component alignment did not move components.");
        AlignPenComponents("Distribute Horizontal Spacing"); var spaced = PathNodes.Parse(_document.WorkPath!)!;
        Check(Math.Abs(spaced.Subpaths[1].Nodes[0].Point.X - 175) < .01, "Component spacing is wrong.");
        _options.Pen.AlignmentTarget = 1; AlignPenComponents("Align Right"); Check(PathNodes.Parse(_document.WorkPath!)!.Subpaths.All(s => Math.Abs(s.Nodes[1].Point.X - 500) < .01), "Canvas alignment ignored canvas bounds.");
        _canvas.SelectedNode = (0, 0); ButtonClick(this, Option("Path Arrangement")); _penMenu!.Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); _penMenu.Close(); Check(_canvas.SelectedNode == (2, 0), "Component order failed.");
        ButtonClick(this, Option("Path Operations")); _penMenu!.Items.OfType<MenuItem>().Last().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); _penMenu.Close(); Check(PathNodes.Parse(_document.WorkPath!)!.Subpaths.Count == 1, "Merge did not merge overlapping components.");
        Fresh(); _options.Pen.Output = PenOutput.Shape; OptionsChanged(); Click(new(50, 50)); Click(new(200, 50)); Click(new(120, 180)); Click(new(50, 50));
        Check(_document!.Layers.Count == 2 && _document.WorkPath is null && PropertyLayer?.LiveShape?.LineWidth is null, "Shape mode did not make a filled shape.");
        LayerDragSelfCheck(Pump, Save);
        Save(this, "editor");
    }

    private void LayerDragSelfCheck(Action pump, Action<Control, string> save)
    {
        static void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
        Height = 1400; pump(); CancelPen(); var doc = LayerPlacement.NewDocument(500, 350)!; AdoptImported(doc, "Layer dragging"); SetTool(Tool.Move);
        var ids = Enumerable.Range(0, 5).Select(i => ShapeEdits.Add(doc, new LayerShapeStyle { Kind = ShapeKind.Rectangle }, SKRectI.Create(30 + i * 35, 40 + i * 20, 80, 80), null)!.Value).ToArray();
        ShowLayers(doc); SelectLayerRow(ids[0]); pump();
        ListBoxItem Row(Guid id) => _layers.Items.OfType<ListBoxItem>().Single(r => Equals(r.Tag, id));
        Point At(Guid id, double y = .5, bool thumbnail = false)
        {
            var row = Row(id); _layers.ScrollIntoView(row); pump();
            Control target = thumbnail ? row.GetVisualDescendants().OfType<LayerThumbnail>().First() : row;
            return target.TranslatePoint(new Point(thumbnail ? target.Bounds.Width / 2 : 160, target.Bounds.Height * y), this)!.Value;
        }
        var from = At(ids[0], thumbnail: true); var to = At(ids[4], .15);
        this.MouseDown(from, MouseButton.Left); this.MouseMove(to, RawInputModifiers.LeftMouseButton); pump();
        Check(_layerDragging && _dropRow?.Tag is Guid id && id == ids[4] && _layerDragOverlay.LineY is not null, "Thumbnail drag has no insertion preview."); save(this, "layer-drag");
        this.MouseUp(to, MouseButton.Left); pump(); Check(doc.Layers[^1].ID == ids[0], "Thumbnail drag did not reorder: " + string.Join(",", doc.Layers.Select(l => Array.IndexOf(ids, l.ID))) + "; history=" + _history.UndoName);
        Undo(); pump(); doc = _document!; Check(doc.Layers[^1].ID == ids[4], "Layer reorder undo failed.");
        SelectLayerRow(ids[0]); SelectLayerRow(ids[1], true); from = At(ids[0]); to = At(ids[4], .15);
        this.MouseDown(from, MouseButton.Left); this.MouseMove(to, RawInputModifiers.LeftMouseButton); this.MouseUp(to, MouseButton.Left); pump();
        Check(doc.Layers.TakeLast(2).Select(l => l.ID).SequenceEqual(new[] { ids[0], ids[1] }), "Multiple layer order was reversed.");
        Undo(); pump(); doc = _document!; SelectLayerRow(ids[2]); from = At(ids[2]); to = At(ids[4], .15); var order = doc.Layers.Select(l => l.ID).ToArray();
        this.MouseDown(from, MouseButton.Left); _layerPressTime -= 250; LayerDragTick(); Check(_layerDragging, "Holding left button did not begin drag.");
        this.MouseMove(to, RawInputModifiers.LeftMouseButton); this.KeyPress(Avalonia.Input.Key.Escape, RawInputModifiers.None, PhysicalKey.None, null); this.KeyRelease(Avalonia.Input.Key.Escape, RawInputModifiers.None, PhysicalKey.None, null); this.MouseUp(to, MouseButton.Left); pump();
        Check(!_layerDragging && doc.Layers.Select(l => l.ID).SequenceEqual(order), "Escape committed a drag.");
        doc.Layers.Single(l => l.ID == ids[2]).Locks = LayerLocks.Position; ShowLayers(doc); SelectLayerRow(ids[2]); pump(); from = At(ids[2]); to = At(ids[4], .15);
        this.MouseDown(from, MouseButton.Left); this.MouseMove(to, RawInputModifiers.LeftMouseButton); this.MouseUp(to, MouseButton.Left); pump(); Check(doc.Layers.Select(l => l.ID).SequenceEqual(order), "Locked layer moved.");
        doc.Layers.Single(l => l.ID == ids[2]).Locks = LayerLocks.None;
        SelectionEdits.Select(doc, SKRectI.Create(40, 40, 50, 50)); MaskProperties.FromSelection(doc, ids[3], true); ShowLayers(doc); SelectLayerRow(ids[3]); pump();
        var mask = Row(ids[3]).GetVisualDescendants().OfType<LayerThumbnail>().Single(t => Equals(t.Tag, "mask-thumbnail")); var maskAt = mask.TranslatePoint(new Point(10, 10), this)!.Value;
        this.MouseDown(maskAt, MouseButton.Left); this.MouseUp(maskAt, MouseButton.Left); pump(); Check(MaskTarget, "Mask thumbnail no longer selects the mask.");
        from = At(ids[3], thumbnail: true); this.MouseDown(from, MouseButton.Left); this.MouseUp(from, MouseButton.Left); pump(); Check(!MaskTarget, "Image thumbnail no longer selects pixels.");
        for (var i = 0; i < 25; i++) ShapeEdits.Add(doc, new LayerShapeStyle { Kind = ShapeKind.Rectangle }, SKRectI.Create(10, 10, 20, 20), null);
        ShowLayers(doc); var top = doc.Layers[^1].ID; SelectLayerRow(top); pump(); from = At(top); var edge = _layers.TranslatePoint(new Point(160, _layers.Bounds.Height - 5), this)!.Value;
        this.MouseDown(from, MouseButton.Left); this.MouseMove(edge, RawInputModifiers.LeftMouseButton); pump();
        var scrollBefore = _layerDragScroll!.Offset.Y; for (var i = 0; i < 12; i++) LayerDragTick();
        Check(_layerDragScroll.Offset.Y > scrollBefore, "Holding near the list edge did not auto-scroll."); EndLayerDrag(); this.MouseUp(edge, MouseButton.Left); pump();
    }
}
