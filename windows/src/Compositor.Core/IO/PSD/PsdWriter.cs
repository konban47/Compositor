using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Compositor.Core.Document;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Core.IO.PSD;

/// <summary>What was written differently from the document, one line per layer or per document, for the report.</summary>
public sealed record PsdWriteNote(string Layer, string Message);

/// <summary>
/// Writes a document as a layered Photoshop file (PSD, RGB, 8 or 16 bits per channel): each pixel, text, shape,
/// smart object and fill layer as pixels with its name, visibility, opacity, blend mode, clipping and pixel mask,
/// groups as Photoshop layer groups, and the composite image for readers that do not read layers. What Photoshop
/// would store as editable objects (type, styles, adjustments) is written as it looks and listed in the notes.
/// </summary>
public static class PsdWriter
{
    public static IReadOnlyList<PsdWriteNote> Write(CanvasDocument document, string path, CancellationToken cancellation = default)
    {
        using var stream = new MemoryStream();
        var notes = Write(document, stream, cancellation);
        AtomicFile.WriteBytes(path, stream.ToArray());
        return notes;
    }

    public static IReadOnlyList<PsdWriteNote> Write(CanvasDocument document, Stream output, CancellationToken cancellation = default)
    {
        if (document.Width > 30000 || document.Height > 30000) throw new ArgumentException("A PSD is at most 30,000 pixels on a side.");
        var notes = new List<PsdWriteNote>();
        var deep = document.Image.Depth > 8 || document.Layers.Any(l => l.Asset?.BitDepth > 8);
        if (document.Image.Depth == 32 || document.Layers.Any(l => l.Asset?.BitDepth == 32))
            notes.Add(new("", "32-bit pixels were written as 16 bits per channel; values past white were clipped."));
        if (document.Image.Mode != Format.ImageMode.RGB) notes.Add(new("", "The image was written in RGB color."));
        var depth = deep ? 16 : 8;
        var records = new List<Record>();
        try
        {
            foreach (var layer in Ordered(document)) { cancellation.ThrowIfCancellationRequested(); records.Add(Prepare(document, layer, depth, notes)); }
            var writer = new BigEndian(output);
            // Header.
            writer.Ascii("8BPS"); writer.U16(1); writer.Bytes(new byte[6]);
            writer.U16(4); writer.U32((uint)document.Height); writer.U32((uint)document.Width); writer.U16((ushort)depth); writer.U16(3);
            writer.U32(0); // Color mode data.
            Resources(writer, document);
            LayersAndMasks(writer, records, depth, cancellation);
            Composite(writer, document, depth, cancellation);
        }
        finally { foreach (var record in records) record.Dispose(); }
        return notes;
    }

    /// <summary>The layers bottom to top as Photoshop lists them: a group's closing divider, its children, then the
    /// group itself.</summary>
    private static IEnumerable<(ImageLayer Layer, Section Section)> Ordered(CanvasDocument document)
    {
        IEnumerable<(ImageLayer, Section)> Children(Guid? parent)
        {
            foreach (var layer in document.Layers.Where(l => l.ParentID == parent))
            {
                if (layer.IsGroup)
                {
                    yield return (layer, Section.Divider);
                    foreach (var child in Children(layer.ID)) yield return child;
                    yield return (layer, Section.Group);
                }
                else yield return (layer, Section.None);
            }
        }
        return Children(null);
    }

    private enum Section { None, Group, Divider }

    private sealed class Record : IDisposable
    {
        public required string Name { get; init; }
        public SKRectI Bounds { get; init; }
        public ushort[][]? Channels { get; init; }   // R, G, B, A at the file's depth, one sample per entry.
        public SKRectI MaskBounds { get; init; }
        public byte[]? Mask { get; init; }
        public bool MaskDisabled { get; init; }
        public required string Blend { get; init; }
        public byte Opacity { get; init; }
        public bool Clipped { get; init; }
        public bool Hidden { get; init; }
        public Section Section { get; init; }
        public int Id { get; init; }
        public void Dispose() { }
    }

