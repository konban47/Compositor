using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Core.Document;

public enum SampleScope { CurrentLayer, CurrentAndBelow, AllLayers, AllWithoutAdjustments, CurrentAndBelowWithoutAdjustments }
public static class ColorSampling
{
    public static readonly int[] Sizes = [1, 3, 5, 11, 31, 51, 101];
    /// <summary>Sample only the bounded footprint, with the compositor's full effect halo.</summary>
    public static SKColor Read(CanvasDocument document, Guid? selected, SKPoint point, int size = 1, SampleScope scope = SampleScope.AllLayers)
    {
        if (!Sizes.Contains(size) || !Enum.IsDefined(scope)) throw new ArgumentOutOfRangeException(nameof(size));
        var x = (int)Math.Floor(point.X); var y = (int)Math.Floor(point.Y);
        if (x < 0 || y < 0 || x >= document.Width || y >= document.Height) return SKColors.Transparent;
        using var view = View(document, selected, scope);
        var radius = size / 2;
        var region = SKRectI.Intersect(SKRectI.Create(x - radius, y - radius, size, size), SKRectI.Create(document.Width, document.Height));
        using var pixels = DocumentRenderer.RenderRegion(view, region);
        long r = 0, g = 0, b = 0, a = 0;
        for (var row = 0; row < pixels.Height; row++) for (var column = 0; column < pixels.Width; column++)
        { var color = pixels.GetPixel(column, row); r += color.Red; g += color.Green; b += color.Blue; a += color.Alpha; }
        var count = pixels.Width * pixels.Height;
        return new SKColor((byte)((r + count / 2) / count), (byte)((g + count / 2) / count), (byte)((b + count / 2) / count), (byte)((a + count / 2) / count));
    }
    public static CanvasDocument View(CanvasDocument document, Guid? selected, SampleScope scope)
    {
        var view = document.Clone();
        var current = view.Layers.FindIndex(l => l.ID == selected);
        if (scope != SampleScope.AllLayers && scope != SampleScope.AllWithoutAdjustments && current < 0)
        {
            foreach (var layer in view.Layers) layer.IsVisible = false;
            view.Image = view.Image with { Background = null }; return view;
        }
        if (scope == SampleScope.CurrentLayer)
        {
            var chosen = view.Layers[current]; var members = view.Descendants(chosen.ID); members.Add(chosen.ID);
            var ancestors = new HashSet<Guid>(); var parent = chosen.ParentID;
            while (parent is { } id && view.Layers.FirstOrDefault(l => l.ID == id) is { } group) { ancestors.Add(id); parent = group.ParentID; }
            foreach (var l in view.Layers) l.IsVisible = ancestors.Contains(l.ID) || members.Contains(l.ID) && (l.ID == chosen.ID || l.IsVisible);
            view.Image = view.Image with { Background = null };
        }
        else if (scope is SampleScope.CurrentAndBelow or SampleScope.CurrentAndBelowWithoutAdjustments)
        {
            var ancestors = new HashSet<Guid>(); var parent = view.Layers[current].ParentID;
            while (parent is { } id && view.Layers.FirstOrDefault(l => l.ID == id) is { } group) { ancestors.Add(id); parent = group.ParentID; }
            var selectedGroup = view.Layers[current].IsGroup ? view.Descendants(view.Layers[current].ID) : [];
            for (var i = current + 1; i < view.Layers.Count; i++) if (!selectedGroup.Contains(view.Layers[i].ID) && !ancestors.Contains(view.Layers[i].ID)) view.Layers[i].IsVisible = false;
        }
        if (scope is SampleScope.AllWithoutAdjustments or SampleScope.CurrentAndBelowWithoutAdjustments)
            foreach (var layer in view.Layers.Where(l => l.Adjustment is not null)) layer.IsVisible = false;
        return view;
    }
}
