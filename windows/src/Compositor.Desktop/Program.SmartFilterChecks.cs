using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Compositor.Core.Document;
using Compositor.Core.Model;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    /// <summary>
    /// Smart filters on a smart object's row (Figures 1–9), Alt-click between two rows clipping the upper (Figure 10), and the
    /// Channels footer on the right.
    /// </summary>
    internal void SmartFilterSelfCheck(string output)
    {
        static void Check(bool value, string why) { if (!value) throw new InvalidOperationException(why); }
        void Pump(int delay = 0) { if (delay > 0) Thread.Sleep(delay); Dispatcher.UIThread.RunJobs(); UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Save(string suffix) { Pump(100); using var frame = this.CaptureRenderedFrame(); frame!.Save(Path.ChangeExtension(output, suffix + ".png"), new PngBitmapEncoderOptions()); }
        Control Row(Guid id) => _visibleLayerItems.Select(i => i.Content).OfType<Control>().First(c => c.GetLogicalDescendants().OfType<LayerThumbnail>().Any() && RowLayer(c) == id);
        Guid? RowLayer(Control row) => row.Tag is Guid id ? id : row.GetLogicalAncestors().OfType<ListBoxItem>().FirstOrDefault()?.Tag as Guid?;
        T? Tagged<T>(Control root, string tag) where T : Control => root.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => Equals(c.Tag, tag));

        var doc = _document!; var start = _history.CurrentRevision;
        // A striped layer of its own, so a blur shows.
        var stripes = new SkiaSharp.SKBitmap(Bitmaps.ColorInfo(120, 90));
        for (var y = 0; y < 90; y++) for (var x = 0; x < 120; x++) stripes.SetPixel(x, y, x / 6 % 2 == 0 ? SkiaSharp.SKColors.Black : SkiaSharp.SKColors.White);
        var source = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(stripes, "Smart Check"), new LayerTransform(10, 10, 120, 90), "Smart Check");
        Edit("New Layer", () => { doc.Layers.Add(source); return true; }); ShowLayers(doc); Pump();
        SelectLayerRow(source.ID); Pump(); ConvertToSmartObject(); Pump();
        var id = Selected!.Value; ImageLayer Smart() => doc.Layers.Single(l => l.ID == id);
        Check(Smart().SmartObject is not null, "Convert to Smart Object did not make a smart object.");
        Check(Row(id).GetLogicalDescendants().OfType<LayerThumbnail>().Any(t => t.Smart), "The smart object's thumbnail has no smart object badge.");

        // Two filters: the row grows a Smart Filters line with the two under it, newest first, and the marks sit left to right.
        var plain = Smart().Asset!.Image.Copy();
        Edit("Gaussian Blur", () => SmartFilterEdits.Add(doc, id, FilterKind.GaussianBlur, new FilterSettings { BlurRadius = 8 }));
        Edit("Add Noise", () => SmartFilterEdits.Add(doc, id, FilterKind.AddNoise, new FilterSettings()));
        ShowLayers(doc); Reselect(id); Pump();
        Check(Smart().SmartFilters is { Items.Count: 2, Enabled: true }, "Two smart filters were not kept on the layer.");
        Check(!Smart().Asset!.Image.Bytes.AsSpan().SequenceEqual(plain.Bytes), "The smart filters did not change the smart object's pixels.");
        var row = Row(id); var filters = Tagged<StackPanel>(row, "smart-filters");
        Check(filters is not null && Tagged<Grid>(filters, "smart-filters-row") is not null, "The smart object has no Smart Filters row.");
        var names = filters!.Children.OfType<Grid>().Select(g => g.Tag as string).Where(t => t!.StartsWith("smart-filter ", StringComparison.Ordinal)).ToArray();
        Check(names.SequenceEqual(["smart-filter " + SmartFilterName(Smart().SmartFilters!.Items[1]), "smart-filter " + SmartFilterName(Smart().SmartFilters!.Items[0])]), "The filters are not listed newest first: " + string.Join(", ", names));
        var marks = Tagged<StackPanel>(row, "layer-marks")!.Children.Select(c => c.Tag as string).ToArray();
        Check(marks.First() == "smart-filter-mark" && marks.Last() == "smart-filter-toggle", "The smart filter mark and arrow are not in order: " + string.Join(", ", marks));
        Save("smart-filters");

        // The arrow folds the rows away and brings them back; the rows are inside the layer's row, so they move with it.
        Tagged<Button>(row, "smart-filter-toggle")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(Tagged<StackPanel>(Row(id), "smart-filters") is null, "The arrow did not fold the smart filters away."); Save("smart-filters-collapsed");
        Tagged<Button>(Row(id), "smart-filter-toggle")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(Tagged<StackPanel>(Row(id), "smart-filters") is not null, "The arrow did not bring the smart filters back.");

        // Disable Smart Filters shows the contents as they are; the mask comes and goes; Clear takes the rows away.
        SmartFilterCommand(SmartFilterAction.ToggleStack, id); Pump();
        Check(Smart().SmartFilters is { Enabled: false } && Smart().Asset!.Image.Bytes.AsSpan().SequenceEqual(plain.Bytes), "Disable Smart Filters did not show the unfiltered contents.");
        SmartFilterCommand(SmartFilterAction.ToggleStack, id); SmartFilterCommand(SmartFilterAction.ToggleMask, id); Pump();
        Check(Smart().SmartFilters is { Enabled: true, HasMask: false }, "Delete Filter Mask did not take the mask away.");
        Edit("Delete Smart Filter", () => SmartFilterEdits.Delete(doc, id, 1)); ShowLayers(doc); Pump();
        Check(Smart().SmartFilters is { Items.Count: 1 }, "Delete Smart Filter did not remove one filter.");
        SmartFilterCommand(SmartFilterAction.Clear, id); Pump();
        Check(Smart().SmartFilters is null && Tagged<StackPanel>(Row(id), "smart-filters") is null && Smart().Asset!.Image.Bytes.AsSpan().SequenceEqual(plain.Bytes), "Clear Smart Filters did not restore the smart object.");
        plain.Dispose();
        for (var i = 0; i < 20 && _history.CurrentRevision != start; i++) { Undo(); Pump(); }
        Check(_history.CurrentRevision == start && doc.Layers.All(l => l.SmartFilters is null), "Undo did not take the smart filter steps back.");

        // Alt-click on the line between two layers clips the upper to the lower, and again releases it.
        ShowLayers(doc); Pump();
        var pair = Enumerable.Range(0, _rows.Count - 1).Select(i => (Upper: doc.Layers.Single(l => l.ID == _rows[i]), Lower: doc.Layers.Single(l => l.ID == _rows[i + 1])))
            .First(p => !p.Upper.IsGroup && !p.Lower.IsGroup && p.Upper.ParentID == p.Lower.ParentID && p.Upper.MaskSourceID is null);
        Check(ClipBoundary(pair.Upper.ID, 39, 42) == pair.Upper.ID && ClipBoundary(pair.Lower.ID, 2, 42) == pair.Upper.ID && ClipBoundary(pair.Upper.ID, 20, 42) is null, "The line between two rows is not where Alt-click clips.");
        ClipAtBoundary(pair.Upper.ID); Pump();
        Check(doc.Layers.Single(l => l.ID == pair.Upper.ID).MaskSourceID == pair.Lower.ID, "Alt-click did not clip the upper layer to the one under it."); Save("alt-clip");
        ClipAtBoundary(pair.Upper.ID); Pump();
        Check(doc.Layers.Single(l => l.ID == pair.Upper.ID).MaskSourceID is null, "A second Alt-click did not release the clipping mask.");
        Undo(); Undo(); Pump();

        // The Channels panel's four buttons sit at the right.
        var footer = this.GetLogicalDescendants().Concat(OwnedWindows.SelectMany(w => w.GetLogicalDescendants())).OfType<StackPanel>().FirstOrDefault(s => Equals(s.Tag, "channel-footer"));
        Check(footer is { HorizontalAlignment: HorizontalAlignment.Right, Children.Count: 4 }, "The Channels footer's four buttons are not on the right.");
        Check(_history.CurrentRevision == start, "The smart filter checks left history steps behind.");
    }
}
