using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Compositor.Core.Document;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;
namespace Compositor.Desktop;
public sealed partial class MainWindow
{
    private sealed class CloneSlot
    {
        internal Guid? Document, Layer;
        internal SKPoint? Point, Anchor;
        internal double Width = 100, Height = 100, Angle, Opacity = 100;
        internal bool FlipX, FlipY, Linked = true, LockFrame, Show = true, Clipped = true, AutoHide, Invert;
        internal int Frame, Offset, Blend;
    }
    private readonly CloneSlot[] _cloneSlots = Enumerable.Range(0, 5).Select(_ => new CloneSlot()).ToArray();
    private int _cloneSlotIndex, _timelineFrame;
    private CloneSlot CloneSlotState => _cloneSlots[_cloneSlotIndex];
    private WriteableBitmap? _clonePreview;
    private string? _clonePreviewKey;
    private bool _clonePreviewBusy, _clonePreviewClosed;
    private (CanvasDocument? Document, Guid? Layer) CloneSourceDocument()
    {
        var slot = CloneSlotState; var document = _tabs.FirstOrDefault(t => t.Document?.ID == slot.Document)?.Document;
        if (document is null) return (null, null);
        var index = (slot.LockFrame ? slot.Frame : _timelineFrame) + slot.Offset;
        return (document, slot.Offset != 0 || slot.LockFrame ? document.Layers.ElementAtOrDefault(index)?.ID : slot.Layer);
    }
    private void BuildCloneSourcePanel(StackPanel body)
    {
        var refresh = new List<Action>(); var loading = false;
        void Changed() { _clonePreviewKey = null; _canvas.InvalidateOverlay(); RefreshUtilityPanels(); }
        var slots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; body.Children.Add(slots);
        for (var i = 0; i < 5; i++)
        {
            var index = i; var button = new Button { Content = new EditorIcon("Clone"), Width = 32, Height = 30, Padding = new Thickness(5), Tag = "clone-source-" + i };
            ToolTip.SetTip(button, Localize.Text("Clone Source") + " " + (i + 1));
            button.Click += (_, _) => { _cloneSlotIndex = index; var s = CloneSlotState; _cloneSource = s.Point; _cloneOffset = null; _canvas.CloneSource = s.Point; _canvas.CloneOffset = null; Changed(); };
            refresh.Add(() => button.Background = index == _cloneSlotIndex ? Skin.SurfaceDarkBrush : Brushes.Transparent); slots.Children.Add(button);
        }
        var sourceName = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Skin.SecondaryBrush }; body.Children.Add(sourceName);
        refresh.Add(() => { var source = CloneSourceDocument(); sourceName.Text = source.Document is null ? Localize.Text("Alt-click to set a source.") : Localize.Text("Source:") + " " + (_tabs.First(t => t.Document == source.Document).Name) + " / " + source.Document.Layers.FirstOrDefault(l => l.ID == source.Layer)?.Name; });
        var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8, RowSpacing = 4 }; body.Children.Add(fields);
        var fieldCount = 0;
        void AddField(Control control)
        {
            if (fieldCount % 2 == 0) fields.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetColumn(control, fieldCount % 2); Grid.SetRow(control, fieldCount / 2); fields.Children.Add(control); fieldCount++;
        }
        void Number(string title, Func<double> get, Action<double> set, double min, double max)
        {
            var field = new NumericUpDown { Minimum = (decimal)min, Maximum = (decimal)max, ShowButtonSpinner = false, FormatString = "0.##", MinHeight = 26, Tag = "clone-" + title };
            var pair = new StackPanel { Spacing = 2 }; pair.Children.Add(new TextBlock { Text = Localize.Text(title), FontSize = 11 }); pair.Children.Add(field); AddField(pair);
            refresh.Add(() => field.Value = (decimal)get()); field.ValueChanged += (_, _) => { if (!loading && field.Value is { } n) { set((double)n); Changed(); } };
        }
        void Check(string title, Func<bool> get, Action<bool> set)
        {
            var field = new CheckBox { Content = Localize.Text(title), MinHeight = 25, Tag = "clone-" + title }; AddField(field);
            refresh.Add(() => field.IsChecked = get()); field.IsCheckedChanged += (_, _) => { if (!loading) { set(field.IsChecked == true); Changed(); } };
        }
        void Position(float x, float y) { CloneSlotState.Point = _cloneSource = new SKPoint(x, y); CloneSlotState.Document ??= _document?.ID; CloneSlotState.Layer ??= Selected; CloneSlotState.Anchor = null; _cloneOffset = null; _canvas.CloneSource = _cloneSource; _canvas.CloneOffset = null; }
        Number("X", () => CloneSlotState.Point?.X ?? 0, n => Position((float)n, CloneSlotState.Point?.Y ?? 0), -100000, 100000);
        Number("Y", () => CloneSlotState.Point?.Y ?? 0, n => Position(CloneSlotState.Point?.X ?? 0, (float)n), -100000, 100000);
        Number("W %", () => CloneSlotState.Width, n => { if (CloneSlotState.Linked) CloneSlotState.Height = Math.Clamp(CloneSlotState.Height * n / CloneSlotState.Width, 1, 1000); CloneSlotState.Width = n; }, 1, 1000);
        Number("H %", () => CloneSlotState.Height, n => { if (CloneSlotState.Linked) CloneSlotState.Width = Math.Clamp(CloneSlotState.Width * n / CloneSlotState.Height, 1, 1000); CloneSlotState.Height = n; }, 1, 1000);
        Check("Constrain Proportions", () => CloneSlotState.Linked, n => CloneSlotState.Linked = n);
        Check("Flip Horizontal", () => CloneSlotState.FlipX, n => CloneSlotState.FlipX = n); Check("Flip Vertical", () => CloneSlotState.FlipY, n => CloneSlotState.FlipY = n);
        Number("Angle", () => CloneSlotState.Angle, n => CloneSlotState.Angle = n, -180, 180);
        AddField(PropertyAction("Reset Transform", () => { var s = CloneSlotState; s.Width = s.Height = 100; s.Angle = 0; s.FlipX = s.FlipY = false; Changed(); }));
        Number("Frame Offset", () => CloneSlotState.Offset, n => CloneSlotState.Offset = (int)n, -9999, 9999);
        Check("Lock Frame", () => CloneSlotState.LockFrame, n => { CloneSlotState.LockFrame = n; if (n) CloneSlotState.Frame = _timelineFrame; });
        body.Children.Add(new TextBlock { Text = Localize.Text("Frame controls sample the layer frames shown in Timeline."), TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Skin.SecondaryBrush });
        Check("Show Overlay", () => CloneSlotState.Show, n => CloneSlotState.Show = n);
        Number("Opacity %", () => CloneSlotState.Opacity, n => CloneSlotState.Opacity = n, 0, 100);
        Check("Clipped", () => CloneSlotState.Clipped, n => CloneSlotState.Clipped = n);
        Check("Auto Hide", () => CloneSlotState.AutoHide, n => CloneSlotState.AutoHide = n); Check("Invert", () => CloneSlotState.Invert, n => CloneSlotState.Invert = n);
        var blend = new ComboBox { ItemsSource = new[] { "Normal", "Darken", "Lighten", "Difference" }.Select(Localize.Text).ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch };
        refresh.Add(() => blend.SelectedIndex = CloneSlotState.Blend); blend.SelectionChanged += (_, _) => { if (!loading && blend.SelectedIndex >= 0) { CloneSlotState.Blend = blend.SelectedIndex; Changed(); } }; body.Children.Add(blend);
        _utilityRefresh["Clone Source"] = () => { loading = true; try { foreach (var action in refresh) action(); } finally { loading = false; } };
        Closed += (_, _) => { _clonePreviewClosed = true; _clonePreview?.Dispose(); _clonePreview = null; };
    }
    private BrushSettings? SampledBrushFor(SKPoint start)
    {
        if (_tool == Tool.HealingBrush && _options.Brush.HealingPattern) return _canvas.Brush with { CloneFrom = new SKPointI(), CloneMapping = null, CloneDocument = null, CloneLayerID = null };
        if (_cloneSource is not { } source) { Say("Alt-click where the Clone Stamp should copy from first"); return null; }
        var slot = CloneSlotState; var sampled = CloneSourceDocument();
        if (sampled.Document is null || sampled.Layer is null) { Say("The clone source is no longer available. Alt-click to choose another source."); return null; }
        slot.Anchor = _options.Brush.CloneAligned ? slot.Anchor ?? start : start;
        var map = CloneSampling.Mapping(source, slot.Anchor.Value, slot.Width, slot.Height, slot.Angle, slot.FlipX, slot.FlipY);
        _cloneOffset = new SKPointI((int)Math.Round(source.X - slot.Anchor.Value.X), (int)Math.Round(source.Y - slot.Anchor.Value.Y)); _canvas.CloneOffset = _cloneOffset;
        return _canvas.Brush with { CloneFrom = _cloneOffset, CloneMapping = map, CloneDocument = sampled.Document, CloneLayerID = sampled.Layer };
    }
    private void DrawCloneOverlay(DrawingContext context, Func<SKPoint, Point> screen)
    {
        var s = CloneSlotState;
        if (_tool is not (Tool.Clone or Tool.HealingBrush) || _tool == Tool.HealingBrush && _options.Brush.HealingPattern || !s.Show || s.AutoHide && _canvas.IsPainting || _canvas.SourceTargetShowing || _cloneSource is not { } source || _canvas.ToolPointerPosition is not { } pointer) return;
        var sample = CloneSourceDocument(); if (sample.Document is not { } doc || sample.Layer is not { } id) return;
        var tab = _tabs.First(t => t.Document == doc); var settings = _options.Brush;
        var key = $"{tab.History.CurrentRevision}/{doc.ID}/{id}/{settings.CloneAllLayers}/{settings.CloneScope}/{settings.CloneIgnoreAdjustments}/{s.Invert}";
        if (_clonePreviewKey != key)
        {
            if (!_clonePreviewBusy && !_clonePreviewClosed)
            {
                var scope = settings.CloneAllLayers ? 2 : settings.CloneScope;
                using var view = ColorSampling.View(doc, id, scope == 0 ? SampleScope.CurrentLayer : scope == 1 ? (settings.CloneIgnoreAdjustments ? SampleScope.CurrentAndBelowWithoutAdjustments : SampleScope.CurrentAndBelow) : (settings.CloneIgnoreAdjustments ? SampleScope.AllWithoutAdjustments : SampleScope.AllLayers));
                _ = PrepareClonePreview(new DocumentLease(view), key, s.Invert);
            }
            return;
        }
        if (_clonePreview is null) return;
        var anchor = settings.CloneAligned && s.Anchor is { } fixedAnchor ? fixedAnchor : _canvas.DocumentPoint(pointer);
        if (!CloneSampling.Mapping(source, anchor, s.Width, s.Height, s.Angle, s.FlipX, s.FlipY).TryInvert(out var toDestination)) return;
        var p0 = screen(toDestination.MapPoint(0, 0)); var px = screen(toDestination.MapPoint(doc.Width, 0)); var py = screen(toDestination.MapPoint(0, doc.Height));
        var size = _clonePreview.Size; var matrix = new Matrix((px.X - p0.X) / size.Width, (px.Y - p0.Y) / size.Width, (py.X - p0.X) / size.Height, (py.Y - p0.Y) / size.Height, p0.X, p0.Y);
        var clip = s.Clipped ? (Geometry)new EllipseGeometry(new Rect(pointer.X - _canvas.OutlineDiameter / 2, pointer.Y - _canvas.OutlineDiameter / 2, _canvas.OutlineDiameter, _canvas.OutlineDiameter)) : new RectangleGeometry(_canvas.Bounds.WithX(0).WithY(0));
        using (context.PushGeometryClip(clip)) using (context.PushOpacity(s.Opacity / 100)) using (context.PushRenderOptions(new RenderOptions { BitmapBlendingMode = s.Blend switch { 1 => BitmapBlendingMode.Darken, 2 => BitmapBlendingMode.Lighten, 3 => BitmapBlendingMode.Difference, _ => BitmapBlendingMode.SourceOver } })) using (context.PushTransform(matrix)) context.DrawImage(_clonePreview, new Rect(size));
    }
    private async Task PrepareClonePreview(DocumentLease lease, string key, bool invert)
    {
        _clonePreviewBusy = true;
        try
        {
            using (lease)
            using (var pixels = await WorkScheduler.Run(_ =>
            {
                var result = ViewportRenderer.Render(lease.Document, SKRectI.Create(lease.Document.Width, lease.Document.Height), 768);
                if (invert) for (var y = 0; y < result.Height; y++) for (var x = 0; x < result.Width; x++)
                { var c = result.GetPixel(x, y); result.SetPixel(x, y, new SKColor((byte)(255 - c.Red), (byte)(255 - c.Green), (byte)(255 - c.Blue), c.Alpha)); }
                return result;
            }))
            {
                if (_clonePreviewClosed) return;
                _clonePreview?.Dispose(); _clonePreview = CanvasView.ToImage(pixels); _clonePreviewKey = key;
            }
        }
        catch (Exception error) when (error is OutOfMemoryException or InvalidOperationException)
        {
            _clonePreview?.Dispose(); _clonePreview = null; _clonePreviewKey = key;
            if (!_clonePreviewClosed) Say(error.Message);
        }
        finally { _clonePreviewBusy = false; if (!_clonePreviewClosed) _canvas.InvalidateOverlay(); }
    }

}
