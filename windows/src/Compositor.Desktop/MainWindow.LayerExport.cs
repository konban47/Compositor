using Compositor.Core.IO;
using Compositor.Core.Model;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private async Task ExportWeb()
    {
        if (_document is null) return;
        FinishPlacement(true); FinishPen(); CommitText();
        using var lease = new DocumentLease(_document);
        try { await new WebExportDialog(lease.Document).ShowDialog(this); }
        catch (Exception error) { Say(error.Message); }
    }

    private async Task ExportLayers(LayerExportKind kind)
    {
        if (_document is null) return;
        FinishPlacement(true); FinishPen(); CommitText();
        using var lease = new DocumentLease(_document);
        try { await new LayerExportDialog(lease.Document, kind).ShowDialog<bool>(this); }
        catch (Exception error) { Say(error.Message); }
    }
}