    private static Record Prepare(CanvasDocument document, (ImageLayer Layer, Section Section) item, int depth, List<PsdWriteNote> notes)
    {
        var (layer, section) = item;
        var blend = section == Section.Divider ? "norm" : Key(layer.BlendMode) ?? "norm";
        var record = new Record
        {
            Name = section == Section.Divider ? "</Layer group>" : layer.Name,
            Blend = section == Section.Group && layer.BlendMode == Format.LayerBlendMode.Normal ? "pass" : blend,
            Opacity = (byte)Math.Clamp(Math.Round(layer.Opacity * 255), 0, 255),
            Clipped = section == Section.None && layer.MaskSourceID is not null,
            Hidden = !layer.IsVisible,
            Section = section,
            Id = Math.Abs(layer.ID.GetHashCode() % 1_000_000) + 1,
        };
        if (section != Section.None) return record;
        if (layer.Adjustment is not null)
        {
            notes.Add(new(layer.Name, "Adjustment layers are not written; the composite image includes their effect."));
            return record;
        }
        if (layer.Text is not null) notes.Add(new(layer.Name, "Type was written as pixels."));
        else if (layer.Shape is not null) notes.Add(new(layer.Name, "The shape was written as pixels."));
        else if (layer.SmartObject is not null) notes.Add(new(layer.Name, "The smart object was written as pixels."));
        if (layer.Effects is not null) notes.Add(new(layer.Name, "Layer styles are not written; the composite image includes them."));
        if (layer.FillOpacity < 1) notes.Add(new(layer.Name, "Fill opacity was not written."));
        var (bounds, channels) = Pixels(document, layer, depth);
        SKRectI maskBounds = default; byte[]? mask = null;
        if (layer.Mask is { } m && !bounds.IsEmpty)
        {
            (maskBounds, mask) = MaskPixels(layer, m, bounds);
            if (m.Density < 1 || m.Feather > 0) notes.Add(new(layer.Name, "Mask density and feather were not written."));
            if (m.VectorPath is not null) notes.Add(new(layer.Name, "The vector mask was written as pixels."));
        }
        return new Record
        {
            Name = record.Name, Blend = record.Blend, Opacity = record.Opacity, Clipped = record.Clipped, Hidden = record.Hidden,
            Section = section, Id = record.Id, Bounds = bounds, Channels = channels, MaskBounds = maskBounds, Mask = mask,
            MaskDisabled = layer.Mask is { IsEnabled: false },
        };
    }

    /// <summary>The layer alone, drawn as the document draws it but without its opacity, blend, mask, clipping or
    /// styles (which the file keeps apart), cropped to what it covers.</summary>
    private static (SKRectI Bounds, ushort[][]? Channels) Pixels(CanvasDocument document, ImageLayer layer, int depth)
    {
        using var solo = document.Clone();
        solo.Layers.RemoveAll(l => l.ID != layer.ID);
        var alone = solo.Layers.Single();
        alone.ParentID = null; alone.IsVisible = true; alone.Opacity = 1; alone.BlendMode = Format.LayerBlendMode.Normal;
        alone.MaskSourceID = null; alone.Mask = null; alone.Effects = null; alone.FillOpacity = 1;
        float[] floats; int width = document.Width, height = document.Height;
        if (depth == 16 && DocumentRenderer.SupportsDeepComposite(solo))
        {
            using var rendered = DocumentRenderer.RenderDeep(solo);
            using var plain = DeepPixels.From(rendered);
            floats = MemoryMarshal.Cast<byte, float>(plain.GetPixelSpan()).ToArray();
            width = plain.Width; height = plain.Height;
            if (plain.RowBytes != width * 16) floats = Rows(floats, plain.RowBytes / 4, width, height);
        }
        else
        {
            using var rendered = DocumentRenderer.Render(solo);
            using var plain = DeepPixels.From(rendered);
            floats = MemoryMarshal.Cast<byte, float>(plain.GetPixelSpan()).ToArray();
            if (plain.RowBytes != width * 16) floats = Rows(floats, plain.RowBytes / 4, width, height);
        }
        int left = width, top = height, right = 0, bottom = 0;
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            if (floats[(y * width + x) * 4 + 3] > 0) { left = Math.Min(left, x); right = Math.Max(right, x + 1); top = Math.Min(top, y); bottom = Math.Max(bottom, y + 1); }
        if (right <= left) return (SKRectI.Empty, null);
        var bounds = new SKRectI(left, top, right, bottom);
        var channels = new ushort[4][];
        var scale = depth == 16 ? 65535.0 : 255.0;
        for (var c = 0; c < 4; c++)
        {
            var plane = new ushort[bounds.Width * bounds.Height];
            for (var y = 0; y < bounds.Height; y++) for (var x = 0; x < bounds.Width; x++)
                plane[y * bounds.Width + x] = (ushort)Math.Clamp(Math.Round(floats[((y + top) * width + x + left) * 4 + c] * scale), 0, scale);
            channels[c] = plane;
        }
        return (bounds, channels);
    }

