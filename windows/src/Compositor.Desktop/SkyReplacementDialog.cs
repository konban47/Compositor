using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Compositor.Core.Document;
using SkiaSharp;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;

namespace Compositor.Desktop;

/// <summary>
/// Edit ▸ Sky Replacement…, laid out as Photoshop's: the sky tools down the left, the sky picker with its library, the edge,
/// sky and foreground sliders, the output and Preview. It stays open beside the document so its Sky Move and Sky Brush tools
/// work on the canvas; it says what changed through <see cref="Changed"/> and the window does the rest.
/// </summary>
internal sealed class SkyReplacementDialog : DialogWindow
{
    internal event Action? Changed;
    internal SkySettings Settings { get; private set; } = new();
    internal SkyEntry? Sky { get; private set; }
    /// <summary>"move", "brush", "hand" or "zoom": what a drag on the canvas does while the dialog is open.</summary>
    internal string Tool { get; private set; } = "move";
    internal bool ShowPreview => _preview.IsChecked == true;
    internal bool DuplicateLayer => SideDialog.Key(_output) == "duplicate";
    internal bool Accepted { get; private set; }
    private readonly Button _skyButton = new() { Width = 180, Height = 104, Padding = new Thickness(2), Tag = "sky-picker" };
    private readonly Popup _library = new() { Placement = PlacementMode.Bottom, IsLightDismissEnabled = true };
    private readonly StackPanel _groups = new() { Spacing = 2 };
    private readonly WrapPanel _recent = new();
    private readonly Slider _thumbnailSize = new() { Minimum = 60, Maximum = 220, Value = 150, Width = 300 };
    private readonly ComboBox _output = SideDialog.Grouped([[("new", "New Layers"), ("duplicate", "Duplicate Layer")]], "new", 250);
    private readonly ComboBox _lighting = SideDialog.Grouped([[("Multiply", "Multiply"), ("Screen", "Screen")]], "Screen", 190);
    private readonly CheckBox _preview = new() { Content = Localize.Text("Preview"), IsChecked = true };
    private readonly CheckBox _flip = new() { Content = Localize.Text("Flip") };
    private readonly Dictionary<string, Avalonia.Media.Imaging.Bitmap> _thumbnails = [];
    private readonly HashSet<string> _closedGroups = [];
    private readonly List<ToggleButton> _toolButtons = [];
    private readonly TextBlock _error = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
    private bool _showRecent = true, _quiet;

