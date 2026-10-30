using Compositor.Core.Model;
using Compositor.Core.Rendering;
using ImageMagick;
using SkiaSharp;

namespace Compositor.Core.IO;

public enum WebFormat { Gif, Jpeg, Png8, Png24, Wbmp }
public enum WebPalette { Adaptive, Perceptual, WebSafe, BlackWhite, Grayscale, Custom }
public enum WebDither { None, Diffusion, Pattern, Noise }
public enum WebResample { Nearest, Bilinear, Bicubic, BicubicSmoother, BicubicSharper }

public sealed record WebExportOptions(int Width, int Height, WebFormat Format = WebFormat.Gif,
    int Quality = 80, int Colors = 256, WebPalette Palette = WebPalette.Adaptive,
    WebDither Dither = WebDither.Diffusion, int DitherAmount = 100, bool Transparency = true,
    WebDither AlphaDither = WebDither.None, SKColor? Matte = null, bool Interlaced = false,
    int WebSnap = 0, bool ConvertToSrgb = true, bool EmbedProfile = false,
    WebResample Resample = WebResample.Bicubic, uint[]? CustomPalette = null,
    uint[]? LockedColors = null, uint[]? TransparentColors = null,
    MetadataPolicy Metadata = MetadataPolicy.CopyrightAndContact)
{
    public string Extension => Format switch { WebFormat.Jpeg => "jpg", WebFormat.Gif => "gif", WebFormat.Wbmp => "wbmp", _ => "png" };
    public bool Indexed => Format is WebFormat.Gif or WebFormat.Png8 or WebFormat.Wbmp;
    public void Validate()
    {
        if (Width < 1 || Height < 1 || Width > DocumentLimits.MaxSide || Height > DocumentLimits.MaxSide
            || (long)Width * Height > DocumentLimits.MaxSurfacePixels || Quality is < 1 or > 100
            || Colors is < 2 or > 256 || DitherAmount is < 0 or > 100 || WebSnap is < 0 or > 100
            || !Enum.IsDefined(Format) || !Enum.IsDefined(Palette) || !Enum.IsDefined(Dither)
            || !Enum.IsDefined(AlphaDither) || !Enum.IsDefined(Resample) || !Enum.IsDefined(Metadata)
            || CustomPalette is { Length: > 256 }
            || LockedColors is { Length: > 256 } || TransparentColors is { Length: > 256 }
            || (LockedColors?.Select(c => c | 0xff000000u).Distinct().Count() ?? 0) > Colors
            || Palette == WebPalette.Custom && (CustomPalette is null || CustomPalette.Length < 2))
            throw new ArgumentOutOfRangeException(nameof(WebExportOptions));
    }
}

public sealed class WebExportResult(byte[] bytes, SKBitmap preview, WebExportOptions options, uint[] palette) : IDisposable
{
    public byte[] Bytes { get; } = bytes;
    public SKBitmap Preview { get; } = preview;
    public WebExportOptions Options { get; } = options;
    public uint[] Palette { get; } = palette;
    public IReadOnlyDictionary<uint, int> PaletteUsage { get; init; } = new Dictionary<uint, int>();
    public void Dispose() => Preview.Dispose();
}

/// <summary>Encodes the bytes used by both the comparison preview and the saved web image.</summary>
public static class WebExport
{
    /// <summary>The document as the web image encodes it; <paramref name="frame"/> picks one of a frame animation's
    /// frames (the document as it stands when there is none).</summary>
    public static WebExportResult Encode(CanvasDocument document, WebExportOptions options, CancellationToken cancellation = default, int frame = -1)
    {
        options.Validate(); cancellation.ThrowIfCancellationRequested();
        var (bitmap, profile) = Prepare(document, options, frame);
        using (bitmap) return EncodePixels(bitmap, options, profile, document.Metadata, cancellation);
    }

    /// <summary>The frames of the document's frame animation, or none when it has fewer than two.</summary>
    public static IReadOnlyList<Document.TimelineFrame> Frames(CanvasDocument document) =>
        document.Timeline is { Mode: Document.TimelineMode.Frames, Frames.Length: > 1 } timeline ? timeline.Frames : [];

