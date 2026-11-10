using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Compositor.Core.Document;
using SkiaSharp;

namespace Compositor.Desktop;

/// <summary>A dialog of the Edit menu laid out as Photoshop's: its content on the left, OK and Cancel down the right.</summary>
internal abstract class SideDialog : DialogWindow
{
    protected readonly StackPanel Body = new() { Spacing = 6 };
    protected readonly StackPanel Actions;
    protected readonly Button Ok, Cancel;
    internal bool Accepted { get; private set; }
    protected SideDialog(string title, double width, string ok = "OK")
    {
        Title = Localize.Text(title); Width = width; SizeToContent = SizeToContent.Height; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Ok = FormDialog.ActionButton(ok, () => { if (Validate()) { Accepted = true; Close(true); } }); Ok.IsDefault = true;
        Cancel = FormDialog.ActionButton("Cancel", () => Close(false)); Cancel.IsCancel = true;
        Actions = DialogKit.ActionColumn(Ok, Cancel);
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(16, 8, 16, 16), Children = { Body } };
        Grid.SetColumn(Actions, 1); root.Children.Add(Actions); Content = root;
    }
    protected virtual bool Validate() => true;
    protected static TextBlock Caption(string text, double width = 0) => new() { Text = Localize.Text(text) + ":", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, TextAlignment = TextAlignment.Right, MinWidth = width, Foreground = Skin.SecondaryBrush };
    protected static StackPanel Line(params Control[] controls) { var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; foreach (var control in controls) { control.VerticalAlignment = VerticalAlignment.Center; line.Children.Add(control); } return line; }
    protected static NumericUpDown Number(double value, double least, double most, double width = 72) => new() { Value = (decimal)value, Minimum = (decimal)least, Maximum = (decimal)most, FormatString = "0.##", ShowButtonSpinner = false, Width = width, Height = 26, MinHeight = 0 };
    /// <summary>A drop-down whose rows can be broken by rules; each real row carries its key in its tag.</summary>
    internal static ComboBox Grouped(IEnumerable<IEnumerable<(string Key, string Label)>> groups, string selected, double width = 0)
    {
        var box = new ComboBox { MinHeight = 0, Height = 26 }; if (width > 0) box.Width = width;
        foreach (var group in groups)
        {
            if (box.Items.Count > 0) box.Items.Add(new ComboBoxItem { IsEnabled = false, Height = 9, MinHeight = 0, Padding = new Thickness(0), Content = new Separator { Margin = new Thickness(0) } });
            foreach (var (key, label) in group) { var row = new ComboBoxItem { Content = Localize.Text(label), Tag = key }; box.Items.Add(row); if (key == selected) box.SelectedItem = row; }
        }
        return box;
    }
    internal static string? Key(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;

    /// <summary>The blending modes in Photoshop's groups, for Fill and Stroke.</summary>
    internal static readonly PaintBlend[][] BlendGroups =
    [
        [PaintBlend.Normal, PaintBlend.Dissolve, PaintBlend.Behind, PaintBlend.Clear],
        [PaintBlend.Darken, PaintBlend.Multiply, PaintBlend.ColorBurn, PaintBlend.LinearBurn, PaintBlend.DarkerColor],
        [PaintBlend.Lighten, PaintBlend.Screen, PaintBlend.ColorDodge, PaintBlend.LinearDodgeAdd, PaintBlend.LighterColor],
        [PaintBlend.Overlay, PaintBlend.SoftLight, PaintBlend.HardLight, PaintBlend.VividLight, PaintBlend.LinearLight, PaintBlend.PinLight, PaintBlend.HardMix],
        [PaintBlend.Difference, PaintBlend.Exclusion, PaintBlend.Subtract, PaintBlend.Divide],
        [PaintBlend.Hue, PaintBlend.Saturation, PaintBlend.Color, PaintBlend.Luminosity],
    ];
    internal static string BlendName(PaintBlend blend) => blend switch
    {
        PaintBlend.ColorBurn => "Color Burn", PaintBlend.LinearBurn => "Linear Burn", PaintBlend.DarkerColor => "Darker Color", PaintBlend.ColorDodge => "Color Dodge",
        PaintBlend.LinearDodgeAdd => "Linear Dodge (Add)", PaintBlend.LighterColor => "Lighter Color", PaintBlend.SoftLight => "Soft Light", PaintBlend.HardLight => "Hard Light",
        PaintBlend.VividLight => "Vivid Light", PaintBlend.LinearLight => "Linear Light", PaintBlend.PinLight => "Pin Light", PaintBlend.HardMix => "Hard Mix", var other => other.ToString(),
    };
    /// <summary>The Blending group Fill and Stroke share: mode, opacity, and Preserve Transparency.</summary>
    protected Control BlendingGroup(out ComboBox mode, out NumericUpDown opacity, out CheckBox preserve)
    {
        mode = Grouped(BlendGroups.Select(g => g.Select(b => (b.ToString(), BlendName(b)))), nameof(PaintBlend.Normal), 200);
        opacity = Number(100, 0, 100); preserve = this.Mnemonic(new CheckBox { Content = Localize.Text("Preserve Transparency"), Tag = "Preserve Transparency" }, 'P');
        var grid = DialogKit.Columns("100,*", 8, Caption("Mode"), mode, Caption("Opacity"), Line(opacity, new TextBlock { Text = "%" }));
        return DialogKit.Group("Blending", new StackPanel { Spacing = 6, Children = { grid, preserve } });
    }
}

