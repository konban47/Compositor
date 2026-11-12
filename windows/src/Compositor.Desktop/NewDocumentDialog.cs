using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;

namespace Compositor.Desktop;

/// <summary>What File ▸ New asks for.</summary>
internal sealed record NewDocumentRequest(string Name, int Width, int Height, double Resolution, ImageMode Mode, int Depth, int Background, uint BackgroundColor,
    string? Profile, bool Artboard);

/// <summary>A size worth starting from: in pixels, inches, centimeters or millimeters (unit 0–3), at a resolution per inch.</summary>
internal sealed record DocumentPreset(string Name, double Width, double Height, int Unit, double Resolution, string Kind = "new", ImageMode Mode = ImageMode.RGB, int Depth = 8, int Background = 0);

/// <summary>
/// File ▸ New, laid out as Photoshop's New Document: the categories along the top (Recent, Saved, Photo, Print, Art &amp;
/// Illustration, Web, Mobile, Film &amp; Video), their presets as cards, a search across them at the foot, and on the right
/// the Preset Details — name and Save Preset, Width with its unit, Height, Orientation, Artboards, Resolution with its unit,
/// Color Mode and bit depth, Background Contents with its swatch, and under Advanced Options the color profile and pixel
/// aspect ratio — with Create and Close.
/// </summary>
internal sealed class NewDocumentDialog : DialogWindow
{
    private static readonly string[] Units = ["Pixels", "Inches", "Centimeters", "Millimeters", "Points", "Picas"];
    private static readonly double[] PerInch = [0, 1, 2.54, 25.4, 72, 6];
    private static readonly string[] Categories = ["Recent", "Saved", "Photo", "Print", "Art & Illustration", "Web", "Mobile", "Film & Video"];
    internal static readonly Dictionary<string, DocumentPreset[]> Presets = new()
    {
        ["Photo"] = [new("Landscape, 4 x 6", 6, 4, 1, 300), new("Portrait, 4 x 6", 4, 6, 1, 300), new("Landscape, 5 x 7", 7, 5, 1, 300), new("Portrait, 5 x 7", 5, 7, 1, 300),
            new("Landscape, 8 x 10", 10, 8, 1, 300), new("Portrait, 8 x 10", 8, 10, 1, 300), new("Square, 5 x 5", 5, 5, 1, 300)],
        ["Print"] = [new("Letter", 8.5, 11, 1, 300), new("Legal", 8.5, 14, 1, 300), new("Tabloid", 11, 17, 1, 300), new("A3", 297, 420, 3, 300), new("A4", 210, 297, 3, 300),
            new("A5", 148, 210, 3, 300), new("B5", 176, 250, 3, 300)],
        ["Art & Illustration"] = [new("Poster", 18, 24, 1, 300), new("Postcard", 6, 4, 1, 300), new("Comic Book", 6.625, 10.25, 1, 300), new("Square Canvas", 3000, 3000, 0, 300),
            new("1920 x 1080 Screen", 1920, 1080, 0, 72)],
        ["Web"] = [new("Web Most Common", 1366, 768, 0, 72), new("Web Large", 1920, 1080, 0, 72), new("Web Medium", 1440, 900, 0, 72), new("Web Minimum", 1024, 768, 0, 72),
            new("MacBook Pro 13", 2560, 1600, 0, 72), new("Surface Pro", 2736, 1824, 0, 72), new("iMac 27", 5120, 2880, 0, 72)],
        ["Mobile"] = [new("iPhone 15 Pro Max", 1290, 2796, 0, 72), new("iPhone 15 Pro", 1179, 2556, 0, 72), new("iPhone 8", 750, 1334, 0, 72), new("iPad Pro 12.9", 2048, 2732, 0, 72),
            new("iPad", 1620, 2160, 0, 72), new("Android 1080p", 1080, 1920, 0, 72), new("Android Tablet", 1280, 800, 0, 72), new("Apple Watch 45mm", 396, 484, 0, 72)],
        ["Film & Video"] = [new("HDTV 1080p", 1920, 1080, 0, 72), new("HDV/HDTV 720p", 1280, 720, 0, 72), new("UHD 4K", 3840, 2160, 0, 72), new("UHD 8K", 7680, 4320, 0, 72),
            new("Film 2K", 2048, 1556, 0, 72), new("Film 4K", 4096, 3112, 0, 72)],
    };
    private static string RecentPath => Path.Combine(AppPaths.SettingsDirectory, "recent-document-sizes.json");
    private static string SavedPath => Path.Combine(AppPaths.SettingsDirectory, "document-presets.json");

