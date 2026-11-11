using SkiaSharp;

namespace Compositor.Core.Pixels;

/// <summary>Port of <c>Rendering/ContentFill.c</c>: PatchMatch-style content-aware fill.</summary>
public static class ContentFill
{
    private static uint NextRandom(ref uint state)
    {
        unchecked
        {
            state = state * 1664525u + 1013904223u;
            return state;
        }
    }

    private static double Match(ReadOnlySpan<byte> pixels, int stride, ReadOnlySpan<byte> known, byte[] donor,
                                int w, int h, int p, int q, int radius)
    {
        int px = p % w, py = p / w, qx = q % w, qy = q / w, count = 0;
        double sum = 0;
        for (int dy = -radius; dy <= radius; ++dy)
            for (int dx = -radius; dx <= radius; ++dx)
            {
                int x = px + dx, y = py + dy, sx = qx + dx, sy = qy + dy;
                if (x < 0 || y < 0 || x >= w || y >= h || sx < 0 || sy < 0 || sx >= w || sy >= h
                    || known[y * w + x] == 0) continue;
                int a = y * stride + x * 4, b = (sy * w + sx) * 4;
                for (int c = 0; c < 4; ++c)
                {
                    int d = pixels[a + c] - donor[b + c];
                    sum += d * d;
                }
                ++count;
            }
        return count != 0 ? sum / count : double.MaxValue;
    }

    /// <summary>
    /// Fills the pixels selected by <paramref name="mask"/> in place, from the unselected opaque pixels
    /// around them. Selected pixels are filled; unselected opaque pixels are the image to match and copy
    /// from; unselected transparent ones are neither. Returns 1 on success and 0 when no source patch
    /// exists; allocation failure throws <see cref="OutOfMemoryException"/> rather than returning -1.
    /// </summary>
    public static int Fill(Span<byte> pixels, int stride, ReadOnlySpan<byte> mask, int maskStride,
                           int width, int height) => Fill(pixels, stride, mask, maskStride, width, height, [], 0, []);

