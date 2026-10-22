using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;
namespace Compositor.Desktop;
public sealed partial class MainWindow
{
    private sealed record ContentMoveDraft(Guid Target, Guid Preview, SKRectI Origin, bool Extend, bool All, int Structure, int Color);
    private ContentMoveDraft? _contentMove;
    private void BeginContentMove(CanvasDocument doc, Guid id, SKPoint delta)
    {
        if (!LayerProtection.CanPaint(doc, id) || !LayerProtection.CanMove(doc, id) || delta == default) return;
        var payload = ContentMoveEdits.Capture(doc, id, Pro.MoveAllLayers); if (payload is null) return;
        using var pixels = payload.Value.Pixels; var origin = payload.Value.Region;
        var destination = new LayerTransform(origin.Left + Math.Round(delta.X), origin.Top + Math.Round(delta.Y), origin.Width, origin.Height);
        if (!Pro.MoveTransform)
        {
            Edit("Content-Aware Move Tool", () => ContentMoveEdits.Apply(doc, id, pixels, origin, destination, Pro.ExtendMove, Pro.MoveAllLayers, Pro.MoveStructure, Pro.MoveColor)); return;
        }
        var before = doc.Clone(); _history.Begin("Content-Aware Move Tool", doc, id);
        var preview = pixels.Copy(); var made = LayerPlacement.AddImage(doc, preview, SKRectI.Create((int)destination.X, (int)destination.Y, (int)destination.Width, (int)destination.Height), "Content Move Preview", id);
        if (made is null) { preview.Dispose(); _history.End(doc, id); before.Dispose(); return; }
        var state = new ContentMoveDraft(id, made.Value, origin, Pro.ExtendMove, Pro.MoveAllLayers, Pro.MoveStructure, Pro.MoveColor);
        BeginPlacement(_open, before, id, [made.Value]); _contentMove = state;
    }
    private bool CompleteContentMove(bool apply)
    {
        var state = _contentMove; _contentMove = null;
        if (!apply || state is null || _document is not { } doc || _placementBefore is null) return false;
        var layer = doc.Layers.FirstOrDefault(l => l.ID == state.Preview); if (layer?.Asset is not { } asset) return false;
        using var pixels = asset.Image.Copy(); var destination = layer.Transform;
        doc.Adopt(_placementBefore);
        try { return ContentMoveEdits.Apply(doc, state.Target, pixels, state.Origin, destination, state.Extend, state.All, state.Structure, state.Color); }
        catch (Exception error) { doc.Adopt(_placementBefore); Say(error.Message); return false; }
    }
}
