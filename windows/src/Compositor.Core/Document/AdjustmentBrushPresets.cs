using Compositor.Core.Format;
namespace Compositor.Core.Document;
public static class AdjustmentBrushPresets
{
    public static LayerAdjustment Create(int index)
    {
        LayerAdjustment Extended(ImageAdjustmentKind kind, params (string Key, double Value)[] values) => new() { Kind = AdjustmentKind.Extended, Extended = new() { Kind = kind, Values = values.ToDictionary(v => v.Key, v => v.Value) } };
        switch (index)
        {
            case 0: return Extended(ImageAdjustmentKind.Vibrance, ("Vibrance", 30), ("Saturation", 10));
            case 1: return Extended(ImageAdjustmentKind.ClarityDehaze, ("Clarity", 25), ("Dehaze", 15));
            case 2: return new() { Kind = AdjustmentKind.Grain, GrainSettings = new() { Amount = 25 } };
            case 3: return Extended(ImageAdjustmentKind.BrightnessContrast, ("Brightness", 20), ("Contrast", 10));
            case 4: var levels = new LayerAdjustment { Kind = AdjustmentKind.Levels }; levels.Levels.Ranges[0].Gamma = 1.25; return levels;
            case 5: var curves = new LayerAdjustment { Kind = AdjustmentKind.Curves }; curves.Curves.Channels[0].Insert(1, new() { X = 128, Y = 155 }); return curves;
            case 6: return new() { Kind = AdjustmentKind.Exposure, ExposureSettings = new() { Exposure = .5 } };
            case 7: return new() { Kind = AdjustmentKind.HueSaturation, Saturation = 25 };
            case 8: return new() { Kind = AdjustmentKind.ColorBalance, ColorBalanceSettings = new() { MidCyanRed = 20, MidYellowBlue = -10 } };
            case 9: return new() { Kind = AdjustmentKind.BlackWhite };
            case 10: return Extended(ImageAdjustmentKind.PhotoFilter, ("Density", 25));
            case 11: return Extended(ImageAdjustmentKind.ChannelMixer, ("0R", 80), ("0G", 20), ("1G", 90), ("1B", 10), ("2B", 100));
            case 12:
                var lookup = Extended(ImageAdjustmentKind.ColorLookup); lookup.Extended!.Cube = new() { Size = 2, Values = Enumerable.Range(0, 8).Select(i => new Color3((i & 1) == 0 ? .04 : 1, (i & 2) == 0 ? .02 : .96, (i & 4) == 0 ? 0 : .9)).ToArray() }; return lookup;
            case 13: return Extended(ImageAdjustmentKind.SelectiveColor, ("Cyan", -20), ("Magenta", 5), ("Yellow", 10), ("Black", -5));
            case 14: return new() { Kind = AdjustmentKind.Invert };
            case 15: return Extended(ImageAdjustmentKind.Posterize, ("Levels", 4));
            case 16: return Extended(ImageAdjustmentKind.Threshold, ("Threshold", 128));
            case 17: return new() { Kind = AdjustmentKind.GradientMap };
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
}
