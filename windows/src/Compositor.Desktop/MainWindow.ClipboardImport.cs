using Avalonia.Input.Platform;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private async Task PasteToNewCanvas()
    {
        var tab = _open;
        try
        {
            using var bitmap = Clipboard is { } clipboard ? await clipboard.TryGetBitmapAsync() : null;
            if (_document is not null || !ReferenceEquals(tab, _open)) return;
            if (bitmap is not null)
            {
                if (bitmap.PixelSize.Width > DocumentLimits.MaxSide || bitmap.PixelSize.Height > DocumentLimits.MaxSide
                    || (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height > DocumentLimits.MaxSurfacePixels) { Say("The clipboard image exceeds the pixel budget."); return; }
                using var stream = new MemoryStream(); bitmap.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions()); var asset = DeepImageCodec.Decode(stream.ToArray(), Localize.Text("Clipboard"));
                AdoptImported(ImageImporter.NewDocument(asset), "Paste"); SetTool(Tool.Move); return;
            }
            if (_layerClipboard is { } layers) { AdoptImported(layers.NewDocument(), "Paste Layers"); return; }
            if (_clipboard is { } pixels) { AdoptImported(ImageImporter.NewDocument(ImportedImage.Create(pixels.Image.Copy(), Localize.Text("Clipboard"))), "Paste"); return; }
            Say("There is nothing to paste");
        }
        catch (Exception error) { Say(Localize.Text("Could not paste image.") + " " + error.Message); }
    }
}
