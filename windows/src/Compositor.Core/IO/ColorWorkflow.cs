using Compositor.Core.Format;
using ImageMagick;

namespace Compositor.Core.IO;

/// <summary>ICC policy resolution is explicit: assets stay normalized to sRGB; the document
/// retains its working/output profile so a managed export can reconstruct device values.</summary>
public static class ColorWorkflow
{
    public static MagickImage Import(MagickImage image, ImageMode mode, ColorManagementSettings settings, out byte[]? documentProfile)
    {
        var embedded = image.GetColorProfile()?.ToByteArray();
        var working = IccProfiles.Resolve(settings.ProfileName(mode), settings);
        var policy = settings.Policy(mode);
        var source = policy != ColorPolicy.Off && IccProfiles.Valid(embedded) ? embedded! : working;
        documentProfile = policy == ColorPolicy.Preserve && IccProfiles.Valid(embedded) ? embedded : working;
        if (policy == ColorPolicy.Convert && !source.AsSpan().SequenceEqual(working))
        {
            using var intermediate = ColorTransforms.Convert(image, source, working, settings);
            return ColorTransforms.Convert(intermediate, working, ColorProfiles.SRGB.ToByteArray(), settings);
        }
        return ColorTransforms.Convert(image, source, ColorProfiles.SRGB.ToByteArray(), settings);
    }
    public static string? Warning(string path, bool paste, ColorManagementSettings settings)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".tif" or ".tiff" or ".webp" or ".bmp" or ".heic" or ".heif" or ".avif")) return null;
        using var image = new MagickImage(); image.Ping(path); var embedded = image.GetColorProfile()?.ToByteArray();
        // Ping may omit profiles for some formats; read metadata only with the file's resource limits.
        var mode = image.ColorSpace == ColorSpace.CMYK ? ImageMode.CMYK : image.ColorSpace == ColorSpace.Gray ? ImageMode.Grayscale : ImageMode.RGB;
        if (embedded is null) return !paste && settings.AskMissingOpen ? "This image has no embedded ICC profile. Interpret it using the selected working space?" : null;
        var working = IccProfiles.Resolve(settings.ProfileName(mode), settings);
        return (paste ? settings.AskMismatchPaste : settings.AskMismatchOpen) && !embedded.AsSpan().SequenceEqual(working) ? "The embedded ICC profile differs from the working space. Continue using the selected color management policy?" : null;
    }
}
