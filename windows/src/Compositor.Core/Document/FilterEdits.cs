using Compositor.Core.Format;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>Which of the filters that share one settings bag is being applied.</summary>
public enum FilterKind
{
    /// <summary>Softens everything by a Gaussian, fading at the layer's edge rather than stopping at it.</summary>
    GaussianBlur,

    /// <summary>Smears everything along a line, the way a moving camera or subject does.</summary>
    MotionBlur,

    /// <summary>Spreads the bright parts of the picture into a soft glow around them.</summary>
    BloomGlow,

    /// <summary>Speckles the picture with random brightness, as film grain or sensor noise.</summary>
    AddNoise,

    /// <summary>Darkens or lightens the edges of the frame, leaving the middle alone.</summary>
    Vignette,

    /// <summary>Local contrast: the fine detail against a blurred copy of itself.</summary>
    TonalContrast,

    /// <summary>Radial distortion: either direction straightens a lens.</summary>
    LensCorrection,
    Scanlines,
    ProgressiveBlur, RgbSplit, LensEffects, Sharpen,

    /// <summary>Colored lights behind the subject that catch its edges, and glow in the air around it.</summary>
    RimLight,

    /// <summary>Haze lit by a light placed on the image, with beams the subject and dark shapes cast shadows through.</summary>
    VolumetricLight,
}

/// <summary>
/// The filters that are not Camera Raw, as one bag of amounts — the Mac build keeps them in one struct too,
/// because only the numbers belonging to the chosen filter are ever read.
/// <para>
/// A class rather than a struct: several of these defaults are not zero (the vignette's shape, the tonal
/// amounts), and a record struct zeroed by <c>new</c> would turn "the filter's own defaults" into "nothing".
/// </para>
/// </summary>
public sealed class FilterSettings
{
    public double SharpenAmount { get; set; } = 100;
    public double SharpenRadius { get; set; } = 1;
    public LensEffectSettings Lens { get; set; } = new();
    public RimLightSettings RimLight { get; set; } = new();
    public VolumetricLightSettings Volumetric { get; set; } = new();
    public double RgbSplitDistance { get; set; } = 10;
    public double RgbSplitBlur { get; set; }
    public double RgbSplitAngle { get; set; }
    public bool RgbSplitRadial { get; set; }
    public double RgbSplitCenterX { get; set; } = 50;
    public double RgbSplitCenterY { get; set; } = 50;
    public double RgbSplitReach { get; set; } = 1000;
    public double ProgressiveFrom { get; set; }
    public double ProgressiveTo { get; set; } = 20;
    public double ProgressiveStartX { get; set; } = 50;
    public double ProgressiveStartY { get; set; } = 35;
    public double ProgressiveEndX { get; set; } = 50;
    public double ProgressiveEndY { get; set; } = 100;
    public bool ProgressiveRadial { get; set; }
    public bool ProgressiveInverted { get; set; }
    public ScanlineSettings Scanlines { get; set; } = new();
    /// <summary>How far the distortion is pushed at ±100, as a share of the corner's distance.</summary>
    public const double LensStrength = 0.35;

    // Gaussian blur
    /// <summary>Standard deviation in layer pixels, 0.1 to 250.</summary>
    public double BlurRadius { get; set; } = 5;
    public bool PreserveTransparency { get; set; }

    // Bloom
    /// <summary>0 to 100: how strongly the glow is laid over the picture.</summary>
    public double BloomAmount { get; set; } = 40;
    /// <summary>The glow's reach in layer pixels, 1 to 150.</summary>
    public double BloomRadius { get; set; } = 24;

    // Motion blur
    /// <summary>Direction in degrees, counterclockwise from horizontal as Photoshop measures it, −90 to 90.</summary>
    public double MotionAngle { get; set; }
    /// <summary>Streak length in layer pixels, 1 to 2000.</summary>
    public double MotionDistance { get; set; } = 10;

    // Add noise
    /// <summary>Photoshop's percentage, 0.1 to 400: uniform noise spans ±this share of half the range.</summary>
    public double NoiseAmount { get; set; } = 10;
    /// <summary>Gaussian (more speckled, a standard deviation of two thirds of that) rather than uniform.</summary>
    public bool NoiseGaussian { get; set; }
    /// <summary>The same amount on every channel, so the brightness changes and the colour does not.</summary>
    public bool NoiseMonochromatic { get; set; }

