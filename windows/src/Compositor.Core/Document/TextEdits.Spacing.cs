using Compositor.Core.Format;
namespace Compositor.Core.Document;

public static partial class TextEdits
{
    private static double AdjustAdvance(string text, double measured, LayerTextStyle style)
    {
        var layout = style.Layout ?? new();
        var space = text.All(char.IsWhiteSpace);
        var width = measured * (space ? layout.WordSpacing / 100 : 1) + style.Tracking + style.FontSize * layout.LetterSpacing / 100;
        if (IsCjk(text)) width -= style.FontSize * layout.Tsume / 100;
        if (layout.Mojikumi > 0 && text.Any(c => NoLineStart(c) || NoLineEnd(c))) width -= measured * new[] { 0, .25, .5, .375, .125 }[layout.Mojikumi];
        width = Math.Max(0, width);
        return layout.FractionalWidths ? width : Math.Round(width);
    }
    private static float AlignmentShift(LayerTextStyle run, LayerTextStyle paragraph)
    {
        var alignment = run.Layout?.CharacterAlignment ?? 0;
        if (alignment == 0) return 0;
        var a = Metrics(run.FontName, run.FontSize); var b = Metrics(paragraph.FontName, paragraph.FontSize);
        return alignment switch { 1 => b.Ascent - a.Ascent, 2 => (b.Ascent + b.Descent - a.Ascent - a.Descent) / 2,
            3 => b.Descent - a.Descent, 4 => (float)(run.FontSize - paragraph.FontSize) * .88f, _ => (float)(paragraph.FontSize - run.FontSize) * .12f };
    }
}
