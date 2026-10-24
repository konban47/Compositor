using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using ImageMagick;
using SkiaSharp;

namespace Compositor.Core.Tests;

[CollectionDefinition("Color globals", DisableParallelization = true)] public sealed class ColorCollection { }
[Collection("Color globals")]
public sealed class ColorManagementTests : ProjectTestBase
{
    private static MagickImage Samples()
    {
        var data = Enumerable.Range(0, 125).SelectMany(i => new byte[] { (byte)(i % 5 * 63), (byte)(i / 5 % 5 * 63), (byte)(i / 25 * 63), (byte)(128 + i) }).ToArray();
        var image = new MagickImage(); image.ReadPixels(data, new PixelReadSettings(125, 1, StorageType.Char, "RGBA")); image.ColorSpace = ColorSpace.sRGB; return image;
    }
    [Theory]
    [InlineData(ColorIntent.Perceptual, false)] [InlineData(ColorIntent.Perceptual, true)]
    [InlineData(ColorIntent.Saturation, false)] [InlineData(ColorIntent.Saturation, true)]
    [InlineData(ColorIntent.Relative, false)] [InlineData(ColorIntent.Relative, true)]
    [InlineData(ColorIntent.Absolute, false)] [InlineData(ColorIntent.Absolute, true)]
    public void IndependentMatrixEngineMatchesReferenceAndPreservesAlpha(ColorIntent intent, bool black)
    {
        using var input = Samples(); var source = ColorProfiles.SRGB.ToByteArray(); var target = ColorProfiles.AdobeRGB1998.ToByteArray();
        var settings = new ColorManagementSettings { Intent = intent, BlackPointCompensation = black };
        using var actual = ColorTransforms.Convert(input, source, target, settings);
        using var reference = ColorTransforms.Convert(input, source, target, settings with { Engine = ColorEngine.LittleCms });
        var a = actual.GetPixels().ToShortArray("RGBA")!; var b = reference.GetPixels().ToShortArray("RGBA")!;
        Assert.InRange(a.Zip(b, (x, y) => Math.Abs((int)x - y)).Max(), 0, 45);
        Assert.Equal(input.GetPixels().ToShortArray("A"), actual.GetPixels().ToShortArray("A"));
        Assert.Equal(target, actual.GetColorProfile()!.ToByteArray());
    }
    [Theory] [InlineData(ColorIntent.Perceptual)] [InlineData(ColorIntent.Saturation)] [InlineData(ColorIntent.Relative)] [InlineData(ColorIntent.Absolute)]
    public void IndependentCmykLutsFollowIntentTables(ColorIntent intent)
    {
        using var input = Samples(); var from = ColorProfiles.SRGB.ToByteArray();
        foreach (var profile in new[] { ColorProfiles.USWebCoatedSWOP, ColorProfiles.CoatedFOGRA39 })
        {
            var to = profile.ToByteArray(); var settings = new ColorManagementSettings { Intent = intent, BlackPointCompensation = false };
            using var actual = ColorTransforms.Convert(input, from, to, settings);
            using var reference = ColorTransforms.Convert(input, from, to, settings with { Engine = ColorEngine.LittleCms });
            var a = actual.GetPixels().ToShortArray("CMYKA")!; var b = reference.GetPixels().ToShortArray("CMYKA")!;
            Assert.InRange(a.Zip(b, (x, y) => Math.Abs((int)x - y)).Max() / 65535d, 0, .025);
            using var restored = ColorTransforms.Convert(actual, to, from, settings);
            Assert.Equal(input.GetPixels().ToShortArray("A"), restored.GetPixels().ToShortArray("A"));
        }
    }
    [Fact] public void RelativeBlackPointCompensationLiftsPressBlackAndAbsoluteIgnoresIt()
    {
        using var input = new MagickImage(MagickColors.Black, 1, 1); var from = ColorProfiles.SRGB.ToByteArray(); var to = ColorProfiles.USWebCoatedSWOP.ToByteArray();
        using var no = ColorTransforms.Convert(input, from, to, new() { Intent = ColorIntent.Relative, BlackPointCompensation = false });
        using var yes = ColorTransforms.Convert(input, from, to, new() { Intent = ColorIntent.Relative, BlackPointCompensation = true });
        Assert.NotEqual(no.GetPixels().ToShortArray("CMYK"), yes.GetPixels().ToShortArray("CMYK"));
        using var absoluteA = ColorTransforms.Convert(input, from, to, new() { Intent = ColorIntent.Absolute, BlackPointCompensation = false });
        using var absoluteB = ColorTransforms.Convert(input, from, to, new() { Intent = ColorIntent.Absolute, BlackPointCompensation = true });
        Assert.Equal(absoluteA.GetPixels().ToShortArray("CMYK"), absoluteB.GetPixels().ToShortArray("CMYK"));
    }
    [Theory] [InlineData(ColorEngine.AceCompatible)] [InlineData(ColorEngine.LittleCms)] [InlineData(ColorEngine.MicrosoftIcm)]
    public void AllEnginesUseCustomGrayProfiles(ColorEngine engine)
    {
        if (engine == ColorEngine.MicrosoftIcm && !OperatingSystem.IsWindows()) return;
        using var input = new MagickImage(MagickColors.Red, 1, 1); var gray = IccProfiles.Gray("Regression Gamma 2.2", 2.2);
        using var converted = ColorTransforms.Convert(input, ColorProfiles.SRGB.ToByteArray(), gray, new() { Engine = engine });
        Assert.InRange(converted.GetPixels().ToShortArray("I")![0], 33050, 33150);
    }
    [Fact] public void InvalidProfileTagsAndNullSettingsAreRejected()
    {
        using var input = Samples(); var bad = ColorProfiles.SRGB.ToByteArray(); BinaryPrimitives.WriteUInt32BigEndian(bad.AsSpan(136), uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => ColorTransforms.Convert(input, bad, ColorProfiles.AdobeRGB1998.ToByteArray()));
        Assert.False((new ColorManagementSettings { Rgb = null! }).IsValid); Assert.False((new ColorManagementSettings { CustomProfiles = null! }).IsValid);
        Assert.False((new OcioSettings { WorkingSpace = null! }).IsValid);
    }
    [Theory] [InlineData("mft1")] [InlineData("mAB ")] [InlineData("mBA ")]
    public void LegacyAndV4LutStagesAgreeWithReference(string type)
    {
        static void U32(byte[] b, int p, int value) => BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(p), value);
        static void Sig(byte[] b, int p, string value) => System.Text.Encoding.ASCII.GetBytes(value).CopyTo(b, p);
        byte[] lut;
        if (type == "mft1")
        {
            lut = new byte[48 + 768 + 24 + 768]; lut[10] = 2; for (var i = 0; i < 3; i++) U32(lut, 12 + (i * 3 + i) * 4, 65536);
            for (var c = 0; c < 3; c++) for (var i = 0; i < 256; i++) { lut[48 + c * 256 + i] = (byte)i; lut[48 + 768 + 24 + c * 256 + i] = (byte)i; }
            for (var i = 0; i < 8; i++) for (var c = 0; c < 3; c++) lut[48 + 768 + i * 3 + c] = (byte)(((i >> (2 - c)) & 1) * 128);
        }
        else if (type == "mAB ")
        {
            lut = new byte[172]; U32(lut, 12, 136); U32(lut, 24, 68); U32(lut, 28, 32);
            for (var c = 0; c < 3; c++) { Sig(lut, 32 + c * 12, "curv"); Sig(lut, 136 + c * 12, "curv"); lut[68 + c] = 2; } lut[84] = 2;
            for (var i = 0; i < 8; i++) for (var c = 0; c < 3; c++) BinaryPrimitives.WriteUInt16BigEndian(lut.AsSpan(88 + (i * 3 + c) * 2), (ushort)(((i >> (2 - c)) & 1) * 32768));
        }
        else
        {
            lut = new byte[152]; U32(lut, 12, 32); U32(lut, 16, 68); U32(lut, 20, 116);
            for (var c = 0; c < 3; c++) { Sig(lut, 32 + c * 12, "curv"); Sig(lut, 116 + c * 12, "curv"); U32(lut, 68 + (c * 3 + c) * 4, 131070); }
        }
        Sig(lut, 0, type); lut[8] = lut[9] = 3;
        var baseProfile = ColorProfiles.SRGB.ToByteArray(); var tags = new List<(string Name, byte[] Data)>(); var count = BinaryPrimitives.ReadInt32BigEndian(baseProfile.AsSpan(128));
        for (var i = 0; i < count; i++) { var at = 132 + i * 12; var offset = BinaryPrimitives.ReadInt32BigEndian(baseProfile.AsSpan(at + 4)); var length = BinaryPrimitives.ReadInt32BigEndian(baseProfile.AsSpan(at + 8)); tags.Add((System.Text.Encoding.ASCII.GetString(baseProfile, at, 4), baseProfile.AsSpan(offset, length).ToArray())); }
        tags.Add((type == "mBA " ? "B2A0" : "A2B0", lut)); var result = new byte[132 + tags.Count * 12 + tags.Sum(t => (t.Data.Length + 3) / 4 * 4)]; baseProfile.AsSpan(0, 128).CopyTo(result); result[8] = type == "mft1" ? (byte)2 : (byte)4; U32(result, 0, result.Length); U32(result, 128, tags.Count); var position = 132 + tags.Count * 12;
        for (var i = 0; i < tags.Count; i++) { var at = 132 + i * 12; Sig(result, at, tags[i].Name); U32(result, at + 4, position); U32(result, at + 8, tags[i].Data.Length); tags[i].Data.CopyTo(result, position); position += (tags[i].Data.Length + 3) / 4 * 4; }
        using var input = Samples(); var source = type == "mBA " ? baseProfile : result; var target = type == "mBA " ? result : baseProfile;
        using var actual = ColorTransforms.Convert(input, source, target, new() { BlackPointCompensation = false }); using var reference = ColorTransforms.Convert(input, source, target, new() { Engine = ColorEngine.LittleCms, BlackPointCompensation = false });
        // The legacy table has 8-bit output precision: allow one table code after TRC conversion.
        Assert.InRange(actual.GetPixels().ToShortArray("RGBA")!.Zip(reference.GetPixels().ToShortArray("RGBA")!, (a, b) => Math.Abs((int)a - b)).Max(), 0, type == "mft1" ? 257 : 80);
    }
    [Fact] public void TruncatedCurveTagCannotReadIntoTheNextTag()
    {
        var profile = IccProfiles.Gray("Truncated", 2.2);
        var count = BinaryPrimitives.ReadInt32BigEndian(profile.AsSpan(128));
        for (var i = 0; i < count; i++)
        {
            var at = 132 + i * 12;
            if (System.Text.Encoding.ASCII.GetString(profile, at, 4) == "kTRC") BinaryPrimitives.WriteInt32BigEndian(profile.AsSpan(at + 8), 8);
        }
        using var input = Samples();
        Assert.Throws<InvalidDataException>(() => ColorTransforms.Convert(input, ColorProfiles.SRGB.ToByteArray(), profile));
    }
    [Fact] public void SceneProfileCompensationChangesContrastAndPreservesAlpha()
    {
        var profile = ColorProfiles.SRGB.ToByteArray();
        var count = BinaryPrimitives.ReadInt32BigEndian(profile.AsSpan(128));
        for (var i = 0; i < count; i++)
        {
            var at = 132 + i * 12;
            if (System.Text.Encoding.ASCII.GetString(profile, at, 4) != "desc") continue;
            var offset = BinaryPrimitives.ReadInt32BigEndian(profile.AsSpan(at + 4)); var length = BinaryPrimitives.ReadInt32BigEndian(profile.AsSpan(at + 8));
            profile.AsSpan(offset, length).Clear(); System.Text.Encoding.ASCII.GetBytes("desc").CopyTo(profile, offset);
            BinaryPrimitives.WriteInt32BigEndian(profile.AsSpan(offset + 8), 9); System.Text.Encoding.ASCII.GetBytes("sceneRGB").CopyTo(profile, offset + 12);
        }
        Assert.Equal("sceneRGB", IccProfiles.Name(profile, "missing"));
        using var input = Samples(); var srgb = ColorProfiles.SRGB.ToByteArray();
        using var plain = ColorTransforms.Convert(input, profile, srgb);
        using var adjusted = ColorTransforms.Convert(input, profile, srgb, new() { CompensateSceneProfiles = true });
        Assert.NotEqual(plain.GetPixels().ToShortArray("RGB"), adjusted.GetPixels().ToShortArray("RGB"));
        Assert.Equal(input.GetPixels().ToShortArray("A"), adjusted.GetPixels().ToShortArray("A"));
    }
    [Fact] public void UnicodeGrayNamesAndSpotCoverageArePreserved()
    {
        var bytes = IccProfiles.Gray("自定网点增大", 0, 15); Assert.Equal("自定网点增大", IccProfiles.Name(bytes, "missing"));
        var a = SpotTone.Curve(new() { Spot = "Dot Gain 10%" }); var b = SpotTone.Curve(new() { Spot = "Dot Gain 30%" }); Assert.True(b[128] < a[128] - .15); Assert.Equal(0, a[0], 3); Assert.Equal(1, a[255], 3);
    }
    [Fact] public void OcioSceneCompensationUsesTheActualDisplayView()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var input = Bitmaps.Allocate(DeepPixels.Info(1, 1)); var samples = MemoryMarshal.Cast<byte, float>(input.GetPixelSpan()); samples[0] = samples[1] = samples[2] = 4; samples[3] = .5f;
        var editor = OpenColorIo.Inspect(OcioSettings.Builtin).EditorSpace;
        using var normal = OpenColorIo.Convert(input, OcioSettings.Builtin, "scene_linear", editor);
        using var rendered = OpenColorIo.Convert(input, OcioSettings.Builtin, "scene_linear", editor, displayView: true);
        var a = MemoryMarshal.Cast<byte, float>(normal.GetPixelSpan()); var b = MemoryMarshal.Cast<byte, float>(rendered.GetPixelSpan()); Assert.True(a[0] > 1); Assert.InRange(b[0], .5f, 1); Assert.Equal(.5f, b[3]);
    }
    [Fact] public void WorkingPolicyDiffersFromPreserveWithoutLosingOutputProfile()
    {
        using var input = Samples(); input.SetProfile(ColorProfiles.AdobeRGB1998);
        using var preserve = ColorWorkflow.Import(input, ImageMode.RGB, new() { RgbPolicy = ColorPolicy.Preserve }, out var embedded);
        using var off = ColorWorkflow.Import(input, ImageMode.RGB, new() { RgbPolicy = ColorPolicy.Off }, out var working);
        Assert.Equal(ColorProfiles.AdobeRGB1998.ToByteArray(), embedded); Assert.Equal(ColorProfiles.SRGB.ToByteArray(), working);
        Assert.NotEqual(preserve.GetPixels().ToShortArray("RGB"), off.GetPixels().ToShortArray("RGB"));
        using var convert = ColorWorkflow.Import(input, ImageMode.RGB, new() { RgbPolicy = ColorPolicy.Convert }, out var convertedProfile);
        Assert.Equal(working, convertedProfile);
    }
    [Fact] public void SettingsRoundTripCustomCurveAndRejectNullJson()
    {
        var path = PathIn("color.json"); var gray = IccProfiles.Gray("Custom Dot Gain", 0, custom: new double[] { 2, 4, 6, 8, 10, 25, 45, 60, 70, 80, 88, 93, 98 });
        var settings = new ColorManagementSettings { Gray = "Custom Dot Gain", CustomProfiles = new() { ["Custom Dot Gain"] = gray }, Engine = ColorEngine.AceCompatible, Intent = ColorIntent.Relative };
        settings.Save(path); var copy = ColorManagementSettings.Load(path); Assert.Equal(gray, IccProfiles.Resolve(copy.Gray, copy)); Assert.Equal(settings.Intent, copy.Intent);
        File.WriteAllText(path, "{\"CustomProfiles\":null}"); Assert.Throws<InvalidDataException>(() => ColorManagementSettings.Load(path));
    }
    [Fact] public void ImportedProfileSurvivesProjectAndManagedTiffExport()
    {
        using var source = Samples(); source.SetProfile(ColorProfiles.AdobeRGB1998); source.Depth = 16;
        using var imported = DeepImageCodec.Decode(source.ToByteArray(MagickFormat.Tiff), "ICC", manageColor: true);
        using var doc = ImageImporter.NewDocument(imported); Assert.Equal(ColorProfiles.AdobeRGB1998.ToByteArray(), doc.Image.ColorProfile);
        using var snapshot = ProjectSnapshot.FromDocument(doc); Assert.Equal(28, snapshot.Manifest.Version); var folder = PathIn("color.comp"); ProjectStore.Save(snapshot, folder);
        using var loaded = ProjectStore.Load(folder); Assert.Equal(doc.Image.ColorProfile, loaded.Manifest.ImageSettings!.ColorProfile);
        snapshot.Manifest.Version = 27; Assert.Throws<ProjectException>(() => ProjectStore.Validate(snapshot.Manifest));
        using var encoded = new MagickImage(DeepImageCodec.EncodeMode(imported.WorkingImage, doc.Image, true, 300)); Assert.Equal(doc.Image.ColorProfile, encoded.GetColorProfile()!.ToByteArray()); Assert.Equal(16u, encoded.Depth);
    }
    [Fact] public void GammaBlendingChangesLightnessWithoutChangingAlphaAndDesaturationIsSeparate()
    {
        using var source = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul)); source.Erase(new SKColor(255, 255, 255, 128));
        using var linear = source.Copy(); linear.Erase(SKColors.Black); using var encoded = linear.Copy();
        ColorControls.Composite(source, linear, 0, 0, 1, 1); ColorControls.Composite(source, encoded, 0, 0, 1, 2.2);
        Assert.InRange(linear.GetPixel(0, 0).Red, 185, 188); Assert.InRange(encoded.GetPixel(0, 0).Red, 127, 129); Assert.Equal(encoded.GetPixel(0, 0).Alpha, linear.GetPixel(0, 0).Alpha);
        linear.Erase(SKColors.Red); ColorControls.Desaturate(linear, 1); Assert.Equal(linear.GetPixel(0, 0).Red, linear.GetPixel(0, 0).Green);
    }
    [Fact] public void OfficialOcioBuiltinUsesRolesAndPreservesHdrAndAlpha()
    {
        if (!OperatingSystem.IsWindows()) return;
        var info = OpenColorIo.Inspect(OcioSettings.Builtin); Assert.Equal("2.4.2", info.Version); Assert.Contains(info.Roles, r => r.Name == "scene_linear"); Assert.NotNull(info.Find("matte_paint"));
        using var input = Bitmaps.Allocate(DeepPixels.Info(2, 1)); var p = MemoryMarshal.Cast<byte, float>(input.GetPixelSpan()); p[0] = p[1] = p[2] = .18f; p[3] = .25f; p[4] = p[5] = p[6] = 2; p[7] = 1;
        using var converted = OpenColorIo.Convert(input, OcioSettings.Builtin, "scene_linear", info.EditorSpace); var v = MemoryMarshal.Cast<byte, float>(converted.GetPixelSpan());
        Assert.InRange(v[0], .46f, .463f); Assert.Equal(.25f, v[3]); Assert.True(v[4] > 1);
        using var restored = OpenColorIo.Convert(converted, OcioSettings.Builtin, info.EditorSpace, "scene_linear"); var r = MemoryMarshal.Cast<byte, float>(restored.GetPixelSpan()); Assert.InRange(r[0], .179f, .181f); Assert.InRange(r[4], 1.99f, 2.01f);
        Assert.Throws<InvalidDataException>(() => OpenColorIo.Validate(new() { Enabled = true, WorkingSpace = "missing-space" }));
    }
}
