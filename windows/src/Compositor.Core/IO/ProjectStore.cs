using System.Text;
using System.Text.Json;
using Compositor.Core.Format;

namespace Compositor.Core.IO;

/// <summary>
/// Reads and writes `.comp` packages: a directory holding `manifest.json`, `images/&lt;id&gt;.png` assets
/// and an optional `QuickLook/Preview.jpg`.
/// </summary>
public static class ProjectStore
{
    public const string ManifestName = "manifest.json";
    public const string ImagesName = "images";
    public const string QuickLookFolderName = "QuickLook";
    public const string QuickLookPreviewName = "Preview.jpg";

    private static readonly object SaveLock = new();

    /// <summary>
    /// Everything the format holds, validated the way the Swift build validates it. Rejections are the
    /// point: a manifest that reaches the app must not be able to describe something it cannot draw.
    /// </summary>
    public static void Validate(ProjectManifest manifest)
    {
        if (manifest.Format != ProjectManifest.FormatIdentifier) throw new ProjectException(ProjectError.Invalid);
        if (!ProjectManifest.IsSupported(manifest.Version)) throw new ProjectException(ProjectError.Version, manifest.Version);
        if (manifest.ColorSpace != "sRGB") throw new ProjectException(ProjectError.Invalid);
        if (manifest.Resolution is { } resolution)
        {
            if (!double.IsFinite(resolution) || resolution is < 1 or > 9600) throw new ProjectException(ProjectError.Invalid);
        }
        if (manifest.Width is < 1 or > Model.DocumentLimits.MaxSide || manifest.Height is < 1 or > Model.DocumentLimits.MaxSide
            || manifest.Layers is not { Count: <= 10_000 })
        {
            throw new ProjectException(ProjectError.TooLarge);
        }

        var version = manifest.Version;
        if (manifest.Marks is { } marks && (version < 17 || marks.Count > 10_000 || marks.Any(m => m is null || !m.IsValid)
            || marks.Select(m => m.ID).Distinct().Count() != marks.Count)) throw new ProjectException(ProjectError.Invalid);
        var channelIDs = new HashSet<Guid>();
        if (manifest.Channels?.Count > 64) throw new ProjectException(ProjectError.TooLarge);
        foreach (var channel in manifest.Channels ?? [])
        {
            if (version < 12 || channel.ID == Guid.Empty || !channelIDs.Add(channel.ID)
                || string.IsNullOrWhiteSpace(channel.Name) || Encoding.UTF8.GetByteCount(channel.Name) > 16_384
                || channel.ImageFile != ProjectChannelRecord.FileName(channel.ID))
                throw new ProjectException(ProjectError.Invalid);
        }
        foreach (var layer in manifest.Layers)
        {
            if ((layer.Blending is not null && (!layer.Blending.IsValid || version < 15))
                || (layer.Effects is not null && (!layer.Effects.IsValid || version < 15 && (layer.Effects.Items is not null || !layer.Effects.Enabled)))
                || (layer.Label is { } label && (version < 15 || !Enum.IsDefined(label)))
                || (layer.Container is { } container && (version < 15 || !Enum.IsDefined(container) || layer.IsGroup != true))
                || (layer.SmartObjectFile is not null && (version < 15 || layer.SmartObjectFile != Model.SmartObjectData.FileName(layer.ID)
                    || layer.SmartObjectID is null || layer.ImageFile is null || layer.IsGroup == true))
                || (layer.SmartObjectFile is null && layer.SmartObjectID is not null)) throw new ProjectException(ProjectError.Invalid);
            if ((layer.Locks is { } locks && (locks < 0 || locks > 15))
                || (layer.FillOpacity is { } fill && (!double.IsFinite(fill) || fill < 0 || fill > 1))
                || (version < 12 && (layer.Locks is not null || layer.FillOpacity is not null || layer.LinkID is not null)))
                throw new ProjectException(ProjectError.Invalid);
            if (layer.Text is { } text)
            {
                // Per-letter colors arrived in version 10, per-letter faces in version 11.
                if (!text.IsValid || text.Vertical is not null && version < 16
                    || (text.ColorRuns is not null && version < 10)
                    || (text.FontRuns is not null && version < 11)
                    || (text.HasTypography && version < 13)
                    || (text.HasAdvancedTypography && version < 14)
                    || layer.ImageFile is null || layer.IsGroup == true || layer.Adjustment is not null)
                {
                    throw new ProjectException(ProjectError.Invalid);
                }
            }
            if (layer.Shape is { } shape)
            {
                if (!shape.IsValid || shape.Kind == Format.ShapeKind.Path && shape.LineWidth is not null && version < 16 || (shape.Kind == Format.ShapeKind.Path && version < 14)
                    || layer.ImageFile is null || layer.IsGroup == true || layer.Adjustment is not null)
                {
                    throw new ProjectException(ProjectError.Invalid);
                }
            }
            if (layer.Adjustment is { } adjustment)
            {
                if (version < 7 || layer.IsGroup == true || layer.ImageFile is not null || !adjustment.IsValid)
                {
                    throw new ProjectException(ProjectError.Invalid);
                }
                if (adjustment.NeedsVersion9 && version < 9) throw new ProjectException(ProjectError.Invalid);
            }
            if (!LayerMask.FromRecord(layer).IsValid(version, layer.IsGroup == true, layer.ID))
            {
                throw new ProjectException(ProjectError.Invalid);
            }

            // Folders took an opacity of their own in version 8, which multiplies into what is inside
            // them; their blend mode is still pass-through, so it stays Normal.
            var opacity = layer.Opacity ?? 1;
            var blend = layer.BlendMode ?? LayerBlendMode.Normal;
            if (!double.IsFinite(opacity) || opacity is < 0 or > 1
                || (version < 3 && (opacity != 1 || blend != LayerBlendMode.Normal))
                || (layer.IsGroup == true && (version < 15 && blend != LayerBlendMode.Normal || version < 8 && opacity != 1)))
            {
                throw new ProjectException(ProjectError.Invalid);
            }
        }

        Model.LayerHierarchy.Validate(manifest.Layers);
        Model.LiveMaskGraph.Validate(manifest.Layers);

        if (version < 5 && manifest.Layers.Any(layer => layer.MaskSourceID is not null)) throw new ProjectException(ProjectError.Invalid);
        if (version == 1 && manifest.Layers.Any(layer => layer.ParentID is not null || layer.IsGroup == true))
        {
            throw new ProjectException(ProjectError.Invalid);
        }

        var ids = new HashSet<Guid>();
        foreach (var layer in manifest.Layers)
        {
            if (!ids.Add(layer.ID) || !layer.Transform.ToRuntime().IsValid
                || string.IsNullOrWhiteSpace(layer.Name)
                || Encoding.UTF8.GetByteCount(layer.Name) > 16_384
                || (layer.ImageFile is not null && layer.ImageFile != LayerMask.ExpectedImageFile(layer.ID)))
            {
                throw new ProjectException(ProjectError.Invalid);
            }
        }
        if (manifest.ActiveLayerID is { } active && !ids.Contains(active)) throw new ProjectException(ProjectError.Invalid);

        ValidateGuides(manifest);
    }

