using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Rendering;

public static partial class DocumentRenderer
{
    private sealed partial class Renderer
    {
        private readonly HashSet<Guid> _isolated = [];
        private readonly List<(SKBitmap Pixels, SKRectI Bounds)> _deepHoles = [];
        private bool Isolated(ImageLayer layer) => layer.Container != LayerContainer.Group || layer.Effects is not null
            || layer.Blending is { IsDefault: false } || layer.BlendMode != LayerBlendMode.Normal
            || _document.Descendants(layer.ID).Any(id => _byID[id].Blending?.Knockout != null && _byID[id].Blending!.Knockout != KnockoutKind.None);
        private double Opacity(ImageLayer layer)
        {
            var opacity = layer.Opacity; var parent = layer.ParentID;
            for (var depth = 0; parent is { } id && depth < 64; depth++)
            {
                if (_isolated.Contains(id) || !_byID.TryGetValue(id, out var group)) break;
                opacity *= group.Opacity * group.FillOpacity; parent = group.ParentID;
            }
            return opacity;
        }
        private void DrawGroup(ImageLayer layer, Target target)
        {
            var opacity = Opacity(layer); if (opacity <= 0) return;
            using var pixels = Allocate(_region.Width, _region.Height);
            var firstHole = _deepHoles.Count;
            _isolated.Add(layer.ID);
            try
            {
                using var canvas = new SKCanvas(pixels); canvas.Translate(-_region.Left, -_region.Top);
                if (layer.Container != LayerContainer.Group)
                {
                    var t = layer.Transform;
                    using var builder = new SKPathBuilder(); builder.AddPoly([t.Point(0, 0), t.Point(1, 0), t.Point(1, 1), t.Point(0, 1)], true);
                    using var path = builder.Detach();
                    canvas.ClipPath(path, antialias: true);
                    if (layer.Container == LayerContainer.Artboard) { using var paint = new SKPaint { Color = SKColors.White }; canvas.DrawPath(path, paint); }
                }
                DrawSiblings(ChildrenOf(layer), new Target(pixels, canvas, _region.Location));
            }
            finally { _isolated.Remove(layer.ID); }
            if (layer.Container != LayerContainer.Group)
            {
                var data = pixels.GetPixelSpan();
                for (var y = 0; y < pixels.Height; y++) for (var x = 0; x < pixels.Width; x++)
                    if (layer.Transform.InBox(new SKPoint(_region.Left + x + 0.5f, _region.Top + y + 0.5f)) is null)
                        data.Slice(y * pixels.RowBytes + x * 4, 4).Clear();
            }
            // A deep knockout propagates through every enclosing isolated surface; a shallow one stops here.
            for (var i = firstHole; i < _deepHoles.Count; i++) Erase(target, _deepHoles[i].Pixels, _deepHoles[i].Bounds);
            using var asset = ImportedImage.Create(pixels.Copy(), layer.Name);
            var rendered = layer.Clone(); rendered.IsGroup = false; rendered.Asset = asset;
            rendered.Transform = new Model.LayerTransform(_region.Left, _region.Top, _region.Width, _region.Height);
            if (rendered.Mask is not null) rendered.Mask.Placement = layer.MaskTransform;
            using var content = Content(rendered, out var bounds);
            if (content is not null) CompositeLayer(target, content, bounds, rendered, opacity);
        }
        private void DrawIndependentStack(List<ImageLayer> siblings, int start, int end, Target target)
        {
            var layer = siblings[start]; using var coverage = Content(layer, out var bounds);
            if (coverage is null) return;
            if (layer.IsVisible) DrawLayer(layer, target);
            for (var i = start + 1; i < end; i++)
            {
                var child = siblings[i]; if (!child.IsVisible) continue;
                if (child.Adjustment is not null) { DrawAdjustment(child, target); continue; }
                using var content = Content(child, out var childBounds); if (content is null) continue;
                using var clipped = Restrict(content, childBounds, coverage, bounds, false);
                CompositeLayer(target, clipped, childBounds, child, Opacity(child));
            }
        }
        private static bool IsInterior(StyleEffect e) => e.Kind is StyleEffectKind.InnerShadow or StyleEffectKind.InnerGlow
            or StyleEffectKind.Satin or StyleEffectKind.ColorOverlay or StyleEffectKind.GradientOverlay or StyleEffectKind.PatternOverlay;
        private static bool SplitInterior(ImageLayer layer) => layer.BlendMode != LayerBlendMode.Normal
            && layer.Blending?.BlendInteriorEffectsAsGroup != true && layer.Effects is { Enabled: true, Items: { } items } && items.Any(e => e.Enabled && IsInterior(e));
        private void CompositeLayer(Target target, SKBitmap content, SKRectI bounds, ImageLayer layer, double opacity)
        {
            CompositeAppearance(target, content, bounds, layer, opacity);
            if (!SplitInterior(layer)) return;
            foreach (var effect in layer.Effects!.Items!.Where(e => e.Enabled && IsInterior(e)))
            {
                var overlay = layer.Clone(); overlay.FillOpacity = 0; overlay.BlendMode = effect.BlendMode;
                overlay.Blending = (layer.Blending ?? new()).Copy(); overlay.Blending.BlendInteriorEffectsAsGroup = true; overlay.Blending.Knockout = KnockoutKind.None;
                var appearance = effect.Copy(); appearance.BlendMode = LayerBlendMode.Normal;
                overlay.Effects = new LayerEffects { Items = [appearance], GlobalLightAngle = layer.Effects.GlobalLightAngle };
                using var painted = Content(overlay, out var area);
                if (painted is not null) CompositeAppearance(target, painted, area, overlay, opacity);
            }
        }
        private void CompositeAppearance(Target target, SKBitmap content, SKRectI bounds, ImageLayer layer, double opacity)
        {
            var blending = layer.Blending;
            if (blending?.Knockout is { } knockout && knockout != KnockoutKind.None && layer.FillOpacity < 1)
            {
                var raw = layer.Clone(); raw.Effects = null; raw.FillOpacity = 1;
                using var rectangle = !blending.TransparencyShapesLayer ? Bitmaps.Allocate(Bitmaps.ColorInfo(1, 1)) : null;
                rectangle?.Erase(SKColors.White);
                using var rectangleAsset = rectangle is not null ? ImportedImage.Create(rectangle.Copy(), layer.Name) : null;
                if (rectangleAsset is not null) raw.Asset = rectangleAsset;
                using var shape = Content(raw, out var shapeBounds);
                if (shape is not null)
                {
                    ScaleChannels(shape, opacity * (1 - layer.FillOpacity)); Erase(target, shape, shapeBounds);
                    if (knockout == KnockoutKind.Deep && _isolated.Count > 0) _deepHoles.Add((shape.Copy(), shapeBounds));
                }
            }
            if (blending is null || blending.IsDefault)
            { Composite(target, content, bounds, layer.BlendMode, opacity); return; }
            using var under = Copy(target.Bitmap);
            if (blending.Ranges.Any(r => !r.Source.IsDefault || !r.Underlying.IsDefault))
            {
                var source = content.GetPixelSpan(); var background = target.Bitmap.GetPixelSpan();
                for (var y = 0; y < content.Height; y++)
                for (var x = 0; x < content.Width; x++)
                {
                    var at = y * content.RowBytes + x * 4; if (source[at + 3] == 0) continue;
                    var tx = bounds.Left + x - target.Origin.X; var ty = bounds.Top + y - target.Origin.Y;
                    if (tx < 0 || ty < 0 || tx >= target.Bitmap.Width || ty >= target.Bitmap.Height) continue;
                    var into = ty * target.Bitmap.RowBytes + tx * 4; var coverage = 1.0;
                    foreach (var range in blending.Ranges)
                        coverage *= range.Source.Coverage(Tone(source.Slice(at, 4), range.Channel))
                            * range.Underlying.Coverage(Tone(background.Slice(into, 4), range.Channel));
                    for (var c = 0; c < 4; c++) source[at + c] = (byte)Math.Round(source[at + c] * coverage);
                }
            }
            Composite(target, content, bounds, layer.BlendMode, opacity);
            if (blending.Red && blending.Green && blending.Blue) return;
            var previous = under.GetPixelSpan(); var result = target.Bitmap.GetPixelSpan();
            for (var y = 0; y < target.Bitmap.Height; y++)
            for (var x = 0; x < target.Bitmap.Width; x++)
            {
                var at = y * target.Bitmap.RowBytes + x * 4;
                for (var c = 0; c < 3; c++)
                    if (!(c == 0 ? blending.Red : c == 1 ? blending.Green : blending.Blue))
                        result[at + c] = previous[at + 3] == 0 ? (byte)0 : (byte)Math.Clamp(Math.Round(previous[at + c] * result[at + 3] / (double)previous[at + 3]), 0, result[at + 3]);
            }
        }
        private static double Tone(ReadOnlySpan<byte> pixel, BlendIfChannel channel)
        {
            if (pixel[3] == 0) return 0;
            var value = channel switch { BlendIfChannel.Red => pixel[0], BlendIfChannel.Green => pixel[1], BlendIfChannel.Blue => pixel[2], _ => .299 * pixel[0] + .587 * pixel[1] + .114 * pixel[2] };
            return value * 255 / pixel[3];
        }
        private static void Erase(Target target, SKBitmap mask, SKRectI bounds)
        {
            using var paint = new SKPaint { BlendMode = SKBlendMode.DstOut };
            target.Canvas.DrawBitmap(mask, bounds, OneToOne, paint);
        }
    }
}
