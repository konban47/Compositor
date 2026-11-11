using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    /// <summary>The Image menu laid out as Photoshop's, and each of its dialogs opened, captured and answered.</summary>
    internal void ImageMenuSelfCheck(string output)
    {
        static void Check(bool value, string why) { if (!value) throw new InvalidOperationException(why); }
        void Pump(int delay = 0) { if (delay > 0) Thread.Sleep(delay); Dispatcher.UIThread.RunJobs(); UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Finish(Task task) { for (var i = 0; i < 300 && !task.IsCompleted; i++) { Pump(); Thread.Sleep(10); } task.GetAwaiter().GetResult(); Pump(); }
        void Click(Window window, string tag) { window.GetLogicalDescendants().OfType<Button>().First(b => Equals(b.Tag, tag)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(120); }
        void Save(Window window, string suffix) { Pump(); window.UpdateLayout(); using var image = window.CaptureRenderedFrame(); image!.Save(Path.ChangeExtension(output, "image-" + suffix + ".png"), new PngBitmapEncoderOptions()); }
        T Dialog<T>() where T : Window { Pump(); return OwnedWindows.OfType<T>().Last(); }
        T Find<T>(Window window, string tag) where T : Control => window.GetLogicalDescendants().OfType<T>().First(c => Equals(c.Tag, tag));

        // The menu: Photoshop's rows in its order, 22 adjustments, the mode list and the Chinese access keys.
        var image = this.GetVisualDescendants().OfType<Menu>().First().Items.OfType<MenuItem>().Single(m => Equals(m.Tag, "Image"));
        var rows = image.Items.OfType<MenuItem>().Select(m => m.Tag as string).ToArray();
        Check(rows.SequenceEqual(["Mode", "Adjustments", "Auto Tone", "Auto Contrast", "Auto Color", "Image Size…", "Generative Upscale…", "Canvas Size…", "Image Rotation", "Crop", "Trim…", "Reveal All",
            "Duplicate Image…", "Apply Image…", "Calculations…", "Variables", "Apply Data Set…", "Trap…", "Analysis"]), "Image menu rows are not in Photoshop's order: " + string.Join(", ", rows));
        Check(image.Items.OfType<MenuItem>().Single(m => Equals(m.Tag, "Adjustments")).Items.OfType<MenuItem>().Count() == 22, "Adjustments does not list 22 commands.");
        Check(image.Items.OfType<MenuItem>().Single(m => Equals(m.Tag, "Mode")).Items.OfType<MenuItem>().Count() == 12, "Mode does not list 8 modes, 3 depths and Color Table.");
        if (Localize.IsChinese) Check(Localize.Text("_Levels…") == "色阶(_L)…" && Localize.Text("Ima_ge Rotation") == "图像旋转(_G)" && Localize.Text("_180°") == "180 度(_1)", "Image menu access keys are not placed as Photoshop's.");

        var doc = _document!; var layer = doc.Layers.First(l => l.Asset is not null && !l.IsGroup && l.Adjustment is null);
        SelectLayerRow(layer.ID); Pump(); var revision = _history.CurrentRevision;

        // Levels: the histogram dialog, a black point typed, OK, and one undo step.
        var task = ImageAdjustment(AdjustmentKind.Levels); var levels = Dialog<AdjustmentDialog>(); Save(levels, "levels");
        Find<ValueBox>(levels, "Input black").Value = 40; Pump(); Check(levels.Work.Levels.Ranges[0].Black == 40, "Levels black point did not take.");
        Click(levels, "OK"); Finish(task); Check(_history.CurrentRevision != revision, "Levels did not apply."); Undo(); Pump();
        // Curves: a built-in preset, then Cancel.
        task = ImageAdjustment(AdjustmentKind.Curves); var curves = Dialog<AdjustmentDialog>(); Save(curves, "curves");
        var preset = Find<ComboBox>(curves, "preset"); preset.SelectedIndex = preset.Items.OfType<ComboBoxItem>().ToList().FindIndex(i => Equals(i.Tag, "Strong Contrast (RGB)")); Pump();
        Check(curves.Work.Curves.Channels[0].Count == 4, "Curves preset did not load its points."); Click(curves, "Cancel"); Finish(task);
        foreach (var kind in new[] { AdjustmentKind.Exposure, AdjustmentKind.HueSaturation, AdjustmentKind.BlackWhite, AdjustmentKind.GradientMap, AdjustmentKind.ColorBalance })
        { task = ImageAdjustment(kind); var dialog = Dialog<AdjustmentDialog>(); Save(dialog, kind.ToString().ToLowerInvariant()); Click(dialog, "Cancel"); Finish(task); }
        Check(revision == _history.CurrentRevision, "Cancelled adjustments left history steps.");

        // The newer adjustments, each opened, captured and cancelled; Brightness/Contrast applied once.
        foreach (var kind in new[] { ImageAdjustmentKind.Vibrance, ImageAdjustmentKind.PhotoFilter, ImageAdjustmentKind.ChannelMixer, ImageAdjustmentKind.ColorLookup, ImageAdjustmentKind.Posterize,
            ImageAdjustmentKind.Threshold, ImageAdjustmentKind.SelectiveColor, ImageAdjustmentKind.ShadowsHighlights, ImageAdjustmentKind.HdrToning, ImageAdjustmentKind.MatchColor, ImageAdjustmentKind.ReplaceColor })
        { task = ExtendedImageAdjustment(kind); var dialog = Dialog<ImageAdjustmentDialog>(); Save(dialog, kind.ToString().ToLowerInvariant()); dialog.Close(false); Finish(task); }
        task = ExtendedImageAdjustment(ImageAdjustmentKind.BrightnessContrast); var brightness = Dialog<ImageAdjustmentDialog>(); Save(brightness, "brightness-contrast");
        Find<CheckBox>(brightness, "Use Legacy").IsChecked = true; brightness.GetVisualDescendants().OfType<ValueBox>().Single(b => Equals(b.Tag, "Brightness value")).Value = 40; Pump();
        Click(brightness, "OK"); Finish(task); Check(_history.CurrentRevision != revision, "Brightness/Contrast did not apply."); Undo(); Pump();

        // Auto Color Correction Options from Levels' Options….
        task = ImageAdjustment(AdjustmentKind.Levels); levels = Dialog<AdjustmentDialog>(); Click(levels, "Options…");
        var options = levels.OwnedWindows.OfType<AutoColorCorrectionDialog>().Single(); Save(options, "auto-color-options"); options.Close(); Pump(); Click(levels, "Cancel"); Finish(task);

        // Image Size: half the width in percent, kept in proportion.
        var width = doc.Width; var height = doc.Height;
        task = ImageSize(); var size = Dialog<ImageSizeDialog>(); Save(size, "image-size");
        size.WidthUnit.SelectedIndex = 0; Pump(); size.WidthBox.Value = 50; Pump(); Click(size, "OK"); Finish(task);
        Check(doc.Width == (int)Math.Round(width / 2.0) && Math.Abs(doc.Height - height / 2.0) <= 1, $"Image Size did not halve the picture: {doc.Width} x {doc.Height}.");
        Undo(); Pump();
        // Canvas Size: ten pixels more across, relative.
        task = CanvasSize(); var canvas = Dialog<CanvasSizeDialog>(); Save(canvas, "canvas-size");
        canvas.Relative.IsChecked = true; Pump(); canvas.WidthBox.Value = 10; Pump(); Click(canvas, "OK"); Finish(task);
        Check(doc.Width == width + 10 && doc.Height == height, "Canvas Size did not add ten pixels across."); Undo(); Pump();

        // Apply Image and Calculations.
        task = ChannelCalculation(false); var apply = Dialog<ChannelCalculationDialog>(); Save(apply, "apply-image");
        Check(apply.Blending.Items.OfType<ComboBoxItem>().Count(i => i.Tag is ChannelBlend) == 23, "Apply Image does not list Photoshop's 23 blending modes."); Click(apply, "Cancel"); Finish(task);
        task = ChannelCalculation(true); var calculations = Dialog<ChannelCalculationDialog>(); Find<CheckBox>(calculations, "Mask").IsChecked = true; Save(calculations, "calculations"); Click(calculations, "Cancel"); Finish(task);

        // Variables, both pages.
        task = DefineImageVariables(); var variables = Dialog<VariablesDialog>(); Save(variables, "variables-define");
        Find<ComboBox>(variables, "variables page").SelectedIndex = 1; Pump(); Save(variables, "variables-data-sets"); variables.Close(false); Finish(task);

        // Analysis: the full list of data points, a measurement recorded into the log, the scale and the marker.
        task = MeasurementFields(); var points = Dialog<DataPointsDialog>(); Save(points, "data-points");
        foreach (var box in points.GetLogicalDescendants().OfType<CheckBox>()) box.IsChecked = true; Click(points, "OK"); Finish(task);
        Check(doc.MeasurementFields.Length == ImageAnalysis.DataPoints.Length, "Select Data Points did not keep every data point.");
        RecordMeasurements(); Pump(200); var log = OwnedWindows.OfType<MeasurementLogWindow>().Single(); log.Width = 1360; Save(log, "measurement-log");
        Check(doc.Measurements.Count == 1 && doc.Measurements[0].Histogram is { Length: 256 }, "Record Measurements did not record the new data points."); log.Close(); Pump();
        Undo(); Undo(); Pump();
        task = SetMeasurementScale(); var scale = Dialog<MeasurementScaleDialog>(); Save(scale, "measurement-scale"); scale.Close(false); Finish(task);
        task = PlaceScaleMarker(); var marker = Dialog<ScaleMarkerDialog>(); Save(marker, "scale-marker"); marker.Close(false); Finish(task);
        var trap = new TrapDialog(doc.Resolution); var shown = trap.ShowDialog<bool>(this); Pump(); Save(trap, "trap"); trap.Close(false); Finish(shown);
        Check(revision == _history.CurrentRevision, "The Image menu checks left history steps behind.");
    }
}
