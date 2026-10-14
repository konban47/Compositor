using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private void RawImportSelfCheck()
    {
        static void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
        void Pump() { Dispatcher.UIThread.RunJobs(); UpdateLayout(); foreach (var w in OwnedWindows) w.UpdateLayout(); }
        foreach (var choice in new[] { "raw-cancel", "raw-develop", "raw-open-camera" })
        {
            var bitmap = new SKBitmap(Bitmaps.ColorInfo(24, 16)); bitmap.Erase(SKColors.Gray);
            var image = ImportedImage.Create(bitmap, "RAW test");
            var operation = RawDevelopDialog.Develop(this, image); Pump();
            var dialog = OwnedWindows.OfType<RawDevelopDialog>().Single();
            dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, choice)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 300 && !operation.IsCompleted; i++) { Pump(); Thread.Sleep(10); }
            Check(operation.IsCompleted, "RAW development did not finish."); var result = operation.GetAwaiter().GetResult();
            Check(choice == "raw-cancel" ? result is null : result is not null && result.OpenCameraRaw == (choice == "raw-open-camera"), "RAW dialog choice was lost.");
            if (result is not null) result.Image.Dispose(); else image.Dispose();
        }
        var target = _open; var layer = Selected!.Value;
        _importingDrop = true; QueueImportedCameraRaw(target, layer);
        Check(_cameraRaw is null, "Camera Raw opened before the import batch completed.");
        _importingDrop = false; OpenImportedCameraRaw(); Pump();
        Check(_cameraRaw is not null && _cameraRawLayer == layer && ReferenceEquals(_open, target) && _cameraRawAfterImport is null, "RAW import opened the wrong layer or left a pending request.");
        CloseCameraRaw(); OpenImportedCameraRaw(); Check(_cameraRaw is null, "RAW request replayed after cancellation.");
        _cameraRawAfterImport = (target, Guid.NewGuid()); OpenImportedCameraRaw(); Check(_cameraRaw is null, "RAW filter opened for a removed layer.");
    }
}
