using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

internal static partial class Program
{
    private static int EditingChecks(string output)
    {
        BuildHeadless().SetupWithoutStarting();
        try { var window = new MainWindow(); window.Show(); window.EditingSelfCheck(output); Console.WriteLine("PASS: cross-document layer/group copy/paste, placement bar/context menu/warp/cancel/undo, new tool families, tonal brushes, adjustment masks, pen curves/anchors, vertical text/type masks and upstream-inspired symbols."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
public sealed partial class MainWindow
{
    internal void EditingSelfCheck(string output)
    {
        void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
        void Pump() { Dispatcher.UIThread.RunJobs(); UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Finish(Task task) { var until = DateTime.UtcNow.AddSeconds(30); while (!task.IsCompleted && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); } Check(task.IsCompleted, "Timed out"); task.GetAwaiter().GetResult(); }
        void Key(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
        { _canvas.Focus(); this.KeyPress(key, modifiers, PhysicalKey.None, null); this.KeyRelease(key, modifiers, PhysicalKey.None, null); Pump(); }
        Point At(SKPoint point) => _canvas.TranslatePoint(_canvas.ToScreen(point), this)!.Value;
        void Drag(SKPoint from, SKPoint to)
        { this.MouseDown(At(from), MouseButton.Left); this.MouseMove(At(to)); this.MouseUp(At(to), MouseButton.Left); Pump(); }
        void Click(SKPoint at) { this.MouseDown(At(at), MouseButton.Left); this.MouseUp(At(at), MouseButton.Left); Pump(); }
        void Save(string suffix) { Pump(); using var frame = this.CaptureRenderedFrame(); frame!.Save(Path.ChangeExtension(output, suffix + ".png"), new PngBitmapEncoderOptions()); }
        var folder = Path.GetDirectoryName(Path.GetFullPath(output))!; Directory.CreateDirectory(folder);
        var doc = LayerPlacement.NewDocument(400, 300)!; AdoptImported(doc, "New"); var original = _open; _canvas.ActualSize(); Pump();
        using var bitmap = Bitmaps.Allocate(Bitmaps.ColorInfo(120, 80)); bitmap.Erase(new SKColor(90, 130, 160)); bitmap.SetPixel(40, 30, SKColors.Orange);
        var file = Path.Combine(folder, "placement-source.png"); using (var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100)) File.WriteAllBytes(file, encoded.ToArray());
        Finish(ImportDroppedPaths([file])); Pump(); Check(_placementTab is not null && _placementBar.IsVisible && !_optionsBar.IsVisible, "No placement toolbar");
        var layer = PropertyLayer!; var initial = layer.Transform;
        _placeNumbers["W"].Value = 125; Pump(); Check(Math.Abs(layer.Transform.Width - 150) < .01, "Placement width percentage failed");
        var menu = new ContextMenu(); BuildPlacementMenu(menu); Check(menu.Items.OfType<MenuItem>().Count() == 20, "Incomplete placement context menu");
        foreach (var mode in new[] { "Skew", "Perspective", "Distort", "Warp" })
        {
            SetPlacementMode(mode); Pump(); var corner = _placeMesh!.Points[0]; Drag(corner, corner + new SKPoint(-8, -5));
            Check(_placeMesh.Points[0] != corner, "Warp handle did not move: " + mode);
        }
        _canvas.ContextPoint = new SKPoint((float)layer.Transform.CenterX, (float)layer.Transform.CenterY); SplitPlaced(true, true);
        Check(_placeMesh!.Points.Count == 9, "Warp split did not create a 3x3 mesh"); Save("placement");
        Key(Avalonia.Input.Key.Escape); Check(_placementTab is null && _document!.Layers.Count == 1 && _history.UndoCount == 0, "Placement cancel left data or history");
        Finish(ImportDroppedPaths([file])); Key(Avalonia.Input.Key.Enter); Check(_history.UndoName == "Place Images", "Placement did not commit as one history state");
        Undo(); Check(_document!.Layers.Count == 1, "Placement undo failed"); Redo(); Check(_document!.Layers.Count == 2, "Placement redo failed");
        var placed = _document.Layers[^1]; SelectLayerRow(placed.ID); SetTool(Tool.Move);
        Key(Avalonia.Input.Key.C, RawInputModifiers.Control); Check(_layerClipboard is not null, "Ctrl+C did not copy layers");
        AdoptImported(LayerPlacement.NewDocument(450, 330)!, "New"); var other = _open; Pump();
        Key(Avalonia.Input.Key.V, RawInputModifiers.Control); Check(_document!.Layers.Count == 2 && PropertyLayer!.ID != placed.ID && PropertyLayer.Name == placed.Name, "Cross-tab paste failed");
        var pastedID = PropertyLayer!.ID; Check(!ReferenceEquals(PropertyLayer.Asset!.Image, placed.Asset!.Image), "Paste shares source bitmap ownership");
        Copy(); Bring(original); Paste(); Check(_document!.Layers.Count == 3, "Reverse cross-tab paste failed"); Undo();
        Bring(other); Reselect(pastedID); _canvas.ActualSize(); Pump();
        foreach (var (key, expected) in new[] { (Avalonia.Input.Key.E, Tool.Eraser), (Avalonia.Input.Key.E, Tool.BackgroundEraser), (Avalonia.Input.Key.E, Tool.MagicEraser), (Avalonia.Input.Key.O, Tool.Dodge), (Avalonia.Input.Key.O, Tool.Burn), (Avalonia.Input.Key.O, Tool.Sponge), (Avalonia.Input.Key.P, Tool.Pen) })
        { Key(key); Check(_tool == expected, "Tool family shortcut failed: " + expected); }
        SetTool(Tool.Pen); Click(new SKPoint(35, 35)); Drag(new SKPoint(100, 40), new SKPoint(120, 70)); Click(new SKPoint(80, 120)); Key(Avalonia.Input.Key.Enter);
        Check(PropertyLayer?.LiveShape is { Kind: Compositor.Core.Format.ShapeKind.Path }, "Pen did not create editable path");
        SetTool(Tool.AddAnchor); Pump(); var nodes = _pathNodes!.NodeCount; Click(new SKPoint(64, 40)); Check(_pathNodes!.NodeCount == nodes + 1, "Add Anchor tool failed");
        SetTool(Tool.DeleteAnchor); Pump(); Click(new SKPoint(64, 40)); Check(_pathNodes!.NodeCount == nodes, "Delete Anchor tool failed");
        SetTool(Tool.ConvertAnchor); Pump(); var point = _pathNodes!.Subpaths[0].Nodes[1].Point;
        var selectedPath = PropertyLayer!; var handle = selectedPath.Transform.Point(point.X, point.Y);
        var historyCount = _history.UndoCount; Drag(handle, handle + new SKPoint(8, 12));
        Check(_pathNodes!.Subpaths[0].Nodes[1].Out is not null && _history.UndoCount == historyCount + 1, "Convert Anchor drag failed");
        SetTool(Tool.FreeformPen); Drag(new SKPoint(50, 140), new SKPoint(140, 180));
        Check(PropertyLayer?.LiveShape is not null && _pen is null, "Freeform path was not committed on release");
        SetTool(Tool.CurvaturePen); Click(new SKPoint(45, 45)); Click(new SKPoint(70, 90)); Click(new SKPoint(125, 60));
        Check(_pen!.Subpaths[0].Nodes[1].In is not null, "Curvature pen has no smooth control point");
        var pathCount = _document!.Layers.Count; Bring(original); Check(_pen is null && other.Document!.Layers.Count == pathCount + 1, "Pen draft leaked between documents"); Bring(other); Pump();
        Reselect(pastedID); SetTool(Tool.PatternStamp); _options.Brush = _options.Brush with { Diameter = 20, Red = 1, Green = 0, Blue = 0 }; PushBrush();
        var at = PropertyLayer!.Transform.Point(.5, .5); Drag(at, at + new SKPoint(8, 0)); Check(_history.UndoName == "Pattern Stamp Tool", "Pattern stamp did not paint");
        Undo(); Reselect(pastedID); SetTool(Tool.AdjustmentBrush); Drag(at, at + new SKPoint(8, 0));
        Check(PropertyLayer?.Adjustment is not null && PropertyLayer.Mask is not null, "Adjustment brush did not create masked adjustment");
        SetTool(Tool.VerticalType); TypeHere(new SKPoint(220, 25)); TypedText("中文竖排"); CommitText();
        Check(PropertyLayer?.LiveText?.Vertical == true, "Vertical text is not editable");
        SetTool(Tool.TypeMask); var count = _document!.Layers.Count; TypeHere(new SKPoint(190, 90)); TypedText("MASK"); CommitText();
        Check(_document.Layers.Count == count && _document.Selection.Path is not null, "Type Mask left a text layer or no selection");
        SetTool(Tool.VerticalTypeMask); TypeHere(new SKPoint(270, 30)); TypedText("蒙版"); CommitText();
        Check(_document.Layers.Count == count && _document.Selection.Path is not null, "Vertical type mask failed");
        foreach (var tool in Enum.GetValues<Tool>())
        {
            SetTool(tool); Pump(); Check(_rail.Marked == tool, "Unreachable tool: " + tool);
            Check(!Localize.IsChinese || Localize.Text(ToolCatalog.Title(tool)) != ToolCatalog.Title(tool), "Untranslated tool " + tool);
        }
        SetTool(Tool.Pen); Save("tools");
        var dialog = new ToolbarDialog(_rail.Layout); dialog.Show(this); Pump();
        using (var shot = dialog.CaptureRenderedFrame()) shot!.Save(Path.ChangeExtension(output, "toolbar.png"), new PngBitmapEncoderOptions()); dialog.Close();
        File.Delete(file);
    }
}
