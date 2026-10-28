using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Compositor.Core.IO;
using System.Text.Json;

namespace Compositor.Desktop;

/// <summary>Batch and image processor share one validated, serializable export recipe.</summary>
internal sealed class BatchDialog : DialogWindow
{
    private readonly IReadOnlyList<AutomationSource> _open;
    private readonly Func<IReadOnlyList<AutomationSource>, BatchOptions, IProgress<BatchProgress>, CancellationToken, Task<BatchResult>> _run;
    private readonly List<AutomationSource> _sources = [];
    private readonly ListBox _list = new() { MinHeight = 155, SelectionMode = SelectionMode.Multiple, Tag = "Batch sources" };
    private readonly ComboBox _source = Choice(["Files", "Folder", "Open Documents"], 0);
    private readonly CheckBox _recursive = Check("Include Subfolders", false);
    private readonly ComboBox _action = Choice(["None", "Auto Color", "Auto Tone", "Auto Contrast", "Vibrance", "Desaturate", "Equalize"], 0);
    private readonly TextBox _destination = new() { IsReadOnly = true, Tag = "Batch destination" };
    private readonly ComboBox _errors = Choice(["Stop on Error", "Log Errors and Continue"], 0);
    private readonly ComboBox _metadata = Choice(["None", "Copyright", "Copyright and Contact", "All Except Camera Information", "All"], 2);
    private readonly NumericUpDown _serial = Number(1, 0, int.MaxValue - 10000);
    private readonly List<(ComboBox Part, TextBox Text)> _names = [];
    private readonly List<OutputRow> _outputs = [];
    private readonly StackPanel _body = new() { Spacing = 12 };
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 55 };
    private readonly TextBox _report = new() { IsReadOnly = true, AcceptsReturn = true, MinHeight = 85, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly Button _execute;
    private readonly Button _load;
    private readonly Button _save;
    private readonly Button _shortcut;
    private CancellationTokenSource? _cancellation;

    private sealed record SourceRow(AutomationSource Source) { public override string ToString() => Source.Name; }
    private sealed record OutputRow(ExportFormat Format, CheckBox Enabled, NumericUpDown Quality, CheckBox Resize,
        NumericUpDown Width, NumericUpDown Height, CheckBox NoEnlarge, CheckBox Srgb);

    public BatchDialog(bool processor, IReadOnlyList<AutomationSource> open,
        Func<IReadOnlyList<AutomationSource>, BatchOptions, IProgress<BatchProgress>, CancellationToken, Task<BatchResult>> run, bool shortcutMode = false)
    {
        _open = open; _run = run;
        Title = Localize.Text(shortcutMode ? "Create Droplet" : processor ? "Image Processor" : "Batch");
        Width = 1060; Height = 830; MinWidth = 750; MinHeight = 480;
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,170"), Margin = new Thickness(16) };
        root.Children.Add(new ScrollViewer { Content = _body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        var actions = new StackPanel { Spacing = 12, Margin = new Thickness(18,8,0,0) };
        Grid.SetColumn(actions,1); root.Children.Add(actions); Content = root;
        _execute = FormDialog.ActionButton(shortcutMode ? "Create…" : "Run", () => _ = shortcutMode ? CreateShortcut() : Execute()); _execute.Tag = "Run batch";
        actions.Children.Add(_execute);
        actions.Children.Add(FormDialog.ActionButton("Cancel", Cancel));
        _load = FormDialog.ActionButton("Load…", () => _ = LoadRecipe());
        _save = FormDialog.ActionButton("Save Settings…", () => _ = SaveRecipe());
        _shortcut = FormDialog.ActionButton("Create Droplet…", () => _ = CreateShortcut());
        _shortcut.IsVisible = OperatingSystem.IsWindows() && !shortcutMode;
        actions.Children.Add(_load); actions.Children.Add(_save); actions.Children.Add(_shortcut); actions.Children.Add(_status);

        var inputColumn = processor ? _body : new StackPanel { Spacing = 12, Margin = new Thickness(0,0,12,0) };
        var outputColumn = processor ? _body : new StackPanel { Spacing = 12 };
        if (!processor)
        {
            var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
            columns.Children.Add(inputColumn); Grid.SetColumn(outputColumn,1); columns.Children.Add(outputColumn);
            _body.Children.Add(columns);
        }

        var source = new StackPanel { Spacing = 7 };
        source.Children.Add(Row("Use", _source)); source.Children.Add(_recursive); source.Children.Add(_list);
        var sourceButtons = new WrapPanel { Orientation = Orientation.Horizontal };
        sourceButtons.Children.Add(FormDialog.ActionButton("Browse…", () => _ = Browse()));
        sourceButtons.Children.Add(FormDialog.ActionButton("Add Open Documents", AddOpen));
        sourceButtons.Children.Add(FormDialog.ActionButton("Remove", RemoveSelected));
        sourceButtons.Children.Add(FormDialog.ActionButton("Sort by Name", () => { _sources.Sort((a,b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name,b.Name)); RefreshSources(); }));
        source.Children.Add(sourceButtons);
        if (!shortcutMode) inputColumn.Children.Add(Section("Source Images",source));
        else
        {
            inputColumn.Children.Add(Label("Drag image files or folders onto the saved shortcut to run this recipe."));
            source.Children.Remove(_recursive);
            inputColumn.Children.Add(_recursive);
        }
        _source.SelectionChanged += (_,_) => { _recursive.IsEnabled = _source.SelectedIndex == 1; if (_source.SelectedIndex == 2) AddOpen(); };
        _recursive.IsEnabled = shortcutMode;

        var play = new StackPanel { Spacing = 7 }; play.Children.Add(Row("Action",_action));
        inputColumn.Children.Add(Section("Play",play));
        var target = new StackPanel { Spacing = 7 };
        target.Children.Add(_destination); target.Children.Add(FormDialog.ActionButton("Select Output Folder…", () => _ = ChooseDestination()));
        target.Children.Add(Row("Errors",_errors));
        target.Children.Add(Label("Existing files are preserved. Each format uses a separate folder when several formats are selected."));
        outputColumn.Children.Add(Section("Destination",target));

        foreach (var format in processor ? new[] { ExportFormat.Jpeg, ExportFormat.Png, ExportFormat.Tiff }
            : new[] { ExportFormat.Png, ExportFormat.Jpeg, ExportFormat.Tiff, ExportFormat.Gif, ExportFormat.Exr, ExportFormat.Pdf })
            AddOutput(format, processor ? format == ExportFormat.Jpeg : format == ExportFormat.Png, inputColumn);

        var naming = new StackPanel { Spacing = 6 };
        for (var i = 0; i < 6; i++)
        {
            var part = Choice(["None", "Document Name", "Document Name (lowercase)", "Document Name (UPPERCASE)", "1 Digit Serial Number", "2 Digit Serial Number", "3 Digit Serial Number", "4 Digit Serial Number", "Date", "Custom Text"], i == 0 ? 1 : 0);
            var literal = new TextBox { IsEnabled = false, MaxLength = 100 };
            part.SelectionChanged += (_,_) => literal.IsEnabled = part.SelectedIndex == 9;
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
            line.Children.Add(part); Grid.SetColumn(literal,1); line.Children.Add(literal); naming.Children.Add(line); _names.Add((part,literal));
        }
        naming.Children.Add(Row("Starting Serial Number",_serial));
        naming.Children.Add(Label("The file extension is added automatically. Invalid filename characters are replaced."));
        outputColumn.Children.Add(Section("File Naming",naming));
        outputColumn.Children.Add(Row("Metadata", _metadata));
        _body.Children.Add(Label("Images are processed from a composite copy; original documents and files are preserved."));
        _body.Children.Add(_report);
        Closing += (_,e) => { if (_cancellation is not null) { e.Cancel = true; _cancellation.Cancel(); } };
        KeyDown += (_,e) => { if (e.Key == Avalonia.Input.Key.Escape) { Cancel(); e.Handled = true; } };
    }

    private void AddOutput(ExportFormat format, bool enabled, StackPanel container)
    {
        var row = new OutputRow(format,Check("Save as " + format.ToString().ToUpperInvariant(),enabled),Number(90,1,100),
            Check("Resize to Fit",false),Number(1920,1,30000),Number(1080,1,30000),Check("Do Not Enlarge",true),Check("Convert to sRGB",false));
        _outputs.Add(row);
        var panel = new StackPanel { Spacing = 6 }; panel.Children.Add(row.Enabled);
        var fields = new StackPanel { Spacing = 6 };
        if (format == ExportFormat.Jpeg) fields.Children.Add(Row("Quality",row.Quality));
        fields.Children.Add(row.Resize);
        fields.Children.Add(Row("Width",row.Width)); fields.Children.Add(Row("Height",row.Height));
        fields.Children.Add(row.NoEnlarge); fields.Children.Add(row.Srgb); panel.Children.Add(fields);
        void Update()
        {
            fields.IsEnabled = row.Enabled.IsChecked == true;
            row.Quality.IsEnabled = format == ExportFormat.Jpeg;
            row.Width.IsEnabled = row.Height.IsEnabled = row.NoEnlarge.IsEnabled = row.Resize.IsChecked == true;
        }
        row.Enabled.IsCheckedChanged += (_,_) => Update(); row.Resize.IsCheckedChanged += (_,_) => Update(); Update();
        var section = Section(format.ToString().ToUpperInvariant(),panel);
        if (container == _body) container.Children.Add(section);
        else container.Children.Add(new Expander { Header = format.ToString().ToUpperInvariant(), IsExpanded = enabled, Content = section });
    }

    private void AddOpen()
    {
        foreach (var source in _open) if (!_sources.Any(s => ReferenceEquals(s.Document,source.Document) && s.Document is not null)) _sources.Add(source);
        RefreshSources();
    }
    private void RemoveSelected()
    {
        foreach (var row in _list.SelectedItems?.OfType<SourceRow>().ToArray() ?? []) _sources.Remove(row.Source);
        RefreshSources();
    }
    private void RefreshSources() => _list.ItemsSource = _sources.Select(s => new SourceRow(s)).ToArray();
    private void AddPath(string path)
    {
        if (!_sources.Any(s => s.Document is null && string.Equals(s.Path,path,StringComparison.OrdinalIgnoreCase)))
            _sources.Add(new AutomationSource(Path.GetFileName(path),path));
    }
    private async Task Browse()
    {
        try
        {
            if (_source.SelectedIndex == 2) { AddOpen(); return; }
            if (_source.SelectedIndex == 1)
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Localize.Text("Source Folder"), AllowMultiple = false });
                if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
                var recursive = _recursive.IsChecked == true;
                var paths = await Task.Run(() => FileAutomation.EnumerateImages(path,recursive));
                foreach (var file in paths) AddPath(file);
            }
            else
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Source Images"), AllowMultiple = true,
                    FileTypeFilter = [new FilePickerFileType(Localize.Text("Images")) { Patterns = ImageImporter.Extensions.Select(e => "*" + e).ToArray() }] });
                foreach (var file in files) if (file.TryGetLocalPath() is { } path) AddPath(path);
            }
            RefreshSources();
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private async Task ChooseDestination()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Localize.Text("Destination"), AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) _destination.Text = path;
    }

    private BatchOptions Options() => new(_destination.Text ?? "", (BatchAction)_action.SelectedIndex,
        _outputs.Where(r => r.Enabled.IsChecked == true).Select(r => new BatchOutput(r.Format,(int)(r.Quality.Value ?? 90),r.Resize.IsChecked == true,
            (int)(r.Width.Value ?? 1920),(int)(r.Height.Value ?? 1080),r.NoEnlarge.IsChecked == true,r.Srgb.IsChecked == true)).ToArray(),
        _names.Where(n => n.Part.SelectedIndex > 0).Select(n => new FileNameComponent((FileNamePart)(n.Part.SelectedIndex - 1),n.Text.Text ?? "")).ToArray(),
        (int)(_serial.Value ?? 1),_errors.SelectedIndex == 1, (MetadataPolicy)_metadata.SelectedIndex);

    private async Task Execute()
    {
        if (_cancellation is not null) return;
        try
        {
            if (_sources.Count == 0) throw new ArgumentException(Localize.Text("Add at least one source image."));
            var options = Options(); BatchProcessing.Validate(options);
            _cancellation = new CancellationTokenSource(); _body.IsEnabled = _execute.IsEnabled = _load.IsEnabled = _save.IsEnabled = _shortcut.IsEnabled = false;
            _report.Text = "";
            var progress = new Progress<BatchProgress>(p => _status.Text = $"{p.Completed}/{p.Total}\n{p.Name}");
            var result = await _run(_sources.ToArray(),options,progress,_cancellation.Token);
            _status.Text = Localize.Text("Completed") + $": {result.Succeeded}\n" + Localize.Text("Errors") + $": {result.Failed}";
            _report.Text = string.Join(Environment.NewLine,result.Items.Select(i => i.Source + ": " + (i.Error ?? string.Join(", ",i.Files))));
            if (result.Failed > 0 && options.ContinueOnError)
            {
                var log = BatchProcessing.UniquePath(options.OutputDirectory,"batch-errors","json");
                await File.WriteAllTextAsync(log,JsonSerializer.Serialize(result,new JsonSerializerOptions { WriteIndented = true }));
                _report.Text += Environment.NewLine + log;
            }
        }
        catch (OperationCanceledException) { _status.Text = Localize.Text("Canceled. Completed files have been preserved."); }
        catch (Exception ex) { _status.Text = ex.Message; }
        finally
        {
            _cancellation?.Dispose(); _cancellation = null; _body.IsEnabled = _execute.IsEnabled = _load.IsEnabled = _save.IsEnabled = _shortcut.IsEnabled = true;
        }
    }
    private void Cancel() { if (_cancellation is not null) _cancellation.Cancel(); else Close(); }

    private async Task SaveRecipe()
    {
        try
        {
            var options = Options(); BatchProcessing.Validate(options);
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Localize.Text("Save Settings…"), SuggestedFileName = "batch-settings.json", DefaultExtension = "json", ShowOverwritePrompt = true });
            if (file?.TryGetLocalPath() is { } path) new BatchRecipe(1,options,_recursive.IsChecked == true).Save(path);
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private async Task LoadRecipe()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Load…"), AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }] });
            if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            ApplyRecipe(BatchRecipe.Read(path));
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private void ApplyRecipe(BatchRecipe recipe)
    {
            var options = recipe.Options; BatchProcessing.Validate(options);
            _recursive.IsChecked = recipe.IncludeSubfolders;
            if (options.Outputs.Any(o => !_outputs.Any(r => r.Format == o.Format))) throw new InvalidDataException(Localize.Text("This recipe uses formats available in the Batch dialog."));
            _destination.Text = options.OutputDirectory; _action.SelectedIndex = (int)options.Action; _errors.SelectedIndex = options.ContinueOnError ? 1 : 0; _serial.Value = options.StartSerial;
            _metadata.SelectedIndex = (int)options.Metadata;
            for (var i = 0; i < _names.Count; i++)
            {
                var value = options.Naming.ElementAtOrDefault(i);
                _names[i].Part.SelectedIndex = value is null ? 0 : (int)value.Part + 1; _names[i].Text.Text = value?.Text ?? "";
            }
            foreach (var row in _outputs)
            {
                var value = options.Outputs.FirstOrDefault(o => o.Format == row.Format); row.Enabled.IsChecked = value is not null;
                if (value is null) continue;
                row.Quality.Value = value.Quality; row.Resize.IsChecked = value.Resize; row.Width.Value = value.MaximumWidth; row.Height.Value = value.MaximumHeight;
                row.NoEnlarge.IsChecked = value.NoEnlarge; row.Srgb.IsChecked = value.ConvertToSrgb;
            }
            _status.Text = "";
    }
    internal async Task RunRecipe(string recipePath, IReadOnlyList<string> inputs)
    {
        try
        {
            var recipe = BatchRecipe.Read(recipePath); ApplyRecipe(recipe);
            foreach (var input in inputs)
            {
                if (Directory.Exists(input))
                    foreach (var path in await Task.Run(() => FileAutomation.EnumerateImages(input,recipe.IncludeSubfolders))) AddPath(path);
                else AddPath(input);
            }
            RefreshSources();
            if (_sources.Count > 0) await Execute();
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private async Task CreateShortcut()
    {
        if (!OperatingSystem.IsWindows() || _cancellation is not null) return;
        try
        {
            var recipe = new BatchRecipe(1,Options(),_recursive.IsChecked == true); BatchProcessing.Validate(recipe.Options);
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Localize.Text("Create Droplet…"),
                SuggestedFileName = "Compositor Batch.lnk", DefaultExtension = "lnk", ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType(Localize.Text("Windows Shortcut")) { Patterns = ["*.lnk"] }] });
            if (file?.TryGetLocalPath() is not { } path) return;
            if (!path.EndsWith(".lnk",StringComparison.OrdinalIgnoreCase)) path += ".lnk";
            var settings = path + "." + Guid.NewGuid().ToString("N") + ".batch.json";
            recipe.Save(settings);
            try { BatchShortcut.Create(path,settings); }
            catch { File.Delete(settings); throw; }
            _status.Text = Localize.Text("Drop images or folders on the shortcut to run this batch. Keep its settings file beside it.");
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private static ComboBox Choice(string[] items,int selected) => new() { ItemsSource = items.Select(Localize.Text).ToArray(), SelectedIndex = selected, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static CheckBox Check(string text,bool value) => new() { Content = Localize.Text(text), IsChecked = value };
    private static NumericUpDown Number(decimal value,decimal min,decimal max) => new() { Value = value, Minimum = min, Maximum = max, Increment = 1, FormatString = "0", HorizontalAlignment = HorizontalAlignment.Stretch };
    private static TextBlock Label(string text) => new() { Text = Localize.Text(text), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private static Control Row(string label,Control field)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("180,*") }; grid.Children.Add(Label(label)); Grid.SetColumn(field,1); grid.Children.Add(field); return grid;
    }
    private static Control Section(string title,Control contents) => new Border
    {
        BorderThickness = new Thickness(1), BorderBrush = Skin.BorderControlBrush, Padding = new Thickness(12),
        Child = new StackPanel { Spacing = 8, Children = { Label(title),contents } }
    };
}
