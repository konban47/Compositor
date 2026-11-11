using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using SkiaSharp;

namespace Compositor.Desktop;

/// <summary>
/// Layer ▸ Layer Style, after Photoshop's dialog: Styles, Blending Options and the effects down the left with their check
/// boxes (Contour and Texture under Bevel and Emboss, + on the effects that can be added again), the selected page's framed
/// groups in the middle with Make Default / Reset to Default, and OK, Cancel, New Style…, Preview and a sample on the right.
/// Every change previews on the canvas; Cancel puts the layer back.
/// </summary>
internal sealed class LayerStyleDialog : DialogWindow
{
    protected override bool SharesEditPosition => true;
    internal LayerStylePreset Draft { get; private set; }
    internal string LayerName => _name.Text?.Trim() ?? "";
    private readonly TextBox _name = DialogKit.Line("style-name");
    private readonly StackPanel _list = new();
    private readonly StackPanel _options = new() { Spacing = 10, Margin = new Thickness(14, 6, 14, 14) };
    private readonly CheckBox _preview = new() { IsChecked = true, Tag = "style-preview" };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Orange };
    private readonly StyleSample _sample;
    private readonly Action<LayerStylePreset?> _changed;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private object? _selected;
    private bool _building;
    private bool _accepted;
    private readonly List<LayerStylePreset> _presets = LoadPresets();
    private static string PresetPath => Path.Combine(AppPaths.SettingsDirectory, "layer-styles.json");
    private static string DefaultsPath => Path.Combine(AppPaths.SettingsDirectory, "layer-style-defaults.json");
    internal LayerStyleDialog(ImageLayer layer, Action<LayerStylePreset?> changed, StyleEffectKind? kind = null)
    {
        Title = Localize.Text("Layer Style"); Width = 1180; Height = 860; MinWidth = 1040; MinHeight = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Draft = Capture(layer); Draft.Effects = (Draft.Effects ?? new()).EditableCopy(); Draft.Blending ??= new();
        _changed = changed; _name.Text = layer.Name; _name.Width = 320;
        foreach (var effectKind in Enum.GetValues<StyleEffectKind>())
            if (!Draft.Effects.Items!.Any(e => e.Kind == effectKind)) { var effect = StartingEffect(effectKind); effect.Enabled = false; Draft.Effects.Items!.Add(effect); }
        // The left list keeps Photoshop's order, whatever order the layer's effects were drawn in.
        Draft.Effects.Items = Draft.Effects.Items!.OrderBy(e => Array.IndexOf(Order, e.Kind)).ToList();
        _sample = new StyleSample(layer.Asset?.Image);
        _preview.Content = Localize.Text("Preview") + "(V)";

        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 10), Children = { new TextBlock { Text = Localize.Text("Name") + "(N):", VerticalAlignment = VerticalAlignment.Center }, _name } };
        var fx = new Button { Content = "fx⌄", FontStyle = FontStyle.Italic, Padding = new Thickness(6, 2), Background = Brushes.Transparent, Tag = "style-fx" };
        ToolTip.SetTip(fx, Localize.Text("Add a layer style")); fx.Click += (_, _) => FxMenu().Open(fx);
        var actions = new DockPanel { Margin = new Thickness(6, 6, 6, 6), LastChildFill = false };
        actions.Children.Add(fx);
        actions.Children.Add(Glyph("up", "Move Effect Up", () => Move(-1))); actions.Children.Add(Glyph("down", "Move Effect Down", () => Move(1)));
        var delete = Glyph("delete", "Delete Effect", Remove); DockPanel.SetDock(delete, Dock.Right); actions.Children.Add(delete);
        var left = new Border
        {
            BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Width = 330, Background = Skin.SurfaceDarkBrush,
            Child = new DockPanel { Children = { actions, new ScrollViewer { Content = _list, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } } },
        };
        DockPanel.SetDock(actions, Dock.Bottom);
        var middle = new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Child = new ScrollViewer { Content = _options, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        var ok = FormDialog.ActionButton("OK", () => { if (LayerName.Length is 0 or > 256 || !Draft.IsValid) { _error.Text = Localize.Text("Enter a valid layer name and style."); return; } _accepted = true; Close(); });
        ok.IsDefault = true;
        var cancel = FormDialog.ActionButton("Cancel", Close); cancel.IsCancel = true;
        var newStyle = FormDialog.ActionButton("New Style…", () => _ = SavePreset());
        var right = DialogKit.ActionColumn(ok, cancel, newStyle, _preview, _sample, _error); right.Width = 196; right.Margin = new Thickness(18, 0, 0, 0);
        this.Mnemonic(newStyle, 'W'); LayerDialogControls.Key(this, 'N', _name); LayerDialogControls.Key(this, 'V', _preview);
        _preview.IsCheckedChanged += (_, _) => Schedule();
        var layout = new Grid { ColumnDefinitions = new("Auto,*,Auto"), RowDefinitions = new("Auto,*"), Margin = new Thickness(16, 12, 16, 16), ColumnSpacing = 14 };
        Grid.SetColumnSpan(nameRow, 2); layout.Children.Add(nameRow);
        Grid.SetRow(left, 1); layout.Children.Add(left);
        Grid.SetRow(middle, 1); Grid.SetColumn(middle, 1); layout.Children.Add(middle);
        Grid.SetColumn(right, 2); Grid.SetRowSpan(right, 2); layout.Children.Add(right);
        Content = layout; _timer.Tick += (_, _) => { _timer.Stop(); Preview(); };
        Closed += (_, _) => { _timer.Stop(); _sample.Dispose(); };
        _selected = kind is { } first ? Draft.Effects.Items!.First(e => e.Kind == first) : "Blending Options";
        if (_selected is StyleEffect selected) selected.Enabled = true;
        Rebuild(); Preview();
    }
    /// <summary>Photoshop's order down the list.</summary>
    private static readonly StyleEffectKind[] Order = [StyleEffectKind.BevelEmboss, StyleEffectKind.Stroke, StyleEffectKind.InnerShadow, StyleEffectKind.InnerGlow, StyleEffectKind.Satin,
        StyleEffectKind.ColorOverlay, StyleEffectKind.GradientOverlay, StyleEffectKind.PatternOverlay, StyleEffectKind.OuterGlow, StyleEffectKind.DropShadow];
    internal static LayerStylePreset Capture(ImageLayer layer) => new()
    {
        Name = "Style", Effects = layer.Effects?.Scaled(1), Blending = layer.Blending?.Copy(),
        BlendMode = layer.BlendMode, Opacity = layer.Opacity, FillOpacity = layer.FillOpacity,
    };
    internal static void Apply(ImageLayer layer, LayerStylePreset style)
    {
        layer.Effects = style.Effects?.Scaled(1); layer.Blending = style.Blending?.Copy();
        layer.BlendMode = style.BlendMode; layer.Opacity = style.Opacity; layer.FillOpacity = style.FillOpacity;
    }
    public static async Task<(LayerStylePreset Style, string Name)?> Ask(Window owner, ImageLayer layer,
        Action<LayerStylePreset?> changed, StyleEffectKind? kind = null)
    {
        var dialog = new LayerStyleDialog(layer, changed, kind); await dialog.ShowDialog(owner);
        if (!dialog._accepted) return null;
        // Effects left switched off are not kept unless the layer had them: what was only looked at is not saved.
        dialog.Draft.Effects!.Items = dialog.Draft.Effects.Items!.Where(e => e.Enabled || layer.Effects?.Items?.Any(o => o.Kind == e.Kind) == true).ToList();
        return (dialog.Draft, dialog.LayerName);
    }
    internal static string NameFor(StyleEffectKind kind) => kind switch
    {
        StyleEffectKind.BevelEmboss => "Bevel & Emboss", StyleEffectKind.InnerShadow => "Inner Shadow",
        StyleEffectKind.InnerGlow => "Inner Glow", StyleEffectKind.ColorOverlay => "Color Overlay",
        StyleEffectKind.GradientOverlay => "Gradient Overlay", StyleEffectKind.PatternOverlay => "Pattern Overlay",
        StyleEffectKind.OuterGlow => "Outer Glow", StyleEffectKind.DropShadow => "Drop Shadow", _ => kind.ToString(),
    };
    private void Schedule() { if (_building) return; _timer.Stop(); _timer.Start(); }
    private void Preview()
    {
        try { _changed(_preview.IsChecked == true ? Draft : null); _sample.Update(_preview.IsChecked == true ? Draft : null); _error.Text = ""; }
        catch (Exception) { _error.Text = Localize.Text("This style is too large to preview. Reduce its size or distance."); }
    }

    // ---- The list down the left ----
    private void Rebuild()
    {
        _building = true; _list.Children.Clear();
        foreach (var section in new[] { "Styles", "Blending Options" })
            _list.Children.Add(Row(null, Localize.Text(section), null, _selected as string == section, () => { _selected = section; Rebuild(); }, section));
        var items = Draft.Effects!.Items!;
        foreach (var effect in items)
        {
            var siblings = items.Where(e => e.Kind == effect.Kind).ToList();
            var label = Localize.Text(NameFor(effect.Kind)) + (siblings.Count > 1 ? " " + (siblings.IndexOf(effect) + 1) : "");
            var check = new CheckBox { IsChecked = effect.Enabled, Tag = "enable " + NameFor(effect.Kind) };
            check.IsCheckedChanged += (_, _) => { effect.Enabled = check.IsChecked == true; Schedule(); };
            Button? add = null;
            if (effect.Kind is StyleEffectKind.Stroke or StyleEffectKind.InnerShadow or StyleEffectKind.ColorOverlay or StyleEffectKind.GradientOverlay or StyleEffectKind.DropShadow)
            {
                add = new Button { Content = new Border { Width = 16, Height = 16, BorderBrush = Skin.LabelBrush, BorderThickness = new Thickness(1.2), Child = new TextBlock { Text = "+", FontSize = 13, Margin = new Thickness(0, -3, 0, 0), HorizontalAlignment = HorizontalAlignment.Center } }, Padding = new Thickness(2), Background = Brushes.Transparent, BorderThickness = default };
                ToolTip.SetTip(add, Localize.Text("Add Another Effect"));
                add.Click += (_, _) => { if (items.Count >= 40) return; var copy = effect.Copy(); copy.Enabled = true; items.Insert(items.IndexOf(effect) + 1, copy); _selected = copy; Rebuild(); Schedule(); };
            }
            _list.Children.Add(Row(check, label, add, ReferenceEquals(_selected, effect), () => { _selected = effect; Rebuild(); }, NameFor(effect.Kind)));
            if (effect.Kind == StyleEffectKind.BevelEmboss && siblings.IndexOf(effect) == 0)
                foreach (var texture in new[] { false, true })
                {
                    var page = texture ? "Texture" : "Contour";
                    var nested = new CheckBox { IsChecked = texture ? effect.TextureEnabled : effect.ContourEnabled, Tag = "enable " + page };
                    nested.IsCheckedChanged += (_, _) => { if (texture) effect.TextureEnabled = nested.IsChecked == true; else effect.ContourEnabled = nested.IsChecked == true; if (nested.IsChecked == true) effect.Enabled = true; Schedule(); };
                    var key = (effect, page);
                    _list.Children.Add(Row(nested, Localize.Text(page), null, Equals(_selected, key), () => { _selected = key; Rebuild(); }, page, indent: true));
                }
        }
        _options.Children.Clear();
        switch (_selected)
        {
            case StyleEffect selected: EffectOptions(selected); break;
            case ValueTuple<StyleEffect, string> sub: if (sub.Item2 == "Texture") TextureOptions(sub.Item1); else ContourPage(sub.Item1); break;
            case "Styles": PresetOptions(); break;
            default: BlendingOptions(); break;
        }
        _building = false;
    }
    /// <summary>One row of the list: its check box, its name as a button, and the + where the effect can be repeated.</summary>
    private static Control Row(CheckBox? check, string label, Button? add, bool selected, Action choose, string? tag = null, bool indent = false)
    {
        var grid = new Grid { ColumnDefinitions = new("Auto,*,Auto"), MinHeight = 38, Background = selected ? Skin.TabFront : Brushes.Transparent, Margin = new Thickness(indent ? 20 : 0, 0, 0, 1) };
        if (check is not null) { check.Margin = new Thickness(8, 0, 0, 0); check.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(check); }
        var button = new Button { Content = label, Tag = tag ?? label, Background = Brushes.Transparent, BorderThickness = default, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(check is null ? 10 : 2, 6) };
        button.Click += (_, _) => choose(); Grid.SetColumn(button, 1); grid.Children.Add(button);
        if (add is not null) { add.Margin = new Thickness(0, 0, 8, 0); add.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(add, 2); grid.Children.Add(add); }
        return new Border { BorderBrush = Skin.BorderSubtleBrush, BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
    }
    private static Button Glyph(string icon, string title, Action action)
    {
        var button = new Button { Content = new InspectorGlyph(icon), Width = 30, Padding = new Thickness(3), Background = Brushes.Transparent, BorderThickness = default };
        ToolTip.SetTip(button, Localize.Text(title)); Avalonia.Automation.AutomationProperties.SetName(button, Localize.Text(title)); button.Click += (_, _) => action(); return button;
    }
    private ContextMenu FxMenu()
    {
        var menu = new ContextMenu();
        var blending = new MenuItem { Header = Localize.Text("Blending Options…") }; blending.Click += (_, _) => { _selected = "Blending Options"; Rebuild(); }; menu.Items.Add(blending); menu.Items.Add(new Separator());
        foreach (var kind in Order)
        {
            var item = new MenuItem { Header = Localize.Text(NameFor(kind)) + "…" };
            item.Click += (_, _) => { var e = Draft.Effects!.Items!.First(x => x.Kind == kind); e.Enabled = true; _selected = e; Rebuild(); Schedule(); };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var reset = new MenuItem { Header = Localize.Text("Reset to Default List") };
        reset.Click += (_, _) => { Draft.Effects!.Items = Draft.Effects.Items!.GroupBy(e => e.Kind).Select(g => g.First()).OrderBy(e => Array.IndexOf(Order, e.Kind)).ToList(); _selected = "Blending Options"; Rebuild(); Schedule(); };
        menu.Items.Add(reset); return menu;
    }
    private void Move(int delta)
    {
        if (_selected is not StyleEffect e) return; var list = Draft.Effects!.Items!; var index = list.IndexOf(e);
        // An effect moves among the copies of its own kind, as Photoshop's do; the kinds keep their order.
        var other = index + delta; if (other < 0 || other >= list.Count || list[other].Kind != e.Kind) return;
        (list[index], list[other]) = (list[other], list[index]); Rebuild(); Schedule();
    }
    private void Remove()
    {
        if (_selected is not StyleEffect e) return; var list = Draft.Effects!.Items!;
        if (list.Count(x => x.Kind == e.Kind) > 1) list.Remove(e); else e.Enabled = false;
        _selected = "Blending Options"; Rebuild(); Schedule();
    }

    // ---- The pieces the pages are made of ----
    /// <summary>A framed page with the effect's name set into its edge.</summary>
    private StackPanel Page(string title) { var body = new StackPanel { Spacing = 10 }; _options.Children.Add(DialogKit.Group(title, body)); return body; }
    private static StackPanel Section(StackPanel page, string title) { var body = new StackPanel { Spacing = 8 }; page.Children.Add(DialogKit.Group(title, body)); return body; }
    private static TextBlock Caption(string text, char key = default) => new()
    { Text = Localize.Text(text) + (key != default ? $"({key})" : "") + ":", Width = 130, TextAlignment = Avalonia.Media.TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private static StackPanel Line(params Control[] controls)
    { var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; foreach (var c in controls) { c.VerticalAlignment = VerticalAlignment.Center; line.Children.Add(c); } return line; }
    /// <summary>A Photoshop slider row: caption, a short slider, the number and its unit.</summary>
    private Control Slider(string label, char key, double value, double min, double max, string unit, Action<double> set, string? tag = null)
    {
        var slider = new Slider { Template = CompactSlider.Template, Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 150, MinHeight = 0, Height = 26 };
        var number = new NumericUpDown { Minimum = (decimal)min, Maximum = (decimal)max, Value = (decimal)Math.Clamp(value, min, max), FormatString = max - min > 50 ? "0" : "0.#", ShowButtonSpinner = false, Width = 64, MinHeight = DialogKit.FieldHeight, Tag = tag ?? label };
        var changing = false;
        number.ValueChanged += (_, _) => { if (changing || number.Value is not { } n) return; changing = true; slider.Value = (double)n; set((double)n); changing = false; Schedule(); };
        slider.PropertyChanged += (_, e) => { if (e.Property != RangeBase.ValueProperty || changing) return; changing = true; var v = Math.Round(slider.Value); number.Value = (decimal)v; set(v); changing = false; Schedule(); };
        if (key != default) LayerDialogControls.Key(this, key, number);
        return Line(Caption(label, key), slider, number, new TextBlock { Text = Localize.Text(unit) });
    }
    private ComboBox Choice(string tag, string[] options, int selected, Action<int> set, double width = 120)
    {
        var combo = new ComboBox { ItemsSource = options.Select(Localize.Text).ToArray(), SelectedIndex = selected, Width = width, MinHeight = DialogKit.FieldHeight, Tag = tag };
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) { set(combo.SelectedIndex); Schedule(); } };
        return combo;
    }
    private ComboBox Mode(LayerBlendMode mode, Action<LayerBlendMode> set, string tag = "Blend Mode")
    {
        var combo = LayerDialogControls.ModeChoice(mode, false, out var read, 190); combo.Tag = tag;
        combo.SelectionChanged += (_, _) => { set(read()); Schedule(); };
        return combo;
    }
    private CheckBox Check(string label, char key, bool value, Action<bool> set, string? tag = null)
    {
        var box = new CheckBox { Content = Localize.Text(label) + (key != default ? $"({key})" : ""), IsChecked = value, Tag = tag ?? label };
        box.IsCheckedChanged += (_, _) => { set(box.IsChecked == true); Schedule(); };
        if (key != default) LayerDialogControls.Key(this, key, box);
        return box;
    }
    /// <summary>A color swatch that opens the Color Picker titled with what it colors, as "Color Picker (Drop Shadow Color)".</summary>
    private Button Swatch(StyleEffect e, bool second, string what)
    {
        var fill = new Border { Width = 46, Height = 30 };
        var button = new Button { Content = fill, Padding = new Thickness(2), Tag = what, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        void Show() { var (r, g, b) = second ? (e.Red2, e.Green2, e.Blue2) : (e.Red, e.Green, e.Blue); fill.Background = new SolidColorBrush(Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255))); }
        Show();
        button.Click += async (_, _) => await ColorPickerDialog.Pick(this, Localize.Format($"Color Picker ({Localize.Text(what)})"), second ? (e.Red2, e.Green2, e.Blue2) : (e.Red, e.Green, e.Blue), rgb =>
        { var (r, g, b) = rgb; if (second) { e.Red2 = r; e.Green2 = g; e.Blue2 = b; } else { e.Red = r; e.Green = g; e.Blue = b; } Show(); Schedule(); });
        return button;
    }
    /// <summary>The angle: the dial, the number in degrees and, where the effect can follow it, Use Global Light.</summary>
    private Control Angle(StyleEffect e, char key, bool global, bool altitude = false)
    {
        var dial = new AngleDial(52) { ShowsAltitude = altitude };
        double Current() => e.UseGlobalLight && global ? Draft.Effects!.GlobalLightAngle : e.Angle;
        void Set(double v) { if (e.UseGlobalLight && global) Draft.Effects!.GlobalLightAngle = v; else e.Angle = v; }
        dial.Angle = Current(); if (altitude) dial.Altitude = e.UseGlobalLight && Draft.Effects!.GlobalLightAltitude is { } a ? a : e.Altitude;
        var number = new NumericUpDown { Minimum = -180, Maximum = 180, Value = (decimal)dial.Angle, FormatString = "0", ShowButtonSpinner = false, Width = 60, MinHeight = DialogKit.FieldHeight, Tag = "Angle" };
        var altitudeBox = new NumericUpDown { Minimum = 0, Maximum = 90, Value = (decimal)dial.Altitude, FormatString = "0", ShowButtonSpinner = false, Width = 60, MinHeight = DialogKit.FieldHeight, Tag = "Altitude" };
        var syncing = false;
        void SetAltitude(double v) { if (e.UseGlobalLight) Draft.Effects!.GlobalLightAltitude = v; else e.Altitude = v; }
        dial.Changed += () => { syncing = true; number.Value = (decimal)dial.Angle; Set(dial.Angle); if (altitude) { altitudeBox.Value = (decimal)dial.Altitude; SetAltitude(dial.Altitude); } syncing = false; Schedule(); };
        number.ValueChanged += (_, _) => { if (syncing || number.Value is not { } v) return; dial.Angle = (double)v; Set(dial.Angle); Schedule(); };
        altitudeBox.ValueChanged += (_, _) => { if (syncing || altitudeBox.Value is not { } v) return; dial.Altitude = (double)v; SetAltitude(dial.Altitude); Schedule(); };
        LayerDialogControls.Key(this, key, number);
        var degrees = () => new TextBlock { Text = Localize.Text("degrees"), VerticalAlignment = VerticalAlignment.Center };
        var useGlobal = Check("Use Global Light", 'G', e.UseGlobalLight, v => { e.UseGlobalLight = v; Rebuild(); });
        if (!altitude)
        {
            var line = Line(Caption("Angle", key), dial, number, degrees());
            if (global) line.Children.Add(useGlobal);
            return line;
        }
        // Bevel and Emboss: the dial sits between Angle and Altitude, as Photoshop draws it.
        var grid = new Grid { ColumnDefinitions = new("Auto,Auto,Auto"), RowDefinitions = new("Auto,Auto,Auto"), ColumnSpacing = 8, RowSpacing = 4 };
        void Put(Control c, int r, int col, int rs = 1) { Grid.SetRow(c, r); Grid.SetColumn(c, col); Grid.SetRowSpan(c, rs); c.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(c); }
        Put(Caption("Angle", key), 0, 0); Put(dial, 0, 1, 3); Put(Line(number, degrees()), 0, 2); Put(useGlobal, 1, 2);
        Put(Caption("Altitude"), 2, 0); Put(Line(altitudeBox, degrees()), 2, 2);
        return grid;
    }
    /// <summary>The contour: its thumbnail opens the Contour Editor, the ⌄ the contour presets with their gear menu.</summary>
    private Control ContourPicker(Func<double[]?> get, Action<double[]> set, string tag = "contour")
    {
        var thumb = new ContourThumb(get) { Width = 50, Height = 50 };
        var open = new Button { Content = thumb, Padding = default, Tag = tag, Background = Brushes.Transparent };
        open.Click += async (_, _) => { if (await ContourEditorDialog.Ask(this, get() ?? Linear()) is { } edited) { set(edited); thumb.InvalidateVisual(); Schedule(); } };
        var arrow = new Button { Content = "⌄", Width = 18, Height = 50, Padding = default, Tag = tag + "-presets", HorizontalContentAlignment = HorizontalAlignment.Center };
        arrow.Click += (_, _) => PresetPopup(arrow, "Contour", () => ContourLibrary.Load().Select(p => (p.Name, (object)p)).ToList(), chosen => { set(((EditorPreset)chosen).Contour!.ToArray()); thumb.InvalidateVisual(); Schedule(); }, item => new ContourThumb(() => ((EditorPreset)item).Contour) { Width = 40, Height = 40 });
        return Line(open, arrow);
    }
    private static double[] Linear() => Enumerable.Range(0, 65).Select(i => i / 64d).ToArray();
    /// <summary>
    /// The drop-down preset grid shared by the contour, gradient and pattern pickers: folders for the groups (a name's
    /// "Group / Item"), thumbnails in them, and the gear menu Photoshop has beside them.
    /// </summary>
    private void PresetPopup(Control anchor, string kind, Func<List<(string Name, object Item)>> items, Action<object> choose, Func<object, Control> thumbnail)
    {
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.BottomEdgeAlignedLeft, IsLightDismissEnabled = true };
        var body = new StackPanel { Spacing = 2, Width = 330 };
        var large = false; var textOnly = false;
        void Fill()
        {
            body.Children.Clear();
            foreach (var group in items().GroupBy(i => i.Name.Contains(" / ") ? i.Name[..i.Name.IndexOf(" / ", StringComparison.Ordinal)] : ""))
            {
                var tiles = new WrapPanel();
                foreach (var (name, item) in group)
                {
                    var shown = name.Contains(" / ") ? name[(name.IndexOf(" / ", StringComparison.Ordinal) + 3)..] : name;
                    Control content = textOnly ? new TextBlock { Text = Localize.Text(shown) } : thumbnail(item);
                    if (large && content is Control c && !textOnly) { c.Width *= 1.5; c.Height *= 1.5; }
                    var tile = new Button { Content = content, Padding = new Thickness(2), Margin = new Thickness(2), Tag = kind + ":" + name, Background = Brushes.Transparent };
                    ToolTip.SetTip(tile, Localize.Text(shown)); tile.Click += (_, _) => { choose(item); popup.IsOpen = false; }; tiles.Children.Add(tile);
                }
                if (group.Key.Length == 0) { body.Children.Add(tiles); continue; }
                var expander = new Expander { Header = "📁 " + Localize.Text(group.Key), Content = tiles, IsExpanded = true, HorizontalAlignment = HorizontalAlignment.Stretch };
                body.Children.Add(expander);
            }
        }
        Fill();
        var gear = new Button { Content = "⚙⌄", Padding = new Thickness(4, 2), VerticalAlignment = VerticalAlignment.Top, Tag = kind + "-gear" };
        gear.Click += (_, _) =>
        {
            var menu = new ContextMenu();
            void Add(string title, Action? act, bool enabled = true, bool check = false)
            { var item = new MenuItem { Header = Localize.Text(title), IsEnabled = enabled && act is not null, ToggleType = check ? MenuItemToggleType.CheckBox : MenuItemToggleType.None, IsChecked = check }; if (act is not null) item.Click += (_, _) => act(); menu.Items.Add(item); }
            Add($"New {kind}…", kind == "Contour" ? async () => { popup.IsOpen = false; await NewContourPreset(); } : null);
            Add($"Rename {kind}…", null, false); Add($"Delete {kind}", null, false); menu.Items.Add(new Separator());
            Add("Text Only", () => { textOnly = true; Fill(); }, check: textOnly); Add("Small Thumbnail", () => { textOnly = large = false; Fill(); }, check: !textOnly && !large);
            Add("Large Thumbnail", () => { textOnly = false; large = true; Fill(); }, check: large && !textOnly); Add("Small List", () => { textOnly = true; Fill(); }); Add("Large List", () => { textOnly = true; Fill(); });
            menu.Items.Add(new Separator());
            Add("Preset Manager…", async () => { popup.IsOpen = false; await new PresetManagerDialog(kind).ShowDialog(this); });
            menu.Open(gear);
        };
        var frame = new Border
        {
            Background = Skin.ChromeBrush, BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Padding = new Thickness(8),
            Child = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6, Children = { new ScrollViewer { Content = body, MaxHeight = 260 }, gear } },
        };
        Grid.SetColumn(gear, 1); popup.Child = frame;
        ((Panel)anchor.Parent!).Children.Add(popup); popup.Closed += (_, _) => ((Panel?)popup.Parent)?.Children.Remove(popup);
        popup.IsOpen = true;
    }
    private async Task NewContourPreset()
    {
        if (await ContourEditorDialog.Ask(this, Linear()) is not { } samples) return;
        var name = await TextPrompt.Ask(this, "Contour Name", "Name", Localize.Text("Contour") + " " + (ContourLibrary.Load().Count + 1));
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            var all = EditorPresets.Load(ContourLibrary.DefaultPath).ToList(); if (!File.Exists(ContourLibrary.DefaultPath)) all.AddRange(ContourLibrary.Defaults());
            all.Add(new EditorPreset(Guid.NewGuid(), name.Trim(), "Contour", Contour: samples)); EditorPresets.Save(ContourLibrary.DefaultPath, all);
        }
        catch (Exception error) { _error.Text = error.Message; }
    }
    /// <summary>The gradient: its strip, the ⌄ with the gradient presets in their groups, the + that saves this one.</summary>
    private Control GradientPicker(StyleEffect e, double width = 160)
    {
        var strip = new StyleGradientStrip(e) { Width = width, Height = 30 };
        var arrow = new Button { Content = "⌄", Width = 20, Height = 30, Padding = default, Tag = "gradient-presets", HorizontalContentAlignment = HorizontalAlignment.Center };
        List<GradientPreset> Presets() { try { return GradientLibrary.Load(GradientLibrary.DefaultPath); } catch (Exception) { return GradientLibrary.Defaults(); } }
        arrow.Click += (_, _) => PresetPopup(arrow, "Gradient", () => Presets().Select(p => (p.Category + " / " + p.Name, (object)p)).ToList(), chosen =>
        {
            var preset = (GradientPreset)chosen; e.GradientStops = preset.Stops.Select(s => new StyleGradientStop(s.Position, s.Color)).ToList();
            var first = new SKColor(preset.Stops[0].Color); var last = new SKColor(preset.Stops[^1].Color);
            (e.Red, e.Green, e.Blue) = (first.Red / 255d, first.Green / 255d, first.Blue / 255d); (e.Red2, e.Green2, e.Blue2) = (last.Red / 255d, last.Green / 255d, last.Blue / 255d);
            strip.InvalidateVisual(); Schedule();
        }, item => new StyleGradientStrip(new StyleEffect { GradientStops = ((GradientPreset)item).Stops.Select(s => new StyleGradientStop(s.Position, s.Color)).ToList() }) { Width = 44, Height = 44 });
        return Line(strip, arrow);
    }
    /// <summary>The pattern: its thumbnail, the ⌄ with the shipped and defined patterns, the + and Snap to Origin.</summary>
    private Control PatternPicker(StyleEffect e)
    {
        var thumb = new StylePatternThumb(e) { Width = 90, Height = 90 };
        var arrow = new Button { Content = "⌄", Width = 18, Height = 90, Padding = default, Tag = "pattern-presets", HorizontalContentAlignment = HorizontalAlignment.Center };
        List<(string, object)> Presets()
        {
            var shipped = PatternLibrary.Defaults().Select(p => (p.Name, (object)p));
            IEnumerable<(string, object)> defined = [];
            try { defined = EditorPresets.Load(EditorPresets.DefaultPath).Where(p => p.Kind == "Pattern" && p.Image is not null).Select(p => (Localize.Text("My Patterns") + " / " + p.Name, (object)p)); } catch (Exception) { }
            var procedural = new[] { StylePattern.Checkerboard, StylePattern.Stripes, StylePattern.Dots }.Select(p => (Localize.Text("Basics") + " / " + p, (object)p));
            return [.. procedural, .. shipped, .. defined];
        }
        arrow.Click += (_, _) => PresetPopup(arrow, "Pattern", Presets, chosen =>
        {
            if (chosen is StylePattern procedural) { e.Pattern = procedural; e.PatternPng = null; }
            else if (chosen is EditorPreset { Image: { } image })
            {
                using var bitmap = SKBitmap.Decode(Convert.FromBase64String(image));
                var scale = Math.Min(1, 1024.0 / Math.Max(bitmap.Width, bitmap.Height));
                using var fitted = Bitmaps.Scale(bitmap, Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale)));
                e.PatternPng = Convert.ToBase64String(PngCodec.Encode(fitted)); e.Pattern = StylePattern.Image;
            }
            thumb.InvalidateVisual(); Schedule();
        }, item => item is StylePattern p ? new StylePatternThumb(new StyleEffect { Pattern = p }) { Width = 40, Height = 40 } : new StylePatternThumb(new StyleEffect { Pattern = StylePattern.Image, PatternPng = ((EditorPreset)item).Image }) { Width = 40, Height = 40 });
        var add = new Button { Content = "＋", Padding = new Thickness(4, 0), Tag = "pattern-new" };
        ToolTip.SetTip(add, Localize.Text("Create a new preset from this pattern"));
        add.Click += async (_, _) =>
        {
            if (e.Pattern != StylePattern.Image || e.PatternPng is null) return;
            var name = await TextPrompt.Ask(this, "Pattern Name", "Name", Localize.Text("Pattern")); if (string.IsNullOrWhiteSpace(name)) return;
            try { var all = EditorPresets.Load(EditorPresets.DefaultPath).ToList(); all.Add(new EditorPreset(Guid.NewGuid(), name.Trim(), "Pattern", Image: e.PatternPng)); EditorPresets.Save(EditorPresets.DefaultPath, all); }
            catch (Exception error) { _error.Text = error.Message; }
        };
        var snap = new Button { Content = Localize.Text("Snap to Origin") + "(A)", Tag = "snap-origin" };
        snap.Click += (_, _) => { e.OffsetX = e.OffsetY = 0; Schedule(); }; this.Mnemonic(snap, 'A');
        return Line(Caption("Pattern"), Line(thumb, arrow), add, snap);
    }
    private Control DefaultButtons(StyleEffect e)
    {
        var make = FormDialog.ActionButton("Make Default", () => SaveDefault(e)); make.Tag = "make-default";
        var reset = FormDialog.ActionButton("Reset to Default", () =>
        {
            var fresh = StartingEffect(e.Kind); fresh.Enabled = true; var list = Draft.Effects!.Items!; list[list.IndexOf(e)] = fresh;
            _selected = fresh; Rebuild(); Schedule();
        });
        reset.Tag = "reset-default";
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, Children = { make, reset } };
    }

    // ---- The pages ----
    private void BlendingOptions()
    {
        var blend = Draft.Blending!; var page = Page("Blending Options");
        var general = Section(page, "General Blending");
        general.Children.Add(Line(Caption("Blend Mode"), Mode(Draft.BlendMode, v => Draft.BlendMode = v)));
        general.Children.Add(Slider("Opacity", 'O', Draft.Opacity * 100, 0, 100, "%", v => Draft.Opacity = v / 100, "Opacity (%)"));
        var advanced = Section(page, "Advanced Blending");
        advanced.Children.Add(Slider("Fill Opacity", 'F', Draft.FillOpacity * 100, 0, 100, "%", v => Draft.FillOpacity = v / 100, "Fill Opacity (%)"));
        var channels = Line(Caption("Channels"));
        foreach (var (name, value, set) in new (string, bool, Action<bool>)[] { ("R", blend.Red, v => blend.Red = v), ("G", blend.Green, v => blend.Green = v), ("B", blend.Blue, v => blend.Blue = v) })
            channels.Children.Add(Check(name, name[0], value, set));
        advanced.Children.Add(channels);
        advanced.Children.Add(Line(Caption("Knockout"), Choice("Knockout", ["None", "Shallow", "Deep"], (int)blend.Knockout, v => blend.Knockout = (KnockoutKind)v, 90)));
        var checks = new StackPanel { Spacing = 6, Margin = new Thickness(138, 0, 0, 0) };
        checks.Children.Add(Check("Blend Interior Effects as Group", 'I', blend.BlendInteriorEffectsAsGroup, v => blend.BlendInteriorEffectsAsGroup = v));
        checks.Children.Add(Check("Blend Clipped Layers as Group", 'P', blend.BlendClippedLayersAsGroup, v => blend.BlendClippedLayersAsGroup = v));
        checks.Children.Add(Check("Transparency Shapes Layer", 'T', blend.TransparencyShapesLayer, v => blend.TransparencyShapesLayer = v));
        checks.Children.Add(Check("Layer Mask Hides Effects", 'S', blend.LayerMaskHidesEffects, v => blend.LayerMaskHidesEffects = v));
        checks.Children.Add(Check("Vector Mask Hides Effects", 'H', blend.VectorMaskHidesEffects, v => blend.VectorMaskHidesEffects = v));
        advanced.Children.Add(checks);
        var rangePanel = new StackPanel { Spacing = 8 };
        void Ranges(BlendIfChannel channel)
        {
            rangePanel.Children.Clear(); var range = blend.Ranges.FirstOrDefault(r => r.Channel == channel);
            if (range is null) { range = new BlendIfRange { Channel = channel }; blend.Ranges.Add(range); }
            foreach (var (title, values) in new[] { ("This Layer", range.Source), ("Underlying Layer", range.Underlying) })
            {
                rangePanel.Children.Add(new TextBlock { Text = Localize.Text(title) + ":" });
                rangePanel.Children.Add(new BlendRangeControl(values, Schedule));
            }
            rangePanel.Children.Add(new TextBlock { Text = Localize.Text("Alt-drag a triangle to split it for a smooth transition."), TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Skin.SecondaryBrush });
        }
        var blendIf = Choice("Blend If", ["Gray", "Red", "Green", "Blue"], 0, v => Ranges((BlendIfChannel)v), 130);
        page.Children.Add(DialogKit.Group("", rangePanel, Line(new TextBlock { Text = Localize.Text("Blend If") + ":" }, blendIf)));
        Ranges(BlendIfChannel.Gray);
    }
    private void EffectOptions(StyleEffect e)
    {
        var page = Page(NameFor(e.Kind));
        switch (e.Kind)
        {
            case StyleEffectKind.BevelEmboss:
            {
                var structure = Section(page, "Structure");
                BevelKind[] styles = [BevelKind.OuterBevel, BevelKind.InnerBevel, BevelKind.Emboss, BevelKind.PillowEmboss, BevelKind.StrokeEmboss];
                structure.Children.Add(Line(Caption("Style"), Choice("Bevel Style", ["Outer Bevel", "Inner Bevel", "Emboss", "Pillow Emboss", "Stroke Emboss"], Array.IndexOf(styles, e.Bevel), v => e.Bevel = styles[v])));
                structure.Children.Add(Line(Caption("Technique"), Choice("Technique", ["Smooth", "Chisel Hard", "Chisel Soft"], e.Technique, v => e.Technique = v)));
                structure.Children.Add(Slider("Depth", 'D', e.Depth, 1, 1000, "%", v => e.Depth = v));
                var up = new RadioButton { Content = Localize.Text("Up"), GroupName = "bevel-direction", IsChecked = !e.Invert, Tag = "Direction Up" };
                var down = new RadioButton { Content = Localize.Text("Down"), GroupName = "bevel-direction", IsChecked = e.Invert, Tag = "Direction Down" };
                down.IsCheckedChanged += (_, _) => { e.Invert = down.IsChecked == true; Schedule(); };
                structure.Children.Add(Line(Caption("Direction"), up, down));
                structure.Children.Add(Slider("Size", 'Z', e.Size, 0, 250, "px", v => e.Size = v, "Size (px)"));
                structure.Children.Add(Slider("Soften", 'F', e.Soften, 0, 16, "px", v => e.Soften = v));
                var shading = Section(page, "Shading");
                shading.Children.Add(Angle(e, 'N', true, altitude: true));
                shading.Children.Add(Line(Caption("Gloss Contour"), ContourPicker(() => e.GlossSamples, v => e.GlossSamples = v, "gloss-contour"), Check("Anti-aliased", 'L', e.AntiAlias == true, v => e.AntiAlias = v)));
                shading.Children.Add(Line(Caption("Highlight Mode"), Mode(e.HighlightMode, v => e.HighlightMode = v, "Highlight Mode"), Swatch(e, true, "Bevel and Emboss Highlight Color")));
                shading.Children.Add(Slider("Opacity", 'O', e.HighlightOpacity * 100, 0, 100, "%", v => e.HighlightOpacity = v / 100, "Highlight Opacity"));
                shading.Children.Add(Line(Caption("Shadow Mode"), Mode(e.ShadowMode, v => e.ShadowMode = v, "Shadow Mode"), Swatch(e, false, "Bevel and Emboss Shadow Color")));
                shading.Children.Add(Slider("Opacity", 'C', e.ShadowOpacity * 100, 0, 100, "%", v => e.ShadowOpacity = v / 100, "Shadow Opacity"));
                break;
            }
            case StyleEffectKind.Stroke:
            {
                var structure = Section(page, "Structure");
                structure.Children.Add(Slider("Size", 'S', e.Size, 1, 250, "px", v => e.Size = v, "Size (px)"));
                structure.Children.Add(Line(Caption("Position"), Choice("Position", ["Outside", "Inside", "Center"], (int)e.Position, v => e.Position = (StrokePosition)v, 90)));
                structure.Children.Add(Line(Caption("Blend Mode"), Mode(e.BlendMode, v => e.BlendMode = v)));
                structure.Children.Add(Slider("Opacity", 'O', e.Opacity * 100, 0, 100, "%", v => e.Opacity = v / 100, "Opacity (%)"));
                structure.Children.Add(Line(new TextBlock { Width = 130 }, Check("Overprint", default, e.Overprint == true, v => e.Overprint = v)));
                var fill = new StackPanel { Spacing = 8, MinHeight = 200 };
                var type = Choice("Fill Type", ["Color", "Gradient", "Pattern"], e.FillType, v => { e.FillType = v; Rebuild(); }, 90);
                page.Children.Add(DialogKit.Group("", fill, Line(new TextBlock { Text = Localize.Text("Fill Type") + ":" }, type)));
                if (e.FillType == 0) fill.Children.Add(Line(new TextBlock { Text = Localize.Text("Color") + ":", VerticalAlignment = VerticalAlignment.Center }, Swatch(e, false, "Stroke Color")));
                else if (e.FillType == 1) GradientRows(fill, e, stroke: true);
                else PatternRows(fill, e);
                break;
            }
            case StyleEffectKind.InnerShadow:
            case StyleEffectKind.DropShadow:
            {
                var drop = e.Kind == StyleEffectKind.DropShadow;
                var structure = Section(page, "Structure");
                structure.Children.Add(Line(Caption("Blend Mode"), Mode(e.BlendMode, v => e.BlendMode = v), Swatch(e, false, drop ? "Drop Shadow Color" : "Inner Shadow Color")));
                structure.Children.Add(Slider("Opacity", 'O', e.Opacity * 100, 0, 100, "%", v => e.Opacity = v / 100, "Opacity (%)"));
                structure.Children.Add(Angle(e, 'A', true));
                structure.Children.Add(Slider("Distance", 'D', e.Distance, 0, 1000, "px", v => e.Distance = v, "Distance (px)"));
                structure.Children.Add(Slider(drop ? "Spread" : "Choke", drop ? 'R' : 'C', e.Spread, 0, 100, "%", v => e.Spread = v, "Spread / Choke (%)"));
                structure.Children.Add(Slider("Size", 'S', e.Size, 0, 250, "px", v => e.Size = v, "Size (px)"));
                var quality = Section(page, "Quality");
                quality.Children.Add(Line(Caption("Contour"), ContourPicker(() => e.ContourSamples, v => { e.ContourSamples = v; e.ContourEnabled = true; }), Check("Anti-aliased", 'L', e.AntiAlias == true, v => e.AntiAlias = v)));
                quality.Children.Add(Slider("Noise", 'N', e.Noise, 0, 100, "%", v => e.Noise = v, "Noise (%)"));
                if (drop) page.Children.Add(Line(new TextBlock { Width = 130 }, Check("Layer Knocks Out Drop Shadow", 'U', e.KnocksOut == true, v => e.KnocksOut = v)));
                break;
            }
            case StyleEffectKind.InnerGlow:
            case StyleEffectKind.OuterGlow:
            {
                var outer = e.Kind == StyleEffectKind.OuterGlow;
                var structure = Section(page, "Structure");
                structure.Children.Add(Line(Caption("Blend Mode"), Mode(e.BlendMode, v => e.BlendMode = v)));
                structure.Children.Add(Slider("Opacity", 'O', e.Opacity * 100, 0, 100, "%", v => e.Opacity = v / 100, "Opacity (%)"));
                structure.Children.Add(Slider("Noise", 'N', e.Noise, 0, 100, "%", v => e.Noise = v, "Noise (%)"));
                var solid = new RadioButton { GroupName = "glow-fill", IsChecked = e.FillType != 1, Tag = "glow-color" };
                var ramp = new RadioButton { GroupName = "glow-fill", IsChecked = e.FillType == 1, Tag = "glow-gradient" };
                solid.IsCheckedChanged += (_, _) => { if (solid.IsChecked == true) { e.FillType = 0; Rebuild(); Schedule(); } };
                ramp.IsCheckedChanged += (_, _) => { if (ramp.IsChecked == true) { e.FillType = 1; e.GradientStops ??= [new(0, Pack(e.Red, e.Green, e.Blue, 1)), new(1, Pack(e.Red, e.Green, e.Blue, 0))]; Rebuild(); Schedule(); } };
                structure.Children.Add(Line(new TextBlock { Width = 130 }, solid, Swatch(e, false, outer ? "Outer Glow Color" : "Inner Glow Color"), ramp, GradientPicker(e)));
                var method = Choice("Gradient Method", ["Perceptual", "Linear", "Classic", "Smooth", "Stripes"], e.GradientMethod ?? 3, v => e.GradientMethod = v, 100);
                method.IsEnabled = e.FillType == 1;
                structure.Children.Add(Line(Caption("Method"), method));
                var elements = Section(page, "Elements");
                elements.Children.Add(Line(Caption("Technique"), Choice("Technique", ["Softer", "Precise"], Math.Min(1, e.Technique), v => e.Technique = v, 90)));
                if (!outer)
                {
                    var center = new RadioButton { Content = Localize.Text("Center") + "(E)", GroupName = "glow-source", IsChecked = e.CenterSource, Tag = "Source Center" };
                    var edge = new RadioButton { Content = Localize.Text("Edge") + "(G)", GroupName = "glow-source", IsChecked = !e.CenterSource, Tag = "Source Edge" };
                    center.IsCheckedChanged += (_, _) => { e.CenterSource = center.IsChecked == true; Schedule(); };
                    LayerDialogControls.Key(this, 'E', center); LayerDialogControls.Key(this, 'G', edge);
                    elements.Children.Add(Line(Caption("Source"), center, edge));
                }
                elements.Children.Add(Slider(outer ? "Spread" : "Choke", outer ? 'P' : 'C', e.Spread, 0, 100, "%", v => e.Spread = v, "Spread / Choke (%)"));
                elements.Children.Add(Slider("Size", 'S', e.Size, 0, 250, "px", v => e.Size = v, "Size (px)"));
                var quality = Section(page, "Quality");
                quality.Children.Add(Line(Caption("Contour"), ContourPicker(() => e.ContourSamples, v => { e.ContourSamples = v; e.ContourEnabled = true; }), Check("Anti-aliased", 'L', e.AntiAlias == true, v => e.AntiAlias = v)));
                quality.Children.Add(Slider("Range", 'R', e.Range ?? 50, 1, 100, "%", v => e.Range = v));
                quality.Children.Add(Slider("Jitter", 'J', e.Jitter ?? 0, 0, 100, "%", v => e.Jitter = v));
                break;
            }
            case StyleEffectKind.Satin:
            {
                var structure = Section(page, "Structure");
                structure.Children.Add(Line(Caption("Blend Mode"), Mode(e.BlendMode, v => e.BlendMode = v), Swatch(e, false, "Satin Color")));
                structure.Children.Add(Slider("Opacity", 'O', e.Opacity * 100, 0, 100, "%", v => e.Opacity = v / 100, "Opacity (%)"));
                structure.Children.Add(Angle(e, 'N', false));
                structure.Children.Add(Slider("Distance", 'D', e.Distance, 1, 250, "px", v => e.Distance = v, "Distance (px)"));
                structure.Children.Add(Slider("Size", 'S', e.Size, 0, 250, "px", v => e.Size = v, "Size (px)"));
                var checks = new StackPanel { Spacing = 4, Children = { Check("Anti-aliased", 'L', e.AntiAlias == true, v => e.AntiAlias = v), Check("Invert", 'I', e.Invert, v => e.Invert = v) } };
                structure.Children.Add(Line(Caption("Contour"), ContourPicker(() => e.ContourSamples, v => { e.ContourSamples = v; e.ContourEnabled = true; }), checks));
                break;
            }
            case StyleEffectKind.ColorOverlay:
            {
                var color = Section(page, "Color");
                color.Children.Add(Line(Caption("Blend Mode"), Mode(e.BlendMode, v => e.BlendMode = v), Swatch(e, false, "Overlay Color")));
                color.Children.Add(Slider("Opacity", 'O', e.Opacity * 100, 0, 100, "%", v => e.Opacity = v / 100, "Opacity (%)"));
                break;
            }
            case StyleEffectKind.GradientOverlay:
            {
                var gradient = Section(page, "Gradient");
                gradient.Children.Add(Line(Caption("Blend Mode"), Mode(e.BlendMode, v => e.BlendMode = v), Check("Dither", default, e.Dither == true, v => e.Dither = v)));
                gradient.Children.Add(Slider("Opacity", 'P', e.Opacity * 100, 0, 100, "%", v => e.Opacity = v / 100, "Opacity (%)"));
                GradientRows(gradient, e, stroke: false);
                gradient.Children.Add(Line(Caption("Method"), Choice("Gradient Method", ["Perceptual", "Linear", "Classic", "Smooth", "Stripes"], e.GradientMethod ?? 3, v => e.GradientMethod = v, 100)));
                break;
            }
            case StyleEffectKind.PatternOverlay:
            {
                var pattern = Section(page, "Pattern");
                pattern.Children.Add(Line(Caption("Blend Mode"), Mode(e.BlendMode, v => e.BlendMode = v)));
                pattern.Children.Add(Slider("Opacity", 'Y', e.Opacity * 100, 0, 100, "%", v => e.Opacity = v / 100, "Opacity (%)"));
                PatternRows(pattern, e, angle: true);
                break;
            }
        }
        page.Children.Add(DefaultButtons(e));
    }
    private void GradientRows(StackPanel panel, StyleEffect e, bool stroke)
    {
        panel.Children.Add(Line(Caption("Gradient"), GradientPicker(e), Check("Reverse", 'R', e.Invert, v => e.Invert = v)));
        panel.Children.Add(Line(Caption("Style"), Choice("Gradient Style", ["Linear", "Radial", "Angle", "Reflected", "Diamond"], (int)e.Gradient, v => e.Gradient = (StyleGradient)v, 160), Check("Align with Layer", 'I', e.AlignWithLayer, v => e.AlignWithLayer = v)));
        var angle = Angle(e, 'N', false);
        var reset = FormDialog.ActionButton("Reset Alignment", () => { e.OffsetX = e.OffsetY = 0; Schedule(); }); reset.Tag = "reset-alignment";
        panel.Children.Add(Line(angle, reset));
        panel.Children.Add(Slider("Scale", 'S', e.Scale, 10, 150, "%", v => e.Scale = v));
    }
    private void PatternRows(StackPanel panel, StyleEffect e, bool angle = false)
    {
        panel.Children.Add(PatternPicker(e));
        if (angle) panel.Children.Add(Angle(e, 'N', false));
        panel.Children.Add(Slider("Scale", 'S', e.Scale, 1, 1000, "%", v => e.Scale = v));
        panel.Children.Add(Line(new TextBlock { Width = 130 }, Check("Link with Layer", 'K', e.AlignWithLayer, v => e.AlignWithLayer = v)));
    }
    /// <summary>Bevel and Emboss ▸ Contour: the bevel edge's profile and its range.</summary>
    private void ContourPage(StyleEffect e)
    {
        var page = Page("Contour"); var elements = Section(page, "Elements");
        elements.Children.Add(Line(Caption("Contour"), ContourPicker(() => e.ContourSamples, v => { e.ContourSamples = v; e.ContourEnabled = true; e.GlossSamples ??= Linear(); }), Check("Anti-aliased", 'L', e.AntiAlias == true, v => e.AntiAlias = v)));
        elements.Children.Add(Slider("Range", 'R', e.Range ?? 50, 1, 100, "%", v => e.Range = v));
    }
    /// <summary>Bevel and Emboss ▸ Texture: the pattern pressed into the bevel.</summary>
    private void TextureOptions(StyleEffect e)
    {
        var page = Page("Texture"); var elements = Section(page, "Elements");
        elements.Children.Add(PatternPicker(e));
        elements.Children.Add(Slider("Scale", 'S', e.Scale, 1, 1000, "%", v => e.Scale = v));
        elements.Children.Add(Slider("Depth", 'D', e.TextureDepth, -1000, 1000, "%", v => e.TextureDepth = v));
        elements.Children.Add(Line(new TextBlock { Width = 130 }, Check("Invert", 'I', e.TextureInvert == true, v => e.TextureInvert = v), Check("Link with Layer", 'K', e.AlignWithLayer, v => e.AlignWithLayer = v)));
    }
    private static uint Pack(double r, double g, double b, double a) => (uint)Math.Round(a * 255) << 24 | (uint)Math.Round(r * 255) << 16 | (uint)Math.Round(g * 255) << 8 | (uint)Math.Round(b * 255);

    // ---- Defaults ----
    /// <summary>A new effect: what Make Default saved for its kind, or Photoshop's own starting values.</summary>
    private static StyleEffect StartingEffect(StyleEffectKind kind)
    {
        try
        {
            if (File.Exists(DefaultsPath) && new FileInfo(DefaultsPath).Length < 4_000_000
                && JsonSerializer.Deserialize<List<StyleEffect>>(File.ReadAllText(DefaultsPath))?.FirstOrDefault(e => e.Kind == kind && e.IsValid) is { } saved)
                return saved.Copy();
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { }
        return StyleEffect.PhotoshopDefault(kind);
    }
    private void SaveDefault(StyleEffect e)
    {
        try
        {
            var all = File.Exists(DefaultsPath) ? JsonSerializer.Deserialize<List<StyleEffect>>(File.ReadAllText(DefaultsPath)) ?? [] : [];
            all.RemoveAll(x => x.Kind == e.Kind); var copy = e.Copy(); copy.Enabled = true; all.Add(copy);
            Directory.CreateDirectory(AppPaths.SettingsDirectory); File.WriteAllText(DefaultsPath + ".tmp", JsonSerializer.Serialize(all)); File.Move(DefaultsPath + ".tmp", DefaultsPath, true);
            _error.Text = "";
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { _error.Text = error.Message; }
    }

    // ---- Styles ----
    private static List<LayerStylePreset> LoadPresets()
    {
        try { var path = PresetPath; if (!File.Exists(path) || new FileInfo(path).Length > 16_000_000) return []; return (JsonSerializer.Deserialize<List<LayerStylePreset>>(File.ReadAllText(path)) ?? []).Where(p => p is { IsValid: true }).Take(100).ToList(); }
        catch { return []; }
    }
    private void StorePresets()
    {
        var json = JsonSerializer.Serialize(_presets);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 16_000_000) throw new InvalidDataException("Style library exceeds its size limit.");
        Directory.CreateDirectory(AppPaths.SettingsDirectory); var temp = PresetPath + ".tmp";
        File.WriteAllText(temp, json); File.Move(temp, PresetPath, true);
    }
    private async Task SavePreset()
    {
        var name = await TextPrompt.Ask(this, "New Style", "Name", Localize.Text("Style") + " " + (_presets.Count + 1));
        if (string.IsNullOrWhiteSpace(name)) return;
        try { var preset = Draft.Copy(); preset.Name = name.Trim(); if (!preset.IsValid) return; if (_presets.Count >= 100) _presets.RemoveAt(0); _presets.Add(preset); StorePresets(); _selected = "Styles"; Rebuild(); }
        catch { _error.Text = Localize.Text("Could not save the style preset."); }
    }
    /// <summary>Compositor's own starting styles, drawn by it, in place of Adobe's style libraries.</summary>
    private static IEnumerable<(string Group, LayerStylePreset Style)> BuiltInStyles()
    {
        LayerStylePreset Make(string name, params StyleEffect[] effects) => new() { Name = name, Effects = new LayerEffects { Items = [.. effects] }, Blending = new() };
        StyleEffect E(StyleEffectKind kind, Action<StyleEffect> tune) { var e = StyleEffect.PhotoshopDefault(kind); tune(e); return e; }
        yield return ("Basics", new LayerStylePreset { Name = "Default Style (None)", Blending = new() });
        yield return ("Basics", Make("Drop Shadow", E(StyleEffectKind.DropShadow, e => { e.Size = 10; e.Distance = 6; })));
        yield return ("Compositor", Make("Raised", E(StyleEffectKind.BevelEmboss, e => { e.Size = 10; }), E(StyleEffectKind.DropShadow, e => { e.Size = 8; })));
        yield return ("Compositor", Make("Pressed", E(StyleEffectKind.InnerShadow, e => { e.Size = 10; e.Opacity = .6; }), E(StyleEffectKind.Stroke, e => { e.Red = e.Green = e.Blue = .3; })));
        yield return ("Compositor", Make("Glow", E(StyleEffectKind.OuterGlow, e => { e.Size = 18; e.Opacity = .7; e.Red = 1; e.Green = .85; e.Blue = .4; e.GradientStops = null; })));
        yield return ("Compositor", Make("Outline", E(StyleEffectKind.Stroke, e => { e.Size = 4; e.Position = StrokePosition.Outside; })));
        yield return ("Compositor", Make("Satin Sheen", E(StyleEffectKind.Satin, e => { e.Red = e.Green = e.Blue = 1; e.BlendMode = LayerBlendMode.Overlay; })));
        yield return ("Compositor", Make("Gradient Wash", E(StyleEffectKind.GradientOverlay, e => { e.GradientStops = [new(0, 0xFF224BD1), new(1, 0xFFBDE9FF)]; e.BlendMode = LayerBlendMode.Overlay; })));
    }
    private void PresetOptions()
    {
        var page = Page("Styles");
        var all = BuiltInStyles().Concat(_presets.Select(p => ("My Styles", p))).ToList();
        var body = new StackPanel { Spacing = 4 };
        var textOnly = false; var large = false;
        void Fill()
        {
            body.Children.Clear();
            foreach (var group in all.GroupBy(s => s.Item1))
            {
                var tiles = new WrapPanel();
                foreach (var (_, style) in group)
                {
                    Control content = textOnly ? new TextBlock { Text = Localize.Text(style.Name) } : new StylePresetThumb(style) { Width = large ? 64 : 42, Height = large ? 64 : 42 };
                    var tile = new Button { Content = content, Padding = new Thickness(2), Margin = new Thickness(3), Tag = "style:" + style.Name, Background = Brushes.Transparent };
                    ToolTip.SetTip(tile, Localize.Text(style.Name));
                    tile.Click += (_, _) => { Draft = style.Copy(); Draft.Effects = (Draft.Effects ?? new()).EditableCopy(); Draft.Blending ??= new(); foreach (var k in Order) if (!Draft.Effects.Items!.Any(x => x.Kind == k)) { var off = StartingEffect(k); off.Enabled = false; Draft.Effects.Items!.Add(off); } Draft.Effects.Items = Draft.Effects.Items!.OrderBy(x => Array.IndexOf(Order, x.Kind)).ToList(); Rebuild(); Schedule(); };
                    if (group.Key == "My Styles")
                    {
                        var menu = new ContextMenu(); var delete = new MenuItem { Header = Localize.Text("Delete Style") };
                        delete.Click += (_, _) => { try { _presets.Remove(style); StorePresets(); Rebuild(); } catch { _error.Text = Localize.Text("Could not save the style preset."); } };
                        menu.Items.Add(delete); tile.ContextMenu = menu;
                    }
                    tiles.Children.Add(tile);
                }
                body.Children.Add(new Expander { Header = "📁 " + Localize.Text(group.Key), Content = tiles, IsExpanded = true, HorizontalAlignment = HorizontalAlignment.Stretch });
            }
        }
        Fill();
        var gear = new Button { Content = "⚙⌄", Padding = new Thickness(4, 2), VerticalAlignment = VerticalAlignment.Top, Tag = "styles-gear" };
        gear.Click += (_, _) =>
        {
            var menu = new ContextMenu();
            void Add(string title, Action? act, bool check = false)
            { var item = new MenuItem { Header = Localize.Text(title), IsEnabled = act is not null, ToggleType = check ? MenuItemToggleType.CheckBox : MenuItemToggleType.None, IsChecked = check }; if (act is not null) item.Click += (_, _) => act(); menu.Items.Add(item); }
            Add("Append Default Styles…", () => { Rebuild(); });
            Add("Import Styles…", () => _ = ImportStyles()); Add("Export Styles…", () => _ = ExportStyles()); menu.Items.Add(new Separator());
            Add("Rename Group…", null); Add("Delete Group…", _presets.Count > 0 ? async () => { if (await ConfirmDialog.Ask(this, "Delete Group", Localize.Text("Delete every style in My Styles?"), "Delete", "Cancel")) { _presets.Clear(); try { StorePresets(); } catch { } Rebuild(); } } : null);
            menu.Items.Add(new Separator());
            Add("Text Only", () => { textOnly = true; Fill(); }, textOnly); Add("Small Thumbnail", () => { textOnly = large = false; Fill(); }, !textOnly && !large);
            Add("Large Thumbnail", () => { textOnly = false; large = true; Fill(); }, large && !textOnly); Add("Small List", () => { textOnly = true; Fill(); }); Add("Large List", () => { textOnly = true; Fill(); });
            menu.Open(gear);
        };
        page.Children.Add(new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8, Children = { new Border { BorderBrush = Skin.BorderSubtleBrush, BorderThickness = new Thickness(1), MinHeight = 520, Child = new ScrollViewer { Content = body } }, gear } });
        Grid.SetColumn(gear, 1);
    }
    private async Task ExportStyles()
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Localize.Text("Export Styles"), SuggestedFileName = "styles.json", DefaultExtension = "json" });
            if (file?.TryGetLocalPath() is { } path) await File.WriteAllTextAsync(path, JsonSerializer.Serialize(_presets.Count > 0 ? _presets : [Draft], new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { _error.Text = Localize.Text("Could not save the style preset."); }
    }
    private async Task ImportStyles()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Import Styles"), FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }] });
            if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            if (new FileInfo(path).Length > 16_000_000) throw new InvalidDataException();
            var text = await File.ReadAllTextAsync(path);
            var imported = text.TrimStart().StartsWith('[') ? JsonSerializer.Deserialize<List<LayerStylePreset>>(text) ?? [] : [JsonSerializer.Deserialize<LayerStylePreset>(text)!];
            foreach (var preset in imported.Where(p => p is { IsValid: true }).Take(100 - _presets.Count)) _presets.Add(preset);
            StorePresets(); Rebuild();
        }
        catch { _error.Text = Localize.Text("This is not a valid layer style preset."); }
    }
}

