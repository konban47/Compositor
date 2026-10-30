using System.Security.Cryptography;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Rendering;

namespace Compositor.Core.Model;

/// <summary>Whether a linked smart object's file is still as it was read, has changed, or cannot be found.</summary>
public enum LinkState { Current, Modified, Missing }

/// <summary>
/// Linked smart objects: a placed file whose content stays the file's. The layer keeps a copy (so the document
/// still opens and draws without the file) and the file's SHA-256; Update Modified Content reads the file again,
/// Relink points it at another file, Embed keeps the copy and forgets the file, and Package copies every linked
/// file beside the project and points the layers at the copies.
/// </summary>
public static class SmartObjectLinks
{
    public const int MaxPathLength = 32768;

    public static bool ValidLink(string? path, string? hash) =>
        path is { Length: > 0 and <= MaxPathLength } && Path.IsPathRooted(path) && !path.Any(char.IsControl)
        && hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    public static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static LinkState State(SmartObjectData smart)
    {
        if (smart.LinkedPath is not { } path) return LinkState.Current;
        if (!File.Exists(path)) return LinkState.Missing;
        try { return string.Equals(Hash(path), smart.LinkedHash, StringComparison.OrdinalIgnoreCase) ? LinkState.Current : LinkState.Modified; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return LinkState.Missing; }
    }

    /// <summary>The distinct linked smart objects in the document with their state, one per shared source.</summary>
    public static IReadOnlyList<(Guid SmartID, string Path, LinkState State)> Links(CanvasDocument document) =>
        [.. document.Layers.Where(l => l.SmartObject?.LinkedPath is not null).GroupBy(l => l.SmartObject!.ID)
            .Select(g => (g.Key, g.First().SmartObject!.LinkedPath!, State(g.First().SmartObject!)))];

    /// <summary>The layer made a linked smart object of <paramref name="path"/>: the file read now, its content and
    /// hash kept. False when the layer is not a smart object or cannot change.</summary>
    public static bool Link(CanvasDocument document, Guid layerID, string path)
    {
        if (document.Layers.FirstOrDefault(l => l.ID == layerID) is not { SmartObject: { } smart }) return false;
        var full = Path.GetFullPath(path);
        var hash = Hash(full);
        using var source = ImageImporter.NewDocument(ImageImporter.Decode(full));
        if (!LayerWorkflow.UpdateSmartObject(document, smart.ID, source)) return false;
        foreach (var layer in document.Layers.Where(l => l.SmartObject?.ID == smart.ID).ToArray())
            layer.SmartObject = new SmartObjectData(layer.SmartObject!.ID, layer.SmartObject.Package) { LinkedPath = full, LinkedHash = hash };
        return true;
    }

    /// <summary>Every linked smart object whose file changed read again; the number updated.</summary>
    public static int UpdateModified(CanvasDocument document, IReadOnlyCollection<Guid>? only = null)
    {
        var updated = 0;
        foreach (var (smartID, path, state) in Links(document))
        {
            if (state != LinkState.Modified) continue;
            var layer = document.Layers.First(l => l.SmartObject?.ID == smartID);
            if (only is not null && !document.Layers.Any(l => l.SmartObject?.ID == smartID && only.Contains(l.ID))) continue;
            if (Link(document, layer.ID, path)) updated++;
        }
        return updated;
    }

    /// <summary>The linked smart objects of the selected layers made embedded: the copy kept, the file forgotten.</summary>
    public static bool Embed(CanvasDocument document, IReadOnlyCollection<Guid> ids)
    {
        var changed = false;
        foreach (var smartID in document.Layers.Where(l => ids.Contains(l.ID) && l.SmartObject?.LinkedPath is not null).Select(l => l.SmartObject!.ID).Distinct().ToArray())
            foreach (var layer in document.Layers.Where(l => l.SmartObject?.ID == smartID))
            { layer.SmartObject = new SmartObjectData(smartID, layer.SmartObject!.Package); changed = true; }
        return changed;
    }

    /// <summary>
    /// File › Package: each linked file copied into <paramref name="folder"/>/Links (names made unique) and the layers
    /// pointed at the copies. A missing file is listed and left as it is. Returns the copies made and the missing.
    /// </summary>
    public static (IReadOnlyList<string> Copied, IReadOnlyList<string> Missing) Package(CanvasDocument document, string folder)
    {
        var links = Path.Combine(Path.GetFullPath(folder), "Links");
        var copied = new List<string>(); var missing = new List<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (smartID, path, _) in Links(document))
        {
            if (!File.Exists(path)) { missing.Add(path); continue; }
            Directory.CreateDirectory(links);
            var name = Path.GetFileName(path); var stem = Path.GetFileNameWithoutExtension(path); var extension = Path.GetExtension(path);
            for (var n = 2; !names.Add(name); n++) name = $"{stem} {n}{extension}";
            var target = Path.Combine(links, name);
            if (!string.Equals(Path.GetFullPath(path), target, StringComparison.OrdinalIgnoreCase)) File.Copy(path, target, true);
            var hash = Hash(target);
            foreach (var layer in document.Layers.Where(l => l.SmartObject?.ID == smartID))
                layer.SmartObject = new SmartObjectData(smartID, layer.SmartObject!.Package) { LinkedPath = target, LinkedHash = hash };
            copied.Add(target);
        }
        return (copied, missing);
    }
}
