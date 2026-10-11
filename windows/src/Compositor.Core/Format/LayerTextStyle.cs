namespace Compositor.Core.Format;

/// <summary>An editable text layer's metadata; the PNG remains the display and export fallback.</summary>
public sealed class LayerTextStyle
{
    public bool? Vertical { get; set; }
    public string Content { get; set; } = "Text";
    public string FontName { get; set; } = "Helvetica";
    public double FontSize { get; set; } = 72;
    public double Red { get; set; }
    public double Green { get; set; }
    public double Blue { get; set; }
    public TextAlignment Alignment { get; set; } = TextAlignment.Left;
    public double Tracking { get; set; }
    public bool? Bold { get; set; }
    public bool? Italic { get; set; }
    public bool? Underline { get; set; }
    public bool? Strikethrough { get; set; }

    /// <summary>Synthetic font treatments from Windows extension 13.</summary>
    public bool HasTypography => Bold is not null || Italic is not null || Underline is not null || Strikethrough is not null;

    /// <summary>Draws lowercase letters as reduced capitals, synthesized from the uppercase glyphs.</summary>
    public bool? SmallCaps { get; set; }

    /// <summary>Draws every letter as a capital.</summary>
    public bool? AllCaps { get; set; }

    /// <summary>Reduced letters raised above the baseline (synthetic).</summary>
    public bool? Superscript { get; set; }

    /// <summary>Reduced letters lowered below the baseline (synthetic).</summary>
    public bool? Subscript { get; set; }

    /// <summary>Opens the font's standard ligatures (OpenType <c>liga</c>) while shaping. Null keeps the default.</summary>
    public bool? Ligatures { get; set; }

    /// <summary>Turns kerning on or off while shaping. Null keeps the default.</summary>
    public bool? Kerning { get; set; }

    /// <summary>Extra OpenType feature tags to enable, each four letters, e.g. <c>dlig</c>, <c>onum</c>, <c>frac</c>, <c>ss01</c>.</summary>
    public List<string>? Features { get; set; }

    /// <summary>Reading direction; Auto follows the first strong character.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public TextDirection Direction { get; set; } = TextDirection.Auto;

    /// <summary>Text shaped and ordered by OpenType for Arabic, Hebrew and Indic scripts.</summary>
    public bool? ComplexShaping { get; set; }

    /// <summary>Which language to shape for, e.g. <c>ar</c>, <c>ja</c>. Null guesses from the content.</summary>
    public string? Language { get; set; }

    /// <summary>Whether <c>{name}</c>, <c>{width}</c>, <c>{date}</c> and other tokens are filled in when drawn.</summary>
    public bool? Dynamic { get; set; }

    /// <summary>Editable text options added by Windows extension 14.</summary>
    public bool HasAdvancedTypography =>
        SmallCaps is not null || AllCaps is not null || Superscript is not null || Subscript is not null
        || Ligatures is not null || Kerning is not null || Features is { Count: > 0 }
        || Direction != TextDirection.Auto || ComplexShaping is not null || Language is not null || Dynamic is not null;

    /// <summary>Baseline to baseline, in layer pixels. 0 is Auto: 120% of the font size.</summary>
    public double Leading { get; set; }

    /// <summary>Fixed paragraph bounds in layer pixels; nil for older point-text layers.</summary>
    public JsonSize? BoxSize { get; set; }

    /// <summary>Letters painted in a color other than `red`/`green`/`blue`, in UTF-16 offsets into `content`.</summary>
    public List<LayerTextColorRun>? ColorRuns { get; set; }

    /// <summary>Letters set in a face other than `fontName`, in the same offsets.</summary>
    public List<LayerTextFontRun>? FontRuns { get; set; }

    public double LineHeight => Leading > 0 ? Leading : FontSize * 1.2;

    public bool IsValid =>
        Content is not null && Content.Length <= 100_000 && BoxIsValid
        && double.IsFinite(FontSize) && FontSize is >= 1 and <= 2000
        && Color.IsValid(Red, Green, Blue)
        && double.IsFinite(Tracking) && Tracking is >= -100 and <= 1000
        && double.IsFinite(Leading) && Leading is >= 0 and <= 5000
        && ColorRunsAreValid && FontRunsAreValid
        && (Superscript is not true || Subscript is not true)
        && FeaturesAreValid && LanguageIsValid;

    /// <summary>Feature tags are four ASCII letters or digits, at most 64 of them, and unique.</summary>
    private bool FeaturesAreValid
    {
        get
        {
            if (Features is null) return true;
            if (Features.Count is 0 or > 64) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tag in Features)
            {
                if (tag is null || tag.Length != 4 || tag.Any(character => !char.IsAsciiLetterOrDigit(character))) return false;
                if (!seen.Add(tag)) return false;
            }
            return true;
        }
    }

    private bool LanguageIsValid =>
        Language is null || (Language.Length is > 0 and <= 64 && !ContainsNewline(Language));

    private bool BoxIsValid
    {
        get
        {
            if (BoxSize is not { } box) return true;
            return double.IsFinite(box.Width) && double.IsFinite(box.Height)
                && box.Width is >= 16 and <= Model.DocumentLimits.MaxSide
                && box.Height is >= 16 and <= Model.DocumentLimits.MaxSide
                && box.Width * box.Height <= Model.DocumentLimits.MaxSurfacePixels;
        }
    }

    /// <summary>Runs are sorted, do not overlap, have a positive length, and end within the content.</summary>
    private bool ColorRunsAreValid
    {
        get
        {
            if (ColorRuns is null) return true;
            var end = 0;
            foreach (var run in ColorRuns)
            {
                if (run.Location < end || run.Length <= 0 || run.Location > int.MaxValue - run.Length
                    || !Color.IsValid(run.Red, run.Green, run.Blue))
                {
                    return false;
                }
                end = run.Location + run.Length;
            }
            return ColorRuns.Count > 0 && end <= Content.Length;
        }
    }

    private bool FontRunsAreValid
    {
        get
        {
            if (FontRuns is null) return true;
            var end = 0;
            foreach (var run in FontRuns)
            {
                if (run.Location < end || run.Length <= 0 || run.Location > int.MaxValue - run.Length
                    || string.IsNullOrEmpty(run.FontName) || run.FontName.Length > 200
                    || ContainsNewline(run.FontName))
                {
                    return false;
                }
                end = run.Location + run.Length;
            }
            return FontRuns.Count > 0 && end <= Content.Length;
        }
    }

    /// <summary>Swift's `Character.isNewline`: the separators a font name may not hold.</summary>
    private static bool ContainsNewline(string value) =>
        value.Any(character => character is '\n' or '\r' or '\u000b' or '\u000c' or '\u0085' or '\u2028' or '\u2029');
}

public sealed class LayerTextColorRun
{
    public int Location { get; set; }
    public int Length { get; set; }
    public double Red { get; set; }
    public double Green { get; set; }
    public double Blue { get; set; }
}

public sealed class LayerTextFontRun
{
    public int Location { get; set; }
    public int Length { get; set; }
    public string FontName { get; set; } = "";
}
