using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Compositor.Core.Format;
using Compositor.Core.Pixels;

namespace Compositor.Desktop;

/// <summary>
/// The curve a channel is bent by, drawn from the operator's own maths so what is shown is what the pixels
/// get. Handles are dragged, the line is clicked to put a new one on it, and a handle is right-clicked to
/// take it off.
/// </summary>
internal sealed class CurveEditor : Control
{
    /// <summary>How near a handle a click has to be, in screen pixels, to take hold of it.</summary>
    private const double Grab = 9;

    /// <summary>The graph's own colours, as the Mac's CurvesControls draws them: black at 35% behind the plot,
    /// a grid of white at 12%, a white curve, and handles that are white until one is taken hold of.</summary>
    private static readonly IBrush Backdrop = Skin.CurveGround;

    private static readonly Pen GridPen = new() { Brush = Skin.CurveGrid, Thickness = 1 };
    private static readonly Pen DiagonalPen = new() { Brush = Skin.CurveGrid, Thickness = 1 };
    private static readonly Pen CurvePen = new() { Brush = Brushes.White, Thickness = 2 };
    private static readonly IBrush HandleBrush = Brushes.White;
    private static readonly IBrush ChosenBrush = Skin.AccentBrush;

    private CurvesSettings _curves = new();
    private int _channel;
    private int _held = -1;

    public CurveEditor()
    {
        Focusable = true;
    }

    /// <summary>Raised after a handle has been moved, added or taken away.</summary>
    public event Action? Changed;

    /// <summary>The curves being edited, held by the panel: a fresh copy each time it is set.</summary>
    public CurvesSettings Curves
    {
        get => _curves;
        set
        {
            _curves = value;
            _held = -1;
            InvalidateVisual();
        }
    }

    /// <summary>Which channel is shown and edited: 0 is the master, then red, green and blue.</summary>
    public int Channel
    {
        get => _channel;
        set
        {
            _channel = Math.Clamp(value, 0, 3);
            _held = -1;
            InvalidateVisual();
        }
    }

    /// <summary>Photoshop's Curve Display Options: grid of 4 or 10 divisions, the histogram behind the curve, the other
    /// channels' curves, the diagonal baseline, and the intersection lines while a handle is dragged.</summary>
    internal int Divisions { get; set { field = value; InvalidateVisual(); } } = 4;
    internal double[]? Histogram { get; set { field = value; InvalidateVisual(); } }
    internal bool ShowHistogram { get; set { field = value; InvalidateVisual(); } } = true;
    internal bool ShowOverlays { get; set { field = value; InvalidateVisual(); } } = true;
    internal bool ShowBaseline { get; set { field = value; InvalidateVisual(); } } = true;
    internal bool ShowIntersection { get; set { field = value; InvalidateVisual(); } } = true;
    /// <summary>Pencil: dragging draws the curve freehand, laid down as handles along the stroke.</summary>
    internal bool Pencil { get; set; }

