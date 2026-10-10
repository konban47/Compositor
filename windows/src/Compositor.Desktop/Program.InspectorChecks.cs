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
using Compositor.Core.Model;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Desktop;

internal static partial class Program
{
    private static int InspectorChecks(string output)
    {
        BuildHeadless().SetupWithoutStarting();
        try
        {
            var window = new MainWindow(); window.Show(); window.InspectorSelfCheck(output);
            Console.WriteLine("PASS: workspace guides and ruler edges, navigator at 100%, mask thumbnail/link/move/delete/cancel/undo, properties, history navigation/branch/snapshot/new document, Chinese UI.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}

public sealed partial class MainWindow
{
    internal void InspectorSelfCheck(string output)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        void Pump() { Dispatcher.UIThread.RunJobs(); UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Save(string suffix) { Pump(); using var frame = this.CaptureRenderedFrame(); frame!.Save(Path.ChangeExtension(output, suffix + ".png"), new PngBitmapEncoderOptions()); }
        void Click(Control control)
        {
            Pump(); var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), this)!.Value;
            this.MouseDown(at, MouseButton.Left); this.MouseUp(at, MouseButton.Left); Pump();
        }
        var document = new CanvasDocument(Guid.NewGuid(), 420, 300);
        var pixels = new SKBitmap(Bitmaps.ColorInfo(160, 100)); pixels.Erase(SKColors.CornflowerBlue);
        var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "图像 / Image"), new LayerTransform(70, 80, 160, 100), "图像 / Image");
        document.Layers.Add(layer); AdoptImported(document, "Import Image"); SetTool(Tool.Move); Pump(); _canvas.ActualSize();
        var view = _mainMenu!.Items.OfType<MenuItem>().Single(item => item.Items.OfType<MenuItem>().Any(child => child.Header?.ToString() == Localize.Text("Navigator")));
        view.IsSubMenuOpen = true; Pump();
        var navigator = view.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == Localize.Text("Navigator"));
        Click(navigator);
        Check(_canvas.NavigatorEnabled && _canvas.NavigatorBounds.Width > 0, "View > Navigator was invisible at 100%.");
        view.IsSubMenuOpen = false;
        if (!_rulersVisible) ShowRulers();
        GuideEdits.Add(document, GuideAxis.Vertical, 50); GuideEdits.Add(document, GuideAxis.Horizontal, 40);
        _guidesVisible = true; PushViewSwitches(); _canvas.InvalidateVisual(); Save("guides");
        // Inspect pixels near both workspace edges; these points are outside the document rectangle.
        using (var frame = this.CaptureRenderedFrame())
        {
            using var buffer = new MemoryStream(); frame!.Save(buffer, new PngBitmapEncoderOptions()); using var shown = SKBitmap.Decode(buffer.ToArray());
            var sx = (50 - _canvas.OriginX) * _canvas.Zoom;
            var sy = (40 - _canvas.OriginY) * _canvas.Zoom;
            bool Cyan(Point p)
            {
                for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
                {
                    var color = shown.GetPixel(Math.Clamp((int)p.X + dx, 0, shown.Width - 1), Math.Clamp((int)p.Y + dy, 0, shown.Height - 1));
                    if (color.Blue > color.Red + 40 && color.Green > color.Red + 40) return true;
                }
                return false;
            }
            Check(Cyan(_canvas.TranslatePoint(new Point(sx, _canvas.Bounds.Height - 3), this)!.Value), "Vertical guide stops at the canvas instead of the workspace bottom.");
            Check(Cyan(_canvas.TranslatePoint(new Point(_canvas.Bounds.Width - 3, sy), this)!.Value), "Horizontal guide stops before workspace right edge.");
        }
        _guidesVisible = false; PushViewSwitches();
        SelectionEdits.Select(document, SKRectI.Create(70, 80, 100, 100)); AddSelectionMask(false); Pump();
        ImageLayer Current() => document.Layers.Single(item => item.ID == layer.ID);
        ListBoxItem Row() => _layers.Items.OfType<ListBoxItem>().Single(item => Equals(item.Tag, layer.ID));
        var link = Row().GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Tag, "mask-link"));
        Click(link); Check(Current().Mask?.IsLinked == false, "Chain icon did not unlink mask.");
        Undo(); Check(Current().Mask?.IsLinked == true, "Unlink was not undoable."); Redo(); Pump();
        var thumbnail = Row().GetVisualDescendants().OfType<LayerThumbnail>().Last(); Click(thumbnail);
        Check(MaskTarget && thumbnail.Active?.Invoke() == true, "Mask thumbnail did not select the mask.");
        var imageBefore = Current().Transform; var maskBefore = Current().MaskTransform;
        var atMask = _canvas.TranslatePoint(new Point((maskBefore.CenterX - _canvas.OriginX) * _canvas.Zoom,
            (maskBefore.CenterY - _canvas.OriginY) * _canvas.Zoom), this)!.Value;
        _snappingOn = false;
        this.MouseDown(atMask, MouseButton.Left); this.MouseMove(atMask + new Vector(25, 0)); this.MouseUp(atMask + new Vector(25, 0), MouseButton.Left); Pump();
        Check(Current().Transform == imageBefore && Math.Abs(Current().MaskTransform.X - maskBefore.X - 25) < .01, "Unlinked mask drag moved the image or panned the canvas.");
        var density = _properties.GetVisualDescendants().OfType<NumericUpDown>().Single(input => Equals(input.Tag, "mask-density"));
        density.Value = 45; density.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.Enter }); Pump();
        Check(Current().Mask!.Density == .45, "Mask density field did not commit."); Save("mask");
        var refine = RefineMask(); Pump(); var refineDialog = OwnedWindows.OfType<MaskRefineDialog>().Single();
        refineDialog.GetVisualDescendants().OfType<NumericUpDown>().Single(input => Equals(input.Tag, "Shift Edge (px)")).Value = 3;
        refineDialog.GetVisualDescendants().OfType<Button>().Single(button => button.Content?.ToString() == Localize.Text("Preview")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(_canvas.PreviewDocument is not null, "Select and Mask did not show its preview.");
        refineDialog.GetVisualDescendants().OfType<Button>().Single(button => button.Content?.ToString() == Localize.Text("Cancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(refine.IsCompletedSuccessfully && _canvas.PreviewDocument is null && Current().Mask!.Density == .45, "Select and Mask cancel changed the mask or left a preview.");
        var previousCoverage = Current().Mask!.Asset; var originalSelection = document.Selection;
        ColorRange(layer.ID); Pump();
        _colorRange!.Pick(document, SKColors.CornflowerBlue, ColorRangeSession.Picking.Replace);
        _colorRangePanel!.GetVisualDescendants().OfType<Button>().Single(button => button.Content?.ToString() == Localize.Text("OK")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(!ReferenceEquals(Current().Mask!.Asset, previousCoverage) && document.Selection.Matches(originalSelection), "Mask Color Range did not replace coverage or restore the selection.");
        DeletePanelTarget(); Pump();
        var confirmation = OwnedWindows.OfType<ConfirmDialog>().Single();
        Check(confirmation.Title == Localize.Text("Delete Layer Mask?"), "Trash did not ask to delete the mask.");
        confirmation.GetVisualDescendants().OfType<Button>().Single(button => button.Content?.ToString() == Localize.Text("Cancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(Current().Mask is not null, "Cancel deleted the mask.");
        DeletePanelTarget(); Pump(); confirmation = OwnedWindows.OfType<ConfirmDialog>().Single();
        confirmation.GetVisualDescendants().OfType<Button>().Single(button => button.Content?.ToString() == Localize.Text("Delete Mask")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(document.Layers.Count == 1 && Current().Mask is null, "Deleting mask removed layer or failed."); Undo(); Pump(); Check(Current().Mask is not null, "Mask deletion did not undo.");
        SetPaintingMask(false); Pump();
        var x = _properties.GetVisualDescendants().OfType<NumericUpDown>().Single(input => Equals(input.Tag, "transform-x"));
        x.Value = 90; x.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.Enter }); Pump(); Check(Current().Transform.X == 90, "Transform field did not commit."); Save("pixel");
        MakeSnapshot(); var saved = _history.Snapshots.Last(); var savedX = Current().Transform.X;
        AlignProperties("Align Right"); Pump(); Check(Current().Transform.X == document.Width - Current().Transform.Width, "Properties alignment failed.");
        OpenInspector(1); Pump();
        var first = _historyRows.Items.OfType<ListBoxItem>().First(item => item.Tag is HistoryTarget { SnapshotID: null });
        _historyRows.SelectedItem = first; Pump(); Check(Current().Transform.X == 70 && _history.CanRedo, "History panel did not restore the initial state.");
        SetPropertyTransform(Current().Transform with { X = 75 }); Pump(); Check(!_history.CanRedo, "Editing an earlier state retained an invalid redo branch.");
        _historyRows.SelectedItem = _historyRows.Items.OfType<ListBoxItem>().Single(item => item.Tag is HistoryTarget target && target.SnapshotID == saved.ID); Pump();
        Check(Current().Transform.X == savedX, "Snapshot did not restore layers."); Save("history");
        var originalTab = _open; var count = _tabs.Count;
        NewDocumentFromHistory(); Pump(); Check(_tabs.Count == count + 1 && _document!.ID != document.ID, "New document from history failed.");
        Check(!ReferenceEquals(_document!.Layers[0].Asset!.Image, Current().Asset!.Image), "History-created document shares owned pixels.");
        _open = originalTab; Show(_open); Pump();
        Edit("Type", () => TextEdits.Add(document, new LayerTextStyle { Content = "中文 Typography", FontName = "Arial", FontSize = 24,
            BoxSize = new JsonSize(300, 90) }, new SKPoint(30, 200)) is not null);
        Reselect(document.Layers[^1].ID); OpenInspector(0); Pump();
        var bold = _properties.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().Single(button => button.Content?.ToString() == Localize.Text("Bold"));
        bold.IsChecked = true; bold.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(PropertyLayer?.LiveText?.Bold == true, "Bold text property did not commit.");
        _properties.Children.OfType<Expander>().Single(section => section.Header?.ToString() == Localize.Text("Transform")).IsExpanded = false;
        Save("text");
        using (var finalFrame = this.CaptureRenderedFrame()) finalFrame!.Save(output, new PngBitmapEncoderOptions());
        Check(!Localize.IsChinese || _inspectorTabs.Items.OfType<TabItem>().Select(item => item.Header?.ToString()).SequenceEqual(new[] { "属性", "历史记录" }), "Inspector headings are untranslated.");
    }
}
