using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;
namespace Compositor.Desktop;
internal static partial class Program
{
    private static int ToolOptions158Checks(string output)
    {
        BuildHeadless().SetupWithoutStarting();
        try { var window = new MainWindow(); window.Show(); window.ToolOptions158SelfCheck(output); Console.WriteLine("PASS: fourteen toolbars, editable gradient with undo, retouch/adjustment strokes, pattern fill, brush cards, zoom, hand/rotation synchronization and path components."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
public sealed partial class MainWindow
{
    internal void ToolOptions158SelfCheck(string output)
    {
        static void Check(bool okay, string message) { if (!okay) throw new InvalidOperationException(message); }
        void Pump() { Dispatcher.UIThread.RunJobs(); UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Shot(string name) { Pump(); using var image = this.CaptureRenderedFrame(); image!.Save(Path.ChangeExtension(output, name + ".png"), new PngBitmapEncoderOptions()); }
        Point At(SKPoint p) => _canvas.TranslatePoint(_canvas.ToScreen(p), this)!.Value;
        void Drag(SKPoint a, SKPoint b) { this.MouseDown(At(a), MouseButton.Left); this.MouseMove(At(b), RawInputModifiers.LeftMouseButton); this.MouseUp(At(b), MouseButton.Left); Pump(); }
        Button Button(string tag) => _optionsBar.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Tag, tag) && b.IsEffectivelyVisible);
        void Click(string tag) { Button(tag).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Pump(); }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!); Width = 1680; Height = 980; Pump();
        using var pixels = Bitmaps.Allocate(Bitmaps.ColorInfo(320, 240)); pixels.Erase(new SKColor(80, 100, 140));
        for (var y = 30; y < 160; y++) for (var x = 30; x < 160; x++) pixels.SetPixel(x, y, SKColors.Orange);
        AdoptImported(ImageImporter.NewDocument(ImportedImage.Create(pixels.Copy(), "Tool reference")), "New"); _canvas.ActualSize(); Pump();
        foreach (var (tool, group) in new[] { (Tool.Gradient,"fillGradient"), (Tool.Bucket,"fillBucket"), (Tool.Blur,"localStrength"), (Tool.Sharpen,"localDetail"), (Tool.Smudge,"localFinger"), (Tool.AdjustmentBrush,"localAdjustment"), (Tool.Dodge,"localTones"), (Tool.Burn,"localTones"), (Tool.Sponge,"localVibrance"), (Tool.Zoom,"viewZoom"), (Tool.Pan,"viewPan"), (Tool.RotateView,"viewRotate"), (Tool.PathSelection,"pathSelectScope"), (Tool.Path,"pathOptions") })
        { SetTool(tool); Pump(); Check(_optionsBar.Showing.Contains(group), "Missing options for " + tool); Shot(tool.ToString()); }
        SetTool(Tool.Gradient); var count = _document!.Layers.Count; Drag(new(35,30),new(270,180));
        Check(_document.Layers.Count == count + 1 && PropertyLayer?.LiveGradient is not null, "Modern gradient did not create an editable layer");
        var original = PropertyLayer!.LiveGradient!; _options.GradientReversed = true; OptionsChanged(); Check(PropertyLayer!.LiveGradient!.Paint.Reverse, "Reverse did not update live gradient");
        Undo(); Pump(); Check(!PropertyLayer!.LiveGradient!.Paint.Reverse && !_options.GradientReversed, "Gradient undo failed to refresh options");
        Undo(); Pump(); Check(_document.Layers.Count == count, "Gradient creation undo failed");
        SetTool(Tool.Bucket); using var pattern = Bitmaps.Allocate(Bitmaps.ColorInfo(2, 2)); pattern.Erase(SKColors.White); pattern.SetPixel(0, 0, SKColors.Black);
        _options.BucketPattern = true; _options.Brush = _options.Brush with { Pattern = pattern, Opacity = 1 }; OptionsChanged(); Drag(new(200,200),new(200,200));
        Check(PropertyLayer!.Asset!.Image.GetPixel(200,200).Red == 0 && PropertyLayer.Asset.Image.GetPixel(201,200).Red == 255, "Pattern bucket did not use the selected tile");
        Undo(); _options.BucketPattern = false; _options.Brush = _options.Brush with { Pattern = null }; Pump();
        SetTool(Tool.Blur); _options.Brush = new(Diameter:30, Hardness:1, Strength:1, Painting:new()); OptionsChanged(); var before=PropertyLayer!.Asset;
        Drag(new(155,50),new(155,130)); Check(PropertyLayer!.Asset != before, "Blur stroke did not edit pixels"); Undo(); Pump();
        SetTool(Tool.AdjustmentBrush); _options.AdjustmentBrushKind=6; OptionsChanged(); Drag(new(50,50),new(120,50));
        Check(PropertyLayer?.Adjustment is not null && PropertyLayer.Mask is not null, "Adjustment stroke did not create a mask");
        var maskBefore=PropertyLayer!.Mask!.Asset.Image.GetPixel(75,50).Red; _options.AdjustmentSubtract=true; OptionsChanged(); Drag(new(50,50),new(120,50));
        Check(PropertyLayer.Mask.Asset.Image.GetPixel(75,50).Red < maskBefore, "Subtract adjustment did not remove mask coverage"); _options.AdjustmentOverlay=true; OptionsChanged(); Shot("AdjustmentOverlay");
        SetTool(Tool.Blur); OpenBrushPicker(Button("Brush Tip Presets")); Pump(); Shot("BrushCards"); this.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump();
        SetTool(Tool.Zoom); _canvas.ActualSize(); _options.ZoomOut=true; OptionsChanged(); var zoom=_canvas.Zoom; Drag(new(150,100),new(150,100)); Check(_canvas.Zoom<zoom,"Zoom-out mode ignored"); Click("100%"); Check(Math.Abs(_canvas.Zoom-1)<.0001,"Actual size ignored");
        SetTool(Tool.Pan); Click("Fit Screen"); var fit=_canvas.Zoom; Click("Fill Screen"); Check(_canvas.Zoom>=fit,"Fill screen did not cover viewport");
        SetTool(Tool.RotateView); _canvas.RotateViewTo(35); Pump(); Check(_options.ViewAngle==35,"Angle field not synchronized"); Click("Reset View"); Check(_canvas.ViewAngle==0,"Reset view ignored");
        _document.WorkPath="M20 20 L80 20 L80 80 L20 80 Z M120 20 L180 20 L180 80 L120 80 Z";
        _options.Pen.Output=PenOutput.Path; SetTool(Tool.PathSelection); SyncPathNodes(); _canvas.ActualSize(); Pump();
        var old=PathNodes.Parse(_document.WorkPath)!; Drag(new(45,45),new(60,60)); var moved=PathNodes.Parse(_document.WorkPath)!;
        Check(moved.Subpaths[0].Nodes[0].Point!=old.Subpaths[0].Nodes[0].Point,"Path component did not move"); Check(moved.Subpaths[1].Nodes[0].Point==old.Subpaths[1].Nodes[0].Point,"Moving one component moved another"); Undo(); Pump();
        PenAction("Path Options",Button("Path Options")).GetAwaiter().GetResult(); Pump(); Shot("PathOptions"); _penFlyout?.Hide();
        SetTool(Tool.Path); PenAction("Path Alignment",Button("Path Alignment")).GetAwaiter().GetResult(); Pump(); Shot("PathAlignment"); _penFlyout?.Hide();
        var other=new MainWindow(); other.Show(); other.AdoptImported(ImageImporter.NewDocument(ImportedImage.Create(pixels.Copy(),"Other")),"New"); Pump();
        SetTool(Tool.Pan); _options.PanAll=true; OptionsChanged(); var otherX=other._canvas.OriginX; Drag(new(150,100),new(180,100)); Check(other._canvas.OriginX!=otherX,"Scroll all windows ignored");
        SetTool(Tool.RotateView); _options.RotateAll=true; OptionsChanged(); var otherAngle=other._canvas.ViewAngle; _canvas.RotateViewTo(25); Pump(); Check(other._canvas.ViewAngle!=otherAngle,"Rotate all windows ignored");
        SetTool(Tool.Zoom); _options.ZoomAll=true; OptionsChanged(); var otherZoom=other._canvas.Zoom; _canvas.ZoomBy(1.25); Pump(); Check(other._canvas.Zoom>otherZoom,"Zoom all windows ignored");
        SetTool(Tool.Gradient); Width=1080; Pump(); Shot("NarrowGradient");
    }
}
