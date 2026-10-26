using Avalonia.Controls;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private void SetTypeShape(string shape)
    {
        CommitText(); ChangeType(s =>
        {
            s.Geometry = new(Enum.Parse<TextGeometryShape>(shape));
            if (shape == "Block") s.BoxSize ??= new(PropertyLayer?.Asset?.Width ?? 300, PropertyLayer?.Asset?.Height ?? 150);
            else s.BoxSize = null;
        }, true);
    }
    private async Task WarpType()
    {
        if (_document is not { } doc || PropertyLayer is not { LiveText: { } layerStyle } layer) return;
        var session = _text; var source = session?.Style ?? layerStyle;
        var revision = _history.CurrentRevision; var options = source.Geometry ?? new(TextGeometryShape.Arch);
        var form = new FormDialog("Warp Text", 470, 470);
        var shape = form.Choice("Style", Enum.GetNames<TextGeometryShape>(), (int)options.Shape);
        var bend = form.Number("Bend (%)", options.Bend, -100, 100);
        var horizontal = form.Number("Horizontal Distortion (%)", options.HorizontalDistortion, -100, 100);
        var vertical = form.Number("Vertical Distortion (%)", options.VerticalDistortion, -100, 100);
        LayerTextStyle Read()
        { var value = TextEdits.Copy(source); value.Geometry = new((TextGeometryShape)shape.SelectedIndex, (double)bend.Value!, (double)horizontal.Value!, (double)vertical.Value!); value.BoxSize = null; return value; }
        using var preview = FilterPreview.Begin(doc, layer.ID);
        form.Preview(); form.Changed = () =>
        {
            _canvas.PreviewDocument = null;
            if (form.PreviewEnabled) { preview?.Show((target, id) => TextEdits.SetStyle(target, id, Read())); _canvas.PreviewDocument = preview?.Document; }
            _canvas.InvalidateVisual();
        };
        try { if (await form.ShowDialog<bool>(this) && ReferenceEquals(doc, _document) && session == _text && revision == _history.CurrentRevision) { var next = Read(); ChangeType(s => { s.Geometry = next.Geometry; s.BoxSize = next.BoxSize; }, true); } }
        finally { _canvas.PreviewDocument = null; _canvas.InvalidateVisual(); }
    }
    private async Task MatchFont()
    {
        if (_document is not { } doc) return;
        var bounds = doc.Selection.Path?.Bounds ?? (PropertyLayer is { } selectedLayer ? SKRect.Create((float)selectedLayer.Transform.X, (float)selectedLayer.Transform.Y, (float)selectedLayer.Transform.Width, (float)selectedLayer.Transform.Height) : SKRect.Create(doc.Width, doc.Height));
        var area = SKRectI.Intersect(new SKRectI((int)Math.Floor(bounds.Left), (int)Math.Floor(bounds.Top), (int)Math.Ceiling(bounds.Right), (int)Math.Ceiling(bounds.Bottom)), SKRectI.Create(doc.Width, doc.Height));
        if (area.IsEmpty) return;
        using var sampleImage = DocumentRenderer.RenderRegion(doc, area);
        var form = new FormDialog("Match Font", 620, 520);
        form.Description("Enter the text visible in the selected region. Installed fonts are compared locally by glyph shape and proportions.");
        var input = form.Text("Sample Text", CurrentType?.Content is { Length: > 0 } text ? text[..Math.Min(24, text.Length)] : "Aa");
        var candidates = new ListBox { Height = 210, Tag = "matched-fonts" }; form.Fields.Children.Add(candidates);
        var busy = false;
        async Task Search()
        {
            if (busy || string.IsNullOrWhiteSpace(input.Text)) return; busy = true; var content = input.Text[..Math.Min(24, input.Text.Length)];
            try { var copy = sampleImage.Copy(); candidates.ItemsSource = await Task.Run(() => { using (copy) return FontMatching.Find(copy, content); }); candidates.SelectedIndex = 0; }
            finally { busy = false; }
        }
        form.Fields.Children.Add(FormDialog.ActionButton("Find Similar Fonts", () => _ = Search()));
        form.Validate = () => candidates.SelectedItem is string ? null : "Find and select a font first.";
        if (!await form.ShowDialog<bool>(this) || !ReferenceEquals(doc, _document) || candidates.SelectedItem is not string selected) return;
        if (CurrentType is not null) ChangeType(s => { s.FontName = selected; s.FontRuns = null; });
        else Edit("Matched Type Layer", () => TextEdits.Add(doc, new() { Content = input.Text ?? "Aa", FontName = selected, FontSize = 36 }, new(area.Left, area.Top)) is not null);
    }
}
