using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;
using SelectionMode = Compositor.Core.Document.SelectionMode;

namespace Compositor.Desktop;

internal static partial class Program
{
    private static int SelectionOptionsChecks(string output)
    {
        BuildHeadless().SetupWithoutStarting();
        try { var window = new MainWindow(); window.Show(); window.SelectionOptionsSelfCheck(output); Console.WriteLine("PASS: selection options, row/column, fixed size, combination/feather, brush softness, tool presets, crop settings, slice division/cancel/properties, theme scrollbars."); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

public sealed partial class MainWindow
{
    internal void SelectionOptionsSelfCheck(string output)
    {
        static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        void Pump() { Dispatcher.UIThread.RunJobs(); UpdateLayout(); foreach (var w in OwnedWindows) w.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Drag(double x, double y, double endX, double endY)
        {
            Point At(double a, double b) => _canvas.TranslatePoint(_canvas.ToScreen(new SKPoint((float)a, (float)b)), this)!.Value;
            this.MouseDown(At(x, y), MouseButton.Left); this.MouseMove(At(endX, endY), RawInputModifiers.LeftMouseButton); this.MouseUp(At(endX, endY), MouseButton.Left); Pump();
        }
        void SaveBar(Tool tool)
        {
            Pump(); using var shot = new RenderTargetBitmap(new PixelSize(Math.Max(1, (int)_optionsBar.Bounds.Width), Math.Max(1, (int)_optionsBar.Bounds.Height)));
            shot.Render(_optionsBar); shot.Save(Path.ChangeExtension(output, tool + ".png"), new PngBitmapEncoderOptions());
        }
        ResetDockPanels(); Check(!_dockPanels["Contextual Task Bar"].Visible, "Contextual task bar must start closed.");
        var doc = LayerPlacement.NewDocument(400, 280)!; AdoptImported(doc, "New"); Pump(); _canvas.ActualSize();
        foreach (var tool in new[] { Tool.Marquee, Tool.Ellipse, Tool.SingleRow, Tool.SingleColumn, Tool.SelectionBrush, Tool.Lasso, Tool.Polygon, Tool.MagneticLasso, Tool.Wand, Tool.QuickSelection, Tool.Object, Tool.PerspectiveCrop, Tool.Crop, Tool.Slice, Tool.SliceSelect })
        { SetTool(tool); Pump(); Check(_optionsBar.Showing.Any(), "Missing toolbar: " + tool); SaveBar(tool); }
        SetTool(Tool.SingleRow); Drag(40, 50, 80, 60); Check(doc.Selection.Path!.Bounds.Height == 1 && doc.Selection.Path.Bounds.Width == 400, "Single-row selection geometry.");
        SetTool(Tool.SingleColumn); Drag(30, 40, 45, 55); Check(doc.Selection.Path!.Bounds.Width == 1 && doc.Selection.Path.Bounds.Height == 280, "Single-column selection geometry.");
        SetTool(Tool.Marquee); _options.Selection.Style = 2; _options.Selection.Width = 35; _options.Selection.Height = 21; OptionsChanged(); Drag(40, 40, 130, 120); Check(doc.Selection.Path!.Bounds.Width == 35 && doc.Selection.Path.Bounds.Height == 21, "Fixed-size marquee ignored dimensions.");
        _options.Selection.Style = 0; _options.Selection.Mode = SelectionMode.Intersect; OptionsChanged(); Drag(50, 45, 160, 130); Check(doc.Selection.Contains(60, 50) && !doc.Selection.Contains(42, 42), "Toolbar intersection mode was not used.");
        // Space held mid-drag moves the Marquee without resizing it or taking up the Hand; let go, the far corner follows again.
        _options.Selection.Mode = SelectionMode.Replace; _options.Selection.Feather = 0; OptionsChanged(); _canvas.Focus();
        Point Spot(double a, double b) => _canvas.TranslatePoint(_canvas.ToScreen(new SKPoint((float)a, (float)b)), this)!.Value;
        this.MouseDown(Spot(40, 40), MouseButton.Left); this.MouseMove(Spot(100, 90), RawInputModifiers.LeftMouseButton);
        Check(TakeHand() && _tool == Tool.Marquee, "Space during a marquee took up the Hand.");
        this.MouseMove(Spot(130, 120), RawInputModifiers.LeftMouseButton); _canvas.HoldMarqueeSpace(false);
        this.MouseMove(Spot(150, 140), RawInputModifiers.LeftMouseButton); this.MouseUp(Spot(150, 140), MouseButton.Left); Pump();
        var moved = doc.Selection.Path!.Bounds;
        Check(Math.Abs(moved.Left - 70) <= 1 && Math.Abs(moved.Top - 70) <= 1 && Math.Abs(moved.Right - 150) <= 1 && Math.Abs(moved.Bottom - 140) <= 1,
            $"Space did not move the marquee: {moved}.");
        _options.Selection.Feather = 4; OptionsChanged(); Drag(30, 30, 90, 90); using (var mask = doc.Selection.Coverage(SKRectI.Create(400, 280))) Check(mask!.GetPixel(29, 50).Red is > 0 and < 255, "Feather did not affect selection coverage.");
        SetTool(Tool.SelectionBrush); _options.Brush = _options.Brush with { Diameter = 20, Hardness = .1, Opacity = .5 }; Pro.SelectionMode = SelectionPaintMode.New; OptionsChanged(); Drag(100, 100, 120, 100); using (var mask = doc.Selection.Coverage(SKRectI.Create(400, 280))) Check(mask!.GetPixel(110, 100).Red is > 90 and < 140 && mask.GetPixel(110, 108).Red is > 0 and < 90, "Selection brush settings were not applied.");
        var preset = CaptureToolPreset("Selection check"); _options.Selection.Width = 999; ApplyToolPreset(preset); Check(_options.Selection.Width == 35, "Selection settings did not round trip through a preset.");
        SetTool(Tool.Crop); _options.Crop.Width = 16; _options.Crop.Height = 9; OptionsChanged(); Check(Math.Abs(_canvas.CropRatio!.Value - 16d / 9) < .0001, "Crop dimensions did not constrain frame.");
        var originalWidth = doc.Width; _cropFrame = SKRectI.Create(20, 20, 100, 100); CancelCrop(); Check(doc.Width == originalWidth && _cropFrame is null, "Cancel crop changed the document.");
        SetTool(Tool.Slice); _options.Crop.SliceStyle = 2; _options.Crop.SliceWidth = 50; _options.Crop.SliceHeight = 30; OptionsChanged(); Drag(20, 20, 80, 90); var slice = doc.Marks.Single(m => m.Kind == MarkKind.Slice); Check(slice.Width == 50 && slice.Height == 30, "Fixed-size slice ignored dimensions.");
        SetTool(Tool.SliceSelect); _selectedMark = slice.ID; var divide = DivideSlice(); Pump(); var dialog = OwnedWindows.OfType<FormDialog>().Single(); dialog.Close(false); Pump(); Check(divide.IsCompleted && doc.Marks.Count == 1 && _slicePreview.Count == 0, "Cancelled division changed slices or left preview.");
        var options = EditSliceOptions(); Pump(); dialog = OwnedWindows.OfType<FormDialog>().Single(); var alt = dialog.GetVisualDescendants().OfType<TextBox>().Single(t => Equals(t.Tag, "Alt Tag:")); alt.Text = "Accessible slice"; Check(dialog.Validate?.Invoke() is null, "Valid slice options refused."); dialog.Close(true); Pump(); Check(options.IsCompletedSuccessfully && doc.Marks[0].AltText == "Accessible slice", "Slice properties were not stored.");
        Undo(); Pump(); Check(doc.Marks[0].AltText == "", "Slice property undo failed.");
        _selectedMark = AutoSlices().First().ID; var autoCount = doc.Marks.Count;
        var automaticOptions = EditSliceOptions(); Pump(); OwnedWindows.OfType<FormDialog>().Single().Close(false); Pump();
        Check(automaticOptions.IsCompleted && doc.Marks.Count == autoCount, "Cancelling automatic slice options promoted it.");
        var automaticDivide = DivideSlice(); Pump(); OwnedWindows.OfType<FormDialog>().Single().Close(false); Pump();
        Check(automaticDivide.IsCompleted && doc.Marks.Count == autoCount && _slicePreview.Count == 0, "Cancelling automatic slice division promoted it.");
        SetTool(Tool.Crop); _canvas.BeginCropStraighten((_, _) => throw new InvalidOperationException("Stale straighten handler")); SetTool(Tool.Marquee); Check(!_canvas.UprightDrawing, "Changing tools left straighten active.");
        var invalid = CaptureToolPreset("Invalid selection"); invalid.Options.Selection.Width = 0; var presetPath = Path.ChangeExtension(output, "invalid-preset.json"); ToolPresetState.Write(presetPath, [invalid]);
        var rejected = false; try { ToolPresetState.Read(presetPath); } catch (InvalidDataException) { rejected = true; } Check(rejected, "Invalid imported selection dimensions accepted.");
        var bar = new ScrollBar { Orientation = Avalonia.Layout.Orientation.Horizontal, Minimum = 0, Maximum = 100, ViewportSize = 20, Width = 300 };
        var owner = new Window { Width = 330, Height = 120, Content = bar }; owner.Show(this); owner.UpdateLayout(); Pump();
        var track = bar.GetVisualDescendants().OfType<Track>().Single(); bar.Value = 40; Pump(); Check(Math.Abs(track.Value - 40) < .01 && track.Thumb is not null, "Scrollbar template binding is broken.");
        var thumb = track.Thumb!; var thumbAt = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), owner)!.Value;
        owner.MouseDown(thumbAt, MouseButton.Left); owner.MouseMove(thumbAt + new Vector(35, 0), RawInputModifiers.LeftMouseButton); owner.MouseUp(thumbAt + new Vector(35, 0), MouseButton.Left); Pump(); Check(bar.Value > 40, "Dragging the horizontal scrollbar thumb did not scroll."); owner.Close();
        SetTool(Tool.Crop); _options.Crop.Mode = 1; _options.Crop.Width = 200; _options.Crop.Height = 140; _options.Crop.Resolution = 300; OptionsChanged(); _cropFrame = null;
        var applied = ApplyAdvancedCrop(); var until = DateTime.UtcNow.AddSeconds(10); while (!applied.IsCompleted && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
        Check(applied.IsCompletedSuccessfully && doc.Width == 200 && doc.Height == 140 && doc.Resolution == 300, "Whole-canvas crop preset did not resample without a drag.");
        SetTool(Tool.Marquee); _options.Selection.Feather = 0; _options.Selection.Mode = SelectionMode.Replace; OptionsChanged();
        using var final = this.CaptureRenderedFrame(); final!.Save(output, new PngBitmapEncoderOptions());
    }
}
