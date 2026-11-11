namespace Compositor.Desktop;

/// <summary>
/// The presets Photoshop lists in each adjustment dialog, by name, as amounts the dialogs read back. The first of each
/// list is Default. The amounts follow Photoshop's presets in kind and direction; they are not copied from its files,
/// which this app does not read, so a preset of the same name may differ from Photoshop's in detail.
/// </summary>
internal static class AdjustmentPresets
{
    private static Dictionary<string, double> Values(params (string Key, double Value)[] values) => values.ToDictionary(v => v.Key, v => v.Value);

    /// <summary>Levels: master input black, gamma and white, then output black and white ("L0B", "L0G", "L0W", "L0OB", "L0OW").</summary>
    private static Dictionary<string, double> Levels(double black, double gamma, double white, double outBlack = 0, double outWhite = 255) =>
        Values(("L0B", black), ("L0G", gamma), ("L0W", white), ("L0OB", outBlack), ("L0OW", outWhite));
    internal static readonly (string, Dictionary<string, double>)[] LevelsList =
    [
        ("Default Values", Levels(0, 1, 255)), ("Darker", Levels(15, .7, 255)), ("Increase Contrast 1", Levels(15, 1, 240)), ("Increase Contrast 2", Levels(30, 1, 225)),
        ("Increase Contrast 3", Levels(45, 1, 210)), ("Lighten Shadows", Levels(0, 1.6, 255, 20)), ("Lighter", Levels(0, 1.4, 240)),
        ("Midtones Brighter", Levels(0, 1.25, 255)), ("Midtones Darker", Levels(0, .8, 255)),
    ];

    /// <summary>Curves: per channel (0 master, then red, green, blue) its points, "C0N" of them at "C0X0", "C0Y0"….</summary>
    private static Dictionary<string, double> Curve(params (int Channel, double[] Points)[] channels)
    {
        var values = new Dictionary<string, double>();
        for (var c = 0; c < 4; c++)
        {
            var points = channels.FirstOrDefault(p => p.Channel == c).Points ?? [0, 0, 255, 255];
            values[$"C{c}N"] = points.Length / 2;
            for (var i = 0; i < points.Length / 2; i++) { values[$"C{c}X{i}"] = points[i * 2]; values[$"C{c}Y{i}"] = points[i * 2 + 1]; }
        }
        return values;
    }
    internal static readonly (string, Dictionary<string, double>)[] CurvesList =
    [
        ("Default Values", Curve()),
        ("Color Negative (RGB)", Curve((1, [0, 255, 255, 30]), (2, [0, 255, 255, 18]), (3, [0, 255, 255, 0]))),
        ("Cross Process (RGB)", Curve((1, [0, 0, 64, 40, 192, 220, 255, 255]), (2, [0, 0, 64, 50, 192, 210, 255, 255]), (3, [0, 40, 255, 215]))),
        ("Darker (RGB)", Curve((0, [0, 0, 128, 100, 255, 255]))),
        ("Increase Contrast (RGB)", Curve((0, [0, 0, 64, 52, 192, 204, 255, 255]))),
        ("Lighter (RGB)", Curve((0, [0, 0, 128, 156, 255, 255]))),
        ("Linear Contrast (RGB)", Curve((0, [0, 0, 64, 48, 192, 208, 255, 255]))),
        ("Medium Contrast (RGB)", Curve((0, [0, 0, 64, 44, 192, 212, 255, 255]))),
        ("Negative (RGB)", Curve((0, [0, 255, 255, 0]))),
        ("Strong Contrast (RGB)", Curve((0, [0, 0, 64, 34, 192, 222, 255, 255]))),
    ];

    internal static readonly (string, Dictionary<string, double>)[] ExposureList =
    [
        ("Default Preset", Values(("Exposure", 0), ("Offset", 0), ("Gamma", 1))),
        ("Minus 1.0", Values(("Exposure", -1), ("Offset", 0), ("Gamma", 1))), ("Minus 2.0", Values(("Exposure", -2), ("Offset", 0), ("Gamma", 1))),
        ("Plus 1.0", Values(("Exposure", 1), ("Offset", 0), ("Gamma", 1))), ("Plus 2.0", Values(("Exposure", 2), ("Offset", 0), ("Gamma", 1))),
    ];

    /// <summary>Hue/Saturation: "Colorize", and hue, saturation and lightness per range, 0 Master to 6 Magentas ("H0", "S0", "L0").</summary>
    private static Dictionary<string, double> Hue(bool colorize = false, params (int Range, double Hue, double Saturation, double Lightness)[] ranges)
    {
        var values = new Dictionary<string, double> { ["Colorize"] = colorize ? 1 : 0 };
        for (var r = 0; r < 7; r++)
        {
            var set = ranges.FirstOrDefault(x => x.Range == r);
            values[$"H{r}"] = set.Hue; values[$"S{r}"] = set.Saturation; values[$"L{r}"] = set.Lightness;
        }
        return values;
    }
    internal static readonly (string, Dictionary<string, double>)[] HueSaturationList =
    [
        ("Default Preset", Hue()), ("Cyanotype", Hue(true, (0, 210, 25, 0))), ("Further Increase Saturation", Hue(false, (0, 0, 30, 0))),
        ("Increase Saturation", Hue(false, (0, 0, 15, 0))), ("Old Style", Hue(true, (0, 35, 15, -5))), ("Red Boost", Hue(false, (1, 0, 40, 0))),
        ("Sepia", Hue(true, (0, 35, 25, 0))), ("Strong Saturation", Hue(false, (0, 0, 50, 0))), ("Yellow Boost", Hue(false, (2, 0, 40, 0))),
    ];

