using System.Runtime.InteropServices;
using Compositor.Core.Document;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using SkiaSharp;

namespace Compositor.Core.Tests;

/// <summary>Filter › Rim Light and Filter › Volumetric Light (upstream b9a0ed8, 97c3b3f and 1405545), on the
/// upstream tests' own pictures: a figure in front of a dark backdrop, and a dark room with a barred window.</summary>
public sealed class LightingFilterTests
{
    private const int Width = 600, Height = 400;

    /// <summary>A head and shoulders, rows top-down, and the same shapes as the subject's mask.</summary>
    private static (SKBitmap Image, SKBitmap Mask) Figure()
    {
        var image = Bitmaps.Allocate(Bitmaps.ColorInfo(Width, Height));
        var mask = Bitmaps.Allocate(Bitmaps.MaskInfo(Width, Height));
        using (var canvas = new SKCanvas(image))
        using (var paint = new SKPaint { IsAntialias = true, Color = new SKColor(140, 107, 89) })
        {
            canvas.Clear(new SKColor(20, 23, 28));
            Shapes(canvas, paint);
        }
        using (var canvas = new SKCanvas(mask))
        using (var paint = new SKPaint { IsAntialias = true, Color = SKColors.White })
        {
            canvas.Clear(SKColors.Black);
            Shapes(canvas, paint);
        }
        return (image, mask);
        static void Shapes(SKCanvas canvas, SKPaint paint)
        {
            canvas.DrawOval(SKRect.Create(250, 60, 100, 120), paint);
            canvas.DrawRect(SKRect.Create(285, 170, 30, 60), paint);
            canvas.DrawOval(SKRect.Create(170, 210, 260, 310), paint);
        }
    }

    private static int[] Color(SKBitmap image, int x, int y)
    {
        using var deep = DeepPixels.From(image);
        var floats = MemoryMarshal.Cast<byte, float>(deep.GetPixelSpan()).Slice(y * deep.RowBytes / 4 + x * 4, 3).ToArray();
        return [.. floats.Select(v => (int)Math.Round(Math.Clamp(v, 0, 1) * 255))];
    }

    /// <summary>Where the subject starts and ends along a row.</summary>
    private static (int Left, int Right) Span(SKBitmap mask, int y)
    {
        var inside = Enumerable.Range(0, Width).Where(x => mask.GetPixel(x, y).Red > 128).ToArray();
        return (inside.First(), inside.Last());
    }

    private static RimLightSettings Light(double strength, double width, double choke = 0, double glow = 10) => new()
    {
        Glow = glow,
        Lights = [new RimLightSource { Angle = 180, Red = 1, Green = 1, Blue = 1, Strength = strength, Width = width, Choke = choke }],
    };

    [Fact]
    public void ALightFromTheLeftCatchesTheLeftEdgeOnly()
    {
        var (original, mask) = Figure();
        using var _ = original; using var __ = mask;
        using var lit = RimLighting.Apply(original, mask, Light(80, 30));
        var span = Span(mask, 110);
        int Lift(int x) => Color(lit, x, 110)[1] - Color(original, x, 110)[1];
        Assert.True(Lift(span.Left + 4) > 25, $"the edge facing the light: {Lift(span.Left + 4)}");
        Assert.True(Lift(span.Right - 4) <= 3, $"the edge facing away: {Lift(span.Right - 4)}");
        Assert.True(Lift((span.Left + span.Right) / 2) <= 3, "the front, which light from behind doesn't reach");
        Assert.Equal(Color(original, 20, 20), Color(lit, 20, 20));
    }

    [Fact]
    public void ChokeHoldsTheLightFurtherIn()
    {
        var (original, mask) = Figure();
        using var _ = original; using var __ = mask;
        var x = Span(mask, 110).Left + 12;
        int Lift(double choke) { using var lit = RimLighting.Apply(original, mask, Light(15, 100, choke)); return Color(lit, x, 110)[1] - Color(original, x, 110)[1]; }
        var held = Lift(100); var feathered = Lift(0);
        Assert.True(held > feathered + 10, $"held {held}, feathered {feathered}");
    }

    [Fact]
    public void GlowLightsTheAirBesideTheLitEdgeAndLeavesTheSubject()
    {
        var (original, mask) = Figure();
        using var _ = original; using var __ = mask;
        using var none = RimLighting.Apply(original, mask, Light(80, 30, glow: 0));
        using var glowing = RimLighting.Apply(original, mask, Light(80, 30, glow: 100));
        var span = Span(mask, 110);
        var beside = Color(glowing, span.Left - 6, 110)[1] - Color(none, span.Left - 6, 110)[1];
        var middle = Color(glowing, (span.Left + span.Right) / 2, 110)[1] - Color(none, (span.Left + span.Right) / 2, 110)[1];
        Assert.True(beside > 10, $"the air beside the lit edge: {beside}");
        Assert.True(Math.Abs(middle) <= 2, $"the subject keeps only its rims: {middle}");
        Assert.Equal(Color(original, span.Left - 6, 110), Color(none, span.Left - 6, 110));
    }

