using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private LayerStylePreset? _copiedStyle;
    private readonly Dictionary<Tab, (Tab Parent, Guid SmartID)> _smartTabs = [];
    private bool _styleOpen;
    private async Task EditLayerStyle(StyleEffectKind? kind = null)
    {
        if (_styleOpen || _document is not { } document || Selected is not { } id
            || document.Layers.FirstOrDefault(l => l.ID == id) is not { } layer
            || LayerProtection.Effective(document, id).HasFlag(LayerLocks.All)) return;
        _styleOpen = true; var original = LayerStyleDialog.Capture(layer);
        var originalEffects = layer.Effects; var originalBlending = layer.Blending;
        (LayerStylePreset Style, string Name)? result = null;
        try
        {
            result = await LayerStyleDialog.Ask(this, layer, style =>
            {
                LayerStyleDialog.Apply(layer, style ?? original); _canvas.InvalidateVisual();
            }, kind);
        }
        catch (Exception error) { Say(Localize.Text("Could not edit layer style.") + " " + error.Message); }
        finally { LayerStyleDialog.Apply(layer, original); layer.Effects = originalEffects; layer.Blending = originalBlending; _styleOpen = false; _canvas.InvalidateVisual(); }
        if (result is { } chosen && ReferenceEquals(document, _document))
            Edit("Layer Style", () => { LayerStyleDialog.Apply(layer, chosen.Style); layer.Name = chosen.Name; return true; });
        Reselect(id);
    }
    private void CopyLayerStyle()
    {
        if (_document?.Layers.FirstOrDefault(l => l.ID == Selected) is not { } layer) return;
        _copiedStyle = LayerStyleDialog.Capture(layer); Say("Layer style copied");
    }
    private void PasteLayerStyle()
    {
        if (_copiedStyle is null || _document is not { } doc) return;
        var ids = SelectedLayers;
        Edit("Paste Layer Style", () => { foreach (var layer in doc.Layers.Where(l => ids.Contains(l.ID) && !LayerProtection.Effective(doc, l.ID).HasFlag(LayerLocks.All))) LayerStyleDialog.Apply(layer, _copiedStyle); return true; });
        ShowLayers(doc);
    }
    private void ToggleEffects()
    {
        if (_document is not { } doc) return; var ids = SelectedLayers;
        var enable = doc.Layers.Where(l => ids.Contains(l.ID)).Any(l => l.Effects is { Enabled: false });
        Edit("Show / Hide Layer Effects", () => { foreach (var layer in doc.Layers.Where(l => ids.Contains(l.ID) && l.Effects is not null)) { var copy = layer.Effects!.Scaled(1); copy.Enabled = enable; layer.Effects = copy; } return true; }); ShowLayers(doc);
    }
    private async Task ScaleLayerEffects()
    {
        if (_document is not { } doc) return; var ids = SelectedLayers;
        var text = await TextPrompt.Ask(this, "Scale Effects", "Scale (%)", "100");
        if (!double.TryParse(text, out var scale) || !double.IsFinite(scale) || scale < 1 || scale > 1000) return;
        Edit("Scale Effects", () => { foreach (var layer in doc.Layers.Where(l => ids.Contains(l.ID))) layer.Effects = layer.Effects?.Scaled(scale / 100); return true; }); ShowLayers(doc);
    }
    private void PopulateLayerContext(ContextMenu menu, ImageLayer layer)
    {
        var doc = _document; if (doc is null) return;
        menu.MaxHeight = Math.Max(320, Bounds.Height - 70);
        var ids = SelectedLayers; var editable = LayerWorkflow.Editable(doc, ids);
        MenuItem Add(string title, Action action, string? key = null, bool enabled = true)
        { var item = Command(title, action, key); item.IsEnabled = enabled; menu.Items.Add(item); return item; }
        void Separator() => menu.Items.Add(new Separator());
        var clean = new MenuItem { Header = Localize.Text("Clean Up Layers") };
        foreach (var (title, code) in new[] { ("Delete Empty Layers", "Empty"), ("Delete Empty Groups", "Groups"), ("Delete Hidden Layers", "Hidden") })
            clean.Items.Add(Command(title, () => _ = CleanLayers(code)));
        menu.Items.Add(clean); Separator();
        Add("New Layer…", () => _ = NewNamedLayer(), "New Blank Layer");
        Add("New Group", GroupSelected, "Group Layers", editable);
        Add("Duplicate Layer", DuplicateLayer, "Duplicate Layer"); Add("Delete Layer", DeleteLayer, "Delete Layer", editable); Separator();
        Add("Quick Export as PNG", () => _ = ExportSelectedLayers(true), "Quick Export Layers PNG");
        Add("Export Layers As…", () => _ = ExportSelectedLayers(false), "Export Layers As"); Separator();
        Add("Merge Layers", MergeLayers, "Merge Layers", editable && LayerMerge.Plan(doc, ids, Selected) is not null);
        Add("Merge Visible", () => MergeAll(false), "Merge Visible", LayerWorkflow.Editable(doc, doc.EffectiveVisibleIDs()));
        Add("Flatten Image", () => _ = FlattenImage(), null, LayerWorkflow.Editable(doc, doc.Layers.Select(l => l.ID))); Separator();
        Add(layer.Locks.HasFlag(LayerLocks.All) ? "Unlock Layer" : "Lock Layer", ToggleLayerLock, "Lock Layer");
        Add("Lock Layers…", () => _ = LayerLocksDialog(), "Lock Layers Dialog");
        Add("Rename Layer…", () => _ = RenameLayer(), "Rename Layer", editable);
        Add(layer.IsVisible ? "Hide Layers" : "Show Layers", ToggleSelectedVisibility, "Hide Layers"); Separator();
        Add("Blending Options…", () => _ = EditLayerStyle(), null, editable);
        Add("Copy Layer Style", CopyLayerStyle); Add("Paste Layer Style", PasteLayerStyle, null, editable && _copiedStyle is not null);
        Add("Clear Layer Style", ClearSelectedStyles, null, editable); Add("Show / Hide Layer Effects", ToggleEffects, null, editable && layer.Effects is not null);
        Add("Scale Effects…", () => _ = ScaleLayerEffects(), null, editable && layer.Effects is not null); Separator();
        Add("New Group from Layers…", () => _ = GroupFromLayers(), null, editable);
        Add("Ungroup Layers", UngroupSelected, "Ungroup Layers", editable && ids.Any(id => doc.Layers.Any(l => l.ID == id && l.IsGroup)));
        Add("Collapse All Groups", () => { foreach (var group in doc.Layers.Where(l => l.IsGroup)) _open.Collapsed.Add(group.ID); ShowLayers(doc); });
        Add("Expand All Groups", () => { _open.Collapsed.Clear(); ShowLayers(doc); });
        Add("Frame from Layers…", () => _ = MakeLayerContainer(LayerContainer.Frame, true), null, editable);
        if (layer.Container == LayerContainer.Frame) Add("Place Image in Frame…", () => _ = PlaceInFrame(layer.ID), null, editable);
        Separator();
        Add("New Artboard…", () => _ = MakeLayerContainer(LayerContainer.Artboard, false));
        Add("Artboard from Layers…", () => _ = MakeLayerContainer(LayerContainer.Artboard, true), null, editable);
        if (layer.Container != LayerContainer.Group) Add("Edit Frame / Artboard…", () => _ = EditContainer(layer.ID), null, editable);
        Separator();
        Add("Convert to Smart Object", ConvertToSmartObject, null, editable);
        if (layer.SmartObject is not null) { Add("Edit Smart Object Contents", EditSmartContents, null, editable); Add("New Smart Object via Copy", IndependentSmartCopy); }
        Add("Rasterize Layer", () => { Edit("Rasterize Layer", () => LayerWorkflow.Rasterize(doc, ids)); ShowLayers(doc); }, null, editable && ids.Any(id => doc.Layers.Any(l => l.ID == id && (l.SmartObject is not null || l.LiveText is not null || l.LiveShape is not null))));
        Add("Mask All Objects", () => _ = MaskAllObjects(), null, editable && layer.Asset is not null); Separator();
        Add(layer.MaskSourceID is null ? "Create Clipping Mask" : "Release Clipping Mask", ToggleClipping, "Toggle Clipping Mask", editable);
        Add("Link / Unlink Layers", LinkLayers); Separator();
        Add("Copy CSS", () => _ = CopyLayerMarkup(false)); Add("Copy SVG", () => _ = CopyLayerMarkup(true)); Separator();
        var colors = new MenuItem { Header = Localize.Text("Color") };
        foreach (var color in Enum.GetValues<LayerLabel>())
        {
            var item = new MenuItem { Header = Localize.Text(color.ToString()), ToggleType = MenuItemToggleType.Radio, IsChecked = layer.Label == color };
            item.Click += (_, _) => { Edit("Layer Color", () => { foreach (var l in doc.Layers.Where(l => ids.Contains(l.ID))) l.Label = color; return true; }); ShowLayers(doc); }; colors.Items.Add(item);
        }
        menu.Items.Add(colors);
    }
    private async Task NewNamedLayer()
    {
        if (_document is not { } doc) return;
        var name = await TextPrompt.Ask(this, "New Layer", "Name", Localize.Text("Layer"));
        if (string.IsNullOrWhiteSpace(name)) return;
        Guid? made = null; Edit("New Blank Layer", () => { made = LayerPlacement.AddBlank(doc, Selected); if (made is null) return false; doc.Layers.Single(l => l.ID == made).Name = name.Trim(); return true; }); Reselect(made);
    }
    private async Task GroupFromLayers()
    {
        if (_document is not { } doc) return; var ids = SelectedLayers;
        var name = await TextPrompt.Ask(this, "New Group from Layers", "Name", Localize.Text("Group")); if (string.IsNullOrWhiteSpace(name)) return;
        Guid? made = null; Edit("Group Layers", () => { made = LayerPlacement.GroupSelected(doc, ids); if (made is null) return false; doc.Layers.Single(l => l.ID == made).Name = name.Trim(); return true; }); Reselect(made);
    }
    private void ToggleSelectedVisibility()
    { if (_document is not { } doc) return; var ids = SelectedLayers; var show = !doc.Layers.Where(l => ids.Contains(l.ID)).All(l => l.IsVisible); Edit("Layer Visibility", () => { foreach (var l in doc.Layers.Where(l => ids.Contains(l.ID))) l.IsVisible = show; return true; }); ShowLayers(doc); }
    private void ClearSelectedStyles()
    { if (_document is not { } doc) return; var ids = SelectedLayers; Edit("Clear Layer Style", () => { foreach (var l in doc.Layers.Where(l => ids.Contains(l.ID))) { l.Effects = null; l.Blending = null; } return true; }); ShowLayers(doc); }
    private async Task CleanLayers(string kind)
    {
        if (_document is not { } doc) return;
        if (kind == "Hidden" && !await ConfirmDialog.Ask(this, "Delete Hidden Layers", "Delete hidden layers and their contents? You can undo this operation.", "Delete", "Cancel")) return;
        Edit("Clean Up Layers", () => LayerWorkflow.Clean(doc, kind) > 0); ShowLayers(doc);
    }
    private void MergeAll(bool flatten)
    { if (_document is not { } doc) return; Guid? made = null; Edit(flatten ? "Flatten Image" : "Merge Visible", () => { made = LayerWorkflow.MergeVisible(doc, flatten); if (made is null) return false; doc.Layers.Single(l => l.ID == made).Name = Localize.Text(flatten ? "Background" : "Merged Visible"); return true; }); if (made is { } id) Reselect(id); }
    private async Task FlattenImage()
    {
        if (_document is not { } doc) return;
        if (doc.Layers.Any(l => !l.IsVisible) && !await ConfirmDialog.Ask(this, "Flatten Image", "Discard hidden layers and flatten the image onto a white background?", "Flatten", "Cancel")) return;
        MergeAll(true);
    }
    private async Task LayerLocksDialog()
    {
        if (_document is not { } doc || doc.Layers.FirstOrDefault(l => l.ID == Selected) is not { } layer) return;
        var ids = SelectedLayers; if (await LayerOperationDialog.Locks(this, layer.Locks) is not { } locks) return;
        Edit("Layer Locks", () => LayerProtection.Set(doc, ids, locks)); ShowLayers(doc);
    }
    private async Task MakeLayerContainer(LayerContainer kind, bool selected)
    {
        if (_document is not { } doc) return; IReadOnlyCollection<Guid> ids = selected ? SelectedLayers : [];
        var bounds = selected ? LayerWorkflow.Bounds(doc, ids) : SKRect.Create(doc.Width, doc.Height);
        var title = kind == LayerContainer.Frame ? "Frame from Layers" : selected ? "Artboard from Layers" : "New Artboard";
        if (await LayerOperationDialog.Container(this, title, Localize.Text(kind == LayerContainer.Frame ? "Frame" : "Artboard"), bounds, kind == LayerContainer.Frame) is not { } asked) return;
        Guid? made = null;
        Edit(title, () =>
        {
            made = LayerWorkflow.MakeContainer(doc, ids, kind, asked.Name, asked.Bounds, asked.Ellipse);
            if (made is null) return false;
            if (kind == LayerContainer.Artboard) { CanvasEdits.Resize(doc, Math.Max(doc.Width, (int)Math.Ceiling(asked.Bounds.Right)), Math.Max(doc.Height, (int)Math.Ceiling(asked.Bounds.Bottom)), 0); }
            return true;
        }); Reselect(made);
    }
    private async Task EditContainer(Guid id)
    {
        if (_document is not { } doc || doc.Layers.FirstOrDefault(l => l.ID == id) is not { } layer) return;
        var t = layer.Transform;
        if (await LayerOperationDialog.Container(this, "Edit Frame / Artboard", layer.Name, SKRect.Create((float)t.X, (float)t.Y, (float)t.Width, (float)t.Height), false) is not { } asked) return;
        Edit("Edit Frame / Artboard", () => { layer.Name = asked.Name; TransformEdits.SetPlacement(layer, new Core.Model.LayerTransform(asked.Bounds.Left, asked.Bounds.Top, asked.Bounds.Width, asked.Bounds.Height)); return true; }); Reselect(id);
    }
    private async Task PlaceInFrame(Guid id)
    {
        if (_document is not { } doc || doc.Layers.FirstOrDefault(l => l.ID == id && l.Container == LayerContainer.Frame) is not { } frame) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Place Image in Frame"), FileTypeFilter = [FilePickerFileTypes.ImageAll] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        try
        {
            var asset = ImageImporter.Decode(path); var t = frame.Transform; var scale = Math.Max(t.Width / asset.Width, t.Height / asset.Height);
            var image = new ImageLayer(Guid.NewGuid(), asset, new Core.Model.LayerTransform(t.X + (t.Width - asset.Width * scale) / 2, t.Y + (t.Height - asset.Height * scale) / 2, asset.Width * scale, asset.Height * scale, t.Rotation), Path.GetFileNameWithoutExtension(path)) { ParentID = id };
            Edit("Place Image in Frame", () => { doc.Layers.Add(image); return true; }); Reselect(image.ID);
        }
        catch (Exception error) { Say(Localize.Text("Could not place image in frame.") + " " + error.Message); }
    }
    private void ConvertToSmartObject()
    {
        if (_document is not { } doc) return; var ids = SelectedLayers; Guid? made = null;
        try { Edit("Convert to Smart Object", () => (made = LayerWorkflow.ConvertToSmartObject(doc, ids)) is not null); Reselect(made); }
        catch (Exception error) { Say(Localize.Text("Could not create smart object.") + " " + error.Message); }
    }
    private void EditSmartContents()
    {
        if (_document?.Layers.FirstOrDefault(l => l.ID == Selected)?.SmartObject is not { } smart) return;
        var parent = _open;
        var existing = _smartTabs.FirstOrDefault(p => p.Value.Parent == parent && p.Value.SmartID == smart.ID);
        if (existing.Key is { } open && _tabs.Contains(open)) { Bring(open); return; }
        try
        {
            var source = smart.Open(); var tab = TabForNew(); tab.Document = source.ToDocument();
            _smartTabs[tab] = (parent, smart.ID); tab.History.Reset(); _open = tab; Show(tab);
            Say("Editing embedded contents. Save to update the parent smart object.");
        }
        catch (Exception error) { Say(Localize.Text("Could not open smart object contents.") + " " + error.Message); }
    }
    private void IndependentSmartCopy()
    {
        if (_document is not { } doc || doc.Layers.FirstOrDefault(l => l.ID == Selected) is not { SmartObject: { } smart } layer) return;
        Guid? made = null; Edit("New Smart Object via Copy", () =>
        {
            var copy = layer.Copy(Guid.NewGuid(), layer.Name + " " + Localize.Text("Copy"), layer.ParentID, layer.MaskSourceID);
            copy.SmartObject = new SmartObjectData(Guid.NewGuid(), smart.Package); doc.Layers.Insert(doc.Layers.IndexOf(layer) + 1, copy); made = copy.ID; return true;
        }); Reselect(made);
    }
    private bool SaveSmartContents(Tab tab)
    {
        if (!_smartTabs.TryGetValue(tab, out var target) || tab.Document is null) return false;
        if (!_tabs.Contains(target.Parent) || target.Parent.Document is not { } doc) { Say("The parent document is closed. Use Save As to keep these contents."); return true; }
        target.Parent.History.Begin("Update Smart Object", doc, null);
        bool changed;
        using var before = doc.Clone();
        try { changed = LayerWorkflow.UpdateSmartObject(doc, target.SmartID, tab.Document); }
        catch (Exception error) { doc.Adopt(before); Say($"Could not save: {error.Message}"); return true; }
        finally { target.Parent.History.End(doc, null); RefreshTabs(); }
        if (changed) { tab.History.MarkSaved(); Say("Smart object updated. Save the parent document to keep the change."); }
        else Say("The parent smart object is missing or locked.");
        return true;
    }
    private async Task ExportSelectedLayers(bool quick)
    {
        if (_document is not { } doc) return; var ids = SelectedLayers;
        try
        {
            using var subset = LayerWorkflow.Subset(doc, ids); using var image = DocumentRenderer.Render(subset);
            var bounds = LayerWorkflow.Bounds(doc, ids); // Effects are retained by exporting the rendered alpha bounds.
            var alpha = new int[4]; Core.Pixels.BrushPixels.AlphaBounds(image.GetPixelSpan(), image.Width, image.Height, image.RowBytes, alpha);
            var crop = SKRectI.Create(alpha[0], alpha[1], Math.Max(1, alpha[2] - alpha[0]), Math.Max(1, alpha[3] - alpha[1]));
            using var cropped = Bitmaps.Allocate(Bitmaps.ColorInfo(crop.Width, crop.Height));
            using (var canvas = new SKCanvas(cropped)) canvas.DrawBitmap(image, -crop.Left, -crop.Top, new SKSamplingOptions(SKFilterMode.Nearest));
            byte[] bytes; string extension;
            if (quick) { bytes = PngCodec.Encode(cropped); extension = "png"; }
            else
            {
                using var exportDoc = new CanvasDocument(Guid.NewGuid(), cropped.Width, cropped.Height, doc.Resolution);
                exportDoc.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(cropped.Copy(), "Export"), new Core.Model.LayerTransform(0, 0, cropped.Width, cropped.Height), "Export"));
                if (await ExportAsDialog.Ask(this, exportDoc, ExportFormat.Png) is not { } result) return;
                bytes = result.Bytes; extension = result.Options.Format.ToString().ToLowerInvariant(); if (extension == "jpeg") extension = "jpg";
            }
            var name = doc.Layers.FirstOrDefault(l => l.ID == Selected)?.Name ?? "Layer";
            foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Localize.Text(quick ? "Quick Export as PNG" : "Export Layers As"), SuggestedFileName = name + "." + extension, DefaultExtension = extension, ShowOverwritePrompt = true });
            if (file?.TryGetLocalPath() is { } path) { await File.WriteAllBytesAsync(path, bytes); Say($"Exported {path}"); }
        }
        catch (Exception error) { Say(Localize.Text("Could not export selected layers.") + " " + error.Message); }
    }
    private async Task CopyLayerMarkup(bool svg)
    {
        if (_document is not { } doc || Clipboard is not { } clipboard) return;
        try { await clipboard.SetTextAsync(svg ? LayerWorkflow.Svg(doc, SelectedLayers) : LayerWorkflow.Css(doc, SelectedLayers)); Say(svg ? "SVG copied" : "CSS copied"); }
        catch (Exception error) { Say(Localize.Text("Could not copy layer markup.") + " " + error.Message); }
    }
}
