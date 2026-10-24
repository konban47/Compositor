using System.ComponentModel;
using System.Runtime.InteropServices;
using Compositor.Core.Document;
using Compositor.Core.Model;
using ImageMagick;
using SkiaSharp;

namespace Compositor.Core.IO;

public static class ColorTransforms
{
    public static MagickImage Convert(MagickImage input, byte[] source, byte[] destination, ColorManagementSettings? settings = null)
    {
        settings ??= ColorDefaults.Settings;
        if (!IccProfiles.Valid(source) || !IccProfiles.Valid(destination)) throw new InvalidDataException("Invalid ICC profile.");
        if (source.AsSpan().SequenceEqual(destination)) { var copy = (MagickImage)input.Clone(); copy.SetProfile(new ColorProfile(destination)); return copy; }
        if (settings.CompensateSceneProfiles && IsSceneProfile(source) != IsSceneProfile(destination))
        {
            var plain = settings with { CompensateSceneProfiles = false }; var srgb = ColorProfiles.SRGB.ToByteArray();
            using var normalized = Convert(input, source, srgb, plain);
            ApplySceneContrast(normalized, IsSceneProfile(source) ? 1.2 : 1 / 1.2);
            return Convert(normalized, srgb, destination, plain);
        }
        if (settings.Engine == ColorEngine.MicrosoftIcm) return WindowsIcm.Convert(input, source, destination, settings.Intent);
        if (settings.Engine == ColorEngine.AceCompatible) return AceCompatibleEngine.Convert(input, source, destination, settings);
        var result = (MagickImage)input.Clone();
        try
        {
            result.RenderingIntent = settings.Intent switch { ColorIntent.Saturation => RenderingIntent.Saturation, ColorIntent.Relative => RenderingIntent.Relative, ColorIntent.Absolute => RenderingIntent.Absolute, _ => RenderingIntent.Perceptual };
            result.BlackPointCompensation = settings.BlackPointCompensation;
            result.TransformColorSpace(new ColorProfile(source), new ColorProfile(destination)); return result;
        }
        catch { result.Dispose(); throw; }
    }
    public static SKBitmap Convert(SKBitmap input, byte[] source, byte[] destination, ColorManagementSettings? settings = null)
    {
        using var original = DeepImageCodec.Magick(input); using var converted = Convert(original, source, destination, settings); return AdvancedEdit.Pixels(converted);
    }
    private static bool IsSceneProfile(byte[] profile)
    {
        if (IccProfiles.Model(profile) != "RGB") return false;
        var name = IccProfiles.Name(profile, "");
        return name.Contains("scene", StringComparison.OrdinalIgnoreCase) || name.Contains("HDTV (Rec. 709)", StringComparison.OrdinalIgnoreCase) || name.StartsWith("SDTV", StringComparison.OrdinalIgnoreCase);
    }
    // Independent scene/display compensation: a 1.2 power on linear luminance.
    // OCIO-managed scenes instead use their configuration's actual display/view transform.
    private static void ApplySceneContrast(MagickImage image, double gamma)
    {
        image.ColorType = ColorType.TrueColorAlpha; using var pixels = image.GetPixelsUnsafe(); var row = new float[checked((int)image.Width * 4)];
        static double Linear(double x) => x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4);
        static double Encoded(double x) => x <= .0031308 ? x * 12.92 : 1.055 * Math.Pow(x, 1 / 2.4) - .055;
        for (var y = 0; y < image.Height; y++)
        {
            var pointer = pixels.GetAreaPointer(0, y, image.Width, 1); Marshal.Copy(pointer, row, 0, row.Length);
            for (var x = 0; x < row.Length; x += 4)
            { var red = Linear(row[x] / 65535d); var green = Linear(row[x + 1] / 65535d); var blue = Linear(row[x + 2] / 65535d); var luma = Math.Max(1e-9, red * .2126 + green * .7152 + blue * .0722); var gain = Math.Pow(luma, gamma - 1); row[x] = (float)(Encoded(red * gain) * 65535); row[x + 1] = (float)(Encoded(green * gain) * 65535); row[x + 2] = (float)(Encoded(blue * gain) * 65535); }
            Marshal.Copy(row, 0, pointer, row.Length);
        }
    }
}

