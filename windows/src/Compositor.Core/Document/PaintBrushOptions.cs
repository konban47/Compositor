using System.Text.Json.Serialization;
using SkiaSharp;
namespace Compositor.Core.Document;

public enum BrushControl { Off, Fade, PenPressure, PenTilt, StylusWheel, Rotation, Direction, InitialDirection }
public enum PaintBlend { Normal, Dissolve, Behind, Clear, Darken, Multiply, ColorBurn, LinearBurn, DarkerColor, Lighten, Screen, ColorDodge, LinearDodgeAdd, LighterColor, Overlay, SoftLight, HardLight, VividLight, LinearLight, PinLight, HardMix, Difference, Exclusion, Subtract, Divide, Hue, Saturation, Color, Luminosity }
public enum BrushSymmetry { Off, Vertical, Horizontal, DualAxis, Diagonal, Wavy, Circle, Spiral, Parallel, Radial, Mandala, SelectedPath }
public readonly record struct PenSample(float Pressure = 1, float TiltX = 0, float TiltY = 0, float Rotation = 0, float Wheel = 1);

/// <summary>Immutable, serializable brush parameters. Native inputs and the mixer reservoir are stroke-local.</summary>
public sealed record PaintBrushOptions
{
    public PaintBlend Blend { get; init; }
    public double Flow { get; init; } = 1;
    public bool Pencil { get; init; }
    public bool AutoErase { get; init; }
    public bool Airbrush { get; init; }
    public bool FlipX { get; init; }
    public bool FlipY { get; init; }
    public bool SpacingEnabled { get; init; } = true;
    public bool SmoothingEnabled { get; init; } = true;
    public bool PulledString { get; init; }
    public bool CatchUp { get; init; } = true;
    public bool CatchUpOnRelease { get; init; }
    public bool AdjustForZoom { get; init; } = true;
    public int Tip { get; init; }
    public bool ShapeEnabled { get; init; }
    public double SizeJitter { get; init; }
    public BrushControl SizeControl { get; init; } = BrushControl.PenPressure;
    public double MinimumDiameter { get; init; }
    public double TiltScale { get; init; } = 1;
    public double AngleJitter { get; init; }
    public BrushControl AngleControl { get; init; }
    public double RoundnessJitter { get; init; }
    public BrushControl RoundnessControl { get; init; }
    public double MinimumRoundness { get; init; } = .25;
    public bool FlipXJitter { get; init; }
    public bool FlipYJitter { get; init; }
    public bool Projection { get; init; }
    public int FadeSteps { get; init; } = 25;
    public bool ScatterEnabled { get; init; }
    public double Scatter { get; init; } = .3;
    public bool ScatterBothAxes { get; init; }
    public BrushControl ScatterControl { get; init; }
    public int Count { get; init; } = 1;
    public double CountJitter { get; init; }
    public BrushControl CountControl { get; init; }
    public bool TextureEnabled { get; init; }
    public int Texture { get; init; }
    public string? TextureImage { get; init; }
    public bool TextureInvert { get; init; }
    public double TextureScale { get; init; } = 1;
    public double TextureBrightness { get; init; }
    public double TextureContrast { get; init; }
    public bool TextureEachTip { get; init; } = true;
    public int TextureMode { get; init; }
    public double TextureDepth { get; init; } = 1;
    public double TextureMinimumDepth { get; init; }
    public double TextureJitter { get; init; }
    public BrushControl TextureControl { get; init; }
    public bool DualEnabled { get; init; }
    public PaintBlend DualMode { get; init; } = PaintBlend.ColorBurn;
    public bool DualFlip { get; init; } = true;
    public int DualTip { get; init; } = 1;
    public double DualSize { get; init; } = 25;
    public double DualSpacing { get; init; } = .25;
    public double DualScatter { get; init; }
    public bool DualBothAxes { get; init; }
    public int DualCount { get; init; } = 1;
    public bool ColorEnabled { get; init; }
    public bool ColorEachTip { get; init; }
    public double ForegroundBackground { get; init; }
    public BrushControl ColorControl { get; init; }
    public double HueJitter { get; init; }
    public double SaturationJitter { get; init; }
    public double BrightnessJitter { get; init; }
    public double Purity { get; init; }
    public bool TransferEnabled { get; init; }
    public double OpacityJitter { get; init; }
    public BrushControl OpacityControl { get; init; }
    public double MinimumOpacity { get; init; }
    public double FlowJitter { get; init; }
    public BrushControl FlowControl { get; init; }
    public double MinimumFlow { get; init; }
    public double WetJitter { get; init; }
    public BrushControl WetControl { get; init; }
    public double MinimumWet { get; init; }
    public double MixJitter { get; init; }
    public BrushControl MixControl { get; init; }
    public double MinimumMix { get; init; }
    public bool PoseEnabled { get; init; }
    public double TiltX { get; init; }
    public double TiltY { get; init; }
    public double Rotation { get; init; }
    public double Pressure { get; init; } = 1;
    public bool OverrideTiltX { get; init; }
    public bool OverrideTiltY { get; init; }
    public bool OverrideRotation { get; init; }
    public bool OverridePressure { get; init; }
    public bool Noise { get; init; }
    public bool WetEdges { get; init; }
    public bool BuildUp { get; init; }
    public bool ProtectTexture { get; init; }
    public ulong LockedSections { get; init; }
    public bool PatternAligned { get; init; } = true;
    public bool PatternImpressionist { get; init; }
    public double ArtArea { get; init; } = 50;
    public double ArtTolerance { get; init; }
    public int ArtStyle { get; init; }
    public int EraserMode { get; init; }
    public bool EraseToHistory { get; init; }
    public int ReplaceSampling { get; init; }
    public int ReplaceLimits { get; init; } = 1;
    public bool ReplaceAntialias { get; init; } = true;
    public BrushControl ToleranceControl { get; init; }
    public bool MixerAutoLoad { get; init; } = true;
    public bool MixerAutoClean { get; init; } = true;
    public bool MixerSolidOnly { get; init; }
    public bool MixerAllLayers { get; init; }
    public int MixerPreset { get; init; }
    public BrushSymmetry Symmetry { get; init; }
    public BrushSymmetry LastSymmetry { get; init; } = BrushSymmetry.Vertical;
    public bool SymmetryVisible { get; init; } = true;
    public int SymmetrySegments { get; init; } = 5;
    public double SymmetryX { get; init; } = .5;
    public double SymmetryY { get; init; } = .5;
    public double SymmetryAngle { get; init; }
    public double SymmetryScale { get; init; } = 1;
    public string? SymmetryPath { get; init; }
    public uint Background { get; init; } = 0xffffffff;
    public uint MixerColor { get; init; } = 0xff000000;
    public bool MixerLoaded { get; init; } = true;
    public string? MixerImage { get; init; }
    [JsonIgnore] public IReadOnlyList<PenSample>? Samples { get; init; }
    [JsonIgnore] public MixerReservoir? Reservoir { get; init; }

