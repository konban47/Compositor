using System.Globalization;
using System.Text;
using System.Text.Json;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Core.Document;

public static class ImageAnalysis
{
    /// <summary>Photoshop's data points, in the groups Select Data Points lists them. "Mean" is Gray Value (Mean) and "Count"
    /// the Count tool's count, the names the Windows build saved before extension 35.</summary>
    public static readonly (string Group, string[] Fields)[] DataPointGroups =
    [
        ("General", ["Label", "Date and Time", "Document", "Source", "Scale", "Scale Units", "Scale Factor"]),
        ("Selection", ["Selection Count", "Area", "Perimeter", "Circularity", "Height", "Width", "Gray Value (Minimum)", "Gray Value (Maximum)", "Mean",
            "Gray Value (Median)", "Integrated Density", "Histogram"]),
        ("Ruler Tool", ["Ruler Count", "Length", "Angle"]),
        ("Count Tool", ["Count"]),
    ];
    public static readonly string[] DataPoints = DataPointGroups.SelectMany(g => g.Fields).ToArray();
    /// <summary>The data points versions before 35 know.</summary>
    public static readonly string[] LegacyDataPoints = ["Area", "Perimeter", "Width", "Height", "Length", "Angle", "Count", "Mean"];

    public static MeasurementRecord Measure(CanvasDocument document, string name, string? documentName = null)
    {
        var scale = document.MeasurementScale; if (!scale.IsValid) throw new InvalidDataException("Invalid measurement scale.");
        var bounds = document.Selection.Path is null ? SKRectI.Create(document.Width, document.Height) : document.Selection.CoverageRect(document.Width, document.Height);
        bounds = SKRectI.Intersect(bounds, SKRectI.Create(document.Width, document.Height));
        if (bounds.IsEmpty) throw new InvalidOperationException("Select a non-empty area to measure.");
        using var mask = document.Selection.Coverage(bounds); using var composite = DocumentRenderer.RenderRegion(document, bounds);
        double area = 0, perimeter = 0, mean = 0;
        var histogram = new int[256];
        var minX = bounds.Width; var minY = bounds.Height; var maxX = -1; var maxY = -1;
        double At(int x, int y) => x < 0 || y < 0 || x >= bounds.Width || y >= bounds.Height ? 0 : mask is null ? 1 : mask.GetPixelSpan()[y * mask.RowBytes + x] / 255.0;
        for (var y = 0; y < bounds.Height; y++) for (var x = 0; x < bounds.Width; x++)
        {
            var a = At(x, y); if (a > 0) { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
            area += a; var c = composite.GetPixel(x, y); var gray = .2126 * c.Red + .7152 * c.Green + .0722 * c.Blue; mean += a * gray;
            if (a >= .5) histogram[Math.Clamp((int)Math.Round(gray), 0, 255)]++;
            perimeter += Math.Abs(a - At(x - 1, y)) + Math.Abs(a - At(x, y - 1));
            if (x == bounds.Width - 1) perimeter += a; if (y == bounds.Height - 1) perimeter += a;
        }
        var ruler = document.Marks.LastOrDefault(m => m.Kind == MarkKind.Measure && m.Visible);
        var factor = scale.Units / scale.Pixels; var length = ruler is null ? 0 : Math.Sqrt(ruler.Width * ruler.Width + ruler.Height * ruler.Height);
        // The gray range and median over the pixels at least half selected, as Photoshop counts them.
        var total = histogram.Sum(); double? minimum = null, maximum = null, median = null;
        if (total > 0)
        {
            minimum = Array.FindIndex(histogram, v => v > 0); maximum = Array.FindLastIndex(histogram, v => v > 0);
            var running = 0; for (var i = 0; i < 256; i++) { running += histogram[i]; if (running * 2 >= total) { median = i; break; } }
        }
        var source = document.Selection.Path is not null ? "Selection" : ruler is not null ? "Ruler Tool" : document.Marks.Any(m => m.Kind == MarkKind.Count && m.Visible) ? "Count Tool" : "Selection";
        return new(name, DateTimeOffset.UtcNow, area * factor * factor, perimeter * factor, Math.Max(0, maxX - minX + 1) * factor, Math.Max(0, maxY - minY + 1) * factor,
            length * factor, ruler is null ? 0 : Math.Atan2(ruler.Height, ruler.Width) * 180 / Math.PI,
            document.Marks.Count(m => m.Kind == MarkKind.Count && m.Visible), area > 0 ? mean / area : 0, scale.Unit,
            minimum, maximum, median, histogram, source, documentName is { Length: > 0 and <= 1024 } ? documentName : null);
    }

    /// <summary>What one data point of a record reads in the log and the exported file.</summary>
    public static string Value(CanvasDocument document, MeasurementRecord record, string field)
    {
        string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
        var scale = document.MeasurementScale;
        return field switch
        {
            "Label" => record.Name, "Date and Time" => record.Time.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture),
            "Document" => record.Document ?? "", "Source" => record.Source ?? "",
            "Scale" => $"{Number(scale.Pixels)} px = {scale.Units.ToString("0.0000", CultureInfo.InvariantCulture)} {scale.Unit}",
            "Scale Units" => record.Unit, "Scale Factor" => (scale.Units / scale.Pixels).ToString("0.000000", CultureInfo.InvariantCulture),
            "Selection Count" => record.Area > 0 ? "1" : "0", "Area" => Number(record.Area), "Perimeter" => Number(record.Perimeter),
            "Circularity" => record.Perimeter > 0 ? Math.Min(1, 4 * Math.PI * record.Area / (record.Perimeter * record.Perimeter)).ToString("0.000000", CultureInfo.InvariantCulture) : "",
            "Height" => Number(record.Height), "Width" => Number(record.Width),
            "Gray Value (Minimum)" => record.GrayMinimum is { } min ? Number(min) : "", "Gray Value (Maximum)" => record.GrayMaximum is { } max ? Number(max) : "",
            "Mean" => Number(record.Mean), "Gray Value (Median)" => record.GrayMedian is { } median ? Number(median) : "",
            "Integrated Density" => Number(record.Area * record.Mean), "Histogram" => record.Histogram is { } h ? string.Join(' ', h) : "",
            "Ruler Count" => record.Length > 0 ? "1" : "0", "Length" => Number(record.Length), "Angle" => Number(record.Angle),
            "Count" => record.Count.ToString(CultureInfo.InvariantCulture), _ => "",
        };
    }
    public static string Csv(CanvasDocument document)
    {
        string Quote(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
        var builder = new StringBuilder("Name,Time,Unit"); foreach (var field in document.MeasurementFields) builder.Append(',').Append(field); builder.AppendLine();
        foreach (var record in document.Measurements)
        {
            builder.Append(Quote(record.Name)).Append(',').Append(record.Time.ToString("O", CultureInfo.InvariantCulture)).Append(',').Append(Quote(record.Unit));
            foreach (var field in document.MeasurementFields) builder.Append(',').Append(Quote(Value(document, record, field)));
            builder.AppendLine();
        }
        return builder.ToString();
    }

    /// <summary>The rows of the given records as tab-separated text, the format Photoshop's Export Selected Measurements writes.</summary>
    public static string Tsv(CanvasDocument document, IEnumerable<MeasurementRecord> records)
    {
        var builder = new StringBuilder(string.Join('\t', document.MeasurementFields)).AppendLine();
        foreach (var record in records) builder.AppendLine(string.Join('\t', document.MeasurementFields.Select(f => Value(document, record, f).Replace('\t', ' '))));
        return builder.ToString();
    }

    public static bool ScaleMarker(CanvasDocument document, double units, string label, bool white) =>
        ScaleMarker(document, units, label, white, SKTypeface.Default.FamilyName, 15 * 72 / Math.Max(1, document.Resolution), true, false);

    /// <summary>
    /// Image ▸ Analysis ▸ Place Scale Marker: a bar <paramref name="units"/> long in the measurement scale, with its length
    /// written under it (or over it, <paramref name="textOnTop"/>) in <paramref name="family"/> at <paramref name="points"/>,
    /// black or white, as a layer of its own at the lower left.
    /// </summary>
    public static bool ScaleMarker(CanvasDocument document, double units, string label, bool white, string family, double points, bool showText, bool textOnTop)
    {
        if (!document.MeasurementScale.IsValid || !double.IsFinite(units) || units <= 0 || !double.IsFinite(points) || points is < 1 or > 1296) return false;
        var length = units * document.MeasurementScale.Pixels / document.MeasurementScale.Units;
        if (length > document.Width - 20 || length < 1 || document.Layers.Count >= LayerPlacement.MaxLayers) return false;
        var size = (float)Math.Clamp(points * document.Resolution / 72, 4, 2000);
        using var typeface = SKTypeface.FromFamilyName(family) ?? SKTypeface.Default; using var font = new SKFont(typeface, size);
        var textWidth = showText ? font.MeasureText(label) : 0; var bar = Math.Max(5f, size / 3);
        var textHeight = showText ? size * 1.3f : 0;
        var pixels = Bitmaps.Allocate(Bitmaps.ColorInfo(Math.Max(32, (int)Math.Ceiling(Math.Max(length, textWidth) + 20)), (int)Math.Ceiling(bar + textHeight + 24))); pixels.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(pixels))
        {
            using var paint = new SKPaint { Color = white ? SKColors.White : SKColors.Black, IsAntialias = true };
            var barTop = textOnTop ? 8 + textHeight : 8; canvas.DrawRect(10, barTop, (float)length, bar, paint);
            if (showText) canvas.DrawText(label, 10, textOnTop ? 8 + size : barTop + bar + size, SKTextAlign.Left, font, paint);
        }
        document.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, label), new Model.LayerTransform(10, Math.Max(0, document.Height - pixels.Height - 10), pixels.Width, pixels.Height), label)); return true;
    }
}

