using Compositor.Core.Format;

namespace Compositor.Core.Document;

/// <summary>
/// The Unicode Bidirectional Algorithm (UAX #9). It turns a line into an embedding level for every code
/// point: an even level reads left to right, an odd one right to left. The layout code shapes each level's
/// text and lays the runs out in the order this gives, so mixed Arabic, Hebrew, Latin and numbers come out
/// as the standard says. Rules P1–P3, X1–X9, W1–W7, N0–N2, I1–I2 and L1–L4 are implemented.
/// </summary>
internal static class Bidi
{
    /// <summary>The bidi classes; the numbers match the generated table's codes.</summary>
    internal enum Kind : byte
    {
        L = 0, R = 1, AL = 2, EN = 3, ES = 4, ET = 5, AN = 6, CS = 7, NSM = 8, BN = 9,
        B = 10, S = 11, WS = 12, ON = 13, LRE = 14, RLE = 15, PDF = 16, LRO = 17, RLO = 18,
        LRI = 19, RLI = 20, FSI = 21, PDI = 22,
    }

    private const byte MaxDepth = 125;

    /// <summary>The paragraph's level, and the resolved level of every code point of the line.</summary>
    internal readonly record struct Result(byte ParagraphLevel, byte[] Levels);

    /// <summary>The bidi class of a code point, from the generated Unicode table.</summary>
    public static Kind Classify(int codepoint)
    {
        var ranges = BidiData.Classes;
        var low = 0;
        var high = ranges.Length - 1;
        while (low <= high)
        {
            var mid = (low + high) >> 1;
            var range = ranges[mid];
            if (codepoint < range.Start) high = mid - 1;
            else if (codepoint > range.End) low = mid + 1;
            else return (Kind)range.Class;
        }
        return Kind.L;
    }

    public static bool IsRtl(int codepoint) => Classify(codepoint) is Kind.R or Kind.AL;

    /// <summary>Whether a line holds any strong character, so the shaped, reordered layout is worth its cost.</summary>
    public static bool HasStrong(IReadOnlyList<int> codepoints)
    {
        foreach (var codepoint in codepoints)
        {
            if (Classify(codepoint) is Kind.L or Kind.R or Kind.AL) return true;
        }
        return false;
    }

    /// <summary>The mirrored glyph shown in place of a code point when it points the other way (rule L4).</summary>
    public static int Mirror(int codepoint)
    {
        var mirrors = BidiData.Mirrors;
        var low = 0;
        var high = mirrors.Length - 1;
        while (low <= high)
        {
            var mid = (low + high) >> 1;
            if (codepoint < mirrors[mid].Code) high = mid - 1;
            else if (codepoint > mirrors[mid].Code) low = mid + 1;
            else return mirrors[mid].Mirror;
        }
        return codepoint;
    }

    /// <summary>The resolved level of every code point of the line.</summary>
    public static Result Compute(IReadOnlyList<int> codepoints, TextDirection direction)
    {
        var count = codepoints.Count;
        var types = new Kind[count];
        var original = new Kind[count];
        for (var index = 0; index < count; index++)
        {
            types[index] = Classify(codepoints[index]);
            original[index] = types[index];
        }
        var paragraphLevel = direction switch
        {
            TextDirection.LeftToRight => (byte)0,
            TextDirection.RightToLeft => (byte)1,
            _ => ParagraphLevel(types),
        };
        var levels = new byte[count];
        ResolveExplicit(types, levels, paragraphLevel);
        var removed = new bool[count];
        for (var index = 0; index < count; index++) removed[index] = types[index] == Kind.BN;
        var matching = MatchIsolates(original, count);

        foreach (var sequence in IsolatingRunSequences(types, levels, removed, original, matching))
        {
            var sos = sequence.Sos;
            var eos = sequence.Eos;
            var order = sequence.Indexes;
            ResolveWeak(types, order, sos);
            ResolveBrackets(types, levels, order, sos, codepoints, original);
            ResolveNeutral(types, levels, order, sos, eos);
            ResolveImplicit(types, levels, order);
        }
        ResolveL1(types, levels, removed, paragraphLevel);
        return new Result(paragraphLevel, levels);
    }

