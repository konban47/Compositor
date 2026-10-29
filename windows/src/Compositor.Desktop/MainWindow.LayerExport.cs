using Compositor.Core.IO;
using Compositor.Core.Model;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private async Task ExportWeb()
    {
        if (_document is null) return;
        FinishPlacement(true); FinishPen(); CommitText();
        var exporting = _open;
        using var lease = new DocumentLease(_document);
        var completed = 0;
        try
        {
            var dialog = new WebExportDialog(lease.Document);
            await dialog.ShowDialog(this);
            completed = dialog.CompletedExports;
        }
        catch (Exception error) { Say(error.Message); }
        // Raised after the modal dialog closes, once however many files it wrote, so a script never runs under it.
        if (completed > 0) await RaiseScriptEvent(ScriptEvent.ExportDocument, exporting);
    }

    private async Task ExportLayers(LayerExportKind kind)
    {
        if (_document is null) return;
        FinishPlacement(true); FinishPen(); CommitText();
        var exporting = _open;
        using var lease = new DocumentLease(_document);
        var completed = 0;
        try
        {
            var dialog = new LayerExportDialog(lease.Document, kind);
            await dialog.ShowDialog<bool>(this);
            completed = dialog.CompletedExports;
        }
        catch (Exception error) { Say(error.Message); }
        if (completed > 0) await RaiseScriptEvent(ScriptEvent.ExportDocument, exporting);
    }
}