/// <summary>The style sample under Preview: the layer's own picture, small, with the style applied.</summary>
internal sealed class StyleSample : Control, IDisposable
{
    private readonly SKBitmap _source;
    private Avalonia.Media.Imaging.Bitmap? _image;
    internal StyleSample(SKBitmap? source)
    {
        Width = 180; Height = 180;
        if (source is null) { _source = Bitmaps.Allocate(Bitmaps.ColorInfo(80, 80)); _source.Erase(new SKColor(130, 130, 130)); }
        else { var scale = Math.Min(1, 100.0 / Math.Max(source.Width, source.Height)); _source = Bitmaps.Scale(source, Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale))); }
    }
    internal void Update(LayerStylePreset? style)
    {
        _image?.Dispose(); _image = null;
        using var premul = Bitmaps.Premultiplied(_source);
        using var raster = style?.Effects is { } effects ? EffectRasterizer.Render(premul, effects.Scaled(0.35), style.FillOpacity, style.Blending?.TransparencyShapesLayer ?? true) : null;
        _image = CanvasView.ToImage(raster?.Pixels ?? _source); InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)), new Pen(Skin.BorderControlBrush, 1), new Rect(Bounds.Size));
        if (_image is not { } image) return; var scale = Math.Min((Bounds.Width - 40) / image.Size.Width, (Bounds.Height - 40) / image.Size.Height);
        context.DrawImage(image, new Rect((Bounds.Width - image.Size.Width * scale) / 2, (Bounds.Height - image.Size.Height * scale) / 2, image.Size.Width * scale, image.Size.Height * scale));
    }
    public void Dispose() { _image?.Dispose(); _source.Dispose(); }
}

