using Avalonia.Platform.Storage;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private async Task ContentAwareScale()
    {
        if (_document is not { } doc || PropertyLayer is not { Asset: { } asset } layer || !LayerProtection.CanPaint(doc, layer.ID) || !LayerProtection.CanMove(doc, layer.ID)) return;
        var revision = _history.CurrentRevision;
        var form = new FormDialog("Content-Aware Scale", height: 380);
        var width = form.Number("Width (px)", asset.Width, 1, DocumentLimits.MaxSide);
        var height = form.Number("Height (px)", asset.Height, 1, DocumentLimits.MaxSide);
        form.Description("Seam carving preserves high-contrast features. Large changes can distort faces and repeated patterns.");
        form.Validate = () => width.Value * height.Value > DocumentLimits.MaxSurfacePixels ? "The image exceeds the pixel budget." : null;
        if (!await form.ShowDialog<bool>(this)) return;
        try
        {
            using var pixels = await Task.Run(() => AdvancedEdit.ContentAwareResize(asset.WorkingImage, (int)(width.Value ?? 1), (int)(height.Value ?? 1)));
            if (!ReferenceEquals(doc, _document) || revision != _history.CurrentRevision) return;
            Edit("Content-Aware Scale", () =>
            {
                layer.Asset = asset.BitDepth > 8 ? ImportedImage.CreateDeep(pixels.Copy(), asset.Name, asset.BitDepth) : ImportedImage.Create(DeepPixels.Preview(pixels), asset.Name);
                var t = layer.Transform; TransformEdits.SetPlacement(layer, t with { Width = t.Width * pixels.Width / asset.Width, Height = t.Height * pixels.Height / asset.Height });
                layer.Text = null; layer.Shape = null; return true;
            });
        }
        catch (Exception error) { Say(Localize.Text("Content-aware scaling failed.") + " " + error.Message); }
    }
    private static SKBitmap RenderOne(CanvasDocument document, ImageLayer layer)
    {
        var copy = document.Clone(); copy.Layers.Clear(); var item = layer.Clone(); item.ParentID = null; item.IsVisible = true; copy.Layers.Add(item);
        return DocumentRenderer.RenderDeep(copy);
    }
    private async Task AutoAlignLayers()
    {
        if (_document is not { } doc) return;
        var layers = doc.Layers.Where(l => SelectedLayers.Contains(l.ID) && l.Asset is not null && LayerProtection.CanMove(doc, l.ID)).ToArray();
        if (layers.Length < 2) { Say("Select at least two unlocked pixel layers."); return; }
        var revision = _history.CurrentRevision;
        var form = new FormDialog("Auto-Align Layers", height: 330);
        form.Description("Translation alignment uses the first selected layer as the reference. Rotation and perspective are not changed.");
        var range = form.Number("Maximum Shift (px)", Math.Max(doc.Width, doc.Height) / 10, 1, Math.Max(doc.Width, doc.Height) / 2);
        if (!await form.ShowDialog<bool>(this)) return;
        try
        {
            using var reference = RenderOne(doc, layers[0]);
            var offsets = new List<(ImageLayer Layer, SKPoint Offset)>();
            foreach (var layer in layers.Skip(1))
            { using var image = RenderOne(doc, layer); offsets.Add((layer, await Task.Run(() => AdvancedEdit.AlignTranslation(reference, image, (int)(range.Value ?? 100))))); }
            if (!ReferenceEquals(doc, _document) || revision != _history.CurrentRevision) return;
            Edit("Auto-Align Layers", () => { foreach (var (layer, delta) in offsets) TransformEdits.SetPlacement(layer, layer.Transform with { X = layer.Transform.X + delta.X, Y = layer.Transform.Y + delta.Y }); return true; });
        }
        catch (Exception error) { Say(error.Message); }
    }
    private async Task AutoBlendLayers()
    {
        if (_document is not { } doc) return;
        var layers = doc.Layers.Where(l => SelectedLayers.Contains(l.ID) && l.Asset is not null).ToArray();
        if (layers.Length < 2) { Say("Select at least two unlocked pixel layers."); return; }
        if ((long)doc.Width * doc.Height * layers.Length > 32_000_000) { Say("Blending is limited to 32 million source pixels per operation."); return; }
        var revision = _history.CurrentRevision; var form = new FormDialog("Auto-Blend Layers", height: 330);
        var mode = form.Choice("Method", ["Average Stack", "Focus Stack"], 0);
        form.Description("Align the layers first. The blend is added as a new layer; source layers remain editable.");
        if (!await form.ShowDialog<bool>(this)) return;
        var images = new List<SKBitmap>();
        try
        {
            foreach (var layer in layers) images.Add(RenderOne(doc, layer));
            using var result = await Task.Run(() => AdvancedEdit.Blend(images, mode.SelectedIndex == 1));
            if (!ReferenceEquals(doc, _document) || revision != _history.CurrentRevision) return;
            Guid? made = null;
            Edit("Auto-Blend Layers", () =>
            {
                using var preview = DeepPixels.Preview(result);
                made = LayerPlacement.AddImage(doc, preview.Copy(), SKRectI.Create(doc.Width, doc.Height), Localize.Text("Blended Layers"), Selected);
                if (made is not { } id) return false;
                var target = doc.Layers.First(l => l.ID == id); target.Asset = ImportedImage.CreateDeep(result.Copy(), target.Name, 32);
                foreach (var layer in layers) layer.IsVisible = false;
                return true;
            });
            Reselect(made);
        }
        catch (Exception error) { Say(error.Message); }
        finally { foreach (var image in images) image.Dispose(); }
    }
    private async Task RemoveReflections()
    {
        if (_document is not { } doc) return;
        var revision = _history.CurrentRevision;
        using var reference = DocumentRenderer.Render(doc);
        var form = new GenerativeDialog(true, reference: PngCodec.Encode(reference), prompt: "Remove glass reflections while preserving the scene behind the glass, framing, colors and all other details. Return the same image dimensions.");
        if (await form.ShowDialog<GenerativeDialog.Result?>(this) is { } result && ReferenceEquals(doc, _document) && revision == _history.CurrentRevision) ImportGenerated(result);
    }
    private async Task SkyReplacement()
    {
        if (_document is not { } doc || doc.Selection.Path is null || doc.Selection.IsEmpty) { Say("Select the sky before choosing its replacement."); return; }
        var selection = doc.Selection; var revision = _history.CurrentRevision;
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Choose Sky Image"), FileTypeFilter = [FilePickerFileTypes.ImageAll] });
        if (picked.FirstOrDefault()?.TryGetLocalPath() is not { } path || !ReferenceEquals(doc, _document)) return;
        try
        {
            using var image = SKBitmap.Decode(path); if (image is null) return;
            using var sky = image.Resize(Bitmaps.ColorInfo(doc.Width, doc.Height), new SKSamplingOptions(SKFilterMode.Linear));
            if (sky is null || revision != _history.CurrentRevision) return;
            Guid? made = null;
            Edit("Sky Replacement", () =>
            {
                made = LayerPlacement.AddImage(doc, sky.Copy(), SKRectI.Create(doc.Width, doc.Height), Localize.Text("Sky Replacement"), Selected);
                return FinishMaskedImage(doc, ref made, selection);
            });
            Reselect(made); _options.PaintOnMask = true; Refresh();
        }
        catch (Exception error) { Say(error.Message); }
    }
}
