using System.Collections.Immutable;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using ImageMagick;
using SkiaSharp;
namespace Compositor.Core.Tests;

public sealed class GifPlaybackTests
{
    [Fact] public void ClockStartsAtSelectedStableIdentityAndSkipsExpiredFrames()
    {
        var animation = new GifAnimation(new GifSource(GifAnimationTests.Sample())) with
        {
            Order = [new(8, 2), new(2, 1), new(5, 0)],
            Delays = ImmutableDictionary<int, double>.Empty.Add(8, .05).Add(2, .03).Add(5, .07)
        };
        var clock = new GifPlaybackSchedule(animation, 2);
        Assert.Equal(15, clock.DurationTicks);
        foreach (var (milliseconds, expected) in new[] { (-10, 2), (0, 2), (29, 2), (30, 5), (99, 5), (100, 8), (149, 8), (150, 2), (480, 5) })
            Assert.Equal(expected, clock.FrameAt(TimeSpan.FromMilliseconds(milliseconds)));
        Assert.Equal(2, clock.FrameAt(TimeSpan.FromDays(1000)));
        Assert.Throws<InvalidDataException>(() => new GifPlaybackSchedule(animation, 3));
    }
    [Fact] public void UniformClockKeepsOrderAndOneFrameLoops()
    {
        var animation = new GifAnimation(new GifSource(GifAnimationTests.Sample())) with { TimingMode = "Same for all", UniformDelay = .2 };
        var clock = new GifPlaybackSchedule(animation, 2);
        Assert.Equal(2, clock.FrameAt(TimeSpan.Zero)); Assert.Equal(0, clock.FrameAt(TimeSpan.FromMilliseconds(200)));
        Assert.Equal(1, clock.FrameAt(TimeSpan.FromMilliseconds(400))); Assert.Equal(2, clock.FrameAt(TimeSpan.FromMilliseconds(600)));
        clock = new GifPlaybackSchedule(animation with { Order = [new(7, 0)] }, 7);
        Assert.Equal(7, clock.FrameAt(TimeSpan.MaxValue));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ReusedReaderMatchesIndependentSeeksAcrossPartialDisposals(bool optimize)
    {
        using var frames = new MagickImageCollection();
        for (var f = 0; f < 4; f++)
        {
            using var pixels = Bitmaps.Allocate(Bitmaps.ColorInfo(20, 20)); pixels.Erase(SKColors.Transparent);
            for (var y = 5; y < 10; y++) for (var x = f * 3; x < f * 3 + 5; x++) pixels.SetPixel(x, y, f % 2 == 0 ? SKColors.Red : SKColors.Blue);
            frames.Add(new MagickImage(PngCodec.Encode(pixels)) { AnimationDelay = 10, GifDisposeMethod = f == 1 ? GifDisposeMethod.Previous : GifDisposeMethod.Background });
        }
        if (optimize) frames.Optimize();
        using var stream = new MemoryStream(); frames.Write(stream, MagickFormat.Gif);
        var source = new GifSource(stream.ToArray()); using var reader = new GifFrameReader(source);
        using var held = reader.Decode(0, "Held"); var original = held.Image.Pixels;
        foreach (var index in new[] { 0, 1, 2, 3, 3, 0, 3, 1, 2, 0 })
        {
            using var actual = reader.Decode(index, "Cached"); using var expected = source.Decode(index, "Fresh");
            Assert.Equal(expected.Image.Pixels, actual.Image.Pixels); Assert.Equal(original, held.Image.Pixels);
        }
        reader.Dispose(); Assert.Throws<ObjectDisposedException>(() => reader.Decode(0, "Closed"));
    }
    [Fact] public async Task ReaderSerializesConcurrentSeeksAndRejectsInvalidRequests()
    {
        var source = new GifSource(GifAnimationTests.Sample()); using var reader = new GifFrameReader(source);
        await Task.WhenAll(Enumerable.Range(0, 24).Select(i => Task.Run(() =>
        {
            using var image = reader.Decode(i % 3, "Concurrent");
            Assert.Equal(new[] { SKColors.Red, SKColors.Blue, SKColors.Lime }[i % 3], image.Image.GetPixel(2, 2));
        })));
        Assert.Throws<InvalidDataException>(() => reader.Decode(3, "Invalid"));
        Assert.Throws<InvalidDataException>(() => reader.Decode(0, "Invalid", new(0, 1, 1, 0, 0, 1, 0, 0)));
        using var valid = reader.Decode(1, "Still open"); Assert.Equal(SKColors.Blue, valid.Image.GetPixel(2, 2));
    }
    [Fact] public void SessionKeepsGeometryAndEditedFramesIndependentOfReaderLifetime()
    {
        var source = new GifSource(GifAnimationTests.Sample());
        var animation = new GifAnimation(source) with { Geometry = new(24, 20, 1, 0, 0, 1, 0, 0, LayerSampling.Nearest) };
        using var edited = source.Decode(1, "Edit", animation.Geometry); animation = animation.Edit(1, edited);
        using var session = new GifDecodeSession(); Assert.Same(edited, session.Decode(animation, 1, "Edit"));
        using var expanded = session.Decode(animation, 2, "Geometry"); Assert.Equal(24, expanded.Width); Assert.Equal(SKColors.Lime, expanded.Image.GetPixel(10, 10));
        using var unscaled = session.Decode(new GifAnimation(source), 0, "Shared source"); Assert.Equal(12, unscaled.Width);
        session.Dispose(); Assert.Equal(SKColors.Blue, edited.Image.GetPixel(10, 10)); Assert.Equal(SKColors.Lime, expanded.Image.GetPixel(10, 10));
        Assert.Throws<ObjectDisposedException>(() => session.Decode(animation, 0, "Closed"));
    }
}