    /// <summary>Whether rule X9 removes a code point (explicit formatting and boundary neutrals).</summary>
    public static bool IsRemoved(int codepoint) => Classify(codepoint) is
        Kind.RLE or Kind.LRE or Kind.RLO or Kind.LRO or Kind.PDF or Kind.BN;

    /// <summary>
    /// Rule L2: the visual order of the code points, in place of their logical order. Characters X9 removed
    /// are left out, since the standard takes them out of the line before reordering.
    /// </summary>
    public static int[] VisualOrder(byte[] levels, bool[]? removed = null)
    {
        var order = new List<int>(levels.Length);
        for (var index = 0; index < levels.Length; index++)
        {
            if (removed is null || !removed[index]) order.Add(index);
        }
        var highest = 0;
        var lowestOdd = byte.MaxValue;
        foreach (var index in order)
        {
            var level = levels[index];
            highest = Math.Max(highest, level);
            if ((level & 1) == 1) lowestOdd = Math.Min(lowestOdd, level);
        }
        for (var level = highest; level >= lowestOdd; level--)
        {
            var index = 0;
            while (index < order.Count)
            {
                if (levels[order[index]] < level) { index++; continue; }
                var start = index;
                while (index < order.Count && levels[order[index]] >= level) index++;
                order.Reverse(start, index - start);
            }
        }
        return [.. order];
    }

    // P2/P3: the first strong character decides, skipping what is inside isolates.
    private static byte ParagraphLevel(Kind[] types)
    {
        foreach (var type in types)
        {
            if (type == Kind.L) return 0;
            if (type is Kind.R or Kind.AL) return 1;
        }
        return 0;
    }

    // X1–X8: explicit levels and overrides, with the directional status stack.
    private static void ResolveExplicit(Kind[] types, byte[] levels, byte paragraphLevel)
    {
        var stack = new List<(byte Level, Kind Override, bool Isolate)> { (paragraphLevel, Kind.ON, false) };
        var overflowIsolate = 0;
        var overflowEmbedding = 0;
        var validIsolate = 0;
        for (var index = 0; index < types.Length; index++)
        {
            var type = types[index];
            switch (type)
            {
                case Kind.RLE:
                case Kind.LRE:
                case Kind.RLO:
                case Kind.LRO:
                    levels[index] = stack[^1].Level;
                    if (overflowIsolate == 0 && overflowEmbedding == 0)
                    {
                        var next = type is Kind.RLE or Kind.RLO ? NextOdd(stack[^1].Level) : NextEven(stack[^1].Level);
                        if (next <= MaxDepth)
                        {
                            var over = type == Kind.RLO ? Kind.R : type == Kind.LRO ? Kind.L : Kind.ON;
                            stack.Add((next, over, false));
                        }
                        else overflowEmbedding++;
                    }
                    else overflowEmbedding++;
                    types[index] = Kind.BN;
                    break;
                case Kind.RLI:
                case Kind.LRI:
                case Kind.FSI:
                    levels[index] = stack[^1].Level;
                    if (overflowIsolate == 0 && overflowEmbedding == 0)
                    {
                        var rtl = type == Kind.RLI || (type == Kind.FSI && FsiIsRtl(types, index + 1) == 1);
                        var next = rtl ? NextOdd(stack[^1].Level) : NextEven(stack[^1].Level);
                        if (next <= MaxDepth)
                        {
                            validIsolate++;
                            stack.Add((next, Kind.ON, true));
                        }
                        else overflowIsolate++;
                    }
                    else overflowIsolate++;
                    break;
                case Kind.PDI:
                    if (overflowIsolate > 0) overflowIsolate--;
                    else if (validIsolate > 0)
                    {
                        overflowEmbedding = 0;
                        while (!stack[^1].Isolate) stack.RemoveAt(stack.Count - 1);
                        stack.RemoveAt(stack.Count - 1);
                        validIsolate--;
                    }
                    levels[index] = stack[^1].Level;
                    break;
                case Kind.PDF:
                    if (overflowIsolate > 0) { }
                    else if (overflowEmbedding > 0) overflowEmbedding--;
                    else if (!stack[^1].Isolate && stack.Count >= 2) stack.RemoveAt(stack.Count - 1);
                    levels[index] = stack[^1].Level;
                    types[index] = Kind.BN;
                    break;
                case Kind.B:
                    levels[index] = paragraphLevel;
                    break;
                default:
                    levels[index] = stack[^1].Level;
                    if (stack[^1].Override != Kind.ON) types[index] = stack[^1].Override;
                    break;
            }
        }
    }

