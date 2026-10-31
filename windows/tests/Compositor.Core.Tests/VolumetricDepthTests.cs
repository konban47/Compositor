using System.Net;
using System.Runtime.InteropServices;
using Compositor.Core.Document;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using SkiaSharp;

namespace Compositor.Core.Tests;

/// <summary>Volumetric Light's depth (upstream 039d445 to 76b7921): Distance, Depth Contrast, the depth model and
/// Extract Depth.</summary>
[Collection(nameof(DepthModel))]
public sealed class VolumetricDepthTests
{
    private const int Width = 160, Height = 120;

    /// <summary>A mid-gray wall, evenly lit, so only the haze changes it.</summary>
    private static SKBitmap Wall()
    {
        var image = Bitmaps.Allocate(Bitmaps.ColorInfo(Width, Height));
        image.Erase(new SKColor(26, 26, 26));
        return image;
    }

    private static float Red(SKBitmap image, int x, int y) => MemoryMarshal.Cast<byte, float>(image.GetPixelSpan())[(y * image.RowBytes / 4) + x * 4];

    private static DepthMap Flat(float value) => new(Enumerable.Repeat(value, 16 * 16).ToArray(), 16, 16);

    [Fact]
    public void DepthContrastSpreadsBunchedDepths()
    {
        var bunched = new DepthMap([0, .01f, .02f, .03f, 1], 5, 1);
        Assert.Equal(bunched.Values, bunched.Equalized(0).Values);
        Assert.Equal([0, .25f, .5f, .75f, 1], bunched.Equalized(1).Values);
        var half = bunched.Equalized(.5f).Values;
        Assert.Equal(half.Order(), half);
        Assert.True(half[2] > .2f, $"half way: {string.Join(", ", half)}");
    }

    /// <summary>Depth reaches the haze: everything near leaves less haze than everything far.</summary>
    [Fact]
    public void DepthThinsTheHazeWhereThingsAreNear()
    {
        using var wall = Wall();
        var settings = new VolumetricLightSettings { Intensity = 0, Haze = 100, Distance = 30 };
        using var near = VolumetricLighting.Apply(wall, null, settings, Flat(1));
        using var far = VolumetricLighting.Apply(wall, null, settings, Flat(0));
        using var guessed = VolumetricLighting.Apply(wall, null, settings);
        Assert.True(Red(far, 80, 10) - Red(near, 80, 10) > .02f, $"near {Red(near, 80, 10)}, far {Red(far, 80, 10)}");
        Assert.NotEqual(Red(guessed, 80, 10), Red(near, 80, 10));
    }

    /// <summary>Distance clears the near part of the scene: the same depth gets less haze as Distance rises.</summary>
    [Fact]
    public void DistanceClearsTheNearerScene()
    {
        using var wall = Wall();
        // Near at the foot of the frame, far at the top.
        var values = new float[16 * 16];
        for (var y = 0; y < 16; y++) for (var x = 0; x < 16; x++) values[y * 16 + x] = y / 15f;
        var depth = new DepthMap(values, 16, 16);
        float At(double distance)
        {
            using var lit = VolumetricLighting.Apply(wall, null, new VolumetricLightSettings { Intensity = 0, Haze = 100, Distance = distance }, depth);
            return Red(lit, 80, 80);
        }
        Assert.True(At(0) > At(80) + .02f, $"{At(0)} against {At(80)}");
        var (start, end) = VolumetricLighting.Ramp(50, [.. Enumerable.Range(0, 101).Select(i => i / 100f)]);
        Assert.Equal(.5f, start, 2);
        Assert.True(end >= start + .45f);
        var guess = VolumetricLighting.Ramp(30, null);
        Assert.Equal(.18f, guess.Start, 4); Assert.Equal(.68f, guess.End, 4);
    }

    /// <summary>The subject is held near, and the band the model's nearness bleeds into takes the backdrop's depth.</summary>
    [Fact]
    public void TheSubjectsOutlineDecidesTheDepthAtItsEdge()
    {
        const int w = 100, h = 100;
        var outline = new float[w * h]; var values = new float[w * h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var inside = x is >= 30 and < 70 && y is >= 30 and < 70;
                outline[y * w + x] = inside ? 1 : 0;
                // The model's nearness bleeds 8 px past the subject into a backdrop at .2.
                values[y * w + x] = x is >= 22 and < 78 && y is >= 22 and < 78 ? .95f : .2f;
            }
        // The band is about as wide as the reach the backdrop is read from beyond, as it is at a photo's own size.
        var composed = VolumetricLighting.ComposedDepth(new DepthMap(values, w, h), outline, w, h, 400);
        Assert.True(composed[50 * w + 50] >= .9f, $"the subject is near: {composed[50 * w + 50]}");
        Assert.True(composed[50 * w + 26] < .6f, $"the band takes the backdrop's depth: {composed[50 * w + 26]}");
        Assert.InRange(composed[5 * w + 5], .15f, .25f);
    }