    [Fact]
    public void GlowWrapsALittleWayInAcrossTheLitEdge()
    {
        var (original, mask) = Figure();
        using var _ = original; using var __ = mask;
        using var none = RimLighting.Apply(original, mask, Light(30, 0, 100, glow: 0));
        using var glowing = RimLighting.Apply(original, mask, Light(30, 0, 100, glow: 100));
        var x = Span(mask, 110).Left + 4;
        var wrap = Color(glowing, x, 110)[1] - Color(none, x, 110)[1];
        Assert.True(wrap > 4, $"the glow across the edge: {wrap}");
    }

    [Fact]
    public void EachLightWrapsAsFarAsItsOwnWidth()
    {
        var (original, mask) = Figure();
        using var _ = original; using var __ = mask;
        var x = Span(mask, 110).Left + 14;
        int Lift(double width) { using var lit = RimLighting.Apply(original, mask, Light(80, width)); return Color(lit, x, 110)[1] - Color(original, x, 110)[1]; }
        Assert.True(Lift(100) > Lift(0) + 10);
    }

    [Fact]
    public void ABusyBackdropBesideTheSubjectStaysDark()
    {
        var (original, mask) = Figure();
        using var _ = original; using var __ = mask;
        // Rocks behind the figure: noise everywhere outside it, which no strand stands out from.
        var random = new Random(7);
        for (var y = 0; y < Height; y++) for (var x = 0; x < Width; x++)
            if (mask.GetPixel(x, y).Red == 0) { var v = (byte)random.Next(10, 120); original.SetPixel(x, y, new SKColor(v, v, v)); }
        using var lit = RimLighting.Apply(original, mask, Light(80, 30, glow: 0));
        var span = Span(mask, 110);
        var changed = Enumerable.Range(span.Left - 20, 12).Count(x => Color(lit, x, 110)[1] - Color(original, x, 110)[1] > 6);
        Assert.True(changed <= 2, $"lit backdrop pixels beside the edge: {changed}");
    }

    [Fact]
    public void LightsAreAddedFromTheOtherSideAndCopiedApart()
    {
        var settings = new RimLightSettings();
        settings.Lights.Add(settings.NextLight);
        Assert.Equal(-45, settings.Lights[1].Angle);
        Assert.Equal((.7, .84, 1.0), (settings.Lights[1].Red, settings.Lights[1].Green, settings.Lights[1].Blue));
        settings.Lights.Add(settings.NextLight);
        Assert.Equal(135, settings.Lights[2].Angle);
        Assert.Equal((1.0, 1.0, 1.0), (settings.Lights[2].Red, settings.Lights[2].Green, settings.Lights[2].Blue));
        Assert.True(settings.IsValid);
        settings.Lights.Add(new RimLightSource());
        Assert.False(settings.IsValid);
        var amounts = new FilterSettings();
        var copy = amounts.Copy();
        copy.RimLight.Lights[0].Strength = 0;
        Assert.Equal(50, amounts.RimLight.Lights[0].Strength);
        Assert.False(copy.DoesAnything(FilterKind.RimLight));
    }