    private static float[] Rows(float[] floats, int stride, int width, int height)
    {
        var packed = new float[width * height * 4];
        for (var y = 0; y < height; y++) Array.Copy(floats, y * stride, packed, y * width * 4, width * 4);
        return packed;
    }

    /// <summary>The layer's pixel mask over the layer's own bounds, 0 outside what the mask covers.</summary>
    private static (SKRectI Bounds, byte[] Pixels) MaskPixels(ImageLayer layer, LayerMask mask, SKRectI bounds)
    {
        using var drawn = Bitmaps.Allocate(Bitmaps.MaskInfo(bounds.Width, bounds.Height));
        using (var canvas = new SKCanvas(drawn))
        {
            canvas.Clear(SKColors.Black);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.Concat(BrushEdits.PixelToDocument(layer.MaskTransform, mask.Asset.Width, mask.Asset.Height));
            canvas.DrawBitmap(mask.Asset.Image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear));
        }
        var span = drawn.GetPixelSpan(); var pixels = new byte[bounds.Width * bounds.Height];
        for (var y = 0; y < bounds.Height; y++) span.Slice(y * drawn.RowBytes, bounds.Width).CopyTo(pixels.AsSpan(y * bounds.Width));
        return (bounds, pixels);
    }

    internal static string? Key(Format.LayerBlendMode mode) => mode switch
    {
        Format.LayerBlendMode.Normal => "norm", Format.LayerBlendMode.Multiply => "mul ", Format.LayerBlendMode.Screen => "scrn",
        Format.LayerBlendMode.Overlay => "over", Format.LayerBlendMode.SoftLight => "sLit", Format.LayerBlendMode.Darken => "dark",
        Format.LayerBlendMode.Lighten => "lite", Format.LayerBlendMode.Difference => "diff", Format.LayerBlendMode.ColorDodge => "div ",
        Format.LayerBlendMode.ColorBurn => "idiv", Format.LayerBlendMode.Hue => "hue ", Format.LayerBlendMode.Saturation => "sat ",
        Format.LayerBlendMode.Color => "colr", Format.LayerBlendMode.Luminosity => "lum ", Format.LayerBlendMode.LinearBurn => "lbrn",
        Format.LayerBlendMode.LinearDodgeAdd => "lddg", Format.LayerBlendMode.HardLight => "hLit", Format.LayerBlendMode.VividLight => "vLit",
        Format.LayerBlendMode.LinearLight => "lLit", Format.LayerBlendMode.PinLight => "pLit", Format.LayerBlendMode.HardMix => "hMix",
        Format.LayerBlendMode.Exclusion => "smud", Format.LayerBlendMode.Subtract => "fsub", Format.LayerBlendMode.Divide => "fdiv",
        _ => null,
    };

    private static void Resources(BigEndian writer, CanvasDocument document)
    {
        using var section = new MemoryStream(); var resources = new BigEndian(section);
        void Resource(ushort id, byte[] data)
        {
            resources.Ascii("8BIM"); resources.U16(id); resources.U16(0); // Empty Pascal name, padded to even.
            resources.U32((uint)data.Length); resources.Bytes(data); if (data.Length % 2 == 1) resources.Bytes([0]);
        }
        // Resolution (1005): fixed-point pixels per inch, shown in inches.
        var ppi = (uint)Math.Clamp(Math.Round(document.Resolution * 65536), 65536, uint.MaxValue);
        using (var resolution = new MemoryStream())
        {
            var r = new BigEndian(resolution);
            r.U32(ppi); r.U16(1); r.U16(1); r.U32(ppi); r.U16(1); r.U16(1);
            Resource(1005, resolution.ToArray());
        }
        if (document.Image.ColorProfile is { Length: > 0 } icc && IccProfiles.Model(icc) == "RGB") Resource(1039, icc);
        writer.U32((uint)section.Length); writer.Bytes(section.ToArray());
    }

    private static void LayersAndMasks(BigEndian writer, List<Record> records, int depth, CancellationToken cancellation)
    {
        using var info = new MemoryStream(); var layers = new BigEndian(info);
        // A negative count says the first alpha channel is the composite's transparency.
        layers.U16(unchecked((ushort)(short)-records.Count));
        var data = new List<byte[]>();
        foreach (var record in records)
        {
            cancellation.ThrowIfCancellationRequested();
            var b = record.Bounds;
            layers.I32(b.Top); layers.I32(b.Left); layers.I32(b.Bottom); layers.I32(b.Right);
            var channels = new List<(short Id, byte[] Data)>();
            if (record.Channels is { } planes)
            {
                channels.Add((-1, Channel(planes[3], b.Width, b.Height, depth)));
                for (short c = 0; c < 3; c++) channels.Add((c, Channel(planes[c], b.Width, b.Height, depth)));
            }
            else foreach (var id in new short[] { -1, 0, 1, 2 }) channels.Add((id, [0, 0]));
            if (record.Mask is { } mask)
                channels.Add((-2, Channel([.. mask.Select(v => depth == 16 ? (ushort)(v * 257) : (ushort)v)], record.MaskBounds.Width, record.MaskBounds.Height, depth)));
            layers.U16((ushort)channels.Count);
            foreach (var (id, bytes) in channels) { layers.I16(id); layers.U32((uint)bytes.Length); }
            layers.Ascii("8BIM"); layers.Ascii(record.Blend);
            layers.Bytes([record.Opacity, (byte)(record.Clipped ? 1 : 0), (byte)((record.Hidden ? 2 : 0) | 8), 0]);
            using var extra = new MemoryStream(); var e = new BigEndian(extra);
            if (record.Mask is not null)
            {
                var m = record.MaskBounds;
                e.U32(20); e.I32(m.Top); e.I32(m.Left); e.I32(m.Bottom); e.I32(m.Right); e.Bytes([0, (byte)(record.MaskDisabled ? 2 : 0), 0, 0]);
            }
            else e.U32(0);
            e.U32(0); // Blending ranges.
            var name = Encoding.Latin1.GetBytes(new string(record.Name.Select(c => c < 256 ? c : '?').Take(255).ToArray()));
            var pascal = 1 + name.Length; var padded = (pascal + 3) / 4 * 4;
            e.Bytes([(byte)name.Length]); e.Bytes(name); e.Bytes(new byte[padded - pascal]);
            // The name in full Unicode.
            using (var unicode = new MemoryStream())
            {
                var u = new BigEndian(unicode); u.U32((uint)record.Name.Length); foreach (var c in record.Name) u.U16(c);
                Tagged(e, "luni", unicode.ToArray());
            }
            using (var id = new MemoryStream()) { new BigEndian(id).U32((uint)record.Id); Tagged(e, "lyid", id.ToArray()); }
            if (record.Section != Section.None)
            {
                using var divider = new MemoryStream(); var d = new BigEndian(divider);
                d.U32(record.Section == Section.Divider ? 3u : 1u);
                if (record.Section == Section.Group) { d.Ascii("8BIM"); d.Ascii(record.Blend); }
                Tagged(e, "lsct", divider.ToArray());
            }
            layers.U32((uint)extra.Length); layers.Bytes(extra.ToArray());
            data.AddRange(channels.Select(c => c.Data));
        }
        foreach (var bytes in data) layers.Bytes(bytes);
        if (info.Length % 2 == 1) layers.Bytes([0]);
        using var section = new MemoryStream(); var all = new BigEndian(section);
        all.U32((uint)info.Length); all.Bytes(info.ToArray());
        all.U32(0); // Global layer mask info.
        writer.U32((uint)section.Length); writer.Bytes(section.ToArray());
    }

    private static void Tagged(BigEndian writer, string key, byte[] data)
    {
        writer.Ascii("8BIM"); writer.Ascii(key);
        var length = (data.Length + 1) / 2 * 2;
        writer.U32((uint)length); writer.Bytes(data); if (length > data.Length) writer.Bytes([0]);
    }

    /// <summary>One channel's data with its compression: PackBits rows at 8 bits, raw samples at 16.</summary>
    private static byte[] Channel(ushort[] samples, int width, int height, int depth)
    {
        using var stream = new MemoryStream(); var w = new BigEndian(stream);
        if (depth == 16)
        {
            w.U16(0);
            foreach (var sample in samples) w.U16(sample);
            return stream.ToArray();
        }
        w.U16(1);
        var rows = new byte[height][];
        for (var y = 0; y < height; y++)
        {
            var row = new byte[width];
            for (var x = 0; x < width; x++) row[x] = (byte)samples[y * width + x];
            rows[y] = PackBits(row);
        }
        foreach (var row in rows) w.U16((ushort)row.Length);
        foreach (var row in rows) w.Bytes(row);
        return stream.ToArray();
    }

    internal static byte[] PackBits(byte[] row)
    {
        var output = new List<byte>(row.Length + row.Length / 128 + 2);
        var i = 0;
        while (i < row.Length)
        {
            var run = 1;
            while (i + run < row.Length && run < 128 && row[i + run] == row[i]) run++;
            if (run >= 2)
            {
                output.Add((byte)(257 - run)); output.Add(row[i]); i += run; continue;
            }
            var start = i; var literal = 0;
            while (i < row.Length && literal < 128 && !(i + 1 < row.Length && row[i + 1] == row[i] && (i + 2 >= row.Length || row[i + 2] == row[i])))
            { i++; literal++; }
            if (literal == 0) { i++; literal = 1; }
            output.Add((byte)(literal - 1));
            for (var k = start; k < start + literal; k++) output.Add(row[k]);
        }
        return [.. output];
    }

    private static void Composite(BigEndian writer, CanvasDocument document, int depth, CancellationToken cancellation)
    {
        float[] floats; int width, height, stride;
        if (depth == 16 && DocumentRenderer.SupportsDeepComposite(document))
        {
            using var rendered = DocumentRenderer.RenderDeep(document); using var plain = DeepPixels.From(rendered);
            floats = MemoryMarshal.Cast<byte, float>(plain.GetPixelSpan()).ToArray(); width = plain.Width; height = plain.Height; stride = plain.RowBytes / 4;
        }
        else
        {
            using var rendered = DocumentRenderer.Render(document); using var plain = DeepPixels.From(rendered);
            floats = MemoryMarshal.Cast<byte, float>(plain.GetPixelSpan()).ToArray(); width = plain.Width; height = plain.Height; stride = plain.RowBytes / 4;
        }
        cancellation.ThrowIfCancellationRequested();
        var scale = depth == 16 ? 65535.0 : 255.0;
        var planes = new ushort[4][];
        for (var c = 0; c < 4; c++)
        {
            var plane = new ushort[width * height];
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                plane[y * width + x] = (ushort)Math.Clamp(Math.Round(floats[y * stride + x * 4 + c] * scale), 0, scale);
            planes[c] = plane;
        }
        if (depth == 16)
        {
            writer.U16(0);
            foreach (var plane in planes) foreach (var sample in plane) writer.U16(sample);
            return;
        }
        writer.U16(1);
        var packed = planes.SelectMany(plane => Enumerable.Range(0, height).Select(y => PackBits([.. plane.AsSpan(y * width, width).ToArray().Select(v => (byte)v)]))).ToArray();
        foreach (var row in packed) writer.U16((ushort)row.Length);
        foreach (var row in packed) writer.Bytes(row);
    }

    private sealed class BigEndian(Stream stream)
    {
        private readonly byte[] _buffer = new byte[4];
        public void Bytes(ReadOnlySpan<byte> bytes) => stream.Write(bytes);
        public void Ascii(string text) => stream.Write(Encoding.ASCII.GetBytes(text));
        public void U16(ushort value) { BinaryPrimitives.WriteUInt16BigEndian(_buffer, value); stream.Write(_buffer, 0, 2); }
        public void I16(short value) { BinaryPrimitives.WriteInt16BigEndian(_buffer, value); stream.Write(_buffer, 0, 2); }
        public void U32(uint value) { BinaryPrimitives.WriteUInt32BigEndian(_buffer, value); stream.Write(_buffer, 0, 4); }
        public void I32(int value) { BinaryPrimitives.WriteInt32BigEndian(_buffer, value); stream.Write(_buffer, 0, 4); }
    }
}
