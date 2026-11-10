using Compositor.Core.Document;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    /// <summary>
    /// Edit ▸ Content-Aware Fill…: opens the workspace on the selection. Each Apply (and OK) is one undo step, with the edited fill
    /// area left as the selection; Filter ▸ Content-Aware Fill still fills at once with the defaults.
    /// </summary>
    private async Task ContentAwareFillWorkspace()
    {
        if (_document is not { } doc || Selected is not { } id) return;
        if (doc.Selection.Path is null || doc.Selection.IsEmpty) { Say("Content-Aware Fill needs a selection to fill"); return; }
        if (doc.Layers.FirstOrDefault(l => l.ID == id) is not { Asset: not null, IsGroup: false }) { Say("Content-Aware Fill needs a layer with pixels of its own"); return; }
        CommitText();
        var fill = new byte[doc.Width * doc.Height];
        using (var coverage = doc.Selection.Coverage(SKRectI.Create(doc.Width, doc.Height)))
        {
            if (coverage is null) return;
            var span = coverage.GetPixelSpan(); for (var y = 0; y < doc.Height; y++) for (var x = 0; x < doc.Width; x++) fill[y * doc.Width + x] = span[y * coverage.RowBytes + x];
        }
        var window = new ContentAwareFillWindow(doc, id, fill, () => DocumentRenderer.Render(doc), request => ReferenceEquals(doc, _document) && ApplyContentFill(doc, id, request));
        await window.ShowDialog<bool>(this);
        Refresh();
    }

    private bool ApplyContentFill(CanvasDocument doc, Guid id, ContentFillRequest request)
    {
        Guid? made = null;
        var done = Edit("Content-Aware Fill", () => ContentAwareFillWindow.Perform(doc, id, request, out made));
        if (done) { Reselect(made); Refresh(); }
        return done;
    }
}
