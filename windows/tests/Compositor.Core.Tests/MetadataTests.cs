using System.Text;
using System.Xml;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using ImageMagick;

namespace Compositor.Core.Tests;

public sealed class MetadataTests
{
    private static DocumentMetadata Sample => new()
    {
        Title = "中文标题 & <test>", Author = "Artist", Description = "Line one\nLine two",
        Copyright = "Copyright 2026", Email = "artist@example.com", Website = "https://example.com",
        Keywords = "art;photo", CameraMake = "Camera", CameraModel = "Model", DateTaken = "2026-10-29",
    };

    [Fact]
    public void PortableXmpRoundTripsAndRejectsEntities()
    {
        var bytes = ImageMetadata.Encode(Sample, MetadataPolicy.All)!;
        Assert.Equal(Sample with { Keywords = "art; photo" }, ImageMetadata.Decode(bytes));
        Assert.Throws<XmlException>(() => ImageMetadata.Decode(Encoding.UTF8.GetBytes(
            "<!DOCTYPE x [<!ENTITY secret SYSTEM 'file:///private'>]><x>&secret;</x>")));
        Assert.False((Sample with { Title = "\0" }).IsValid);
        Assert.False((Sample with { Title = new string('x', 32769) }).IsValid);
        Assert.Null(ImageMetadata.Encode(new(), MetadataPolicy.All));
    }

    public static IEnumerable<object[]> WebPolicies =>
        from format in new[] { WebFormat.Png24, WebFormat.Png8, WebFormat.Jpeg, WebFormat.Gif }
        from policy in Enum.GetValues<MetadataPolicy>()
        select new object[] { format, policy };

    [Theory]
    [MemberData(nameof(WebPolicies))]
    public void PrivacyPoliciesControlEncodedOutput(WebFormat format, MetadataPolicy policy)
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 8, 8) { Metadata = Sample };
        using var result = WebExport.Encode(document, new(8, 8, format, Metadata: policy));
        using var image = new MagickImage(result.Bytes);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + (format == WebFormat.Gif ? ".gif" : ".png"));
        DocumentMetadata actual;
        try
        {
            File.WriteAllBytes(path, result.Bytes);
            actual = ImageMetadata.Read(path);
        }
        finally { File.Delete(path); }
        if (policy == MetadataPolicy.None) { Assert.True(actual.IsEmpty); return; }
        Assert.Equal(Sample.Copyright, actual.Copyright);
        Assert.Equal(policy >= MetadataPolicy.CopyrightAndContact ? Sample.Email : "", actual.Email);
        Assert.Equal(policy >= MetadataPolicy.AllExceptCamera ? Sample.Title : "", actual.Title);
        Assert.Equal(policy == MetadataPolicy.All ? Sample.CameraModel : "", actual.CameraModel);
        Assert.Null(image.GetColorProfile());
    }

    [Fact]
    public void TruncatedGifMetadataDoesNotEscapeImport()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 8, 8) { Metadata = Sample };
        using var result = ExportImage.Encode(document, new(ExportFormat.Gif, 8, 8, Metadata: MetadataPolicy.All));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gif");
        try
        {
            foreach (var length in new[] { 0, 6, 13, 25, 40, result.Bytes.Length / 2 })
            {
                File.WriteAllBytes(path, result.Bytes[..length]);
                Assert.True(ImageMetadata.Read(path).IsEmpty);
            }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(ExportFormat.Png)]
    [InlineData(ExportFormat.Jpeg)]
    [InlineData(ExportFormat.Tiff)]
    [InlineData(ExportFormat.Gif)]
    public void ExportMetadataPreservesDecodedPixelsAndSurvivesImport(ExportFormat format)
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 8, 8) { Metadata = Sample };
        using var plain = ExportImage.Encode(document, new(format, 8, 8));
        using var tagged = ExportImage.Encode(document, new(format, 8, 8, Metadata: MetadataPolicy.AllExceptCamera));
        using var before = new MagickImage(plain.Bytes);
        using var after = new MagickImage(tagged.Bytes);
        Assert.Equal(0, before.Compare(after, ErrorMetric.Absolute));
        Assert.Null(before.GetXmpProfile());
        var actual = format == ExportFormat.Gif ? Sample with { Keywords = "art; photo", CameraMake = "", CameraModel = "", DateTaken = "" } : ImageMetadata.Decode(after.GetXmpProfile()!.ToByteArray()!);
        Assert.Equal(Sample.Title, actual.Title);
        Assert.Empty(actual.CameraMake);
        var extension = format == ExportFormat.Jpeg ? "jpg" : format.ToString().ToLowerInvariant();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "." + extension);
        try
        {
            File.WriteAllBytes(path, tagged.Bytes);
            Assert.Equal(actual, ImageMetadata.Read(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ProjectAndSnapshotsPreserveMetadata()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 8, 8) { Metadata = Sample };
        using var snapshot = ProjectSnapshot.FromDocument(document);
        Assert.Equal(31, snapshot.Manifest.Version);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".comp");
        try
        {
            ProjectStore.Save(snapshot, path);
            using var loaded = ProjectStore.Load(path);
            using var restored = loaded.ToDocument();
            Assert.Equal(Sample, restored.Metadata);
            using var clone = restored.Clone();
            restored.Metadata = new();
            Assert.Equal(Sample, clone.Metadata);
            Assert.NotEqual(restored.Metadata, clone.Metadata);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        snapshot.Manifest.Version = 30;
        Assert.Throws<ProjectException>(() => ProjectStore.Validate(snapshot.Manifest));
    }
}
