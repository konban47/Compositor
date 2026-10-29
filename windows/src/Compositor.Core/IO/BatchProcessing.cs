using Compositor.Core.Document;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;
using System.Globalization;

namespace Compositor.Core.IO;

public enum BatchAction { None, AutoColor, AutoTone, AutoContrast, Vibrance, Desaturate, Equalize }
public enum FileNamePart { DocumentName, LowerName, UpperName, Serial1, Serial2, Serial3, Serial4, Date, Literal }
public sealed record FileNameComponent(FileNamePart Part, string Text = "");
public sealed record BatchOutput(ExportFormat Format, int Quality = 90, bool Resize = false,
    int MaximumWidth = 1920, int MaximumHeight = 1080, bool NoEnlarge = true, bool ConvertToSrgb = false);
public sealed record BatchOptions(string OutputDirectory, BatchAction Action, IReadOnlyList<BatchOutput> Outputs,
    IReadOnlyList<FileNameComponent> Naming, int StartSerial = 1, bool ContinueOnError = false,
    MetadataPolicy Metadata = MetadataPolicy.CopyrightAndContact);
public sealed record BatchProgress(int Completed, int Total, string Name);
public sealed record BatchItemResult(string Source, IReadOnlyList<string> Files, string? Error);
public sealed record BatchResult(IReadOnlyList<BatchItemResult> Items)
{
    public int Succeeded => Items.Count(i => i.Error is null);
    public int Failed => Items.Count(i => i.Error is not null);
}

