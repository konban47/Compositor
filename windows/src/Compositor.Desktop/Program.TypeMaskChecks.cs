using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

internal static partial class Program
{
    private static int TypeMaskChecks(string output)
    {
        BuildHeadless().SetupWithoutStarting();
        try { var window = new MainWindow(); window.Show(); window.TypeMaskSelfCheck(output); Console.WriteLine("PASS: native alignment glyphs, immediate light theme, pixel/vector thumbnail menus, select-and-mask properties, editable text range formatting, type menu, font candidates and type panels."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
public sealed partial class MainWindow
{
    internal void TypeMaskSelfCheck(string output)
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        void Pump() { Dispatcher.UIThread.RunJobs(); UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Save(string suffix) { Pump(); using var bitmap = this.CaptureRenderedFrame(); bitmap!.Save(Path.ChangeExtension(output, suffix + ".png"), new PngBitmapEncoderOptions()); }
        void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
        var doc = new CanvasDocument(Guid.NewGuid(), 640, 420);
        for (var i = 0; i < 3; i++)
        {
            var pixels = new SKBitmap(Bitmaps.ColorInfo(90, 70)); pixels.Erase(new SKColor((byte)(60 + i * 40), 125, 180));
            doc.Layers.Add(new(Guid.NewGuid(), ImportedImage.Create(pixels, "Object"), new(35 + i * 130, 70 + i * 65, 90, 70), "Object " + i));
        }
        AdoptImported(doc, "Type/Mask Check"); OpenInspector(0); ShowLayers(doc); Pump();
        foreach (var item in _layers.Items) _layers.SelectedItems!.Add(item); BuildProperties(); Pump();
        var section = _properties.Children.OfType<Expander>().Single(e => Equals(e.Tag, "property-section-Align and Distribute"));
        Check(section.GetVisualDescendants().OfType<Button>().Count(b => b.Tag is string tag && (tag.StartsWith("Align ") || tag.StartsWith("Distribute "))) == 14, "Alignment commands lost.");
        section.BringIntoView(); Pump();
        var panelPreview = new Border { Width = section.Bounds.Width, Height = section.Bounds.Height, Background = Skin.ChromeBrush,
            Child = new Avalonia.Controls.Shapes.Rectangle { Fill = new VisualBrush(section) } };
        panelPreview.Measure(section.Bounds.Size); panelPreview.Arrange(new Rect(section.Bounds.Size));
        using (var render = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(section.Bounds.Width * 3), (int)Math.Ceiling(section.Bounds.Height * 3)), new Vector(288, 288)))
        { render.Render(panelPreview); render.Save(Path.ChangeExtension(output, "alignment-panel.png"), new PngBitmapEncoderOptions()); }
        Save("alignment");
        var originalTheme = EditorPreferences.Current.Theme; EditorPreferences.Current.Theme = 2; ApplyPreferences(); Pump();
        Check(((SolidColorBrush)Skin.SurfaceDarkBrush).Color.R >= 160, "Layer surface stayed dark."); Save("light-theme");
        EditorPreferences.Current.Theme = originalTheme; ApplyPreferences();
        _layers.SelectedItems!.Clear(); Reselect(doc.Layers[0].ID); var layer = doc.Layers[0];
        var maskPixels = new SKBitmap(Bitmaps.MaskInfo(90, 70)); maskPixels.Erase(SKColors.White); layer.Mask = Compositor.Core.Model.LayerMask.AssetFrom(maskPixels);
        ShowLayers(doc); Reselect(layer.ID); SetPaintingMask(true); Pump();
        var thumbnail = _layerThumbnails.First(t => t.Active?.Invoke() == true); thumbnail.BringIntoView(); Pump();
        var point = thumbnail.TranslatePoint(new Point(10, 10), this)!.Value;
        this.MouseDown(point, MouseButton.Right); this.MouseUp(point, MouseButton.Right); Pump();
        Check(thumbnail.ContextMenu?.Items.OfType<MenuItem>().Any(i => Equals(i.Tag, "Add Mask to Selection")) == true, "Mask right click opened the layer menu."); Save("mask-menu"); thumbnail.ContextMenu!.Close();
        layer.Mask.VectorPath = "M0 0H45V70H0Z"; layer.Mask.VectorEvenOdd = true;
        OpenMaskContext(thumbnail, layer); Pump(); Check(thumbnail.ContextMenu!.Items.OfType<MenuItem>().Any(i => Equals(i.Tag, "Rasterize Vector Mask")), "Vector mask menu missing."); Save("vector-menu"); thumbnail.ContextMenu.Close();
        _ = RefineMask(); Pump(); Check(_maskWorkspace is not null && _properties.GetVisualDescendants().OfType<ComboBox>().Any(c => Equals(c.Tag, "Output To")), "Mask workspace did not open.");
        Save("mask-workspace"); Click(_properties.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "Cancel")));
        var id = TextEdits.Add(doc, new() { Content = "中文 ABCDE", FontName = "Arial", FontSize = 36 }, new(80, 240))!.Value;
        ShowLayers(doc); Reselect(id); SetTool(Tool.Type); var textLayer = doc.Layers.Single(l => l.ID == id);
        TypeHere(new((float)textLayer.Transform.X + 14, (float)textLayer.Transform.Y + 20)); Pump();
        Check(_text?.LayerID == id, "Type tool did not edit existing text."); _text!.SelectRange(1, 4); ShowTextCaret();
        ChangeType(s => { s.Red = 1; s.Bold = true; s.FontSize = 44; }); Pump();
        Check(TextRanges.At(_text.Style, 1).Bold == true && TextRanges.At(_text.Style, 0).Bold != true, "Range formatting changed the whole layer."); Save("text-selection");
        CommitText(); Reselect(id); BuildProperties(); Pump();
        var typeMenu = _mainMenu!.Items.OfType<MenuItem>().Single(m => Equals(m.Tag, "_Type")); typeMenu.IsSubMenuOpen = true; Pump();
        Check(typeMenu.Items.OfType<MenuItem>().Any(m => Equals(m.Tag, "Manage Missing Fonts…")), "Type menu incomplete."); Save("type-menu"); typeMenu.IsSubMenuOpen = false;
        var fonts = _properties.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "font-candidate-button")); fonts.BringIntoView(); Pump(); Click(fonts); Save("font-browser");
        this.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); this.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump();
        foreach (var panel in new[] { "Character", "Paragraph", "Glyphs", "Character Styles", "Paragraph Styles" }) { OpenTypePanel(panel); Pump(); Check(_typePanels[panel].Children.Count > 0, "Type panel is empty."); }
        Save("type-panels");
        _canvas.ProofColors = true; _canvas.GamutWarning = true; _canvas.InvalidateVisual(); Pump(); Save("cmyk-proof");
        Check(!_canvas.AcceleratedScene, "ICC proof bypassed the display transform."); _canvas.ProofColors = _canvas.GamutWarning = false;

    }
}


