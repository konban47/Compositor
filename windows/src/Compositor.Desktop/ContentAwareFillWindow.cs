using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Compositor.Core.Document;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;

namespace Compositor.Desktop;

/// <summary>What the workspace asks the window to do: fill this area from that sampling area, into this output.</summary>
internal sealed record ContentFillRequest(byte[] Fill, ContentFillOptions Options, bool AllLayers, int Output);

/// <summary>
/// Edit ▸ Content-Aware Fill…, Photoshop's workspace: the document with the sampling area over it in green, the Sampling Brush and
/// Lasso to change what is sampled and what is filled, a live preview of the result, and the panel of sampling, fill and output
/// settings. Apply fills and stays; OK fills and closes.
/// </summary>
internal sealed class ContentAwareFillWindow : DialogWindow
{
    protected override bool FullCaption => true;
    private readonly CanvasDocument _document;
    private readonly Guid _layer;
    private readonly Func<ContentFillRequest, bool> _apply;
    private readonly Func<SKBitmap> _render;
    private SKBitmap _composite, _alone;
    private SKBitmap Usable => _allLayers.IsChecked == true ? _composite : _alone;
    internal byte[] FillArea { get; private set; }
    internal byte[] SamplingArea { get; private set; }
    private readonly FillView _view;
    private readonly Image _preview = new() { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _previewNote = new() { Foreground = Skin.SecondaryBrush, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6) };
    private readonly Slider _previewZoom = new() { Minimum = 10, Maximum = 400, Value = 100, Width = 220 };
    private readonly TextBox _previewPercent = DialogKit.Line("preview-zoom");
    private readonly CheckBox _showSampling = new() { Content = Localize.Text("Show Sampling Area"), IsChecked = true };
    private readonly Slider _opacity = new() { Minimum = 0, Maximum = 100, Value = 50, Width = 150 };
    private readonly TextBox _opacityValue = DialogKit.Line("overlay-opacity");
    private readonly ComboBox _indicates = SideDialog.Grouped([[("sampling", "Sampling Area"), ("excluded", "Excluded Area")]], "sampling", 150);
    private readonly Border _overlayColor = new() { Width = 22, Height = 18, Background = new SolidColorBrush(Color.FromRgb(0x5C, 0xD6, 0x3C)), BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand) };
    private readonly ToggleButton _auto = Segment("Auto"), _rectangular = Segment("Rectangular"), _custom = Segment("Custom");
    private readonly CheckBox _allLayers = new() { Content = Localize.Text("Sample All Layers") }, _scale = new() { Content = Localize.Text("Scale") }, _mirror = new() { Content = Localize.Text("Mirror") };
    private readonly ComboBox _color = SideDialog.Grouped([[("0", "None"), ("1", "Default"), ("2", "High"), ("3", "Very High")]], "1", 120);
    private readonly ComboBox _rotation = SideDialog.Grouped([[("0", "None"), ("1", "Low"), ("2", "Medium"), ("3", "High"), ("4", "Full")]], "0", 120);
    private readonly ComboBox _output = SideDialog.Grouped([[("0", "Current Layer"), ("1", "New Layer"), ("2", "Duplicate Layer")]], "1", 150);
    private readonly NumericUpDown _brushSize = new() { Minimum = 1, Maximum = 2000, Value = 120, Width = 80, Height = 26, MinHeight = 0, ShowButtonSpinner = false, FormatString = "0" };
    private readonly ToggleButton _addMode = new() { Width = 28, Height = 26, Padding = new Thickness(4), IsChecked = true }, _subtractMode = new() { Width = 28, Height = 26, Padding = new Thickness(4) };
    private readonly List<ToggleButton> _tools = [];
    private readonly TextBlock _error = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
    private CancellationTokenSource? _pending;
    private int _wave;
    internal string Tool { get; private set; } = "brush";
    internal bool Subtracting => _subtractMode.IsChecked == true;
    internal bool PreviewReady { get; private set; }

    internal ContentAwareFillWindow(CanvasDocument document, Guid layer, byte[] fill, Func<SKBitmap> render, Func<ContentFillRequest, bool> apply)
    {
        _document = document; _layer = layer; _render = render; _apply = apply; _composite = render(); FillArea = fill;
        _alone = LayerAlone(document, layer); SamplingArea = ContentFillEdits.Sampling(WithFill(), false, _alone);
        Title = Localize.Text("Content-Aware Fill"); Width = 1440; Height = 900; MinWidth = 1080; MinHeight = 640; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _view = new FillView(this);

        // Options bar: the tool, add or subtract, and the brush size.
        _addMode.Content = Glyph("M12,5 V19 M5,12 H19 M12,2 A10,10 0 1 1 11.99,2 Z"); _subtractMode.Content = Glyph("M5,12 H19 M12,2 A10,10 0 1 1 11.99,2 Z");
        ToolTip.SetTip(_addMode, Localize.Text("Add to Area")); ToolTip.SetTip(_subtractMode, Localize.Text("Subtract from Area"));
        _addMode.Click += (_, _) => { _addMode.IsChecked = true; _subtractMode.IsChecked = false; }; _subtractMode.Click += (_, _) => { _subtractMode.IsChecked = true; _addMode.IsChecked = false; };
        var options = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(10, 4), Children = { _addMode, _subtractMode, new TextBlock { Text = Localize.Text("Size") + ":", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) }, _brushSize } };

        var toolStrip = new StackPanel { Spacing = 4, Margin = new Thickness(4, 6), Width = 36 };
        foreach (var (key, tip, data) in new[]
        {
            ("brush", "Sampling Brush Tool (B)", "M3,21 C3,17 6,15 8,15 C10,15 11,17 10,19 C9,21 5,22 3,21 Z M10,15 L20,3 L22,5 L12,17"),
            ("lasso", "Lasso Tool (L)", "M12,4 C18,4 21,7 21,10 C21,13 17,15 12,15 C7,15 3,13 3,10 C3,7 6,4 12,4 Z M7,14 C6,17 7,19 9,19 C10,19 10,21 8,22"),
            ("hand", "Hand Tool (H)", "M7,12 V5 A1.5,1.5 0 0 1 10,5 V11 M10,10 V3.5 A1.5,1.5 0 0 1 13,3.5 V11 M13,10 V4.5 A1.5,1.5 0 0 1 16,4.5 V12 M16,11 V7 A1.5,1.5 0 0 1 19,7 V15 C19,19 16,22 12,22 C9,22 7,20 5,17 L3,13 A1.5,1.5 0 0 1 6,12 L7,14"),
            ("zoom", "Zoom Tool (Z)", "M10,3 A7,7 0 1 1 9.99,3 Z M15,15 L21,21"),
        })
        {
            var button = new ToggleButton { Width = 32, Height = 30, Padding = new Thickness(5), Tag = "fill-tool-" + key, IsChecked = key == Tool, Content = Glyph(data) };
            ToolTip.SetTip(button, Localize.Text(tip)); var chosen = key; button.Click += (_, _) => PickTool(chosen); _tools.Add(button); toolStrip.Children.Add(button);
        }
        KeyDown += (_, e) => { if (e.KeyModifiers == KeyModifiers.None && e.Source is not TextBox && e.Key switch { Key.B => "brush", Key.L => "lasso", Key.H => "hand", Key.Z => "zoom", _ => null } is { } key) { PickTool(key); e.Handled = true; } };

        // Preview panel.
        _previewPercent.Width = 60; _previewPercent.Text = "100%";
        _previewZoom.ValueChanged += (_, _) => { _previewPercent.Text = Math.Round(_previewZoom.Value) + "%"; ScalePreview(); };
        var previewScroll = new ScrollViewer { Content = _preview, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Skin.SurfaceDarkBrush };
        var previewPanel = new DockPanel { Children =
        {
            new Border { Background = Skin.TabStripBrush, Padding = new Thickness(10, 6), Child = new TextBlock { Text = Localize.Text("Preview") }, [DockPanel.DockProperty] = Dock.Top },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8), Children = { _previewPercent, _previewZoom }, [DockPanel.DockProperty] = Dock.Bottom },
            new Border { Child = _previewNote, [DockPanel.DockProperty] = Dock.Bottom },
            previewScroll,
        } };

        // The Content-Aware Fill panel.
        _opacityValue.Width = 56; _opacityValue.Text = "50%";
        _opacity.ValueChanged += (_, _) => { _opacityValue.Text = Math.Round(_opacity.Value) + "%"; _view.Refresh(); };
        _showSampling.IsCheckedChanged += (_, _) => _view.Refresh(); _indicates.SelectionChanged += (_, _) => _view.Refresh();
        _overlayColor.PointerPressed += async (_, _) =>
        {
            var current = ((SolidColorBrush)_overlayColor.Background!).Color;
            await ColorPickerDialog.Pick(this, Localize.Text("Sampling Area Overlay Color"), (current.R / 255d, current.G / 255d, current.B / 255d), rgb =>
            { _overlayColor.Background = new SolidColorBrush(Color.FromRgb((byte)Math.Round(rgb.Red * 255), (byte)Math.Round(rgb.Green * 255), (byte)Math.Round(rgb.Blue * 255))); _view.Refresh(); });
        };
        _auto.IsChecked = true;
        foreach (var segment in new[] { _auto, _rectangular, _custom }) segment.Click += (_, _) => PickSampling(segment);
        foreach (var check in new[] { _scale, _mirror }) check.IsCheckedChanged += (_, _) => Changed();
        _allLayers.IsCheckedChanged += (_, _) => Edited(sampling: false);
        foreach (var box in new[] { _color, _rotation }) box.SelectionChanged += (_, _) => Changed();
        var overlay = Heading("Sampling Area Overlay", null, new StackPanel { Spacing = 8, Children =
        {
            _showSampling,
            new Grid { ColumnDefinitions = new("90,*,Auto"), ColumnSpacing = 8, Children = { Label("Opacity"), new Border { Child = _opacity, [Grid.ColumnProperty] = 1 }, new Border { Child = _opacityValue, [Grid.ColumnProperty] = 2 } } },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Label("Color"), _overlayColor, new TextBlock { Text = Localize.Text("Indicates") + ":", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) }, _indicates } },
        } });
        var sampling = Heading("Sampling Area Options", ResetButton(() => { PickSampling(_auto); _allLayers.IsChecked = false; }), new StackPanel { Spacing = 8, Children =
        {
            new UniformGrid { Columns = 3, Children = { _auto, _rectangular, _custom } }, _allLayers,
        } });
        var fillSettings = Heading("Fill Settings", ResetButton(() => { Select(_color, "1"); Select(_rotation, "0"); _scale.IsChecked = _mirror.IsChecked = false; }), new StackPanel { Spacing = 8, Children =
        {
            new Grid { ColumnDefinitions = new("120,*"), ColumnSpacing = 8, Children = { Label("Color Adaptation"), new Border { Child = _color, [Grid.ColumnProperty] = 1 } } },
            new Grid { ColumnDefinitions = new("120,*"), ColumnSpacing = 8, Children = { Label("Rotation Adaptation"), new Border { Child = _rotation, [Grid.ColumnProperty] = 1 } } },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, Children = { _scale, _mirror } },
        } });
        var output = Heading("Output Settings", null, new Grid { ColumnDefinitions = new("120,*"), ColumnSpacing = 8, Children = { Label("Output To"), new Border { Child = _output, [Grid.ColumnProperty] = 1 } } });
        var reset = ResetButton(ResetAll); ToolTip.SetTip(reset, Localize.Text("Reset all settings"));
        var ok = FormDialog.ActionButton("OK", () => { if (Fill()) Close(true); }); ok.IsDefault = true;
        var applyButton = FormDialog.ActionButton("Apply", () => { if (Fill()) Rebase(); });
        var cancel = FormDialog.ActionButton("Cancel", () => Close(false)); cancel.IsCancel = true;
        foreach (var button in new[] { ok, applyButton, cancel }) button.Width = 84;
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10), Children = { reset, ok, applyButton, cancel } };
        var panel = new DockPanel { Width = 330, Children =
        {
            new Border { Background = Skin.TabStripBrush, Padding = new Thickness(10, 6), Child = new TextBlock { Text = Localize.Text("Content-Aware Fill") }, [DockPanel.DockProperty] = Dock.Top },
            new Border { Child = footer, [DockPanel.DockProperty] = Dock.Bottom }, new Border { Child = _error, Margin = new Thickness(10, 0), [DockPanel.DockProperty] = Dock.Bottom },
            new ScrollViewer { Content = new StackPanel { Spacing = 4, Margin = new Thickness(10, 6), Children = { overlay, sampling, fillSettings, output } }, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
        } };

        var middle = new Grid { ColumnDefinitions = new("Auto,*,4,340,1,Auto"), Children = { toolStrip, new Border { Child = _view, Background = Skin.PasteboardBrush, ClipToBounds = true, [Grid.ColumnProperty] = 1 }, new Border { Child = previewPanel, [Grid.ColumnProperty] = 3, BorderBrush = Skin.BorderSubtleBrush, BorderThickness = new Thickness(1, 0) }, new Border { Child = panel, [Grid.ColumnProperty] = 5 } } };
        Content = new DockPanel { Children = { new Border { Child = options, Background = Skin.ChromeBrush, BorderBrush = Skin.BorderSubtleBrush, BorderThickness = new Thickness(0, 0, 0, 1), [DockPanel.DockProperty] = Dock.Top }, middle } };
        Opened += (_, _) => { _view.Fit(); Changed(); };
        Closed += (_, _) => { _pending?.Cancel(); _composite.Dispose(); _alone.Dispose(); };
    }

    private static Control Glyph(string data) => new AvaloniaPath { Data = Geometry.Parse(data), Stroke = Skin.LabelBrush, StrokeThickness = 1.5, Stretch = Stretch.Uniform };
    private static ToggleButton Segment(string text) => new() { Content = Localize.Text(text), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Height = 26, MinHeight = 0, Padding = new Thickness(4, 0), Tag = "sampling-" + text };
    private static TextBlock Label(string text) => new() { Text = Localize.Text(text), VerticalAlignment = VerticalAlignment.Center, Foreground = Skin.SecondaryBrush };
    private static Button ResetButton(Action action)
    {
        var button = new Button { Width = 26, Height = 24, Padding = new Thickness(4), Background = Brushes.Transparent, Content = Glyph("M4,12 A8,8 0 1 0 7,5.5 M4,4 V9 H9") };
        ToolTip.SetTip(button, Localize.Text("Reset")); button.Click += (_, _) => action(); return button;
    }
    private static Control Heading(string title, Control? trailing, Control content)
    {
        var chevron = new AvaloniaPath { Data = Geometry.Parse("M0,0 L4,4 L8,0"), Stroke = Skin.SecondaryBrush, StrokeThickness = 1.2, Width = 9, Height = 9, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
        var header = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 8, Background = Brushes.Transparent, Margin = new Thickness(0, 10, 0, 6), Cursor = new Cursor(StandardCursorType.Hand), Children = { chevron, new TextBlock { Text = Localize.Text(title), [Grid.ColumnProperty] = 1 } } };
        if (trailing is not null) { Grid.SetColumn(trailing, 2); header.Children.Add(trailing); }
        header.PointerPressed += (_, e) => { if (e.Source is Visual v && trailing is not null && (v == trailing || Avalonia.VisualTree.VisualExtensions.IsVisualAncestorOf(trailing, v))) return; content.IsVisible = !content.IsVisible; chevron.Data = Geometry.Parse(content.IsVisible ? "M0,0 L4,4 L8,0" : "M0,0 L4,4 L0,8"); };
        content.Margin = new Thickness(14, 0, 0, 0);
        return new StackPanel { Children = { header, content } };
    }
    private static void Select(ComboBox box, string key) => box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, key));
    private void PickTool(string key) { Tool = key; foreach (var button in _tools) button.IsChecked = Equals(button.Tag, "fill-tool-" + key); _view.UpdateCursor(); }
    private void PickSampling(ToggleButton chosen)
    {
        foreach (var segment in new[] { _auto, _rectangular, _custom }) segment.IsChecked = segment == chosen;
        if (chosen != _custom) SamplingArea = ContentFillEdits.Sampling(WithFill(), chosen == _rectangular, Usable);
        _view.Refresh(); Changed();
    }
    private void ResetAll() { PickSampling(_auto); _allLayers.IsChecked = false; Select(_color, "1"); Select(_rotation, "0"); _scale.IsChecked = _mirror.IsChecked = false; Select(_output, "1"); _opacity.Value = 50; _showSampling.IsChecked = true; }

    /// <summary>The document with the fill area as its selection, which is what the sampling areas are measured from.</summary>
    private CanvasDocument WithFill()
    {
        var view = _document.Clone();
        // The selection keeps the coverage bitmap it is made from, so it is not disposed here.
        var gray = new SKBitmap(new SKImageInfo(_document.Width, _document.Height, SKColorType.Gray8, SKAlphaType.Opaque)); FillArea.CopyTo(gray.GetPixelSpan());
        view.Selection = DocumentSelection.FromCoverage(gray); return view;
    }
    internal Color OverlayColor => ((SolidColorBrush)_overlayColor.Background!).Color;
    internal double OverlayOpacity => _showSampling.IsChecked == true ? _opacity.Value / 100 : 0;
    internal bool ShowsExcluded => SideDialog.Key(_indicates) == "excluded";
    internal SKBitmap Composite => _composite;
    internal int BrushSize => (int)(_brushSize.Value ?? 120);
    private ContentFillRequest Request() => new(FillArea, new ContentFillOptions
    {
        Sampling = SamplingArea, ColorAdaptation = int.Parse(SideDialog.Key(_color) ?? "1"), RotationAdaptation = int.Parse(SideDialog.Key(_rotation) ?? "0"), Scale = _scale.IsChecked == true, Mirror = _mirror.IsChecked == true,
    }, _allLayers.IsChecked == true, int.Parse(SideDialog.Key(_output) ?? "1"));

    /// <summary>The brush painted the sampling area or the lasso changed the fill area: the sampling area is now the person's own.</summary>
    internal void Edited(bool sampling)
    {
        if (sampling) { foreach (var segment in new[] { _auto, _rectangular, _custom }) segment.IsChecked = segment == _custom; }
        else if (_custom.IsChecked != true) SamplingArea = ContentFillEdits.Sampling(WithFill(), _rectangular.IsChecked == true, Usable);
        for (var i = 0; i < SamplingArea.Length; i++) if (FillArea[i] > 0) SamplingArea[i] = 0;
        _view.Refresh(); Changed();
    }
    /// <summary>Redraws the preview a moment after the last change: the fill run on a copy, cropped around the fill area.</summary>
    private void Changed()
    {
        var wave = ++_wave; _ = Settle();
        async Task Settle()
        {
            await Task.Delay(250); if (wave != _wave) return;
            _pending?.Cancel(); var cancel = _pending = new CancellationTokenSource(); var request = Request(); var allLayers = request.AllLayers;
            _previewNote.Text = Localize.Text("Updating preview…"); PreviewReady = false;
            try
            {
                var (image, crop) = await Task.Run(() => PreviewFill(request, allLayers), cancel.Token);
                if (cancel.IsCancellationRequested || wave != _wave) { image?.Dispose(); return; }
                var old = _preview.Source as IDisposable; _preview.Source = image is null ? null : CanvasView.ToImage(image); image?.Dispose(); old?.Dispose();
                _previewNote.Text = image is null ? Localize.Text("Nothing to fill from: paint more sampling area.") : $"{crop.Width} × {crop.Height} px"; PreviewReady = image is not null; ScalePreview();
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { _previewNote.Text = Localize.Text(error.Message); }
        }
    }
    private (SKBitmap? Image, SKRectI Crop) PreviewFill(ContentFillRequest request, bool allLayers)
    {
        using var copy = _document.Clone();
        // The crop is the fill area's own box, measured from its pixels, with a quarter of its size around it.
        int width = copy.Width, left = width, top = copy.Height, right = -1, bottom = -1;
        for (var i = 0; i < request.Fill.Length; i++) if (request.Fill[i] > 0) { int x = i % width, y = i / width; left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
        if (right < 0) return (null, default);
        var bounds = SKRectI.Create(left, top, right - left + 1, bottom - top + 1); var margin = Math.Max(bounds.Width, bounds.Height) / 4 + 8;
        var crop = SKRectI.Intersect(SKRectI.Inflate(bounds, margin, margin), SKRectI.Create(copy.Width, copy.Height));
        // The preview is the output as it would be made: the same layer, copy or new layer, on a copy of the document.
        return Perform(copy, _layer, request, out _) ? (DocumentRenderer.RenderRegion(copy, crop), crop) : (null, crop);
    }

    /// <summary>Fills <paramref name="doc"/> as the request asks: the fill area becomes the selection, and the fill goes into the layer,
    /// a duplicate of it, or a new layer above it that samples the layer alone (or every layer).</summary>
    internal static bool Perform(CanvasDocument doc, Guid id, ContentFillRequest request, out Guid? made)
    {
        // The selection keeps the coverage bitmap it is made from, so it is not disposed here.
        var gray = new SKBitmap(new SKImageInfo(doc.Width, doc.Height, SKColorType.Gray8, SKAlphaType.Opaque)); request.Fill.CopyTo(gray.GetPixelSpan()); doc.Selection = DocumentSelection.FromCoverage(gray);
        using var source = request.AllLayers ? DocumentRenderer.Render(doc) : request.Output == 1 ? LayerAlone(doc, id) : null;
        var options = request.Options with { Source = source };
        switch (request.Output)
        {
            case 2:
                made = LayerEdits.Duplicate(doc, id);
                return made is { } copy && ContentFillEdits.Apply(doc, copy, options);
            case 1:
                var empty = new SKBitmap(Bitmaps.ColorInfo(doc.Width, doc.Height)); empty.Erase(SKColors.Transparent);
                made = LayerPlacement.AddImage(doc, empty, SKRectI.Create(doc.Width, doc.Height), Localize.Text("Content-Aware Fill"), id);
                return made is { } layer && ContentFillEdits.Apply(doc, layer, options);
            default:
                made = id;
                return ContentFillEdits.Apply(doc, id, options);
        }
    }
    /// <summary>One layer drawn by itself at its place in the document: what a new fill layer samples without Sample All Layers.</summary>
    private static SKBitmap LayerAlone(CanvasDocument doc, Guid id)
    {
        using var alone = doc.Clone();
        foreach (var layer in alone.Layers) if (layer.ID != id && !alone.Layers.Any(l => l.ID == id && l.ParentID == layer.ID)) layer.IsVisible = false;
        return DocumentRenderer.Render(alone);
    }
    private void ScalePreview() { if (_preview.Source is Bitmap image) { _preview.Width = image.PixelSize.Width * _previewZoom.Value / 100; _preview.Height = image.PixelSize.Height * _previewZoom.Value / 100; _preview.Stretch = Stretch.Fill; } }
    private bool Fill()
    {
        try { if (_apply(Request())) { _error.Text = ""; return true; } _error.Text = Localize.Text("Content-Aware Fill found nothing to fill from: paint more sampling area."); }
        catch (Exception error) { _error.Text = Localize.Text(error.Message); }
        return false;
    }
    /// <summary>After Apply the document has changed under the workspace: it is drawn again and the preview starts over.</summary>
    private void Rebase() { var old = _composite; _composite = _render(); old.Dispose(); var alone = _alone; _alone = LayerAlone(_document, _layer); alone.Dispose(); _view.Rebase(); Changed(); }
    internal void ApplyForCheck() => Fill();
    internal string ErrorText => _error.Text + " / " + _previewNote.Text;

    /// <summary>The document as the workspace shows it, with the sampling overlay and the fill area's outline, and the tools on it.</summary>
    private sealed class FillView(ContentAwareFillWindow owner) : Control
    {
        private Bitmap? _picture, _overlay; private double _scale = 1, _zoom = 1; private Point _origin; private Geometry? _outline;
        private Point? _panFrom; private readonly List<SKPoint> _lasso = []; private bool _painting, _erasing;
        private double DisplayScale => Math.Min(1, 2048d / Math.Max(owner._document.Width, owner._document.Height));
        internal void Rebase() { _picture?.Dispose(); _picture = null; Refresh(); }
        internal void Fit()
        {
            if (Bounds.Width <= 0) return;
            _zoom = Math.Min(Bounds.Width / owner._document.Width, Bounds.Height / owner._document.Height) * .92;
            _origin = new Point((Bounds.Width - owner._document.Width * _zoom) / 2, (Bounds.Height - owner._document.Height * _zoom) / 2); Refresh();
        }
        internal void UpdateCursor() => Cursor = new Cursor(owner.Tool switch { "hand" => StandardCursorType.Hand, "zoom" => StandardCursorType.Cross, _ => StandardCursorType.Cross });
        /// <summary>Redraws the overlay and the outline at the display size, which is at most 2048 pixels across.</summary>
        internal void Refresh()
        {
            _scale = DisplayScale; int w = Math.Max(1, (int)(owner._document.Width * _scale)), h = Math.Max(1, (int)(owner._document.Height * _scale));
            if (_picture is null) { using var small = owner.Composite.Resize(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)); _picture = small is null ? null : CanvasView.ToImage(small); }
            var overlay = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            var color = owner.OverlayColor; var alpha = owner.OverlayOpacity; var excluded = owner.ShowsExcluded; var fillSmall = new byte[w * h];
            using (var locked = overlay.Lock())
            unsafe
            {
                var row = (byte*)locked.Address; int docW = owner._document.Width, docH = owner._document.Height;
                for (var y = 0; y < h; y++, row += locked.RowBytes)
                    for (var x = 0; x < w; x++)
                    {
                        var dx = Math.Min(docW - 1, (int)(x / _scale)); var dy = Math.Min(docH - 1, (int)(y / _scale)); var i = dy * docW + dx;
                        fillSmall[y * w + x] = (byte)(owner.FillArea[i] > 127 ? 1 : 0);
                        var shown = (owner.SamplingArea[i] > 0) != excluded && owner.FillArea[i] == 0; var a = shown ? alpha : 0;
                        row[x * 4] = (byte)(color.B * a); row[x * 4 + 1] = (byte)(color.G * a); row[x * 4 + 2] = (byte)(color.R * a); row[x * 4 + 3] = (byte)(255 * a);
                    }
            }
            _overlay?.Dispose(); _overlay = overlay;
            using var outline = SelectionEdits.Outline(fillSmall, w, h); _outline = outline is null ? null : Geometry.Parse(outline.ToSvgPathData());
            InvalidateVisual();
        }
        public override void Render(DrawingContext context)
        {
            if (_picture is null) return;
            var target = new Rect(_origin.X, _origin.Y, owner._document.Width * _zoom, owner._document.Height * _zoom);
            context.DrawImage(_picture, target); if (_overlay is not null) context.DrawImage(_overlay, target);
            if (_outline is { } outline)
            {
                var factor = _zoom / _scale;
                using (context.PushTransform(Matrix.CreateScale(factor, factor) * Matrix.CreateTranslation(_origin.X, _origin.Y)))
                {
                    context.DrawGeometry(null, new Pen(Brushes.Black, 1 / factor), outline);
                    context.DrawGeometry(null, new Pen(Brushes.White, 1 / factor, new DashStyle([4, 4], 0)), outline);
                }
            }
            if (_lasso.Count > 1)
            {
                var geometry = new StreamGeometry(); using (var g = geometry.Open()) { g.BeginFigure(View(_lasso[0]), false); foreach (var p in _lasso.Skip(1)) g.LineTo(View(p)); g.EndFigure(false); }
                context.DrawGeometry(null, new Pen(Brushes.White, 1, new DashStyle([3, 3], 0)), geometry);
            }
        }
        private Point View(SKPoint p) => new(_origin.X + p.X * _zoom, _origin.Y + p.Y * _zoom);
        private SKPoint Document(Point p) => new((float)((p.X - _origin.X) / _zoom), (float)((p.Y - _origin.Y) / _zoom));
        protected override void OnPointerWheelChanged(PointerWheelEventArgs e) { ZoomAt(e.GetPosition(this), e.Delta.Y > 0 ? 1.2 : 1 / 1.2); e.Handled = true; }
        private void ZoomAt(Point at, double factor)
        {
            var before = Document(at); _zoom = Math.Clamp(_zoom * factor, .02, 32);
            _origin = new Point(at.X - before.X * _zoom, at.Y - before.Y * _zoom); InvalidateVisual();
        }
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            var point = e.GetCurrentPoint(this); var at = point.Position; e.Pointer.Capture(this); e.Handled = true;
            if (point.Properties.IsMiddleButtonPressed || owner.Tool == "hand") { _panFrom = at; return; }
            if (owner.Tool == "zoom") { ZoomAt(at, e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? 1 / 1.5 : 1.5); return; }
            _erasing = owner.Subtracting ^ e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            if (owner.Tool == "brush") { _painting = true; Paint(Document(at)); Refresh(); }
            else if (owner.Tool == "lasso") { _lasso.Clear(); _lasso.Add(Document(at)); }
        }
        protected override void OnPointerMoved(PointerEventArgs e)
        {
            var at = e.GetPosition(this);
            if (_panFrom is { } from) { _origin += at - from; _panFrom = at; InvalidateVisual(); return; }
            if (_painting) { Paint(Document(at)); Refresh(); }
            else if (_lasso.Count > 0) { _lasso.Add(Document(at)); InvalidateVisual(); }
        }
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            e.Pointer.Capture(null);
            if (_panFrom is not null) { _panFrom = null; return; }
            if (_painting) { _painting = false; owner.Edited(sampling: true); return; }
            if (_lasso.Count > 2) { Lasso(_lasso, _erasing); _lasso.Clear(); owner.Edited(sampling: false); }
            else _lasso.Clear();
        }
        /// <summary>The Sampling Brush: a round dab added to (or, with Alt or Subtract, taken from) the sampling area.</summary>
        private void Paint(SKPoint at)
        {
            int width = owner._document.Width, height = owner._document.Height; var radius = Math.Max(1, owner.BrushSize / 2d);
            for (var y = (int)Math.Max(0, at.Y - radius); y < Math.Min(height, at.Y + radius); y++)
                for (var x = (int)Math.Max(0, at.X - radius); x < Math.Min(width, at.X + radius); x++)
                    if ((x + .5 - at.X) * (x + .5 - at.X) + (y + .5 - at.Y) * (y + .5 - at.Y) <= radius * radius) owner.SamplingArea[y * width + x] = (byte)(_erasing ? 0 : 255);
        }
        /// <summary>The Lasso: the outline drawn is added to (or taken from) the area to fill.</summary>
        private void Lasso(IReadOnlyList<SKPoint> points, bool erase)
        {
            using var shape = new SKBitmap(new SKImageInfo(owner._document.Width, owner._document.Height, SKColorType.Alpha8, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(shape)) using (var path = SelectionEdits.Lasso(points)) using (var paint = new SKPaint { IsAntialias = true, Color = SKColors.White }) { canvas.Clear(SKColors.Transparent); canvas.DrawPath(path, paint); }
            var span = shape.GetPixelSpan();
            for (var i = 0; i < owner.FillArea.Length; i++) owner.FillArea[i] = erase ? (byte)(owner.FillArea[i] * (255 - span[i]) / 255) : Math.Max(owner.FillArea[i], span[i]);
        }
    }
}