    private static byte NextOdd(byte level) => (byte)((level + 1) | 1);
    private static byte NextEven(byte level) => (byte)((level + 2) & ~1);

    // P2/P3 for an FSI: the first strong character inside the isolate.
    private static int FsiIsRtl(Kind[] types, int from)
    {
        var depth = 0;
        for (var index = from; index < types.Length; index++)
        {
            switch (types[index])
            {
                case Kind.LRI:
                case Kind.RLI:
                case Kind.FSI: depth++; break;
                case Kind.PDI when depth == 0: return -1;
                case Kind.PDI: depth--; break;
                case Kind.L when depth == 0: return 0;
                case Kind.R:
                case Kind.AL when depth == 0: return 1;
            }
        }
        return -1;
    }

    // BD9: each isolate initiator and its matching PDI.
    private static int[] MatchIsolates(Kind[] types, int count)
    {
        var matching = new int[count];
        Array.Fill(matching, -1);
        var stack = new Stack<int>();
        for (var index = 0; index < count; index++)
        {
            switch (types[index])
            {
                case Kind.LRI:
                case Kind.RLI:
                case Kind.FSI: stack.Push(index); break;
                case Kind.PDI when stack.Count > 0:
                {
                    var open = stack.Pop();
                    matching[open] = index;
                    matching[index] = open;
                    break;
                }
            }
        }
        return matching;
    }

    private readonly record struct Sequence(int[] Indexes, Kind Sos, Kind Eos);

    // BD13/BD14: the isolating run sequences their rules apply to, with the surrounding strong directions.
    private static IEnumerable<Sequence> IsolatingRunSequences(Kind[] types, byte[] levels, bool[] removed,
        Kind[] original, int[] matching)
    {
        var active = new List<int>();
        for (var index = 0; index < types.Length; index++)
        {
            if (!removed[index]) active.Add(index);
        }
        if (active.Count == 0) yield break;
        var runs = new List<List<int>>();
        var runAt = new int[types.Length];
        Array.Fill(runAt, -1);
        foreach (var index in active)
        {
            if (runs.Count == 0 || levels[index] != levels[runs[^1][^1]]) runs.Add([]);
            runs[^1].Add(index);
            runAt[index] = runs.Count - 1;
        }
        var visited = new bool[runs.Count];
        for (var start = 0; start < runs.Count; start++)
        {
            if (visited[start]) continue;
            var indexes = new List<int>();
            var current = start;
            while (true)
            {
                visited[current] = true;
                indexes.AddRange(runs[current]);
                var last = runs[current][^1];
                if (original[last] is Kind.LRI or Kind.RLI or Kind.FSI && matching[last] >= 0)
                {
                    var next = runAt[matching[last]];
                    if (next >= 0 && !visited[next]) { current = next; continue; }
                }
                break;
            }
            var firstIndex = indexes[0];
            var lastIndex = indexes[^1];
            var level = levels[firstIndex];
            // The character just before and after the sequence, removed ones included: X9 gave them levels too.
            var beforeLevel = firstIndex > 0 ? levels[firstIndex - 1] : level;
            var afterLevel = lastIndex + 1 < levels.Length ? levels[lastIndex + 1] : level;
            var sos = (Math.Max(level, beforeLevel) & 1) == 1 ? Kind.R : Kind.L;
            var eos = (Math.Max(level, afterLevel) & 1) == 1 ? Kind.R : Kind.L;
            yield return new Sequence([.. indexes], sos, eos);
        }
    }

