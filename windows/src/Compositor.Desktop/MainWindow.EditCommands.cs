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
        var form = new FormDialog("Find and Replace Text", height: 430);
        var find = form.Text("Find", ""); var replace = form.Text("Replace With", "");
        var match = form.Check("Match Case", false); var whole = form.Check("Whole Words", false); var all = form.Check("All Text Layers", true);
        form.Validate = () => string.IsNullOrEmpty(find.Text) ? "Enter text to find." : null;
        if (!await form.ShowDialog<bool>(this) || !ReferenceEquals(doc, _document)) return;
        var pattern = Regex.Escape(find.Text!); if (whole.IsChecked == true) pattern = @"\b" + pattern + @"\b";
        var regex = new Regex(pattern, match.IsChecked == true ? RegexOptions.None : RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        var replacements = new List<(Guid ID, LayerTextStyle Style)>();
        try
        {
            foreach (var layer in doc.Layers.Where(l => l.LiveText is not null && (all.IsChecked == true || l.ID == Selected)))
            {
                if (!LayerProtection.CanPaint(doc, layer.ID)) continue;
                var style = CopyFilterSettings(layer.LiveText!); var result = regex.Replace(style.Content, _ => replace.Text ?? "");
                if (result == style.Content) continue;
                if (result.Length > 1_000_000) { Say("That text is too long."); return; }
                style.Content = result; style.ColorRuns = null; style.FontRuns = null;
                replacements.Add((layer.ID, style));
            }
        }
        catch (RegexMatchTimeoutException) { Say("The text search timed out. Try a shorter search."); return; }
        var changed = 0;
        Edit("Find and Replace Text", () =>
        {
            foreach (var (id, style) in replacements) if (TextEdits.SetStyle(doc, id, style)) changed++;
            return changed > 0;
        });
        Say(Localize.Text("Text layers updated") + $": {changed}");
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
        if (_document is not { } doc || Selected is not { } id) return;
        var revision = _history.CurrentRevision; var form = new FormDialog(stroke ? "Stroke" : "Fill", height: 510);
        var source = stroke ? null : form.Choice("Contents", ["Foreground Color", "Background Color", "Color", "Pattern", "Black", "White", "50% Gray"], 0);
        var color = form.Text("Color (#RRGGBB)", $"#{BrushColour().Red:X2}{BrushColour().Green:X2}{BrushColour().Blue:X2}");
        var width = stroke ? form.Number("Width (px)", 3, 1, 250) : null;
        var location = stroke ? form.Choice("Location", ["Inside", "Center", "Outside"], 1) : null;
        var opacity = form.Number("Opacity (%)", 100, 0, 100);
        var modes = new[] { SKBlendMode.SrcOver, SKBlendMode.Multiply, SKBlendMode.Screen, SKBlendMode.Overlay, SKBlendMode.Darken, SKBlendMode.Lighten, SKBlendMode.Difference };
        var mode = form.Choice("Blending Mode", ["Normal", "Multiply", "Screen", "Overlay", "Darken", "Lighten", "Difference"], 0);
        var preserve = form.Check("Preserve Transparency", false);
        if (!stroke) form.Fields.Children.Add(FormDialog.ActionButton("Load Pattern…", () => _ = ChoosePattern()));
        form.Validate = () => !SKColor.TryParse(color.Text, out _) ? "Enter a valid color." : source?.SelectedIndex == 3 && _options.Brush.Pattern is null ? "Load a pattern first." : null;
        if (!await form.ShowDialog<bool>(this) || !ReferenceEquals(doc, _document) || _history.CurrentRevision != revision) return;
        SKColor.TryParse(color.Text, out var ink);
        if (!stroke) ink = source!.SelectedIndex switch { 0 => BrushColour(), 1 => BackgroundColour(), 4 => SKColors.Black, 5 => SKColors.White, 6 => new SKColor(128, 128, 128), _ => ink };
        Edit(stroke ? "Stroke" : "Fill", () => EditWorkspace.Paint(doc, id, ink, (double)(opacity.Value ?? 100) / 100, modes[mode.SelectedIndex],
            preserve.IsChecked == true, source?.SelectedIndex == 3 ? _options.Brush.Pattern : null, (double)(width?.Value ?? 0), location?.SelectedIndex ?? 1));
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
