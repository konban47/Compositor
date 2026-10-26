using Compositor.Core.Format;
using Compositor.Core.Model;
using ImageMagick;
using SkiaSharp;

namespace Compositor.Core.IO;

/// <summary>Assigning changes the interpretation of document channel values, never its assets.
/// The editor stores sRGB-normalized assets, so a fixed encoding profile recovers the original
/// document values before interpreting them with the newly assigned profile.</summary>
public static class ProfileAssignment
{
    public static string Model(ImageSettings image) => image.Mode == ImageMode.CMYK ? "CMYK" : image.Mode == ImageMode.Grayscale ? "GRAY" : "RGB";
    public static byte[]? Current(ImageSettings image) => image.Mode == ImageMode.CMYK ? image.CmykProfile : image.ColorProfile;
    public static ImageSettings Assign(ImageSettings image, byte[]? profile, ColorManagementSettings settings)
    {
        if (image.Mode is not (ImageMode.RGB or ImageMode.CMYK or ImageMode.Grayscale)) throw new InvalidOperationException("Assign Profile requires an RGB, CMYK or grayscale document.");
        if (profile is not null && (!IccProfiles.Valid(profile) || IccProfiles.Model(profile) != Model(image))) throw new InvalidDataException("The ICC profile must match the document color model.");
        var previous = Current(image);
        if (previous is null && profile is null || previous is not null && profile is not null && previous.AsSpan().SequenceEqual(profile)) return image;
        var encoding = image.ProfileEncoding ?? previous ?? IccProfiles.Resolve(settings.ProfileName(image.Mode), settings);
        return image with { ProfileEncoding = encoding.ToArray(), ColorProfile = image.Mode == ImageMode.CMYK ? image.ColorProfile : profile?.ToArray(), CmykProfile = image.Mode == ImageMode.CMYK ? profile?.ToArray() : image.CmykProfile };
    }
    public static void ApplyDisplay(SKBitmap bitmap, ImageSettings image)
    {
        if (image.ProfileEncoding is not { } encoding) return;
        // Untagged RGB is displayed as sRGB. Non-RGB documents still need a channel model.
        var target = Current(image) ?? (Model(image) == "RGB" ? ColorProfiles.SRGB.ToByteArray() : IccProfiles.Resolve(ColorDefaults.Settings.ProfileName(image.Mode), ColorDefaults.Settings));
        if (encoding.AsSpan().SequenceEqual(target)) return;
        var settings = ColorDefaults.Settings with { Dither = false };
        var srgb = ColorProfiles.SRGB.ToByteArray();
        using var input = DeepImageCodec.Magick(bitmap);
        using var values = ColorTransforms.Convert(input, srgb, encoding, settings);
        // Replacing this tag deliberately preserves channel numbers.
        values.RemoveProfile("icc"); values.RemoveProfile("icm");
        values.SetProfile(new ColorProfile(target));
        using var display = ColorTransforms.Convert(values, target, srgb, settings);
        using var pixels = Document.AdvancedEdit.Pixels(display);
        using var output = SKImage.FromBitmap(pixels);
        if (!output.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0)) throw new IOException("Could not display the assigned profile.");
    }
}
