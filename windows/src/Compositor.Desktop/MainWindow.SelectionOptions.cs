using Avalonia.Controls;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;
using SelectionMode = Compositor.Core.Document.SelectionMode;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private static readonly uint[] SelectionColors = [0xffdf48a5, 0xffe85353, 0xff59b858, 0xff4089df, 0xffffcf43];
    private static readonly string[] SelectionColorNames = ["Magenta", "Red", "Green", "Blue", "Yellow"];
    private void SyncSelectionOptions()
    {
        _canvas.DefaultSelectionMode = _options.Selection.Mode;
        _canvas.ConstrainMarquee = _tool is Tool.Marquee or Tool.Ellipse or Tool.SingleRow or Tool.SingleColumn ? SelectionRectangle : null;
        SyncCropOptions();
        _canvas.InvalidateVisual();
    }
    private SKRectI SelectionRectangle(SKPoint a, SKPoint b)
    {
        var s = _options.Selection; var w = Math.Abs(b.X - a.X); var h = Math.Abs(b.Y - a.Y);
        if (_tool == Tool.SingleRow) return SKRectI.Create(0, (int)b.Y, _document?.Width ?? 1, 1);
        if (_tool == Tool.SingleColumn) return SKRectI.Create((int)b.X, 0, 1, _document?.Height ?? 1);
        if (s.Style == 2) { w = (float)s.Width; h = (float)s.Height; }
        else if (s.Style == 1) { var ratio = s.Width / Math.Max(1, s.Height); if (w / Math.Max(1, h) > ratio) h = (float)(w / ratio); else w = (float)(h * ratio); }
        return SKRectI.Create((int)(b.X < a.X ? a.X - w : a.X), (int)(b.Y < a.Y ? a.Y - h : a.Y), (int)Math.Ceiling(w), (int)Math.Ceiling(h));
    }
    private bool ApplyToolSelection(CanvasDocument doc, SKPath path, SelectionMode mode)
    {
        var aa = _options.SelectionAntialiased && _tool is not (Tool.Marquee or Tool.SingleRow or Tool.SingleColumn);
        if (_options.Selection.Feather <= 0) return SelectionEdits.Apply(doc, path, mode, aa);
        var selection = DocumentSelection.FromPath(new SKPath(path), aa, _options.Selection.Feather);
        var mask = selection.Coverage(SKRectI.Create(doc.Width, doc.Height));
        return mask is not null && SelectionBrushEdits.Combine(doc, mask, 1, (SelectionPaintMode)(int)mode);
    }
    private async Task SelectionAction(string action, Control anchor)
    {
        var s = _options.Selection; var popup = new ToolSettingsPopup { Changed = OptionsChanged };
        switch (action)
        {
            case "Swap Selection Dimensions": (s.Width, s.Height) = (s.Height, s.Width); OptionsChanged(); return;
            case "Selection Brush Settings":
                popup.Number("Size:", _options.Brush.Diameter, 1, 5000, v => _options.Brush = _options.Brush with { Diameter = v }, true);
                popup.Number("Hardness:", _options.Brush.Hardness * 100, 0, 100, v => _options.Brush = _options.Brush with { Hardness = v / 100 }, true); break;
            case "Selection Overlay Options":
                popup.Heading("Overlay Options"); popup.Choice("Color:", SelectionColorNames, Math.Max(0, Array.IndexOf(SelectionColors, s.OverlayColor)), i => s.OverlayColor = SelectionColors[i]); break;
            case "Subject Processing":
                popup.Choice("Processing", ["Device", "Cloud"], s.CloudSubject ? 1 : 0, i => s.CloudSubject = i == 1); break;
            case "Object Finder Settings":
                popup.Check("Object Finder", s.ObjectFinder, v => s.ObjectFinder = v);
                popup.Check("Object Subtract", s.ObjectSubtract, v => s.ObjectSubtract = v);
                popup.Check("Show Object Details on Canvas", s.ObjectDetails, v => s.ObjectDetails = v);
                popup.Choice("Object Finder Mode", ["Auto Refresh", "Manual Refresh"], s.ObjectAutoRefresh ? 0 : 1, i => s.ObjectAutoRefresh = i == 0);
                popup.Heading("Overlay Options"); popup.Choice("Color:", SelectionColorNames, Math.Max(0, Array.IndexOf(SelectionColors, s.OverlayColor)), i => s.OverlayColor = SelectionColors[i]);
                popup.Number("Outline:", s.OverlayOutline, 0, 10, v => s.OverlayOutline = v);
                popup.Number("Opacity:", s.OverlayOpacity, 0, 100, v => s.OverlayOpacity = v);
                popup.Choice("Show Overlay", ["Auto Show Overlay", "Always Show Overlay", "Never Show Overlay"], s.OverlayMode, i => s.OverlayMode = i); break;
            case "Select Subject":
                if (s.CloudSubject) await CloudSubject(); else await DetectSubject(false, mode: s.Mode); return;
            case "Select People":
                var menu = new ContextMenu();
                var device = new MenuItem { Header = Localize.Text("Select People on Canvas") };
                device.Click += async (_, _) => { s.ObjectFinder = true; s.OverlayMode = 0; await RefreshObjectFinder(true); OptionsChanged(); Say("Click the person to select their foreground region. The device model does not classify people or body parts."); };
                var cloud = new MenuItem { Header = Localize.Text("Select People with Cloud") }; cloud.Click += async (_, _) => await CloudSubject(true);
                menu.Items.Add(device); menu.Items.Add(cloud); menu.Open(anchor); return;
            case "Refresh Object Finder": await RefreshObjectFinder(true); return;
            case "Show All Objects": s.OverlayMode = s.OverlayMode == 1 ? 0 : 1; await RefreshObjectFinder(true); OptionsChanged(); return;
            case "Select and Mask…": await RefineSelection(); return;
            default: await CropSliceAction(action, anchor); return;
        }
        popup.Show(anchor);
    }
    private async Task CloudSubject(bool people = false)
    {
        if (_document is not { } doc) return; var revision = _history.CurrentRevision;
        using var source = SelectionEdits.Sample(doc, (_tool == Tool.QuickSelection ? Pro.SampleAll : _options.WandAllLayers) ? null : Selected); if (source is null) return;
        var dialog = new GenerativeDialog(false, reference: PngCodec.Encode(source), prompt: people
            ? "Return a precise grayscale segmentation mask of all people in the image. White for people, black for all other objects and background. Preserve framing and dimensions. If no people are present, return solid black. No labels or artwork."
            : "Return a precise grayscale segmentation mask of the main foreground subject, white for the subject, black for the background. Preserve the original framing and dimensions. No labels or artwork.");
        if (await dialog.ShowDialog<GenerativeDialog.Result?>(this) is not { } result || !ReferenceEquals(doc, _document) || _history.CurrentRevision != revision) return;
        using var image = SKBitmap.Decode(result.Image.Bytes); if (image is null) return;
        using var resized = image.Resize(Bitmaps.ColorInfo(doc.Width, doc.Height), new SKSamplingOptions(SKFilterMode.Linear)); if (resized is null) return;
        var mask = Bitmaps.Allocate(Bitmaps.MaskInfo(doc.Width, doc.Height));
        for (var y = 0; y < mask.Height; y++) for (var x = 0; x < mask.Width; x++) { var c = resized.GetPixel(x, y); var v = (byte)((c.Red + c.Green + c.Blue) / 3 * c.Alpha / 255); mask.SetPixel(x, y, new SKColor(v, v, v)); }
        Edit("Select Subject", () => SelectionBrushEdits.Combine(doc, mask, 1, (SelectionPaintMode)(int)_options.Selection.Mode));
    }
    private async Task RefineSelection()
    {
        if (_document is not { } doc || doc.Selection.Path is null) { Say("Make a selection first."); return; }
        var original = doc.Selection; var revision = _history.CurrentRevision; var settings = new MaskRefinementSettings();
        using var source = DocumentRenderer.Render(doc); using var coverage = original.Coverage(SKRectI.Create(doc.Width, doc.Height)); if (coverage is null) return;
        var form = new FormDialog("Select and Mask", 460, 590);
        form.Number("Radius", 0, 0, 100, v => settings = settings with { Radius = v });
        form.Check("Smart Radius", false, v => settings = settings with { SmartRadius = v });
        form.Number("Smooth (px)", 0, 0, 100, v => settings = settings with { Smooth = v });
        form.Number("Feather (px)", 0, 0, 1000, v => settings = settings with { Feather = v });
        form.Number("Contrast (%)", 0, 0, 100, v => settings = settings with { Contrast = v });
        form.Number("Shift Edge (%)", 0, -100, 100, v => settings = settings with { ShiftPercent = v }); form.Preview();
        DocumentSelection Refined()
        {
            using var draft = new CanvasDocument(Guid.NewGuid(), doc.Width, doc.Height);
            var placement = new LayerTransform(0, 0, doc.Width, doc.Height);
            var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(source.Copy(), "Preview"), placement, "Preview") { Mask = new LayerMask(ImportedImage.Create(coverage.Copy(), "Mask")) }; draft.Layers.Add(layer);
            MaskRefinement.Apply(draft, layer.ID, settings);
            return DocumentSelection.FromCoverage(MaskProperties.Coverage(layer.Mask!, placement, placement, doc.Width, doc.Height));
        }
        form.Changed = () => { if (!ReferenceEquals(doc, _document) || _history.CurrentRevision != revision) return; doc.Selection = form.PreviewEnabled ? Refined() : original; _canvas.InvalidateVisual(); };
        var accepted = await form.ShowDialog<bool>(this);
        if (!ReferenceEquals(doc, _document) || _history.CurrentRevision != revision) return;
        doc.Selection = original;
        if (accepted) Edit("Select and Mask", () => { doc.Selection = Refined(); return true; }); else SyncCropOptions();
        _canvas.InvalidateVisual();
    }
}
