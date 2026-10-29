using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Compositor.Core.IO;

/// <summary>The slide change effects File ▸ Automate ▸ PDF Presentation offers, mapped to PDF /Trans dictionaries.</summary>
public enum PdfTransition
{
    None, BlindsHorizontal, BlindsVertical, BoxIn, BoxOut, Dissolve, GlitterDown, GlitterRight, GlitterRightDown, Random,
    SplitHorizontalIn, SplitHorizontalOut, SplitVerticalIn, SplitVerticalOut, WipeDown, WipeLeft, WipeRight, WipeUp,
}

/// <summary>
/// Presentation properties added to a finished PDF as one incremental update: every page gets its display
/// duration (/Dur) and transition (/Trans), and the catalog opens full screen. Looping has no standard PDF entry;
/// it is requested with the Acrobat full-screen JavaScript object, which viewers without JavaScript ignore.
/// </summary>
public static class PdfSlideshow
{
    public const double MinimumSeconds = 1;
    public const double MaximumSeconds = 3600;

    private static readonly Encoding Bytes = Encoding.Latin1;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static string TransitionName(PdfTransition value) => value switch
    {
        PdfTransition.BlindsHorizontal => "Blinds Horizontal",
        PdfTransition.BlindsVertical => "Blinds Vertical",
        PdfTransition.BoxIn => "Box In",
        PdfTransition.BoxOut => "Box Out",
        PdfTransition.Dissolve => "Dissolve",
        PdfTransition.GlitterDown => "Glitter Down",
        PdfTransition.GlitterRight => "Glitter Right",
        PdfTransition.GlitterRightDown => "Glitter Right-Down",
        PdfTransition.Random => "Random Transition",
        PdfTransition.SplitHorizontalIn => "Split Horizontal In",
        PdfTransition.SplitHorizontalOut => "Split Horizontal Out",
        PdfTransition.SplitVerticalIn => "Split Vertical In",
        PdfTransition.SplitVerticalOut => "Split Vertical Out",
        PdfTransition.WipeDown => "Wipe Down",
        PdfTransition.WipeLeft => "Wipe Left",
        PdfTransition.WipeRight => "Wipe Right",
        PdfTransition.WipeUp => "Wipe Up",
        _ => "None",
    };

    /// <summary>The /Trans dictionary, or null for an instant page change. Random is resolved by the caller.</summary>
    public static string? TransitionDictionary(PdfTransition value) => value switch
    {
        PdfTransition.BlindsHorizontal => "<< /Type /Trans /S /Blinds /Dm /H /D 1 >>",
        PdfTransition.BlindsVertical => "<< /Type /Trans /S /Blinds /Dm /V /D 1 >>",
        PdfTransition.BoxIn => "<< /Type /Trans /S /Box /M /I /D 1 >>",
        PdfTransition.BoxOut => "<< /Type /Trans /S /Box /M /O /D 1 >>",
        PdfTransition.Dissolve => "<< /Type /Trans /S /Dissolve /D 1 >>",
        PdfTransition.GlitterDown => "<< /Type /Trans /S /Glitter /Di 270 /D 1 >>",
        PdfTransition.GlitterRight => "<< /Type /Trans /S /Glitter /Di 0 /D 1 >>",
        PdfTransition.GlitterRightDown => "<< /Type /Trans /S /Glitter /Di 315 /D 1 >>",
        PdfTransition.SplitHorizontalIn => "<< /Type /Trans /S /Split /Dm /H /M /I /D 1 >>",
        PdfTransition.SplitHorizontalOut => "<< /Type /Trans /S /Split /Dm /H /M /O /D 1 >>",
        PdfTransition.SplitVerticalIn => "<< /Type /Trans /S /Split /Dm /V /M /I /D 1 >>",
        PdfTransition.SplitVerticalOut => "<< /Type /Trans /S /Split /Dm /V /M /O /D 1 >>",
        PdfTransition.WipeDown => "<< /Type /Trans /S /Wipe /Di 270 /D 1 >>",
        PdfTransition.WipeLeft => "<< /Type /Trans /S /Wipe /Di 180 /D 1 >>",
        PdfTransition.WipeRight => "<< /Type /Trans /S /Wipe /Di 0 /D 1 >>",
        PdfTransition.WipeUp => "<< /Type /Trans /S /Wipe /Di 90 /D 1 >>",
        _ => null,
    };

