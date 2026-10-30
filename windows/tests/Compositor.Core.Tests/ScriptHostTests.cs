using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Tests;

/// <summary>JavaScript and ExtendScript files run against a document, within the host's limits.</summary>
public sealed class ScriptHostTests
{
    private static CanvasDocument Document(out ImageLayer bottom, out ImageLayer top)
    {
        var document = new CanvasDocument(Guid.NewGuid(), 60, 40);
        ImageLayer Layer(string name) { var b = Bitmaps.Allocate(Bitmaps.ColorInfo(60, 40)); b.Erase(SKColors.Gray); return new ImageLayer(Guid.NewGuid(), ImportedImage.Create(b, name), new LayerTransform(0, 0, 60, 40), name); }
        bottom = Layer("Bottom"); top = Layer("Top");
        document.Layers.AddRange([bottom, top]);
        return document;
    }

    [Fact]
    public void ScriptsReadTheDocumentAndSpeak()
    {
        using var document = Document(out _, out _);
        var outcome = ScriptHost.Run("""
            var doc = app.activeDocument;
            alert(doc.name + " " + doc.width + "x" + doc.height);
            $.writeln(doc.layers.length + " layers, top is " + doc.layers[0].name);
            $.writeln(app.name);
            """, document, null, "Poster.comp");
        Assert.Null(outcome.Error);
        Assert.False(outcome.Changed);
        Assert.Equal(["Poster.comp 60x40"], outcome.Alerts);
        Assert.Equal(["2 layers, top is Top", "Compositor"], outcome.Output);
    }

    [Fact]
    public void ScriptsChangeLayersFileInfoAndSize()
    {
        using var document = Document(out var bottom, out var top);
        var outcome = ScriptHost.Run("""
            var doc = app.activeDocument;
            var layer = doc.layers[0];
            layer.name = "Renamed"; layer.opacity = 25; layer.visible = false; layer.blendMode = BlendMode.MULTIPLY;
            doc.layers[1].translate(5, -3);
            doc.info.author = "脚本"; doc.info.caption = "Made by a script";
            doc.resizeImage(30, 20);
            """, document, top.ID);
        Assert.Null(outcome.Error);
        Assert.True(outcome.Changed);
        Assert.Equal(("Renamed", .25, false, LayerBlendMode.Multiply), (top.Name, top.Opacity, top.IsVisible, top.BlendMode));
        Assert.Equal(("脚本", "Made by a script"), (document.Metadata.Author, document.Metadata.Description));
        Assert.Equal((30, 20), (document.Width, document.Height));
        // Moved (5, −3), then halved with the image, which keeps positions on whole pixels.
        Assert.InRange(bottom.Transform.X, 2, 3); Assert.InRange(bottom.Transform.Y, -2, -1);
        var unknown = ScriptHost.Run("app.activeDocument.layers[0].blendMode = 'SPARKLE';", document, null);
        Assert.Contains("SPARKLE", unknown.Error);
    }

    [Fact]
    public void ScriptsCannotReachTheMachineOrRunForever()
    {
        using var document = Document(out _, out _);
        foreach (var escape in new[] { "System.IO.File.Delete('x')", "importNamespace('System.IO')", "app.GetType().Assembly", "require('fs')" })
            Assert.NotNull(ScriptHost.Run(escape, document, null).Error);
        var forever = ScriptHost.Run("while (true) {}", document, null, timeout: TimeSpan.FromMilliseconds(300));
        Assert.Contains("too long", forever.Error);
        var deep = ScriptHost.Run("function f(n) { return f(n + 1); } f(0);", document, null);
        Assert.Contains("recursed", deep.Error);
        var broken = ScriptHost.Run("var = ;", document, null);
        Assert.StartsWith("The script could not be read", broken.Error);
        Assert.Equal("There is no open document.", ScriptHost.Run("app.activeDocument.name", null, null).Error?.Split(" (line")[0]);
    }

    [Fact]
    public void ALockedLayerRefusesAScript()
    {
        using var document = Document(out _, out var top);
        top.Locks = LayerLocks.All;
        var outcome = ScriptHost.Run("app.activeDocument.layers[0].name = 'x';", document, null);
        Assert.Contains("locked", outcome.Error);
        Assert.Equal("Top", top.Name);
    }
}