    internal SkyReplacementDialog()
    {
        Title = Localize.Text("Sky Replacement"); Width = 460; SizeToContent = SizeToContent.Height; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var tools = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 10, 0) };
        foreach (var (key, tip, data) in new[]
        {
            ("move", "Sky Move Tool (V)", "M12,2 V22 M2,12 H22 M12,2 L9,5 M12,2 L15,5 M12,22 L9,19 M12,22 L15,19 M2,12 L5,9 M2,12 L5,15 M22,12 L19,9 M22,12 L19,15"),
            ("brush", "Sky Brush (B)", "M3,21 C3,17 6,15 8,15 C10,15 11,17 10,19 C9,21 5,22 3,21 Z M10,15 L20,3 L22,5 L12,17"),
            ("hand", "Hand Tool (H)", "M7,12 V5 A1.5,1.5 0 0 1 10,5 V11 M10,10 V3.5 A1.5,1.5 0 0 1 13,3.5 V11 M13,10 V4.5 A1.5,1.5 0 0 1 16,4.5 V12 M16,11 V7 A1.5,1.5 0 0 1 19,7 V15 C19,19 16,22 12,22 C9,22 7,20 5,17 L3,13 A1.5,1.5 0 0 1 6,12 L7,14"),
            ("zoom", "Zoom Tool (Z)", "M10,3 A7,7 0 1 1 9.99,3 Z M15,15 L21,21"),
        })
        {
            var button = new ToggleButton { Width = 36, Height = 34, Padding = new Thickness(6), Tag = "sky-tool-" + key, IsChecked = key == Tool, Content = new AvaloniaPath { Data = Geometry.Parse(data), Stroke = Skin.LabelBrush, StrokeThickness = 1.6, Stretch = Stretch.Uniform } };
            ToolTip.SetTip(button, Localize.Text(tip)); var chosen = key; button.Click += (_, _) => PickTool(chosen); _toolButtons.Add(button); tools.Children.Add(button);
        }
        KeyDown += (_, e) => { if (e.KeyModifiers == KeyModifiers.None && e.Source is not TextBox && e.Key switch { Key.V => "move", Key.B => "brush", Key.H => "hand", Key.Z => "zoom", _ => null } is { } key) { PickTool(key); e.Handled = true; } };

        _skyButton.Click += (_, _) => { FillLibrary(); _library.IsOpen = true; };
        var picker = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { new TextBlock { Text = Localize.Text("Sky") + ":", VerticalAlignment = VerticalAlignment.Center }, new Grid { Children = { _skyButton, _library } } } };
        _library.PlacementTarget = _skyButton; _library.Child = LibraryPanel();
        _thumbnailSize.ValueChanged += (_, _) => FillLibrary();

        var body = new StackPanel { Spacing = 0 };
        body.Children.Add(picker);
        body.Children.Add(SliderRow("Shift Edge", -100, 100, 0, v => Settings = Settings with { ShiftEdge = v }));
        body.Children.Add(SliderRow("Fade Edge", 0, 100, 50, v => Settings = Settings with { FadeEdge = v }));
        _flip.IsCheckedChanged += (_, _) => { Settings = Settings with { Flip = _flip.IsChecked == true }; Raise(); };
        body.Children.Add(Section("Sky Adjustments", SliderRow("Brightness", -100, 100, 0, v => Settings = Settings with { Brightness = v }),
            SliderRow("Temperature", -100, 100, 0, v => Settings = Settings with { Temperature = v }, warm: true),
            SliderRow("Scale", 25, 400, 100, v => Settings = Settings with { Scale = v }), _flip));
        _lighting.SelectionChanged += (_, _) => { Settings = Settings with { Lighting = SideDialog.Key(_lighting) == "Multiply" ? SkyLighting.Multiply : SkyLighting.Screen }; Raise(); };
        body.Children.Add(Section("Foreground Adjustments", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = Localize.Text("Lighting Mode") + ":", VerticalAlignment = VerticalAlignment.Center }, _lighting } },
            SliderRow("Foreground Lighting", 0, 100, 24, v => Settings = Settings with { ForegroundLighting = v }),
            SliderRow("Edge Lighting", 0, 100, 70, v => Settings = Settings with { EdgeLighting = v }),
            SliderRow("Color Adjustment", 0, 100, 35, v => Settings = Settings with { ColorAdjustment = v })));
        var panel = new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Padding = new Thickness(12, 10), Child = body };
        var top = new Grid { ColumnDefinitions = new("Auto,*"), Children = { tools, panel } }; Grid.SetColumn(panel, 1);
        var output = DialogKit.Group("Output", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = Localize.Text("Output To") + ":", VerticalAlignment = VerticalAlignment.Center }, _output } });
        output.Margin = new Thickness(46, 6, 0, 0);
        var ok = FormDialog.ActionButton("OK", () => { Accepted = true; Close(); }); ok.IsDefault = true; ok.Width = 110;
        var cancel = FormDialog.ActionButton("Cancel", Close); cancel.IsCancel = true; cancel.Width = 110;
        _preview.IsCheckedChanged += (_, _) => Raise(); _output.SelectionChanged += (_, _) => Raise();
        var bottom = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(46, 10, 0, 0), Children = { _preview, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { ok, cancel }, [Grid.ColumnProperty] = 1 } } };
        Content = new StackPanel { Margin = new Thickness(12, 10, 12, 14), Children = { top, output, _error, bottom } };
        // The first opening draws the default skies, which takes a moment, so it happens off the interface thread.
        Opened += async (_, _) => { var first = await Task.Run(() => SkyLibrary.Recent().FirstOrDefault() ?? SkyLibrary.All().FirstOrDefault()); if (first is not null && Sky is null) Pick(first, close: false); };
        Closed += (_, _) => { foreach (var image in _thumbnails.Values) image.Dispose(); _thumbnails.Clear(); };
    }

    private void Raise() { if (!_quiet) Changed?.Invoke(); }
    private void PickTool(string key) { Tool = key; foreach (var button in _toolButtons) button.IsChecked = Equals(button.Tag, "sky-tool-" + key); Raise(); }
    internal void Report(string? message) => _error.Text = message is null ? "" : Localize.Text(message);
    internal void SetOffset(double x, double y) => Settings = Settings with { OffsetX = x, OffsetY = y };

    private Control SliderRow(string label, double least, double most, double value, Action<double> set, bool warm = false)
    {
        var number = new NumericUpDown { Minimum = (decimal)least, Maximum = (decimal)most, Value = (decimal)value, FormatString = "0", ShowButtonSpinner = false, Width = 90, Height = 26, MinHeight = 0, Tag = "sky-" + label };
        var slider = new Slider { Minimum = least, Maximum = most, Value = value, Margin = new Thickness(6, -10, 6, -8) };
        number.ValueChanged += (_, _) => { if (number.Value is not { } v || _quiet) return; _quiet = true; slider.Value = (double)v; _quiet = false; set((double)v); Raise(); };
        slider.ValueChanged += (_, _) => { if (_quiet) return; _quiet = true; number.Value = (decimal)Math.Round(slider.Value); _quiet = false; set(Math.Round(slider.Value)); Raise(); };
        Control track = slider;
        if (warm)
        {
            var band = new Border { Height = 3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, -10, 8, -8), Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.FromRgb(0, 0, 0xF0), 0), new GradientStop(Color.FromRgb(0x80, 0x80, 0x80), .5), new GradientStop(Color.FromRgb(0xF0, 0xF0, 0), 1) } } };
            track = new Grid { Children = { band, slider } };
        }
        var head = new Grid { ColumnDefinitions = new("*,Auto"), Children = { new TextBlock { Text = Localize.Text(label), VerticalAlignment = VerticalAlignment.Center }, number } }; Grid.SetColumn(number, 1);
        return new StackPanel { Spacing = 0, Children = { head, track } };
    }
    private static Control Section(string title, params Control[] rows)
    {
        var content = new StackPanel { Spacing = 4, Margin = new Thickness(16, 0, 0, 0) }; foreach (var row in rows) content.Children.Add(row);
        var chevron = new AvaloniaPath { Data = Geometry.Parse("M0,0 L4,4 L8,0"), Stroke = Skin.SecondaryBrush, StrokeThickness = 1.2, Width = 9, Height = 9, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 2), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Children = { chevron, new TextBlock { Text = Localize.Text(title) } } };
        header.PointerPressed += (_, _) => { content.IsVisible = !content.IsVisible; chevron.Data = Geometry.Parse(content.IsVisible ? "M0,0 L4,4 L8,0" : "M0,0 L4,4 L0,8"); };
        return new StackPanel { Children = { header, content } };
    }

    // ---- The library ----
    private Control LibraryPanel()
    {
        var gear = new Button { Background = Brushes.Transparent, Padding = new Thickness(4, 2), HorizontalAlignment = HorizontalAlignment.Right, Tag = "sky-library-menu",
            Content = new AvaloniaPath { Data = Geometry.Parse("M10.3,1 H13.7 L14.2,3.6 A8.5,8.5 0 0 1 16.4,4.9 L18.9,4 L20.6,7 L18.6,8.7 A8.5,8.5 0 0 1 18.6,11.3 L20.6,13 L18.9,16 L16.4,15.1 A8.5,8.5 0 0 1 14.2,16.4 L13.7,19 H10.3 L9.8,16.4 A8.5,8.5 0 0 1 7.6,15.1 L5.1,16 L3.4,13 L5.4,11.3 A8.5,8.5 0 0 1 5.4,8.7 L3.4,7 L5.1,4 L7.6,4.9 A8.5,8.5 0 0 1 9.8,3.6 Z M12,7 A3,3 0 1 0 12.01,7 Z"), Fill = Skin.LabelBrush, Width = 16, Height = 16, Stretch = Stretch.Uniform } };
        gear.Click += (_, _) => GearMenu().Open(gear);
        var scroll = new ScrollViewer { Height = 470, Content = _groups, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Children =
        {
            IconButton("New Sky Group", "M3,6 H9 L11,8 H21 V19 H3 Z", async () => await NewGroup()), IconButton("Import Images", "M4,4 H20 V20 H4 Z M12,8 V16 M8,12 H16", async () => await ImportImages()),
            IconButton("Delete Sky", "M5,6 H19 M9,6 V4 H15 V6 M7,6 L8,21 H16 L17,6", DeleteSky),
        } };
        return new Border { Width = 380, Background = Skin.ChromeBrush, BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Padding = new Thickness(10), Child = new StackPanel { Spacing = 8, Children = { gear, _recent, new Border { BorderBrush = Skin.BorderSubtleBrush, BorderThickness = new Thickness(1), Child = scroll }, footer, _thumbnailSize } } };
    }
    private static Button IconButton(string tip, string data, Action action)
    {
        var button = new Button { Background = Brushes.Transparent, Padding = new Thickness(4), Width = 30, Height = 28, Tag = tip, Content = new AvaloniaPath { Data = Geometry.Parse(data), Stroke = Skin.LabelBrush, StrokeThickness = 1.4, Stretch = Stretch.Uniform } };
        ToolTip.SetTip(button, Localize.Text(tip)); button.Click += (_, _) => action(); return button;
    }
    private ContextMenu GearMenu()
    {
        var menu = new ContextMenu();
        MenuItem Item(string text, Action action) { var item = new MenuItem { Header = Localize.Text(text) }; item.Click += (_, _) => action(); return item; }
        var more = new MenuItem { Header = Localize.Text("Get More Skies") };
        more.Items.Add(Item("Import Images…", async () => await ImportImages())); more.Items.Add(Item("Import Presets…", async () => await ImportPresets()));
        menu.Items.Add(more); menu.Items.Add(Item("Create New Sky Group…", async () => await NewGroup())); menu.Items.Add(new Separator());
        menu.Items.Add(Item("Rename Sky…", async () => await RenameSky())); menu.Items.Add(Item("Delete Sky…", DeleteSky)); menu.Items.Add(new Separator());
        var recent = Item("Show Recently Used", () => { _showRecent = !_showRecent; FillLibrary(); }); recent.ToggleType = MenuItemToggleType.CheckBox; recent.IsChecked = _showRecent; menu.Items.Add(recent); menu.Items.Add(new Separator());
        menu.Items.Add(Item("Append Default Skies…", () => Attempt(() => { SkyLibrary.AppendDefaults(); FillLibrary(); })));
        menu.Items.Add(Item("Export Selected Skies…", async () => await ExportSkies()));
        return menu;
    }
    private void Attempt(Action action) { try { action(); Report(null); } catch (Exception error) { Report(error.Message); } }
    private Avalonia.Media.Imaging.Bitmap? Thumbnail(string path)
    {
        if (_thumbnails.TryGetValue(path, out var image)) return image;
        try
        {
            using var decoded = SKBitmap.Decode(path); if (decoded is null) return null;
            var scale = 240d / Math.Max(decoded.Width, decoded.Height);
            using var small = decoded.Resize(new SKImageInfo(Math.Max(1, (int)(decoded.Width * scale)), Math.Max(1, (int)(decoded.Height * scale))), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            return _thumbnails[path] = CanvasView.ToImage(small!);
        }
        catch (IOException) { return null; }
    }
    private Control Tile(SkyEntry sky, double width)
    {
        var picture = new Image { Source = Thumbnail(sky.Path), Stretch = Stretch.UniformToFill, Width = width, Height = width * .66 };
        var tile = new Button { Padding = new Thickness(1), Margin = new Thickness(2), Content = picture, Tag = "sky-" + sky.Name, BorderBrush = Sky?.Path == sky.Path ? Skin.AccentBrush : Skin.BorderSubtleBrush, BorderThickness = new Thickness(2) };
        ToolTip.SetTip(tile, sky.Name); tile.Click += (_, _) => Pick(sky, close: true); return tile;
    }
    private void FillLibrary()
    {
        _groups.Children.Clear(); _recent.Children.Clear();
        var size = _thumbnailSize.Value;
        _recent.IsVisible = _showRecent; if (_showRecent) foreach (var sky in SkyLibrary.Recent()) _recent.Children.Add(Tile(sky, 60));
        foreach (var group in SkyLibrary.Groups())
        {
            var open = !_closedGroups.Contains(group);
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Background = Skin.SurfaceControlBrush, Cursor = new Cursor(StandardCursorType.Hand), Children =
            {
                new AvaloniaPath { Data = Geometry.Parse(open ? "M0,0 L4,4 L8,0" : "M0,0 L4,4 L0,8"), Stroke = Skin.SecondaryBrush, StrokeThickness = 1.2, Width = 9, Height = 9, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) },
                new AvaloniaPath { Data = Geometry.Parse("M3,6 H9 L11,8 H21 V19 H3 Z"), Stroke = Skin.LabelBrush, StrokeThickness = 1.3, Width = 18, Height = 14, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = SkyLibrary.DefaultGroups.Contains(group) ? Localize.Text(group) : group, Margin = new Thickness(0, 6) },
            } };
            var name = group; header.PointerPressed += (_, _) => { if (!_closedGroups.Remove(name)) _closedGroups.Add(name); FillLibrary(); };
            _groups.Children.Add(header);
            if (!open) continue;
            var tiles = new WrapPanel { Margin = new Thickness(28, 2, 0, 6) }; foreach (var sky in SkyLibrary.Skies(group)) tiles.Children.Add(Tile(sky, size)); _groups.Children.Add(tiles);
        }
    }
    internal void Pick(SkyEntry sky, bool close)
    {
        Sky = sky; SkyLibrary.Used(sky);
        _skyButton.Content = new Image { Source = Thumbnail(sky.Path), Stretch = Stretch.UniformToFill };
        ToolTip.SetTip(_skyButton, sky.Name);
        if (close) _library.IsOpen = false; else if (_library.IsOpen) FillLibrary();
        Raise();
    }
    private string CurrentGroup => Sky?.Group ?? SkyLibrary.Groups().FirstOrDefault() ?? SkyLibrary.DefaultGroups[0];
    private async Task ImportImages()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Import Images"), AllowMultiple = true, FileTypeFilter = [FilePickerFileTypes.ImageAll] });
        SkyEntry? last = null;
        foreach (var file in files) if (file.TryGetLocalPath() is { } path) Attempt(() => last = SkyLibrary.Import(CurrentGroup, path));
        if (last is not null) Pick(last, close: false); FillLibrary();
    }
    /// <summary>Import Presets… reads a .skies file Export Selected Skies wrote: a ZIP of JPEGs, one folder a group.</summary>
    private async Task ImportPresets()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Import Presets"), FileTypeFilter = [new("Compositor Skies") { Patterns = ["*.skies"] }] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        Attempt(() =>
        {
            var folder = Directory.CreateTempSubdirectory("compositor-skies-");
            try
            {
                using var zip = ZipFile.OpenRead(path);
                if (zip.Entries.Count > 500 || zip.Entries.Sum(e => e.Length) > 1L << 30) throw new InvalidDataException("The sky file is too large.");
                foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)))
                {
                    var parts = entry.FullName.Split('/', '\\'); var group = parts.Length > 1 && SkyLibrary.ValidName(parts[^2]) ? parts[^2] : CurrentGroup;
                    var name = SkyLibrary.ValidName(System.IO.Path.GetFileNameWithoutExtension(parts[^1])) ? System.IO.Path.GetFileNameWithoutExtension(parts[^1]) : "Sky";
                    var temp = System.IO.Path.Combine(folder.FullName, Guid.NewGuid().ToString("N"), name + ".jpg"); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(temp)!);
                    entry.ExtractToFile(temp); SkyLibrary.Import(group, temp);
                }
            }
            finally { folder.Delete(true); }
            FillLibrary();
        });
    }
    private async Task ExportSkies()
    {
        if (Sky is not { } sky) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Localize.Text("Export Selected Skies"), SuggestedFileName = sky.Name + ".skies", DefaultExtension = "skies" });
        if (file?.TryGetLocalPath() is not { } path) return;
        Attempt(() => { if (File.Exists(path)) File.Delete(path); using var zip = ZipFile.Open(path, ZipArchiveMode.Create); zip.CreateEntryFromFile(sky.Path, sky.Group + "/" + sky.Name + ".jpg"); });
    }
    private async Task NewGroup()
    {
        var form = new FormDialog("Create New Sky Group", 420, 200); var name = form.Text("Name", Localize.Text("Sky Group"));
        form.Validate = () => SkyLibrary.ValidName((name.Text ?? "").Trim()) ? null : Localize.Text("Enter a name without \\ / : * ? \" < > |.");
        if (await form.ShowDialog<bool>(this)) Attempt(() => { SkyLibrary.CreateGroup(name.Text!.Trim()); FillLibrary(); });
    }
    private async Task RenameSky()
    {
        if (Sky is not { } sky) return;
        var form = new FormDialog("Rename Sky", 420, 200); var name = form.Text("Name", sky.Name);
        form.Validate = () => SkyLibrary.ValidName((name.Text ?? "").Trim()) ? null : Localize.Text("Enter a name without \\ / : * ? \" < > |.");
        if (!await form.ShowDialog<bool>(this)) return;
        Attempt(() => { SkyLibrary.Rename(sky, name.Text!.Trim()); var renamed = SkyLibrary.Skies(sky.Group).First(s => s.Name == name.Text!.Trim()); _thumbnails.Remove(sky.Path); Pick(renamed, close: false); FillLibrary(); });
    }
    private void DeleteSky()
    {
        if (Sky is not { } sky) return;
        Attempt(() => { SkyLibrary.Delete(sky); _thumbnails.Remove(sky.Path); Sky = null; var next = SkyLibrary.All().FirstOrDefault(); if (next is not null) Pick(next, close: false); FillLibrary(); });
    }
}
