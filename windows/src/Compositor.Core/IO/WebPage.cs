using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.IO;

public enum HtmlTagCase { Upper, Mixed, Lower }
public enum HtmlAttributeCase { Upper, Mixed, MixedLowerFirst, Lower }
public enum HtmlIndent { Tabs, None, OneSpace, TwoSpaces, FourSpaces, FiveSpaces, EightSpaces }
public enum HtmlLineEnding { Auto, Mac, Windows, Unix }
public enum WebSliceLayout { Table, Css }
public enum WebCssReference { ById, Inline, ByClass }
public enum WebEmptyCells { GifImageSize, GifCellSize, NoWrapCellSize }
public enum WebCellSize { Auto, Always, Never }
public enum WebSpacerCells { Auto, AutoBottom, Always, AlwaysBottom, Never }
public enum WebBackgroundView { Html, Image }
public enum WebFileNames { Windows, MacOS, Unix }
public enum WebSaveKind { HtmlAndImages, ImagesOnly, HtmlOnly }
public enum WebSliceScope { AllSlices, AllUserSlices, SelectedSlices }

/// <summary>
/// Save for Web's Output Settings: how the page is written (HTML), how slices become a table or CSS boxes
/// (Slices), the page behind them (Background) and how the files are named and placed (Saving Files).
/// </summary>
public sealed record WebHtmlSettings
{
    // HTML
    public bool Xhtml { get; init; }
    public HtmlTagCase TagCase { get; init; } = HtmlTagCase.Lower;
    public HtmlAttributeCase AttributeCase { get; init; } = HtmlAttributeCase.Lower;
    public HtmlIndent Indent { get; init; } = HtmlIndent.Tabs;
    public HtmlLineEnding LineEnding { get; init; } = HtmlLineEnding.Auto;
    public bool IncludeComments { get; init; } = true;
    public bool AlwaysAddAlt { get; init; } = true;
    public bool AlwaysQuote { get; init; } = true;
    public bool CloseAllTags { get; init; }
    public bool ZeroMargins { get; init; } = true;
    // Slices
    public WebSliceLayout Layout { get; init; } = WebSliceLayout.Table;
    public WebEmptyCells EmptyCells { get; init; } = WebEmptyCells.GifImageSize;
    public WebCellSize CellSize { get; init; } = WebCellSize.Auto;
    public WebSpacerCells SpacerCells { get; init; } = WebSpacerCells.Auto;
    public WebCssReference CssReference { get; init; } = WebCssReference.ById;
    /// <summary>How many digits a slice's number gets in its default name, 1–3.</summary>
    public int SliceDigits { get; init; } = 2;
    // Background
    public WebBackgroundView BackgroundView { get; init; } = WebBackgroundView.Html;
    public string BackgroundImage { get; init; } = "";
    public uint? BackgroundColor { get; init; }
    // Saving Files
    public WebFileNames FileNames { get; init; } = WebFileNames.Windows;
    public bool PutImagesInFolder { get; init; } = true;
    public string ImagesFolder { get; init; } = "images";
    public bool CopyBackgroundImage { get; init; }

    public static WebHtmlSettings Default { get; } = new();
    public static WebHtmlSettings BackgroundImagePreset { get; } = new() { BackgroundView = WebBackgroundView.Image, SpacerCells = WebSpacerCells.Never };
    public static WebHtmlSettings XhtmlPreset { get; } = new() { Xhtml = true, Layout = WebSliceLayout.Css, CloseAllTags = true };

    public bool IsValid => Enum.IsDefined(TagCase) && Enum.IsDefined(AttributeCase) && Enum.IsDefined(Indent) && Enum.IsDefined(LineEnding)
        && Enum.IsDefined(Layout) && Enum.IsDefined(EmptyCells) && Enum.IsDefined(CellSize) && Enum.IsDefined(SpacerCells)
        && Enum.IsDefined(CssReference) && Enum.IsDefined(BackgroundView) && Enum.IsDefined(FileNames) && SliceDigits is >= 1 and <= 3
        && ImagesFolder is { Length: > 0 and <= 64 } && ImagesFolder.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && ImagesFolder is not ("." or "..")
        && BackgroundImage is { Length: <= 2048 } && !BackgroundImage.Contains('"') && !BackgroundImage.Contains('<');

