using Compositor.Core.Document;
using Compositor.Core.Model;
using ImageMagick;

namespace Compositor.Core.IO;

/// <summary>Raster import using the DICOM decoder, including its window center/width interpretation.</summary>
public static class DicomImport
{
    public static CanvasDocument Load(IReadOnlyList<string> paths, CancellationToken cancellation = default)
    {
        if (paths.Count is < 1 or > 256) throw new ArgumentException("Choose between 1 and 256 DICOM files.");
        var planned = new List<(string Path, int Frame, int Width, int Height)>();
        long pixels = 0;
        foreach (var path in paths)
        {
            cancellation.ThrowIfCancellationRequested();
            if (new FileInfo(path).Length > DocumentLimits.MaxAssetBytes) throw new ImportException(ImportError.TooLarge);
            using var headers = new MagickImageCollection();
            headers.Ping(path, new MagickReadSettings { Format = MagickFormat.Dcm });
            for (var frame = 0; frame < headers.Count; frame++)
            {
                var header = headers[frame];
                pixels += (long)header.Width * header.Height;
                if (header.Width is 0 or > DocumentLimits.MaxSide || header.Height is 0 or > DocumentLimits.MaxSide
                    || planned.Count >= 256 || pixels > DocumentLimits.DocumentPixelBudget)
                    throw new ImportException(ImportError.TooLarge);
                planned.Add((path, frame, (int)header.Width, (int)header.Height));
            }
        }
        if (planned.Count == 0) throw new InvalidDataException("No DICOM image frames were found.");
        MemoryBudget.Current.Check(checked(pixels * 36));
        var document = new CanvasDocument(Guid.NewGuid(), planned.Max(item => item.Width), planned.Max(item => item.Height));
        document.Image = document.Image with { Depth = 16 };
        try
        {
            var remaining = DocumentLimits.DocumentPixelBudget;
            foreach (var item in planned)
            {
                cancellation.ThrowIfCancellationRequested();
                using var frame = new MagickImage();
                frame.Read(item.Path, new MagickReadSettings { Format = MagickFormat.Dcm, FrameIndex = (uint)item.Frame, FrameCount = 1 });
                if (frame.Width != item.Width || frame.Height != item.Height) throw new InvalidDataException("The DICOM file changed during import.");
                frame.Strip();
                var name = $"{Path.GetFileNameWithoutExtension(item.Path)} — {item.Frame + 1}";
                var asset = DeepImageCodec.Decode(frame.ToByteArray(MagickFormat.Png64), name, remaining);
                remaining -= asset.Width * asset.Height;
                document.Layers.Add(new(Guid.NewGuid(), asset, new LayerTransform(0, 0, asset.Width, asset.Height), name));
            }
            cancellation.ThrowIfCancellationRequested();
            return document;
        }
        catch { document.Dispose(); throw; }
    }
}
