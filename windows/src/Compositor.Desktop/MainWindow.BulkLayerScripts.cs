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
        Edit("Flatten All Masks", () =>
        {
            var changed = false;
            foreach (var layer in document.Layers.ToArray())
                changed |= MaskProperties.Apply(document, layer.ID);
            return changed;
        });
    }

    private void FlattenAllLayerEffects()
    {
        FinishPlacement(true); FinishPen(); CommitText();
        RasterizeSelected("Effects", true);
        if (_document?.Layers.Any(layer => layer.Gif is not null && layer.Effects is not null) == true)
            Say("Animated layer effects were preserved. Baking animation effects is not supported yet.");
    }
}
