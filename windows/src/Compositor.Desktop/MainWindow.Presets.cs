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
        var form = new FormDialog("Define " + kind + " Preset", height: 310); var name = form.Text("Name", Localize.Text(kind));
        form.Validate = () => string.IsNullOrWhiteSpace(name.Text) || name.Text.Length > 200 ? "Enter a name (1–200 characters)." : null;
        if (!await form.ShowDialog<bool>(this)) return;
        try
        {
            string? image = null, path = null;
            if (kind == "Shape")
            {
                path = PropertyLayer?.LiveShape?.Path ?? _document?.Selection.Path?.ToSvgPathData();
                if (path is null) { Say("Select a vector shape or make a selection first."); return; }
                path = ShapeTemplates.Normalize(path);
            }
            else if (_document is { } doc)
            {
                var region = doc.Selection.CoverageRect(doc.Width, doc.Height);
                if (region.Width <= 0 || region.Height <= 0 || region.Width > 2048 || region.Height > 2048) { Say("Preset images must be between 1 and 2048 pixels on each side."); return; }
                using var pixels = DocumentRenderer.RenderRegion(doc, region);
                using var coverage = doc.Selection.Coverage(region);
                if (coverage is not null)
                    for (var y = 0; y < pixels.Height; y++) for (var x = 0; x < pixels.Width; x++)
                    { var c = pixels.GetPixel(x, y); pixels.SetPixel(x, y, c.WithAlpha((byte)(c.Alpha * coverage.GetPixelSpan()[y * coverage.RowBytes + x] / 255))); }
                image = Convert.ToBase64String(PngCodec.Encode(pixels));
            }
            if (kind == "Pattern" && image is null) { Say("Open an image to define a pattern."); return; }
            var preset = new EditorPreset(Guid.NewGuid(), name.Text!.Trim(), kind, _options.Brush.Diameter, _options.Brush.Hardness, _options.Brush.Opacity, image, path);
            EditorPresets.Save(EditorPresets.DefaultPath, EditorPresets.Load(EditorPresets.DefaultPath).Append(preset)); ApplyPreset(preset);
        }
        catch (Exception error) { Say(error.Message); }
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
    private Task PdfPresets() => EditPreferences("Export");
    private async Task RemoteConnections()
    {
        var form = new FormDialog("Remote Connections", 580, 360);
        form.Description("Connect to an OpenAI-compatible image service using its API endpoint and key. The Photoshop remote-control protocol is not supported.");
        form.Fields.Children.Add(FormDialog.ActionButton("Image Service Settings…", async () => { form.Close(false); await OpenGenerativeWorkspace(); }));
        await form.ShowDialog<bool>(this);
    }
}
