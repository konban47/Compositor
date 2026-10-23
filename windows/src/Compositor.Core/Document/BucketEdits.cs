using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Document;

public static class BucketEdits
{
    public static bool Apply(CanvasDocument document, Guid layerID, SKPoint point, WandOptions options, SKColor color, bool erase, double opacity = 1, bool allLayers = false)
    {
        if (!LayerProtection.CanPaint(document, layerID) || opacity <= 0) return false;
        BrushEdits.EnsurePixels(document, layerID);
        using var sample = SelectionEdits.Sample(document, allLayers ? null : layerID);
        if (sample is null) return false;
        using var selected = document.Clone(); selected.Selection = DocumentSelection.All;
        if (!SelectionEdits.SelectWand(selected, sample, (int)point.X, (int)point.Y, options, SelectionMode.Replace)) return false;
        var region = SKRectI.Create(document.Width, document.Height);
        using var matches = selected.Selection.Coverage(region); if (matches is null) return false;
        using var existing = document.Selection.Coverage(region);
        var coverage = Bitmaps.Allocate(Bitmaps.MaskInfo(document.Width, document.Height));
        var buffer = coverage.GetPixelSpan(); var matched = matches.GetPixelSpan(); var old = existing is null ? default : existing.GetPixelSpan();
        for (var y = 0; y < document.Height; y++) for (var x = 0; x < document.Width; x++)
        {
            var edge = 1d;
            if (options.Antialias && matched[y * matches.RowBytes + x] > 0)
            {
                var neighbours = 0; var total = 0;
                for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
                { var nx = x + dx; var ny = y + dy; if (nx < 0 || ny < 0 || nx >= document.Width || ny >= document.Height) continue; total++; if (matched[ny * matches.RowBytes + nx] > 0) neighbours++; }
                edge = .5 + .5 * neighbours / Math.Max(1d, total);
            }
            buffer[y * coverage.RowBytes + x] = (byte)Math.Round(edge * matched[y * matches.RowBytes + x] * (existing is null ? 1 : old[y * existing.RowBytes + x] / 255.0) * Math.Clamp(opacity, 0, 1));
        }
        var saved = document.Selection;
        try { document.Selection = DocumentSelection.FromCoverage(coverage); return erase ? FillEdits.Clear(document, layerID) : FillEdits.Fill(document, layerID, color); }
        finally { document.Selection = saved; }
    }
}
