using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

internal static partial class Program
{
    private static int FileWorkflowChecks(string output)
    {
        BuildHeadless().SetupWithoutStarting();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var document = new CanvasDocument(Guid.NewGuid(), 360, 240);
        var pixels = new SKBitmap(Bitmaps.ColorInfo(360, 240));
        using (var canvas = new SKCanvas(pixels))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { IsAntialias = true, Color = SKColors.Coral };
            canvas.DrawCircle(120, 110, 85, paint);
            paint.Color = SKColors.SteelBlue;
            canvas.DrawRect(150, 80, 150, 110, paint);
        }
        document.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "Export sample"), new(0, 0, 360, 240), "Export sample"));
        using var lease = new DocumentLease(document);
        var owner = new MainWindow();
        try
        {
            owner.Show();
            void Pump() { using var tick = new CancellationTokenSource(10); Dispatcher.UIThread.MainLoop(tick.Token); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout(); }
            void Capture(Window window, string suffix)
            {
                Pump(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                if (frame is null) throw new InvalidOperationException("Missing dialog frame.");
                frame.Save(Path.ChangeExtension(output, suffix + ".png"), new PngBitmapEncoderOptions());
            }
            var web = new WebExportDialog(document);
            _ = web.ShowDialog(owner);
            web.FileWorkflowSelfCheck(Pump, suffix => Capture(web, suffix));
            var sources = new[] { new AutomationSource("Export sample", null, document) };
            foreach (var kind in Enum.GetValues<AutomationKind>())
            {
                var dialog = new AutomationDialog(kind, sources, (_, _) => Task.CompletedTask);
                _ = dialog.ShowDialog(owner); Capture(dialog, kind.ToString()); dialog.Close();
            }
            foreach (var processor in new[] { false, true })
            {
                var dialog = new BatchDialog(processor, sources, (_, _, _, _) => throw new InvalidOperationException("Unexpected batch execution."));
                _ = dialog.ShowDialog(owner); Capture(dialog, processor ? "processor" : "batch"); dialog.Close();
            }
            var layers = new LayerExportDialog(document, LayerExportKind.Layers);
            _ = layers.ShowDialog(owner); Capture(layers, "layers"); layers.Close();
            Console.WriteLine("PASS: Web preview encoding, four-up, palette, linked sizing, stale previews, cancellation and file workflow layouts.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { owner.Close(); }
    }
}

internal sealed partial class WebExportDialog
{
    internal void FileWorkflowSelfCheck(Action pump, Action<string> capture)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        void Wait(Func<bool> ready)
        {
            var deadline = DateTime.UtcNow.AddSeconds(45);
            while (!ready() && DateTime.UtcNow < deadline) { pump(); Thread.Sleep(5); }
            Check(ready(), "Web preview timed out: " + _status.Text + $" (rendering={_rendering}, revision={_revision}, timer={_timer.IsEnabled}, results={_results.Length}, view={_view.SelectedIndex})");
        }
        Wait(() => _save.IsEnabled && !_rendering);
        Check(_results.Length == 1 && _results[0].Options.Format == WebFormat.Gif, "Default GIF preview missing.");
        _view.SelectedIndex = 3;
        Wait(() => _save.IsEnabled && !_rendering && _results.Length == 3);
        Check(_previews.Children.Count == 4, "Four-up does not contain the original and three encoded previews.");
        capture("web-four-up");
        _width.Value = 180;
        Check(_height.Value == 120, "Linked dimensions did not preserve aspect ratio.");
        _format.SelectedIndex = (int)WebFormat.Jpeg;
        _quality.Value = 35;
        Wait(() => _save.IsEnabled && !_rendering && _results[0].Options.Format == WebFormat.Jpeg);
        Check(_results[0].Preview.Width == 180 && _results[0].Options.Quality == 35, "Preview ignored export controls.");
        Check(!_transparent.IsEnabled, "JPEG transparency must be disabled.");
        _format.SelectedIndex = (int)WebFormat.Png8;
        SetPalette([(uint)SKColors.Black, (uint)SKColors.White, (uint)SKColors.Red]);
        Wait(() => _save.IsEnabled && !_rendering && _results[0].Options.Format == WebFormat.Png8);
        Check(_results[0].Options.Palette == WebPalette.Custom, "Custom palette did not reach encoder.");
        _width.Value = 170; _width.Value = 160; _width.Value = 150;
        Wait(() => _save.IsEnabled && !_rendering && _results[0].Preview.Width == 150);
        Check(_results[0].Options.Height == 100, "An obsolete preview replaced the latest dimensions.");
        _view.SelectedIndex = 1;
        Width = 860; Height = 700;
        Wait(() => _save.IsEnabled && !_rendering && _results.Length == 1);
        pump(); capture("web-narrow");
        Queue();
        Close();
        Wait(() => !_rendering);
        Check(_closed && _results.Length == 0, "Closed Web export retained encoded preview resources.");
    }
}
