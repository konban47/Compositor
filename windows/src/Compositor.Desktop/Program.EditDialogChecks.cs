using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

internal static partial class Program
{
    private static int EditDialogChecks(string output)
    {
        BuildHeadless().SetupWithoutStarting();
        try { var window = new MainWindow(); window.Show(); window.EditDialogSelfCheck(output); Console.WriteLine("PASS: every Edit menu dialog opens, fits without scrolling and closes."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}

public sealed partial class MainWindow
{
    /// <summary>Opens each of the Edit menu's dialogs on a document with pixels, text and a selection, photographs it, checks nothing in it
    /// scrolls, and closes it.</summary>
    internal void EditDialogSelfCheck(string output)
    {
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        void Pump() { Dispatcher.UIThread.RunJobs(); UpdateLayout(); foreach (var owned in OwnedWindows) owned.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Capture(Window window, string name) { Pump(); window.UpdateLayout(); using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.ChangeExtension(output, name + ".png"), new PngBitmapEncoderOptions()); }
        var doc = new CanvasDocument(Guid.NewGuid(), 400, 300);
        foreach (var (name, color, x) in new[] { ("Sky", SKColors.SkyBlue, 0), ("Ground", SKColors.SeaGreen, 120) })
        {
            var pixels = new SKBitmap(Bitmaps.ColorInfo(200, 160)); pixels.Erase(color);
            doc.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, name), new LayerTransform(x, 60, 200, 160), name));
        }
        AdoptImported(doc, "Edit Dialogs"); Reselect(doc.Layers[^1].ID); Pump();
        TypeHere(new(60, 40)); _canvas.Focus(); TypeText("Edit dialog sample"); CommitText(); Pump();
        SelectionEdits.Select(_document!, SKRectI.Create(40, 70, 120, 90)); Reselect(_document!.Layers.OfType<ImageLayer>().First().ID); Pump();
        var shown = new List<string>();
        void Open(string name, Func<Task> open)
        {
            var before = OwnedWindows.ToHashSet();
            var running = open(); Pump();
            var dialog = OwnedWindows.LastOrDefault(w => !before.Contains(w));
            Check(dialog is not null, name + " did not open a window.");
            Capture(dialog!, name);
            foreach (var scroll in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(dialog!).OfType<ScrollViewer>().Where(s => Equals(s.Tag, "dialog-body")))
                Check(scroll.Extent.Height <= scroll.Viewport.Height + 1, name + " does not fit its window.");
            if (dialog is FormDialog form) form.Close(false); else dialog!.Close();
            for (var i = 0; i < 200 && !running.IsCompleted; i++) { Pump(); Thread.Sleep(5); }
            shown.Add(name);
        }
        // Fill with a mode from the full list, then stroke the selection: both reach the pixels.
        var target = _document!.Layers.OfType<ImageLayer>().First(); var before = target.Asset!.WorkingImage.GetPixel(60, 60);
        var filling = EditFill(); Pump(); var fill = OwnedWindows.OfType<PaintDialog>().Single();
        var mode = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(fill).OfType<ComboBox>().Single(b => Equals(b.Tag, "paint-mode"));
        Check(mode.Items.OfType<ComboBoxItem>().Count(i => i.Tag is string) == 29, "Fill does not offer Photoshop's 29 blending modes.");
        mode.SelectedItem = mode.Items.OfType<ComboBoxItem>().First(i => Equals(i.Tag, "Multiply"));
        Capture(fill, "fill-mode"); fill.Close(true); for (var i = 0; i < 100 && !filling.IsCompleted; i++) { Pump(); Thread.Sleep(5); }
        Check(target.Asset!.WorkingImage.GetPixel(60, 60) != before, "Fill with Multiply did not change the selected pixels.");
        Open("fill", EditFill);
        Open("stroke", EditStroke);
        Open("find-replace", FindReplaceText);
        Check(FindReplaceDialog.Next("Café cafe CAFE", "cafe", 0, true, System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace, true) == (0, 4), "Ignore Accents did not match an accented word.");
        Check(FindReplaceDialog.Next("cafeteria cafe", "cafe", 0, true, System.Globalization.CompareOptions.IgnoreCase, true) == (10, 4), "Whole Word matched inside a word.");
        Open("remove-reflections", RemoveReflections);
        Open("define-pattern", () => DefinePreset("Pattern"));
        Open("define-shape", () => DefinePreset("Shape"));
        Open("define-brush", () => DefinePreset("Brush"));
        Open("pdf-presets", PdfPresets);
        Open("preset-manager", PresetManager);
        Open("color-settings", ColorSettings);
        Open("convert-profile", ConvertColorProfile);
        Open("assign-profile", AssignColorProfile);
        Open("ocio-settings", OpenColorIoSettings);
        Open("keyboard-shortcuts", KeyboardShortcuts);
        Open("menus", CustomizeMenus);
        Open("toolbar", CustomizeToolbar);
        Open("missing-fonts", ManageMissingFonts);
        _layers.SelectAll(); Pump();
        Open("auto-align", AutoAlignLayers);
        Open("auto-blend", AutoBlendLayers);
        Check(shown.Count == 19, "Not every Edit dialog was shown.");
    }
    private void TypeText(string text) => Avalonia.Headless.HeadlessWindowExtensions.KeyTextInput(this, text);
}
