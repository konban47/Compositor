using System.Text.Json;
using System.Text.Json.Serialization;
using Compositor.Core.Format;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Document;

/// <summary>One smart filter: a Filter menu filter with its amounts, its own blending (mode and opacity, Photoshop's Edit
/// Smart Filter Blending Options) and whether it is on.</summary>
public sealed class SmartFilter
{
    public FilterKind Kind { get; set; }
    public FilterSettings Settings { get; set; } = new();
    public bool Enabled { get; set; } = true;
    public LayerBlendMode Mode { get; set; } = LayerBlendMode.Normal;
    public double Opacity { get; set; } = 1;
    [JsonIgnore] public bool IsValid => Enum.IsDefined(Kind) && Settings is not null && Enum.IsDefined(Mode) && double.IsFinite(Opacity) && Opacity is >= 0 and <= 1
        && Kind is not (FilterKind.RimLight or FilterKind.VolumetricLight);
}

/// <summary>
/// A smart object's smart filters (Windows extension 36), in the order they apply, the first applied first: the panel
/// lists them the other way up, the newest on top. The stack can be switched off as a whole, and carries the filter mask,
/// a gray PNG at the size of the smart object's picture (white where the filters show), absent when the mask was deleted.
/// </summary>
public sealed class SmartFilterStack
{
    public bool Enabled { get; set; } = true;
    public List<SmartFilter> Items { get; set; } = [];
    /// <summary>Whether the stack has a filter mask; false after Delete Filter Mask.</summary>
    public bool HasMask { get; set; } = true;
    /// <summary>Disable Filter Mask: the mask kept but not used.</summary>
    public bool MaskEnabled { get; set; } = true;
    /// <summary>The mask as a base64 gray PNG; null is all white.</summary>
    public string? Mask { get; set; }
    [JsonIgnore] public bool IsValid => Items is { Count: <= 64 } && Items.All(i => i is { IsValid: true }) && (Mask is null || Mask.Length <= 64 * 1024 * 1024);

    /// <summary>A copy that edits can change while the history keeps the original.</summary>
    public SmartFilterStack Copy() =>
        JsonSerializer.Deserialize<SmartFilterStack>(JsonSerializer.Serialize(this, ManifestJson.Options), ManifestJson.Options)!;
}

/// <summary>
/// The smart filter edits: adding a filter to a smart object, switching filters and the stack on and off, their blending,
/// deleting and clearing them and the filter mask, and drawing the result. Each draws the smart object's contents again and
/// runs the enabled filters over them in order, so the layer's picture is always the contents seen through the filters.
/// </summary>
public static class SmartFilterEdits
{
    /// <summary>The smart object's contents as drawn, before any filter.</summary>
    public static SKBitmap Contents(SmartObjectData smart)
    {
        using var contents = smart.Open().ToDocument();
        return SmartObjectStacks.Render(contents, smart.StackMode);
    }

    /// <summary>The layer's picture: <paramref name="contents"/> with the layer's enabled smart filters applied, held to the filter mask.</summary>
    public static ImportedImage Bake(ImageLayer layer, SKBitmap contents) => Bake(layer.SmartFilters, contents, layer.Name);

    /// <summary>The same for a stack not yet on the layer, as a dialog's preview shows it.</summary>
    public static ImportedImage Bake(SmartFilterStack? stack, SKBitmap contents, string name)
    {
        if (stack is not { Enabled: true } || stack.Items.All(i => !i.Enabled)) return ImportedImage.Create(contents.Copy(), name);
        var current = contents.Copy();
        foreach (var filter in stack.Items.Where(i => i.Enabled))
        {
            var next = Filtered(current, filter);
            if (filter.Mode != LayerBlendMode.Normal || filter.Opacity < 1) { var blended = Blend(current, next, filter.Mode, filter.Opacity); next.Dispose(); next = blended; }
            current.Dispose(); current = next;
        }
        if (stack is { HasMask: true, MaskEnabled: true } && stack.Mask is { } encoded && MaskImage(encoded, contents.Width, contents.Height) is { } mask)
        {
            // Where the mask is black the contents show unfiltered.
            using (mask) for (var y = 0; y < current.Height; y++) for (var x = 0; x < current.Width; x++)
            {
                var amount = mask.GetPixel(x, y).Red / 255.0; if (amount >= 1) continue;
                var a = contents.GetPixel(x, y); var b = current.GetPixel(x, y);
                byte Mix(byte from, byte to) => (byte)Math.Round(from + (to - from) * amount);
                current.SetPixel(x, y, new SKColor(Mix(a.Red, b.Red), Mix(a.Green, b.Green), Mix(a.Blue, b.Blue), Mix(a.Alpha, b.Alpha)));
            }
        }
        return ImportedImage.Create(current, name);
    }