    private static void ValidateGuides(ProjectManifest manifest)
    {
        var guides = manifest.Guides ?? [];
        if (manifest.Version < 8)
        {
            if (guides.Count > 0) throw new ProjectException(ProjectError.Invalid);
            return;
        }
        if (guides.Count > 1_000) throw new ProjectException(ProjectError.TooLarge);
        var ids = new HashSet<Guid>();
        foreach (var guide in guides)
        {
            if (!ids.Add(guide.ID) || !double.IsFinite(guide.Position) || Math.Abs(guide.Position) > 1_000_000)
            {
                throw new ProjectException(ProjectError.Invalid);
            }
        }
    }

    /// <summary>
    /// One image or mask, held to the side limit and to the document's shared pixel budget. Images and
    /// masks are counted separately, each against a full budget.
    /// </summary>
    public static void CheckSize(int width, int height, ref int used) =>
        CheckSize(width, height, ref used, Model.DocumentLimits.DocumentPixelBudget);

    public static void CheckSize(int width, int height, ref int used, int budget)
    {
        if (width is < 1 or > Model.DocumentLimits.MaxSide || height is < 1 or > Model.DocumentLimits.MaxSide
            || (long)width * height > budget - used)
        {
            throw new ProjectException(ProjectError.TooLarge);
        }
        used += width * height;
    }

