namespace Compositor.Core.IO;

public static class ContourLibrary
{
    public static bool Valid(double[]? samples) => samples is { Length: >= 2 and <= 256 } && samples.All(v => double.IsFinite(v) && v is >= 0 and <= 1);
    public static double Evaluate(double[] samples, double value)
    {
        var position = Math.Clamp(value, 0, 1) * (samples.Length - 1); var left = (int)position;
        return samples[left] + (samples[Math.Min(left + 1, samples.Length - 1)] - samples[left]) * (position - left);
    }
    public static EditorPreset[] Defaults()
    {
        (string Name, Func<double, double> Curve)[] curves = [
            ("Linear", t => t), ("Cone", t => 1 - Math.Abs(t * 2 - 1)), ("Inverted Cone", t => Math.Abs(t * 2 - 1)),
            ("Cove Deep", t => Math.Pow(t, 3)), ("Cove Shallow", t => Math.Sqrt(t)),
            ("Gaussian", t => Math.Exp(-Math.Pow((t - 1) * 3, 2))), ("Half Round", t => Math.Sqrt(1 - Math.Pow(t - 1, 2))),
            ("Ring", t => Math.Pow(Math.Sin(t * Math.PI), 2)), ("Double Ring", t => Math.Pow(Math.Sin(t * Math.PI * 2), 2)),
            ("Rolling Slope", t => Math.Clamp(t + Math.Sin(t * Math.PI * 3) * .2, 0, 1)),
            ("Rounded Steps", t => Math.Round(t * 4) / 4), ("Sawtooth", t => 1 - Math.Abs((t * 3 % 1) * 2 - 1)) ];
        return curves.Select((c, i) => new EditorPreset(new Guid(i + 1, 405, 29, new byte[8]), c.Name, "Contour", Contour: Enumerable.Range(0, 65).Select(n => c.Curve(n / 64d)).ToArray())).ToArray();
    }
    public static string DefaultPath => Path.Combine(AppPaths.SettingsDirectory, "contours.json");
    public static IReadOnlyList<EditorPreset> Load()
    {
        var presets = EditorPresets.Load(DefaultPath).Where(p => p.Kind == "Contour").ToArray();
        return File.Exists(DefaultPath) ? presets : Defaults();
    }
}
