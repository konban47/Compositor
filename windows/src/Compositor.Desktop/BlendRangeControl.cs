using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Compositor.Core.Format;

namespace Compositor.Desktop;

/// <summary>Four keyboard-accessible values plus Photoshop-style Alt-split gradient handles.</summary>
internal sealed class BlendRangeControl : Control
{
    private readonly BlendRange _range;
    private readonly Action _changed;
    private int _handle = -1;
    private bool _split;
    internal BlendRangeControl(BlendRange range, Action changed)
    {
        _range = range; _changed = changed; Height = 58; MinWidth = 200; Focusable = true;
        ToolTip.SetTip(this, Localize.Text("Drag black or white handles. Alt-drag splits a handle. Arrow keys adjust; Tab changes the active handle."));
    }
    private double[] Values => [_range.Black, _range.BlackSplit, _range.WhiteSplit, _range.White];
    public override void Render(DrawingContext context)
    {
        var width = Math.Max(1, Bounds.Width - 16);
        var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative), GradientStops = [new GradientStop(Colors.Black, 0), new GradientStop(Colors.White, 1)] };
        context.DrawRectangle(brush, null, new Rect(8, 23, width, 16));
        var values = Values;
        var text = new FormattedText($"{values[0]:0} / {values[1]:0}                     {values[2]:0} / {values[3]:0}", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Skin.LabelBrush);
        context.DrawText(text, new Point(8, 1));
        for (var i = 0; i < 4; i++)
        {
            var x = 8 + values[i] / 255 * width;
            var geo = new StreamGeometry(); using (var path = geo.Open())
            { path.BeginFigure(new Point(x, 39), true); path.LineTo(new Point(x + (i % 2 == 0 ? -6 : 6), 51)); path.LineTo(new Point(x, 51)); path.EndFigure(true); }
            context.DrawGeometry(i < 2 ? Brushes.Black : Brushes.White, new Pen(IsFocused && i == _handle ? Skin.AccentBrush : Skin.SecondaryBrush, 1), geo);
        }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus(); var x = Math.Clamp((e.GetPosition(this).X - 8) / Math.Max(1, Bounds.Width - 16) * 255, 0, 255);
        var values = Values; _split = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        _handle = Enumerable.Range(0, 4).OrderBy(i => Math.Abs(values[i] - x)).First();
        if (_split && _handle == 0 && _range.Black == _range.BlackSplit) _handle = 1;
        if (_split && _handle == 3 && _range.White == _range.WhiteSplit) _handle = 2;
        e.Pointer.Capture(this); Move(x); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    { if (ReferenceEquals(e.Pointer.Captured, this)) Move((e.GetPosition(this).X - 8) / Math.Max(1, Bounds.Width - 16) * 255); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { e.Pointer.Capture(null); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right)
        { if (_handle < 0) _handle = 0; _split = e.KeyModifiers.HasFlag(KeyModifiers.Alt); Move(Values[_handle] + (e.Key == Key.Left ? -1 : 1) * (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1)); e.Handled = true; }
        else if (e.Key == Key.Tab && _handle < 3 && !e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { _handle++; InvalidateVisual(); e.Handled = true; }
        base.OnKeyDown(e);
    }
    private void Move(double value)
    {
        value = Math.Round(Math.Clamp(value, 0, 255));
        if (!_split && _handle < 2 && _range.Black == _range.BlackSplit) _range.Black = _range.BlackSplit = Math.Min(value, _range.WhiteSplit);
        else if (!_split && _handle >= 2 && _range.White == _range.WhiteSplit) _range.White = _range.WhiteSplit = Math.Max(value, _range.BlackSplit);
        else switch (_handle)
        {
            case 0: _range.Black = Math.Min(value, _range.BlackSplit); break;
            case 1: _range.BlackSplit = Math.Clamp(value, _range.Black, _range.WhiteSplit); break;
            case 2: _range.WhiteSplit = Math.Clamp(value, _range.BlackSplit, _range.White); break;
            case 3: _range.White = Math.Max(value, _range.WhiteSplit); break;
        }
        _changed(); InvalidateVisual();
    }
}