    [Fact]
    public void TheFilterLightsTheLayerFoundSubjectAndKeepsItsDepth()
    {
        var (original, mask) = Figure();
        using var _ = mask;
        using var document = new CanvasDocument(Guid.NewGuid(), Width, Height);
        var asset = ImportedImage.CreateDeep(DeepPixels.From(original), "Figure");
        original.Dispose();
        var layer = new ImageLayer(Guid.NewGuid(), asset, new Model.LayerTransform(0, 0, Width, Height), "Figure");
        document.Layers.Add(layer);
        SubjectCache.Remember(asset.Image, mask.Copy());
        var settings = new FilterSettings { RimLight = Light(80, 30) };
        Assert.True(FilterEdits.Apply(document, layer.ID, FilterKind.RimLight, settings));
        var after = document.Layers.First(l => l.ID == layer.ID).Asset!;
        Assert.Equal(16, after.BitDepth);
        var x = Span(mask, 110).Left + 4;
        Assert.True(Color(after.WorkingImage, x, 110)[1] > Color(asset.WorkingImage, x, 110)[1] + 25);

        // A layer with no subject found has no edge to light.
        var blank = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(Bitmaps.Allocate(Bitmaps.ColorInfo(40, 40)), "Blank"), new Model.LayerTransform(0, 0, 40, 40), "Blank");
        document.Layers.Add(blank);
        SubjectCache.Remember(blank.Asset!.Image, null);
        Assert.False(FilterEdits.Apply(document, blank.ID, FilterKind.RimLight, settings));
    }

    private static readonly SKRectI Window = SKRectI.Create(260, 40, 80, 80);

    /// <summary>A dark room with a bright window crossed by a bar down its middle.</summary>
    private static SKBitmap Room()
    {
        var image = Bitmaps.Allocate(Bitmaps.ColorInfo(Width, Height));
        using var canvas = new SKCanvas(image);
        canvas.Clear(new SKColor(15, 15, 18));
        using var paint = new SKPaint { Color = new SKColor(255, 247, 230) };
        canvas.DrawRect(Window, paint);
        paint.Color = new SKColor(10, 10, 10);
        canvas.DrawRect(SKRect.Create(Window.MidX - 4, Window.Top, 8, Window.Height), paint);
        return image;
    }

    private static int Gray(SKBitmap image, int x, int y) => Color(image, x, y).Sum() / 3;

    [Fact]
    public void TheLightStartsOnTheBrightestSpot()
    {
        using var room = Room();
        var point = VolumetricLighting.BrightestPoint(room);
        var around = Window; around.Inflate(10, 10);
        Assert.True(around.Contains((int)point.X, (int)point.Y), $"on the window: {point}");
    }

    [Fact]
    public void TheLightStartsInTheBiggestBrightArea()
    {
        using var image = Bitmaps.Allocate(Bitmaps.ColorInfo(Width, Height));
        using (var canvas = new SKCanvas(image))
        using (var paint = new SKPaint { Color = new SKColor(230, 224, 217) })
        {
            canvas.Clear(new SKColor(13, 13, 13));
            canvas.DrawRect(SKRect.Create(220, 20, 160, 120), paint);
            paint.Color = SKColors.White;
            canvas.DrawRect(SKRect.Create(100, 330, 30, 30), paint);
        }
        var point = VolumetricLighting.BrightestPoint(image);
        Assert.True(SKRect.Create(220, 20, 160, 120).Contains(point), $"in the opening: {point}");
    }

    [Fact]
    public void BeamsLightTheRoomAndTheBarCastsAShaftOfShadow()
    {
        using var original = Room();
        var settings = new VolumetricLightSettings
        {
            CenterX = (Window.MidX) / (double)Width * 100, CenterY = (Window.MidY) / (double)Height * 100,
            Haze = 0, Shafts = 0, Length = 100, Intensity = 80,
        };
        using var lit = VolumetricLighting.Apply(original, null, settings);
        var below = Window.Bottom + 60;
        var shadowed = Gray(lit, Window.MidX, below); var open = Gray(lit, Window.Left + 18, below);
        var before = Gray(original, Window.Left + 18, below);
        Assert.True(open > before + 20, $"the beams light the room below the window: {before} → {open}");
        Assert.True(shadowed < open - 6, $"the bar's shadow runs through them: {shadowed} against {open}");
        var corner = Gray(lit, 5, Height - 5);
        Assert.True(corner < open - 10, $"the far corner gets less than near the window: {corner} against {open}");
    }

    [Fact]
    public void TheSubjectStandsInFrontOfTheHaze()
    {
        using var original = Room();
        using var subject = Bitmaps.Allocate(Bitmaps.MaskInfo(Width, Height));
        using (var canvas = new SKCanvas(subject)) using (var paint = new SKPaint { Color = SKColors.White })
        { canvas.Clear(SKColors.Black); canvas.DrawRect(SKRect.Create(60, 150, 120, 200), paint); }
        var settings = new VolumetricLightSettings { CenterX = 50, CenterY = 20, Haze = 80, Intensity = 0 };
        using var bare = VolumetricLighting.Apply(original, null, settings);
        using var standing = VolumetricLighting.Apply(original, subject, settings);
        // Mid-height, where the haze is thick: in front of the subject it's thinner than the same spot without one.
        Assert.True(Gray(standing, 120, 200) < Gray(bare, 120, 200) - 5, $"{Gray(standing, 120, 200)} against {Gray(bare, 120, 200)}");
        Assert.Equal(Gray(bare, 500, 200), Gray(standing, 500, 200));
    }

    [Fact]
    public void ZeroIntensityAndHazeLeaveTheImage()
    {
        var settings = new VolumetricLightSettings { Intensity = 0, Haze = 0 };
        Assert.False(settings.DoesAnything);
        using var room = Room();
        using var same = VolumetricLighting.Apply(room, null, settings);
        Assert.Equal(Gray(room, 300, 300), Gray(same, 300, 300));
        Assert.False(new FilterSettings { Volumetric = settings }.DoesAnything(FilterKind.VolumetricLight));
        Assert.False(new VolumetricLightSettings { CenterX = double.NaN }.IsValid);
    }

    [Fact]
    public void BoxBlurApproximatesAGaussian()
    {
        var plane = new float[101 * 1]; plane[50] = 1;
        var blurred = LightPlanes.Blur(plane, 101, 1, 5);
        Assert.Equal(1, blurred.Sum(), 3);
        // A Gaussian of sigma 5 peaks at 1/(5√(2π)) ≈ 0.0798.
        Assert.InRange(blurred[50], .07, .09);
        Assert.InRange(RimLighting.InverseError(.5f), .47, .48);
    }
}
