using Compositor.Core.Model;
using PDFtoImage;
using SkiaSharp;
namespace Compositor.Core.IO;
public sealed record PdfPageSize(double Width, double Height);
/// <summary>Page-at-a-time PDFium import. No Ghostscript installation or whole-file byte array is needed.</summary>
// PDFtoImage 5.4 uses capitalized OS names in its platform metadata. The runtime guards below
// deliberately restrict each native entry point to the three supported desktop platforms.
#pragma warning disable CA1416
public static class PdfImporter
{
    public static IReadOnlyList<PdfPageSize> Pages(string path, string? password = null)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        using var stream = Open(path);
        var count = Conversion.GetPageCount(stream, leaveOpen: true, password: password);
        if (count is < 1 or > 10000) throw new ImportException(ImportError.TooLarge);
        stream.Position = 0;
        return Conversion.GetPageSizes(stream, leaveOpen: true, password: password).Select(s => new PdfPageSize(s.Width, s.Height)).ToArray();
    }
    public static ImportedImage Page(string path, int page, int dpi = 72, string? password = null, CancellationToken cancellation = default)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        if (dpi is < 1 or > 2400) throw new ArgumentOutOfRangeException(nameof(dpi));
        var sizes = Pages(path, password); if (page < 0 || page >= sizes.Count) throw new ArgumentOutOfRangeException(nameof(page));
        var width = Math.Ceiling(sizes[page].Width * dpi / 72); var height = Math.Ceiling(sizes[page].Height * dpi / 72);
        if (!double.IsFinite(width + height) || width < 1 || height < 1 || width > DocumentLimits.MaxSide || height > DocumentLimits.MaxSide || width * height > DocumentLimits.MaxSurfacePixels) throw new ImportException(ImportError.TooLarge);
        using var reserve = MemoryBudget.Current.Reserve(checked((long)(width * height) * 12)); cancellation.ThrowIfCancellationRequested();
        using var stream = Open(path); using var rendered = Conversion.ToImage(stream, page, leaveOpen: true, password: password, options: new RenderOptions(Dpi: dpi, Width: (int)width, Height: (int)height));
        cancellation.ThrowIfCancellationRequested();
        var pixels = Bitmaps.Allocate(Bitmaps.ColorInfo((int)width, (int)height));
        try { using (var canvas = new SKCanvas(pixels)) canvas.DrawBitmap(rendered, new SKPoint(), new SKSamplingOptions(SKFilterMode.Nearest)); return ImportedImage.Create(pixels, Path.GetFileNameWithoutExtension(path) + " - " + (page + 1)); }
        catch { pixels.Dispose(); throw; }
    }
    private static FileStream Open(string path)
    {
        var file = new FileInfo(path); if (file.Length > uint.MaxValue) throw new ImportException(ImportError.TooLarge);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.RandomAccess);
    }
    public static int[] ParsePages(string value, int count)
    {
        if (value.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)) return Enumerable.Range(0, count).ToArray();
        var result = new SortedSet<int>();
        foreach (var token in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var ends = token.Split('-', StringSplitOptions.TrimEntries);
            if (ends.Length is < 1 or > 2 || !int.TryParse(ends[0], out var first)) throw new FormatException("Use page numbers or ranges, such as 1-3,5.");
            var last = first; if (ends.Length == 2 && !int.TryParse(ends[1], out last)) throw new FormatException("Invalid page range.");
            if (first < 1 || last < first || last > count) throw new FormatException("Page number is out of range.");
            for (var page = first; page <= last; page++) result.Add(page - 1);
        }
        if (result.Count == 0) throw new FormatException("Choose at least one page."); return result.ToArray();
    }
}

#pragma warning restore CA1416
