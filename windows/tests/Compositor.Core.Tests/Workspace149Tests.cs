using System.Net;
using System.Text.Json;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;
using Xunit;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Tests;

public sealed class Workspace149Tests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => action(request, token); }
    private static byte[] Png()
    { using var b = new SKBitmap(4, 3); b.Erase(SKColors.Cyan); using var d = b.Encode(SKEncodedImageFormat.Png, 100); return d.ToArray(); }
    [Theory]
    [InlineData("https://images.example/v1", "https://images.example/v1/images/generations")]
    [InlineData("http://localhost:8190/v1/", "http://localhost:8190/v1/images/generations")]
    [InlineData("https://images.example/custom/images/generations", "https://images.example/custom/images/generations")]
    public void GenerationEndpoints(string address, string expected) => Assert.Equal(expected, ImageGenerationClient.Endpoint(address).AbsoluteUri);
    [Theory]
    [InlineData("http://images.example/v1")]
    [InlineData("file:///secret")]
    [InlineData("https://user:password@example.com")]
    [InlineData("https://example.com/?key=secret")]
    public void RejectsUnsafeEndpoint(string address) => Assert.Throws<ArgumentException>(() => ImageGenerationClient.Endpoint(address));
    [Fact]
    public async Task GenerationPostsProtocolAndReadsBase64()
    {
        using var http = new HttpClient(new Handler(async (request, token) => {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme); Assert.Equal("test-only", request.Headers.Authorization?.Parameter);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("画一个图标", json.RootElement.GetProperty("prompt").GetString());
            Assert.Equal("provider-model", json.RootElement.GetProperty("model").GetString());
            Assert.False(json.RootElement.TryGetProperty("response_format", out _));
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { data = new[] { new { b64_json = Convert.ToBase64String(Png()) } } })) };
        }));
        var result = await new ImageGenerationClient(http).Generate(new("https://example.com/v1", "provider-model", "画一个图标"), "test-only");
        Assert.Equal((4, 3), (result.Single().Width, result.Single().Height));
    }
    [Fact]
    public async Task ResultDownloadDoesNotForwardKey()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((request, _) => {
            calls++;
            if (calls == 1) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[{\"url\":\"https://cdn.example/result.png\"}]}") });
            Assert.Null(request.Headers.Authorization); Assert.Equal("cdn.example", request.RequestUri!.Host);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png()) });
        }));
        Assert.Single(await new ImageGenerationClient(http).Generate(new("https://example.com", "model", "prompt"), "test-only"));
        Assert.Equal(2, calls);
    }
    [Fact]
    public async Task ErrorNeverEchoesServiceBody()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("private-key-and-prompt") })));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => new ImageGenerationClient(http).Generate(new("https://example.com", "model", "prompt"), "test-only"));
        Assert.DoesNotContain("private", error.ToString()); Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }
    [Fact]
    public async Task CancellationStopsGeneration()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        using var http = new HttpClient(new Handler((_, token) => Task.FromCanceled<HttpResponseMessage>(token)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ImageGenerationClient(http).Generate(new("https://example.com", "model", "prompt"), "", cancel.Token));
    }
    [Fact]
    public void ToolbarPresetsRoundTripAndAddNewTools()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try {
            var layout = new ToolbarLayout { Groups = [["Move", "Hand"], ["Brush"]], ShowColors = false, DisableExtraShortcuts = true };
            layout.Save(file, ["Move", "Hand", "Brush"]);
            var loaded = ToolbarLayout.Load(file, ["Move", "Hand", "Brush", "Zoom"]);
            Assert.Equal(new[] { "Move", "Hand" }, loaded.Groups[0]); Assert.Equal("Zoom", loaded.Extras.Single());
            Assert.False(loaded.ShowColors); Assert.True(loaded.DisableExtraShortcuts);
            loaded.Groups[0].Add("Brush"); Assert.Throws<InvalidDataException>(() => loaded.Save(file, ["Move", "Hand", "Brush", "Zoom"]));
            Assert.Equal(2, ToolbarLayout.Load(file, ["Move", "Hand", "Brush"]).Groups[0].Count);
        } finally { File.Delete(file); }
    }
    private static CanvasDocument Document(params LayerTransform[] transforms)
    {
        var d = new CanvasDocument(Guid.NewGuid(), 300, 200);
        foreach (var t in transforms) { var b = new SKBitmap(Bitmaps.ColorInfo(8, 8)); b.Erase(SKColors.Red); d.Layers.Add(new(Guid.NewGuid(), ImportedImage.Create(b, "test"), t, "layer")); }
        return d;
    }
    [Fact]
    public void AlignRotatedBoundsToSelectionAndRespectLock()
    {
        using var d = Document(new(30, 40, 80, 20, 90), new(110, 20, 20, 20));
        SelectionEdits.Select(d, SKRectI.Create(10, 10, 200, 120));
        d.Layers[1].Locks = LayerLocks.All;
        var locked = d.Layers[1].Transform;
        Assert.True(LayerAlignment.Apply(d, d.Layers.Select(l => l.ID).ToArray(), "Align Left", AlignmentTarget.Selection));
        Assert.Equal(10, new[] { d.Layers[0].Transform.Point(0,0).X, d.Layers[0].Transform.Point(1,1).X }.Min(), 4);
        Assert.Equal(locked, d.Layers[1].Transform);
    }
    [Fact]
    public void DistributeEdgesAndEqualGaps()
    {
        using var d = Document(new(10, 0, 20, 20), new(55, 0, 40, 20), new(200, 0, 60, 20));
        var ids = d.Layers.Select(l => l.ID).ToArray();
        Assert.True(LayerAlignment.Apply(d, ids, "Distribute Left", AlignmentTarget.SelectedLayers)); Assert.Equal(105, d.Layers[1].Transform.X);
        Assert.True(LayerAlignment.Apply(d, ids, "Distribute Horizontal Spacing", AlignmentTarget.SelectedLayers));
        Assert.Equal(d.Layers[1].Transform.X - 30, 200 - d.Layers[1].Transform.X - 40, 6);
    }
    [Fact]
    public void AllShapeTemplatesProduceEditableNonemptyPaths()
    {
        foreach (var svg in ShapeTemplates.Presets.Values.Concat(new[] { ShapeTemplates.Polygon(3), ShapeTemplates.Polygon(7), ShapeTemplates.Polygon(5, .45) }))
        { using var path = SKPath.ParseSvgPathData(ShapeTemplates.Normalize(svg)); Assert.NotNull(path); Assert.InRange(path.TightBounds.Width, .999f, 1.001f); Assert.InRange(path.TightBounds.Height, .999f, 1.001f); }
    }
    [Fact]
    public void ResizeScalesEffectsAndResolutionOnlyPreservesLiveText()
    {
        using var d = Document(new LayerTransform(10, 20, 40, 40)); var l = d.Layers[0];
        var fx = new LayerEffects { Stroke = new() { Size = 4 }, Shadow = new() { Distance = 12, Blur = 6 } }; l.Effects = fx;
        Assert.True(ImageEdits.Resize(d, 600, 400, 300));
        Assert.Equal(8, l.Effects!.Stroke!.Size); Assert.Equal(24, l.Effects.Shadow!.Distance); Assert.Equal(4, fx.Stroke!.Size);
    }
    [Fact]
    public void CropAndResolutionOnlyKeepLiveMetadata()
    {
        using var d = Document(new LayerTransform(10,20,40,40)); var layer=d.Layers[0];
        var text=new LayerText(new LayerTextStyle { Content="editable" },layer.Asset!.Image); layer.Text=text;
        layer.Effects=new LayerEffects { Stroke=new StrokeEffect { Size=3 } };
        Assert.True(ImageEdits.Resize(d,300,200,144)); Assert.Same(text.Style,layer.LiveText);
        Assert.True(CanvasEdits.Crop(d,SKRectI.Create(5,5,280,180))); Assert.Same(text.Style,layer.LiveText); Assert.Equal(3,layer.Effects.Stroke.Size);
        Assert.True(ImageEdits.Resize(d,560,360,144)); Assert.Null(layer.LiveText); Assert.Equal(6,layer.Effects.Stroke.Size);
    }
    [Theory]
    [InlineData(true,223)]
    [InlineData(false,30)]
    public void CameraRaw149VignettesMatchUpstream(bool lens,int expected)
    {
        using var d=new CanvasDocument(Guid.NewGuid(),300,200); var b=new SKBitmap(Bitmaps.ColorInfo(300,200));b.Erase(new SKColor(128,128,128));
        var layer=new ImageLayer(Guid.NewGuid(),ImportedImage.Create(b,"gray"),new(0,0,300,200),"gray");d.Layers.Add(layer);
        var s=lens?new CameraRawSettings { OpticsVignetteAmount=100 }:new CameraRawSettings { VignetteAmount=-60 };
        Assert.True(CameraRawEdits.Apply(d,layer.ID,s));Assert.InRange((int)layer.Asset!.Image.GetPixel(0,0).Red,expected-6,expected+6);
    }
    [Theory]
    [InlineData(0,230,115,0,189,130,107)]
    [InlineData(1,230,92,92,227,125,83)]
    [InlineData(2,92,92,230,142,142,248)]
    [InlineData(3,92,92,230,149,149,226)]
    [InlineData(4,230,92,92,228,118,37)]
    [InlineData(5,130,130,130,101,101,101)]
    public void CameraRaw149MatchesUpstreamMeasuredCases(int kind, byte r, byte g, byte b, int er, int eg, int eb)
    {
        using var d = Document(new LayerTransform(0,0,8,8)); var l = d.Layers[0]; l.Asset!.Image.Erase(new SKColor(r,g,b));
        var s = new CameraRawSettings();
        switch(kind) { case 0:s.Mixer[9]=-60;break;case 1:s.Mixer[0]=60;break;case 2:s.Mixer[21]=60;break;case 3:s.BlueSaturation=-60;break;case 4:s.RedHue=60;break;case 5:s.CurveDarks=-60;break; }
        Assert.True(CameraRawEdits.Apply(d,l.ID,s)); var c=l.Asset.Image.GetPixel(0,0);
        Assert.InRange((int)c.Red,er-4,er+4); Assert.InRange((int)c.Green,eg-4,eg+4); Assert.InRange((int)c.Blue,eb-4,eb+4);
    }
}
