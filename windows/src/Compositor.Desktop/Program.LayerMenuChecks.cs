using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    /// <summary>The Layer menu laid out as Photoshop's, and each of its dialogs opened, captured and answered.</summary>
    internal void LayerMenuSelfCheck(string output)
    {
        static void Check(bool value, string why) { if (!value) throw new InvalidOperationException(why); }
        void Pump(int delay = 0) { if (delay > 0) Thread.Sleep(delay); Dispatcher.UIThread.RunJobs(); UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Click(Window window, string title) { window.GetVisualDescendants().OfType<Button>().First(b => b.Content?.ToString() == Localize.Text(title)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(120); }
        void Save(Window window, string suffix) { Pump(); window.UpdateLayout(); using var image = window.CaptureRenderedFrame(); image!.Save(Path.ChangeExtension(output, suffix + ".png"), new PngBitmapEncoderOptions()); }
        T Dialog<T>() where T : Window { Pump(); return OwnedWindows.OfType<T>().Single(); }

        var layerMenu = this.GetVisualDescendants().OfType<Menu>().First().Items.OfType<MenuItem>().Single(m => Equals(m.Tag, "Layer"));
        MenuItem Child(MenuItem parent, string tag) => parent.Items.OfType<MenuItem>().FirstOrDefault(m => Equals(m.Tag, tag)) ?? throw new InvalidOperationException("Missing Layer menu item: " + tag);
        foreach (var tag in new[] { "New", "Copy CSS", "Copy SVG", "Duplicate Layer…", "Delete", "Quick Export as PNG", "Export As…", "Rename Layer…", "Layer Style", "Smart Filters", "New Fill Layer",
            "New Adjustment Layer", "Harmonize", "Layer Content Options…", "Layer Mask", "Vector Mask", "Mask All Objects", "Smart Objects", "Video Layers", "Rasterize", "New Layer Based Slice",
            "Group Layers", "Ungroup Layers", "Hide Layers", "Arrange", "Combine Shapes", "Align", "Distribute", "Lock Layers…", "Link Layers", "Select Linked Layers", "Merge Visible", "Flatten Image", "Matting" })
            Child(layerMenu, tag);
        foreach (var tag in new[] { "Layer…", "Background from Layer", "Group…", "Group from Layers…", "Artboard…", "Artboard from Group", "Artboard from Layers…", "Frame from Layers…", "Convert to Frame", "Layer via Copy", "Layer via Cut" })
            Child(Child(layerMenu, "New"), tag);
        var styles = Child(layerMenu, "Layer Style");
        foreach (var tag in new[] { "Blending Options…", "Copy Layer Style", "Paste Layer Style", "Clear Layer Style", "Global Light…", "Create Layers", "Hide All Effects", "Scale Effects…" }.Concat(Enum.GetValues<StyleEffectKind>().Select(k => LayerStyleDialog.NameFor(k) + "…")))
            Child(styles, tag);
        var smart = Child(layerMenu, "Smart Objects");
        foreach (var tag in new[] { "Convert to Smart Object", "New Smart Object via Copy", "Reveal in Explorer", "Update Modified Content", "Update All Modified Content", "Edit Contents", "Reset Transform", "Relink to File…",
            "Relink to Library Graphic…", "Replace Contents…", "Export Contents…", "Embed Linked", "Embed All Linked", "Convert to Linked…", "Convert to Layers", "Stack Mode", "Rasterize" })
            Child(smart, tag);
        Check(Child(smart, "Stack Mode").Items.OfType<MenuItem>().Select(m => m.Tag).SequenceEqual(SmartObjectStacks.Modes.Prepend("None")), "Stack Mode does not list None and Photoshop's 11 modes.");
        Check(Child(layerMenu, "New Adjustment Layer").Items.OfType<MenuItem>().Count() == 22, "New Adjustment Layer does not list 22 adjustments.");
        Check(Child(layerMenu, "Combine Shapes").Items.OfType<MenuItem>().All(m => m.Icon is ShapeOperationGlyph) && Child(layerMenu, "Align").Items.OfType<MenuItem>().All(m => m.Icon is InspectorGlyph), "Combine Shapes and Align rows have no icons.");
        if (Localize.IsChinese) Check(Localize.Text("_Duplicate Layer…") == "复制图层(_D)…" && (string?)Child(layerMenu, "Layer Style").Header == "图层样式(_Y)", "Chinese access keys are not placed as Photoshop's.");

        var doc = _document!; var count = doc.Layers.Count;
        var operation = NewNamedLayer(); var options = Dialog<LayerOptionsDialog>(); Save(options, "new-layer");
        Check(options.NameBox.Text == Localize.Text("Layer") + " 1" && !options.Neutral.IsEnabled, "New Layer did not offer Layer 1 with Fill with Neutral Color off for Normal.");
        options.NameBox.Text = "检查图层"; Click(options, "OK"); Pump();
        Check(operation.IsCompletedSuccessfully && doc.Layers.Count == count + 1 && doc.Layers.Any(l => l.Name == "检查图层"), "New Layer… did not add the named layer.");
        var named = doc.Layers.Single(l => l.Name == "检查图层"); SelectLayerRow(named.ID); Pump();

        operation = NewNamedGroup(); options = Dialog<LayerOptionsDialog>(); Save(options, "new-group"); Click(options, "OK"); Pump();
        Check(operation.IsCompletedSuccessfully && doc.Layers.Any(l => l.IsGroup), "New Group… did not add a group.");
        SelectLayerRow(named.ID); Pump(); count = doc.Layers.Count;
        operation = DuplicateNamedLayer(); var duplicate = Dialog<DuplicateLayerDialog>(); Save(duplicate, "duplicate-layer"); Click(duplicate, "OK"); Pump();
        Check(operation.IsCompletedSuccessfully && doc.Layers.Count == count + 1, "Duplicate Layer… did not copy the layer.");

        SelectLayerRow(named.ID); Pump();
        operation = LayerLocksDialog(); var locks = Dialog<LockLayersDialog>(); Save(locks, "lock-layers");
        locks.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Tag, "lock Prevent Auto-Nesting")).IsChecked = true; Click(locks, "OK"); Pump();
        Check(operation.IsCompletedSuccessfully && doc.Layers.Single(l => l.ID == named.ID).Locks.HasFlag(LayerLocks.Nesting), "Lock Layers… did not set Prevent Auto-Nesting.");
        Undo(); Pump();

        operation = GlobalLight(); var light = Dialog<GlobalLightDialog>(); Save(light, "global-light");
        light.GetVisualDescendants().OfType<NumericUpDown>().Single(n => Equals(n.Tag, "global-angle")).Value = 45; Pump();
        Check(light.Dial.Angle == 45, "Global Light angle field does not turn the dial."); Click(light, "Cancel"); Pump();
        Check(operation.IsCompletedSuccessfully, "Global Light did not close.");

        operation = MakeLayerContainer(LayerContainer.Artboard, false); var board = Dialog<ContainerDialog>(); Save(board, "new-artboard"); Click(board, "Cancel"); Pump();
        operation = MakeLayerContainer(LayerContainer.Frame, true); var frame = Dialog<ContainerDialog>(); Save(frame, "add-frame"); Click(frame, "Cancel"); Pump();
        Check(operation.IsCompletedSuccessfully, "Frame dialog did not close.");
        operation = NewFillLayer(StyleEffectKind.ColorOverlay); options = Dialog<LayerOptionsDialog>();
        Check(options.NameBox.Text == Localize.Text("Color Fill") + " 1", "Solid Color fill layer is not named Color Fill 1."); Save(options, "new-fill-layer"); Click(options, "Cancel"); Pump();

        var contour = ContourEditorDialog.Ask(this, [.. Enumerable.Range(0, 65).Select(i => i / 64.0)]); var editor = Dialog<ContourEditorDialog>(); Save(editor, "contour-editor"); Click(editor, "OK"); Pump();
        Check(contour.IsCompletedSuccessfully && contour.Result is { Length: 65 } curve && Math.Abs(curve[32] - .5) < .02, "Contour Editor did not return the linear curve.");

        if (doc.Layers.FirstOrDefault(l => l.SmartObject is not null) is { } smartLayer)
        {
            SelectLayerRow(smartLayer.ID); Pump(); SetStackMode("Median"); Pump();
            Check(doc.Layers.Single(l => l.ID == smartLayer.ID).SmartObject!.StackMode == "Median" && _history.UndoName == "Stack Mode", "Stack Mode did not apply Median as one undo step.");
            Undo(); Pump(); Check(doc.Layers.Single(l => l.ID == smartLayer.ID).SmartObject!.StackMode is null, "Undo did not clear Stack Mode.");
        }
    }
}