    private sealed record Stored(int Version, WebHtmlSettings Settings);
    public byte[] Save() => JsonSerializer.SerializeToUtf8Bytes(new Stored(1, this), new JsonSerializerOptions { WriteIndented = true });
    public static WebHtmlSettings Load(byte[] json)
    {
        var stored = JsonSerializer.Deserialize<Stored>(json) ?? throw new InvalidDataException("Not Output Settings.");
        if (stored.Version != 1 || stored.Settings is not { IsValid: true } settings) throw new InvalidDataException("These Output Settings are not supported.");
        return settings;
    }
}

/// <summary>One slice of the page: where it is on the output image, what it is called and what it links to.</summary>
public sealed record WebSlice(SKRectI Bounds, string FileName, DocumentMark Mark, bool User);

/// <summary>What Save for Web wrote, for the export event and for the caller to report.</summary>
public sealed record WebPageResult(IReadOnlyList<string> Files);

/// <summary>
/// Save for Web as Photoshop writes a page: each slice its own image, and HTML that puts them back together as a
/// table or as positioned boxes, with the slice's link, target, alt text and background. A frame animation saved as
/// GIF is one animated image.
/// </summary>
public static class WebPage
{
    /// <summary>The slices to write, in output pixels: the user's own and the automatic ones that fill the gaps
    /// (or only the user's, or only <paramref name="selected"/>), named after the page.</summary>
    public static IReadOnlyList<WebSlice> Slices(CanvasDocument document, WebExportOptions options, WebHtmlSettings settings, string baseName,
        WebSliceScope scope = WebSliceScope.AllSlices, IReadOnlySet<Guid>? selected = null)
    {
        var user = document.Marks.Where(m => m.Kind == MarkKind.Slice && m.Visible).ToList();
        var marks = user.Count == 0
            ? [new DocumentMark { Kind = MarkKind.Slice, X = 0, Y = 0, Width = document.Width, Height = document.Height }]
            : scope == WebSliceScope.AllSlices ? user.Concat(SliceEdits.Automatic(document)).ToList() : user;
        if (scope == WebSliceScope.SelectedSlices && selected is not null) marks = marks.Where(m => selected.Contains(m.ID)).ToList();
        double sx = options.Width / (double)document.Width, sy = options.Height / (double)document.Height;
        var ordered = marks.Select(m => (Mark: m, Bounds: SKRectI.Round(SKRect.Create((float)(m.X * sx), (float)(m.Y * sy), (float)(m.Width * sx), (float)(m.Height * sy)))))
            .Select(s => (s.Mark, Bounds: SKRectI.Intersect(s.Bounds, SKRectI.Create(options.Width, options.Height))))
            .Where(s => !s.Bounds.IsEmpty).OrderBy(s => s.Bounds.Top).ThenBy(s => s.Bounds.Left).ToList();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<WebSlice>();
        var safeBase = FileName(baseName, settings.FileNames);
        for (var i = 0; i < ordered.Count; i++)
        {
            var (mark, bounds) = ordered[i];
            var isUser = user.Any(u => u.ID == mark.ID);
            var name = isUser && mark.Name.Length > 0 ? FileName(mark.Name, settings.FileNames)
                : ordered.Count == 1 ? safeBase : safeBase + "_" + (i + 1).ToString(new string('0', settings.SliceDigits), CultureInfo.InvariantCulture);
            var unique = name; var n = 2;
            while (!names.Add(unique)) unique = name + "-" + n++;
            result.Add(new WebSlice(bounds, unique, mark, isUser));
        }
        return result;
    }

    /// <summary>A name the chosen systems can all keep: no reserved characters, spaces as underscores, and short
    /// enough for classic Mac OS when that is asked for.</summary>
    public static string FileName(string name, WebFileNames system)
    {
        var builder = new StringBuilder();
        foreach (var c in name.Trim())
        {
            var bad = c < 32 || c is '/' || system == WebFileNames.Windows && "<>:\"\\|?*".Contains(c) || system == WebFileNames.MacOS && c == ':';
            builder.Append(bad || char.IsWhiteSpace(c) ? '_' : c);
        }
        var text = builder.ToString().TrimEnd('.', '_');
        if (text.Length == 0 || text.StartsWith('.')) text = "Untitled" + text;
        if (system == WebFileNames.Windows && LayerWorkflow.ReservedName(text)) text += "_";
        return system == WebFileNames.MacOS && text.Length > 27 ? text[..27] : text;
    }

