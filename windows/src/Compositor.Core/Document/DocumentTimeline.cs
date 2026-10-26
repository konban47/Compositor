using System.Collections.Immutable;
using Compositor.Core.Model;
using Transform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Document;

public enum TimelineMode { Video, Frames }
public enum KeyInterpolation { Linear, Hold }
public sealed record TimelineKey(double Time, Transform Transform, double Opacity, KeyInterpolation Interpolation = KeyInterpolation.Linear)
{
    public bool IsValid => double.IsFinite(Time) && Time is >= 0 and <= DocumentTimeline.MaxDuration && Transform.IsValid
        && double.IsFinite(Opacity) && Opacity is >= 0 and <= 1 && Enum.IsDefined(Interpolation);
}
public sealed record TimelineClip
{
    public Guid ID { get; init; } = Guid.NewGuid();
    public Guid LayerID { get; init; }
    public double Start { get; init; }
    public double Duration { get; init; } = 5;
    public double SourceStart { get; init; }
    public double Speed { get; init; } = 1;
    public double FadeIn { get; init; }
    public double FadeOut { get; init; }
    public bool Favorite { get; init; }
    public bool? Visibility { get; init; }
    public string? SourceFile { get; init; }
    public LayerCompState? State { get; init; }
    public ImmutableArray<TimelineKey> Keys { get; init; } = [];
    public double End => Start + Duration;
    public bool IsValid => ID != Guid.Empty && LayerID != Guid.Empty && new[] { Start, Duration, SourceStart, Speed, FadeIn, FadeOut }.All(double.IsFinite)
        && Start >= 0 && Duration >= .001 && End <= DocumentTimeline.MaxDuration && SourceStart >= 0
        && Speed is >= .01 and <= 100 && FadeIn >= 0 && FadeOut >= 0 && FadeIn <= Duration && FadeOut <= Duration
        && (SourceFile is null || SourceFile.Length is > 0 and <= 32768 && !SourceFile.Contains('\0'))
        && (State is null || State.IsValid && State.LayerID == LayerID)
        && !Keys.IsDefault && Keys.Length <= 10000 && Keys.All(k => k is not null && k.IsValid && k.Time <= Duration)
        && Keys.Select(k => k.Time).SequenceEqual(Keys.Select(k => k.Time).Distinct().Order());
}
public sealed record TimelineFrame(Guid ID, double Duration, LayerComp State)
{
    public bool IsValid => ID != Guid.Empty && double.IsFinite(Duration) && Duration is >= .001 and <= 600 && State is not null && State.IsValid;
}
public sealed record TimelineNote(Guid ID, double Time, string Text)
{
    public bool IsValid => ID != Guid.Empty && double.IsFinite(Time) && Time is >= 0 and <= DocumentTimeline.MaxDuration && Text is { Length: <= 10000 };
}
public sealed record TimelineAudio(Guid ID, string File, double Start, double Duration, double SourceStart = 0, double Volume = 1, bool Muted = false)
{
    public bool IsValid => ID != Guid.Empty && File is { Length: > 0 and <= 32768 } && !File.Contains('\0')
        && new[] { Start, Duration, SourceStart, Volume }.All(double.IsFinite) && Start >= 0 && Duration > 0
        && Start + Duration <= DocumentTimeline.MaxDuration && SourceStart >= 0 && Volume is >= 0 and <= 4;
}
/// <summary>Times are seconds; the editor quantizes gestures to frames, while rate changes preserve time.</summary>
public sealed record DocumentTimeline
{
    public const double MaxDuration = 86400;
    public TimelineMode Mode { get; init; }
    public double FrameRate { get; init; } = 30;
    public double WorkStart { get; init; }
    public double WorkEnd { get; init; } = 5;
    public bool Loop { get; init; } = true;
    public bool AllowFrameSkipping { get; init; } = true;
    public bool AudioEnabled { get; init; } = true;
    public bool Shortcuts { get; init; } = true;
    public bool AutoGroup { get; init; } = true;
    public bool OnionSkin { get; init; }
    public double OnionOpacity { get; init; } = .25;
    public int ThumbnailSize { get; init; } = 2;
    public bool FrameNumbers { get; init; }
    public int PreviewPercent { get; init; } = 50;
    public int DisplayFilter { get; init; }
    public bool ShowNotes { get; init; } = true;
    public ImmutableArray<TimelineClip> Clips { get; init; } = [];
    public ImmutableArray<TimelineFrame> Frames { get; init; } = [];
    public ImmutableArray<TimelineNote> Notes { get; init; } = [];
    public ImmutableArray<TimelineAudio> Audio { get; init; } = [];
    public double Duration => Mode == TimelineMode.Frames ? Math.Max(.01, Frames.Sum(f => f.Duration)) : Math.Max(WorkEnd, Math.Max(Clips.Select(c => c.End).DefaultIfEmpty(0).Max(), Audio.Select(a => a.Start + a.Duration).DefaultIfEmpty(0).Max()));
    public bool IsValid => Enum.IsDefined(Mode) && double.IsFinite(FrameRate) && FrameRate is >= 1 and <= 120
        && double.IsFinite(WorkStart) && double.IsFinite(WorkEnd) && WorkStart >= 0 && WorkEnd > WorkStart && WorkEnd <= MaxDuration
        && double.IsFinite(OnionOpacity) && OnionOpacity is >= 0 and <= 1 && ThumbnailSize is >= 0 and <= 3
        && PreviewPercent is 25 or 50 or 100 && DisplayFilter is >= 0 and <= 2
        && !Clips.IsDefault && Clips.Length <= 10000 && Clips.All(c => c is not null && c.IsValid) && Unique(Clips.Select(c => c.ID))
        && !Frames.IsDefault && Frames.Length <= 4096 && Frames.All(f => f is not null && f.IsValid) && Unique(Frames.Select(f => f.ID))
        && !Notes.IsDefault && Notes.Length <= 10000 && Notes.All(n => n is not null && n.IsValid) && Unique(Notes.Select(n => n.ID))
        && !Audio.IsDefault && Audio.Length <= 128 && Audio.All(a => a is not null && a.IsValid) && Unique(Audio.Select(a => a.ID))
        && Duration <= MaxDuration;
    private static bool Unique(IEnumerable<Guid> ids) { var values = ids.ToArray(); return values.Distinct().Count() == values.Length; }
}