/// <summary>Edit ▸ Fill and Edit ▸ Stroke.</summary>
internal sealed class PaintDialog : SideDialog
{
    internal record Choice(string Contents, SKColor Color, double Width, int Location, PaintBlend Mode, double Opacity, bool Preserve);
    private readonly ComboBox _contents, _mode;
    private readonly NumericUpDown _opacity, _width = Number(1, 1, 250);
    private readonly CheckBox _preserve;
    private readonly RadioButton[] _location = new RadioButton[3];
    private readonly Button _swatch;
    private SKColor _color;
    private readonly bool _stroke;
    private readonly Func<bool> _hasPattern;
    internal Func<Task>? LoadPattern { get; init; }

    internal PaintDialog(bool stroke, SKColor color, bool contentAware, bool history, Func<bool> hasPattern) : base(stroke ? "Stroke" : "Fill", stroke ? 560 : 500)
    {
        _stroke = stroke; _color = color; _hasPattern = hasPattern;
        _contents = Grouped([[("Foreground", "Foreground Color"), ("Background", "Background Color"), ("Color", "Color…")], [("ContentAware", "Content-Aware"), ("Pattern", "Pattern"), ("History", "History")], [("Black", "Black"), ("Gray", "50% Gray"), ("White", "White")]], "Foreground", 220);
        foreach (var row in _contents.Items.OfType<ComboBoxItem>())
        {
            if (Equals(row.Tag, "ContentAware") && !contentAware) { row.IsEnabled = false; ToolTip.SetTip(row, Localize.Text("Make a selection on a pixel layer first.")); }
            if (Equals(row.Tag, "History") && !history) { row.IsEnabled = false; ToolTip.SetTip(row, Localize.Text("Set a history brush source in the History panel first.")); }
        }
        _swatch = new Button { Width = 90, Height = 26, MinHeight = 0, Padding = new Thickness(0), BorderThickness = new Thickness(1), BorderBrush = Skin.BorderControlBrush, Tag = "paint-color" };
        _swatch.Click += async (_, _) => await PickColor();
        ShowColor();
        if (stroke)
        {
            var width = Line(_width, new TextBlock { Text = Localize.Text("px") }); _width.Tag = "stroke-width";
            Body.Children.Add(DialogKit.Group("Stroke", DialogKit.Columns("100,*", 8, Caption("Width"), width, Caption("Color"), _swatch)));
            var places = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 28 };
            foreach (var (name, index, key) in new[] { ("Inside", 0, 'I'), ("Center", 1, 'C'), ("Outside", 2, 'U') })
            { _location[index] = this.Mnemonic(new RadioButton { Content = Localize.Text(name), GroupName = "stroke-location", IsChecked = index == 1, Tag = "stroke-" + name }, key); places.Children.Add(_location[index]); }
            Body.Children.Add(DialogKit.Group("Location", places));
        }
        else
        {
            var pattern = FormDialog.ActionButton("Load Pattern…", () => _ = LoadPattern?.Invoke()); pattern.Margin = default; pattern.IsVisible = false;
            var contents = Line(Caption("Contents"), _contents, _swatch, pattern); contents.Margin = new Thickness(60, 4, 0, 6);
            _contents.SelectionChanged += async (_, _) =>
            {
                _swatch.IsVisible = Key(_contents) == "Color"; pattern.IsVisible = Key(_contents) == "Pattern";
                _blending!.IsEnabled = Key(_contents) != "ContentAware";
                if (Key(_contents) == "Color") await PickColor();
            };
            _swatch.IsVisible = false;
            Body.Children.Add(contents);
        }
        Body.Children.Add(_blending = BlendingGroup(out _mode, out _opacity, out _preserve));
        _contents.Tag = "fill-contents"; _mode.Tag = "paint-mode"; _opacity.Tag = "paint-opacity";
    }
    private Control? _blending;
    private void ShowColor() => _swatch.Background = new SolidColorBrush(Color.FromRgb(_color.Red, _color.Green, _color.Blue));
    private async Task PickColor()
    {
        var start = (_color.Red / 255d, _color.Green / 255d, _color.Blue / 255d); var picked = start;
        await ColorPickerDialog.Pick(this, _stroke ? "Color Picker (Stroke Color)" : "Color Picker (Fill Color)", start, value => picked = value);
        _color = new SKColor((byte)Math.Round(picked.Item1 * 255), (byte)Math.Round(picked.Item2 * 255), (byte)Math.Round(picked.Item3 * 255)); ShowColor();
    }
    internal void Select(string contents) => _contents.SelectedItem = _contents.Items.OfType<ComboBoxItem>().First(i => Equals(i.Tag, contents));
    protected override bool Validate() => _stroke || Key(_contents) != "Pattern" || _hasPattern();
    internal Choice Result => new(_stroke ? "Stroke" : Key(_contents) ?? "Foreground", _color, (double)(_width.Value ?? 1), Array.FindIndex(_location, r => r?.IsChecked == true) is var i and >= 0 ? i : 1,
        Enum.Parse<PaintBlend>(Key(_mode) ?? nameof(PaintBlend.Normal)), (double)(_opacity.Value ?? 100) / 100, _preserve.IsChecked == true);
}

