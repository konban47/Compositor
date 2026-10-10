using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.Model;
using Compositor.Core.IO;
using SkiaSharp;

namespace Compositor.Desktop;

internal static partial class Program
{
    private static int PanelChecks(string output)
    {
        BuildHeadless().SetupWithoutStarting();
        try
        {
            var window = new MainWindow(); window.Show(); window.PanelSelfCheck(output);
            Console.WriteLine("PASS: mouse-centered wheel zoom, shortcut tooltips, lock/unlock, multi-selection group, collapse, filters, fill, RGB and alpha channels, undo.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}

public sealed partial class MainWindow
{
    internal void PanelSelfCheck(string output)
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static ImageLayer Layer(string name, float x, SKColor color)
        {
            var pixels = new SKBitmap(Bitmaps.ColorInfo(120, 100)); pixels.Erase(color);
            return new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, name), new LayerTransform(x, 50, 120, 100), name);
        }
        var document = new CanvasDocument(Guid.NewGuid(), 400, 260);
        var first = Layer("红色图层", 40, SKColors.IndianRed); var second = Layer("蓝色图层", 210, SKColors.CornflowerBlue);
        document.Layers.AddRange([first, second]); AdoptImported(document, "Import Image"); SetTool(Tool.Move); UpdateLayout(); _canvas.Fit();
        var mouse = new Point(157, 121);
        SKPoint Under() => new((float)(_canvas.Viewport.OriginX + mouse.X / _canvas.Zoom), (float)(_canvas.Viewport.OriginY + mouse.Y / _canvas.Zoom));
        var before = Under();
        this.MouseWheel(_canvas.TranslatePoint(mouse, this)!.Value, new Vector(0, 1));
        Check(Math.Abs(Under().X - before.X) < .0001 && Math.Abs(Under().Y - before.Y) < .0001, "Mouse wheel did not anchor at the cursor.");
        // Direct anchored calculation also covers fractional trackpad deltas and the clamped maximum.
        _canvas.ZoomAt(1.15, mouse); var anchored = Under();
        _canvas.ZoomAt(1 / 1.15, mouse); var returned = Under();
        Check(Math.Abs(anchored.X - returned.X) < .0001 && Math.Abs(anchored.Y - returned.Y) < .0001, "Wheel anchor drifted.");
        _canvas.ZoomAt(10000, mouse); var maximum = Under(); _canvas.ZoomAt(2, mouse);
        Check(Math.Abs(Under().X - maximum.X) < .0001, "Clamped zoom anchor drifted."); _canvas.Fit();
        _canvas.ActualSize(); var zoom = _canvas.Zoom; before = Under();
        var screenMouse = _canvas.TranslatePoint(mouse, this)!.Value;
        this.MouseDown(screenMouse, MouseButton.Middle, RawInputModifiers.Control);
        this.MouseMove(screenMouse - new Vector(0, 100), RawInputModifiers.Control);
        this.MouseUp(screenMouse - new Vector(0, 100), MouseButton.Middle, RawInputModifiers.Control);
        Check(Math.Abs(_canvas.Zoom - zoom * 2) < .0001 && Math.Abs(Under().X - before.X) < .0001, "Ctrl+middle zoom failed.");
        _canvas.NavigatorEnabled = true; _canvas.ZoomBy(2);
        Check(_canvas.NavigatorBounds.Width > 0, "Navigator did not appear above 300%.");
        var navigation = _canvas.TranslatePoint(_canvas.NavigatorBounds.Center, this)!.Value;
        this.MouseDown(navigation, MouseButton.Left); this.MouseUp(navigation, MouseButton.Left);
        Check(Math.Abs(_canvas.Viewport.OriginX + _canvas.Bounds.Width / _canvas.Zoom / 2 - document.Width / 2.0) < .01, "Navigator did not center the viewport.");
        _canvas.Fit();
        Check(ToolTip.GetTip(_rail.ButtonFor(Tool.Move)!)?.ToString()?.Contains("(V)") == true, "Move tooltip has no shortcut.");
        SelectLayerRow(first.ID); _canvas.Focus();
        this.KeyPress(Key.OemPipe, RawInputModifiers.Control, PhysicalKey.Backslash, "\\");
        Check(document.Layers.First(layer => layer.ID == first.ID).Locks.HasFlag(LayerLocks.All), "Ctrl+backslash did not lock.");
        var unchanged = document.Layers.First(layer => layer.ID == first.ID).Asset;
        FillPixels(SKColors.White, "Fill"); Check(ReferenceEquals(unchanged, document.Layers.First(layer => layer.ID == first.ID).Asset), "Locked fill changed pixels.");
        UpdateLayout();
        var row = _layers.Items.OfType<ListBoxItem>().Single(item => Equals(item.Tag, first.ID));
        var unlock = row.GetVisualDescendants().OfType<Button>().Single(button => ToolTip.GetTip(button)?.ToString() == Localize.Text("Unlock Layer"));
        unlock.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Check(document.Layers.First(layer => layer.ID == first.ID).Locks == LayerLocks.None, "Lock icon did not unlock.");
        SelectLayerRow(first.ID); SelectLayerRow(second.ID, true); GroupSelected();
        var group = document.Layers.Single(layer => layer.IsGroup);
        Check(document.Layers.Count(layer => layer.ParentID == group.ID) == 2, "Multi-selection group lost layers.");
        _open.Collapsed.Add(group.ID); ShowLayers(document); Check(_rows.Count == 1, "Collapsed group still shows children.");
        _open.Collapsed.Clear(); ShowLayers(document); Check(_rows.Count == 3, "Expanded group lost children.");
        _layerSearch.Text = "红色"; Dispatcher.UIThread.RunJobs(); Check(_rows.SequenceEqual([first.ID]), "Layer search failed."); _layerSearch.Text = ""; Dispatcher.UIThread.RunJobs();
        SelectLayerRow(first.ID); ToggleLayerLock();
        _layerKind.SelectedIndex = 6; Check(_rows.Contains(first.ID) && _rows.Count == 1, "Locked layer filter failed.");
        _layerKind.SelectedIndex = 0; ToggleLayerLock(); SelectLayerRow(first.ID);
        _fill.Value = 50; Check(document.Layers.First(layer => layer.ID == first.ID).FillOpacity == .5, "Fill control failed.");
        _fill.Value = 100;
        SelectionEdits.Select(document, SKRectI.Create(60, 65, 65, 50));
        NewAlpha(true); Check(document.Channels.Count == 1 && _open.ActiveAlpha is not null, "Save selection as channel failed.");
        var original = document.Layers.First(layer => layer.ID == first.ID).Asset;
        FillPixels(SKColors.Black, "Fill");
        Check(document.Channels[0].Asset.Image.GetPixel(80, 80).Red == 0, "Channel fill missed alpha target.");
        Check(ReferenceEquals(original, document.Layers.First(layer => layer.ID == first.ID).Asset), "Alpha edit changed image layer.");
        Undo(); LoadChannelSelection();
        using var coverage = document.Selection.Coverage(SKRectI.Create(0, 0, 400, 260));
        Check(coverage?.GetPixel(80, 80).Red == 255 && coverage.GetPixel(5, 5).Red == 0, "Channel selection failed.");
        SelectColorChannel(ColorChannels.Red); Check(document.EditChannels == ColorChannels.Red && _canvas.DisplayChannels == ColorChannels.Red, "Red channel shortcut target failed.");
        SelectColorChannel(ColorChannels.RGB); UpdateLayout(); Dispatcher.UIThread.RunJobs();
        using (var frame = this.CaptureRenderedFrame()) frame!.Save(Path.ChangeExtension(output, "channels.png"), new PngBitmapEncoderOptions());
        _panelTabs.SelectedIndex = 0; SelectLayerRow(second.ID); ToggleLayerLock(); SelectLayerRow(group.ID);
        UpdateLayout(); Dispatcher.UIThread.RunJobs(); using (var frame = this.CaptureRenderedFrame()) frame!.Save(output, new PngBitmapEncoderOptions());
        var export = new ExportAsDialog(document, ExportFormat.Png); export.Show(this);
        var rendering = export.RenderPreview(); var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!rendering.IsCompleted && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        Check(rendering.IsCompletedSuccessfully, "Export preview failed to complete.");
        export.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        using (var frame = export.CaptureRenderedFrame()) frame!.Save(Path.ChangeExtension(output, "export.png"), new PngBitmapEncoderOptions());
        var exportButton = export.GetVisualDescendants().OfType<Button>().Single(button => button.Content?.ToString() == Localize.Text("Export"));
        Check(exportButton.IsEnabled, "Export preview did not produce encoded bytes.");
        exportButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Check(export.Result?.Bytes.AsSpan().StartsWith(new byte[] {137,80,78,71}) == true, "Export dialog result is not PNG.");
    }
}