    // Vignette
    /// <summary>0 to 100: how far the edge colour is blended in.</summary>
    public double VignetteAmount { get; set; } = 35;
    /// <summary>What the edges are taken towards.</summary>
    public double VignetteRed { get; set; }
    public double VignetteGreen { get; set; }
    public double VignetteBlue { get; set; }
    public double VignetteMidpoint { get; set; } = 50;
    /// <summary>−100 to 100: 100 is a circle, lower is a rectangle.</summary>
    public double VignetteRoundness { get; set; } = 100;
    public double VignetteFeather { get; set; } = 60;
    /// <summary>0 to 100: how much bright pixels are protected while the edges darken.</summary>
    public double VignetteHighlights { get; set; } = 25;

    // Tonal contrast
    /// <summary>0 to 100: how much of the local detail is added back.</summary>
    public double TonalAmount { get; set; } = 50;
    /// <summary>The detail radius in layer pixels.</summary>
    public double TonalRadius { get; set; } = 16;
    public double TonalShadows { get; set; } = 40;
    public double TonalMidtones { get; set; } = 60;
    public double TonalHighlights { get; set; } = 30;

    // Lens correction
    public double Distortion { get; set; }

    /// <summary>
    /// Another object holding the same amounts, which is what a panel edits: the window remembers the amounts
    /// a filter was last used with, and a panel that writes into a copy leaves them alone until Apply.
    /// </summary>
    public FilterSettings Copy()
    {
        var copy = (FilterSettings)MemberwiseClone(); copy.Scanlines = Scanlines.Copy(); copy.Lens = Lens with { }; copy.RimLight = RimLight.Copy(); copy.Volumetric = Volumetric with { }; return copy;
    }

    /// <summary>Whether the amounts this filter reads are ones it may use.</summary>
    public bool IsValid(FilterKind kind) => kind switch
    {
        FilterKind.Sharpen => Within(SharpenAmount, 0, 500) && Within(SharpenRadius, .1, 100),
        FilterKind.LensEffects => Lens.IsValid,
        FilterKind.RimLight => RimLight.IsValid,
        FilterKind.VolumetricLight => Volumetric.IsValid,
        FilterKind.RgbSplit => Within(RgbSplitDistance, 0, 200) && Within(RgbSplitBlur, 0, 400) && Within(RgbSplitAngle, -180, 180) && Within(RgbSplitReach, 1, 100000) && double.IsFinite(RgbSplitCenterX + RgbSplitCenterY),
        FilterKind.ProgressiveBlur => Within(ProgressiveFrom, 0, 250) && Within(ProgressiveTo, 0, 250) && new[] { ProgressiveStartX, ProgressiveStartY, ProgressiveEndX, ProgressiveEndY }.All(double.IsFinite),
        FilterKind.Scanlines => Scanlines.IsValid,
        FilterKind.GaussianBlur => Within(BlurRadius, 0.1, 250),
        FilterKind.MotionBlur => Within(MotionAngle, -90, 90) && Within(MotionDistance, 1, 2000),
        FilterKind.BloomGlow => Within(BloomAmount, 0, 100) && Within(BloomRadius, 1, 150),
        FilterKind.AddNoise => Within(NoiseAmount, 0.1, 400),
        FilterKind.Vignette =>
            Within(VignetteAmount, 0, 100) && Within(VignetteMidpoint, 0, 100) && Within(VignetteRoundness, -100, 100)
            && Within(VignetteFeather, 0, 100) && Within(VignetteHighlights, 0, 100)
            && Within(VignetteRed, 0, 1) && Within(VignetteGreen, 0, 1) && Within(VignetteBlue, 0, 1),
        FilterKind.TonalContrast =>
            Within(TonalAmount, 0, 100) && Within(TonalRadius, 1, 100)
            && Within(TonalShadows, -100, 100) && Within(TonalMidtones, -100, 100) && Within(TonalHighlights, -100, 100),
        _ => Within(Distortion, -100, 100),
    };

