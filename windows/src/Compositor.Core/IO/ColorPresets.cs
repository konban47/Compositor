namespace Compositor.Core.IO;

public static class ColorPresets
{
    public static readonly string[] Names = ["Custom", "Japan Web/Internet", "Japan Prepress 2", "Japan General Purpose 2", "Japan Newspaper", "Japan Magazine Advertisement", "Monitor Color", "North America Web/Internet", "North America Prepress 2", "North America General Purpose 2", "North America Newspaper", "Japan General Purpose 3", "Europe Web/Internet 2", "Europe Prepress 3", "Europe General Purpose 3"];
    public static ColorManagementSettings Select(string name, ColorManagementSettings current)
    {
        if (!Names.Contains(name)) throw new ArgumentException("Unknown color preset.");
        if (name == "Custom") return current with { Preset = name };
        var japan = name.StartsWith("Japan"); var europe = name.StartsWith("Europe"); var prepress = name.Contains("Prepress"); var news = name.Contains("Newspaper");
        var cmyk = japan ? news ? "Japan Color 2002 Newspaper" : name.EndsWith("3") ? "Japan Color 2011 Coated" : "Japan Color 2001 Coated" : europe ? "Coated FOGRA39 (ISO 12647-2:2004)" : news ? "US Newsprint (SNAP 2007)" : "U.S. Web Coated (SWOP) v2";
        // Never silently substitute another press condition for an unavailable ICC profile.
        _ = IccProfiles.Resolve(cmyk, current);
        return current with { Preset = name, Rgb = prepress ? "Adobe RGB (1998)" : "sRGB IEC61966-2.1", Cmyk = cmyk, Gray = europe ? "Dot Gain 15%" : japan ? "Dot Gain 15%" : "Dot Gain 20%", Spot = europe || japan ? "Dot Gain 15%" : "Dot Gain 20%", RgbPolicy = name == "Monitor Color" ? ColorPolicy.Off : ColorPolicy.Preserve, CmykPolicy = ColorPolicy.Preserve, GrayPolicy = ColorPolicy.Preserve, AskMismatchOpen = prepress, AskMismatchPaste = prepress, AskMissingOpen = prepress, Intent = ColorIntent.Perceptual, BlackPointCompensation = true, Dither = true };
    }
}