    [Fact]
    public async Task ADownloadThatDoesntMatchIsNotKept()
    {
        var folder = Directory.CreateTempSubdirectory("depth-model-").FullName;
        var previous = Environment.GetEnvironmentVariable("COMPOSITOR_DEPTH_MODEL_DIR");
        try
        {
            Environment.SetEnvironmentVariable("COMPOSITOR_DEPTH_MODEL_DIR", folder);
            DepthModel.Unload();
            DepthModel.Handler = new Canned(HttpStatusCode.OK, [1, 2, 3]);
            Assert.False(await DepthModel.PrepareAsync());
            Assert.False(DepthModel.IsReady);
            Assert.Empty(Directory.GetFiles(folder));
            DepthModel.Handler = new Canned(HttpStatusCode.NotFound, []);
            Assert.False(await DepthModel.PrepareAsync());
            Assert.Empty(Directory.GetFiles(folder));
            // A copy on disk that doesn't check out is removed, so the next try downloads afresh.
            File.WriteAllBytes(DepthModel.FilePath, [9, 9, 9]);
            Assert.False(DepthModel.Load(DepthModel.FilePath));
            Assert.False(File.Exists(DepthModel.FilePath));
            using var wall = Wall();
            Assert.Null(DepthModel.Depth(wall));
        }
        finally
        {
            DepthModel.Handler = null;
            Environment.SetEnvironmentVariable("COMPOSITOR_DEPTH_MODEL_DIR", previous);
            Directory.Delete(folder, true);
        }
    }

    private sealed class Canned(HttpStatusCode status, byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
    }

    [Fact]
    public void ExtractDepthAddsALayerAboveInTheLayersPlace()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 200, 100);
        var bitmap = Bitmaps.Allocate(Bitmaps.ColorInfo(40, 30)); bitmap.Erase(SKColors.Teal);
        var source = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(bitmap, "Photo"), new Model.LayerTransform(10, 20, 80, 60, 15), "Photo");
        var top = new ImageLayer(Guid.NewGuid(), null, new Model.LayerTransform(0, 0, 200, 100), "Top");
        document.Layers.Add(source); document.Layers.Add(top);
        using var wrong = Bitmaps.Allocate(Bitmaps.ColorInfo(10, 10));
        Assert.Null(VolumetricLighting.AddDepthLayer(document, source.ID, wrong, "Depth"));
        var depth = Bitmaps.Allocate(Bitmaps.ColorInfo(40, 30));
        var made = VolumetricLighting.AddDepthLayer(document, source.ID, depth, "Depth");
        Assert.Equal(made, document.Layers[1].ID);
        Assert.Equal(source.Transform, document.Layers[1].Transform);
        Assert.Equal("Depth", document.Layers[1].Name);
    }

    /// <summary>The real model, from a copy fetched by hand into <c>COMPOSITOR_DEPTH_MODEL_FILE</c>: it loads, and a
    /// floor running away under a wall comes out nearer at the foot of the frame. Passes without looking when unset.</summary>
    [Fact]
    public void TheModelSeesTheFloorNearerThanTheWall()
    {
        if (Environment.GetEnvironmentVariable("COMPOSITOR_DEPTH_MODEL_FILE") is not { Length: > 0 } path || !File.Exists(path)) return;
        var copy = Path.Combine(Directory.CreateTempSubdirectory("depth-model-").FullName, "model.onnx");
        File.Copy(path, copy);
        try
        {
            Assert.True(DepthModel.Load(copy));
            using var scene = Bitmaps.Allocate(Bitmaps.ColorInfo(320, 240));
            using (var canvas = new SKCanvas(scene))
            using (var paint = new SKPaint { IsAntialias = true })
            {
                canvas.Clear(new SKColor(120, 130, 140));
                // A tiled floor in perspective, its lines meeting at the horizon.
                paint.Color = new SKColor(90, 70, 50);
                canvas.DrawRect(SKRect.Create(0, 120, 320, 120), paint);
                paint.Color = new SKColor(40, 30, 20); paint.StrokeWidth = 2; paint.Style = SKPaintStyle.Stroke;
                for (var x = -320; x <= 640; x += 40) canvas.DrawLine(160, 120, x, 240, paint);
                for (var y = 0; y < 6; y++) { var row = 120 + 120 * (1 - MathF.Pow(.6f, y + 1)); canvas.DrawLine(0, row, 320, row, paint); }
            }
            var depth = DepthModel.Depth(scene);
            Assert.NotNull(depth);
            Assert.All(depth.Values, v => Assert.InRange(v, 0, 1));
            float Mean(int fromRow, int toRow) => (float)Enumerable.Range(fromRow, toRow - fromRow)
                .SelectMany(y => Enumerable.Range(0, depth.Width).Select(x => depth.Values[y * depth.Width + x])).Average();
            Assert.True(Mean(depth.Height * 9 / 10, depth.Height) > Mean(0, depth.Height / 4), "the floor at the foot is nearer than the wall");
            using var map = VolumetricLighting.DepthImage(scene, null, new VolumetricLightSettings());
            Assert.NotNull(map);
            Assert.Equal((320, 240), (map.Width, map.Height));
        }
        finally { DepthModel.Unload(); Directory.Delete(Path.GetDirectoryName(copy)!, true); }
    }
}

[CollectionDefinition(nameof(DepthModel), DisableParallelization = true)]
public sealed class DepthModelCollection;
