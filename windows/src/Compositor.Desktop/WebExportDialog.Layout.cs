using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.IO;
using SkiaSharp;

namespace Compositor.Desktop;

/// <summary>Save for Web as Photoshop lays it out: tools down the left, the tip and the view tabs over the previews, the settings
/// down the right, and the zoom, readout and buttons along the bottom.</summary>
internal sealed partial class WebExportDialog
{
    private enum WebTool { Hand, SliceSelect, Zoom, Eyedropper }
    private WebTool _tool = WebTool.Eyedropper;
    private readonly Dictionary<WebTool, Button> _toolButtons = [];
    private readonly Button[] _tabs = new Button[4];
    private readonly Border _pickedSwatch = new() { Width = 24, Height = 24, Background = Brushes.Black, BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1) };
    private SKColor _picked = SKColors.Black;
    private bool _showSlices = true;
    private int _selectedPane = 1;
    private double _bitsPerSecond = 56600;
    private readonly Border _matteSwatch = new() { Width = 62, Height = 22, Background = Brushes.White, BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1) };
    private readonly TextBlock _tableCount = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Skin.SecondaryBrush, Width = 44 };
    private readonly Control[] _indexedOnly = new Control[8];
    private readonly StackPanel _qualityFields = new() { Spacing = 5 };
    private readonly NumericUpDown _lossy = Number(0, 0, 100), _alphaAmount = Number(100, 0, 100);
    private readonly ComboBox _display = Choice(["Monitor Color", "Legacy Macintosh (No Color Management)", "Internet Standard RGB (No Color Management)", "Use Document Profile"]);
    private readonly ComboBox _preset = Choice([.. WebPresets.Select(p => p.Name), "[Unnamed]"], WebPresets.Length);
    private bool _applyingPreset;
    private ScrollViewer? _settings;
    /// <summary>What the self check reads: true when the settings fit without scrolling.</summary>
    internal bool SettingsFit => _settings is null || _settings.Extent.Height <= _settings.Viewport.Height + 1;
    /// <summary>What the Original pane calls the document.</summary>
    internal string DocumentName { get; init; } = Localize.Text("Untitled");

    /// <summary>The named settings in the Preset menu.</summary>
    private static readonly (string Name, Func<WebExportOptions, WebExportOptions> Apply)[] WebPresets =
    [
        ("GIF 128 Dithered", o => o with { Format = WebFormat.Gif, Palette = WebPalette.Selective, Colors = 128, Dither = WebDither.Diffusion, DitherAmount = 88, Transparency = true }),
        ("GIF 128 No Dither", o => o with { Format = WebFormat.Gif, Palette = WebPalette.Selective, Colors = 128, Dither = WebDither.None, Transparency = true }),
        ("GIF 32 Dithered", o => o with { Format = WebFormat.Gif, Palette = WebPalette.Selective, Colors = 32, Dither = WebDither.Diffusion, DitherAmount = 100, Transparency = true }),
        ("GIF 32 No Dither", o => o with { Format = WebFormat.Gif, Palette = WebPalette.Selective, Colors = 32, Dither = WebDither.None, Transparency = true }),
        ("GIF 64 Dithered", o => o with { Format = WebFormat.Gif, Palette = WebPalette.Selective, Colors = 64, Dither = WebDither.Diffusion, DitherAmount = 100, Transparency = true }),
        ("GIF 64 No Dither", o => o with { Format = WebFormat.Gif, Palette = WebPalette.Selective, Colors = 64, Dither = WebDither.None, Transparency = true }),
        ("GIF Restrictive", o => o with { Format = WebFormat.Gif, Palette = WebPalette.WebSafe, Colors = 256, Dither = WebDither.Diffusion, DitherAmount = 100, Transparency = true }),
        ("JPEG High", o => o with { Format = WebFormat.Jpeg, Quality = 60 }),
        ("JPEG Low", o => o with { Format = WebFormat.Jpeg, Quality = 10 }),
        ("JPEG Medium", o => o with { Format = WebFormat.Jpeg, Quality = 30 }),
        ("PNG-24", o => o with { Format = WebFormat.Png24, Transparency = true }),
        ("PNG-8 128 Dithered", o => o with { Format = WebFormat.Png8, Palette = WebPalette.Selective, Colors = 128, Dither = WebDither.Diffusion, DitherAmount = 88, Transparency = true }),
    ];
    private static readonly WebPalette[] Palettes = [WebPalette.Perceptual, WebPalette.Selective, WebPalette.Adaptive, WebPalette.WebSafe, WebPalette.Custom, WebPalette.BlackWhite, WebPalette.Grayscale, WebPalette.MacOS, WebPalette.Windows];
    private static readonly (string Name, double Bits)[] Bandwidths =
    [
        ("9600 bps Modem", 9600), ("14.4 Kbps Modem", 14400), ("28.8 Kbps Modem", 28800), ("56.6 Kbps Modem/ISDN", 56600), ("128 Kbps Dual ISDN", 128000), ("256 Kbps Cable/DSL", 256000),
        ("384 Kbps Cable/DSL", 384000), ("512 Kbps Cable/DSL", 512000), ("768 Kbps Cable/DSL", 768000), ("1 Mbps Cable", 1000000), ("1.5 Mbps Cable/T1", 1500000), ("2 Mbps", 2000000),
    ];

    private static Button IconButton(Control icon, string tip, Action action, double size = 22)
    {
        var button = new Button { Content = icon, Width = size, Height = size, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(button, Localize.Text(tip)); button.Click += (_, _) => action(); return button;
    }
    private static Control Menu3() => new ChromeGlyph("≡") { Width = 18, Height = 18, IsHitTestVisible = false };
    private static MenuItem Item(string text, Action action, bool enabled = true, bool chosen = false)
    {
        var item = new MenuItem { Header = Localize.Text(text), IsEnabled = enabled, ToggleType = chosen ? MenuItemToggleType.CheckBox : MenuItemToggleType.None, IsChecked = chosen, Tag = text };
        item.Click += (_, _) => action(); return item;
    }
    /// <summary>A number with the small arrow beside it that opens what it can be set to.</summary>
    private static Control Spin(NumericUpDown field, Func<Control> popup)
    {
        field.Width = 56; field.Height = 24; field.MinHeight = 0;
        var arrow = new Button { Content = new ChromeGlyph("⌄") { Width = 12, Height = 12, IsHitTestVisible = false }, Width = 18, Height = 24, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        arrow.Flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        arrow.Click += (_, _) => { if (arrow.Flyout is Flyout flyout) flyout.Content = popup(); };
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { field, arrow } };
    }
    private static Control Slide(NumericUpDown field)
    {
        var slider = new Slider { Minimum = (double)field.Minimum, Maximum = (double)field.Maximum, Value = (double)(field.Value ?? 0), Width = 150 };
        slider.ValueChanged += (_, e) => field.Value = (decimal)Math.Round(e.NewValue);
        return slider;
    }
    private Control ColorCounts()
    {
        var list = new StackPanel();
        foreach (var count in new[] { 2, 4, 8, 16, 32, 64, 128, 256 })
        {
            var row = new Button { Content = count.ToString(CultureInfo.InvariantCulture), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            row.Click += (_, _) => _colors.Value = count; list.Children.Add(row);
        }
        return list;
    }

    private void BuildLayout()
    {
        var root = new Grid { RowDefinitions = new("Auto,*,Auto"), ColumnDefinitions = new("Auto,*,440"), Margin = new Thickness(10, 4, 10, 10), ColumnSpacing = 8 };
        var tools = new StackPanel { Spacing = 4, Width = 36 };
        foreach (var (tool, icon, name) in new[] { (WebTool.Hand, "Pan", "Hand Tool"), (WebTool.SliceSelect, "SliceSelect", "Slice Select Tool"), (WebTool.Zoom, "Zoom", "Zoom Tool"), (WebTool.Eyedropper, "Eyedropper", "Eyedropper Tool") })
        {
            var button = IconButton(new EditorIcon(icon) { Width = 20, Height = 20, IsHitTestVisible = false }, name, () => SetWebTool(tool), 34); button.Tag = "web-tool-" + tool;
            _toolButtons[tool] = button; tools.Children.Add(button);
        }
        tools.Children.Add(new Border { Height = 1, Background = Skin.BorderControlBrush, Margin = new Thickness(0, 4) });
        ToolTip.SetTip(_pickedSwatch, Localize.Text("Eyedropper Color"));
        tools.Children.Add(new Border { HorizontalAlignment = HorizontalAlignment.Center, Child = _pickedSwatch });
        tools.Children.Add(IconButton(new EditorIcon("slice-show") { Width = 20, Height = 20, IsHitTestVisible = false }, "Toggle Slices Visibility", () => { _showSlices = !_showSlices; ShowPreviews(); }, 34));
        Grid.SetRowSpan(tools, 2); Grid.SetRow(tools, 1); root.Children.Add(tools);
        SetWebTool(_tool);

        var hint = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 8, Margin = new Thickness(0, 4, 0, 6) };
        hint.Children.Add(new TextBlock { Text = "ⓘ", FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
        var tip = new TextBlock { Text = Localize.Text("Tip: use File > Export > Export As… or right-click a layer to quickly export assets"), VerticalAlignment = VerticalAlignment.Center, Foreground = Skin.SecondaryBrush, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(tip, 1); hint.Children.Add(tip);
        var learn = FormDialog.ActionButton("Learn More", () => _ = LearnMore()); learn.Margin = default; Grid.SetColumn(learn, 2); hint.Children.Add(learn);
        Grid.SetColumn(hint, 1); Grid.SetColumnSpan(hint, 2); root.Children.Add(hint);

        var preview = new Grid { RowDefinitions = new("Auto,*,Auto") };
        var tabStrip = new StackPanel { Orientation = Orientation.Horizontal, Background = Skin.TabStripBrush };
        foreach (var (label, index) in new[] { "Original", "Optimized", "2-Up", "4-Up" }.Select((label, index) => (label, index)))
        {
            var tab = new Button { Content = Localize.Text(label), MinWidth = 88, Height = 34, CornerRadius = new CornerRadius(0), BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center, Tag = "web-view-" + label };
            tab.Click += (_, _) => _view.SelectedIndex = index; _tabs[index] = tab; tabStrip.Children.Add(tab);
        }
        preview.Children.Add(tabStrip); _previews.Margin = new Thickness(0, 4); Grid.SetRow(_previews, 1); preview.Children.Add(_previews);

        var zoomMenu = new Button { Content = new ChromeGlyph("⌄") { Width = 12, Height = 12, IsHitTestVisible = false }, Width = 20, Height = 26, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Tag = "web-zoom-menu" };
        zoomMenu.Click += (_, _) => ZoomMenu().Open(zoomMenu);
        _zoom.Width = 64; _zoom.Height = 26; _zoom.MinHeight = 0; _zoom.Tag = "web-zoom";
        _sample.Text = Readout(null, -1); _sample.Foreground = Skin.SecondaryBrush;
        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            Children =
            {
                IconButton(new TextBlock { Text = "−", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }, "Zoom Out", () => _zoom.Value = Math.Max(6, (_zoom.Value ?? 100) / 1.25m), 24),
                IconButton(new TextBlock { Text = "+", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }, "Zoom In", () => _zoom.Value = Math.Min(1600, (_zoom.Value ?? 100) * 1.25m), 24),
                _zoom, zoomMenu, new Border { Width = 1, Background = Skin.BorderControlBrush, Margin = new Thickness(4, 2) }, _sample,
            },
        };
        Grid.SetRow(bottom, 2); preview.Children.Add(bottom);
        Grid.SetRow(preview, 1); Grid.SetColumn(preview, 1); root.Children.Add(preview);

        var settings = _settings = new ScrollViewer { Content = SettingsColumn(), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(settings, 1); Grid.SetColumn(settings, 2); root.Children.Add(settings);

        var previewButton = FormDialog.ActionButton("Preview…", () => _ = PreviewInBrowser()); previewButton.Margin = default;
        var browsers = new Button { Content = new ChromeGlyph("⌄") { Width = 12, Height = 12, IsHitTestVisible = false }, Width = 24, Height = 32, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Tag = "web-browsers" };
        browsers.Click += (_, _) => { var menu = new ContextMenu(); menu.Items.Add(Item("Default Browser", () => _ = PreviewInBrowser(), true, true)); menu.Open(browsers); };
        var footer = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 12, Margin = new Thickness(0, 8, 0, 0) };
        footer.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { previewButton, browsers } });
        _status.VerticalAlignment = VerticalAlignment.Center; _status.TextWrapping = TextWrapping.NoWrap; _status.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(_status, 1); footer.Children.Add(_status);
        var cancel = FormDialog.ActionButton("Cancel", Close); var done = FormDialog.ActionButton("Done", () => { _remembered = Options(); Close(); });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _save, cancel, done } }; foreach (var button in buttons.Children) button.Margin = default;
        Grid.SetColumn(buttons, 2); footer.Children.Add(buttons);
        Grid.SetRow(footer, 2); Grid.SetColumn(footer, 1); Grid.SetColumnSpan(footer, 2); root.Children.Add(footer);
        Content = root;
        ShowTabs();
    }

    private void SetWebTool(WebTool tool)
    {
        _tool = tool;
        foreach (var (key, button) in _toolButtons) button.Background = key == tool ? Skin.SurfaceControlPressedBrush : Brushes.Transparent;
    }
    private void ShowTabs()
    {
        for (var i = 0; i < _tabs.Length; i++)
        {
            var chosen = _view.SelectedIndex == i;
            _tabs[i].Background = chosen ? Skin.ChromeBrush : Brushes.Transparent;
            _tabs[i].Foreground = chosen ? Skin.LabelBrush : Skin.SecondaryBrush;
        }
    }
    private ContextMenu ZoomMenu()
    {
        var menu = new ContextMenu();
        void Sizes(params int[] values) { foreach (var value in values) menu.Items.Add(Item(value.ToString(CultureInfo.InvariantCulture) + "%", () => _zoom.Value = value)); }
        Sizes(6, 12, 25, 33, 50, 66); menu.Items.Add(new Separator()); Sizes(100); menu.Items.Add(new Separator()); Sizes(200, 300, 400, 600, 800, 1000, 1200, 1600); menu.Items.Add(new Separator());
        menu.Items.Add(Item("Actual Pixels", () => _zoom.Value = 100));
        menu.Items.Add(Item("Fit in View", () => FitZoom(false)));
        menu.Items.Add(Item("Fit on Screen", () => FitZoom(true)));
        return menu;
    }
    private void FitZoom(bool screen)
    {
        var bitmap = _results.FirstOrDefault()?.Preview ?? _original; if (bitmap is null) return;
        var rows = _view.SelectedIndex == 3 ? 4 : _view.SelectedIndex == 2 ? 2 : 1;
        var fit = Math.Min((_previews.Bounds.Width - 36) / bitmap.Width, (_previews.Bounds.Height / rows - 90) / bitmap.Height);
        _zoom.Value = (decimal)Math.Clamp((screen ? fit : Math.Min(1, fit)) * 100, 6, 1600);
    }
    private static string Readout(SKColor? c, int index) => string.Format(CultureInfo.InvariantCulture, "R: {0}    G: {1}    B: {2}    Alpha: {3}    {4}: {5}    {6}: {7}",
        c?.Red.ToString(CultureInfo.InvariantCulture) ?? "--", c?.Green.ToString(CultureInfo.InvariantCulture) ?? "--", c?.Blue.ToString(CultureInfo.InvariantCulture) ?? "--", c?.Alpha.ToString(CultureInfo.InvariantCulture) ?? "--",
        Localize.Text("Hex"), c is { } s ? $"{s.Red:X2}{s.Green:X2}{s.Blue:X2}" : "--", Localize.Text("Index"), index >= 0 ? index.ToString(CultureInfo.InvariantCulture) : "--");

    /// <summary>The settings down the right: preset, format and what it offers, then the color table, the size and the animation.</summary>
    private Control SettingsColumn()
    {
        var column = new StackPanel { Spacing = 6 };
        var presetMenu = IconButton(Menu3(), "Optimize Menu", () => { }, 24); presetMenu.Tag = "web-optimize-menu";
        presetMenu.Click += (_, _) => OptimizeMenu().Open(presetMenu);
        var preset = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 8, Children = { DialogKit.Label("Preset") } };
        Grid.SetColumn(_preset, 1); preset.Children.Add(_preset); Grid.SetColumn(presetMenu, 2); preset.Children.Add(presetMenu);
        _preset.SelectionChanged += (_, _) => { if (!_applyingPreset && _preset.SelectedIndex is var i && i >= 0 && i < WebPresets.Length) ApplyPreset(i); };

        _format.Width = 200; _palette.MinHeight = 0;
        Control Labeled(string label, Control field) { var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6, Children = { DialogKit.Label(label, HorizontalAlignment.Right) } }; Grid.SetColumn(field, 1); row.Children.Add(field); return row; }
        var matte = new Button { Content = _matteSwatch, Padding = new Thickness(0), Width = 62, Height = 24, MinWidth = 0, MinHeight = 0, BorderThickness = new Thickness(0), Tag = "web-matte" };
        matte.Click += (_, _) => _ = PickMatte();
        var matteArrow = new Button { Content = new ChromeGlyph("⌄") { Width = 12, Height = 12, IsHitTestVisible = false }, Width = 18, Height = 24, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        matteArrow.Click += (_, _) => MatteMenu().Open(matteArrow);
        var left = new StackPanel { Spacing = 5, Children = { _palette, _dither, _transparent, _alphaDither, _interlaced } };
        _indexedOnly[0] = Labeled("Colors", Spin(_colors, ColorCounts)); _indexedOnly[1] = Labeled("Dither", Spin(_amount, () => Slide(_amount)));
        _indexedOnly[2] = Labeled("Amount", Spin(_alphaAmount, () => Slide(_alphaAmount))); _indexedOnly[3] = Labeled("Web Snap", Spin(_snap, () => Slide(_snap))); _indexedOnly[4] = Labeled("Lossy", Spin(_lossy, () => Slide(_lossy)));
        var right = new StackPanel
        {
            Spacing = 5, Width = 170,
            Children = { _indexedOnly[0], _indexedOnly[1], Labeled("Matte", new StackPanel { Orientation = Orientation.Horizontal, Children = { matte, matteArrow } }), _indexedOnly[2], _indexedOnly[3], _indexedOnly[4], _qualityFields },
        };
        _qualityFields.Children.Add(Labeled("Quality", Spin(_quality, () => Slide(_quality))));
        var fields = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12, Children = { left } }; Grid.SetColumn(right, 1); fields.Children.Add(right);
        var format = DialogKit.Group("", fields, _format);
        var view = new StackPanel { Spacing = 5, Children = { _srgb, DialogKit.Row("Preview", _display, 66), DialogKit.Row("Metadata", _metadata, 66) } };
        column.Children.Add(DialogKit.Group("", new StackPanel { Spacing = 6, Children = { format, view } }, preset));
        preset.Width = 380;

        var tableMenu = IconButton(Menu3(), "Color Table Menu", () => { }, 22); tableMenu.Tag = "web-table-menu";
        tableMenu.Click += (_, _) => TableMenu().Open(tableMenu);
        var heading = new Grid { Width = 380, ColumnDefinitions = new("*,Auto"), Children = { new TextBlock { Text = Localize.Text("Color Table"), VerticalAlignment = VerticalAlignment.Center } } };
        Grid.SetColumn(tableMenu, 1); heading.Children.Add(tableMenu);
        var table = new ScrollViewer { Content = _swatches, Height = 126, Width = 16 * 18 + 16, Background = Skin.SurfaceDarkBrush, HorizontalAlignment = HorizontalAlignment.Center, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Children = { _tableCount } };
        foreach (var (icon, tip, action) in new (string, string, Action)[]
        {
            ("transparency", "Maps selected colors to transparent", () => ChangePaletteColor(1)), ("web-cube", "Shifts/unshifts selected colors to Web palette", () => ChangePaletteColor(2)), ("lock", "Locks selected colors to prevent dropping", () => ChangePaletteColor(0)),
            ("page-new", "Adds the Eyedropper Color to the palette", () => _ = NewColor()), ("delete", "Deletes all selected colors", DeleteColor),
        }) tools.Children.Add(IconButton(new InspectorGlyph(icon) { Width = 18, Height = 18, IsHitTestVisible = false }, tip, action));
        column.Children.Add(DialogKit.Group("", new StackPanel { Spacing = 4, Children = { table, tools } }, heading));

        var link = new Button { Content = new EditorIcon("link") { Width = 16, Height = 16, IsHitTestVisible = false }, Width = 24, Height = 48, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Tag = "web-link" };
        ToolTip.SetTip(link, Localize.Text("Constrain Proportions")); link.Click += (_, _) => { _linked.IsChecked = _linked.IsChecked != true; link.Opacity = _linked.IsChecked == true ? 1 : .4; };
        Control Size(string name, NumericUpDown field) { field.Width = 84; field.Height = 24; field.MinHeight = 0; return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new TextBlock { Text = name, Width = 16, VerticalAlignment = VerticalAlignment.Center, Foreground = Skin.SecondaryBrush }, field, new TextBlock { Text = Localize.Text("Pixels"), VerticalAlignment = VerticalAlignment.Center, Foreground = Skin.SecondaryBrush } } }; }
        _percent.Width = 76; _percent.Height = 24; _percent.MinHeight = 0; _resample.Width = 120;
        var size = new Grid
        {
            ColumnDefinitions = new("Auto,Auto,*"), ColumnSpacing = 8,
            Children =
            {
                new StackPanel { Spacing = 4, Children = { Size("W:", _width), Size("H:", _height) } },
                Placed(link, 1),
                Placed(new StackPanel { Spacing = 4, Children = { Labeled("Percent", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _percent, new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center } } }), Labeled("Quality", _resample) } }, 2),
            },
        };
        column.Children.Add(DialogKit.Group("Image Size", size));

        var frames = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right };
        frames.Children.Add(_frameLabel);
        foreach (var (glyph, tip, step) in new[] { ("⏮", "Select First Frame", int.MinValue), ("◀", "Select Previous Frame", -1), ("▶", "Play Animation", 1), ("⏭", "Select Next Frame", 1), ("⏭", "Select Last Frame", int.MaxValue) })
        {
            var button = FormDialog.ActionButton(glyph, () => StepFrame(step)); button.MinWidth = 0; button.Width = 44; button.MinHeight = 0; button.Height = 24; button.Margin = default; button.Padding = new Thickness(0); ToolTip.SetTip(button, Localize.Text(tip));
            _frameButtons.Add(button); frames.Children.Add(button);
        }
        _loops.Width = 60; _loops.Height = 24; _loops.MinHeight = 0;
        var loop = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 6, Children = { DialogKit.Label("Looping Options"), Placed(_loop, 1), Placed(_loops, 2) } };
        column.Children.Add(DialogKit.Group("Animation", new StackPanel { Spacing = 4, Children = { loop, frames } }));
        return column;
    }
    private static Control Placed(Control control, int column) { Grid.SetColumn(control, column); return control; }

    private void ApplyPreset(int index)
    {
        _applyingPreset = true;
        var options = WebPresets[index].Apply(Options()) with { Width = (int)(_width.Value ?? 1), Height = (int)(_height.Value ?? 1) };
        Apply(options); _applyingPreset = false; Queue();
    }
    private ContextMenu MatteMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("None", () => { _transparent.IsChecked = true; SetMatte(SKColors.White); }));
        menu.Items.Add(Item("Eyedropper Color", () => SetMatte(_picked)));
        menu.Items.Add(Item("White", () => SetMatte(SKColors.White)));
        menu.Items.Add(Item("Black", () => SetMatte(SKColors.Black)));
        menu.Items.Add(Item("Other…", () => _ = PickMatte()));
        return menu;
    }
    private void SetMatte(SKColor color) { _matte.Text = $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}"; _matteSwatch.Background = new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue)); }
    private async Task PickMatte()
    {
        SKColor.TryParse(_matte.Text, out var color); var chosen = (color.Red / 255d, color.Green / 255d, color.Blue / 255d); var picked = chosen;
        await ColorPickerDialog.Pick(this, "Color Picker (Matte Color)", chosen, moved => picked = moved);
        SetMatte(new SKColor((byte)Math.Round(picked.Item1 * 255), (byte)Math.Round(picked.Item2 * 255), (byte)Math.Round(picked.Item3 * 255)));
    }
    private ContextMenu OptimizeMenu()
    {
        var menu = new ContextMenu();
        var slices = _lease.Document.Marks.Any(m => m.Kind == Compositor.Core.Format.MarkKind.Slice && m.Visible);
        menu.Items.Add(Item("Save Settings…", () => _ = SaveSettings()));
        menu.Items.Add(Item("Load Settings…", () => _ = LoadSettings()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Optimize to File Size…", () => _ = Optimize()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Repopulate Views", Queue, _view.SelectedIndex == 3));
        menu.Items.Add(new Separator());
        foreach (var (name, index) in new[] { "Images Only", "HTML and Images", "HTML Only" }.Select((name, index) => (name, index))) menu.Items.Add(Item(name, () => _saveAs.SelectedIndex = index, true, _saveAs.SelectedIndex == index));
        menu.Items.Add(new Separator());
        foreach (var (name, index) in new[] { "All Slices", "All User Slices" }.Select((name, index) => (name, index))) menu.Items.Add(Item(name, () => _scope.SelectedIndex = index, slices, _scope.SelectedIndex == index));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Edit Output Settings…", () => _ = EditOutput()));
        return menu;
    }
    private async Task LearnMore()
    {
        try { if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher) await launcher.LaunchUriAsync(new Uri("https://github.com/konban47/Compositor/blob/windows-port/windows/docs/README.md")); }
        catch (Exception error) { _status.Text = error.Message; }
    }
    private async Task PreviewInBrowser()
    {
        if (_results.Length == 0) return;
        try
        {
            var result = _results[0]; var folder = Directory.CreateTempSubdirectory("compositor-web-").FullName;
            var image = Path.Combine(folder, "preview." + result.Options.Extension); await File.WriteAllBytesAsync(image, result.Bytes);
            var page = Path.Combine(folder, "preview.html");
            await File.WriteAllTextAsync(page, $"<!doctype html><meta charset=\"utf-8\"><title>{Title}</title><body style=\"margin:16px;background:#fff;font:13px sans-serif\"><img src=\"{Path.GetFileName(image)}\"><p>{result.Options.Width} × {result.Options.Height} · {SizeText(result.Bytes.Length)}</p>");
            if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher) await launcher.LaunchUriAsync(new Uri(page));
        }
        catch (Exception error) { _status.Text = error.Message; }
    }
    private static string SizeText(long bytes) => bytes < 1024 ? bytes.ToString(CultureInfo.InvariantCulture) + " " + Localize.Text("bytes") : (bytes / 1024d).ToString("0.###", CultureInfo.InvariantCulture) + "K";

    private SKBitmap Displayed(SKBitmap bitmap)
    {
        if (_display.SelectedIndex != 1) return bitmap;
        // An old Mac's display gamma of 1.8 shown on this one, which is what the choice previews.
        var table = new byte[256]; for (var i = 0; i < 256; i++) table[i] = (byte)Math.Round(Math.Pow(i / 255d, 1.8 / 2.2) * 255);
        var copy = bitmap.Copy(); var pixels = copy.Pixels;
        for (var i = 0; i < pixels.Length; i++) { var c = pixels[i]; pixels[i] = new SKColor(table[c.Red], table[c.Green], table[c.Blue], c.Alpha); }
        copy.Pixels = pixels; return copy;
    }

    /// <summary>One pane: the picture with its slice mark on the dark ground, and what it is under it.</summary>
    private void ShowPreviews()
    {
        if (_closed) return;
        ShowTabs();
        _previews.Children.Clear(); foreach (var image in _images) image.Dispose(); _images.Clear();
        var count = _view.SelectedIndex == 3 ? 4 : _view.SelectedIndex == 2 ? 2 : 1;
        _previews.RowDefinitions = new(string.Join(',', Enumerable.Repeat("*", count)));
        var zoom = (double)(_zoom.Value ?? 100) / 100;
        for (var i = 0; i < count; i++)
        {
            var resultIndex = count == 1 ? (_view.SelectedIndex == 0 ? -1 : 0) : Math.Min(i - 1, _results.Length - 1);
            var result = resultIndex >= 0 && resultIndex < _results.Length ? _results[resultIndex] : null;
            var bitmap = resultIndex < 0 ? _original : result?.Preview; if (bitmap is null) continue;
            var shown = Displayed(bitmap); var image = CanvasView.ToImage(shown); if (!ReferenceEquals(shown, bitmap)) shown.Dispose(); _images.Add(image);
            var picture = new Image { Source = image, Width = bitmap.Width * zoom, Height = bitmap.Height * zoom, Stretch = Stretch.Fill };
            var frame = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Background = Skin.Checker, Children = { picture } };
            if (_showSlices)
            {
                frame.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xA0, 0x20)), BorderThickness = new Thickness(1), IsHitTestVisible = false });
                frame.Children.Add(new Border { Background = new SolidColorBrush(Colors.Gray, .8), Padding = new Thickness(4, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Child = new TextBlock { Text = "01", FontWeight = FontWeight.Bold, FontSize = 11, Foreground = Brushes.White } });
            }
            var scroll = new ScrollViewer { Content = frame, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Skin.SurfaceDarkBrush };
            scroll.PointerPressed += (_, e) => { if (_tool == WebTool.Hand && e.GetCurrentPoint(scroll).Properties.IsLeftButtonPressed) { _pan = e.GetPosition(scroll); _panOffset = scroll.Offset; e.Pointer.Capture(scroll); } };
            scroll.PointerMoved += (_, e) => { if (_pan is { } start) scroll.Offset = _panOffset - (e.GetPosition(scroll) - start); };
            scroll.PointerReleased += (_, e) => { _pan = null; e.Pointer.Capture(null); };
            SKColor? Under(Avalonia.Input.PointerEventArgs e)
            {
                var p = e.GetPosition(picture); var x = (int)(p.X / picture.Width * bitmap.Width); var y = (int)(p.Y / picture.Height * bitmap.Height);
                return x >= 0 && y >= 0 && x < bitmap.Width && y < bitmap.Height ? bitmap.GetPixel(x, y) : null;
            }
            picture.PointerMoved += (_, e) => { if (Under(e) is { } c) _sample.Text = Readout(c, result is null ? -1 : Array.IndexOf(_displayPalette, (uint)c)); };
            picture.PointerExited += (_, _) => _sample.Text = Readout(null, -1);
            var pane = i; var selectable = result is not null;
            picture.PointerPressed += (_, e) =>
            {
                if (_tool == WebTool.Zoom) { _zoom.Value = Math.Clamp((_zoom.Value ?? 100) * (e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? .5m : 2m), 6, 1600); e.Handled = true; return; }
                if (_tool == WebTool.Eyedropper && Under(e) is { } c)
                {
                    _picked = c; _pickedSwatch.Background = new SolidColorBrush(Color.FromRgb(c.Red, c.Green, c.Blue));
                    var at = Array.IndexOf(_displayPalette, (uint)c); if (at >= 0) { _selected.Clear(); _selected.Add((uint)c); ShowPalette(); }
                }
                if (selectable && _selectedPane != pane) { _selectedPane = pane; if (count > 1) { Apply(result!.Options); Queue(); } }
            };
            var cell = new Grid { RowDefinitions = new("*,Auto"), Margin = new Thickness(2) }; cell.Children.Add(scroll);
            var caption = result is null ? OriginalCaption() : ResultCaption(result);
            Grid.SetRow(caption, 1); cell.Children.Add(caption);
            var outline = new Border { Child = cell, BorderThickness = new Thickness(1), BorderBrush = count == 1 || i == _selectedPane ? Skin.SecondaryBrush : Skin.BorderControlBrush }; Grid.SetRow(outline, i);
            _previews.Children.Add(outline);
        }
    }
    private Control OriginalCaption() => new TextBlock
    {
        Margin = new Thickness(8, 6), MinHeight = 48, Foreground = Skin.SecondaryBrush,
        Text = string.Format(CultureInfo.InvariantCulture, "{0}: “{1}”\n{2}", Localize.Text("Original"), DocumentName, _original is null ? "" : SizeText(_original.ByteCount)),
    };
    private Control ResultCaption(WebExportResult result)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(result.Bytes.Length * 8 / _bitsPerSecond));
        var speed = Array.Find(Bandwidths, b => b.Bits == _bitsPerSecond).Name ?? "";
        var menu = IconButton(new StackPanel { Orientation = Orientation.Horizontal, IsHitTestVisible = false, Children = { new ChromeGlyph("⌄") { Width = 8, Height = 12 }, Menu3() } }, "Preview Menu", () => { }, 28); menu.Tag = "web-speed-menu";
        menu.Click += (_, _) =>
        {
            var list = new ContextMenu();
            foreach (var (name, bits) in Bandwidths) list.Items.Add(Item(string.Format(CultureInfo.InvariantCulture, Localize.Text("Size/Download Time ({0})"), Localize.Text(name)), () => { _bitsPerSecond = bits; ShowPreviews(); }, true, bits == _bitsPerSecond));
            list.Open(menu);
        };
        var left = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = result.Options.Format switch { WebFormat.Png8 => "PNG-8", WebFormat.Png24 => "PNG-24", var f => f.ToString().ToUpperInvariant() } },
                new TextBlock { Text = SizeText(result.Bytes.Length) },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = string.Format(CultureInfo.InvariantCulture, Localize.Text("{0} sec @ {1}"), seconds, speed.Split(' ')[0] + " " + speed.Split(' ')[1]), VerticalAlignment = VerticalAlignment.Center }, menu } },
            },
        };
        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        if (result.Options.Indexed)
        {
            right.Children.Add(new TextBlock { Text = string.Format(CultureInfo.InvariantCulture, Localize.Text("{0}% dither"), result.Options.Dither == WebDither.None ? 0 : result.Options.DitherAmount), HorizontalAlignment = HorizontalAlignment.Right });
            right.Children.Add(new TextBlock { Text = string.Format(CultureInfo.InvariantCulture, Localize.Text("“{0}” palette"), Localize.Text(PaletteName(result.Options.Palette))), HorizontalAlignment = HorizontalAlignment.Right });
            right.Children.Add(new TextBlock { Text = string.Format(CultureInfo.InvariantCulture, Localize.Text("{0} colors"), result.Palette.Length), HorizontalAlignment = HorizontalAlignment.Right });
        }
        else if (result.Options.Format == WebFormat.Jpeg) right.Children.Add(new TextBlock { Text = Localize.Text("Quality") + $": {result.Options.Quality}", HorizontalAlignment = HorizontalAlignment.Right });
        var caption = new Grid { ColumnDefinitions = new("*,*"), Margin = new Thickness(8, 4), MinHeight = 48, Children = { left } };
        Grid.SetColumn(right, 1); caption.Children.Add(right);
        Avalonia.Controls.Documents.TextElement.SetForeground(caption, Skin.SecondaryBrush);
        return caption;
    }
    private static string PaletteName(WebPalette palette) => palette switch
    {
        WebPalette.Perceptual => "Perceptual", WebPalette.Selective => "Selective", WebPalette.Adaptive => "Adaptive", WebPalette.WebSafe => "Restrictive", WebPalette.BlackWhite => "Black - White",
        WebPalette.Grayscale => "Grayscale", WebPalette.MacOS => "Mac OS", WebPalette.Windows => "Windows", _ => "Custom",
    };

    /// <summary>The Optimize to File Size dialog. Starting from the automatic choice picks GIF for few colors and JPEG for many.</summary>
    private async Task Optimize()
    {
        if (_rendering || _closed) return;
        var dialog = new FormDialog("Optimize To File Size", 460, 330); dialog.SideActions();
        var size = AutomationDialog.Number(32, 1, 1024000, "Desired File Size"); size.Width = 120; size.FormatString = "0";
        dialog.Fields.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { DialogKit.Label("Desired File Size"), size, new TextBlock { Text = "K", VerticalAlignment = VerticalAlignment.Center } } });
        var current = new RadioButton { Content = Localize.Text("Current Settings"), GroupName = "web-start", IsChecked = true }; var automatic = new RadioButton { Content = Localize.Text("Auto Select GIF/JPEG"), GroupName = "web-start" };
        dialog.Fields.Children.Add(DialogKit.Group("Start With", new StackPanel { Children = { current, automatic } }));
        var slices = _lease.Document.Marks.Any(m => m.Kind == Compositor.Core.Format.MarkKind.Slice && m.Visible);
        var use = new StackPanel { Children = { new RadioButton { Content = Localize.Text("Current Slice"), GroupName = "web-use", IsChecked = true }, new RadioButton { Content = Localize.Text("Each Slice"), GroupName = "web-use", IsEnabled = false }, new RadioButton { Content = Localize.Text("Total of All Slices"), GroupName = "web-use", IsEnabled = false } } };
        dialog.Fields.Children.Add(DialogKit.Group("Use", use)); use.IsEnabled = !slices;
        if (!await dialog.ShowDialog<bool>(this) || _closed) return;
        if (automatic.IsChecked == true && _original is { } picture)
        {
            var colors = new HashSet<SKColor>(); foreach (var pixel in picture.Pixels) { colors.Add(pixel); if (colors.Count > 256) break; }
            _changing = true; _format.SelectedIndex = (int)(colors.Count > 256 ? WebFormat.Jpeg : WebFormat.Gif); _changing = false;
        }
        _timer.Stop(); _revision++; _save.IsEnabled = false; _status.Text = Localize.Text("Rendering preview…");
        await RenderPreview((long)(size.Value ?? 32) * 1024);
    }
}