    // W1–W7.
    private static void ResolveWeak(Kind[] types, int[] order, Kind sos)
    {
        // W1: a non-spacing mark takes the type before it.
        var previous = sos;
        foreach (var index in order)
        {
            if (types[index] == Kind.NSM) types[index] = previous;
            previous = types[index];
        }
        // W2: an EN becomes AN after an AL.
        var lastStrong = sos;
        foreach (var index in order)
        {
            if (types[index] is Kind.R or Kind.L or Kind.AL) lastStrong = types[index];
            else if (types[index] == Kind.EN && lastStrong == Kind.AL) types[index] = Kind.AN;
        }
        // W3: an AL becomes R.
        foreach (var index in order)
        {
            if (types[index] == Kind.AL) types[index] = Kind.R;
        }
        // W4: a separator between two numbers takes the number's type.
        for (var at = 1; at < order.Length - 1; at++)
        {
            var index = order[at];
            if (types[index] == Kind.ES && types[order[at - 1]] == Kind.EN && types[order[at + 1]] == Kind.EN) types[index] = Kind.EN;
            else if (types[index] == Kind.CS && types[order[at - 1]] == Kind.EN && types[order[at + 1]] == Kind.EN) types[index] = Kind.EN;
            else if (types[index] == Kind.CS && types[order[at - 1]] == Kind.AN && types[order[at + 1]] == Kind.AN) types[index] = Kind.AN;
        }
        // W5: a run of terminators next to a number becomes a number.
        for (var at = 0; at < order.Length; at++)
        {
            if (types[order[at]] != Kind.ET) continue;
            var end = at;
            while (end < order.Length && types[order[end]] == Kind.ET) end++;
            var before = at > 0 && types[order[at - 1]] == Kind.EN;
            var after = end < order.Length && types[order[end]] == Kind.EN;
            if (before || after)
            {
                for (var fill = at; fill < end; fill++) types[order[fill]] = Kind.EN;
            }
            at = end - 1;
        }
        // W6: what is left of the separators and terminators is neutral.
        foreach (var index in order)
        {
            if (types[index] is Kind.ET or Kind.ES or Kind.CS) types[index] = Kind.ON;
        }
        // W7: an EN becomes L after an L.
        lastStrong = sos;
        foreach (var index in order)
        {
            if (types[index] is Kind.R or Kind.L) lastStrong = types[index];
            else if (types[index] == Kind.EN && lastStrong == Kind.L) types[index] = Kind.L;
        }
    }

    // N0: paired brackets take a direction from what they hold.
    private static void ResolveBrackets(Kind[] types, byte[] levels, int[] order, Kind sos,
        IReadOnlyList<int> codepoints, Kind[] original)
    {
        var stack = new List<(int Pair, int Position)>();
        var pairs = new List<(int Open, int Close)>();
        for (var at = 0; at < order.Length; at++)
        {
            var index = order[at];
            if (types[index] != Kind.ON) continue;
            if (!TryBracket(codepoints[index], out var pair, out var open)) continue;
            if (open)
            {
                if (stack.Count >= 63) continue;
                stack.Add((pair, at));
            }
            else
            {
                for (var depth = stack.Count - 1; depth >= 0; depth--)
                {
                    if (stack[depth].Pair != codepoints[index]) continue;
                    pairs.Add((stack[depth].Position, at));
                    stack.RemoveRange(depth, stack.Count - depth);
                    break;
                }
            }
        }
        pairs.Sort((left, right) => left.Open.CompareTo(right.Open));
        foreach (var (openAt, closeAt) in pairs)
        {
            var openIndex = order[openAt];
            var closeIndex = order[closeAt];
            if (types[openIndex] != Kind.ON || types[closeIndex] != Kind.ON) continue;
            var embedding = (levels[openIndex] & 1) == 1 ? Kind.R : Kind.L;
            var opposite = embedding == Kind.L ? Kind.R : Kind.L;
            var matchesEmbedding = false;
            var matchesOpposite = false;
            for (var at = openAt + 1; at < closeAt; at++)
            {
                var strong = StrongOnly(types[order[at]]);
                if (strong == embedding) { matchesEmbedding = true; break; }
                if (strong == opposite) matchesOpposite = true;
            }
            Kind resolved;
            if (matchesEmbedding)
            {
                resolved = embedding;
            }
            else if (matchesOpposite)
            {
                var before = sos;
                for (var at = openAt - 1; at >= 0; at--)
                {
                    var strong = StrongOnly(types[order[at]]);
                    if (strong != Kind.ON) { before = strong; break; }
                }
                resolved = before == opposite ? opposite : embedding;
            }
            else continue;
            types[openIndex] = resolved;
            types[closeIndex] = resolved;
            // A mark that followed a bracket before W1 follows the direction the bracket was given.
            PropagateMark(types, order, openAt, resolved, original);
            PropagateMark(types, order, closeAt, resolved, original);
        }
    }

