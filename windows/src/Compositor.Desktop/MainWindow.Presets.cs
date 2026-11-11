using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private async Task DefinePreset(string kind)
    {
        try
        {
            string? image = null, path = null; SKBitmap? thumbnail = null;
            if (kind == "Shape")
            {
                path = PropertyLayer?.LiveShape?.Path ?? (_document?.Selection is { IsEmpty: false } selection ? selection.Path?.ToSvgPathData() : null);
                if (path is null) { await ConfirmDialog.Tell(this, "Define Custom Shape", "Select a shape layer, or make a selection, to define a custom shape."); return; }
                path = ShapeTemplates.Normalize(path);
                using var shape = SKPath.ParseSvgPathData(path);
                thumbnail = new SKBitmap(96, 96); using var canvas = new SKCanvas(thumbnail); canvas.Clear(SKColors.Transparent);
                var bounds = shape.Bounds; var scale = 80 / Math.Max(1, Math.Max(bounds.Width, bounds.Height));
                canvas.Translate(48, 48); canvas.Scale(scale); canvas.Translate(-bounds.MidX, -bounds.MidY);
                using var fill = new SKPaint { Color = SKColors.White, IsAntialias = true }; using var edge = new SKPaint { Color = SKColors.Black, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3 / scale };
                canvas.DrawPath(shape, fill); canvas.DrawPath(shape, edge);
            }
            else if (_document is { } doc)
            {
                // As Photoshop does: the selection's box, or the whole canvas when nothing is selected.
                var selected = doc.Selection.Path is not null && !doc.Selection.IsEmpty;
                var region = selected ? doc.Selection.CoverageRect(doc.Width, doc.Height) : SKRectI.Create(doc.Width, doc.Height);
                if (region.Width <= 0 || region.Height <= 0) { await ConfirmDialog.Tell(this, kind == "Pattern" ? "Define Pattern" : "Define Brush Preset", "The selection covers no pixels of the canvas."); return; }
                using var pixels = DocumentRenderer.RenderRegion(doc, region);
                using var coverage = selected ? doc.Selection.Coverage(region) : null;
                if (coverage is not null)
                    for (var y = 0; y < pixels.Height; y++) for (var x = 0; x < pixels.Width; x++)
                    { var c = pixels.GetPixel(x, y); pixels.SetPixel(x, y, c.WithAlpha((byte)(c.Alpha * coverage.GetPixelSpan()[y * coverage.RowBytes + x] / 255))); }
                // A brush tip is at most 5000 pixels and a pattern 4096 across; a larger area is scaled down to fit.
                var most = kind == "Pattern" ? 4096d : 5000d; var fit = Math.Min(1, most / Math.Max(pixels.Width, pixels.Height));
                using var sized = fit < 1 ? pixels.Resize(new SKImageInfo(Math.Max(1, (int)(pixels.Width * fit)), Math.Max(1, (int)(pixels.Height * fit)), pixels.ColorType, pixels.AlphaType), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)) : null;
                var tip = sized ?? pixels;
                image = Convert.ToBase64String(PngCodec.Encode(tip)); thumbnail = tip.Copy();
            }
            if (kind != "Shape" && image is null) { await ConfirmDialog.Tell(this, kind == "Pattern" ? "Define Pattern" : "Define Brush Preset", "Open an image first."); return; }
            var existing = EditorPresets.Load(EditorPresets.DefaultPath);
            var number = existing.Count(p => p.Kind == kind) + 1;
            var title = kind switch { "Shape" => "Shape Name", "Pattern" => "Pattern Name", _ => "Brush Name" };
            var defaultName = kind == "Brush" ? $"{Localize.Text("Sampled Brush")} {number}" : $"{Localize.Text(kind)} {number}";
            var dialog = new PresetNameDialog(title, defaultName, thumbnail, kind == "Brush" && thumbnail is not null ? Math.Max(thumbnail.Width, thumbnail.Height).ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
            var accepted = await dialog.ShowDialog<bool>(this); thumbnail?.Dispose();
            if (!accepted) return;
            var preset = new EditorPreset(Guid.NewGuid(), dialog.PresetName, kind, _options.Brush.Diameter, _options.Brush.Hardness, _options.Brush.Opacity, image, path);
            EditorPresets.Save(EditorPresets.DefaultPath, existing.Append(preset)); ApplyPreset(preset);
        }
        catch (Exception error) { await ConfirmDialog.Tell(this, kind == "Shape" ? "Define Custom Shape" : kind == "Pattern" ? "Define Pattern" : "Define Brush Preset", error.Message); }
    }
    private void ApplyPreset(EditorPreset preset)
    {
        if (preset.Kind == "Shape") { _customShape = preset.Path!; SetTool(Tool.CustomShape); }
        else if (preset.Kind == "Pattern") { _options.Brush = _options.Brush with { Pattern = SKBitmap.Decode(Convert.FromBase64String(preset.Image!)) }; SetTool(Tool.PatternStamp); }
        else
        {
            ApplyBrushPreset(preset);
        }
        Refresh();
    }
    private async Task PresetManager()
    {
        try { await new PresetManagerDialog().ShowDialog(this); RefreshUtilityPanels(); Refresh(); }
        catch (Exception error) { Say(error.Message); }
    }
    private async Task MigratePresets()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Import Compositor Presets"), FileTypeFilter = [new FilePickerFileType("Compositor Presets") { Patterns = ["*.json"] }] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        try
        {
            var current = EditorPresets.Load(EditorPresets.DefaultPath).ToList(); var ids = current.Select(p => p.ID).ToHashSet();
            current.AddRange(EditorPresets.Load(path).Where(p => !ids.Contains(p.ID))); EditorPresets.Save(EditorPresets.DefaultPath, current);
            await PresetManager();
        }
        catch (Exception error) { Say(error.Message); }
    }
    private async Task PdfPresets() => await new PdfPresetsDialog().ShowDialog(this);
    private async Task RemoteConnections()
    {
        var form = new FormDialog("Remote Connections", 580, 360);
        form.Description("Connect to an OpenAI-compatible image service using its API endpoint and key. The Photoshop remote-control protocol is not supported.");
        form.Fields.Children.Add(FormDialog.ActionButton("Image Service Settings…", async () => { form.Close(false); await OpenGenerativeWorkspace(); }));
        await form.ShowDialog<bool>(this);
    }
}