    /// <summary>The page's HTML for <paramref name="slices"/> whose images are <paramref name="extension"/> files in
    /// <paramref name="settings"/>' images folder (or beside the page).</summary>
    public static string Html(WebHtmlSettings settings, string title, int width, int height, IReadOnlyList<WebSlice> slices, string extension,
        IReadOnlySet<int>? emptyImages = null)
    {
        var page = new Writer(settings);
        var folder = settings.PutImagesInFolder ? settings.ImagesFolder + "/" : "";
        string Image(WebSlice slice) => folder + slice.FileName + "." + extension;
        var spacer = folder + "spacer.gif";
        var bodyColor = settings.BackgroundColor is { } color ? Hex(color) : "#FFFFFF";
        if (settings.Xhtml)
        {
            page.Raw("<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Transitional//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd\">");
            page.Open("html", ("xmlns", "http://www.w3.org/1999/xhtml"));
        }
        else page.Open("html");
        page.Open("head");
        page.Open("title", inline: true); page.Text(title); page.Close("title", inline: true);
        page.Void("meta", ("http-equiv", "Content-Type"), ("content", "text/html; charset=utf-8"));
        var css = settings.Layout == WebSliceLayout.Css && settings.CssReference != WebCssReference.Inline;
        if (css)
        {
            page.Open("style", ("type", "text/css"));
            page.Raw(Selector(settings, "Table_01") + " {");
            page.Raw($"\tposition:absolute; left:0px; top:0px; width:{width}px; height:{height}px;");
            page.Raw("}");
            for (var i = 0; i < slices.Count; i++)
            {
                var s = slices[i];
                page.Raw(Selector(settings, Id(s)) + " {");
                page.Raw($"\tposition:absolute; left:{s.Bounds.Left}px; top:{s.Bounds.Top}px; width:{s.Bounds.Width}px; height:{s.Bounds.Height}px;"
                    + (s.Mark.Background is { } back ? $" background-color:{Hex(back)};" : ""));
                page.Raw("}");
            }
            page.Close("style");
        }
        page.Close("head");
        var body = new List<(string, string)>();
        if (settings.BackgroundView == WebBackgroundView.Image && settings.BackgroundImage.Length > 0) body.Add(("background", settings.BackgroundImage));
        if (settings.Xhtml)
            body.Add(("style", $"background-color:{bodyColor};" + (settings.ZeroMargins ? " margin:0px;" : "")));
        else
        {
            body.Add(("bgcolor", bodyColor));
            if (settings.ZeroMargins) body.AddRange([("leftmargin", "0"), ("topmargin", "0"), ("marginwidth", "0"), ("marginheight", "0")]);
        }
        page.Open("body", [.. body]);
        if (settings.IncludeComments) page.Raw($"<!-- Save for Web Slices ({WebUtility.HtmlEncode(title)}) -->");
        if (settings.Layout == WebSliceLayout.Css)
        {
            page.Open("div", Box(settings, "Table_01", 0, 0, width, height, null));
            for (var i = 0; i < slices.Count; i++)
            {
                var s = slices[i];
                page.Open("div", Box(settings, Id(s), s.Bounds.Left, s.Bounds.Top, s.Bounds.Width, s.Bounds.Height, s.Mark.Background));
                Content(s, i);
                page.Close("div");
            }
            page.Close("div");
        }
        else Table();
        if (settings.IncludeComments) page.Raw("<!-- End Save for Web Slices -->");
        page.Close("body");
        page.Close("html");
        return page.ToString();

        void Table()
        {
            var xs = slices.SelectMany(s => new[] { s.Bounds.Left, s.Bounds.Right }).Append(0).Append(width).Distinct().Order().ToArray();
            var ys = slices.SelectMany(s => new[] { s.Bounds.Top, s.Bounds.Bottom }).Append(0).Append(height).Distinct().Order().ToArray();
            var columns = xs.Length - 1; var rows = ys.Length - 1;
            var spacers = settings.SpacerCells switch
            {
                WebSpacerCells.Never => false,
                WebSpacerCells.Always or WebSpacerCells.AlwaysBottom => true,
                // Spans that cross more than one column or row are what make a browser guess widths wrongly.
                _ => slices.Any(s => Array.IndexOf(xs, s.Bounds.Right) - Array.IndexOf(xs, s.Bounds.Left) > 1 || Array.IndexOf(ys, s.Bounds.Bottom) - Array.IndexOf(ys, s.Bounds.Top) > 1),
            };
            var right = spacers && settings.SpacerCells is WebSpacerCells.Auto or WebSpacerCells.Always;
            var sizes = settings.CellSize == WebCellSize.Always || settings.CellSize == WebCellSize.Auto && !spacers;
            page.Open("table", ("id", "Table_01"), ("width", Text(width + (right ? 1 : 0))), ("height", Text(height + (spacers ? 1 : 0))),
                ("border", "0"), ("cellpadding", "0"), ("cellspacing", "0"));
            for (var row = 0; row < rows; row++)
            {
                page.Open("tr");
                foreach (var (s, index) in slices.Select((s, i) => (s, i)).Where(p => p.s.Bounds.Top == ys[row]).OrderBy(p => p.s.Bounds.Left))
                {
                    var colspan = Array.IndexOf(xs, s.Bounds.Right) - Array.IndexOf(xs, s.Bounds.Left);
                    var rowspan = Array.IndexOf(ys, s.Bounds.Bottom) - Array.IndexOf(ys, s.Bounds.Top);
                    var cell = new List<(string, string)>();
                    if (colspan > 1) cell.Add(("colspan", Text(colspan)));
                    if (rowspan > 1) cell.Add(("rowspan", Text(rowspan)));
                    var empty = s.Mark.NoImage || emptyImages?.Contains(index) == true;
                    if (sizes || empty && settings.EmptyCells != WebEmptyCells.GifImageSize)
                    { cell.Add(("width", Text(s.Bounds.Width))); cell.Add(("height", Text(s.Bounds.Height))); }
                    if (empty && settings.EmptyCells == WebEmptyCells.NoWrapCellSize) cell.Add(("nowrap", "nowrap"));
                    if (s.Mark.Background is { } back) cell.Add(("bgcolor", Hex(back)));
                    page.Open("td", [.. cell]);
                    Content(s, index);
                    page.Close("td");
                }
                if (right)
                {
                    page.Open("td");
                    page.Void("img", ("src", spacer), ("width", "1"), ("height", Text(ys[row + 1] - ys[row])), ("alt", ""));
                    page.Close("td");
                }
                page.Close("tr");
            }
            if (spacers)
            {
                page.Open("tr");
                for (var column = 0; column < columns; column++)
                {
                    page.Open("td");
                    page.Void("img", ("src", spacer), ("width", Text(xs[column + 1] - xs[column])), ("height", "1"), ("alt", ""));
                    page.Close("td");
                }
                if (right) { page.Open("td"); page.Close("td"); }
                page.Close("tr");
            }
            page.Close("table");
        }

        void Content(WebSlice s, int index)
        {
            var empty = s.Mark.NoImage || emptyImages?.Contains(index) == true;
            var link = s.Mark.Url.Length > 0 && !empty;
            if (link)
            {
                var anchor = new List<(string, string)> { ("href", s.Mark.Url) };
                if (s.Mark.Target.Length > 0) anchor.Add(("target", s.Mark.Target));
                page.Open("a", [.. anchor]);
            }
            if (s.Mark.NoImage) { if (s.Mark.Text.Length > 0) page.Text(s.Mark.Text); }
            else if (empty)
            {
                if (settings.EmptyCells == WebEmptyCells.GifImageSize)
                    page.Void("img", ("src", spacer), ("width", Text(s.Bounds.Width)), ("height", Text(s.Bounds.Height)), ("alt", ""));
            }
            else
            {
                var image = new List<(string, string)> { ("src", Image(s)), ("width", Text(s.Bounds.Width)), ("height", Text(s.Bounds.Height)) };
                if (settings.AlwaysAddAlt || s.Mark.AltText.Length > 0) image.Add(("alt", s.Mark.AltText));
                if (link && !settings.Xhtml) image.Add(("border", "0"));
                page.Void("img", [.. image]);
            }
            if (link) page.Close("a");
        }
    }

