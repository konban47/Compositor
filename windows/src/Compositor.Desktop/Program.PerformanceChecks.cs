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
        var parsed = await Task.Run(() => PsdImporter.Read(path));
        var importMs = clock.Elapsed.TotalMilliseconds;
        using var snapshot = parsed.Snapshot();
        var document = snapshot.ToDocument();
        var frame = new TaskCompletionSource<(bool Gpu, double SubmitMs)>(TaskCreationOptions.RunContinuationsAsynchronously);
        CanvasView.FrameRendered = (gpu, time) => frame.TrySetResult((gpu, time));
        try
        {
            AdoptImported(document, "Performance Check");
            var first = await frame.Task.WaitAsync(TimeSpan.FromSeconds(60));
            var firstFrameMs = clock.Elapsed.TotalMilliseconds;
            var viewport = _canvas.Viewport;
            var frames = new List<double>(); var submissions = new List<double>();
            for (var i = 0; i < 60; i++)
            {
                frame = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var start = Stopwatch.GetTimestamp();
                _canvas.RestoreViewport((viewport.Zoom * (1 + .1 * Math.Sin(i * .1)), viewport.OriginX + i * 3, viewport.OriginY));
                var sample = await frame.Task.WaitAsync(TimeSpan.FromSeconds(10));
                frames.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds); submissions.Add(sample.SubmitMs);
                await Task.Delay(1);
            }
            _canvas.RestoreViewport(viewport);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
            var result = new { InputBytes = new FileInfo(path).Length, document.Width, document.Height,
                Layers = document.Layers.Count, ImportMs = importMs, FirstFrameMs = firstFrameMs,
                Gpu = first.Gpu, _canvas.AcceleratedScene, _canvas.SceneBuildCount,
                Frames = frames.Count, MedianFrameMs = frames.Order().ElementAt(frames.Count / 2),
                P95FrameMs = frames.Order().ElementAt((int)(frames.Count * .95)),
                MedianDrawSubmissionMs = submissions.Order().ElementAt(submissions.Count / 2),
                PeakWorkingSetMiB = Process.GetCurrentProcess().PeakWorkingSet64 / 1048576,
                ConversionNotes = parsed.Notes.Count };
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { CanvasView.FrameRendered = null; }
    }
}
