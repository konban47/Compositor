using Compositor.Core.Model;

namespace Compositor.Core.IO;

/// <summary>Saved GIF timing on a monotonic clock, starting at a stable frame identity and skipping expired frames.</summary>
public sealed class GifPlaybackSchedule
{
    private readonly (int Index, long End)[] _frames;
    private readonly long _offset;
    public long DurationTicks { get; }
    public GifPlaybackSchedule(GifAnimation animation, int selectedIndex)
    {
        var frames = animation.Frames; var found = false; long end = 0;
        _frames = new (int, long)[frames.Count];
        for (var i = 0; i < frames.Count; i++)
        {
            if (frames[i].Index == selectedIndex) { _offset = end; found = true; }
            var delay = animation.TimingMode == "Same for all" ? animation.UniformDelay : animation.Delays.GetValueOrDefault(frames[i].Index, animation.Source.Delays[frames[i].SourceIndex]);
            end = checked(end + Math.Max(2, (long)Math.Round(delay * 100)));
            _frames[i] = (frames[i].Index, end);
        }
        if (!found) throw new InvalidDataException("Unknown GIF playback frame.");
        DurationTicks = end;
    }
    public int FrameAt(TimeSpan elapsed)
    {
        var ticks = Math.Max(0, elapsed.Ticks / (TimeSpan.TicksPerMillisecond * 10));
        var phase = (ticks % DurationTicks + _offset) % DurationTicks;
        foreach (var frame in _frames) if (phase < frame.End) return frame.Index;
        return _frames[^1].Index;
    }
}