    private static string Id(WebSlice slice) => slice.FileName.Replace('.', '-') + "_";
    private static string Selector(WebHtmlSettings settings, string id) => (settings.CssReference == WebCssReference.ByClass ? "." : "#") + id;
    private static (string, string)[] Box(WebHtmlSettings settings, string id, int left, int top, int width, int height, uint? back) =>
        settings.CssReference switch
        {
            WebCssReference.Inline => [("id", id), ("style", $"position:absolute; left:{left}px; top:{top}px; width:{width}px; height:{height}px;" + (back is { } b ? $" background-color:{Hex(b)};" : ""))],
            WebCssReference.ByClass => [("class", id)],
            _ => [("id", id)],
        };
    private static string Hex(uint argb) => "#" + (argb & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);
    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Writes the page and its images beside <paramref name="htmlPath"/> (or only the images, or only the HTML), each
    /// file replaced whole or not at all. A GIF with a frame animation is written as one animated image.
    /// </summary>
    public static WebPageResult Save(CanvasDocument document, WebExportOptions options, WebHtmlSettings settings, string htmlPath, WebSaveKind kind,
        string title, int loops = 0, WebSliceScope scope = WebSliceScope.AllSlices, IReadOnlySet<Guid>? selected = null, CancellationToken cancellation = default)
    {
        if (!settings.IsValid) throw new ArgumentException("Invalid Output Settings.", nameof(settings));
        options.Validate();
        var directory = Path.GetDirectoryName(Path.GetFullPath(htmlPath)) ?? throw new ArgumentException("No folder for the page.", nameof(htmlPath));
        var baseName = Path.GetFileNameWithoutExtension(htmlPath);
        var imageFolder = kind == WebSaveKind.ImagesOnly || !settings.PutImagesInFolder ? directory : Path.Combine(directory, settings.ImagesFolder);
        var written = new List<string>();
        var animated = options.Format == WebFormat.Gif ? WebExport.EncodeAnimation(document, options, loops, cancellation) : null;
        var slices = animated is null
            ? Slices(document, options, settings, baseName, scope, selected)
            : [new WebSlice(SKRectI.Create(options.Width, options.Height), FileName(baseName, settings.FileNames), new DocumentMark { Kind = MarkKind.Slice, X = 0, Y = 0, Width = document.Width, Height = document.Height }, false)];
        var empty = new HashSet<int>();
        if (kind != WebSaveKind.HtmlOnly)
        {
            Directory.CreateDirectory(imageFolder);
            if (animated is not null)
            {
                var file = Path.Combine(imageFolder, slices[0].FileName + ".gif");
                AtomicFile.WriteBytes(file, animated); written.Add(file);
            }
            else
            {
                var encoded = WebExport.EncodeRegions(document, options, slices.Select(s => s.Mark.NoImage ? SKRectI.Empty : s.Bounds).ToArray(), cancellation);
                try
                {
                    for (var i = 0; i < slices.Count; i++)
                    {
                        if (encoded[i] is not { } result) { empty.Add(i); continue; }
                        var file = Path.Combine(imageFolder, slices[i].FileName + "." + options.Extension);
                        AtomicFile.WriteBytes(file, result.Bytes); written.Add(file);
                    }
                }
                finally { foreach (var result in encoded) result?.Dispose(); }
            }
        }
        if (kind != WebSaveKind.ImagesOnly)
        {
            var html = Html(settings, title, options.Width, options.Height, slices, animated is null ? options.Extension : "gif", empty);
            if (html.Contains("spacer.gif", StringComparison.Ordinal) && kind == WebSaveKind.HtmlAndImages)
            {
                var spacer = Path.Combine(imageFolder, "spacer.gif");
                AtomicFile.WriteBytes(spacer, Spacer); written.Add(spacer);
            }
            var bytes = new UTF8Encoding(false).GetBytes(html);
            AtomicFile.WriteBytes(htmlPath, bytes); written.Insert(0, htmlPath);
        }
        return new WebPageResult(written);
    }

