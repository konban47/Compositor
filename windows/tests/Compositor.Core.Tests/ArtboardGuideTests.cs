using System.Text.Json;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Xunit;

namespace Compositor.Core.Tests;

public sealed class ArtboardGuideTests : ProjectTestBase
{
    [Fact]
    public void OwnedGuidesRoundTripWithFormat21AndKeepTheirArtboard()
    {
        using var doc = new CanvasDocument(Guid.NewGuid(), 500, 300);
        var owner = Guid.NewGuid();
        doc.Layers.Add(new(owner, null, new(0, 0, 400, 200), "Artboard") { IsGroup = true, Container = LayerContainer.Artboard });
        doc.Guides.Add(new() { ID = Guid.NewGuid(), Axis = GuideAxis.Vertical, Position = 120, ArtboardID = owner });
        var snapshot = ProjectSnapshot.FromDocument(doc); Assert.Equal(21, snapshot.Manifest.Version);
        var path = PathIn("guides.comp"); ProjectStore.Save(snapshot, path);
        using var loaded = ProjectStore.Load(path).ToDocument();
        Assert.Equal(owner, Assert.Single(loaded.Guides).ArtboardID);
        Assert.Equal(owner, loaded.Clone().Guides[0].ArtboardID);
        using var independent = DocumentCopies.Independent(loaded);
        Assert.Equal(owner, independent.Guides[0].ArtboardID);
        CanvasRotation.Rotate(loaded, true);
        Assert.Equal(owner, loaded.Guides[0].ArtboardID);
        Assert.Equal(GuideAxis.Horizontal, loaded.Guides[0].Axis);
        Assert.Equal(120, independent.Guides[0].Position);
    }

    [Fact]
    public void CanvasGuidesRemainCompatibleAndDoNotWriteNullOwners()
    {
        using var doc = new CanvasDocument(Guid.NewGuid(), 200, 100);
        doc.Guides.Add(new() { ID = Guid.NewGuid(), Axis = GuideAxis.Horizontal, Position = 40 });
        Assert.True(ProjectSnapshot.FromDocument(doc).Manifest.Version < 21);
        Assert.DoesNotContain("ArtboardID", JsonSerializer.Serialize(doc.Guides[0]));
    }
}
