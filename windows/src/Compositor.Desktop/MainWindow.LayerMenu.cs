using Avalonia.Controls;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private readonly List<(MenuItem Item, Func<bool> Ready)> _layerMenuAvailability = [];
    private MenuItem BuildLayerMenu()
    {
        MenuItem Group(string name, params Control[] children) { var item = new MenuItem { Header = Localize.Text(name), Tag = name }; foreach (var child in children) item.Items.Add(child); return item; }
        MenuItem Item(string name, Action action, string? key = null, Func<bool>? ready = null)
        {
            var item = Command(name, action, key);
            _layerMenuAvailability.Add((item, ready ?? (() => _document is not null && SelectedLayers.Count > 0)));
            return item;
        }
        MenuItem Missing(string name, string reason)
        { var item = new MenuItem { Header = Localize.Text(name), Tag = name, IsEnabled = false }; ToolTip.SetTip(item, Localize.Text(reason)); return item; }
        bool Document() => _document is not null;
        bool Editable() => _document is { } d && SelectedLayers.Count > 0 && LayerWorkflow.Editable(d, SelectedLayers);
        bool Pixels() => Editable() && PropertyLayer?.Asset is not null;
        bool Smart() => Editable() && PropertyLayer?.SmartObject is not null;
        bool NewMask() => Editable() && PropertyLayer?.Mask is null;
        bool Mask() => Editable() && PropertyLayer?.Mask is not null;
        var menu = new MenuItem { Header = Localize.Text("_Layer"), Tag = "Layer" };
        menu.Items.Add(Group("New",
            Item("Layer…", () => _ = NewNamedLayer(), "New Blank Layer", Document),
            Item("Background from Layer", MakeBackground, ready: Pixels),
            Item("Group…", () => _ = NewNamedGroup(), ready: Document),
            Item("Group from Layers…", () => _ = GroupFromLayers(), ready: Editable),
            Item("Artboard…", () => _ = MakeLayerContainer(LayerContainer.Artboard, false), ready: Document),
            Item("Artboard from Group…", () => _ = MakeLayerContainer(LayerContainer.Artboard, true), ready: () => Editable() && PropertyLayer is { IsGroup: true }),
            Item("Artboard from Layers…", () => _ = MakeLayerContainer(LayerContainer.Artboard, true), ready: Editable),
            Item("Frame from Layers…", () => _ = MakeLayerContainer(LayerContainer.Frame, true), ready: Editable),
            Item("Convert to Frame…", () => _ = MakeLayerContainer(LayerContainer.Frame, true), ready: Editable), new Separator(),
            Item("Layer via Copy", () => LayerViaSelection(false), "Layer via Copy", Pixels),
            Item("Layer via Cut", () => LayerViaSelection(true), "Layer via Cut", () => Pixels() && _document!.Selection.Path is not null)));
        menu.Items.Add(Item("Copy CSS", () => _ = CopyLayerMarkup(false)));
        menu.Items.Add(Item("Copy SVG", () => _ = CopyLayerMarkup(true)));
        menu.Items.Add(Item("Duplicate Layer…", () => _ = DuplicateNamedLayer(), "Duplicate Layer"));
        menu.Items.Add(Group("Delete", Item("Layer", DeleteLayer, "Delete Layer", Editable), Item("Hidden Layers", () => _ = CleanLayers("Hidden"), ready: Document)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Quick Export as PNG", () => _ = ExportSelectedLayers(true), "Quick Export Layers PNG"));
        menu.Items.Add(Item("Export Layers As…", () => _ = ExportSelectedLayers(false), "Export Layers As"));
        menu.Items.Add(new Separator()); menu.Items.Add(Item("Rename Layer…", () => _ = RenameLayer(), "Rename Layer", Editable));
        _effectsMenu.Header = Localize.Text("Layer Style");
        _effectsMenu.Items.Add(Item("Copy Layer Style", CopyLayerStyle));
        _effectsMenu.Items.Add(Item("Paste Layer Style", PasteLayerStyle, ready: () => Editable() && _copiedStyle is not null));
        _effectsMenu.Items.Add(Item("Clear Layer Style", ClearSelectedStyles, ready: Editable));
        _effectsMenu.Items.Add(new Separator());
        _effectsMenu.Items.Add(Item("Global Light…", () => _ = GlobalLight(), ready: Document));
        _effectsMenu.Items.Add(Missing("Create Layers", "Separating style effects into independent layers is not supported. Rasterize Layer Style bakes the appearance."));
        _effectsMenu.Items.Add(Item("Show / Hide Layer Effects", ToggleEffects, ready: Editable));
        _effectsMenu.Items.Add(Item("Scale Effects…", () => _ = ScaleLayerEffects(), ready: () => Editable() && PropertyLayer?.Effects is not null));
        menu.Items.Add(_effectsMenu);
        menu.Items.Add(Missing("Smart Filters", "Editable filter stacks are not supported by this document engine."));
        menu.Items.Add(new Separator());
        menu.Items.Add(Group("New Fill Layer", Item("Solid Color…", () => _ = NewFillLayer(StyleEffectKind.ColorOverlay), ready: Document),
            Item("Gradient…", () => _ = NewFillLayer(StyleEffectKind.GradientOverlay), ready: Document), Item("Pattern…", () => _ = NewFillLayer(StyleEffectKind.PatternOverlay), ready: Document)));
        var adjustment = Group("New Adjustment Layer");
        void Advanced(ImageAdjustmentKind kind) => adjustment.Items.Add(Item(ImageAdjustmentTitle(kind), () => _ = ExtendedImageAdjustment(kind, adjustmentLayer: true), ready: Document));
        void Existing(string name, AdjustmentKind kind) => adjustment.Items.Add(Item(name, () => _ = NewAdjustment(kind), ready: Document));
        Advanced(ImageAdjustmentKind.Vibrance);
        adjustment.Items.Add(Missing("Clarity / Dehaze…", "Clarity and dehaze are available in Camera Raw; a separate adjustment-layer operator is not implemented."));
        Existing("Grain…", AdjustmentKind.Grain); adjustment.Items.Add(new Separator());
        Advanced(ImageAdjustmentKind.BrightnessContrast); Existing("Levels…", AdjustmentKind.Levels); Existing("Curves…", AdjustmentKind.Curves); Existing("Exposure…", AdjustmentKind.Exposure);
        adjustment.Items.Add(new Separator()); Existing("Hue/Saturation…", AdjustmentKind.HueSaturation); Existing("Color Balance…", AdjustmentKind.ColorBalance);
        Existing("Black & White…", AdjustmentKind.BlackWhite); Advanced(ImageAdjustmentKind.PhotoFilter); Advanced(ImageAdjustmentKind.ChannelMixer);
        Advanced(ImageAdjustmentKind.ColorLookup); Advanced(ImageAdjustmentKind.SelectiveColor); adjustment.Items.Add(new Separator());
        Existing("Invert", AdjustmentKind.Invert); Advanced(ImageAdjustmentKind.Posterize); Advanced(ImageAdjustmentKind.Threshold); Existing("Gradient Map…", AdjustmentKind.GradientMap);
        adjustment.Items.Add(new Separator()); Existing("Add Noise…", AdjustmentKind.AddNoise); Existing("Gaussian Blur…", AdjustmentKind.GaussianBlur); Existing("Motion Blur…", AdjustmentKind.MotionBlur);
        menu.Items.Add(adjustment);
        menu.Items.Add(Item("Layer Content Options…", () => _ = EditLayerContents(), ready: () => Editable() && PropertyLayer is { } l && (l.Adjustment is not null || l.Effects is not null || l.LiveText is not null || l.LiveShape is not null)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Group("Layer Mask",
            Item("Reveal All", () => LayerMaskCommand("Reveal"), ready: NewMask), Item("Hide All", () => LayerMaskCommand("Hide"), ready: NewMask),
            Item("Reveal Selection", () => LayerMaskCommand("Selection"), ready: () => NewMask() && _document!.Selection.Path is not null),
            Item("Hide Selection", () => LayerMaskCommand("Inverse"), ready: () => NewMask() && _document!.Selection.Path is not null),
            Item("From Transparency", () => LayerMaskCommand("Transparency"), ready: () => Pixels() && PropertyLayer!.Mask is null), new Separator(),
            Item("Delete", DeleteMask, ready: Mask), Item("Apply", () => LayerMaskCommand("Apply"), ready: () => Mask() && Pixels()), new Separator(), _maskToggle, _maskLink));
        menu.Items.Add(Group("Vector Mask",
            Item("Reveal All", () => VectorMaskCommand("Reveal"), ready: NewMask), Item("Hide All", () => VectorMaskCommand("Hide"), ready: NewMask),
            Item("Current Path", () => VectorMaskCommand("Path"), ready: () => NewMask() && (_pen is not null || _pathNodes is not null)), new Separator(),
            Item("Delete", DeleteMask, ready: () => Mask() && PropertyLayer!.Mask!.VectorPath is not null),
            Item("Enable / Disable", () => LayerMaskCommand("Toggle"), ready: () => Mask() && PropertyLayer!.Mask!.VectorPath is not null),
            Item("Link / Unlink", () => LayerMaskCommand("Link"), ready: () => Mask() && PropertyLayer!.Mask!.VectorPath is not null)));
        menu.Items.Add(_clipping); menu.Items.Add(Item("Mask All Objects", () => _ = MaskAllObjects(), ready: Pixels)); menu.Items.Add(new Separator());
        menu.Items.Add(Group("Smart Objects",
            Item("Convert to Smart Object", ConvertToSmartObject, ready: Editable), Item("New Smart Object via Copy", IndependentSmartCopy, ready: Smart), new Separator(),
            Item("Edit Contents", EditSmartContents, ready: Smart), Item("Reset Transform", ResetSmartTransform, ready: Smart),
            Item("Replace Contents…", () => _ = ReplaceSmartContents(), ready: Smart), Item("Export Contents…", () => _ = ExportSmartContents(), ready: Smart),
            Item("Convert to Layers", UnpackSmartObject, ready: Smart),
            Missing("Linked Smart Objects…", "Smart objects are embedded. External linking, relinking and automatic linked updates are not supported."),
            Missing("Stack Mode", "Statistical smart-object stack modes are not supported."), Item("Rasterize", () => RasterizeSelected("Smart"), ready: Smart)));
        menu.Items.Add(Missing("Video Layers", "This editor has no video timeline or frame decoder. Video layers cannot be imported or edited."));
        menu.Items.Add(Group("Rasterize",
            Item("Type", () => RasterizeSelected("Type"), ready: () => Editable() && PropertyLayer?.LiveText is not null),
            Item("Shape", () => RasterizeSelected("Shape"), ready: () => Editable() && PropertyLayer?.LiveShape is not null),
            Item("Fill Content", () => RasterizeSelected("Effects"), ready: () => Editable() && PropertyLayer?.Effects is not null),
            Item("Vector Mask", () => LayerMaskCommand("Rasterize"), ready: () => Mask() && PropertyLayer!.Mask!.VectorPath is not null),
            Item("Smart Object", () => RasterizeSelected("Smart"), ready: Smart),
            Missing("3D", "This editor has no 3D scene engine."), Item("Layer Style", () => RasterizeSelected("Effects"), ready: () => Editable() && PropertyLayer?.Effects is not null),
            new Separator(), Item("Layer", () => RasterizeSelected("Layer"), ready: Editable), Item("All Layers", () => RasterizeSelected("All"), ready: Document)));
        menu.Items.Add(new Separator()); menu.Items.Add(Item("New Layer Based Slice", LayerSlice));
        menu.Items.Add(new Separator()); menu.Items.Add(Item("Group Layers", GroupSelected, "Group Layers", Editable));
        menu.Items.Add(Item("Ungroup Layers", UngroupSelected, "Ungroup Layers", Editable)); menu.Items.Add(Item("Hide / Show Layers", ToggleSelectedVisibility, "Hide Layers"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Group("Arrange", Item("Bring to Front", () => ArrangeLayers("Front"), "Bring to Front", Editable),
            Item("Bring Forward", () => ArrangeLayers("Forward"), "Move Layer Up", Editable), Item("Send Backward", () => ArrangeLayers("Backward"), "Move Layer Down", Editable),
            Item("Send to Back", () => ArrangeLayers("Back"), "Send to Back", Editable), Item("Reverse", () => ArrangeLayers("Reverse"), ready: () => Editable() && SelectedLayers.Count > 1)));
        var shapes = Group("Combine Shapes");
        foreach (var (label, op) in new[] { ("Unite Shapes", SKPathOp.Union), ("Subtract Front Shape", SKPathOp.Difference), ("Intersect Shape Areas", SKPathOp.Intersect), ("Exclude Overlapping Shapes", SKPathOp.Xor) })
            shapes.Items.Add(Item(label, () => CombineLayerShapes(op), ready: () => _document is { } doc && LayerMenuEdits.CanCombineShapes(doc, SelectedLayers)));
        menu.Items.Add(shapes);
        foreach (var (label, operations) in new[] { ("Align", new[] { "Align Top", "Align V Center", "Align Bottom", "Align Left", "Align H Center", "Align Right" }),
            ("Distribute", new[] { "Distribute Top", "Distribute Vertically", "Distribute Bottom", "Distribute Left", "Distribute Horizontally", "Distribute Right", "Distribute Horizontal Spacing", "Distribute Vertical Spacing" }) })
        {
            var group = Group(label); foreach (var operation in operations) group.Items.Add(Item(operation, () => AlignProperties(operation), ready: () => Editable() && (label == "Align" || SelectedLayers.Count >= 3)));
            menu.Items.Add(group);
        }
        menu.Items.Add(new Separator()); menu.Items.Add(Item("Lock Layers…", () => _ = LayerLocksDialog(), "Lock Layers Dialog"));
        menu.Items.Add(Item("Link / Unlink Layers", LinkLayers, ready: Editable)); menu.Items.Add(Item("Select Linked Layers", SelectLinkedLayers));
        menu.Items.Add(new Separator()); menu.Items.Add(_merge); menu.Items.Add(Item("Merge Visible", () => MergeAll(false), "Merge Visible", Document)); menu.Items.Add(Item("Flatten Image", () => _ = FlattenImage(), ready: Document));
        menu.Items.Add(new Separator()); menu.Items.Add(Group("Matting",
            Item("Color Decontaminate…", () => _ = DefringeLayer(true), ready: Pixels), Item("Defringe…", () => _ = DefringeLayer(false), ready: Pixels),
            Item("Remove Black Matte", () => RemoveMatte(false), ready: Pixels), Item("Remove White Matte", () => RemoveMatte(true), ready: Pixels)));
        menu.SubmenuOpened += (_, _) => { foreach (var (item, ready) in _layerMenuAvailability) item.IsEnabled = ready(); };
        return menu;
    }
}
