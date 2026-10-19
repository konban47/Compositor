using Compositor.Core.Format;
using Compositor.Core.Model;

namespace Compositor.Core.IO;

/// <summary>A manifest with the pixels its layers and masks point at.</summary>
public sealed class ProjectSnapshot : IDisposable
{
    public ProjectSnapshot(ProjectManifest manifest)
    {
        Manifest = manifest;
    }

    public ProjectSnapshot(ProjectManifest manifest, Dictionary<Guid, ImportedImage> images, Dictionary<Guid, ImportedImage> masks)
    {
        Manifest = manifest;
        Images = images;
        Masks = masks;
    }

    public ProjectManifest Manifest { get; }

    public Dictionary<Guid, ImportedImage> Images { get; } = [];

    public Dictionary<Guid, Model.SmartObjectData> SmartObjects { get; } = [];

    public Dictionary<Guid, ImportedImage> Masks { get; } = [];
    public Dictionary<Guid, ImportedImage> Channels { get; } = [];

    /// <summary>The runtime mask of a record, or nil when the layer has none or its pixels are missing.</summary>
    public Model.LayerMask? MaskFor(ProjectLayerRecord layer)
    {
        if (layer.MaskFile is null || !Masks.TryGetValue(layer.ID, out var asset)) return null;
        return new Model.LayerMask(asset, layer.MaskEnabled ?? true,
            layer.MaskPlacement?.ToRuntime(), layer.MaskLinked ?? true, layer.MaskDensity ?? 1, layer.MaskFeather ?? 0, layer.MaskVectorPath, layer.MaskVectorEvenOdd ?? false);
    }

    /// <summary>The rows the layers panel draws, and the layers a renderer draws, in document order.</summary>
    public List<ImageLayer> RuntimeLayers()
    {
        var layers = new List<ImageLayer>(Manifest.Layers.Count);
        foreach (var record in Manifest.Layers)
        {
            var layer = new ImageLayer(record.ID, Images.GetValueOrDefault(record.ID), record.Transform.ToRuntime(),
                record.Name)
            {
                IsVisible = record.IsVisible,
                Locks = (LayerLocks)(record.Locks ?? 0),
                FillOpacity = record.FillOpacity ?? 1,
                LinkID = record.LinkID,
                ParentID = record.ParentID,
                IsGroup = record.IsGroup ?? false,
                Opacity = record.Opacity ?? 1,
                BlendMode = record.BlendMode ?? LayerBlendMode.Normal,
                MaskSourceID = record.MaskSourceID,
                Mask = MaskFor(record),
                Adjustment = record.Adjustment,
                Effects = record.Effects,
                Blending = record.Blending, Label = record.Label ?? LayerLabel.None,
                Container = record.Container ?? LayerContainer.Group, ArtboardBackground = record.ArtboardBackground ?? 0xffffffff, SmartObject = SmartObjects.GetValueOrDefault(record.ID),
                Shape = record.Shape is { } shape && Images.TryGetValue(record.ID, out var pixels)
                    ? new LayerShape(shape, pixels.Image) : null,
                Text = record.Text is { } text && Images.TryGetValue(record.ID, out var textPixels)
                    ? new LayerText(text, textPixels.Image) : null,
            };
            layers.Add(layer);
        }
        return layers;
    }

    /// <summary>The document this snapshot holds, ready to edit. Takes a copy of the pixel references.</summary>
    public CanvasDocument ToDocument()
    {
        var document = new CanvasDocument(Manifest.DocumentID, Manifest.Width, Manifest.Height, Manifest.Resolution ?? 72);
        document.Image = Manifest.ImageSettings ?? new();
        document.Variables.AddRange(Manifest.Variables ?? []); document.DataSets.AddRange(Manifest.DataSets ?? []);
        document.MeasurementScale = Manifest.MeasurementScale ?? new(); document.Measurements.AddRange(Manifest.Measurements ?? []);
        if (Manifest.MeasurementFields is { } fields) document.MeasurementFields = fields;
        document.Layers.AddRange(RuntimeLayers());
        document.Guides.AddRange(Manifest.Guides ?? []);
        document.Marks.AddRange(Manifest.Marks ?? []);
        document.WorkPath = Manifest.WorkPath;
        foreach (var channel in Manifest.Channels ?? [])
            document.Channels.Add(new AlphaChannel(channel.ID, channel.Name, Channels[channel.ID]));
        return document;
    }

