using Avalonia.Controls;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private bool _applyingCrop;
    private void SyncCropOptions()
    {
        var c = _options.Crop; _canvas.CropSettings = c;
        if (_tool == Tool.Crop)
        {
            double? ratio = c.Width > 0 && c.Height > 0 ? c.Width / c.Height : null;
            if (_canvas.CropRatio != ratio && ratio is { } value && _document is { } doc)
            { _cropFrame = CropEdits.ApplyRatio(_cropFrame ?? SKRectI.Create(doc.Width, doc.Height), value); ShowCropBox(); }
            _canvas.CropRatio = ratio;
        }
        Pro.CropWidth = (int)c.Width; Pro.CropHeight = (int)c.Height;
    }
    private async Task CropSliceAction(string action, Control anchor)
    {
        var c = _options.Crop; var popup = new ToolSettingsPopup { Changed = OptionsChanged };
        switch (action)
        {
            case "Swap Crop Dimensions": (c.Width, c.Height) = (c.Height, c.Width); OptionsChanged(); return;
            case "Clear Crop": c.Width = c.Height = c.Resolution = 0; OptionsChanged(); return;
            case "Front Image": if (_document is { } d) { c.Width = d.Width; c.Height = d.Height; c.Resolution = d.Resolution; c.ResolutionUnit = 0; c.Mode = 1; } OptionsChanged(); return;
            case "Reset Crop": foreach (var property in typeof(CropToolSettings).GetProperties()) property.SetValue(c, property.GetValue(new CropToolSettings())); CancelCrop(); OptionsChanged(); return;
            case "Apply Crop": if (_tool == Tool.PerspectiveCrop) FinishProfessionalDraft(); else ApplyCrop(); return;
            case "Cancel Crop": CancelProfessionalDraft(); CancelCrop(); return;
            case "Straighten": _canvas.BeginCropStraighten((a, b) => { if (_document is not { } doc) return; var angle = Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI; angle -= Math.Round(angle / 90) * 90; Edit("Straighten", () => ImageGeometry.Rotate(doc, -angle)); _cropFrame = null; ShowCropBox(); _canvas.Fit(); }); Say("Drag along an edge that should be horizontal or vertical."); return;
            case "Crop Overlay":
                popup.Choice("Overlay", ["Rule of Thirds", "Grid", "Diagonal", "Triangle", "Golden Ratio", "Golden Spiral"], c.Overlay, v => c.Overlay = v);
                popup.Choice("Show Overlay", ["Auto Show Overlay", "Always Show Overlay", "Never Show Overlay"], c.OverlayDisplay, v => c.OverlayDisplay = v);
                popup.Children.Add(FormDialog.ActionButton("Cycle Overlay", () => { c.Overlay = (c.Overlay + 1) % 6; OptionsChanged(); }));
                popup.Children.Add(FormDialog.ActionButton("Cycle Orientation", () => { c.Orientation = (c.Orientation + 1) % 4; OptionsChanged(); })); break;
            case "Crop Options":
                popup.Check("Use Classic Mode", c.Classic, v => c.Classic = v); popup.Check("Show Cropped Area", c.ShowArea, v => c.ShowArea = v);
                popup.Check("Auto Center Preview", c.AutoCenter, v => c.AutoCenter = v); popup.Check("Enable Crop Shield", c.Shield, v => c.Shield = v);
                popup.Choice("Color:", ["Match Canvas", "Black", "White"], c.ShieldMatchCanvas ? 0 : c.ShieldColor == 0xffffffff ? 2 : 1, v => { c.ShieldMatchCanvas = v == 0; c.ShieldColor = v == 2 ? 0xffffffff : 0xff000000; });
                popup.Number("Opacity:", c.ShieldOpacity, 0, 100, v => c.ShieldOpacity = v); popup.Check("Auto Adjust Opacity", c.AutoOpacity, v => c.AutoOpacity = v); break;
            case "Fill Options":
                popup.Heading("Fill Options"); popup.Heading("Transparent: retain empty pixels in the expanded area."); popup.Heading("Generative Expand: use the configured image service."); popup.Heading("Content-Aware Fill: sample nearby image texture."); break;
            case "Crop Presets":
                var menu = new ContextMenu();
                foreach (var mode in new[] { 0, 1 })
                {
                    var item = new MenuItem { Header = Localize.Text(mode == 0 ? "Ratio" : "W × H × Resolution"), ToggleType = MenuItemToggleType.Radio, IsChecked = c.Mode == mode };
                    item.Click += (_, _) => { c.Mode = mode; OptionsChanged(); }; menu.Items.Add(item);
                }
                menu.Items.Add(new Separator());
                void Preset(CropPreset p) { var item = new MenuItem { Header = Localize.Text(p.Name) }; item.Click += (_, _) => { c.Mode = p.Mode; c.Width = p.Width; c.Height = p.Height; c.Resolution = p.Resolution; c.ResolutionUnit = 0; OptionsChanged(); }; menu.Items.Add(item); }
                if (_document is { } doc) Preset(new("Original Ratio", 0, doc.Width, doc.Height, 0));
                foreach (var p in new[] { new CropPreset("1:1 (Square)", 0, 1, 1, 0), new("4:5 (8:10)", 0, 4, 5, 0), new("5:7", 0, 5, 7, 0), new("2:3 (4:6)", 0, 2, 3, 0), new("16:9", 0, 16, 9, 0), new("4 × 5 in · 300 ppi", 1, 1200, 1500, 300), new("8.5 × 11 in · 300 ppi", 1, 2550, 3300, 300), new("1024 × 768 px · 92 ppi", 1, 1024, 768, 92), new("1280 × 800 px · 113 ppi", 1, 1280, 800, 113), new("1366 × 768 px · 135 ppi", 1, 1366, 768, 135) }) Preset(p);
                var front = new MenuItem { Header = Localize.Text("Front Image"), IsEnabled = _document is not null }; front.Click += async (_, _) => await CropSliceAction("Front Image", anchor); menu.Items.Add(front);
                menu.Items.Add(new Separator());
                foreach (var p in EditorPreferences.Current.CropPresets) Preset(p);
                var add = new MenuItem { Header = Localize.Text("New Crop Preset…") }; add.Click += async (_, _) => { if (await TextPrompt.Ask(this, "New Crop Preset…", "Name", "") is not { Length: > 0 } name) return; EditorPreferences.Current.CropPresets.Add(new(name, c.Mode, c.Width, c.Height, Math.Min(9600, c.Resolution * (c.ResolutionUnit == 1 ? 2.54 : 1)))); SavePreferenceChanges(); }; menu.Items.Add(add);
                var remove = new MenuItem { Header = Localize.Text("Delete Crop Preset…"), IsEnabled = EditorPreferences.Current.CropPresets.Count > 0 }; foreach (var p in EditorPreferences.Current.CropPresets) { var item = new MenuItem { Header = p.Name }; item.Click += (_, _) => { EditorPreferences.Current.CropPresets.Remove(p); SavePreferenceChanges(); }; remove.Items.Add(item); } menu.Items.Add(remove); menu.Open(anchor); return;
            default: await SliceAction(action, anchor); return;
        }
        popup.Show(anchor);
    }
    private async Task ApplyAdvancedCrop()
    {
        if (_applyingCrop || _document is not { } doc) return;
        var frame = _cropFrame ?? SKRectI.Create(doc.Width, doc.Height); if (!CropEdits.Valid(frame)) return;
        var c = new CropToolSettings(); foreach (var property in typeof(CropToolSettings).GetProperties()) property.SetValue(c, property.GetValue(_options.Crop));
        var revision = _history.CurrentRevision; using var draft = doc.Clone();
        _applyingCrop = true;
        try
        {
            var ppi = Math.Min(9600, c.ResolutionUnit == 1 ? c.Resolution * 2.54 : c.Resolution);
            if (!await Task.Run(() => AdvancedCropEdits.Apply(draft, frame, c.DeletePixels, c.Fill == 2, c.Mode == 1 ? (int)c.Width : 0, c.Mode == 1 ? (int)c.Height : 0, ppi))) { Say("The crop could not be applied. Check dimensions and source pixels."); return; }
            if (!ReferenceEquals(doc, _document) || _history.CurrentRevision != revision) return;
            if (c.Fill == 1 && (frame.Left < 0 || frame.Top < 0 || frame.Right > doc.Width || frame.Bottom > doc.Height))
            {
                using var preview = DocumentRenderer.Render(draft); var dialog = new GenerativeDialog(false, reference: PngCodec.Encode(preview), prompt: "Extend the scene naturally into the transparent outer regions. Preserve all existing image pixels and the exact image dimensions.");
                if (await dialog.ShowDialog<GenerativeDialog.Result?>(this) is not { } result) return;
                using var image = SKBitmap.Decode(result.Image.Bytes); if (image is null) return;
                using var extension = image.Resize(Bitmaps.ColorInfo(draft.Width, draft.Height), new SKSamplingOptions(SKFilterMode.Linear)); if (extension is null) return;
                using (var canvas = new SKCanvas(extension)) { var sx = (float)draft.Width / frame.Width; var sy = (float)draft.Height / frame.Height; canvas.ClipRect(SKRect.Create(-frame.Left * sx, -frame.Top * sy, doc.Width * sx, doc.Height * sy)); canvas.Clear(SKColors.Transparent); }
                draft.Layers.Insert(0, new ImageLayer(Guid.NewGuid(), ImportedImage.Create(extension.Copy(), Localize.Text("Generative Expand")), new LayerTransform(0, 0, draft.Width, draft.Height), Localize.Text("Generative Expand")));
            }
            if (!ReferenceEquals(doc, _document) || _history.CurrentRevision != revision) return;
            Edit("Crop", () => { doc.Adopt(draft); return true; }); _cropFrame = null; _canvas.SnapLines = (null, null); ShowCropBox();
            if (c.AutoCenter && !c.Classic) _canvas.Fit();
        }
        catch (Exception ex) { Say($"Could not crop: {ex.Message}"); }
        finally { _applyingCrop = false; }
    }
}
