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
    public static MeasurementRecord Measure(CanvasDocument document, string name)
    {
        var scale = document.MeasurementScale; if (!scale.IsValid) throw new InvalidDataException("Invalid measurement scale.");
        var bounds = document.Selection.Path is null ? SKRectI.Create(document.Width, document.Height) : document.Selection.CoverageRect(document.Width, document.Height);
        bounds = SKRectI.Intersect(bounds, SKRectI.Create(document.Width, document.Height));
        if (bounds.IsEmpty) throw new InvalidOperationException("Select a non-empty area to measure.");
        using var mask = document.Selection.Coverage(bounds); using var composite = DocumentRenderer.RenderRegion(document, bounds);
        double area = 0, perimeter = 0, mean = 0;
        var minX = bounds.Width; var minY = bounds.Height; var maxX = -1; var maxY = -1;
        double At(int x, int y) => x < 0 || y < 0 || x >= bounds.Width || y >= bounds.Height ? 0 : mask is null ? 1 : mask.GetPixelSpan()[y * mask.RowBytes + x] / 255.0;
        for (var y = 0; y < bounds.Height; y++) for (var x = 0; x < bounds.Width; x++)
        {
            var a = At(x, y); if (a > 0) { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); } area += a; var c = composite.GetPixel(x, y); mean += a * (.2126 * c.Red + .7152 * c.Green + .0722 * c.Blue);
            perimeter += Math.Abs(a - At(x - 1, y)) + Math.Abs(a - At(x, y - 1));
            if (x == bounds.Width - 1) perimeter += a; if (y == bounds.Height - 1) perimeter += a;
        }
        var ruler = document.Marks.LastOrDefault(m => m.Kind == MarkKind.Measure && m.Visible);
        var factor = scale.Units / scale.Pixels; var length = ruler is null ? 0 : Math.Sqrt(ruler.Width * ruler.Width + ruler.Height * ruler.Height);
        return new(name, DateTimeOffset.UtcNow, area * factor * factor, perimeter * factor, Math.Max(0, maxX - minX + 1) * factor, Math.Max(0, maxY - minY + 1) * factor,
            length * factor, ruler is null ? 0 : Math.Atan2(ruler.Height, ruler.Width) * 180 / Math.PI,
            document.Marks.Count(m => m.Kind == MarkKind.Count && m.Visible), area > 0 ? mean / area : 0, scale.Unit);
    }
    public static string Csv(CanvasDocument document)
    {
        string Quote(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
        var builder = new StringBuilder("Name,Time,Unit"); foreach (var field in document.MeasurementFields) builder.Append(',').Append(field); builder.AppendLine();
        foreach (var record in document.Measurements)
        {
            builder.Append(Quote(record.Name)).Append(',').Append(record.Time.ToString("O", CultureInfo.InvariantCulture)).Append(',').Append(Quote(record.Unit));
            foreach (var field in document.MeasurementFields)
            {
                var value = field switch { "Area" => record.Area, "Perimeter" => record.Perimeter, "Width" => record.Width, "Height" => record.Height,
                    "Length" => record.Length, "Angle" => record.Angle, "Count" => record.Count, _ => record.Mean };
                builder.Append(',').Append(value.ToString("G12", CultureInfo.InvariantCulture));
            }
            builder.AppendLine();
        }
        return builder.ToString();
    }
    public static bool ScaleMarker(CanvasDocument document, double units, string label, bool white)
    {
        if (!document.MeasurementScale.IsValid || !double.IsFinite(units) || units <= 0) return false;
        var length = units * document.MeasurementScale.Pixels / document.MeasurementScale.Units;
        if (length > document.Width - 20 || length < 1 || document.Layers.Count >= LayerPlacement.MaxLayers) return false;
        var pixels = Bitmaps.Allocate(Bitmaps.ColorInfo(Math.Max(32, (int)Math.Ceiling(length + 20)), 64)); pixels.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(pixels))
        {
            using var paint = new SKPaint { Color = white ? SKColors.White : SKColors.Black, IsAntialias = true };
            canvas.DrawRect(10, 8, (float)length, 5, paint); using var font = new SKFont(SKTypeface.Default, 15);
            canvas.DrawText(label, 10, 38, SKTextAlign.Left, font, paint);
        }
        document.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, label), new Model.LayerTransform(10, Math.Max(0, document.Height - 74), pixels.Width, pixels.Height), label)); return true;
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
                    layer.Asset = replacement; layer.Shape = null; layer.Text = null; break;
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
