using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Document;

/// <summary>Quarter turns of the canvas change placements, never source pixels (macOS 1.4.6).</summary>
public static class CanvasRotation
{
    public static bool Rotate(CanvasDocument document, bool clockwise)
    {
        var width = document.Width; var height = document.Height;
        LayerTransform Turn(LayerTransform transform)
        {
            var x = clockwise ? height - transform.CenterY : transform.CenterY;
            var y = clockwise ? transform.CenterX : width - transform.CenterX;
            return transform with { X = x - transform.Width / 2, Y = y - transform.Height / 2,
                Rotation = (transform.Rotation + (clockwise ? 90 : -90)) % 360 };
        }
        if (document.Layers.Any(layer => !Turn(layer.Transform).IsValid
            || layer.Mask?.Placement is { } mask && !Turn(mask).IsValid)) return false;
        foreach (var layer in document.Layers)
        {
            layer.Transform = Turn(layer.Transform);
            // Masks are shared with undo snapshots: replace the wrapper before changing placement.
            if (layer.Mask is { Placement: { } placement } mask)
                layer.Mask = new Model.LayerMask(mask.Asset, mask.IsEnabled, Turn(placement), mask.IsLinked);
        }
        for (var i = 0; i < document.Guides.Count; i++)
        {
            var guide = document.Guides[i];
            var vertical = guide.Axis == GuideAxis.Vertical;
            document.Guides[i] = new CanvasGuide { ID = guide.ID,
                Axis = vertical ? GuideAxis.Horizontal : GuideAxis.Vertical,
                Position = vertical ? clockwise ? guide.Position : width - guide.Position
                    : clockwise ? height - guide.Position : guide.Position };
        }
        if (document.Selection.Path is { } path)
        {
            using var builder = new SKPathBuilder(); builder.AddPath(path, SKPathAddMode.Append);
            var turned = builder.Detach();
            var matrix = clockwise
                ? new SKMatrix(0, -1, height, 1, 0, 0, 0, 0, 1)
                : new SKMatrix(0, 1, 0, -1, 0, width, 0, 0, 1);
            turned.Transform(matrix);
            document.Selection = document.Selection.WithPath(turned);
        }
        ChannelEdits.Transform(document, height, width, clockwise
            ? new SKMatrix(0, -1, height, 1, 0, 0, 0, 0, 1) : new SKMatrix(0, 1, 0, -1, 0, width, 0, 0, 1));
        document.Width = height; document.Height = width;
        return true;
    }
}
