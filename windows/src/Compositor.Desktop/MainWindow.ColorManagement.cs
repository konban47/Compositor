using Avalonia.Platform.Storage;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using ImageMagick;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private async Task ColorSettings()
    {
        var dialog = new ColorSettingsDialog(EditorPreferences.Current.ColorSettings, settings => { ColorDefaults.Settings = settings; _canvas.ColorSettingsChanged(); });
        if (!await dialog.ShowDialog<bool>(this)) return;
        try { var preferences = EditorPreferences.Current.Copy(); preferences.ColorSettings = dialog.Draft; EditorPreferences.Commit(preferences); ApplyPreferences(); }
        catch (Exception error) { ColorDefaults.Settings = EditorPreferences.Current.ColorSettings; _canvas.ColorSettingsChanged(); Say(error.Message); }
    }
    private async Task<byte[]?> ChooseIcc(string title, bool rgbOnly = false)
    {
        var settings = EditorPreferences.Current.ColorSettings;
        var catalog = new[] { "RGB", "GRAY", "CMYK" }.Where(m => !rgbOnly || m == "RGB").SelectMany(m => IccProfiles.Catalog(settings, m)).ToArray();
        var form = new FormDialog(title, 640, 270);
        var names = catalog.Select(p => p.Name).Append("Custom ICC Profile…").ToArray();
        var profile = form.Choice("ICC Profile", names, Math.Max(0, Array.FindIndex(catalog, p => p.Name == settings.Rgb)));
        form.Description("The selected color engine, rendering intent and black-point compensation are applied to this conversion.");
        if (!await form.ShowDialog<bool>(this)) return null;
        if (profile.SelectedIndex < catalog.Length) return catalog[profile.SelectedIndex].Bytes;
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = Localize.Text("ICC Profile"), FileTypeFilter = [new("ICC Profile") { Patterns = ["*.icc", "*.icm"] }] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return null;
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("ICC profile is too large.");
        var bytes = await File.ReadAllBytesAsync(path); if (!IccProfiles.Valid(bytes) || rgbOnly && IccProfiles.Model(bytes) != "RGB") throw new InvalidDataException("Choose an ICC profile matching the layer's RGB channels."); return bytes;
    }
    private async Task AssignColorProfile()
    {
        if (_document is not { } doc || PropertyLayer is not { Asset: { } asset } layer || !LayerProtection.CanPaint(doc, layer.ID)) return;
        var revision = _history.CurrentRevision;
        try
        {
            if (await ChooseIcc("Assign Source Profile to Layer", true) is not { } profile || !ReferenceEquals(doc, _document) || revision != _history.CurrentRevision) return;
            using var lease = asset.Retain(); using var original = DeepImageCodec.Magick(asset.WorkingImage);
            using var converted = await Task.Run(() => ColorTransforms.Convert(original, profile, ColorProfiles.SRGB.ToByteArray())); using var pixels = AdvancedEdit.Pixels(converted);
            if (!ReferenceEquals(doc, _document) || revision != _history.CurrentRevision) return;
            Edit("Assign Profile", () => ImageAdjustments.SetPixels(doc, layer, pixels, selection: false));
        }
        catch (Exception error) { Say(error.Message); }
    }
    private async Task ConvertColorProfile()
    {
        if (_document is not { } doc) return;
        try
        {
            if (await ChooseIcc("Convert and Export ICC TIFF") is not { } profile || !ReferenceEquals(doc, _document)) return;
            var file = await StorageProvider.SaveFilePickerAsync(new() { SuggestedFileName = "color-managed.tif", DefaultExtension = "tif" });
            if (file?.TryGetLocalPath() is not { } path) return;
            using var snapshot = new DocumentLease(doc);
            await WorkScheduler.Run(_ => { using var composite = DocumentRenderer.RenderDeep(snapshot.Document); using var original = DeepImageCodec.Magick(composite); using var converted = ColorTransforms.Convert(original, ColorProfiles.SRGB.ToByteArray(), profile); converted.Depth = (uint)Math.Max(16, doc.Image.Depth); if (converted.Depth == 32) converted.Settings.SetDefine(MagickFormat.Tiff, "floating-point", "true"); converted.Density = new Density(doc.Resolution, doc.Resolution, DensityUnit.PixelsPerInch); AtomicFile.Write(path, output => converted.Write(output, MagickFormat.Tiff)); return true; });
            Say("Color-managed TIFF exported.");
        }
        catch (Exception error) { Say(error.Message); }
    }
    private async Task OpenColorIoSettings()
    {
        var dialog = new OpenColorIoDialog(EditorPreferences.Current.Ocio);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try { var preferences = EditorPreferences.Current.Copy(); preferences.Ocio = dialog.Draft; EditorPreferences.Commit(preferences); ApplyPreferences(); }
        catch (Exception error) { Say(error.Message); }
    }
    private async Task ConvertOpenColorIo()
    {
        if (_document is not { } doc || PropertyLayer is not { Asset: { } asset } layer || !LayerProtection.CanPaint(doc, layer.ID)) return;
        var revision = _history.CurrentRevision; var settings = EditorPreferences.Current.Ocio;
        if (!settings.Enabled) { await OpenColorIoSettings(); settings = EditorPreferences.Current.Ocio; if (!settings.Enabled) return; }
        try
        {
            var info = await Task.Run(() => OpenColorIo.Inspect(settings.EffectiveConfig));
            var form = new FormDialog("OpenColorIO Conversion", 650, 360);
            var names = info.Spaces.Select(s => s.Name).ToArray();
            var source = form.Choice("Input Color Space", names, Math.Max(0, Array.IndexOf(names, info.Find(settings.WorkingSpace)?.Name)));
            var target = form.Choice("Output Color Space", names, Math.Max(0, Array.IndexOf(names, info.EditorSpace)));
            var export = form.Check("Export Float TIFF Instead of Applying", false);
            form.Description("Applying interprets the layer's stored RGB values as the input space. Use sRGB as the output for normal editing. Export preserves the selected output space in a 32-bit floating-point TIFF.");
            if (!await form.ShowDialog<bool>(this) || !ReferenceEquals(doc, _document) || revision != _history.CurrentRevision) return;
            string? path = null; if (export.IsChecked == true) { var file = await StorageProvider.SaveFilePickerAsync(new() { SuggestedFileName = "ocio-output.tif", DefaultExtension = "tif" }); path = file?.TryGetLocalPath(); if (path is null) return; }
            var sourceSpace = names[source.SelectedIndex]; var targetSpace = names[target.SelectedIndex];
            using var lease = asset.Retain(); using var result = await Task.Run(() => OpenColorIo.Convert(asset.WorkingImage, settings.EffectiveConfig, sourceSpace, targetSpace));
            if (path is not null) { using var image = DeepImageCodec.Magick(result); image.Depth = 32; image.Settings.SetDefine(MagickFormat.Tiff, "floating-point", "true"); image.SetAttribute("comment", "OpenColorIO: " + settings.EffectiveConfig + " | " + targetSpace); AtomicFile.Write(path, output => image.Write(output, MagickFormat.Tiff)); Say("Color-managed TIFF exported."); return; }
            if (!ReferenceEquals(doc, _document) || revision != _history.CurrentRevision) return;
            Edit("OpenColorIO", () => { doc.Image = doc.Image with { Depth = 32, OcioConfiguration = settings.EffectiveConfig, OcioWorkingSpace = targetSpace }; return ImageAdjustments.SetPixels(doc, layer, result, selection: false); });
        }
        catch (Exception error) { Say(error.Message); }
    }
}
