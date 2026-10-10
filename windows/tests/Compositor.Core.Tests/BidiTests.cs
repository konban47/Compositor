using Compositor.Core.Document;
using Compositor.Core.Format;

namespace Compositor.Core.Tests;

/// <summary>
/// The Unicode Bidirectional Algorithm. The expected levels and orderings are taken from the Unicode
/// conformance data, BidiCharacterTest.txt, so the layout matches the standard on the cases it covers.
/// </summary>
public class BidiTests
{
    private static void Check(string codepoints, TextDirection direction, byte paragraph, string levels, string order)
    {
        var cps = codepoints.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => Convert.ToInt32(value, 16)).ToArray();
        var result = Bidi.Compute(cps, direction);
        Assert.Equal(paragraph, result.ParagraphLevel);
        var expected = levels.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < cps.Length; index++)
        {
            if (expected[index] == "x") continue;
            Assert.Equal(byte.Parse(expected[index]), result.Levels[index]);
        }
        var removed = new bool[cps.Length];
        for (var index = 0; index < cps.Length; index++) removed[index] = Bidi.IsRemoved(cps[index]);
        var visual = Bidi.VisualOrder(result.Levels, removed);
        Assert.Equal(order.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse), visual);
    }

    [Fact]
    public void HebrewAndLatinInALeftToRightParagraph()
    {
        // From UAX #9's own examples: Hebrew with Latin letters and bracketed text.
        Check("05D0 05D1 0028 05D2 05D3 005B 0026 0065 0066 005D 002E 0029 0067 0068",
            TextDirection.LeftToRight, 0,
            "1 1 0 1 1 0 0 0 0 0 0 0 0 0",
            "1 0 2 4 3 5 6 7 8 9 10 11 12 13");
    }

    [Fact]
    public void HebrewAndLatinInARightToLeftParagraph()
    {
        Check("05D0 05D1 0028 05D2 05D3 005B 0026 0065 0066 005D 002E 0029 0067 0068",
            TextDirection.RightToLeft, 1,
            "1 1 1 1 1 1 1 2 2 1 1 1 2 2",
            "12 13 11 10 9 7 8 6 5 4 3 2 1 0");
    }

    [Fact]
    public void AMarkAfterAPairedBracketFollowsTheBracket()
    {
        // Conformance: "Nonspacing marks applied to paired brackets".
        Check("0061 0028 0062 0029 0331", TextDirection.RightToLeft, 1, "2 2 2 2 2", "0 1 2 3 4");
        Check("05D0 0028 05D1 0029 0331", TextDirection.LeftToRight, 0, "1 1 1 1 1", "4 3 2 1 0");
        Check("0661 0028 0662 0029 0331", TextDirection.LeftToRight, 0, "2 1 2 1 1", "4 3 2 1 0");
    }

    [Fact]
    public void AnAutoParagraphFollowsItsFirstStrongCharacter()
    {
        var rtl = Bidi.Compute([0x05D0, 0x0061], TextDirection.Auto);
        Assert.Equal(1, rtl.ParagraphLevel);
        var ltr = Bidi.Compute([0x0061, 0x05D0], TextDirection.Auto);
        Assert.Equal(0, ltr.ParagraphLevel);
        // European digits do not decide it.
        var digits = Bidi.Compute([0x0031, 0x05D0], TextDirection.Auto);
        Assert.Equal(1, digits.ParagraphLevel);
    }

    [Fact]
    public void BracketsAreMirroredForRightToLeftRuns()
    {
        Assert.Equal(0x29, Bidi.Mirror(0x28));
        Assert.Equal(0x28, Bidi.Mirror(0x29));
        Assert.Equal(0x41, Bidi.Mirror(0x41));
    }

    [Fact]
    public void TheAlgorithmIsInTheGeneratedTableAndRemovesFormatters()
    {
        Assert.Equal(Bidi.Kind.AL, Bidi.Classify(0x0627));
        Assert.Equal(Bidi.Kind.R, Bidi.Classify(0x05D0));
        Assert.Equal(Bidi.Kind.L, Bidi.Classify(0x0041));
        Assert.Equal(Bidi.Kind.EN, Bidi.Classify(0x0031));
        Assert.Equal(Bidi.Kind.AN, Bidi.Classify(0x0661));
        Assert.Equal(Bidi.Kind.NSM, Bidi.Classify(0x0331));
        Assert.True(Bidi.IsRemoved(0x202B));
        Assert.True(Bidi.IsRemoved(0x202C));
        Assert.False(Bidi.IsRemoved(0x0627));
    }
}
