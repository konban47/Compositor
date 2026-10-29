using System.Text;
using Compositor.Core.IO;

namespace Compositor.Core.Tests;

public sealed class DicomImportTests : ProjectTestBase
{
    [Fact]
    public void MultipleFilesBecomeIndependentLayers()
    {
        var first = Sample("first.dcm", false);
        var second = Sample("second.dcm", true);
        using var doc = DicomImport.Load([first, second]);
        Assert.Equal(2, doc.Width); Assert.Equal(2, doc.Height);
        Assert.Equal(16, doc.Image.Depth);
        Assert.Equal(2, doc.Layers.Count);
        Assert.True(doc.Layers[0].Asset!.Image.GetPixel(0, 0).Red < doc.Layers[0].Asset!.Image.GetPixel(1, 1).Red);
        Assert.True(doc.Layers[1].Asset!.Image.GetPixel(0, 0).Red > doc.Layers[1].Asset!.Image.GetPixel(1, 1).Red);
    }

    [Fact]
    public void CancellationDoesNotReturnPartialDocument()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => DicomImport.Load([Sample("first.dcm", false)], cancellation.Token));
    }

    [Fact]
    public void MultiframeDicomImportsEachFrameRatherThanRepeatingTheFirst()
    {
        using var doc = DicomImport.Load([Sample("frames.dcm", false, true)]);
        Assert.Equal(2, doc.Layers.Count);
        Assert.True(doc.Layers[0].Asset!.Image.GetPixel(0, 0).Red < doc.Layers[1].Asset!.Image.GetPixel(0, 0).Red);
    }

    private string Sample(string name, bool reverse, bool multiple = false)
    {
        var path = PathIn(name);
        using var stream = File.Create(path); using var writer = new BinaryWriter(stream);
        writer.Write(new byte[128]); writer.Write(Encoding.ASCII.GetBytes("DICM"));
        void Tag(ushort group, ushort element, string vr, byte[] bytes)
        {
            writer.Write(group); writer.Write(element); writer.Write(Encoding.ASCII.GetBytes(vr));
            if (vr is "OW" or "OB") { writer.Write((ushort)0); writer.Write((uint)bytes.Length); }
            else writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }
        void Text(ushort group, ushort element, string vr, string value)
        {
            if (value.Length % 2 != 0) value += vr == "UI" ? "\0" : " ";
            Tag(group, element, vr, Encoding.ASCII.GetBytes(value));
        }
        void Number(ushort element, ushort value) => Tag(0x0028, element, "US", BitConverter.GetBytes(value));
        Text(0x0002, 0x0010, "UI", "1.2.840.10008.1.2.1");
        Text(0x0008, 0x0016, "UI", "1.2.840.10008.5.1.4.1.1.7");
        Text(0x0008, 0x0018, "UI", "2.25.123456");
        Number(0x0002, 1); Text(0x0028, 0x0004, "CS", "MONOCHROME2");
        if (multiple) Text(0x0028, 0x0008, "IS", "2");
        Number(0x0010, 2); Number(0x0011, 2);
        Number(0x0100, 16); Number(0x0101, 16); Number(0x0102, 15); Number(0x0103, 0);
        ushort[] values = reverse ? [65535, 40000, 10000, 0] : [0, 10000, 40000, 65535];
        if (multiple) values = values.Concat(values.Reverse()).ToArray();
        Tag(0x7fe0, 0x0010, "OW", values.SelectMany(BitConverter.GetBytes).ToArray());
        return path;
    }
}