    private readonly (int Width, int Height)? _clipboard;
    private readonly List<Button> _tabs = [];
    private readonly WrapPanel _cards = new() { Tag = "preset-cards" };
    private readonly TextBlock _heading = new() { FontSize = 13, Margin = new Thickness(2, 0, 0, 14) };
    private readonly TextBox _search = new() { Width = 400, BorderThickness = new Thickness(0, 0, 0, 1), Background = Brushes.Transparent, Tag = "preset-search", FontStyle = FontStyle.Italic };
    private int _category;
    private Border? _selectedCard;
    private bool _syncing;
    private double _pixelsWide = 794, _pixelsHigh = 594;
    private uint _customColor = 0xffffffff;
    internal NewDocumentRequest? Result { get; private set; }
    internal TextBox NameBox { get; } = new() { Text = "", Tag = "document-name", BorderThickness = new Thickness(0, 0, 0, 1), Background = Brushes.Transparent, Padding = new Thickness(2, 4) };
    internal ValueBox WidthBox { get; } = new(.001, 300000, 794, "0.###", tag: "document-width") { Width = 106, Height = 36 };
    internal ValueBox HeightBox { get; } = new(.001, 300000, 594, "0.###", tag: "document-height") { Width = 106, Height = 36 };
    internal ValueBox ResolutionBox { get; } = new(1, 9600, 72, "0.##", tag: "document-resolution") { Width = 106, Height = 36 };
    internal ComboBox UnitBox { get; } = new() { Height = 36, HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "document-unit" };
    internal ComboBox ResolutionUnit { get; } = new() { Height = 36, HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "document-resolution-unit" };
    internal ComboBox ModeBox { get; } = new() { Height = 36, HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "document-mode" };
    internal ComboBox DepthBox { get; } = new() { Height = 36, Width = 104, Tag = "document-depth" };
    internal ComboBox BackgroundBox { get; } = new() { Height = 36, HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "document-background" };
    internal ComboBox ProfileBox { get; } = new() { Height = 36, HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "document-profile", MaxDropDownHeight = 420 };
    internal ComboBox AspectBox { get; } = new() { Height = 36, HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "document-aspect" };
    internal CheckBox ArtboardBox { get; } = new() { Tag = "document-artboard" };
    private readonly ToggleButton _portrait = new() { Width = 34, Height = 36, Padding = new Thickness(3), Tag = "portrait" }, _landscape = new() { Width = 34, Height = 36, Padding = new Thickness(3), Tag = "landscape" };
    private readonly Button _swatch = new() { Width = 36, Height = 36, Padding = default, BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Tag = "background-swatch", Background = Brushes.White };
    private readonly List<string?> _profiles = [];

