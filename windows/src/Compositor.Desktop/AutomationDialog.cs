using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Compositor.Core.IO;
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
    private readonly NumericUpDown _columns = Number(5, 1, 100, "Columns");
    private readonly NumericUpDown _rows = Number(6, 1, 100, "Rows");
    private readonly NumericUpDown _horizontal = Number(10, 0, 30000, "Horizontal spacing");
    private readonly NumericUpDown _vertical = Number(10, 0, 30000, "Vertical spacing");
    private readonly CheckBox _automatic = Check("Use Auto-Spacing", true);
    private readonly CheckBox _flatten = Check("Flatten All Layers", true);
    private readonly CheckBox _rotate = Check("Rotate for Best Fit", false);
    private readonly CheckBox _captions = Check("Use Filename as Caption", true);
    private readonly TextBox _font = new() { Text = "Arial", MinWidth = 160, Tag = "Caption font" };
    private readonly NumericUpDown _fontSize = Number(12, 1, 300, "Caption size");
    private readonly ComboBox _order = Choice(["Across First", "Down First"], 0, "Thumbnail order");
    private readonly ComboBox _statistic = Choice(["Mean", "Sum", "Minimum", "Maximum", "Median", "Variance", "Standard Deviation", "Skewness", "Kurtosis", "Range", "Entropy"], 0, "Stack mode");
    private readonly ComboBox _background = Choice(["White", "Gray", "Black"], 0, "PDF background");
    private readonly CheckBox _fileNames = Check("File Names", false);
    private int _previousUnits = 1;

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
            actions.Children.Add(FormDialog.ActionButton("Reset…", () => ApplySettings(new ContactSheetOptions())));
        }
        actions.Children.Add(_status);
        if (kind == AutomationKind.Statistics) _body.Children.Add(Row("Stack Mode", _statistic));
        var sourcePanel = SourcesPanel();
        if (kind == AutomationKind.PdfPresentation)
        {
            var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 16 };
            columns.Children.Add(Group("Source Files", sourcePanel));
            var pdf = new StackPanel { Spacing = 12 };
            pdf.Children.Add(Label("Multi-Page Document"));
            pdf.Children.Add(Row("Background", _background)); pdf.Children.Add(Row("Font Size", _fontSize)); pdf.Children.Add(_fileNames);
            var options = Group("Output Options", pdf); Grid.SetColumn(options, 1); columns.Children.Add(options); _body.Children.Add(columns);
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
        var document = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 20 };
        var left = new StackPanel { Spacing = 10 }; var right = new StackPanel { Spacing = 10 }; Grid.SetColumn(right, 1);
        left.Children.Add(Row("Units", _units)); left.Children.Add(Row("Width", _width)); left.Children.Add(Row("Height", _height)); left.Children.Add(Row("Resolution (ppi)", _resolution));
        right.Children.Add(_flatten); right.Children.Add(Label("RGB Color · 8 Bits/Channel"));
        document.Children.Add(left); document.Children.Add(right); _body.Children.Add(Group("Document", document));
        var thumbnails = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 20 };
        var a = new StackPanel { Spacing = 10 }; var b = new StackPanel { Spacing = 10 }; Grid.SetColumn(b, 1);
        a.Children.Add(Row("Place", _order)); a.Children.Add(Row("Columns", _columns)); a.Children.Add(Row("Rows", _rows)); a.Children.Add(_rotate);
        b.Children.Add(_automatic); b.Children.Add(Row("Vertical (px)", _vertical)); b.Children.Add(Row("Horizontal (px)", _horizontal));
        thumbnails.Children.Add(a); thumbnails.Children.Add(b); _body.Children.Add(Group("Thumbnails", thumbnails));
        var caption = new StackPanel { Spacing = 10 }; caption.Children.Add(_captions); caption.Children.Add(Row("Font", _font)); caption.Children.Add(Row("Size (pt)", _fontSize));
        _body.Children.Add(Group("Caption", caption));
        _automatic.IsCheckedChanged += (_, _) => { _horizontal.IsEnabled = _vertical.IsEnabled = _automatic.IsChecked != true; };
        _horizontal.IsEnabled = _vertical.IsEnabled = false;
        _captions.IsCheckedChanged += (_, _) => { _font.IsEnabled = _fontSize.IsEnabled = _captions.IsChecked == true; };
        _units.SelectionChanged += (_, _) =>
        {
            var next = _units.SelectedIndex; if (next < 0 || next == _previousUnits) return;
            double oldFactor = UnitFactor(_previousUnits), newFactor = UnitFactor(next);
            _width.Value = (decimal)Math.Clamp(Value(_width) * oldFactor / newFactor, .01, 30000);
            _height.Value = (decimal)Math.Clamp(Value(_height) * oldFactor / newFactor, .01, 30000); _previousUnits = next;
        };
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
    private double UnitFactor(int index) => index switch { 0 => Value(_resolution), 1 => Value(_resolution) / 2.54, _ => 1 };
    private ContactSheetOptions ContactOptions()
    {
        int width = (int)Math.Round(Value(_width) * UnitFactor(_units.SelectedIndex)), height = (int)Math.Round(Value(_height) * UnitFactor(_units.SelectedIndex));
        int gap = _automatic.IsChecked == true ? Math.Max(1, (int)Math.Round(Value(_resolution) / 30)) : -1;
        return new ContactSheetOptions(width, height, Value(_resolution), (int)Value(_columns), (int)Value(_rows), _order.SelectedIndex == 1,
            gap >= 0 ? gap : (int)Value(_horizontal), gap >= 0 ? gap : (int)Value(_vertical), _rotate.IsChecked == true,
            _captions.IsChecked == true, _font.Text ?? "Arial", Value(_fontSize), _flatten.IsChecked == true);
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
                new PdfPresentationOptions(_background.SelectedIndex switch { 1 => SKColors.Gray, 2 => SKColors.Black, _ => SKColors.White }, Value(_fontSize), _fileNames.IsChecked == true), path,
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
        _units.SelectedIndex = 2; _width.Value = o.Width; _height.Value = o.Height; _resolution.Value = (decimal)o.Resolution;
        _columns.Value = o.Columns; _rows.Value = o.Rows; _horizontal.Value = o.HorizontalSpacing; _vertical.Value = o.VerticalSpacing;
        _automatic.IsChecked = false; _order.SelectedIndex = o.DownFirst ? 1 : 0; _rotate.IsChecked = o.RotateToFit;
        _captions.IsChecked = o.Captions; _font.Text = o.FontFamily; _fontSize.Value = (decimal)o.FontSize; _flatten.IsChecked = o.Flatten;
    }
    internal static double Value(NumericUpDown control) => (double)(control.Value ?? 0);
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
