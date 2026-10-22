using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Desktop;
public sealed partial class MainWindow
{
    private SKPath? _removalArea;
    private readonly List<SKPath> _removalUndo = [];
    private SKPoint? _removalLast;
    private Guid? _removalDocument, _removalLayer, _removalRevision;
    private bool _removalBusy, _removalSubtract;
    private bool RemovalCurrent => _document is { } doc && doc.ID == _removalDocument && Selected == _removalLayer && _history.CurrentRevision == _removalRevision;
    private void RefreshRemoval() { _optionsBar.RemovalState(RemovalCurrent && _removalArea?.IsEmpty == false, _removalUndo.Count > 0, _removalBusy); _canvas.InvalidateOverlay(); }
    private void ClearRemoval()
    {
        _removalArea?.Dispose(); _removalArea = null; foreach (var path in _removalUndo) path.Dispose(); _removalUndo.Clear();
        _removalDocument = _removalLayer = _removalRevision = null; _removalLast = null; RefreshRemoval();
    }
    private void RememberRemoval()
    {
        _removalUndo.Add(_removalArea is null ? new SKPath() : new SKPath(_removalArea));
        if (_removalUndo.Count > 32) { _removalUndo[0].Dispose(); _removalUndo.RemoveAt(0); }
    }
    private bool RemovePressed(SKPoint at, KeyModifiers keys)
    {
        if (_tool != Tool.Remove) return false;
        if (_removalBusy || _document is not { } doc || Selected is not { } id || _open.ActiveAlpha is not null) return true;
        if (doc.Layers.FirstOrDefault(l => l.ID == id) is not { IsGroup: false } || !Pro.RemoveNewLayer && !LayerProtection.CanPaint(doc, id)) { Say("Select an unlocked pixel layer."); return true; }
        if (!RemovalCurrent) ClearRemoval();
        _removalDocument = doc.ID; _removalLayer = id; _removalRevision = _history.CurrentRevision;
        RememberRemoval(); _removalSubtract = Pro.RemoveSubtract ^ keys.HasFlag(KeyModifiers.Alt); _removalLast = at; RemoveDab(at, at); return true;
    }
    private bool RemoveMoved(SKPoint at)
    {
        if (_removalLast is not { } from) return false;
        RemoveDab(from, at); _removalLast = at; return true;
    }
    private void RemoveDab(SKPoint from, SKPoint to)
    {
        var radius = (float)(_options.Brush.Diameter / 2 * _options.Brush.Dynamics(_canvas.PointerPressure).Scale);
        using var builder = new SKPathBuilder(); builder.AddCircle(to.X, to.Y, radius);
        if (from != to)
        {
            var dx = to.X - from.X; var dy = to.Y - from.Y; var length = Math.Sqrt(dx * dx + dy * dy);
            var normal = new SKPoint((float)(-dy / length * radius), (float)(dx / length * radius));
            builder.AddPoly([from + normal, to + normal, to - normal, from - normal], true); builder.AddCircle(from.X, from.Y, radius);
        }
        using var dab = builder.Detach();
        var next = (_removalArea ??= new SKPath()).Op(dab, _removalSubtract ? SKPathOp.Difference : SKPathOp.Union);
        if (next is not null) { _removalArea.Dispose(); _removalArea = next; }
        RefreshRemoval();
    }
    private bool RemoveReleased()
    {
        if (_removalLast is null) return false; _removalLast = null;
        if (Pro.RemoveEachStroke && !_removalSubtract) _ = ApplyRemoval(); else RefreshRemoval(); return true;
    }
    private bool RemoveKey(KeyEventArgs e)
    {
        if (_tool != Tool.Remove || !RemovalCurrent || _removalArea is null) return false;
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None) { _ = ApplyRemoval(); return true; }
        if (e.Key == Key.Escape) { if (!_removalBusy) ClearRemoval(); return true; }
        if (e.Key == Key.Z && e.KeyModifiers == KeyModifiers.Control && _removalUndo.Count > 0) { UndoRemoval(); return true; }
        return false;
    }
    private void UndoRemoval()
    {
        if (_removalBusy || _removalUndo.Count == 0) return;
        _removalArea?.Dispose(); _removalArea = _removalUndo[^1]; _removalUndo.RemoveAt(_removalUndo.Count - 1); RefreshRemoval();
    }
    private void DrawRemoveOverlay(DrawingContext context, Func<SKPoint, Point> screen)
    {
        if (_tool != Tool.Remove || !RemovalCurrent || _removalArea is not { IsEmpty: false } area) return;
        using var path = new SKPath(area); var origin = screen(default); var x = screen(new SKPoint(1, 0)); var y = screen(new SKPoint(0, 1));
        path.Transform(new SKMatrix((float)(x.X - origin.X), (float)(y.X - origin.X), (float)origin.X, (float)(x.Y - origin.Y), (float)(y.Y - origin.Y), (float)origin.Y, 0, 0, 1));
        var color = Color.FromUInt32((Pro.RemoveOverlayColor & 0xffffff) | (uint)Math.Round(Pro.RemoveOverlayOpacity / 100 * 255) << 24);
        context.DrawGeometry(new SolidColorBrush(color), null, StreamGeometry.Parse(path.ToSvgPathData()));
    }
    private async Task RetouchAction(string action, Control anchor)
    {
        var popup = new ToolSettingsPopup { Changed = () => { OptionsChanged(); RefreshRemoval(); } };
        switch (action)
        {
            case "Clone Source": OpenUtilityPanel("Clone Source"); return;
            case "Healing Pattern…": case "Patch Pattern…": await ChoosePattern(); return;
            case "Ignore Adjustment Layers": _options.Brush = _options.Brush with { CloneIgnoreAdjustments = !_options.Brush.CloneIgnoreAdjustments }; OptionsChanged(); return;
            case "Use Pattern":
                if (_document is { } patternDoc && Selected is { } patternID)
                {
                    if (_options.Brush.Pattern is null) await ChoosePattern();
                    if (_options.Brush.Pattern is { } pattern) Edit("Patch Tool", () => RepairEdits.Patch(patternDoc, patternID, default, true, transparent: Pro.PatchTransparent, contentAware: Pro.PatchContentAware, diffusion: Pro.PatchDiffusion, pattern: pattern));
                }
                return;
            case "Content Move Structure": popup.Number("Structure:", Pro.MoveStructure, 1, 7, n => Pro.MoveStructure = (int)n, true); break;
            case "Content Move Color": popup.Number("Color:", Pro.MoveColor, 0, 10, n => Pro.MoveColor = (int)n, true); break;
            case "Pupil Size Slider": popup.Number("Pupil Size %", Pro.Pupil * 100, 5, 100, n => Pro.Pupil = n / 100, true); break;
            case "Darken Slider": popup.Number("Darken %", Pro.Darken * 100, 0, 100, n => Pro.Darken = n / 100, true); break;
            case "Sampled Healing Brush Settings":
                popup.Number("Size:", _options.Brush.Diameter, 1, 5000, n => _options.Brush = _options.Brush with { Diameter = n }, true);
                popup.Number("Hardness:", _options.Brush.Hardness * 100, 0, 100, n => _options.Brush = _options.Brush with { Hardness = n / 100 }, true);
                popup.Number("Spacing %", _options.Brush.SpacingFraction > 0 ? _options.Brush.SpacingFraction * 100 : BrushEdits.Spacing(_options.Brush.Diameter, _options.Brush.Hardness) / _options.Brush.Diameter * 100, 1, 1000, n => _options.Brush = _options.Brush with { SpacingFraction = n / 100 }, true);
                popup.Number("Angle:", _options.Brush.Angle, -180, 180, n => _options.Brush = _options.Brush with { Angle = n });
                popup.Number("Roundness:", _options.Brush.Roundness * 100, 1, 100, n => _options.Brush = _options.Brush with { Roundness = n / 100 });
                popup.Choice("Size Control", ["Off", "Pen Pressure"], _options.Brush.PressureSize ? 1 : 0, n => _options.Brush = _options.Brush with { PressureSize = n == 1 }); break;
            case "Healing Brush Settings": case "Removal Brush Size":
                popup.Number("Size:", _options.Brush.Diameter, 1, 5000, n => _options.Brush = _options.Brush with { Diameter = n }, true);
                if (action == "Healing Brush Settings")
                {
                    popup.Number("Hardness:", _options.Brush.Hardness * 100, 0, 100, n => _options.Brush = _options.Brush with { Hardness = n / 100 }, true);
                    popup.Number("Roundness:", _options.Brush.Roundness * 100, 1, 100, n => _options.Brush = _options.Brush with { Roundness = n / 100 });
                }
                break;
            case "Sampled Healing Size Pressure": case "Healing Size Pressure": case "Removal Size Pressure": _options.Brush = _options.Brush with { PressureSize = !_options.Brush.PressureSize }; OptionsChanged(); return;
            case "Add Removal Area": Pro.RemoveSubtract = false; OptionsChanged(); return;
            case "Subtract Removal Area": Pro.RemoveSubtract = true; OptionsChanged(); return;
            case "Removal Overlay Options":
                popup.Heading("Overlay Options");
                popup.Choice("Color:", ["Magenta", "Red", "Green", "Blue", "Cyan", "Yellow", "White"], Math.Max(0, Array.IndexOf(RemovalColors, Pro.RemoveOverlayColor)), n => Pro.RemoveOverlayColor = RemovalColors[n]);
                popup.Number("Opacity:", Pro.RemoveOverlayOpacity, 0, 100, n => Pro.RemoveOverlayOpacity = n, true); break;
            case "Find Distractions":
                foreach (var kind in new[] { "Common Distractions", "People", "Wires and Cables" })
                { var button = new Button { Content = Localize.Text(kind) + " · " + Localize.Text(kind == "Wires and Cables" ? "Remove" : "Find"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch }; button.Click += async (_, _) => await FindDistractions(kind); popup.Children.Add(button); }
                popup.Heading("Detection uses the configured image service. Review found areas before applying."); break;
            case "Removal Feedback":
                popup.Heading("Removal Feedback");
                var retry = new Button { Content = Localize.Text("Undo Last Removal") }; retry.Click += (_, _) => { if (!_removalBusy) Undo(); }; popup.Children.Add(retry);
                var report = new Button { Content = Localize.Text("Report an Issue…") }; report.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/konban47/Compositor/issues/new") { UseShellExecute = true }); } catch (Exception error) { Say(error.Message); } }; popup.Children.Add(report); break;
            case "Undo Removal Stroke": UndoRemoval(); return;
            case "Cancel Removal": if (!_removalBusy) ClearRemoval(); return;
            case "Apply Removal": await ApplyRemoval(); return;
            default: return;
        }
        popup.Show(anchor);
    }
    private static readonly uint[] RemovalColors = [0xFFEB54AD, 0xFFFF4040, 0xFF40FF40, 0xFF4080FF, 0xFF40FFFF, 0xFFFFFF40, 0xFFFFFFFF];
    private async Task ApplyRemoval()
    {
        if (_removalBusy || !RemovalCurrent || _removalArea is not { IsEmpty: false } area || _document is not { } doc || Selected is not { } id) return;
        _removalBusy = true; RefreshRemoval(); var revision = _history.CurrentRevision;
        var all = Pro.RemoveAllLayers; var newLayer = Pro.RemoveNewLayer; var mode = Pro.RemoveMode;
        using var path = new SKPath(area); using var lease = new DocumentLease(doc); var working = lease.Document;
        var originals = working.Layers.Select(l => l.Asset).OfType<ImportedImage>().ToHashSet(); var committed = false;
        try
        {
            SKBitmap? generated = null;
            try
            {
                // Auto stays on-device; explicit On opens the provider's existing review/send workflow.
                if (mode == 1)
                {
                    var region = RemoveEdits.Region(working, path); using var reference = RemoveEdits.Sample(working, id, region, all);
                    using (var canvas = new SKCanvas(reference)) { canvas.Translate(-region.Left, -region.Top); using var paint = new SKPaint { Color = new SKColor(235, 84, 173, 220), IsAntialias = true }; canvas.DrawPath(path, paint); }
                    var dialog = new GenerativeDialog(false, reference: PngCodec.Encode(reference), prompt: "Remove the objects covered by the magenta overlay and reconstruct plausible background. Remove the overlay itself. Preserve exact image dimensions and all unmarked content. Return the repaired image, not a mask.", title: "Generative Removal");
                    if (await dialog.ShowDialog<GenerativeDialog.Result?>(this) is not { } result || !ReferenceEquals(doc, _document) || revision != _history.CurrentRevision) return;
                    generated = SKBitmap.Decode(result.Image.Bytes) ?? throw new InvalidDataException("Invalid generated image.");
                }
                Say("Removing…");
                var made = await WorkScheduler.Run(_ => RemoveEdits.Apply(working, id, path, all, newLayer, generated), CancellationToken.None);
                if (made is null) { Say("No repairable pixels in this area. Sample all layers or choose a pixel layer."); return; }
                if (!ReferenceEquals(doc, _document) || revision != _history.CurrentRevision) { Say("The document changed. The removal result was discarded."); return; }
                Edit("Remove Tool", () => { doc.Adopt(working); return true; }); committed = true; ClearRemoval(); Reselect(made); Say();
            }
            finally { generated?.Dispose(); }
        }
        catch (Exception error) { Say("Could not remove: " + error.Message); }
        finally
        {
            if (!committed) foreach (var asset in working.Layers.Select(l => l.Asset).OfType<ImportedImage>().Where(a => !originals.Contains(a)).Distinct()) asset.Dispose();
            _removalBusy = false; RefreshRemoval();
        }
    }
    private async Task FindDistractions(string kind)
    {
        if (_removalBusy || _document is not { } doc || Selected is not { } id) return;
        var revision = _history.CurrentRevision; _removalBusy = true; RefreshRemoval();
        try
        {
            using var lease = new DocumentLease(doc);
            using var preview = await WorkScheduler.Run(_ => ViewportRenderer.Render(lease.Document, SKRectI.Create(doc.Width, doc.Height), 1536), CancellationToken.None);
            var target = kind == "People" ? "all people" : kind == "Wires and Cables" ? "power lines, wires and cables" : "incidental distractions such as litter, small unwanted background objects and photobombers, preserving the main subject";
            var dialog = new GenerativeDialog(false, reference: PngCodec.Encode(preview), prompt: $"Return only a precise black and white segmentation mask of {target}. White marks the objects to remove; black marks everything else. Preserve framing and dimensions. Return solid black if none are present. No labels.", title: "Find Distractions");
            if (await dialog.ShowDialog<GenerativeDialog.Result?>(this) is not { } result || !ReferenceEquals(doc, _document) || revision != _history.CurrentRevision) return;
            using var image = SKBitmap.Decode(result.Image.Bytes) ?? throw new InvalidDataException();
            using var mask = image.Resize(Bitmaps.ColorInfo(preview.Width, preview.Height), new SKSamplingOptions(SKFilterMode.Linear));
            if (mask is null) return;
            var binary = MemoryBudget.Allocate<byte>(checked(mask.Width * mask.Height));
            for (var y = 0; y < mask.Height; y++) for (var x = 0; x < mask.Width; x++) { var c = mask.GetPixel(x, y); binary[y * mask.Width + x] = c.Alpha >= 128 && (c.Red + c.Green + c.Blue) / 3 >= 128 ? (byte)1 : (byte)0; }
            var found = SelectionEdits.Outline(binary, mask.Width, mask.Height); if (found is null || found.IsEmpty) { found?.Dispose(); Say("No distractions were found."); return; }
            found.Transform(SKMatrix.CreateScale((float)doc.Width / mask.Width, (float)doc.Height / mask.Height));
            if (!RemovalCurrent) ClearRemoval(); RememberRemoval(); _removalArea?.Dispose(); _removalArea = found;
            _removalDocument = doc.ID; _removalLayer = id; _removalRevision = revision; Say("Review the highlighted areas; add or subtract strokes, then apply removal.");
        }
        catch (Exception error) { Say("Could not find distractions: " + error.Message); }
        finally { _removalBusy = false; RefreshRemoval(); }
        if (kind == "Wires and Cables" && RemovalCurrent) await ApplyRemoval();
    }
}
