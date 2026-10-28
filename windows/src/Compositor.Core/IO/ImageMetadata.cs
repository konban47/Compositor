using System.Text;
using System.Xml;
using System.Xml.Linq;
using Compositor.Core.Format;
using ImageMagick;

namespace Compositor.Core.IO;

public enum MetadataPolicy { None, Copyright, CopyrightAndContact, AllExceptCamera, All }

public static class ImageMetadata
{
    /// <summary>Add curated XMP to freshly encoded output without recompressing JPEG/PNG pixels.</summary>
    public static byte[] AddToEncoded(byte[] bytes, ExportFormat format, DocumentMetadata metadata, MetadataPolicy policy)
    {
        var xmp = Encode(metadata, policy);
        if (xmp is null) return bytes;
        using var stream = new MemoryStream();
        if (format == ExportFormat.Jpeg)
        {
            var identifier = Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0");
            var length = checked(identifier.Length + xmp.Length + 2);
            if (length > ushort.MaxValue) throw new InvalidOperationException("JPEG metadata exceeds the 64 KB XMP packet limit. Shorten File Info or choose a narrower metadata policy.");
            if (bytes.Length < 2 || bytes[0] != 255 || bytes[1] != 216) throw new InvalidDataException("Invalid encoded JPEG.");
            stream.Write(bytes.AsSpan(0, 2)); stream.WriteByte(255); stream.WriteByte(225);
            stream.WriteByte((byte)(length >> 8)); stream.WriteByte((byte)length);
            stream.Write(identifier); stream.Write(xmp); stream.Write(bytes.AsSpan(2));
            return stream.ToArray();
        }
        if (format == ExportFormat.Png)
        {
            if (bytes.Length < 20 || !bytes.AsSpan(bytes.Length - 8, 4).SequenceEqual("IEND"u8)) throw new InvalidDataException("Invalid encoded PNG.");
            var header = Encoding.ASCII.GetBytes("XML:com.adobe.xmp\0\0\0\0\0");
            var data = new byte[header.Length + xmp.Length]; header.CopyTo(data, 0); xmp.CopyTo(data, header.Length);
            stream.Write(bytes.AsSpan(0, bytes.Length - 12));
            Span<byte> number = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length); stream.Write(number);
            stream.Write("iTXt"u8); stream.Write(data);
            uint crc = uint.MaxValue;
            void Update(byte value) { crc ^= value; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u); }
            foreach (var value in "iTXt"u8) Update(value);
            foreach (var value in data) Update(value);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); stream.Write(number);
            stream.Write(bytes.AsSpan(bytes.Length - 12)); return stream.ToArray();
        }
        if (format == ExportFormat.Tiff)
        {
            using var image = new MagickImage(bytes);
            image.SetProfile(new XmpProfile(xmp)); image.Settings.Compression = CompressionMethod.Zip;
            return image.ToByteArray(MagickFormat.Tiff);
        }
        if (format == ExportFormat.Gif)
        {
            if (bytes.Length < 14 || !bytes.AsSpan(0, 3).SequenceEqual("GIF"u8) || bytes[^1] != 0x3b)
                throw new InvalidDataException("Invalid encoded GIF.");
            var offset = 13 + ((bytes[10] & 128) == 0 ? 0 : 3 * (1 << ((bytes[10] & 7) + 1)));
            if (offset >= bytes.Length) throw new InvalidDataException("Invalid GIF color table.");
            stream.Write("GIF89a"u8); stream.Write(bytes.AsSpan(6, offset - 6));
            stream.WriteByte(0x21); stream.WriteByte(0xff); stream.WriteByte(11); stream.Write("XMP DataXMP"u8);
            // XMP Part 3: GIF application data is the raw UTF-8 packet followed by
            // a 258-byte trailer, not the usual length-prefixed GIF sub-blocks.
            stream.Write(xmp); stream.Write(GifXmpTrailer);
            stream.Write(bytes.AsSpan(offset)); return stream.ToArray();
        }
        throw new NotSupportedException("This export format does not support document metadata.");
    }

    private static readonly byte[] GifXmpTrailer = new byte[] { 1 }
        .Concat(Enumerable.Range(0, 256).Reverse().Select(n => (byte)n)).Append((byte)0).ToArray();

    private static byte[]? ReadGifXmp(string path)
    {
        using var input = File.OpenRead(path);
        using var reader = new BinaryReader(input);
        var header = reader.ReadBytes(13);
        if (header.Length != 13 || !header.AsSpan(0, 3).SequenceEqual("GIF"u8)) return null;
        void Skip(long count)
        {
            if (count < 0 || count > input.Length - input.Position) throw new InvalidDataException("Truncated GIF.");
            input.Position += count;
        }
        void Blocks() { int count; while ((count = reader.ReadByte()) != 0) Skip(count); }
        if ((header[10] & 128) != 0) Skip(3 * (1 << ((header[10] & 7) + 1)));
        while (input.Position < input.Length)
        {
            switch (reader.ReadByte())
            {
                case 0x3b: return null;
                case 0x2c:
                    var descriptor = reader.ReadBytes(9);
                    if (descriptor.Length != 9) return null;
                    if ((descriptor[8] & 128) != 0) Skip(3 * (1 << ((descriptor[8] & 7) + 1)));
                    Skip(1); Blocks(); break;
                case 0x21:
                    if (reader.ReadByte() != 0xff) { Blocks(); break; }
                    var name = reader.ReadBytes(reader.ReadByte());
                    if (!name.AsSpan().SequenceEqual("XMP DataXMP"u8)) { Blocks(); break; }
                    using (var packet = new MemoryStream())
                    {
                        while (packet.Length <= 1048576 + GifXmpTrailer.Length && input.Position < input.Length)
                        {
                            var value = reader.ReadByte(); packet.WriteByte(value);
                            if (value == 0 && packet.Length >= GifXmpTrailer.Length &&
                                packet.GetBuffer().AsSpan((int)packet.Length - GifXmpTrailer.Length, GifXmpTrailer.Length).SequenceEqual(GifXmpTrailer))
                                return packet.GetBuffer().AsSpan(0, (int)packet.Length - GifXmpTrailer.Length).ToArray();
                        }
                    }
                    return null;
                default: return null;
            }
        }
        return null;
    }

    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Iptc = "http://iptc.org/std/Iptc4xmpCore/1.0/xmlns/";
    private static readonly XNamespace Tiff = "http://ns.adobe.com/tiff/1.0/";
    private static readonly XNamespace Exif = "http://ns.adobe.com/exif/1.0/";

    public static byte[]? Encode(DocumentMetadata metadata, MetadataPolicy policy)
    {
        if (!metadata.IsValid || !Enum.IsDefined(policy)) throw new ArgumentException("Invalid document metadata.");
        if (policy == MetadataPolicy.None) return null;
        var description = new XElement(Rdf + "Description", new XAttribute(Rdf + "about", ""));
        void Add(XName name, string value, string? collection = null)
        {
            if (value.Length == 0) return;
            description.Add(collection is null ? new XElement(name, value) :
                new XElement(name, new XElement(Rdf + collection, new XElement(Rdf + "li",
                    collection == "Alt" ? new XAttribute(XNamespace.Xml + "lang", "x-default") : null, value))));
        }
        Add(Dc + "rights", metadata.Copyright, "Alt");
        if (policy >= MetadataPolicy.CopyrightAndContact)
        {
            Add(Dc + "creator", metadata.Author, "Seq");
            if (metadata.Email.Length != 0 || metadata.Website.Length != 0)
                description.Add(new XElement(Iptc + "CreatorContactInfo", new XAttribute(Rdf + "parseType", "Resource"),
                    new XElement(Iptc + "CiEmailWork", metadata.Email), new XElement(Iptc + "CiUrlWork", metadata.Website)));
        }
        if (policy >= MetadataPolicy.AllExceptCamera)
        {
            Add(Dc + "title", metadata.Title, "Alt"); Add(Dc + "description", metadata.Description, "Alt");
            if (metadata.Keywords.Length != 0) description.Add(new XElement(Dc + "subject",
                new XElement(Rdf + "Bag", metadata.Keywords.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.Ordinal).Select(k => new XElement(Rdf + "li", k)))));
        }
        if (policy == MetadataPolicy.All)
        { Add(Tiff + "Make", metadata.CameraMake); Add(Tiff + "Model", metadata.CameraModel); Add(Exif + "DateTimeOriginal", metadata.DateTaken); }
        if (!description.HasElements) return null;
        return Encoding.UTF8.GetBytes(new XElement(XName.Get("xmpmeta", "adobe:ns:meta/"),
            new XElement(Rdf + "RDF", description)).ToString(SaveOptions.DisableFormatting));
    }

    public static DocumentMetadata Decode(byte[] data)
    {
        if (data.Length > 1048576) throw new ArgumentException("Metadata exceeds the size limit.");
        using var stream = new MemoryStream(data);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1048576 });
        var xml = XDocument.Load(reader);
        string Get(XName name)
        {
            var element = xml.Descendants(name).FirstOrDefault();
            return element?.Descendants(Rdf + "li").FirstOrDefault()?.Value ?? element?.Value
                ?? xml.Descendants().Attributes(name).FirstOrDefault()?.Value ?? "";
        }
        var result = new DocumentMetadata
        {
            Title = Get(Dc + "title"), Author = Get(Dc + "creator"), Description = Get(Dc + "description"), Copyright = Get(Dc + "rights"),
            Email = Get(Iptc + "CiEmailWork"), Website = Get(Iptc + "CiUrlWork"),
            Keywords = string.Join("; ", xml.Descendants(Dc + "subject").Descendants(Rdf + "li").Select(e => e.Value)),
            CameraMake = Get(Tiff + "Make"), CameraModel = Get(Tiff + "Model"), DateTaken = Get(Exif + "DateTimeOriginal")
        };
        if (!result.IsValid) throw new ArgumentException("Invalid document metadata.");
        return result;
    }

    public static DocumentMetadata Read(string path)
    {
        if (!new[] { ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".gif", ".webp", ".heic", ".heif" }.Contains(Path.GetExtension(path).ToLowerInvariant())) return new();
        try
        {
            if (Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase))
                return ReadGifXmp(path) is { } packet ? Decode(packet) : new();
            using var image = new MagickImage();
            // PNG text chunks and GIF application extensions may follow pixel data;
            // Ping stops before those chunks. Limit animated files to the first frame.
            image.Read(path, new MagickReadSettings { FrameCount = 1 });
            var profile = image.GetXmpProfile();
            return profile is null ? new() : Decode(profile.ToByteArray());
        }
        catch (Exception ex) when (ex is MagickException or XmlException or ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException) { return new(); }
    }
}
