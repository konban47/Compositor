using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Compositor.Core.IO;
using SkiaSharp;

namespace Compositor.Desktop;

/// <summary>
/// Save for Web's Output Settings, laid out as the reference shows them: a preset at the top, one of four pages
/// (HTML, Slices, Background, Saving Files) below it, and OK, Cancel, Previous, Next, Load… and Save… beside them.
/// Changing anything turns the preset to Custom.
/// </summary>
internal sealed class OutputSettingsDialog : DialogWindow
{
    private static readonly string[] Presets = ["Custom", "Background Image", "Default Settings", "XHTML"];
    private readonly ComboBox _preset = Combo(Presets, 2);
    private readonly ComboBox _page = Combo(["HTML", "Slices", "Background", "Saving Files"], 0);
    private readonly ContentControl _body = new();
    private readonly TextBlock _error = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
    private readonly Control[] _pages;
    private WebHtmlSettings _settings;
    private WebHtmlSettings? _result;
    private bool _loading;

    // HTML
    private readonly CheckBox _xhtml = Check("Output XHTML");
    private readonly ComboBox _tags = Combo(["Uppercase (BODY)", "Mixed Case (Body)", "Lowercase (body)"], 2);
    private readonly ComboBox _attributes = Combo(["Uppercase (ROWSPAN)", "Mixed Case (RowSpan)", "Mixed Case, Lowercase First (rowSpan)", "Lowercase (rowspan)"], 3);
    private readonly ComboBox _indent = Combo(["Tabs", "None", "1 Space", "2 Spaces", "4 Spaces", "5 Spaces", "8 Spaces"], 0);
    private readonly ComboBox _endings = Combo(["Auto", "Mac", "Win", "Unix"], 0);
    private readonly CheckBox _comments = Check("Include Comments"), _alt = Check("Always Add Alt Attribute"), _quote = Check("Always Quote Attributes"),
        _close = Check("Close All Tags"), _margins = Check("Include Zero Margins on Body Tag");
    // Slices
    private readonly ComboBox _layout = Combo(["Generate Table", "Generate CSS"], 0);
    private readonly ComboBox _empty = Combo(["GIF, IMG W&H", "GIF, TD W&H", "NoWrap, TD W&H"], 0);
    private readonly ComboBox _cells = Combo(["Auto", "Always", "Never"], 0);
    private readonly ComboBox _spacers = Combo(["Auto", "Auto (Bottom)", "Always", "Always (Bottom)", "Never"], 0);
    private readonly ComboBox _css = Combo(["By ID", "Inline", "By Class"], 0);
    private readonly ComboBox _digits = Combo(["1", "01", "001"], 1);
    // Background
    private readonly ComboBox _view = Combo(["HTML", "Image"], 0);
    private readonly TextBox _image = new() { MinHeight = 28 };
    private readonly TextBox _color = new() { MinHeight = 28, PlaceholderText = "#FFFFFF" };
    // Saving Files
    private readonly ComboBox _names = Combo(["Windows", "Mac OS", "Unix"], 0);
    private readonly CheckBox _folder = Check("Put Images in Folder:");
    private readonly TextBox _folderName = new() { MinHeight = 28 };
    private readonly CheckBox _copyBackground = Check("Copy Background Image when Saving");

