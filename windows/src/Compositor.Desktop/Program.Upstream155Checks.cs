using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.Format;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    internal void Upstream155SelfCheck(string output)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        AdoptImported(LayerPlacement.NewDocument(120, 80)!, "Upstream checks");
        Dispatcher.UIThread.RunJobs(); UpdateLayout();
        Check(_canvas.Zoom <= 1, "Opening a small document enlarged it above 100%.");
        SetTool(Tool.Brush); _optionsBar.EnablePenPressure();
        var preset = new ToolOptions { Brush = new BrushSettings(PressureSize: true, PressureOpacity: true), ShapeLineStyle = "Dotted", ShapeLineCap = "Round" };
        ToolPresetState.CopyOptions(preset, _options); OptionsChanged();
        _optionsBar.Show(_tool, true, false);
        var controls = _optionsBar.GetVisualDescendants().OfType<CheckBox>().Where(c => Equals(c.Content, Localize.Text("Pressure Size")) || Equals(c.Content, Localize.Text("Pressure Opacity"))).ToArray();
        Check(controls.Length == 2 && controls.All(c => c.IsChecked == true), "Pressure controls did not refresh from a preset.");
        controls[0].IsChecked = false;
        Check(!_options.Brush.PressureSize && preset.Brush.PressureSize, "Pressure toggle did not update the active brush independently.");
        SetTool(Tool.Line); _optionsBar.Show(_tool, true, false);
        Check(_optionsBar.Shows("lineStyle"), "Line pattern/cap options are hidden.");
        var (style, _) = ShapePlan(SkiaSharp.SKRectI.Create(10, 10, 50, 1));
        Check(style.LineStyle == "Dotted" && style.LineCap == "Round", "Line plan lost its pattern or cap.");
        Check(FontCatalog.Faces.Contains(FontCatalog.Families[0]), "Font browser lost family choices.");
        Dispatcher.UIThread.RunJobs(); UpdateLayout();
        using var shot = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize((int)Bounds.Width, (int)Bounds.Height));
        shot.Render(this); shot.Save(Path.ChangeExtension(output, "upstream-line.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        Console.WriteLine("PASS: initial zoom, pressure preset/control binding, line options and font faces.");
    }
}