    /// <summary>
    /// The fill with Content-Aware Fill's settings: <paramref name="sources"/>, when not empty, says which pixels may be copied from
    /// (one byte a pixel); <paramref name="transforms"/> add rotated, scaled or mirrored copies of the source to search, each matrix
    /// taking a pixel of the copy to where it reads in the source; <paramref name="colorAdaptation"/> 0 to 3 is how strongly the
    /// filled colors are pulled toward the colors at the edge of the hole afterwards.
    /// </summary>
    public static int Fill(Span<byte> pixels, int stride, ReadOnlySpan<byte> mask, int maskStride, int width, int height,
                           ReadOnlySpan<byte> sources, int colorAdaptation, IReadOnlyList<SKMatrix> transforms)
    {
        int n = width * height;
        // Every copy of the source costs a full image; past this budget the copies are left out rather than run out of memory.
        var copies = transforms.Take(Math.Max(0, 16_000_000 / Math.Max(1, n) - 1)).ToArray();
        int images = 1 + copies.Length;
        byte[] known, target, queued;
        byte[][] donors = new byte[images][], valid = new byte[images][];
        int[] list, queue, chosen;
        try
        {
            known = new byte[n];
            target = new byte[n];
            queued = new byte[n];
            queue = new int[n];
            chosen = new int[n];
            for (var k = 0; k < images; k++) { donors[k] = new byte[n * 4]; valid[k] = new byte[n]; }
        }
        catch (OutOfMemoryException)
        {
            return -1;
        }
        int radius = width >= 5 && height >= 5 ? 2 : 0;
        int missing = 0, donorCount = 0, head = 0, tail = 0, scan = 0;
        var usable = new byte[n];
        for (int y = 0; y < height; ++y)
            for (int x = 0; x < width; ++x)
            {
                int p = y * width + x;
                target[p] = (byte)(mask[y * maskStride + x] != 0 ? 1 : 0);
                known[p] = (byte)(target[p] == 0 && pixels[y * stride + x * 4 + 3] == 255 ? 1 : 0);
                usable[p] = (byte)(known[p] != 0 && (sources.IsEmpty || sources[p] != 0) ? 1 : 0);
                chosen[p] = -1;
                if (target[p] != 0) ++missing;
                pixels.Slice(y * stride + x * 4, 4).CopyTo(donors[0].AsSpan(p * 4, 4));
            }
        if (missing == 0) return 1;
        // The copies: each pixel of a copy reads the source where its matrix says, and is usable where that source pixel is.
        var usableIn = new byte[images][]; usableIn[0] = usable;
        for (var k = 1; k < images; k++)
        {
            usableIn[k] = new byte[n]; var m = copies[k - 1];
            for (int y = 0; y < height; ++y)
                for (int x = 0; x < width; ++x)
                {
                    var at = m.MapPoint(x + .5f, y + .5f); int sx = (int)Math.Floor(at.X), sy = (int)Math.Floor(at.Y);
                    if (sx < 0 || sy < 0 || sx >= width || sy >= height || usable[sy * width + sx] == 0) continue;
                    int p = y * width + x; usableIn[k][p] = 1; Array.Copy(donors[0], (sy * width + sx) * 4, donors[k], p * 4, 4);
                }
        }
        try { list = new int[images * n]; }
        catch (OutOfMemoryException) { return -1; }
        for (var k = 0; k < images; k++)
            for (int y = 0; y < height; ++y)
                for (int x = 0; x < width; ++x)
                {
                    int p = y * width + x;
                    if (usableIn[k][p] == 0) continue;
                    bool ok = true;
                    for (int dy = -radius; dy <= radius && ok; ++dy)
                        for (int dx = -radius; dx <= radius; ++dx)
                        {
                            int sx = x + dx, sy = y + dy;
                            if (sx < 0 || sy < 0 || sx >= width || sy >= height || usableIn[k][sy * width + sx] == 0)
                            {
                                ok = false;
                                break;
                            }
                        }
                    if (ok)
                    {
                        valid[k][p] = 1;
                        list[donorCount++] = k * n + p;
                    }
                }
        if (donorCount == 0) return 0;
        for (int y = 0; y < height; ++y)
            for (int x = 0; x < width; ++x)
            {
                int p = y * width + x;
                if (target[p] != 0
                    && ((x != 0 && known[p - 1] != 0) || (x + 1 < width && known[p + 1] != 0)
                        || (y != 0 && known[p - width] != 0) || (y + 1 < height && known[p + width] != 0)))
                {
                    queue[tail++] = p;
                    queued[p] = 1;
                }
            }
        uint seed = 0x6d2b79f5;
        Span<int> neighbors = stackalloc int[4];
        bool Valid(int encoded) => encoded >= 0 && encoded < images * n && valid[encoded / n][encoded % n] != 0;
        for (;;)
        {
            while (head < tail)
            {
                int p = queue[head++], x = p % width, y = p / width, best = -1;
                double score = double.MaxValue;
                neighbors[0] = x != 0 ? p - 1 : -1;
                neighbors[1] = x + 1 < width ? p + 1 : -1;
                neighbors[2] = y != 0 ? p - width : -1;
                neighbors[3] = y + 1 < height ? p + width : -1;
                // Propagate coherent source offsets, then refine with randomized patch search.
                for (int k = 0; k < 28; ++k)
                {
                    int q = -1;
                    if (k < 4)
                    {
                        int t = neighbors[k];
                        if (t >= 0)
                        {
                            var from = chosen[t] >= 0 ? chosen[t] : t; var image = from / n; var position = from % n + (p - t);
                            if (position >= 0 && position < n) q = image * n + position;
                        }
                    }
                    else
                    {
                        q = list[(int)(NextRandom(ref seed) % (uint)donorCount)];
                    }
                    if (!Valid(q)) continue;
                    double s = Match(pixels, stride, known, donors[q / n], width, height, p, q % n, radius);
                    if (best < 0 || s < score)
                    {
                        score = s;
                        best = q;
                    }
                }
                if (best < 0) best = list[0];
                for (int r = 64; r >= 1; r /= 2)
                {
                    int image = best / n, at = best % n;
                    int qx = at % width + (int)(NextRandom(ref seed) % (uint)(2 * r + 1)) - r;
                    int qy = at / width + (int)(NextRandom(ref seed) % (uint)(2 * r + 1)) - r;
                    if (qx < 0 || qy < 0 || qx >= width || qy >= height || valid[image][qy * width + qx] == 0) continue;
                    int q = image * n + qy * width + qx;
                    double s = Match(pixels, stride, known, donors[image], width, height, p, q % n, radius);
                    if (s < score)
                    {
                        score = s;
                        best = q;
                    }
                }
                // The donor is copied into the hole.
                donors[best / n].AsSpan(best % n * 4, 4).CopyTo(pixels.Slice(y * stride + x * 4, 4));
                known[p] = 1;
                chosen[p] = best;
                for (int k = 0; k < 4; ++k)
                {
                    int q = neighbors[k];
                    if (q >= 0 && target[q] != 0 && known[q] == 0 && queued[q] == 0)
                    {
                        queued[q] = 1;
                        queue[tail++] = q;
                    }
                }
            }
            // A selected area that only transparency touches starts from the best random donor, then spreads.
            while (scan < n && (target[scan] == 0 || known[scan] != 0)) ++scan;
            if (scan >= n) break;
            queue[tail++] = scan;
            queued[scan] = 1;
        }
        Refine(pixels, stride, target, chosen, donors, usableIn, valid, width, height, ref seed);
        if (colorAdaptation > 0) Adapt(pixels, stride, target, chosen, donors, width, height, colorAdaptation);
        return 1;
    }

