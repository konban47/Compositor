using Compositor.Core.Model;
using SkiaSharp;
namespace Compositor.Core.IO.PSD;
public sealed record PsdArtboardImport(string Name, PsdImport Import);
public static class PsdArtboards
{
    public static IReadOnlyList<PsdArtboardImport> Read(string path, CancellationToken cancellation = default)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.RandomAccess);
        var budget = DocumentLimits.DocumentPixelBudget;
        var source = PsdReader.Read(stream, budget, Path.GetFileNameWithoutExtension(path), cancellation);
        var result = new List<PsdArtboardImport>();
        try
        {
            var boards = Split(source);
            if (boards.Count == 0) { result.Add(new(Path.GetFileNameWithoutExtension(path), PsdDocumentBuilder.Build(source, budget))); return result; }
            foreach (var (name, document) in boards)
            { cancellation.ThrowIfCancellationRequested(); result.Add(new(name, PsdDocumentBuilder.Build(document, budget))); }
            var used = boards.SelectMany(b => b.Document.Layers).Select(l => l.Id).ToHashSet();
            var outside = source.Layers.Count(l => l.Artboard is null && !used.Contains(l.Id));
            if (outside > 0)
            {
                var first = result[0]; var import = first.Import;
                result[0] = first with { Import = new PsdImport { Manifest = import.Manifest, Images = import.Images, Masks = import.Masks,
                    Notes = import.Notes.Concat([new PsdConversionNote(first.Name, "Layers outside all artboards are excluded from the separate artboard documents.")]).ToArray() } };
            }
            foreach (var layer in source.Layers.Where(l => !used.Contains(l.Id))) { layer.Image?.Dispose(); layer.Mask?.Dispose(); }
            return result;
        }
        catch
        {
            foreach (var item in result) item.Import.Snapshot().Dispose();
            foreach (var layer in source.Layers) { layer.Image?.Dispose(); layer.Mask?.Dispose(); }
            throw;
        }
    }
    internal static List<(string Name, PsdDocument Document)> Split(PsdDocument source)
    {
        var boards = source.Layers.Where(l => l.IsGroup && l.Artboard is not null).ToArray();
        var ids = boards.Select(l => l.Id).ToHashSet(); var byId = source.Layers.ToDictionary(l => l.Id);
        Guid? Board(PsdRecord layer)
        {
            var parent = layer.ParentId; var visited = new HashSet<Guid>();
            while (parent is { } id && visited.Add(id)) { if (ids.Contains(id)) return id; parent = byId.GetValueOrDefault(id)?.ParentId; } return null;
        }
        var output = new List<(string, PsdDocument)>();
        foreach (var board in boards)
        {
            var box = board.Artboard!.Value;
            if (box.Width < 1 || box.Height < 1 || box.Width > DocumentLimits.MaxSide || box.Height > DocumentLimits.MaxSide || (long)box.Width * box.Height > DocumentLimits.MaxSurfacePixels) throw PsdFailure.TooLarge;
            var document = new PsdDocument { Width = box.Width, Height = box.Height, Resolution = source.Resolution, CmykProfile = source.CmykProfile };
            PsdRect Shift(PsdRect r) => new(r.Left - box.Left, r.Top - box.Top, r.Right - box.Left, r.Bottom - box.Top);
            foreach (var original in source.Layers.Where(l => Board(l) == board.Id))
            {
                var layer = original.Copy(); if (layer.ParentId == board.Id) layer.ParentId = null;
                layer.Bounds = layer.IsGroup ? new(0, 0, box.Width, box.Height) : Shift(layer.Bounds); layer.MaskBounds = Shift(layer.MaskBounds);
                if (layer.Text is { } text) layer.Text = text with { X = text.X - box.Left, Y = text.Y - box.Top };
                if (layer.VectorMask is { } vector) { using var path = SKPath.ParseSvgPathData(vector.Path); if (path is not null) { path.Transform(SKMatrix.CreateTranslation(-box.Left, -box.Top)); layer.VectorMask = vector with { Path = path.ToSvgPathData() }; } }
                document.Layers.Add(layer);
            }
            output.Add((board.Name, document));
        }
        return output;
    }
}
