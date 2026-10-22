using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;
namespace Compositor.Desktop;
internal static partial class Program
{
    private static int UtilityRetouchChecks(string output)
    {
        BuildHeadless().SetupWithoutStarting();
        try { var window = new MainWindow(); window.Show(); window.UtilityRetouchSelfCheck(output); Console.WriteLine("PASS: utility/retouch toolbar controls, clone slots and transforms, sampled healing, patch selection modes, content move apply/cancel/undo, removal add/subtract/apply/undo, notes/count/ruler and presets."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
public sealed partial class MainWindow
{
    internal void UtilityRetouchSelfCheck(string output)
    {
        static void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
        void Pump() { Dispatcher.UIThread.RunJobs(); UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Finish(Task task) { var until = DateTime.UtcNow.AddSeconds(60); while (!task.IsCompleted && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); } Check(task.IsCompleted, "Timed out"); task.GetAwaiter().GetResult(); Pump(); }
        Point At(SKPoint p) => _canvas.TranslatePoint(_canvas.ToScreen(p), this)!.Value;
        void Click(SKPoint p, RawInputModifiers modifiers = RawInputModifiers.None) { this.MouseDown(At(p), MouseButton.Left, modifiers); this.MouseUp(At(p), MouseButton.Left, modifiers); Pump(); }
        void Drag(SKPoint a, SKPoint b, RawInputModifiers modifiers = RawInputModifiers.None) { this.MouseDown(At(a), MouseButton.Left, modifiers); this.MouseMove(At(b), modifiers | RawInputModifiers.LeftMouseButton); this.MouseUp(At(b), MouseButton.Left, modifiers); Pump(); }
        void Save(string suffix) { Pump(); using var shot = this.CaptureRenderedFrame(); shot!.Save(Path.ChangeExtension(output, suffix + ".png"), new PngBitmapEncoderOptions()); }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!); Width = 1680; Height = 980; Pump();
        using var pixels = Bitmaps.Allocate(Bitmaps.ColorInfo(320, 240)); pixels.Erase(new SKColor(75, 120, 160));
        for (var y = 30; y < 100; y++) for (var x = 30; x < 100; x++) pixels.SetPixel(x, y, SKColors.Orange);
        AdoptImported(ImageImporter.NewDocument(ImportedImage.Create(pixels.Copy(), "Repair")), "New"); _canvas.ActualSize(); Pump(); var id = PropertyLayer!.ID;
        foreach (var (tool, group) in new[] { (Tool.Frame, "utilityFrame"), (Tool.Eyedropper, "utilityEye"), (Tool.ColorSampler, "utilitySampler"), (Tool.Ruler, "utilityRuler"), (Tool.Note, "utilityNote"), (Tool.Count, "utilityCount"), (Tool.Heal, "spotOptions"), (Tool.Remove, "removeOptions"), (Tool.HealingBrush, "healingOptions"), (Tool.Patch, "patchOptions"), (Tool.ContentMove, "moveContentOptions"), (Tool.RedEye, "redEyeOptions") })
        { SetTool(tool); Pump(); Check(_optionsBar.Showing.Contains(group), "Missing toolbar " + group); Save(tool.ToString()); }
        SetTool(Tool.HealingBrush); _options.Brush = _options.Brush with { Diameter = 18, HealingBlend = HealingBlend.Replace }; OptionsChanged();
        Click(new(50, 50), RawInputModifiers.Alt); OpenUtilityPanel("Clone Source"); Pump();
        Check(_cloneSlots[0].Point == new SKPoint(50, 50), "Clone slot not set");
        var slot2 = _utilityPanels["Clone Source"].GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "clone-source-1")); slot2.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Click(new(60, 50), RawInputModifiers.Alt); CloneSlotState.Width = 150; CloneSlotState.FlipX = true;
        Drag(new(180, 120), new(188, 120)); Check(PropertyLayer!.Asset!.Image.GetPixel(185, 120).Red > 230, "Sampled healing failed");
        this.MouseMove(At(new(190, 130))); Pump(); Save("CloneSource");
        var anchor = _optionsBar.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Tag, "Sampled Healing Brush Settings")); Finish(RetouchAction("Sampled Healing Brush Settings", anchor)); Save("HealingSettings"); Click(new(250, 220));
        SetTool(Tool.ContentMove); _document!.Selection = DocumentSelection.Rectangular(new(30, 30, 100, 100), 320, 240); Refresh(); _options.Selection.Mode = Compositor.Core.Document.SelectionMode.Replace;
        var revision = _history.CurrentRevision; var before = PropertyLayer!.Asset;
        Drag(new(50, 50), new(150, 90)); Check(_placementTab is not null && _contentMove is not null, "Content move did not enter transform"); Save("MoveTransform"); FinishPlacement(false); Pump();
        Check(_history.CurrentRevision == revision && PropertyLayer!.Asset == before, "Content move cancel changed pixels/history");
        SetTool(Tool.ContentMove); Drag(new(50, 50), new(150, 90)); FinishPlacement(true); Pump();
        Check(_document!.Layers.Count == 1 && PropertyLayer!.Asset!.Image.GetPixel(150, 100).Red > 200, "Content move did not settle into target");
        Undo(); Pump(); Check(PropertyLayer!.Asset == before, "Content move undo did not restore source");
        SetTool(Tool.Patch); _options.Selection.Mode = Compositor.Core.Document.SelectionMode.Add; OptionsChanged();
        ProfessionalPressed(new(200, 150), KeyModifiers.None, 1); ProfessionalMoved(new(220, 150), KeyModifiers.None); ProfessionalMoved(new(220, 170), KeyModifiers.None); ProfessionalMoved(new(200, 170), KeyModifiers.None); ProfessionalReleased();
        Check(_document!.Selection.Contains(210, 160) && _document.Selection.Contains(50, 50), "Patch add selection replaced existing area");
        _document.Selection = DocumentSelection.All; Refresh(); SetTool(Tool.Remove); Pro.RemoveEachStroke = false; Pro.RemoveNewLayer = true; _options.Brush = _options.Brush with { Diameter = 24 }; OptionsChanged();
        Drag(new(45, 45), new(55, 45)); Check(_removalArea?.Contains(50, 45) == true, "Removal area missing");
        Pro.RemoveSubtract = true; _options.Brush = _options.Brush with { Diameter = 8 }; OptionsChanged(); Click(new(50, 45)); Check(_removalArea?.Contains(50, 45) == false, "Removal subtraction failed");
        UndoRemoval(); Check(_removalArea?.Contains(50, 45) == true, "Removal stroke undo failed"); Save("RemovalPending"); Finish(ApplyRemoval()); Check(_document.Layers.Count == 2 && _removalArea is null, "Removal did not create a layer"); Undo(); Pump(); Check(_document.Layers.Count == 1, "Removal undo failed");
        SetTool(Tool.Ruler); Drag(new(20, 20), new(120, 20)); Drag(new(20, 20), new(20, 100), RawInputModifiers.Alt); Check(_document.Marks.Single(m => m.Kind == MarkKind.Measure).SecondY == 100, "Ruler second arm missing");
        SetTool(Tool.Count); Click(new(40, 180)); Click(new(60, 180)); Check(_document.Marks.Count(m => m.Kind == MarkKind.Count) == 2 && _document.CountGroups.Count == 1, "Count group not created"); Finish(UtilityAction("Toggle Count Group")); Check(!_document.CountGroups[0].Visible, "Count group did not hide");
        SetTool(Tool.Note); Pro.NoteAuthor = "Author"; Click(new(180, 180)); Pump(); Check(_document.Marks.Single(m => m.Kind == MarkKind.Note).Author == "Author", "Note author missing"); Save("Notes");
        _noteEditor!.GetVisualDescendants().OfType<TextBox>().Single(t => Equals(t.Tag, "note-text")).Text = "Draft committed on save";
        CommitText(); Pump(); Check(_noteEditor is null && _document.Marks.Single(m => m.Kind == MarkKind.Note).Text == "Draft committed on save", "Save did not commit the open note");
        var folder = LayerWorkflow.MakeContainer(_document, [id], LayerContainer.Group, "Selected folder", SKRect.Create(320, 240));
        Refresh(); Reselect(folder); _layerKind.SelectedIndex = 9; Pump(); Check(_rows.Contains(folder!.Value) && _rows.Contains(id), "Selected folder filter lost descendants"); _layerKind.SelectedIndex = 0;
        var originalTab = _open; var count = _tabs.Count; var pdfPath = Path.ChangeExtension(output, "pdf");
        using (var pdf = SKDocument.CreatePdf(pdfPath)) { for (var i = 0; i < 2; i++) { var page = pdf.BeginPage(96, 72); page.Clear(i == 0 ? SKColors.Red : SKColors.Blue); pdf.EndPage(); } pdf.Close(); }
        var import = OpenPdf(pdfPath); var until = DateTime.UtcNow.AddSeconds(30);
        while (!OwnedWindows.OfType<FormDialog>().Any(d => d.Title == Localize.Text("Import PDF")) && !import.IsCompleted && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
        var chooser = OwnedWindows.OfType<FormDialog>().Single(d => d.Title == Localize.Text("Import PDF"));
        chooser.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, Localize.Text("OK"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Finish(import);
        Check(_tabs.Count == count + 2 && _document!.Width == 96 && PropertyLayer!.Asset!.Image.GetPixel(20, 20) == SKColors.Blue, "PDF page chooser failed"); Bring(originalTab);
        _options.Brush = _options.Brush with { SpacingFraction = .25, Angle = 42, Roundness = .4, HealingDiffusion = 7, CloneScope = 1, HealingPattern = true }; Pro.MoveStructure = 6; Pro.MoveColor = 8;
        var preset = CaptureToolPreset("Retouch check"); var path = Path.ChangeExtension(output, "presets.json"); ToolPresetState.Write(path, [preset]); var read = ToolPresetState.Read(path).Single(); ApplyToolPreset(read);
        Check(_options.Brush.Angle == 42 && _options.Brush.CloneScope == 1 && Pro.MoveColor == 8, "Retouch preset lost settings");
    }
}
