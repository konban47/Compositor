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
    private static int PaintingChecks(string output)
    {
        BuildHeadless().SetupWithoutStarting();
        try { var window = new MainWindow(); window.Show(); window.PaintingSelfCheck(output); Console.WriteLine("PASS: eleven painting toolbars, brush picker, thirteen settings sections, stamp/history/eraser strokes with undo, pattern picker, pressure/flow options, and brush preset round trip."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
public sealed partial class MainWindow
{
    internal void PaintingSelfCheck(string output)
    {
        static void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
        void Pump(){Dispatcher.UIThread.RunJobs();UpdateLayout();Dispatcher.UIThread.RunJobs();}
        Point At(SKPoint p)=>_canvas.TranslatePoint(_canvas.ToScreen(p),this)!.Value;
        void Click(SKPoint p,RawInputModifiers modifiers=RawInputModifiers.None){this.MouseDown(At(p),MouseButton.Left,modifiers);this.MouseUp(At(p),MouseButton.Left,modifiers);Pump();}
        void Drag(SKPoint a,SKPoint b){this.MouseDown(At(a),MouseButton.Left);this.MouseMove(At(b),RawInputModifiers.LeftMouseButton);this.MouseUp(At(b),MouseButton.Left);Pump();}
        void Save(string suffix){Pump();using var shot=this.CaptureRenderedFrame();shot!.Save(Path.ChangeExtension(output,suffix+".png"),new PngBitmapEncoderOptions());}
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!); Width=1680;Height=980;Pump();
        using var pixels=Bitmaps.Allocate(Bitmaps.ColorInfo(320,240));pixels.Erase(SKColors.Blue);for(var y=30;y<100;y++)for(var x=30;x<100;x++)pixels.SetPixel(x,y,SKColors.Orange);
        AdoptImported(ImageImporter.NewDocument(ImportedImage.Create(pixels.Copy(),"Paint")),"New");_canvas.ActualSize();Pump();
        foreach(var(tool,group)in new[]{(Tool.Brush,"paintBlend"),(Tool.Pencil,"paintPencil"),(Tool.ColorReplacement,"paintReplace"),(Tool.MixerBrush,"paintMixer"),(Tool.Clone,"paintClone"),(Tool.PatternStamp,"paintPattern"),(Tool.HistoryBrush,"paintStampBlend"),(Tool.ArtHistory,"paintArt"),(Tool.Eraser,"paintEraseMode"),(Tool.BackgroundEraser,"paintProtect"),(Tool.MagicEraser,"paintMagic")})
        {SetTool(tool);Pump();Check(_optionsBar.Showing.Contains(group),"Missing toolbar "+group);Save(tool.ToString());}
        SetTool(Tool.Brush);_options.Brush=new(Diameter:16,Red:1,Painting:new());OptionsChanged();var before=PropertyLayer!.Asset;Drag(new(140,130),new(180,130));Check(PropertyLayer!.Asset!=before&&PropertyLayer.Asset!.Image.GetPixel(160,130).Red>200,"Brush did not paint");Undo();Pump();Check(PropertyLayer!.Asset==before,"Brush undo failed");
        SetTool(Tool.Clone);Click(new(50,50),RawInputModifiers.Alt);Click(new(160,130));Check(PropertyLayer!.Asset!.Image.GetPixel(160,130).Red>200,"Clone did not sample source");Undo();Pump();
        _historyBrushSource?.Dispose();_historyBrushSource=pixels.Copy();SetTool(Tool.Eraser);Click(new(50,50));Check(PropertyLayer!.Asset!.Image.GetPixel(50,50).Alpha==0,"Eraser did not clear");
        ChangePainting(p=>p with {EraseToHistory=true});Click(new(50,50));Check(PropertyLayer!.Asset!.Image.GetPixel(50,50).Alpha==255,"Erase to history did not restore");Undo();Pump();SetTool(Tool.HistoryBrush);Click(new(50,50));Check(PropertyLayer!.Asset!.Image.GetPixel(50,50).Alpha==255,"History brush did not restore");
        SetTool(Tool.Brush);OpenUtilityPanel("Brush Settings");Pump();Check(_brushSettingsEditor is not null,"Brush settings missing");
        for(var i=0;i<BrushSettingsEditor.Sections.Length;i++){_brushSettingsEditor!.SelectSection(i);Pump();Check(_brushSettingsEditor.GetVisualDescendants().OfType<ToggleButton>().Any(b=>Equals(b.Tag,"brush-lock-"+i)),"Missing section lock");Save("Section"+i);}
        _brushSettingsEditor!.SelectSection(1);Pump();var enabled=_brushSettingsEditor.GetVisualDescendants().OfType<CheckBox>().Single(b=>Equals(b.Tag,"brush-enable-1"));enabled.IsChecked=true;Check(PaintingOptions.ShapeEnabled,"Dynamics checkbox did not update state");
        BrushLayoutSelfCheck(output);
        var anchor=_optionsBar.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Tag,"Brush Tip Presets"));OpenBrushPicker(anchor);Pump();Save("BrushPicker");
        SetTool(Tool.PatternStamp);var pattern=_optionsBar.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Tag,"Pattern Presets"));OpenPatternPicker(pattern);Pump();Save("Patterns");
        var presets=PatternLibrary.Defaults();ApplyPreset(presets[0]);Click(new(200,170));Click(new(200,170));Check(PropertyLayer!.Asset!.Image.GetPixel(200,170)!=SKColors.Blue,"Pattern did not paint");
        var stored=new EditorPreset(Guid.NewGuid(),"Dynamic eraser","Brush",Brush:new BrushSettings(Diameter:23,Painting:new(){Flow=.4,ArtStyle=8,EraseToHistory=true}),Tool:"Eraser",IncludeTool:true);var path=Path.ChangeExtension(output,"presets.json");EditorPresets.Save(path,[stored]);ApplyBrushPreset(EditorPresets.Load(path).Single());Check(_tool==Tool.Eraser&&PaintingOptions.EraseToHistory&&PaintingOptions.Flow==.4,"Preset lost tool or brush options");
        Save("Result");
    }
}