/// <summary>The small "Name" dialog Photoshop shows when a pattern, shape, brush or contour is defined, with a picture of it.</summary>
internal sealed class PresetNameDialog : SideDialog
{
    private readonly TextBox _name = DialogKit.Line("preset-name");
    internal string PresetName => _name.Text?.Trim() ?? "";
    internal PresetNameDialog(string title, string name, SKBitmap? thumbnail, string? caption = null) : base(title, 760)
    {
        _name.Text = name; _name.Width = 420;
        var picture = new Border { Width = 96, Height = 96, BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Background = Skin.Checker, Margin = new Thickness(0, 8, 24, 0) };
        if (thumbnail is not null) { var image = CanvasView.ToImage(thumbnail); picture.Child = new Image { Source = image, Stretch = Stretch.Uniform }; Closed += (_, _) => image.Dispose(); }
        var left = new StackPanel { Children = { picture } };
        if (caption is not null) left.Children.Add(new TextBlock { Text = caption, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Skin.SecondaryBrush });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { left, Line(this.Mnemonic(new Label { Content = Localize.Text("Name") + ":", VerticalAlignment = VerticalAlignment.Center }, 'N'), _name) } };
        Body.Children.Add(row);
        Opened += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }
    protected override bool Validate() => PresetName.Length is > 0 and <= 200;
}

/// <summary>Edit ▸ Remove Reflections: whether to keep what was removed as a layer of its own, and how hard to work.</summary>
internal sealed class RemoveReflectionsDialog : SideDialog
{
    internal readonly CheckBox Separate = new() { Content = Localize.Text("Create Separate Reflection Layer"), IsChecked = true, Tag = "reflection-layer" };
    internal readonly ComboBox Quality = BatchDialog.Choice(["Preview", "Standard", "Best"], 0);
    internal RemoveReflectionsDialog() : base("Remove Reflections", 440)
    {
        Quality.Width = 160; Quality.Tag = "reflection-quality";
        Body.Children.Add(Separate); Body.Children.Add(Line(new TextBlock { Text = Localize.Text("Quality"), VerticalAlignment = VerticalAlignment.Center }, Quality));
        Body.Margin = new Thickness(8, 12, 0, 0); Body.Spacing = 14;
    }
    /// <summary>The longest side the picture is sent at for each quality.</summary>
    internal int LongestSide => Quality.SelectedIndex switch { 0 => 1024, 1 => 2048, _ => int.MaxValue };
}

/// <summary>Edit ▸ Auto-Align Layers and Edit ▸ Auto-Blend Layers, with Photoshop's pictures of each choice.</summary>
internal sealed class AutoLayersDialog : SideDialog
{
    private readonly RadioButton[] _choices;
    private readonly TextBlock _about = new() { TextWrapping = TextWrapping.Wrap, Foreground = Skin.SecondaryBrush };
    internal readonly CheckBox Vignette = new() { Content = Localize.Text("Vignette Removal") }, Geometric = new() { Content = Localize.Text("Geometric Distortion") };
    internal readonly CheckBox Seamless = new() { Content = Localize.Text("Seamless Tones and Colors"), IsChecked = true, Tag = "blend-seamless" }, FillTransparent = new() { Content = Localize.Text("Content-Aware Fill Transparent Areas"), IsChecked = true, Tag = "blend-fill" };
    internal int Choice => Array.FindIndex(_choices, r => r.IsChecked == true);
    internal static readonly string[] Projections = ["Auto", "Perspective", "Collage", "Cylindrical", "Spherical", "Reposition"];

