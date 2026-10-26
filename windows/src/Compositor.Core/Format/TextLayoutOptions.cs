namespace Compositor.Core.Format;

/// <summary>Windows format 18: editable character scaling and paragraph layout, in layer pixels.</summary>
public sealed record TextLayoutOptions
{
    public bool FractionalWidths { get; init; } = true;
    public bool NoBreak { get; init; }
    public int CharacterAlignment { get; init; }
    public int KinsokuMode { get; init; }
    public int Mojikumi { get; init; }
    public bool Hyphenate { get; init; }
    public bool HangingPunctuation { get; init; }
    public bool MultiLineComposer { get; init; }
    public double WordSpacing { get; init; } = 100;
    public double LetterSpacing { get; init; }
    public int HyphenMinimum { get; init; } = 6;
    public bool HasExtendedLayout => !FractionalWidths || NoBreak || CharacterAlignment != 0 || KinsokuMode != 0 || Mojikumi != 0
        || Hyphenate || HangingPunctuation || MultiLineComposer || WordSpacing != 100 || LetterSpacing != 0 || HyphenMinimum != 6;
    public bool TateChuYoko { get; init; }
    public bool BottomLeading { get; init; }
    public double HorizontalScale { get; init; } = 100;
    public double VerticalScale { get; init; } = 100;
    public double BaselineShift { get; init; }
    public double LeftIndent { get; init; }
    public double RightIndent { get; init; }
    public double FirstLineIndent { get; init; }
    public double SpaceBefore { get; init; }
    public double SpaceAfter { get; init; }
    public TextJustification Justification { get; init; }
    public TextSmoothing Smoothing { get; init; } = TextSmoothing.Smooth;
    public bool Kinsoku { get; init; } = true;
    public double Tsume { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public bool IsValid => double.IsFinite(HorizontalScale) && HorizontalScale is >= 1 and <= 1000
        && double.IsFinite(VerticalScale) && VerticalScale is >= 1 and <= 1000
        && new[] { BaselineShift, FirstLineIndent }.All(v => double.IsFinite(v) && Math.Abs(v) <= 10000)
        && new[] { LeftIndent, RightIndent, SpaceBefore, SpaceAfter }.All(v => double.IsFinite(v) && v is >= 0 and <= 10000)
        && double.IsFinite(Tsume) && Tsume is >= 0 and <= 100 && Enum.IsDefined(Justification) && Enum.IsDefined(Smoothing)
        && CharacterAlignment is >= 0 and <= 5 && KinsokuMode is >= 0 and <= 2 && Mojikumi is >= 0 and <= 4
        && double.IsFinite(WordSpacing) && WordSpacing is >= 0 and <= 1000 && double.IsFinite(LetterSpacing) && LetterSpacing is >= -100 and <= 1000
        && HyphenMinimum is >= 2 and <= 100;
}
public enum TextJustification { None, LastLeft, LastCenter, LastRight, All }
public enum TextSmoothing { None, Sharp, Crisp, Strong, Smooth, WindowsLcd, Windows }