    // The N0 note: marks that followed a bracket (type NSM before W1) take the bracket's new direction.
    private static void PropagateMark(Kind[] types, int[] order, int bracketAt, Kind resolved, Kind[] original)
    {
        for (var at = bracketAt + 1; at < order.Length; at++)
        {
            if (original[order[at]] != Kind.NSM) break;
            types[order[at]] = resolved;
        }
    }

    // The paired bracket for a code point, and whether it opens. Canonically equivalent brackets are folded.
    private static bool TryBracket(int codepoint, out int pair, out bool open)
    {
        pair = 0;
        open = false;
        if (codepoint == 0x2329) codepoint = 0x3008;
        else if (codepoint == 0x232A) codepoint = 0x3009;
        var brackets = BidiData.Brackets;
        var low = 0;
        var high = brackets.Length - 1;
        while (low <= high)
        {
            var mid = (low + high) >> 1;
            if (codepoint < brackets[mid].Code) high = mid - 1;
            else if (codepoint > brackets[mid].Code) low = mid + 1;
            else
            {
                pair = brackets[mid].Pair;
                open = brackets[mid].Open == 0;
                return true;
            }
        }
        return false;
    }

    private static Kind StrongOnly(Kind type) => type switch
    {
        Kind.L => Kind.L,
        Kind.R or Kind.EN or Kind.AN => Kind.R,
        _ => Kind.ON,
    };

    // N1/N2.
    private static void ResolveNeutral(Kind[] types, byte[] levels, int[] order, Kind sos, Kind eos)
    {
        for (var at = 0; at < order.Length; at++)
        {
            if (!IsNeutral(types[order[at]])) continue;
            var end = at;
            while (end < order.Length && IsNeutral(types[order[end]])) end++;
            var before = at > 0 ? StrongOf(types[order[at - 1]]) : sos;
            var after = end < order.Length ? StrongOf(types[order[end]]) : eos;
            var embedding = (levels[order[at]] & 1) == 1 ? Kind.R : Kind.L;
            var set = before == after ? before : embedding;
            for (var fill = at; fill < end; fill++) types[order[fill]] = set;
            at = end - 1;
        }
    }

    private static bool IsNeutral(Kind type) =>
        type is Kind.B or Kind.S or Kind.WS or Kind.ON or Kind.FSI or Kind.LRI or Kind.RLI or Kind.PDI;

    private static Kind StrongOf(Kind type) => type is Kind.R or Kind.EN or Kind.AN ? Kind.R : Kind.L;

    // I1/I2.
    private static void ResolveImplicit(Kind[] types, byte[] levels, int[] order)
    {
        foreach (var index in order)
        {
            var level = levels[index];
            var type = types[index];
            if ((level & 1) == 0)
            {
                if (type == Kind.R) levels[index] = (byte)(level + 1);
                else if (type is Kind.EN or Kind.AN) levels[index] = (byte)(level + 2);
            }
            else if (type is Kind.L or Kind.EN or Kind.AN) levels[index] = (byte)(level + 1);
        }
    }

    // L1: separators and trailing whitespace fall back to the paragraph level.
    private static void ResolveL1(Kind[] types, byte[] levels, bool[] removed, byte paragraphLevel)
    {
        for (var index = 0; index < types.Length; index++)
        {
            if (!removed[index] && types[index] is Kind.S or Kind.B) levels[index] = paragraphLevel;
        }
        for (var index = types.Length - 1; index >= 0; index--)
        {
            if (removed[index]) { levels[index] = paragraphLevel; continue; }
            if (types[index] is Kind.WS or Kind.LRI or Kind.RLI or Kind.FSI or Kind.PDI) levels[index] = paragraphLevel;
            else break;
        }
    }
}