    /// <summary>
    /// Writes the manifest and every referenced asset into a staging directory beside the target, then
    /// moves it into place. A watcher sees either the old package or the new one, never a half-written
    /// one; the only gap is the pair of directory renames Windows needs to swap a package.
    /// </summary>
    public static void Save(ProjectSnapshot snapshot, string path, byte[]? quickLookPreview = null)
    {
        Validate(snapshot.Manifest);

        var images = new Dictionary<string, byte[]>();
        var pixels = 0;
        var maskPixels = 0;
        foreach (var layer in snapshot.Manifest.Layers)
        {
            foreach (var isMask in new[] { false, true })
            {
                var filename = isMask ? layer.MaskFile : layer.ImageFile;
                if (filename is null) continue;
                var assets = isMask ? snapshot.Masks : snapshot.Images;
                if (!assets.TryGetValue(layer.ID, out var asset)) throw new ProjectException(ProjectError.MissingImage);

                if (isMask)
                {
                    if (!Model.LayerMask.IsValid(asset.Image)) throw new ProjectException(ProjectError.Invalid);
                    CheckSize(asset.Width, asset.Height, ref maskPixels);
                }
                else
                {
                    CheckSize(asset.Width, asset.Height, ref pixels);
                }
                images[filename] = PngCodec.Encode(asset.Image);
            }
        }

        foreach (var channel in snapshot.Manifest.Channels ?? [])
        {
            if (!snapshot.Channels.TryGetValue(channel.ID, out var asset)
                || asset.Width != snapshot.Manifest.Width || asset.Height != snapshot.Manifest.Height)
                throw new ProjectException(ProjectError.MissingImage);
            CheckSize(asset.Width, asset.Height, ref maskPixels);
            images[channel.ImageFile] = PngCodec.Encode(asset.Image);
        }
        long embeddedBytes = 0;
        foreach (var layer in snapshot.Manifest.Layers.Where(l => l.SmartObjectFile is not null))
        {
            if (!snapshot.SmartObjects.TryGetValue(layer.ID, out var smart) || smart.ID != layer.SmartObjectID)
                throw new ProjectException(ProjectError.MissingImage);
            embeddedBytes += smart.Package.Length;
            if (embeddedBytes > Model.SmartObjectData.MaxBytes) throw new ProjectException(ProjectError.TooLarge);
            images[layer.SmartObjectFile!] = smart.Package;
        }
        var metadata = ManifestJson.Serialize(snapshot.Manifest);
        if (metadata.Length > Model.DocumentLimits.MaxManifestBytes) throw new ProjectException(ProjectError.TooLarge);

        var target = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(target) ?? throw new ProjectException(ProjectError.Encode);
        var staging = Path.Combine(parent, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        var backup = Path.Combine(parent, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.old");
        lock (SaveLock)
        {
            // Never replace an unrelated directory or follow a package junction while saving.
            // This check belongs inside the writer lock, immediately before replacement.
            if (Directory.Exists(target))
            {
                if (File.GetAttributes(target).HasFlag(FileAttributes.ReparsePoint))
                    throw new ProjectException(ProjectError.Invalid);
                if (Directory.EnumerateFileSystemEntries(target).Any())
                {
                    var existing = ResolveAsset(target, ManifestName, Model.DocumentLimits.MaxManifestBytes);
                    Validate(ManifestJson.Deserialize(File.ReadAllBytes(existing)));
                }
            }
            try
            {
                Directory.CreateDirectory(staging);
                var imagesDirectory = Path.Combine(staging, ImagesName);
                Directory.CreateDirectory(imagesDirectory);
                foreach (var (filename, bytes) in images)
                {
                    File.WriteAllBytes(Path.Combine(imagesDirectory, filename), bytes);
                }
                if (quickLookPreview is not null)
                {
                    var quickLook = Path.Combine(staging, QuickLookFolderName);
                    Directory.CreateDirectory(quickLook);
                    File.WriteAllBytes(Path.Combine(quickLook, QuickLookPreviewName), quickLookPreview);
                }
                File.WriteAllBytes(Path.Combine(staging, ManifestName), metadata);

                var replaced = false;
                if (Directory.Exists(target))
                {
                    Directory.Move(target, backup);
                    replaced = true;
                }
                try
                {
                    Directory.Move(staging, target);
                }
                catch
                {
                    if (replaced) Directory.Move(backup, target);
                    throw;
                }
                if (replaced) TryDelete(backup);
            }
            finally
            {
                if (Directory.Exists(staging)) TryDelete(staging);
            }
        }
    }

    /// <summary>Reads a package. Nothing is decoded until the manifest has been validated.</summary>
    public static ProjectSnapshot Load(string path)
    {
        var package = Path.GetFullPath(path);
        if (!Directory.Exists(package)) throw new ProjectException(ProjectError.Invalid);
        if (File.GetAttributes(package).HasFlag(FileAttributes.ReparsePoint)) throw new ProjectException(ProjectError.Invalid);

        var metadata = File.ReadAllBytes(ResolveAsset(package, ManifestName, Model.DocumentLimits.MaxManifestBytes));

        ManifestJson.Header header;
        try
        {
            header = ManifestJson.ReadHeader(metadata);
        }
        catch (JsonException)
        {
            throw new ProjectException(ProjectError.Invalid);
        }
        if (header.Format != ProjectManifest.FormatIdentifier) throw new ProjectException(ProjectError.Invalid);
        if (!ProjectManifest.IsSupported(header.Version)) throw new ProjectException(ProjectError.Version, header.Version);

        ProjectManifest manifest;
        try
        {
            manifest = ManifestJson.Deserialize(metadata);
        }
        catch (Exception error) when (error is JsonException or NotSupportedException or FormatException or InvalidOperationException or OverflowException)
        {
            throw new ProjectException(ProjectError.Invalid);
        }
        Validate(manifest);

        var snapshot = new ProjectSnapshot(manifest);
        var pixels = 0;
        var maskPixels = 0;
        try
        {
            foreach (var layer in manifest.Layers)
            {
                foreach (var isMask in new[] { false, true })
                {
                    var filename = isMask ? layer.MaskFile : layer.ImageFile;
                    if (filename is null) continue;
                    var file = ResolveAsset(package, $"{ImagesName}/{filename}", Model.DocumentLimits.MaxAssetBytes);
                    var bytes = File.ReadAllBytes(file);
                    var headerOfAsset = PngCodec.ReadHeader(bytes);
                    if (!headerOfAsset.IsEightBitOrLess) throw new ProjectException(ProjectError.MissingImage);
                    if (isMask) CheckSize(headerOfAsset.Width, headerOfAsset.Height, ref maskPixels);
                    else CheckSize(headerOfAsset.Width, headerOfAsset.Height, ref pixels);

                    var bitmap = isMask ? PngCodec.DecodeMask(headerOfAsset, bytes) : PngCodec.DecodeImage(headerOfAsset, bytes);
                    var asset = new Model.ImportedImage(bitmap, Model.Bitmaps.Thumbnail(bitmap), layer.Name);
                    if (isMask) snapshot.Masks[layer.ID] = asset;
                    else snapshot.Images[layer.ID] = asset;
                }
            }
            long embeddedBytes = 0;
            foreach (var layer in manifest.Layers.Where(l => l.SmartObjectFile is not null))
            {
                var bytes = File.ReadAllBytes(ResolveAsset(package, $"{ImagesName}/{layer.SmartObjectFile}", Model.SmartObjectData.MaxBytes));
                embeddedBytes += bytes.Length;
                if (embeddedBytes > Model.SmartObjectData.MaxBytes) throw new ProjectException(ProjectError.TooLarge);
                snapshot.SmartObjects[layer.ID] = new Model.SmartObjectData(layer.SmartObjectID!.Value, bytes);
            }
            foreach (var channel in manifest.Channels ?? [])
            {
                var file = ResolveAsset(package, $"{ImagesName}/{channel.ImageFile}", Model.DocumentLimits.MaxAssetBytes);
                var bytes = File.ReadAllBytes(file);
                var channelHeader = PngCodec.ReadHeader(bytes);
                if (!channelHeader.IsEightBitOrLess || channelHeader.Width != manifest.Width || channelHeader.Height != manifest.Height)
                    throw new ProjectException(ProjectError.Invalid);
                CheckSize(channelHeader.Width, channelHeader.Height, ref maskPixels);
                var bitmap = PngCodec.DecodeMask(channelHeader, bytes);
                snapshot.Channels[channel.ID] = new Model.ImportedImage(bitmap, Model.Bitmaps.Thumbnail(bitmap), channel.Name);
            }
        }
        catch
        {
            snapshot.Dispose();
            throw;
        }
        return snapshot;
    }

    /// <summary>
    /// A file inside the package, with the checks the Swift build makes: regular, not a link, no larger
    /// than `maximumBytes`, and rooted in the package once `..` is resolved.
    /// </summary>
    private static string ResolveAsset(string package, string relative, int maximumBytes)
    {
        var root = package.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var file = Path.GetFullPath(Path.Combine(root, relative));
        if (!file.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new ProjectException(ProjectError.Invalid);
        var info = new FileInfo(file);
        // Existence first: walking the path for links on a file that is not there throws out of the API.
        if (!info.Exists || info.Attributes.HasFlag(FileAttributes.Directory)) throw new ProjectException(ProjectError.MissingImage);
        if (IsLink(package, file)) throw new ProjectException(ProjectError.Invalid);
        if (info.Length > maximumBytes) throw new ProjectException(ProjectError.TooLarge);
        return file;
    }

    /// <summary>Whether any directory on the way to `file`, or the file itself, is a symbolic link.</summary>
    private static bool IsLink(string package, string file)
    {
        var root = Path.GetFullPath(package);
        var relative = Path.GetRelativePath(root, file);
        var current = root;
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) return true;
        }
        return false;
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover staging directory is harmless; the next save picks a new name.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