    private OutputSettingsDialog(WebHtmlSettings start)
    {
        _settings = start;
        Title = Localize.Text("Output Settings"); Width = 660; Height = 620; MinWidth = 560; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _pages = [HtmlPage(), SlicesPage(), BackgroundPage(), SavingPage()];
        var left = new Grid { RowDefinitions = new("Auto,Auto,*,Auto"), Margin = new Thickness(16, 16, 8, 16) };
        left.Children.Add(Labeled("Settings:", _preset, 70));
        Grid.SetRow(_page, 1); _page.Margin = new Thickness(0, 14, 0, 6); _page.Width = 230; _page.HorizontalAlignment = HorizontalAlignment.Left; left.Children.Add(_page);
        var frame = new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(14), Child = new ScrollViewer { Content = _body } };
        Grid.SetRow(frame, 2); left.Children.Add(frame);
        Grid.SetRow(_error, 3); left.Children.Add(_error);
        var right = new StackPanel { Spacing = 8, Margin = new Thickness(8, 16, 16, 16), Width = 120 };
        Button Action(string text, Action act, bool accent = false)
        {
            var button = FormDialog.ActionButton(text, act); button.HorizontalAlignment = HorizontalAlignment.Stretch; button.Margin = new Thickness(0);
            if (accent) { button.Background = Skin.AccentBrush; button.Foreground = Brushes.White; button.IsDefault = true; }
            return button;
        }
        right.Children.Add(Action("OK", Accept, true));
        var cancel = Action("Cancel", Close); cancel.IsCancel = true; right.Children.Add(cancel);
        right.Children.Add(new Border { Height = 12 });
        right.Children.Add(Action("Previous", () => _page.SelectedIndex = (_page.SelectedIndex + 3) % 4));
        right.Children.Add(Action("Next", () => _page.SelectedIndex = (_page.SelectedIndex + 1) % 4));
        right.Children.Add(new Border { Height = 12 });
        right.Children.Add(Action("Load…", () => _ = Load()));
        right.Children.Add(Action("Save…", () => _ = Save()));
        var layout = new Grid { ColumnDefinitions = new("*,Auto") };
        layout.Children.Add(left); Grid.SetColumn(right, 1); layout.Children.Add(right);
        Content = layout;
        Show(start);
        _page.SelectionChanged += (_, _) => _body.Content = _pages[Math.Max(0, _page.SelectedIndex)];
        _body.Content = _pages[0];
        _preset.SelectionChanged += (_, _) =>
        {
            if (_loading || _preset.SelectedIndex <= 0) return;
            Show(_preset.SelectedIndex switch { 1 => WebHtmlSettings.BackgroundImagePreset, 3 => WebHtmlSettings.XhtmlPreset, _ => WebHtmlSettings.Default });
        };
        foreach (var box in new[] { _tags, _attributes, _indent, _endings, _layout, _empty, _cells, _spacers, _css, _digits, _view, _names }) box.SelectionChanged += (_, _) => Edited();
        foreach (var check in new[] { _xhtml, _comments, _alt, _quote, _close, _margins, _folder, _copyBackground }) check.IsCheckedChanged += (_, _) => Edited();
        foreach (var text in new[] { _image, _color, _folderName }) text.TextChanged += (_, _) => Edited();
    }

    private static ComboBox Combo(string[] items, int selected) =>
        new() { ItemsSource = items.Select(Localize.Text).ToArray(), SelectedIndex = selected, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 28 };
    private static CheckBox Check(string text) => new() { Content = Localize.Text(text) };
    private static Grid Labeled(string label, Control control, double width = 130)
    {
        var row = new Grid { ColumnDefinitions = new($"{width.ToString(System.Globalization.CultureInfo.InvariantCulture)},*") };
        row.Children.Add(new TextBlock { Text = Localize.Text(label), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 0, 8, 0), TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(control, 1); row.Children.Add(control);
        return row;
    }
    private static Control Group(string title, params Control[] children)
    {
        var panel = new StackPanel { Spacing = 6 };
        foreach (var child in children) panel.Children.Add(child);
        return new StackPanel
        {
            Spacing = 4, Margin = new Thickness(0, 6, 0, 4),
            Children = { new TextBlock { Text = Localize.Text(title), FontWeight = FontWeight.SemiBold, Foreground = Skin.LabelBrush },
                new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 8), Child = panel } },
        };
    }

    private Control HtmlPage() => new StackPanel
    {
        Spacing = 4,
        Children =
        {
            _xhtml,
            Group("Formatting", Labeled("Tags Case:", _tags), Labeled("Attributes Case:", _attributes), Labeled("Indent:", _indent), Labeled("Line Endings:", _endings)),
            Group("Coding", _comments, _alt, _quote, _close, _margins),
        },
    };
    private Control SlicesPage() => new StackPanel
    {
        Spacing = 4,
        Children =
        {
            Group("Slice Output", Labeled("Layout:", _layout), Labeled("Empty Cells:", _empty), Labeled("TD W&H:", _cells), Labeled("Spacer Cells:", _spacers), Labeled("Referenced:", _css)),
            Group("Default Slice Naming", Labeled("Slice Numbers:", _digits),
                new TextBlock { Text = Localize.Text("Slices are named after the page and their number unless they have a name of their own."), TextWrapping = TextWrapping.Wrap, Foreground = Skin.SecondaryBrush, FontSize = 11 }),
        },
    };
    private Control BackgroundPage()
    {
        var choose = FormDialog.ActionButton("Choose…", () => _ = ChooseImage());
        var image = new Grid { ColumnDefinitions = new("*,Auto") };
        image.Children.Add(_image); Grid.SetColumn(choose, 1); image.Children.Add(choose);
        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                Group("Background", Labeled("View Document As:", _view), Labeled("Background Image:", image), Labeled("Color:", _color)),
            },
        };
    }
    private Control SavingPage() => new StackPanel
    {
        Spacing = 4,
        Children =
        {
            Group("File Naming", Labeled("Filename Compatibility:", _names)),
            Group("Optimized Files", new Grid { ColumnDefinitions = new("Auto,*"), Children = { _folder, Place(_folderName, 1) } }, _copyBackground),
        },
    };
    private static Control Place(Control control, int column) { Grid.SetColumn(control, column); control.Margin = new Thickness(8, 0, 0, 0); return control; }

    private void Show(WebHtmlSettings settings)
    {
        _loading = true;
        _xhtml.IsChecked = settings.Xhtml; _tags.SelectedIndex = (int)settings.TagCase; _attributes.SelectedIndex = (int)settings.AttributeCase;
        _indent.SelectedIndex = (int)settings.Indent; _endings.SelectedIndex = (int)settings.LineEnding;
        _comments.IsChecked = settings.IncludeComments; _alt.IsChecked = settings.AlwaysAddAlt; _quote.IsChecked = settings.AlwaysQuote;
        _close.IsChecked = settings.CloseAllTags; _margins.IsChecked = settings.ZeroMargins;
        _layout.SelectedIndex = (int)settings.Layout; _empty.SelectedIndex = (int)settings.EmptyCells; _cells.SelectedIndex = (int)settings.CellSize;
        _spacers.SelectedIndex = (int)settings.SpacerCells; _css.SelectedIndex = (int)settings.CssReference; _digits.SelectedIndex = settings.SliceDigits - 1;
        _view.SelectedIndex = (int)settings.BackgroundView; _image.Text = settings.BackgroundImage;
        _color.Text = settings.BackgroundColor is { } color ? "#" + (color & 0xFFFFFF).ToString("X6", System.Globalization.CultureInfo.InvariantCulture) : "";
        _names.SelectedIndex = (int)settings.FileNames; _folder.IsChecked = settings.PutImagesInFolder; _folderName.Text = settings.ImagesFolder;
        _copyBackground.IsChecked = settings.CopyBackgroundImage;
        _preset.SelectedIndex = settings == WebHtmlSettings.Default ? 2 : settings == WebHtmlSettings.XhtmlPreset ? 3 : settings == WebHtmlSettings.BackgroundImagePreset ? 1 : 0;
        _settings = settings;
        Enable();
        _loading = false;
    }

    /// <summary>XHTML decides cases and closing for itself, as Photoshop greys them out.</summary>
    private void Enable()
    {
        var xhtml = _xhtml.IsChecked == true;
        _tags.IsEnabled = _attributes.IsEnabled = _close.IsEnabled = _quote.IsEnabled = !xhtml;
        _css.IsEnabled = _layout.SelectedIndex == 1;
        _empty.IsEnabled = _cells.IsEnabled = _spacers.IsEnabled = _layout.SelectedIndex == 0;
        _folderName.IsEnabled = _folder.IsChecked == true;
    }

    private void Edited()
    {
        if (_loading) return;
        Enable();
        if (Current() is { } settings)
        {
            _settings = settings; _error.Text = "";
            _loading = true;
            _preset.SelectedIndex = settings == WebHtmlSettings.Default ? 2 : settings == WebHtmlSettings.XhtmlPreset ? 3 : settings == WebHtmlSettings.BackgroundImagePreset ? 1 : 0;
            _loading = false;
        }
    }

    /// <summary>The settings as the pages show them, or null with the reason shown when one is not usable.</summary>
    internal WebHtmlSettings? Current()
    {
        uint? color = null;
        if (!string.IsNullOrWhiteSpace(_color.Text))
        {
            if (!SKColor.TryParse(_color.Text.Trim(), out var parsed)) { _error.Text = Localize.Text("Enter a valid hexadecimal background color."); return null; }
            color = (uint)parsed | 0xFF000000u;
        }
        var settings = new WebHtmlSettings
        {
            Xhtml = _xhtml.IsChecked == true, TagCase = (HtmlTagCase)_tags.SelectedIndex, AttributeCase = (HtmlAttributeCase)_attributes.SelectedIndex,
            Indent = (HtmlIndent)_indent.SelectedIndex, LineEnding = (HtmlLineEnding)_endings.SelectedIndex,
            IncludeComments = _comments.IsChecked == true, AlwaysAddAlt = _alt.IsChecked == true, AlwaysQuote = _quote.IsChecked == true,
            CloseAllTags = _close.IsChecked == true, ZeroMargins = _margins.IsChecked == true,
            Layout = (WebSliceLayout)_layout.SelectedIndex, EmptyCells = (WebEmptyCells)_empty.SelectedIndex, CellSize = (WebCellSize)_cells.SelectedIndex,
            SpacerCells = (WebSpacerCells)_spacers.SelectedIndex, CssReference = (WebCssReference)_css.SelectedIndex, SliceDigits = _digits.SelectedIndex + 1,
            BackgroundView = (WebBackgroundView)_view.SelectedIndex, BackgroundImage = _image.Text?.Trim() ?? "", BackgroundColor = color,
            FileNames = (WebFileNames)_names.SelectedIndex, PutImagesInFolder = _folder.IsChecked == true, ImagesFolder = _folderName.Text?.Trim() ?? "",
            CopyBackgroundImage = _copyBackground.IsChecked == true,
        };
        if (!settings.IsValid) { _error.Text = Localize.Text("Enter a folder name without path characters, and a background image path without quotes."); return null; }
        return settings;
    }

    private void Accept()
    {
        if (Current() is not { } settings) return;
        _result = settings; Close();
    }

    private async Task ChooseImage()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = Localize.Text("Background Image"), AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(Localize.Text("Images")) { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif"] }] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) _image.Text = Path.GetFileName(path);
    }

    private async Task Load()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = Localize.Text("Load Output Settings"), AllowMultiple = false });
            if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            if (new FileInfo(path).Length > 65536) throw new InvalidDataException("The settings file is too large.");
            Show(WebHtmlSettings.Load(await File.ReadAllBytesAsync(path)));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        { _error.Text = Localize.Text(error.Message); }
    }

    private async Task Save()
    {
        if (Current() is not { } settings) return;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new() { Title = Localize.Text("Save Output Settings"), SuggestedFileName = "output-settings.json", DefaultExtension = "json" });
            if (file?.TryGetLocalPath() is { } path) Compositor.Core.IO.AtomicFile.WriteBytes(path, settings.Save());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { _error.Text = Localize.Text(error.Message); }
    }

    /// <summary>The settings chosen, or null when the dialog was cancelled.</summary>
    public static async Task<WebHtmlSettings?> Ask(Window owner, WebHtmlSettings start)
    {
        var dialog = new OutputSettingsDialog(start);
        await dialog.ShowDialog(owner);
        return dialog._result;
    }

    /// <summary>For the self check: the dialog, shown but not waited on.</summary>
    internal static OutputSettingsDialog Open(Window owner, WebHtmlSettings start, out Task closed)
    {
        var dialog = new OutputSettingsDialog(start);
        closed = dialog.ShowDialog(owner);
        return dialog;
    }
    internal void SelectPage(int page) => _page.SelectedIndex = page;
    internal void SelectPreset(int preset) => _preset.SelectedIndex = preset;
    internal void Confirm() => Accept();
    internal WebHtmlSettings? Result => _result;
}