    internal AutoLayersDialog(bool blend) : base(blend ? "Auto-Blend Layers" : "Auto-Align Layers", blend ? 380 : 780)
    {
        if (blend)
        {
            _choices = [new RadioButton { Content = Localize.Text("Panorama"), GroupName = "blend", Tag = "blend-panorama" }, new RadioButton { Content = Localize.Text("Stack Images"), GroupName = "blend", IsChecked = true, Tag = "blend-stack" }];
            var methods = new StackPanel { Spacing = 6, Children = { _choices[0], BlendIcon(false), _choices[1], BlendIcon(true) } };
            Body.Children.Add(DialogKit.Group("Blend Method", methods)); Body.Children.Add(Seamless); Body.Children.Add(FillTransparent);
            return;
        }
        _choices = [.. Projections.Select((name, i) => new RadioButton { Content = Localize.Text(name), GroupName = "projection", IsChecked = i == 0, Tag = "projection-" + name })];
        var grid = DialogKit.Columns("*,*,*", 12, [.. _choices.Select(c => (Control)new StackPanel { Spacing = 4, Children = { c, AutomationDialog.LayoutIcon(c.Tag!.ToString()!["projection-".Length..] is var n && n == "Reposition" ? "Reposition" : n) } })]);
        foreach (var (choice, i) in _choices.Select((c, i) => (c, i)))
        {
            if (Projections[i] is not ("Auto" or "Reposition")) { choice.IsEnabled = false; ToolTip.SetTip(choice, Localize.Text("Compositor aligns by moving layers; this projection isn't available.")); }
            choice.IsCheckedChanged += (_, _) => Describe();
        }
        Body.Children.Add(DialogKit.Group(Localize.IsChinese ? "投影" : "Projection", grid));
        Vignette.IsEnabled = Geometric.IsEnabled = false;
        Body.Children.Add(DialogKit.Group("Lens Correction", new StackPanel { Spacing = 6, Children = { Vignette, Geometric } }));
        Body.Children.Add(new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Background = Skin.SurfaceDarkBrush, Padding = new Thickness(8), Height = 120, Child = _about, Margin = new Thickness(0, 10, 0, 0) });
        Describe();
    }
    private void Describe() => _about.Text = Localize.Text(Choice == 5 ? "Moves each layer to line up with the first selected layer, without stretching it." : "Finds the best way to line the layers up: here, by moving each to match the first selected layer.")
        + "\n\n" + Localize.Text("Warning: no camera data was found, so Vignette Removal and Geometric Distortion are unavailable.");
    private static Control BlendIcon(bool stack)
    {
        var canvas = new Canvas { Width = 200, Height = 70, Margin = new Thickness(16, 0, 0, 4) };
        Avalonia.Controls.Shapes.Path Shape(string data, IBrush fill, IBrush? stroke = null) => new() { Data = Geometry.Parse(data), Fill = fill, Stroke = stroke, StrokeThickness = 1.5 };
        if (stack)
        {
            foreach (var dx in new[] { 0, 8, 16 }) canvas.Children.Add(Shape($"M{dx},{dx} h60 v44 h-60 Z", Skin.LabelBrush, Skin.ChromeBrush));
            canvas.Children.Add(Shape("M18,58 L32,42 L42,50 L54,38 L74,58 Z", Skin.ChromeBrush));
            canvas.Children.Add(Shape("M86,30 h14 M96,25 l5,5 l-5,5", Brushes.Transparent, Skin.LabelBrush));
            canvas.Children.Add(Shape("M110,10 h70 v50 h-70 Z", Skin.LabelBrush, Skin.ChromeBrush)); canvas.Children.Add(Shape("M118,58 L136,38 L148,48 L162,34 L178,58 Z", Skin.ChromeBrush));
        }
        else
        {
            foreach (var x in new[] { 0, 60, 120 }) canvas.Children.Add(Shape($"M{x},{(x == 60 ? 10 : 2)} h66 v46 h-66 Z", Skin.LabelBrush, Skin.ChromeBrush));
            canvas.Children.Add(Shape("M66,58 L86,36 L100,46 L112,32 L126,58 Z", Skin.ChromeBrush));
            foreach (var x in new[] { 55, 115 }) canvas.Children.Add(Shape($"M{x},64 h16 M{x + 3},61 l-3,3 l3,3 M{x + 13},61 l3,3 l-3,3", Brushes.Transparent, Skin.LabelBrush));
        }
        return canvas;
    }
}