    /// <summary>One filter run over a picture, on a document of its own the picture's size, as the Filter menu runs it on a layer.</summary>
    private static SKBitmap Filtered(SKBitmap picture, SmartFilter filter)
    {
        using var scratch = new CanvasDocument(Guid.NewGuid(), picture.Width, picture.Height);
        var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(picture.Copy(), "Smart Filter"), new LayerTransform(0, 0, picture.Width, picture.Height), "Smart Filter");
        scratch.Layers.Add(layer);
        FilterEdits.Apply(scratch, layer.ID, filter.Kind, filter.Settings);
        return scratch.Layers[0].Asset!.Image.Copy();
    }

    /// <summary>The filtered picture over the one before it, in the filter's mode and opacity.</summary>
    private static SKBitmap Blend(SKBitmap below, SKBitmap above, LayerBlendMode mode, double opacity)
    {
        using var scratch = new CanvasDocument(Guid.NewGuid(), below.Width, below.Height);
        scratch.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(below.Copy(), "Below"), new LayerTransform(0, 0, below.Width, below.Height), "Below"));
        scratch.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(above.Copy(), "Above"), new LayerTransform(0, 0, above.Width, above.Height), "Above") { BlendMode = mode, Opacity = opacity });
        return DocumentRenderer.Render(scratch);
    }

    private static SKBitmap? MaskImage(string encoded, int width, int height)
    {
        try
        {
            var decoded = SKBitmap.Decode(Convert.FromBase64String(encoded));
            if (decoded is null) return null;
            if (decoded.Width == width && decoded.Height == height) return decoded;
            using (decoded) return decoded.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear));
        }
        catch (FormatException) { return null; }
    }

    /// <summary>Draws a smart object layer again from its contents through its filters.</summary>
    public static bool Rebake(CanvasDocument document, Guid layerID)
    {
        if (document.Layers.FirstOrDefault(l => l.ID == layerID) is not { SmartObject: { } smart } layer) return false;
        using var contents = Contents(smart);
        layer.Asset = Bake(layer, contents);
        return true;
    }

    private static ImageLayer? Smart(CanvasDocument document, Guid layerID) =>
        document.Layers.FirstOrDefault(l => l.ID == layerID) is { SmartObject: not null } layer && LayerWorkflow.Editable(document, [layerID]) ? layer : null;

    /// <summary>Adds a filter on top of the stack. A selection becomes the filter mask when the stack is new, as in Photoshop.</summary>
    public static bool Add(CanvasDocument document, Guid layerID, FilterKind kind, FilterSettings settings)
    {
        if (Smart(document, layerID) is not { } layer) return false;
        var filter = new SmartFilter { Kind = kind, Settings = Copy(settings) };
        if (!filter.IsValid) return false;
        var stack = layer.SmartFilters?.Copy() ?? new SmartFilterStack();
        if (stack.Items.Count >= 64) return false;
        if (layer.SmartFilters is null && document.Selection.Path is not null && layer.Asset is { } asset)
            stack.Mask = SelectionMask(document, layer, asset.Width, asset.Height);
        stack.Items.Add(filter);
        layer.SmartFilters = stack;
        return Rebake(document, layerID);
    }

    /// <summary>Replaces one filter's amounts (Edit Smart Filter…), by its place in the application order.</summary>
    public static bool Change(CanvasDocument document, Guid layerID, int index, FilterSettings settings) =>
        Modify(document, layerID, stack => { if (index < 0 || index >= stack.Items.Count) return false; stack.Items[index].Settings = Copy(settings); return true; });

    /// <summary>Edit Smart Filter Blending Options: the filter's mode and opacity.</summary>
    public static bool SetBlending(CanvasDocument document, Guid layerID, int index, LayerBlendMode mode, double opacity) =>
        Modify(document, layerID, stack =>
        {
            if (index < 0 || index >= stack.Items.Count || !Enum.IsDefined(mode) || !double.IsFinite(opacity) || opacity is < 0 or > 1) return false;
            if (stack.Items[index].Mode == mode && stack.Items[index].Opacity == opacity) return false;
            stack.Items[index].Mode = mode; stack.Items[index].Opacity = opacity; return true;
        });

    /// <summary>One filter on or off (its eye, or Disable/Enable Smart Filter).</summary>
    public static bool SetEnabled(CanvasDocument document, Guid layerID, int index, bool enabled) =>
        Modify(document, layerID, stack => { if (index < 0 || index >= stack.Items.Count || stack.Items[index].Enabled == enabled) return false; stack.Items[index].Enabled = enabled; return true; });

    /// <summary>Every filter on or off at once (the Smart Filters row's eye, or Disable/Enable Smart Filters).</summary>
    public static bool SetStackEnabled(CanvasDocument document, Guid layerID, bool enabled) =>
        Modify(document, layerID, stack => { if (stack.Enabled == enabled) return false; stack.Enabled = enabled; return true; });

    /// <summary>Delete Smart Filter: one filter taken off; the last one takes the stack with it.</summary>
    public static bool Delete(CanvasDocument document, Guid layerID, int index)
    {
        if (Smart(document, layerID) is not { SmartFilters: { } current } layer || index < 0 || index >= current.Items.Count) return false;
        var stack = current.Copy(); stack.Items.RemoveAt(index);
        layer.SmartFilters = stack.Items.Count == 0 ? null : stack;
        return Rebake(document, layerID);
    }

    /// <summary>Clear Smart Filters: every filter and the mask taken off.</summary>
    public static bool Clear(CanvasDocument document, Guid layerID)
    {
        if (Smart(document, layerID) is not { SmartFilters: not null } layer) return false;
        layer.SmartFilters = null;
        return Rebake(document, layerID);
    }

    /// <summary>Delete Filter Mask, and Add Filter Mask (all white, or the selection when there is one).</summary>
    public static bool SetMask(CanvasDocument document, Guid layerID, bool present) =>
        Modify(document, layerID, stack =>
        {
            if (stack.HasMask == present) return false;
            stack.HasMask = present;
            var layer = document.Layers.First(l => l.ID == layerID);
            stack.Mask = present && document.Selection.Path is not null && layer.Asset is { } asset ? SelectionMask(document, layer, asset.Width, asset.Height) : null;
            return true;
        });

    /// <summary>Disable / Enable Filter Mask.</summary>
    public static bool SetMaskEnabled(CanvasDocument document, Guid layerID, bool enabled) =>
        Modify(document, layerID, stack => { if (!stack.HasMask || stack.MaskEnabled == enabled) return false; stack.MaskEnabled = enabled; return true; });

    /// <summary>Moves a filter in the application order (dragging it in the panel).</summary>
    public static bool Move(CanvasDocument document, Guid layerID, int from, int to) =>
        Modify(document, layerID, stack =>
        {
            if (from == to || from < 0 || to < 0 || from >= stack.Items.Count || to >= stack.Items.Count) return false;
            var item = stack.Items[from]; stack.Items.RemoveAt(from); stack.Items.Insert(to, item); return true;
        });

    private static bool Modify(CanvasDocument document, Guid layerID, Func<SmartFilterStack, bool> change)
    {
        if (Smart(document, layerID) is not { SmartFilters: { } current } layer) return false;
        var stack = current.Copy();
        if (!change(stack)) return false;
        layer.SmartFilters = stack;
        return Rebake(document, layerID);
    }

    public static FilterSettings Copy(FilterSettings settings) =>
        JsonSerializer.Deserialize<FilterSettings>(JsonSerializer.Serialize(settings, ManifestJson.Options), ManifestJson.Options)!;

    /// <summary>The selection as a gray mask in the smart object's own pixels.</summary>
    private static string? SelectionMask(CanvasDocument document, ImageLayer layer, int width, int height)
    {
        using var coverage = document.Selection.Coverage(SKRectI.Create(document.Width, document.Height));
        if (coverage is null) return null;
        using var mask = new SKBitmap(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque));
        var toDocument = BrushEdits.PixelToDocument(layer.Transform, width, height);
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var p = toDocument.MapPoint(x + .5f, y + .5f); var dx = (int)Math.Floor(p.X); var dy = (int)Math.Floor(p.Y);
            var value = dx < 0 || dy < 0 || dx >= coverage.Width || dy >= coverage.Height ? (byte)0 : coverage.GetPixelSpan()[dy * coverage.RowBytes + dx];
            mask.GetPixelSpan()[y * mask.RowBytes + x] = value;
        }
        using var data = mask.Encode(SKEncodedImageFormat.Png, 100);
        return Convert.ToBase64String(data.ToArray());
    }
}
