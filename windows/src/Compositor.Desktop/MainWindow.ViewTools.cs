using Avalonia;
using Avalonia.Controls;
using Compositor.Core.Document;
using SkiaSharp;
namespace Compositor.Desktop;
public sealed partial class MainWindow
{
    private static bool _syncingViews;
    private double _lastViewZoom = 1, _lastViewAngle;
    private SKPoint _lastViewOrigin;
    private void ApplyViewOptions()
    { _canvas.ZoomOutMode = _options.ZoomOut; _canvas.ScrubbyZoom = _options.ZoomScrubby; }
    private bool HandleViewAction(string action)
    {
        switch (action)
        {
            case "100%": _canvas.ActualSize(); return true;
            case "Fit Screen": _canvas.Fit(); return true;
            case "Fill Screen": _canvas.FillScreen(); return true;
            case "Reset View": _canvas.RotateViewTo(0); return true;
            default: return false;
        }
    }
    private void ViewToolsChanged()
    {
        UpdateRulers(); var zoom = _canvas.Zoom; var angle = _canvas.ViewAngle; var origin = new SKPoint((float)_canvas.OriginX, (float)_canvas.OriginY);
        var factor = zoom / _lastViewZoom; var delta = origin - _lastViewOrigin; var angleDelta = angle - _lastViewAngle;
        _lastViewZoom = zoom; _lastViewAngle = angle; _lastViewOrigin = origin; _options.ViewAngle = angle > 180 ? angle - 360 : angle;
        RefreshOptionsBar();
        if (_syncingViews || _document is null) return;
        try
        {
            _syncingViews = true;
            foreach (var editor in Editors.Where(e => !ReferenceEquals(e, this) && e._document is not null).ToArray())
            {
                if (_tool == Tool.Zoom && _options.ZoomAll && Math.Abs(factor - 1) > .00001) editor._canvas.ZoomBy(factor);
                if (_tool == Tool.Pan && _options.PanAll) editor._canvas.RestoreViewport((editor._canvas.Zoom, editor._canvas.OriginX + delta.X, editor._canvas.OriginY + delta.Y));
                if (_tool == Tool.RotateView && _options.RotateAll && Math.Abs(angleDelta) > .00001) editor._canvas.RotateViewTo(editor._canvas.ViewAngle + angleDelta);
            }
            if (_tool == Tool.Zoom && _options.ZoomResize && WindowState == WindowState.Normal && Math.Abs(factor - 1) > .00001 && Screens.ScreenFromWindow(this) is { } screen)
            {
                var maximum = screen.WorkingArea.Size.ToSize(screen.Scaling);
                Width = Math.Clamp(_document.Width * zoom + Bounds.Width - _canvas.Bounds.Width, MinWidth, Math.Max(MinWidth, maximum.Width));
                Height = Math.Clamp(_document.Height * zoom + Bounds.Height - _canvas.Bounds.Height, MinHeight, Math.Max(MinHeight, maximum.Height));
            }
        }
        finally { _syncingViews = false; }
    }
}
