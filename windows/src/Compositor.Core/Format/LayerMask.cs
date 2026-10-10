namespace Compositor.Core.Format;

/// <summary>
/// The mask metadata a layer record carries: <c>maskFile</c>, <c>maskEnabled</c>, <c>maskPlacement</c> and
/// <c>maskLinked</c>. The record stays flat — this is a view over those four fields, with the rules that
/// tie them together (see <c>ProjectStore.validate</c>).
/// </summary>
public sealed class LayerMask
{
    public string? MaskFile { get; set; }
    public bool? MaskEnabled { get; set; }
    public LayerTransform? MaskPlacement { get; set; }
    public bool? MaskLinked { get; set; }
    public double? MaskDensity { get; set; }
    public double? MaskFeather { get; set; }
    public string? MaskVectorPath { get; set; }

    public static LayerMask FromRecord(ProjectLayerRecord layer) => new()
    {
        MaskFile = layer.MaskFile,
        MaskEnabled = layer.MaskEnabled,
        MaskPlacement = layer.MaskPlacement,
        MaskLinked = layer.MaskLinked,
        MaskDensity = layer.MaskDensity, MaskFeather = layer.MaskFeather, MaskVectorPath = layer.MaskVectorPath,
    };

    public void ApplyTo(ProjectLayerRecord layer)
    {
        layer.MaskFile = MaskFile;
        layer.MaskEnabled = MaskEnabled;
        layer.MaskPlacement = MaskPlacement;
        layer.MaskLinked = MaskLinked;
        layer.MaskDensity = MaskDensity; layer.MaskFeather = MaskFeather; layer.MaskVectorPath = MaskVectorPath;
    }

    /// <summary>The only filename a layer's mask may use.</summary>
    public static string ExpectedFile(Guid layerID) => $"{layerID:D}".ToUpperInvariant() + ".mask.png";

    /// <summary>The only filename a layer's image may use.</summary>
    public static string ExpectedImageFile(Guid layerID) => $"{layerID:D}".ToUpperInvariant() + ".png";

    /// <summary>An enabled mask defaults to true when one exists.</summary>
    public bool IsEnabled => MaskEnabled ?? true;

    /// <summary>Nil means linked.</summary>
    public bool IsLinked => MaskLinked ?? true;

    /// <summary>
    /// Layer masks arrived in version 4, folder masks in version 6. `maskEnabled` needs a file to belong
    /// to, and a placement needs a file too and must be a valid transform.
    /// </summary>
    public bool IsValid(int version, bool isGroup, Guid layerID)
    {
        if (MaskFile is not null)
        {
            var earliest = isGroup ? 6 : 4;
            if (version < earliest) return false;
            if (MaskFile != ExpectedFile(layerID)) return false;
        }
        if (MaskEnabled is not null && MaskFile is null) return false;
        if (MaskPlacement is not null && (MaskFile is null || !MaskPlacement.ToRuntime().IsValid)) return false;
        if (MaskDensity is not null || MaskFeather is not null || MaskVectorPath is not null)
        {
            if (version < 13 || MaskFile is null) return false;
            if (MaskDensity is { } density && (!double.IsFinite(density) || density < 0 || density > 1)) return false;
            if (MaskFeather is { } feather && (!double.IsFinite(feather) || feather < 0 || feather > 1000)) return false;
            if (MaskVectorPath is { } path)
            {
                if (path.Length == 0 || path.Length > 1_000_000) return false;
                using var parsed = SkiaSharp.SKPath.ParseSvgPathData(path);
                if (parsed is null) return false;
            }
        }
        return true;
    }

    /// <summary>Mask filenames live under <c>images/</c>, next to the layer images.</summary>
    public const string Directory = "images";
}
