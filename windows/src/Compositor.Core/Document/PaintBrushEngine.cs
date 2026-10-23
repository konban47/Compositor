using System.Numerics;
using System.Runtime.InteropServices;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;
namespace Compositor.Core.Document;

/// <summary>Deterministic dab painting. Only the affected rectangle has working buffers; originals are published atomically.</summary>
public static class PaintBrushEngine
{
    public static bool Paint(CanvasDocument document, ImageLayer layer, IReadOnlyList<SKPoint> points, BrushSettings brush, bool mask = false)
    {
        if (!double.IsFinite(brush.Diameter + brush.Opacity + brush.Hardness + brush.Angle + brush.Roundness) || brush.Diameter is <= 0 or > 5000 || points.Count > 60000 || points.Any(p => !float.IsFinite(p.X + p.Y))) return false;
        if (points.Count == 0 || brush.Painting is not { } options || !options.IsValid) return false;
        var asset = mask ? layer.Mask?.Asset : layer.Asset;
        if (asset is null || layer.IsGroup || !LayerProtection.CanPaint(document, layer.ID)) return false;
        var toDocument = BrushEdits.PixelToDocument(mask ? layer.MaskTransform : layer.Transform, asset.Width, asset.Height);
        if (!toDocument.TryInvert(out var toPixel)) return false;
        var symmetry = new BrushSymmetryMap(options, document.Width, document.Height);
        var left = asset.Width; var top = asset.Height; var right = 0; var bottom = 0; long work = 0;
        IEnumerable<BrushDab> Dabs() => BrushDabs.Generate(points, brush).SelectMany(d => brush.Mode == BrushMode.ArtHistory ? ArtDabs(d, options, brush.Seed) : [d]).SelectMany(symmetry.Dabs);
        SKRectI Bounds(BrushDab dab)
        {
            // Rotated square/custom tips extend beyond the circular brush radius.
            var radius = (float)(dab.Radius * (options.Tip == 22 || brush.TipImage is not null || brush.Erasing && options.EraserMode == 2 ? Math.Sqrt(1 + dab.Roundness * dab.Roundness) : 1)) + 1;
            return SKRectI.Intersect(SKRectI.Ceiling(toPixel.MapRect(SKRect.Create(dab.At.X - radius, dab.At.Y - radius, radius * 2, radius * 2))), SKRectI.Create(asset.Width, asset.Height));
        }
        foreach (var dab in Dabs()) { var box = Bounds(dab); if (box.IsEmpty) continue; left = Math.Min(left, box.Left); top = Math.Min(top, box.Top); right = Math.Max(right, box.Right); bottom = Math.Max(bottom, box.Bottom); work += (long)box.Width * box.Height; if (work > 300_000_000) throw new PaintStrokeLimitException("The stroke exceeds the interactive work limit. Increase spacing or reduce the brush size."); }
        if (right <= left || bottom <= top) return false;
        var region = new SKRectI(left, top, right, bottom); var width = region.Width; var height = region.Height;
        var docRegion = SKRectI.Intersect(SKRectI.Ceiling(toDocument.MapRect(new SKRect(left, top, right, bottom))), SKRectI.Create(document.Width, document.Height));
        if (docRegion.IsEmpty) return false;
        using var selection = document.Selection.Coverage(docRegion);
        var coverage = MemoryBudget.Allocate<float>(checked(width * height));
        using var ink = Bitmaps.Allocate(Bitmaps.ColorInfo(width, height)); ink.Erase(SKColors.Transparent);
        using var texture = Decode(options.TextureImage);
        using var loadImage = Decode(options.MixerImage);
        using var allLayers = brush.Mode == BrushMode.Mixer && options.MixerAllLayers && !mask ? DocumentRenderer.RenderRegion(document, docRegion) : null;
        using var clone = brush.Mode == BrushMode.Clone ? CloneSampling.Read(document, layer.ID, [new SKPoint(docRegion.Left, docRegion.Top), new SKPoint(docRegion.Right, docRegion.Bottom)], brush, out brush) : null;
        var history = brush.Mode is BrushMode.History or BrushMode.ArtHistory ? brush.History : null;
        if (brush.Mode is BrushMode.History or BrushMode.ArtHistory && (history is null || history.Width < document.Width || history.Height < document.Height)) return false;
        var original = asset.Image;
        var foreground = new SKColor((byte)Math.Clamp(brush.Red * 255, 0, 255), (byte)Math.Clamp(brush.Green * 255, 0, 255), (byte)Math.Clamp(brush.Blue * 255, 0, 255));
        var start = toPixel.MapPoint(points[0]);
        SKColor OriginalAt(int x, int y) => original.GetPixel(Math.Clamp(x, 0, original.Width - 1), Math.Clamp(y, 0, original.Height - 1));
        var firstColor = OriginalAt((int)Math.Floor(start.X), (int)Math.Floor(start.Y));
        var autoErase = options.Pencil && options.AutoErase && Distance(firstColor, foreground) <= 2 && firstColor.Alpha > 0;
        var reservoir = options.Reservoir;
        var carried = reservoir?.Color ?? new SKColor(options.MixerColor);
        var remaining = reservoir?.Amount ?? (options.MixerLoaded ? brush.MixerLoad : 0);
        var changed = false;
        // Reused flood buffers are sized to the largest dab only, never to the document.
        byte[] visited = []; int[] queue = [];
        foreach (var dab in Dabs())
        {
            double Control(BrushControl control) => BrushDabs.Control(control, dab.Sample, dab.Index, options.FadeSteps, dab.Direction, dab.InitialDirection);
            var box = Bounds(dab); if (box.IsEmpty || dab.Flow <= 0 || dab.Opacity <= 0) continue;
            var center = toPixel.MapPoint(dab.At); var sampled = options.ReplaceSampling switch { 1 => firstColor, 2 => new SKColor(options.Background), _ => OriginalAt((int)Math.Floor(center.X), (int)Math.Floor(center.Y)) };
            var tolerance = brush.Tolerance * Control(options.ToleranceControl);
            var color = autoErase ? new SKColor(options.Background) : dab.Color;
            double wet = brush.MixerWet, mix = brush.MixerMix;
            if (brush.Mode == BrushMode.Mixer)
            {
                if (options.TransferEnabled) { wet *= Math.Max(options.MinimumWet, Control(options.WetControl) * (1 - options.WetJitter * BrushDabs.Random(brush.Seed, dab.Index, 80))); mix *= Math.Max(options.MinimumMix, Control(options.MixControl) * (1 - options.MixJitter * BrushDabs.Random(brush.Seed, dab.Index, 81))); }
                double r = 0, g = 0, b = 0, a = 0; var n = 0;
                for (var yy = -2; yy <= 2; yy++) for (var xx = -2; xx <= 2; xx++)
                {
                    var at = toPixel.MapPoint(dab.At + new SKPoint((float)(xx * dab.Radius / 3), (float)(yy * dab.Radius / 3))); var x = (int)Math.Floor(at.X); var y = (int)Math.Floor(at.Y);
                    if (x < 0 || y < 0 || x >= original.Width || y >= original.Height) continue;
                    var c = OriginalAt(x, y); var localX = x - left; var localY = y - top;
                    if (allLayers is not null) { var dc = toDocument.MapPoint(x + .5f, y + .5f); var ax = (int)Math.Floor(dc.X) - docRegion.Left; var ay = (int)Math.Floor(dc.Y) - docRegion.Top; if (ax >= 0 && ay >= 0 && ax < allLayers.Width && ay < allLayers.Height) c = allLayers.GetPixel(ax, ay); }
                    if (localX >= 0 && localY >= 0 && localX < width && localY < height) c = BrushBlending.Composite(c, ink.GetPixel(localX, localY), coverage[localY * width + localX] * brush.Opacity, PaintBlend.Normal);
                    r += c.Red * c.Alpha; g += c.Green * c.Alpha; b += c.Blue * c.Alpha; a += c.Alpha; n++;
                }
                var picked = a <= 0 ? SKColors.Transparent : new SKColor(Byte(r / a), Byte(g / a), Byte(b / a), Byte(a / Math.Max(1, n)));
                if (remaining <= 0) carried = RetouchBrush.Mix(carried.WithAlpha(0), picked, wet);
                else carried = RetouchBrush.Mix(carried, picked, wet * .2);
                var loaded = remaining > 0 ? carried : carried.WithAlpha(Byte(carried.Alpha * wet));
                color = RetouchBrush.Mix(loaded, picked, mix * wet);
                remaining = Math.Max(0, remaining - .001 * Math.Max(.05, dab.Flow) * (brush.SpacingFraction > 0 ? brush.SpacingFraction / .25 : 1));
            }
            if (brush.Mode is BrushMode.ColorReplacement or BrushMode.BackgroundErase && options.ReplaceLimits != 0)
            {
                var count = checked(box.Width * box.Height);
                if (visited.Length < count) { visited = MemoryBudget.Allocate<byte>(count); queue = MemoryBudget.Allocate<int>(count); } else Array.Clear(visited, 0, count);
                int seed = -1; double nearest = double.MaxValue;
                for (var y = box.Top; y < box.Bottom; y++) for (var x = box.Left; x < box.Right; x++)
                { if (Distance(OriginalAt(x, y), sampled) > tolerance || OriginalAt(x, y).Alpha == 0) continue; var d = (x - center.X) * (x - center.X) + (y - center.Y) * (y - center.Y); if (d < nearest) { nearest = d; seed = (y - box.Top) * box.Width + x - box.Left; } }
                if (seed >= 0)
                {
                    var head = 0; var tail = 0; queue[tail++] = seed; visited[seed] = 1;
                    while (head < tail) { var i = queue[head++]; var x = i % box.Width; var y = i / box.Width; void Visit(int nx, int ny) { if (nx < 0 || ny < 0 || nx >= box.Width || ny >= box.Height) return; var j = ny * box.Width + nx; if (visited[j] != 0) return; var c = OriginalAt(nx + box.Left, ny + box.Top); if (c.Alpha == 0 || Distance(c, sampled) > tolerance) return; visited[j] = 1; queue[tail++] = j; } Visit(x - 1, y); Visit(x + 1, y); Visit(x, y - 1); Visit(x, y + 1); }
                }
            }
            var angle = dab.Angle * Math.PI / 180; var cos = Math.Cos(angle); var sin = Math.Sin(angle);
            for (var y = box.Top; y < box.Bottom; y++) for (var x = box.Left; x < box.Right; x++)
            {
                var at = toDocument.MapPoint(x + .5f, y + .5f); var dx = at.X - dab.At.X; var dy = at.Y - dab.At.Y;
                var tipX = (dx * cos + dy * sin) / dab.Radius * (dab.FlipX ? -1 : 1); var tipY = (-dx * sin + dy * cos) / (dab.Radius * dab.Roundness) * (dab.FlipY ? -1 : 1);
                var shape = BrushDabs.Tip(options.Tip, tipX, tipY, options.Pencil ? 1 : brush.Hardness);
                if (brush.TipImage is { } image) { var side = Math.Max(image.Width, image.Height); var tx = (int)Math.Floor(tipX * side / 2 + image.Width / 2d); var ty = (int)Math.Floor(tipY * side / 2 + image.Height / 2d); if (tx < 0 || ty < 0 || tx >= image.Width || ty >= image.Height) continue; var c = image.GetPixel(tx, ty); shape = (1 - (.2126 * c.Red + .7152 * c.Green + .0722 * c.Blue) / 255) * c.Alpha / 255; }
                if (options.Pencil || brush.Mode == BrushMode.ColorReplacement && !options.ReplaceAntialias) shape = shape >= .5 ? 1 : 0;
                if (brush.Erasing && options.EraserMode == 2) shape = Math.Abs(tipX) <= 1 && Math.Abs(tipY) <= 1 ? 1 : 0;
                if (shape <= 0) continue;
                if (options.DualEnabled)
                {
                    double second = 0; var phase = dab.Distance % Math.Max(.25, options.DualSize * options.DualSpacing);
                    for (var n = 0; n < options.DualCount; n++) { var scatter = options.DualScatter * options.DualSize; var sx = options.DualBothAxes ? (BrushDabs.Random(brush.Seed, dab.Index, 100 + n) * 2 - 1) * scatter : 0; var sy = (BrushDabs.Random(brush.Seed, dab.Index, 120 + n) * 2 - 1) * scatter; var xx = (dx - sx - phase) / (options.DualSize / 2); var yy = (dy - sy) / (options.DualSize / 2); second = Math.Max(second, BrushDabs.Tip(options.DualTip, options.DualFlip ? -xx : xx, yy, 1)); }
                    shape *= BrushBlending.Channel((float)second, (float)shape, options.DualMode);
                }
                if (options.TextureEnabled)
                {
                    var tx = (options.TextureEachTip ? dx : at.X) / options.TextureScale; var ty = (options.TextureEachTip ? dy : at.Y) / options.TextureScale;
                    double grain;
                    if (texture is not null) { var c = texture.GetPixel(Mod((int)Math.Floor(tx), texture.Width), Mod((int)Math.Floor(ty), texture.Height)); grain = (.2126 * c.Red + .7152 * c.Green + .0722 * c.Blue) / 255; }
                    else grain = options.Texture switch { 1 => (Math.Sin(tx * .8) + Math.Sin(ty * .8) + 2) / 4, 2 => (Math.Sin(tx * .2 + Math.Sin(ty * .3)) + 1) / 2, 3 => (Mod((int)Math.Floor(tx / 8), 2) + Mod((int)Math.Floor(ty / 8), 2)) % 2, _ => BrushDabs.Random(71, (int)Math.Floor(tx) + (int)Math.Floor(ty) * 32749, 0) };
                    grain = Math.Clamp((grain - .5) * (1 + options.TextureContrast * 3) + .5 + options.TextureBrightness, 0, 1); if (options.TextureInvert) grain = 1 - grain;
                    var depth = Math.Max(options.TextureMinimumDepth, options.TextureDepth * Control(options.TextureControl) * (1 - options.TextureJitter * BrushDabs.Random(brush.Seed, dab.Index, 140)));
                    shape *= options.TextureMode switch { 1 => 1 - depth + depth * grain, 2 => 1 - depth + depth * Math.Min(shape, grain), 3 => 1 - depth + depth * Math.Max(shape, grain), 4 => 1 - depth + depth * Math.Abs(shape - grain), 5 => 1 - depth + depth * BrushBlending.Channel((float)shape, (float)grain, PaintBlend.ColorBurn), _ => Math.Clamp(1 - depth + grain, 0, 1) };
                }
                if (options.Noise) shape *= BrushDabs.Random(brush.Seed, x + y * asset.Width, dab.Index + 170);
                if (options.WetEdges) { var r = Math.Sqrt(tipX * tipX + tipY * tipY); shape *= .35 + .65 * Math.Clamp(r * r, 0, 1); }
                var selected = FillEdits.Amount(selection, docRegion, at); if (selected <= 0) continue;
                var paintedColor = color;
                if (brush.Mode is BrushMode.ColorReplacement or BrushMode.BackgroundErase)
                {
                    var old = OriginalAt(x, y); var difference = Distance(old, sampled);
                    if (old.Alpha == 0 || difference > tolerance || options.ReplaceLimits != 0 && visited[(y - box.Top) * box.Width + x - box.Left] == 0) continue;
                    if (options.ReplaceLimits == 2) { var gradient = Math.Max(Distance(old, OriginalAt(x + 1, y)), Distance(old, OriginalAt(x, y + 1))); shape *= 1 - Math.Clamp(gradient / 255, 0, .9); }
                    if (options.ReplaceAntialias && tolerance > 0) shape *= Math.Clamp((tolerance - difference + 1) / Math.Max(1, tolerance * .12), 0, 1);
                    if (brush.Mode == BrushMode.BackgroundErase && brush.ProtectForeground && Distance(old, foreground) <= Math.Max(1, tolerance)) continue;
                    var mode = brush.ReplaceMode switch { 1 => PaintBlend.Hue, 2 => PaintBlend.Saturation, 3 => PaintBlend.Luminosity, _ => PaintBlend.Color };
                    paintedColor = BrushBlending.Composite(old.WithAlpha(255), color, 1, mode).WithAlpha(255);
                }
                if (clone is not null)
                {
                    var offset = brush.CloneFrom ?? default; var source = brush.CloneMapping?.MapPoint(at) ?? at + new SKPoint(offset.X, offset.Y);
                    var sx = (int)Math.Floor(source.X) - brush.SampleOrigin.X; var sy = (int)Math.Floor(source.Y) - brush.SampleOrigin.Y;
                    paintedColor = sx >= 0 && sy >= 0 && sx < clone.Width && sy < clone.Height ? clone.GetPixel(sx, sy) : SKColors.Transparent;
                }
                if (brush.Mode == BrushMode.Pattern)
                {
                    var origin = options.PatternAligned ? default : points[0]; var patternPoint = options.PatternImpressionist ? dab.At : at;
                    var px = (int)Math.Floor((patternPoint.X - origin.X) / Math.Max(.01, brush.PatternScale)); var py = (int)Math.Floor((patternPoint.Y - origin.Y) / Math.Max(.01, brush.PatternScale));
                    paintedColor = brush.Pattern is { } tile ? tile.GetPixel(Mod(px, tile.Width), Mod(py, tile.Height)) : ((Mod(px / 8, 2) + Mod(py / 8, 2)) % 2 == 0 ? SKColors.Black : SKColors.White);
                }
                if (history is not null)
                {
                    var sampleAt = brush.Mode == BrushMode.ArtHistory ? dab.At : at;
                    var sx = (int)Math.Floor(sampleAt.X) - brush.HistoryOffset.X; var sy = (int)Math.Floor(sampleAt.Y) - brush.HistoryOffset.Y;
                    paintedColor = sx >= 0 && sy >= 0 && sx < history.Width && sy < history.Height ? history.GetPixel(sx, sy) : SKColors.Transparent;
                    if (brush.Mode == BrushMode.ArtHistory && Math.Max(Distance(OriginalAt(x, y), paintedColor), Math.Abs(OriginalAt(x, y).Alpha - paintedColor.Alpha)) <= options.ArtTolerance * 255) continue;
                }
                if (loadImage is not null && brush.Mode == BrushMode.Mixer && !options.MixerSolidOnly && remaining > 0) { var tx = Math.Clamp((int)((tipX + 1) / 2 * loadImage.Width), 0, loadImage.Width - 1); var ty = Math.Clamp((int)((tipY + 1) / 2 * loadImage.Height), 0, loadImage.Height - 1); paintedColor = RetouchBrush.Mix(loadImage.GetPixel(tx, ty), paintedColor, wet); }
                var amount = Math.Clamp(shape * dab.Flow * dab.Opacity * (history is not null ? 1 : paintedColor.Alpha / 255d), 0, 1); if (amount <= 0) continue;
                var ix = x - left; var iy = y - top; var index = iy * width + ix; var prior = coverage[index]; var total = Math.Max(prior, Math.Min(dab.Opacity, prior + (1 - prior) * amount));
                var oldInk = ink.GetPixel(ix, iy); var share = Math.Min(1, amount / Math.Max(amount, total));
                ink.SetPixel(ix, iy, new SKColor(Byte(oldInk.Red * (1 - share) + paintedColor.Red * share), Byte(oldInk.Green * (1 - share) + paintedColor.Green * share), Byte(oldInk.Blue * (1 - share) + paintedColor.Blue * share), history is not null ? Byte(oldInk.Alpha * (1 - share) + paintedColor.Alpha * share) : (byte)255));
                coverage[index] = (float)total; changed = true;
            }
        }
        if (!changed) return false;
        var result = Bitmaps.Copy(mask ? original : asset.WorkingImage);
        try
        {
            var deep = !mask && asset.Deep is not null;
            for (var y = top; y < bottom; y++) for (var x = left; x < right; x++)
            {
                var amount = coverage[(y - top) * width + x - left] * brush.Opacity;
                if (amount <= 0) continue; var at = toDocument.MapPoint(x + .5f, y + .5f); amount *= FillEdits.Amount(selection, docRegion, at); if (amount <= 0) continue;
                var mode = brush.Erasing || brush.Mode == BrushMode.BackgroundErase ? PaintBlend.Clear : brush.Mode is BrushMode.Mixer or BrushMode.ColorReplacement ? PaintBlend.Normal : options.Blend;
                if (mode == PaintBlend.Dissolve) { amount = BrushDabs.Random(brush.Seed, x + y * asset.Width, 190) < amount ? 1 : 0; mode = PaintBlend.Normal; }
                var color = ink.GetPixel(x - left, y - top);
                if (mask) { var old = original.GetPixel(x, y).Red; var gray = (color.Red + color.Green + color.Blue) / 3d; result.GetPixelSpan()[y * result.RowBytes + x] = Byte(old + (gray - old) * amount); }
                else if (deep) { var span = MemoryMarshal.Cast<byte, float>(result.GetPixelSpan()); var i = y * result.RowBytes / 4 + x * 4; var next = BrushBlending.Composite(new SKColorF(span[i], span[i + 1], span[i + 2], span[i + 3]), new SKColorF(color.Red / 255f, color.Green / 255f, color.Blue / 255f, 1), amount, mode); if (history is not null && mode == PaintBlend.Normal) next = new SKColorF((float)(span[i] + (color.Red / 255f - span[i]) * amount), (float)(span[i + 1] + (color.Green / 255f - span[i + 1]) * amount), (float)(span[i + 2] + (color.Blue / 255f - span[i + 2]) * amount), (float)(span[i + 3] + (color.Alpha / 255f - span[i + 3]) * amount)); span[i] = next.Red; span[i + 1] = next.Green; span[i + 2] = next.Blue; span[i + 3] = brush.Mode == BrushMode.ColorReplacement ? span[i + 3] : next.Alpha; }
                else { var next = history is not null && mode == PaintBlend.Normal ? Restore(original.GetPixel(x, y), color, amount) : BrushBlending.Composite(original.GetPixel(x, y), color, amount, mode); if (brush.Mode == BrushMode.ColorReplacement) next = next.WithAlpha(original.GetPixel(x, y).Alpha); result.SetPixel(x, y, next); }
            }
            var imported = deep ? ImportedImage.CreateDeep(result, asset.Name, asset.BitDepth) : ImportedImage.Create(result, asset.Name);
            if (mask) layer.Mask = layer.Mask!.Replacing(imported); else layer.Asset = imported;
            if (reservoir is not null) { reservoir.Color = carried; reservoir.Amount = remaining; }
            return true;
        }
        catch { result.Dispose(); throw; }
    }
    private static SKColor Restore(SKColor old, SKColor source, double amount) => new(Byte(old.Red + (source.Red - old.Red) * amount), Byte(old.Green + (source.Green - old.Green) * amount), Byte(old.Blue + (source.Blue - old.Blue) * amount), Byte(old.Alpha + (source.Alpha - old.Alpha) * amount));
    private static IEnumerable<BrushDab> ArtDabs(BrushDab dab, PaintBrushOptions options, uint seed)
    {
        var style = options.ArtStyle; var loose = style is 3 or 4 or 8 or 9; var curl = style >= 6;
        var length = style switch { 0 => .25, 1 => .5, 2 => 1, 3 => .5, 4 => 1, 5 => 0, 6 => .5, 7 => 1, 8 => .5, _ => 1 };
        var count = style == 5 ? 1 : 8; var rotation = (dab.Angle + dab.Direction) * Math.PI / 180;
        var jitter = loose ? options.ArtArea * .4 : options.ArtArea * .08;
        var center = dab.At + new SKPoint((float)((BrushDabs.Random(seed, dab.Index, 201) - .5) * jitter), (float)((BrushDabs.Random(seed, dab.Index, 202) - .5) * jitter));
        for (var i = 0; i < count; i++)
        {
            var t = count == 1 ? 0 : i / (double)(count - 1); var a = rotation + (curl ? t * Math.PI * (loose ? 3 : 2) : 0);
            var reach = (t - .5) * options.ArtArea * length;
            yield return dab with { At = center + new SKPoint((float)(Math.Cos(a) * reach), (float)(Math.Sin(a) * reach)), Radius = Math.Max(.5, dab.Radius * (loose ? .55 : .35)), Angle = a * 180 / Math.PI };
        }
    }
    public static SKBitmap? Decode(string? data)
    {
        if (data is null) return null; if (data.Length > 6 * 1024 * 1024) throw new InvalidDataException("Brush image exceeds 4 MB.");
        var bytes = Convert.FromBase64String(data); using var codec = SKCodec.Create(new SKMemoryStream(bytes));
        if (codec is null || codec.Info.Width is < 1 or > 2048 || codec.Info.Height is < 1 or > 2048) throw new InvalidDataException("Brush images must be at most 2048 pixels per side.");
        var image = Bitmaps.Allocate(Bitmaps.ColorInfo(codec.Info.Width, codec.Info.Height));
        if (codec.GetPixels(image.Info, image.GetPixels()) != SKCodecResult.Success) { image.Dispose(); throw new InvalidDataException("Invalid brush image."); } return image;
    }
    private static double Distance(SKColor a, SKColor b) => Math.Max(Math.Abs(a.Red - b.Red), Math.Max(Math.Abs(a.Green - b.Green), Math.Abs(a.Blue - b.Blue)));
    private static byte Byte(double n) => (byte)Math.Clamp(Math.Round(n), 0, 255);
    private static int Mod(int n, int d) => (n % d + d) % d;
}
