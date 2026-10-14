using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class CanvasView
{
    private Point? _toolPointer;
    internal Point? ToolPointerPosition => _toolPointer;
    internal event Action<Point?>? PointerTracked;
    private bool _sourceModifier;
    private static readonly Dictionary<StandardCursorType, Cursor> ToolCursors = new();
    internal StandardCursorType ActiveCursorKind { get; private set; }
    internal bool PenCursorEnabled { get; set; }
    internal bool PreciseBrushCursor { get; set; }
    internal bool BrushOutlineEnabled { get; set; }
    internal SKPoint? CloneSource { get; set; }
    internal SKPointI? CloneOffset { get; set; }
    internal double OutlineDiameter => Brush.Diameter * Zoom;
    internal bool SourceTargetShowing => _toolPointer is not null && SampleSourceOnClick && _sourceModifier;
    internal bool BrushOutlineShowing => _toolPointer is not null && BrushOutlineEnabled && !SourceTargetShowing && !PanEnabled && !IsPanning;
    internal void UpdateToolCursor()
    {
        if (UpdateGuideCursor()) return;
        if (SetIllustratedCursor()) { InvalidateOverlay(); return; }
        ActiveCursorKind = IsPanning ? StandardCursorType.SizeAll : PanEnabled ? StandardCursorType.Hand
            : TypeOnClick ? StandardCursorType.Ibeam : BrushOutlineEnabled || PenCursorEnabled && EditorPreferences.Current.PenCursor ? StandardCursorType.None : StandardCursorType.Arrow;
        if (!ToolCursors.TryGetValue(ActiveCursorKind, out var cursor)) ToolCursors[ActiveCursorKind] = cursor = new Cursor(ActiveCursorKind);
        Cursor = cursor;
        InvalidateOverlay();
    }
    private void TrackToolPointer(PointerEventArgs e)
    { _toolPointer = e.GetPosition(this); _sourceModifier = e.KeyModifiers.HasFlag(KeyModifiers.Alt); PointerTracked?.Invoke(_toolPointer); UpdateToolCursor(); }
    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); TrackToolPointer(e); }
    protected override void OnKeyUp(KeyEventArgs e)
    { _sourceModifier = e.Key is not (Key.LeftAlt or Key.RightAlt) && e.KeyModifiers.HasFlag(KeyModifiers.Alt); UpdateToolCursor(); base.OnKeyUp(e); }
    protected override void OnLostFocus(FocusChangedEventArgs e)
    { _sourceModifier = false; UpdateToolCursor(); base.OnLostFocus(e); }
    private void DrawToolCursor(DrawingContext context)
    {
        if (HoveredGuide is not null) return;
        if (_painting && EditorPreferences.Current.HidePaintingCursor) return;
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
        if (PenCursorEnabled && EditorPreferences.Current.PenCursor)
        {
            using var translate = context.PushTransform(Matrix.CreateTranslation(pointer.X - 1, pointer.Y - 1));
            context.DrawGeometry(Brushes.White, new Pen(Brushes.Black, 2), StreamGeometry.Parse("M1,1 L5,18 L10,21 L21,10 L18,5 Z M1,1 L12,12 M10,17 L17,10"));
            context.DrawEllipse(Brushes.Black, null, new Point(12, 12), 1.8, 1.8);
            var badge = CursorTool switch { Tool.AddAnchor => "+", Tool.DeleteAnchor => "−", Tool.ConvertAnchor => "∧", _ => "" };
            if (badge.Length > 0)
            {
                context.FillRectangle(Brushes.White, new Rect(20, 16, 12, 14));
                context.DrawText(new FormattedText(badge, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Arial"), 13, Brushes.Black), new Point(21,15));
            }
        }
        if (SourceTargetShowing) Target(pointer, true);
        else if (BrushOutlineShowing)
        {
            if (PreciseBrushCursor) { Target(pointer, false); return; }
            var radius = Math.Max(.5, OutlineDiameter / 2);
            context.DrawEllipse(null, new Pen(Brushes.Black, EditorPreferences.Current.BrushOutlineWidth + 2), pointer, radius, radius);
            context.DrawEllipse(null, new Pen(Brushes.White, EditorPreferences.Current.BrushOutlineWidth), pointer, radius, radius);
            if (EditorPreferences.Current.BrushCrosshair) Target(pointer, false);
        }
        if (SampleSourceOnClick && CloneSource is { } source)
        {
            var offset = Brush.CloneAligned ? CloneOffset : null;
            if (HoveredGuide is not null) return;
        if (_painting && _stroke.Count > 0) offset ??= new SKPointI((int)(source.X - _stroke[0].X), (int)(source.Y - _stroke[0].Y));
            var from = offset is { } delta && !_sourceModifier ? ToDocument(pointer) + new SKPoint(delta.X, delta.Y) : source;
            Target(ToScreen(from), false);
        }
    }
}
