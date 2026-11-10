using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Compositor.Core.IO;
using FontCatalog = Compositor.Core.Document.FontCatalog;
using PhotomergeLayout = Compositor.Core.Document.PhotomergeLayout;
using PhotomergeOptions = Compositor.Core.Document.PhotomergeOptions;
using Compositor.Core.Format;
using SkiaSharp;
using System.Text.Json;

namespace Compositor.Desktop;

internal enum AutomationKind { ContactSheet, LoadLayers, Statistics, PdfPresentation, Photomerge, HdrPro, LensCorrection }

/// <summary>Lens Correction's destination, the profile when one is chosen rather than matched, and the corrections.</summary>
internal sealed record LensJob(string Folder, ExportFormat? Format, string? Lens, Compositor.Core.Document.LensCorrectionOptions Options);
internal sealed record AutomationJob(AutomationKind Kind, IReadOnlyList<AutomationSource> Sources,
    ContactSheetOptions Contact, StackStatistic Statistic, PdfPresentationOptions Pdf, string? OutputPath,
    bool AutoAlign = false, bool CreateSmartObject = false, PhotomergeOptions? Photomerge = null, LensJob? Lens = null);

/// <summary>Shared source selection, with each operation's own grouped layout.</summary>
internal sealed class AutomationDialog : DialogWindow
{
    private readonly AutomationKind _kind;
    private readonly IReadOnlyList<AutomationSource> _open;
    private readonly Func<AutomationJob, CancellationToken, Task> _run;
    private readonly List<AutomationSource> _sources = [];
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Multiple, Tag = "Automation sources" };
    private readonly ComboBox _sourceType = Choice(["Files", DialogKit.FolderWord, "Open Documents"], 0, "Source type");
    private readonly CheckBox _recursive = Check("Include Subfolders", false);
    private readonly CheckBox _autoAlign = Check("Attempt to Automatically Align Source Images", false);
    private readonly CheckBox _createSmartObject = Check("Create Smart Object after Loading Layers", false);
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 36 };
    private readonly StackPanel _body = new() { Spacing = 6 };
    private readonly Button _execute;
    private readonly Button _cancel;
    private CancellationTokenSource? _cancellation;
    private bool _running;
    private readonly ComboBox _units = Choice(["Inches", "Centimeters", "Pixels"], 1, "Document units");
    private readonly NumericUpDown _width = Number(20.32, .01, 30000, "Document width");
    private readonly NumericUpDown _height = Number(25.4, .01, 30000, "Document height");
    private readonly NumericUpDown _resolution = Number(118.11, .4, 3780, "Resolution");
    private readonly ComboBox _resolutionUnits = Choice(["Pixels/Inch", "Pixels/Centimeter"], 1, "Resolution units");
    private readonly ComboBox _mode = Choice(["Grayscale", "RGB Color", "CMYK Color", "Lab Color"], 1, "Contact color mode");
    private readonly ComboBox _depth = Choice(["8 Bit", "16 Bit"], 0, "Contact bit depth");
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
    private readonly RadioButton[] _layouts = [.. new[] { "Auto", "Perspective", "Cylindrical", "Spherical", "Collage", "Reposition" }
        .Select((name, i) => new RadioButton { Content = Localize.Text(name), GroupName = "Photomerge layout", IsChecked = i == 0, Tag = name })];
    private readonly CheckBox _blendImages = Check("Blend Images Together", true);
    private readonly CheckBox _vignette = Check("Vignette Removal", false);
    private readonly CheckBox _distortion = Check("Geometric Distortion Correction", false);
    private readonly CheckBox _fillTransparent = Check("Content-Aware Fill Transparent Areas", false);
    private readonly ComboBox _lensFormat = Choice(["JPG", "PNG", "PSD", "TIFF"], 2, "Lens correction file type");
    private readonly TextBlock _lensFolder = new() { Foreground = Skin.SecondaryBrush }, _lensProfileName = new() { Foreground = Skin.SecondaryBrush };
    private readonly CheckBox _matchBest = Check("Match Best Profile", true);
    private readonly ComboBox _lensProfile = new() { MinHeight = 30, HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false, Tag = "Lens profile" };
    private readonly CheckBox _geometric = Check("Geometric Distortion", true), _autoScale = Check("Auto Scale Image", true),
        _aberration = Check("Chromatic Aberration", false), _lensVignette = Check("Vignette", false);
    private readonly ComboBox _lensEdge = Choice(["Edge Extension", "Transparency", "Black", "White"], 1, "Lens correction edge");
    private string? _destination;
    private readonly CheckBox _fileNames = Check("File Name", false);
    private readonly RadioButton _multiPage = new() { GroupName = "pdf-save-as", Content = Localize.Text("Multi-Page Document"), IsChecked = true, Tag = "pdf-multi-page" };
    private readonly RadioButton _presentation = new() { GroupName = "pdf-save-as", Content = Localize.Text("Presentation"), Tag = "pdf-presentation" };
    internal static readonly int[] PdfFontSizes = [6, 8, 10, 12, 14, 16];
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
    private int _previousResolutionUnits = 1;
    private static readonly ImageMode[] ContactModes = [ImageMode.Grayscale, ImageMode.RGB, ImageMode.CMYK, ImageMode.Lab];

    internal AutomationDialog(AutomationKind kind, IReadOnlyList<AutomationSource> open, Func<AutomationJob, CancellationToken, Task> run)
    {
        _kind = kind; _open = open; _run = run;
        Title = Localize.Text(kind switch { AutomationKind.ContactSheet => "Contact Sheet II", AutomationKind.LoadLayers => "Load Layers", AutomationKind.Statistics => "Image Statistics", AutomationKind.Photomerge => "Photomerge", AutomationKind.HdrPro => "Merge to HDR Pro", AutomationKind.LensCorrection => "Lens Correction", _ => "PDF Presentation" });
        (Width, Height) = kind switch
        {
            AutomationKind.PdfPresentation => (1000d, 480d), AutomationKind.Photomerge => (860d, 790d), AutomationKind.ContactSheet => (800d, 800d),
            AutomationKind.LensCorrection => (800d, 730d), AutomationKind.Statistics => (720d, 480d), _ => (720d, 500d),
        };
        MinWidth = 640; MinHeight = 420; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(16, 8, 16, 16) };
        // The window is sized to what it holds; the scroll is there for a screen too short for it.
        _scroll = new ScrollViewer { Content = _body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        root.Children.Add(_scroll);
        _execute = FormDialog.ActionButton(kind == AutomationKind.PdfPresentation ? "Save" : "OK", () => _ = Execute()); _execute.Tag = "Run automation";
        _cancel = FormDialog.ActionButton("Cancel", Cancel);
        var actions = DialogKit.ActionColumn(_execute, _cancel);
        Grid.SetColumn(actions, 1); root.Children.Add(actions);
        if (kind == AutomationKind.ContactSheet)
        {
            actions.Children.Add(new Border { Height = 24 });
            foreach (var button in new[] { FormDialog.ActionButton("Load…", () => _ = LoadSettings()), FormDialog.ActionButton("Save…", () => _ = SaveSettings()),
                FormDialog.ActionButton("Reset…", () => ApplySettings(new ContactSheetOptions(AutoSpacing: true, DisplayUnit: ContactSheetUnit.Centimeters))) })
            { button.Margin = default; button.HorizontalAlignment = HorizontalAlignment.Stretch; actions.Children.Add(button); }
            actions.Children.Add(new TextBlock { Text = Localize.Text("Press the ESC key to Cancel processing images"), TextWrapping = Avalonia.Media.TextWrapping.Wrap, Foreground = Skin.SecondaryBrush, Margin = new Thickness(0, 12, 0, 0) });
        }
        actions.Children.Add(_status);
        switch (kind)
        {
            case AutomationKind.PdfPresentation:
                var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 16 };
                columns.Children.Add(DialogKit.Group("Source Files", SourcesPanel()));
                var right = new StackPanel { Spacing = 8 }; Grid.SetColumn(right, 1); columns.Children.Add(right);
                right.Children.Add(DialogKit.Group("Output Options", PdfOutputPanel()));
                right.Children.Add(_presentationOptions = DialogKit.Group("Presentation Options", PdfPresentationPanel()));
                _body.Children.Add(columns);
                _multiPage.IsCheckedChanged += (_, _) => UpdatePdfOptions();
                _presentation.IsCheckedChanged += (_, _) => UpdatePdfOptions();
                _fileNames.IsCheckedChanged += (_, _) => UpdatePdfOptions();
                _advance.IsCheckedChanged += (_, _) => UpdatePdfOptions();
                UpdatePdfOptions();
                break;
            case AutomationKind.LensCorrection: BuildLensCorrection(); break;
            case AutomationKind.Photomerge: BuildPhotomerge(); break;
            case AutomationKind.ContactSheet: _body.Children.Add(DialogKit.Group("Source Images", ContactSources())); BuildContactSettings(); break;
            case AutomationKind.Statistics:
                _statistic.Width = 340; _statistic.HorizontalAlignment = HorizontalAlignment.Left;
                var mode = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), ColumnSpacing = 8, Margin = new Thickness(0, 0, 0, 6) };
                mode.Children.Add(DialogKit.Label("Choose Stack Mode", HorizontalAlignment.Right)); Grid.SetColumn(_statistic, 1); mode.Children.Add(_statistic);
                _body.Children.Add(mode);
                _body.Children.Add(DialogKit.Group("Source Files", SourcesPanel("Choose two or more files to compute statistics on.")));
                break;
            case AutomationKind.LoadLayers: _body.Children.Add(DialogKit.Group("Source Files", SourcesPanel("Choose two or more files to load into an image stack."))); break;
            default: _body.Children.Add(DialogKit.Group("Source Files", SourcesPanel("Choose two or more files to merge."))); break;
        }
        _sourceType.SelectionChanged += (_, _) =>
        {
            if (_kind == AutomationKind.ContactSheet) ShowContactSource();
            else if (_sourceType.SelectedIndex == 2) AddOpen();
        };
        _list.SelectionChanged += (_, _) => UpdateAvailability();
        KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) { Cancel(); e.Handled = true; } };
        Closing += (_, e) => { if (_running) { e.Cancel = true; Cancel(); } };
        Content = root; UpdateAvailability();
    }
    private readonly ScrollViewer _scroll;
    /// <summary>What the self check reads: true when everything fits without scrolling.</summary>
    internal bool FitsWithoutScrolling => _scroll.Extent.Height <= _scroll.Viewport.Height + 1 && _scroll.Extent.Width <= _scroll.Viewport.Width + 1;

    /// <summary>Source files, destination, profile and options, top to bottom, as Photoshop's Lens Correction lays them out.</summary>
    private void BuildLensCorrection()
    {
        _body.Children.Add(DialogKit.Group("Source Files", SourcesPanel("Choose the files to process with lens correction.")));
        _lensFormat.Width = 110; _lensFormat.HorizontalAlignment = HorizontalAlignment.Left;
        var destination = new StackPanel { Spacing = 8 };
        destination.Children.Add(DialogKit.Row("File Type", _lensFormat));
        var choose = FormDialog.ActionButton("Choose…", () => _ = ChooseDestination()); choose.HorizontalAlignment = HorizontalAlignment.Left; choose.Margin = default; choose.Width = 130;
        destination.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { this.Mnemonic(choose, 'H'), _lensFolder } });
        _lensFolder.VerticalAlignment = VerticalAlignment.Center; _lensFolder.Text = Localize.Text("No destination folder chosen.");
        _body.Children.Add(DialogKit.Group("Destination Folder", destination));
        var profiles = new StackPanel { Spacing = 8 };
        var pick = FormDialog.ActionButton("Choose…", () => _ = ChooseLensProfile()); pick.HorizontalAlignment = HorizontalAlignment.Left; pick.Margin = default; pick.Width = 130; pick.IsEnabled = false;
        profiles.Children.Add(this.Mnemonic(_matchBest, 'M'));
        profiles.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { this.Mnemonic(pick, 'O'), _lensProfileName } });
        _lensProfileName.VerticalAlignment = VerticalAlignment.Center;
        _body.Children.Add(DialogKit.Group("Lens Correction Profiles", profiles));
        _lensEdge.Width = 150; _lensEdge.HorizontalAlignment = HorizontalAlignment.Left;
        var edge = DialogKit.Row("Edge", _lensEdge, 40);
        var options = DialogKit.Columns("Auto,*", 28, this.Mnemonic(_geometric, 'D'), this.Mnemonic(_autoScale, 'S'), this.Mnemonic(_aberration, 'C'), edge, this.Mnemonic(_lensVignette, 'V'));
        _body.Children.Add(DialogKit.Group("Correction Options", options));
        _matchBest.IsCheckedChanged += (_, _) => { pick.IsEnabled = _lensProfile.IsEnabled = _matchBest.IsChecked != true; UpdateAvailability(); };
        _lensProfile.ItemsSource = Compositor.Core.Document.LensDatabase.Bundled().Lenses.Select(l => l.Name).Distinct().Order(StringComparer.OrdinalIgnoreCase).ToArray();
        _lensProfile.SelectionChanged += (_, _) => { _lensProfileName.Text = _lensProfile.SelectedItem as string ?? ""; UpdateAvailability(); };
    }

    /// <summary>The lenses are thousands long, so choosing one is a searchable list of its own.</summary>
    private async Task ChooseLensProfile()
    {
        var names = _lensProfile.ItemsSource as string[] ?? [];
        var dialog = new FormDialog("Lens Correction Profiles", 560, 520);
        var search = dialog.Text("Search", "");
        var list = new ListBox { Height = 340, ItemsSource = names, SelectedItem = _lensProfile.SelectedItem, Tag = "Lens profile list" };
        search.TextChanged += (_, _) => list.ItemsSource = names.Where(n => n.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
        dialog.Fields.Children.Add(list);
        if (await dialog.ShowDialog<bool>(this) && list.SelectedItem is string chosen) _lensProfile.SelectedItem = chosen;
    }

    /// <summary>The layouts down the left, the source files and the four options on the right, as Photoshop's Photomerge has them.</summary>
    private void BuildPhotomerge()
    {
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*"), ColumnSpacing = 14 };
        var layouts = new StackPanel { Spacing = 4 };
        foreach (var layout in _layouts) { layouts.Children.Add(layout); layouts.Children.Add(LayoutIcon((string)layout.Tag!)); }
        columns.Children.Add(DialogKit.Group("Layout", layouts));
        var right = SourcesPanel();
        foreach (var option in new[] { _blendImages, _vignette, _distortion, _fillTransparent }) right.Children.Add(option);
        var source = DialogKit.Group("Source Files", right); Grid.SetColumn(source, 1); columns.Children.Add(source);
        _body.Children.Add(columns);
    }

    /// <summary>Save As, background, font size and the caption fields, laid out as in Photoshop's dialog.</summary>
    private Control PdfOutputPanel()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { DialogKit.Label("Save As"), this.Mnemonic(_multiPage, 'M'), this.Mnemonic(_presentation, 'P') } });
        _background.Width = 100; _pdfFontSize.Width = 72;
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(36, 0, 0, 0), Children = { DialogKit.Label("Background"), _background, new Border { Width = 24 }, DialogKit.Label("Font Size"), _pdfFontSize } });
        ToolTip.SetTip(_pdfNotes, Localize.Text("Writes the notes of open documents as PDF comments where they sit on the picture."));
        var include = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"), ColumnSpacing = 14, RowSpacing = 4 };
        include.Children.Add(DialogKit.Label("Include"));
        (CheckBox Check, char Key)[] fields = [(_fileNames, 'F'), (_pdfExtension, 'N'), (_pdfTitle, 'I'), (_pdfDescription, 'C'), (_pdfAuthor, 'U'), (_pdfCopyright, 'Y'), (_pdfExif, 'X'), (_pdfNotes, 'A')];
        for (var index = 0; index < fields.Length; index++)
        {
            var check = fields[index].Check; if (fields[index].Key != 'A') this.Mnemonic(check, fields[index].Key);
            Grid.SetRow(check, index / 2); Grid.SetColumn(check, 1 + index % 2); include.Children.Add(check);
        }
        panel.Children.Add(include);
        return panel;
    }

    private Control PdfPresentationPanel()
    {
        var panel = new StackPanel { Spacing = 6 };
        _advanceSeconds.Width = 56;
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { this.Mnemonic(_advance, 'E'), _advanceSeconds, Label("Seconds") } });
        panel.Children.Add(this.Mnemonic(_loop, 'L'));
        _transition.Width = 170; _transition.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(DialogKit.Row("Transition", _transition, 72));
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
            (PdfTransition)Math.Max(0, _transition.SelectedIndex), captions, _pdfNotes.IsChecked == true);
    }

    /// <summary>"Use: Files", the list and its buttons, and what each operation adds under them. PDF Presentation has a check box
    /// in place of the "Use" row, as Photoshop's does.</summary>
    private StackPanel SourcesPanel(string? description = null)
    {
        var panel = new StackPanel { Spacing = 8 };
        if (description is not null) panel.Children.Add(new TextBlock { Text = Localize.Text(description), TextWrapping = Avalonia.Media.TextWrapping.Wrap, Foreground = Skin.SecondaryBrush, Margin = new Thickness(0, 0, 0, 8) });
        if (_kind == AutomationKind.PdfPresentation)
        {
            var addOpen = this.Mnemonic(Check("Add Open Files", false), 'O');
            addOpen.IsEnabled = _open.Count > 0;
            addOpen.IsCheckedChanged += (_, _) => { if (addOpen.IsChecked == true) AddOpen(); else { _sources.RemoveAll(s => _open.Contains(s)); RefreshSources(); } };
            panel.Children.Add(addOpen);
        }
        else
        {
            _sourceType.ItemsSource = new[] { Localize.Text("Files"), Localize.Text(DialogKit.FolderWord) }; _sourceType.SelectedIndex = 0;
            _sourceType.Width = 300; _sourceType.HorizontalAlignment = HorizontalAlignment.Left;
            panel.Children.Add(DialogKit.Row("Use", _sourceType, 40));
        }
        panel.Children.Add(SourceList());
        if (_kind is AutomationKind.LoadLayers or AutomationKind.Statistics or AutomationKind.HdrPro)
        {
            ToolTip.SetTip(_autoAlign, Localize.Text("Aligns translation. Image statistics uses the common overlapping area."));
            panel.Children.Add(this.Mnemonic(_autoAlign, 'A'));
        }
        if (_kind == AutomationKind.LoadLayers) panel.Children.Add(this.Mnemonic(_createSmartObject, 'C'));
        return panel;
    }

    private Grid SourceList()
    {
        _list.Height = _kind switch { AutomationKind.PdfPresentation => 330, AutomationKind.Photomerge => 450, AutomationKind.ContactSheet => 120, AutomationKind.LensCorrection => 170, _ => 210 };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 14 };
        grid.Children.Add(new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Child = _list });
        var buttons = new StackPanel { Spacing = 10, Width = _kind == AutomationKind.PdfPresentation ? 130 : 180, VerticalAlignment = _kind == AutomationKind.PdfPresentation ? VerticalAlignment.Center : VerticalAlignment.Top }; Grid.SetColumn(buttons, 1); grid.Children.Add(buttons);
        void Add(Button button, char key) { button.Margin = default; button.HorizontalAlignment = HorizontalAlignment.Stretch; buttons.Children.Add(this.Mnemonic(button, key)); }
        Add(FormDialog.ActionButton("Browse…", () => _ = Browse()), 'B');
        if (_kind == AutomationKind.PdfPresentation) Add(_duplicate = FormDialog.ActionButton("Duplicate", () =>
        { if (_list.SelectedItem is SourceRow selected) { _sources.Insert(_sources.IndexOf(selected.Source) + 1, selected.Source); RefreshSources(); } }), 'D');
        Add(_remove = FormDialog.ActionButton("Remove", () =>
        {
            var indices = _list.SelectedItems?.Cast<SourceRow>().Select(r => r.Source).ToArray() ?? [];
            foreach (var item in indices) _sources.Remove(item); RefreshSources();
        }), 'R');
        if (_kind != AutomationKind.PdfPresentation)
        {
            var addOpen = FormDialog.ActionButton("Add Open Files", AddOpen); addOpen.IsEnabled = _open.Count > 0; Add(addOpen, 'F');
        }
        if (_kind is not (AutomationKind.Photomerge or AutomationKind.LensCorrection))
            Add(_sort = FormDialog.ActionButton("Sort by Name", () => { _sources.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name)); RefreshSources(); }), 'S');
        return grid;
    }
    private Button? _duplicate, _remove, _sort;

    /// <summary>Contact Sheet's sources: a folder is a path beside Choose, files are a list, and open documents need neither.</summary>
    private Control ContactSources()
    {
        var panel = new StackPanel { Spacing = 8, MinHeight = 196 };
        _sourceType.Width = 190; _sourceType.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(DialogKit.Row("Use", _sourceType, 90));
        var choose = FormDialog.ActionButton("Choose…", () => _ = Browse()); choose.Margin = default; choose.Width = 120;
        Grid.SetColumn(_folderPath, 1);
        _contactFolder = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Children = { choose, _folderPath } };
        _contactFiles = SourceList();
        panel.Children.Add(_contactFolder); panel.Children.Add(_contactFiles);
        panel.Children.Add(_recursive); panel.Children.Add(_groupFolders);
        _recursive.IsCheckedChanged += (_, _) => { if (_folder is { } folder && _sourceType.SelectedIndex == 1) _ = LoadFolder(folder); };
        _sourceType.SelectedIndex = 1; _recursive.IsChecked = true;
        ShowContactSource();
        return panel;
    }
    private Control? _contactFolder, _contactFiles;
    private string? _folder;
    private readonly TextBox _folderPath = new() { IsReadOnly = true, Tag = "Contact folder", VerticalAlignment = VerticalAlignment.Center, Text = "[" + Localize.Text("Choose Image Folder") + "]" };

    private void ShowContactSource()
    {
        if (_contactFolder is null || _contactFiles is null) return;
        var use = _sourceType.SelectedIndex;
        _contactFolder.IsVisible = _recursive.IsVisible = use == 1; _contactFiles.IsVisible = use == 0;
        _sources.Clear(); if (use == 2) AddOpen(); else if (use == 1 && _folder is { } folder) _ = LoadFolder(folder); else RefreshSources();
    }

    private async Task LoadFolder(string folder)
    {
        _folder = folder; _folderPath.Text = folder;
        var recursive = _recursive.IsChecked == true;
        var paths = await Task.Run(() => FileAutomation.EnumerateImages(folder, recursive));
        _sources.Clear();
        foreach (var path in paths) _sources.Add(new AutomationSource(Path.GetFileName(path), path));
        RefreshSources();
    }

    private void BuildContactSettings()
    {
        Control Field(Control field, double width) { field.Width = width; field.HorizontalAlignment = HorizontalAlignment.Left; return field; }
        const double label = 90;
        var resolution = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Field(_resolution, 100), Field(_resolutionUnits, 150) } };
        var documentLeft = new StackPanel { Spacing = 6, Children = { DialogKit.Row("Units", Field(_units, 150), label), DialogKit.Row("Width", Field(_width, 100), label), DialogKit.Row("Height", Field(_height, 100), label), DialogKit.Row("Resolution", resolution, label), DialogKit.Row("Color Profile", Field(_profile, 210), label) } };
        var documentRight = new StackPanel { Spacing = 6, Children = { _flatten, DialogKit.Row("Mode", Field(_mode, 150), 76), DialogKit.Row("Bit Depth", Field(_depth, 150), 76) } };
        _body.Children.Add(DialogKit.Group("Document", DialogKit.Columns("*,*", 16, documentLeft, documentRight)));
        documentLeft.VerticalAlignment = documentRight.VerticalAlignment = VerticalAlignment.Top;
        Control Spacing(NumericUpDown value, TextBlock unit) => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Field(value, 90), unit } };
        _horizontal.Value /= (decimal)UnitFactor(_units.SelectedIndex); _vertical.Value /= (decimal)UnitFactor(_units.SelectedIndex);
        var left = new StackPanel { Spacing = 6, Children = { DialogKit.Row("Place", Field(_order, 150), label), DialogKit.Row("Columns", Field(_columns, 74), label), DialogKit.Row("Rows", Field(_rows, 74), label), _rotate } };
        var right = new StackPanel { Spacing = 6, Children = { _automatic, DialogKit.Row("Vertical", Spacing(_vertical, _verticalUnit), 76), DialogKit.Row("Horizontal", Spacing(_horizontal, _horizontalUnit), 76) } };
        _body.Children.Add(DialogKit.Group("Thumbnails", DialogKit.Columns("*,*", 16, left, right)));
        left.VerticalAlignment = right.VerticalAlignment = VerticalAlignment.Top;
        _font.ItemsSource = FontCatalog.Families;
        _font.SelectionChanged += (_, _) => RefreshCaptionStyles();
        _font.SelectedItem = FontCatalog.Families.FirstOrDefault(name => name == "Arial") ?? FontCatalog.Families.FirstOrDefault();
        _captions.Content = null; _captions.MinWidth = 0;
        var caption = DialogKit.Columns("Auto,Auto,2*,1.4*,56,Auto", 8, _captions, DialogKit.Label("Font"), _font, _fontStyle, _fontSize, Label("pt"));
        _body.Children.Add(DialogKit.Group("Use Filename as Caption", caption));
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
                if (_kind == AutomationKind.ContactSheet) { await LoadFolder(folder); return; }
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
    private void UpdateAvailability()
    {
        _execute.IsEnabled = !_running && _sources.Count >= (_kind is AutomationKind.Statistics or AutomationKind.Photomerge or AutomationKind.HdrPro ? 2 : 1)
            && (_kind != AutomationKind.LensCorrection || _destination is not null && (_matchBest.IsChecked == true || _lensProfile.SelectedItem is not null));
        var selected = _list.SelectedItems?.Count > 0;
        if (_remove is not null) _remove.IsEnabled = selected; if (_duplicate is not null) _duplicate.IsEnabled = selected; if (_sort is not null) _sort.IsEnabled = _sources.Count > 1;
    }

    private async Task ChooseDestination()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Localize.Text("Destination Folder"), AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } folder) return;
        _destination = folder; _lensFolder.Text = folder; UpdateAvailability();
    }

    /// <summary>Lens Correction's destination, profile and options as the panel shows them.</summary>
    internal LensJob? LensChoices() => _destination is null ? null : new LensJob(_destination,
        _lensFormat.SelectedIndex switch { 3 => ExportFormat.Tiff, 0 => ExportFormat.Jpeg, 1 => ExportFormat.Png, _ => null },
        _matchBest.IsChecked == true ? null : _lensProfile.SelectedItem as string,
        new Compositor.Core.Document.LensCorrectionOptions(_geometric.IsChecked == true, _aberration.IsChecked == true, _lensVignette.IsChecked == true,
            _autoScale.IsChecked == true, _lensEdge.SelectedIndex switch { 0 => Compositor.Core.Document.LensEdge.EdgeExtension, 2 => Compositor.Core.Document.LensEdge.Black, 3 => Compositor.Core.Document.LensEdge.White, _ => Compositor.Core.Document.LensEdge.Transparency }));
    internal void SetDestination(string folder) { _destination = folder; _lensFolder.Text = folder; UpdateAvailability(); }
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
                _autoAlign.IsChecked == true, _createSmartObject.IsChecked == true, PhotomergeChoices(), LensChoices());
            await _run(job, _cancellation.Token);
            _running = false; Close(true);
        }
        catch (OperationCanceledException) { _status.Text = Localize.Text("Canceled."); }
        catch (Exception error) { _status.Text = error.Message; }
        finally { _running = false; _body.IsEnabled = true; _cancel.IsEnabled = true; _cancellation?.Dispose(); _cancellation = null; UpdateAvailability(); }
    }
    /// <summary>The layout and options the Photomerge panel shows.</summary>
    internal PhotomergeOptions PhotomergeChoices() => new(
        (PhotomergeLayout)Math.Max(0, Array.FindIndex(_layouts, r => r.IsChecked == true)), _blendImages.IsChecked == true,
        _vignette.IsChecked == true, _distortion.IsChecked == true, _fillTransparent.IsChecked == true);

    /// <summary>A small picture of each layout: how the frame of the merged photo bends around the picture in its middle.</summary>
    internal static Control LayoutIcon(string layout)
    {
        var canvas = new Canvas { Width = 92, Height = 60, Margin = new Thickness(26, 0, 0, 6) };
        Avalonia.Controls.Shapes.Path Shape(string data, Avalonia.Media.IBrush fill) => new() { Data = Avalonia.Media.Geometry.Parse(data), Fill = fill };
        var back = layout switch
        {
            "Perspective" => "M0,4 L28,14 L64,14 L92,4 L92,56 L64,46 L28,46 L0,56 Z",
            "Cylindrical" => "M2,10 Q24,0 46,8 Q68,0 90,10 L90,50 Q68,60 46,52 Q24,60 2,50 Z",
            "Spherical" => "M2,2 Q46,20 90,2 Q74,30 90,58 Q46,40 2,58 Q18,30 2,2 Z",
            "Collage" => "M4,16 L34,8 L88,8 L88,36 L60,50 L4,44 Z",
            "Reposition" => "M2,6 L90,6 L90,54 L66,54 L60,42 L32,42 L26,54 L2,54 Z",
            _ => "M4,6 L88,6 L88,54 L4,54 Z",
        };
        canvas.Children.Add(Shape(back, layout == "Collage" ? Skin.LabelBrush : Skin.AccentBrush));
        canvas.Children.Add(Shape(layout == "Collage" ? "M18,26 L34,18 L74,18 L74,32 L54,40 L18,36 Z" : "M24,16 L68,16 L68,44 L24,44 Z", layout == "Collage" ? Skin.AccentBrush : Skin.LabelBrush));
        canvas.Children.Add(Shape("M28,42 L38,28 L44,34 L52,24 L64,42 Z", Skin.ChromeBrush));
        if (layout == "Auto")
            foreach (var corner in new[] { "M6,8 L16,8 L6,18 Z", "M86,8 L76,8 L86,18 Z", "M6,52 L16,52 L6,42 Z", "M86,52 L76,52 L86,42 Z" })
                canvas.Children.Add(Shape(corner, Skin.LabelBrush));
        return canvas;
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
    internal void LensCorrectionSelfCheck(Action pump)
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        Check(!_execute.IsEnabled && LensChoices() is null, "Lens Correction ran without a destination.");
        SetDestination(Path.GetTempPath()); pump();
        var choices = LensChoices()!;
        Check(choices.Format is null && choices.Lens is null && choices.Options.Distortion && choices.Options.AutoScale && !choices.Options.Vignette,
            "Lens Correction did not open on PSD, Match Best Profile, Geometric Distortion and Auto Scale Image.");
        Check(!_lensProfile.IsEnabled && _lensProfile.ItemCount > 1000, "The lens profiles were not listed.");
        _matchBest.IsChecked = false; pump();
        Check(_lensProfile.IsEnabled && !_execute.IsEnabled, "Choosing a profile did not wait for one to be selected.");
        _lensProfile.SelectedIndex = 0; _lensFormat.SelectedIndex = 1; pump();
        Check(LensChoices() is { Format: ExportFormat.Png, Lens: not null }, "The chosen profile or file type did not reach the job.");
        _matchBest.IsChecked = true; pump();
    }

    internal void PhotomergeSelfCheck(Action pump)
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        Check(PhotomergeChoices() == new PhotomergeOptions(), "Photomerge did not open on Auto with Blend Images.");
        _layouts[2].IsChecked = true; _blendImages.IsChecked = false; _vignette.IsChecked = true; pump();
        var chosen = PhotomergeChoices();
        Check(chosen.Layout == PhotomergeLayout.Cylindrical && !chosen.BlendImages && chosen.VignetteRemoval, "Photomerge's choices did not reach the job.");
        Check(!_execute.IsEnabled, "Photomerge ran with fewer than two photos.");
        _layouts[0].IsChecked = true; _blendImages.IsChecked = true; _vignette.IsChecked = false; pump();
    }

    internal void PdfPresentationSelfCheck(Action pump)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        pump();
        Check(_presentationOptions?.IsEnabled == false && !_pdfExtension.IsEnabled, "PDF presentation controls are enabled for a multi-page document.");
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
