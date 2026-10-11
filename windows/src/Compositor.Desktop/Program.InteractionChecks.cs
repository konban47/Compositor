using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

internal static partial class Program
{
    private static int InteractionChecks(string output)
    {
        BuildHeadless().SetupWithoutStarting();
        var folder = Path.Combine(Path.GetTempPath(), "compositor-interaction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var window = new MainWindow(); window.Show();
            window.InteractionSelfCheck(folder);
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            frame!.Save(output, new PngBitmapEncoderOptions());
            Console.WriteLine("PASS: selected-layer move, hidden handles, canvas hit selection, Hand pan, eyes, row drag/undo, Delete/undo, marching ants, drop import, Last Filter and Chinese labels.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Directory.Delete(folder, recursive: true); }
    }
}

public sealed partial class MainWindow
{
    internal void InteractionSelfCheck(string folder)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Finish(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
            Check(task.IsCompleted, "Import did not finish."); task.GetAwaiter().GetResult();
        }
        static ImageLayer Make(string name, float x, float y, SKColor color)
        {
            var bitmap = new SKBitmap(Bitmaps.ColorInfo(80, 80)); bitmap.Erase(color);
            return new ImageLayer(Guid.NewGuid(), ImportedImage.Create(bitmap, name), new LayerTransform(x, y, 80, 80), name);
        }
        var document = new CanvasDocument(Guid.NewGuid(), 320, 240);
        var first = Make("红色方块", 25, 25, SKColors.IndianRed);
        var second = Make("蓝色方块", 160, 110, SKColors.CornflowerBlue);
        document.Layers.AddRange([first, second]); AdoptImported(document, "Import Image");
        SetTool(Tool.Move); _snappingOn = false; UpdateLayout(); _canvas.Fit();
        Point Aim(SKPoint point) => _canvas.TranslatePoint(_canvas.InView(point), this)!.Value;
        // Change the selected row AFTER entering Move: this caught the stale transform-box bug.
        SelectLayerRow(first.ID); UpdateLayout();
        var viewport = _canvas.Viewport; var other = second.Transform; var before = first.Transform;
        Drag(Aim(new SKPoint(60, 60)), Aim(new SKPoint(75, 70)));
        Check(Math.Abs(first.Transform.X - before.X - 15) < 0.01 && Math.Abs(first.Transform.Y - before.Y - 10) < 0.01, "Selected object did not move by the drag delta.");
        Check(second.Transform == other && _canvas.Viewport == viewport, "Move shifted another object or the canvas.");
        Undo(); first = document.Layers.Single(l => l.ID == first.ID); second = document.Layers.Single(l => l.ID == second.ID);
        SelectLayerRow(first.ID); _transformShown = false; PushViewSwitches();
        Drag(Aim(new SKPoint(60, 60)), Aim(new SKPoint(70, 60)));
        Check(Math.Abs(first.Transform.X - before.X - 10) < .01 && _canvas.Viewport == viewport, "Hiding handles disabled Move or panned.");
        Undo(); _transformShown = true; PushViewSwitches();
        SelectLayerRow(first.ID);
        Drag(Aim(new SKPoint(195, 145)), Aim(new SKPoint(203, 151)));
        Check(Selected == second.ID && _canvas.Viewport == viewport, "Canvas click did not select the other object.");
        Undo();
        SetTool(Tool.Pan); Drag(Aim(new SKPoint(140, 80)), Aim(new SKPoint(150, 80)));
        Check(_canvas.Viewport != viewport, "Hand did not pan."); _canvas.RestoreViewport(viewport);
        SetTool(Tool.Move); SelectLayerRow(first.ID); UpdateLayout();
        var row = _layers.Items.OfType<ListBoxItem>().Single(item => Equals(item.Tag, first.ID));
        var eye = row.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Tag, "visibility"));
        var eyePoint = eye.TranslatePoint(new Point(eye.Bounds.Width / 2, eye.Bounds.Height / 2), this)!.Value;
        this.MouseDown(eyePoint, MouseButton.Left); this.MouseUp(eyePoint, MouseButton.Left);
        Check(!document.Layers.Single(l => l.ID == first.ID).IsVisible, "Eye did not hide layer.");
        Undo(); Check(document.Layers.Single(l => l.ID == first.ID).IsVisible, "Visibility did not undo.");
        UpdateLayout();
        row = _layers.Items.OfType<ListBoxItem>().Single(item => Equals(item.Tag, first.ID));
        var target = _layers.Items.OfType<ListBoxItem>().Single(item => Equals(item.Tag, second.ID));
        var from = row.TranslatePoint(new Point(95, row.Bounds.Height / 2), this)!.Value;
        var to = target.TranslatePoint(new Point(95, 3), this)!.Value;
        Drag(from, to);
        Check(document.Layers[^1].ID == first.ID, "Layer row drag did not reorder.");
        Undo(); Check(document.Layers[^1].ID == second.ID, "Layer reorder did not undo.");
        SelectLayerRow(first.ID); SetTool(Tool.Marquee);
        Drag(Aim(new SKPoint(40, 40)), Aim(new SKPoint(65, 65)));
        using (var initial = this.CaptureRenderedFrame()) { }
        var composites = _canvas.CompositeRenderCount;
        var phase = _canvas.AntsPhase; _canvas.AdvanceAnts(); Check(phase != _canvas.AntsPhase, "Selection outline did not animate.");
        using (var nextFrame = this.CaptureRenderedFrame()) { }
        Check(_canvas.CompositeRenderCount == composites, "Marching ants recomposited the document instead of its overlay.");
        var count = document.Layers.Count;
        this.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        first = document.Layers.Single(l => l.ID == first.ID);
        Check(document.Layers.Count == count && first.Asset!.Image.GetPixel(25, 25).Alpha == 0, "Delete did not clear selected pixels.");
        Check(first.Asset!.Image.GetPixel(3, 3).Alpha == 255 && document.Layers.Single(l => l.ID == second.ID).Asset!.Image.GetPixel(25, 25).Alpha == 255, "Delete changed pixels outside its target.");
        Undo(); Check(document.Layers.Single(l => l.ID == first.ID).Asset!.Image.GetPixel(25, 25).Alpha == 255, "Clear did not undo.");
        SelectionEdits.Deselect(document);
        var imported = Path.Combine(folder, "桌面拖入.png");
        using (var png = document.Layers[0].Asset!.Image.Encode(SKEncodedImageFormat.Png, 100))
        using (var stream = File.Create(imported)) png.SaveTo(stream);
        Check(DragDrop.GetAllowDrop(_canvas), "Canvas does not accept shell drops.");
        var storageTask = StorageProvider.TryGetFileFromPathAsync(new Uri(imported));
        Finish(storageTask);
        var storageFile = storageTask.Result ?? throw new InvalidOperationException("Cannot prepare shell file transfer.");
        using (var transfer = new DataTransfer())
        {
            transfer.Add(DataTransferItem.CreateFile(storageFile));
            transfer.Add(DataTransferItem.CreateFile(storageFile));
            var over = new DragEventArgs(DragDrop.DragOverEvent, transfer, _canvas, new Point(50, 50), KeyModifiers.None);
            _canvas.RaiseEvent(over);
            Check(over.DragEffects == DragDropEffects.Copy, "Shell drag was not accepted as Copy.");
            var drop = new DragEventArgs(DragDrop.DropEvent, transfer, _canvas, new Point(50, 50), KeyModifiers.None);
            _canvas.RaiseEvent(drop);
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (_importingDrop && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
            Check(!_importingDrop && drop.Handled, "Shell drop did not finish.");
        }
        Check(document.Layers.Count == count + 2 && document.Layers[^1].Name == "桌面拖入", "Dropped files did not become image layers.");
        Check(_placementTab is not null, "Drop did not enter placement mode."); FinishPlacement(true); Undo();
        SelectLayerRow(first.ID);
        var applying = ApplyFilter(FilterKind.Vignette);
        Dispatcher.UIThread.RunJobs();
        var filter = OwnedWindows.OfType<FilterDialog>().Single();
        filter.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, Localize.Text("Apply")))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Finish(applying);
        Check(_lastFilterName == "Vignette", "Applying a filter did not remember it.");
        var cancelTask = ApplyFilter(FilterKind.GaussianBlur);
        Dispatcher.UIThread.RunJobs();
        OwnedWindows.OfType<FilterDialog>().Single().Close(); Finish(cancelTask);
        Check(_lastFilterName == "Vignette", "Cancel replaced the last filter.");
        var ink = InkOf(document.Layers.Single(l => l.ID == first.ID).Asset!.Image);
        this.KeyPress(Key.F, RawInputModifiers.Control | RawInputModifiers.Alt, PhysicalKey.F, "f");
        Check(InkOf(document.Layers.Single(l => l.ID == first.ID).Asset!.Image) != ink, "Last Filter shortcut did not apply.");
        Undo(); Check(InkOf(document.Layers.Single(l => l.ID == first.ID).Asset!.Image) == ink, "Last Filter did not undo.");
        if (Localize.IsChinese)
        {
            SetTool(Tool.Brush); UpdateLayout();
            foreach (var button in _optionsBar.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible && button.Content is string))
                Check(!System.Text.RegularExpressions.Regex.IsMatch((string)button.Content!, @"Size|Hardness|Radius|Width|Tolerance|Sample|Opacity"), "Untranslated numeric tool button: " + button.Content);
            foreach (var tool in Enum.GetValues<Tool>())
            {
                var tip = ToolTip.GetTip(_rail.ButtonFor(tool)!)?.ToString() ?? "";
                Check(System.Text.RegularExpressions.Regex.IsMatch(tip, @"[\u4e00-\u9fff]"), "Untranslated tool tip: " + tip);
            }
            Check(Localize.Format($"Size {23:0}") == "大小 23", "Numeric label localization failed.");
            Check(Localize.Text("Saved a_b.comp") == "已保存 a_b.comp", "Localization changed underscores in a file name.");
            Check(Localize.Text("Vector shape was rasterized to pixels.").Contains("栅格化"), "PSD note was not translated.");
        }
        SetTool(Tool.Marquee); SelectionEdits.Select(document, SKRectI.Create(40, 40, 25, 25)); Refresh();
    }
}
