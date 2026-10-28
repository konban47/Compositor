using Compositor.Core.IO;

namespace Compositor.Desktop;

public partial class MainWindow
{
    private async Task RevertSavedProject()
    {
        FinishPlacement(true); FinishPen(); CommitText();
        var tab = _open;
        if (tab.Saving is { } saving) await saving;
        if (tab.Document is not { } document || tab.Path is not { } path) return;
        if (!await ConfirmDialog.Ask(this, "Revert to saved version?",
            Localize.Text("Reload the saved project from disk? You can undo this operation."), "Revert", "Cancel")) return;
        var revision = tab.History.CurrentRevision;
        try
        {
            var snapshot = await RunLongTask("Revert", token => ProjectStore.Load(path, token));
            if (!ReferenceEquals(_open, tab) || !ReferenceEquals(tab.Document, document)
                || tab.Path != path || tab.History.CurrentRevision != revision)
            {
                snapshot.Dispose();
                Say("The document changed while the saved version was loading. Revert was cancelled.");
                return;
            }
            var selected = Selected;
            // History snapshots own the previous pixel references. Adopt transfers the loaded references;
            // disposing either document here would invalidate undo or the newly loaded image.
            tab.History.Begin("Revert", document, selected);
            document.Adopt(snapshot.ToDocument());
            tab.History.End(document, selected);
            tab.History.MarkSaved();
            _canvas.Document = document;
            ShowLayers(document);
            if (selected is { } id && document.Layers.Any(layer => layer.ID == id)) Reselect(id);
            Refresh();
            Say("Reverted to the saved project.");
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Say(error.Message); }
    }
}