    /// <summary>
    /// After the onion-peel pass every hole pixel has a donor, but each was chosen against a half-filled neighbourhood. Two rounds of
    /// PatchMatch improve that: each pixel tries its neighbours' offsets and a shrinking random search, now matching whole patches,
    /// and then every pixel becomes the average of what the patches overlapping it say it should be, which removes the seams and
    /// lone mismatched pixels the greedy pass leaves.
    /// </summary>
    private static void Refine(Span<byte> pixels, int stride, byte[] target, int[] chosen, byte[][] donors, byte[][] usable, byte[][] valid,
                               int width, int height, ref uint seed)
    {
        int n = width * height; const int radius = 2;
        var holes = new List<int>(); for (var p = 0; p < n; p++) if (target[p] != 0 && chosen[p] >= 0) holes.Add(p);
        if (holes.Count == 0 || holes.Count > 4_000_000) return;
        double Cost(Span<byte> image, int p, int q)
        {
            int px = p % width, py = p / width, image2 = q / n, qp = q % n, qx = qp % width, qy = qp / width, count = 0; double sum = 0;
            var donor = donors[image2]; var open = usable[image2];
            for (var dy = -radius; dy <= radius; dy++)
                for (var dx = -radius; dx <= radius; dx++)
                {
                    int x = px + dx, y = py + dy, sx = qx + dx, sy = qy + dy;
                    if (x < 0 || y < 0 || x >= width || y >= height || sx < 0 || sy < 0 || sx >= width || sy >= height || open[sy * width + sx] == 0) continue;
                    int a = y * stride + x * 4, b = (sy * width + sx) * 4;
                    for (var c = 0; c < 4; c++) { int d = image[a + c] - donor[b + c]; sum += d * d; }
                    count++;
                }
            return count < 9 ? double.MaxValue : sum / count;
        }
        bool Valid(int q) => q >= 0 && q < donors.Length * n && valid[q / n][q % n] != 0;
        var votes = new float[holes.Count * 4];
        for (var round = 0; round < 2; round++)
        {
            // Search: forward on even rounds, backward on odd ones, so offsets spread both ways.
            for (var k = 0; k < holes.Count; k++)
            {
                var p = holes[round % 2 == 0 ? k : holes.Count - 1 - k]; int x = p % width, y = p / width;
                var best = chosen[p]; var score = Cost(pixels, p, best);
                var step = round % 2 == 0 ? -1 : 1;
                foreach (var t in new[] { x + step >= 0 && x + step < width ? p + step : -1, y + step >= 0 && y + step < height ? p + step * width : -1 })
                {
                    if (t < 0 || chosen[t] < 0) continue;
                    var position = chosen[t] % n + (p - t); if (position < 0 || position >= n) continue;
                    var q = chosen[t] / n * n + position; if (!Valid(q)) continue;
                    var s = Cost(pixels, p, q); if (s < score) { score = s; best = q; }
                }
                for (var r = Math.Max(width, height) / 2; r >= 1; r /= 2)
                {
                    int image = best / n, at = best % n;
                    int qx = at % width + (int)(NextRandom(ref seed) % (uint)(2 * r + 1)) - r, qy = at / width + (int)(NextRandom(ref seed) % (uint)(2 * r + 1)) - r;
                    if (qx < 0 || qy < 0 || qx >= width || qy >= height) continue;
                    var q = image * n + qy * width + qx; if (!Valid(q)) continue;
                    var s = Cost(pixels, p, q); if (s < score) { score = s; best = q; }
                }
                chosen[p] = best;
            }
            // Vote: each hole pixel takes the mean of what every overlapping patch's donor holds at its place.
            Array.Clear(votes);
            for (var k = 0; k < holes.Count; k++)
            {
                int p = holes[k], x = p % width, y = p / width, count = 0; float r0 = 0, g0 = 0, b0 = 0, a0 = 0;
                for (var dy = -radius; dy <= radius; dy++)
                    for (var dx = -radius; dx <= radius; dx++)
                    {
                        int nx = x + dx, ny = y + dy; if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                        var q = ny * width + nx; if (target[q] == 0 || chosen[q] < 0) continue;
                        int image = chosen[q] / n, from = chosen[q] % n, fx = from % width - dx, fy = from / width - dy;
                        if (fx < 0 || fy < 0 || fx >= width || fy >= height || usable[image][fy * width + fx] == 0) continue;
                        var at = (fy * width + fx) * 4; var donor = donors[image];
                        r0 += donor[at]; g0 += donor[at + 1]; b0 += donor[at + 2]; a0 += donor[at + 3]; count++;
                    }
                if (count == 0) { var own = y * stride + x * 4; votes[k * 4] = pixels[own]; votes[k * 4 + 1] = pixels[own + 1]; votes[k * 4 + 2] = pixels[own + 2]; votes[k * 4 + 3] = pixels[own + 3]; continue; }
                votes[k * 4] = r0 / count; votes[k * 4 + 1] = g0 / count; votes[k * 4 + 2] = b0 / count; votes[k * 4 + 3] = a0 / count;
            }
            for (var k = 0; k < holes.Count; k++)
            {
                var at = holes[k] / width * stride + holes[k] % width * 4;
                for (var c = 0; c < 4; c++) pixels[at + c] = (byte)Math.Clamp(Math.Round(votes[k * 4 + c]), 0, 255);
            }
        }
    }

