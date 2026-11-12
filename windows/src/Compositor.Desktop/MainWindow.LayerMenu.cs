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
        // Names carry Photoshop's access keys; tags, command events and menu customizations use the name without them.
        MenuItem Group(string name, params Control[] children) { var item = new MenuItem { Header = Localize.Text(name), Tag = name.Replace("_", "") }; foreach (var child in children) item.Items.Add(child); return item; }
        MenuItem Item(string name, Action action, string? key = null, Func<bool>? ready = null)
        {
            var item = Command(name.Replace("_", ""), action, key); item.Header = Localize.Text(name);
            _layerMenuAvailability.Add((item, ready ?? (() => _document is not null && SelectedLayers.Count > 0)));
            return item;
        }
        MenuItem Missing(string name, string reason)
        { var item = new MenuItem { Header = Localize.Text(name), Tag = name.Replace("_", ""), IsEnabled = false }; ToolTip.SetTip(item, Localize.Text(reason)); return item; }
        bool Document() => _document is not null;
        bool Editable() => _document is { } d && SelectedLayers.Count > 0 && LayerWorkflow.Editable(d, SelectedLayers);
        bool Pixels() => Editable() && PropertyLayer?.Asset is not null;
        bool Smart() => Editable() && PropertyLayer?.SmartObject is not null;
        bool NewMask() => Editable() && PropertyLayer?.Mask is null;
        bool Mask() => Editable() && PropertyLayer?.Mask is not null;
        var menu = new MenuItem { Header = Localize.Text("_Layer"), Tag = "Layer" };
        menu.Items.Add(Group("_New",
            Item("_Layer…", () => _ = NewNamedLayer(), "New Blank Layer", Document),
            Item("_Background from Layer", MakeBackground, ready: Pixels),
            Item("_Group…", () => _ = NewNamedGroup(), ready: Document),
            Item("Group from L_ayers…", () => _ = GroupFromLayers(), ready: Editable),
            Item("Artboard…", () => _ = MakeLayerContainer(LayerContainer.Artboard, false), ready: Document),
            Item("Artboard from Group", () => _ = MakeLayerContainer(LayerContainer.Artboard, true), ready: () => Editable() && PropertyLayer is { IsGroup: true }),
            Item("Artboard from Layers…", () => _ = MakeLayerContainer(LayerContainer.Artboard, true), ready: Editable),
            Item("Frame from Layers…", () => _ = MakeLayerContainer(LayerContainer.Frame, true), ready: Editable),
            Item("Convert to Frame", () => _ = MakeLayerContainer(LayerContainer.Frame, true), ready: () => Pixels() && PropertyLayer is { IsGroup: false }), new Separator(),
            Item("Layer via Copy", () => LayerViaSelection(false), "Layer via Copy", Pixels),
            Item("Layer via Cut", () => LayerViaSelection(true), "Layer via Cut", () => Pixels() && _document!.Selection.Path is not null)));
        menu.Items.Add(Item("Copy CSS", () => _ = CopyLayerMarkup(false)));
        menu.Items.Add(Item("Copy SVG", () => _ = CopyLayerMarkup(true)));
        menu.Items.Add(Item("_Duplicate Layer…", () => _ = DuplicateNamedLayer(), "Duplicate Layer"));
        menu.Items.Add(Group("Delete", Item("_Layer", DeleteLayer, "Delete Layer", Editable), Item("_Hidden Layers", () => _ = CleanLayers("Hidden"), ready: Document)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Quick Export as PNG", () => _ = ExportSelectedLayers(true), "Quick Export Layers PNG"));
        menu.Items.Add(Item("Export As…", () => _ = ExportSelectedLayers(false), "Export Layers As"));
        menu.Items.Add(new Separator()); menu.Items.Add(Item("Rename Layer…", () => _ = RenameLayer(), "Rename Layer", Editable));
        _effectsMenu.Header = Localize.Text("La_yer Style"); _effectsMenu.Tag = "Layer Style";
        _effectsMenu.Items.Add(Item("_Copy Layer Style", CopyLayerStyle, ready: () => Editable() && PropertyLayer?.Effects is not null));
        _effectsMenu.Items.Add(Item("_Paste Layer Style", PasteLayerStyle, ready: () => Editable() && _copiedStyle is not null));
        _effectsMenu.Items.Add(Item("Cle_ar Layer Style", ClearSelectedStyles, ready: () => Editable() && PropertyLayer?.Effects is not null));
        _effectsMenu.Items.Add(new Separator());
        _effectsMenu.Items.Add(Item("Global _Light…", () => _ = GlobalLight(), ready: Document));
        _effectsMenu.Items.Add(Missing("C_reate Layers", "Separating style effects into independent layers is not supported. Rasterize Layer Style bakes the appearance."));
        _effectsMenu.Items.Add(Item("_Hide All Effects", ToggleEffects, ready: () => Editable() && PropertyLayer?.Effects is not null));
        _effectsMenu.Items.Add(Item("Scale E_ffects…", () => _ = ScaleLayerEffects(), ready: () => Editable() && PropertyLayer?.Effects is not null));
        menu.Items.Add(_effectsMenu);
        // Smart Filters: the filters a smart object carries, switched off together, their mask deleted or switched off, or cleared.
        bool Filtered() => Smart() && PropertyLayer?.SmartFilters is not null;
        var stackToggle = Item("Disable Smart Filters", () => SmartFilterCommand(SmartFilterAction.ToggleStack), ready: Filtered);
        var maskDelete = Item("Delete Filter Mask", () => SmartFilterCommand(SmartFilterAction.ToggleMask), ready: Filtered);
        var maskToggle = Item("Disa_ble Filter Mask", () => SmartFilterCommand(SmartFilterAction.ToggleMaskEnabled), ready: () => Filtered() && PropertyLayer!.SmartFilters!.HasMask);
        var clearFilters = Item("Clear Smart Filters", () => SmartFilterCommand(SmartFilterAction.Clear), ready: Filtered);
        var smartFilters = Group("Smart Filters", stackToggle, new Separator(), maskDelete, maskToggle, new Separator(), clearFilters);
        smartFilters.SubmenuOpened += (_, _) =>
        {
            var stack = PropertyLayer?.SmartFilters;
            stackToggle.Header = Localize.Text(stack?.Enabled == false ? "Enable Smart Filters" : "Disable Smart Filters");
            maskDelete.Header = Localize.Text(stack?.HasMask == false ? "Add Filter Mask" : "Delete Filter Mask");
            maskToggle.Header = Localize.Text(stack?.MaskEnabled == false ? "Ena_ble Filter Mask" : "Disa_ble Filter Mask");
        };
        menu.Items.Add(smartFilters);
        menu.Items.Add(new Separator());
        menu.Items.Add(Group("Ne_w Fill Layer", Item("S_olid Color…", () => _ = NewFillLayer(StyleEffectKind.ColorOverlay), ready: Document),
            Item("_Gradient…", () => _ = NewFillLayer(StyleEffectKind.GradientOverlay), ready: Document), Item("Patte_rn…", () => _ = NewFillLayer(StyleEffectKind.PatternOverlay), ready: Document)));
        var adjustment = Group("New Ad_justment Layer");
        // Every adjustment asks for the new layer's name, color, mode and opacity first, as Photoshop's New Layer dialog does.
        void Advanced(string title, ImageAdjustmentKind kind) => adjustment.Items.Add(Item(title, () => _ = NewAdjustmentLayer(title.Replace("_", ""), () => ExtendedImageAdjustment(kind, adjustmentLayer: true)), ready: Document));
        void Existing(string title, AdjustmentKind kind) => adjustment.Items.Add(Item(title, () => _ = NewAdjustmentLayer(title.Replace("_", ""), () => NewAdjustment(kind)), ready: Document));
        Advanced("Vib_rance…", ImageAdjustmentKind.Vibrance); Advanced("_Clarity and Dehaze…", ImageAdjustmentKind.ClarityDehaze); Existing("_Grain…", AdjustmentKind.Grain); adjustment.Items.Add(new Separator());
        Advanced("Brightness/_Contrast…", ImageAdjustmentKind.BrightnessContrast); Existing("_Levels…", AdjustmentKind.Levels); Existing("Cur_ves…", AdjustmentKind.Curves); Existing("_Exposure…", AdjustmentKind.Exposure);
        adjustment.Items.Add(new Separator()); Existing("_Hue/Saturation…", AdjustmentKind.HueSaturation); Existing("Color _Balance…", AdjustmentKind.ColorBalance);
        Existing("Blac_k & White…", AdjustmentKind.BlackWhite); Advanced("Photo _Filter…", ImageAdjustmentKind.PhotoFilter); Advanced("Channel Mi_xer…", ImageAdjustmentKind.ChannelMixer);
        Advanced("Color Lookup…", ImageAdjustmentKind.ColorLookup); Advanced("_Selective Color…", ImageAdjustmentKind.SelectiveColor); adjustment.Items.Add(new Separator());
        Existing("_Invert…", AdjustmentKind.Invert); Advanced("_Posterize…", ImageAdjustmentKind.Posterize); Advanced("_Threshold…", ImageAdjustmentKind.Threshold); Existing("Gradient _Map…", AdjustmentKind.GradientMap);
        adjustment.Items.Add(new Separator()); Existing("Reduce Noise…", AdjustmentKind.ReduceNoise); Existing("Add Noise…", AdjustmentKind.AddNoise); Existing("Gaussian Blur…", AdjustmentKind.GaussianBlur); Existing("Motion Blur…", AdjustmentKind.MotionBlur);
        menu.Items.Add(adjustment);
        menu.Items.Add(Missing("Harmonize", "Harmonize is Photoshop's generative compositing service; Compositor has no equivalent."));
        menu.Items.Add(Item("Layer Content _Options…", () => _ = EditLayerContents(), ready: () => Editable() && PropertyLayer is { } l && (l.Adjustment is not null || l.Effects is not null || l.LiveText is not null || l.LiveShape is not null)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Group("Layer _Mask",
            Item("_Reveal All", () => LayerMaskCommand("Reveal"), ready: NewMask), Item("_Hide All", () => LayerMaskCommand("Hide"), ready: NewMask),
            Item("Re_veal Selection", () => LayerMaskCommand("Selection"), ready: () => NewMask() && _document!.Selection.Path is not null),
            Item("Hi_de Selection", () => LayerMaskCommand("Inverse"), ready: () => NewMask() && _document!.Selection.Path is not null),
            Item("From _Transparency", () => LayerMaskCommand("Transparency"), ready: () => Pixels() && PropertyLayer!.Mask is null), new Separator(),
            Item("D_elete", DeleteMask, ready: Mask), Item("_Apply", () => LayerMaskCommand("Apply"), ready: () => Mask() && Pixels()), new Separator(), _maskToggle, _maskLink));
        menu.Items.Add(Group("_Vector Mask",
            Item("_Reveal All", () => VectorMaskCommand("Reveal"), ready: NewMask), Item("_Hide All", () => VectorMaskCommand("Hide"), ready: NewMask),
            Item("C_urrent Path", () => VectorMaskCommand("Path"), ready: () => NewMask() && (_pen is not null || _pathNodes is not null)), new Separator(),
            Item("_Delete", DeleteMask, ready: () => Mask() && PropertyLayer!.Mask!.VectorPath is not null), new Separator(),
            Item("Ena_ble / Disable", () => LayerMaskCommand("Toggle"), ready: () => Mask() && PropertyLayer!.Mask!.VectorPath is not null),
            Item("_Link / Unlink", () => LayerMaskCommand("Link"), ready: () => Mask() && PropertyLayer!.Mask!.VectorPath is not null)));
        menu.Items.Add(_clipping); menu.Items.Add(Item("Mask All Objects", () => _ = MaskAllObjects(), ready: Pixels)); menu.Items.Add(new Separator());
        bool Linked() => Smart() && PropertyLayer?.SmartObject?.LinkedPath is not null;
        var stack = Group("Stack Mode");
        foreach (var mode in SmartObjectStacks.Modes.Prepend(null))
        {
            var chosen = mode; var item = Item(mode ?? "None", () => SetStackMode(chosen), ready: Smart);
            item.ToggleType = MenuItemToggleType.Radio; stack.Items.Add(item);
            if (mode is null) stack.Items.Add(new Separator());
        }
        stack.SubmenuOpened += (_, _) => { var current = PropertyLayer?.SmartObject?.StackMode; foreach (var item in stack.Items.OfType<MenuItem>()) item.IsChecked = Equals(item.Tag, current ?? "None"); };
        menu.Items.Add(Group("Smart Objects",
            Item("Convert to _Smart Object", ConvertToSmartObject, ready: Editable), Item("New Smart Object via _Copy", IndependentSmartCopy, ready: Smart), new Separator(),
            Item("Reveal in Explorer", RevealLinkedFile, ready: Linked), new Separator(),
            Item("_Update Modified Content", () => UpdateLinked(true), ready: Linked),
            Item("Update _All Modified Content", () => UpdateLinked(false), ready: () => _document?.Layers.Any(l => l.SmartObject?.LinkedPath is not null) == true), new Separator(),
            Item("_Edit Contents", EditSmartContents, ready: Smart), Item("Reset Transform", ResetSmartTransform, ready: Smart),
            Item("Relink to _File…", () => _ = RelinkSmartObject(), ready: Smart),
            Missing("Relink to _Library Graphic…", "Creative Cloud Libraries are not available in Compositor."),
            Item("_Replace Contents…", () => _ = ReplaceSmartContents(), ready: Smart), Item("E_xport Contents…", () => _ = ExportSmartContents(), ready: Smart), new Separator(),
            Item("Embed Linked", EmbedLinked, ready: Linked),
            Item("Embed All Linked", EmbedAllLinked, ready: () => _document?.Layers.Any(l => l.SmartObject?.LinkedPath is not null) == true),
            Item("Convert to Linked…", () => _ = ConvertToLinked(), ready: () => Smart() && PropertyLayer?.SmartObject?.LinkedPath is null),
            Item("Convert to Layers", UnpackSmartObject, ready: Smart), new Separator(),
            stack, Item("Rasterize", () => RasterizeSelected("Smart"), ready: Smart)));
        const string NoVideo = "Video layers need a video timeline; Compositor imports video as frames instead.";
        menu.Items.Add(Group("Video Layers",
            Item("New Video Layer from File…", () => _ = ImportVideoFrames(), ready: Document), new Separator(),
            Missing("New Blank Video Layer", NoVideo), Missing("Insert Blank Frame", NoVideo), Missing("Duplicate Frame", NoVideo), Missing("Delete Frame", NoVideo), new Separator(),
            Missing("Replace Footage…", NoVideo), Missing("Interpret Footage…", NoVideo), new Separator(), Missing("Show Altered Video", NoVideo), new Separator(),
            Missing("Restore Frame", NoVideo), Missing("Restore All Frames", NoVideo), new Separator(), Missing("Reload Frame", NoVideo), new Separator(), Missing("Rasterize", NoVideo)));
        menu.Items.Add(Group("Rasteri_ze",
            Item("_Type", () => RasterizeSelected("Type"), ready: () => Editable() && PropertyLayer?.LiveText is not null),
            Item("_Shape", () => RasterizeSelected("Shape"), ready: () => Editable() && PropertyLayer?.LiveShape is not null),
            Item("_Fill Content", () => RasterizeSelected("Effects"), ready: () => Editable() && PropertyLayer?.Effects is not null),
            Item("_Vector Mask", () => LayerMaskCommand("Rasterize"), ready: () => Mask() && PropertyLayer!.Mask!.VectorPath is not null),
            Item("Smart _Object", () => RasterizeSelected("Smart"), ready: Smart),
            Missing("_Generative", "Generative layers are not part of Compositor."), Missing("Vi_deo", NoVideo), Missing("_3D", "This editor has no 3D scene engine."),
            Item("Layer Style", () => RasterizeSelected("Effects"), ready: () => Editable() && PropertyLayer?.Effects is not null),
            new Separator(), Item("_Layer", () => RasterizeSelected("Layer"), ready: Editable), Item("_All Layers", () => RasterizeSelected("All"), ready: Document)));
        menu.Items.Add(Item("Convert Layer to 16-Bit", () => ConvertSelectedDepth(16), ready: () => Editable() && DeepPaint.CanConvertLayers(_document!, SelectedLayers, 16)));
        menu.Items.Add(Item("Convert Layer to 8-Bit", () => ConvertSelectedDepth(8), ready: () => Editable() && DeepPaint.CanConvertLayers(_document!, SelectedLayers, 8)));
        menu.Items.Add(new Separator()); menu.Items.Add(Item("New Layer _Based Slice", LayerSlice));
        menu.Items.Add(new Separator()); menu.Items.Add(Item("_Group Layers", GroupSelected, "Group Layers", Editable));
        menu.Items.Add(Item("_Ungroup Layers", UngroupSelected, "Ungroup Layers", () => Editable() && PropertyLayer is { IsGroup: true })); menu.Items.Add(Item("Hide Laye_rs", ToggleSelectedVisibility, "Hide Layers"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Group("_Arrange", Item("Bring to _Front", () => ArrangeLayers("Front"), "Bring to Front", Editable),
            Item("Bring For_ward", () => ArrangeLayers("Forward"), "Move Layer Up", Editable), Item("Send Bac_kward", () => ArrangeLayers("Backward"), "Move Layer Down", Editable),
            Item("Send to _Back", () => ArrangeLayers("Back"), "Send to Back", Editable), new Separator(), Item("_Reverse", () => ArrangeLayers("Reverse"), ready: () => Editable() && SelectedLayers.Count > 1)));
        var shapes = Group("Combine S_hapes");
        foreach (var (label, op) in new[] { ("Unite Shapes", SKPathOp.Union), ("Subtract Front Shape", SKPathOp.Difference), ("Unite Shapes at Overlap", SKPathOp.Intersect), ("Subtract Shapes at Overlap", SKPathOp.Xor) })
        { var item = Item(label, () => CombineLayerShapes(op), ready: () => _document is { } doc && LayerMenuEdits.CanCombineShapes(doc, SelectedLayers)); item.Icon = new ShapeOperationGlyph(op); shapes.Items.Add(item); }
        menu.Items.Add(shapes);
        menu.Items.Add(new Separator());
        var align = Group("Al_ign");
        foreach (var (operation, title) in new[] { ("Align Top", "_Top Edges"), ("Align V Center", "_Vertical Centers"), ("Align Bottom", "_Bottom Edges"), ("", ""), ("Align Left", "_Left Edges"), ("Align H Center", "_Horizontal Centers"), ("Align Right", "_Right Edges") })
        {
            if (operation.Length == 0) { align.Items.Add(new Separator()); continue; }
            var op = operation; var item = Item(title, () => AlignProperties(op), ready: () => Editable() && (SelectedLayers.Count > 1 || _document!.Selection.Path is not null)); item.Icon = new InspectorGlyph(op); align.Items.Add(item);
        }
        menu.Items.Add(align);
        var distribute = Group("Dis_tribute");
        foreach (var (operation, title) in new[] { ("Distribute Top", "_Top Edges"), ("Distribute Vertically", "_Vertical Centers"), ("Distribute Bottom", "_Bottom Edges"), ("", ""),
            ("Distribute Left", "_Left Edges"), ("Distribute Horizontally", "_Horizontal Centers"), ("Distribute Right", "_Right Edges"), ("", ""),
            ("Distribute Horizontal Spacing", "H_orizontal"), ("Distribute Vertical Spacing", "Verti_cal") })
        {
            if (operation.Length == 0) { distribute.Items.Add(new Separator()); continue; }
            var op = operation; var item = Item(title, () => AlignProperties(op), ready: () => Editable() && SelectedLayers.Count >= 3); item.Icon = new InspectorGlyph(op); distribute.Items.Add(item);
        }
        menu.Items.Add(distribute);
        // With a selection, Photoshop's Align becomes Align Layers to Selection.
        menu.SubmenuOpened += (_, _) => align.Header = Localize.Text(_document?.Selection.Path is not null ? "Align Layers to Select_ion" : "Al_ign");
        menu.Items.Add(new Separator()); menu.Items.Add(Item("_Lock Layers…", () => _ = LayerLocksDialog(), "Lock Layers Dialog"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Lin_k Layers", LinkLayers, ready: () => Editable() && SelectedLayers.Count > 1)); menu.Items.Add(Item("_Select Linked Layers", SelectLinkedLayers, ready: () => PropertyLayer?.LinkID is not null));
        menu.Items.Add(new Separator()); menu.Items.Add(_merge); menu.Items.Add(Item("Merge Visible", () => MergeAll(false), "Merge Visible", Document)); menu.Items.Add(Item("_Flatten Image", () => _ = FlattenImage(), ready: Document));
        menu.Items.Add(new Separator()); menu.Items.Add(Group("Matting",
            Item("_Color Decontaminate…", () => _ = DefringeLayer(true), ready: Pixels), Item("_Defringe…", () => _ = DefringeLayer(false), ready: Pixels),
            Item("Remove _Black Matte", () => RemoveMatte(false), ready: Pixels), Item("Remove _White Matte", () => RemoveMatte(true), ready: Pixels)));
        menu.SubmenuOpened += (_, _) => { foreach (var (item, ready) in _layerMenuAvailability) item.IsEnabled = ready(); };
        return menu;
    }
}
