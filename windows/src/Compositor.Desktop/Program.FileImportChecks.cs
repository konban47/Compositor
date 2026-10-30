using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;
using LayerMask = Compositor.Core.Model.LayerMask;

namespace Compositor.Desktop;

public partial class MainWindow
{
    internal void FileImportSelfCheck(Action pump, Action<Window, string> capture)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var document = new CanvasDocument(Guid.NewGuid(), 16, 16);
        ImageLayer Layer(SKColor color, string name)
        {
            var pixels = new SKBitmap(Bitmaps.ColorInfo(16, 16)); pixels.Erase(color);
            return new(Guid.NewGuid(), ImportedImage.Create(pixels, name), new(0, 0, 16, 16), name);
        }
        var first = Layer(SKColors.Red, "First"); var second = Layer(SKColors.Blue, "Second");
        first.Opacity = .4; second.IsVisible = false;
        document.Layers.AddRange([first, second, Layer(SKColors.Transparent, "Empty")]);
        AdoptImported(document, "File Import Check");
        var menu = BuildFileMenu();
        var scripts = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, Localize.Text("Scripts")));
        void Click(string name) => scripts.Items.OfType<MenuItem>().Single(item => Equals(item.Header, Localize.Text(name)))
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Click("Delete All Empty Layers");
        Check(_document!.Layers.Count == 2, "Empty layer script did not run.");
        Undo(); Check(_document!.Layers.Count == 3, "Empty layer script did not undo.");
        Redo(); Check(_document!.Layers.Count == 2, "Empty layer script did not redo.");
        foreach (var layer in _document.Layers)
        {
            var mask = new SKBitmap(Bitmaps.MaskInfo(16, 16)); mask.Erase(SKColors.White);
            layer.Mask = new LayerMask(ImportedImage.Create(mask, "Mask"));
        }
        Click("Flatten All Masks");
        Check(_document.Layers.All(layer => layer.Mask is null), "Not all layer masks were applied.");
        Undo(); Check(_document!.Layers.All(layer => layer.Mask is not null), "Masks were not restored by undo.");
        Redo(); Check(_document!.Layers.All(layer => layer.Mask is null), "Mask redo failed.");
        foreach (var layer in _document.Layers) layer.Effects = new LayerEffects { Items = [StyleEffect.Default(StyleEffectKind.ColorOverlay)] };
        using var expected = DocumentRenderer.Render(_document);
        Click("Flatten All Layer Effects");
        Check(_document.Layers.All(layer => layer.Effects is null), "Effects on unselected or hidden layers were skipped.");
        using var actual = DocumentRenderer.Render(_document);
        Check(Math.Abs(expected.GetPixel(8, 8).Alpha - actual.GetPixel(8, 8).Alpha) <= 1, "Rasterization applied layer opacity twice.");
        Check(_document.Layers[0].Opacity == .4 && !_document.Layers[1].IsVisible, "Rasterization changed layer visibility/opacity.");
        Undo(); Check(_document!.Layers.All(layer => layer.Effects is not null), "Effects were not restored by undo.");
        var imports = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, Localize.Text("Import")));
        Check(imports.Items.OfType<MenuItem>().Count() == 4, "Import menu entries are missing.");
        var form = BuildVideoImportForm(new(64, 32, 1, false, 8), "sample.mp4", out var options);
        _ = form.ShowDialog(this); pump();
        Check(form.Validate!() is null && options().FrameRate == 8, "Video metadata was not connected to the form.");
        var every = form.Fields.GetVisualDescendants().OfType<NumericUpDown>().Single(field => Equals(field.Tag, "Import Every Nth Frame"));
        every.Value = 2; pump();
        Check(VideoFrameImport.Times(new(64, 32, 1, false), options()).Length == 4, "Frame interval did not affect the import plan.");
        capture(form, "video-import"); form.Close(false);
        Check(_document.Layers.Count == 2, "Canceling the import form changed the document.");
        var gifSource = new GifSource(Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7"));
        var gifLayer = new ImageLayer(Guid.NewGuid(), gifSource.Decode(0, "Animation"), new(0, 0, 1, 1), "Animation")
        {
            Gif = new GifAnimation(gifSource), Effects = new LayerEffects { Items = [StyleEffect.Default(StyleEffectKind.ColorOverlay)] }
        };
        _document.Layers.Add(gifLayer);
        Click("Flatten All Layer Effects");
        // Every frame takes the effect, not only the one on show; undo puts the live effect back.
        Check(gifLayer.Effects is null && gifLayer.Gif!.Edited.Count == gifLayer.Gif.Frames.Count,
            "Baking animation effects did not reach every GIF frame.");
        Undo(); Check(_document!.Layers.Single(l => l.Name == "Animation").Effects is not null, "Animation effects were not restored by undo.");
        Console.WriteLine("PASS: Scripts dispatch/undo/redo, effect opacity, import menu, video sampling form and cancellation.");
    }
}
