using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Document;

public static class RepairEdits
{
    public static bool Patch(CanvasDocument doc, Guid id, SKPoint offset, bool heal, bool allLayers = false)
    {
        if (!LayerProtection.CanPaint(doc, id) || doc.Selection.Path is not { IsEmpty: false }) return false;
        var box = doc.Selection.CoverageRect(doc.Width, doc.Height);
        return BrushEdits.Paint(doc, id, [new SKPoint(box.MidX, box.MidY)], new BrushSettings(Diameter: Math.Sqrt((double)box.Width * box.Width + (double)box.Height * box.Height) + 4, Hardness: 1,
            Mode: heal ? BrushMode.HealingSample : BrushMode.Clone, CloneFrom: new SKPointI((int)offset.X, (int)offset.Y), CloneAllLayers: allLayers));
    }
    public static bool MoveContent(CanvasDocument doc, Guid id, int dx, int dy, bool extend)
    {
        if (!LayerProtection.CanPaint(doc, id) || !LayerProtection.CanMove(doc, id) || (dx == 0 && dy == 0)) return false;
        using var rollback = doc.Clone();
        using var floating = SelectionEdits.LiftPixels(doc, id); if (floating is null) return false;
        if (extend) doc.Layers.Single(l => l.ID == id).Asset = rollback.Layers.Single(l => l.ID == id).Asset;
        else if (!ContentFillEdits.Apply(doc, id)) { doc.Adopt(rollback); return false; }
        if (!SelectionEdits.SettlePixels(doc, floating, dx, dy)) { doc.Adopt(rollback); return false; }
        return true;
    }
    public static bool RedEye(CanvasDocument doc, Guid id, SKRect box, double pupil, double darken)
    {
        if (!LayerProtection.CanPaint(doc, id) || doc.Layers.FirstOrDefault(l => l.ID == id) is not { Asset: { } asset } layer) return false;
        var output = asset.Image.Copy(); var changed = false; var map = BrushEdits.PixelToDocument(layer.Transform, output.Width, output.Height);
        using var coverage = FillEdits.Coverage(doc, out var region);
        var rx = Math.Max(1, box.Width * Math.Clamp(pupil, .05, 1) / 2); var ry = Math.Max(1, box.Height * Math.Clamp(pupil, .05, 1) / 2);
        for (var y = 0; y < output.Height; y++) for (var x = 0; x < output.Width; x++)
        {
            var at = map.MapPoint(x + .5f, y + .5f); if (Math.Pow((at.X - box.MidX) / rx, 2) + Math.Pow((at.Y - box.MidY) / ry, 2) > 1) continue;
            var c = output.GetPixel(x, y); if (c.Red < Math.Max(c.Green, c.Blue) * 1.3 || c.Red < 40) continue;
            var amount = FillEdits.Amount(coverage, region, at); if (amount <= 0) continue;
            var gray = (c.Green + c.Blue) / 2.0 * (1 - Math.Clamp(darken, 0, 1) * .7);
            output.SetPixel(x, y, new SKColor((byte)(c.Red * (1 - amount) + gray * amount), c.Green, c.Blue, c.Alpha)); changed = true;
        }
        if (!changed) { output.Dispose(); return false; } layer.Asset = ImportedImage.Create(output, asset.Name); return true;
    }
}
