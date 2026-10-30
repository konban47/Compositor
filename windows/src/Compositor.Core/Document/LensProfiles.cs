using System.Globalization;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Linq;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using SkiaSharp;

namespace Compositor.Core.Document;

public enum LensEdge { Transparency, EdgeExtension, Black, White }

/// <summary>File › Automate › Lens Correction's options, as the reference dialog has them.</summary>
public sealed record LensCorrectionOptions(bool Distortion = true, bool ChromaticAberration = false, bool Vignette = false,
    bool AutoScale = true, LensEdge Edge = LensEdge.Transparency);

/// <summary>One calibration of a lens at a focal length (and, for vignetting, an aperture).</summary>
public sealed record LensCalibration(double Focal, string Model, double[] Terms, double Aperture = 0);

/// <summary>A lens of the database: its maker, model, mounts, the crop factor it was measured at, and its calibrations.</summary>
public sealed record LensProfile(string Maker, string Model, IReadOnlyList<string> Mounts, double CropFactor,
    IReadOnlyList<LensCalibration> Distortion, IReadOnlyList<LensCalibration> Tca, IReadOnlyList<LensCalibration> Vignetting)
{
    public string Name => Model.StartsWith(Maker, StringComparison.OrdinalIgnoreCase) ? Model : $"{Maker} {Model}";
}

public sealed record CameraProfile(string Maker, string Model, string Mount, double CropFactor);

/// <summary>What a photo says about how it was taken, from its EXIF.</summary>
public sealed record LensExif(string CameraMaker, string CameraModel, string Lens, double FocalLength, double Aperture);

/// <summary>
/// The lens profiles File › Automate › Lens Correction matches photos against: the lensfun database (its XML files,
/// CC BY-SA 3.0) and the corrections they describe — distortion as poly3, poly5 or ptlens, lateral chromatic aberration
/// as linear or poly3 scaling of red and blue, and vignetting as the pa polynomial — interpolated between the focal
/// lengths a lens was measured at. Adobe's own lens profiles are not included.
/// </summary>
public sealed class LensDatabase
{
    public IReadOnlyList<LensProfile> Lenses { get; }
    public IReadOnlyList<CameraProfile> Cameras { get; }

    private LensDatabase(List<LensProfile> lenses, List<CameraProfile> cameras) { Lenses = lenses; Cameras = cameras; }

    public static LensDatabase Load(IEnumerable<string> files)
    {
        var lenses = new List<LensProfile>(); var cameras = new List<CameraProfile>();
        foreach (var file in files)
        {
            using var stream = File.OpenRead(file);
            Read(stream, lenses, cameras);
        }
        return new LensDatabase(lenses, cameras);
    }

    public static LensDatabase Parse(string xml)
    {
        var lenses = new List<LensProfile>(); var cameras = new List<CameraProfile>();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        Read(stream, lenses, cameras);
        return new LensDatabase(lenses, cameras);
    }

    /// <summary>The database bundled beside the app, in its <c>lensfun</c> folder.</summary>
    public static LensDatabase Bundled(string? folder = null)
    {
        folder ??= Path.Combine(AppContext.BaseDirectory, "lensfun");
        return Directory.Exists(folder) ? Load(Directory.EnumerateFiles(folder, "*.xml").Order()) : new LensDatabase([], []);
    }

