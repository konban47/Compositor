using System.Runtime.InteropServices;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using SkiaSharp;

namespace Compositor.Core.Document;

public enum PhotomergeLayout { Auto, Perspective, Cylindrical, Spherical, Collage, Reposition }

/// <summary>File › Automate › Photomerge: the layout, and the four options under the source list.</summary>
public sealed record PhotomergeOptions(PhotomergeLayout Layout = PhotomergeLayout.Auto, bool BlendImages = true, bool VignetteRemoval = false,
    bool GeometricDistortion = false, bool FillTransparent = false);

/// <summary>The panorama as layers, the layout that was used (Auto picks one), and the sources that matched nothing.</summary>
public sealed record PhotomergeResult(CanvasDocument Document, PhotomergeLayout Layout, IReadOnlyList<string> Unplaced, double DistortionK1);

/// <summary>
/// Photomerge stitches overlapping photos into one picture, each photo a layer. Corners are found in each photo and
/// described by the patch around them (Brown, Szeliski and Winder's multi-scale oriented patches), matched between
/// photos, and the matches that agree are found by RANSAC: a shift (Reposition), a shift, turn and scale (Collage), a
/// homography (Perspective), or a shift once the photos are laid on a cylinder or a sphere. Blending gives each photo a
/// mask that meets its neighbors at a feathered seam, after their exposures are matched; Vignette Removal also lifts
/// the corners the lens darkened, and Geometric Distortion Correction finds the barrel or pincushion that makes the
/// photos agree best.
/// </summary>
public static class Photomerge
{
    private const int MatchSide = 800;

