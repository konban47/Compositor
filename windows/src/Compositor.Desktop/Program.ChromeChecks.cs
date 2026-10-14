using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

internal static partial class Program
{
    private static int ChromeChecks(string output)
    {
        BuildHeadless().SetupWithoutStarting();
        try { var window = new MainWindow(); window.Show(); window.ChromeSelfCheck(output); Console.WriteLine("PASS: compact chrome, narrow one/two-column tools, workspace geometry, option presets, artboard creation/resize/background/undo, ruler tracking, viewport scroll and transform cursors."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
public sealed partial class MainWindow
{
    internal void ChromeSelfCheck(string output)
    {
        static void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
        void Pump() { Dispatcher.UIThread.RunJobs(); UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Save(string suffix, Window? window = null) { Pump(); var view = window ?? this; view.UpdateLayout(); using var image = view.CaptureRenderedFrame(); image!.Save(Path.ChangeExtension(output, suffix + ".png"), new PngBitmapEncoderOptions()); }
        Point At(SKPoint at) => _canvas.TranslatePoint(_canvas.InView(at), this)!.Value;
        var doc = new CanvasDocument(Guid.NewGuid(), 500, 340);
        var pixels = new SKBitmap(80, 60); pixels.Erase(SKColors.CornflowerBlue);
        var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "Object"), new(70, 60, 80, 60), "Object"); doc.Layers.Add(layer);
        AdoptImported(doc, "Chrome Check"); SetTool(Tool.Move); SelectLayerRow(layer.ID); Pump();
        Check(_canvasHorizontal.Parent is DockPanel status && status.Children.Contains(_statusInfo) && status.Children.Contains(_status), "Horizontal scrollbar is not embedded between the bottom status fields.");
        Check(WindowDecorations == WindowDecorations.BorderOnly, "Native window border was lost.");
        Check(this.GetVisualDescendants().OfType<Image>().Any(i => Equals(i.Tag, "project-logo") && i.Source is not null), "Project logo missing from caption.");
        var options = _dockPanels["Options"];
        Check(!options.Slot!.GetVisualDescendants().OfType<Button>().Any(b => b.Tag?.ToString()?.StartsWith("close-panel-") == true), "Options retains title controls.");
        FloatPanel(options); Pump(); Check(options.Floating!.Height == 36, "Options float has a title bar."); Save("options", options.Floating);
        DockToolBack(options); Pump();
        var tools = _dockPanels["Tools"];
        Check(!tools.Slot!.GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Tag, "close-panel-Tools")), "Docked tool rail has a close button.");
        FloatPanel(tools); Pump(); Check(tools.Floating!.Width == 46, "Single-column toolbar is too wide."); Save("tools-one", tools.Floating);
        ToggleToolCollapse(tools); Pump(); Check(_rail.TwoColumns && tools.Floating!.Width == 82, "Expand did not make two columns."); Save("tools-two", tools.Floating);
        var layout = CaptureDockWorkspace(); RestoreDockWorkspace(layout); Pump(); Check(_rail.TwoColumns && tools.Floating!.Width == 82, "Workspace lost toolbar width/columns.");
        DockToolBack(tools); ToggleToolCollapse(tools); Pump();
        _options.Brush = _options.Brush with { Diameter = 37 }; var preset = CaptureToolPreset("Round trip");
        var path = Path.Combine(AppPaths.SettingsDirectory, "chrome-presets.json"); ToolPresetState.Write(path, [preset]);
        var read = ToolPresetState.Read(path).Single(); _options.Brush = _options.Brush with { Diameter = 9 }; ApplyToolPreset(read);
        Check(_options.Brush.Diameter == 37 && _tool == Tool.Move, "Preset round trip lost tool options.");
        _canvas.Fit(); Pump(); var before = _canvas.InView(new(100,100)); var bar = _canvasHorizontal; bar.Value = Math.Min(bar.Maximum, bar.Value + 35); Pump();
        Check(Math.Abs(_canvas.InView(new(100,100)).X - before.X + 35) < .1, "Horizontal scrollbar did not pan by its delta.");
        before = _canvas.InView(new(100,100)); _canvasVertical.Value = Math.Min(_canvasVertical.Maximum, _canvasVertical.Value + 25); Pump();
        Check(Math.Abs(_canvas.InView(new(100,100)).Y - before.Y + 25) < .1, "Vertical scrollbar did not pan by its delta.");
        _canvas.RotateViewTo(25); Pump(); before = _canvas.InView(new(100,100)); _canvasHorizontal.Value += 20; Pump();
        Check(Math.Abs(_canvas.InView(new(100,100)).X - before.X + 20) < .1 && Math.Abs(_canvas.InView(new(100,100)).Y - before.Y) < .1, "Rotated scrolling moved the wrong screen axis."); _canvas.RotateViewTo(0);
        _rulersVisible = true; _rulerAcross.IsVisible = _rulerDown.IsVisible = _rulerCorner.IsVisible = true; Pump();
        this.MouseMove(At(new(125,92))); Pump();
        Check(Math.Abs((_rulerAcross.PointerValue ?? 0) - 125) < .1 && Math.Abs((_rulerDown.PointerValue ?? 0) - 92) < .1, "Rulers lost pointer coordinates after scrolling.");
        Save("rulers");
        this.MouseMove(At(new(70,90))); Pump(); Check(_canvas.CursorDescription == "Resize", "Side transform handle lacks resize cursor.");
        this.MouseMove(At(new(70 - (float)(17 / _canvas.Zoom),90))); Pump(); Check(_canvas.CursorDescription == "Rotate", "Outside transform band lacks rotate cursor.");
        _canvas.RestoreViewport((1, -80, -70)); Pump();
        var outside = At(new(-35,-35)); this.MouseDown(outside, MouseButton.Left); this.MouseUp(outside, MouseButton.Left); Pump(); Save("pasteboard"); Check(Selected is null, $"Pasteboard click did not select document properties: {outside}, canvas {_canvas.Bounds}, selected {_layers.SelectedIndex}.");
        SetTool(Tool.Artboard); EditorPreferences.Current.ArtboardAutoCanvas = false; _optionsBar.Artboard.Background = 0xff123456;
        var down = At(new(220,40)); var up = At(new(380,200)); this.MouseDown(down, MouseButton.Left); this.MouseMove(up, RawInputModifiers.LeftMouseButton); this.MouseUp(up, MouseButton.Left); Pump();
        var board = doc.Layers.Single(l => l.Container == LayerContainer.Artboard);
        Check(Math.Abs(board.Transform.Width - 160) < .1 && board.ArtboardBackground == 0xff123456, "Dragging did not create the requested artboard.");
        Check(_canvas.TransformBox == board.Transform, "Artboard handles do not match its boundaries.");
        _optionsBar.Artboard.Width = 200; _optionsBar.Artboard.Height = 210; ArtboardCommand("Size"); Pump();
        Check(board.Transform.Width == 200 && board.Transform.Height == 210, "Artboard dimensions did not apply.");
        Check(layer.Transform.Width == 80, "Artboard resizing scaled unrelated contents.");
        _optionsBar.Artboard.Background = 0; ArtboardCommand("Background"); Check(board.ArtboardBackground == 0, "Transparent background did not apply.");
        Check(ProjectSnapshot.FromDocument(doc).Manifest.Version == 22, "Artboard background did not require v22.");
        Undo(); Check(_document!.Layers.Single(l => l.Container == LayerContainer.Artboard).ArtboardBackground == 0xff123456, "Undo did not restore artboard background."); Redo();
        Save("artboard"); SetTool(Tool.Move); Save("final");
    }
}
