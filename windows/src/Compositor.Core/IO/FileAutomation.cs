using Compositor.Core.Model;
using Compositor.Core.Document;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Core.IO;

public sealed record AutomationSource(string Name, string? Path = null, CanvasDocument? Document = null);
public enum StackStatistic { Mean, Sum, Minimum, Maximum, Median, Variance, StandardDeviation, Skewness, Kurtosis, Range, Entropy }
public sealed record ContactSheetOptions(int Width = 2400, int Height = 3000, double Resolution = 300,
    int Columns = 5, int Rows = 6, bool DownFirst = false, int HorizontalSpacing = 10, int VerticalSpacing = 10,
    bool RotateToFit = false, bool Captions = true, string FontFamily = "Arial", double FontSize = 12, bool Flatten = true);
public sealed record PdfPresentationOptions(SKColor Background, double FontSize = 12, bool FileNames = false);

/// <summary>Shared, UI-independent file workflows. Callers retain open documents for the duration of a job.</summary>
public static class FileAutomation
{
    public static IReadOnlyList<string> EnumerateImages(string directory, bool recursive)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System, ReturnSpecialDirectories = false };
        return Directory.EnumerateFiles(directory, "*", options)
            .Where(p => ImageImporter.Extensions.Contains(System.IO.Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static SKBitmap RenderSource(AutomationSource source)
    {
        if (source.Document is { } document) return DocumentRenderer.Render(document);
        if (string.IsNullOrWhiteSpace(source.Path)) throw new ArgumentException("Select a source file.");
        using var opened = ImageImporter.NewDocument(ImageImporter.Decode(source.Path));
        return DocumentRenderer.Render(opened);
    }

    public static (int Width, int Height) Fit(int width, int height, int maximumWidth, int maximumHeight, bool noEnlarge)
    {
        CheckSize(width, height); CheckSize(maximumWidth, maximumHeight);
        var scale = Math.Min((double)maximumWidth / width, (double)maximumHeight / height);
        if (noEnlarge) scale = Math.Min(1, scale);
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    public static CanvasDocument LoadLayers(IReadOnlyList<AutomationSource> sources, CancellationToken cancellationToken = default)
        => LoadLayers(sources, false, false, cancellationToken);

    public static CanvasDocument LoadLayers(IReadOnlyList<AutomationSource> sources, bool autoAlign, bool createSmartObject,
        CancellationToken cancellationToken = default)
    {
        RequireSources(sources, 1);
        var result = new CanvasDocument(Guid.NewGuid(), 1, 1);
        long pixels = 0;
        try
        {
            foreach (var source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bitmap = RenderSource(source);
                pixels += (long)bitmap.Width * bitmap.Height;
                if (pixels > DocumentLimits.DocumentPixelBudget) { bitmap.Dispose(); throw new InvalidOperationException("The source images exceed the document memory budget."); }
                result.Width = Math.Max(result.Width, bitmap.Width); result.Height = Math.Max(result.Height, bitmap.Height);
                var asset = ImportedImage.Create(bitmap, source.Name);
                result.Layers.Add(new ImageLayer(Guid.NewGuid(), asset, new LayerTransform(0, 0, asset.Width, asset.Height), source.Name));
            }
            CheckSize(result.Width, result.Height);
            foreach (var layer in result.Layers) layer.Transform = layer.Transform with
                { X = (result.Width - layer.Transform.Width) / 2, Y = (result.Height - layer.Transform.Height) / 2 };
            if (autoAlign && result.Layers.Count > 1) AlignLayers(result, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (createSmartObject && LayerWorkflow.ConvertToSmartObject(result, result.Layers.Select(l => l.ID).ToArray()) is null)
                throw new InvalidOperationException("Could not create a smart object from the loaded layers.");
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    private static void AlignLayers(CanvasDocument document, CancellationToken cancellationToken)
    {
        SKBitmap Padded(ImageLayer layer)
        {
            var image = new SKBitmap(document.Width, document.Height);
            using var canvas = new SKCanvas(image); canvas.Clear(SKColors.Transparent);
            // Pixel-aligned origins also make the final union deterministic for odd-sized inputs.
            layer.Transform = layer.Transform with { X = Math.Floor(layer.Transform.X), Y = Math.Floor(layer.Transform.Y) };
            canvas.DrawBitmap(layer.Asset!.Image, (float)layer.Transform.X, (float)layer.Transform.Y, new SKSamplingOptions(SKFilterMode.Nearest));
            return image;
        }
        using var reference = Padded(document.Layers[0]);
        foreach (var layer in document.Layers.Skip(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var moving = Padded(layer);
            var delta = AdvancedEdit.AlignTranslation(reference, moving, Math.Max(document.Width, document.Height) / 4, cancellationToken);
            layer.Transform = layer.Transform with { X = layer.Transform.X + Math.Round(delta.X), Y = layer.Transform.Y + Math.Round(delta.Y) };
        }
        var left = document.Layers.Min(l => l.Transform.X); var top = document.Layers.Min(l => l.Transform.Y);
        var right = document.Layers.Max(l => l.Transform.X + l.Transform.Width); var bottom = document.Layers.Max(l => l.Transform.Y + l.Transform.Height);
        var width = checked((int)Math.Ceiling(right - left)); var height = checked((int)Math.Ceiling(bottom - top));
        CheckSize(width, height); document.Width = width; document.Height = height;
        foreach (var layer in document.Layers) layer.Transform = layer.Transform with { X = layer.Transform.X - left, Y = layer.Transform.Y - top };
    }

    public static IReadOnlyList<CanvasDocument> ContactSheets(IReadOnlyList<AutomationSource> sources, ContactSheetOptions options,
        CancellationToken cancellationToken = default)
    {
        RequireSources(sources, 1); CheckSize(options.Width, options.Height);
        if (options.Columns is < 1 or > 100 || options.Rows is < 1 or > 100 || options.HorizontalSpacing < 0 || options.VerticalSpacing < 0
            || !double.IsFinite(options.Resolution) || options.Resolution is < 1 or > 9600 || !double.IsFinite(options.FontSize) || options.FontSize is < 1 or > 300)
            throw new ArgumentException("Invalid contact sheet settings.");
        int capacity = options.Columns * options.Rows, pages = (sources.Count + capacity - 1) / capacity;
        if ((long)pages * options.Width * options.Height * (options.Flatten ? 1 : 2) > DocumentLimits.DocumentPixelBudget)
            throw new InvalidOperationException("The contact sheets exceed the document memory budget.");
        float cellWidth = (options.Width - (options.Columns + 1f) * options.HorizontalSpacing) / options.Columns;
        float cellHeight = (options.Height - (options.Rows + 1f) * options.VerticalSpacing) / options.Rows;
        float textSize = (float)(options.FontSize * options.Resolution / 72), caption = options.Captions ? textSize * 1.5f : 0;
        if (cellWidth < 1 || cellHeight - caption < 1) throw new ArgumentException("The page is too small for the rows, columns and captions.");
        var results = new List<CanvasDocument>();
        using var typeface = SKTypeface.FromFamilyName(options.FontFamily);
        using var font = new SKFont(typeface, textSize);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        try
        {
            for (int page = 0; page < pages; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var document = new CanvasDocument(Guid.NewGuid(), options.Width, options.Height, options.Resolution);
                results.Add(document);
                using var background = new SKBitmap(options.Width, options.Height); background.Erase(SKColors.White);
                using var pageCanvas = new SKCanvas(background);
                for (int slot = 0; slot < capacity && page * capacity + slot < sources.Count; slot++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var source = sources[page * capacity + slot];
                    using var image = RenderSource(source);
                    int column = options.DownFirst ? slot / options.Rows : slot % options.Columns;
                    int row = options.DownFirst ? slot % options.Rows : slot / options.Columns;
                    float x = options.HorizontalSpacing + column * (cellWidth + options.HorizontalSpacing);
                    float y = options.VerticalSpacing + row * (cellHeight + options.VerticalSpacing);
                    bool rotate = options.RotateToFit && Math.Min(cellWidth / image.Height, (cellHeight - caption) / image.Width)
                        > Math.Min(cellWidth / image.Width, (cellHeight - caption) / image.Height);
                    using var tile = new SKBitmap(Math.Max(1, (int)cellWidth), Math.Max(1, (int)cellHeight)); tile.Erase(SKColors.Transparent);
                    using (var canvas = new SKCanvas(tile))
                    {
                        float scale = Math.Min(cellWidth / (rotate ? image.Height : image.Width), (cellHeight - caption) / (rotate ? image.Width : image.Height));
                        canvas.Save(); canvas.Translate(cellWidth / 2, (cellHeight - caption) / 2);
                        if (rotate) canvas.RotateDegrees(90);
                        canvas.Scale(scale); canvas.DrawBitmap(image, -image.Width / 2f, -image.Height / 2f, new SKSamplingOptions(SKFilterMode.Linear)); canvas.Restore();
                        if (options.Captions)
                        {
                            var name = System.IO.Path.GetFileName(source.Name);
                            while (name.Length > 1 && font.MeasureText(name + "…") > cellWidth) name = name[..^1];
                            if (name != System.IO.Path.GetFileName(source.Name)) name += "…";
                            canvas.DrawText(name, cellWidth / 2, cellHeight - textSize * .25f, SKTextAlign.Center, font, paint);
                        }
                    }
                    if (options.Flatten) pageCanvas.DrawBitmap(tile, x, y, new SKSamplingOptions(SKFilterMode.Linear));
                    else document.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(tile.Copy(), source.Name),
                        new LayerTransform(x, y, tile.Width, tile.Height), source.Name));
                }
                document.Layers.Insert(0, new ImageLayer(Guid.NewGuid(), ImportedImage.Create(background.Copy(), "Background"),
                    new LayerTransform(0, 0, options.Width, options.Height), "Background"));
            }
            return results;
        }
        catch { foreach (var document in results) document.Dispose(); throw; }
    }

    public static CanvasDocument Statistics(IReadOnlyList<AutomationSource> sources, StackStatistic statistic, CancellationToken cancellationToken = default)
        => Statistics(sources, statistic, false, cancellationToken);

    public static CanvasDocument Statistics(IReadOnlyList<AutomationSource> sources, StackStatistic statistic, bool autoAlign,
        CancellationToken cancellationToken = default)
    {
        RequireSources(sources, 2);
        if (!Enum.IsDefined(statistic)) throw new ArgumentOutOfRangeException(nameof(statistic));
        var images = new List<SKBitmap>();
        try
        {
            long pixels = 0;
            foreach (var source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var image = RenderSource(source); images.Add(image);
                pixels += (long)image.Width * image.Height;
                if (pixels > DocumentLimits.DocumentPixelBudget) throw new InvalidOperationException("The image stack exceeds the document memory budget.");
                if (image.Width != images[0].Width || image.Height != images[0].Height) throw new ArgumentException("Image statistics requires equally sized source images.");
            }
            var offsets = new SKPointI[images.Count];
            int left = 0, top = 0, right = images[0].Width, bottom = images[0].Height;
            if (autoAlign)
            {
                for (int i = 1; i < images.Count; i++)
                {
                    var delta = AdvancedEdit.AlignTranslation(images[0], images[i], Math.Max(images[0].Width, images[0].Height) / 4, cancellationToken);
                    offsets[i] = new SKPointI((int)Math.Round(delta.X), (int)Math.Round(delta.Y));
                    left = Math.Max(left, offsets[i].X); top = Math.Max(top, offsets[i].Y);
                    right = Math.Min(right, images[i].Width + offsets[i].X);
                    bottom = Math.Min(bottom, images[i].Height + offsets[i].Y);
                }
            }
            if (right <= left || bottom <= top) throw new InvalidOperationException("The aligned images have no common area.");
            using var output = new SKBitmap(right - left, bottom - top);
            var red = new double[images.Count];
            var green = new double[images.Count];
            var blue = new double[images.Count];
            for (int y = 0; y < output.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < output.Width; x++)
                {
                    byte alpha = 0;
                    for (int i = 0; i < images.Count; i++)
                    {
                        var color = images[i].GetPixel(x + left - offsets[i].X, y + top - offsets[i].Y);
                        red[i] = color.Red; green[i] = color.Green; blue[i] = color.Blue;
                        alpha = Math.Max(alpha, color.Alpha);
                    }
                    output.SetPixel(x, y, new SKColor(Channel(red, statistic), Channel(green, statistic), Channel(blue, statistic), alpha));
                }
            }
            return ImageImporter.NewDocument(ImportedImage.Create(output.Copy(), statistic.ToString()));
        }
        finally { foreach (var image in images) image.Dispose(); }
    }

    public static double Statistic(double[] values, StackStatistic statistic)
    {
        return StatisticInPlace((double[])values.Clone(), statistic);
    }

    private static byte Channel(double[] values, StackStatistic statistic) => (byte)Math.Clamp(Math.Round(StatisticInPlace(values, statistic)), 0, 255);

    private static double StatisticInPlace(double[] values, StackStatistic statistic)
    {
        if (values.Length == 0) throw new ArgumentException("An image stack must not be empty.");
        double sum = 0, min = double.PositiveInfinity, max = double.NegativeInfinity;
        foreach (double value in values) { sum += value; min = Math.Min(min, value); max = Math.Max(max, value); }
        double mean = sum / values.Length;
        if (statistic == StackStatistic.Mean) return mean;
        if (statistic == StackStatistic.Sum) return sum;
        if (statistic == StackStatistic.Minimum) return min;
        if (statistic == StackStatistic.Maximum) return max;
        if (statistic == StackStatistic.Range) return max - min;
        if (statistic is StackStatistic.Median or StackStatistic.Entropy)
        {
            Array.Sort(values);
            if (statistic == StackStatistic.Median) return (values[(values.Length - 1) / 2] + values[values.Length / 2]) / 2;
            double entropy = 0;
            for (int start = 0; start < values.Length;)
            {
                int end = start + 1;
                while (end < values.Length && values[end] == values[start]) end++;
                double probability = (double)(end - start) / values.Length;
                entropy -= probability * Math.Log2(probability); start = end;
            }
            return entropy;
        }
        double m2 = 0, m3 = 0, m4 = 0;
        foreach (double value in values)
        {
            double delta = value - mean, square = delta * delta;
            m2 += square; m3 += square * delta; m4 += square * square;
        }
        double variance = m2 / values.Length;
        return statistic switch
        {
            StackStatistic.Variance => variance, StackStatistic.StandardDeviation => Math.Sqrt(variance),
            StackStatistic.Skewness => variance == 0 ? 0 : (m3 / values.Length) / Math.Pow(variance, 1.5),
            StackStatistic.Kurtosis => variance == 0 ? 0 : (m4 / values.Length) / (variance * variance),
            _ => throw new ArgumentOutOfRangeException(nameof(statistic))
        };
    }

    public static void PdfPresentation(string path, IReadOnlyList<AutomationSource> sources, PdfPresentationOptions options, CancellationToken cancellationToken = default)
    {
        RequireSources(sources, 1);
        if (!double.IsFinite(options.FontSize) || options.FontSize is < 1 or > 300) throw new ArgumentException("Invalid caption size.");
        AtomicFile.Write(path, stream =>
        {
            using var pdf = SKDocument.CreatePdf(stream);
            using var font = new SKFont(SKTypeface.Default, (float)options.FontSize);
            using var paint = new SKPaint { Color = options.Background == SKColors.Black ? SKColors.White : SKColors.Black, IsAntialias = true };
            foreach (var source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var image = RenderSource(source);
                float scale = Math.Min(1, 14400f / Math.Max(image.Width, image.Height));
                float width = image.Width * scale, height = image.Height * scale, caption = options.FileNames ? (float)options.FontSize * 2 : 0;
                var canvas = pdf.BeginPage(width, height + caption); canvas.Clear(options.Background);
                canvas.DrawBitmap(image, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear));
                if (options.FileNames) canvas.DrawText(source.Name, width / 2, height + (float)options.FontSize, SKTextAlign.Center, font, paint);
                pdf.EndPage();
            }
            pdf.Close();
        }, cancellationToken);
    }

    private static void RequireSources(IReadOnlyList<AutomationSource> sources, int minimum)
    { if (sources.Count < minimum || sources.Count > 10000) throw new ArgumentException($"Select between {minimum} and 10000 source images."); }
    private static void CheckSize(int width, int height)
    {
        if (width < 1 || height < 1 || width > DocumentLimits.MaxSide || height > DocumentLimits.MaxSide
            || (long)width * height > Math.Min(DocumentLimits.MaxSurfacePixels, DocumentLimits.DocumentPixelBudget))
            throw new ArgumentException("The image dimensions exceed the supported limit.");
    }
}
