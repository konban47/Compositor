using SkiaSharp;
namespace Compositor.Core.Document;

public readonly record struct BrushDab(SKPoint At, double Radius, double Angle, double Roundness, double Opacity, double Flow, SKColor Color, double Pressure, int Index, bool FlipX, bool FlipY, double Distance, PenSample Sample, double Direction, double InitialDirection);

public sealed class PaintStrokeLimitException(string message) : InvalidOperationException(message);

public static class BrushDabs
{
    public static readonly string[] TipNames = ["Soft Round", "Hard Round", "Star", "Speckle", "Leaves", "Chalk", "Grain", "Oval", "Ink", "Cloud", "Dry Bristles", "Stipple", "Flat", "Rake", "Grass", "Sparse", "Charcoal", "Spatter", "Snow", "Circle", "Crosshatch", "Dots", "Soft Square", "Fan"];
    public static double Random(uint seed, int index, int channel)
    { uint n = seed ^ unchecked((uint)index * 747796405u + (uint)channel * 2891336453u); n = (n ^ n >> 16) * 2246822519u; n = (n ^ n >> 13) * 3266489917u; return (n ^ n >> 16) / (double)uint.MaxValue; }
    public static double Control(BrushControl control, PenSample p, int index, int fade, double direction, double initial) => control switch
    {
        BrushControl.Fade => Math.Max(0, 1 - index / (double)Math.Max(1, fade)), BrushControl.PenPressure => p.Pressure,
        BrushControl.PenTilt => Math.Clamp(Math.Sqrt(p.TiltX * p.TiltX + p.TiltY * p.TiltY) / 90, 0, 1), BrushControl.StylusWheel => p.Wheel,
        BrushControl.Rotation => (p.Rotation % 360 + 360) % 360 / 360, BrushControl.Direction => (direction + 360) % 360 / 360,
        BrushControl.InitialDirection => (initial + 360) % 360 / 360, _ => 1
    };
    public static IEnumerable<BrushDab> Generate(IReadOnlyList<SKPoint> points, BrushSettings brush)
    {
        if (points.Count == 0) yield break;
        var o = brush.Painting ?? new(); var seed = brush.Seed; var index = 0; double distance = 0, next = 0;
        var initial = points.Count > 1 ? Math.Atan2(points[1].Y - points[0].Y, points[1].X - points[0].X) * 180 / Math.PI : 0;
        PenSample Sample(int i) { var p = o.Samples is { } ps && i < ps.Count ? ps[i] : new PenSample(brush.Pressures is { } pressure && i < pressure.Count ? pressure[i] : 1); p = new PenSample(float.IsFinite(p.Pressure) ? Math.Clamp(p.Pressure, 0, 1) : 1, float.IsFinite(p.TiltX) ? Math.Clamp(p.TiltX, -90, 90) : 0, float.IsFinite(p.TiltY) ? Math.Clamp(p.TiltY, -90, 90) : 0, float.IsFinite(p.Rotation) ? p.Rotation % 360 : 0, float.IsFinite(p.Wheel) ? Math.Clamp(p.Wheel, 0, 1) : 1); return new PenSample((float)Math.Clamp(o.PoseEnabled && o.OverridePressure ? o.Pressure : p.Pressure, 0, 1), (float)(o.PoseEnabled && o.OverrideTiltX ? o.TiltX : p.TiltX), (float)(o.PoseEnabled && o.OverrideTiltY ? o.TiltY : p.TiltY), (float)(o.PoseEnabled && o.OverrideRotation ? o.Rotation : p.Rotation), p.Wheel); }
        for (var segment = 0; segment < points.Count; segment++)
        {
            var a = segment == 0 ? points[0] : points[segment - 1]; var b = points[segment]; var length = SKPoint.Distance(a, b);
            var angle = length > 0 ? Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI : initial;
            var from = Sample(Math.Max(0, segment - 1)); var to = Sample(segment);
            if (segment > 0 && length == 0) { if (!(o.Airbrush || o.BuildUp)) continue; next = 0; }
            while (next <= length)
            {
                if (index >= 100000) throw new PaintStrokeLimitException("The stroke contains too many dabs. Increase brush spacing or shorten the stroke.");
                var t = length > 0 ? next / length : 0; var p = new PenSample((float)(from.Pressure + (to.Pressure - from.Pressure) * t), (float)(from.TiltX + (to.TiltX - from.TiltX) * t), (float)(from.TiltY + (to.TiltY - from.TiltY) * t), (float)(from.Rotation + (to.Rotation - from.Rotation) * t), (float)(from.Wheel + (to.Wheel - from.Wheel) * t));
                double C(BrushControl c) => Control(c, p, index, o.FadeSteps, angle, initial);
                double J(double jitter, int salt) => 1 - jitter * Random(seed, index, salt);
                double shape = o.ShapeEnabled ? Math.Max(o.MinimumDiameter, C(o.SizeControl) * J(o.SizeJitter, 1)) : 1;
                if (brush.PressureSize) shape *= p.Pressure;
                if (o.ShapeEnabled && o.SizeControl == BrushControl.PenTilt) shape *= o.TiltScale;
                var diameter = Math.Max(1, brush.Diameter * shape);
                var tipAngle = brush.Angle + (o.ShapeEnabled ? (Random(seed, index, 2) * 2 - 1) * o.AngleJitter * 180 + (o.AngleControl == BrushControl.Off ? 0 : C(o.AngleControl) * 360) : 0);
                var roundness = brush.Roundness * (o.ShapeEnabled ? Math.Max(o.MinimumRoundness, C(o.RoundnessControl) * J(o.RoundnessJitter, 3)) : 1);
                if (o.ShapeEnabled && o.Projection || o.PoseEnabled) { var tilt = Math.Sqrt(p.TiltX * p.TiltX + p.TiltY * p.TiltY); roundness *= Math.Max(.05, Math.Cos(Math.Min(89, tilt) * Math.PI / 180)); if (tilt > 0) tipAngle += Math.Atan2(p.TiltY, p.TiltX) * 180 / Math.PI; tipAngle += p.Rotation; }
                var opacity = (brush.PressureOpacity ? p.Pressure : 1) * (o.TransferEnabled ? Math.Max(o.MinimumOpacity, C(o.OpacityControl) * J(o.OpacityJitter, 4)) : 1);
                var flow = (brush.Mode == BrushMode.Mixer ? brush.MixerFlow : o.Pencil ? 1 : o.Flow) * (o.TransferEnabled ? Math.Max(o.MinimumFlow, C(o.FlowControl) * J(o.FlowJitter, 5)) : 1);
                var count = o.ScatterEnabled ? Math.Max(1, (int)Math.Round(o.Count * C(o.CountControl) * J(o.CountJitter, 6))) : 1;
                var center = new SKPoint((float)(a.X + (b.X - a.X) * t), (float)(a.Y + (b.Y - a.Y) * t));
                for (var n = 0; n < count; n++)
                {
                    var amount = o.ScatterEnabled ? o.Scatter * diameter * C(o.ScatterControl) : 0;
                    var across = (Random(seed, index, 10 + n * 2) * 2 - 1) * amount; var along = o.ScatterBothAxes ? (Random(seed, index, 11 + n * 2) * 2 - 1) * amount : 0;
                    var radians = angle * Math.PI / 180; var at = center + new SKPoint((float)(along * Math.Cos(radians) - across * Math.Sin(radians)), (float)(along * Math.Sin(radians) + across * Math.Cos(radians)));
                    if (o.Pencil) at = new SKPoint(MathF.Floor(at.X) + .5f, MathF.Floor(at.Y) + .5f);
                    var color = Color(brush, o, o.ColorEachTip ? index : 0, C(o.ColorControl));
                    yield return new BrushDab(at, diameter / 2, tipAngle, Math.Clamp(roundness, .01, 1), opacity, flow, color, p.Pressure, index, o.FlipX ^ (o.ShapeEnabled && o.FlipXJitter && Random(seed, index, 7) > .5), o.FlipY ^ (o.ShapeEnabled && o.FlipYJitter && Random(seed, index, 8) > .5), distance + next, p, angle, initial);
                }
                index++;
                next += Math.Max(.25, diameter * (o.SpacingEnabled ? brush.SpacingFraction > 0 ? brush.SpacingFraction : .25 : .015));
                if (length == 0) break;
            }
            next -= length; distance += length;
        }
    }
    private static SKColor Color(BrushSettings brush, PaintBrushOptions o, int index, double control)
    {
        var foreground = new SKColor((byte)Math.Clamp(brush.Red * 255, 0, 255), (byte)Math.Clamp(brush.Green * 255, 0, 255), (byte)Math.Clamp(brush.Blue * 255, 0, 255));
        if (!o.ColorEnabled) return foreground;
        var color = RetouchBrush.Mix(foreground, new SKColor(o.Background), Math.Clamp((o.ColorControl == BrushControl.Off ? Random(brush.Seed, index, 40) : 1 - control) * o.ForegroundBackground, 0, 1));
        color.ToHsv(out var h, out var s, out var v);
        h = (float)((h + (Random(brush.Seed, index, 41) * 2 - 1) * o.HueJitter * 180 + 360) % 360);
        s = (float)Math.Clamp(s + (Random(brush.Seed, index, 42) * 2 - 1) * o.SaturationJitter * 100 + o.Purity * 100, 0, 100);
        v = (float)Math.Clamp(v + (Random(brush.Seed, index, 43) * 2 - 1) * o.BrightnessJitter * 100, 0, 100);
        return SKColor.FromHsv(h, s, v);
    }
    public static double Tip(int kind, double x, double y, double hardness)
    {
        var r = Math.Sqrt(x * x + y * y); if (Math.Abs(x) > 1 || Math.Abs(y) > 1) return 0;
        var angle = Math.Atan2(y, x); var hash = Random(19, (int)Math.Floor(x * 29) + (int)Math.Floor(y * 29) * 73, kind);
        var edge = kind switch { 2 => .65 + .35 * Math.Cos(angle * 5), 4 => .72 + .25 * Math.Cos(angle * 3), 7 => 1 / Math.Sqrt(x * x + y * y * 4 + .0001) * r, _ => 1 };
        var baseTip = BrushEdits.Tip(r, edge, hardness); if (kind is 22) baseTip = BrushEdits.Tip(Math.Max(Math.Abs(x), Math.Abs(y)), 1, hardness);
        if (kind is 0 or 1 or 2 or 4 or 7 or 19 or 22) return baseTip;
        return baseTip * (kind switch {
            3 or 6 or 11 or 15 or 17 or 18 or 21 => hash > (kind == 15 ? .86 : .5) ? 1 : 0,
            5 or 16 => .25 + hash * .75, 8 => 1 - .4 * Math.Abs(Math.Sin(x * 5 + y * 3)), 9 => .4 + .6 * Math.Sin(x * 7 + Math.Sin(y * 9)) * Math.Sin(y * 7),
            10 or 12 or 13 => Math.Abs(Math.Sin(x * (kind == 13 ? 18 : 30))) > .65 ? 1 : 0,
            14 => Math.Abs(Math.Sin((x + y * y) * 24)) > .8 ? 1 : 0, 20 => Math.Abs(Math.Sin((x + y) * 20)) > .7 ? 1 : 0,
            23 => Math.Abs(Math.Sin(angle * 27)) > .6 && y > -.2 ? 1 : 0, _ => 1 });
    }
}
