using SkiaSharp;

namespace Compositor.Core.Model;

/// <summary>A layer made with the Shape tool: an ordinary raster plus the style that drew it.</summary>
public sealed class LayerShape
{
    public LayerShape(Format.LayerShapeStyle style, SKBitmap image)
    {
        Style = style;
        Image = image;
    }

    public Format.LayerShapeStyle Style { get; }
    public SKBitmap Image { get; }
}

/// <summary>A layer made with the Type tool: an ordinary raster plus the text metadata that drew it.</summary>
public sealed class LayerText
{
    public LayerText(Format.LayerTextStyle style, SKBitmap image)
    {
        Style = style;
        Image = image;
    }

    public Format.LayerTextStyle Style { get; }
    public SKBitmap Image { get; }
}

public sealed class ImageLayer : IDisposable
{
    public ImageLayer(Guid id, ImportedImage? asset, LayerTransform transform, string name)
    {
        ID = id;
        Asset = asset;
        Transform = transform;
        Name = name;
    }

    public Guid ID { get; init; }
    private ImportedImage? _asset;
    public GifAnimation? Gif { get; set; }
    public int GifFrameIndex { get; private set; }
    public ImportedImage? Asset
    {
        get => _asset;
        set
        {
            // Validate before publishing: a refused geometry edit must preserve every frame.
            var animation = Gif is { } gif && value is not null ? gif.Edit(GifFrameIndex, value) : null;
            _asset = value; Gif = animation;
        }
    }
    /// <summary>Navigation uses a fresh display asset without turning a decoded frame into an edit.</summary>
    public void ShowGifFrame(int index, ImportedImage? decoded = null)
    {
        if (Gif is not { } gif) return;
        var frame = decoded ?? gif.Decode(index, Name); _asset = frame; GifFrameIndex = index;
    }
    public LayerTransform Transform { get; set; }
    public string Name { get; set; }
    public bool IsVisible { get; set; } = true;
    public LayerLocks Locks { get; set; }
    public double FillOpacity { get; set; } = 1;
    public Guid? LinkID { get; set; }
    public Guid? ParentID { get; set; }
    public bool IsGroup { get; set; }
    public double Opacity { get; set; } = 1;
    public Format.LayerBlendMode BlendMode { get; set; } = Format.LayerBlendMode.Normal;
    public Guid? MaskSourceID { get; set; }
    public LayerMask? Mask { get; set; }
    public Format.LayerAdjustment? Adjustment { get; set; }
    public LayerShape? Shape { get; set; }
    public Document.LayerGradient? Gradient { get; set; }
    public Document.LiveGradientSettings? LiveGradient => Gradient is { } g && Asset is { } a && ReferenceEquals(g.Image, a.Image) ? g.Settings : null;
    public Format.LayerEffects? Effects { get; set; }
    public LayerText? Text { get; set; }
    public Format.LayerBlending? Blending { get; set; }
    public Format.LayerLabel Label { get; set; }
    public Format.LayerContainer Container { get; set; }
    public Format.FrameStyle? FrameStyle { get; set; }
    public uint ArtboardBackground { get; set; } = 0xffffffff;
    public SmartObjectData? SmartObject { get; set; }

    /// <summary>The shape this layer still is: null once its pixels were edited some other way.</summary>
    public Format.LayerShapeStyle? LiveShape =>
        Shape is { } shape && Asset is { } asset && ReferenceEquals(asset.Image, shape.Image) ? shape.Style : null;

    /// <summary>The text this layer still is: null once its pixels were edited some other way.</summary>
    public Format.LayerTextStyle? LiveText =>
        Text is { } text && Asset is { } asset && ReferenceEquals(asset.Image, text.Image) ? text.Style : null;

    /// <summary>Where the mask's pixels sit on the document: its own placement, else the layer's.</summary>
    public LayerTransform MaskTransform => Mask?.Placement ?? Transform;

    /// <summary>
    /// A copy holding the same values and the same pixels. The pixels are shared, not copied — that is what
    /// makes a history snapshot cheap — so a clone must never dispose them.
    /// </summary>
    public ImageLayer Clone() => Copy(ID, Name, ParentID, MaskSourceID);