    /// <summary>The rendered, color-converted pixels at the output size, which every slice and frame is cut from.</summary>
    private static (SKBitmap Bitmap, byte[] Profile) Prepare(CanvasDocument document, WebExportOptions options, int frame)
    {
        var frames = Frames(document);
        using var posed = frame >= 0 && frame < frames.Count ? document.Clone() : null;
        if (posed is not null) Document.LayerComps.Apply(posed, frames[frame].State, respectLocks: false);
        using var source = DocumentRenderer.Render(posed ?? document);
        using var resized = source.Resize(Bitmaps.ColorInfo(options.Width, options.Height), Sampling(options.Resample))
            ?? throw new IOException("The image could not be resized.");
        using var rendered = DeepImageCodec.Magick(resized);
        var profile = !options.ConvertToSrgb && document.Image.ColorProfile is { } icc && IccProfiles.Model(icc) == "RGB" ? icc : ColorProfiles.SRGB.ToByteArray();
        using var converted = ColorTransforms.Convert(rendered, ColorProfiles.SRGB.ToByteArray(), profile);
        converted.Depth = 8;
        var bitmap = SKBitmap.Decode(converted.ToByteArray(MagickFormat.Png)) ?? throw new IOException("The image could not be decoded.");
        return (bitmap, profile);
    }

