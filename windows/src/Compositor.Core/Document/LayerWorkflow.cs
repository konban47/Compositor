using System.Globalization;
using System.Security;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Core.Document;

public static class LayerWorkflow
{
    public static HashSet<Guid> Members(CanvasDocument document, IEnumerable<Guid> ids)
    {
        var set = ids.ToHashSet(); foreach (var id in set.ToArray()) set.UnionWith(document.Descendants(id)); return set;
    }
    public static bool Editable(CanvasDocument document, IEnumerable<Guid> ids) => Members(document, ids)
        .All(id => LayerProtection.Effective(document, id) == LayerLocks.None);
    public static CanvasDocument Subset(CanvasDocument document, IEnumerable<Guid> ids)
    {
        var keep = Members(document, ids); var clone = document.Clone(); clone.Layers.RemoveAll(l => !keep.Contains(l.ID));
        foreach (var layer in clone.Layers)
        {
            if (layer.ParentID is { } parent && !keep.Contains(parent)) layer.ParentID = null;
            if (layer.MaskSourceID is { } source && !keep.Contains(source)) layer.MaskSourceID = null;
        }
        return clone;
    }
    public static SKRect Bounds(CanvasDocument document, IEnumerable<Guid> ids)
    {
        var set = Members(document, ids); var members = document.Layers.Where(l => set.Contains(l.ID) && (!l.IsGroup || l.Container != LayerContainer.Group)).ToArray();
        if (members.Length == 0) return SKRect.Create(document.Width, document.Height);
        var points = members.SelectMany(l => new[] { l.Transform.Point(0, 0), l.Transform.Point(1, 0), l.Transform.Point(0, 1), l.Transform.Point(1, 1) }).ToArray();
        return new SKRect(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
    }
    public static Guid? MergeVisible(CanvasDocument document, bool flatten)
    {
        var visible = document.EffectiveVisibleIDs();
        var removed = flatten ? document.Layers.Select(l => l.ID).ToHashSet() : visible;
        if (removed.Count == 0 || !Editable(document, removed)) return null;
        using var render = DocumentRenderer.Render(document);
        if (flatten)
        {
            using var canvas = new SKCanvas(render); using var paint = new SKPaint { BlendMode = SKBlendMode.DstOver, Color = SKColors.White };
            canvas.DrawRect(SKRect.Create(render.Width, render.Height), paint);
        }
        var (pixels, transform) = LayerMerge.Trimmed(render, new Model.LayerTransform(0, 0, document.Width, document.Height));
        var made = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "Merged"), transform, flatten ? "Background" : "Merged Visible");
        // Hidden descendants survive a visible-group merge, promoted to the nearest surviving ancestor.
        var old = document.Layers.ToDictionary(l => l.ID); var insertion = document.Layers.FindLastIndex(l => removed.Contains(l.ID));
        insertion = document.Layers.Take(insertion + 1).Count(l => !removed.Contains(l.ID));
        document.Layers.RemoveAll(l => removed.Contains(l.ID));
        foreach (var layer in document.Layers)
        {
            while (layer.ParentID is { } parent && removed.Contains(parent)) layer.ParentID = old[parent].ParentID;
            if (layer.MaskSourceID is { } clip && removed.Contains(clip)) layer.MaskSourceID = null;
        }
        document.Layers.Insert(Math.Clamp(insertion, 0, document.Layers.Count), made); return made.ID;
    }
    public static Guid? MakeContainer(CanvasDocument document, IReadOnlyCollection<Guid> ids, LayerContainer kind,
        string name, SKRect bounds, bool ellipse = false)
    {
        if (!Editable(document, ids) || bounds.Width <= 0 || bounds.Height <= 0) return null;
        Guid? made;
        if (ids.Count > 0) made = LayerPlacement.GroupSelected(document, ids);
        else
        {
            var folder = new ImageLayer(Guid.NewGuid(), null, new Model.LayerTransform(bounds.Left, bounds.Top, bounds.Width, bounds.Height), name) { IsGroup = true };
            document.Layers.Add(folder); made = folder.ID;
        }
        if (made is null) return null;
        var group = document.Layers.Single(l => l.ID == made); group.Container = kind; group.Name = name;
        group.Transform = new Model.LayerTransform(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        if (kind == LayerContainer.Artboard) group.ParentID = null;
        if (kind == LayerContainer.Frame)
        {
            using var builder = new SKPathBuilder();
            if (ellipse) builder.AddOval(SKRect.Create(bounds.Width, bounds.Height)); else builder.AddRect(SKRect.Create(bounds.Width, bounds.Height));
            using var path = builder.Detach();
            var mask = Bitmaps.Allocate(Bitmaps.MaskInfo(Math.Max(1, (int)Math.Ceiling(bounds.Width)), Math.Max(1, (int)Math.Ceiling(bounds.Height))));
            using (var canvas = new SKCanvas(mask)) { canvas.Clear(SKColors.Black); using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true }; canvas.DrawPath(path, paint); }
            group.Mask = Model.LayerMask.AssetFrom(mask); group.Mask.VectorPath = path.ToSvgPathData();
        }
        return made;
    }
    public static Guid? ConvertToSmartObject(CanvasDocument document, IReadOnlyCollection<Guid> ids)
    {
        if (ids.Count == 0 || !Editable(document, ids)) return null;
        var keep = Members(document, ids); using var source = Subset(document, ids);
        var bounds = Bounds(document, ids);
        var margin = source.Layers.Where(l => l.Effects is { Enabled: true }).Sum(l =>
        {
            var fx = l.Effects!.EditableCopy().Items!.Where(e => e.Enabled).Select(e => e.Kind switch
            {
                StyleEffectKind.DropShadow => e.Distance + e.Size * 3,
                StyleEffectKind.OuterGlow => e.Size * 3,
                StyleEffectKind.Stroke => e.Size,
                StyleEffectKind.BevelEmboss => e.Size + e.Soften * 3,
                _ => 0,
            }).DefaultIfEmpty(0).Max();
            return fx > 0 ? Math.Ceiling(fx * 1.42) + 2 : 0;
        });
        bounds.Inflate((float)margin, (float)margin);
        var x = (int)Math.Floor(bounds.Left); var y = (int)Math.Floor(bounds.Top);
        source.Width = Math.Max(1, (int)Math.Ceiling(bounds.Right) - x); source.Height = Math.Max(1, (int)Math.Ceiling(bounds.Bottom) - y);
        if ((long)source.Width * source.Height > DocumentLimits.MaxSurfacePixels) throw new ProjectException(ProjectError.TooLarge);
        foreach (var layer in source.Layers)
        {
            layer.Transform = layer.Transform with { X = layer.Transform.X - x, Y = layer.Transform.Y - y };
            if (layer.Mask?.Placement is { } placement) layer.Mask.Placement = placement with { X = placement.X - x, Y = placement.Y - y };
        }
        source.Guides.Clear(); source.Channels.Clear(); source.Selection = DocumentSelection.All;
        var smart = SmartObjectData.FromDocument(source); var render = DocumentRenderer.Render(source);
        var top = document.Layers.Last(l => ids.Contains(l.ID)); var parent = top.ParentID;
        while (parent is { } id && keep.Contains(id)) parent = document.Layers.Single(l => l.ID == id).ParentID;
        var made = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(render, top.Name), new Model.LayerTransform(x, y, source.Width, source.Height), top.Name)
            { ParentID = parent, SmartObject = smart };
        var slot = document.Layers.Take(document.Layers.IndexOf(top) + 1).Count(l => !keep.Contains(l.ID));
        document.Layers.RemoveAll(l => keep.Contains(l.ID)); document.Layers.Insert(Math.Clamp(slot, 0, document.Layers.Count), made);
        foreach (var layer in document.Layers) if (layer.MaskSourceID is { } clip && keep.Contains(clip)) layer.MaskSourceID = null;
        return made.ID;
    }
    public static bool UpdateSmartObject(CanvasDocument document, Guid smartID, CanvasDocument source)
    {
        var layers = document.Layers.Where(l => l.SmartObject?.ID == smartID).ToArray();
        if (layers.Length == 0 || !Editable(document, layers.Select(l => l.ID))) return false;
        var smart = SmartObjectData.FromDocument(source, smartID); using var rendered = DocumentRenderer.Render(source);
        foreach (var layer in layers) { layer.SmartObject = smart; layer.Asset = ImportedImage.Create(rendered.Copy(), layer.Name); }
        return true;
    }
    public static bool Rasterize(CanvasDocument document, IEnumerable<Guid> ids)
    {
        var changed = false;
        foreach (var layer in document.Layers.Where(l => ids.Contains(l.ID) && (LayerProtection.Effective(document, l.ID) & (LayerLocks.All | LayerLocks.Pixels)) == 0))
        { changed |= layer.SmartObject is not null || layer.LiveShape is not null || layer.LiveText is not null; layer.SmartObject = null; layer.Shape = null; layer.Text = null; }
        return changed;
    }
    public static int Clean(CanvasDocument document, string kind)
    {
        var removed = new HashSet<Guid>();
        foreach (var layer in document.Layers)
        {
            if (!Editable(document, [layer.ID])) continue;
            var empty = layer.Asset is { } asset && layer.Effects is null && layer.Adjustment is null && layer.SmartObject is null
                && !HasPixels(asset.Image);
            if (kind == "Hidden" ? !layer.IsVisible : kind == "Groups" ? layer.IsGroup && layer.Container == LayerContainer.Group && document.Descendants(layer.ID).Count == 0
                : !layer.IsGroup && empty) removed.UnionWith(Members(document, [layer.ID]));
        }
        document.Layers.RemoveAll(l => removed.Contains(l.ID));
        foreach (var layer in document.Layers) if (layer.MaskSourceID is { } clip && removed.Contains(clip)) layer.MaskSourceID = null;
        return removed.Count;
    }
    private static bool HasPixels(SKBitmap image)
    {
        var data = image.GetPixelSpan();
        for (var y = 0; y < image.Height; y++) for (var x = 0; x < image.Width; x++)
            if (data[y * image.RowBytes + x * 4 + 3] != 0) return true;
        return false;
    }
    public static string Svg(CanvasDocument document, IReadOnlyCollection<Guid> ids)
    {
        using var subset = Subset(document, ids);
        if (VectorSvg(subset) is { } vector) return vector;
        using var render = DocumentRenderer.Render(subset);
        var (pixels, transform) = LayerMerge.Trimmed(render, new Model.LayerTransform(0, 0, document.Width, document.Height));
        using (pixels)
        {
            var data = Convert.ToBase64String(PngCodec.Encode(pixels));
            return FormattableString.Invariant($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{pixels.Width}\" height=\"{pixels.Height}\" viewBox=\"0 0 {pixels.Width} {pixels.Height}\"><title>{SecurityElement.Escape(string.Join(", ", subset.Layers.Where(l => ids.Contains(l.ID)).Select(l => l.Name)))}</title><image width=\"{pixels.Width}\" height=\"{pixels.Height}\" href=\"data:image/png;base64,{data}\"/></svg>");
        }
    }
    private static string? VectorSvg(CanvasDocument subset)
    {
        var layers = subset.Layers.Where(l => l.IsVisible).ToArray();
        if (layers.Length == 0 || layers.Any(l => l.LiveShape is null || l.ParentID is not null || l.MaskSourceID is not null
            || l.Mask is not null || l.Effects is { IsEmpty: false } || l.Blending is { IsDefault: false } || l.BlendMode != LayerBlendMode.Normal)) return null;
        var xml = new System.Text.StringBuilder();
        using (var writer = System.Xml.XmlWriter.Create(xml, new System.Xml.XmlWriterSettings { OmitXmlDeclaration = true }))
        {
            const string ns = "http://www.w3.org/2000/svg";
            writer.WriteStartElement("svg", ns);
            void Attribute(string key, double value) => writer.WriteAttributeString(key, value.ToString("0.######", CultureInfo.InvariantCulture));
            Attribute("width", subset.Width); Attribute("height", subset.Height);
            writer.WriteAttributeString("viewBox", $"0 0 {subset.Width} {subset.Height}");
            foreach (var layer in layers)
            {
                var t = layer.Transform; var style = layer.LiveShape!; var width = layer.Asset!.Width; var height = layer.Asset.Height;
                writer.WriteStartElement("g", ns);
                writer.WriteAttributeString("transform", FormattableString.Invariant($"translate({t.CenterX} {t.CenterY}) rotate({t.Rotation}) scale({t.Width / width * (t.FlipX ? -1 : 1)} {t.Height / height * (t.FlipY ? -1 : 1)}) translate({-width / 2.0} {-height / 2.0})"));
                Attribute("opacity", layer.Opacity * layer.FillOpacity);
                writer.WriteElementString("title", ns, layer.Name);
                var color = $"#{(int)Math.Round(style.Red * 255):X2}{(int)Math.Round(style.Green * 255):X2}{(int)Math.Round(style.Blue * 255):X2}";
                if (style.Kind == ShapeKind.Path)
                {
                    writer.WriteStartElement("path", ns); writer.WriteAttributeString("d", style.Path);
                    writer.WriteAttributeString("transform", FormattableString.Invariant($"scale({width} {height})"));
                }
                else if (style.Kind == ShapeKind.Ellipse)
                {
                    writer.WriteStartElement("ellipse", ns); Attribute("cx", width / 2.0); Attribute("cy", height / 2.0); Attribute("rx", width / 2.0); Attribute("ry", height / 2.0);
                }
                else if (style.Kind == ShapeKind.Line)
                {
                    var thickness = Math.Max(1, style.LineWidth ?? 1);
                    writer.WriteStartElement("line", ns); Attribute("x1", style.Start is { } a ? a.X * width : Math.Min(thickness, width) / 2);
                    Attribute("y1", style.Start is { } b ? b.Y * height : Math.Min(thickness, height) / 2);
                    Attribute("x2", style.End is { } c ? c.X * width : width - Math.Min(thickness, width) / 2);
                    Attribute("y2", style.End is { } d ? d.Y * height : height - Math.Min(thickness, height) / 2);
                    writer.WriteAttributeString("stroke", color); Attribute("stroke-width", thickness); writer.WriteAttributeString("stroke-linecap", "round");
                }
                else
                {
                    writer.WriteStartElement("rect", ns); Attribute("width", width); Attribute("height", height);
                    Attribute("rx", Math.Min(Math.Max(0, style.CornerRadius), Math.Min(width, height) / 2.0));
                }
                writer.WriteAttributeString("fill", color); writer.WriteEndElement(); writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        return xml.ToString();
    }
    public static string Css(CanvasDocument document, IReadOnlyCollection<Guid> ids)
    {
        using var subset = Subset(document, ids); using var render = DocumentRenderer.Render(subset);
        var (pixels, t) = LayerMerge.Trimmed(render, new Model.LayerTransform(0, 0, document.Width, document.Height));
        using (pixels)
        {
            var data = Convert.ToBase64String(PngCodec.Encode(pixels));
            return FormattableString.Invariant($".compositor-layer {{\n  position: absolute;\n  left: {t.X:0.###}px;\n  top: {t.Y:0.###}px;\n  width: {pixels.Width}px;\n  height: {pixels.Height}px;\n  background: url(\"data:image/png;base64,{data}\") no-repeat 0 0 / 100% 100%;\n}}\n");
        }
    }
}