    /// <summary>Whether this filter would change anything at all.</summary>
    public bool DoesAnything(FilterKind kind) => kind switch
    {
        FilterKind.Sharpen => SharpenAmount > 0,
        FilterKind.LensEffects => Lens.DoesAnything,
        FilterKind.RimLight => RimLight.DoesAnything,
        FilterKind.VolumetricLight => Volumetric.DoesAnything,
        FilterKind.RgbSplit => RgbSplitDistance > 0 || RgbSplitBlur > 0,
        FilterKind.ProgressiveBlur => ProgressiveFrom > 0 || ProgressiveTo > 0,
        FilterKind.Scanlines => true,
        FilterKind.GaussianBlur => BlurRadius > 0,
        FilterKind.MotionBlur => MotionDistance > 0,
        FilterKind.BloomGlow => BloomAmount > 0,
        FilterKind.AddNoise => NoiseAmount > 0,
        FilterKind.Vignette => VignetteAmount > 0,
        FilterKind.TonalContrast =>
            TonalAmount > 0 && (TonalShadows != 0 || TonalMidtones != 0 || TonalHighlights != 0),
        _ => Distortion != 0,
    };

    private static bool Within(double value, double least, double most) =>
        double.IsFinite(value) && value >= least && value <= most;
}

/// <summary>
/// The filters that are not Camera Raw, run over a layer's own pixels and held to the selection, each as one
/// edit. The kernels were ported from the Mac build's C and are covered by the pixel tests; this is the
/// surface that reaches them.
/// </summary>
public static class FilterEdits
{
    /// <summary>
    /// How far past the layer's edge a spreading filter is given room, in layer pixels — the Mac build's
    /// margin, wide enough for the spread to have died out before it meets the buffer's border.
    /// </summary>
    public static int Margin(FilterKind kind, FilterSettings settings) => kind switch
    {
        FilterKind.GaussianBlur => (int)Math.Ceiling(settings.BlurRadius * 3 + 2),
        FilterKind.MotionBlur => (int)Math.Ceiling(settings.MotionDistance / 2 + 2),
        FilterKind.BloomGlow => (int)Math.Ceiling(settings.BloomRadius * 3 + 2),
        FilterKind.RgbSplit => (int)Math.Ceiling((settings.RgbSplitDistance + settings.RgbSplitBlur) * (settings.RgbSplitRadial ? 2 : 1) + 2),
        _ => 0,
    };

    /// <summary>Whether a filter spreads past the layer's edge, and so needs room and trimming back.</summary>
    private static bool Spreads(FilterKind kind) =>
        kind is FilterKind.GaussianBlur or FilterKind.MotionBlur or FilterKind.BloomGlow;

