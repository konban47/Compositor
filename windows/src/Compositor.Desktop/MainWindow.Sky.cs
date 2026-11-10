using Avalonia.Input;

using Compositor.Core.Document;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;
using SkyEngine = Compositor.Core.Document.SkyReplacement;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    /// <summary>An open Sky Replacement: the picture it works on, the sky it found there, and what the brush has changed.</summary>
    private sealed class SkySession(SkyReplacementDialog dialog, CanvasDocument document, SKBitmap composite, Tool tool, Guid revision) : IDisposable
    {
        internal SkyReplacementDialog Dialog { get; } = dialog;
        internal CanvasDocument Document { get; } = document;
        internal SKBitmap Composite { get; } = composite;
        internal Tool PreviousTool { get; } = tool;
        internal Guid Revision { get; } = revision;
        internal byte[]? Detected { get; set; }
        internal byte[]? Mask { get; set; }
        internal SKBitmap? Sky { get; set; }
        internal string? SkyPath { get; set; }
        internal CanvasDocument? Preview { get; set; }
        internal CancellationTokenSource? Pending { get; set; }
        internal SKPoint? DragFrom { get; set; }
        internal (double X, double Y) DragOffset { get; set; }
        internal int Wave { get; set; }
        public void Dispose() { Pending?.Cancel(); Composite.Dispose(); Sky?.Dispose(); Preview?.Dispose(); }
    }
    private SkySession? _sky;

    /// <summary>Edit ▸ Sky Replacement…: finds the sky (or takes the selection as it), and opens the dialog beside the document.</summary>
    private async Task SkyReplacement()
    {
        if (_document is not { } doc || _sky is not null) return;
        CommitText();
        var composite = DocumentRenderer.Render(doc);
        var dialog = new SkyReplacementDialog();
        var session = new SkySession(dialog, doc, composite, _tool, _history.CurrentRevision); _sky = session;
        var selection = doc.Selection.Path is not null && !doc.Selection.IsEmpty ? doc.Selection : null;
        dialog.Changed += () => SkyChanged(session);
        var closed = new TaskCompletionSource();
        dialog.Closed += (_, _) => { FinishSky(session); closed.TrySetResult(); };
        dialog.Show(this); SetTool(Tool.Move);
        try
        {
            session.Detected = await Task.Run(() => selection is null ? SkyEngine.Detect(composite) : Coverage(selection, doc.Width, doc.Height));
            session.Mask = (byte[])session.Detected.Clone(); SkyChanged(session);
        }
        catch (Exception error) { dialog.Report(error.Message); }
        await closed.Task;
    }
    private static byte[] Coverage(DocumentSelection selection, int width, int height)
    {
        var mask = new byte[width * height];
        using var coverage = selection.Coverage(SKRectI.Create(width, height)); if (coverage is null) return mask;
        var pixels = coverage.GetPixelSpan();
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) mask[y * width + x] = pixels[y * coverage.RowBytes + x];
        return mask;
    }
    /// <summary>A setting, the sky, the tool or the brush changed: switch the canvas tool to match, and redraw the preview a moment later.</summary>
    private void SkyChanged(SkySession session)
    {
        if (!ReferenceEquals(_sky, session)) return;
        var tool = session.Dialog.Tool switch { "hand" => Tool.Pan, "zoom" => Tool.Zoom, _ => Tool.Move };
        if (_tool != tool) SetTool(tool);
        // A burst of changes (a slider drag, a brush stroke) redraws once, a moment after it settles.
        var wave = ++session.Wave; _ = Settle();
        async Task Settle() { await Task.Delay(140); if (wave == session.Wave) await PreviewSky(session); }
    }
    private SKBitmap? LoadSky(SkySession session)
    {
        if (session.Dialog.Sky is not { } entry) return null;
        if (session.SkyPath == entry.Path && session.Sky is not null) return session.Sky;
        session.Sky?.Dispose(); session.Sky = SKBitmap.Decode(entry.Path); session.SkyPath = entry.Path; return session.Sky;
    }
    private async Task PreviewSky(SkySession session)
    {
        if (!ReferenceEquals(_sky, session) || session.Mask is not { } mask) return;
        if (!session.Dialog.ShowPreview || LoadSky(session) is not { } sky) { ClearSkyPreview(session); return; }
        session.Pending?.Cancel(); var cancel = session.Pending = new CancellationTokenSource(); var settings = session.Dialog.Settings; var maskCopy = (byte[])mask.Clone();
        try
        {
            var preview = await Task.Run(() =>
            {
                using var layers = SkyEngine.Build(session.Composite, maskCopy, sky, settings);
                cancel.Token.ThrowIfCancellationRequested();
                using var composite = SkyEngine.Composite(session.Composite, layers, settings);
                var copy = session.Document.Clone();
                LayerPlacement.AddImage(copy, composite.Copy(), SKRectI.Create(copy.Width, copy.Height), "Sky Replacement", null);
                return copy;
            }, cancel.Token);
            if (cancel.IsCancellationRequested || !ReferenceEquals(_sky, session)) { preview.Dispose(); return; }
            var old = session.Preview; session.Preview = preview; _canvas.PreviewDocument = preview; _canvas.InvalidateVisual(); old?.Dispose();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { session.Dialog.Report(error.Message); }
    }
    private void ClearSkyPreview(SkySession session)
    {
        if (ReferenceEquals(_canvas.PreviewDocument, session.Preview)) _canvas.PreviewDocument = null;
        session.Preview?.Dispose(); session.Preview = null; _canvas.InvalidateVisual();
    }

    /// <summary>Sky Move drags the sky; Sky Brush paints sky in, or out with Alt held.</summary>
    private bool SkyPressed(SKPoint at, KeyModifiers modifiers)
    {
        if (_sky is not { Mask: not null } session || session.Dialog.Tool is not ("move" or "brush")) return false;
        session.DragFrom = at; session.DragOffset = (session.Dialog.Settings.OffsetX, session.Dialog.Settings.OffsetY);
        if (session.Dialog.Tool == "brush") PaintSky(session, at, modifiers.HasFlag(KeyModifiers.Alt));
        return true;
    }
    private bool SkyMoved(SKPoint at, KeyModifiers modifiers)
    {
        if (_sky is not { DragFrom: { } from } session) return false;
        if (session.Dialog.Tool == "brush") PaintSky(session, at, modifiers.HasFlag(KeyModifiers.Alt));
        else session.Dialog.SetOffset(session.DragOffset.X + at.X - from.X, session.DragOffset.Y + at.Y - from.Y);
        SkyChanged(session); return true;
    }
    private bool SkyReleased() { if (_sky is not { DragFrom: not null } session) return false; session.DragFrom = null; SkyChanged(session); return true; }
    private static void PaintSky(SkySession session, SKPoint at, bool erase)
    {
        var mask = session.Mask!; int width = session.Document.Width, height = session.Document.Height;
        var radius = Math.Max(4, Math.Max(width, height) * .025);
        for (var y = (int)Math.Max(0, at.Y - radius); y < Math.Min(height, at.Y + radius); y++)
            for (var x = (int)Math.Max(0, at.X - radius); x < Math.Min(width, at.X + radius); x++)
            {
                var d = Math.Sqrt((x + .5 - at.X) * (x + .5 - at.X) + (y + .5 - at.Y) * (y + .5 - at.Y)) / radius; if (d >= 1) continue;
                var strength = (1 - d * d) * 255; var i = y * width + x;
                mask[i] = (byte)Math.Clamp(erase ? mask[i] - strength : mask[i] + strength, 0, 255);
            }
    }

    /// <summary>The dialog closed: on OK the sky is added as Photoshop adds it, then the tool and the canvas go back as they were.</summary>
    private void FinishSky(SkySession session)
    {
        if (!ReferenceEquals(_sky, session)) return;
        _sky = null; ClearSkyPreview(session);
        try
        {
            if (session.Dialog.Accepted && session.Mask is { } mask && LoadSky(session) is { } sky && ReferenceEquals(session.Document, _document) && session.Revision == _history.CurrentRevision)
                ApplySky(session.Document, session.Composite, mask, sky, session.Dialog.Settings, session.Dialog.DuplicateLayer);
        }
        catch (Exception error) { Say(error.Message); }
        finally { SetTool(session.PreviousTool); session.Dispose(); Refresh(); }
    }
    private void ApplySky(CanvasDocument doc, SKBitmap composite, byte[] mask, SKBitmap sky, SkySettings settings, bool duplicate)
    {
        using var layers = SkyEngine.Build(composite, mask, sky, settings);
        var whole = SKRectI.Create(doc.Width, doc.Height); Guid? made = null;
        if (duplicate)
        {
            using var flat = SkyEngine.Composite(composite, layers, settings);
            Edit("Sky Replacement", () => (made = LayerPlacement.AddImage(doc, flat.Copy(), whole, Localize.Text("Sky Replacement"), Selected)) is not null);
            Reselect(made); return;
        }
        Edit("Sky Replacement", () =>
        {
            if (LayerPlacement.AddImage(doc, layers.Sky.Copy(), whole, Localize.Text("Sky"), Selected) is not { } skyLayer) return false;
            using (var gray = new SKBitmap(new SKImageInfo(doc.Width, doc.Height, SKColorType.Gray8, SKAlphaType.Opaque)))
            {
                layers.Mask.GetPixelSpan().CopyTo(gray.GetPixelSpan());
                EditWorkspace.MaskPasted(doc, [skyLayer], DocumentSelection.FromCoverage(gray), false);
            }
            if (LayerPlacement.AddImage(doc, layers.Lighting.Copy(), whole, Localize.Text("Foreground Lighting"), skyLayer) is not { } light) return false;
            doc.Layers.First(l => l.ID == light).BlendMode = settings.Lighting == SkyLighting.Multiply ? Compositor.Core.Format.LayerBlendMode.Multiply : Compositor.Core.Format.LayerBlendMode.Screen;
            if (LayerPlacement.AddImage(doc, layers.Color.Copy(), whole, Localize.Text("Foreground Color"), light) is not { } color) return false;
            doc.Layers.First(l => l.ID == color).BlendMode = Compositor.Core.Format.LayerBlendMode.Color;
            made = LayerPlacement.GroupSelected(doc, [skyLayer, light, color]);
            if (made is { } group) doc.Layers.First(l => l.ID == group).Name = Localize.Text("Sky Replacement Group");
            return true;
        });
        Reselect(made);
    }
}
