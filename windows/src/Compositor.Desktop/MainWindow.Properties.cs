using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private readonly TabControl _inspectorTabs = new();
    private readonly StackPanel _properties = new() { Spacing = 8, Margin = new Thickness(10) };
    private readonly Dictionary<string, bool> _propertySections = [];
    private bool _refreshingLayerRows;
    private Guid? _maskTargetLayer;
    private Tab? _maskTargetTab;
    private bool _proportionLocked = true, _propertiesPending;
    private AlignmentTarget _alignmentTarget = AlignmentTarget.Auto;
    private string _propertiesKey = "";
    private ImageLayer? PropertyLayer => _document?.Layers.FirstOrDefault(layer => layer.ID == Selected);
    private bool MaskTarget => _options.PaintOnMask && PropertyLayer?.Mask is not null;

    private void SyncMaskTarget()
    {
        if (!_refreshingLayerRows && _options.PaintOnMask && (!ReferenceEquals(_maskTargetTab, _open)
            || _maskTargetLayer != Selected || PropertyLayer?.Mask is null)) SetPaintingMask(false);
    }

    private Control BuildInspector(Control layers)
    {
        _inspectorTabs.Items.Add(new TabItem { Header = Localize.Text("Properties"), FontSize = 11.5, FontWeight = FontWeight.SemiBold,
            Content = new ScrollViewer { Content = _properties, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } });
        _inspectorTabs.Items.Add(new TabItem { Header = Localize.Text("History"), FontSize = 11.5, FontWeight = FontWeight.SemiBold, Content = BuildHistoryPanel() });
        _inspectorTabs.SelectedIndex = 0;
        var dock = new Grid { RowDefinitions = new RowDefinitions("4*,4,5*") };
        _inspectorTabs.MinHeight = 100; layers.MinHeight = 150;
        dock.Children.Add(_inspectorTabs);
        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Rows, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Skin.BorderSubtleBrush };
        Grid.SetRow(splitter, 1); dock.Children.Add(splitter);
        Grid.SetRow(layers, 2); dock.Children.Add(layers);
        return dock;
    }

    private void OpenInspector(int tab)
    {
        _layersSide.IsVisible = true; _inspectorTabs.SelectedIndex = tab;
        RefreshInspector();
    }

    private void RefreshInspector()
    {
        if (_propertiesPending) return;
        _propertiesPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _propertiesPending = false;
            var key = $"{_document?.ID}:{_history.CurrentRevision}:{Selected}:{MaskTarget}:{string.Join(',', SelectedLayers)}";
            if (key != _propertiesKey) { _propertiesKey = key; BuildProperties(); }
            RefreshHistory();
            foreach (var thumbnail in _layerThumbnails) thumbnail.InvalidateVisual();
        }, DispatcherPriority.Background);
    }

    private void BuildProperties()
    {
        _properties.Children.Clear();
        if (_document is not { } document) { _properties.Children.Add(PropertyLabel("Open a document to view its properties")); return; }
        var layer = PropertyLayer;
        if (layer is null)
        {
            _properties.Children.Add(PropertyLabel("Document"));
            _properties.Children.Add(new TextBlock { Text = $"{document.Width} × {document.Height} px · {document.Resolution:0.##} ppi · RGB / 8", FontSize = 12 });
            _properties.Children.Add(PropertyAction("Image Size…", () => _ = ImageSize()));
            _properties.Children.Add(PropertyAction("Canvas Size…", () => _ = CanvasSize())); return;
        }
        var heading = MaskTarget ? layer.Mask!.VectorPath is null ? "Layer Mask" : "Vector Mask"
            : SelectedLayers.Count > 1 ? "Multiple Layers" : layer.IsGroup ? "Group" : layer.LiveText is not null ? "Type Layer"
            : layer.LiveShape is not null ? "Shape Layer" : layer.Adjustment is not null ? "Adjustment Layer" : "Pixel Layer";
        _properties.Children.Add(new TextBlock { Text = Localize.Text(heading), FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Skin.LabelBrush });
        _properties.Children.Add(new TextBlock { Text = layer.Name, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Skin.SecondaryBrush });
        if (MaskTarget) { BuildMaskProperties(layer); return; }
        BuildTransformProperties(layer);
        if (layer.LiveText is { } text) BuildTextProperties(layer.ID, text);
        if (layer.LiveShape is not null) BuildShapeProperties(layer);
        if (layer.Adjustment is not null)
            Section("Adjustments", PropertyAction("Adjustment Settings…", () => _ = EditAdjustment()));
        BuildAlignmentProperties();
        var quick = new StackPanel { Spacing = 5 };
        if (layer.Asset is not null && layer.Adjustment is null)
        {
            quick.Children.Add(PropertyAction("Remove Background", () => _ = DetectSubject(true)));
            quick.Children.Add(PropertyAction("Select Subject", () => _ = DetectSubject(false)));
            quick.Children.Add(PropertyAction("Select Layer Pixels", SelectLayerPixels));
        }
        if (layer.Mask is null)
        {
            quick.Children.Add(PropertyAction("Add Mask from Selection", () => AddSelectionMask(false)));
            quick.Children.Add(PropertyAction("Add Vector Mask from Selection", () => AddSelectionMask(true)));
        }
        else quick.Children.Add(PropertyAction("Edit Layer Mask", () => SetPaintingMask(true)));
        if (layer.LiveText is not null || layer.LiveShape is not null)
            quick.Children.Add(PropertyAction("Rasterize Layer", () => Edit("Rasterize Layer", () => { if (PropertyLayer is not { } current) return false; current.Text = null; current.Shape = null; return true; })));
        if (quick.Children.Count > 0) Section("Quick Actions", quick);
    }

    private static TextBlock PropertyLabel(string text) => new() { Text = Localize.Text(text), FontSize = 11, Foreground = Skin.SecondaryBrush, TextWrapping = TextWrapping.Wrap };
    private static Button PropertyAction(string text, Action action)
    {
        var button = new Button
        {
            Content = Localize.Text(text),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            MinHeight = 24,
            Height = 24,
            FontSize = 11,
            Padding = new Thickness(6, 2),
            CornerRadius = new CornerRadius(7),
            Background = Skin.SurfaceControlBrush,
            BorderBrush = Skin.BorderControlBrush,
            BorderThickness = new Thickness(1),
            Foreground = Skin.LabelBrush,
            Tag = text,
        };
        ToolTip.SetTip(button, Localize.Text(text));
        button.Click += (_, _) => action(); return button;
    }
    private static Button InspectorButton(string icon, string text, Action action)
    {
        var button = new Button { Content = new InspectorGlyph(icon), Width = 34, Height = 30, Padding = new Thickness(5, 4),
            Background = Brushes.Transparent, Tag = text };
        ToolTip.SetTip(button, Localize.Text(text));
        Avalonia.Automation.AutomationProperties.SetName(button, Localize.Text(text));
        button.Click += (_, _) => action(); return button;
    }
    private static Control InlineProperty(string label, Control input)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*") };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = Skin.SecondaryBrush });
        input.MinHeight = 28;
        Grid.SetColumn(input, 1); row.Children.Add(input); return row;
    }
    private void Section(string title, params Control[] contents)
    {
        var body = new StackPanel { Spacing = 7, Margin = new Thickness(0, 5, 0, 8) };
        foreach (var item in contents) body.Children.Add(item);
        var section = new Expander { Header = Localize.Text(title), Content = body, IsExpanded = _propertySections.GetValueOrDefault(title, true),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0) };
        section.PropertyChanged += (_, e) => { if (e.Property == Expander.IsExpandedProperty) _propertySections[title] = section.IsExpanded; };
        _properties.Children.Add(section);
    }
    private static Control PropertyPair(Control first, Control second)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        first.Margin = new Thickness(0, 0, 4, 0); second.Margin = new Thickness(4, 0, 0, 0);
        row.Children.Add(first); Grid.SetColumn(second, 1); row.Children.Add(second); return row;
    }
    private static Control PropertyField(string label, Control field) => new StackPanel { Spacing = 2, Children = { PropertyLabel(label), field } };
    private static NumericUpDown PropertyNumber(string tag, double value, double min, double max, Action<double> changed)
    {
        var input = new NumericUpDown
        {
            Value = (decimal)value,
            Minimum = (decimal)min,
            Maximum = (decimal)max,
            FormatString = "0.##",
            ShowButtonSpinner = false,
            Increment = 1,
            Height = 24,
            FontSize = 11,
            CornerRadius = new CornerRadius(7),
            Background = Skin.SurfaceDarkBrush,
            BorderBrush = Skin.BorderControlBrush,
            Foreground = Skin.LabelBrush,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Tag = tag,
        };
        var last = (decimal)value;
        void Commit()
        {
            if (input.Value is not { } next || next == last) return;
            last = next; changed((double)next);
        }
        input.LostFocus += (_, _) => Commit();
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        ToolTip.SetTip(input, Localize.Text("Enter or leave the field to apply"));
        return input;
    }

    private void BuildTransformProperties(ImageLayer layer)
    {
        var box = SelectedLayers.Count > 1 || layer.IsGroup ? TransformEdits.GroupBox(_document!, SelectedLayers) : layer.Transform;
        if (box is not { } transform) return;
        var locked = new ToggleButton { Content = new InspectorGlyph("link"), IsChecked = _proportionLocked, Width = 30, Height = 34, Padding = new Thickness(4), Tag = "Constrain Proportions" };
        ToolTip.SetTip(locked, Localize.Text("Constrain Proportions"));
        locked.IsCheckedChanged += (_, _) => _proportionLocked = locked.IsChecked == true;
        NumericUpDown Number(string axis, double v, double min, double max) => PropertyNumber("transform-" + axis, v, min, max, next =>
        {
            var draft = axis switch
            {
                "width" => transform with { Width = next, Height = _proportionLocked ? transform.Height * next / transform.Width : transform.Height },
                "height" => transform with { Height = next, Width = _proportionLocked ? transform.Width * next / transform.Height : transform.Width },
                "x" => transform with { X = next }, "y" => transform with { Y = next }, _ => transform with { Rotation = next },
            };
            SetPropertyTransform(draft);
        });
        var controls = new Grid { ColumnDefinitions = new ColumnDefinitions("34,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), IsEnabled = LayerProtection.CanMove(_document!, layer.ID) };
        controls.Children.Add(locked); Grid.SetRowSpan(locked, 2);
        void Put(Control control, int row, int column) { control.Margin = new Thickness(3); Grid.SetRow(control, row); Grid.SetColumn(control, column); controls.Children.Add(control); }
        Put(InlineProperty("W", Number("width", transform.Width, 1, 300000)), 0, 1);
        Put(InlineProperty("X", Number("x", transform.X, -1000000, 1000000)), 0, 2);
        Put(InlineProperty("H", Number("height", transform.Height, 1, 300000)), 1, 1);
        Put(InlineProperty("Y", Number("y", transform.Y, -1000000, 1000000)), 1, 2);
        Put(new InspectorGlyph("rotation"), 2, 0);
        var angle = Number("rotation", transform.Rotation, -36000, 36000);
        ToolTip.SetTip(angle, Localize.Text("Rotation in degrees — Enter to apply")); Put(angle, 2, 1);
        var flips = new StackPanel { Orientation = Orientation.Horizontal };
        flips.Children.Add(InspectorButton("flip-x", "Flip Horizontal", () => SetPropertyTransform(transform with { FlipX = !transform.FlipX })));
        flips.Children.Add(InspectorButton("flip-y", "Flip Vertical", () => SetPropertyTransform(transform with { FlipY = !transform.FlipY })));
        flips.Children.Add(InspectorButton("reset", "Reset Rotation", () => SetPropertyTransform(transform with { Rotation = 0 })));
        Put(flips, 2, 2); Section("Transform", controls);
    }
    private void SetPropertyTransform(LayerTransform draft)
    {
        if (_document is not { } document || PropertyLayer is not { } layer || !draft.IsValid) return;
        Edit("Transform", () =>
        {
            if (SelectedLayers.Count == 1 && !layer.IsGroup) { TransformEdits.SetPlacement(layer, draft); return true; }
            if (TransformEdits.GroupBox(document, SelectedLayers) is not { } from) return false;
            return TransformEdits.Carry(document, TransformEdits.Originals(document, SelectedLayers), from, draft);
        });
    }

    private void BuildAlignmentProperties()
    {
        var all = new StackPanel { Spacing = 9 };
        Control Row(params string[] labels)
        {
            var row = new UniformGrid { Columns = labels.Length, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var label in labels)
            {
                var button = InspectorButton(label, label, () => AlignProperties(label));
                button.IsEnabled = !label.StartsWith("Distribute") || SelectedLayers.Count >= (_alignmentTarget is AlignmentTarget.Canvas or AlignmentTarget.Selection ? 2 : 3);
                row.Children.Add(button);
            }
            return row;
        }
        all.Children.Add(PropertyLabel("Align:"));
        all.Children.Add(Row("Align Left", "Align H Center", "Align Right", "Align Top", "Align V Center", "Align Bottom"));
        all.Children.Add(new Separator());
        all.Children.Add(PropertyLabel("Distribute:"));
        all.Children.Add(Row("Distribute Top", "Distribute Vertically", "Distribute Bottom", "Distribute Left", "Distribute Horizontally", "Distribute Right"));
        all.Children.Add(new Separator());
        var target = new ComboBox { ItemsSource = new[] { Localize.Text("Automatic"), Localize.Text("Selected Layers"), Localize.Text("Canvas"), Localize.Text("Selection") },
            SelectedIndex = (int)_alignmentTarget, MinWidth = 130, Tag = "alignment-target" };
        ToolTip.SetTip(target, Localize.Text("Reference bounds for alignment and distribution"));
        target.SelectionChanged += (_, _) => { _alignmentTarget = (AlignmentTarget)Math.Max(0, target.SelectedIndex); BuildProperties(); };
        all.Children.Add(PropertyPair(PropertyField("Distribute Spacing:", Row("Distribute Vertical Spacing", "Distribute Horizontal Spacing")), PropertyField("Align to:", target)));
        var more = InspectorButton("more", "Alignment Options", () => { });
        more.HorizontalAlignment = HorizontalAlignment.Right;
        more.Click += (_, _) =>
        {
            var menu = new ContextMenu();
            menu.Items.Add(Command("Select All Layers", () => { _layers.SelectAll(); BuildProperties(); }));
            menu.Items.Add(Command("Align to Selection", () => { _alignmentTarget = AlignmentTarget.Selection; BuildProperties(); }));
            menu.Items.Add(Command("Align to Canvas", () => { _alignmentTarget = AlignmentTarget.Canvas; BuildProperties(); }));
            menu.Open(more);
        };
        all.Children.Add(more); Section("Align and Distribute", all);
    }
    private void AlignProperties(string operation)
    {
        if (_document is not { } document) return;
        if (_alignmentTarget == AlignmentTarget.Selection && document.Selection.Path is not { IsEmpty: false }) { Say("Create a selection to use it as the alignment reference."); return; }
        Edit(operation, () => LayerAlignment.Apply(document, SelectedLayers, operation, _alignmentTarget));
    }

    private void BuildTextProperties(Guid id, LayerTextStyle text)
    {
        void Change(Action<LayerTextStyle> modify)
        {
            if (_document is not { } document || PropertyLayer?.ID != id) return;
            var style = JsonSerializer.Deserialize<LayerTextStyle>(JsonSerializer.Serialize(text, ManifestJson.Options), ManifestJson.Options)!;
            modify(style); Edit("Character Properties", () => TextEdits.SetStyle(document, id, style));
            ShowLayers(document);
        }
        var font = new TextBox { Text = text.FontName, Tag = "property-font" };
        font.LostFocus += (_, _) => { if (!string.IsNullOrWhiteSpace(font.Text) && font.Text != text.FontName) Change(style => { style.FontName = font.Text.Trim(); style.FontRuns = null; }); };
        var formatting = new WrapPanel();
        foreach (var (name, on) in new[] { ("Bold", text.Bold), ("Italic", text.Italic), ("Underline", text.Underline), ("Strikethrough", text.Strikethrough) })
        {
            var button = new ToggleButton { Content = Localize.Text(name), IsChecked = on == true, Padding = new Thickness(6, 3), Margin = new Thickness(2), FontSize = 11 };
            button.Click += (_, _) => Change(style => { bool? value = button.IsChecked == true ? true : null;
                switch (name) { case "Bold": style.Bold = value; break; case "Italic": style.Italic = value; break; case "Underline": style.Underline = value; break; default: style.Strikethrough = value; break; } });
            formatting.Children.Add(button);
        }
        // The OpenType and script switches: small caps, super/subscript, ligatures and kerning, plus the
        // reading direction. They rerender the layer through the same Change the rest use.
        var script = new WrapPanel();
        foreach (var (name, on) in new[]
                 {
                     ("Small Caps", text.SmallCaps), ("All Caps", text.AllCaps), ("Superscript", text.Superscript),
                     ("Subscript", text.Subscript), ("Ligatures", text.Ligatures), ("Kerning", text.Kerning),
                 })
        {
            var button = new ToggleButton { Content = Localize.Text(name), IsChecked = on == true, Padding = new Thickness(6, 3), Margin = new Thickness(2), FontSize = 11 };
            button.Click += (_, _) =>
            {
                bool? value = button.IsChecked == true ? true : null;
                Change(style =>
                {
                    switch (name)
                    {
                        case "Small Caps": style.SmallCaps = value; break;
                        case "All Caps": style.AllCaps = value; break;
                        case "Superscript": style.Superscript = value; break;
                        case "Subscript": style.Subscript = value; break;
                        case "Ligatures": style.Ligatures = value; break;
                        default: style.Kerning = value; break;
                    }
                });
            };
            script.Children.Add(button);
        }
        var direction = new ComboBox
        {
            ItemsSource = new[] { Localize.Text("Auto direction"), Localize.Text("Left to right"), Localize.Text("Right to left") },
            SelectedIndex = (int)text.Direction,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        direction.SelectionChanged += (_, _) => Change(style => style.Direction = (Compositor.Core.Format.TextDirection)Math.Max(0, direction.SelectedIndex));
        var dynamic = new CheckBox { Content = Localize.Text("Dynamic text (fills {width}, {date} and other tokens)"), IsChecked = text.Dynamic == true };
        dynamic.IsCheckedChanged += (_, _) => Change(style => style.Dynamic = dynamic.IsChecked == true ? true : null);
        var color = new TextBox { Text = $"#{(int)(text.Red * 255):X2}{(int)(text.Green * 255):X2}{(int)(text.Blue * 255):X2}", Tag = "property-text-color" };
        var previousColor = color.Text;
        color.LostFocus += (_, _) => { if (color.Text != previousColor && SKColor.TryParse(color.Text, out var parsed)) { previousColor = color.Text; Change(style => { style.Red = parsed.Red / 255.0; style.Green = parsed.Green / 255.0; style.Blue = parsed.Blue / 255.0; style.ColorRuns = null; }); } };
        Section("Character", PropertyField("Font Family", font),
            PropertyPair(PropertyField("Size (pt)", PropertyNumber("text-size", text.FontSize * 72 / _document!.Resolution, 1, 2000, v => Change(style => style.FontSize = v * _document.Resolution / 72))),
                PropertyField("Leading (px, 0 = Auto)", PropertyNumber("text-leading", text.Leading, 0, 5000, v => Change(style => style.Leading = v)))),
            PropertyPair(PropertyField("Tracking (px)", PropertyNumber("text-tracking", text.Tracking, -100, 1000, v => Change(style => style.Tracking = v))), PropertyField("Text Color (#RRGGBB)", color)), formatting);
        var alignment = new ComboBox { ItemsSource = new[] { Localize.Text("Left"), Localize.Text("Center"), Localize.Text("Right") }, SelectedIndex = (int)text.Alignment, HorizontalAlignment = HorizontalAlignment.Stretch };
        alignment.SelectionChanged += (_, _) => Change(style => { style.Alignment = (Compositor.Core.Format.TextAlignment)alignment.SelectedIndex; style.BoxSize ??= new JsonSize { Width = PropertyLayer!.Asset!.Width, Height = PropertyLayer.Asset.Height }; });
        Section("Paragraph", alignment, PropertyAction("Edit Text…", () => _ = EditText()),
            PropertyPair(PropertyAction("Bulleted List", () => Change(style => { style.Content = string.Join('\n', style.Content.Split('\n').Select(line => line.StartsWith("• ") ? line[2..] : "• " + line)); style.ColorRuns = null; style.FontRuns = null; })),
                PropertyAction("Numbered List", () => Change(style => { style.Content = string.Join('\n', style.Content.Split('\n').Select((line, index) => $"{index + 1}. {line}")); style.ColorRuns = null; style.FontRuns = null; }))),
            PropertyPair(PropertyAction("Uppercase", () => Change(style => { style.Content = style.Content.ToUpperInvariant(); style.ColorRuns = null; style.FontRuns = null; })),
                PropertyAction("Lowercase", () => Change(style => { style.Content = style.Content.ToLowerInvariant(); style.ColorRuns = null; style.FontRuns = null; }))));
        Section("OpenType and Script", script, direction);
        Section("Dynamic Text", dynamic);
        Section("Convert", PropertyPair(
            PropertyAction("Convert to Frame", () => Edit("Convert to Frame", () => TextEdits.MakeFrame(_document!, id))),
            PropertyAction("Convert to Vector Shape", () => Edit("Convert to Vector Shape", () => TextEdits.ToVectorShape(_document!, id)))));
    }

    private void BuildMaskProperties(ImageLayer layer)
    {
        var mask = layer.Mask!;
        var thumb = new LayerThumbnail(() => PropertyLayer?.Mask?.Asset.Thumbnail) { Width = 54, Height = 54, HorizontalAlignment = HorizontalAlignment.Left };
        _properties.Children.Add(thumb);
        void Change(double density, double feather) { if (_document is { } doc) Edit("Mask Properties", () => MaskProperties.Set(doc, layer.ID, density, feather)); }
        Control Amount(string title, string tag, double value, double maximum, Action<double> apply)
        {
            var slider = new Slider { Minimum = 0, Maximum = maximum, Value = value, Tag = tag + "-slider" };
            slider.PointerReleased += (_, _) => apply(slider.Value); slider.KeyUp += (_, _) => apply(slider.Value);
            return new StackPanel { Spacing = 4, Children = { PropertyField(title, PropertyNumber(tag, value, 0, maximum, apply)), slider } };
        }
        var body = new StackPanel { Spacing = 10, IsEnabled = !LayerProtection.Effective(_document!, layer.ID).HasFlag(LayerLocks.All) && !LayerProtection.Effective(_document!, layer.ID).HasFlag(LayerLocks.Pixels) };
        body.Children.Add(Amount("Density (%)", "mask-density", mask.Density * 100, 100, value => Change(value / 100, mask.Feather)));
        body.Children.Add(Amount("Feather (px)", "mask-feather", mask.Feather, 1000, value => Change(mask.Density, value)));
        body.Children.Add(PropertyAction("Select and Mask…", () => _ = RefineMask()));
        body.Children.Add(PropertyAction("Load Mask as Selection", () => Edit("Mask to Selection", () => MaskProperties.LoadSelection(_document!, layer.ID))));
        body.Children.Add(PropertyAction("Replace Mask from Selection", () => AddSelectionMask(mask.VectorPath is not null)));
        body.Children.Add(PropertyAction("Color Range…", () => ColorRange(layer.ID)));
        body.Children.Add(PropertyAction("Invert Mask", () => Edit("Invert Mask", () => MaskProperties.Invert(_document!, layer.ID))));
        body.Children.Add(PropertyAction(mask.IsLinked ? "Unlink Layer Mask" : "Link Layer Mask", ToggleMaskLink));
        body.Children.Add(PropertyAction(mask.IsEnabled ? "Disable Layer Mask" : "Enable Layer Mask", ToggleMask));
        if (layer.Asset is not null && !layer.IsGroup)
            body.Children.Add(PropertyAction("Apply Layer Mask", () => { Edit("Apply Layer Mask", () => MaskProperties.Apply(_document!, layer.ID)); SetPaintingMask(false); Reselect(layer.ID); }));
        if (mask.VectorPath is not null)
        {
            body.Children.Add(PropertyAction("Edit Path Nodes", () => SetTool(Tool.Path)));
            body.Children.Add(PropertyAction("Rasterize Mask", () => Edit("Rasterize Mask", () => { layer.Mask!.VectorPath = null; return true; })));
        }
        body.Children.Add(PropertyAction("Delete Layer Mask", () => _ = ConfirmDeleteMask()));
        Section("Mask", body);
    }

    private void BuildShapeProperties(ImageLayer layer)
    {
        var source = layer.LiveShape!;
        void Change(Action<LayerShapeStyle> change)
        {
            if (_document is not { } document || PropertyLayer?.ID != layer.ID) return;
            var style = JsonSerializer.Deserialize<LayerShapeStyle>(JsonSerializer.Serialize(source, ManifestJson.Options), ManifestJson.Options)!;
            change(style);
            Edit("Shape Properties", () =>
            {
                if (ShapeEdits.Image(style, layer.Asset!.Width, layer.Asset.Height) is not { } image) return false;
                layer.Asset = ImportedImage.Create(image, layer.Name); layer.Shape = new LayerShape(style, image); return true;
            });
        }
        var color = new TextBox { Text = $"#{(int)(source.Red * 255):X2}{(int)(source.Green * 255):X2}{(int)(source.Blue * 255):X2}" };
        var previous = color.Text;
        color.LostFocus += (_, _) => { if (previous != color.Text && SKColor.TryParse(color.Text, out var parsed)) { previous = color.Text; Change(style => { style.Red = parsed.Red / 255.0; style.Green = parsed.Green / 255.0; style.Blue = parsed.Blue / 255.0; }); } };
        var fields = new List<Control> { PropertyField("Fill Color (#RRGGBB)", color) };
        if (source.Kind == ShapeKind.Rectangle) fields.Add(PropertyField("Corner Radius (px)", PropertyNumber("shape-radius", source.CornerRadius, 0, 1000, value => Change(style => style.CornerRadius = value))));
        if (source.Kind == ShapeKind.Line) fields.Add(PropertyField("Line Width (px)", PropertyNumber("shape-line", source.LineWidth ?? 1, 1, 1000, value => Change(style => style.LineWidth = value))));
        Section("Shape", fields.ToArray());
    }

    private void AddSelectionMask(bool vector)
    {
        if (_document is not { } document || Selected is not { } id) return;
        Edit(vector ? "Vector Mask from Selection" : "Mask from Selection", () => MaskProperties.FromSelection(document, id, vector));
        Reselect(id); SetPaintingMask(true);
    }

    private async Task RefineMask()
    {
        if (_document is not { } document || PropertyLayer is not { Mask: { } mask } layer) return;
        var original = document.Clone(); var revision = _history.CurrentRevision;
        try
        {
            var result = await MaskRefineDialog.Ask(this, mask, settings =>
            {
                var draft = original.Clone();
                MaskProperties.Refine(draft, layer.ID, settings.Density, settings.Feather, settings.Shift, settings.Smooth);
                _canvas.PreviewDocument = draft; _canvas.InvalidateVisual();
            });
            if (result is null || !ReferenceEquals(document, _document) || _history.CurrentRevision != revision) return;
            Edit("Refine Mask", () => MaskProperties.Refine(document, layer.ID, result.Density, result.Feather, result.Shift, result.Smooth));
            Reselect(layer.ID);
        }
        finally { _canvas.PreviewDocument = null; _canvas.InvalidateVisual(); }
    }
    private async Task ConfirmDeleteMask()
    {
        if (_document is not { } document || PropertyLayer is not { Mask: not null } layer) return;
        var revision = _history.CurrentRevision;
        if (!await ConfirmDialog.Ask(this, "Delete Layer Mask?", "Delete the selected mask? The layer and its image will be kept.", "Delete Mask", "Cancel")) return;
        if (!ReferenceEquals(_document, document) || revision != _history.CurrentRevision) return;
        Edit("Delete Layer Mask", () => LayerMaskEdits.Remove(document, layer.ID));
        SetPaintingMask(false); Reselect(layer.ID);
    }
    private void DeletePanelTarget() { if (MaskTarget) _ = ConfirmDeleteMask(); else DeleteLayer(); }
}
