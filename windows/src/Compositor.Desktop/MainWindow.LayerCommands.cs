using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private void ArrangeLayers(string operation)
    { if (_document is { } doc) Edit(operation, () => LayerMenuEdits.Arrange(doc, SelectedLayers, operation)); }
    private void MakeBackground()
    {
        if (_document is not { } doc || PropertyLayer is not { } layer) return;
        if (doc.Layers.Where(l => l.ID != layer.ID).Sum(l => (long)(l.Asset?.Width ?? 0) * (l.Asset?.Height ?? 0)) + (long)doc.Width * doc.Height > DocumentLimits.DocumentPixelBudget) return;
        Edit("Background from Layer", () =>
        {
            using var isolated = LayerWorkflow.Subset(doc, [layer.ID]); isolated.Image = isolated.Image with { Background = 0xFFFFFFFF };
            var rendered = DocumentRenderer.Render(isolated);
            layer.Asset = ImportedImage.Create(rendered, "Background"); layer.Transform = new LayerTransform(0, 0, doc.Width, doc.Height);
            layer.Mask = null; layer.MaskSourceID = null; layer.ParentID = null; layer.Effects = null; layer.Blending = null;
            layer.Text = null; layer.Shape = null; layer.SmartObject = null; layer.Opacity = layer.FillOpacity = 1; layer.BlendMode = LayerBlendMode.Normal;
            layer.Name = Localize.Text("Background"); layer.Locks = LayerLocks.All;
            doc.Layers.Remove(layer); doc.Layers.Insert(0, layer); return true;
        });
    }
    private async Task NewNamedGroup()
    {
        if (_document is not { } doc) return;
        var name = await TextPrompt.Ask(this, "New Group", "Name", Localize.Text("Group")); if (string.IsNullOrWhiteSpace(name)) return;
        Guid? made = null; Edit("New Group", () => { made = LayerPlacement.AddFolder(doc, Selected); if (made is null) return false; doc.Layers.First(l => l.ID == made).Name = name.Trim(); return true; }); Reselect(made);
    }
    private async Task DuplicateNamedLayer()
    {
        if (_document is not { } doc || PropertyLayer is not { } layer) return;
        var name = await TextPrompt.Ask(this, "Duplicate Layer", "Name", layer.Name + " " + Localize.Text("Copy")); if (string.IsNullOrWhiteSpace(name)) return;
        Guid? made = null; Edit("Duplicate Layer", () => { made = LayerEdits.Duplicate(doc, layer.ID); if (made is null) return false; doc.Layers.First(l => l.ID == made).Name = name.Trim(); return true; }); Reselect(made);
    }
    private void LayerViaSelection(bool cut)
    {
        if (_document is not { } doc || Selected is not { } id) return;
        if (doc.Selection.Path is null) { if (!cut) DuplicateLayer(); return; }
        Guid? made = null;
        Edit(cut ? "Layer via Cut" : "Layer via Copy", () =>
        {
            if (!cut) return (made = SelectionClipboard.LayerViaCopy(doc, id, id)) is not null;
            using var before = doc.Clone();
            if (!SelectionClipboard.Cut(doc, id, out var pixels) || pixels is null) return false;
            using (pixels) made = SelectionClipboard.Paste(doc, pixels, id);
            if (made is null) doc.Adopt(before); return made is not null;
        }); Reselect(made);
    }
    private async Task GlobalLight()
    {
        if (_document is not { } doc) return;
        var form = new FormDialog("Global Light", 420, 240); var angle = form.Number("Angle", PropertyLayer?.Effects?.GlobalLightAngle ?? 120, -360, 360);
        if (!await form.ShowDialog<bool>(this)) return;
        Edit("Global Light", () => { var changed = false; foreach (var l in doc.Layers.Where(l => l.Effects is not null && LayerWorkflow.Editable(doc, [l.ID]))) { l.Effects = l.Effects!.Scaled(1); l.Effects.GlobalLightAngle = (double)(angle.Value ?? 120); changed = true; } return changed; });
    }
    private async Task NewFillLayer(StyleEffectKind kind)
    {
        if (_document is not { } doc || doc.Layers.Count >= LayerPlacement.MaxLayers || doc.Layers.Sum(l => (long)(l.Asset?.Width ?? 0) * (l.Asset?.Height ?? 0)) + (long)doc.Width * doc.Height > DocumentLimits.DocumentPixelBudget) return;
        var name = kind == StyleEffectKind.ColorOverlay ? "Solid Color Fill" : kind == StyleEffectKind.GradientOverlay ? "Gradient Fill" : "Pattern Fill";
        using var image = Bitmaps.Allocate(Bitmaps.ColorInfo(doc.Width, doc.Height)); image.Erase(SKColors.White);
        using var temporary = new CanvasDocument(Guid.NewGuid(), doc.Width, doc.Height);
        var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(image.Copy(), name), new LayerTransform(0, 0, doc.Width, doc.Height), Localize.Text(name))
            { Effects = new LayerEffects { Items = [StyleEffect.Default(kind)] } };
        temporary.Layers.Add(layer);
        var result = await LayerStyleDialog.Ask(this, layer, _ => { }, kind); if (result is null || !ReferenceEquals(doc, _document)) return;
        Guid? made = null;
        Edit("New Fill Layer", () =>
        {
            made = LayerPlacement.AddImage(doc, image.Copy(), SKRectI.Create(doc.Width, doc.Height), result.Value.Name, Selected);
            if (made is null) return false; LayerStyleDialog.Apply(doc.Layers.First(l => l.ID == made), result.Value.Style); return true;
        }); Reselect(made);
    }
    private async Task EditLayerContents()
    {
        if (PropertyLayer is not { } layer) return;
        if (layer.Adjustment is not null) await EditAdjustment();
        else if (layer.LiveText is not null) await EditText();
        else if (layer.Effects is not null) await EditLayerStyle(layer.Effects.Items?.FirstOrDefault()?.Kind);
        else OpenInspector(0);
    }
    private void LayerMaskCommand(string operation)
    {
        if (_document is not { } doc || PropertyLayer is not { } layer || !LayerWorkflow.Editable(doc, [layer.ID])) return;
        Edit("Layer Mask", () => operation switch
        {
            "Reveal" => LayerMaskEdits.Add(doc, layer.ID, true), "Hide" => LayerMaskEdits.Add(doc, layer.ID, false),
            "Selection" => MaskProperties.FromSelection(doc, layer.ID, false),
            "Inverse" => MaskProperties.FromSelection(doc, layer.ID, false) && MaskProperties.Invert(doc, layer.ID),
            "Transparency" => LayerMenuEdits.TransparencyMask(doc, layer.ID), "Apply" => MaskProperties.Apply(doc, layer.ID),
            "Toggle" => layer.Mask is { } mask && LayerMaskEdits.SetEnabled(doc, layer.ID, !mask.IsEnabled),
            "Link" => layer.Mask is { } linked && LayerMaskEdits.SetLinked(doc, layer.ID, !linked.IsLinked),
            "Rasterize" => RasterizeVectorMask(layer), _ => false,
        }); Reselect(layer.ID);
    }
    private static bool RasterizeVectorMask(ImageLayer layer)
    { if (layer.Mask?.VectorPath is null) return false; layer.Mask.VectorPath = null; return true; }
    private void VectorMaskCommand(string operation)
    {
        if (_document is not { } doc || PropertyLayer is not { } layer || !LayerWorkflow.Editable(doc, [layer.ID])) return;
        Edit("Vector Mask", () =>
        {
            using var path = operation == "Path" ? SKPath.ParseSvgPathData((_pen ?? _pathNodes)?.ToSvg()) : null;
            var width = layer.Asset?.Width ?? doc.Width; var height = layer.Asset?.Height ?? doc.Height;
            string svg;
            if (operation == "Path")
            {
                if (path is null) return false;
                if (_pen is null && _canvas.PathToDocument is { } toDocument)
                {
                    // Editable shape coordinates may be normalized; map through the source path's document transform.
                    var a = toDocument(new SKPoint(0, 0)); var b = toDocument(new SKPoint(1, 0)); var c = toDocument(new SKPoint(0, 1));
                    path.Transform(new SKMatrix(b.X - a.X, c.X - a.X, a.X, b.Y - a.Y, c.Y - a.Y, a.Y, 0, 0, 1));
                }
                if (BrushEdits.PixelToDocument(layer.Transform, width, height).TryInvert(out var map)) path.Transform(map); else return false;
                svg = path.ToSvgPathData();
            }
            else svg = operation == "Reveal" ? $"M0 0 H{width} V{height} H0 Z" : "M0 0 Z";
            if (layer.Mask is not null) return false;
            using var validPath = SKPath.ParseSvgPathData(svg); if (validPath is null) return false;
            // SetVectorPath retains the source mask grid, so create a correctly sized raster first.
            var bitmap = Bitmaps.Allocate(Bitmaps.MaskInfo(width, height)); layer.Mask = Core.Model.LayerMask.AssetFrom(bitmap);
            if (MaskProperties.SetVectorPath(doc, layer.ID, svg)) return true;
            layer.Mask = null; return false;
        }); Reselect(layer.ID);
    }
    private void RasterizeSelected(string kind, bool allLayers = false)
    {
        if (_document is not { } doc) return; var ids = kind == "All" || allLayers ? doc.Layers.Select(l => l.ID).ToArray() : SelectedLayers.ToArray();
        Edit("Rasterize Layer", () =>
        {
            var changed = false;
            foreach (var layer in doc.Layers.Where(l => ids.Contains(l.ID) && !l.IsGroup && LayerWorkflow.Editable(doc, [l.ID])).ToArray())
            {
                if (kind == "Type" && layer.LiveText is null || kind == "Shape" && layer.LiveShape is null || kind == "Smart" && layer.SmartObject is null
                    || kind == "Effects" && layer.Effects is null) continue;
                // Effects apply to the whole animation. Baking only its display frame would
                // remove the effect from every other frame and may also change the frame grid.
                if (layer.Gif is not null && layer.Effects is not null && kind is "Effects" or "Layer" or "All") continue;
                if (kind is "Effects" or "Layer" or "All" && layer.Effects is not null)
                {
                    using var isolated = LayerWorkflow.Subset(doc, [layer.ID]); isolated.Image = isolated.Image with { Background = null };
                    var copied = isolated.Layers[0]; copied.IsVisible = true; copied.Opacity = 1; copied.BlendMode = LayerBlendMode.Normal;
                    if (doc.Layers.Where(l => l.ID != layer.ID).Sum(l => (long)(l.Asset?.Width ?? 0) * (l.Asset?.Height ?? 0)) + (long)doc.Width * doc.Height > DocumentLimits.DocumentPixelBudget) continue;
                    var rendered = DocumentRenderer.Render(isolated); layer.Asset = ImportedImage.Create(rendered, layer.Name);
                    layer.Transform = new LayerTransform(0, 0, doc.Width, doc.Height); layer.Effects = null; layer.Mask = null; layer.FillOpacity = 1;
                    changed = true;
                }
                changed |= layer.Text is not null || layer.Shape is not null || layer.SmartObject is not null;
                layer.Text = null; layer.Shape = null; layer.SmartObject = null;
            }
            return changed;
        });
    }
    private void ResetSmartTransform()
    { if (_document is { } doc && PropertyLayer is { SmartObject: not null, Asset: { } asset } layer) Edit("Reset Transform", () => { TransformEdits.SetPlacement(layer, new LayerTransform(layer.Transform.CenterX - asset.Width / 2d, layer.Transform.CenterY - asset.Height / 2d, asset.Width, asset.Height)); return true; }); }
    private async Task ReplaceSmartContents()
    {
        if (_document is not { } doc || PropertyLayer is not { SmartObject: { } smart } layer) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Replace Contents"), AllowMultiple = false });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        try
        {
            using var source = Directory.Exists(path) ? ProjectStore.Load(path).ToDocument() : ImageImporter.NewDocument(await Task.Run(() => ImageImporter.Decode(path)));
            Edit("Replace Contents", () => LayerWorkflow.UpdateSmartObject(doc, smart.ID, source));
        }
        catch (Exception error) { Say(error.Message); }
    }
    private async Task ExportSmartContents()
    {
        if (PropertyLayer is not { SmartObject: { } smart } layer) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Localize.Text("Export Contents"), SuggestedFileName = layer.Name + ".comp", DefaultExtension = "comp" });
        if (file?.TryGetLocalPath() is not { } path) return;
        try { using var snapshot = smart.Open(); ProjectStore.Save(snapshot, path); } catch (Exception error) { Say(error.Message); }
    }
    private void UnpackSmartObject()
    {
        if (_document is { } doc && Selected is { } id) Edit("Convert to Layers", () => LayerMenuEdits.UnpackSmartObject(doc, id));
    }
    private void LayerSlice()
    {
        if (_document is not { } doc) return; var box = LayerWorkflow.Bounds(doc, SelectedLayers);
        Edit("New Layer Based Slice", () => { doc.Marks.Add(new DocumentMark { Kind = MarkKind.Slice, Name = PropertyLayer?.Name ?? "Slice", X = box.Left, Y = box.Top, Width = box.Width, Height = box.Height }); return true; });
    }
    private void CombineLayerShapes(SKPathOp operation)
    { if (_document is not { } doc) return; Guid? made = null; Edit("Combine Shapes", () => (made = LayerMenuEdits.CombineShapes(doc, SelectedLayers, operation)) is not null); Reselect(made); }
    private void SelectLinkedLayers()
    {
        if (_document is not { } doc || PropertyLayer?.LinkID is not { } link) return;
        var linked = doc.Layers.Where(l => l.LinkID == link).Select(l => l.ID).ToHashSet();
        _layers.SelectedItems?.Clear(); foreach (var row in _layers.Items.OfType<ListBoxItem>().Where(r => r.Tag is Guid id && linked.Contains(id)).ToArray()) _layers.SelectedItems?.Add(row);
    }
    private async Task DefringeLayer(bool decontaminate)
    {
        if (_document is not { } doc || Selected is not { } id) return;
        var form = new FormDialog(decontaminate ? "Color Decontaminate" : "Defringe", 420, 240); var radius = form.Number("Width (pixels)", decontaminate ? 3 : 1, 1, 100);
        if (await form.ShowDialog<bool>(this)) Edit(decontaminate ? "Color Decontaminate" : "Defringe", () => LayerMenuEdits.Defringe(doc, id, (int)(radius.Value ?? 1)));
    }
    private void RemoveMatte(bool white)
    { if (_document is { } doc && Selected is { } id) Edit(white ? "Remove White Matte" : "Remove Black Matte", () => LayerMenuEdits.Matte(doc, id, white)); }
}
