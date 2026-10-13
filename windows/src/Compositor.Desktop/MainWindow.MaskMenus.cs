using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private void OpenMaskContext(Control anchor, ImageLayer layer)
    {
        SelectLayerRow(layer.ID); SetPaintingMask(true);
        var mask = layer.Mask!; var vector = mask.VectorPath is not null; var menu = new ContextMenu();
        void Add(string name, Action action, bool enabled = true)
        { var item = new MenuItem { Header = Localize.Text(name), Tag = name, IsEnabled = enabled }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add(vector ? mask.IsEnabled ? "Disable Vector Mask" : "Enable Vector Mask" : mask.IsEnabled ? "Disable Layer Mask" : "Enable Layer Mask", ToggleMask);
        menu.Items.Add(new Separator());
        Add(vector ? "Delete Vector Mask" : "Delete Layer Mask", () => _ = ConfirmDeleteMask());
        if (vector) Add("Rasterize Vector Mask", () => Edit("Rasterize Vector Mask", () => { layer.Mask!.VectorPath = null; layer.Mask.VectorEvenOdd = false; return true; }));
        else Add("Apply Layer Mask", () => { Edit("Apply Layer Mask", () => MaskProperties.Apply(_document!, layer.ID)); SetPaintingMask(false); }, !layer.IsGroup && layer.Asset is not null);
        menu.Items.Add(new Separator());
        foreach (var (name, mode) in new[] { ("Add Mask to Selection", SelectionPaintMode.Add), ("Subtract Mask from Selection", SelectionPaintMode.Subtract), ("Intersect Mask with Selection", SelectionPaintMode.Intersect) })
            Add(name, () => SelectThumbnail(layer.ID, true, mode));
        menu.Items.Add(new Separator()); Add("Select and Mask…", () => _ = RefineMask());
        Add("Mask Options…", () => _ = MaskDisplayOptions());
        anchor.ContextMenu = menu; menu.Open(anchor);
    }

    private void OpenThumbnailContext(Control anchor, ImageLayer layer)
    {
        SelectLayerRow(layer.ID); SetPaintingMask(false); var menu = new ContextMenu();
        void Add(string name, Action action, bool enabled = true, bool? check = null)
        { var item = new MenuItem { Header = Localize.Text(name), Tag = name, IsEnabled = enabled, ToggleType = check is null ? MenuItemToggleType.None : MenuItemToggleType.Radio, IsChecked = check == true }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add("Blending Options…", () => _ = EditLayerStyle());
        Add("Edit Adjustment…", () => _ = EditAdjustment(), layer.Adjustment is not null);
        menu.Items.Add(new Separator()); Add("Select Pixels", () => SelectThumbnail(layer.ID, false, SelectionPaintMode.New), !layer.IsGroup);
        foreach (var (name, mode) in new[] { ("Add Transparency to Selection", SelectionPaintMode.Add), ("Subtract Transparency from Selection", SelectionPaintMode.Subtract), ("Intersect Transparency with Selection", SelectionPaintMode.Intersect) })
            Add(name, () => SelectThumbnail(layer.ID, false, mode), !layer.IsGroup);
        menu.Items.Add(new Separator());
        foreach (var (name, size) in new[] { ("No Thumbnails", 0), ("Small Thumbnails", 32), ("Medium Thumbnails", 48), ("Large Thumbnails", 64) })
            Add(name, () => { EditorPreferences.Current.LayerThumbnailSize = size; SavePreferenceChanges(); ShowLayers(_document!); }, true, EditorPreferences.Current.LayerThumbnailSize == size);
        menu.Items.Add(new Separator());
        foreach (var (name, crop) in new[] { ("Clip Thumbnails to Layer Bounds", true), ("Clip Thumbnails to Document Bounds", false) })
            Add(name, () => { EditorPreferences.Current.ThumbnailLayerBounds = crop; SavePreferenceChanges(); ShowLayers(_document!); }, true, EditorPreferences.Current.ThumbnailLayerBounds == crop);
        menu.Items.Add(new Separator()); var colors = new MenuItem { Header = Localize.Text("Color") };
        foreach (var value in Enum.GetValues<LayerLabel>())
        {
            var item = new MenuItem { Header = Localize.Text(value.ToString()), ToggleType = MenuItemToggleType.Radio, IsChecked = layer.Label == value };
            item.Click += (_, _) => { Edit("Layer Color", () => { layer.Label = value; return true; }); ShowLayers(_document!); }; colors.Items.Add(item);
        }
        menu.Items.Add(colors); anchor.ContextMenu = menu; menu.Open(anchor);
    }

    private void SelectThumbnail(Guid id, bool mask, SelectionPaintMode mode)
    {
        if (_document is not { } doc) return;
        Edit("Thumbnail to Selection", () =>
        {
            var previous = doc.Selection;
            if (!(mask ? MaskProperties.LoadSelection(doc, id) : SelectionEdits.SelectLayerPixels(doc, id))) return false;
            using var coverage = doc.Selection.Coverage(SKRectI.Create(doc.Width, doc.Height));
            doc.Selection = previous;
            return coverage is not null && SelectionBrushEdits.Combine(doc, coverage, 1, mode);
        });
    }

    private async Task MaskDisplayOptions()
    {
        var form = new FormDialog("Layer Mask Display Options", 450, 330);
        var color = form.Text("Color (#RRGGBB)", EditorPreferences.Current.MaskOverlayColor);
        color.Background = new SolidColorBrush(Color.Parse(EditorPreferences.Current.MaskOverlayColor));
        var opacity = form.Number("Opacity (%)", EditorPreferences.Current.MaskOverlayOpacity, 0, 100);
        var show = form.Check("Show Mask Overlay", true);
        form.Validate = () => EditorPreferences.ValidColor(color.Text) ? null : "Enter a color such as #FF0000.";
        if (!await form.ShowDialog<bool>(this)) return;
        EditorPreferences.Current.MaskOverlayColor = color.Text!; EditorPreferences.Current.MaskOverlayOpacity = (double)opacity.Value!;
        _canvas.ShowsMaskOverlay = show.IsChecked == true; SavePreferenceChanges(); UpdateMaskOverlay();
    }

    private string _maskOverlayKey = "";
    private void UpdateMaskOverlay()
    {
        var current = PropertyLayer;
        var key = $"{_canvas.ShowsMaskOverlay}:{_document?.ID}:{current?.ID}:{current?.Mask?.Asset.GetHashCode()}:{current?.MaskTransform}:{current?.Mask?.Density}:{current?.Mask?.Feather}:{current?.Mask?.VectorPath?.GetHashCode()}:{EditorPreferences.Current.MaskOverlayColor}:{EditorPreferences.Current.MaskOverlayOpacity}";
        if (key == _maskOverlayKey) return; _maskOverlayKey = key;
        _canvas.MaskOverlay?.Dispose(); _canvas.MaskOverlay = null;
        if (_canvas.ShowsMaskOverlay && _document is { } doc && PropertyLayer is { Mask: { } mask } layer)
        {
            using var coverage = MaskProperties.Coverage(mask, layer.MaskTransform, new Compositor.Core.Model.LayerTransform(0, 0, doc.Width, doc.Height), doc.Width, doc.Height);
            using var pixels = new SKBitmap(Bitmaps.ColorInfo(doc.Width, doc.Height));
            var color = SKColor.Parse(EditorPreferences.Current.MaskOverlayColor); var rgba = pixels.GetPixelSpan(); var gray = coverage.GetPixelSpan();
            for (var y = 0; y < doc.Height; y++) for (var x = 0; x < doc.Width; x++)
            { var at = y * pixels.RowBytes + x * 4; rgba[at] = color.Red; rgba[at + 1] = color.Green; rgba[at + 2] = color.Blue; rgba[at + 3] = (byte)((255 - gray[y * coverage.RowBytes + x]) * EditorPreferences.Current.MaskOverlayOpacity / 100); }
            _canvas.MaskOverlay = CanvasView.ToImage(pixels);
        }
        _canvas.InvalidateOverlay();
    }
}
