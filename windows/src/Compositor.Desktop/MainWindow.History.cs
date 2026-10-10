using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Model;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private readonly ListBox _historyRows = new();
    private bool _showingHistory;
    private Guid? _chosenSnapshot;
    private readonly Dictionary<Guid, SkiaSharp.SKBitmap> _historyPreviews = [];
    private Control BuildHistoryPanel()
    {
        Closed += (_, _) => { foreach (var bitmap in _historyPreviews.Values) bitmap.Dispose(); _historyPreviews.Clear(); };
        var panel = new DockPanel();
        var footer = new WrapPanel { Margin = new Thickness(6), Orientation = Orientation.Horizontal };
        footer.Children.Add(PropertyAction("New Document from State", NewDocumentFromHistory));
        footer.Children.Add(PropertyAction("Create Snapshot", MakeSnapshot));
        footer.Children.Add(PanelButton("delete", "Delete History State", () => _ = DeleteHistoryTarget()));
        footer.Children.Add(PropertyAction("Clear History", () => _ = ClearHistorySteps()));
        DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer); panel.Children.Add(_historyRows);
        _historyRows.SelectionChanged += (_, _) =>
        {
            if (_showingHistory || _historyRows.SelectedItem is not ListBoxItem { Tag: HistoryTarget target } || _document is null) return;
            CommitText();
            if (target.SnapshotID is { } snapshot)
            {
                _chosenSnapshot = snapshot;
                if (_history.Snapshots.FirstOrDefault(item => item.ID == snapshot) is not { } saved || saved.State.Document is null) return;
                Edit("Restore Snapshot", () => { _document.Adopt(saved.State.Document); return true; });
                ShowLayers(_document); if (saved.State.ActiveLayerID is { } id) SelectLayerRow(id);
            }
            else { _chosenSnapshot = null; RestoreHistory(_history.GoTo(target.Revision)); }
            RefreshInspector();
        };
        return panel;
    }
    private sealed record HistoryTarget(Guid Revision, Guid? SnapshotID = null);

    private void RefreshHistory()
    {
        _showingHistory = true;
        try
        {
            if (_document is not { } document) { _historyRows.ItemsSource = null; return; }
            _history.EnsureInitialSnapshot(document, Selected, _open.Name);
            var retained = _tabs.SelectMany(tab => tab.History.Snapshots).Select(snapshot => snapshot.ID).ToHashSet();
            foreach (var expired in _historyPreviews.Keys.Where(id => !retained.Contains(id)).ToArray()) { _historyPreviews[expired].Dispose(); _historyPreviews.Remove(expired); }
            if (_chosenSnapshot is { } active && _history.Snapshots.FirstOrDefault(item => item.ID == active)?.State.Document?.SameAs(document) != true)
                _chosenSnapshot = null;
            var items = new List<ListBoxItem>();
            foreach (var snapshot in _history.Snapshots)
            {
                if (!_historyPreviews.ContainsKey(snapshot.ID) && snapshot.State.Document is { } saved && (long)saved.Width * saved.Height <= 8_000_000)
                    _historyPreviews[snapshot.ID] = Compositor.Core.Rendering.DocumentRenderer.Preview(saved, 48);
                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children =
                {
                    new LayerThumbnail(() => _historyPreviews.GetValueOrDefault(snapshot.ID), "▣") { Width = 40, Height = 32 },
                    new TextBlock { Text = snapshot.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis },
                } };
                items.Add(new ListBoxItem { Content = content, Tag = new HistoryTarget(snapshot.State.Revision, snapshot.ID) });
            }
            ListBoxItem? current = null;
            foreach (var state in _history.States(document, Selected))
            {
                var item = new ListBoxItem { Content = new TextBlock { Text = (state.IsCurrent ? "▸  " : "   ") + Localize.Text(state.Name),
                    TextTrimming = TextTrimming.CharacterEllipsis, Foreground = state.IsFuture ? Skin.SecondaryBrush : Skin.LabelBrush },
                    Tag = new HistoryTarget(state.Snapshot.Revision), Opacity = state.IsFuture ? .6 : 1 };
                items.Add(item); if (state.IsCurrent) current = item;
            }
            _historyRows.ItemsSource = items;
            var selected = _chosenSnapshot is { } chosen ? items.FirstOrDefault(item => item.Tag is HistoryTarget target && target.SnapshotID == chosen) : null;
            _historyRows.SelectedItem = selected ?? current;
            if (selected is null) _chosenSnapshot = null;
        }
        finally { _showingHistory = false; }
    }
    private void RestoreHistory(DocumentHistory.Snapshot? snapshot)
    {
        if (snapshot?.Document is not { } saved || _document is not { } document) return;
        document.Adopt(saved); ShowLayers(document);
        if (snapshot.Value.ActiveLayerID is { } id) SelectLayerRow(id);
        if (PropertyLayer?.Mask is null) _options.PaintOnMask = false;
        Refresh();
    }
    private void MakeSnapshot()
    {
        CommitText(); if (_document is not { } document) return;
        var name = Localize.Text("Snapshot") + " " + (++_open.SnapshotNumber);
        var made = _history.CreateSnapshot(name, document, Selected);
        _chosenSnapshot = made?.ID; RefreshHistory();
    }
    private void NewDocumentFromHistory()
    {
        CommitText(); if (_document is not { } document) return;
        var source = _chosenSnapshot is { } id ? _history.Snapshots.FirstOrDefault(item => item.ID == id)?.State.Document : document;
        if (source is null) return;
        var independent = DocumentCopies.Independent(source);
        AdoptImported(independent, "New Document from State"); _chosenSnapshot = null;
    }
    private async Task DeleteHistoryTarget()
    {
        var history = _history; var snapshot = _chosenSnapshot; var revision = history.CurrentRevision;
        if (snapshot is null && !history.CanUndo) return;
        if (!await ConfirmDialog.Ask(this, "Delete History State?", snapshot is null ? "Delete this state and all later states?" : "Delete the selected snapshot?", "Delete", "Cancel")) return;
        if (!ReferenceEquals(history, _history) || history.CurrentRevision != revision) return;
        if (snapshot is { } id) { history.DeleteSnapshot(id); _chosenSnapshot = null; }
        else RestoreHistory(history.DeleteCurrentAndLater());
        RefreshHistory();
    }
    private async Task ClearHistorySteps()
    {
        var history = _history; var revision = history.CurrentRevision;
        if (!await ConfirmDialog.Ask(this, "Clear History?", "Remove undo and redo states? The current image and snapshots will be kept.", "Clear", "Cancel")) return;
        if (!ReferenceEquals(history, _history) || history.CurrentRevision != revision) return;
        history.ClearSteps(); _chosenSnapshot = null; Refresh();
    }
}