/// <summary>A contour's curve drawn as Photoshop's thumbnails draw it: white under the curve, dark above.</summary>
internal sealed class ContourThumb(Func<double[]?> samples) : Control
{
    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width; var h = Bounds.Height; context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)), new Pen(Skin.BorderControlBrush, 1), new Rect(Bounds.Size));
        var values = samples() ?? Enumerable.Range(0, 65).Select(i => i / 64d).ToArray();
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(1, h - 1), true);
            for (var i = 0; i < values.Length; i++) g.LineTo(new Point(1 + (w - 2) * i / (values.Length - 1), h - 1 - (h - 2) * values[i]));
            g.LineTo(new Point(w - 1, h - 1)); g.EndFigure(true);
        }
        context.DrawGeometry(Brushes.White, null, geometry);
    }
}

/// <summary>A layer style gradient drawn as a strip, transparency over a checkerboard.</summary>
internal sealed class StyleGradientStrip(StyleEffect effect) : Control
{
    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size); context.DrawRectangle(Skin.Checker, null, rect);
        var steps = Math.Max(2, (int)Bounds.Width);
        for (var i = 0; i < steps; i++)
        {
            var (r, g, b, a) = effect.GradientAt(i / (double)(steps - 1));
            context.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)Math.Round(a * 255), (byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255))), new Rect(i * Bounds.Width / steps, 0, Bounds.Width / steps + 1, Bounds.Height));
        }
        context.DrawRectangle(null, new Pen(Skin.BorderControlBrush, 1), rect);
    }
}