    private static Dictionary<string, double> Gray(double reds, double yellows, double greens, double cyans, double blues, double magentas) =>
        Values(("Reds", reds), ("Yellows", yellows), ("Greens", greens), ("Cyans", cyans), ("Blues", blues), ("Magentas", magentas), ("Tint", 0), ("TintHue", 42), ("TintSaturation", 20));
    internal static readonly (string, Dictionary<string, double>)[] BlackWhiteList =
    [
        ("Default Preset", Gray(40, 60, 40, 60, 20, 80)), ("Blue Filter", Gray(0, 0, 0, 70, 120, 110)), ("Darker", Gray(25, 35, 25, 35, 10, 50)),
        ("Green Filter", Gray(40, 120, 120, 40, 0, 0)), ("High Contrast Blue Filter", Gray(-50, -50, -50, 150, 150, 150)),
        ("High Contrast Red Filter", Gray(120, 120, -10, -50, -50, 120)), ("Infrared", Gray(-40, 235, 144, -68, -3, -107)), ("Lighter", Gray(60, 110, 40, 30, 10, 80)),
        ("Maximum Black", Gray(0, 0, 0, 0, 0, 0)), ("Maximum White", Gray(100, 100, 100, 100, 100, 100)), ("Neutral Density", Gray(70, 60, 50, 40, 30, 50)),
        ("Red Filter", Gray(120, 110, -10, -50, -50, 120)), ("Yellow Filter", Gray(120, 110, 40, 30, -50, 60)),
    ];

    /// <summary>Channel Mixer, monochrome: the gray made from red, green and blue in percent.</summary>
    private static Dictionary<string, double> Mono(double red, double green, double blue) =>
        Values(("Monochrome", 1), ("0R", red), ("0G", green), ("0B", blue), ("0Constant", 0), ("1R", 0), ("1G", 100), ("1B", 0), ("1Constant", 0), ("2R", 0), ("2G", 0), ("2B", 100), ("2Constant", 0));
    internal static readonly (string, Dictionary<string, double>)[] ChannelMixerList =
    [
        ("Default Preset", Values(("Monochrome", 0), ("0R", 100), ("0G", 0), ("0B", 0), ("0Constant", 0), ("1R", 0), ("1G", 100), ("1B", 0), ("1Constant", 0), ("2R", 0), ("2G", 0), ("2B", 100), ("2Constant", 0))),
        ("Black & White Infrared (RGB)", Mono(-70, 200, -30)), ("Black & White with Blue Filter (RGB)", Mono(0, 0, 100)), ("Black & White with Green Filter (RGB)", Mono(0, 100, 0)),
        ("Black & White with Orange Filter (RGB)", Mono(50, 50, 0)), ("Black & White with Red Filter (RGB)", Mono(100, 0, 0)), ("Black & White with Yellow Filter (RGB)", Mono(34, 66, 0)),
    ];

    internal static readonly (string, Dictionary<string, double>)[] SelectiveColorList = [("Default Preset", new())];

    /// <summary>HDR Toning: method 3 is Local Adaptation, and its edge glow, tone and detail, and advanced amounts.</summary>
    private static Dictionary<string, double> Hdr(double radius, double strength, double gamma, double exposure, double detail, double shadow, double highlight, double vibrance, double saturation) =>
        Values(("Method", 3), ("Radius", radius), ("Strength", strength), ("Gamma", gamma), ("Exposure", exposure), ("Detail", detail), ("Shadow", shadow), ("Highlight", highlight),
            ("Vibrance", vibrance), ("Saturation", saturation), ("Smooth", 0), ("CurveCount", 0));
    internal static readonly (string, Dictionary<string, double>)[] HdrList =
    [
        ("Default Values", Hdr(136, .52, 1, 0, 30, 0, 0, 0, 20)), ("City Twilight", Hdr(100, .8, .9, .3, 60, 10, -20, 20, 10)), ("Smooth", Hdr(200, .3, 1.2, 0, 0, 30, -30, 0, 0)),
        ("Monochromatic Artistic", Hdr(130, 1.2, .8, 0, 150, 0, 0, 0, -100)), ("Monochromatic High Contrast", Hdr(100, .8, .6, 0, 80, -20, 20, 0, -100)),
        ("Monochromatic Low Contrast", Hdr(250, .4, 1.3, 0, 20, 30, -30, 0, -100)), ("Monochromatic", Hdr(136, .52, 1, 0, 30, 0, 0, 0, -100)),
        ("More Saturated", Hdr(136, .52, 1, 0, 30, 0, 0, 30, 50)), ("Photorealistic High Contrast", Hdr(80, .6, .8, 0, 40, -10, 10, 10, 20)),
        ("Photorealistic Low Contrast", Hdr(250, .3, 1.2, 0, 15, 20, -20, 0, 10)), ("Photorealistic", Hdr(150, .45, 1, 0, 25, 0, 0, 0, 15)),
        ("RC5", Hdr(60, 1.4, 1, .2, 110, 15, -15, 35, 30)), ("Saturated", Hdr(136, .52, 1, 0, 30, 0, 0, 20, 70)), ("Scott5", Hdr(40, 1.6, .9, 0, 200, 20, -40, 30, 20)),
        ("Surrealistic High Contrast", Hdr(30, 2.5, .7, 0, 250, -30, 40, 40, 40)), ("Surrealistic Low Contrast", Hdr(50, 2, 1.3, 0, 200, 40, -40, 30, 30)),
        ("Surrealistic", Hdr(40, 2.2, 1, 0, 230, 0, 0, 40, 40)),
    ];
}
