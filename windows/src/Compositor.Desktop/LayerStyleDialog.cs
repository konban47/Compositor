using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
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

internal sealed class LayerStyleDialog : DialogWindow
{
    internal LayerStylePreset Draft { get; private set; }
    internal string LayerName => _name.Text?.Trim() ?? "";
    private readonly TextBox _name = new();
    private readonly StackPanel _list = new() { Spacing = 2 };
    private readonly StackPanel _options = new() { Spacing = 6, Margin = new Thickness(18, 8) };
    private readonly CheckBox _preview = new() { Content = Localize.Text("Preview"), IsChecked = true };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Orange };
    private readonly StyleSample _sample;
    private readonly Action<LayerStylePreset?> _changed;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private object? _selected;
    private bool _building;
    private bool _accepted;
    private readonly List<LayerStylePreset> _presets = LoadPresets();
    private static string PresetPath => Path.Combine(AppPaths.SettingsDirectory, "layer-styles.json");
    internal LayerStyleDialog(ImageLayer layer, Action<LayerStylePreset?> changed, StyleEffectKind? kind = null)
    {
        Title = Localize.Text("Layer Style"); Width = 1100; Height = 800; MinWidth = 920; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Draft = Capture(layer); Draft.Effects = (Draft.Effects ?? new()).EditableCopy(); Draft.Blending ??= new();
        _changed = changed; _name.Text = layer.Name;
        foreach (var effectKind in Enum.GetValues<StyleEffectKind>())
            if (!Draft.Effects.Items!.Any(e => e.Kind == effectKind)) { var effect = StyleEffect.Default(effectKind); effect.Enabled = false; Draft.Effects.Items!.Add(effect); }

        _sample = new StyleSample(layer.Asset?.Image);
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("260,*,190"), RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(14) };
        var nameRow = new Grid { ColumnDefinitions = new ColumnDefinitions("80,*"), Margin = new Thickness(0, 0, 12, 12) };
        nameRow.Children.Add(new TextBlock { Text = Localize.Text("Name:"), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(_name, 1); nameRow.Children.Add(_name); Grid.SetColumnSpan(nameRow, 2); layout.Children.Add(nameRow);
        var left = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(6) };
        actions.Children.Add(IconButton("more", "Add Layer Effect", AddEffectMenu));
        actions.Children.Add(IconButton("up", "Move Effect Up", () => Move(-1)));
        actions.Children.Add(IconButton("down", "Move Effect Down", () => Move(1)));
        actions.Children.Add(IconButton("delete", "Delete Effect", Remove));
        DockPanel.SetDock(actions, Dock.Bottom); left.Children.Add(actions); left.Children.Add(new ScrollViewer { Content = _list });
        Grid.SetRow(left, 1); layout.Children.Add(left);
        var content = new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Child = new ScrollViewer { Content = _options } };
        Grid.SetRow(content, 1); Grid.SetColumn(content, 1); layout.Children.Add(content);
        var right = new StackPanel { Spacing = 12, Margin = new Thickness(16, 0, 0, 0) };
        var ok = ActionButton("OK", () => { if (LayerName.Length is 0 or > 256 || !Draft.IsValid) { _error.Text = Localize.Text("Enter a valid layer name and style."); return; } _accepted = true; Close(); }); ok.IsDefault = true;
        var cancel = ActionButton("Cancel", Close); cancel.IsCancel = true;
        right.Children.Add(ok); right.Children.Add(cancel); right.Children.Add(ActionButton("New Style…", () => _ = SavePreset()));
        right.Children.Add(_preview); right.Children.Add(_sample); right.Children.Add(_error);
        _preview.IsCheckedChanged += (_, _) => Schedule();
        Grid.SetColumn(right, 2); Grid.SetRowSpan(right, 3); layout.Children.Add(right);
        Content = layout; _timer.Tick += (_, _) => { _timer.Stop(); Preview(); };
        Closed += (_, _) => { _timer.Stop(); _sample.Dispose(); };
        _selected = kind is { } first ? Draft.Effects.Items!.First(e => e.Kind == first) : "Blending Options";
        if (_selected is StyleEffect selected) selected.Enabled = true;
        Rebuild(); Preview();
    }
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
        return dialog._accepted ? (dialog.Draft, dialog.LayerName) : null;
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
    private void Rebuild()
    {
        _building = true; _list.Children.Clear();
        foreach (var section in new[] { "Styles", "Blending Options" })
            _list.Children.Add(ActionButton(section, () => { _selected = section; Rebuild(); }));
        var items = Draft.Effects!.Items!;
        foreach (var effect in items)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*,28"), MinHeight = 32 };
            var check = new CheckBox { IsChecked = effect.Enabled, VerticalAlignment = VerticalAlignment.Center };
            check.IsCheckedChanged += (_, _) => { effect.Enabled = check.IsChecked == true; Schedule(); };
            row.Children.Add(check);
            var siblings = items.Where(e => e.Kind == effect.Kind).ToList();
            var label = Localize.Text(NameFor(effect.Kind)) + (siblings.Count > 1 ? " " + (siblings.IndexOf(effect) + 1) : "");
            var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch, Background = ReferenceEquals(_selected, effect) ? Skin.TabFront : Brushes.Transparent, BorderThickness = new Thickness(0) };
            button.Click += (_, _) => { _selected = effect; Rebuild(); }; Grid.SetColumn(button, 1); row.Children.Add(button);
            if (effect.Kind is StyleEffectKind.Stroke or StyleEffectKind.InnerShadow or StyleEffectKind.ColorOverlay or StyleEffectKind.GradientOverlay or StyleEffectKind.DropShadow)
            {
                var add = new Button { Content = "+", Padding = new Thickness(2) }; ToolTip.SetTip(add, Localize.Text("Add Another Effect"));
                add.Click += (_, _) => { if (items.Count >= 40) return; var copy = effect.Copy(); copy.Enabled = true; items.Insert(items.IndexOf(effect) + 1, copy); _selected = copy; Rebuild(); Schedule(); };
                Grid.SetColumn(add, 2); row.Children.Add(add);
            }
            _list.Children.Add(row);
            if (effect.Kind == StyleEffectKind.BevelEmboss)
            {
                foreach (var texture in new[] { false, true })
                {
                    var nested = new CheckBox { Content = Localize.Text(texture ? "Texture" : "Contour"), IsChecked = texture ? effect.TextureEnabled : effect.ContourEnabled, Margin = new Thickness(24, 0, 0, 0) };
                    nested.IsCheckedChanged += (_, _) => { if (texture) effect.TextureEnabled = nested.IsChecked == true; else effect.ContourEnabled = nested.IsChecked == true; effect.Enabled = true; _selected = effect; Rebuild(); Schedule(); };
                    _list.Children.Add(nested);
                }
            }
        }
        _options.Children.Clear();
        if (_selected is StyleEffect selected) EffectOptions(selected);
        else if (_selected?.ToString() == "Styles") PresetOptions(); else BlendingOptions();
        _building = false;
    }
    private void Heading(string text) => _options.Children.Add(new TextBlock { Text = Localize.Text(text), FontSize = 14, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
    private void Number(string label, double value, double min, double max, Action<double> set, double step = 1)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("160,*,85"), Height = 32, Margin = new Thickness(0, 2) };
        row.Children.Add(new TextBlock { Text = Localize.Text(label), VerticalAlignment = VerticalAlignment.Center });
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Margin = new Thickness(0, 0, 8, 0) };
        var number = new NumericUpDown { Minimum = (decimal)min, Maximum = (decimal)max, Value = (decimal)Math.Clamp(value, min, max), Increment = (decimal)step, FormatString = "0.##", ShowButtonSpinner = false, MinHeight = 28, Height = 28, Tag = label };
        var changing = false;
        number.ValueChanged += (_, _) => { if (changing) return; changing = true; slider.Value = (double)(number.Value ?? (decimal)value); set(slider.Value); changing = false; Schedule(); };
        slider.PropertyChanged += (_, e) => { if (e.Property != Slider.ValueProperty || changing) return; changing = true; number.Value = (decimal)slider.Value; set(slider.Value); changing = false; Schedule(); };
        Grid.SetColumn(slider, 1); row.Children.Add(slider); Grid.SetColumn(number, 2); row.Children.Add(number); _options.Children.Add(row);
    }
    private void Check(string label, bool value, Action<bool> set)
    {
        var box = new CheckBox { Content = Localize.Text(label), IsChecked = value, MinHeight = 24, Height = 26 };
        box.IsCheckedChanged += (_, _) => { set(box.IsChecked == true); Schedule(); }; _options.Children.Add(box);
    }
    private void Choice<T>(string label, T value, Action<T> set, Func<T, string>? title = null) where T : struct, Enum
    {
        var values = Enum.GetValues<T>(); var combo = new ComboBox { ItemsSource = values.Select(v => Localize.Text(title is null ? SplitName(v.ToString()) : title(v))).ToArray(), SelectedIndex = Array.IndexOf(values, value), HorizontalAlignment = HorizontalAlignment.Stretch };
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex < 0) return; set(values[combo.SelectedIndex]); Schedule(); };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("160,*") };
        row.Children.Add(new TextBlock { Text = Localize.Text(label), VerticalAlignment = VerticalAlignment.Center }); Grid.SetColumn(combo, 1); row.Children.Add(combo); _options.Children.Add(row);
    }
    private static string SplitName(string name) => System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z])([A-Z])", " $1");
    private void Mode(string label, LayerBlendMode value, Action<LayerBlendMode> set) => Choice(label, value, set, mode => BlendModes.Name(BlendModes.From(mode)));
    private void Color(string label, StyleEffect e, bool second = false)
    {
        var button = new Button { Content = Localize.Text(label), HorizontalAlignment = HorizontalAlignment.Stretch };
        void Swatch() { var c = second ? (e.Red2, e.Green2, e.Blue2) : (e.Red, e.Green, e.Blue); button.BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb((byte)(c.Item1 * 255), (byte)(c.Item2 * 255), (byte)(c.Item3 * 255))); button.BorderThickness = new Thickness(16, 1, 1, 1); }
        Swatch(); button.Click += async (_, _) => await ColorPickerDialog.Pick(this, Localize.Text(label), second ? (e.Red2, e.Green2, e.Blue2) : (e.Red, e.Green, e.Blue), rgb =>
        { var (r, g, b) = rgb; if (second) { e.Red2 = r; e.Green2 = g; e.Blue2 = b; } else { e.Red = r; e.Green = g; e.Blue = b; } Swatch(); Schedule(); });
        _options.Children.Add(button);
    }
    private void BlendingOptions()
    {
        var blend = Draft.Blending!;
        Heading("General Blending"); Mode("Blend Mode", Draft.BlendMode, v => Draft.BlendMode = v);
        Number("Opacity (%)", Draft.Opacity * 100, 0, 100, v => Draft.Opacity = v / 100);
        Heading("Advanced Blending"); Number("Fill Opacity (%)", Draft.FillOpacity * 100, 0, 100, v => Draft.FillOpacity = v / 100);
        var channels = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
        channels.Children.Add(new TextBlock { Text = Localize.Text("Channels:"), VerticalAlignment = VerticalAlignment.Center });
        foreach (var (name, value, set) in new (string, bool, Action<bool>)[] { ("R", blend.Red, v => blend.Red = v), ("G", blend.Green, v => blend.Green = v), ("B", blend.Blue, v => blend.Blue = v) })
        { var box = new CheckBox { Content = name, IsChecked = value }; box.IsCheckedChanged += (_, _) => { set(box.IsChecked == true); Schedule(); }; channels.Children.Add(box); }
        _options.Children.Add(channels); Choice("Knockout", blend.Knockout, v => blend.Knockout = v);
        Check("Blend Interior Effects as Group", blend.BlendInteriorEffectsAsGroup, v => blend.BlendInteriorEffectsAsGroup = v);
        Check("Blend Clipped Layers as Group", blend.BlendClippedLayersAsGroup, v => blend.BlendClippedLayersAsGroup = v);
        Check("Transparency Shapes Layer", blend.TransparencyShapesLayer, v => blend.TransparencyShapesLayer = v);
        Check("Layer Mask Hides Effects", blend.LayerMaskHidesEffects, v => blend.LayerMaskHidesEffects = v);
        Check("Vector Mask Hides Effects", blend.VectorMaskHidesEffects, v => blend.VectorMaskHidesEffects = v);
        Heading("Blend If");
        var rangePanel = new StackPanel { Spacing = 8 };
        void Ranges(BlendIfChannel channel)
        {
            rangePanel.Children.Clear(); var range = blend.Ranges.FirstOrDefault(r => r.Channel == channel);
            if (range is null) { range = new BlendIfRange { Channel = channel }; blend.Ranges.Add(range); }
            foreach (var (title, values) in new[] { ("This Layer", range.Source), ("Underlying Layer", range.Underlying) })
            {
                rangePanel.Children.Add(new TextBlock { Text = Localize.Text(title) });
                rangePanel.Children.Add(new BlendRangeControl(values, Schedule));
            }
            rangePanel.Children.Add(new TextBlock { Text = Localize.Text("Alt-drag a triangle to split it for a smooth transition."), TextWrapping = TextWrapping.Wrap, FontSize = 11 });
        }
        Choice("Blend If:", BlendIfChannel.Gray, Ranges); _options.Children.Add(rangePanel); Ranges(BlendIfChannel.Gray);
    }
    private void EffectOptions(StyleEffect e)
    {
        Heading(NameFor(e.Kind)); Check("Enable Effect", e.Enabled, v => { e.Enabled = v; Rebuild(); });
        if (e.Kind != StyleEffectKind.BevelEmboss) Mode("Blend Mode", e.BlendMode, v => e.BlendMode = v);
        Number("Opacity (%)", e.Opacity * 100, 0, 100, v => e.Opacity = v / 100);
        if (e.Kind != StyleEffectKind.BevelEmboss) Color("Color", e);
        if (e.Kind is StyleEffectKind.DropShadow or StyleEffectKind.InnerShadow or StyleEffectKind.BevelEmboss)
            Check("Use Global Light", e.UseGlobalLight, v => { e.UseGlobalLight = v; Rebuild(); });
        if (e.Kind is not (StyleEffectKind.ColorOverlay or StyleEffectKind.InnerGlow or StyleEffectKind.OuterGlow))
            Number("Angle (degrees)", e.UseGlobalLight ? Draft.Effects!.GlobalLightAngle : e.Angle, -180, 180, v => { if (e.UseGlobalLight) Draft.Effects!.GlobalLightAngle = v; else e.Angle = v; });
        if (e.Kind is StyleEffectKind.DropShadow or StyleEffectKind.InnerShadow or StyleEffectKind.Satin)
            Number("Distance (px)", e.Distance, 0, 5000, v => e.Distance = v);
        if (e.Kind is not (StyleEffectKind.ColorOverlay or StyleEffectKind.GradientOverlay or StyleEffectKind.PatternOverlay))
            Number("Size (px)", e.Size, 0, 500, v => e.Size = v);
        if (e.Kind is StyleEffectKind.DropShadow or StyleEffectKind.InnerShadow or StyleEffectKind.InnerGlow or StyleEffectKind.OuterGlow)
        { Number("Spread / Choke (%)", e.Spread, 0, 100, v => e.Spread = v); Number("Noise (%)", e.Noise, 0, 100, v => e.Noise = v); }
        if (e.Kind == StyleEffectKind.InnerGlow) Check("Source: Center", e.CenterSource, v => e.CenterSource = v);
        if (e.Kind == StyleEffectKind.Satin) Check("Invert", e.Invert, v => e.Invert = v);
        if (e.Kind == StyleEffectKind.Stroke)
        {
            Choice("Position", e.Position, v => e.Position = v);
            var fill = new ComboBox { ItemsSource = new[] { "Color", "Gradient", "Pattern" }.Select(Localize.Text).ToArray(), SelectedIndex = e.FillType };
            fill.SelectionChanged += (_, _) => { e.FillType = fill.SelectedIndex; Rebuild(); Schedule(); }; _options.Children.Add(fill);
        }
        if (e.Kind == StyleEffectKind.GradientOverlay || e.Kind == StyleEffectKind.Stroke && e.FillType == 1)
        {
            Color("End Color", e, true); Choice("Gradient Style", e.Gradient, v => e.Gradient = v); Number("Scale (%)", e.Scale, 1, 1000, v => e.Scale = v);
            Check("Reverse", e.Invert, v => e.Invert = v); Check("Align with Layer", e.AlignWithLayer, v => e.AlignWithLayer = v);
            Number("Offset X (px)", e.OffsetX, -10000, 10000, v => e.OffsetX = v); Number("Offset Y (px)", e.OffsetY, -10000, 10000, v => e.OffsetY = v);
        }
        if (e.Kind == StyleEffectKind.PatternOverlay || e.Kind == StyleEffectKind.Stroke && e.FillType == 2) PatternOptions(e);
        if (e.Kind == StyleEffectKind.BevelEmboss)
        {
            Choice("Style", e.Bevel, v => e.Bevel = v);
            var technique = new ComboBox { ItemsSource = new[] { "Smooth", "Chisel Hard", "Chisel Soft" }.Select(Localize.Text).ToArray(), SelectedIndex = e.Technique };
            technique.SelectionChanged += (_, _) => { e.Technique = technique.SelectedIndex; Schedule(); }; _options.Children.Add(technique);
            Number("Depth (%)", e.Depth, 0, 1000, v => e.Depth = v); Check("Direction: Down", e.Invert, v => e.Invert = v);
            Number("Soften (px)", e.Soften, 0, 100, v => e.Soften = v); Number("Altitude (degrees)", e.Altitude, 0, 90, v => e.Altitude = v);
            Mode("Highlight Mode", e.HighlightMode, v => e.HighlightMode = v); Color("Highlight Color", e, true);
            Number("Highlight Opacity (%)", e.HighlightOpacity * 100, 0, 100, v => e.HighlightOpacity = v / 100);
            Mode("Shadow Mode", e.ShadowMode, v => e.ShadowMode = v); Color("Shadow Color", e);
            Number("Shadow Opacity (%)", e.ShadowOpacity * 100, 0, 100, v => e.ShadowOpacity = v / 100);
            Check("Texture", e.TextureEnabled, v => { e.TextureEnabled = v; Rebuild(); });
            if (e.TextureEnabled) { PatternOptions(e); Number("Texture Depth (%)", e.TextureDepth, -1000, 1000, v => e.TextureDepth = v); }
        }
        if (e.Kind is not (StyleEffectKind.ColorOverlay or StyleEffectKind.GradientOverlay or StyleEffectKind.PatternOverlay or StyleEffectKind.Stroke))
        { Check("Contour", e.ContourEnabled, v => e.ContourEnabled = v); Choice("Contour", e.Contour, v => e.Contour = v); }
        _options.Children.Add(ActionButton("Reset Effect", () => { var copy = StyleEffect.Default(e.Kind); Draft.Effects!.Items![Draft.Effects.Items.IndexOf(e)] = copy; _selected = copy; Rebuild(); Schedule(); }));
    }
    private void PatternOptions(StyleEffect e)
    {
        Choice("Pattern", e.Pattern, v => e.Pattern = v); Color("Pattern Second Color", e, true);
        Number("Pattern Scale (%)", e.Scale, 1, 1000, v => e.Scale = v); Check("Link with Layer", e.AlignWithLayer, v => e.AlignWithLayer = v);
        Number("Offset X (px)", e.OffsetX, -10000, 10000, v => e.OffsetX = v); Number("Offset Y (px)", e.OffsetY, -10000, 10000, v => e.OffsetY = v);
        _options.Children.Add(ActionButton("Load Pattern Image…", async () =>
        {
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Load Pattern Image"), FileTypeFilter = [FilePickerFileTypes.ImageAll] });
                if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
                using var image = ImageImporter.Decode(path); var scale = Math.Min(1, 512.0 / Math.Max(image.Width, image.Height)); using var small = Bitmaps.Scale(image.Image, Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)));
                e.PatternPng = Convert.ToBase64String(PngCodec.Encode(small)); e.Pattern = StylePattern.Image; Rebuild(); Schedule();
            }
            catch (Exception) { _error.Text = Localize.Text("Could not load that pattern image."); }
        }));
    }
    private void AddEffectMenu()
    {
        var menu = new ContextMenu(); foreach (var kind in Enum.GetValues<StyleEffectKind>())
        { var item = new MenuItem { Header = Localize.Text(NameFor(kind)) }; item.Click += (_, _) => { if (Draft.Effects!.Items!.Count >= 40) return; var e = StyleEffect.Default(kind); Draft.Effects.Items.Add(e); _selected = e; Rebuild(); Schedule(); }; menu.Items.Add(item); } menu.Open(_list);
    }
    private void Move(int delta)
    { if (_selected is not StyleEffect e) return; var list = Draft.Effects!.Items!; var index = list.IndexOf(e); var other = index + delta; if (other < 0 || other >= list.Count) return; (list[index], list[other]) = (list[other], list[index]); Rebuild(); Schedule(); }
    private void Remove() { if (_selected is not StyleEffect e) return; Draft.Effects!.Items!.Remove(e); _selected = "Blending Options"; Rebuild(); Schedule(); }
    private static Button ActionButton(string title, Action action)
    { var button = new Button { Content = Localize.Text(title), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center }; button.Click += (_, _) => action(); return button; }
    private static Button IconButton(string icon, string title, Action action)
    { var button = ActionButton(title, action); button.Content = new InspectorGlyph(icon); button.Width = 30; button.Padding = new Thickness(3); ToolTip.SetTip(button, Localize.Text(title)); Avalonia.Automation.AutomationProperties.SetName(button, Localize.Text(title)); return button; }
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
    private void PresetOptions()
    {
        Heading("Styles");
        var list = new ListBox { ItemsSource = _presets.Select(p => p.Name).ToArray(), MinHeight = 180, MaxHeight = 350 };
        _options.Children.Add(list);
        _options.Children.Add(ActionButton("Apply Style", () => { if (list.SelectedIndex < 0) return; Draft = _presets[list.SelectedIndex].Copy(); Draft.Effects = (Draft.Effects ?? new()).EditableCopy(); Draft.Blending ??= new(); _selected = "Blending Options"; Rebuild(); Schedule(); }));
        _options.Children.Add(ActionButton("Delete Style", () => { if (list.SelectedIndex < 0) return; try { _presets.RemoveAt(list.SelectedIndex); StorePresets(); Rebuild(); } catch { _error.Text = Localize.Text("Could not save the style preset."); } }));
        _options.Children.Add(ActionButton("Save Style Preset…", async () =>
        {
            try
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Localize.Text("Save Style Preset"), SuggestedFileName = "style.json", DefaultExtension = "json" });
                if (file?.TryGetLocalPath() is { } path) await File.WriteAllTextAsync(path, JsonSerializer.Serialize(Draft, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { _error.Text = Localize.Text("Could not save the style preset."); }
        }));
        _options.Children.Add(ActionButton("Load Style Preset…", async () =>
        {
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Load Style Preset"), FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }] });
                if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
                if (new FileInfo(path).Length > 4_000_000) throw new InvalidDataException();
                var preset = JsonSerializer.Deserialize<LayerStylePreset>(await File.ReadAllTextAsync(path));
                if (preset is not { IsValid: true }) throw new InvalidDataException();
                Draft = preset; Draft.Effects = (Draft.Effects ?? new()).EditableCopy(); Draft.Blending ??= new(); _selected = "Blending Options"; Rebuild(); Schedule();
            }
            catch { _error.Text = Localize.Text("This is not a valid layer style preset."); }
        }));
    }
}

internal sealed class StyleSample : Control, IDisposable
{
    private readonly SKBitmap _source;
    private Avalonia.Media.Imaging.Bitmap? _image;
    internal StyleSample(SKBitmap? source)
    {
        Width = 174; Height = 190;
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
        context.DrawRectangle(Skin.Checker, new Pen(Skin.BorderControlBrush, 1), new Rect(Bounds.Size));
        if (_image is not { } image) return; var scale = Math.Min((Bounds.Width - 16) / image.Size.Width, (Bounds.Height - 16) / image.Size.Height);
        context.DrawImage(image, new Rect((Bounds.Width - image.Size.Width * scale) / 2, (Bounds.Height - image.Size.Height * scale) / 2, image.Size.Width * scale, image.Size.Height * scale));
    }
    public void Dispose() { _image?.Dispose(); _source.Dispose(); }
}
