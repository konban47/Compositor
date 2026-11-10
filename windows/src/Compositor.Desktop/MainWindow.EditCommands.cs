using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private Guid? _toggleRevision;
    private bool _toggleUndone;
    private void ToggleLastState()
    {
        if (_toggleUndone && _toggleRevision == _history.CurrentRevision && _history.CanRedo) { Redo(); _toggleUndone = false; }
        else if (_history.CanUndo) { Undo(); _toggleUndone = true; }
        _toggleRevision = _history.CurrentRevision;
    }
    private async Task FadeLastEdit()
    {
        CommitText();
        if (_document is not { } doc || Selected is not { } id) return;
        var states = _history.States(doc, id); var index = states.ToList().FindIndex(s => s.IsCurrent);
        if (index <= 0 || states[index - 1].Snapshot.Document is not { } before || !EditWorkspace.CanFade(before, doc, id))
        { Say("The last operation cannot be faded on this layer."); return; }
        var revision = _history.CurrentRevision;
        var form = new FormDialog("Fade", height: 330); var opacity = form.Number("Opacity (%)", 100, 0, 100);
        if (await form.ShowDialog<bool>(this) && ReferenceEquals(doc, _document) && revision == _history.CurrentRevision)
            Edit("Fade", () => EditWorkspace.Fade(doc, before, id, (double)(opacity.Value ?? 100) / 100));
    }
    private void PasteMasked(bool outside)
    {
        if (_document is not { } doc || doc.Selection.Path is null || doc.Selection.IsEmpty)
        { Say("Make a destination selection before pasting into a mask."); return; }
        var selection = doc.Selection; var old = doc.Layers.Select(l => l.ID).ToHashSet(); var before = doc.Clone();
        _history.Begin(outside ? "Paste Outside" : "Paste Into", doc, Selected);
        try
        {
            Paste();
            var made = doc.Layers.Where(l => !old.Contains(l.ID)).ToArray();
            var roots = made.Where(l => l.ParentID is null || old.Contains(l.ParentID.Value)).Select(l => l.ID).ToArray();
            if (roots.Length == 0) return;
            if (!EditWorkspace.MaskPasted(doc, roots, selection, outside)) { doc.Adopt(before); Say("The paste would exceed the mask memory limit."); }
        }
        finally { _history.End(_document, Selected); Show(_open); Refresh(); }
    }
    private async Task PastePlainText()
    {
        var tab = _open; var text = Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;
        if (string.IsNullOrEmpty(text) || !ReferenceEquals(tab, _open) || _document is not { } doc) return;
        if (text.Length > 1_000_000) { Say("That text is too long."); return; }
        var color = BrushColour(); Guid? made = null;
        Edit("Paste without Formatting", () => (made = TextEdits.Add(doc, new LayerTextStyle { Content = text, FontName = "Segoe UI", FontSize = 32,
            Red = color.Red / 255.0, Green = color.Green / 255.0, Blue = color.Blue / 255.0 }, new SKPoint(doc.Width / 2f, doc.Height / 2f))) is not null);
        Reselect(made);
    }
    private async Task FindReplaceText()
    {
        if (_document is not { } doc) return;
        (Guid Layer, int Index, int Length)? found = null;
        FindReplaceDialog? dialog = null;
        List<Guid> Layers() => [.. doc.Layers.Where(l => l.LiveText is not null && LayerProtection.CanPaint(doc, l.ID) && (dialog!.AllLayers.IsChecked == true || l.ID == Selected)).Select(l => l.ID)];
        string Text(Guid id) => doc.Layers.First(l => l.ID == id).LiveText!.Content;
        (Guid, int, int)? Next(bool wrapped = false)
        {
            var layers = Layers(); if (layers.Count == 0 || string.IsNullOrEmpty(dialog!.Find.Text)) return null;
            var forward = dialog.Forward.IsChecked == true;
            var start = found is { } at ? layers.IndexOf(at.Layer) : forward ? 0 : layers.Count - 1; if (start < 0) start = 0;
            for (var step = 0; step <= layers.Count; step++)
            {
                var index = ((forward ? start + step : start - step) % layers.Count + layers.Count) % layers.Count; var id = layers[index]; var text = Text(id);
                var from = step == 0 && found is { } current && current.Layer == id ? (forward ? current.Index + Math.Max(1, current.Length) : current.Index - 1) : forward ? 0 : text.Length;
                if (step == layers.Count && from == 0) break;
                if (FindReplaceDialog.Next(text, dialog.Find.Text!, Math.Clamp(from, 0, text.Length), forward, dialog.Options, dialog.WholeWord.IsChecked == true) is { } hit) return (id, hit.Index, hit.Length);
            }
            return null;
        }
        bool Replace((Guid Layer, int Index, int Length) hit)
        {
            var layer = doc.Layers.First(l => l.ID == hit.Layer); var style = CopyFilterSettings(layer.LiveText!);
            style.Content = style.Content.Remove(hit.Index, hit.Length).Insert(hit.Index, dialog!.Change.Text ?? ""); style.ColorRuns = null; style.FontRuns = null;
            return style.Content.Length <= 1_000_000 && TextEdits.SetStyle(doc, hit.Layer, style);
        }
        void Act(string what)
        {
            if (!ReferenceEquals(doc, _document)) { dialog!.Close(); return; }
            CommitText();
            if (what is "change" or "change-find" && found is { } hit)
            {
                if (Edit("Find and Replace Text", () => Replace(hit))) found = (hit.Layer, hit.Index, (dialog!.Change.Text ?? "").Length);
                dialog!.Status.Text = Localize.Text("Changed.");
                if (what == "change") { dialog.Update(false); return; }
            }
            if (what == "all")
            {
                var count = 0; found = null;
                Edit("Find and Replace Text", () =>
                {
                    var forward = dialog!.Forward.IsChecked; dialog.Forward.IsChecked = true;
                    try { for (var guard = 0; guard < 100_000 && Next() is { } hit2; guard++) { if (!Replace(hit2)) break; found = (hit2.Item1, hit2.Item2, (dialog.Change.Text ?? "").Length); count++; } }
                    finally { dialog.Forward.IsChecked = forward; }
                    return count > 0;
                });
                found = null; dialog!.Status.Text = string.Format(Localize.Text("{0} replacements made."), count); dialog.Update(false); return;
            }
            found = Next();
            if (found is { } next) { Reselect(next.Layer); dialog!.Status.Text = string.Format(Localize.Text("Found in “{0}”."), doc.Layers.First(l => l.ID == next.Layer).Name); }
            else dialog!.Status.Text = Localize.Text("No match was found.");
            dialog.Update(found is not null);
        }
        dialog = new FindReplaceDialog(Act);
        await dialog.ShowDialog(this);
    }
    private static bool FinishMaskedImage(CanvasDocument doc, ref Guid? made, DocumentSelection selection)
    {
        if (made is not { } id) return false;
        if (EditWorkspace.MaskPasted(doc, [id], selection, false)) return true;
        var layer = doc.Layers.First(l => l.ID == id);
        doc.Layers.Remove(layer); layer.Asset?.Dispose(); made = null;
        return false;
    }
    private Task EditFill() => PaintDialog(false);
    private Task EditStroke() => PaintDialog(true);
    private async Task PaintDialog(bool stroke)
    {
        if (_document is not { } doc || Selected is not { } id || doc.Layers.FirstOrDefault(l => l.ID == id) is not { IsGroup: false, Adjustment: null } layer) return;
        var revision = _history.CurrentRevision;
        var selection = doc.Selection.Path is not null && !doc.Selection.IsEmpty;
        var dialog = new PaintDialog(stroke, BrushColour(), selection && layer.Asset is not null, false, () => _options.Brush.Pattern is not null) { LoadPattern = ChoosePattern };
        if (!await dialog.ShowDialog<bool>(this) || !ReferenceEquals(doc, _document) || _history.CurrentRevision != revision) return;
        var choice = dialog.Result;
        if (choice.Contents == "ContentAware") { ContentAwareFill(); return; }
        var ink = choice.Contents switch
        {
            "Foreground" => BrushColour(), "Background" => BackgroundColour(), "Black" => SKColors.Black, "White" => SKColors.White, "Gray" => new SKColor(128, 128, 128), _ => choice.Color,
        };
        var pattern = choice.Contents == "Pattern" ? _options.Brush.Pattern : null;
        SKColorF Ink(SKColor c) => new(c.Red / 255f, c.Green / 255f, c.Blue / 255f, c.Alpha / 255f);
        Func<SKPoint, SKColorF> sample = pattern is null ? _ => Ink(ink)
            : point => Ink(pattern.GetPixel(((int)Math.Floor(point.X) % pattern.Width + pattern.Width) % pattern.Width, ((int)Math.Floor(point.Y) % pattern.Height + pattern.Height) % pattern.Height));
        SKBitmap? band = null; Func<SKPoint, float>? reach = null;
        if (stroke)
        {
            var t = layer.Transform;
            using var outline = selection ? new SKPath(doc.Selection.Path!) : SKPath.ParseSvgPathData(FormattableString.Invariant($"M{t.X} {t.Y} h{t.Width} v{t.Height} h{-t.Width} Z"));
            band = EditWorkspace.StrokeBand(doc, outline, choice.Width, choice.Location);
            var strokeBand = band;
            reach = point => (int)point.X is var x && (int)point.Y is var y && x >= 0 && y >= 0 && x < strokeBand.Width && y < strokeBand.Height ? strokeBand.GetPixelSpan()[y * strokeBand.RowBytes + x] / 255f : 0;
        }
        try { Edit(stroke ? "Stroke" : "Fill", () => EditWorkspace.EnsurePixels(doc, layer) && DeepPaint.Paint(doc, layer, sample, choice.Opacity, choice.Mode == PaintBlend.Clear, choice.Mode, choice.Preserve, reach)); }
        finally { band?.Dispose(); }
    }
    private async Task Purge(string kind)
    {
        if (kind == "Video Cache") { Say("This editor has no video cache."); return; }
        if (!await ConfirmDialog.Ask(this, "Purge", "Purging cannot be undone. Continue?", "Purge", "Cancel")) return;
        if (kind is "Clipboard" or "All") { _clipboard?.Dispose(); _clipboard = null; _layerClipboard = null; if (Clipboard is { } clipboard) await clipboard.ClearAsync(); }
        if (kind is "History" or "All") { _history.ClearSteps(); foreach (var item in _history.Snapshots.ToArray()) _history.DeleteSnapshot(item.ID); }
        Refresh();
    }
    private async Task GenerativeFill()
    {
        if (_document is not { } doc || doc.Selection.Path is null) { Say("Make a selection before generative fill."); return; }
        var revision = _history.CurrentRevision; var selection = doc.Selection;
        using var composite = DocumentRenderer.Render(doc);
        var form = new GenerativeDialog(false, reference: PngCodec.Encode(composite), prompt: "Edit this image as requested, preserving the original composition and dimensions.");
        if (await form.ShowDialog<GenerativeDialog.Result?>(this) is not { } result || !ReferenceEquals(doc, _document) || _history.CurrentRevision != revision) return;
        using var image = SKBitmap.Decode(result.Image.Bytes); if (image is null) return;
        using var resized = image.Resize(Bitmaps.ColorInfo(doc.Width, doc.Height), new SKSamplingOptions(SKFilterMode.Linear)); if (resized is null) return;
        Guid? made = null;
        Edit("Generative Fill", () =>
        {
            made = LayerPlacement.AddImage(doc, resized.Copy(), SKRectI.Create(doc.Width, doc.Height), Localize.Text("Generative Fill"), Selected);
            return FinishMaskedImage(doc, ref made, selection);
        });
        Reselect(made);
    }
}