/// <summary>A layer style pattern tile, the image when there is one and the procedural tile otherwise.</summary>
internal sealed class StylePatternThumb(StyleEffect effect) : Control
{
    public override void Render(DrawingContext context)
    {
        using var tile = Bitmaps.Allocate(Bitmaps.ColorInfo(64, 64));
        if (effect.Pattern == StylePattern.Image && effect.PatternPng is { } png)
        {
            try { using var decoded = SKBitmap.Decode(Convert.FromBase64String(png)); using var canvas = new SKCanvas(tile); using var picture = SKImage.FromBitmap(decoded); canvas.DrawImage(picture, new SKRect(0, 0, 64, 64), new SKSamplingOptions(SKFilterMode.Linear)); }
            catch (FormatException) { tile.Erase(SKColors.Gray); }
        }
        else for (var y = 0; y < 64; y++) for (var x = 0; x < 64; x++)
        {
            var a = x / 16; var b = y / 16;
            var on = effect.Pattern switch { StylePattern.Stripes => x % 16 < 8, StylePattern.Dots => Math.Pow(x % 16 - 8, 2) + Math.Pow(y % 16 - 8, 2) < 20, _ => (a + b) % 2 == 0 };
            tile.SetPixel(x, y, on ? SKColors.White : new SKColor(70, 70, 70));
        }
        using var image = CanvasView.ToImage(tile);
        context.DrawImage(image, new Rect(Bounds.Size)); context.DrawRectangle(null, new Pen(Skin.BorderControlBrush, 1), new Rect(Bounds.Size));
    }
}

/// <summary>A style preset's thumbnail: a gray square with the style drawn on it.</summary>
internal sealed class StylePresetThumb(LayerStylePreset style) : Control
{
    public override void Render(DrawingContext context)
    {
        using var square = Bitmaps.Allocate(Bitmaps.ColorInfo(24, 24)); square.Erase(new SKColor(150, 150, 150));
        using var premul = Bitmaps.Premultiplied(square);
        EffectRaster? raster = null;
        try { if (style.Effects is { Items.Count: > 0 } effects) raster = EffectRasterizer.Render(premul, effects.Scaled(.25), style.FillOpacity, true); } catch (Exception) { }
        using (raster)
        {
            var pixels = raster?.Pixels ?? square; using var image = CanvasView.ToImage(pixels);
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)), new Pen(Skin.BorderControlBrush, 1), new Rect(Bounds.Size));
            var inset = Bounds.Width * .12; context.DrawImage(image, new Rect(inset, inset, Bounds.Width - inset * 2, Bounds.Height - inset * 2));
            if (style.Effects is not { Items.Count: > 0 }) context.DrawLine(new Pen(Brushes.Red, 2), new Point(inset, inset), new Point(Bounds.Width - inset, Bounds.Height - inset));
        }
    }
}
