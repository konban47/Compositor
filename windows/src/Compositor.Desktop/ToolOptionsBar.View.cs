using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
namespace Compositor.Desktop;
internal sealed partial class ToolOptionsBar
{
    internal event Action<double>? ViewAngleRequested;
    private void BuildViewOptions()
    {
        OptionCheck("viewPan", "Scroll All Windows", () => _options.PanAll, v => _options.PanAll = v);
        PaintingButton("viewPan", "100%"); PaintingButton("viewPan", "Fit Screen"); PaintingButton("viewPan", "Fill Screen");
        OptionNumber("viewRotate", "Rotation Angle:", () => _options.ViewAngle, n => { _options.ViewAngle = n; ViewAngleRequested?.Invoke(n); }, -180, 180, 65);
        var dial = new ViewRotationDial(() => _options.ViewAngle, n => ViewAngleRequested?.Invoke(n)) { Width = 28, Height = 28 }; Cell("viewRotate", dial); _optionRefreshers.Add(dial.InvalidateVisual);
        PaintingButton("viewRotate", "Reset View"); OptionCheck("viewRotate", "Rotate All Windows", () => _options.RotateAll, v => _options.RotateAll = v);
    }
}
internal sealed class ViewRotationDial(Func<double> get, Action<double> set) : Control
{
    private bool _dragging;
    public override void Render(DrawingContext context)
    { var center = new Point(Bounds.Width / 2, Bounds.Height / 2); var radius = Math.Min(Bounds.Width, Bounds.Height) / 2 - 2; var angle = (get() - 90) * Math.PI / 180; var pen = new Pen(Skin.LabelBrush, 1); context.DrawEllipse(null, pen, center, radius, radius); context.DrawLine(pen, center, center + new Vector(Math.Cos(angle) * radius, Math.Sin(angle) * radius)); context.DrawEllipse(Skin.LabelBrush, null, center, 2, 2); }
    private void Move(Point p) { set((Math.Atan2(p.Y - Bounds.Height / 2, p.X - Bounds.Width / 2) * 180 / Math.PI + 450) % 360); InvalidateVisual(); }
    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e) { _dragging = true; e.Pointer.Capture(this); Move(e.GetPosition(this)); e.Handled = true; }
    protected override void OnPointerMoved(Avalonia.Input.PointerEventArgs e) { if (_dragging) Move(e.GetPosition(this)); }
    protected override void OnPointerReleased(Avalonia.Input.PointerReleasedEventArgs e) { _dragging = false; e.Pointer.Capture(null); }
}
