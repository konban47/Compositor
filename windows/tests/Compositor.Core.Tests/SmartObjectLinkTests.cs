using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Tests;

/// <summary>Linked smart objects: placed from a file, updated when it changes, saved, embedded and packaged.</summary>
public sealed class SmartObjectLinkTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("smart-links-").FullName;
    public void Dispose() => Directory.Delete(_folder, true);

    private string Png(string name, SKColor color)
    {
        using var bitmap = Bitmaps.Allocate(Bitmaps.ColorInfo(16, 12)); bitmap.Erase(color);
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, PngCodec.Encode(bitmap));
        return path;
    }

    private static (CanvasDocument Document, ImageLayer Layer) Linked(string path)
    {
        var document = new CanvasDocument(Guid.NewGuid(), 40, 30);
        var bitmap = Bitmaps.Allocate(Bitmaps.ColorInfo(16, 12)); bitmap.Erase(SKColors.Gray);
        document.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(bitmap, "Placed"), new LayerTransform(4, 4, 16, 12), "Placed"));
        var smart = LayerWorkflow.ConvertToSmartObject(document, [document.Layers[0].ID])!.Value;
        Assert.True(SmartObjectLinks.Link(document, smart, path));
        return (document, document.Layers.Single(l => l.ID == smart));
    }

    [Fact]
    public void ALinkedFileIsReadAndFollowedWhenItChanges()
    {
        var path = Png("art.png", SKColors.Red);
        var (document, layer) = Linked(path);
        using var _ = document;
        Assert.Equal(path, layer.SmartObject!.LinkedPath);
        Assert.Equal(SKColors.Red, layer.Asset!.Image.GetPixel(2, 2));
        Assert.Equal(LinkState.Current, SmartObjectLinks.State(layer.SmartObject));
        Png("art.png", SKColors.Blue);
        Assert.Equal(LinkState.Modified, SmartObjectLinks.Links(document).Single().State);
        Assert.Equal(1, SmartObjectLinks.UpdateModified(document));
        layer = document.Layers.Single(l => l.SmartObject is not null);
        Assert.Equal(SKColors.Blue, layer.Asset!.Image.GetPixel(2, 2));
        Assert.Equal(0, SmartObjectLinks.UpdateModified(document));
        File.Delete(path);
        Assert.Equal(LinkState.Missing, SmartObjectLinks.State(layer.SmartObject!));
    }

    [Fact]
    public void TheLinkIsSavedAsVersion32AndEmbeddingForgetsIt()
    {
        var path = Png("art.png", SKColors.Green);
        var (document, layer) = Linked(path);
        using var _ = document;
        var project = Path.Combine(_folder, "Linked.comp");
        var snapshot = ProjectSnapshot.FromDocument(document);
        Assert.Equal(32, snapshot.Manifest.Version);
        ProjectStore.Save(snapshot, project);
        using (var loaded = ProjectStore.Load(project).ToDocument())
        {
            var smart = loaded.Layers.Single(l => l.SmartObject is not null).SmartObject!;
            Assert.Equal((path, layer.SmartObject!.LinkedHash), (smart.LinkedPath, smart.LinkedHash));
        }
        Assert.True(SmartObjectLinks.Embed(document, [layer.ID]));
        Assert.Null(document.Layers.Single(l => l.SmartObject is not null).SmartObject!.LinkedPath);
        Assert.True(ProjectSnapshot.FromDocument(document).Manifest.Version < 32);
        Assert.False(SmartObjectLinks.ValidLink("relative.png", new string('A', 64)));
        Assert.False(SmartObjectLinks.ValidLink(path, "not-a-hash"));
    }

    [Fact]
    public void PackagingCopiesTheLinkedFilesBesideTheProject()
    {
        var path = Png("art.png", SKColors.Orange);
        var (document, _) = Linked(path);
        using var __ = document;
        var package = Path.Combine(_folder, "Package");
        var (copied, missing) = SmartObjectLinks.Package(document, package);
        var copy = Assert.Single(copied);
        Assert.Empty(missing);
        Assert.Equal(Path.Combine(package, "Links", "art.png"), copy);
        Assert.Equal(copy, document.Layers.Single(l => l.SmartObject is not null).SmartObject!.LinkedPath);
        Assert.Equal(LinkState.Current, SmartObjectLinks.Links(document).Single().State);
    }
}