    public static PhotomergeResult Merge(IReadOnlyList<(string Name, SKBitmap Image)> sources, PhotomergeOptions options, CancellationToken cancellation = default)
    {
        if (sources.Count < 2) throw new ArgumentException("Photomerge needs at least two photos.");
        if (sources.Count > 40) throw new ArgumentException("Photomerge takes at most 40 photos.");
        var images = sources.Select(s => new Photo(s.Name, s.Image)).ToArray();
        foreach (var photo in images) { cancellation.ThrowIfCancellationRequested(); photo.Detect(); }
        // Lens distortion first: it moves every keypoint, and so every match.
        var k1 = 0.0;
        if (options.GeometricDistortion)
        {
            var best = double.MaxValue;
            foreach (var candidate in new[] { -.24, -.2, -.16, -.12, -.08, -.04, 0, .04, .08, .12, .16, .2, .24 })
            {
                cancellation.ThrowIfCancellationRequested();
                var trial = Pairs(images, candidate, Motion.Homography, cancellation);
                if (trial.Count == 0) continue;
                // A pair that stops matching counts as badly as a match can.
                var score = trial.Sum(p => p.Cost) + (images.Length - 1 - Math.Min(images.Length - 1, trial.Count)) * 9.0;
                if (score < best) { best = score; k1 = candidate; }
            }
        }
        foreach (var photo in images) photo.K1 = k1;
        var layout = options.Layout;
        var focal = 0.0;
        if (layout == PhotomergeLayout.Auto)
        {
            // Perspective while the photos stay near their size when laid on the middle one's plane; past that the
            // view is too wide for a flat picture and they go on a cylinder.
            var planar = Solve(images, Pairs(images, k1, Motion.Homography, cancellation), Motion.Homography);
            layout = planar is { } plane && Plausible(images, plane) ? PhotomergeLayout.Perspective : PhotomergeLayout.Cylindrical;
        }
        if (layout is PhotomergeLayout.Cylindrical or PhotomergeLayout.Spherical)
        {
            focal = Focal(images, Pairs(images, k1, Motion.Homography, cancellation));
            foreach (var photo in images) { photo.Focal = focal; photo.Projection = layout == PhotomergeLayout.Cylindrical ? Projection.Cylinder : Projection.Sphere; }
        }
        var model = layout switch
        {
            PhotomergeLayout.Perspective => Motion.Homography,
            PhotomergeLayout.Collage => Motion.Similarity,
            _ => Motion.Translation,
        };
        var pairs = Pairs(images, k1, model, cancellation);
        var placement = Solve(images, pairs, model) ?? throw new InvalidOperationException("Photomerge found no overlap between the photos.");
        if (model == Motion.Homography && !Plausible(images, placement))
            throw new InvalidOperationException("The photos cover too wide a view for Perspective; try Cylindrical or Spherical.");
        var placed = images.Where((_, i) => placement.Matrices[i] is not null).ToArray();
        var unplaced = images.Where((_, i) => placement.Matrices[i] is null).Select(p => p.Name).ToArray();
        for (var i = 0; i < images.Length; i++) images[i].ToPanorama = placement.Matrices[i];

        // Where the panorama lies, and a scale that keeps it within the document limits.
        var bounds = SKRect.Empty;
        foreach (var photo in placed) bounds = bounds.IsEmpty ? photo.Outline() : SKRect.Union(bounds, photo.Outline());
        var scale = Math.Min(1, Math.Min(DocumentLimits.MaxSide / (double)Math.Max(bounds.Width, bounds.Height),
            Math.Sqrt(DocumentLimits.DocumentPixelBudget / 3.0 / Math.Max(1, bounds.Width * bounds.Height))));
        if (bounds.Width * scale < 1 || bounds.Height * scale < 1 || !double.IsFinite(scale)) throw new InvalidOperationException("Photomerge could not lay out the photos.");
        foreach (var photo in placed) photo.Shift(-bounds.Left, -bounds.Top, scale);
        var width = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale)); var height = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));

        // Exposure (and the lens's darkened corners) matched where the photos overlap.
        if (options.BlendImages || options.VignetteRemoval) Photometric(placed, width, height, options.VignetteRemoval, cancellation);

        var document = new CanvasDocument(Guid.NewGuid(), width, height);
        try
        {
            var owner = new byte[width * height]; Array.Fill(owner, byte.MaxValue);
            if (options.BlendImages) Seams(placed, owner, width, height, cancellation);
            for (var i = 0; i < placed.Length; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var photo = placed[i];
                var (pixels, box) = photo.Render(width, height);
                if (pixels is null) continue;
                var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, photo.Name), new Model.LayerTransform(box.Left, box.Top, box.Width, box.Height), photo.Name);
                if (options.BlendImages) layer.Mask = new Model.LayerMask(ImportedImage.Create(SeamMask(owner, width, height, i, box, Math.Max(2, Math.Max(width, height) / 200.0)), "Layer Mask"));
                document.Layers.Add(layer);
            }
            if (options.FillTransparent) FillTransparent(document);
            return new PhotomergeResult(document, layout, unplaced, k1);
        }
        catch { document.Dispose(); throw; }
    }

    private enum Motion { Translation, Similarity, Homography }
    private enum Projection { Plane, Cylinder, Sphere }

    /// <summary>One source photo: its features, how its pixels reach the panorama, and its exposure.</summary>
    private sealed class Photo(string name, SKBitmap image)
    {
        public string Name { get; } = name;
        public SKBitmap Image { get; } = image;
        public int Width => Image.Width;
        public int Height => Image.Height;
        public double K1 { get; set; }
        public double Focal { get; set; }
        public Projection Projection { get; set; }
        /// <summary>From this photo's (undistorted, projected) coordinates to the panorama's; null when unplaced.</summary>
        public double[]? ToPanorama { get; set; }
        public double Gain { get; set; } = 1;
        public (double A, double B) Vignette { get; set; }
        public List<(double X, double Y)> Points { get; } = [];
        public List<float[]> Descriptors { get; } = [];

        private double Half => Math.Max(Width, Height) / 2.0;

        /// <summary>A pixel of the photo as an ideal lens would have placed it.</summary>
        public (double X, double Y) Undistort(double x, double y, double k1)
        {
            var dx = (x - Width / 2.0) / Half; var dy = (y - Height / 2.0) / Half;
            var factor = 1 + k1 * (dx * dx + dy * dy);
            return (Width / 2.0 + dx * factor * Half, Height / 2.0 + dy * factor * Half);
        }
        public (double X, double Y) Distort(double x, double y)
        {
            var ux = (x - Width / 2.0) / Half; var uy = (y - Height / 2.0) / Half;
            double dx = ux, dy = uy;
            for (var i = 0; i < 6; i++) { var factor = 1 + K1 * (dx * dx + dy * dy); dx = ux / factor; dy = uy / factor; }
            return (Width / 2.0 + dx * Half, Height / 2.0 + dy * Half);
        }
        /// <summary>Onto the cylinder or sphere, or as it is for a flat layout.</summary>
        public (double X, double Y) Project(double x, double y)
        {
            if (Projection == Projection.Plane) return (x, y);
            var u = x - Width / 2.0; var v = y - Height / 2.0; var f = Focal;
            if (Projection == Projection.Cylinder) return (f * Math.Atan2(u, f), f * v / Math.Sqrt(u * u + f * f));
            return (f * Math.Atan2(u, f), f * Math.Atan2(v, Math.Sqrt(u * u + f * f)));
        }
        public (double X, double Y)? Unproject(double x, double y)
        {
            if (Projection == Projection.Plane) return (x, y);
            var f = Focal; var theta = x / f;
            if (Math.Abs(theta) >= Math.PI / 2 * .98) return null;
            if (Projection == Projection.Cylinder) return (Width / 2.0 + f * Math.Tan(theta), Height / 2.0 + y / f * f / Math.Cos(theta));
            var phi = y / f;
            if (Math.Abs(phi) >= Math.PI / 2 * .98) return null;
            var X = Math.Sin(theta) * Math.Cos(phi); var Y = Math.Sin(phi); var Z = Math.Cos(theta) * Math.Cos(phi);
            return Z <= 1e-6 ? null : (Width / 2.0 + f * X / Z, Height / 2.0 + f * Y / Z);
        }
        /// <summary>A photo pixel all the way to the panorama.</summary>
        public (double X, double Y) Forward(double x, double y)
        {
            var (ux, uy) = Undistort(x, y, K1); var (px, py) = Project(ux, uy);
            return Apply(ToPanorama!, px, py);
        }
        /// <summary>A panorama pixel back to the photo, or null where the photo cannot reach.</summary>
        public (double X, double Y)? Backward(double x, double y, double[] inverse)
        {
            var (px, py) = Apply(inverse, x, y);
            if (!double.IsFinite(px) || !double.IsFinite(py)) return null;
            if (Unproject(px, py) is not { } flat) return null;
            return Distort(flat.X, flat.Y);
        }
        public SKRect Outline()
        {
            var points = new List<(double X, double Y)>();
            for (var i = 0; i <= 16; i++)
            {
                var t = i / 16.0;
                points.Add(Forward(t * Width, 0)); points.Add(Forward(t * Width, Height));
                points.Add(Forward(0, t * Height)); points.Add(Forward(Width, t * Height));
            }
            return new SKRect((float)points.Min(p => p.X), (float)points.Min(p => p.Y), (float)points.Max(p => p.X), (float)points.Max(p => p.Y));
        }
        public void Shift(double dx, double dy, double scale)
        {
            if (ToPanorama is null) return;
            ToPanorama = Multiply([scale, 0, dx * scale, 0, scale, dy * scale, 0, 0, 1], ToPanorama);
        }

        /// <summary>Harris corners spread over the photo, each with an oriented 8×8 patch sampled from 40×40 pixels.</summary>
        public void Detect()
        {
            var (w, h, s) = LightPlanes.Grid(Width, Height, MatchSide);
            var rgba = LightPlanes.Rgba(Image, w, h);
            var gray = new float[w * h];
            for (var i = 0; i < gray.Length; i++) gray[i] = (.2126f * rgba[i * 4] + .7152f * rgba[i * 4 + 1] + .0722f * rgba[i * 4 + 2]) * rgba[i * 4 + 3];
            var smooth = LightPlanes.Blur(gray, w, h, 1);
            var ix = new float[gray.Length]; var iy = new float[gray.Length];
            for (var y = 1; y < h - 1; y++) for (var x = 1; x < w - 1; x++)
            { ix[y * w + x] = (smooth[y * w + x + 1] - smooth[y * w + x - 1]) / 2; iy[y * w + x] = (smooth[(y + 1) * w + x] - smooth[(y - 1) * w + x]) / 2; }
            var xx = new float[gray.Length]; var yy = new float[gray.Length]; var xy = new float[gray.Length];
            for (var i = 0; i < gray.Length; i++) { xx[i] = ix[i] * ix[i]; yy[i] = iy[i] * iy[i]; xy[i] = ix[i] * iy[i]; }
            xx = LightPlanes.Blur(xx, w, h, 1.5); yy = LightPlanes.Blur(yy, w, h, 1.5); xy = LightPlanes.Blur(xy, w, h, 1.5);
            var response = new float[gray.Length]; var peak = 0f;
            for (var i = 0; i < gray.Length; i++) { var tr = xx[i] + yy[i]; response[i] = xx[i] * yy[i] - xy[i] * xy[i] - .04f * tr * tr; peak = Math.Max(peak, response[i]); }
            if (peak <= 0) return;
            var gx = LightPlanes.Blur(ix, w, h, 4.5); var gy = LightPlanes.Blur(iy, w, h, 4.5);
            var patch = LightPlanes.Blur(gray, w, h, 2);
            // Patches near the edge read the edge pixels again, which matches as well as anything there can.
            const int margin = 10;
            var cell = Math.Max(16, Math.Max(w, h) / 24);
            var candidates = new List<(int X, int Y, float R)>();
            for (var y = margin; y < h - margin; y++) for (var x = margin; x < w - margin; x++)
            {
                var r = response[y * w + x];
                if (r < peak * .0005f) continue;
                var top = true;
                for (var dy = -1; dy <= 1 && top; dy++) for (var dx = -1; dx <= 1; dx++)
                    if ((dx != 0 || dy != 0) && response[(y + dy) * w + x + dx] > r) { top = false; break; }
                if (top) candidates.Add((x, y, r));
            }
            foreach (var group in candidates.GroupBy(c => (c.X / cell, c.Y / cell)))
                foreach (var (x, y, _) in group.OrderByDescending(c => c.R).Take(6))
                {
                    var angle = Math.Atan2(gy[y * w + x], gx[y * w + x]);
                    var (cos, sin) = (Math.Cos(angle), Math.Sin(angle));
                    var descriptor = new float[64];
                    for (var j = 0; j < 8; j++) for (var i = 0; i < 8; i++)
                    {
                        var ox = (i - 3.5) * 5; var oy = (j - 3.5) * 5;
                        descriptor[j * 8 + i] = LightPlanes.Sample(patch, w, h, (float)(x + .5 + ox * cos - oy * sin), (float)(y + .5 + ox * sin + oy * cos));
                    }
                    var mean = descriptor.Average(); var deviation = Math.Sqrt(descriptor.Sum(v => (v - mean) * (v - mean)) / 64);
                    if (deviation < 1e-3) continue;
                    for (var k = 0; k < 64; k++) descriptor[k] = (float)((descriptor[k] - mean) / deviation);
                    Points.Add(((x + .5) / s, (y + .5) / s)); Descriptors.Add(descriptor);
                }
        }
    }

    private sealed record Pair(int A, int B, double[] BToA, int Inliers, List<((double X, double Y) A, (double X, double Y) B)> Matches, double Cost);

    /// <summary>Each pair of photos that share enough matches, with the model from B's coordinates to A's.</summary>
    private static List<Pair> Pairs(Photo[] photos, double k1, Motion model, CancellationToken cancellation)
    {
        var pairs = new List<Pair>();
        var locks = new object();
        var jobs = new List<(int A, int B)>();
        for (var a = 0; a < photos.Length; a++) for (var b = a + 1; b < photos.Length; b++) jobs.Add((a, b));
        Parallel.ForEach(jobs, new ParallelOptions { CancellationToken = cancellation }, job =>
        {
            var (a, b) = job;
            var matches = Match(photos[a], photos[b]);
            if (matches.Count < 8) return;
            var points = matches.Select(m => (A: Coordinates(photos[a], photos[a].Points[m.A], k1), B: Coordinates(photos[b], photos[b].Points[m.B], k1))).ToList();
            // Three pixels where the corners were found, which is the photo itself up to the matching size.
            var threshold = 3.0 * Math.Max(1, Math.Max(photos[a].Width, photos[a].Height) / (double)MatchSide);
            if (Ransac(points, model, threshold, out var inliers) is not { } transform || inliers.Count < 10) return;
            // How well every match agrees, each counting at most the threshold: what the lens correction is chosen by.
            var cost = points.Sum(p => Math.Min(threshold * threshold, Error(transform, p) * Error(transform, p)));
            lock (locks) pairs.Add(new Pair(a, b, transform, inliers.Count, inliers, cost / points.Count));
        });
        return pairs;
    }

    private static (double X, double Y) Coordinates(Photo photo, (double X, double Y) point, double k1)
    {
        var (x, y) = photo.Undistort(point.X, point.Y, k1);
        return photo.Project(x, y);
    }

    /// <summary>Nearest descriptors both ways, kept when clearly nearer than the next best (Lowe's ratio).</summary>
    private static List<(int A, int B)> Match(Photo a, Photo b)
    {
        static (int Best, float First, float Second) Nearest(float[] d, List<float[]> others)
        {
            int best = -1; float first = float.MaxValue, second = float.MaxValue;
            for (var i = 0; i < others.Count; i++)
            {
                var o = others[i]; float sum = 0;
                for (var k = 0; k < 64 && sum < second; k++) { var e = d[k] - o[k]; sum += e * e; }
                if (sum < first) { second = first; first = sum; best = i; }
                else if (sum < second) second = sum;
            }
            return (best, first, second);
        }
        var result = new List<(int, int)>();
        if (a.Descriptors.Count == 0 || b.Descriptors.Count == 0) return result;
        var back = b.Descriptors.Select(d => Nearest(d, a.Descriptors).Best).ToArray();
        for (var i = 0; i < a.Descriptors.Count; i++)
        {
            var (best, first, second) = Nearest(a.Descriptors[i], b.Descriptors);
            if (best >= 0 && first < .64f * second && back[best] == i) result.Add((i, best));
        }
        return result;
    }

    /// <summary>The model most matches agree on, refined over them; null when none is found.</summary>
    private static double[]? Ransac(List<((double X, double Y) A, (double X, double Y) B)> points, Motion model, double threshold,
        out List<((double X, double Y) A, (double X, double Y) B)> inliers)
    {
        var need = model switch { Motion.Translation => 1, Motion.Similarity => 2, _ => 4 };
        inliers = [];
        if (points.Count < need + 4) return null;
        var random = new Random(points.Count * 7919 + need);
        double[]? best = null; var bestCount = 0;
        for (var iteration = 0; iteration < 600; iteration++)
        {
            var sample = new HashSet<int>();
            while (sample.Count < need) sample.Add(random.Next(points.Count));
            if (Fit(sample.Select(i => points[i]).ToList(), model) is not { } candidate) continue;
            var count = points.Count(p => Error(candidate, p) < threshold);
            if (count > bestCount) { bestCount = count; best = candidate; }
        }
        if (best is null) return null;
        for (var round = 0; round < 3; round++)
        {
            inliers = points.Where(p => Error(best, p) < threshold).ToList();
            if (Fit(inliers, model) is { } refined) best = refined;
        }
        inliers = points.Where(p => Error(best, p) < threshold).ToList();
        return best;
    }

    private static double Error(double[] m, ((double X, double Y) A, (double X, double Y) B) p)
    {
        var (x, y) = Apply(m, p.B.X, p.B.Y);
        return Math.Sqrt((x - p.A.X) * (x - p.A.X) + (y - p.A.Y) * (y - p.A.Y));
    }

    /// <summary>The model taking B's points to A's, by least squares over every point given.</summary>
    private static double[]? Fit(List<((double X, double Y) A, (double X, double Y) B)> points, Motion model)
    {
        if (model == Motion.Translation)
            return [1, 0, points.Average(p => p.A.X - p.B.X), 0, 1, points.Average(p => p.A.Y - p.B.Y), 0, 0, 1];
        if (model == Motion.Similarity)
        {
            // x' = a x − b y + c, y' = b x + a y + d.
            var n = points.Count;
            double mx = points.Average(p => p.B.X), my = points.Average(p => p.B.Y), ux = points.Average(p => p.A.X), uy = points.Average(p => p.A.Y);
            double sxx = 0, sab = 0, sba = 0;
            foreach (var p in points)
            {
                var bx = p.B.X - mx; var by = p.B.Y - my; var ax = p.A.X - ux; var ay = p.A.Y - uy;
                sxx += bx * bx + by * by; sab += bx * ax + by * ay; sba += bx * ay - by * ax;
            }
            if (sxx < 1e-9 || n < 2) return null;
            var a = sab / sxx; var b = sba / sxx;
            return [a, -b, ux - a * mx + b * my, b, a, uy - b * mx - a * my, 0, 0, 1];
        }
        // Homography by the normalized direct linear transform, h33 = 1.
        var (ta, sa) = Normalization(points.Select(p => p.A).ToList()); var (tb, sb) = Normalization(points.Select(p => p.B).ToList());
        var ata = new double[8, 8]; var atb = new double[8];
        foreach (var p in points)
        {
            var (x, y) = ((p.B.X - tb.X) * sb, (p.B.Y - tb.Y) * sb); var (u, v) = ((p.A.X - ta.X) * sa, (p.A.Y - ta.Y) * sa);
            double[] r1 = [x, y, 1, 0, 0, 0, -u * x, -u * y]; double[] r2 = [0, 0, 0, x, y, 1, -v * x, -v * y];
            for (var i = 0; i < 8; i++) { for (var j = 0; j < 8; j++) ata[i, j] += r1[i] * r1[j] + r2[i] * r2[j]; atb[i] += r1[i] * u + r2[i] * v; }
        }
        if (Solve(ata, atb) is not { } h) return null;
        double[] normalized = [h[0], h[1], h[2], h[3], h[4], h[5], h[6], h[7], 1];
        double[] fromB = [sb, 0, -tb.X * sb, 0, sb, -tb.Y * sb, 0, 0, 1];
        double[] toA = [1 / sa, 0, ta.X, 0, 1 / sa, ta.Y, 0, 0, 1];
        var result = Multiply(toA, Multiply(normalized, fromB));
        if (Math.Abs(result[8]) < 1e-12) return null;
        for (var i = 0; i < 9; i++) result[i] /= result[8];
        // A flip or a collapse is never a view of the same scene.
        var determinant = result[0] * result[4] - result[1] * result[3];
        return determinant > .05 && determinant < 20 ? result : null;
    }

    private static ((double X, double Y) Center, double Scale) Normalization(List<(double X, double Y)> points)
    {
        var cx = points.Average(p => p.X); var cy = points.Average(p => p.Y);
        var spread = points.Average(p => Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy)));
        return ((cx, cy), spread < 1e-9 ? 1 : Math.Sqrt(2) / spread);
    }

    private static double[]? Solve(double[,] a, double[] b)
    {
        var n = b.Length; var m = (double[,])a.Clone(); var v = (double[])b.Clone();
        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            for (var row = col + 1; row < n; row++) if (Math.Abs(m[row, col]) > Math.Abs(m[pivot, col])) pivot = row;
            if (Math.Abs(m[pivot, col]) < 1e-12) return null;
            if (pivot != col)
            {
                for (var k = 0; k < n; k++) (m[col, k], m[pivot, k]) = (m[pivot, k], m[col, k]);
                (v[col], v[pivot]) = (v[pivot], v[col]);
            }
            for (var row = 0; row < n; row++)
            {
                if (row == col) continue;
                var f = m[row, col] / m[col, col];
                if (f == 0) continue;
                for (var k = col; k < n; k++) m[row, k] -= f * m[col, k];
                v[row] -= f * v[col];
            }
        }
        var x = new double[n];
        for (var i = 0; i < n; i++) x[i] = v[i] / m[i, i];
        return x;
    }

    private sealed record Placement(double[]?[] Matrices);

    /// <summary>The photos placed on the one with the most matches, each through the strongest chain of pairs.</summary>
    private static Placement? Solve(Photo[] photos, List<Pair> pairs, Motion model)
    {
        if (pairs.Count == 0) return null;
        var reference = Enumerable.Range(0, photos.Length).OrderByDescending(i => pairs.Where(p => p.A == i || p.B == i).Sum(p => p.Inliers)).First();
        var matrices = new double[]?[photos.Length];
        matrices[reference] = [1, 0, 0, 0, 1, 0, 0, 0, 1];
        while (true)
        {
            // Prim's tree, the strongest edge from a placed photo to one not yet placed.
            Pair? next = null;
            foreach (var pair in pairs)
                if ((matrices[pair.A] is null) != (matrices[pair.B] is null) && (next is null || pair.Inliers > next.Inliers)) next = pair;
            if (next is null) break;
            if (matrices[next.A] is { } placedA) matrices[next.B] = Multiply(placedA, next.BToA);
            else if (Invert(next.BToA) is { } aToB) matrices[next.A] = Multiply(matrices[next.B]!, aToB);
            else break;
        }
        return new Placement(matrices);
    }

    private static bool Plausible(Photo[] photos, Placement placement)
    {
        for (var i = 0; i < photos.Length; i++)
        {
            if (placement.Matrices[i] is not { } m) continue;
            var corners = new[] { (0.0, 0.0), (photos[i].Width, 0.0), (photos[i].Width, (double)photos[i].Height), (0.0, (double)photos[i].Height) }
                .Select(c => Apply(m, c.Item1, c.Item2)).ToArray();
            if (corners.Any(c => !double.IsFinite(c.X) || !double.IsFinite(c.Y))) return false;
            var area = 0.0;
            for (var k = 0; k < 4; k++) area += corners[k].X * corners[(k + 1) % 4].Y - corners[(k + 1) % 4].X * corners[k].Y;
            var ratio = Math.Abs(area / 2) / ((double)photos[i].Width * photos[i].Height);
            if (ratio is < .4 or > 2.5) return false;
            // Projected corners keep the photo convex and the right way round.
            for (var k = 0; k < 4; k++)
            {
                var (a, b, c) = (corners[k], corners[(k + 1) % 4], corners[(k + 2) % 4]);
                if ((b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X) <= 0) return false;
            }
        }
        return true;
    }

    /// <summary>The focal length in pixels from the pairs' homographies (Szeliski and Shum), or one a little wider
    /// than the photo when they do not say.</summary>
    private static double Focal(Photo[] photos, List<Pair> pairs)
    {
        var estimates = new List<double>();
        foreach (var pair in pairs)
        {
            var a = photos[pair.A]; var b = photos[pair.B];
            // The homography between centered coordinates.
            double[] fromB = [1, 0, b.Width / 2.0, 0, 1, b.Height / 2.0, 0, 0, 1];
            double[] toA = [1, 0, -a.Width / 2.0, 0, 1, -a.Height / 2.0, 0, 0, 1];
            var h = Multiply(toA, Multiply(pair.BToA, fromB));
            double d1 = h[6] * h[7], d2 = (h[7] - h[6]) * (h[7] + h[6]), v1 = -(h[0] * h[1] + h[3] * h[4]), v2 = h[0] * h[0] + h[3] * h[3] - h[1] * h[1] - h[4] * h[4];
            double? f1 = null;
            if (Math.Abs(d1) > 1e-12 && Math.Abs(d2) > 1e-12) { var f = Math.Abs(d2) > Math.Abs(d1) ? v2 / d2 : v1 / d1; if (f > 0) f1 = Math.Sqrt(f); }
            else if (Math.Abs(d1) > 1e-12 && v1 / d1 > 0) f1 = Math.Sqrt(v1 / d1);
            else if (Math.Abs(d2) > 1e-12 && v2 / d2 > 0) f1 = Math.Sqrt(v2 / d2);
            if (f1 is { } focal && focal > .2 * a.Width && focal < 20 * a.Width) estimates.Add(focal);
        }
        if (estimates.Count == 0) return 1.1 * photos.Max(p => Math.Max(p.Width, p.Height));
        estimates.Sort();
        return estimates[estimates.Count / 2];
    }

    /// <summary>Each photo's gain, and the lens's falloff when asked, from samples where photos overlap.</summary>
    private static void Photometric(Photo[] photos, int width, int height, bool vignette, CancellationToken cancellation)
    {
        var inverses = photos.Select(p => Invert(p.ToPanorama!)).ToArray();
        var lumas = photos.Select(p => Luma(p.Image)).ToArray();
        var rows = new List<(int I, int J, double Ri, double Rj, double Difference)>();
        var step = Math.Max(2, (int)Math.Sqrt((double)width * height / 40000));
        for (var y = step / 2; y < height; y += step) for (var x = step / 2; x < width; x += step)
        {
            var covered = new List<(int Index, double Value, double Radius)>();
            for (var i = 0; i < photos.Length; i++)
            {
                if (inverses[i] is not { } inverse || photos[i].Backward(x + .5, y + .5, inverse) is not { } at) continue;
                if (at.X < 1 || at.Y < 1 || at.X >= photos[i].Width - 1 || at.Y >= photos[i].Height - 1) continue;
                var value = Sample(lumas[i], photos[i].Width, photos[i].Height, at.X, at.Y);
                if (value is < .02 or > .95) continue;
                var dx = (at.X - photos[i].Width / 2.0) / (Math.Max(photos[i].Width, photos[i].Height) / 2.0);
                var dy = (at.Y - photos[i].Height / 2.0) / (Math.Max(photos[i].Width, photos[i].Height) / 2.0);
                covered.Add((i, value, dx * dx + dy * dy));
            }
            for (var a = 0; a < covered.Count; a++) for (var b = a + 1; b < covered.Count; b++)
                rows.Add((covered[a].Index, covered[b].Index, covered[a].Radius, covered[b].Radius, Math.Log(covered[a].Value) - Math.Log(covered[b].Value)));
        }
        cancellation.ThrowIfCancellationRequested();
        if (rows.Count < 8) return;
        // Unknowns: log gain of each photo but the first, then the falloff's r² and r⁴ terms (a weak prior keeps gains near 1).
        var n = photos.Length - 1 + (vignette ? 2 : 0);
        var ata = new double[n, n]; var atb = new double[n];
        foreach (var (i, j, ri, rj, difference) in rows)
        {
            var row = new double[n];
            if (i > 0) row[i - 1] += 1; if (j > 0) row[j - 1] -= 1;
            if (vignette) { row[n - 2] = ri - rj; row[n - 1] = ri * ri - rj * rj; }
            for (var p = 0; p < n; p++) { for (var q = 0; q < n; q++) ata[p, q] += row[p] * row[q]; atb[p] += row[p] * difference; }
        }
        for (var p = 0; p < n; p++) ata[p, p] += 1e-3 * rows.Count / 100;
        if (Solve(ata, atb) is not { } solution) return;
        for (var i = 0; i < photos.Length; i++) photos[i].Gain = Math.Exp(i == 0 ? 0 : Math.Clamp(solution[i - 1], -2, 2));
        if (vignette)
        {
            // Falloff can only darken: positive terms would brighten the corners.
            var falloff = (Math.Min(0, solution[n - 2]), Math.Min(0, solution[n - 1]));
            foreach (var photo in photos) photo.Vignette = falloff;
        }
        // The middle exposure stays where it was.
        var middle = photos.Select(p => Math.Log(p.Gain)).Order().ElementAt(photos.Length / 2);
        foreach (var photo in photos) photo.Gain = Math.Exp(Math.Log(photo.Gain) - middle);
    }

    private static float[] Luma(SKBitmap image)
    {
        var rgba = LightPlanes.Rgba(image, image.Width, image.Height);
        var luma = new float[image.Width * image.Height];
        for (var i = 0; i < luma.Length; i++)
            luma[i] = .2126f * LightPlanes.Linear(rgba[i * 4]) + .7152f * LightPlanes.Linear(rgba[i * 4 + 1]) + .0722f * LightPlanes.Linear(rgba[i * 4 + 2]);
        return luma;
    }

    private static double Sample(float[] plane, int width, int height, double x, double y) => LightPlanes.Sample(plane, width, height, (float)x, (float)y);

    /// <summary>Which photo shows each pixel: the one whose middle is nearest, among those that cover it.</summary>
    private static void Seams(Photo[] photos, byte[] owner, int width, int height, CancellationToken cancellation)
    {
        var inverses = photos.Select(p => Invert(p.ToPanorama!)).ToArray();
        var centers = photos.Select(p => p.Forward(p.Width / 2.0, p.Height / 2.0)).ToArray();
        Parallel.For(0, height, new ParallelOptions { CancellationToken = cancellation }, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var best = double.MaxValue; var who = byte.MaxValue;
                for (var i = 0; i < photos.Length && i < byte.MaxValue; i++)
                {
                    if (inverses[i] is not { } inverse || photos[i].Backward(x + .5, y + .5, inverse) is not { } at) continue;
                    if (at.X < 0 || at.Y < 0 || at.X >= photos[i].Width || at.Y >= photos[i].Height) continue;
                    var d = (x - centers[i].X) * (x - centers[i].X) + (y - centers[i].Y) * (y - centers[i].Y);
                    if (d < best) { best = d; who = (byte)i; }
                }
                owner[y * width + x] = who;
            }
        });
    }

    /// <summary>A photo's layer mask: white where it is the one shown, feathered across the seam.</summary>
    private static SKBitmap SeamMask(byte[] owner, int width, int height, int index, SKRectI box, double feather)
    {
        var plane = new float[box.Width * box.Height];
        for (var y = 0; y < box.Height; y++) for (var x = 0; x < box.Width; x++)
        {
            var px = box.Left + x; var py = box.Top + y;
            plane[y * box.Width + x] = px >= 0 && py >= 0 && px < width && py < height && owner[py * width + px] == index ? 1 : 0;
        }
        var soft = LightPlanes.Blur(plane, box.Width, box.Height, feather);
        var mask = Bitmaps.Allocate(Bitmaps.MaskInfo(box.Width, box.Height));
        var bytes = mask.GetPixelSpan();
        for (var y = 0; y < box.Height; y++) for (var x = 0; x < box.Width; x++)
            bytes[y * mask.RowBytes + x] = (byte)Math.Clamp(Math.Round(Math.Max(plane[y * box.Width + x] > 0 ? .5f : 0, soft[y * box.Width + x]) * 255), 0, 255);
        return mask;
    }

    private static (SKBitmap? Pixels, SKRectI Box) Render(this Photo photo, int width, int height)
    {
        var outline = photo.Outline();
        var box = SKRectI.Intersect(new SKRectI((int)Math.Floor(outline.Left), (int)Math.Floor(outline.Top), (int)Math.Ceiling(outline.Right), (int)Math.Ceiling(outline.Bottom)), SKRectI.Create(width, height));
        if (box.IsEmpty || Invert(photo.ToPanorama!) is not { } inverse) return (null, box);
        var source = LightPlanes.Rgba(photo.Image, photo.Width, photo.Height);
        var w = photo.Width; var h = photo.Height;
        var half = Math.Max(w, h) / 2.0;
        var output = Bitmaps.Allocate(Bitmaps.ColorInfo(box.Width, box.Height));
        var target = new byte[box.Width * box.Height * 4];
        var gain = photo.Gain; var (va, vb) = photo.Vignette;
        var correct = Math.Abs(gain - 1) > 1e-6 || va != 0 || vb != 0;
        Parallel.For(0, box.Height, y =>
        {
            for (var x = 0; x < box.Width; x++)
            {
                if (photo.Backward(box.Left + x + .5, box.Top + y + .5, inverse) is not { } at) continue;
                var sx = at.X - .5; var sy = at.Y - .5;
                if (sx < -.5 || sy < -.5 || sx > w - .5 || sy > h - .5) continue;
                var ix = (int)Math.Floor(sx); var iy = (int)Math.Floor(sy); var fx = sx - ix; var fy = sy - iy;
                var pixel = new double[4];
                var weight = 0.0;
                for (var k = 0; k < 4; k++)
                {
                    var cx = Math.Clamp(ix + (k & 1), 0, w - 1); var cy = Math.Clamp(iy + (k >> 1), 0, h - 1);
                    var wk = ((k & 1) == 1 ? fx : 1 - fx) * ((k >> 1) == 1 ? fy : 1 - fy);
                    var i = (cy * w + cx) * 4; var a = source[i + 3];
                    for (var c = 0; c < 3; c++) pixel[c] += source[i + c] * a * wk;
                    pixel[3] += a * wk; weight += wk;
                }
                if (pixel[3] <= 0) continue;
                var factor = 1.0;
                if (correct)
                {
                    var dx = (at.X - w / 2.0) / half; var dy = (at.Y - h / 2.0) / half; var r = dx * dx + dy * dy;
                    factor = 1 / (gain * Math.Exp(va * r + vb * r * r));
                }
                var o = (y * box.Width + x) * 4;
                for (var c = 0; c < 3; c++)
                {
                    var value = pixel[c] / pixel[3];
                    if (correct) value = LightPlanes.Gamma((float)(LightPlanes.Linear((float)value) * factor));
                    target[o + c] = (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
                }
                target[o + 3] = (byte)Math.Clamp(Math.Round(pixel[3] / weight * 255), 0, 255);
            }
        });
        var bytes = output.GetPixelSpan();
        for (var y = 0; y < box.Height; y++) target.AsSpan(y * box.Width * 4, box.Width * 4).CopyTo(bytes.Slice(y * output.RowBytes));
        return (output, box);
    }

    /// <summary>Content-Aware Fill Transparent Areas: the merged panorama as a layer on top, its empty edges made up
    /// from the picture around them.</summary>
    private static void FillTransparent(CanvasDocument document)
    {
        var merged = Rendering.DocumentRenderer.Render(document);
        var empty = Bitmaps.Allocate(Bitmaps.MaskInfo(document.Width, document.Height));
        var pixels = merged.GetPixelSpan(); var bytes = empty.GetPixelSpan(); var any = false;
        for (var y = 0; y < document.Height; y++) for (var x = 0; x < document.Width; x++)
        {
            var hole = pixels[y * merged.RowBytes + x * 4 + 3] < 250;
            bytes[y * empty.RowBytes + x] = hole ? (byte)255 : (byte)0; any |= hole;
        }
        var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(merged, "Content-Aware Fill"), new Model.LayerTransform(0, 0, document.Width, document.Height), "Content-Aware Fill");
        document.Layers.Add(layer);
        using (empty)
        {
            if (!any) return;
            using var outline = SubjectEdits.Outline(empty);
            if (outline.IsEmpty) return;
            SelectionEdits.Apply(document, outline, SelectionMode.Replace, false);
            ContentFillEdits.Apply(document, layer.ID);
            SelectionEdits.Deselect(document);
        }
    }

    private static (double X, double Y) Apply(double[] m, double x, double y)
    {
        var w = m[6] * x + m[7] * y + m[8];
        return ((m[0] * x + m[1] * y + m[2]) / w, (m[3] * x + m[4] * y + m[5]) / w);
    }

    private static double[] Multiply(double[] a, double[] b)
    {
        var r = new double[9];
        for (var i = 0; i < 3; i++) for (var j = 0; j < 3; j++) r[i * 3 + j] = a[i * 3] * b[j] + a[i * 3 + 1] * b[3 + j] + a[i * 3 + 2] * b[6 + j];
        return r;
    }

    private static double[]? Invert(double[] m)
    {
        var det = m[0] * (m[4] * m[8] - m[5] * m[7]) - m[1] * (m[3] * m[8] - m[5] * m[6]) + m[2] * (m[3] * m[7] - m[4] * m[6]);
        if (Math.Abs(det) < 1e-15) return null;
        return
        [
            (m[4] * m[8] - m[5] * m[7]) / det, (m[2] * m[7] - m[1] * m[8]) / det, (m[1] * m[5] - m[2] * m[4]) / det,
            (m[5] * m[6] - m[3] * m[8]) / det, (m[0] * m[8] - m[2] * m[6]) / det, (m[2] * m[3] - m[0] * m[5]) / det,
            (m[3] * m[7] - m[4] * m[6]) / det, (m[1] * m[6] - m[0] * m[7]) / det, (m[0] * m[4] - m[1] * m[3]) / det,
        ];
    }
}