    /// <summary>
    /// Applies a filter to a layer's pixels. False when there is nothing to do, when the amounts or the layer
    /// cannot take it, or when the pixels would not fit in memory — in which case the layer is left as it was.
    /// <paramref name="seed"/> fixes Add Noise's pattern; zero takes a fresh one each time.
    /// </summary>
    public static bool Apply(CanvasDocument document, Guid layerID, FilterKind kind, FilterSettings settings, uint seed = 0)
    {
        if (!settings.IsValid(kind) || !settings.DoesAnything(kind)) return false;
        if (document.Layers.FirstOrDefault(layer => layer.ID == layerID) is not { } layer) return false;
        if (kind == FilterKind.LensEffects && layer.Asset is { } lensSource)
        { using var result = LensEffects.Apply(lensSource.WorkingImage, settings.Lens); return ImageAdjustments.SetPixels(document, layer, result); }
        // The subject is found once per layer image; without one, Rim Light has no edge to light.
        if (kind == FilterKind.RimLight && layer.Asset is { } rimSource)
        {
            if (SubjectCache.Subject(rimSource.Image) is not { } subject) return false;
            using var result = RimLighting.Apply(rimSource.WorkingImage, subject, settings.RimLight); return ImageAdjustments.SetPixels(document, layer, result);
        }
        if (kind == FilterKind.VolumetricLight && layer.Asset is { } hazeSource)
        {
            // The depth model's map once it's been downloaded; until then the haze guesses.
            using var result = VolumetricLighting.Apply(hazeSource.WorkingImage, SubjectCache.Subject(hazeSource.Image), settings.Volumetric,
                DepthModel.Depth(hazeSource.Image));
            return ImageAdjustments.SetPixels(document, layer, result);
        }
        if (kind == FilterKind.RgbSplit && layer.Asset is { } splitSource)
        {
            var margin = Margin(kind, settings); var oldMask = layer.MaskTransform;
            if (!FilterSurface.Begin(layer, margin, out var surface, out var transform)) return false;
            using (surface)
            using (var splitBefore = document.Selection.Path is null ? null : FilterSurface.Copy(surface))
            {
                var local = new FilterSettings { RgbSplitDistance = settings.RgbSplitDistance, RgbSplitBlur = settings.RgbSplitBlur, RgbSplitAngle = settings.RgbSplitAngle, RgbSplitRadial = settings.RgbSplitRadial, RgbSplitReach = settings.RgbSplitReach,
                    RgbSplitCenterX = (settings.RgbSplitCenterX / 100 * splitSource.Width + margin) / surface.Width * 100,
                    RgbSplitCenterY = (settings.RgbSplitCenterY / 100 * splitSource.Height + margin) / surface.Height * 100 };
                var t = layer.Transform; var sides = t.Rotation == 0 && !t.FlipX && !t.FlipY ? (t.X <= 0 ? 1 : 0) | (t.X + t.Width >= document.Width ? 2 : 0) | (t.Y <= 0 ? 4 : 0) | (t.Y + t.Height >= document.Height ? 8 : 0) : 0;
                using var split = SpectralDispersion.Apply(surface, local, -margin, -margin, clampBounds: SKRectI.Create(margin, margin, splitSource.Width, splitSource.Height), sides: sides);
                FilterSurface.Settle(document, layer, split, splitBefore, transform, oldMask, true); return true;
            }
        }
        if (kind == FilterKind.ProgressiveBlur && layer.Asset is { } progressiveSource)
        {
            using var blurred = ProgressiveBlur.Apply(progressiveSource.WorkingImage, settings);
            return ImageAdjustments.SetPixels(document, layer, blurred);
        }
        if (kind == FilterKind.GaussianBlur && layer.Asset is { } deepSource && (deepSource.Deep is not null || settings.PreserveTransparency))
        {
            using var blurred = DeepAdjustments.Blur(deepSource.WorkingImage, settings.BlurRadius, settings.PreserveTransparency);
            return ImageAdjustments.SetPixels(document, layer, blurred);
        }
        // Where the mask sat before the layer's grid changes under it, which is what carries it across.
        var maskPlacement = layer.MaskTransform;
        if (!FilterSurface.Begin(layer, Margin(kind, settings), out var work, out var placement)) return false;
        using var _ = work;
        using var was = document.Selection.Path is null ? null : FilterSurface.Copy(work);
        var pixels = work.GetPixelSpan();
        var width = work.Width;
        var height = work.Height;
        var stride = work.RowBytes;
        switch (kind)
        {
            case FilterKind.Sharpen:
                SharpenPixels.Apply(work, settings.SharpenAmount, settings.SharpenRadius);
                break;
            case FilterKind.Scanlines:
                ScanlinePixels.Apply(pixels, width, height, stride, settings.Scanlines);
                break;
            case FilterKind.GaussianBlur:
                // Not clamped: the room around the layer exists so the blur fades at its edge; the border of
                // the wider buffer is far enough away that reading it as the edge changes nothing.
                GaussianBlur.Clamped(pixels, width, height, 4, stride, settings.BlurRadius);
                break;
            case FilterKind.BloomGlow:
            {
                // What glows is the picture lifted: bright parts spread, dark ones left where they are.
                using var lifted = FilterSurface.Copy(work);
                BloomPixels.Lift(lifted.GetPixelSpan(), lifted.GetPixelSpan(), width, height, stride,
                    settings.BloomAmount / 50.0);
                GaussianBlur.Clamped(lifted.GetPixelSpan(), width, height, 4, lifted.RowBytes, settings.BloomRadius);
                BloomPixels.Screen(pixels, lifted.GetPixelSpan(), width, height, stride, settings.BloomAmount / 50.0);
                break;
            }
            case FilterKind.MotionBlur:
                using (var source = FilterSurface.Copy(work))
                {
                    MotionPixels.Streak(source.GetPixelSpan(), pixels, width, height, stride,
                        settings.MotionDistance * MotionPixels.RadiusPerPixel, settings.MotionAngle * Math.PI / 180);
                }
                break;
            case FilterKind.AddNoise:
                NoisePixels.NoiseAdd(pixels, width, height, stride, (float)settings.NoiseAmount,
                    settings.NoiseGaussian, settings.NoiseMonochromatic, seed != 0 ? seed : (uint)Random.Shared.Next(1, int.MaxValue));
                break;
            case FilterKind.Vignette:
                // The whole layer is the frame: this is not a crop, so nothing is outside it to paint.
                AdjustPixels.ColoredVignette(pixels, width, height, stride, 0, 0, width, height, fillsClear: false,
                    settings.VignetteAmount, settings.VignetteMidpoint, settings.VignetteRoundness,
                    settings.VignetteFeather, settings.VignetteHighlights,
                    settings.VignetteRed, settings.VignetteGreen, settings.VignetteBlue);
                break;
            case FilterKind.TonalContrast:
                using (var blurred = Blur(work, settings.TonalRadius))
                {
                    AdjustPixels.TonalContrast(pixels, blurred.GetPixelSpan(), width, height, stride, blurred.RowBytes,
                        settings.TonalAmount, settings.TonalShadows, settings.TonalMidtones, settings.TonalHighlights);
                }
                break;
            default:
                using (var source = FilterSurface.Copy(work))
                {
                    LensPixels.LensDistort(source.GetPixelSpan(), pixels, width, height, stride,
                        settings.Distortion / 100 * FilterSettings.LensStrength);
                }
                break;
        }
        FilterSurface.Settle(document, layer, work, was, placement, maskPlacement, Spreads(kind));
        return true;
    }

