using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class CanvasView
{
    private Point? _toolPointer;
    private bool _sourceModifier;
    private static readonly Dictionary<StandardCursorType, Cursor> ToolCursors = new();
    internal StandardCursorType ActiveCursorKind { get; private set; }
    internal bool BrushOutlineEnabled { get; set; }
    internal SKPoint? CloneSource { get; set; }
    internal SKPointI? CloneOffset { get; set; }
    internal double OutlineDiameter => Brush.Diameter * Zoom;
    internal bool SourceTargetShowing => _toolPointer is not null && SampleSourceOnClick && _sourceModifier;
    internal bool BrushOutlineShowing => _toolPointer is not null && BrushOutlineEnabled && !SourceTargetShowing && !PanEnabled && !IsPanning;
    internal void UpdateToolCursor()
    {
        ActiveCursorKind = IsPanning ? StandardCursorType.SizeAll : PanEnabled ? StandardCursorType.Hand
            : TypeOnClick ? StandardCursorType.Ibeam : BrushOutlineEnabled ? StandardCursorType.None : StandardCursorType.Arrow;
        if (!ToolCursors.TryGetValue(ActiveCursorKind, out var cursor)) ToolCursors[ActiveCursorKind] = cursor = new Cursor(ActiveCursorKind);
        Cursor = cursor;
        InvalidateVisual();
    }
    private void TrackToolPointer(PointerEventArgs e)
    { _toolPointer = e.GetPosition(this); _sourceModifier = e.KeyModifiers.HasFlag(KeyModifiers.Alt); UpdateToolCursor(); }
    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); TrackToolPointer(e); }
    protected override void OnKeyUp(KeyEventArgs e)
    { _sourceModifier = e.Key is not (Key.LeftAlt or Key.RightAlt) && e.KeyModifiers.HasFlag(KeyModifiers.Alt); UpdateToolCursor(); base.OnKeyUp(e); }
    protected override void OnLostFocus(FocusChangedEventArgs e)
    { _sourceModifier = false; UpdateToolCursor(); base.OnLostFocus(e); }
    private void DrawToolCursor(DrawingContext context)
    {
        if (_toolPointer is not { } pointer || _document is null || PanEnabled || IsPanning) return;
        void Target(Point point, bool ring)
        {
            foreach (var pen in new[] { new Pen(Brushes.Black, 3), new Pen(Brushes.White, 1) })
            {
                context.DrawLine(pen, point - new Vector(9, 0), point + new Vector(9, 0));
                context.DrawLine(pen, point - new Vector(0, 9), point + new Vector(0, 9));
                if (ring) context.DrawEllipse(null, pen, point, 5, 5);
            }
        }
        if (SourceTargetShowing) Target(pointer, true);
        else if (BrushOutlineShowing)
        {
            var radius = Math.Max(.5, OutlineDiameter / 2);
            context.DrawEllipse(null, new Pen(Brushes.Black, 3), pointer, radius, radius);
            context.DrawEllipse(null, new Pen(Brushes.White, 1), pointer, radius, radius);
        }
        if (SampleSourceOnClick && CloneSource is { } source)
        {
            var offset = Brush.CloneAligned ? CloneOffset : null;
            if (_painting && _stroke.Count > 0) offset ??= new SKPointI((int)(source.X - _stroke[0].X), (int)(source.Y - _stroke[0].Y));
            var from = offset is { } delta && !_sourceModifier ? ToDocument(pointer) + new SKPoint(delta.X, delta.Y) : source;
            Target(ToScreen(from), false);
        }
    }
}
