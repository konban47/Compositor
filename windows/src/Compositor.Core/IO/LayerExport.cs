using Compositor.Core.Rendering;
using SkiaSharp;
using Compositor.Core.Document;
using Compositor.Core.Model;

namespace Compositor.Core.IO;

public enum LayerExportKind { Layers, LayerComps, LayerCompsPdf }
public sealed record LayerExportOptions(string OutputDirectory, LayerExportKind Kind,
    ExportFormat Format = ExportFormat.Png, bool VisibleOnly = true, int Quality = 90,
    string Prefix = "");
public sealed record LayerExportItem(Guid ID, string Name);

/// <summary>Exports isolated layers or recorded compositions without changing the source document.</summary>
public static class LayerExport
{
    public static IReadOnlyList<LayerExportItem> Items(CanvasDocument document, LayerExportOptions options)
    {
        if (options.Kind != LayerExportKind.Layers)
            return document.LayerComps.Select(c => new LayerExportItem(c.ID, c.Name)).ToArray();
        var visible = document.EffectiveVisibleIDs();
        return document.Layers.Where(l => !options.VisibleOnly || visible.Contains(l.ID))
            .Select(l => new LayerExportItem(l.ID, l.Name)).ToArray();
    }

    public static IReadOnlyList<string> Run(CanvasDocument document, LayerExportOptions options,
        IReadOnlyCollection<Guid>? selected = null, IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.OutputDirectory);
        if (!Enum.IsDefined(options.Kind) || !Enum.IsDefined(options.Format) || options.Quality is < 1 or > 100
            || options.Prefix is null || options.Prefix.Length > 100)
            throw new ArgumentException("Invalid export settings.", nameof(options));
        var items = Items(document, options).Where(i => selected is null || selected.Contains(i.ID)).ToArray();
        if (items.Length == 0) throw new InvalidOperationException("Select at least one item to export.");
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(options.OutputDirectory);
        if (options.Kind == LayerExportKind.LayerCompsPdf)
            return ExportCompsPdf(document, options, items, progress, cancellationToken);
        using var source = new DocumentLease(document);
        var files = new List<string>();
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var copy = new DocumentLease(source.Document);
            if (options.Kind == LayerExportKind.Layers) Isolate(copy.Document, item.ID);
            else LayerComps.Apply(copy.Document, source.Document.LayerComps.Single(c => c.ID == item.ID), respectLocks: false);
            var export = new ExportOptions(options.Format, copy.Document.Width, copy.Document.Height,
                Quality: options.Quality, Resolution: copy.Document.Resolution,
                NativeColorMode: options.Format == ExportFormat.Tiff);
            using var encoded = ExportImage.Encode(copy.Document, export);
            cancellationToken.ThrowIfCancellationRequested();
            var name = BatchProcessing.BuildName(item.Name, files.Count + 1, DateTime.Today,
                [new(FileNamePart.Literal, options.Prefix), new(FileNamePart.DocumentName)]);
            var path = BatchProcessing.UniquePath(options.OutputDirectory, name, export.Extension);
            AtomicFile.WriteBytes(path, encoded.Bytes);
            files.Add(path);
            progress?.Report(new BatchProgress(files.Count, items.Length, item.Name));
        }
        return files;
    }

    private static IReadOnlyList<string> ExportCompsPdf(CanvasDocument document, LayerExportOptions options,
        IReadOnlyList<LayerExportItem> items, IProgress<BatchProgress>? progress, CancellationToken cancellation)
    {
        using var source = new DocumentLease(document);
        var name = BatchProcessing.BuildName("Layer Comps", 1, DateTime.Today,
            [new(FileNamePart.Literal, options.Prefix), new(FileNamePart.DocumentName)]);
        var path = BatchProcessing.UniquePath(options.OutputDirectory, name, ".pdf");
        AtomicFile.Write(path, stream =>
        {
            using var pdf = SKDocument.CreatePdf(stream);
            for (var index = 0; index < items.Count; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                using var copy = new DocumentLease(source.Document);
                LayerComps.Apply(copy.Document, source.Document.LayerComps.Single(c => c.ID == items[index].ID), respectLocks: false);
                using var image = DocumentRenderer.Render(copy.Document);
                var scale = Math.Min(72d / copy.Document.Resolution, 14400d / Math.Max(image.Width, image.Height));
                var width = (float)(image.Width * scale); var height = (float)(image.Height * scale);
                var canvas = pdf.BeginPage(width, height);
                canvas.Clear(SKColors.White);
                canvas.DrawBitmap(image, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear));
                pdf.EndPage();
                progress?.Report(new BatchProgress(index + 1, items.Count, items[index].Name));
            }
            cancellation.ThrowIfCancellationRequested();
            pdf.Close();
        }, cancellation);
        return [path];
    }

    private static void Isolate(CanvasDocument document, Guid id)
    {
        var layer = document.Layers.Single(l => l.ID == id);
        var included = document.Descendants(id);
        included.Add(id);
        var ancestors = new HashSet<Guid>();
        for (var parent = layer.ParentID; parent is { } parentID;)
        {
            if (!ancestors.Add(parentID)) break;
            parent = document.Layers.FirstOrDefault(l => l.ID == parentID)?.ParentID;
        }
        foreach (var candidate in document.Layers)
            candidate.IsVisible = candidate.ID == id || ancestors.Contains(candidate.ID)
                || included.Contains(candidate.ID) && candidate.IsVisible;
        // Keep invisible clipping bases in the hierarchy: their pixels still define coverage.
    }
}
