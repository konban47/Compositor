using System.Text.Json;
using System.Text.Json.Serialization;
using Compositor.Core.Format;

namespace Compositor.Core.IO;

/// <summary>Portable text annotations, independent of the image pixels and their document IDs.</summary>
public static class DocumentNotesFile
{
    private sealed record Envelope(int Version, DocumentMark[] Notes);
    private const int MaxBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static DocumentMark[] Read(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaxBytes) throw new InvalidDataException("The notes file exceeds 4 MB.");
        var file = JsonSerializer.Deserialize<Envelope>(stream, Json);
        if (file is not { Version: 1, Notes: { Length: > 0 and <= 1000 } }
            || file.Notes.Any(note => note is null || note.Kind != MarkKind.Note || !note.IsValid))
            throw new InvalidDataException("Invalid Compositor notes file.");
        return file.Notes.Select(note => note with { ID = Guid.NewGuid() }).ToArray();
    }

    public static byte[] Encode(IEnumerable<DocumentMark> marks)
    {
        var notes = marks.Where(mark => mark.Kind == MarkKind.Note).ToArray();
        if (notes.Length is < 1 or > 1000 || notes.Any(note => !note.IsValid))
            throw new InvalidDataException("Select between 1 and 1000 valid text notes.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Envelope(1, notes), Json);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("The notes file exceeds 4 MB.");
        return bytes;
    }
}
