using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

internal static partial class Program
{
    private static int ProfessionalChecks(string output)
    {
        BuildHeadless().SetupWithoutStarting();
        try { var window = new MainWindow(); window.Show(); window.ProfessionalSelfCheck(output); Console.WriteLine("PASS: brush/source/type cursors, workspace grid, shortcut layout, all 69 tools, selection painting/magnetic/quick selection, perspective crop, frame import, slice/measure/count/note/sampler persistence, repair brushes and upstream artwork."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
public sealed partial class MainWindow
{
    internal void ProfessionalSelfCheck(string output)
    {
        void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
        void Pump() { Dispatcher.UIThread.RunJobs(); UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Key(Key key, RawInputModifiers modifiers = RawInputModifiers.None) { _canvas.Focus(); this.KeyPress(key, modifiers, PhysicalKey.None, null); this.KeyRelease(key, modifiers, PhysicalKey.None, null); Pump(); }
        Point At(SKPoint at) => _canvas.TranslatePoint(_canvas.ToScreen(at), this)!.Value;
        void Click(SKPoint at, RawInputModifiers modifiers = RawInputModifiers.None) { this.MouseMove(At(at), modifiers); this.MouseDown(At(at), MouseButton.Left, modifiers); this.MouseUp(At(at), MouseButton.Left, modifiers); Pump(); }
        void Drag(SKPoint from, SKPoint to) { this.MouseMove(At(from)); this.MouseDown(At(from), MouseButton.Left); this.MouseMove(At(to)); this.MouseUp(At(to), MouseButton.Left); Pump(); }
        void Save(string suffix) { Pump(); using var shot = this.CaptureRenderedFrame(); shot!.Save(Path.ChangeExtension(output, suffix + ".png"), new PngBitmapEncoderOptions()); }
        void Finish(Task task) { var until = DateTime.UtcNow.AddSeconds(30); while (!task.IsCompleted && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); } Check(task.IsCompleted, "Timed out"); task.GetAwaiter().GetResult(); }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        using var pixels = Bitmaps.Allocate(Bitmaps.ColorInfo(320, 240)); pixels.Erase(new SKColor(100, 120, 140));
        for (var y = 0; y < 240; y++) for (var x = 160; x < 320; x++) pixels.SetPixel(x, y, new SKColor(230, 150, 50));
        var doc = ImageImporter.NewDocument(ImportedImage.Create(pixels.Copy(), "工具测试")); AdoptImported(doc, "New"); _canvas.ActualSize(); Pump();
        var id = PropertyLayer!.ID; SetTool(Tool.Clone); _options.Brush = _options.Brush with { Diameter = 40, Hardness = 1 }; PushBrush();
        Click(new(40, 40), RawInputModifiers.Alt); Check(_cloneSource is { X: > 39, Y: > 39 } && _canvas.CloneSource is not null, "Alt-click source was not stored");
        this.MouseMove(At(new(90, 90)), RawInputModifiers.Alt); Pump(); Check(_canvas.SourceTargetShowing, "No Alt source target"); Save("source");
        this.MouseMove(At(new(90, 90))); Pump(); Check(_canvas.BrushOutlineShowing && _canvas.ActiveCursorKind == StandardCursorType.None && _canvas.OutlineDiameter == 40, "Brush outline does not match diameter"); Save("cursor");
        _canvas.RestoreViewport((2, 0, 0)); Pump(); Check(_canvas.OutlineDiameter == 80, "Brush outline ignored zoom"); _canvas.ActualSize(); Pump();
        foreach (var tool in Enum.GetValues<Tool>())
        {
            SetTool(tool); Pump(); Check(_rail.Marked == tool, "Unreachable tool " + tool);
            Check(_canvas.ActiveCursorKind == (ToolCatalog.IsType(tool) ? StandardCursorType.Ibeam : ToolCatalog.HasRoundCursor(tool) ? StandardCursorType.None : tool == Tool.Pan ? StandardCursorType.Hand : StandardCursorType.Arrow), "Wrong cursor " + tool);
            Check(!Localize.IsChinese || Localize.Text(ToolCatalog.Title(tool)) != ToolCatalog.Title(tool), "Untranslated tool " + tool);
            using var target = new RenderTargetBitmap(new PixelSize(24, 24)); var symbol = new EditorIcon(tool.ToString()); symbol.Measure(new Size(24, 24)); symbol.Arrange(new Rect(0, 0, 24, 24)); target.Render(symbol);
        }
        SetTool(Tool.MixerBrush); Save("mixer");
        SetTool(Tool.SelectionBrush); Pro.SelectionMode = SelectionPaintMode.New; Drag(new(40, 40), new(80, 40)); Check(doc.Selection.Path?.Contains(60, 40) == true && !doc.Selection.Path.Contains(110, 40), "Selection brush failed");
        _canvas.Grid = new LayoutGrid(); _canvas.InvalidateVisual(); Save("grid"); _canvas.RotateViewTo(25); Save("grid-rotated"); _canvas.RotateViewTo(0); _canvas.Grid = null;
        _canvas.PixelGrid = true; _canvas.RestoreViewport((8, -100, -80)); Save("pixel-grid"); _canvas.PixelGrid = false; _canvas.ActualSize(); Pump();
        SetTool(Tool.QuickSelection); Drag(new(20, 80), new(60, 80)); Check(doc.Selection.Path?.Contains(40, 80) == true && !doc.Selection.Path.Contains(200, 80), "Quick selection crossed edge");
        doc.Selection = DocumentSelection.All; SetTool(Tool.MagneticLasso); Click(new(160, 20)); this.MouseMove(At(new(160, 160))); Pump(); Click(new(250, 160)); Click(new(250, 20)); Key(Avalonia.Input.Key.Enter); Check(doc.Selection.Path is not null && _proPoints.Count == 0, "Magnetic selection did not finish");
        doc.Selection = DocumentSelection.All; SetTool(Tool.Slice); Drag(new(20, 20), new(110, 100)); Check(doc.Marks.Count == 1 && doc.Marks[0].Kind == MarkKind.Slice, "Slice not created");
        SetTool(Tool.SliceSelect); Drag(new(50, 50), new(70, 60)); Check(doc.Marks[0].X == 40 && doc.Marks[0].Y == 30, "Slice move failed"); Undo(); Check(_document!.Marks[0].X == 20, "Slice move undo failed"); doc = _document;
        SetTool(Tool.Count); Click(new(80, 120)); Click(new(100, 120)); Check(doc.Marks.Count(m => m.Kind == MarkKind.Count) == 2, "Count tool failed");
        SetTool(Tool.ColorSampler); Click(new(200, 100)); Check(doc.Marks.Any(m => m.Kind == MarkKind.Sampler), "Color sampler failed");
        SetTool(Tool.Ruler); Drag(new(10, 200), new(110, 200)); Check(doc.Marks.Single(m => m.Kind == MarkKind.Measure).Width == 100, "Ruler failed");
        SetTool(Tool.Note); Click(new(150, 80)); Pump();
        var noteDialog = OwnedWindows.OfType<MarkDialog>().Single();
        noteDialog.GetVisualDescendants().OfType<TextBox>().Single(t => t.AcceptsReturn).Text = "中文注释";
        noteDialog.GetVisualDescendants().OfType<Button>().Single(b => b.IsDefault).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(doc.Marks.Single(m => m.Kind == MarkKind.Note).Text == "中文注释", "Note dialog did not save text");
        var snapshot = ProjectSnapshot.FromDocument(doc); ProjectStore.Validate(snapshot.Manifest); Check(snapshot.Manifest.Version == 17 && snapshot.Manifest.Marks!.Count == 6, "Annotations not serializable");
        Save("annotations"); SetTool(Tool.PerspectiveCrop); Drag(new(10, 10), new(300, 220)); Check(_perspectiveQuad is not null, "No perspective quad");
        Drag(new(10, 10), new(20, 20)); Save("perspective"); Key(Avalonia.Input.Key.Enter); Check(_document!.Width < 320 && _perspectiveQuad is null, "Perspective crop did not apply"); Undo(); doc = _document!;
        SetTool(Tool.Frame); Drag(new(20, 20), new(100, 100)); Check(PropertyLayer?.Container == LayerContainer.Frame, "Frame tool did not create frame");
        var frameID = PropertyLayer!.ID; var file = Path.ChangeExtension(output, "frame-source.png"); using (var encoded = pixels.Encode(SKEncodedImageFormat.Png, 100)) File.WriteAllBytes(file, encoded.ToArray());
        Finish(ImportDroppedPaths([file])); Key(Avalonia.Input.Key.Enter); Check(PropertyLayer!.ParentID == frameID && Math.Abs(PropertyLayer.Transform.Height - 80) < .01, "Frame import was not fitted or parented"); File.Delete(file);
        Save("frame");
        var shortcuts = new ShortcutDialog(new Dictionary<string, ShortcutChord>()); shortcuts.Show(this); Pump(); shortcuts.Width = 580; Pump();
        foreach (var label in shortcuts.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible && t.Bounds.Width > 0 && t.TextWrapping == TextWrapping.Wrap))
            Check(label.Bounds.Height + .5 >= label.TextLayout.Height, "Clipped shortcut label: " + label.Text);
        using (var image = shortcuts.CaptureRenderedFrame()) image!.Save(Path.ChangeExtension(output, "shortcuts.png"), new PngBitmapEncoderOptions()); shortcuts.Close();
        var dialog = new ToolbarDialog(ToolCatalog.Defaults()); dialog.Show(this); Pump(); using (var image = dialog.CaptureRenderedFrame()) image!.Save(Path.ChangeExtension(output, "toolbar.png"), new PngBitmapEncoderOptions()); dialog.Close();
        AdoptImported(LayerPlacement.NewDocument(120, 120)!, "New"); Check(_cloneSource is null && _canvas.CloneSource is null, "Clone source leaked across documents");
    }
}
