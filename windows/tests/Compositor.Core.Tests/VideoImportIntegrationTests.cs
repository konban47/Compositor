using System.Diagnostics;
using Compositor.Core.IO;

namespace Compositor.Core.Tests;

public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute()
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        foreach (var program in new[] { "ffmpeg", "ffprobe" })
            if (!paths.Any(path => File.Exists(Path.Combine(path, program + (OperatingSystem.IsWindows() ? ".exe" : "")))))
                Skip = "FFmpeg and ffprobe are optional; install them on PATH for media integration tests.";
    }
}

public sealed class VideoImportIntegrationTests : ProjectTestBase
{
    [FfmpegFact]
    public void RealVideoFramesRemainIndependentAndPlayableAfterProjectReopen()
    {
        var path = PathIn("video.mkv");
        var start = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-nostdin", "-v", "error", "-f", "lavfi", "-i", "testsrc=size=16x8:rate=4:duration=1", "-c:v", "ffv1", path })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000)) { process.Kill(true); throw new TimeoutException("Test video generation timed out."); }
        Assert.True(process.ExitCode == 0, error.GetAwaiter().GetResult());
        var media = TimelineMedia.Inspect(path);
        Assert.Equal(4, media.FrameRate);
        using var document = VideoFrameImport.Import(path, media, new(0, 1, media.FrameRate, 2));
        Assert.Equal(16, document.Width); Assert.Equal(8, document.Height); Assert.Equal(2, document.Layers.Count);
        Assert.All(document.Timeline!.Frames, frame => Assert.Equal(.5, frame.Duration));
        var project = PathIn("video.comp");
        // FromDocument borrows the live document's pixel assets; the document owns their lifetime.
        var snapshot = ProjectSnapshot.FromDocument(document);
        ProjectStore.Save(snapshot, project);
        File.Delete(path);
        using var loaded = ProjectStore.Load(project);
        using var reopened = loaded.ToDocument();
        Assert.True(reopened.Timeline!.IsValid);
        Assert.Equal(2, reopened.Timeline.Frames.Length);
        Assert.Equal(document.Layers[1].Asset!.Image.GetPixel(8, 4), reopened.Layers[1].Asset!.Image.GetPixel(8, 4));
    }
}
