using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
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
    private static int ImageWorkspaceChecks(string output)
    {
        BuildHeadless().SetupWithoutStarting();
        try { var window = new MainWindow(); window.Show(); window.ImageWorkspaceSelfCheck(output); Console.WriteLine("PASS: grouped tool/image/plugin menus, document/pixel/type properties, ellipsis expansion, localized dialog text, native image mode conversion, preview cancellation and image adjustment undo."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
public sealed partial class MainWindow
{
    internal void ImageWorkspaceSelfCheck(string output)
    {
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        void Pump() { Dispatcher.UIThread.RunJobs(); UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Save(string suffix) { Pump(); using var frame = this.CaptureRenderedFrame(); frame!.Save(Path.ChangeExtension(output, suffix + ".png"), new PngBitmapEncoderOptions()); }
        void Finish(Task task) { for (var i = 0; i < 300 && !task.IsCompleted; i++) { Pump(); Thread.Sleep(10); } task.GetAwaiter().GetResult(); Pump(); }
        void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
        using (var source = new SKBitmap(Bitmaps.ColorInfo(29, 17)))
        {
            source.Erase(SKColors.Coral); using var encoded = source.Encode(SKEncodedImageFormat.Png, 100); using var stream = new MemoryStream(encoded.ToArray());
            using var clipboardBitmap = new Avalonia.Media.Imaging.Bitmap(stream);
            Finish(Avalonia.Input.Platform.ClipboardExtensions.SetBitmapAsync(Clipboard!, clipboardBitmap));
            Finish(PasteToNewCanvas()); Check(_document is { Width: 29, Height: 17, Layers.Count: 1 }, "Pasting on New Canvas did not create a correctly sized document.");
        }
        var doc = new CanvasDocument(Guid.NewGuid(), 600, 450);
        var pixels = new SKBitmap(Bitmaps.ColorInfo(220, 150)); pixels.Erase(new SKColor(80, 120, 180));
        var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "Pixel"), new Compositor.Core.Model.LayerTransform(40, 50, 220, 150), "Pixel"); doc.Layers.Add(layer);
        AdoptImported(doc, "Import Image"); OpenInspector(0); Pump();
        var tools = _mainMenu!.Items.OfType<MenuItem>().Single(m => m.Header?.ToString() == Localize.Text("_Tools"));
        Check(tools.Items.OfType<MenuItem>().Count() <= 14 && _toolItems.Count >= 65, "Tool grouping lost tools or remains too long.");
        tools.IsSubMenuOpen = true; Save("tools-menu"); tools.IsSubMenuOpen = false;
        var image = _mainMenu.Items.OfType<MenuItem>().Single(m => m.Header?.ToString() == Localize.Text("_Image"));
        Check(image.Items.OfType<MenuItem>().Single(m => m.Header?.ToString() == Localize.Text("Adjustments")).Items.OfType<MenuItem>().Count() >= 22, "Image adjustments are missing.");
        image.IsSubMenuOpen = true; Save("image-menu"); image.IsSubMenuOpen = false;
        OpenPluginsPanel(); Pump(); Check(_inspectorTabs.Items.OfType<TabItem>().Any(t => t.Header?.ToString() == Localize.Text("Plugins")), "Plugin panel not opened."); Save("plugins"); OpenInspector(0);
        _layers.SelectedItems?.Clear(); _layers.SelectedIndex = -1; BuildProperties(); Pump();
        Check(_properties.GetVisualDescendants().OfType<NumericUpDown>().Any(n => Equals(n.Tag, "canvas-width")), "Document canvas properties missing."); Save("document-properties");
        Reselect(layer.ID); BuildProperties(); Pump();
        var more = _properties.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "property-more-Alignment"));
        var target = _properties.GetVisualDescendants().OfType<ComboBox>().Single(c => Equals(c.Tag, "alignment-target"));
        Check(target.IsEffectivelyVisible, "Alignment advanced options initially missing."); Click(more); Check(!target.IsEffectivelyVisible, "Ellipsis did not collapse."); Click(more); Check(target.IsEffectivelyVisible, "Ellipsis did not expand."); Save("pixel-properties");
        var revision = _history.CurrentRevision; var pending = ConvertImageMode(ImageMode.Grayscale); Pump();
        var dialog = OwnedWindows.OfType<FormDialog>().Single(); dialog.Close(false); Finish(pending); Check(doc.Image.Mode == ImageMode.RGB && revision == _history.CurrentRevision, "Cancel changed color mode.");
        pending = ConvertImageMode(ImageMode.Grayscale); Pump(); dialog = OwnedWindows.OfType<FormDialog>().Single();
        using (var capture = dialog.CaptureRenderedFrame()) capture!.Save(Path.ChangeExtension(output, "mode-dialog.png"), new PngBitmapEncoderOptions());
        Click(dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == Localize.Text("OK"))); Finish(pending);
        Check(doc.Image.Mode == ImageMode.Grayscale && layer.Asset!.Image.GetPixel(0, 0).Red == layer.Asset.Image.GetPixel(0, 0).Green, "Color mode dialog did not apply."); Undo(); Pump();
        var old = doc.Layers[0].Asset; pending = ExtendedImageAdjustment(ImageAdjustmentKind.Posterize, false); Pump();
        dialog = OwnedWindows.OfType<FormDialog>().Single(); dialog.Close(false); Finish(pending); Check(ReferenceEquals(doc.Layers[0].Asset, old), "Adjustment cancel mutated pixels.");
        var id = TextEdits.Add(doc, new LayerTextStyle { Content = "中文排版 Typography\nParagraph settings", FontName = "Arial", FontSize = 32 }, new(80, 150))!.Value;
        ShowLayers(doc); Reselect(id); BuildProperties(); Pump();
        var horizontal = _properties.GetVisualDescendants().OfType<NumericUpDown>().Single(n => Equals(n.Tag, "text-hscale"));
        horizontal.Value = 140; horizontal.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Avalonia.Input.Key.Enter }); Pump();
        Check(PropertyLayer!.LiveText!.Layout?.HorizontalScale == 140, "Character scaling did not commit."); Save("type-properties");
        var checkDialog = new FormDialog("Properties", 440, 420);
        var longButton = new Button { Content = Localize.Text("Add or Update Variable"), Width = 72, Height = 20 };
        var row = new Grid { RowDefinitions = new RowDefinitions("24") }; row.Children.Add(longButton); checkDialog.Fields.Children.Add(row);
        var shown = checkDialog.ShowDialog<bool>(this); Pump(); checkDialog.UpdateLayout(); checkDialog.FitTextButtons(); checkDialog.UpdateLayout();
        Check(double.IsNaN(longButton.Height) && longButton.Bounds.Height >= 32 && row.RowDefinitions[0].Height.IsAuto, "Fixed dialog row still clips wrapped button text.");
        using (var capture = checkDialog.CaptureRenderedFrame()) capture!.Save(Path.ChangeExtension(output, "button-wrap.png"), new PngBitmapEncoderOptions());
        checkDialog.Close(false); Finish(shown); Save("final");
    }
}
