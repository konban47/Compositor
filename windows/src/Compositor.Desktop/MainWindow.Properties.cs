using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
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
    private readonly StackPanel _properties = new() { Spacing = 0, Background = PropertySurface };
    private readonly Dictionary<string, bool> _propertySections = [];
    private bool _refreshingLayerRows;
    private Guid? _maskTargetLayer;
    private Tab? _maskTargetTab;
    private bool _proportionLocked = true, _propertiesPending;
    private AlignmentTarget _alignmentTarget = AlignmentTarget.Auto;
    private string _propertiesKey = "";
    private ImageLayer? PropertyLayer => _layers.SelectedIndex >= 0 ? _document?.Layers.FirstOrDefault(layer => layer.ID == Selected) : null;
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
        return _dockRoot;
    }

    private void OpenInspector(int tab)
    {
        _layersSide.IsVisible = true;
        if (_dockPanels.Count > 0) ShowDockPanel(tab == 0 ? "Properties" : "History");
        RefreshInspector();
    }

    private void RefreshInspector()
    {
        if (_propertiesPending) return;
        _propertiesPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _propertiesPending = false;
            var key = $"{_document?.ID}:{_history.CurrentRevision}:{Selected}:{_layers.SelectedIndex}:{MaskTarget}:{string.Join(',', SelectedLayers)}";
            if (key != _propertiesKey) { _propertiesKey = key; BuildProperties(); }
            RefreshHistory();
            foreach (var thumbnail in _layerThumbnails) thumbnail.InvalidateVisual();
        }, DispatcherPriority.Background);
    }

    private void BuildProperties()
    {
        _properties.Children.Clear();
        if (BuildMaskWorkspace()) return;
        if (_document is not { } document) { _properties.Children.Add(PropertyLabel("Open a document to view its properties")); return; }
        var layer = PropertyLayer;
        if (layer is null)
        {
            BuildDocumentProperties(document); return;
        }
        var heading = MaskTarget ? layer.Mask!.VectorPath is null ? "Layer Mask" : "Vector Mask"
            : SelectedLayers.Count > 1 ? "Multiple Layers" : layer.IsGroup ? "Group" : layer.LiveText is not null ? "Type Layer"
            : layer.LiveShape is not null ? "Shape Layer" : layer.Adjustment is not null ? "Adjustment Layer" : "Pixel Layer";
        PropertyHeading(heading, MaskTarget ? "mask" : layer.LiveText is not null ? "Type" : layer.LiveShape is not null ? "Shape" : layer.IsGroup ? "folder" : "image");
        if (MaskTarget) { BuildMaskProperties(layer); return; }
        BuildTransformProperties(layer);
        if (layer.LiveText is { } text) BuildTextProperties(layer.ID, text);
        if (layer.LiveShape is not null) BuildShapeProperties(layer);
        if (layer.Adjustment is not null)
            Section("Adjustments", PropertyAction("Adjustment Settings…", () => _ = EditAdjustment()));
        if (layer.LiveText is null) BuildAlignmentProperties();
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
        if (layer.LiveText is not null)
        {
            quick.Children.Add(PropertyAction("Convert to Frame", () => Edit("Convert to Frame", () => TextEdits.MakeFrame(document, layer.ID))));
            quick.Children.Add(PropertyAction("Convert to Vector Shape", () => Edit("Convert to Vector Shape", () => TextEdits.ToVectorShape(document, layer.ID))));
        }
        if (quick.Children.Count > 0) Section("Quick Actions", quick);
    }

    private static TextBlock PropertyLabel(string text) => new() { Text = Localize.Text(text), FontSize = 12, Foreground = Skin.LabelBrush, TextWrapping = TextWrapping.Wrap };
    private static Button PropertyAction(string text, Action action)
    {
        var button = new Button
        {
            Content = Localize.Text(text),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            MinHeight = 26,
            FontSize = 12,
            Padding = new Thickness(6, 2),
            CornerRadius = new CornerRadius(2),
            Background = PropertyFieldSurface,
            BorderBrush = PropertyLine,
            BorderThickness = new Thickness(1),
            Foreground = Skin.LabelBrush,
            Tag = text,
        };
        ToolTip.SetTip(button, Localize.Text(text));
        button.Click += (_, _) => action(); return button;
    }
    private static Button InspectorButton(string icon, string text, Action action)
    {
        var button = new Button { Content = new InspectorGlyph(icon), Width = 30, Height = 26, Padding = new Thickness(4, 2), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(2),
            Background = Brushes.Transparent, Tag = text };
        ToolTip.SetTip(button, Localize.Text(text));
        Avalonia.Automation.AutomationProperties.SetName(button, Localize.Text(text));
        button.Click += (_, _) => action(); return button;
    }
    private static Control InlineProperty(string label, Control input)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*") };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = Skin.LabelBrush });
        input.MinHeight = 24;
        Grid.SetColumn(input, 1); row.Children.Add(input); return row;
    }
    private static readonly IBrush PropertySurface = Skin.ChromeBrush;
    private static readonly IBrush PropertyFieldSurface = Skin.SurfaceControlBrush;
    private static readonly IBrush PropertyLine = Skin.BorderControlBrush;
    private void PropertyHeading(string title, string icon) => _properties.Children.Add(new Border { Padding = new Thickness(8, 5), BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(0, 0, 0, 1),
        Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = {
            new Border { Width = 28, Height = 28, Padding = new Thickness(3), CornerRadius = new CornerRadius(2), Background = PropertyFieldSurface, BorderBrush = PropertyLine, BorderThickness = new Thickness(1), Child = new InspectorGlyph(icon) },
            new TextBlock { Text = Localize.Text(title), FontSize = 13, VerticalAlignment = VerticalAlignment.Center } } } });
    private void Section(string title, params Control[] contents)
    {
        if (_propertySectionFilter is not null && !_propertySectionFilter.Contains(title)) return;
        var body = new StackPanel { Spacing = 6, Margin = new Thickness(8, 4, 10, 9), MinWidth = 236, MaxWidth = 290, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var item in contents) body.Children.Add(item);
        // Keep Expander semantics and keyboard access, with the flat header/dividers from the reference.
        var section = new Expander { Header = Localize.Text(title == "Transform" ? "Transform Properties" : title), Tag = "property-section-" + title, Content = body,
            IsExpanded = _propertySections.GetValueOrDefault(title, true), HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0) };
        section.Template = new FuncControlTemplate<Expander>((owner, scope) =>
        {
            var arrow = new TextBlock { Text = owner.IsExpanded ? "⌄" : "›", Width = 14, VerticalAlignment = VerticalAlignment.Center, FontSize = 14 };
            var header = new Button { Height = 28, Padding = new Thickness(6, 2), Background = Brushes.Transparent, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0), HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Children = { arrow, new TextBlock { Text = owner.Header?.ToString(), FontSize = 12.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center } } } };
            var content = new ContentPresenter { Content = owner.Content, IsVisible = owner.IsExpanded, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            header.Click += (_, _) => owner.IsExpanded = !owner.IsExpanded;
            owner.PropertyChanged += (_, e) => { if (e.Property == Expander.IsExpandedProperty) { content.IsVisible = owner.IsExpanded; arrow.Text = owner.IsExpanded ? "⌄" : "›"; } };
            return new Border { BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Skin.BorderControlBrush, Child = new StackPanel { Children = { header, content } } };
        });
        section.PropertyChanged += (_, e) => { if (e.Property == Expander.IsExpandedProperty) _propertySections[title] = section.IsExpanded; };
        (_propertyDestination ?? _properties).Children.Add(section);
    }
    private Control MoreProperties(string section, Control extra, bool expanded = false)
    {
        var key = section + ".more"; extra.IsVisible = _propertySections.GetValueOrDefault(key, expanded);
        var button = InspectorButton("more", "More Options", () => { extra.IsVisible = !extra.IsVisible; _propertySections[key] = extra.IsVisible; });
        button.Tag = "property-more-" + section; button.Width = 28; button.Height = 24;
        button.HorizontalAlignment = HorizontalAlignment.Right; button.Background = PropertyFieldSurface;
        button.BorderBrush = PropertyLine; button.BorderThickness = new Thickness(1);
        return new StackPanel { Spacing = 6, Children = { extra, button } };
    }
    private static ComboBox PropertyChoice(string tag, string[] labels, int selected, Action<int> changed)
    {
        var combo = new ComboBox { Tag = tag, ItemsSource = labels.Select(Localize.Text).ToArray(), SelectedIndex = selected,
            MinHeight = 24, Padding = new Thickness(5, 2), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = PropertyFieldSurface, BorderBrush = PropertyLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(1) };
        ToolTip.SetTip(combo, Localize.Text(tag)); combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) changed(combo.SelectedIndex); }; return combo;
    }
    private static ToggleButton PropertyToggle(string icon, string text, bool selected, Action<bool> changed)
    {
        var button = new ToggleButton { Content = new InspectorGlyph(icon), Tag = text, IsChecked = selected, Background = selected ? PropertyFieldSurface : Brushes.Transparent, BorderBrush = PropertyLine, BorderThickness = new Thickness(selected ? 1 : 0), Width = 28, MinHeight = 26, Height = 26, Padding = new Thickness(4), CornerRadius = new CornerRadius(2) };
        button.Template = new FuncControlTemplate<ToggleButton>((owner, scope) =>
        {
            var border = new Border { CornerRadius = new CornerRadius(2), Padding = new Thickness(4), BorderBrush = PropertyLine,
                Child = new ContentPresenter { Content = owner.Content, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center } };
            void Refresh() { border.Background = owner.IsChecked == true ? PropertyFieldSurface : owner.IsPointerOver ? Skin.SurfaceControlHoverBrush : Brushes.Transparent; border.BorderThickness = new Thickness(owner.IsChecked == true ? 1 : 0); }
            owner.PropertyChanged += (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty || e.Property == IsPointerOverProperty) Refresh(); };
            Refresh(); return border;
        });
        ToolTip.SetTip(button, Localize.Text(text)); Avalonia.Automation.AutomationProperties.SetName(button, Localize.Text(text));
        button.Click += (_, _) => { var on = button.IsChecked == true; button.Background = on ? PropertyFieldSurface : Brushes.Transparent; button.BorderThickness = new Thickness(on ? 1 : 0); changed(on); }; return button;
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
            Height = 24, MinHeight = 24,
            Padding = new Thickness(5, 2),
            FontSize = 12,
            CornerRadius = new CornerRadius(1),
            Background = PropertyFieldSurface,
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
        var locked = PropertyToggle("link", "Constrain Proportions", _proportionLocked, on => _proportionLocked = on);
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
        var controls = new Grid { MinWidth = 256, MaxWidth = 300, ColumnDefinitions = new ColumnDefinitions("30,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), IsEnabled = LayerProtection.CanMove(_document!, layer.ID) };
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
        var all = new StackPanel { Spacing = 5 };
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
        var extra = new StackPanel { Spacing = 6 };
        extra.Children.Add(new Separator());
        extra.Children.Add(PropertyLabel("Distribute:"));
        extra.Children.Add(Row("Distribute Top", "Distribute Vertically", "Distribute Bottom", "Distribute Left", "Distribute Horizontally", "Distribute Right"));
        extra.Children.Add(new Separator());
        var target = PropertyChoice("alignment-target", ["Automatic", "Selected Layers", "Canvas", "Selection"], (int)_alignmentTarget,
            value => { _alignmentTarget = (AlignmentTarget)value; BuildProperties(); });
        ToolTip.SetTip(target, Localize.Text("Reference bounds for alignment and distribution"));
        extra.Children.Add(PropertyPair(PropertyField("Distribute Spacing:", Row("Distribute Vertical Spacing", "Distribute Horizontal Spacing")), PropertyField("Align to:", target)));
        all.Children.Add(MoreProperties("Alignment", extra, true)); Section("Align and Distribute", all);
    }
    private void AlignProperties(string operation)
    {
        if (_document is not { } document) return;
        if (_alignmentTarget == AlignmentTarget.Selection && document.Selection.Path is not { IsEmpty: false }) { Say("Create a selection to use it as the alignment reference."); return; }
        Edit(operation, () => LayerAlignment.Apply(document, SelectedLayers, operation, _alignmentTarget));
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
        var body = new StackPanel { Spacing = 6, IsEnabled = !LayerProtection.Effective(_document!, layer.ID).HasFlag(LayerLocks.All) && !LayerProtection.Effective(_document!, layer.ID).HasFlag(LayerLocks.Pixels) };
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
        if (source.Kind == ShapeKind.Rectangle) { var smooth = new CheckBox { Content = Localize.Text("Smooth Corners"), IsChecked = source.Smoothing == true, IsEnabled = source.CornerRadius > 0 }; smooth.IsCheckedChanged += (_, _) => Change(style => style.Smoothing = smooth.IsChecked == true ? true : null); fields.Add(smooth); }
        if (source.Kind == ShapeKind.Line) fields.Add(PropertyField("Line Width (px)", PropertyNumber("shape-line", source.LineWidth ?? 1, 1, 1000, value => Change(style => style.LineWidth = value))));
        Section("Shape", fields.ToArray());
    }

    private void AddSelectionMask(bool vector)
    {
        if (_document is not { } document || Selected is not { } id) return;
        Edit(vector ? "Vector Mask from Selection" : "Mask from Selection", () => MaskProperties.FromSelection(document, id, vector));
        Reselect(id); SetPaintingMask(true);
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
