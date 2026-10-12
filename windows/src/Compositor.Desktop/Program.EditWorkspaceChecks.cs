using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

internal static partial class Program
{
    private static int EditWorkspaceChecks(string output)
    {
        BuildHeadless().SetupWithoutStarting();
        try { var window = new MainWindow(); window.Show(); window.EditWorkspaceSelfCheck(output); Console.WriteLine("PASS: upstream footer/tab layout, complete opacity thumb, edit menus, transform cancel/repeat/undo, paste masks, presets, preferences, and layer drop sizes."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
public sealed partial class MainWindow
{
    internal void EditWorkspaceSelfCheck(string output)
    {
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        void Pump() { Dispatcher.UIThread.RunJobs(); UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Finish(Task task) { for (var i = 0; i < 300 && !task.IsCompleted; i++) { Pump(); Thread.Sleep(10); } Check(task.IsCompleted, "Dialog did not close."); task.GetAwaiter().GetResult(); Pump(); }
        void Save(string suffix) { Pump(); using var frame = this.CaptureRenderedFrame(); frame!.Save(Path.ChangeExtension(output, suffix + ".png"), new PngBitmapEncoderOptions()); }
        var pixels = new SKBitmap(Bitmaps.ColorInfo(64, 40)); pixels.Erase(SKColors.Coral);
        var doc = new CanvasDocument(Guid.NewGuid(), 400, 300); var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "Layer"), new LayerTransform(40, 50, 64, 40), "Layer"); doc.Layers.Add(layer);
        AdoptImported(doc, "New"); Reselect(layer.ID); Pump();
        Check(_tabStrip.Children.All(c => c is Border), "Duplicate new-tab button remains.");
        Check(_tabStrip.Children.OfType<Border>().All(b => b.CornerRadius.TopLeft == 14), "Tabs do not use upstream capsules.");
        foreach (var button in _rail.GetVisualDescendants().OfType<Button>().Where(b => b.Tag is string s && s is "Extra Tools" or "Quick Mask (Q)" or "Screen Mode (F)" or "Generative Workspace"))
        { var at = button.TranslatePoint(default, _rail)!.Value; Check(Math.Abs(at.X + button.Bounds.Width / 2 - _rail.Bounds.Width / 2) < 1, "Footer button is off-center: " + button.Tag); }
        var thumb = _opacity.GetVisualDescendants().OfType<Thumb>().Single(); var pos = thumb.TranslatePoint(default, _opacity)!.Value;
        Check(pos.Y >= 0 && pos.Y + thumb.Bounds.Height <= _opacity.Bounds.Height && Math.Abs(pos.Y + thumb.Bounds.Height / 2 - _opacity.Bounds.Height / 2) < 1, "Opacity thumb is clipped or off-center.");
        var extra = _rail.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "Extra Tools")); extra.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(extra.ContextMenu?.Items.Count == 1, "Extra menu contains duplicate tools."); extra.ContextMenu!.Close();
        // Every Edit command must be discoverable in both languages.
        var edit = _mainMenu!.Items.OfType<MenuItem>().Single(m => m.Header?.ToString() == Localize.Text("_Edit"));
        Check(edit.Items.OfType<MenuItem>().Any(m => m.Header?.ToString() == Localize.Text("Paste Special")), "Paste Special menu missing.");
        Check(edit.Items.OfType<MenuItem>().Any(m => m.Header?.ToString() == Localize.Text("Preferences")), "Preferences menu missing.");
        var original = layer.Transform; BeginEditTransform("Scale"); ChangePlacementNumber("X", original.CenterX + 20); FinishPlacement(false); Pump();
        doc = _document!; layer = doc.Layers.Single(); Check(layer.Transform == original, "Cancel transform changed placement.");
        BeginEditTransform("Scale"); ChangePlacementNumber("X", original.CenterX + 20); FinishPlacement(true); Pump();
        Check(layer.Transform.X == original.X + 20 && _history.CanUndo, "Transform did not commit as one undo step."); RepeatTransform(); Pump();
        Check(layer.Transform.X == original.X + 40, "Repeat transform lost translation."); Undo(); Pump(); Check(_document!.Layers.Single().Transform.X == original.X + 20, "Undo repeated transform failed.");
        doc = _document!; layer = doc.Layers.Single(); BeginPuppetWarp(); PuppetPressed(new SKPoint(55, 65), Avalonia.Input.KeyModifiers.None); PuppetMoved(new SKPoint(60, 70));
        Check(_puppetPins.Count == 1 && _placeMesh is not null, "Puppet pin did not move."); FinishPlacement(false); Pump();
        doc = _document!; layer = doc.Layers.Single(); Copy(); doc.Selection = DocumentSelection.Rectangular(new(30, 30, 90, 90), 400, 300); PasteMasked(false); Pump();
        Check(_document!.Layers.Count == 2 && _document!.Layers.Any(l => l.Mask is not null), "Paste Into did not create a mask."); Undo(); Pump();
        var task = EditPreferences("History"); Pump(); OwnedWindows.OfType<PreferencesDialog>().Single().Close(false); Finish(task);
        FormDialog form;
        var oldDirectory = EditorPresets.DefaultPath;
        task = DefinePreset("Brush"); Pump(); form = OwnedWindows.OfType<FormDialog>().Single(); form.Close(true); Finish(task);
        Check(EditorPresets.Load(oldDirectory).Any(p => p.Kind == "Brush"), "Define Brush did not save a usable preset.");
        var area = _tabDropArea!; var target = area.TranslatePoint(new Point(area.Bounds.Width - 250, area.Bounds.Height / 2), this)!.Value;
        var previous = _open; Check(DropLayersOnTabs(target), "Layer drop outside the tabs was ignored."); Pump();
        Check(!ReferenceEquals(previous, _open) && _document is { Width: 64, Height: 40 }, "Dropped layers did not size the new canvas to their bounds.");
        if (OperatingSystem.IsWindows())
        {
            try { Check(WindowsSpelling.Check("The elefant is here.", "en-US").Any(i => i.Word == "elefant"), "Windows spelling did not return the misspelled word."); }
            catch (NotSupportedException error) { Console.WriteLine("Spelling dictionary unavailable: " + error.Message); }
        }
        OpenInspector(0); Save("workspace");
        edit.IsSubMenuOpen = true; Save("edit-menu"); edit.IsSubMenuOpen = false;
        var gallery = new FormDialog("Tool Icons", 740, 570); var icons = new WrapPanel { ItemWidth = 74, ItemHeight = 56 };
        foreach (var tool in Enum.GetValues<Tool>()) icons.Children.Add(new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Children = {
            new EditorIcon(tool.ToString()) { Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center },
            new TextBlock { Text = tool.ToString(), FontSize = 8, MaxWidth = 72 } } });
        gallery.Fields.Children.Add(icons); gallery.Show(this); Pump();
        using (var frame = gallery.CaptureRenderedFrame()) frame!.Save(Path.ChangeExtension(output, "icons.png"), new PngBitmapEncoderOptions()); gallery.Close();
        Save("final");
    }
}
