using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Compositor.Core.Document;
namespace Compositor.Desktop;
internal sealed partial class ToolOptionsBar
{
    internal GradientPaintSettings GradientSettings => new() { Shape = _options.Gradient, Method = _options.GradientMethod, Dither = _options.GradientDither, Reverse = _options.GradientReversed,
        Stops = _options.GradientStops ?? [new(0, (uint)new SkiaSharp.SKColor((byte)(_options.Brush.Red * 255), (byte)(_options.Brush.Green * 255), (byte)(_options.Brush.Blue * 255))),
        new(1, _options.GradientToBackground ? (uint)new SkiaSharp.SKColor((byte)(_options.GradientBackground.Red * 255), (byte)(_options.GradientBackground.Green * 255), (byte)(_options.GradientBackground.Blue * 255)) : (uint)new SkiaSharp.SKColor((byte)(_options.Brush.Red * 255), (byte)(_options.Brush.Green * 255), (byte)(_options.Brush.Blue * 255), 0))] };
    private void BuildGradientOptions()
    {
        OptionChoice("fillGradient", "", ["Gradient", "Classic Gradient"], () => _options.GradientClassic ? 1 : 0, n => _options.GradientClassic = n == 1, 135);
        var preview = PaintingButton("fillGradient", "Gradient Editor"); preview.Width = 110; preview.Padding = new Thickness(2);
        _optionRefreshers.Add(() => preview.Content = new GradientStrip(() => GradientSettings) { Width = 100, Height = 22 });
        var drop = PaintingButton("fillGradient", "Gradient Presets"); drop.Content = "⌄"; drop.Width = 22;
        foreach (var shape in Enum.GetValues<GradientShape>())
        {
            var button = PaintingButton("fillGradient", shape + " Gradient"); button.Content = new GradientShapeIcon(shape) { Width = 21, Height = 21 }; button.Width = 30;
            button.Click += (_, _) => { _options.Gradient = shape; Changed?.Invoke(); };
            _optionRefreshers.Add(() => button.Background = _options.Gradient == shape ? Skin.SurfaceDarkBrush : Brushes.Transparent);
        }
        OptionCheck("fillGradient", "Reverse", () => _options.GradientReversed, v => _options.GradientReversed = v);
        OptionCheck("fillGradient", "Dither", () => _options.GradientDither, v => _options.GradientDither = v);
        OptionChoice("fillGradient", "Method:", ["Perceptual", "Linear", "Classic", "Smooth", "Stripes"], () => (int)_options.GradientMethod, n => _options.GradientMethod = (GradientMethod)n, 110);
        OptionChoice("fillGradientClassic", "Mode:", PaintModeNames, () => (int)PaintOptions.Blend, i => SetPaint(p => p with { Blend = (PaintBlend)i }), 130);
        PaintSlider("fillGradientClassic", "Opacity:", "Paint Opacity Slider", () => _options.Brush.Opacity * 100, n => _options.Brush = _options.Brush with { Opacity = n / 100 });
    }
}
internal sealed class GradientStrip(Func<GradientPaintSettings> get) : Control
{
    public override void Render(DrawingContext context)
    {
        var settings = get() with { Dither = false }; var stops = new GradientStops();
        for(var y=0;y<Bounds.Height;y+=8) for(var x=0;x<Bounds.Width;x+=8) context.FillRectangle((x/8+y/8)%2==0 ? Brushes.LightGray : Brushes.Gray,new Rect(x,y,Math.Min(8,Bounds.Width-x),Math.Min(8,Bounds.Height-y)));
        for(var i=0;i<=80;i++) { var c=settings.Sample(i/80d); stops.Add(new GradientStop(Color.FromArgb((byte)Math.Clamp(c.Alpha*255,0,255),(byte)Math.Clamp(c.Red*255,0,255),(byte)Math.Clamp(c.Green*255,0,255),(byte)Math.Clamp(c.Blue*255,0,255)),i/80d)); }
        context.FillRectangle(new LinearGradientBrush { StartPoint=new RelativePoint(0,.5,RelativeUnit.Relative), EndPoint=new RelativePoint(1,.5,RelativeUnit.Relative), GradientStops=stops }, new Rect(Bounds.Size));
    }
}
internal sealed class GradientShapeIcon(GradientShape shape) : Control
{
    public override void Render(DrawingContext context)
    {
        const int n = 16; var start = shape == GradientShape.Linear ? new SkiaSharp.SKPoint(0, 8) : new SkiaSharp.SKPoint(8, 8); var end = new SkiaSharp.SKPoint(16, 8);
        for (var y = 0; y < n; y++) for (var x = 0; x < n; x++)
        { var value = (byte)(GradientEdits.Parameter(start, end, new(x, y), shape) * 255); context.FillRectangle(new SolidColorBrush(Color.FromRgb(value, value, value)), new Rect(x * Bounds.Width / n, y * Bounds.Height / n, Bounds.Width / n + .2, Bounds.Height / n + .2)); }
        context.DrawRectangle(null, new Pen(Skin.LabelBrush, 1), new Rect(Bounds.Size).Deflate(.5));
    }
}
