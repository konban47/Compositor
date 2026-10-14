using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Compositor.Core.Document;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private readonly ScrollBar _canvasHorizontal = new() { Orientation = Orientation.Horizontal, Height = 14, AllowAutoHide = false, Background = Skin.SurfaceDarkBrush, Tag = "canvas-horizontal-scroll" };
    private readonly ScrollBar _canvasVertical = new() { Orientation = Orientation.Vertical, Width = 14, AllowAutoHide = false, Background = Skin.SurfaceDarkBrush, Tag = "canvas-vertical-scroll" };
    private bool _updatingCanvasScroll;
    private double _scrollX, _scrollY;
    private void InitializeCanvasNavigation()
    {
        _canvasHorizontal.ValueChanged += (_, _) => ScrollCanvas(true);
        _canvasVertical.ValueChanged += (_, _) => ScrollCanvas(false);
        _canvas.PointerTracked += TrackRulerPointer;
        _canvas.SizeChanged += (_, _) => UpdateCanvasScrollbars();
        _canvas.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (_document is not { } document || !e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed || _tool == Tool.Artboard) return;
            if (_canvas.NavigatorBounds.Contains(e.GetPosition(_canvas)) || _canvas.HoveredGuide is not null) return;
            var at = _canvas.InDocument(e.GetPosition(_canvas));
            var boards = document.Layers.Where(l => l.Container == Compositor.Core.Format.LayerContainer.Artboard).ToArray();
            if (boards.Length > 0 ? boards.Any(b => b.Transform.Contains(at)) : at.X >= 0 && at.Y >= 0 && at.X < document.Width && at.Y < document.Height) return;
            if (_canvas.TransformBox is { } box && _canvas.TransformEnabled && _transformShown && _canvas.TransformHandleAt(box, at) is not null) return;
            CommitText(); _layers.SelectedItems?.Clear(); _layers.SelectedIndex = -1; SetPaintingMask(false);
            ShowTransformBox(); RefreshInspector();
            if (_tool == Tool.Move) e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }
    private void TrackRulerPointer(Point? pointer)
    {
        if (pointer is null) { _moveHoverBox = null; _canvas.InvalidateOverlay(); }
        var at = pointer is { } p ? _canvas.InDocument(p) : (SKPoint?)null;
        _rulerAcross.PointerPosition = pointer?.X; _rulerDown.PointerPosition = pointer?.Y;
        _rulerAcross.PointerValue = at?.X; _rulerDown.PointerValue = at?.Y;
        _rulerAcross.InvalidateVisual(); _rulerDown.InvalidateVisual();
    }
    private void ScrollCanvas(bool horizontal)
    {
        if (_updatingCanvasScroll || _document is null) return;
        var change = horizontal ? _canvasHorizontal.Value - _scrollX : _canvasVertical.Value - _scrollY;
        _canvas.PanScreenBy(horizontal ? -change : 0, horizontal ? 0 : -change);
    }
    private void UpdateCanvasScrollbars()
    {
        if (_updatingCanvasScroll) return;
        _updatingCanvasScroll = true;
        try
        {
            var doc = _document; _canvasHorizontal.IsEnabled = _canvasVertical.IsEnabled = doc is not null;
            if (doc is null) { _canvasHorizontal.Value = _canvasVertical.Value = 0; return; }
            var corners = new[] { new SKPoint(), new SKPoint(doc.Width, 0), new SKPoint(0, doc.Height), new SKPoint(doc.Width, doc.Height) }.Select(_canvas.InView).ToArray();
            var left = corners.Min(p => p.X); var top = corners.Min(p => p.Y);
            _scrollX = -left; _scrollY = -top;
            void Set(ScrollBar bar, double value, double extent, double viewport)
            {
                var pad = Math.Max(100, viewport / 2);
                bar.Minimum = Math.Min(-pad, value); bar.Maximum = Math.Max(Math.Max(bar.Minimum, extent + pad - viewport), value);
                bar.ViewportSize = viewport; bar.SmallChange = 20; bar.LargeChange = Math.Max(20, viewport * .85); bar.Value = value;
            }
            Set(_canvasHorizontal, _scrollX, corners.Max(p => p.X) - left, _canvas.Bounds.Width);
            Set(_canvasVertical, _scrollY, corners.Max(p => p.Y) - top, _canvas.Bounds.Height);
            TrackRulerPointer(_canvas.ToolPointerPosition);
        }
        finally { _updatingCanvasScroll = false; }
    }
}