    public bool IsValid => GetType().GetProperties().Where(p => p.PropertyType == typeof(double)).All(p => double.IsFinite((double)p.GetValue(this)!))
        && GetType().GetProperties().Where(p => p.PropertyType.IsEnum).All(p => Enum.IsDefined(p.PropertyType, p.GetValue(this)!))
        && ArtArea is >= 1 and <= 500 && ArtTolerance is >= 0 and <= 1 && ArtStyle is >= 0 and <= 9 && EraserMode is >= 0 and <= 2
        && Flow is >= 0 and <= 1 && Tip is >= 0 and < 24 && DualTip is >= 0 and < 24
        && Count is >= 1 and <= 16 && DualCount is >= 1 and <= 16 && FadeSteps is >= 1 and <= 10000
        && DualSize is >= 1 and <= 5000 && DualSpacing is >= .01 and <= 10 && Scatter is >= 0 and <= 10 && DualScatter is >= 0 and <= 10
        && TextureScale is >= .01 and <= 10 && Texture is >= 0 and <= 3 && TextureMode is >= 0 and <= 5
        && ReplaceSampling is >= 0 and <= 2 && ReplaceLimits is >= 0 and <= 2 && MixerPreset is >= 0 and <= 12
        && SymmetrySegments is >= 2 and <= 12 && SymmetryScale is >= .01 and <= 10 && Math.Abs(SymmetryX) <= 10 && Math.Abs(SymmetryY) <= 10
        && (SymmetryPath?.Length ?? 0) <= 200000 && (TextureImage?.Length ?? 0) <= 6 * 1024 * 1024 && (MixerImage?.Length ?? 0) <= 6 * 1024 * 1024
        && new[] { SizeJitter, MinimumDiameter, AngleJitter, RoundnessJitter, MinimumRoundness, CountJitter, TextureDepth, TextureMinimumDepth, TextureJitter,
            ForegroundBackground, HueJitter, SaturationJitter, BrightnessJitter, OpacityJitter, MinimumOpacity, FlowJitter, MinimumFlow, WetJitter, MinimumWet, MixJitter, MinimumMix, Pressure }.All(n => n is >= 0 and <= 1)
        && Math.Abs(Purity) <= 1 && Math.Abs(TextureBrightness) <= 1 && Math.Abs(TextureContrast) <= 1 && Math.Abs(TiltX) <= 90 && Math.Abs(TiltY) <= 90 && Rotation is >= 0 and <= 360 && TiltScale is >= .01 and <= 2;
}

public sealed class MixerReservoir
{
    public SKColor Color { get; set; }
    public double Amount { get; set; }
    public MixerReservoir(SKColor color, double amount) { Color = color; Amount = amount; }
}
