using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class CanvasView
{
    private bool _customDragging;
    internal Func<SKPoint, KeyModifiers, int, bool>? CustomPressed;
    internal Action<SKPoint, KeyModifiers>? CustomMoved;
    internal Action? CustomReleased;
    internal Action<DrawingContext, Func<SKPoint, Point>>? EditingOverlay;
    internal SKPoint ContextPoint;
}