    /// <summary>The files <see cref="Save"/> would write (the page first), so the caller can ask before replacing any.
    /// Spacer images are left out: they are the same everywhere.</summary>
    public static IReadOnlyList<string> Planned(CanvasDocument document, WebExportOptions options, WebHtmlSettings settings, string htmlPath, WebSaveKind kind,
        WebSliceScope scope = WebSliceScope.AllSlices, IReadOnlySet<Guid>? selected = null)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(htmlPath))!;
        var baseName = Path.GetFileNameWithoutExtension(htmlPath);
        var imageFolder = kind == WebSaveKind.ImagesOnly || !settings.PutImagesInFolder ? directory : Path.Combine(directory, settings.ImagesFolder);
        var files = new List<string>();
        if (kind != WebSaveKind.ImagesOnly) files.Add(Path.GetFullPath(htmlPath));
        if (kind == WebSaveKind.HtmlOnly) return files;
        if (options.Format == WebFormat.Gif && WebExport.Frames(document).Count > 0)
            files.Add(Path.Combine(imageFolder, FileName(baseName, settings.FileNames) + ".gif"));
        else
            files.AddRange(Slices(document, options, settings, baseName, scope, selected).Where(s => !s.Mark.NoImage)
                .Select(s => Path.Combine(imageFolder, s.FileName + "." + options.Extension)));
        return files;
    }

    /// <summary>A transparent 1×1 GIF, which spacer cells hold to their size.</summary>
    public static readonly byte[] Spacer = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

    /// <summary>The page as text, cased, indented and ended as the settings ask.</summary>
    private sealed class Writer(WebHtmlSettings settings)
    {
        private readonly StringBuilder _text = new();
        private int _depth;
        private bool _inline;
        private string NewLine => settings.LineEnding switch { HtmlLineEnding.Mac => "\r", HtmlLineEnding.Unix => "\n", _ => "\r\n" };
        private string Indent => settings.Indent switch
        {
            HtmlIndent.None => "", HtmlIndent.OneSpace => " ", HtmlIndent.TwoSpaces => "  ", HtmlIndent.FourSpaces => "    ",
            HtmlIndent.FiveSpaces => "     ", HtmlIndent.EightSpaces => "        ", _ => "\t",
        };
        private string Tag(string name) => settings.Xhtml ? name : settings.TagCase switch
        {
            HtmlTagCase.Upper => name.ToUpperInvariant(),
            HtmlTagCase.Mixed => char.ToUpperInvariant(name[0]) + name[1..],
            _ => name,
        };
        private string Attribute(string name)
        {
            if (settings.Xhtml) return name;
            var parts = name.Split('-');
            string Title(string p) => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..];
            // "rowspan" reads as RowSpan, the way the reference shows it.
            static string Words(string p) => p switch { "rowspan" => "RowSpan", "colspan" => "ColSpan", "cellpadding" => "CellPadding", "cellspacing" => "CellSpacing",
                "leftmargin" => "LeftMargin", "topmargin" => "TopMargin", "marginwidth" => "MarginWidth", "marginheight" => "MarginHeight", "bgcolor" => "BgColor",
                "nowrap" => "NoWrap", _ => char.ToUpperInvariant(p[0]) + p[1..] };
            return settings.AttributeCase switch
            {
                HtmlAttributeCase.Upper => name.ToUpperInvariant(),
                HtmlAttributeCase.Mixed => string.Join("-", parts.Select(p => p.Length == 0 ? p : Words(p))),
                HtmlAttributeCase.MixedLowerFirst => string.Join("-", parts.Select((p, i) => p.Length == 0 ? p : i == 0 ? char.ToLowerInvariant(Words(p)[0]) + Words(p)[1..] : Title(p))),
                _ => name,
            };
        }
        private string Attributes((string Name, string Value)[] attributes)
        {
            var text = new StringBuilder();
            foreach (var (name, value) in attributes)
            {
                // XHTML spells out a boolean attribute's value; HTML may leave it bare.
                if (name == "nowrap" && !settings.Xhtml) { text.Append(' ').Append(Attribute(name)); continue; }
                var encoded = WebUtility.HtmlEncode(value);
                var quote = settings.Xhtml || settings.AlwaysQuote || encoded.Length == 0 || encoded.Any(c => !char.IsLetterOrDigit(c) && c is not ('-' or '.' or '_' or ':'));
                text.Append(' ').Append(Attribute(name)).Append('=').Append(quote ? "\"" + encoded + "\"" : encoded);
            }
            return text.ToString();
        }
        private void Line(string text) => _text.Append(string.Concat(Enumerable.Repeat(Indent, _depth))).Append(text).Append(NewLine);
        public void Raw(string text) { if (_inline) _text.Append(text); else Line(text); }
        public void Text(string text) { if (_inline) _text.Append(WebUtility.HtmlEncode(text)); else Line(WebUtility.HtmlEncode(text)); }
        public void Open(string name, params (string, string)[] attributes) => Open(name, false, attributes);
        public void Open(string name, bool inline, params (string, string)[] attributes)
        {
            var tag = "<" + Tag(name) + Attributes(attributes) + ">";
            if (inline) { _text.Append(string.Concat(Enumerable.Repeat(Indent, _depth))).Append(tag); _inline = true; }
            else { Line(tag); _depth++; }
        }
        public void Close(string name, bool inline = false)
        {
            if (inline) { _text.Append("</" + Tag(name) + ">").Append(NewLine); _inline = false; return; }
            _depth--;
            Line("</" + Tag(name) + ">");
        }
        /// <summary>An element with no content: closed in XHTML or when every tag is to be closed.</summary>
        public void Void(string name, params (string, string)[] attributes) =>
            Raw("<" + Tag(name) + Attributes(attributes) + (settings.Xhtml || settings.CloseAllTags ? " />" : ">"));
        public override string ToString() => _text.ToString();
    }
}
