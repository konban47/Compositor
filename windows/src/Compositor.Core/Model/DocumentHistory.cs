using SkiaSharp;

namespace Compositor.Core.Model;

/// <summary>
/// Undo and redo as value snapshots. A document is copied when an edit begins and again when it ends, and
/// the copies share the same pixels, so a step costs a list of layer records rather than a set of images.
/// </summary>
public sealed partial class DocumentHistory
{
    public readonly record struct Snapshot(CanvasDocument? Document, Guid? ActiveLayerID, Guid Revision);

    private sealed record Entry(string Name, Snapshot Before, Snapshot After);

    private readonly List<Entry> _past = [];
    private readonly List<Entry> _future = [];
    private Guid _revision = Guid.NewGuid();
    private Guid? _savedRevision;
    private Snapshot? _pending;
    private string _pendingName = "Edit";
    private int _depth;

    public DocumentHistory(int entryLimit = 100, int retainedByteLimit = 256 * 1024 * 1024)
    {
        EntryLimit = Math.Max(0, entryLimit);
        RetainedByteLimit = Math.Max(0, retainedByteLimit);
        _savedRevision = _revision;
    }

    public int EntryLimit { get; private set; }
    public int RetainedByteLimit { get; private set; }
    public void Configure(int count, int bytes, CanvasDocument? document)
    { EntryLimit = Math.Clamp(count, 1, 1000); RetainedByteLimit = Math.Max(0, bytes); Trim(document); }

    /// <summary>
    /// When set, an edit made after stepping back keeps the states that were ahead of it instead of throwing
    /// them away, so the redo branch survives a change of mind — Photoshop's non-linear history.
    /// </summary>
    public bool AllowNonLinear { get; set; }
    public Action<string>? Committed { get; set; }

    public bool CanUndo => _depth == 0 && _past.Count > 0;
    public bool CanRedo => _depth == 0 && _future.Count > 0;
    public string UndoName => _past.Count > 0 ? _past[^1].Name : "";
    public string RedoName => _future.Count > 0 ? _future[^1].Name : "";
    public bool IsModified => _revision != _savedRevision;
    public int UndoCount => _past.Count;

    /// <summary>The document as it stands, for a save that captures it now and finishes later.</summary>
    public Guid CurrentRevision => _revision;

    public void MarkUnsaved() => _savedRevision = null;

    public void MarkSaved() => _savedRevision = _revision;

    /// <summary>
    /// A save of <paramref name="saved"/> finished. Edits made while it was writing leave the document
    /// modified; undoing back to it does not.
    /// </summary>
    public void MarkSaved(Guid saved) => _savedRevision = saved;

    public void Reset()
    {
        _snapshots.Clear();
        _initialSnapshotMade = false;
        _past.Clear();
        _future.Clear();
        _pending = null;
        _depth = 0;
        _revision = Guid.NewGuid();
        _savedRevision = _revision;
    }

    public void Begin(string name, CanvasDocument? document, Guid? activeLayer)
    {
        if (_depth == 0)
        {
            _pending = new Snapshot(document?.Clone(), activeLayer, _revision);
            _pendingName = name;
        }
        _depth++;
    }

    public void End(CanvasDocument? document, Guid? activeLayer)
    {
        if (_depth == 0) return;
        _depth--;
        if (_depth > 0 || _pending is not { } before) return;
        _pending = null;
        if (before.Document is not null && document is not null)
        {
            LayerProtection.Enforce(before.Document, document, _pendingName);
            if (_pendingName is not ("Convert Layer to 8-Bit" or "Convert Layer to 16-Bit")) Document.ImageModeEdits.Enforce(before.Document, document);
        }
        // Selecting, navigating and edits that changed nothing must preserve the redo history.
        if (Same(before.Document, document)) return;
        _revision = Guid.NewGuid();
        _past.Add(new Entry(_pendingName, before, new Snapshot(document?.Clone(), activeLayer, _revision)));
        // Linear history throws the redo branch away; non-linear history keeps it so the states ahead stay
        // reachable. The new state still lands after the current one, so both remain in order.
        if (!AllowNonLinear) _future.Clear();
        Trim(document); Committed?.Invoke(_pendingName);
    }

    public Snapshot? Undo()
    {
        if (!CanUndo) return null;
        var entry = _past[^1];
        _past.RemoveAt(_past.Count - 1);
        _future.Add(entry);
        _revision = entry.Before.Revision;
        Trim(entry.Before.Document);
        return entry.Before;
    }