internal static class WindowsIcm
{
    [StructLayout(LayoutKind.Sequential)] private struct Profile { public uint Type; public IntPtr Data; public uint Size; }
    // COLOR contains a reserved pointer, so its x64 ABI size is sixteen bytes.
    [StructLayout(LayoutKind.Explicit, Size = 16)] private struct Color
    { [FieldOffset(0)] public ushort A; [FieldOffset(2)] public ushort B; [FieldOffset(4)] public ushort C; [FieldOffset(6)] public ushort D; }
    [DllImport("mscms.dll", SetLastError = true)] private static extern IntPtr OpenColorProfileW(ref Profile profile, uint access, uint share, uint creation);
    [DllImport("mscms.dll")] private static extern bool CloseColorProfile(IntPtr profile);
    [DllImport("mscms.dll", SetLastError = true)] private static extern IntPtr CreateMultiProfileTransform(IntPtr[] profiles, uint count, uint[] intents, uint intentCount, uint flags, uint preferred);
    [DllImport("mscms.dll")] private static extern bool DeleteColorTransform(IntPtr transform);
    [DllImport("mscms.dll", SetLastError = true)] private static extern bool TranslateColors(IntPtr transform, [In] Color[] source, uint count, uint inputType, [Out] Color[] destination, uint outputType);
    private static uint Type(string model) => model == "CMYK" ? 7u : model == "GRAY" ? 1u : 2u;
    private static string Mapping(string model) => model == "GRAY" ? "I" : model;
    public static MagickImage Convert(MagickImage input, byte[] source, byte[] destination, ColorIntent intent)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Microsoft ICM requires Windows.");
        var pins = new[] { GCHandle.Alloc(source, GCHandleType.Pinned), GCHandle.Alloc(destination, GCHandleType.Pinned) };
        var profiles = new IntPtr[2]; var transform = IntPtr.Zero;
        try
        {
            for (var i = 0; i < 2; i++) { var profile = new Profile { Type = 2, Data = pins[i].AddrOfPinnedObject(), Size = (uint)(i == 0 ? source.Length : destination.Length) }; profiles[i] = OpenColorProfileW(ref profile, 1, 1, 3); if (profiles[i] == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error()); }
            uint renderingIntent = intent switch { ColorIntent.Saturation => 2, ColorIntent.Relative => 1, ColorIntent.Absolute => 3, _ => 0 };
            transform = CreateMultiProfileTransform(profiles, 2, [renderingIntent], 1, 3, 0);
            if (transform == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            var from = IccProfiles.Model(source); var to = IccProfiles.Model(destination); var inputMap = Mapping(from); var outputMap = Mapping(to);
            var width = checked((int)input.Width); var height = checked((int)input.Height); var channels = outputMap.Length;
            MemoryBudget.Current.Check(checked((long)width * height * (channels + 1) * 6));
            var values = new ushort[checked(width * height * (channels + 1))]; var src = new Color[width]; var dst = new Color[width];
            using var pixels = input.GetPixels();
            for (var y = 0; y < height; y++)
            {
                var row = pixels.ToShortArray(0, y, input.Width, 1, inputMap + "A")!; var n = inputMap.Length;
                for (var x = 0; x < width; x++) { var p = x * (n + 1); src[x] = new Color { A = row[p], B = n > 1 ? row[p + 1] : (ushort)0, C = n > 2 ? row[p + 2] : (ushort)0, D = n > 3 ? row[p + 3] : (ushort)0 }; }
                if (!TranslateColors(transform, src, (uint)width, Type(from), dst, Type(to))) throw new Win32Exception(Marshal.GetLastWin32Error());
                for (var x = 0; x < width; x++) { var p = (y * width + x) * (channels + 1); values[p] = dst[x].A; if (channels > 1) values[p + 1] = dst[x].B; if (channels > 2) values[p + 2] = dst[x].C; if (channels > 3) values[p + 3] = dst[x].D; values[p + channels] = row[x * (n + 1) + n]; }
            }
            var result = new MagickImage();
            try { result.ReadPixels(MemoryMarshal.AsBytes(values.AsSpan()).ToArray(), new PixelReadSettings(input.Width, input.Height, StorageType.Short, outputMap + "A")); result.ColorSpace = to == "CMYK" ? ColorSpace.CMYK : to == "GRAY" ? ColorSpace.Gray : ColorSpace.sRGB; result.SetProfile(new ColorProfile(destination)); return result; }
            catch { result.Dispose(); throw; }
        }
        finally { if (transform != IntPtr.Zero) DeleteColorTransform(transform); foreach (var profile in profiles) if (profile != IntPtr.Zero) CloseColorProfile(profile); foreach (var pin in pins) pin.Free(); }
    }
}
