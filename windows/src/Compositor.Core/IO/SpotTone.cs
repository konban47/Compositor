using ImageMagick;
namespace Compositor.Core.IO;

public static class SpotTone
{
    /// <summary>Paper coverage for the existing duotone preview; this is not a plate/separation export.</summary>
    public static double[] Curve(ColorManagementSettings settings)
    {
        var data = Enumerable.Range(0, 256).SelectMany(i => new byte[] { (byte)i, 255 }).ToArray();
        using var image = new MagickImage(); image.ReadPixels(data, new PixelReadSettings(256, 1, StorageType.Char, "IA")); image.ColorSpace = ColorSpace.Gray;
        using var converted = ColorTransforms.Convert(image, IccProfiles.Resolve(settings.Spot, settings), IccProfiles.Gray("Linear Gray", 1), settings);
        return converted.GetPixels().ToShortArray("I")!.Select(n => n / 65535d).ToArray();
    }
}
