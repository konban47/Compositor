using System.Diagnostics;
using System.Text.Json;
using Avalonia.Threading;
using Compositor.Core.IO.PSD;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    /// <summary>Opt-in, real Windows renderer benchmark. Never writes to the input document.</summary>
    internal async Task PerformanceCheck(string path, string report)
    {
        var clock = Stopwatch.StartNew();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
        var stage = "import";
        var frame = new TaskCompletionSource<(bool Gpu, double WorkMs)>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Failed(string message) => frame.TrySetException(new InvalidOperationException(message));
        _canvas.RenderFailed += Failed;
        try
        {
            var notes = 0;
            using var snapshot = await Compositor.Core.Model.WorkScheduler.Run(_ =>
            {
                if (Directory.Exists(path)) return Compositor.Core.IO.ProjectStore.Load(path);
                var parsed = PsdImporter.Read(path); notes = parsed.Notes.Count; return parsed.Snapshot();
            });
            var importMs = clock.Elapsed.TotalMilliseconds;
            var document = snapshot.ToDocument();
            CanvasView.FrameRendered = (gpu, time) => frame.TrySetResult((gpu, time));
            stage = "first-frame";
            AdoptImported(document, "Performance Check");
            var first = await frame.Task.WaitAsync(TimeSpan.FromSeconds(60));
            var firstFrameMs = clock.Elapsed.TotalMilliseconds;
            var viewport = _canvas.Viewport;
            var frames = new List<double>(); var work = new List<double>();
            stage = "viewport-frames";
            for (var i = 0; i < 60; i++)
            {
                frame = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var start = Stopwatch.GetTimestamp();
                _canvas.RestoreViewport((viewport.Zoom * (1 + .1 * Math.Sin(i * .1)), viewport.OriginX + i * 3, viewport.OriginY));
                var sample = await frame.Task.WaitAsync(TimeSpan.FromSeconds(10));
                frames.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds); work.Add(sample.WorkMs);
                await Task.Delay(1);
            }
            _canvas.RestoreViewport(viewport);
            var result = new { Success = true, InputBytes = Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length) : new FileInfo(path).Length, document.Width, document.Height,
                Layers = document.Layers.Count, ImportMs = importMs, FirstFrameMs = firstFrameMs,
                Gpu = first.Gpu, _canvas.AcceleratedScene, _canvas.SceneBuildCount,
                MetricStage = _canvas.AcceleratedScene ? "scene-draw-submission" : "cpu-composite-including-queue",
                Frames = frames.Count, MedianFrameMs = frames.Order().ElementAt(frames.Count / 2),
                P95FrameMs = frames.Order().ElementAt((int)(frames.Count * .95)),
                MedianRenderWorkMs = work.Order().ElementAt(work.Count / 2),
                PeakWorkingSetMiB = Process.GetCurrentProcess().PeakWorkingSet64 / 1048576,
                ConversionNotes = notes, _canvas.CpuCachedBytes, _canvas.CpuTileHits, _canvas.CpuTilesRendered,
                OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription, ProcessorCount = Environment.ProcessorCount };
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { Success = false, Stage = stage,
                Error = error.GetType().Name, error.Message, ElapsedMs = clock.Elapsed.TotalMilliseconds }, new JsonSerializerOptions { WriteIndented = true }));
            throw;
        }
        finally { CanvasView.FrameRendered = null; _canvas.RenderFailed -= Failed; }
    }
}
