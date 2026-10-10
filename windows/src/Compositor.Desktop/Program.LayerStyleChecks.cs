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
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Desktop;

internal static partial class Program
{
    private static int LayerStyleChecks(string output)
    {
        BuildHeadless().SetupWithoutStarting();
        try { var window = new MainWindow(); window.Show(); window.LayerStyleSelfCheck(output); Console.WriteLine("PASS: layer-style preview/cancel/apply/undo, all effect panels, duplicate instances, Alt-split Blend If, context menu commands, smart contents save, labels, native SVG, Chinese labels."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
public sealed partial class MainWindow
{
    internal void LayerStyleSelfCheck(string output)
    {
        static void Check(bool value, string why) { if (!value) throw new InvalidOperationException(why); }
        void Pump(int delay = 0) { if (delay > 0) Thread.Sleep(delay); Dispatcher.UIThread.RunJobs(); UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Click(Window window, string title) { window.GetVisualDescendants().OfType<Button>().First(b => b.Content?.ToString() == Localize.Text(title)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(180); }
        void Save(Window window, string suffix) { Pump(); window.UpdateLayout(); using var image = window.CaptureRenderedFrame(); image!.Save(Path.ChangeExtension(output, suffix + ".png"), new PngBitmapEncoderOptions()); }
        var style = new LayerShapeStyle { Kind = ShapeKind.Rectangle, Red = .3, Green = .55, Blue = .8, CornerRadius = 16 };
        var pixels = ShapeEdits.Image(style, 160, 120)!;
        var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "测试图层"), new LayerTransform(80, 70, 160, 120), "测试图层") { Shape = new LayerShape(style, pixels) };
        var doc = new CanvasDocument(Guid.NewGuid(), 400, 300); doc.Layers.Add(layer); AdoptImported(doc, "New"); Pump(); SelectLayerRow(layer.ID);
        var original = layer.Effects; var revision = _history.CurrentRevision;
        var operation = EditLayerStyle(StyleEffectKind.Stroke); Pump();
        var dialog = OwnedWindows.OfType<LayerStyleDialog>().Single();
        dialog.GetVisualDescendants().OfType<NumericUpDown>().Single(n => Equals(n.Tag, "Size (px)")).Value = 12; Pump(180);
        Check(layer.Effects?.Items?.Any(e => e.Kind == StyleEffectKind.Stroke && e.Enabled && e.Size == 12) == true, "Style preview did not update canvas data.");
        Click(dialog, "Cancel"); Check(operation.IsCompletedSuccessfully && ReferenceEquals(layer.Effects, original) && _history.CurrentRevision == revision, "Cancel changed the layer or its history.");
        operation = EditLayerStyle(); Pump(); dialog = OwnedWindows.OfType<LayerStyleDialog>().Single();
        Save(dialog, "blending");
        var range = dialog.GetVisualDescendants().OfType<BlendRangeControl>().First(); range.BringIntoView(); Pump();
        var start = range.TranslatePoint(new Point(8, 46), dialog)!.Value;
        dialog.MouseDown(start, MouseButton.Left, RawInputModifiers.Alt); dialog.MouseMove(start + new Vector(60, 0), RawInputModifiers.Alt | RawInputModifiers.LeftMouseButton); dialog.MouseUp(start + new Vector(60, 0), MouseButton.Left, RawInputModifiers.Alt); Pump(180);
        var split = dialog.Draft.Blending!.Ranges[0].Source; Check(split.Black == 0 && split.BlackSplit > 0 && split.IsValid, "Alt drag did not split Blend If.");
        split.BlackSplit = 0;
        foreach (var kind in Enum.GetValues<StyleEffectKind>())
        {
            Click(dialog, LayerStyleDialog.NameFor(kind));
            var enabled = dialog.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content?.ToString() == Localize.Text("Enable Effect")); enabled.IsChecked = true; Pump(180);
            using var rendered = DocumentRenderer.Render(doc); Check(rendered.GetPixelSpan().Length > 0, "Effect preview failed: " + kind);
        }
        Click(dialog, "Bevel & Emboss"); Save(dialog, "bevel");
        Click(dialog, "Stroke");
        var plus = dialog.GetVisualDescendants().OfType<Button>().First(b => ToolTip.GetTip(b)?.ToString() == Localize.Text("Add Another Effect")); plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(dialog.Draft.Effects!.Items!.Count(e => e.Kind == StyleEffectKind.Stroke) == 2, "Repeated stroke was not added.");
        Click(dialog, "OK"); Check(operation.IsCompletedSuccessfully && _history.UndoName == "Layer Style", "Style was not committed as one undo step.");
        Undo(); Pump(); Check(doc.Layers[0].Effects is null, "Undo did not restore original style."); Redo(); Pump(); layer = doc.Layers[0]; SelectLayerRow(layer.ID);
        var menu = new ContextMenu(); PopulateLayerContext(menu, layer);
        var expected = new[] { "Clean Up Layers", "New Layer…", "New Group", "Duplicate Layer", "Delete Layer", "Quick Export as PNG", "Export Layers As…", "Merge Visible", "Flatten Image", "Lock Layers…", "Rename Layer…", "Hide Layers", "Blending Options…", "New Group from Layers…", "Collapse All Groups", "Frame from Layers…", "New Artboard…", "Artboard from Layers…", "Convert to Smart Object", "Mask All Objects", "Create Clipping Mask", "Copy CSS", "Copy SVG", "Color" };
        foreach (var title in expected) Check(menu.Items.OfType<MenuItem>().Any(m => m.Header?.ToString() == Localize.Text(title)), "Missing context menu: " + title);
        var labels = menu.Items.OfType<MenuItem>().Single(m => m.Header?.ToString() == Localize.Text("Color"));
        labels.Items.OfType<MenuItem>().Single(m => m.Header?.ToString() == Localize.Text("Red")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump(); Check(layer.Label == LayerLabel.Red, "Color label did not apply.");
        ClearSelectedStyles(); Pump(); Check(LayerWorkflow.Svg(doc, [layer.ID]).Contains("<rect"), "Shape SVG should remain vector.");
        ConvertToSmartObject(); Pump(); var smart = doc.Layers.Single(); Check(smart.SmartObject is not null, "Smart conversion failed.");
        Check(!LayerProtection.CanPaint(doc, smart.ID), "Smart pixels can be painted without rasterization.");
        var parent = _open; EditSmartContents(); Pump(); Check(_open != parent && _document!.Layers[0].LiveShape is not null, "Smart contents lost editable shapes.");
        var child = _open; Edit("Layer Opacity", () => { child.Document!.Layers[0].Opacity = .5; return true; });
        Check(SaveSmartContents(child) && !child.History.IsModified && parent.History.UndoName == "Update Smart Object", "Saving contents did not update parent.");
        Check(smart.Asset!.Image.GetPixel(80, 60).Alpha is >= 126 and <= 129, "Smart cache was not updated."); Bring(parent); Pump();
        Edit("Rasterize Layer", () => LayerWorkflow.Rasterize(doc, [smart.ID])); Check(doc.Layers[0].SmartObject is null && LayerProtection.CanPaint(doc, smart.ID), "Rasterize did not unlock pixel edits.");
        Undo(); Pump(); Check(doc.Layers[0].SmartObject is not null, "Rasterize lost undo source.");
        if (Localize.IsChinese) Check(Localize.Text("Blending Options") == "混合选项" && Localize.Text("Mask All Objects") == "遮住所有对象", "New labels are not Chinese.");
        Save(this, "final"); using var final = this.CaptureRenderedFrame(); final!.Save(output, new PngBitmapEncoderOptions());
    }
}