    /// <summary>
    /// Color adaptation: where a filled pixel meets the original image, the difference between the image and what the donor had
    /// beside it is taken, and spread smoothly through the hole (a membrane, as Poisson blending does), so the fill takes on the
    /// brightness and color of its surroundings.
    /// </summary>
    private static void Adapt(Span<byte> pixels, int stride, byte[] target, int[] chosen, byte[][] donors, int width, int height, int strength)
    {
        int n = width * height;
        var correction = new float[n * 3]; var fixedAt = new bool[n]; var holes = new List<int>(); Span<float> sum = stackalloc float[3];
        for (int p = 0; p < n; p++)
        {
            if (target[p] == 0 || chosen[p] < 0) continue;
            holes.Add(p); int x = p % width, y = p / width, image = chosen[p] / n, from = chosen[p] % n; int count = 0; sum.Clear();
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, ny = y + dy, fx = from % width + dx, fy = from / width + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height || target[ny * width + nx] != 0 || fx < 0 || fy < 0 || fx >= width || fy >= height) continue;
                for (var c = 0; c < 3; c++) sum[c] += pixels[ny * stride + nx * 4 + c] - donors[image][(fy * width + fx) * 4 + c];
                count++;
            }
            if (count == 0) continue;
            fixedAt[p] = true; for (var c = 0; c < 3; c++) correction[p * 3 + c] = sum[c] / count;
        }
        if (holes.Count == 0) return;
        var iterations = Math.Min(strength >= 3 ? 600 : 300, 2 * Math.Max(width, height));
        var next = (float[])correction.Clone();
        for (var i = 0; i < iterations; i++)
        {
            foreach (var p in holes)
            {
                if (fixedAt[p]) continue;
                int x = p % width, y = p / width, count = 0; float r = 0, g = 0, b = 0;
                foreach (var q in new[] { x > 0 ? p - 1 : -1, x + 1 < width ? p + 1 : -1, y > 0 ? p - width : -1, y + 1 < height ? p + width : -1 })
                {
                    if (q < 0 || target[q] == 0) continue;
                    r += correction[q * 3]; g += correction[q * 3 + 1]; b += correction[q * 3 + 2]; count++;
                }
                if (count == 0) continue;
                next[p * 3] = r / count; next[p * 3 + 1] = g / count; next[p * 3 + 2] = b / count;
            }
            (correction, next) = (next, correction);
        }
        var amount = strength switch { 1 => .6f, _ => 1f };
        foreach (var p in holes)
        {
            int at = p / width * stride + p % width * 4; var alpha = pixels[at + 3];
            for (var c = 0; c < 3; c++) pixels[at + c] = (byte)Math.Clamp(Math.Round(pixels[at + c] + correction[p * 3 + c] * amount), 0, alpha);
        }
    }

    /// <summary>The copies Rotation Adaptation, Scale and Mirror add, as matrices from a copy's pixel to where it reads the source.</summary>
    public static IReadOnlyList<SKMatrix> Copies(int width, int height, int rotation, bool scale, bool mirror)
    {
        var center = new SKPoint(width / 2f, height / 2f); var copies = new List<SKMatrix>();
        double[] angles = rotation switch { 1 => [-10, 10], 2 => [-20, -10, 10, 20], 3 => [-45, -30, -15, 15, 30, 45], 4 => [45, 90, 135, 180, 225, 270, 315], _ => [] };
        foreach (var angle in angles) copies.Add(SKMatrix.CreateRotationDegrees((float)angle, center.X, center.Y));
        if (scale) foreach (var factor in new[] { .8f, 1.25f }) copies.Add(SKMatrix.CreateScale(1 / factor, 1 / factor, center.X, center.Y));
        if (mirror) copies.Add(SKMatrix.CreateScale(-1, 1, center.X, center.Y));
        return copies;
    }
}
