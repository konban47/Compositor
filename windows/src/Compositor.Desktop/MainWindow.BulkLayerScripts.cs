using Compositor.Core.Document;

namespace Compositor.Desktop;

public partial class MainWindow
{
    private void DeleteAllEmptyLayers()
    {
        FinishPlacement(true); FinishPen(); CommitText();
        if (_document is not { } document) return;
        Edit("Delete All Empty Layers", () => LayerWorkflow.Clean(document, "Empty") > 0);
    }

    private void FlattenAllMasks()
    {
        FinishPlacement(true); FinishPen(); CommitText();
        if (_document is not { } document) return;
        var notes = new List<string>();
        Edit("Flatten All Masks", () =>
        {
            var changed = false;
            foreach (var layer in document.Layers.Where(l => !l.IsGroup).ToArray())
                changed |= MaskProperties.Apply(document, layer.ID);
            // Groups innermost first, so an outer group's merge takes in what the inner ones became.
            foreach (var group in Innermost(document))
                changed |= LayerBaking.FlattenGroupMask(document, group, notes);
            return changed;
        });
        if (notes.Count > 0) Say(string.Join("  ", notes.Select(Localize.Text)));
    }

    private void FlattenAllLayerEffects()
    {
        FinishPlacement(true); FinishPen(); CommitText();
        RasterizeSelected("Effects", true);
        if (_document is not { } document) return;
        var notes = new List<string>();
        if (document.Layers.Any(l => l.Effects is not null && (l.IsGroup || l.Gif is not null)))
            Edit("Flatten All Layer Effects", () =>
            {
                var changed = false;
                foreach (var layer in document.Layers.Where(l => l.Gif is not null && l.Effects is not null).ToArray())
                    changed |= LayerBaking.FlattenGifEffects(document, layer.ID, notes);
                foreach (var group in Innermost(document))
                    changed |= LayerBaking.FlattenGroupEffects(document, group, notes);
                return changed;
            });
        if (notes.Count > 0) Say(string.Join("  ", notes.Select(Localize.Text)));
    }

    /// <summary>The groups, the most deeply nested first.</summary>
    private static Guid[] Innermost(Compositor.Core.Model.CanvasDocument document)
    {
        int Depth(Guid id) { var depth = 0; var layer = document.Layers.FirstOrDefault(l => l.ID == id); while (layer?.ParentID is { } parent && depth < 64) { depth++; layer = document.Layers.FirstOrDefault(l => l.ID == parent); } return depth; }
        return [.. document.Layers.Where(l => l.IsGroup).Select(l => l.ID).OrderByDescending(Depth)];
    }
}