/// <summary>Image processor and batch dialog share these actions, encoders and naming rules.</summary>
public static class BatchProcessing
{
    public static BatchResult Run(IReadOnlyList<AutomationSource> sources, BatchOptions options,
        IProgress<BatchProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        Validate(options);
        if (sources.Count is < 1 or > 10000) throw new ArgumentException("Select between 1 and 10000 source images.");
        var directory = Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(directory);
        var results = new List<BatchItemResult>();
        var date = DateTime.Today;
        for (var index = 0; index < sources.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = sources[index];
            var files = new List<string>();
            string? error = null;
            try
            {
                using var document = OpenComposite(source);
                Apply(document, options.Action);
                var name = BuildName(source.Name, checked(options.StartSerial + index), date, options.Naming);
                foreach (var output in options.Outputs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var lease = new DocumentLease(document);
                    var copy = lease.Document;
                    if (output.ConvertToSrgb) copy.Image = copy.Image with { ColorProfile = null, CmykProfile = null,
                        ProfileEncoding = null, Mode = Compositor.Core.Format.ImageMode.RGB };
                    var size = output.Resize ? FileAutomation.Fit(copy.Width, copy.Height, output.MaximumWidth, output.MaximumHeight, output.NoEnlarge)
                        : (copy.Width, copy.Height);
                    var export = new ExportOptions(output.Format, size.Item1, size.Item2, output.Quality,
                        Resolution: copy.Resolution, NativeColorMode: output.Format == ExportFormat.Tiff && !output.ConvertToSrgb,
                        Metadata: output.Format is ExportFormat.Pdf or ExportFormat.Exr ? MetadataPolicy.None : options.Metadata);
                    var folder = options.Outputs.Count > 1 ? Path.Combine(directory, output.Format.ToString()) : directory;
                    Directory.CreateDirectory(folder);
                    var path = UniquePath(folder, name, export.Extension);
                    using var encoded = ExportImage.Encode(copy, export);
                    cancellationToken.ThrowIfCancellationRequested();
                    AtomicFile.WriteBytes(path, encoded.Bytes, cancellationToken);
                    files.Add(path);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                error = exception.Message;
            }
            results.Add(new BatchItemResult(source.Name, files, error));
            progress?.Report(new BatchProgress(index + 1, sources.Count, source.Name));
            if (error is not null && !options.ContinueOnError) break;
        }
        return new BatchResult(results);
    }

    public static void Validate(BatchOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.OutputDirectory)) throw new ArgumentException("Choose an output folder.");
        if (!Enum.IsDefined(options.Action) || !Enum.IsDefined(options.Metadata) || options.StartSerial < 0 || options.StartSerial > int.MaxValue - 10000)
            throw new ArgumentException("Invalid batch action or starting serial number.");
        if (options.Naming is null || options.Naming.Count is < 1 or > 6 || options.Naming.Any(n => n is null || !Enum.IsDefined(n.Part) || n.Text is null || n.Text.Length > 100))
            throw new ArgumentException("Choose between 1 and 6 filename components.");
        if (options.Outputs is null || options.Outputs.Count is < 1 or > 6 || options.Outputs.Any(o => o is null) || options.Outputs.Select(o => o.Format).Distinct().Count() != options.Outputs.Count)
            throw new ArgumentException("Select at least one output format, without duplicates.");
        foreach (var output in options.Outputs)
        {
            if (!Enum.IsDefined(output.Format) || output.Quality is < 1 or > 100) throw new ArgumentException("Invalid output format or quality.");
            if (output.Resize) _ = FileAutomation.Fit(1, 1, output.MaximumWidth, output.MaximumHeight, output.NoEnlarge);
        }
    }

    public static string BuildName(string source, int serial, DateTime date, IReadOnlyList<FileNameComponent> components)
    {
        var original = Path.GetFileNameWithoutExtension(source);
        var name = string.Concat(components.Select(component => component.Part switch
        {
            FileNamePart.DocumentName => original,
            FileNamePart.LowerName => original.ToLowerInvariant(),
            FileNamePart.UpperName => original.ToUpperInvariant(),
            FileNamePart.Serial1 => serial.ToString(CultureInfo.InvariantCulture),
            FileNamePart.Serial2 => serial.ToString("D2", CultureInfo.InvariantCulture),
            FileNamePart.Serial3 => serial.ToString("D3", CultureInfo.InvariantCulture),
            FileNamePart.Serial4 => serial.ToString("D4", CultureInfo.InvariantCulture),
            FileNamePart.Date => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            FileNamePart.Literal => component.Text,
            _ => throw new ArgumentException("Unknown filename component.")
        }));
        var invalid = Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*").ToHashSet();
        name = new string(name.Select(c => c < 32 || invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        if (name.Length > 180) name = name[..180].TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(name)) name = "Image";
        var stem = name.Split('.')[0];
        if (new[] { "CON", "PRN", "AUX", "NUL" }.Contains(stem, StringComparer.OrdinalIgnoreCase)
            || stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9') name = "_" + name;
        return name;
    }

    public static string UniquePath(string folder, string name, string extension)
    {
        var path = Path.Combine(folder, name + "." + extension);
        for (var index = 2; File.Exists(path) || Directory.Exists(path); index++)
            path = Path.Combine(folder, name + "-" + index.ToString(CultureInfo.InvariantCulture) + "." + extension);
        return path;
    }

    private static CanvasDocument OpenComposite(AutomationSource source)
    {
        if (source.Document is null)
        {
            if (string.IsNullOrWhiteSpace(source.Path)) throw new ArgumentException("Select a source file.");
            return ImageImporter.NewDocument(ImageImporter.Decode(source.Path));
        }
        var input = source.Document;
        var depth = Math.Max(input.Image.Depth, input.Layers.Select(l => l.Asset?.BitDepth ?? 8).DefaultIfEmpty(8).Max());
        var pixels = depth > 8 ? DocumentRenderer.RenderDeep(input) : DocumentRenderer.Render(input);
        var asset = depth > 8 ? ImportedImage.CreateDeep(pixels, source.Name, depth) : ImportedImage.Create(pixels, source.Name);
        var result = ImageImporter.NewDocument(asset, input.Resolution);
        result.Image = input.Image with { ColorProfile = input.Image.ColorProfile?.ToArray(), CmykProfile = input.Image.CmykProfile?.ToArray() };
        return result;
    }

    private static void Apply(CanvasDocument document, BatchAction action)
    {
        if (action == BatchAction.None) return;
        _ = ApplyTo(document, document.Layers[0].ID, action);
    }

    /// <summary>One built-in action on one pixel layer; the Script Events Manager runs these on the active document.</summary>
    public static bool ApplyTo(CanvasDocument document, Guid id, BatchAction action) => action switch
    {
        BatchAction.None => false,
        BatchAction.AutoColor => LevelsEdits.Auto(document, id, LevelsAuto.Neutral),
        BatchAction.AutoTone => LevelsEdits.Auto(document, id, LevelsAuto.Color),
        BatchAction.AutoContrast => LevelsEdits.Auto(document, id, LevelsAuto.Contrast),
        BatchAction.Vibrance => ImageAdjustments.Apply(document, id, new ImageAdjustmentSettings { Kind = ImageAdjustmentKind.Vibrance, Values = new() { ["Vibrance"] = 25 } }),
        BatchAction.Desaturate => ImageAdjustments.Apply(document, id, new ImageAdjustmentSettings { Kind = ImageAdjustmentKind.Desaturate }),
        BatchAction.Equalize => ImageAdjustments.Apply(document, id, new ImageAdjustmentSettings { Kind = ImageAdjustmentKind.Equalize }),
        _ => throw new ArgumentException("Unknown batch action.")
    };
}
