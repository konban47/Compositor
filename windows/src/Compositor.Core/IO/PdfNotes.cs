using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Compositor.Core.Format;
using Compositor.Core.Model;

namespace Compositor.Core.IO;

/// <summary>A text annotation as PDF keeps it: its page, its rectangle in points (origin bottom left), what it says,
/// who wrote it and its color.</summary>
public sealed record PdfNote(int Page, double Left, double Bottom, double Right, double Top, string Text, string Author, uint Color);

/// <summary>
/// File › Import › Notes from PDF, FDF and XFDF (Acrobat's comment exchange formats), and the same notes written as FDF
/// or XFDF, or into a PDF as text annotations. Positions are kept: a PDF page's media box maps onto the document; FDF
/// and XFDF, which have no page size, are read in points at the document's resolution.
/// </summary>
public static class PdfNotes
{
    private const int MaxBytes = 64 * 1024 * 1024;
    private const int MaxNotes = 1000;

    /// <summary>Notes from a .pdf, .fdf or .xfdf file, by its content rather than its name.</summary>
    public static (IReadOnlyList<PdfNote> Notes, IReadOnlyList<(double Left, double Bottom, double Right, double Top)> Pages) Read(byte[] bytes)
    {
        if (bytes.Length > MaxBytes) throw new InvalidDataException("The notes file is too large.");
        var head = Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, 1024)).TrimStart('﻿', ' ', '\r', '\n', '\t');
        if (head.StartsWith("<?xml", StringComparison.Ordinal) || head.StartsWith("<xfdf", StringComparison.Ordinal)) return (ReadXfdf(bytes), []);
        if (!head.StartsWith("%PDF", StringComparison.Ordinal) && !head.StartsWith("%FDF", StringComparison.Ordinal))
            throw new InvalidDataException("This is not a PDF, FDF or XFDF file.");
        var objects = new PdfObjects(bytes);
        var notes = new List<PdfNote>();
        var pages = new List<(double, double, double, double)>();
        if (objects.Catalog() is { } catalog && objects.Resolve(catalog.Get("Pages")) is Dictionary<string, object?> tree)
        {
            foreach (var (page, box) in objects.Pages(tree))
            {
                pages.Add(box);
                if (objects.Resolve(page.Get("Annots")) is List<object?> annots)
                    foreach (var annot in annots) Add(objects.Resolve(annot) as Dictionary<string, object?>, pages.Count - 1);
            }
        }
        else if (objects.FirstWith("FDF") is { } fdf && objects.Resolve(fdf.Get("FDF")) is Dictionary<string, object?> body && objects.Resolve(body.Get("Annots")) is List<object?> list)
        {
            foreach (var annot in list)
                if (objects.Resolve(annot) is Dictionary<string, object?> dictionary)
                    Add(dictionary, objects.Resolve(dictionary.Get("Page")) is double page ? (int)page : 0);
        }
        return (notes, pages);

        void Add(Dictionary<string, object?>? annot, int page)
        {
            if (annot is null || notes.Count >= MaxNotes) return;
            if (objects.Resolve(annot.Get("Subtype")) is not PdfName { Value: "Text" or "FreeText" }) return;
            if (objects.Resolve(annot.Get("Rect")) is not List<object?> { Count: 4 } rect) return;
            var r = rect.Select(v => objects.Resolve(v) is double d ? d : double.NaN).ToArray();
            if (r.Any(v => !double.IsFinite(v))) return;
            var text = objects.Resolve(annot.Get("Contents")) is byte[] contents ? PdfObjects.Text(contents) : "";
            var author = objects.Resolve(annot.Get("T")) is byte[] title ? PdfObjects.Text(title) : "";
            var color = 0xFFFFD54Fu;
            if (objects.Resolve(annot.Get("C")) is List<object?> { Count: 3 } c && c.Select(objects.Resolve).All(v => v is double))
                color = 0xFF000000u | (uint)Channel(c[0]) << 16 | (uint)Channel(c[1]) << 8 | (uint)Channel(c[2]);
            notes.Add(new PdfNote(page, Math.Min(r[0], r[2]), Math.Min(r[1], r[3]), Math.Max(r[0], r[2]), Math.Max(r[1], r[3]), text, author, color));
            int Channel(object? v) => (int)Math.Clamp(Math.Round((double)objects.Resolve(v)! * 255), 0, 255);
        }
    }

    private static List<PdfNote> ReadXfdf(byte[] bytes)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxBytes };
        using var reader = XmlReader.Create(new MemoryStream(bytes), settings);
        var document = XDocument.Load(reader);
        var notes = new List<PdfNote>();
        foreach (var element in document.Descendants().Where(e => e.Name.LocalName is "text" or "freetext"))
        {
            if (notes.Count >= MaxNotes) break;
            var rect = (string?)element.Attribute("rect");
            var parts = rect?.Split(',').Select(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN).ToArray();
            if (parts is not { Length: 4 } || parts.Any(v => !double.IsFinite(v))) continue;
            var page = int.TryParse((string?)element.Attribute("page"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 0;
            var color = 0xFFFFD54Fu;
            if ((string?)element.Attribute("color") is { Length: 7 } hex && hex[0] == '#' && uint.TryParse(hex[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
                color = 0xFF000000u | rgb;
            var contents = element.Elements().FirstOrDefault(e => e.Name.LocalName == "contents")?.Value
                ?? element.Elements().FirstOrDefault(e => e.Name.LocalName == "contents-richtext")?.Value ?? "";
            notes.Add(new PdfNote(page, Math.Min(parts[0], parts[2]), Math.Min(parts[1], parts[3]), Math.Max(parts[0], parts[2]), Math.Max(parts[1], parts[3]),
                contents, (string?)element.Attribute("title") ?? "", color));
        }
        return notes;
    }

    /// <summary>
    /// The notes on <paramref name="page"/> as the document's notes: a PDF page's media box stretched over the
    /// document, or, without one, points at the document's resolution measured from its top left.
    /// </summary>
    public static DocumentMark[] ToMarks(IReadOnlyList<PdfNote> notes, IReadOnlyList<(double Left, double Bottom, double Right, double Top)> pages,
        int width, int height, double resolution, int page = 0)
    {
        var marks = new List<DocumentMark>();
        foreach (var note in notes.Where(n => n.Page == page))
        {
            double x, y;
            if (page < pages.Count && pages[page] is var box && box.Right > box.Left && box.Top > box.Bottom)
            {
                x = (note.Left - box.Left) / (box.Right - box.Left) * width;
                y = (box.Top - note.Top) / (box.Top - box.Bottom) * height;
            }
            else
            {
                var scale = resolution / 72;
                x = note.Left * scale; y = height - note.Top * scale;
            }
            var mark = new DocumentMark
            {
                Kind = MarkKind.Note, X = Math.Clamp(x, 0, width), Y = Math.Clamp(y, 0, height),
                Text = note.Text.Length > 8192 ? note.Text[..8192] : note.Text, Author = note.Author.Length > 256 ? note.Author[..256] : note.Author, Color = note.Color,
            };
            if (mark.IsValid) marks.Add(mark);
        }
        return [.. marks];
    }

    private static (double Left, double Bottom, double Right, double Top) Rect(DocumentMark note, int height, double resolution)
    {
        var scale = 72 / resolution;
        var left = note.X * scale; var top = (height - note.Y) * scale;
        return (left, top - 18, left + 18, top);
    }

    /// <summary>The document's notes as an FDF file, which Acrobat imports as comments on page 1.</summary>
    public static byte[] WriteFdf(IEnumerable<DocumentMark> marks, int height, double resolution)
    {
        var notes = Notes(marks);
        var text = new StringBuilder("%FDF-1.2\n%âãÏÓ\n");
        var offsets = new List<int>();
        var builder = new StringBuilder();
        builder.Append("1 0 obj\n<< /FDF << /Annots [");
        for (var i = 0; i < notes.Length; i++) builder.Append(i + 2).Append(" 0 R ");
        builder.Append("] >> >>\nendobj\n");
        for (var i = 0; i < notes.Length; i++)
        {
            var (left, bottom, right, top) = Rect(notes[i], height, resolution);
            builder.Append(i + 2).Append(" 0 obj\n").Append(Annotation(notes[i], left, bottom, right, top, "/Page 0 ")).Append("\nendobj\n");
        }
        builder.Append("trailer\n<< /Root 1 0 R >>\n%%EOF\n");
        return Encoding.Latin1.GetBytes(text.Append(builder).ToString());
    }

    /// <summary>The document's notes as XFDF, Acrobat's XML comment format.</summary>
    public static byte[] WriteXfdf(IEnumerable<DocumentMark> marks, int height, double resolution)
    {
        XNamespace ns = "http://ns.adobe.com/xfdf/";
        var annots = new XElement(ns + "annots");
        foreach (var note in Notes(marks))
        {
            var (left, bottom, right, top) = Rect(note, height, resolution);
            annots.Add(new XElement(ns + "text",
                new XAttribute("page", 0), new XAttribute("rect", string.Join(",", new[] { left, bottom, right, top }.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture)))),
                new XAttribute("color", "#" + (note.Color & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture)), new XAttribute("title", note.Author),
                new XAttribute("name", note.ID.ToString()), new XAttribute("icon", "Comment"),
                new XElement(ns + "contents", note.Text)));
        }
        var document = new XDocument(new XDeclaration("1.0", "UTF-8", null), new XElement(ns + "xfdf", new XAttribute(XNamespace.Xml + "space", "preserve"), annots));
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true })) document.Save(writer);
        return stream.ToArray();
    }

    private static DocumentMark[] Notes(IEnumerable<DocumentMark> marks)
    {
        var notes = marks.Where(m => m.Kind == MarkKind.Note).ToArray();
        if (notes.Length is < 1 or > MaxNotes) throw new InvalidDataException("Select between 1 and 1000 valid text notes.");
        return notes;
    }

    /// <summary>A text annotation dictionary, its strings in UTF-16 so any language survives.</summary>
    internal static string Annotation(DocumentMark note, double left, double bottom, double right, double top, string extra = "")
    {
        static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        var color = note.Color;
        return $"<< /Type /Annot /Subtype /Text /Rect [{N(left)} {N(bottom)} {N(right)} {N(top)}] {extra}/Contents {Hex(note.Text)} /T {Hex(note.Author)} " +
            $"/C [{N((color >> 16 & 255) / 255.0)} {N((color >> 8 & 255) / 255.0)} {N((color & 255) / 255.0)}] /Name /Comment /F 4 /Open false >>";
    }

    private static string Hex(string text)
    {
        var bytes = new byte[] { 0xFE, 0xFF }.Concat(Encoding.BigEndianUnicode.GetBytes(text));
        return "<" + Convert.ToHexString(bytes.ToArray()) + ">";
    }

    /// <summary>
    /// <paramref name="pdf"/> with text annotations added to its pages as an incremental update: page i gets
    /// <paramref name="pages"/>[i], each note placed by its document position on a page of the given size in points.
    /// </summary>
    public static byte[] Annotate(byte[] pdf, IReadOnlyList<(IReadOnlyList<DocumentMark> Notes, double DocumentWidth, double DocumentHeight, double PageWidth, double PageTop)> pages)
    {
        if (pages.All(p => p.Notes.Count == 0)) return pdf;
        var objects = new PdfObjects(pdf);
        if (objects.Catalog() is not { } catalog || objects.Resolve(catalog.Get("Pages")) is not Dictionary<string, object?> tree)
            throw new InvalidDataException("The PDF has no pages.");
        var pageRefs = objects.PageReferences(tree);
        var text = Encoding.Latin1.GetString(pdf);
        var size = objects.Size;
        var startxref = System.Text.RegularExpressions.Regex.Match(text, @"startxref\s+(\d+)\s*%%EOF\s*$", System.Text.RegularExpressions.RegexOptions.RightToLeft);
        if (!startxref.Success) throw new InvalidDataException("The PDF has no cross-reference position.");
        var output = new StringBuilder(text);
        if (!text.EndsWith('\n')) output.Append('\n');
        var written = new List<(int Number, int Generation, long Offset)>();
        for (var index = 0; index < Math.Min(pages.Count, pageRefs.Count); index++)
        {
            var (notes, docWidth, docHeight, pageWidth, pageTop) = pages[index];
            if (notes.Count == 0) continue;
            var (number, generation) = pageRefs[index];
            var annots = new List<int>();
            foreach (var note in notes.Take(MaxNotes))
            {
                var scale = pageWidth / docWidth;
                var left = note.X * scale; var top = pageTop - note.Y * scale;
                var made = size++;
                written.Add((made, 0, output.Length));
                output.Append(made).Append(" 0 obj\n").Append(Annotation(note, left, top - 18, left + 18, top, $"/P {number} {generation} R ")).Append("\nendobj\n");
                annots.Add(made);
            }
            var body = objects.RawBody(number) ?? throw new InvalidDataException("A PDF page could not be read.");
            var page = System.Text.RegularExpressions.Regex.Replace(body.Trim(), @"/Annots\s*(\[[^\]]*\]|\d+\s+\d+\s+R)", "");
            page = page[..^2] + " /Annots [" + string.Join(" ", annots.Select(a => $"{a} 0 R")) + "] >>";
            written.Add((number, generation, output.Length));
            output.Append(number).Append(' ').Append(generation).Append(" obj\n").Append(page).Append("\nendobj\n");
        }
        var xref = output.Length;
        output.Append("xref\n");
        foreach (var (number, generation, offset) in written.OrderBy(w => w.Number))
            output.Append(number).Append(" 1\n").Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(' ').Append(generation.ToString("D5", CultureInfo.InvariantCulture)).Append(" n\r\n");
        output.Append("trailer\n<< /Size ").Append(size).Append(" /Root ").Append(objects.RootReference).Append(" /Prev ").Append(startxref.Groups[1].Value);
        if (objects.InfoReference is { } info) output.Append(" /Info ").Append(info);
        output.Append(" >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.Latin1.GetBytes(output.ToString());
    }
}