    /// <summary>
    /// Each of <paramref name="regions"/> (in output pixels) encoded on its own with the same settings, as Save for
    /// Web writes a sliced page's images. A region past the image is cut to it; one wholly outside gives null.
    /// </summary>
    public static IReadOnlyList<WebExportResult?> EncodeRegions(CanvasDocument document, WebExportOptions options, IReadOnlyList<SKRectI> regions,
        CancellationToken cancellation = default)
    {
        options.Validate();
        var (bitmap, profile) = Prepare(document, options, -1);
        using var _ = bitmap;
        var results = new List<WebExportResult?>();
        try
        {
            foreach (var region in regions)
            {
                cancellation.ThrowIfCancellationRequested();
                var cut = SKRectI.Intersect(region, SKRectI.Create(bitmap.Width, bitmap.Height));
                if (cut.IsEmpty) { results.Add(null); continue; }
                using var piece = new SKBitmap(bitmap.Info.WithSize(cut.Width, cut.Height));
                if (!bitmap.ExtractSubset(piece, cut)) throw new IOException("A slice could not be cut from the image.");
                using var owned = piece.Copy();
                results.Add(EncodePixels(owned, options with { Width = cut.Width, Height = cut.Height }, profile, document.Metadata, cancellation));
            }
            return results;
        }
        catch
        {
            foreach (var result in results) result?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The document's frame animation as one GIF: each frame indexed with the same settings, held for its own
    /// time, and played <paramref name="loops"/> times (0 for forever). Null when there is no frame animation.
    /// </summary>
    public static byte[]? EncodeAnimation(CanvasDocument document, WebExportOptions options, int loops, CancellationToken cancellation = default)
    {
        var frames = Frames(document);
        if (frames.Count == 0 || options.Format != WebFormat.Gif || loops is < 0 or > 65535) return null;
        using var collection = new MagickImageCollection();
        for (var i = 0; i < frames.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            using var result = Encode(document, options, cancellation, i);
            var image = new MagickImage(result.Bytes, MagickFormat.Gif)
            {
                AnimationDelay = (uint)Math.Clamp(Math.Round(frames[i].Duration * 100), 1, 65535),
                AnimationIterations = (uint)loops,
                GifDisposeMethod = GifDisposeMethod.Background,
            };
            collection.Add(image);
        }
        return collection.ToByteArray(MagickFormat.Gif);
    }

    public static WebExportResult Optimize(CanvasDocument document, WebExportOptions options, long targetBytes, CancellationToken cancellation = default)
    {
        if (targetBytes < 1) throw new ArgumentOutOfRangeException(nameof(targetBytes));
        WebExportResult? best = null;
        try
        {
            if (options.Format == WebFormat.Jpeg)
            {
                // Inspect descending quality: JPEG sizes are not guaranteed to be monotonic.
                for (var quality = options.Quality; quality >= 1; quality--)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var candidate = Encode(document, options with { Quality = quality }, cancellation);
                    if (best is null || candidate.Bytes.Length < best.Bytes.Length) { best?.Dispose(); best = candidate; }
                    else candidate.Dispose();
                    if (best.Bytes.Length <= targetBytes) break;
                }
            }
            else if (options.Indexed && options.Palette is WebPalette.Adaptive or WebPalette.Perceptual)
            {
                var minimumColors = Math.Min(options.Colors, Math.Max(2,
                    (options.LockedColors?.Select(c => c | 0xff000000u).Distinct().Count() ?? 0) + (options.Transparency ? 1 : 0)));
                for (var colors = options.Colors; ; colors = Math.Max(minimumColors, colors / 2))
                {
                    cancellation.ThrowIfCancellationRequested();
                    var candidate = Encode(document, options with { Colors = colors }, cancellation);
                    if (best is null || candidate.Bytes.Length < best.Bytes.Length) { best?.Dispose(); best = candidate; }
                    else candidate.Dispose();
                    if (best.Bytes.Length <= targetBytes || colors == minimumColors) break;
                }
            }
            else best = Encode(document, options, cancellation);
            var result = best; best = null; return result!;
        }
        finally { best?.Dispose(); }
    }

    private static WebExportResult EncodePixels(SKBitmap bitmap, WebExportOptions options, byte[] profile, Format.DocumentMetadata metadata, CancellationToken cancellation)
    {
        using var pixels = bitmap.Copy();
        var matte = options.Matte ?? SKColors.White;
        var transparent = options.Transparency && options.Format is not (WebFormat.Jpeg or WebFormat.Wbmp);
        var colors = pixels.Pixels;
        var alphaCurrent = new float[pixels.Width + 2];
        var alphaNext = new float[alphaCurrent.Length];
        for (var y = 0; y < pixels.Height; y++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < pixels.Width; x++)
            {
                var index = y * pixels.Width + x; var c = colors[index];
                if (!transparent) colors[index] = Composite(c, matte);
                else if (options.Indexed)
                {
                    var threshold = options.AlphaDither switch
                    {
                        WebDither.Pattern => Bayer(x, y),
                        WebDither.Noise => Noise(x, y),
                        _ => (byte)128
                    };
                    var alpha = c.Alpha + (options.AlphaDither == WebDither.Diffusion ? alphaCurrent[x + 1] : 0);
                    var visible = c.Alpha == 255 || c.Alpha > 0 && alpha >= threshold;
                    if (options.AlphaDither == WebDither.Diffusion)
                    {
                        var error = alpha - (visible ? 255 : 0);
                        alphaCurrent[x + 2] += error * 7 / 16;
                        alphaNext[x] += error * 3 / 16;
                        alphaNext[x + 1] += error * 5 / 16;
                        alphaNext[x + 2] += error / 16;
                    }
                    // Without transparency dithering, composite the visible edge against the selected matte.
                    colors[index] = !visible ? SKColors.Transparent : options.AlphaDither == WebDither.None ? Composite(c, matte) : c.WithAlpha(255);
                }
            }
            (alphaCurrent, alphaNext) = (alphaNext, alphaCurrent); Array.Clear(alphaNext);
        }
        pixels.Pixels = colors;
        uint[] palette = [];
        if (options.Indexed)
        {
            var hasAlpha = colors.Any(c => c.Alpha == 0);
            palette = MakePalette(pixels, options, hasAlpha, cancellation);
            Map(colors, pixels.Width, pixels.Height, palette, options.Dither, options.DitherAmount, cancellation);
            if (options.Transparency && options.Format is WebFormat.Gif or WebFormat.Png8
                && options.TransparentColors is { Length: > 0 })
            {
                var transparentMappings = options.TransparentColors.Select(c => c | 0xff000000u).ToHashSet();
                var mapped = palette.Where(c => new SKColor(c).Alpha > 0 && transparentMappings.Contains(c)).ToHashSet();
                if (mapped.Count > 0)
                {
                    for (var i = 0; i < colors.Length; i++)
                    {
                        if ((i & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                        if (mapped.Contains((uint)colors[i])) colors[i] = SKColors.Transparent;
                    }
                    palette = palette.Where(c => !mapped.Contains(c)).Append((uint)SKColors.Transparent).Distinct().ToArray();
                }
            }
            pixels.Pixels = colors;
        }
        cancellation.ThrowIfCancellationRequested();
        using var output = DeepImageCodec.Magick(pixels);
        output.Depth = 8; output.Quality = (uint)options.Quality;
        // This image is constructed from pixels and has no source profiles to strip.
        // Strip also suppresses subsequently attached PNG XMP/ICC chunks.
        output.Settings.SetDefine(MagickFormat.Png, "exclude-chunk", "date,time");
        var xmp = ImageMetadata.Encode(metadata, options.Metadata);
        if (xmp is not null && options.Format != WebFormat.Wbmp)
        {
            output.SetProfile(new XmpProfile(xmp));
        }
        if (options.EmbedProfile && options.Format is WebFormat.Jpeg or WebFormat.Png8 or WebFormat.Png24)
            output.SetProfile(new ColorProfile(profile));
        output.Settings.Interlace = options.Interlaced ? options.Format switch { WebFormat.Gif => Interlace.Gif, WebFormat.Jpeg => Interlace.Jpeg, _ => Interlace.Png } : Interlace.NoInterlace;
        if (options.Format == WebFormat.Png24) output.ColorType = transparent ? ColorType.TrueColorAlpha : ColorType.TrueColor;
        var format = options.Format switch { WebFormat.Gif => MagickFormat.Gif, WebFormat.Jpeg => MagickFormat.Jpeg, WebFormat.Wbmp => MagickFormat.Wbmp, WebFormat.Png8 => MagickFormat.Png8, _ => MagickFormat.Png };
        var bytes = output.ToByteArray(format);
        if (options.Format == WebFormat.Gif) bytes = ImageMetadata.AddToEncoded(bytes, ExportFormat.Gif, metadata, options.Metadata);
        cancellation.ThrowIfCancellationRequested();
        using var decoded = new MagickImage(bytes, format);
        // Preview has the destination profile interpreted, while bytes retain the requested encoding.
        using var display = ColorTransforms.Convert(decoded, profile, ColorProfiles.SRGB.ToByteArray());
        var preview = SKBitmap.Decode(display.ToByteArray(MagickFormat.Png)) ?? throw new IOException("The encoded preview could not be displayed.");
        var usage = new Dictionary<uint, int>();
        if (options.Indexed)
            for (var i = 0; i < colors.Length; i++)
            {
                if ((i & 4095) == 0 && cancellation.IsCancellationRequested) { preview.Dispose(); cancellation.ThrowIfCancellationRequested(); }
                var color = (uint)colors[i]; usage[color] = usage.GetValueOrDefault(color) + 1;
            }
        return new WebExportResult(bytes, preview, options, palette) { PaletteUsage = usage };
    }

    private static uint[] MakePalette(SKBitmap pixels, WebExportOptions options, bool alpha, CancellationToken cancellation)
    {
        var count = Math.Max(1, options.Colors - (alpha ? 1 : 0));
        IEnumerable<SKColor> palette;
        if (options.Format == WebFormat.Wbmp || options.Palette == WebPalette.BlackWhite) palette = new[] { SKColors.Black, SKColors.White }.Take(count);
        else if (options.Palette == WebPalette.Custom) palette = options.CustomPalette!.Select(c => new SKColor(c).WithAlpha(255)).Take(count);
        else if (options.Palette == WebPalette.WebSafe && count >= 216)
            palette = Enumerable.Range(0, 216).Select(i => new SKColor((byte)(i / 36 * 51), (byte)(i / 6 % 6 * 51), (byte)(i % 6 * 51))).Take(count);
        else if (options.Palette == WebPalette.Grayscale)
            palette = Enumerable.Range(0, count).Select(i => { var v = (byte)(count == 1 ? 128 : i * 255 / (count - 1)); return new SKColor(v, v, v); });
        else
        {
            using var sample = pixels.Resize(Bitmaps.ColorInfo(Math.Min(512, pixels.Width), Math.Min(512, pixels.Height)), new SKSamplingOptions(SKFilterMode.Linear))!;
            using var image = DeepImageCodec.Magick(sample);
            image.Quantize(new QuantizeSettings { Colors = (uint)count, ColorSpace = options.Palette == WebPalette.Perceptual ? ColorSpace.Lab : ColorSpace.sRGB, DitherMethod = DitherMethod.No });
            image.ColorSpace = ColorSpace.sRGB;
            using var reduced = SKBitmap.Decode(image.ToByteArray(MagickFormat.Png))!;
            palette = reduced.Pixels.Where(c => c.Alpha > 0).Select(c => c.WithAlpha(255)).Distinct().Take(count).ToArray();
        }
        cancellation.ThrowIfCancellationRequested();
        var locked = options.Format == WebFormat.Wbmp ? [] :
            (options.LockedColors ?? []).Select(c => c | 0xff000000u).Distinct().ToArray();
        if (locked.Length > count) throw new ArgumentException("Too many locked colors for this palette and its transparency.");
        var result = locked.Concat(palette.Select(c => (uint)Snap(c, options.Palette == WebPalette.WebSafe ? 100 : options.WebSnap)))
            .Distinct().Take(count).ToList();
        if (result.Count == 0) result.Add((uint)SKColors.Black);
        if (alpha) result.Add((uint)SKColors.Transparent);
        return result.ToArray();
    }

    private static void Map(SKColor[] pixels, int width, int height, uint[] palette, WebDither dither, int amount, CancellationToken cancellation)
    {
        var opaque = palette.Select(c => new SKColor(c)).Where(c => c.Alpha != 0).ToArray();
        var current = new float[(width + 2) * 3]; var next = new float[current.Length];
        var strength = amount / 100f;
        var cache = new Dictionary<int, SKColor>();
        for (var y = 0; y < height; y++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x; var c = pixels[i]; if (c.Alpha == 0) continue;
                var e = (x + 1) * 3;
                var offset = dither switch { WebDither.Pattern => (Bayer(x, y) - 128) * .25f * strength, WebDither.Noise => (Noise(x, y) - 128) * .25f * strength, _ => 0 };
                var r = Math.Clamp(c.Red + offset + (dither == WebDither.Diffusion ? current[e] : 0), 0, 255);
                var g = Math.Clamp(c.Green + offset + (dither == WebDither.Diffusion ? current[e + 1] : 0), 0, 255);
                var b = Math.Clamp(c.Blue + offset + (dither == WebDither.Diffusion ? current[e + 2] : 0), 0, 255);
                var key = (int)r << 16 | (int)g << 8 | (int)b;
                if (!cache.TryGetValue(key, out var nearest))
                {
                    var distance = double.MaxValue;
                    foreach (var p in opaque)
                    {
                        var dr = r - p.Red; var dg = g - p.Green; var db = b - p.Blue;
                        var delta = dr * dr * .299 + dg * dg * .587 + db * db * .114;
                        if (delta < distance) { distance = delta; nearest = p; }
                    }
                    if (cache.Count < 262144) cache[key] = nearest;
                }
                pixels[i] = nearest;
                if (dither != WebDither.Diffusion) continue;
                Spread(e, r - nearest.Red); Spread(e + 1, g - nearest.Green); Spread(e + 2, b - nearest.Blue);
            }
            (current, next) = (next, current); Array.Clear(next);
        }
        void Spread(int index, float error)
        {
            error *= strength;
            current[index + 3] += error * 7 / 16;
            next[index - 3] += error * 3 / 16; next[index] += error * 5 / 16; next[index + 3] += error / 16;
        }
    }

    private static SKColor Composite(SKColor c, SKColor matte)
    {
        var a = c.Alpha / 255f;
        return new SKColor((byte)Math.Round(c.Red * a + matte.Red * (1 - a)), (byte)Math.Round(c.Green * a + matte.Green * (1 - a)), (byte)Math.Round(c.Blue * a + matte.Blue * (1 - a)));
    }
    private static SKColor Snap(SKColor c, int amount)
    {
        var snapped = new SKColor((byte)(Math.Round(c.Red / 51.0) * 51), (byte)(Math.Round(c.Green / 51.0) * 51), (byte)(Math.Round(c.Blue / 51.0) * 51));
        return Math.Max(Math.Abs(c.Red - snapped.Red), Math.Max(Math.Abs(c.Green - snapped.Green), Math.Abs(c.Blue - snapped.Blue))) <= amount * .255 ? snapped : c;
    }
    private static readonly byte[] Thresholds = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];
    private static byte Bayer(int x, int y) => (byte)(Thresholds[(y & 3) * 4 + (x & 3)] * 16 + 8);
    private static byte Noise(int x, int y) { var n = unchecked((uint)(x * 374761393 + y * 668265263)); n = (n ^ (n >> 13)) * 1274126177; return (byte)(n ^ (n >> 16)); }
    private static SKSamplingOptions Sampling(WebResample value) => value switch
    {
        WebResample.Nearest => new(SKFilterMode.Nearest), WebResample.Bilinear => new(SKFilterMode.Linear),
        WebResample.BicubicSmoother => new(new SKCubicResampler(1, 0)),
        WebResample.BicubicSharper => new(SKCubicResampler.CatmullRom), _ => new(SKCubicResampler.Mitchell)
    };
}
