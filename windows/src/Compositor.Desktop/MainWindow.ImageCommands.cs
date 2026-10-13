using System.Globalization;
using Avalonia;
using Avalonia.Controls;
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
    private async Task ConvertImageMode(ImageMode mode)
    {
        if (_document is not { } doc) return;
        var revision = _history.CurrentRevision; var options = doc.Image with { Mode = mode, Depth = mode is ImageMode.Bitmap or ImageMode.Indexed or ImageMode.Duotone ? 8 : doc.Image.Depth, Palette = null };
        int colors = 256; bool dither = true; double threshold = .5;
        var dialog = new FormDialog("Convert Color Mode", 500, 400);
        dialog.Description(ModeTitle(mode));
        if (mode is ImageMode.Bitmap or ImageMode.Indexed or ImageMode.Duotone or ImageMode.Multichannel)
            dialog.Description("This mode combines the visible image into one layer. Undo restores the original layers.");
        if (mode == ImageMode.Indexed) dialog.Number("Colors", 256, 2, 256, v => colors = (int)v);
        if (mode is ImageMode.Bitmap or ImageMode.Indexed) dialog.Check("Dither", true, v => dither = v);
        if (mode == ImageMode.Bitmap) dialog.Number("Threshold", 128, 0, 255, v => threshold = v / 255);
        if (mode == ImageMode.Duotone) ColorInput(dialog, "Ink Color", options.Ink, v => options = options with { Ink = v });
        if (mode is ImageMode.CMYK or ImageMode.Lab) dialog.Description("The canvas uses an RGB preview. In Export As choose TIFF and Document Mode to write native color channels. Choose CMYK for ICC-managed printing.");
        if (!await dialog.ShowDialog<bool>(this) || !ReferenceEquals(doc, _document) || revision != _history.CurrentRevision) return;
        Edit("Convert Color Mode", () => ImageModeEdits.Convert(doc, options, colors, dither, threshold));
        _open.ActiveAlpha = null; _open.VisibleChannels = ColorChannels.RGB; doc.EditChannels = ColorChannels.RGB; Refresh();
    }
    private async Task ConvertImageDepth(int bits)
    {
        if (_document is not { } doc || doc.Image.Depth == bits) return;
        var revision = _history.CurrentRevision;
        var dialog = new FormDialog("Bits/Channel", 470, 340);
        dialog.Description($"{bits} Bits/Channel");
        dialog.Description(bits < doc.Image.Depth ? "Reducing bit depth discards precision. Undo restores the original." : "Increasing bit depth preserves future edits but cannot recover detail already lost.");
        if (await dialog.ShowDialog<bool>(this) && ReferenceEquals(doc, _document) && revision == _history.CurrentRevision)
            Edit("Bits/Channel", () => ImageModeEdits.Convert(doc, doc.Image with { Depth = bits }));
    }
    private async Task ColorTable()
    {
        if (_document is not { Image.Mode: ImageMode.Indexed } doc) { Say("Convert the document to Indexed Color first."); return; }
        var revision = _history.CurrentRevision; var dialog = new FormDialog("Color Table", 560, 620);
        var text = dialog.Text("Palette (#RRGGBB, one per line)", string.Join('\n', (doc.Image.Palette ?? []).Select(c => $"#{c & 0xFFFFFF:X6}")), true); text.MinHeight = 320;
        uint[]? Parse()
        {
            var lines = (text.Text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length is < 2 or > 256) return null;
            var palette = new List<uint>(); foreach (var line in lines) { if (!SKColor.TryParse(line, out var color)) return null; palette.Add((uint)color); } return palette.ToArray();
        }
        dialog.Validate = () => Parse() is null ? "Enter between 2 and 256 valid colors." : null;
        dialog.Fields.Children.Add(FormDialog.ActionButton("Load Color Table…", async () =>
        {
            var picked = await dialog.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { FileTypeFilter = [new FilePickerFileType("Color table") { Patterns = ["*.act", "*.txt"] }] });
            if (picked.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            try
            {
                if (Path.GetExtension(path).Equals(".act", StringComparison.OrdinalIgnoreCase))
                {
                    var bytes = await File.ReadAllBytesAsync(path); if (bytes.Length is not (768 or 772)) throw new InvalidDataException();
                    var count = bytes.Length == 772 ? bytes[768] * 256 + bytes[769] : 256; if (count == 0) count = 256;
                    if (count is < 2 or > 256) throw new InvalidDataException();
                    text.Text = string.Join('\n', Enumerable.Range(0, count).Select(i => $"#{bytes[i * 3]:X2}{bytes[i * 3 + 1]:X2}{bytes[i * 3 + 2]:X2}"));
                }
                else text.Text = await File.ReadAllTextAsync(path);
            }
            catch { Say("Could not read the color table."); }
        }));
        dialog.Fields.Children.Add(FormDialog.ActionButton("Save Color Table…", async () =>
        {
            if (Parse() is not { } colors) return; var file = await dialog.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = "palette.act", DefaultExtension = ".act" });
            if (file?.TryGetLocalPath() is not { } path) return; var bytes = new byte[772];
            for (var i = 0; i < colors.Length; i++) { bytes[i * 3] = (byte)(colors[i] >> 16); bytes[i * 3 + 1] = (byte)(colors[i] >> 8); bytes[i * 3 + 2] = (byte)colors[i]; }
            bytes[768] = (byte)(colors.Length >> 8); bytes[769] = (byte)colors.Length; bytes[770] = bytes[771] = 255;
            try { await File.WriteAllBytesAsync(path, bytes); } catch { Say("Could not save the color table."); }
        }));
        if (await dialog.ShowDialog<bool>(this) && Parse() is { } palette && ReferenceEquals(doc, _document) && revision == _history.CurrentRevision)
            Edit("Color Table", () => ImageModeEdits.Convert(doc, doc.Image with { Palette = palette }, palette.Length, false));
    }
    private void CropImageSelection()
    {
        if (_document is not { } doc) return;
        if (doc.Selection.Path is null) { SetTool(Tool.Crop); return; }
        var bounds = doc.Selection.CoverageRect(doc.Width, doc.Height);
        Edit("Crop", () => { if (!CanvasEdits.Crop(doc, bounds)) return false; doc.Selection = DocumentSelection.All; return true; }); _canvas.Fit();
    }
    private async Task RotateImageArbitrary()
    {
        if (_document is not { } doc) return; var revision = _history.CurrentRevision;
        var dialog = new FormDialog("Rotate Canvas", 450, 330); var angle = dialog.Number("Angle (degrees clockwise)", 0, -36000, 36000);
        dialog.Description("The canvas expands to contain the rotated image. Non-orthogonal rotation removes axis-aligned guides.");
        if (await dialog.ShowDialog<bool>(this) && ReferenceEquals(doc, _document) && revision == _history.CurrentRevision)
        { Edit("Rotate Canvas", () => ImageGeometry.Rotate(doc, (double)(angle.Value ?? 0))); _canvas.Fit(); }
    }
    private async Task DuplicateImage()
    {
        if (_document is not { } doc) return; var dialog = new FormDialog("Duplicate Image", 480, 350);
        var name = dialog.Text("Name", _open.Name + " " + Localize.Text("Copy")); var merged = dialog.Check("Duplicate Merged Layers Only", false);
        if (!await dialog.ShowDialog<bool>(this)) return;
        var copy = merged.IsChecked == true ? ImageImporter.NewDocument(ImportedImage.Create(DocumentRenderer.Render(doc), name.Text ?? "Copy"), doc.Resolution) : DocumentCopies.Independent(doc);
        AdoptImported(copy, "Duplicate Image"); _open.CustomName = name.Text; RefreshTabs();
    }
    private async Task ChannelCalculation(bool calculation)
    {
        if (_document is not { } doc || !calculation && PropertyLayer is not { Asset: not null }) return;
        var layerId = Selected; var revision = _history.CurrentRevision; var title = calculation ? "Calculations" : "Apply Image";
        var dialog = new FormDialog(title, 580, 720); var documents = _tabs.Where(t => t.Document is not null).ToArray();
        Func<ChannelSource> Source(string label)
        {
            dialog.Description(label); var selectedDoc = Array.FindIndex(documents, t => ReferenceEquals(t.Document, doc));
            var documentBox = dialog.Choice("Document", documents.Select(t => t.Name), selectedDoc);
            var layers = new ComboBox(); dialog.Row("Layer", layers); var channels = new ComboBox(); dialog.Row("Channel", channels);
            var invert = dialog.Check("Invert", false); List<Guid?> layerIDs = []; List<Guid?> alphaIDs = [];
            void Fill()
            {
                var source = documents[Math.Max(0, documentBox.SelectedIndex)].Document!;
                layerIDs = [null, .. source.Layers.Select(l => (Guid?)l.ID)]; layers.ItemsSource = new[] { Localize.Text("Merged") }.Concat(source.Layers.Select(l => l.Name)).ToArray(); layers.SelectedIndex = 0;
                alphaIDs = Enumerable.Repeat<Guid?>(null, 6).Concat(source.Channels.Select(c => (Guid?)c.ID)).ToList();
                channels.ItemsSource = new[] { "RGB", "Red", "Green", "Blue", "Alpha", "Gray" }.Select(Localize.Text).Concat(source.Channels.Select(c => c.Name)).ToArray(); channels.SelectedIndex = calculation ? 5 : 0;
            }
            documentBox.SelectionChanged += (_, _) => Fill(); Fill();
            return () => new ChannelSource(documents[Math.Max(0, documentBox.SelectedIndex)].Document!, layerIDs[Math.Max(0, layers.SelectedIndex)],
                (ImageChannel)Math.Clamp(channels.SelectedIndex, 0, 5), invert.IsChecked == true, alphaIDs[Math.Max(0, channels.SelectedIndex)]);
        }
        var first = Source("Source 1"); var second = calculation ? Source("Source 2") : null;
        var blend = dialog.Choice("Blending", Enum.GetNames<ChannelBlend>(), (int)ChannelBlend.Multiply);
        var opacity = dialog.Number("Opacity (%)", 100, 0, 100); var scale = dialog.Number("Scale", 1, 1, 2); var offset = dialog.Number("Offset", 0, -255, 255);
        var result = calculation ? dialog.Choice("Result", ["New Channel", "Selection", "New Document"], 0) : null;
        var preserve = !calculation ? dialog.Check("Preserve Transparency", true) : null;
        var useMask = !calculation ? dialog.Check("Use Source Mask", false) : null;
        var maskStart = dialog.Fields.Children.Count;
        var maskSource = !calculation ? Source("Source Mask") : null;
        var maskFields = dialog.Fields.Children.Skip(maskStart).ToArray();
        foreach (var field in maskFields) field.IsVisible = false;
        if (useMask is not null) useMask.IsCheckedChanged += (_, _) => { foreach (var field in maskFields) field.IsVisible = useMask.IsChecked == true; };
        dialog.Validate = () => first().Document.Width != doc.Width || first().Document.Height != doc.Height || second is not null && (second().Document.Width != doc.Width || second().Document.Height != doc.Height)
            || useMask?.IsChecked == true && (maskSource!().Document.Width != doc.Width || maskSource().Document.Height != doc.Height)
            ? "Source images must have the same pixel dimensions." : null;
        if (!await dialog.ShowDialog<bool>(this) || !ReferenceEquals(doc, _document) || revision != _history.CurrentRevision) return;
        var settings = new CalculationSettings((ChannelBlend)blend.SelectedIndex, (double)(opacity.Value ?? 100) / 100, (double)(scale.Value ?? 1), (double)(offset.Value ?? 0), preserve?.IsChecked != false);
        try
        {
            if (!calculation)
            { Edit(title, () => ChannelCalculations.Apply(doc, layerId!.Value, first(), settings, useMask?.IsChecked == true ? maskSource!() : null)); return; }
            using var pixels = ChannelCalculations.Calculate(first(), second!(), settings); using var gray = Bitmaps.Allocate(Bitmaps.MaskInfo(doc.Width, doc.Height));
            for (var y = 0; y < gray.Height; y++) for (var x = 0; x < gray.Width; x++) gray.GetPixelSpan()[y * gray.RowBytes + x] = pixels.GetPixel(x, y).Red;
            if (result!.SelectedIndex == 2) AdoptImported(ImageImporter.NewDocument(ImportedImage.Create(gray.Copy(), Localize.Text("Calculations")), doc.Resolution), title);
            else Edit(title, () =>
            {
                if (result.SelectedIndex == 1) doc.Selection = DocumentSelection.FromCoverage(gray.Copy());
                else { if (doc.Channels.Count >= 64) return false; doc.Channels.Add(new AlphaChannel(Guid.NewGuid(), Localize.Text("Calculations"), ImportedImage.Create(gray.Copy(), "Alpha"))); }
                return true;
            });
        }
        catch (Exception e) { Say(Localize.Text("Calculation failed.") + " " + e.Message); }
    }
    private async Task DefineImageVariables()
    {
        if (_document is not { Layers.Count: > 0 } doc) return; var revision = _history.CurrentRevision;
        var dialog = new FormDialog("Define Variables", 680, 650); var definitions = doc.Variables.ToList();
        var list = new ListBox { MinHeight = 160 }; dialog.Fields.Children.Add(list);
        void RefreshDefinitions() => list.ItemsSource = definitions.Select(v => $"{v.Name} — {doc.Layers.FirstOrDefault(l => l.ID == v.LayerID)?.Name} — {Localize.Text(v.Kind == VariableKind.Text ? "Text Replacement" : v.Kind == VariableKind.PixelReplacement ? "Pixel Replacement" : "Visibility")}").ToArray();
        RefreshDefinitions(); var name = dialog.Text("Variable Name", "variable1"); var layers = dialog.Choice("Layer", doc.Layers.Select(l => l.Name), Math.Max(0, doc.Layers.FindIndex(l => l.ID == Selected)));
        var kind = dialog.Choice("Variable Type", ["Visibility", "Text Replacement", "Pixel Replacement"], 0);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap }; dialog.Fields.Children.Add(error);
        dialog.Fields.Children.Add(FormDialog.ActionButton("Add or Update Variable", () =>
        {
            var key = name.Text?.Trim() ?? ""; var layer = doc.Layers[Math.Max(0, layers.SelectedIndex)];
            if (key.Length is < 1 or > 128 || key.Equals("name", StringComparison.OrdinalIgnoreCase) || key.Any(c => c is '\n' or '\r' or ',' or '\t') || kind.SelectedIndex == 1 && layer.LiveText is null)
            { error.Text = Localize.Text("Use a unique variable name. Text replacement needs a type layer."); return; }
            definitions.RemoveAll(v => v.Name.Equals(key, StringComparison.OrdinalIgnoreCase)); definitions.Add(new(key, layer.ID, (VariableKind)kind.SelectedIndex)); error.Text = ""; RefreshDefinitions();
        }));
        dialog.Fields.Children.Add(FormDialog.ActionButton("Delete Variable", () => { if (list.SelectedIndex >= 0) { definitions.RemoveAt(list.SelectedIndex); RefreshDefinitions(); } }));
        if (await dialog.ShowDialog<bool>(this) && ReferenceEquals(doc, _document) && revision == _history.CurrentRevision)
            Edit("Define Variables", () => { doc.Variables.Clear(); doc.Variables.AddRange(definitions); return true; });
    }
    private async Task ImageDataSets(bool apply)
    {
        if (_document is not { } doc) return; var revision = _history.CurrentRevision;
        var dialog = new FormDialog(apply ? "Apply Data Set" : "Data Sets", 630, 620); var sets = doc.DataSets.ToList();
        var list = new ListBox { MinHeight = 150 }; var preview = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 150 };
        dialog.Fields.Children.Add(list); dialog.Fields.Children.Add(preview);
        void Fill() { list.ItemsSource = sets.Select(d => d.Name).ToArray(); if (sets.Count > 0) list.SelectedIndex = 0; }
        list.SelectionChanged += (_, _) => preview.Text = list.SelectedIndex >= 0 ? string.Join('\n', sets[list.SelectedIndex].Values.Select(v => v.Key + " = " + v.Value)) : ""; Fill();
        dialog.Description("CSV/TSV: first row contains variable names; optional name column names each data set. Pixel replacement paths are resolved against the imported file.");
        dialog.Fields.Children.Add(FormDialog.ActionButton("Import Data Sets…", async () =>
        {
            var picked = await dialog.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { FileTypeFilter = [new FilePickerFileType("Data sets") { Patterns = ["*.csv", "*.tsv", "*.txt"] }] });
            if (picked.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            try
            {
                sets = ImageVariables.ParseCsv(await File.ReadAllTextAsync(path)).Select(set => set with { Values = set.Values.ToDictionary(v => v.Key,
                    v => doc.Variables.Any(variable => variable.Name == v.Key && variable.Kind == VariableKind.PixelReplacement) ? Path.GetFullPath(v.Value, Path.GetDirectoryName(path)!) : v.Value) }).ToList(); Fill();
            }
            catch (Exception e) { preview.Text = Localize.Text("Could not import data sets.") + " " + e.Message; }
        }));
        dialog.Fields.Children.Add(FormDialog.ActionButton("Delete Data Set", () => { if (list.SelectedIndex >= 0) { sets.RemoveAt(list.SelectedIndex); Fill(); } }));
        var applyNow = dialog.Check("Apply Selected Data Set", apply);
        dialog.Validate = () => applyNow.IsChecked == true && list.SelectedIndex < 0 ? "Choose a data set." : null;
        if (!await dialog.ShowDialog<bool>(this) || !ReferenceEquals(doc, _document) || revision != _history.CurrentRevision) return;
        try { Edit("Data Sets", () => { doc.DataSets.Clear(); doc.DataSets.AddRange(sets); return applyNow.IsChecked != true || ImageVariables.Apply(doc, sets[list.SelectedIndex], Environment.CurrentDirectory); }); }
        catch (Exception e) { Say(Localize.Text("Could not apply data set.") + " " + e.Message); }
    }
    private MenuItem BuildAnalysisMenu()
    {
        var menu = new MenuItem { Header = Localize.Text("Analysis") };
        var scale = new MenuItem { Header = Localize.Text("Set Measurement Scale") };
        scale.Items.Add(Command("Default (1 pixel)", () => { if (_document is { } d) Edit("Measurement Scale", () => { d.MeasurementScale = new(); return true; }); }));
        scale.Items.Add(Command("Custom…", () => _ = SetMeasurementScale())); menu.Items.Add(scale);
        var fields = new MenuItem { Header = Localize.Text("Select Data Points") };
        fields.Items.Add(Command("All", () => { if (_document is { } d) Edit("Measurement Fields", () => { d.MeasurementFields = ["Area", "Perimeter", "Width", "Height", "Length", "Angle", "Count", "Mean"]; return true; }); }));
        fields.Items.Add(Command("Custom…", () => _ = MeasurementFields())); menu.Items.Add(fields);
        menu.Items.Add(Command("Record Measurements", RecordMeasurements)); menu.Items.Add(Command("Measurement Log…", () => _ = MeasurementLog()));
        menu.Items.Add(new Separator()); menu.Items.Add(Command("Ruler Tool", () => SetTool(Tool.Ruler))); menu.Items.Add(Command("Count Tool", () => SetTool(Tool.Count)));
        menu.Items.Add(new Separator()); menu.Items.Add(Command("Place Scale Marker…", () => _ = PlaceScaleMarker())); return menu;
    }
    private async Task SetMeasurementScale()
    {
        if (_document is not { } doc) return; var revision = _history.CurrentRevision; var dialog = new FormDialog("Measurement Scale", 500, 380);
        var measured = doc.Marks.LastOrDefault(m => m.Kind == MarkKind.Measure); var distance = measured is null ? doc.MeasurementScale.Pixels : Math.Sqrt(measured.Width * measured.Width + measured.Height * measured.Height);
        var pixels = dialog.Number("Pixel Length", distance, .001, 1000000); var units = dialog.Number("Logical Length", doc.MeasurementScale.Units, .001, 1000000);
        var unit = dialog.Text("Logical Units", doc.MeasurementScale.Unit); dialog.Validate = () => string.IsNullOrWhiteSpace(unit.Text) || unit.Text.Length > 64 ? "Enter valid measurement units." : null;
        if (await dialog.ShowDialog<bool>(this) && ReferenceEquals(doc, _document) && revision == _history.CurrentRevision)
            Edit("Measurement Scale", () => { doc.MeasurementScale = new((double)pixels.Value!, (double)units.Value!, unit.Text!.Trim()); return true; });
    }
    private async Task MeasurementFields()
    {
        if (_document is not { } doc) return; var dialog = new FormDialog("Select Data Points", 460, 480); var fields = new[] { "Area", "Perimeter", "Width", "Height", "Length", "Angle", "Count", "Mean" };
        var boxes = fields.Select(f => dialog.Check(f, doc.MeasurementFields.Contains(f))).ToArray();
        if (await dialog.ShowDialog<bool>(this) && ReferenceEquals(doc, _document)) Edit("Measurement Fields", () => { doc.MeasurementFields = fields.Where((_, i) => boxes[i].IsChecked == true).ToArray(); return true; });
    }
    private void RecordMeasurements()
    {
        if (_document is not { } doc) return;
        try { Edit("Record Measurements", () => { if (doc.Measurements.Count >= 10000) return false; doc.Measurements.Add(ImageAnalysis.Measure(doc, Localize.Text("Measurement") + " " + (doc.Measurements.Count + 1))); return true; }); _ = MeasurementLog(); }
        catch (Exception e) { Say(e.Message); }
    }
    private async Task MeasurementLog()
    {
        if (_document is not { } doc) return; var dialog = new FormDialog("Measurement Log", 800, 600);
        var text = dialog.Text("Measurements", ImageAnalysis.Csv(doc), true); text.IsReadOnly = true; text.MinHeight = 350;
        dialog.Fields.Children.Add(FormDialog.ActionButton("Export Measurements…", async () =>
        {
            var file = await dialog.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = "measurements.csv", DefaultExtension = ".csv" });
            if (file?.TryGetLocalPath() is { } path) try { await File.WriteAllTextAsync(path, ImageAnalysis.Csv(doc), new System.Text.UTF8Encoding(true)); } catch { Say("Could not export measurements."); }
        }));
        dialog.Fields.Children.Add(FormDialog.ActionButton("Clear Measurements", () => { if (ReferenceEquals(doc, _document)) { Edit("Clear Measurements", () => { doc.Measurements.Clear(); return true; }); text.Text = ImageAnalysis.Csv(doc); } }));
        await dialog.ShowDialog<bool>(this);
    }
    private async Task PlaceScaleMarker()
    {
        if (_document is not { } doc) return; var dialog = new FormDialog("Scale Marker", 470, 380); var revision = _history.CurrentRevision;
        var units = dialog.Number("Length", Math.Max(.1, doc.Width / 5.0 * doc.MeasurementScale.Units / doc.MeasurementScale.Pixels), .001, 1000000);
        var label = dialog.Text("Label", Localize.Text("Scale") + " (" + doc.MeasurementScale.Unit + ")"); var white = dialog.Check("White", true);
        if (await dialog.ShowDialog<bool>(this) && ReferenceEquals(doc, _document) && revision == _history.CurrentRevision)
            Edit("Scale Marker", () => ImageAnalysis.ScaleMarker(doc, (double)units.Value!, label.Text ?? "", white.IsChecked == true));
    }
    private async Task TrapImage()
    {
        if (_document is not { } doc || PropertyLayer is not { Asset: { } asset } layer) return; var revision = _history.CurrentRevision;
        var dialog = new FormDialog("Trap", 480, 330); var radius = dialog.Number("Width (pixels)", 1, 1, 10);
        dialog.Description("Spread darker neighboring colors at boundaries to reduce gaps between printed color plates. The current selection limits the operation.");
        if (!await dialog.ShowDialog<bool>(this) || !ReferenceEquals(doc, _document) || revision != _history.CurrentRevision) return;
        Edit("Trap", () => ImageTrapping.Apply(doc, layer.ID, (int)radius.Value!));
    }
    private async Task GenerativeUpscale()
    {
        if (_document is not { } doc) return;
        using var composite = DocumentRenderer.Render(doc); var bytes = PngCodec.Encode(composite);
        var dialog = new GenerativeDialog(false, reference: bytes, prompt: $"Upscale this image to twice its original resolution ({doc.Width * 2} x {doc.Height * 2}). Preserve composition, text, colors and details; improve resolution only.");
        if (await dialog.ShowDialog<GenerativeDialog.Result?>(this) is { } result)
        { var image = SKBitmap.Decode(result.Image.Bytes); if (image is not null) AdoptImported(ImageImporter.NewDocument(ImportedImage.Create(image, Localize.Text("Generative Upscale")), doc.Resolution), "Generative Upscale"); }
    }
}