public static class ImageVariables
{
    public static IReadOnlyList<ImageDataSet> ParseCsv(string csv)
    {
        if (csv.Length > 4 * 1024 * 1024) throw new InvalidDataException("Data set file is too large.");
        var rows = new List<string[]>(); var row = new List<string>(); var field = new StringBuilder(); bool quoted = false;
        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (c == '"') { if (quoted && i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i++; } else quoted = !quoted; }
            else if (!quoted && c is ',' or '\t') { row.Add(field.ToString()); field.Clear(); }
            else if (!quoted && c is '\r' or '\n') { if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++; row.Add(field.ToString()); field.Clear(); rows.Add(row.ToArray()); row.Clear(); }
            else field.Append(c);
        }
        if (quoted) throw new InvalidDataException("Unclosed CSV quote.");
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row.ToArray()); }
        if (rows.Count < 2 || rows.Count > 1001 || rows[0].Any(string.IsNullOrWhiteSpace) || rows[0].Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows[0].Length)
            throw new InvalidDataException("Use unique variable names in the first row and at most 1000 data sets.");
        return rows.Skip(1).Where(r => r.Any(v => v.Length > 0)).Select((r, index) =>
        {
            if (r.Length != rows[0].Length) throw new InvalidDataException("Data set columns do not match the header.");
            var values = rows[0].Zip(r).ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.OrdinalIgnoreCase);
            var name = values.Remove("name", out var named) ? named : "Data Set " + (index + 1);
            return new ImageDataSet(name, values);
        }).ToArray();
    }
    public static bool Apply(CanvasDocument document, ImageDataSet dataSet, string baseDirectory)
    {
        using var draft = document.Clone(); var allocated = new HashSet<ImportedImage>();
        try
        {
        foreach (var variable in document.Variables)
        {
            if (!dataSet.Values.TryGetValue(variable.Name, out var value)) continue;
            var layer = draft.Layers.FirstOrDefault(l => l.ID == variable.LayerID) ?? throw new InvalidDataException("A variable layer no longer exists.");
            if (LayerProtection.Effective(document, layer.ID).HasFlag(LayerLocks.All)) throw new InvalidOperationException("A variable layer is locked.");
            var previous = layer.Asset;
            switch (variable.Kind)
            {
                case VariableKind.Visibility:
                    if (value is not ("true" or "false" or "1" or "0" or "是" or "否")) throw new InvalidDataException("Visibility values must be true, false, 1 or 0.");
                    layer.IsVisible = value is "true" or "1" or "是"; break;
                case VariableKind.Text:
                    if (layer.LiveText is not { } text || !LayerProtection.CanPaint(document, layer.ID)) throw new InvalidOperationException("Text variables need an unlocked type layer.");
                    var style = JsonSerializer.Deserialize<LayerTextStyle>(JsonSerializer.Serialize(text, ManifestJson.Options), ManifestJson.Options)!;
                    style.Content = value; style.FontRuns = null; style.ColorRuns = null;
                    if (!TextEdits.SetStyle(draft, layer.ID, style)) throw new InvalidDataException("The replacement text could not be rendered."); break;
                case VariableKind.PixelReplacement:
                    if (!LayerProtection.CanPaint(document, layer.ID)) throw new InvalidOperationException("The replacement layer is locked.");
                    var path = Path.GetFullPath(value, baseDirectory); var replacement = ImageImporter.Decode(path);
                    if (draft.Layers.Where(l => l.ID != layer.ID).Sum(l => (long)(l.Asset?.Width ?? 0) * (l.Asset?.Height ?? 0)) + (long)replacement.Width * replacement.Height > DocumentLimits.DocumentPixelBudget)
                    { replacement.Dispose(); throw new InvalidDataException("Data set images exceed the pixel budget."); }
                    layer.Shape = null; layer.Text = null;
                    if ((variable.Method ?? PixelMethod.Conform) == PixelMethod.Conform) { layer.Asset = replacement; break; }
                    // Fit, Fill or As Is: the image keeps its proportions, set in the layer's box at the alignment, and with Clip
                    // cut to the box, as Photoshop places a replacement.
                    var box = layer.Transform; var sw = (double)replacement.Width; var sh = (double)replacement.Height;
                    var scale = variable.Method switch { PixelMethod.Fit => Math.Min(box.Width / sw, box.Height / sh), PixelMethod.Fill => Math.Max(box.Width / sw, box.Height / sh), _ => 1 };
                    var alignment = variable.Alignment ?? 4; var w = sw * scale; var h = sh * scale;
                    var x = box.X + (box.Width - w) * (alignment % 3) / 2; var y = box.Y + (box.Height - h) * (alignment / 3) / 2;
                    if (variable.Clip == true && (w > box.Width + .5 || h > box.Height + .5))
                    {
                        var clipped = Bitmaps.Allocate(Bitmaps.ColorInfo(Math.Max(1, (int)Math.Round(box.Width)), Math.Max(1, (int)Math.Round(box.Height)))); clipped.Erase(SKColors.Transparent);
                        using (var canvas = new SKCanvas(clipped)) using (var picture = SKImage.FromBitmap(replacement.Image))
                            canvas.DrawImage(picture, SKRect.Create((float)(x - box.X), (float)(y - box.Y), (float)w, (float)h), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                        layer.Asset = ImportedImage.Create(clipped, replacement.Name); replacement.Dispose();
                    }
                    else { layer.Asset = replacement; layer.Transform = box with { X = x, Y = y, Width = w, Height = h }; }
                    break;
            }
            if (layer.Asset is { } made && !ReferenceEquals(made, previous)) allocated.Add(made);
        }
        document.Adopt(draft); return true;
        }
        finally
        {
            var retained = document.Layers.Select(l => l.Asset).ToHashSet();
            foreach (var made in allocated) if (!retained.Contains(made)) made.Dispose();
        }
    }
}
