using System.Net;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Desktop;

internal static partial class Program
{
    private static int WorkspaceChecks(string output)
    {
        BuildHeadless().SetupWithoutStarting();
        try { var window = new MainWindow(); window.Show(); window.WorkspaceSelfCheck(output); Console.WriteLine("PASS: F/Shift+F/Escape, seven shape tools, path nodes, rotated zoom and hit-testing, quick mask, toolbar drag/presets, alignment/history icons, generated image request/preview/import/undo, encrypted settings."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
public sealed partial class MainWindow
{
    private sealed class GenerationCheckHandler(byte[] image) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { data = new[] { new { b64_json = Convert.ToBase64String(image) } } })) }); }
    }
    internal void WorkspaceSelfCheck(string output)
    {
        static void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
        void Pump() { Dispatcher.UIThread.RunJobs(); UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Click(Window owner, Control control)
        { owner.UpdateLayout(); var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), owner)!.Value; owner.MouseDown(point, MouseButton.Left); owner.MouseUp(point, MouseButton.Left); Pump(); }
        void Save(Window owner, string suffix)
        { Pump(); owner.UpdateLayout(); using var shot = owner.CaptureRenderedFrame(); shot!.Save(Path.ChangeExtension(output, suffix + ".png"), new PngBitmapEncoderOptions()); }
        void Key(Key key, RawInputModifiers mods = RawInputModifiers.None)
        { _canvas.Focus(); this.KeyPress(key, mods, PhysicalKey.None, null); this.KeyRelease(key, mods, PhysicalKey.None, null); Pump(); }
        Point At(double x, double y)
        { var point = _canvas.ToScreen(new SKPoint((float)x,(float)y)); return _canvas.TranslatePoint(point, this)!.Value; }
        var doc = LayerPlacement.NewDocument(500, 350)!; AdoptImported(doc, "New"); Pump(); _canvas.ActualSize();
        Key(Avalonia.Input.Key.F); Check(WindowState == WindowState.FullScreen && _rail.IsVisible && _layersSide.IsVisible && !_canvasOnly, "F hid the editing tools."); Save(this, "fullscreen");
        Key(Avalonia.Input.Key.F); Check(!_editingFullscreen, "F did not restore normal mode.");
        Key(Avalonia.Input.Key.F, RawInputModifiers.Shift); Check(_canvasOnly, "Shift+F did not show canvas-only.");
        Key(Avalonia.Input.Key.Escape); Check(!_canvasOnly && !_editingFullscreen && _rail.IsVisible, "Escape did not restore chrome.");
        foreach (var tool in new[] { Tool.Shape, Tool.ShapeEllipse, Tool.Triangle, Tool.PolygonShape, Tool.Star, Tool.Line, Tool.CustomShape })
        {
            SetTool(tool); var count=doc.Layers.Count;
            this.MouseDown(At(60,60),MouseButton.Left); this.MouseMove(At(190,150)); this.MouseUp(At(190,150),MouseButton.Left); Pump();
            Check(doc.Layers.Count == count+1 && doc.Layers[^1].LiveShape is not null, "Shape tool failed: " + tool);
        }
        SetTool(Tool.Path); Pump(); Check(_canvas.PathNodes is { NodeCount: > 0 }, "Direct selection has no live shape nodes.");
        var beforePath=doc.Layers[^1].LiveShape!.Path;
        var node = _canvas.PathToDocument!(_canvas.PathNodes!.Subpaths[0].Nodes[0].Point);
        var nodeAt = At(node.X,node.Y);
        this.MouseDown(nodeAt,MouseButton.Left); this.MouseMove(nodeAt+new Vector(12,-8),RawInputModifiers.LeftMouseButton); this.MouseUp(nodeAt+new Vector(12,-8),MouseButton.Left); Pump();
        Check(doc.Layers[^1].LiveShape?.Path != beforePath, "Direct selection did not save a node edit.");
        Undo(); Pump(); Check(doc.Layers[^1].LiveShape?.Path == beforePath, "Path node edit was not undoable.");
        SetTool(Tool.RotateView);
        var center=_canvas.TranslatePoint(new Point(_canvas.Bounds.Width/2,_canvas.Bounds.Height/2),this)!.Value;
        this.MouseDown(center+new Vector(70,0),MouseButton.Left); this.MouseMove(center+new Vector(0,70),RawInputModifiers.LeftMouseButton); this.MouseUp(center+new Vector(0,70),MouseButton.Left); Pump();
        Check(Math.Abs(_canvas.ViewAngle-90)<.01,"Rotate View tool did not rotate the viewport.");
        _canvas.RotateViewTo(35); Pump();
        var screen = _canvas.ToScreen(new SKPoint(240,190)); var mapped = _canvas.ToDocument(screen);
        Check(Math.Abs(mapped.X-240)<.001 && Math.Abs(mapped.Y-190)<.001, "Rotated coordinate conversion is not reversible.");
        var zoom = _canvas.Zoom; SetTool(Tool.Zoom); this.MouseDown(At(240,190),MouseButton.Left); this.MouseUp(At(240,190),MouseButton.Left); Pump();
        var after = _canvas.ToDocument(screen); Check(Math.Abs(_canvas.Zoom-zoom*1.25)<.0001 && Math.Abs(after.X-240)<.01 && Math.Abs(after.Y-190)<.01, "Zoom tool drifted from its mouse anchor in a rotated view.");
        SetTool(Tool.Move); _snappingOn=false;
        var shape=doc.Layers[^1]; var oldTransform=shape.Transform; var oldOrigin=new Point(_canvas.OriginX,_canvas.OriginY);
        var start=At(oldTransform.CenterX,oldTransform.CenterY); var finish=At(oldTransform.CenterX+22,oldTransform.CenterY+13);
        this.MouseDown(start,MouseButton.Left); this.MouseMove(finish,RawInputModifiers.LeftMouseButton); this.MouseUp(finish,MouseButton.Left); Pump();
        Check(Math.Abs(shape.Transform.X-oldTransform.X-22)<.1 && Math.Abs(shape.Transform.Y-oldTransform.Y-13)<.1 && new Point(_canvas.OriginX,_canvas.OriginY)==oldOrigin,"Moving a layer in a rotated view moved the canvas or wrong distance.");
        _canvas.RotateViewTo(0); _canvas.ActualSize(); SetTool(Tool.Move);
        SelectionEdits.Select(doc,SKRectI.Create(40,40,100,80)); ToggleQuickMask(); Pump();
        Check(doc.Channels.Single(c=>c.IsTemporary).ID == _open.ActiveAlpha && _canvas.AlphaDisplay is not null, "Quick mask was not active.");
        Check(ProjectSnapshot.FromDocument(doc).Channels.Count==0, "Temporary quick mask leaked into project channels.");
        ToggleQuickMask(); Pump(); Check(doc.Channels.Count==0 && doc.Selection.Path is not null, "Quick mask did not restore selection.");
        Undo(); Pump(); Check(doc.Channels.Any(c=>c.IsTemporary) && _open.ActiveAlpha is not null, "Undo did not restore quick mask edit target."); Redo(); Pump();
        var toolbar=new ToolbarDialog(ToolCatalog.Defaults()); var task=toolbar.ShowDialog<ToolbarLayout?>(this); Pump();
        var lists=toolbar.GetVisualDescendants().OfType<ListBox>().ToArray(); var main=lists[0]; var extra=lists[1];
        var move=main.Items.OfType<ListBoxItem>().First(row=>row.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text==Localize.Text(ToolCatalog.Title(Tool.Move))));
        var from=move.TranslatePoint(new Point(35,17),toolbar)!.Value; var to=extra.TranslatePoint(new Point(80,70),toolbar)!.Value;
        toolbar.MouseDown(from,MouseButton.Left); toolbar.MouseMove(from+new Vector(9,2),RawInputModifiers.LeftMouseButton); toolbar.MouseMove(to,RawInputModifiers.LeftMouseButton); toolbar.MouseUp(to,MouseButton.Left); Pump();
        Check(toolbar.Draft.Extras.Contains(nameof(Tool.Move)), "Toolbar drag to Extra Tools did not move the tool.");
        Save(toolbar,"toolbar");
        Click(toolbar,toolbar.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()==Localize.Text("Done"))); Pump();
        Check(task.IsCompletedSuccessfully && task.Result is not null,"Customize toolbar did not return its preset.");
        var preset=Path.Combine(AppPaths.SettingsDirectory,"test-toolbar.json"); task.Result!.Save(preset,ToolCatalog.IDs); var read=ToolbarLayout.Load(preset,ToolCatalog.IDs); _rail.ApplyLayout(read); Pump();
        Check(_rail.ButtonFor(Tool.Move) is null,"Saved Extra Tool is still on the rail.");
        read.DisableExtraShortcuts=true;
        foreach(var group in read.Groups) group.Remove(nameof(Tool.Shape)); read.Groups.RemoveAll(g=>g.Count==0); read.Extras.Add(nameof(Tool.Shape));
        _rail.ApplyLayout(read); SetTool(Tool.Pan); Key(Avalonia.Input.Key.V); Check(_tool==Tool.Pan,"Disabled extra tool still responds to V.");
        Key(Avalonia.Input.Key.U); Check(ToolCatalog.IsShape(_tool) && _tool!=Tool.Shape,"Shape family shortcut chose a disabled extra tool.");
        _rail.ApplyLayout(ToolCatalog.Defaults()); Pump();
        var shapeOptions = new ShapeSettingsDialog(7,.45,ShapeTemplates.Presets["Heart"],null); shapeOptions.Show(this); Pump(); Save(shapeOptions,"shape-options"); shapeOptions.Close();
        doc.Selection=DocumentSelection.All; SetTool(Tool.Move); OpenInspector(0); Pump(); Save(this,"properties");
        Check(_properties.GetVisualDescendants().OfType<Button>().Count(b=>b.Tag is string s && (s.StartsWith("Align ")||s.StartsWith("Distribute ")))>=14,"Alignment/distribution buttons missing.");
        OpenInspector(1); Pump(); var revision=_history.CurrentRevision;
        var source=_historyRows.GetVisualDescendants().OfType<Button>().Last(b=>b.IsEffectivelyVisible && ToolTip.GetTip(b)?.ToString()==Localize.Text("Set History Brush Source"));
        Click(this,source); Check(_historyBrushSource is not null && _history.CurrentRevision==revision,"Source column changed the active history state."); Save(this,"history");
        using var bitmap=new SKBitmap(16,12); bitmap.Erase(SKColors.LimeGreen); using var encoded=bitmap.Encode(SKEncodedImageFormat.Png,100);
        var generator=new GenerativeDialog(true,new GenerationCheckHandler(encoded.ToArray())); var generated=generator.ShowDialog<GenerativeDialog.Result?>(this); Pump();
        foreach(var (tag,text) in new[]{("generation-endpoint","https://example.invalid/v1"),("generation-model","mock-model"),("generation-prompt","测试图像 / Test image"),("generation-key","test-only")})
            generator.GetVisualDescendants().OfType<TextBox>().Single(t=>Equals(t.Tag,tag)).Text=text;
        var generation=generator.Generate();
        for(var i=0;!generation.IsCompleted&&i<1000;i++){Pump();Thread.Sleep(5);} Pump();
        Check(generation.IsCompletedSuccessfully && generator.GetVisualDescendants().OfType<ListBox>().Single().ItemCount==1,"Mock generation did not show a result."); Save(generator,"generator");
        Click(generator,generator.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Tag,"Import as Layer"))); Pump();
        Check(generated.IsCompletedSuccessfully && generated.Result is not null,"Generation result did not return for import.");
        var oldCount=doc.Layers.Count; ImportGenerated(generated.Result!); Pump();
        Check(doc.Layers.Count==oldCount+1 && doc.Layers[^1].Asset!.Image.GetPixel(0,0).Green>100,"Generated image was disposed after import."); Undo(); Pump(); Check(doc.Layers.Count==oldCount,"Generated import was not undoable.");
        new GenerationSettings { Endpoint="https://example.invalid/v1",Model="mock-model" }.Save("test-only",true);
        var settings=GenerationSettings.Load(); Check(settings.Key()=="test-only" && settings.ProtectedKey!="test-only","Windows key encryption did not round trip.");
        new GenerationSettings().Save("",false); _rail.ApplyLayout(ToolCatalog.Defaults()); OpenInspector(0); Save(this,"final");
        if(Localize.IsChinese) Check(Localize.Text("Customize Toolbar")=="自定义工具栏" && Localize.Text("Generative Workspace")=="生成式工作区","New workspace labels are not Chinese.");
    }
}