    private static void Read(Stream stream, List<LensProfile> lenses, List<CameraProfile> cameras)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using var reader = XmlReader.Create(stream, settings);
        var root = XDocument.Load(reader).Root;
        if (root is null) return;
        static string Text(XElement e, string name) => e.Elements(name).FirstOrDefault(x => x.Attribute("lang") is null)?.Value.Trim() ?? e.Element(name)?.Value.Trim() ?? "";
        static double Number(XElement? e, string attribute, double fallback = 0) =>
            e?.Attribute(attribute) is { } a && double.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        foreach (var camera in root.Elements("camera"))
            cameras.Add(new CameraProfile(Text(camera, "maker"), Text(camera, "model"), Text(camera, "mount"),
                double.TryParse(Text(camera, "cropfactor"), NumberStyles.Float, CultureInfo.InvariantCulture, out var crop) ? crop : 1));
        foreach (var lens in root.Elements("lens"))
        {
            var calibration = lens.Element("calibration");
            var distortion = new List<LensCalibration>(); var tca = new List<LensCalibration>(); var vignetting = new List<LensCalibration>();
            foreach (var d in calibration?.Elements("distortion") ?? [])
            {
                var model = (string?)d.Attribute("model") ?? "";
                double[]? terms = model switch
                {
                    "poly3" => [Number(d, "k1")],
                    "poly5" => [Number(d, "k1"), Number(d, "k2")],
                    "ptlens" => [Number(d, "a"), Number(d, "b"), Number(d, "c")],
                    _ => null,
                };
                if (terms is not null) distortion.Add(new LensCalibration(Number(d, "focal"), model, terms));
            }
            foreach (var t in calibration?.Elements("tca") ?? [])
            {
                var model = (string?)t.Attribute("model") ?? "";
                double[]? terms = model switch
                {
                    "linear" => [Number(t, "kr", 1), Number(t, "kb", 1)],
                    "poly3" => [Number(t, "vr", 1), Number(t, "cr"), Number(t, "br"), Number(t, "vb", 1), Number(t, "cb"), Number(t, "bb")],
                    _ => null,
                };
                if (terms is not null) tca.Add(new LensCalibration(Number(t, "focal"), model, terms));
            }
            foreach (var v in calibration?.Elements("vignetting") ?? [])
                if ((string?)v.Attribute("model") == "pa")
                    vignetting.Add(new LensCalibration(Number(v, "focal"), "pa", [Number(v, "k1"), Number(v, "k2"), Number(v, "k3")], Number(v, "aperture")));
            lenses.Add(new LensProfile(Text(lens, "maker"), Text(lens, "model"), [.. lens.Elements("mount").Select(m => m.Value.Trim())],
                double.TryParse(Text(lens, "cropfactor"), NumberStyles.Float, CultureInfo.InvariantCulture, out var crop) ? crop : 1,
                distortion, tca, vignetting));
        }
    }

    /// <summary>The lens the EXIF names, matched by its words; null when nothing matches well enough.</summary>
    public LensProfile? Match(LensExif exif)
    {
        if (exif.Lens.Length == 0) return null;
        var wanted = Words(exif.Lens);
        var mounts = Cameras.Where(c => Same(c.Maker, exif.CameraMaker) && Same(c.Model, exif.CameraModel)).Select(c => c.Mount).ToHashSet(StringComparer.OrdinalIgnoreCase);
        LensProfile? best = null; var bestScore = 0.0;
        foreach (var lens in Lenses)
        {
            var words = Words(lens.Name);
            if (words.Count == 0) continue;
            // How much of what the photo says the lens name also says, a little less for every word it adds.
            var common = wanted.Intersect(words).Count();
            var score = common / (double)wanted.Count - .02 * words.Except(wanted).Count();
            if (mounts.Count > 0 && lens.Mounts.Any(mounts.Contains)) score += .05;
            // The focal range in the name has to hold the photo's focal length.
            if (exif.FocalLength > 0 && FocalRange(lens.Model) is { } range && (exif.FocalLength < range.Min - .5 || exif.FocalLength > range.Max + .5)) score -= .5;
            if (score > bestScore) { bestScore = score; best = lens; }
        }
        return bestScore >= .6 ? best : null;
    }

    private static bool Same(string a, string b) => string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
    private static string Normalize(string text) => new string(text.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    /// <summary>Letters and numbers apart, so "EF24-105mm f/4L" and "EF 24-105mm f/4 L" say the same.</summary>
    private static HashSet<string> Words(string text) =>
        [.. System.Text.RegularExpressions.Regex.Matches(text.ToLowerInvariant(), @"[a-z]+|\d+(?:\.\d+)?").Select(m => m.Value).Where(w => w != "lens")];
    private static (double Min, double Max)? FocalRange(string model)
    {
        var match = System.Text.RegularExpressions.Regex.Match(model, @"(\d+(?:\.\d+)?)(?:\s*-\s*(\d+(?:\.\d+)?))?\s*mm", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        var min = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var max = match.Groups[2].Success ? double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : min;
        return (min, max);
    }
}

/// <summary>A lens profile's corrections applied to a photo's pixels.</summary>
public static class LensCorrection
{
    /// <summary>The EXIF that matching needs, read from a file; empty fields where the file has none.</summary>
    public static LensExif ReadExif(string path)
    {
        try
        {
            using var image = new ImageMagick.MagickImage();
            image.Ping(path);
            var exif = image.GetExifProfile();
            string Text(ImageMagick.ExifTag<string> tag) => exif?.GetValue(tag)?.Value?.Trim() ?? "";
            double Rational(ImageMagick.ExifTag<ImageMagick.Rational> tag) => exif?.GetValue(tag)?.Value is { } r && r.Denominator != 0 ? r.ToDouble() : 0;
            return new LensExif(Text(ImageMagick.ExifTag.Make), Text(ImageMagick.ExifTag.Model), Text(ImageMagick.ExifTag.LensModel),
                Rational(ImageMagick.ExifTag.FocalLength), Rational(ImageMagick.ExifTag.FNumber));
        }
        catch (ImageMagick.MagickException) { return new LensExif("", "", "", 0, 0); }
    }

    /// <summary>A calibration's terms at <paramref name="focal"/>: between the two measured focal lengths around it,
    /// or the nearest when it lies outside them. Null when the lens has none of this kind.</summary>
    public static LensCalibration? At(IReadOnlyList<LensCalibration> calibrations, double focal, double aperture = 0)
    {
        if (calibrations.Count == 0) return null;
        var candidates = calibrations;
        if (aperture > 0 && calibrations.Any(c => c.Aperture > 0))
        {
            var nearest = calibrations.Where(c => c.Aperture > 0).MinBy(c => Math.Abs(Math.Log(c.Aperture / aperture)))!.Aperture;
            candidates = [.. calibrations.Where(c => c.Aperture == nearest)];
        }
        var model = candidates.GroupBy(c => c.Model).MaxBy(g => g.Count())!.Key;
        var ordered = candidates.Where(c => c.Model == model).OrderBy(c => c.Focal).ToArray();
        if (focal <= 0 || focal <= ordered[0].Focal) return ordered[0];
        if (focal >= ordered[^1].Focal) return ordered[^1];
        var upper = Array.FindIndex(ordered, c => c.Focal >= focal);
        var (a, b) = (ordered[upper - 1], ordered[upper]);
        var t = (focal - a.Focal) / Math.Max(1e-9, b.Focal - a.Focal);
        return new LensCalibration(focal, model, [.. a.Terms.Zip(b.Terms, (x, y) => x + (y - x) * t)], a.Aperture);
    }

    /// <summary>
    /// The photo corrected, as RgbaF32: each output pixel reads the photo where the lens put it (per channel for
    /// chromatic aberration), divided by the vignetting there. Distances follow lensfun: distortion and aberration
    /// in units of half the shorter side, vignetting in units of half the diagonal.
    /// </summary>
    public static SKBitmap Apply(SKBitmap source, LensProfile lens, double focal, double aperture, LensCorrectionOptions options)
    {
        int w = source.Width, h = source.Height;
        var rgba = LightPlanes.Rgba(source, w, h);
        var distortion = options.Distortion ? At(lens.Distortion, focal) : null;
        var tca = options.ChromaticAberration ? At(lens.Tca, focal) : null;
        var vignetting = options.Vignette ? At(lens.Vignetting, focal, aperture) : null;
        double cx = w / 2.0, cy = h / 2.0, unit = Math.Min(w, h) / 2.0, diagonal = Math.Sqrt(w * w + h * h) / 2.0;
        double Distorted(double r) => distortion is null ? r : distortion.Model switch
        {
            "poly3" => r * (1 - distortion.Terms[0] + distortion.Terms[0] * r * r),
            "poly5" => r * (1 + distortion.Terms[0] * r * r + distortion.Terms[1] * r * r * r * r),
            _ => r * (distortion.Terms[0] * r * r * r + distortion.Terms[1] * r * r + distortion.Terms[2] * r + 1 - distortion.Terms[0] - distortion.Terms[1] - distortion.Terms[2]),
        };
        double Channel(double r, int c) => tca is null || c == 1 ? r : tca.Model == "linear" ? r * tca.Terms[c == 0 ? 0 : 1]
            : r * (tca.Terms[c == 0 ? 0 : 3] + tca.Terms[c == 0 ? 1 : 4] * r + tca.Terms[c == 0 ? 2 : 5] * r * r);
        // Auto Scale: grown until no corner or edge middle reads past the photo.
        var scale = 1.0;
        if (options.AutoScale && distortion is not null)
        {
            var reach = 0.0;
            foreach (var (px, py) in new[] { (0.0, 0.0), (cx, 0.0), (0.0, cy) })
            {
                var r = Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy)) / unit;
                if (r <= 0) continue;
                reach = Math.Max(reach, Distorted(r) / r);
            }
            if (reach > 1) scale = 1 / reach;
        }
        var result = Bitmaps.Allocate(DeepPixels.Info(w, h));
        var output = new float[w * h * 4];
        var fill = options.Edge switch { LensEdge.Black => 0f, LensEdge.White => 1f, _ => 0f };
        Parallel.For(0, h, y =>
        {
            for (var x = 0; x < w; x++)
            {
                var dx = (x + .5 - cx) * scale; var dy = (y + .5 - cy) * scale;
                var r = Math.Sqrt(dx * dx + dy * dy) / unit;
                var o = (y * w + x) * 4;
                var inside = true;
                for (var c = 0; c < 4; c++)
                {
                    var rc = c == 3 ? Distorted(r) : Channel(Distorted(r), c);
                    var factor = r > 1e-9 ? rc / r : 1;
                    var sx = cx + dx * factor; var sy = cy + dy * factor;
                    if (sx < 0 || sy < 0 || sx > w || sy > h)
                    {
                        if (options.Edge != LensEdge.EdgeExtension) { inside = false; break; }
                        sx = Math.Clamp(sx, 0, w); sy = Math.Clamp(sy, 0, h);
                    }
                    output[o + c] = Bilinear(rgba, w, h, sx, sy, c);
                }
                if (!inside)
                {
                    for (var c = 0; c < 3; c++) output[o + c] = fill;
                    output[o + 3] = options.Edge == LensEdge.Transparency ? 0 : 1;
                    continue;
                }
                if (vignetting is not null)
                {
                    var v = Math.Sqrt((x + .5 - cx) * (x + .5 - cx) + (y + .5 - cy) * (y + .5 - cy)) / diagonal;
                    var v2 = v * v;
                    var falloff = 1 + vignetting.Terms[0] * v2 + vignetting.Terms[1] * v2 * v2 + vignetting.Terms[2] * v2 * v2 * v2;
                    if (falloff > .05)
                        for (var c = 0; c < 3; c++) output[o + c] = LightPlanes.Gamma((float)(LightPlanes.Linear(output[o + c]) / falloff));
                }
            }
        });
        output.CopyTo(MemoryMarshal.Cast<byte, float>(result.GetPixelSpan()));
        return result;
    }

    /// <summary>
    /// File › Automate › Lens Correction: each source corrected with its best matching profile (or the chosen one) and
    /// written to <paramref name="folder"/> as PSD, TIFF, JPEG or PNG under its own name. Sources no profile matches are
    /// listed and not written.
    /// </summary>
    public static (IReadOnlyList<string> Written, IReadOnlyList<string> Unmatched) Batch(IReadOnlyList<IO.AutomationSource> sources, string folder,
        IO.ExportFormat? format, LensDatabase database, LensProfile? chosen, LensCorrectionOptions options, CancellationToken cancellation = default)
    {
        Directory.CreateDirectory(folder);
        var written = new List<string>(); var unmatched = new List<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            cancellation.ThrowIfCancellationRequested();
            var exif = source.Path is { } path ? ReadExif(path) : new LensExif("", "", "", 0, 0);
            var lens = chosen ?? database.Match(exif);
            if (lens is null) { unmatched.Add(source.Name); continue; }
            using var image = IO.FileAutomation.RenderSource(source, true);
            using var corrected = Apply(image, lens, exif.FocalLength, exif.Aperture, options);
            var deep = image.ColorType == SKColorType.RgbaF32;
            using var document = new CanvasDocument(Guid.NewGuid(), corrected.Width, corrected.Height) { Image = new Format.ImageSettings { Depth = deep ? 16 : 8 } };
            var stem = IO.WebPage.FileName(Path.GetFileNameWithoutExtension(source.Name), IO.WebFileNames.Windows);
            var asset = deep ? ImportedImage.CreateDeep(corrected.Copy(), stem, 16) : ImportedImage.Create(DeepPixels.Preview(corrected), stem);
            document.Layers.Add(new ImageLayer(Guid.NewGuid(), asset, new Model.LayerTransform(0, 0, corrected.Width, corrected.Height), stem));
            var extension = format switch { IO.ExportFormat.Tiff => ".tif", IO.ExportFormat.Jpeg => ".jpg", IO.ExportFormat.Png => ".png", _ => ".psd" };
            var name = stem; for (var n = 2; !names.Add(name); n++) name = $"{stem} {n}";
            var target = Path.Combine(folder, name + extension);
            if (format is { } export)
            {
                using var result = IO.ExportImage.Encode(document, new IO.ExportOptions(export, document.Width, document.Height, 92,
                    Transparency: export != IO.ExportFormat.Jpeg, Background: SKColors.White));
                IO.AtomicFile.WriteBytes(target, result.Bytes);
            }
            else IO.PSD.PsdWriter.Write(document, target, cancellation);
            written.Add(target);
        }
        return (written, unmatched);
    }

    private static float Bilinear(float[] rgba, int w, int h, double x, double y, int c)
    {
        x = Math.Clamp(x - .5, 0, w - 1); y = Math.Clamp(y - .5, 0, h - 1);
        var ix = (int)x; var iy = (int)y; var fx = (float)(x - ix); var fy = (float)(y - iy);
        var ix1 = Math.Min(w - 1, ix + 1); var iy1 = Math.Min(h - 1, iy + 1);
        float P(int px, int py) => rgba[(py * w + px) * 4 + c];
        return (P(ix, iy) * (1 - fx) + P(ix1, iy) * fx) * (1 - fy) + (P(ix, iy1) * (1 - fx) + P(ix1, iy1) * fx) * fy;
    }
}