    /// <summary>
    /// The snapshot that saves a document as it stands. It shares the document's pixels, so the document
    /// keeps owning them: save this and drop it, do not dispose it while the document is alive.
    /// </summary>
    public static ProjectSnapshot FromDocument(CanvasDocument document)
    {
        var channels = document.Channels.Where(c => !c.IsTemporary).ToArray();
        var defaultMeasurementFields = document.MeasurementFields.SequenceEqual(new[] { "Area", "Perimeter", "Width", "Height", "Length", "Angle", "Count", "Mean" });
        var manifest = new ProjectManifest
        {
            Version = document.WorkPath is not null ? 24 : document.Layers.Any(l => l.LiveShape?.Smoothing is not null) || document.Marks.Any(m => m.HasSliceOptions) ? 23 : document.Layers.Any(l => l.Container == LayerContainer.Artboard && l.ArtboardBackground != 0xffffffff) ? 22 : document.Guides.Any(g => g.ArtboardID is not null) ? 21 : document.Image.CmykProfile is not null || document.Layers.Any(l => l.Label > LayerLabel.Gray || l.Mask is { VectorPath: not null, VectorEvenOdd: true } || l.LiveText?.StyleRuns is not null || l.LiveText?.Geometry is not null || l.LiveText?.Layout is { BottomLeading: true } or { TateChuYoko: true } or { Smoothing: > TextSmoothing.Smooth }) ? 20 : document.Layers.Any(l => l.Adjustment?.Extended is not null) ? 19 : !document.Image.IsDefault || document.Variables.Count > 0 || document.DataSets.Count > 0
                || document.MeasurementScale != new MeasurementScale() || document.Measurements.Count > 0
                || document.Layers.Any(l => l.Asset?.Deep is not null || l.LiveText?.Layout is not null) || !defaultMeasurementFields ? 18
                : document.Marks.Count > 0 ? 17 : document.Layers.Any(layer => layer.LiveText?.Vertical is not null || layer.LiveShape is { Kind: ShapeKind.Path, LineWidth: not null }) ? 16
                : document.Layers.Any(layer => layer.Blending is not null || layer.Effects?.Items is not null
                    || layer.Effects?.Enabled == false || layer.IsGroup && (layer.Effects is not null || layer.BlendMode != LayerBlendMode.Normal)
                    || layer.Label != LayerLabel.None || layer.Container != LayerContainer.Group || layer.SmartObject is not null) ? 15
                : document.Layers.Any(layer => layer.LiveText?.HasAdvancedTypography == true
                    || layer.LiveShape?.Kind == Format.ShapeKind.Path) ? 14
                : document.Layers.Any(layer => layer.LiveText?.HasTypography == true || layer.Mask is { } mask && (mask.Density != 1 || mask.Feather != 0 || mask.VectorPath is not null)) ? 13
                : channels.Length > 0 || document.Layers.Any(layer => layer.Locks != LayerLocks.None
                || layer.FillOpacity != 1 || layer.LinkID is not null) ? 12 : 11,
            DocumentID = document.ID,
            Width = document.Width,
            Height = document.Height,
            Resolution = document.Resolution,
            ImageSettings = document.Image.IsDefault ? null : document.Image,
            Variables = document.Variables.Count == 0 ? null : [.. document.Variables],
            DataSets = document.DataSets.Count == 0 ? null : [.. document.DataSets],
            MeasurementScale = document.MeasurementScale == new MeasurementScale() ? null : document.MeasurementScale,
            Measurements = document.Measurements.Count == 0 ? null : [.. document.Measurements],
            MeasurementFields = defaultMeasurementFields ? null : document.MeasurementFields,
            ActiveLayerID = document.Layers.Count > 0 ? document.Layers[^1].ID : null,
            Layers = [.. document.Layers.Select(CanvasDocument.Record)],
            Guides = document.Guides.Count > 0 ? [.. document.Guides] : null,
            Marks = document.Marks.Count > 0 ? [.. document.Marks] : null,
            WorkPath = document.WorkPath,
            Channels = channels.Length == 0 ? null : channels.Select(channel => new ProjectChannelRecord
                { ID = channel.ID, Name = channel.Name, ImageFile = ProjectChannelRecord.FileName(channel.ID) }).ToList(),
        };
        var snapshot = new ProjectSnapshot(manifest);
        foreach (var channel in channels) snapshot.Channels[channel.ID] = channel.Asset;
        foreach (var layer in document.Layers)
        {
            if (layer.SmartObject is { } smart) snapshot.SmartObjects[layer.ID] = smart;
            if (layer.Asset is { } asset) snapshot.Images[layer.ID] = asset;
            if (layer.Mask is { } mask) snapshot.Masks[layer.ID] = mask.Asset;
        }
        return snapshot;
    }

    public void Dispose()
    {
        foreach (var image in Images.Values) image.Dispose();
        foreach (var mask in Masks.Values) mask.Dispose();
        foreach (var channel in Channels.Values) channel.Dispose();
        Channels.Clear();
        Images.Clear();
        Masks.Clear();
    }
}
