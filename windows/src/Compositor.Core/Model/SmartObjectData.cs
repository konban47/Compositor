using System.IO.Compression;
using Compositor.Core.IO;
using Compositor.Core.Format;

namespace Compositor.Core.Model;

/// <summary>Immutable embedded source; transforms sample the cached original-size image, never a rescaled copy.</summary>
public sealed class SmartObjectData
{
    public const int MaxBytes = 128 * 1024 * 1024;
    public Guid ID { get; }
    public byte[] Package { get; }
    public SmartObjectData(Guid id, byte[] package)
    {
        if (id == Guid.Empty || package.Length is < 22 or > MaxBytes) throw new ProjectException(ProjectError.Invalid);
        ID = id; Package = package;
    }
    public static string FileName(Guid id) => $"{id:D}.smart.zip".ToUpperInvariant();
    public static SmartObjectData FromDocument(CanvasDocument document, Guid? id = null)
    {
        var snapshot = ProjectSnapshot.FromDocument(document); ProjectStore.Validate(snapshot.Manifest);
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            void Write(string name, byte[] bytes)
            {
                using var file = zip.CreateEntry(name, CompressionLevel.Fastest).Open(); file.Write(bytes);
                if (stream.Length > MaxBytes) throw new ProjectException(ProjectError.TooLarge);
            }
            Write(ProjectStore.ManifestName, ManifestJson.Serialize(snapshot.Manifest));
            foreach (var layer in snapshot.Manifest.Layers)
            {
                if (layer.ImageFile is { } image) Write(image, PngCodec.Encode(snapshot.Images[layer.ID].Image));
                if (layer.MaskFile is { } mask) Write(mask, PngCodec.Encode(snapshot.Masks[layer.ID].Image));
                if (layer.SmartObjectFile is { } nested) Write(nested, snapshot.SmartObjects[layer.ID].Package);
            }
            foreach (var channel in snapshot.Manifest.Channels ?? []) Write(channel.ImageFile, PngCodec.Encode(snapshot.Channels[channel.ID].Image));
        }
        return new SmartObjectData(id ?? Guid.NewGuid(), stream.ToArray());
    }
    public ProjectSnapshot Open()
    {
        using var stream = new MemoryStream(Package, false);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        if (zip.Entries.Count > 30_001 || zip.Entries.Select(e => e.FullName).Distinct(StringComparer.Ordinal).Count() != zip.Entries.Count
            || zip.Entries.Sum(e => e.Length) > MaxBytes * 4L) throw new ProjectException(ProjectError.TooLarge);
        byte[] Read(string name, int max)
        {
            var entry = zip.GetEntry(name) ?? throw new ProjectException(ProjectError.MissingImage);
            if (entry.Length < 0 || entry.Length > max) throw new ProjectException(ProjectError.TooLarge);
            using var file = entry.Open(); var result = new byte[checked((int)entry.Length)]; file.ReadExactly(result);
            if (file.ReadByte() != -1) throw new ProjectException(ProjectError.Invalid);
            return result;
        }
        var manifest = ManifestJson.Deserialize(Read(ProjectStore.ManifestName, DocumentLimits.MaxManifestBytes)); ProjectStore.Validate(manifest);
        var result = new ProjectSnapshot(manifest); var pixels = 0; var masks = 0;
        try
        {
            ImportedImage Asset(string name, string title, bool mask)
            {
                var bytes = Read(name, DocumentLimits.MaxAssetBytes); var header = PngCodec.ReadHeader(bytes);
                if (!header.IsEightBitOrLess) throw new ProjectException(ProjectError.Invalid);
                if (mask) ProjectStore.CheckSize(header.Width, header.Height, ref masks); else ProjectStore.CheckSize(header.Width, header.Height, ref pixels);
                return ImportedImage.Create(mask ? PngCodec.DecodeMask(header, bytes) : PngCodec.DecodeImage(header, bytes), title);
            }
            foreach (var layer in manifest.Layers)
            {
                if (layer.ImageFile is { } image) result.Images[layer.ID] = Asset(image, layer.Name, false);
                if (layer.MaskFile is { } mask) result.Masks[layer.ID] = Asset(mask, layer.Name, true);
                if (layer.SmartObjectFile is { } nested) result.SmartObjects[layer.ID] = new SmartObjectData(layer.SmartObjectID!.Value, Read(nested, MaxBytes));
            }
            foreach (var channel in manifest.Channels ?? [])
            {
                var asset = Asset(channel.ImageFile, channel.Name, true);
                if (asset.Width != manifest.Width || asset.Height != manifest.Height) { asset.Dispose(); throw new ProjectException(ProjectError.Invalid); }
                result.Channels[channel.ID] = asset;
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