    /// <summary>
    /// The same layer under a new id, name, parent and clip source, still sharing its pixels. The whole
    /// list is given so a copy of a folder can be made in one pass with every reference remapped.
    /// </summary>
    public ImageLayer Copy(Guid id, string name, Guid? parentID, Guid? maskSourceID) =>
        new(id, Asset, Transform, name)
        {
            Gif = Gif, GifFrameIndex = GifFrameIndex,
            IsVisible = IsVisible,
            Locks = Locks,
            FillOpacity = FillOpacity,
            LinkID = LinkID,
            ParentID = parentID,
            IsGroup = IsGroup,
            Opacity = Opacity,
            BlendMode = BlendMode,
            MaskSourceID = maskSourceID,
            Mask = Mask?.Clone(),
            Adjustment = Adjustment,
            Shape = Shape,
            Gradient = Gradient,
            Effects = Effects,
            Text = Text,
            Blending = Blending, Label = Label, Container = Container, ArtboardBackground = ArtboardBackground, FrameStyle = FrameStyle, SmartObject = SmartObject,
        };

    /// <summary>
    /// Whether two layers hold the same thing, with pixels compared by identity. This is how a snapshot and
    /// the live document are compared to tell a real edit from selecting or navigating.
    /// </summary>
    public bool SameAs(ImageLayer other) =>
        ID == other.ID && Name == other.Name && IsVisible == other.IsVisible && Transform == other.Transform
        && Locks == other.Locks && FillOpacity == other.FillOpacity && LinkID == other.LinkID
        && ParentID == other.ParentID && IsGroup == other.IsGroup && Opacity == other.Opacity
        && BlendMode == other.BlendMode && MaskSourceID == other.MaskSourceID
        && ReferenceEquals(Asset?.Image, other.Asset?.Image) && ReferenceEquals(Gif, other.Gif)
        && SameMask(Mask, other.Mask)
        && ReferenceEquals(Adjustment, other.Adjustment) && ReferenceEquals(Shape, other.Shape)
        && FrameStyle == other.FrameStyle && Label == other.Label && Container == other.Container && ArtboardBackground == other.ArtboardBackground && ReferenceEquals(Blending, other.Blending) && ReferenceEquals(SmartObject, other.SmartObject)
        && ReferenceEquals(Effects, other.Effects) && ReferenceEquals(Text, other.Text) && ReferenceEquals(Gradient, other.Gradient);

    private static bool SameMask(LayerMask? left, LayerMask? right) =>
        left is null ? right is null
        : right is not null && ReferenceEquals(left.Asset.Image, right.Asset.Image)
            && left.IsEnabled == right.IsEnabled && left.IsLinked == right.IsLinked
            && left.Placement == right.Placement && left.Density == right.Density && left.Feather == right.Feather
            && left.VectorPath == right.VectorPath && left.VectorEvenOdd == right.VectorEvenOdd;

    public double EffectiveOpacity(IReadOnlyDictionary<Guid, ImageLayer> byID) =>
        LayerOpacity.Effective(this, byID);

    public void Dispose()
    {
        Asset?.Dispose();
        foreach (var frame in Gif?.Edited.Values ?? []) frame.Dispose();
        Mask?.Dispose();
    }
}

/// <summary>
/// A folder's opacity multiplies into everything inside it: a layer at 50% in a folder at 50% shows at
/// 25%, while the layer itself still reads 50%. Folders are pass-through, so a folder's opacity is
/// applied to each of those layers rather than to the folder as a whole.
/// </summary>
public static class LayerOpacity
{
    public static double Effective(ImageLayer layer, IReadOnlyDictionary<Guid, ImageLayer> byID)
    {
        var opacity = layer.Opacity;
        var parent = layer.ParentID;
        for (var depth = 0; parent is { } id && depth < 64; depth++)
        {
            if (!byID.TryGetValue(id, out var folder)) break;
            opacity *= folder.Opacity * folder.FillOpacity;
            parent = folder.ParentID;
        }
        return opacity;
    }

    public static double Effective(Format.ProjectLayerRecord layer, IReadOnlyDictionary<Guid, Format.ProjectLayerRecord> byID)
    {
        var opacity = layer.Opacity ?? 1;
        var parent = layer.ParentID;
        for (var depth = 0; parent is { } id && depth < 64; depth++)
        {
            if (!byID.TryGetValue(id, out var folder)) break;
            opacity *= (folder.Opacity ?? 1) * (folder.FillOpacity ?? 1);
            parent = folder.ParentID;
        }
        return opacity;
    }
}
