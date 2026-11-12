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
        if (_document is not { } doc || doc.Image.Mode == mode) return;
        var revision = _history.CurrentRevision; var options = doc.Image with { Mode = mode, Depth = mode is ImageMode.Bitmap or ImageMode.Indexed or ImageMode.Duotone ? 8 : doc.Image.Depth, Palette = null };
        int colors = 256; bool dither = true; double threshold = .5;
        // Photoshop asks first when there is more than one layer: the flattening modes whether to flatten, the others whether to
        // merge before the change, since blending looks different in another mode (Merge, Cancel, Don't Merge).
        var flatten = mode is ImageMode.Bitmap or ImageMode.Indexed or ImageMode.Duotone or ImageMode.Multichannel;
        var merge = false;
        if (doc.Layers.Count > 1)
        {
            if (flatten)
            {
                if (await AlertDialog.Ask(this, "Flatten image?", [("Flatten", 'F'), ("Cancel", null)], 1) != 0) return;
            }
            else
            {
                var answer = await AlertDialog.Ask(this, "Changing modes can affect the appearance of layers. Merge layers before changing mode?",
                    [("Merge", 'M'), ("Cancel", null), ("Don't Merge", 'D')], 1);
                if (answer is -1 or 1) return;
                merge = answer == 0;
            }
        }
        if (mode is ImageMode.Indexed or ImageMode.Bitmap or ImageMode.Duotone)
        {
            // Only the modes with choices of their own open a dialog, as in Photoshop.
            var dialog = new FormDialog(ModeTitle(mode), 500, 360);
            if (mode == ImageMode.Indexed) dialog.Number("Colors", 256, 2, 256, v => colors = (int)v);
            if (mode is ImageMode.Bitmap or ImageMode.Indexed) dialog.Check("Dither", true, v => dither = v);
            if (mode == ImageMode.Bitmap) dialog.Number("Threshold", 128, 0, 255, v => threshold = v / 255);
            if (mode == ImageMode.Duotone) ColorInput(dialog, "Ink Color", options.Ink, v => options = options with { Ink = v });
            if (!await dialog.ShowDialog<bool>(this)) return;
        }
        if (!ReferenceEquals(doc, _document) || revision != _history.CurrentRevision) return;
        Edit("Convert Color Mode", () => (!merge || LayerWorkflow.MergeVisible(doc, true) is not null) && ImageModeEdits.Convert(doc, options, colors, dither, threshold));
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
            try { await Compositor.Core.IO.AtomicFile.WriteBytesAsync(path, bytes); } catch { Say("Could not save the color table."); }
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
        var documents = _tabs.Where(t => t.Document is not null).Select(t => (t.Name, t.Document!)).ToArray();
        var current = Array.FindIndex(documents, d => ReferenceEquals(d.Item2, doc));
        var preview = AdjustmentKit.Preview();
        var dialog = new ChannelCalculationDialog(calculation, documents, current, _open.Name, preview);
        // The picture follows the dialog: Apply Image blends into the layer; Calculations shows its gray result on it.
        var previewing = layerId is { } id && PropertyLayer is { Asset: not null };
        if (previewing)
        {
            StartPreview(doc, layerId!.Value);
            void Show()
            {
                if (preview.IsChecked != true || dialog.Current() is not { } asked) { RequestPreview((_, _) => true); return; }
                RequestPreview((target, layer) =>
                {
                    if (!calculation) return ChannelCalculations.Apply(target, layer, asked.First, asked.Settings, asked.Mask);
                    using var gray = ChannelCalculations.Calculate(asked.First, asked.Second!, asked.Settings, asked.Mask);
                    return target.Layers.FirstOrDefault(l => l.ID == layer) is { } shown && ImageAdjustments.SetPixels(target, shown, Fitted(gray, shown), false);
                });
            }
            dialog.Changed += Show; preview.IsCheckedChanged += (_, _) => Show(); dialog.Opened += (_, _) => Show();
        }
        try { await dialog.ShowDialog<bool>(this); } finally { if (previewing) StopPreview(); }
        if (dialog.Result is not { } result || !ReferenceEquals(doc, _document) || revision != _history.CurrentRevision) return;
        try
        {
            if (!calculation)
            { Edit(title, () => ChannelCalculations.Apply(doc, layerId!.Value, result.First, result.Settings, result.Mask)); return; }
            using var pixels = ChannelCalculations.Calculate(result.First, result.Second!, result.Settings, result.Mask); using var gray = Bitmaps.Allocate(Bitmaps.MaskInfo(doc.Width, doc.Height));
            for (var y = 0; y < gray.Height; y++) for (var x = 0; x < gray.Width; x++) gray.GetPixelSpan()[y * gray.RowBytes + x] = pixels.GetPixel(x, y).Red;
            // Result: 0 New Document, 1 New Channel, 2 Selection, in Photoshop's order.
            if (result.Result == 0) AdoptImported(ImageImporter.NewDocument(ImportedImage.Create(gray.Copy(), Localize.Text("Calculations")), doc.Resolution), title);
            else Edit(title, () =>
            {
                if (result.Result == 2) doc.Selection = DocumentSelection.FromCoverage(gray.Copy());
                else { if (doc.Channels.Count >= 64) return false; doc.Channels.Add(new AlphaChannel(Guid.NewGuid(), Localize.Text("Calculations"), ImportedImage.Create(gray.Copy(), "Alpha"))); }
                return true;
            });
        }
        catch (Exception e) { Say(Localize.Text("Calculation failed.") + " " + e.Message); }
    }

    /// <summary>A document-sized picture cut to where a layer sits, for showing a calculation on that layer.</summary>
    private static SKBitmap Fitted(SKBitmap picture, ImageLayer layer)
    {
        var asset = layer.Asset!; var fitted = new SKBitmap(new SKImageInfo(asset.Width, asset.Height, picture.ColorType, picture.AlphaType));
        using var canvas = new SKCanvas(fitted); canvas.Clear(SKColors.Transparent);
        canvas.Scale(asset.Width / (float)Math.Max(1, layer.Transform.Width), asset.Height / (float)Math.Max(1, layer.Transform.Height));
        canvas.Translate(-(float)layer.Transform.X, -(float)layer.Transform.Y); canvas.DrawBitmap(picture, 0, 0, new SKSamplingOptions(SKFilterMode.Linear));
        return fitted;
    }
    private async Task DefineImageVariables() => await ImageVariablesDialog(false, false);
    private async Task ImageDataSets(bool apply) => await ImageVariablesDialog(true, apply);

    /// <summary>Image ▸ Variables ▸ Define… / Data Sets… and Apply Data Set…: one dialog, its two pages, kept as one undo step.</summary>
    private async Task ImageVariablesDialog(bool dataSets, bool apply)
    {
        if (_document is not { } doc) return; var revision = _history.CurrentRevision;
        var dialog = new VariablesDialog(doc, Selected, dataSets, apply);
        await dialog.ShowDialog<bool>(this);
        if (!dialog.Accepted || !ReferenceEquals(doc, _document) || revision != _history.CurrentRevision) return;
        var variables = dialog.Variables.ToList(); var sets = dialog.DataSets.ToList(); var chosen = dialog.ChosenSet;
        var baseDirectory = dialog.ImportedFrom is { } from ? Path.GetDirectoryName(from)! : Environment.CurrentDirectory;
        try
        {
            Edit(dialog.ApplySet ? "Apply Data Set" : "Variables", () =>
            {
                doc.Variables.Clear(); doc.Variables.AddRange(variables); doc.DataSets.Clear(); doc.DataSets.AddRange(sets);
                return !dialog.ApplySet || sets.Count == 0 || ImageVariables.Apply(doc, sets[Math.Clamp(chosen, 0, sets.Count - 1)], baseDirectory);
            });
        }
        catch (Exception e) { Say(Localize.Text("Could not apply data set.") + " " + e.Message); }
    }

    /// <summary>
    /// Image ▸ Analysis, as Photoshop's: Set Measurement Scale ▸ Default and Custom… (and saved scales), Select Data Points ▸
    /// Custom… (and saved sets), Record Measurements, the Ruler and Count tools, and Place Scale Marker….
    /// </summary>
    private MenuItem BuildAnalysisMenu()
    {
        var menu = new MenuItem { Header = Localize.Text("_Analysis"), Tag = "Analysis" };
        menu.Items.Add(ScaleMenu()); menu.Items.Add(DataPointsMenu());
        var record = Command("Record _Measurements", RecordMeasurements); menu.Items.Add(record);
        menu.Items.Add(new Separator()); menu.Items.Add(Command("_Ruler Tool", () => SetTool(Tool.Ruler))); menu.Items.Add(Command("_Count Tool", () => SetTool(Tool.Count)));
        menu.Items.Add(new Separator()); menu.Items.Add(Command("_Place Scale Marker…", () => _ = PlaceScaleMarker()));
        // Record Measurements has something to measure only with a selection, a ruler line or counts, as Photoshop greys it.
        menu.SubmenuOpened += (_, _) => record.IsEnabled = _document is { } d && (d.Selection.Path is not null || d.Marks.Any(m => m.Kind is MarkKind.Measure or MarkKind.Count && m.Visible));
        return menu;
    }
    private MenuItem ScaleMenu()
    {
        var scale = new MenuItem { Header = Localize.Text("Set Measurement _Scale") };
        void Fill()
        {
            scale.Items.Clear();
            var standard = Keyed("_Default Values", () => { if (_document is { } d) Edit("Measurement Scale", () => { d.MeasurementScale = new(); return true; }); }, id: "Default"); standard.ToggleType = MenuItemToggleType.Radio;
            standard.IsChecked = _document?.MeasurementScale == new MeasurementScale(); scale.Items.Add(standard);
            foreach (var (name, preset) in MeasurementScaleDialog.Presets())
            {
                var item = new MenuItem { Header = name, ToggleType = MenuItemToggleType.Radio, IsChecked = _document?.MeasurementScale == preset, Tag = "scale " + name };
                item.Click += (_, _) => { if (_document is { } d) Edit("Measurement Scale", () => { d.MeasurementScale = preset; return true; }); }; scale.Items.Add(item);
            }
            scale.Items.Add(Command("_Custom…", () => _ = SetMeasurementScale()));
        }
        Fill(); scale.SubmenuOpened += (_, _) => Fill();
        return scale;
    }
    private MenuItem DataPointsMenu()
    {
        var points = new MenuItem { Header = Localize.Text("Select _Data Points") };
        void Fill()
        {
            points.Items.Clear();
            foreach (var (name, fields) in DataPointsDialog.Presets())
            {
                var item = new MenuItem { Header = name, ToggleType = MenuItemToggleType.Radio, IsChecked = _document?.MeasurementFields.SequenceEqual(fields) == true, Tag = "data points " + name };
                item.Click += (_, _) => { if (_document is { } d) Edit("Measurement Fields", () => { d.MeasurementFields = fields; return true; }); _measurementLog?.Refresh(); }; points.Items.Add(item);
            }
            var custom = Command("_Custom…", () => _ = MeasurementFields()); custom.ToggleType = MenuItemToggleType.Radio; custom.IsChecked = points.Items.OfType<MenuItem>().All(i => i.IsChecked != true);
            points.Items.Add(custom);
        }
        Fill(); points.SubmenuOpened += (_, _) => Fill();
        return points;
    }
    private async Task SetMeasurementScale()
    {
        if (_document is not { } doc) return; var revision = _history.CurrentRevision;
        var measured = doc.Marks.LastOrDefault(m => m.Kind == MarkKind.Measure);
        var dialog = new MeasurementScaleDialog(doc.MeasurementScale, measured is null ? null : Math.Sqrt(measured.Width * measured.Width + measured.Height * measured.Height));
        if (await dialog.ShowDialog<bool>(this) && dialog.Result is { } scale && ReferenceEquals(doc, _document) && revision == _history.CurrentRevision)
        { Edit("Measurement Scale", () => { doc.MeasurementScale = scale; return true; }); _measurementLog?.Refresh(); }
    }
    private async Task MeasurementFields()
    {
        if (_document is not { } doc) return; var dialog = new DataPointsDialog(doc.MeasurementFields);
        if (await dialog.ShowDialog<bool>(this) && dialog.Result is { } fields && ReferenceEquals(doc, _document))
        { Edit("Measurement Fields", () => { doc.MeasurementFields = fields; return true; }); _measurementLog?.Refresh(); }
    }
    private void RecordMeasurements()
    {
        if (_document is not { } doc) return;
        try
        {
            Edit("Record Measurements", () => { if (doc.Measurements.Count >= 10000) return false; doc.Measurements.Add(ImageAnalysis.Measure(doc, Localize.Text("Measurement") + " " + (doc.Measurements.Count + 1), _open.Name)); return true; });
            if (MeasurementLogView.AutoShow) _ = MeasurementLog(); else _measurementLog?.Refresh();
        }
        catch (Exception e) { Say(e.Message); }
    }

    private MeasurementLogView? _measurementLog;
    /// <summary>The Measurement Log panel's content: the table, its buttons and its menu, made once and kept in the panel.</summary>
    private MeasurementLogView MeasurementLogContent() => _measurementLog ??= new MeasurementLogView(() => _document, RecordMeasurements, rows =>
    {
        if (_document is not { } doc) return;
        Edit("Delete Measurements", () => { foreach (var row in rows.OrderByDescending(r => r)) if (row < doc.Measurements.Count) doc.Measurements.RemoveAt(row); return rows.Count > 0; });
    }, ScaleMenu, DataPointsMenu, () => { if (_dockPanels.GetValueOrDefault("Measurement Log") is { } panel) HidePanel(panel); });

    /// <summary>Window ▸ Measurement Log, and what Record Measurements opens: one floating panel, as Photoshop's.</summary>
    private Task MeasurementLog()
    {
        OpenUtilityPanel("Measurement Log");
        _measurementLog?.Refresh();
        return Task.CompletedTask;
    }
    private async Task PlaceScaleMarker()
    {
        if (_document is not { } doc) return; var revision = _history.CurrentRevision;
        var length = Math.Max(.1, Math.Round(doc.Width / 5.0 * doc.MeasurementScale.Units / doc.MeasurementScale.Pixels));
        var dialog = new ScaleMarkerDialog(doc.MeasurementScale, length);
        if (!await dialog.ShowDialog<bool>(this) || dialog.Result is not { } asked || !ReferenceEquals(doc, _document) || revision != _history.CurrentRevision) return;
        var label = $"{asked.Length:0.###} {doc.MeasurementScale.Unit}";
        if (!Edit("Scale Marker", () => ImageAnalysis.ScaleMarker(doc, asked.Length, label, asked.White, asked.Font, asked.Points, asked.ShowText, asked.Top)))
            Say("The scale marker does not fit across the image.");
    }
    private async Task TrapImage()
    {
        if (_document is not { } doc || PropertyLayer is not { Asset: not null } layer) return; var revision = _history.CurrentRevision;
        var dialog = new TrapDialog(doc.Resolution);
        if (!await dialog.ShowDialog<bool>(this) || dialog.Pixels is not { } pixels || !ReferenceEquals(doc, _document) || revision != _history.CurrentRevision) return;
        Edit("Trap", () => ImageTrapping.Apply(doc, layer.ID, pixels));
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