    /// <summary>
    /// Runs an adjustment over a layer's own pixels, held to the selection, as one edit — what the Image
    /// menu does, and what an adjustment layer does without being asked twice. False when there is nothing to
    /// do, when the amounts or the layer cannot take it, or when the pixels would not fit in memory.
    /// </summary>
    public static bool ApplyAdjustment(CanvasDocument document, Guid layerID, LayerAdjustment settings)
    {
        if (!settings.IsValid) return false;
        // An adjustment layer holds no pixels of its own: its effect is drawn as the stack is composited.
        if (document.Layers.FirstOrDefault(layer => layer.ID == layerID) is not { Adjustment: null } layer) return false;
        if (layer.Asset?.Deep is not null && DeepAdjustments.Supports(settings)) return DeepAdjustments.Apply(document, layer, settings);
        var maskPlacement = FilterSurface.MaskPlacementOf(layer);
        if (!FilterSurface.Begin(layer, settings.SamplingMargin, out var work, out var placement)) return false;
        using var _ = work;
        using var was = document.Selection.Path is null ? null : FilterSurface.Copy(work);
        // Grain and noise are anchored to the document, so the buffer's first pixel is placed first.
        var origin = BrushEdits.PixelToDocument(placement, work.Width, work.Height).MapPoint(0.5f, 0.5f);
        AdjustmentOperators.Apply(settings, 1, work.GetPixelSpan(), work.Width, work.Height, work.RowBytes,
            (long)Math.Floor(origin.X), (long)Math.Floor(origin.Y));
        FilterSurface.Settle(document, layer, work, was, placement, maskPlacement, settings.SamplingMargin > 0);
        return true;
    }
    /// <summary>The same pixels softened by <paramref name="radius"/>, which local contrast measures against.</summary>
    private static SKBitmap Blur(SKBitmap source, double radius)
    {
        var blurred = FilterSurface.Copy(source);
        // The same clamped blur the Mac build uses, so the edge of the picture is not treated as empty.
        GaussianBlur.Clamped(blurred.GetPixelSpan(), blurred.Width, blurred.Height, 4, blurred.RowBytes, radius);
        return blurred;
    }
}
