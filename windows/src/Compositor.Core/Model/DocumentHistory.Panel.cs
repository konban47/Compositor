namespace Compositor.Core.Model;

public sealed partial class DocumentHistory
{
    public sealed record State(string Name, Snapshot Snapshot, bool IsCurrent, bool IsFuture);
    public sealed record SavedSnapshot(Guid ID, string Name, Snapshot State);
    private readonly List<SavedSnapshot> _snapshots = [];
    private bool _initialSnapshotMade;
    public IReadOnlyList<SavedSnapshot> Snapshots => _snapshots;
    public bool IsEditing => _depth > 0;

    public void EnsureInitialSnapshot(CanvasDocument document, Guid? activeLayer, string name)
    {
        if (_initialSnapshotMade || _depth > 0) return;
        _initialSnapshotMade = true;
        var original = _past.Count > 0 && _past[0].Before.Document is { } before ? before : document;
        CreateSnapshot(name, original, activeLayer);
    }

    public IReadOnlyList<State> States(CanvasDocument? document, Guid? activeLayer)
    {
        var entries = _past.Concat(_future.AsEnumerable().Reverse()).ToArray();
        var first = entries.Length > 0 ? entries[0].Before : new Snapshot(document?.Clone(), activeLayer, _revision);
        var result = new List<State> { new("Initial State", first, first.Revision == _revision, false) };
        result.AddRange(entries.Select((entry, index) => new State(entry.Name, entry.After,
            entry.After.Revision == _revision, index >= _past.Count)));
        return result;
    }

    /// <summary>Jump without discarding redo; the next edit branches from the chosen state.</summary>
    public Snapshot? GoTo(Guid revision)
    {
        if (_depth > 0 || revision == _revision) return null;
        var entries = _past.Concat(_future.AsEnumerable().Reverse()).ToArray();
        if (entries.Length == 0) return null;
        var target = revision == entries[0].Before.Revision ? 0 : Array.FindIndex(entries, entry => entry.After.Revision == revision) + 1;
        if (target == 0 && revision != entries[0].Before.Revision) return null;
        _past.Clear(); _past.AddRange(entries.Take(target));
        _future.Clear(); _future.AddRange(entries.Skip(target).Reverse());
        var state = target == 0 ? entries[0].Before : entries[target - 1].After;
        _revision = state.Revision; return state;
    }

    public SavedSnapshot? CreateSnapshot(string name, CanvasDocument document, Guid? activeLayer)
    {
        if (_depth > 0) return null;
        var snapshot = new SavedSnapshot(Guid.NewGuid(), name, new Snapshot(document.Clone(), activeLayer, _revision));
        _snapshots.Add(snapshot);
        while (_snapshots.Count > 20) _snapshots.RemoveAt(0);
        Trim(document);
        return _snapshots.Contains(snapshot) ? snapshot : null;
    }

    public bool DeleteSnapshot(Guid id) => _snapshots.RemoveAll(snapshot => snapshot.ID == id) > 0;

    /// <summary>Delete the selected current state and every later redo state, keeping earlier states.</summary>
    public Snapshot? DeleteCurrentAndLater()
    {
        if (!CanUndo) return null;
        var previous = Undo(); _future.Clear(); return previous;
    }

    public void ClearSteps()
    {
        if (_depth > 0) return;
        _past.Clear(); _future.Clear();
    }
}
