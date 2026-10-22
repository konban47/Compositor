using System.Text;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.IO.PSD;

namespace Compositor.Core.Tests;

public partial class PsdImportTests
{
    [Fact] public void ArtboardDescriptorOpensItsOffCanvasContentsAsASeparateDocument()
    {
        using var descriptor = new MemoryStream(); var d = new BeWriter(descriptor); d.U32(16); d.U32(0); d.U32(0); d.Ascii("null"); d.U32(1);
        d.U32(12); d.Ascii("artboardRect"); d.Ascii("Objc"); d.U32(0); d.U32(0); d.Ascii("Rct1"); d.U32(4);
        foreach (var (key, value) in new[] { ("Left", 30d), ("Top ", 20d), ("Rght", 38d), ("Btom", 26d) }) { d.U32(0); d.Ascii(key); d.Ascii("doub"); d.U64(BitConverter.DoubleToInt64Bits(value)); }
        var group = new LayerSpec { Name = "Board", Section = 1 }; group.Additional.Add(("artb", descriptor.ToArray()));
        var image = new LayerSpec { Name = "Contents", Left = 30, Top = 20, Right = 38, Bottom = 26, ChannelIds = [0, 1, 2], Planes = [Plane(8, 6, 240), Plane(8, 6, 120), Plane(8, 6, 60)] };
        using var file = new MemoryStream(); var w = new BeWriter(file); Header(w, 1, 3); w.U32(0); w.U32(0);
        var section = LayerSection([new LayerSpec { Name = "End", Section = 3 }, image, group], false); w.U32(section.Length); w.Bytes(section); w.U16(0); w.Bytes(new byte[CanvasWidth * CanvasHeight * 3]);
        var boards = PsdArtboards.Read(Write("artboards.psd", file.ToArray())); Assert.Single(boards); Assert.Equal("Board", boards[0].Name);
        using var snapshot = boards[0].Import.Snapshot(); using var doc = snapshot.ToDocument(); Assert.Equal(8, doc.Width); Assert.Equal(6, doc.Height);
        Assert.Single(doc.Layers); Assert.Equal(0, doc.Layers[0].Transform.X); Assert.Equal(0, doc.Layers[0].Transform.Y); Assert.Equal(240, doc.Layers[0].Asset!.Image.GetPixel(2, 2).Red);
    }
    [Fact]
    public void TypeDescriptorImportsUnicodeAndPerCharacterFormattingAsEditableText()
    {
        var layer = SyntheticLayer(); layer.Additional.Add(("TySh", TypeDescriptor()));
        var import = PsdImporter.Read(Write("editable.psd", MetadataFile(layer)));
        using var snapshot = import.Snapshot(); ProjectStore.Validate(snapshot.Manifest); using var doc = snapshot.ToDocument();
        var text = doc.Layers.Single().LiveText!; Assert.NotNull(text); Assert.Equal("中文AB", text.Content);
        Assert.Equal(14, TextRanges.At(text, 0).FontSize); Assert.Equal(24, TextRanges.At(text, 2).FontSize);
        Assert.True(TextRanges.At(text, 2).Bold); Assert.Equal(1, TextRanges.At(text, 2).Red);
        var editing = TextSession.Editing(doc.Layers.Single()); Assert.True(editing.Type(doc, "C")); Assert.Equal("中文ABC", editing.Content);
    }

    [Theory]
    [InlineData(0, true, true)]
    [InlineData(6, false, false)]
    public void VectorMaskImportsPathEnabledAndLinkedFlags(int flags, bool enabled, bool linked)
    {
        var layer = SyntheticLayer(); layer.Additional.Add(("vmsk", VectorDescriptor(flags)));
        var import = PsdImporter.Read(Write("vector.psd", MetadataFile(layer)));
        using var snapshot = import.Snapshot(); ProjectStore.Validate(snapshot.Manifest); using var doc = snapshot.ToDocument();
        var mask = doc.Layers.Single().Mask!; Assert.NotNull(mask.VectorPath); Assert.True(mask.VectorEvenOdd);
        Assert.Equal(enabled, mask.IsEnabled); Assert.Equal(linked, mask.IsLinked);
        using var path = mask.CreateVectorPath(); Assert.True(path!.Contains(4, 3)); Assert.False(path.Contains(0, 0));
        var project = Path.Combine(_root, "vector.comp"); ProjectStore.Save(snapshot, project);
        using var loaded = ProjectStore.Load(project); Assert.True(loaded.Manifest.Layers.Single().MaskVectorEvenOdd);
    }