public static class TimelineEdits
{
    public static DocumentTimeline Create(CanvasDocument document, TimelineMode mode) => new()
    {
        Mode = mode,
        WorkEnd = mode == TimelineMode.Frames ? .1 : 5,
        Clips = document.Layers.Where(l => !l.IsGroup).Select(l => new TimelineClip { LayerID = l.ID }).ToImmutableArray(),
        Frames = mode == TimelineMode.Frames ? [new(Guid.NewGuid(), .1, LayerComps.Capture(document, "Frame 1"))] : []
    };
    public static double Snap(double time, double rate) => Math.Max(0, Math.Round(time * rate) / rate);
    public static string Timecode(double time, double rate, bool frames = false)
    {
        var frame = Math.Max(0, (long)Math.Round(time * rate)); if (frames) return frame.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var whole = (int)Math.Floor(frame / rate); var fraction = (int)Math.Round(frame - Math.Floor(whole * rate));
        return $"{whole / 3600}:{whole / 60 % 60:00}:{whole % 60:00}:{fraction:00}";
    }
    public static int FrameAt(DocumentTimeline timeline, double time)
    {
        var end = 0d; for (var i = 0; i < timeline.Frames.Length; i++) { end += timeline.Frames[i].Duration; if (time < end) return i; }
        return Math.Max(0, timeline.Frames.Length - 1);
    }
    public static DocumentTimeline Replace(DocumentTimeline timeline, TimelineClip clip)
    {
        if (!clip.IsValid) throw new ArgumentException("Invalid clip timing or keyframes.");
        var i = timeline.Clips.FindIndex(c => c.ID == clip.ID);
        return timeline with { Clips = i >= 0 ? timeline.Clips.SetItem(i, clip) : timeline.Clips.Add(clip) };
    }
    public static DocumentTimeline Split(DocumentTimeline timeline, Guid id, double time)
    {
        var clip = timeline.Clips.FirstOrDefault(c => c.ID == id); if (clip is null || time <= clip.Start || time >= clip.End) return timeline;
        var local = time - clip.Start;
        var left = clip with { Duration = local, FadeIn = Math.Min(clip.FadeIn, local), FadeOut = 0, Keys = clip.Keys.Where(k => k.Time < local).ToImmutableArray() };
        var right = clip with { ID = Guid.NewGuid(), Start = time, Duration = clip.End - time, SourceStart = clip.SourceStart + local * clip.Speed,
            FadeIn = 0, FadeOut = Math.Min(clip.FadeOut, clip.End - time), Keys = clip.Keys.Where(k => k.Time >= local).Select(k => k with { Time = k.Time - local }).ToImmutableArray() };
        if (clip.Keys.Length > 0)
        {
            var at = Interpolate(clip.Keys, local); left = left with { Keys = left.Keys.Add(at with { Time = local }) };
            right = right with { Keys = right.Keys.Where(k => k.Time != 0).Prepend(at with { Time = 0 }).ToImmutableArray() };
        }
        return Replace(timeline, left) with { Clips = Replace(timeline, left).Clips.Add(right) };
    }
    public static TimelineClip Trim(TimelineClip clip, double from, double to)
    {
        from = Math.Clamp(from, clip.Start, clip.End - .001); to = Math.Clamp(to, from + .001, clip.End);
        var offset = from - clip.Start; var duration = to - from;
        var keys = clip.Keys.Where(k => k.Time > offset && k.Time < offset + duration).Select(k => k with { Time = k.Time - offset }).ToImmutableArray();
        if (clip.Keys.Length > 0) keys = keys.Insert(0, Interpolate(clip.Keys, offset) with { Time = 0 }).Add(Interpolate(clip.Keys, offset + duration) with { Time = duration });
        return clip with { Start = from, Duration = duration, SourceStart = clip.SourceStart + offset * clip.Speed, Keys = keys, FadeIn = Math.Min(clip.FadeIn, duration), FadeOut = Math.Min(clip.FadeOut, duration) };
    }
    public static TimelineKey Interpolate(ImmutableArray<TimelineKey> keys, double time)
    {
        var a = keys.LastOrDefault(k => k.Time <= time) ?? keys[0]; var b = keys.FirstOrDefault(k => k.Time > time) ?? a;
        if (a == b || a.Interpolation == KeyInterpolation.Hold) return a with { Time = time };
        var t = Math.Clamp((time - a.Time) / (b.Time - a.Time), 0, 1); double Mix(double x, double y) => x + (y - x) * t;
        var x = a.Transform; var y = b.Transform;
        return a with { Time = time, Opacity = Mix(a.Opacity, b.Opacity), Transform = x with { X = Mix(x.X,y.X), Y = Mix(x.Y,y.Y), Width = Mix(x.Width,y.Width), Height = Mix(x.Height,y.Height), Rotation = Mix(x.Rotation,y.Rotation) } };
    }
    /// <summary>Applies time to a borrowed clone only; scrubbing never dirties source pixels or history.</summary>
    public static void Evaluate(CanvasDocument view, DocumentTimeline timeline, double time)
    {
        if (timeline.Mode == TimelineMode.Frames && timeline.Frames.Length > 0) { LayerComps.Apply(view, timeline.Frames[FrameAt(timeline, time)].State, respectLocks: false); return; }
        var tracked = timeline.Clips.Select(c => c.LayerID).ToHashSet();
        foreach (var layer in view.Layers.Where(l => tracked.Contains(l.ID)))
        {
            var clip = timeline.Clips.LastOrDefault(c => c.LayerID == layer.ID && time >= c.Start && time < c.End);
            if (clip is null) { layer.IsVisible = false; continue; }
            if (clip.State is { } state) LayerComps.Apply(view, new LayerComp { Layers = [state] }, respectLocks: false);
            if (clip.Visibility is { } visible) layer.IsVisible = visible;
            var local = time - clip.Start;
            if (clip.Keys.Length > 0) { var key = Interpolate(clip.Keys, local); layer.Transform = key.Transform; layer.Opacity = key.Opacity; }
            var fade = (clip.FadeIn > 0 ? Math.Min(1, local / clip.FadeIn) : 1) * (clip.FadeOut > 0 ? Math.Min(1, (clip.Duration - local) / clip.FadeOut) : 1);
            layer.Opacity *= fade;
        }
    }
    public static DocumentTimeline ConvertToFrames(CanvasDocument source, DocumentTimeline timeline)
    {
        var count = (int)Math.Ceiling(timeline.Duration * timeline.FrameRate);
        if (count > 4096) throw new InvalidOperationException("Frame animation is limited to 4096 frames.");
        var frames = ImmutableArray.CreateBuilder<TimelineFrame>();
        for (var i = 0; i < count; i++)
        {
            var time = i / timeline.FrameRate;
            using var view = source.Clone(); Evaluate(view, timeline, time);
            frames.Add(new(Guid.NewGuid(), Math.Max(.001, Math.Min(1 / timeline.FrameRate, timeline.Duration - time)), LayerComps.Capture(view, $"Frame {i + 1}")));
        }
        return timeline with { Mode = TimelineMode.Frames, Frames = frames.ToImmutable(), WorkStart = 0, WorkEnd = frames.Sum(f => f.Duration) };
    }
    public static DocumentTimeline ConvertToVideo(DocumentTimeline timeline)
    {
        if (timeline.Mode == TimelineMode.Video) return timeline;
        if (timeline.Frames.Sum(frame => (long)frame.State.Layers.Length) > 10000)
            throw new InvalidOperationException("The converted timeline would exceed 10000 clips.");
        var clips = ImmutableArray.CreateBuilder<TimelineClip>(); var start = 0d;
        foreach (var frame in timeline.Frames)
        {
            foreach (var state in frame.State.Layers)
            {
                // Frame conversion stores property snapshots, while pixels remain linked to their media clock.
                var source = timeline.Clips.LastOrDefault(c => c.LayerID == state.LayerID && start >= c.Start && start < c.End);
                clips.Add(new() { LayerID = state.LayerID, State = state, Visibility = state.Visible, Start = start, Duration = frame.Duration,
                    SourceFile = source?.SourceFile, SourceStart = source is null ? start : source.SourceStart + (start - source.Start) * source.Speed,
                    Speed = source?.Speed ?? 1, Keys = [new(0, state.Transform, state.Visible ? state.Opacity : 0, KeyInterpolation.Hold)] });
            }
            start += frame.Duration;
        }
        return timeline with { Mode = TimelineMode.Video, Clips = clips.ToImmutable(), WorkStart = 0, WorkEnd = Math.Max(.01, start) };
    }
}

internal static class TimelineArray
{
    public static int FindIndex<T>(this ImmutableArray<T> array, Func<T, bool> predicate)
    { for (var i = 0; i < array.Length; i++) if (predicate(array[i])) return i; return -1; }
}