    public static void Validate(PdfPresentationOptions options)
    {
        if (!Enum.IsDefined(options.Transition) || !double.IsFinite(options.AdvanceSeconds)
            || options.AdvanceSeconds is < MinimumSeconds or > MaximumSeconds)
            throw new ArgumentException("Invalid presentation options.");
    }

    /// <summary>Returns the PDF unchanged for a multi-page document, otherwise with the presentation update appended.</summary>
    public static byte[] Apply(byte[] pdf, PdfPresentationOptions options, int seed = 1)
    {
        if (!options.Presentation) return pdf;
        Validate(options);
        var text = Bytes.GetString(pdf);
        var objects = ReadXref(text, out var trailer);
        var root = Reference(trailer, "Root") ?? throw new InvalidDataException("The PDF has no catalog.");
        var info = Reference(trailer, "Info");
        var size = Number(trailer, "Size") ?? throw new InvalidDataException("The PDF trailer has no size.");
        var previous = StartXref(text);

        var random = new Random(seed);
        var choices = Enum.GetValues<PdfTransition>().Where(t => t is not (PdfTransition.None or PdfTransition.Random)).ToArray();
        var updated = new SortedDictionary<int, (int Generation, string Body)>();
        foreach (var (number, (offset, generation)) in objects)
        {
            var body = Body(text, number, offset);
            if (!IsDictionary(body)) continue;
            if (number == root)
            {
                var catalog = Regex.Replace(body, @"/PageMode\s*/[A-Za-z]+", "", RegexOptions.None, Timeout);
                var extra = " /PageMode /FullScreen";
                if (options.Loop && !Regex.IsMatch(catalog, @"/OpenAction\b", RegexOptions.None, Timeout))
                {
                    var seconds = options.AdvanceSeconds.ToString("0.###", CultureInfo.InvariantCulture);
                    var script = options.AutoAdvance
                        ? $"app.fs.loop = true; app.fs.useTimer = true; app.fs.timeDelay = {seconds}; app.fs.isFullScreen = true;"
                        : "app.fs.loop = true; app.fs.isFullScreen = true;";
                    extra += " /OpenAction << /S /JavaScript /JS (" + script + ") >>";
                }
                updated[number] = (generation, Insert(catalog, extra));
            }
            else if (Regex.IsMatch(body, @"/Type\s*/Page(?![A-Za-z])", RegexOptions.None, Timeout))
            {
                var page = Regex.Replace(body, @"/Dur\s+[0-9.]+", "", RegexOptions.None, Timeout);
                var extra = "";
                if (options.AutoAdvance) extra += " /Dur " + options.AdvanceSeconds.ToString("0.###", CultureInfo.InvariantCulture);
                var transition = options.Transition == PdfTransition.Random ? choices[random.Next(choices.Length)] : options.Transition;
                if (TransitionDictionary(transition) is { } trans && !Regex.IsMatch(page, @"/Trans\b", RegexOptions.None, Timeout)) extra += " /Trans " + trans;
                if (extra.Length > 0) updated[number] = (generation, Insert(page, extra));
            }
        }
        if (!updated.ContainsKey(root)) throw new InvalidDataException("The PDF catalog could not be updated.");
        if (updated.Count < 2 && options.AutoAdvance) throw new InvalidDataException("The PDF pages could not be updated.");

        var output = new StringBuilder(text);
        if (!text.EndsWith('\n')) output.Append('\n');
        var offsets = new List<(int Number, int Generation, long Offset)>();
        foreach (var (number, (generation, body)) in updated)
        {
            offsets.Add((number, generation, output.Length));
            output.Append(number.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(generation.ToString(CultureInfo.InvariantCulture))
                .Append(" obj\n").Append(body.Trim()).Append("\nendobj\n");
        }
        var xref = output.Length;
        output.Append("xref\n");
        foreach (var (number, generation, offset) in offsets)
            output.Append(number.ToString(CultureInfo.InvariantCulture)).Append(" 1\n")
                .Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(' ')
                .Append(generation.ToString("D5", CultureInfo.InvariantCulture)).Append(" n\r\n");
        output.Append("trailer\n<< /Size ").Append(size.ToString(CultureInfo.InvariantCulture))
            .Append(" /Root ").Append(root.ToString(CultureInfo.InvariantCulture)).Append(" 0 R");
        if (info is { } infoNumber) output.Append(" /Info ").Append(infoNumber.ToString(CultureInfo.InvariantCulture)).Append(" 0 R");
        output.Append(" /Prev ").Append(previous.ToString(CultureInfo.InvariantCulture)).Append(" >>\nstartxref\n")
            .Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");
        return Bytes.GetBytes(output.ToString());
    }

    private static bool IsDictionary(string body)
    {
        var trimmed = body.Trim();
        return trimmed.StartsWith("<<", StringComparison.Ordinal) && trimmed.EndsWith(">>", StringComparison.Ordinal)
            && !Regex.IsMatch(trimmed, @">>\s*stream\b", RegexOptions.None, Timeout);
    }

    private static string Insert(string dictionary, string entries)
    {
        var trimmed = dictionary.Trim();
        return trimmed[..^2] + entries + " >>";
    }

    private static string Body(string text, int number, long offset)
    {
        if (offset < 0 || offset >= text.Length) throw new InvalidDataException("The PDF cross-reference table is damaged.");
        var header = new Regex(@"\G\s*(\d+)\s+(\d+)\s+obj\b", RegexOptions.None, Timeout).Match(text, (int)offset);
        if (!header.Success || int.Parse(header.Groups[1].Value, CultureInfo.InvariantCulture) != number)
            throw new InvalidDataException("The PDF cross-reference table is damaged.");
        var start = header.Index + header.Length;
        var end = text.IndexOf("endobj", start, StringComparison.Ordinal);
        if (end < 0) throw new InvalidDataException("The PDF object is incomplete.");
        return text[start..end];
    }

    private static long StartXref(string text)
    {
        var match = Regex.Match(text, @"startxref\s+(\d+)\s*%%EOF\s*$", RegexOptions.RightToLeft, Timeout);
        if (!match.Success) throw new InvalidDataException("The PDF has no cross-reference position.");
        return long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>The newest classic cross-reference section and its trailer. Cross-reference streams are refused.</summary>
    private static Dictionary<int, (long Offset, int Generation)> ReadXref(string text, out string trailer)
    {
        var position = StartXref(text);
        if (position < 0 || position >= text.Length || string.CompareOrdinal(text, (int)position, "xref", 0, 4) != 0)
            throw new InvalidDataException("Only PDFs with a classic cross-reference table can be made into presentations.");
        var result = new Dictionary<int, (long, int)>();
        var at = (int)position + 4;
        var section = new Regex(@"\G\s*(\d+)\s+(\d+)[ \t]*\r?\n", RegexOptions.None, Timeout);
        var entry = new Regex(@"\G\s*(\d{10})\s+(\d{5})\s+([nf])", RegexOptions.None, Timeout);
        while (true)
        {
            var header = section.Match(text, at);
            if (!header.Success) break;
            at = header.Index + header.Length;
            int first = int.Parse(header.Groups[1].Value, CultureInfo.InvariantCulture), count = int.Parse(header.Groups[2].Value, CultureInfo.InvariantCulture);
            for (var index = 0; index < count; index++)
            {
                var row = entry.Match(text, at);
                if (!row.Success) throw new InvalidDataException("The PDF cross-reference table is damaged.");
                at = row.Index + row.Length;
                if (row.Groups[3].Value == "n")
                    result[first + index] = (long.Parse(row.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(row.Groups[2].Value, CultureInfo.InvariantCulture));
            }
        }
        var trailerMatch = new Regex(@"\G\s*trailer\s*(<<.*?>>)\s*startxref", RegexOptions.Singleline, Timeout).Match(text, at);
        if (!trailerMatch.Success) throw new InvalidDataException("The PDF trailer is missing.");
        trailer = trailerMatch.Groups[1].Value;
        if (Regex.IsMatch(trailer, @"/Prev\b", RegexOptions.None, Timeout))
            throw new InvalidDataException("Incrementally updated PDFs are not supported here.");
        return result;
    }

    private static int? Reference(string dictionary, string key)
    {
        var match = Regex.Match(dictionary, "/" + key + @"\s+(\d+)\s+\d+\s+R", RegexOptions.None, Timeout);
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    private static int? Number(string dictionary, string key)
    {
        var match = Regex.Match(dictionary, "/" + key + @"\s+(\d+)", RegexOptions.None, Timeout);
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }
}