    public override void Render(DrawingContext context)
    {
        var box = new Rect(Bounds.Size);
        context.FillRectangle(Backdrop, box);
        if (ShowHistogram && Histogram is { Length: 256 } bins)
        {
            var peak = Math.Max(1e-9, bins.OrderByDescending(v => v).Skip(2).FirstOrDefault());
            var bar = new Pen(new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x4A)), Math.Max(1, Bounds.Width / 256 + .5));
            for (var i = 0; i < 256; i++)
            {
                var h = Math.Min(1, bins[i] / peak) * Bounds.Height * .9; if (h <= 0) continue;
                var x = i / 255.0 * Bounds.Width; context.DrawLine(bar, new Point(x, Bounds.Height), new Point(x, Bounds.Height - h));
            }
        }
        // The input runs left to right and the output upwards, as Photoshop draws a curve.
        for (var step = 1; step < Divisions; step++)
        {
            var at = Bounds.Width * step / Divisions;
            context.DrawLine(GridPen, new Point(at, 0), new Point(at, Bounds.Height));
            var down = Bounds.Height * step / Divisions;
            context.DrawLine(GridPen, new Point(0, down), new Point(Bounds.Width, down));
        }
        if (ShowBaseline) context.DrawLine(DiagonalPen, new Point(0, Bounds.Height), new Point(Bounds.Width, 0));
        if (ShowOverlays && _channel == 0)
        {
            // The color channels' own curves, in their colors, behind the composite's.
            var inks = new[] { Colors.Red, Colors.Lime, Colors.DodgerBlue };
            for (var channel = 1; channel <= 3; channel++)
            {
                if (_curves.Channels[channel].Count == 2 && _curves.Channels[channel][0] is { X: 0, Y: 0 } && _curves.Channels[channel][1] is { X: 255, Y: 255 }) continue;
                var pen = new Pen(new SolidColorBrush(inks[channel - 1]), 1);
                for (var input = 1; input <= 255; input++)
                    context.DrawLine(pen, At(input - 1, AdjustmentOperators.CurvesValue(_curves, channel, input - 1)), At(input, AdjustmentOperators.CurvesValue(_curves, channel, input)));
            }
        }

        var points = _curves.Channels[_channel];
        if (ShowIntersection && _held >= 0 && _held < points.Count)
        {
            var at = At(points[_held].X, points[_held].Y); var cross = new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 1);
            context.DrawLine(cross, new Point(at.X, 0), new Point(at.X, Bounds.Height)); context.DrawLine(cross, new Point(0, at.Y), new Point(Bounds.Width, at.Y));
        }
        var line = new List<Point>(256);
        for (var input = 0; input <= 255; input++)
        {
            var output = AdjustmentOperators.CurvesValue(_curves, _channel, input);
            line.Add(At(input, output));
        }
        for (var index = 1; index < line.Count; index++) context.DrawLine(CurvePen, line[index - 1], line[index]);

        for (var index = 0; index < points.Count; index++)
        {
            var at = At(points[index].X, points[index].Y);
            var brush = index == _held ? ChosenBrush : HandleBrush;
            context.FillRectangle(brush, new Rect(at.X - 4, at.Y - 4, 8, 8));
        }
    }

    private bool _drawing;
    private double _lastInput = -1;

    /// <summary>Lays a pencil stroke down: the handles the stroke passes over give way to one where it now is.</summary>
    private void DrawTo(Point at)
    {
        var points = _curves.Channels[_channel];
        var input = Math.Clamp(Math.Round(Input(at)), 0, 255); var output = Math.Clamp(Math.Round(Output(at)), 0, 255);
        var from = _lastInput < 0 ? input : _lastInput; var low = Math.Min(from, input); var high = Math.Max(from, input);
        points.RemoveAll(p => p.X >= low - 3 && p.X <= high + 3 && p.X != input && p.X is > 0 and < 255);
        if (points.FirstOrDefault(p => p.X == input) is { } same) same.Y = output;
        else if (points.Count < 32) { points.Add(new CurvePoint { X = input, Y = output }); points.Sort((l, r) => l.X.CompareTo(r.X)); }
        _lastInput = input; Announce();
    }

    /// <summary>Photoshop's Smooth: each handle between the ends moves toward the average of its neighbors.</summary>
    internal void Smooth()
    {
        var points = _curves.Channels[_channel]; if (points.Count < 3) return;
        var ys = points.Select(p => p.Y).ToArray();
        for (var i = 1; i < points.Count - 1; i++) points[i].Y = Math.Round((ys[i - 1] + ys[i] * 2 + ys[i + 1]) / 4);
        Announce();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var at = e.GetPosition(this);
        if (Pencil && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _drawing = true; _lastInput = -1; e.Pointer.Capture(this); DrawTo(at); return;
        }
        _held = Nearest(at);
        if (_held < 0 && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            // A click on the line itself puts a handle on it, and it is then being dragged.
            var points = _curves.Channels[_channel];
            var input = Math.Round(Input(at), MidpointRounding.AwayFromZero);
            if (input > 0 && input < 255 && points.Count < 32)
            {
                var output = Math.Round(AdjustmentOperators.CurvesValue(_curves, _channel, input), MidpointRounding.AwayFromZero);
                var made = new CurvePoint { X = input, Y = Math.Clamp(output, 0, 255) };
                points.Add(made);
                points.Sort((left, right) => left.X.CompareTo(right.X));
                _held = points.IndexOf(made);
                Announce();
            }
        }
        else if (_held >= 0 && e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            // A handle away from the ends is taken off by right-clicking it.
            var points = _curves.Channels[_channel];
            if (_held != 0 && _held != points.Count - 1)
            {
                points.RemoveAt(_held);
                _held = -1;
                Announce();
            }
        }
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drawing) { DrawTo(e.GetPosition(this)); return; }
        if (_held < 0) return;
        var points = _curves.Channels[_channel];
        var at = e.GetPosition(this);
        var input = Math.Clamp(Math.Round(Input(at), MidpointRounding.AwayFromZero),
            points[_held == 0 ? 0 : _held - 1].X + 1, _held == points.Count - 1 ? 255 : points[_held + 1].X - 1);
        // The ends stay where they are across; only their output moves.
        if (_held != 0 && _held != points.Count - 1) points[_held].X = input;
        points[_held].Y = Math.Clamp(Math.Round(Output(at), MidpointRounding.AwayFromZero), 0, 255);
        Announce();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _held = -1; _drawing = false;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    private void Announce()
    {
        InvalidateVisual();
        Changed?.Invoke();
    }

    /// <summary>The handle within reach of a click, or -1 when the click is on the line rather than a handle.</summary>
    private int Nearest(Point at)
    {
        var points = _curves.Channels[_channel];
        var best = -1;
        var nearest = Grab;
        for (var index = 0; index < points.Count; index++)
        {
            var there = At(points[index].X, points[index].Y);
            var distance = Math.Sqrt((there.X - at.X) * (there.X - at.X) + (there.Y - at.Y) * (there.Y - at.Y));
            if (distance > nearest) continue;
            nearest = distance;
            best = index;
        }
        return best;
    }

    /// <summary>Pigment/Ink %: the graph turned end for end, so ink rises to the right as Photoshop shows it.</summary>
    internal bool Pigment { get; set { field = value; InvalidateVisual(); } }

    private Point At(double input, double output) => Pigment ? new((255 - input) / 255 * Bounds.Width, output / 255 * Bounds.Height) :
        new(input / 255 * Bounds.Width, Bounds.Height - output / 255 * Bounds.Height);

    private double Input(Point at) => Bounds.Width <= 0 ? 0 : (Pigment ? Bounds.Width - at.X : at.X) / Bounds.Width * 255;

    private double Output(Point at) => Bounds.Height <= 0 ? 0 : (Pigment ? at.Y : Bounds.Height - at.Y) / Bounds.Height * 255;
}
