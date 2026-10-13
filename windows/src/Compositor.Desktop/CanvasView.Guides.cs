using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Compositor.Core.Format;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class CanvasView
{
    internal HashSet<Guid> SelectedGuides { get; } = [];
    internal Guid? HoveredGuide => ShowsGuides && _toolPointer is { } at ? GuideAt(ToDocument(at)) : null;
    internal Action? GuideSettingsRequested { get; set; }
    private static readonly Dictionary<GuideAxis, (Cursor Cursor, WriteableBitmap Image)> GuideCursors = [];
    internal Guid? GuideAtPoint(SKPoint point) => ShowsGuides ? GuideAt(point) : null;
    private bool SelectGuidePointer(PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;
        if (!ShowsGuides || !properties.IsLeftButtonPressed && !properties.IsRightButtonPressed) return false;
        var found = GuideAt(ToDocument(e.GetPosition(this)));
        if (found is not { } id) { if (properties.IsLeftButtonPressed) { SelectedGuides.Clear(); InvalidateOverlay(); } return false; }
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift) && !SelectedGuides.Contains(id)) SelectedGuides.Clear();
        SelectedGuides.Add(id); InvalidateOverlay();
        if (properties.IsLeftButtonPressed && e.ClickCount == 2) { GuideSettingsRequested?.Invoke(); e.Handled = true; return true; }
        if (properties.IsLeftButtonPressed && !LocksGuides && !PanEnabled && !IsPanning)
        { _guideDrag = id; GuideDragStarted?.Invoke(); e.Pointer.Capture(this); e.Handled = true; return true; }
        return false;
    }
    private bool UpdateGuideCursor()
    {
        if (PanEnabled || IsPanning || !ShowsGuides || LocksGuides || _document?.Guides.FirstOrDefault(g => g.ID == (_guideDrag ?? HoveredGuide)) is not { } guide) return false;
        ActiveCursorKind = guide.Axis == GuideAxis.Vertical ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth;
        if (!GuideCursors.TryGetValue(guide.Axis, out var entry))
        {
            using var bitmap = new SKBitmap(32, 32); bitmap.Erase(SKColors.Transparent);
            using var canvas = new SKCanvas(bitmap);
            if (guide.Axis == GuideAxis.Horizontal) { canvas.Translate(16,16); canvas.RotateDegrees(90); canvas.Translate(-16,-16); }
            using var path = SKPath.ParseSvgPathData("M13 3V29 M19 3V29 M2 16H11 M2 16L7 11 M2 16L7 21 M21 16H30 M30 16L25 11 M30 16L25 21");
            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 5, Color = SKColors.White, StrokeJoin = SKStrokeJoin.Round };
            canvas.DrawPath(path, paint); paint.Color = SKColors.Black; paint.StrokeWidth = 2; canvas.DrawPath(path, paint);
            var image = ToImage(bitmap); entry = (new Cursor(image, new PixelPoint(16,16)), image); GuideCursors[guide.Axis] = entry;
        }
        Cursor = entry.Cursor; InvalidateOverlay(); return true;
    }
}
