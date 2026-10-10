namespace Compositor.Core.Model;

public static class DocumentCopies
{
    /// <summary>A new document owns new pixels, so either tab can close independently.</summary>
    public static CanvasDocument Independent(CanvasDocument source)
    {
        var copy = new CanvasDocument(Guid.NewGuid(), source.Width, source.Height, source.Resolution);
        try
        {
            foreach (var original in source.Layers)
            {
                var layer = original.Clone();
                layer.Asset = original.Asset is { } asset ? ImportedImage.Create(asset.Image.Copy(), layer.Name) : null;
                if (layer.Asset is { } pixels)
                {
                    layer.Text = original.LiveText is { } text ? new LayerText(text, pixels.Image) : null;
                    layer.Shape = original.LiveShape is { } shape ? new LayerShape(shape, pixels.Image) : null;
                }
                if (original.Mask is { } mask)
                {
                    layer.Mask = mask.Replacing(LayerMask.AssetFrom(mask.Asset.Image.Copy()).Asset);
                    layer.Mask.VectorPath = mask.VectorPath;
                }
                copy.Layers.Add(layer);
            }
            foreach (var channel in source.Channels)
                copy.Channels.Add(new AlphaChannel(channel.ID, channel.Name, LayerMask.AssetFrom(channel.Asset.Image.Copy()).Asset));
            copy.Guides.AddRange(source.Guides.Select(guide => new Format.CanvasGuide { ID = guide.ID, Axis = guide.Axis, Position = guide.Position }));
            copy.Selection = source.Selection; return copy;
        }
        catch { copy.Dispose(); throw; }
    }
}
