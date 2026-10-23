using Avalonia.Threading;
using Compositor.Core.Document;
using SkiaSharp;
namespace Compositor.Desktop;
public sealed partial class CanvasView
{
    internal PenSample CurrentPenSample { get; private set; } = new(1);
    private readonly List<PenSample> _penSamples = [];
    internal IReadOnlyList<PenSample>? CompletedPenSamples { get; private set; }
    private DispatcherTimer? _brushTimer;
    private SKPoint _brushTarget;
    private void StartPaintInput(SKPoint point)
    {
        _penSamples.Clear(); _penSamples.Add(CurrentPenSample); _brushTarget = point;
        _brushTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Input, (_, _) => TickPaintInput());
        if (Brush.Painting is not null) _brushTimer.Start();
    }
    private void TickPaintInput()
    {
        if (!_painting || Brush.Painting is not { } p) { _brushTimer?.Stop(); return; }
        if (_stroke.Count >= 60000) return;
        var last = _stroke[^1]; var next = Smoothed(_brushTarget);
        if (p.CatchUp && !p.PulledString && SKPoint.Distance(last, next) >= .25f) AddPaintPoint(next);
        else if (p.Airbrush || p.BuildUp) AddPaintPoint(last);
    }
    private void AddPaintPoint(SKPoint point)
    { if (_stroke.Count >= 60000) return; _stroke.Add(point); _strokePressures.Add((float)PointerPressure); _penSamples.Add(CurrentPenSample); InvalidateOverlay(); }
    private void EndPaintInput()
    {
        _brushTimer?.Stop();
        if (Brush.Painting is { SmoothingEnabled: true, CatchUpOnRelease: true } && _stroke.Count > 0 && SKPoint.Distance(_stroke[^1], _brushTarget) > .25) AddPaintPoint(_brushTarget);
        CompletedPenSamples = _penSamples.ToArray(); _penSamples.Clear();
    }
    private void CancelPaintInput()
    { _brushTimer?.Stop(); _painting = false; _stroke.Clear(); _strokePressures.Clear(); _penSamples.Clear(); CompletedPenSamples = null; InvalidateOverlay(); }
}
