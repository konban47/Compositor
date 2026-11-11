using Avalonia;
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
        Open("edit-pdf-preset", () => new PdfPresetDialog(Compositor.Core.IO.PdfPresets.BuiltIn[0] with { Name = "Custom", BuiltIn = false }, new HashSet<string>()).ShowDialog<bool>(this));
        Open("preset-manager", PresetManager);
        Open("color-settings", ColorSettings);
        Open("custom-rgb", () => new CustomRgbDialog().ShowDialog<bool>(this));
        Open("custom-cmyk", () => new CustomCmykDialog().ShowDialog<bool>(this));
        Open("custom-dot-gain", () => new GrayProfileDialog(true).ShowDialog<bool>(this));
        Open("custom-gray-gamma", () => new GrayProfileDialog(false).ShowDialog<bool>(this));
        Open("ink-colors", () => new InkColorsDialog(CustomColorProfiles.InkSets[5]).ShowDialog<bool>(this));
        void Accept(Window dialog) { Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(dialog).OfType<Button>().First(b => Equals(b.Content, Localize.Text("OK"))).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Pump(); }
        var press = new CustomCmykDialog(); var pressing = press.ShowDialog<bool>(this); Pump(); Accept(press); for (var i = 0; i < 200 && !pressing.IsCompleted; i++) { Pump(); Thread.Sleep(5); }
        Check(pressing.Result && press.ProfileBytes is { } cmyk && IccProfiles.Model(cmyk) == "CMYK", "Custom CMYK did not build a CMYK profile.");
        var display = new CustomRgbDialog(); var displaying = display.ShowDialog<bool>(this); Pump(); Accept(display); for (var i = 0; i < 200 && !displaying.IsCompleted; i++) { Pump(); Thread.Sleep(5); }
        Check(displaying.Result && display.ProfileBytes is { } rgb && IccProfiles.Model(rgb) == "RGB", "Custom RGB did not build an RGB profile.");
        Open("convert-profile", ConvertColorProfile);
        Open("assign-profile", AssignColorProfile);
        Open("ocio-settings", OpenColorIoSettings);
        Open("keyboard-shortcuts", KeyboardShortcuts);
        Open("menus", CustomizeMenus);
        // Hiding and coloring a row in the Menus tab reaches the menu itself, which then offers Show All Menu Items.
        var sample = MenuTree().SelectMany(m => m.Children).First(c => c.CanHide && c.Children.Count == 0);
        var menus = CustomizeMenus(); Pump(); var sheet = OwnedWindows.OfType<ShortcutDialog>().Single();
        sheet.ToggleHidden(sample.Path); sheet.ColorRow(sample.Path, "Blue"); sheet.SetLegacyUndo(true); Pump();
        Check(sheet.Draft["Menus:Undo"] == new ShortcutChord("Z", ShortcutModifiers.Control | ShortcutModifiers.Alt) && sheet.CanSave, "Legacy undo shortcuts did not swap Undo and Toggle Last State.");
        sheet.SetLegacyUndo(false); Capture(sheet, "menus-edited"); sheet.Accept(); for (var i = 0; i < 200 && !menus.IsCompleted; i++) { Pump(); Thread.Sleep(5); }
        var top = _mainMenu!.Items.OfType<MenuItem>().First(m => sample.Path.StartsWith(MenuPath(m, "") + "/", StringComparison.Ordinal));
        Check(EditorPreferences.Current.HiddenMenus.Contains(sample.Path) && EditorPreferences.Current.MenuColors.GetValueOrDefault(sample.Path) == "Blue", "Menus did not keep the hidden and colored row.");
        Check(top.Items.OfType<MenuItem>().Any(i => Equals(i.Tag, ShowAllTag) && i.IsVisible), "A menu with a hidden row does not offer Show All Menu Items.");
        EditorPreferences.Current.HiddenMenus.Clear(); EditorPreferences.Current.MenuColors.Clear(); ApplyHiddenMenus();
        Open("toolbar", CustomizeToolbar);
        Open("missing-fonts", ManageMissingFonts);
        _layers.SelectAll(); Pump();
        Open("auto-align", AutoAlignLayers);
        Open("auto-blend", AutoBlendLayers);
        // The Content-Aware Fill workspace previews the fill, and Apply adds it on a new layer as its default output.
        var stripes = new SKBitmap(Bitmaps.ColorInfo(240, 160));
        for (var y = 0; y < 160; y++) for (var x = 0; x < 240; x++) stripes.SetPixel(x, y, (x / 12) % 2 == 0 ? new SKColor(214, 168, 72) : new SKColor(56, 104, 150));
        var striped = new CanvasDocument(Guid.NewGuid(), 240, 160); striped.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(stripes, "Stripes"), new LayerTransform(0, 0, 240, 160), "Stripes"));
        AdoptImported(striped, "Stripes"); Reselect(striped.Layers[0].ID); SelectionEdits.Select(_document!, SKRectI.Create(90, 60, 50, 40)); Pump();
        var workspaceRun = ContentAwareFillWorkspace(); Pump(); var workspace = OwnedWindows.OfType<ContentAwareFillWindow>().Single();
        for (var i = 0; i < 3000 && !workspace.PreviewReady; i++) { Pump(); Thread.Sleep(10); }
        Check(workspace.PreviewReady, "The Content-Aware Fill workspace did not preview the fill: " + workspace.ErrorText);
        workspace.HoverForCheck(); Capture(workspace, "content-aware-fill");
        // Each tool brings its own options bar; the lasso takes from the fill area and Expand grows it.
        foreach (var tool in new[] { "lasso", "hand", "zoom", "brush" }) { workspace.PickToolForCheck(tool); Pump(); Check(Equals(workspace.OptionsBar.Tag, "options-" + tool), "The options bar did not follow the " + tool + " tool."); Capture(workspace, "content-aware-fill-" + tool); }
        var filledBefore = workspace.FillArea.Count(b => b > 127);
        workspace.LassoForCheck([new(90, 60), new(115, 60), new(115, 100), new(90, 100)], "subtract"); var afterLasso = workspace.FillArea.Count(b => b > 127);
        workspace.GrowForCheck(true); var afterGrow = workspace.FillArea.Count(b => b > 127);
        Check(afterLasso < filledBefore && afterGrow > afterLasso, $"Lasso subtract or Expand did not change the fill area ({filledBefore}, {afterLasso}, {afterGrow}).");
        workspace.LassoForCheck([new(90, 60), new(140, 60), new(140, 100), new(90, 100)], "new"); Pump();
        for (var i = 0; i < 3000 && !workspace.PreviewReady; i++) { Pump(); Thread.Sleep(10); } var layersBefore = _document!.Layers.Count; workspace.ApplyForCheck(); Pump();
        var fillLayer = _document!.Layers.FirstOrDefault(l => l.Name.StartsWith(Localize.Text("Content-Aware Fill"), StringComparison.Ordinal));
        Check(_document.Layers.Count == layersBefore + 1 && fillLayer?.Asset?.Image.GetPixel(115, 80).Alpha == 255 && fillLayer.Asset.Image.GetPixel(5, 5).Alpha == 0, $"Apply did not add the fill on a new layer: {workspace.ErrorText} ({_document.Layers.Count} layers, {layersBefore} before).");
        workspace.Close(false); for (var i = 0; i < 300 && !workspaceRun.IsCompleted; i++) { Pump(); Thread.Sleep(5); }
        shown.Add("content-aware-fill");
        // Sky Replacement on a landscape: the dialog finds the sky and loads a default sky, and OK adds Photoshop's layer group.
        var landscape = new SKBitmap(Bitmaps.ColorInfo(240, 160));
        for (var y = 0; y < 160; y++) for (var x = 0; x < 240; x++) landscape.SetPixel(x, y, y < 64 ? new SKColor((byte)(110 + y), (byte)(170 + y / 2), 235) : new SKColor(40, (byte)(90 + (x * 7 + y * 3) % 20), 35));
        var land = new CanvasDocument(Guid.NewGuid(), 240, 160); land.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(landscape, "Landscape"), new LayerTransform(0, 0, 240, 160), "Landscape"));
        AdoptImported(land, "Landscape"); Reselect(land.Layers[0].ID); Pump();
        var replacing = SkyReplacement();
        for (var i = 0; i < 3000 && (_sky?.Mask is null || _sky.Dialog.Sky is null || _sky.Preview is null); i++) { Pump(); Thread.Sleep(10); }
        Check(_sky is { Mask: not null, Preview: not null } && _sky.Dialog.Sky is not null, $"Sky Replacement did not find the sky, load a sky or preview it: mask {_sky?.Mask is not null}, sky {_sky?.Dialog.Sky is not null}, preview {_sky?.Preview is not null}.");
        var skyDialog = _sky!.Dialog; Capture(skyDialog, "sky-replacement"); Capture(this, "sky-preview"); Accept(skyDialog);
        for (var i = 0; i < 300 && !replacing.IsCompleted; i++) { Pump(); Thread.Sleep(10); }
        var skyGroup = _document!.Layers.FirstOrDefault(l => l.Name == Localize.Text("Sky Replacement Group"));
        Check(skyGroup is not null && _document.Layers.Count(l => l.ParentID == skyGroup.ID) == 3, "Sky Replacement did not add its layer group.");
        shown.Add("sky-replacement");
        // The Move tool pressed inside a selection carries the selected pixels within the same layer.
        var moveSource = new SKBitmap(Bitmaps.ColorInfo(120, 80)); moveSource.Erase(SKColors.White);
        using (var paintCanvas = new SKCanvas(moveSource)) using (var red = new SKPaint { Color = SKColors.Red }) paintCanvas.DrawRect(10, 10, 20, 20, red);
        var moveDoc = new CanvasDocument(Guid.NewGuid(), 120, 80); moveDoc.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(moveSource, "Move"), new LayerTransform(0, 0, 120, 80), "Move"));
        AdoptImported(moveDoc, "Move"); Reselect(moveDoc.Layers[0].ID); SelectionEdits.Select(_document!, SKRectI.Create(10, 10, 20, 20)); SetTool(Tool.Move); Pump();
        Avalonia.Point OnScreen(SKPoint p) => ((Avalonia.Visual)_canvas).TranslatePoint(_canvas.ToScreen(p), this)!.Value;
        this.MouseDown(OnScreen(new(20, 20)), Avalonia.Input.MouseButton.Left); this.MouseMove(OnScreen(new(60, 20)), Avalonia.Input.RawInputModifiers.LeftMouseButton); this.MouseUp(OnScreen(new(60, 20)), Avalonia.Input.MouseButton.Left); Pump();
        using (var after = Compositor.Core.Rendering.DocumentRenderer.Render(_document!))
            Check(_document!.Layers.Count == 1 && after.GetPixel(55, 20).Red > 200 && after.GetPixel(55, 20).Green < 60 && after.GetPixel(15, 20) is not { Red: > 200, Green: < 60, Alpha: 255 }, "The Move tool did not carry the selected pixels within the layer.");
        // Perspective Crop placed by four clicks, then Enter, crops to the frame the clicks made.
        SelectionEdits.Deselect(_document!); SetTool(Tool.PerspectiveCrop); Pump();
        foreach (var corner in new SKPoint[] { new(20, 10), new(100, 14), new(96, 70), new(18, 66) }) { this.MouseDown(OnScreen(corner), Avalonia.Input.MouseButton.Left); this.MouseUp(OnScreen(corner), Avalonia.Input.MouseButton.Left); Pump(); }
        Check(_perspectiveQuad is { Length: 4 } placed && SKPoint.Distance(placed[2], new SKPoint(96, 70)) < 1.5f && _perspectiveClicks.Count == 0, "Perspective Crop did not take four clicked corners.");
        Capture(this, "perspective-clicks"); FinishProfessionalDraft(); Pump();
        Check(_document!.Width is > 60 and < 100 && _document.Height is > 40 and < 70, $"Perspective Crop from clicked corners did not crop ({_document.Width}x{_document.Height}).");
        Check(shown.Count == 27, "Not every Edit dialog was shown.");
    }
    private void TypeText(string text) => Avalonia.Headless.HeadlessWindowExtensions.KeyTextInput(this, text);
}