    public Snapshot? Redo()
    {
        if (!CanRedo) return null;
        var entry = _future[^1];
        _future.RemoveAt(_future.Count - 1);
        _past.Add(entry);
        _revision = entry.After.Revision;
        Trim(entry.After.Document);
        return entry.After;
    }

    /// <summary>Bytes only the history holds: the images no live layer names.</summary>
    public long RetainedBytes(CanvasDocument? current) => CountRetained(current).Values.Sum(value => value.Bytes);

    private Dictionary<object, (int Count, long Bytes)> CountRetained(CanvasDocument? current)
    {
        var live = MemoryReferences(current).Select(p => p.Key).ToHashSet(ReferenceEqualityComparer.Instance);
        var retained = new Dictionary<object, (int Count, long Bytes)>(ReferenceEqualityComparer.Instance);
        foreach (var snapshot in _past.Concat(_future).SelectMany(e => new[] { e.Before, e.After }).Concat(_snapshots.Select(s => s.State)))
            foreach (var (key, bytes) in MemoryReferences(snapshot.Document))
            {
                if (live.Contains(key)) continue;
                retained.TryGetValue(key, out var previous);
                retained[key] = (previous.Count + 1, bytes);
            }
        return retained;
    }

    private static IEnumerable<(object Key, long Bytes)> MemoryReferences(CanvasDocument? document)
    {
        foreach (var (image, thumbnail) in Assets(document))
        {
            yield return (image, (long)image.RowBytes * image.Height);
            yield return (thumbnail, (long)thumbnail.RowBytes * thumbnail.Height);
        }
        if (document?.Selection.Path is { } path) yield return (path, 128L + path.PointCount * 16L);
        if (document?.WorkPath is { } work) yield return (work, 24L + work.Length * 2L);
        foreach (var layer in document?.Layers ?? [])
            if (layer.Gif is { } gif) yield return (gif.Source.Data, gif.Source.Data.LongLength);
        foreach (var layer in document?.Layers ?? [])
            if (layer.SmartObject is { } smart) yield return (smart.Package, smart.Package.LongLength);
    }

    private static IEnumerable<(SKBitmap Image, SKBitmap Thumbnail)> Assets(CanvasDocument? document)
    {
        if (document?.Selection.RasterCoverage is { } coverage) yield return (coverage, coverage);
        foreach (var channel in document?.Channels ?? []) yield return (channel.Asset.Image, channel.Asset.Thumbnail);
        foreach (var layer in document?.Layers ?? [])
        {
            foreach (var frame in layer.Gif?.Edited.Values ?? []) { yield return (frame.Image, frame.Thumbnail); if (frame.Deep is { } deepFrame) yield return (deepFrame, deepFrame); }
            if (layer.Asset is { } asset) { yield return (asset.Image, asset.Thumbnail); if (asset.Deep is { } deep) yield return (deep, deep); }
            if (layer.Mask?.Asset is { } mask) yield return (mask.Image, mask.Thumbnail);
        }
    }

    private void Trim(CanvasDocument? current)
    {
        var references = CountRetained(current);
        var bytes = references.Values.Sum(value => value.Bytes);
        void Remove(Snapshot snapshot)
        {
            foreach (var (key, _) in MemoryReferences(snapshot.Document))
            {
                if (!references.TryGetValue(key, out var value)) continue;
                if (value.Count == 1) { bytes -= value.Bytes; references.Remove(key); }
                else references[key] = (value.Count - 1, value.Bytes);
            }
        }
        var past = 0; var future = 0; var saved = 0;
        while (_past.Count + _future.Count - past - future > EntryLimit || bytes > RetainedByteLimit)
        {
            if (past < _past.Count) { var entry = _past[past++]; Remove(entry.Before); Remove(entry.After); }
            else if (future < _future.Count) { var entry = _future[future++]; Remove(entry.Before); Remove(entry.After); }
            else if (saved < _snapshots.Count) Remove(_snapshots[saved++].State);
            else break;
        }
        _past.RemoveRange(0, past); _future.RemoveRange(0, future); _snapshots.RemoveRange(0, saved);
    }

    private static bool Same(CanvasDocument? left, CanvasDocument? right) => left is null
        ? right is null
        : right is not null && left.SameAs(right);
}