public sealed record PdfName(string Value);

internal static class PdfDictionaries
{
    public static object? Get(this Dictionary<string, object?> dictionary, string key) => dictionary.TryGetValue(key, out var value) ? value : null;
}

/// <summary>
/// Just enough of a PDF reader for annotations: every numbered object (later ones replacing earlier, as incremental
/// updates do), compressed object streams opened, and references resolved.
/// </summary>
internal sealed class PdfObjects
{
    private readonly byte[] _bytes;
    private readonly Dictionary<int, object?> _objects = [];
    private readonly Dictionary<int, (int Start, int End)> _bodies = [];
    private readonly Dictionary<string, object?> _trailer = [];

    public PdfObjects(byte[] bytes)
    {
        _bytes = bytes;
        var text = Encoding.Latin1.GetString(bytes);
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, @"(?<![0-9])(\d+)\s+(\d+)\s+obj\b"))
        {
            var number = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var at = match.Index + match.Length;
            try
            {
                var parser = new Parser(bytes, at);
                var value = parser.Value();
                parser.Skip();
                if (value is Dictionary<string, object?> dictionary && parser.Keyword("stream"))
                {
                    var start = parser.Position;
                    if (start < bytes.Length && bytes[start] == '\r') start++;
                    if (start < bytes.Length && bytes[start] == '\n') start++;
                    var end = text.IndexOf("endstream", start, StringComparison.Ordinal);
                    if (end < 0) continue;
                    var length = dictionary.Get("Length") is double declared && start + (int)declared <= end ? (int)declared : end - start;
                    dictionary["\u0000stream"] = bytes[start..(start + length)];
                }
                _objects[number] = value;
                var close = text.IndexOf("endobj", at, StringComparison.Ordinal);
                if (close > at) _bodies[number] = (at, close);
            }
            catch (Exception exception) when (exception is InvalidDataException or IndexOutOfRangeException or ArgumentOutOfRangeException) { }
        }
        // Compressed objects, from object streams.
        foreach (var stream in _objects.Values.OfType<Dictionary<string, object?>>().Where(d => d.Get("Type") is PdfName { Value: "ObjStm" }).ToArray())
        {
            if (Decoded(stream) is not { } data || stream.Get("N") is not double count || stream.Get("First") is not double first) continue;
            var header = new Parser(data, 0);
            var entries = new List<(int Number, int Offset)>();
            for (var i = 0; i < (int)count; i++)
                if (header.Value() is double n && header.Value() is double offset) entries.Add(((int)n, (int)offset));
            foreach (var (n, offset) in entries)
                if (!_objects.ContainsKey(n))
                    try { _objects[n] = new Parser(data, (int)first + offset).Value(); } catch (InvalidDataException) { }
        }
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, @"trailer\s*<<"))
            if (new Parser(bytes, match.Index + "trailer".Length).Value() is Dictionary<string, object?> trailer)
                foreach (var (key, value) in trailer) _trailer[key] = value;
        // Cross-reference streams carry the trailer's entries in their own dictionary.
        foreach (var xref in _objects.Values.OfType<Dictionary<string, object?>>().Where(d => d.Get("Type") is PdfName { Value: "XRef" }))
            foreach (var key in new[] { "Root", "Info", "Size" }) if (!_trailer.ContainsKey(key) && xref.Get(key) is { } value) _trailer[key] = value;
    }

    public int Size => Math.Max(_trailer.Get("Size") is double size ? (int)size : 0, _objects.Keys.DefaultIfEmpty(0).Max() + 1);
    public string? RootReference => _trailer.Get("Root") is PdfReference r ? $"{r.Number} {r.Generation} R" : null;
    public string? InfoReference => _trailer.Get("Info") is PdfReference r ? $"{r.Number} {r.Generation} R" : null;

    public Dictionary<string, object?>? Catalog() =>
        Resolve(_trailer.Get("Root")) as Dictionary<string, object?> ?? _objects.Values.OfType<Dictionary<string, object?>>().FirstOrDefault(d => d.Get("Type") is PdfName { Value: "Catalog" });
    public Dictionary<string, object?>? FirstWith(string key) => _objects.Values.OfType<Dictionary<string, object?>>().FirstOrDefault(d => d.ContainsKey(key));

    public object? Resolve(object? value)
    {
        for (var depth = 0; value is PdfReference reference && depth < 16; depth++) value = _objects.GetValueOrDefault(reference.Number);
        return value;
    }

    public string? RawBody(int number) => _bodies.TryGetValue(number, out var span) ? Encoding.Latin1.GetString(_bytes, span.Start, span.End - span.Start) : null;

    /// <summary>The pages in order, each with its media box (inherited from the tree when it has none).</summary>
    public List<(Dictionary<string, object?> Page, (double, double, double, double) Box)> Pages(Dictionary<string, object?> tree)
    {
        var result = new List<(Dictionary<string, object?>, (double, double, double, double))>();
        void Walk(Dictionary<string, object?> node, (double, double, double, double) box, int depth)
        {
            if (depth > 32 || result.Count > 10000) return;
            if (Resolve(node.Get("MediaBox")) is List<object?> { Count: 4 } media && media.Select(Resolve).All(v => v is double))
            { var m = media.Select(v => (double)Resolve(v)!).ToArray(); box = (m[0], m[1], m[2], m[3]); }
            if (node.Get("Type") is PdfName { Value: "Page" }) { result.Add((node, box)); return; }
            if (Resolve(node.Get("Kids")) is List<object?> kids)
                foreach (var kid in kids) if (Resolve(kid) is Dictionary<string, object?> child) Walk(child, box, depth + 1);
        }
        Walk(tree, (0, 0, 612, 792), 0);
        return result;
    }

    public List<(int Number, int Generation)> PageReferences(Dictionary<string, object?> tree)
    {
        var result = new List<(int, int)>();
        void Walk(Dictionary<string, object?> node, int depth)
        {
            if (depth > 32 || Resolve(node.Get("Kids")) is not List<object?> kids) return;
            foreach (var kid in kids)
            {
                if (kid is not PdfReference reference || Resolve(kid) is not Dictionary<string, object?> child) continue;
                if (child.Get("Type") is PdfName { Value: "Page" }) result.Add((reference.Number, reference.Generation)); else Walk(child, depth + 1);
            }
        }
        Walk(tree, 0);
        return result;
    }

    private byte[]? Decoded(Dictionary<string, object?> stream)
    {
        if (stream.Get("\u0000stream") is not byte[] data) return null;
        var filter = Resolve(stream.Get("Filter"));
        if (filter is null) return data;
        if (filter is not PdfName { Value: "FlateDecode" } && !(filter is List<object?> { Count: 1 } list && list[0] is PdfName { Value: "FlateDecode" })) return null;
        try
        {
            using var input = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[81920]; int read;
            while ((read = input.Read(buffer)) > 0) { output.Write(buffer, 0, read); if (output.Length > 64L << 20) return null; }
            return output.ToArray();
        }
        catch (InvalidDataException) { return null; }
    }

    /// <summary>A PDF string's text: UTF-16 with its byte order mark, otherwise PDFDocEncoding (Latin-1 near enough).</summary>
    public static string Text(byte[] bytes) =>
        bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF ? Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2)
        : bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3)
        : Encoding.Latin1.GetString(bytes);

    private sealed class Parser(byte[] data, int position)
    {
        public int Position { get; private set; } = position;
        private int _depth;

        public void Skip()
        {
            while (Position < data.Length)
            {
                var c = data[Position];
                if (c is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t' or (byte)'\f' or 0) Position++;
                else if (c == '%') { while (Position < data.Length && data[Position] is not ((byte)'\r' or (byte)'\n')) Position++; }
                else break;
            }
        }

        public bool Keyword(string word)
        {
            Skip();
            if (Position + word.Length > data.Length) return false;
            for (var i = 0; i < word.Length; i++) if (data[Position + i] != word[i]) return false;
            Position += word.Length;
            return true;
        }

        public object? Value()
        {
            if (++_depth > 64) throw new InvalidDataException("The PDF nests too deeply.");
            try
            {
                Skip();
                if (Position >= data.Length) throw new InvalidDataException("The PDF ends early.");
                var c = (char)data[Position];
                if (c == '<' && Position + 1 < data.Length && data[Position + 1] == '<')
                {
                    Position += 2;
                    var dictionary = new Dictionary<string, object?>();
                    while (true)
                    {
                        Skip();
                        if (Position + 1 < data.Length && data[Position] == '>' && data[Position + 1] == '>') { Position += 2; return dictionary; }
                        if (Value() is not PdfName key) throw new InvalidDataException("A PDF dictionary key is not a name.");
                        dictionary[key.Value] = Value();
                    }
                }
                if (c == '[')
                {
                    Position++;
                    var list = new List<object?>();
                    while (true)
                    {
                        Skip();
                        if (Position < data.Length && data[Position] == ']') { Position++; return list; }
                        list.Add(Value());
                    }
                }
                if (c == '/')
                {
                    var start = ++Position;
                    while (Position < data.Length && !Delimiter(data[Position])) Position++;
                    var raw = Encoding.Latin1.GetString(data, start, Position - start);
                    return new PdfName(System.Text.RegularExpressions.Regex.Replace(raw, "#([0-9A-Fa-f]{2})", m => ((char)Convert.ToByte(m.Groups[1].Value, 16)).ToString()));
                }
                if (c == '(') return Literal();
                if (c == '<')
                {
                    var end = Array.IndexOf(data, (byte)'>', Position);
                    if (end < 0) throw new InvalidDataException("A PDF hex string is not closed.");
                    var hex = new string(Encoding.Latin1.GetString(data, Position + 1, end - Position - 1).Where(Uri.IsHexDigit).ToArray());
                    Position = end + 1;
                    if (hex.Length % 2 == 1) hex += "0";
                    return Convert.FromHexString(hex);
                }
                if (char.IsDigit(c) || c is '-' or '+' or '.')
                {
                    var number = Number();
                    // "n g R" is a reference.
                    var mark = Position;
                    if (number == Math.Floor(number) && number >= 0)
                    {
                        Skip();
                        if (Position < data.Length && char.IsDigit((char)data[Position]))
                        {
                            var generation = Number();
                            Skip();
                            if (Position < data.Length && data[Position] == 'R' && (Position + 1 >= data.Length || Delimiter(data[Position + 1])))
                            { Position++; return new PdfReference((int)number, (int)generation); }
                        }
                    }
                    Position = mark;
                    return number;
                }
                var word = Position;
                while (Position < data.Length && !Delimiter(data[Position])) Position++;
                var token = Encoding.Latin1.GetString(data, word, Position - word);
                return token switch { "true" => true, "false" => false, "null" => null, _ => throw new InvalidDataException("Unexpected PDF token.") };
            }
            finally { _depth--; }
        }

        private double Number()
        {
            var start = Position;
            while (Position < data.Length && (char.IsDigit((char)data[Position]) || data[Position] is (byte)'-' or (byte)'+' or (byte)'.')) Position++;
            return double.TryParse(Encoding.Latin1.GetString(data, start, Position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
        }

        private byte[] Literal()
        {
            Position++;
            var output = new List<byte>(); var depth = 1;
            while (Position < data.Length)
            {
                var b = data[Position++];
                if (b == '\\' && Position < data.Length)
                {
                    var e = data[Position++];
                    switch ((char)e)
                    {
                        case 'n': output.Add(10); break; case 'r': output.Add(13); break; case 't': output.Add(9); break;
                        case 'b': output.Add(8); break; case 'f': output.Add(12); break;
                        case '\r': if (Position < data.Length && data[Position] == '\n') Position++; break;
                        case '\n': break;
                        default:
                            if (e is >= (byte)'0' and <= (byte)'7')
                            {
                                var value = e - '0';
                                for (var k = 0; k < 2 && Position < data.Length && data[Position] is >= (byte)'0' and <= (byte)'7'; k++) value = value * 8 + data[Position++] - '0';
                                output.Add((byte)value);
                            }
                            else output.Add(e);
                            break;
                    }
                    continue;
                }
                if (b == '(') depth++;
                if (b == ')' && --depth == 0) return [.. output];
                output.Add(b);
            }
            throw new InvalidDataException("A PDF string is not closed.");
        }

        private static bool Delimiter(byte b) => b is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t' or (byte)'\f' or 0
            or (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';
    }
}

internal sealed record PdfReference(int Number, int Generation);
