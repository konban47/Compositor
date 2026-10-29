using Compositor.Core.Model;
using Compositor.Core.Document;
using Compositor.Core.Rendering;
using Compositor.Core.Format;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.IO;

public sealed record AutomationSource(string Name, string? Path = null, CanvasDocument? Document = null);
public enum StackStatistic { Mean, Sum, Minimum, Maximum, Median, Variance, StandardDeviation, Skewness, Kurtosis, Range, Entropy }
public enum ContactSheetUnit { Inches, Centimeters, Pixels }
public sealed record ContactSheetOptions(int Width = 2400, int Height = 3000, double Resolution = 300,
    int Columns = 5, int Rows = 6, bool DownFirst = false, int HorizontalSpacing = 10, int VerticalSpacing = 10,
    bool RotateToFit = false, bool Captions = true, string FontFamily = "Arial", double FontSize = 12, bool Flatten = true,
    ImageMode Mode = ImageMode.RGB, int Depth = 8, string? ColorProfile = null, bool GroupByFolder = false,
    bool AutoSpacing = false, ContactSheetUnit DisplayUnit = ContactSheetUnit.Pixels, bool ResolutionPerCentimeter = false);
/// <summary>What is written under each PDF page, in this order.</summary>
[Flags]
public enum PdfCaption { None = 0, FileName = 1, Extension = 2, Title = 4, Description = 8, Author = 16, Copyright = 32, Exif = 64 }
/// <summary>
/// <paramref name="FileNames"/> is the earlier single switch and still means file name with extension. Presentation
/// properties apply only when <paramref name="Presentation"/> is set; see <see cref="PdfSlideshow"/>.
/// </summary>
public sealed record PdfPresentationOptions(SKColor Background, double FontSize = 12, bool FileNames = false,
    bool Presentation = false, bool AutoAdvance = true, double AdvanceSeconds = 5, bool Loop = false,
    PdfTransition Transition = PdfTransition.None, PdfCaption Captions = PdfCaption.None)
{
    public PdfCaption EffectiveCaptions => Captions | (FileNames ? PdfCaption.FileName | PdfCaption.Extension : PdfCaption.None);
}

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

    public static SKBitmap RenderSource(AutomationSource source) => RenderSource(source, false);

    public static SKBitmap RenderSource(AutomationSource source, bool deep)
    {
        if (source.Document is { } document) return deep ? DocumentRenderer.RenderDeep(document) : DocumentRenderer.Render(document);
        if (string.IsNullOrWhiteSpace(source.Path)) throw new ArgumentException("Select a source file.");
        using var opened = ImageImporter.NewDocument(ImageImporter.Decode(source.Path));
        return deep ? DocumentRenderer.RenderDeep(opened) : DocumentRenderer.Render(opened);
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
        if (options.AutoSpacing)
        {
            if (!double.IsFinite(options.Resolution) || options.Resolution is < 1 or > 9600) throw new ArgumentException("Invalid contact sheet settings.");
            var gap = Math.Max(1, (int)Math.Round(options.Resolution / 30));
            options = options with { HorizontalSpacing = gap, VerticalSpacing = gap };
        }
        if (options.Columns is < 1 or > 100 || options.Rows is < 1 or > 100 || options.HorizontalSpacing < 0 || options.VerticalSpacing < 0
            || !double.IsFinite(options.Resolution) || options.Resolution is < 1 or > 9600 || !double.IsFinite(options.FontSize) || options.FontSize is < 1 or > 300
            || options.Depth is not (8 or 16) || options.Mode is not (ImageMode.RGB or ImageMode.Grayscale or ImageMode.CMYK or ImageMode.Lab)
            || options.FontFamily is not { Length: > 0 and <= 256 } || !Enum.IsDefined(options.DisplayUnit))
            throw new ArgumentException("Invalid contact sheet settings.");
        int capacity = options.Columns * options.Rows;
        var groups = options.GroupByFolder ? sources.GroupBy(source => source.Path is null ? "" : Path.GetDirectoryName(Path.GetFullPath(source.Path)), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.ToArray()).ToArray() : [sources.ToArray()];
        var pageSources = groups.SelectMany(group => group.Chunk(capacity)).ToArray();
        int pages = pageSources.Length;
        if ((long)pages * options.Width * options.Height * (options.Flatten ? 1 : 2) > DocumentLimits.DocumentPixelBudget)
            throw new InvalidOperationException("The contact sheets exceed the document memory budget.");
        MemoryBudget.Current.Check(checked((long)pages * options.Width * options.Height * (options.Depth == 16 ? 36 : 12) * (options.Flatten ? 1 : 2)));
        byte[]? profile = null;
        if (options.Mode != ImageMode.Lab)
        {
            profile = IccProfiles.Resolve(options.ColorProfile ?? ColorDefaults.Settings.ProfileName(options.Mode));
            var model = options.Mode == ImageMode.CMYK ? "CMYK" : options.Mode == ImageMode.Grayscale ? "GRAY" : "RGB";
            if (IccProfiles.Model(profile) != model) throw new ArgumentException("The color profile does not match the contact sheet mode.");
        }
        var settings = new ImageSettings { Mode = options.Mode, Depth = options.Depth,
            ColorProfile = options.Mode == ImageMode.CMYK ? null : profile, CmykProfile = options.Mode == ImageMode.CMYK ? profile : null };
        float cellWidth = (options.Width - (options.Columns + 1f) * options.HorizontalSpacing) / options.Columns;
        float cellHeight = (options.Height - (options.Rows + 1f) * options.VerticalSpacing) / options.Rows;
        float textSize = (float)(options.FontSize * options.Resolution / 72), caption = options.Captions ? textSize * 1.5f : 0;
        if (cellWidth < 1 || cellHeight - caption < 1) throw new ArgumentException("The page is too small for the rows, columns and captions.");
        var results = new List<CanvasDocument>();
        using var typeface = FontCatalog.Resolve(options.FontFamily);
        using var font = new SKFont(typeface, textSize);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        try
        {
            for (int page = 0; page < pages; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var document = new CanvasDocument(Guid.NewGuid(), options.Width, options.Height, options.Resolution);
                document.Image = settings;
                results.Add(document);
                using var background = new SKBitmap(options.Depth == 16 ? DeepPixels.Info(options.Width, options.Height) : Bitmaps.ColorInfo(options.Width, options.Height)); background.Erase(SKColors.White);
                using var pageCanvas = new SKCanvas(background);
                for (int slot = 0; slot < pageSources[page].Length; slot++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var source = pageSources[page][slot];
                    using var image = RenderSource(source, options.Depth == 16);
                    int column = options.DownFirst ? slot / options.Rows : slot % options.Columns;
                    int row = options.DownFirst ? slot % options.Rows : slot / options.Columns;
                    float x = options.HorizontalSpacing + column * (cellWidth + options.HorizontalSpacing);
                    float y = options.VerticalSpacing + row * (cellHeight + options.VerticalSpacing);
                    bool rotate = options.RotateToFit && Math.Min(cellWidth / image.Height, (cellHeight - caption) / image.Width)
                        > Math.Min(cellWidth / image.Width, (cellHeight - caption) / image.Height);
                    int tileWidth = Math.Max(1, (int)cellWidth), tileHeight = Math.Max(1, (int)cellHeight);
                    using var tile = new SKBitmap(options.Depth == 16 ? DeepPixels.Info(tileWidth, tileHeight) : Bitmaps.ColorInfo(tileWidth, tileHeight)); tile.Erase(SKColors.Transparent);
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
                    else document.Layers.Add(new ImageLayer(Guid.NewGuid(), ImageModeEdits.Converted(tile, source.Name, settings),
                        new LayerTransform(x, y, tile.Width, tile.Height), source.Name));
                }
                document.Layers.Insert(0, new ImageLayer(Guid.NewGuid(), ImageModeEdits.Converted(background, "Background", settings),
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
        if (options.Captions < PdfCaption.None || options.Captions > (PdfCaption)127) throw new ArgumentException("Invalid caption fields.");
        if (options.Presentation) PdfSlideshow.Validate(options);
        // The pages are drawn first; presentation timing and transitions are an update appended to that file.
        using var rendered = new MemoryStream();
        using (var pdf = SKDocument.CreatePdf(rendered))
        {
            using var font = new SKFont(SKTypeface.Default, (float)options.FontSize);
            using var paint = new SKPaint { Color = options.Background == SKColors.Black ? SKColors.White : SKColors.Black, IsAntialias = true };
            var lineHeight = (float)options.FontSize * 1.4f;
            foreach (var source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var image = RenderSource(source);
                var lines = PdfCaptionLines(source, options.EffectiveCaptions);
                float scale = Math.Min(1, 14400f / Math.Max(image.Width, image.Height));
                float width = image.Width * scale, height = image.Height * scale;
                var caption = lines.Count == 0 ? 0 : lines.Count * lineHeight + (float)options.FontSize * .6f;
                var canvas = pdf.BeginPage(width, height + caption); canvas.Clear(options.Background);
                canvas.DrawBitmap(image, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear));
                for (var line = 0; line < lines.Count; line++)
                    canvas.DrawText(lines[line], width / 2, height + (float)options.FontSize + line * lineHeight, SKTextAlign.Center, font, paint);
                pdf.EndPage();
            }
            pdf.Close();
        }
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = PdfSlideshow.Apply(rendered.ToArray(), options);
        AtomicFile.WriteBytes(path, bytes, cancellationToken);
    }

    /// <summary>The caption lines for one page: name, then the File Info fields asked for that have a value.</summary>
    public static IReadOnlyList<string> PdfCaptionLines(AutomationSource source, PdfCaption captions)
    {
        var lines = new List<string>();
        if (captions == PdfCaption.None) return lines;
        if (captions.HasFlag(PdfCaption.FileName))
            lines.Add(captions.HasFlag(PdfCaption.Extension) ? source.Name : System.IO.Path.GetFileNameWithoutExtension(source.Name));
        if ((captions & ~(PdfCaption.FileName | PdfCaption.Extension)) != PdfCaption.None)
        {
            var metadata = source.Document?.Metadata ?? (source.Path is { Length: > 0 } file ? ImageMetadata.Read(file) : new DocumentMetadata());
            void Add(PdfCaption flag, string value) { if (captions.HasFlag(flag) && !string.IsNullOrWhiteSpace(value)) lines.Add(value.Trim()); }
            Add(PdfCaption.Title, metadata.Title);
            Add(PdfCaption.Description, metadata.Description);
            Add(PdfCaption.Author, metadata.Author);
            Add(PdfCaption.Copyright, metadata.Copyright);
            Add(PdfCaption.Exif, string.Join("  ", new[] { metadata.CameraMake, metadata.CameraModel, metadata.DateTaken }.Where(v => !string.IsNullOrWhiteSpace(v))));
        }
        // One page caption line stays readable; a long description is shortened rather than drawn off the page.
        return lines.Select(line => line.Replace('\r', ' ').Replace('\n', ' ')).Select(line => line.Length > 300 ? line[..300] + "…" : line).ToArray();
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
