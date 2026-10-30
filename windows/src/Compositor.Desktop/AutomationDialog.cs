using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Compositor.Core.IO;
using FontCatalog = Compositor.Core.Document.FontCatalog;
using Compositor.Core.Format;
using SkiaSharp;
using System.Text.Json;

namespace Compositor.Desktop;

internal enum AutomationKind { ContactSheet, LoadLayers, Statistics, PdfPresentation }
internal sealed record AutomationJob(AutomationKind Kind, IReadOnlyList<AutomationSource> Sources,
    ContactSheetOptions Contact, StackStatistic Statistic, PdfPresentationOptions Pdf, string? OutputPath,
    bool AutoAlign = false, bool CreateSmartObject = false);

/// <summary>Shared source selection, with each operation's own grouped layout.</summary>
internal sealed class AutomationDialog : DialogWindow
{
    private readonly AutomationKind _kind;
    private readonly IReadOnlyList<AutomationSource> _open;
    private readonly Func<AutomationJob, CancellationToken, Task> _run;
    private readonly List<AutomationSource> _sources = [];
    private readonly ListBox _list = new() { MinHeight = 160, SelectionMode = SelectionMode.Multiple, Tag = "Automation sources" };
    private readonly ComboBox _sourceType = Choice(["Files", "Folder", "Open Documents"], 0, "Source type");
    private readonly CheckBox _recursive = Check("Include Subfolders", false);
    private readonly CheckBox _autoAlign = Check("Attempt to Automatically Align Source Images", false);
    private readonly CheckBox _createSmartObject = Check("Create Smart Object after Loading Layers", false);
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 36 };
    private readonly StackPanel _body = new() { Spacing = 14 };
    private readonly Button _execute;
    private readonly Button _cancel;
    private CancellationTokenSource? _cancellation;
    private bool _running;
    private readonly ComboBox _units = Choice(["Inches", "Centimeters", "Pixels"], 1, "Document units");
    private readonly NumericUpDown _width = Number(20.32, .01, 30000, "Document width");
    private readonly NumericUpDown _height = Number(25.4, .01, 30000, "Document height");
    private readonly NumericUpDown _resolution = Number(300, 1, 9600, "Resolution");
    private readonly ComboBox _resolutionUnits = Choice(["Pixels/Inch", "Pixels/Centimeter"], 0, "Resolution units");
    private readonly ComboBox _mode = Choice(["Grayscale", "RGB Color", "CMYK Color", "Lab Color"], 1, "Contact color mode");
    private readonly ComboBox _depth = Choice(["8 Bits/Channel", "16 Bits/Channel"], 0, "Contact bit depth");
    private readonly ComboBox _profile = new() { MinHeight = 30, HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "Contact color profile" };
    private readonly CheckBox _groupFolders = Check("Group Images by Folder", false);
    private readonly NumericUpDown _columns = Number(5, 1, 100, "Columns");
    private readonly NumericUpDown _rows = Number(6, 1, 100, "Rows");
    private readonly NumericUpDown _horizontal = Number(10, 0, 30000, "Horizontal spacing");
    private readonly NumericUpDown _vertical = Number(10, 0, 30000, "Vertical spacing");
    private readonly TextBlock _verticalUnit = Label("cm");
    private readonly TextBlock _horizontalUnit = Label("cm");
    private readonly CheckBox _automatic = Check("Use Auto-Spacing", true);
    private readonly CheckBox _flatten = Check("Flatten All Layers", true);
    private readonly CheckBox _rotate = Check("Rotate for Best Fit", false);
    private readonly CheckBox _captions = Check("Use Filename as Caption", true);
    private readonly ComboBox _font = new() { MinHeight = 30, HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "Caption font" };
    private readonly ComboBox _fontStyle = new() { MinHeight = 30, HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "Caption font style" };
    private IReadOnlyList<FontCatalog.FontFace> _captionFaces = [];
    private readonly NumericUpDown _fontSize = Number(12, 1, 300, "Caption size");
    private readonly ComboBox _order = Choice(["Across First", "Down First"], 0, "Thumbnail order");
    private readonly ComboBox _statistic = Choice(["Mean", "Sum", "Minimum", "Maximum", "Median", "Variance", "Standard Deviation", "Skewness", "Kurtosis", "Range", "Entropy"], 0, "Stack mode");
    private readonly ComboBox _background = Choice(["White", "Gray", "Black"], 0, "PDF background");
    private readonly CheckBox _fileNames = Check("File Name", false);
    private readonly RadioButton _multiPage = new() { GroupName = "pdf-save-as", Content = Localize.Text("Multi-Page Document"), IsChecked = true, Tag = "pdf-multi-page" };
    private readonly RadioButton _presentation = new() { GroupName = "pdf-save-as", Content = Localize.Text("Presentation"), Tag = "pdf-presentation" };
    private static readonly int[] PdfFontSizes = [6, 8, 10, 12, 14, 16];
    private readonly ComboBox _pdfFontSize = Choice(PdfFontSizes.Select(size => size.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray(), 3, "PDF font size");
    private readonly CheckBox _pdfExtension = Check("Extension", false);
    private readonly CheckBox _pdfTitle = Check("Title", false);
    private readonly CheckBox _pdfDescription = Check("Description", false);
    private readonly CheckBox _pdfAuthor = Check("Author", false);
    private readonly CheckBox _pdfCopyright = Check("Copyright", false);
    private readonly CheckBox _pdfExif = Check("EXIF Info", false);
    private readonly CheckBox _pdfNotes = Check("Annotations", false);
    private readonly CheckBox _advance = Check("Advance Every", true);
    private readonly NumericUpDown _advanceSeconds = Number(5, PdfSlideshow.MinimumSeconds, PdfSlideshow.MaximumSeconds, "Advance seconds");
    private readonly CheckBox _loop = Check("Loop after Last Page", false);
    private readonly ComboBox _transition = Choice(Enum.GetValues<PdfTransition>().Select(PdfSlideshow.TransitionName).ToArray(), 0, "PDF transition");
    private Control? _presentationOptions;
    private int _previousUnits = 1;
    private int _previousResolutionUnits;
    private static readonly ImageMode[] ContactModes = [ImageMode.Grayscale, ImageMode.RGB, ImageMode.CMYK, ImageMode.Lab];

    internal AutomationDialog(AutomationKind kind, IReadOnlyList<AutomationSource> open, Func<AutomationJob, CancellationToken, Task> run)
    {
        _kind = kind; _open = open; _run = run;
        Title = Localize.Text(kind switch { AutomationKind.ContactSheet => "Contact Sheet II", AutomationKind.LoadLayers => "Load Layers", AutomationKind.Statistics => "Image Statistics", _ => "PDF Presentation" });
        Width = kind == AutomationKind.PdfPresentation ? 1050 : 900; Height = kind == AutomationKind.ContactSheet ? 900 : 620;
        MinWidth = 640; MinHeight = 420; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,150"), Margin = new Thickness(16) };
        var scroll = new ScrollViewer { Content = _body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        root.Children.Add(scroll);
        var actions = new StackPanel { Spacing = 12, Margin = new Thickness(18, 8, 0, 0) }; Grid.SetColumn(actions, 1); root.Children.Add(actions);
        _execute = FormDialog.ActionButton(kind == AutomationKind.PdfPresentation ? "Save…" : "OK", () => _ = Execute());
        _execute.Tag = "Run automation";
        _cancel = FormDialog.ActionButton("Cancel", Cancel);
        actions.Children.Add(_execute); actions.Children.Add(_cancel);
        if (kind == AutomationKind.ContactSheet)
        {
            actions.Children.Add(new Border { Height = 16 });
            actions.Children.Add(FormDialog.ActionButton("Load…", () => _ = LoadSettings()));
            actions.Children.Add(FormDialog.ActionButton("Save…", () => _ = SaveSettings()));
            actions.Children.Add(FormDialog.ActionButton("Reset…", () => ApplySettings(new ContactSheetOptions(AutoSpacing: true, DisplayUnit: ContactSheetUnit.Centimeters))));
        }
        actions.Children.Add(_status);
        if (kind == AutomationKind.Statistics) _body.Children.Add(Row("Stack Mode", _statistic));
        var sourcePanel = SourcesPanel();
        if (kind == AutomationKind.PdfPresentation)
        {
            var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 16 };
            columns.Children.Add(Group("Source Files", sourcePanel));
            var right = new StackPanel { Spacing = 14 }; Grid.SetColumn(right, 1); columns.Children.Add(right);
            right.Children.Add(Group("Output Options", PdfOutputPanel()));
            right.Children.Add(_presentationOptions = Group("Presentation Options", PdfPresentationPanel()));
            _body.Children.Add(columns);
            _multiPage.IsCheckedChanged += (_, _) => UpdatePdfOptions();
            _presentation.IsCheckedChanged += (_, _) => UpdatePdfOptions();
            _fileNames.IsCheckedChanged += (_, _) => UpdatePdfOptions();
            _advance.IsCheckedChanged += (_, _) => UpdatePdfOptions();
            UpdatePdfOptions();
        }
        else
        {
            _body.Children.Add(Group("Source Images", sourcePanel));
            if (kind == AutomationKind.ContactSheet) BuildContactSettings();
        }
        _sourceType.SelectionChanged += (_, _) =>
        {
            if (_sourceType.SelectedIndex == 2) AddOpen();
            _recursive.IsEnabled = _sourceType.SelectedIndex == 1;
        };
        _recursive.IsEnabled = false;
        _list.SelectionChanged += (_, _) => UpdateAvailability();
        KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) { Cancel(); e.Handled = true; } };
        Closing += (_, e) => { if (_running) { e.Cancel = true; Cancel(); } };
        Content = root; UpdateAvailability();
    }

    /// <summary>Save As, background, font size and the caption fields, laid out as in the reference dialog.</summary>
    private Control PdfOutputPanel()
    {
        var panel = new StackPanel { Spacing = 10 };
        var saveAs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 };
        saveAs.Children.Add(new TextBlock { Text = Localize.Text("Save As") + ":", VerticalAlignment = VerticalAlignment.Center }); saveAs.Children.Add(_multiPage); saveAs.Children.Add(_presentation);
        panel.Children.Add(saveAs);
        // Labels size to their text so both choices keep a readable width in the half-width output group.
        var look = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*"), ColumnSpacing = 10 };
        Control[] cells = [Label("Background"), _background, Label("Font Size"), _pdfFontSize];
        for (var column = 0; column < cells.Length; column++) { Grid.SetColumn(cells[column], column); look.Children.Add(cells[column]); }
        _background.HorizontalAlignment = _pdfFontSize.HorizontalAlignment = HorizontalAlignment.Stretch;
        panel.Children.Add(look);
        _pdfNotes.IsEnabled = false;
        ToolTip.SetTip(_pdfNotes, Localize.Text("PDF annotations are not written in this version."));
        var include = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"), ColumnSpacing = 14, RowSpacing = 8 };
        include.Children.Add(new TextBlock { Text = Localize.Text("Include") + ":", VerticalAlignment = VerticalAlignment.Center });
        CheckBox[] fields = [_fileNames, _pdfExtension, _pdfTitle, _pdfDescription, _pdfAuthor, _pdfCopyright, _pdfExif, _pdfNotes];
        for (var index = 0; index < fields.Length; index++)
        {
            Grid.SetRow(fields[index], index / 2); Grid.SetColumn(fields[index], 1 + index % 2); include.Children.Add(fields[index]);
        }
        panel.Children.Add(include);
        return panel;
    }

    private Control PdfPresentationPanel()
    {
        var panel = new StackPanel { Spacing = 10 };
        var advance = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        _advanceSeconds.Width = 70;
        advance.Children.Add(_advance); advance.Children.Add(_advanceSeconds); advance.Children.Add(Label("Seconds"));
        panel.Children.Add(advance); panel.Children.Add(_loop); panel.Children.Add(Row("Transition", _transition));
        return panel;
    }

    private void UpdatePdfOptions()
    {
        var presentation = _presentation.IsChecked == true;
        if (_presentationOptions is not null) _presentationOptions.IsEnabled = presentation;
        _advanceSeconds.IsEnabled = presentation && _advance.IsChecked == true;
        _pdfExtension.IsEnabled = _fileNames.IsChecked == true;
        if (_fileNames.IsChecked != true) _pdfExtension.IsChecked = false;
    }

    internal PdfPresentationOptions PdfOptions()
    {
        var captions = PdfCaption.None;
        foreach (var (check, flag) in new[] { (_fileNames, PdfCaption.FileName), (_pdfExtension, PdfCaption.Extension), (_pdfTitle, PdfCaption.Title),
            (_pdfDescription, PdfCaption.Description), (_pdfAuthor, PdfCaption.Author), (_pdfCopyright, PdfCaption.Copyright), (_pdfExif, PdfCaption.Exif) })
            if (check.IsChecked == true && check.IsEnabled) captions |= flag;
        return new PdfPresentationOptions(_background.SelectedIndex switch { 1 => SKColors.Gray, 2 => SKColors.Black, _ => SKColors.White },
            PdfFontSizes[Math.Clamp(_pdfFontSize.SelectedIndex, 0, PdfFontSizes.Length - 1)], false,
            _presentation.IsChecked == true, _advance.IsChecked == true, Value(_advanceSeconds), _loop.IsChecked == true,
            (PdfTransition)Math.Max(0, _transition.SelectedIndex), captions);
    }

    private StackPanel SourcesPanel()
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(Row("Use", _sourceType));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,180"), ColumnSpacing = 12 };
        grid.Children.Add(_list);
        var buttons = new StackPanel { Spacing = 10 }; Grid.SetColumn(buttons, 1); grid.Children.Add(buttons);
        buttons.Children.Add(FormDialog.ActionButton("Browse…", () => _ = Browse()));
        buttons.Children.Add(FormDialog.ActionButton("Remove", () =>
        {
            var indices = _list.SelectedItems?.Cast<SourceRow>().Select(r => r.Source).ToArray() ?? [];
            foreach (var item in indices) _sources.Remove(item); RefreshSources();
        }));
        var addOpen = FormDialog.ActionButton("Add Open Files", AddOpen); addOpen.IsEnabled = _open.Count > 0; buttons.Children.Add(addOpen);
        if (_kind == AutomationKind.PdfPresentation) buttons.Children.Add(FormDialog.ActionButton("Duplicate", () =>
        { if (_list.SelectedItem is SourceRow selected) { _sources.Insert(_sources.IndexOf(selected.Source) + 1, selected.Source); RefreshSources(); } }));
        buttons.Children.Add(FormDialog.ActionButton("Sort by Name", () => { _sources.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name)); RefreshSources(); }));
        panel.Children.Add(grid); panel.Children.Add(_recursive);
        if (_kind == AutomationKind.ContactSheet) panel.Children.Add(_groupFolders);
        if (_kind is AutomationKind.LoadLayers or AutomationKind.Statistics)
        {
            ToolTip.SetTip(_autoAlign, Localize.Text("Aligns translation. Image statistics uses the common overlapping area."));
            panel.Children.Add(_autoAlign);
        }
        if (_kind == AutomationKind.LoadLayers) panel.Children.Add(_createSmartObject);
        return panel;
    }

    private void BuildContactSettings()
    {
        _sourceType.SelectedIndex = 1; _recursive.IsChecked = true;
        var document = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 20 };
        var left = new StackPanel { Spacing = 10 }; var right = new StackPanel { Spacing = 10 }; Grid.SetColumn(right, 1);
        left.Children.Add(Row("Units", _units)); left.Children.Add(Row("Width", _width)); left.Children.Add(Row("Height", _height));
        var resolution = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 6 };
        resolution.Children.Add(_resolution); Grid.SetColumn(_resolutionUnits, 1); resolution.Children.Add(_resolutionUnits);
        left.Children.Add(Row("Resolution", resolution)); left.Children.Add(Row("Color Profile", _profile));
        right.Children.Add(_flatten); right.Children.Add(Row("Mode", _mode)); right.Children.Add(Row("Bit Depth", _depth));
        document.Children.Add(left); document.Children.Add(right); _body.Children.Add(Group("Document", document));
        var thumbnails = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 20 };
        var a = new StackPanel { Spacing = 10 }; var b = new StackPanel { Spacing = 10 }; Grid.SetColumn(b, 1);
        a.Children.Add(Row("Place", _order)); a.Children.Add(Row("Columns", _columns)); a.Children.Add(Row("Rows", _rows)); a.Children.Add(_rotate);
        Grid SpacingInput(NumericUpDown value, TextBlock unit)
        {
            var input = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6 };
            input.Children.Add(value); Grid.SetColumn(unit, 1); input.Children.Add(unit); return input;
        }
        _horizontal.Value /= (decimal)UnitFactor(_units.SelectedIndex); _vertical.Value /= (decimal)UnitFactor(_units.SelectedIndex);
        b.Children.Add(_automatic); b.Children.Add(Row("Vertical", SpacingInput(_vertical, _verticalUnit))); b.Children.Add(Row("Horizontal", SpacingInput(_horizontal, _horizontalUnit)));
        thumbnails.Children.Add(a); thumbnails.Children.Add(b); _body.Children.Add(Group("Thumbnails", thumbnails));
        _font.ItemsSource = FontCatalog.Families;
        _font.SelectionChanged += (_, _) => RefreshCaptionStyles();
        _font.SelectedItem = FontCatalog.Families.FirstOrDefault(name => name == "Arial") ?? FontCatalog.Families.FirstOrDefault();
        var caption = new Grid { ColumnDefinitions = new("Auto,2*,1.5*,70,Auto"), ColumnSpacing = 8 };
        _captions.Content = Label("Font");
        Control[] captionControls = [_captions, _font, _fontStyle, _fontSize, Label("pt")];
        for (var index = 0; index < captionControls.Length; index++) { Grid.SetColumn(captionControls[index], index); caption.Children.Add(captionControls[index]); }
        _body.Children.Add(Group("Use Filename as Caption", caption));
        _automatic.IsCheckedChanged += (_, _) => { _horizontal.IsEnabled = _vertical.IsEnabled = _automatic.IsChecked != true; };
        _horizontal.IsEnabled = _vertical.IsEnabled = false;
        _captions.IsCheckedChanged += (_, _) => { _font.IsEnabled = _fontStyle.IsEnabled = _fontSize.IsEnabled = _captions.IsChecked == true; };
        _mode.SelectionChanged += (_, _) => RefreshContactProfiles(); RefreshContactProfiles();
        _resolutionUnits.SelectionChanged += (_, _) =>
        {
            var next = _resolutionUnits.SelectedIndex;
            if (next < 0 || next == _previousResolutionUnits) return;
            var ppi = Value(_resolution) * (_previousResolutionUnits == 1 ? 2.54 : 1);
            var factor = next == 1 ? 2.54 : 1;
            _resolution.Minimum = (decimal)(1 / factor); _resolution.Maximum = (decimal)(9600 / factor);
            _resolution.Value = (decimal)(ppi / factor); _previousResolutionUnits = next;
        };
        _units.SelectionChanged += (_, _) =>
        {
            var next = _units.SelectedIndex; if (next < 0 || next == _previousUnits) return;
            double oldFactor = UnitFactor(_previousUnits), newFactor = UnitFactor(next);
            _width.Value = (decimal)Math.Clamp(Value(_width) * oldFactor / newFactor, .01, 30000);
            _height.Value = (decimal)Math.Clamp(Value(_height) * oldFactor / newFactor, .01, 30000);
            _horizontal.Value = (decimal)Math.Clamp(Value(_horizontal) * oldFactor / newFactor, 0, 30000);
            _vertical.Value = (decimal)Math.Clamp(Value(_vertical) * oldFactor / newFactor, 0, 30000);
            _verticalUnit.Text = _horizontalUnit.Text = next == 0 ? "in" : next == 1 ? "cm" : "px";
            _previousUnits = next;
        };
    }

    private void RefreshCaptionStyles(string? selectedFace = null)
    {
        _captionFaces = FontCatalog.Styles(_font.SelectedItem as string ?? "Arial");
        _fontStyle.ItemsSource = _captionFaces.Select(face => face.Style).ToArray();
        var selected = _captionFaces.ToList().FindIndex(face => selectedFace is null ? face.Weight == 400 && face.Width == 5 && !face.Italic : face.Name == selectedFace);
        _fontStyle.SelectedIndex = Math.Max(0, selected);
    }

    private void RefreshContactProfiles(string? selected = null)
    {
        var mode = ContactModes[Math.Clamp(_mode.SelectedIndex, 0, ContactModes.Length - 1)];
        _profile.IsEnabled = mode != ImageMode.Lab;
        if (mode == ImageMode.Lab) { _profile.ItemsSource = new[] { "Lab D50" }; _profile.SelectedIndex = 0; return; }
        var profiles = IccProfiles.Catalog(ColorDefaults.Settings, mode == ImageMode.CMYK ? "CMYK" : mode == ImageMode.Grayscale ? "GRAY" : "RGB")
            .Select(entry => entry.Name).ToArray();
        _profile.ItemsSource = profiles;
        _profile.SelectedItem = profiles.FirstOrDefault(name => name == (selected ?? ColorDefaults.Settings.ProfileName(mode))) ?? profiles.FirstOrDefault();
    }

    private async Task Browse()
    {
        try
        {
            if (_sourceType.SelectedIndex == 2) { AddOpen(); return; }
            IEnumerable<string> paths;
            if (_sourceType.SelectedIndex == 1)
            {
                var folder = (await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Localize.Text("Source Images") })).FirstOrDefault()?.TryGetLocalPath();
                if (folder is null) return;
                var recursive = _recursive.IsChecked == true;
                paths = await Task.Run(() => FileAutomation.EnumerateImages(folder, recursive));
            }
            else paths = (await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Source Images"), AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType(Localize.Text("Images")) { Patterns = ImageImporter.Extensions.Select(e => "*" + e).ToArray() }] }))
                .Select(f => f.TryGetLocalPath()).OfType<string>();
            foreach (var path in paths) if (!_sources.Any(s => StringComparer.OrdinalIgnoreCase.Equals(s.Path, path))) _sources.Add(new AutomationSource(Path.GetFileName(path), path));
            RefreshSources();
        }
        catch (Exception error) { _status.Text = error.Message; }
    }
    private void AddOpen() { foreach (var source in _open) if (!_sources.Contains(source)) _sources.Add(source); RefreshSources(); }
    private sealed record SourceRow(AutomationSource Source) { public override string ToString() => Source.Name; }
    private void RefreshSources() { _list.ItemsSource = _sources.Select(s => new SourceRow(s)).ToArray(); UpdateAvailability(); }
    private void UpdateAvailability() => _execute.IsEnabled = !_running && _sources.Count >= (_kind == AutomationKind.Statistics ? 2 : 1);
    private double ResolutionPpi => Math.Round(Value(_resolution) * (_resolutionUnits.SelectedIndex == 1 ? 2.54 : 1), 8);
    private double UnitFactor(int index) => index switch { 0 => ResolutionPpi, 1 => ResolutionPpi / 2.54, _ => 1 };
    private ContactSheetOptions ContactOptions()
    {
        int width = (int)Math.Round(Value(_width) * UnitFactor(_units.SelectedIndex)), height = (int)Math.Round(Value(_height) * UnitFactor(_units.SelectedIndex));
        int gap = _automatic.IsChecked == true ? Math.Max(1, (int)Math.Round(ResolutionPpi / 30)) : -1;
        return new ContactSheetOptions(width, height, ResolutionPpi, (int)Value(_columns), (int)Value(_rows), _order.SelectedIndex == 1,
            gap >= 0 ? gap : (int)Math.Round(Value(_horizontal) * UnitFactor(_units.SelectedIndex)), gap >= 0 ? gap : (int)Math.Round(Value(_vertical) * UnitFactor(_units.SelectedIndex)), _rotate.IsChecked == true,
            _captions.IsChecked == true, _captionFaces.ElementAtOrDefault(_fontStyle.SelectedIndex)?.Name ?? _font.SelectedItem as string ?? "Arial",
            Value(_fontSize), _flatten.IsChecked == true, ContactModes[Math.Clamp(_mode.SelectedIndex, 0, ContactModes.Length - 1)],
            _depth.SelectedIndex == 1 ? 16 : 8, _mode.SelectedIndex == 3 ? null : _profile.SelectedItem as string, _groupFolders.IsChecked == true,
            _automatic.IsChecked == true, (ContactSheetUnit)_units.SelectedIndex, _resolutionUnits.SelectedIndex == 1);
    }
    private async Task Execute()
    {
        if (_running) return;
        try
        {
            string? path = null;
            if (_kind == AutomationKind.PdfPresentation)
            {
                path = (await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Title, SuggestedFileName = "Presentation.pdf", DefaultExtension = "pdf", ShowOverwritePrompt = true,
                    FileTypeChoices = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }] }))?.TryGetLocalPath();
                if (path is null) return;
            }
            _cancellation = new CancellationTokenSource(); _running = true; _body.IsEnabled = false; UpdateAvailability();
            _status.Text = Localize.Text("Processing… Press Esc to cancel.");
            var job = new AutomationJob(_kind, _sources.ToArray(), ContactOptions(), (StackStatistic)_statistic.SelectedIndex,
                PdfOptions(), path,
                _autoAlign.IsChecked == true, _createSmartObject.IsChecked == true);
            await _run(job, _cancellation.Token);
            _running = false; Close(true);
        }
        catch (OperationCanceledException) { _status.Text = Localize.Text("Canceled."); }
        catch (Exception error) { _status.Text = error.Message; }
        finally { _running = false; _body.IsEnabled = true; _cancel.IsEnabled = true; _cancellation?.Dispose(); _cancellation = null; UpdateAvailability(); }
    }
    private void Cancel() { if (_running) { _cancellation?.Cancel(); _cancel.IsEnabled = false; _status.Text = Localize.Text("Canceling…"); } else Close(false); }
    private async Task SaveSettings()
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Localize.Text("Save Contact Sheet Settings"), SuggestedFileName = "Contact Sheet.json", DefaultExtension = "json", ShowOverwritePrompt = true });
            if (file?.TryGetLocalPath() is { } path) AtomicFile.WriteBytes(path, JsonSerializer.SerializeToUtf8Bytes(ContactOptions(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error) { _status.Text = error.Message; }
    }
    private async Task LoadSettings()
    {
        try
        {
            var file = (await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Load Contact Sheet Settings"), FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }] })).FirstOrDefault();
            if (file?.TryGetLocalPath() is { } path) ApplySettings(JsonSerializer.Deserialize<ContactSheetOptions>(await File.ReadAllTextAsync(path)) ?? throw new InvalidDataException("Invalid contact sheet settings."));
        }
        catch (Exception error) { _status.Text = error.Message; }
    }
    private void ApplySettings(ContactSheetOptions o)
    {
        _resolutionUnits.SelectedIndex = 0; _units.SelectedIndex = 2; _width.Value = o.Width; _height.Value = o.Height; _resolution.Value = (decimal)o.Resolution;
        _columns.Value = o.Columns; _rows.Value = o.Rows; _horizontal.Value = o.HorizontalSpacing; _vertical.Value = o.VerticalSpacing;
        _automatic.IsChecked = o.AutoSpacing; _order.SelectedIndex = o.DownFirst ? 1 : 0; _rotate.IsChecked = o.RotateToFit;
        _captions.IsChecked = o.Captions; _font.SelectedItem = FontCatalog.Family(o.FontFamily); RefreshCaptionStyles(o.FontFamily);
        _fontSize.Value = (decimal)o.FontSize; _flatten.IsChecked = o.Flatten; _groupFolders.IsChecked = o.GroupByFolder;
        _mode.SelectedIndex = Array.IndexOf(ContactModes, o.Mode); _depth.SelectedIndex = o.Depth == 16 ? 1 : 0; RefreshContactProfiles(o.ColorProfile);
        _units.SelectedIndex = (int)o.DisplayUnit; _resolutionUnits.SelectedIndex = o.ResolutionPerCentimeter ? 1 : 0;
    }
    internal static double Value(NumericUpDown control) => (double)(control.Value ?? 0);
    internal void PdfPresentationSelfCheck(Action pump)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        pump();
        Check(_presentationOptions?.IsEnabled == false && !_pdfExtension.IsEnabled && !_pdfNotes.IsEnabled, "PDF presentation controls are enabled for a multi-page document.");
        Check(PdfOptions() is { Presentation: false, FontSize: 12, Captions: PdfCaption.None }, "PDF defaults are wrong.");
        _presentation.IsChecked = true; _fileNames.IsChecked = true; pump();
        Check(_presentationOptions?.IsEnabled == true && _pdfExtension.IsEnabled && _advanceSeconds.IsEnabled, "Presentation controls did not follow Save As.");
        _pdfExtension.IsChecked = true; _pdfAuthor.IsChecked = true; _advanceSeconds.Value = 7; _loop.IsChecked = true;
        _transition.SelectedIndex = (int)PdfTransition.Dissolve; _pdfFontSize.SelectedIndex = 5; _background.SelectedIndex = 2; pump();
        Check(PdfOptions() == new PdfPresentationOptions(SKColors.Black, 16, false, true, true, 7, true, PdfTransition.Dissolve,
            PdfCaption.FileName | PdfCaption.Extension | PdfCaption.Author), "PDF presentation options did not reach the writer.");
        _advance.IsChecked = false; _fileNames.IsChecked = false; pump();
        Check(!_advanceSeconds.IsEnabled && !_pdfExtension.IsEnabled && PdfOptions() is { AutoAdvance: false, Captions: PdfCaption.Author },
            "Unchecking advance or file name left dependent options active.");
        _multiPage.IsChecked = true; pump();
        Check(_presentationOptions?.IsEnabled == false && !PdfOptions().Presentation, "Multi-page mode kept presentation settings active.");
        Console.WriteLine("PASS: PDF Presentation save-as, captions, advance, loop and transition options.");
    }

    internal void ContactSheetSelfCheck(Action pump)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var initial = ContactOptions();
        _automatic.IsChecked = false;
        var gap = ContactOptions().HorizontalSpacing;
        _resolutionUnits.SelectedIndex = 1; _units.SelectedIndex = 2; pump();
        var converted = ContactOptions();
        Check(converted.Width == initial.Width && converted.Height == initial.Height && Math.Abs(converted.Resolution - initial.Resolution) < .001,
            "Contact sheet unit conversion changed output dimensions or resolution.");
        Check(converted.HorizontalSpacing == gap, "Changing document units changed thumbnail spacing.");
        _mode.SelectedIndex = 2; _depth.SelectedIndex = 1; _groupFolders.IsChecked = true; pump();
        Check(ContactOptions().Mode == ImageMode.CMYK && ContactOptions().Depth == 16 && _profile.SelectedItem is string,
            "CMYK profile and depth controls did not reach contact sheet options.");
        if (_captionFaces.Count > 1) _fontStyle.SelectedIndex = 1;
        var saved = JsonSerializer.Deserialize<ContactSheetOptions>(JsonSerializer.Serialize(ContactOptions()))!;
        ApplySettings(new ContactSheetOptions()); ApplySettings(saved); pump();
        Check(ContactOptions() == saved, "Contact sheet settings did not restore mode, profile, font style or folder grouping. Expected " + JsonSerializer.Serialize(saved) + " Actual " + JsonSerializer.Serialize(ContactOptions()));
        _mode.SelectedIndex = 3; pump();
        Check(!_profile.IsEnabled && ContactOptions().ColorProfile is null, "Lab incorrectly retained an RGB/CMYK profile.");
        ApplySettings(new ContactSheetOptions(AutoSpacing: true, DisplayUnit: ContactSheetUnit.Centimeters)); pump();
        Console.WriteLine("PASS: Contact sheet color/depth/profile, font style, physical units, spacing and settings round-trip.");
    }
    internal static NumericUpDown Number(double value, double min, double max, string tag) => new() { Value = (decimal)value, Minimum = (decimal)min, Maximum = (decimal)max, Increment = 1, FormatString = "0.###", ShowButtonSpinner = false, MinHeight = 30, Tag = tag };
    internal static ComboBox Choice(string[] items, int selected, string tag) => new() { ItemsSource = items.Select(Localize.Text).ToArray(), SelectedIndex = selected, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 30, Tag = tag };
    internal static CheckBox Check(string text, bool value) => new() { Content = Label(text), IsChecked = value, Tag = text };
    internal static TextBlock Label(string text) => new() { Text = Localize.Text(text), VerticalAlignment = VerticalAlignment.Center, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    internal static Grid Row(string label, Control control)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*"), ColumnSpacing = 8 };
        row.Children.Add(Label(label)); Grid.SetColumn(control, 1); control.HorizontalAlignment = HorizontalAlignment.Stretch; row.Children.Add(control); return row;
    }
    internal static Control Group(string title, Control contents)
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(12) }; panel.Children.Add(Label(title)); panel.Children.Add(contents);
        return new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Child = panel };
    }
}
