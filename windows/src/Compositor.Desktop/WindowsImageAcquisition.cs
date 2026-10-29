using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Compositor.Core.IO;
using Compositor.Core.Model;

namespace Compositor.Desktop;

internal static class WindowsImageAcquisition
{
    internal static Task<ImportedImage?> Acquire()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("WIA requires Windows.");
        var completion = new TaskCompletionSource<ImportedImage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("WIA requires Windows.");
                completion.SetResult(AcquireOnSta());
            }
            catch (Exception error) { completion.SetException(error); }
        }) { IsBackground = true, Name = "Compositor WIA" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    [SupportedOSPlatform("windows")]
    private static ImportedImage? AcquireOnSta()
    {
        object? dialog = null;
        object? image = null;
        var path = Path.Combine(Path.GetTempPath(), "Compositor-scan-" + Guid.NewGuid().ToString("N") + ".bmp");
        try
        {
            var type = Type.GetTypeFromProgID("WIA.CommonDialog")
                ?? throw new NotSupportedException("Windows Image Acquisition is not installed.");
            dialog = Activator.CreateInstance(type)!;
            // A scanner selection/driver dialog handles resolution, color mode and native cancellation.
            image = ((dynamic)dialog).ShowAcquireImage(1, 0, 131072,
                "{B96B3CAB-0728-11D3-9D7B-0000F81EF32E}", true, true, false);
            if (image is null) return null;
            ((dynamic)image).SaveFile(path);
            return ImageImporter.Decode(path);
        }
        catch (COMException error) when (error.HResult == unchecked((int)0x80210015))
        { throw new IOException("No WIA scanner is available. Connect a scanner and install its WIA driver.", error); }
        finally
        {
            if (image is not null && Marshal.IsComObject(image)) Marshal.FinalReleaseComObject(image);
            if (dialog is not null && Marshal.IsComObject(dialog)) Marshal.FinalReleaseComObject(dialog);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