    [Fact]
    public void DamagedTypeDescriptorKeepsTheStoredPixelsAndReportsFallback()
    {
        var layer = SyntheticLayer(); layer.Additional.Add(("TySh", TypeDescriptor()[..60]));
        var import = PsdImporter.Read(Write("damaged-text.psd", MetadataFile(layer)));
        using var snapshot = import.Snapshot(); Assert.Null(snapshot.Manifest.Layers.Single().Text);
        Assert.Contains(import.Notes, n => n.What.Contains("could not be decoded")); Assert.Single(import.Images);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void CmykPhotoshopLayersAndMergedImageConvertUsingAnInkProfile(bool layered)
    {
        using var stream = new MemoryStream(); var w = new BeWriter(stream); Header(w, 1, 4); w.U32(0); w.U32(0);
        if (layered)
        {
            var layer = SyntheticLayer(); layer.ChannelIds = [0, 1, 2, 3]; layer.Planes = [Plane(CanvasWidth, CanvasHeight, 0), Plane(CanvasWidth, CanvasHeight, 255), Plane(CanvasWidth, CanvasHeight, 255), Plane(CanvasWidth, CanvasHeight, 255)];
            var section = LayerSection([layer], false); w.U32(section.Length); w.Bytes(section);
        }
        else w.U32(0);
        w.U16(0); foreach (var value in new byte[] { 0, 255, 255, 255 }) w.Bytes(Enumerable.Repeat(value, CanvasWidth * CanvasHeight).ToArray());
        var bytes = stream.ToArray(); bytes[25] = 4;
        var imported = PsdImporter.Read(Write("cmyk.psd", bytes)); using var snapshot = imported.Snapshot(); ProjectStore.Validate(snapshot.Manifest);
        Assert.True(CmykColor.IsProfile(snapshot.Manifest.ImageSettings!.CmykProfile));
        var color = snapshot.Images.Values.Single().Image.GetPixel(0, 0); Assert.True(color.Blue > color.Red && color.Green > color.Red);
    }

    private static LayerSpec SyntheticLayer() => new() { Name = "Fixture", Right = CanvasWidth, Bottom = CanvasHeight,
        ChannelIds = [0, 1, 2], Planes = [Plane(CanvasWidth, CanvasHeight, 240), Plane(CanvasWidth, CanvasHeight, 120), Plane(CanvasWidth, CanvasHeight, 60)] };
    private static byte[] MetadataFile(LayerSpec layer)
    {
        using var file = new MemoryStream(); var w = new BeWriter(file); Header(w, 1, 3); w.U32(0); w.U32(0);
        var section = LayerSection([layer], false); w.U32(section.Length); w.Bytes(section); w.U16(0); w.Bytes(new byte[CanvasWidth * CanvasHeight * 3]); return file.ToArray();
    }
    private static byte[] TypeDescriptor()
    {
        using var file = new MemoryStream(); var w = new BeWriter(file); w.U16(1);
        foreach (var value in new[] { 1d, 0, 0, 1, 12, 24 }) w.U64(BitConverter.DoubleToInt64Bits(value));
        w.U16(50); w.U32(16); w.U32(0); w.U32(0); w.Ascii("TxLr"); w.U32(2);
        w.U32(0); w.Ascii("Txt "); w.Ascii("TEXT"); var units = Encoding.BigEndianUnicode.GetBytes("中文AB\r"); w.U32(units.Length / 2); w.Bytes(units);
        w.U32(10); w.Ascii("EngineData"); w.Ascii("tdta");
        var engine = Encoding.ASCII.GetBytes("<< /ResourceDict << /FontSet [ << /Name (ArialMT) >> ] >> /EngineDict << /StyleRun << /RunLengthArray [2 3] /RunArray [ << /StyleSheet << /StyleSheetData << /Font 0 /FontSize 14 >> >> >> << /StyleSheet << /StyleSheetData << /Font 0 /FontSize 24 /FauxBold true /FillColor << /Values [1 1 0 0] >> >> >> >> ] >> >> >>");
        w.U32(engine.Length); w.Bytes(engine); return file.ToArray();
    }
    private static byte[] VectorDescriptor(int flags)
    {
        using var file = new MemoryStream(); var w = new BeWriter(file); w.U32(3); w.U32(flags); w.U16(0); w.U16(4); w.Bytes(new byte[22]);
        foreach (var (x, y) in new[] { (.25, .25), (.75, .25), (.75, .75), (.25, .75) })
        { w.U16(1); for (var i = 0; i < 3; i++) { w.I32((int)(y * 16777216)); w.I32((int)(x * 16777216)); } }
        return file.ToArray();
    }
}