    internal NewDocumentDialog((int Width, int Height)? clipboard = null, string? name = null)
    {
        _clipboard = clipboard;
        Title = Localize.Text("New Document"); Width = 1290; Height = 860; MinWidth = 1000; MinHeight = 700; CanResize = true; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        NameBox.Text = name ?? Localize.Text("Untitled");
        // The categories along the top, the active one underlined.
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(24, 0, 0, 0) };
        for (var i = 0; i < Categories.Length; i++)
        {
            var index = i;
            var label = new TextBlock { Text = Localize.Text(Categories[i]), FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
            Control content = i == 0 ? new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new IconGlyph("history") { Width = 18, Height = 18 }, label } } : label;
            var tab = new Button { Content = content, Background = Brushes.Transparent, BorderThickness = new Thickness(0, 0, 0, 2), BorderBrush = Brushes.Transparent, Padding = new Thickness(10, 14, 10, 12), CornerRadius = default, Tag = "category " + Categories[i] };
            tab.Click += (_, _) => { _search.Text = ""; ShowCategory(index); };
            _tabs.Add(tab); strip.Children.Add(tab);
        }
        var top = new Border { Background = Skin.ChromeBrush, BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(0, 0, 0, 1), Child = strip };

        // The search at the foot looks through every category's presets.
        _search.PlaceholderText = Localize.Text("Find presets by name");
        var go = FormDialog.ActionButton("Go", () => Search()); go.Tag = "search-go"; go.CornerRadius = new CornerRadius(18); go.MinWidth = 100; go.Height = 44;
        _search.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Search(); e.Handled = true; } };
        var searchRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Children = { new DocumentPresetGlyph("search") { Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center }, _search, go } };
        var searchBar = new Border { Background = Skin.ChromeBrush, Height = 86, Margin = new Thickness(0, 12, 0, 0), Child = searchRow };
        var cardsScroll = new ScrollViewer { Content = _cards, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var left = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new Thickness(28, 26, 28, 18), Children = { _heading, cardsScroll, searchBar } };
        Grid.SetRow(cardsScroll, 1); Grid.SetRow(searchBar, 2);

        var right = DetailsPanel();
        var body = new Grid { ColumnDefinitions = new("*,380") };
        body.Children.Add(new Border { Background = Skin.SurfaceDarkBrush, Child = left });
        Grid.SetColumn(right, 1); body.Children.Add(right);
        var root = new Grid { RowDefinitions = new("Auto,*"), Children = { top, body }, Tag = "new-document" }; Grid.SetRow(body, 1);
        Content = root;
        ShowCategory(0);
        Opened += (_, _) => { WidthBox.Focus(); WidthBox.SelectAll(); };
    }

    /// <summary>The Preset Details column.</summary>
    private Control DetailsPanel()
    {
        TextBlock Label(string text) => new() { Text = Localize.Text(text), Foreground = Skin.SecondaryBrush, Margin = new Thickness(0, 12, 0, 6) };
        UnitBox.ItemsSource = Units.Select(Localize.Text).ToArray(); UnitBox.SelectedIndex = 0;
        ResolutionUnit.ItemsSource = new[] { "Pixels/Inch", "Pixels/Centimeter" }.Select(Localize.Text).ToArray(); ResolutionUnit.SelectedIndex = 0;
        ModeBox.ItemsSource = new[] { "Bitmap", "Grayscale", "RGB Color", "CMYK Color", "Lab Color" }.Select(Localize.Text).ToArray(); ModeBox.SelectedIndex = 2;
        BackgroundBox.ItemsSource = new[] { "White", "Black", "Background Color", "Transparent", "Custom…" }.Select(Localize.Text).ToArray(); BackgroundBox.SelectedIndex = 0;
        // Compositor draws square pixels; the other pixel aspect ratios are listed as Photoshop lists them, but greyed.
        var aspects = new[] { "Square Pixels", "D1/DV NTSC (0.91)", "D1/DV PAL (1.09)", "D1/DV NTSC Widescreen (1.21)", "HDV 1080/DVCPRO HD 720 (1.33)", "D1/DV PAL Widescreen (1.46)", "Anamorphic 2:1 (2)", "DVCPRO HD 1080 (1.5)" };
        AspectBox.ItemsSource = aspects.Select((a, i) => (object)new ComboBoxItem { Content = Localize.Text(a), IsEnabled = i == 0 }).ToArray(); AspectBox.SelectedIndex = 0;
        ToolTip.SetTip(AspectBox, Localize.Text("Compositor shows and edits square pixels only."));
        _portrait.Content = new OrientationGlyph(false); _landscape.Content = new OrientationGlyph(true);
        ToolTip.SetTip(_portrait, Localize.Text("Portrait")); ToolTip.SetTip(_landscape, Localize.Text("Landscape"));
        _portrait.Click += (_, _) => Orient(false); _landscape.Click += (_, _) => Orient(true);
        WidthBox.ValueChanged += () => { if (_syncing) return; _pixelsWide = ToPixels(WidthBox.Value); Refresh(); };
        HeightBox.ValueChanged += () => { if (_syncing) return; _pixelsHigh = ToPixels(HeightBox.Value); Refresh(); };
        ResolutionBox.ValueChanged += () => { if (_syncing) return; Refresh(true); };
        UnitBox.SelectionChanged += (_, _) => { if (!_syncing) Refresh(); };
        ResolutionUnit.SelectionChanged += (_, _) => { if (_syncing) return; _syncing = true; ResolutionBox.Value = Math.Round(PerInchResolution * (ResolutionUnit.SelectedIndex == 1 ? 1 / 2.54 : 1), 2); _syncing = false; };
        ModeBox.SelectionChanged += (_, _) => { Depths(); Profiles(); };
        BackgroundBox.SelectionChanged += async (_, _) => { if (BackgroundBox.SelectedIndex == 4) await PickCustom(); Paint(); };
        _swatch.Click += async (_, _) => { BackgroundBox.SelectedIndex = 4; await PickCustom(); Paint(); };
        Depths(); Profiles(); Paint();
        ArtboardBox.Content = null;
        var save = new Button { Content = new DocumentPresetGlyph("save") { Width = 22, Height = 22 }, Width = 36, Height = 34, Padding = default, Background = Brushes.Transparent, BorderThickness = default, Tag = "save-document-preset" };
        ToolTip.SetTip(save, Localize.Text("Save Document Preset")); save.Click += async (_, _) => await SavePreset();
        var nameRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8, Children = { NameBox } }; Grid.SetColumn(save, 1); nameRow.Children.Add(save);
        Grid Pair(Control first, Control second, string columns = "106,*") { var g = new Grid { ColumnDefinitions = new(columns), ColumnSpacing = 18, Children = { first } }; Grid.SetColumn(second, 1); g.Children.Add(second); return g; }
        var heightRow = new Grid { ColumnDefinitions = new("106,Auto,Auto"), ColumnSpacing = 18 };
        heightRow.Children.Add(new StackPanel { Children = { Label("Height"), HeightBox } });
        var orientation = new StackPanel { Children = { Label("Orientation"), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _portrait, _landscape } } } };
        Grid.SetColumn(orientation, 1); heightRow.Children.Add(orientation);
        var artboards = new StackPanel { Children = { Label("Artboards"), ArtboardBox } }; ArtboardBox.Margin = new Thickness(4, 6, 0, 0);
        Grid.SetColumn(artboards, 2); heightRow.Children.Add(artboards);
        var advancedBody = new StackPanel { Children = { Label("Color Profile"), ProfileBox, Label("Pixel Aspect Ratio"), AspectBox } };
        var advancedArrow = new TextBlock { Text = "⌄", Width = 14 };
        var advanced = new Button { Background = Brushes.Transparent, BorderThickness = default, Padding = new Thickness(0, 8), Tag = "advanced-options",
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { advancedArrow, new TextBlock { Text = Localize.Text("Advanced Options") } } } };
        advanced.Click += (_, _) => { advancedBody.IsVisible = !advancedBody.IsVisible; advancedArrow.Text = advancedBody.IsVisible ? "⌄" : "›"; };
        var details = new StackPanel
        {
            Margin = new Thickness(28, 26, 28, 0),
            Children =
            {
                new TextBlock { Text = Localize.Text("Preset Details"), Foreground = Skin.SecondaryBrush, Margin = new Thickness(0, 0, 0, 14) }, nameRow,
                Label("Width"), Pair(WidthBox, UnitBox), heightRow,
                Label("Resolution"), Pair(ResolutionBox, ResolutionUnit),
                Label("Color Mode"), Pair(ModeBox, DepthBox, "*,Auto"),
                Label("Background Contents"), Pair(BackgroundBox, _swatch, "*,Auto"),
                advanced, advancedBody,
            },
        };
        var create = FormDialog.ActionButton("Create", Create); create.IsDefault = true; create.Tag = "Create"; create.MinWidth = 110; create.Height = 46; create.CornerRadius = new CornerRadius(23);
        create.Background = Skin.AccentBrush; create.Foreground = Brushes.White; create.BorderBrush = Skin.AccentBrush;
        var close = FormDialog.ActionButton("Close", () => Close()); close.IsCancel = true; close.Tag = "Close"; close.MinWidth = 110; close.Height = 46; close.CornerRadius = new CornerRadius(23);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 22, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 28, 30), Children = { create, close } };
        var scroll = new ScrollViewer { Content = details, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var column = new Grid { RowDefinitions = new("*,Auto"), Background = Skin.ChromeBrush, Children = { scroll, buttons } }; Grid.SetRow(buttons, 1);
        return column;
    }

    private double PerInchResolution => Math.Max(1, ResolutionBox.Value * (ResolutionUnit.SelectedIndex == 1 ? 2.54 : 1));
    private double ToPixels(double value) => UnitBox.SelectedIndex == 0 ? value : value / PerInch[UnitBox.SelectedIndex] * PerInchResolution;
    private double FromPixels(double pixels) => UnitBox.SelectedIndex == 0 ? Math.Round(pixels) : pixels / PerInchResolution * PerInch[UnitBox.SelectedIndex];

    /// <summary>Shows the size in the chosen unit; a new resolution keeps a print size and changes the pixels, as Photoshop does.</summary>
    private void Refresh(bool resolutionChanged = false)
    {
        _syncing = true;
        if (resolutionChanged && UnitBox.SelectedIndex != 0) { _pixelsWide = ToPixels(WidthBox.Value); _pixelsHigh = ToPixels(HeightBox.Value); }
        WidthBox.Value = Math.Round(FromPixels(_pixelsWide), 3); HeightBox.Value = Math.Round(FromPixels(_pixelsHigh), 3);
        _portrait.IsChecked = _pixelsHigh >= _pixelsWide; _landscape.IsChecked = _pixelsWide > _pixelsHigh;
        _syncing = false;
    }

    private void Orient(bool landscape)
    {
        if (landscape == _pixelsWide > _pixelsHigh || _pixelsWide == _pixelsHigh) { Refresh(); return; }
        (_pixelsWide, _pixelsHigh) = (_pixelsHigh, _pixelsWide); Refresh();
    }

    /// <summary>The bit depths each mode offers: Bitmap is 1 bit; CMYK and Lab stop at 16.</summary>
    private void Depths()
    {
        var mode = ModeBox.SelectedIndex; var before = DepthBox.SelectedItem as string;
        string[] depths = mode == 0 ? ["1 bit"] : mode is 3 or 4 ? ["8 bit", "16 bit"] : ["8 bit", "16 bit", "32 bit"];
        DepthBox.ItemsSource = depths; DepthBox.SelectedIndex = Math.Max(0, Array.IndexOf(depths, before));
    }

    /// <summary>Working profile, Don't Color Manage, then every installed profile of the mode's kind, as Photoshop lists them.</summary>
    private void Profiles()
    {
        var settings = ColorDefaults.Settings; var mode = ModeBox.SelectedIndex;
        var (model, working, label) = mode == 3 ? ("CMYK", settings.Cmyk, "Working CMYK") : mode is 0 or 1 ? ("GRAY", settings.Gray, "Working Gray") : ("RGB", settings.Rgb, "Working RGB");
        _profiles.Clear(); var names = new List<string>();
        _profiles.Add(null); names.Add($"{Localize.Text(label)}: {working}");
        _profiles.Add(""); names.Add(Localize.Text("Don't Color Manage"));
        if (mode != 4) foreach (var entry in IccProfiles.Catalog(settings, model)) { _profiles.Add(entry.Name); names.Add(entry.Name); }
        ProfileBox.ItemsSource = names; ProfileBox.SelectedIndex = 0;
    }

    private async Task PickCustom()
    {
        var c = Color.FromUInt32(_customColor);
        await ColorPickerDialog.Pick(this, "Color Picker (Custom Background Color)", (c.R / 255.0, c.G / 255.0, c.B / 255.0), moved =>
        { _customColor = 0xff000000u | (uint)Math.Round(moved.Red * 255) << 16 | (uint)Math.Round(moved.Green * 255) << 8 | (uint)Math.Round(moved.Blue * 255); Paint(); });
    }

    private uint BackgroundColor => BackgroundBox.SelectedIndex switch { 1 => 0xff000000, 2 => EditorBackground, 3 => 0x00000000, 4 => _customColor, _ => 0xffffffff };
    internal static uint EditorBackground { get; set; } = 0xffffffff;
    private void Paint() => _swatch.Background = BackgroundBox.SelectedIndex == 3 ? Skin.Checker : new SolidColorBrush(Color.FromUInt32(BackgroundColor));

    // ------------------------------------------------------------------------------------------------ Cards

    private IEnumerable<DocumentPreset> Recent()
    {
        var list = new List<DocumentPreset>();
        if (_clipboard is { } clip) list.Add(new("Clipboard", clip.Width, clip.Height, 0, EditorPreferences.Current.ScreenResolutionPerInch, "clipboard"));
        try { if (File.Exists(RecentPath) && JsonSerializer.Deserialize<List<DocumentPreset>>(File.ReadAllText(RecentPath)) is { } saved) list.AddRange(saved.Where(Valid).Take(20)); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        list.Add(new("Default Photoshop Size", 16, 12, 2, 300, "image"));
        return list;
    }
    private static IEnumerable<DocumentPreset> Saved()
    {
        try { if (File.Exists(SavedPath) && JsonSerializer.Deserialize<List<DocumentPreset>>(File.ReadAllText(SavedPath)) is { } saved) return saved.Where(Valid).Take(200); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        return [];
    }
    private static bool Valid(DocumentPreset p) => p is not null && p.Name is { Length: > 0 and <= 200 } && double.IsFinite(p.Width) && double.IsFinite(p.Height) && p.Width > 0 && p.Height > 0
        && p.Unit is >= 0 and <= 3 && p.Resolution is >= 1 and <= 9600 && Enum.IsDefined(p.Mode);

    private void ShowCategory(int index)
    {
        _category = index;
        for (var i = 0; i < _tabs.Count; i++) { _tabs[i].BorderBrush = i == index ? Skin.LabelBrush : Brushes.Transparent; _tabs[i].Foreground = i == index ? Skin.LabelBrush : Skin.SecondaryBrush; }
        var presets = index switch { 0 => Recent().ToList(), 1 => Saved().ToList(), _ => Presets[Categories[index]].ToList() };
        _heading.Text = index switch
        {
            0 => $"{Localize.Text("Your Recent Items")} ({presets.Count})",
            1 => $"{Localize.Text("Saved Blank Document Presets")} ({presets.Count})",
            _ => $"{Localize.Text("Blank Document Presets")} ({presets.Count})",
        };
        Fill(presets);
    }

    private void Search()
    {
        var text = _search.Text?.Trim() ?? ""; if (text.Length == 0) { ShowCategory(_category); return; }
        var found = Presets.Values.SelectMany(p => p).Concat(Saved()).Concat(Recent())
            .Where(p => p.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase) || Localize.Text(p.Name).Contains(text, StringComparison.CurrentCultureIgnoreCase)).DistinctBy(p => (p.Name, p.Width, p.Height)).ToList();
        _heading.Text = $"{Localize.Text("Search Results")} ({found.Count})";
        Fill(found);
    }

    private void Fill(IReadOnlyList<DocumentPreset> presets)
    {
        _cards.Children.Clear(); _selectedCard = null;
        foreach (var preset in presets) _cards.Children.Add(Card(preset));
        if (_cards.Children.FirstOrDefault() is Border first) Choose(first, (DocumentPreset)first.Tag!);
    }

    private static string Describe(DocumentPreset p)
    {
        string Number(double v) => v.ToString(p.Unit == 0 ? "0" : "0.###", CultureInfo.CurrentCulture);
        return $"{Number(p.Width)} x {Number(p.Height)} {Localize.Text(Units[p.Unit])} @ {p.Resolution.ToString("0.##", CultureInfo.CurrentCulture)} ppi";
    }

    private Border Card(DocumentPreset preset)
    {
        var name = new TextBlock { Text = preset.Kind == "new" && preset.Name == "Custom" ? Localize.Text("Custom") : Localize.Text(preset.Name), HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var size = new TextBlock { Text = Describe(preset), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12, Foreground = Skin.SecondaryBrush, TextTrimming = TextTrimming.CharacterEllipsis };
        var card = new Border
        {
            Width = 190, Height = 206, Margin = new Thickness(0, 0, 14, 14), CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(3), BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent, Tag = preset, Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 8, Margin = new Thickness(10), Children = { new DocumentPresetGlyph(preset.Kind) { Width = 84, Height = 84, HorizontalAlignment = HorizontalAlignment.Center }, name, size } },
        };
        card.PointerEntered += (_, _) => { if (!ReferenceEquals(card, _selectedCard)) card.Background = Skin.SurfaceControlBrush; name.Foreground = Skin.AccentBrush; };
        card.PointerExited += (_, _) => { if (!ReferenceEquals(card, _selectedCard)) card.Background = Brushes.Transparent; name.Foreground = Skin.LabelBrush; };
        card.PointerPressed += (_, e) => { Choose(card, preset); if (e.ClickCount == 2) Create(); };
        return card;
    }

    /// <summary>Chooses the card of that name in the category shown, as a click on it does.</summary>
    internal void ChooseCard(string name)
    {
        if (_cards.Children.OfType<Border>().FirstOrDefault(c => c.Tag is DocumentPreset p && p.Name == name) is { } card) Choose(card, (DocumentPreset)card.Tag!);
    }

    /// <summary>A card chosen: its details fill the column on the right.</summary>
    private void Choose(Border card, DocumentPreset preset)
    {
        if (_selectedCard is { } previous) { previous.BorderBrush = Brushes.Transparent; previous.Background = Brushes.Transparent; }
        _selectedCard = card; card.BorderBrush = Skin.AccentBrush;
        _syncing = true;
        UnitBox.SelectedIndex = preset.Unit; ResolutionUnit.SelectedIndex = 0; ResolutionBox.Value = preset.Resolution;
        _pixelsWide = preset.Unit == 0 ? preset.Width : preset.Width / PerInch[preset.Unit] * preset.Resolution;
        _pixelsHigh = preset.Unit == 0 ? preset.Height : preset.Height / PerInch[preset.Unit] * preset.Resolution;
        ModeBox.SelectedIndex = preset.Mode switch { ImageMode.Bitmap => 0, ImageMode.Grayscale => 1, ImageMode.CMYK => 3, ImageMode.Lab => 4, _ => 2 };
        _syncing = false; Depths();
        DepthBox.SelectedIndex = Math.Max(0, (DepthBox.ItemsSource as string[] ?? []).ToList().IndexOf(preset.Depth + " bit"));
        BackgroundBox.SelectedIndex = Math.Clamp(preset.Background, 0, 3);
        Refresh();
    }

    private async Task SavePreset()
    {
        var ask = new FormDialog("Save Document Preset", 420, 200); var name = ask.Text("Name", NameBox.Text ?? Localize.Text("Custom"));
        ask.Validate = () => string.IsNullOrWhiteSpace(name.Text) || name.Text.Length > 200 ? "Enter a name." : null;
        if (!await ask.ShowDialog<bool>(this)) return;
        var saved = Saved().ToList(); saved.RemoveAll(p => p.Name == name.Text!.Trim());
        saved.Add(Current(name.Text!.Trim()));
        try { Directory.CreateDirectory(AppPaths.SettingsDirectory); File.WriteAllText(SavedPath, JsonSerializer.Serialize(saved)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        ShowCategory(1);
    }

    private ImageMode Mode => ModeBox.SelectedIndex switch { 0 => ImageMode.Bitmap, 1 => ImageMode.Grayscale, 3 => ImageMode.CMYK, 4 => ImageMode.Lab, _ => ImageMode.RGB };
    private int Depth => Mode == ImageMode.Bitmap ? 8 : int.Parse((DepthBox.SelectedItem as string ?? "8 bit").Split(' ')[0], CultureInfo.InvariantCulture);

    private DocumentPreset Current(string name) =>
        new(name, UnitBox.SelectedIndex is >= 0 and <= 3 ? Math.Round(WidthBox.Value, 3) : Math.Round(_pixelsWide), UnitBox.SelectedIndex is >= 0 and <= 3 ? Math.Round(HeightBox.Value, 3) : Math.Round(_pixelsHigh),
            UnitBox.SelectedIndex is >= 0 and <= 3 ? UnitBox.SelectedIndex : 0, Math.Round(PerInchResolution, 3), "new", Mode, Depth, Math.Min(3, BackgroundBox.SelectedIndex));

    private void Create()
    {
        WidthBox.Commit(); HeightBox.Commit(); ResolutionBox.Commit();
        var width = (int)Math.Round(_pixelsWide); var height = (int)Math.Round(_pixelsHigh);
        if (width < 1 || height < 1 || width > DocumentLimits.MaxSide || height > DocumentLimits.MaxSide || (long)width * height > DocumentLimits.MaxSurfacePixels) return;
        Result = new NewDocumentRequest(string.IsNullOrWhiteSpace(NameBox.Text) ? Localize.Text("Untitled") : NameBox.Text.Trim(), width, height, Math.Round(PerInchResolution, 3), Mode, Depth,
            BackgroundBox.SelectedIndex, BackgroundColor, ProfileBox.SelectedIndex >= 0 && ProfileBox.SelectedIndex < _profiles.Count ? _profiles[ProfileBox.SelectedIndex] : null, ArtboardBox.IsChecked == true);
        // The size goes to the front of Recent, as Photoshop keeps it.
        try
        {
            var recent = Recent().Where(p => p.Kind == "new").ToList(); var made = Current("Custom");
            recent.RemoveAll(p => p.Width == made.Width && p.Height == made.Height && p.Unit == made.Unit && p.Resolution == made.Resolution);
            recent.Insert(0, made); Directory.CreateDirectory(AppPaths.SettingsDirectory); File.WriteAllText(RecentPath, JsonSerializer.Serialize(recent.Take(20).ToList()));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        Close();
    }

    /// <summary>The dialog's body on its own, for --dialogs to draw.</summary>
    internal Control TakeBody() { var body = (Control)Content!; Content = null; body.Height = 860; return body; }

    internal static async Task<NewDocumentRequest?> Ask(Window owner, (int Width, int Height)? clipboard, string name)
    {
        var dialog = new NewDocumentDialog(clipboard, name);
        await dialog.ShowDialog(owner);
        return dialog.Result;
    }
}

/// <summary>The pictures on the preset cards: a new page with its corner marks, the clipboard, or a picture.</summary>
internal sealed class DocumentPresetGlyph(string kind) : Control
{
    public override void Render(DrawingContext context)
    {
        var s = Math.Min(Bounds.Width, Bounds.Height) / 96; var pen = new Pen(Skin.LabelBrush, 2.4 * s);
        Point P(double x, double y) => new(x * s + (Bounds.Width - 96 * s) / 2, y * s + (Bounds.Height - 96 * s) / 2);
        switch (kind)
        {
            case "search":
                pen = new Pen(Skin.SecondaryBrush, 9 * s); context.DrawEllipse(null, pen, P(40, 40), 26 * s, 26 * s); context.DrawLine(pen, P(60, 60), P(88, 88)); break;
            case "save":
                pen = new Pen(Skin.SecondaryBrush, 7 * s);
                context.DrawLine(pen, P(48, 8), P(48, 60)); context.DrawLine(pen, P(28, 42), P(48, 62)); context.DrawLine(pen, P(68, 42), P(48, 62));
                var tray = new StreamGeometry();
                using (var g = tray.Open()) { g.BeginFigure(P(10, 58), false); g.LineTo(P(10, 86)); g.LineTo(P(86, 86)); g.LineTo(P(86, 58)); }
                context.DrawGeometry(null, pen, tray); break;
            case "clipboard":
                context.DrawRectangle(null, pen, new Rect(P(20, 18), P(76, 92)), 3 * s, 3 * s);
                context.DrawRectangle(null, pen, new Rect(P(36, 10), P(60, 22)), 3 * s, 3 * s); break;
            case "image":
                context.DrawRectangle(null, pen, new Rect(P(6, 18), P(90, 80)));
                var hills = new StreamGeometry();
                using (var g = hills.Open()) { g.BeginFigure(P(10, 76), false); g.LineTo(P(36, 50)); g.LineTo(P(52, 66)); g.LineTo(P(64, 54)); g.LineTo(P(86, 76)); }
                context.DrawGeometry(null, pen, hills); context.DrawEllipse(null, pen, P(72, 34), 6 * s, 6 * s); break;
            default:
                context.DrawLine(pen, P(22, 6), P(22, 24)); context.DrawLine(pen, P(6, 30), P(18, 30));
                var page = new StreamGeometry();
                using (var g = page.Open()) { g.BeginFigure(P(28, 30), true); g.LineTo(P(80, 30)); g.LineTo(P(90, 42)); g.LineTo(P(90, 82)); g.LineTo(P(28, 82)); g.EndFigure(true); }
                context.DrawGeometry(null, pen, page); context.DrawLine(pen, P(80, 30), P(80, 42)); context.DrawLine(pen, P(80, 42), P(90, 42)); break;
        }
    }
}

/// <summary>The orientation buttons' pictures: a person standing in a tall or a wide frame.</summary>
internal sealed class OrientationGlyph(bool landscape) : Control
{
    protected override Size MeasureOverride(Size availableSize) => new(26, 28);
    public override void Render(DrawingContext context)
    {
        var frame = landscape ? new Rect(1, 5, 24, 18) : new Rect(4, 1, 18, 26);
        context.DrawRectangle(null, new Pen(Skin.LabelBrush, 1.4), frame);
        var cx = frame.Center.X; context.DrawEllipse(Skin.LabelBrush, null, new Point(cx, frame.Top + frame.Height * .35), 3.2, 3.2);
        context.DrawRectangle(Skin.LabelBrush, null, new Rect(cx - 5, frame.Top + frame.Height * .55, 10, frame.Height * .42), 3, 3);
    }
}
