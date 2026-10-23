using System.Numerics;
using SkiaSharp;
namespace Compositor.Core.Document;

/// <summary>Straight-alpha paint compositing, including the four nonseparable W3C blend functions.</summary>
public static class BrushBlending
{
    public static SKColor Composite(SKColor b, SKColor s, double amount, PaintBlend mode)
    {
        var v = Composite(new SKColorF(b.Red / 255f, b.Green / 255f, b.Blue / 255f, b.Alpha / 255f),
            new SKColorF(s.Red / 255f, s.Green / 255f, s.Blue / 255f, s.Alpha / 255f), amount, mode);
        return new SKColor(Byte(v.Red * 255), Byte(v.Green * 255), Byte(v.Blue * 255), Byte(v.Alpha * 255));
    }
    public static SKColorF Composite(SKColorF b, SKColorF s, double amount, PaintBlend mode)
    {
        var sa = (float)Math.Clamp(amount * s.Alpha, 0, 1); var ba = b.Alpha;
        if (mode == PaintBlend.Clear) return new SKColorF(b.Red, b.Green, b.Blue, ba * (1 - sa));
        if (mode == PaintBlend.Behind) { sa *= 1 - ba; var a = ba + sa; return a <= 0 ? b : new SKColorF((b.Red * ba + s.Red * sa) / a, (b.Green * ba + s.Green * sa) / a, (b.Blue * ba + s.Blue * sa) / a, a); }
        var alpha = sa + ba * (1 - sa); if (alpha <= 0) return b;
        var back = new Vector3(b.Red, b.Green, b.Blue); var source = new Vector3(s.Red, s.Green, s.Blue);
        var blend = Color(back, source, mode);
        var result = ((1 - sa) * ba * back + (1 - ba) * sa * source + sa * ba * blend) / alpha;
        return new SKColorF(result.X, result.Y, result.Z, alpha);
    }
    public static Vector3 Color(Vector3 b, Vector3 s, PaintBlend mode)
    {
        if (mode is PaintBlend.Normal or PaintBlend.Dissolve or PaintBlend.Behind or PaintBlend.Clear) return s;
        if (mode == PaintBlend.Hue) return SetLum(SetSat(s, Sat(b)), Lum(b));
        if (mode == PaintBlend.Saturation) return SetLum(SetSat(b, Sat(s)), Lum(b));
        if (mode == PaintBlend.Color) return SetLum(s, Lum(b));
        if (mode == PaintBlend.Luminosity) return SetLum(b, Lum(s));
        if (mode == PaintBlend.DarkerColor) return Lum(b) <= Lum(s) ? b : s;
        if (mode == PaintBlend.LighterColor) return Lum(b) >= Lum(s) ? b : s;
        return new Vector3(Channel(b.X, s.X, mode), Channel(b.Y, s.Y, mode), Channel(b.Z, s.Z, mode));
    }
    public static float Channel(float b, float s, PaintBlend mode) => Math.Clamp(mode switch
    {
        PaintBlend.Darken => Math.Min(b, s), PaintBlend.Multiply => b * s, PaintBlend.ColorBurn => Burn(b, s), PaintBlend.LinearBurn => b + s - 1,
        PaintBlend.Lighten => Math.Max(b, s), PaintBlend.Screen => b + s - b * s, PaintBlend.ColorDodge => Dodge(b, s), PaintBlend.LinearDodgeAdd => b + s,
        PaintBlend.Overlay => b <= .5f ? 2 * b * s : 1 - 2 * (1 - b) * (1 - s),
        PaintBlend.SoftLight => s <= .5f ? b - (1 - 2 * s) * b * (1 - b) : b + (2 * s - 1) * ((b <= .25f ? ((16 * b - 12) * b + 4) * b : MathF.Sqrt(b)) - b),
        PaintBlend.HardLight => s <= .5f ? 2 * b * s : 1 - 2 * (1 - b) * (1 - s),
        PaintBlend.VividLight => s <= .5f ? Burn(b, 2 * s) : Dodge(b, 2 * s - 1), PaintBlend.LinearLight => b + 2 * s - 1,
        PaintBlend.PinLight => s <= .5f ? Math.Min(b, 2 * s) : Math.Max(b, 2 * s - 1), PaintBlend.HardMix => b + s < 1 ? 0 : 1,
        PaintBlend.Difference => Math.Abs(b - s), PaintBlend.Exclusion => b + s - 2 * b * s, PaintBlend.Subtract => b - s, PaintBlend.Divide => s <= 0 ? 1 : b / s, _ => s
    }, 0, 1);
    private static float Burn(float b, float s) => b >= 1 ? 1 : s <= 0 ? 0 : 1 - Math.Min(1, (1 - b) / s);
    private static float Dodge(float b, float s) => b <= 0 ? 0 : s >= 1 ? 1 : Math.Min(1, b / (1 - s));
    private static float Lum(Vector3 v) => v.X * .3f + v.Y * .59f + v.Z * .11f;
    private static float Sat(Vector3 v) => Math.Max(v.X, Math.Max(v.Y, v.Z)) - Math.Min(v.X, Math.Min(v.Y, v.Z));
    private static Vector3 SetSat(Vector3 v, float s) { var lo = Math.Min(v.X, Math.Min(v.Y, v.Z)); var delta = Sat(v); return delta <= 0 ? Vector3.Zero : (v - new Vector3(lo)) * (s / delta); }
    private static Vector3 SetLum(Vector3 v, float lum)
    {
        v += new Vector3(lum - Lum(v)); var lo = Math.Min(v.X, Math.Min(v.Y, v.Z)); var hi = Math.Max(v.X, Math.Max(v.Y, v.Z));
        if (lo < 0) v = new Vector3(lum) + (v - new Vector3(lum)) * (lum / (lum - lo));
        if (hi > 1) v = new Vector3(lum) + (v - new Vector3(lum)) * ((1 - lum) / (hi - lum));
        return Vector3.Clamp(v, Vector3.Zero, Vector3.One);
    }
    private static byte Byte(double n) => (byte)Math.Clamp(Math.Round(n), 0, 255);
}
