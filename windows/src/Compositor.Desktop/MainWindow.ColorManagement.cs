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
        CommitText(); _stopGifPlayback?.Invoke();
        if (_document is not { } doc) return;
        if (doc.Image.Mode is not (ImageMode.RGB or ImageMode.CMYK or ImageMode.Grayscale)) { Say("Assign Profile requires an RGB, CMYK or grayscale document."); return; }
        var revision = _history.CurrentRevision;
        using var preview = doc.Clone();
        try
        {
            var dialog = new AssignProfileDialog(doc.Image, EditorPreferences.Current.ColorSettings, value =>
            {
                if (!ReferenceEquals(doc, _document)) return;
                if (value is not null) preview.Image = value;
                _canvas.PreviewDocument = value is null ? null : preview; _canvas.ColorSettingsChanged();
            });
            if (!await dialog.ShowDialog<bool>(this) || !ReferenceEquals(doc, _document) || revision != _history.CurrentRevision) return;
            Edit("Assign Profile", () => { if (doc.Image == dialog.Draft) return false; doc.Image = dialog.Draft; return true; });
        }
        catch (Exception error) { Say(error.Message); }
        finally { if (ReferenceEquals(_canvas.PreviewDocument, preview)) _canvas.PreviewDocument = null; _canvas.ColorSettingsChanged(); }
    }
    private async Task ConvertColorProfile()
    {
        CommitText(); _stopGifPlayback?.Invoke();
        if (_document is not { } doc) return;
        var settings = EditorPreferences.Current.ColorSettings; var revision = _history.CurrentRevision;
        var source = doc.Image.Mode == ImageMode.Lab ? Localize.Text("Lab Color") : IccProfiles.Name(ProfileAssignment.Current(doc.Image) ?? IccProfiles.Resolve(settings.ProfileName(doc.Image.Mode), settings), settings.ProfileName(doc.Image.Mode));
        CanvasDocument? preview = null; CancellationTokenSource? pending = null; ConvertProfileDialog? dialog = null;
        async void Show()
        {
            pending?.Cancel(); pending = new CancellationTokenSource(); var token = pending.Token;
            if (dialog!.Preview.IsChecked != true) { _canvas.PreviewDocument = null; _canvas.ColorSettingsChanged(); return; }
            var (mode, profile) = dialog.Target(settings); var flatten = dialog.Flatten.IsChecked == true;
            var copy = doc.Clone();
            var converted = await Task.Run(() => ConvertedTo(copy, mode, profile, flatten) ? copy : null, token).ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : null);
            if (token.IsCancellationRequested || converted is null || !ReferenceEquals(doc, _document)) { converted?.Dispose(); return; }
            var old = preview; preview = converted; _canvas.PreviewDocument = preview; _canvas.ColorSettingsChanged(); old?.Dispose();
        }
        dialog = new ConvertProfileDialog(source, settings, Show);
        dialog.Opened += (_, _) => Show();
        try
        {
            if (!await dialog.ShowDialog<bool>(this) || !ReferenceEquals(doc, _document) || revision != _history.CurrentRevision) return;
            var (mode, profile) = dialog.Target(settings); var flatten = dialog.Flatten.IsChecked == true;
            Edit("Convert to Profile", () => ConvertedTo(doc, mode, profile, flatten));
            _open.ActiveAlpha = null; _open.VisibleChannels = ColorChannels.RGB; doc.EditChannels = ColorChannels.RGB; Refresh();
        }
        catch (Exception error) { Say(error.Message); }
        finally { pending?.Cancel(); _canvas.PreviewDocument = null; preview?.Dispose(); _canvas.ColorSettingsChanged(); }
    }
    /// <summary>Flatten Image to Preserve Appearance: the layers become the one picture they make.</summary>
    private static void FlattenForConversion(CanvasDocument doc)
    {
        var deep = doc.Image.Depth > 8;
        var pixels = deep ? DocumentRenderer.RenderDeep(doc) : DocumentRenderer.Render(doc);
        var name = Localize.Text("Background");
        var asset = deep ? ImportedImage.CreateDeep(pixels, name, doc.Image.Depth) : ImportedImage.Create(pixels, name);
        doc.Layers.Clear(); doc.Layers.Add(new ImageLayer(Guid.NewGuid(), asset, new(0, 0, doc.Width, doc.Height), name));
    }
    /// <summary>Converts a document so it looks the same in another profile: within RGB the pixels are moved into the new profile, and a
    /// change of model goes through the document mode conversion with the chosen profile.</summary>
    private static bool ConvertedTo(CanvasDocument doc, ImageMode mode, byte[]? profile, bool flatten)
    {
        var settings = ColorDefaults.Settings;
        if (mode == ImageMode.RGB && doc.Image.Mode == ImageMode.RGB && profile is not null)
        {
            var from = ProfileAssignment.Current(doc.Image) ?? IccProfiles.Resolve(settings.Rgb, settings);
            if (from.AsSpan().SequenceEqual(profile)) return false;
            if (flatten && doc.Layers.Count > 1) FlattenForConversion(doc);
            foreach (var layer in doc.Layers.Where(l => l.Asset is not null && l.Adjustment is null).ToArray())
            {
                using var converted = ColorTransforms.Convert(layer.Asset!.WorkingImage, from, profile, settings);
                using var deep = DeepPixels.From(converted);
                ImageAdjustments.SetPixels(doc, layer, deep, selection: false);
            }
            doc.Image = doc.Image with { ColorProfile = profile.ToArray() }; return true;
        }
        if (flatten && doc.Layers.Count > 1) FlattenForConversion(doc);
        var target = doc.Image with
        {
            Mode = mode, Palette = null,
            ColorProfile = mode is ImageMode.RGB or ImageMode.Grayscale ? profile?.ToArray() : doc.Image.ColorProfile,
            CmykProfile = mode == ImageMode.CMYK ? profile?.ToArray() : doc.Image.CmykProfile,
        };
        return ImageModeEdits.Convert(doc, target);
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
