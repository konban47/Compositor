using Avalonia;
using Avalonia.Media;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private Compositor.Core.Model.LayerTransform? _moveHoverBox;
    private void MoveHover(SKPoint at)
    {
        _moveHoverBox = null;
        if (_tool != Tool.Move || !EditorPreferences.Current.MoveHoverBounds || _document is not { } doc) return;
        foreach (var entry in doc.HierarchyEntries(topFirst: true))
        {
            var layer = doc.Layers.Single(l => l.ID == entry.Layer.ID);
            if (!entry.Visible || layer.IsGroup || layer.Opacity <= 0 || layer.Asset is not { } asset || layer.Transform.InBox(at) is not { } point) continue;
            var x = Math.Clamp((int)(point.X / layer.Transform.Width * asset.Width), 0, asset.Width - 1);
            var y = Math.Clamp((int)(point.Y / layer.Transform.Height * asset.Height), 0, asset.Height - 1);
            if (asset.Image.GetPixel(x,y).Alpha == 0) continue;
            if (EditorPreferences.Current.MoveSelectGroup)
                while (layer.ParentID is { } parent && doc.Layers.FirstOrDefault(l => l.ID == parent) is { } group) layer = group;
            _moveHoverBox = layer.IsGroup ? TransformEdits.GroupBox(doc, [layer.ID]) : layer.Transform; break;
        }
        _canvas.InvalidateOverlay();
    }
    private void DrawMoveHover(DrawingContext context, Func<SKPoint, Point> screen)
    {
        if (_moveHoverBox is not { } box || _tool != Tool.Move) return;
        var corners = TransformEdits.Corners(box).Select(screen).ToArray(); var pen = new Pen(Skin.AccentBrush, 1);
        for (var i = 0; i < 4; i++) context.DrawLine(pen, corners[i], corners[(i+1)%4]);
    }
    private void NestMovedLayers()
    {
        if (_tool != Tool.Move || !EditorPreferences.Current.ArtboardAutoNest || _document is not { } doc) return;
        var boards = doc.Layers.Where(l => l.Container == LayerContainer.Artboard).Reverse().ToArray(); if (boards.Length == 0) return;
        foreach (var id in SelectedLayers)
        {
            if (doc.Layers.FirstOrDefault(l => l.ID == id) is not { } layer || layer.Container == LayerContainer.Artboard) continue;
            if (layer.ParentID is { } parent && doc.Layers.FirstOrDefault(l => l.ID == parent)?.Container != LayerContainer.Artboard) continue;
            var descendants = doc.Descendants(layer.ID);
            var bounds = layer.IsGroup ? TransformEdits.GroupBox(doc, [layer.ID]) ?? layer.Transform : layer.Transform;
            layer.ParentID = boards.FirstOrDefault(b => !descendants.Contains(b.ID) && LayerProtection.CanMove(doc, b.ID) && b.Transform.Contains(bounds.Point(.5,.5)))?.ID;
        }
    }
    private bool ReorderWithArtboardPosition(CanvasDocument doc, Guid source, Guid target, bool above)
    {
        var layer = doc.Layers.FirstOrDefault(l => l.ID == source); if (layer is null) return false;
        var oldBoard = ArtboardOf(layer); var old = doc.Layers.FirstOrDefault(l => l.ID == oldBoard)?.Transform;
        if (!LayerEdits.MoveRelative(doc, source, target, above)) return false;
        var newBoard = ArtboardOf(layer); var next = doc.Layers.FirstOrDefault(l => l.ID == newBoard)?.Transform;
        if (EditorPreferences.Current.ArtboardKeepPosition && oldBoard != newBoard && next is { } destination)
        {
            var dx = destination.X - (old?.X ?? 0); var dy = destination.Y - (old?.Y ?? 0); var members = doc.Descendants(source);
            foreach (var member in doc.Layers.Where(l => l.ID == source || members.Contains(l.ID)))
                TransformEdits.SetPlacement(member, member.Transform with { X = member.Transform.X + dx, Y = member.Transform.Y + dy });
        }
        return true;
    }
}
