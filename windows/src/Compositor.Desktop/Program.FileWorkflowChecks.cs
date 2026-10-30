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
            web.FileWorkflowSelfCheck(Pump, suffix => Capture(web, suffix), Capture);
            using (var animated = document.Clone())
            {
                var shown = LayerComps.Capture(animated, "Shown");
                animated.Layers[0].IsVisible = false;
                animated.Timeline = new DocumentTimeline { Mode = TimelineMode.Frames, Frames = [new(Guid.NewGuid(), .5, shown), new(Guid.NewGuid(), .5, LayerComps.Capture(animated, "Hidden"))] };
                animated.Layers[0].IsVisible = true;
                var moving = new WebExportDialog(animated);
                _ = moving.ShowDialog(owner);
                moving.AnimationSelfCheck(Pump, suffix => Capture(moving, suffix));
            }
            var sources = new[] { new AutomationSource("Export sample", null, document) };
            foreach (var kind in Enum.GetValues<AutomationKind>())
            {
                var dialog = new AutomationDialog(kind, sources, (_, _) => Task.CompletedTask);
                _ = dialog.ShowDialog(owner);
                if (kind == AutomationKind.ContactSheet) dialog.ContactSheetSelfCheck(Pump);
                if (kind == AutomationKind.PdfPresentation) dialog.PdfPresentationSelfCheck(Pump);
                if (kind == AutomationKind.Photomerge) dialog.PhotomergeSelfCheck(Pump);
                Capture(dialog, kind.ToString()); dialog.Close();
            }
            foreach (var processor in new[] { false, true })
            {
                var dialog = new BatchDialog(processor, sources, (_, _, _, _) => throw new InvalidOperationException("Unexpected batch execution."));
                _ = dialog.ShowDialog(owner); Capture(dialog, processor ? "processor" : "batch"); dialog.Close();
            }
            var layers = new LayerExportDialog(document, LayerExportKind.Layers);
            _ = layers.ShowDialog(owner); Capture(layers, "layers"); layers.Close();
            // Merge to HDR Pro's toning panel, on a small made-up merge.
            var light = new float[64 * 32 * 4];
            for (var i = 0; i < 64 * 32; i++) { var l = .002f * MathF.Pow(5000, i % 64 / 63f); light[i * 4] = l * 1.1f; light[i * 4 + 1] = l; light[i * 4 + 2] = l * .85f; light[i * 4 + 3] = 1; }
            var hdr = HdrProDialog.Open(owner, light, 64, 32, out var hdrClosed);
            Pump(); hdr.ShowPreview(); Capture(hdr, "hdr-pro");
            hdr.SetMethod((int)HdrToneMethod.ExposureAndGamma); Pump();
            if (hdr.SliderEnabled("Radius") || !hdr.SliderEnabled("Gamma")) throw new InvalidOperationException("Exposure and Gamma offered Edge Glow.");
            hdr.SetMode(0); Pump();
            if (hdr.ToningEnabled) throw new InvalidOperationException("A 32-bit merge offered toning.");
            hdr.Confirm(); Pump();
            if (!hdrClosed.IsCompleted || hdr.Result is not { Mode: HdrMode.Bits32 }) throw new InvalidOperationException("Merge to HDR Pro did not return its mode.");
            owner.FileImportSelfCheck(Pump, Capture);
            owner.ScriptEventSelfCheck(Pump, Capture);
            Console.WriteLine("PASS: Web preview encoding, four-up, palette, linked sizing, stale previews, cancellation and file workflow layouts.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { owner.Close(); }
    }
}

internal sealed partial class WebExportDialog
{
    /// <summary>A document with a frame animation: the GIF's animation controls step through its frames.</summary>
    internal void AnimationSelfCheck(Action pump, Action<string> capture)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        void Wait(Func<bool> ready)
        {
            var deadline = DateTime.UtcNow.AddSeconds(45);
            while (!ready() && DateTime.UtcNow < deadline) { pump(); Thread.Sleep(5); }
            Check(ready(), "Animated Web preview timed out: " + _status.Text);
        }
        Wait(() => _save.IsEnabled && !_rendering);
        Check(_loop.IsEnabled && _frameLabel.Text == "1/2" && !_loops.IsEnabled, "Animation controls did not follow the frames.");
        var first = _results[0].Preview.GetPixel(120, 110);
        StepFrame(1);
        Wait(() => _save.IsEnabled && !_rendering);
        Check(_frameLabel.Text == "2/2" && _results[0].Preview.GetPixel(120, 110) != first, "The next frame did not reach the preview.");
        _loop.SelectedIndex = 2; pump();
        Check(_loops.IsEnabled && Loops == 3, "Other did not offer a play count.");
        capture("web-animation");
        _format.SelectedIndex = (int)WebFormat.Png24; pump();
        Check(!_loop.IsEnabled, "A PNG offered GIF animation.");
        Close();
        Wait(() => !_rendering);
    }

    internal void FileWorkflowSelfCheck(Action pump, Action<string> capture, Action<Window, string> captureWindow)
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
        Check(!_loop.IsEnabled && _frameLabel.Text == "1/1" && _saveAs.SelectedIndex == 0, "A still document offered animation controls or a page by default.");
        var output = OutputSettingsDialog.Open(this, _html, out var outputClosed);
        pump(); captureWindow(output, "web-output-settings");
        output.SelectPage(1); pump(); captureWindow(output, "web-output-slices");
        output.SelectPreset(3); pump();
        Check(output.Current() == WebHtmlSettings.XhtmlPreset, "The XHTML preset did not fill the pages.");
        output.SelectPreset(1); output.Confirm(); pump();
        Check(outputClosed.IsCompleted && output.Result == WebHtmlSettings.BackgroundImagePreset, "Output Settings did not return the chosen preset.");
        Queue();
        Close();
        Wait(() => !_rendering);
        Check(_closed && _results.Length == 0, "Closed Web export retained encoded preview resources.");
    }
}
