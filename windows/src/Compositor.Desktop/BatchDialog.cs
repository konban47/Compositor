using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Compositor.Core.IO;
using System.Text.Json;

namespace Compositor.Desktop;

/// <summary>Batch and Create Droplet, laid out as Photoshop's: what to play and on what on the left, where it goes on the right.
/// Both make the same validated, serializable recipe; only a droplet saves it rather than running it.</summary>
internal sealed class BatchDialog : DialogWindow
{
    internal static readonly (string Set, BatchAction[] Actions)[] ActionSets =
    [
        ("Default Actions", [BatchAction.None, BatchAction.AutoColor, BatchAction.AutoTone, BatchAction.AutoContrast, BatchAction.Vibrance, BatchAction.Desaturate, BatchAction.Equalize]),
        ("Image Size", [BatchAction.FitLong1920, BatchAction.FitLong1280, BatchAction.FitLong640]),
    ];
    private static readonly ExportFormat[] Formats = [ExportFormat.Png, ExportFormat.Jpeg, ExportFormat.Tiff, ExportFormat.Gif, ExportFormat.Psd];
    private readonly IReadOnlyList<AutomationSource> _open;
    private readonly Func<IReadOnlyList<AutomationSource>, BatchOptions, IProgress<BatchProgress>, CancellationToken, Task<BatchResult>> _run;
    private readonly bool _droplet;
    private readonly List<AutomationSource> _sources = [];
    private readonly ComboBox _set = Choice(ActionSets.Select(s => s.Set).ToArray(), 0);
    private readonly ComboBox _action = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _source = Choice([DialogKit.FolderWord, "Files", "Opened Files"], 0);
    private readonly Button _chooseSource;
    private readonly TextBlock _sourceText = new() { Foreground = Skin.SecondaryBrush, VerticalAlignment = VerticalAlignment.Center, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
    private readonly CheckBox _overrideOpen = Check("Override Action \"Open\" Commands", false);
    private readonly CheckBox _recursive = Check("Include All Subfolders", false);
    private readonly CheckBox _quietOpen = Check("Suppress File Open Options Dialogs", true);
    private readonly CheckBox _quietColor = Check("Suppress Color Profile Warnings", true);
    private readonly ComboBox _errors = Choice(["Stop for Errors", "Log Errors to File"], 0);
    private readonly Button _errorLogButton;
    private string? _errorLog;
    private readonly ComboBox _destination = Choice(["None", "Save and Close", DialogKit.FolderWord], 0);
    private readonly Button _chooseDestination;
    private readonly TextBlock _destinationText = new() { Foreground = Skin.SecondaryBrush, VerticalAlignment = VerticalAlignment.Center, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, MaxWidth = 260 };
    private string _folder = "";
    private string _dropletPath = "";
    private readonly CheckBox _overrideSave = Check("Override Action \"Save As\" Commands", true);
    private readonly ComboBox _format = Choice(["PNG", "JPEG", "TIFF", "GIF", "PSD"], 0);
    private readonly NumericUpDown _quality = Number(90, 1, 100);
    private readonly ComboBox _metadata = Choice(["None", "Copyright", "Copyright and Contact", "All Except Camera Information", "All"], 2);
    private readonly NumericUpDown _serial = Number(1, 0, int.MaxValue - 10000);
    private readonly TextBlock _example = new() { Foreground = Skin.SecondaryBrush, VerticalAlignment = VerticalAlignment.Center };
    private readonly List<(ComboBox Part, TextBox Text)> _names = [];
    private readonly Control _namingGroup;
    private readonly StackPanel _body = new();
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly Button _execute;
    private CancellationTokenSource? _cancellation;
    /// <summary>What a loaded recipe saves to when that's more than this dialog's one format: kept until the format is changed here.</summary>
    private IReadOnlyList<BatchOutput>? _loadedOutputs;
    private string? _sourceFolder;
    /// <summary>Opens what a batch with no destination made, since it isn't saved anywhere of the person's choosing.</summary>
    internal Action<IReadOnlyList<string>>? ResultsMade { get; init; }
    /// <summary>What the self check reads: true when everything fits without scrolling.</summary>
    internal bool FitsWithoutScrolling => _scroll.Extent.Height <= _scroll.Viewport.Height + 1 && _scroll.Extent.Width <= _scroll.Viewport.Width + 1;

    public BatchDialog(IReadOnlyList<AutomationSource> open,
        Func<IReadOnlyList<AutomationSource>, BatchOptions, IProgress<BatchProgress>, CancellationToken, Task<BatchResult>> run, bool droplet = false)
    {
        _open = open; _run = run; _droplet = droplet;
        Title = Localize.Text(droplet ? "Create Droplet" : "Batch");
        Width = 1010; Height = droplet ? 640 : 580; MinWidth = 900; MinHeight = 520;
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(16, 8, 16, 16) };
        _scroll = new ScrollViewer { Content = _body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        root.Children.Add(_scroll);
        _execute = FormDialog.ActionButton("OK", () => _ = droplet ? CreateDroplet() : Execute()); _execute.Tag = "Run batch";
        var actions = DialogKit.ActionColumn(_execute, FormDialog.ActionButton("Cancel", Cancel));
        if (!droplet)
        {
            actions.Children.Add(new Border { Height = 12 });
            foreach (var button in new[] { FormDialog.ActionButton("Load…", () => _ = LoadRecipe()), FormDialog.ActionButton("Save…", () => _ = SaveRecipe()) })
            { button.Margin = default; button.HorizontalAlignment = HorizontalAlignment.Stretch; actions.Children.Add(button); }
        }
        actions.Children.Add(_status);
        Grid.SetColumn(actions, 1); root.Children.Add(actions); Content = root;

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("420,*"), ColumnSpacing = 14 };
        var left = new StackPanel { Spacing = 6 }; columns.Children.Add(left);
        _body.Children.Add(columns);

        Button Pick(string title, Action action, char key)
        {
            var button = FormDialog.ActionButton(title, action); button.Margin = default; button.Width = 128; button.HorizontalAlignment = HorizontalAlignment.Left; return this.Mnemonic(button, key);
        }
        var play = new StackPanel { Spacing = 6, Children = { DialogKit.Row("Set", _set, 56), DialogKit.Row("Action", _action, 56) } };
        var checks = new Control[] { this.Mnemonic(_overrideOpen, 'R'), this.Mnemonic(_recursive, 'I'), this.Mnemonic(_quietOpen, 'F'), this.Mnemonic(_quietColor, 'P') };
        // This app's actions have no Open step and it asks nothing while it opens a file, so these say what always happens.
        _overrideOpen.IsEnabled = _quietOpen.IsEnabled = _quietColor.IsEnabled = false;
        ToolTip.SetTip(_overrideOpen, Localize.Text("Compositor's actions have no Open step."));
        ToolTip.SetTip(_quietOpen, Localize.Text("Compositor doesn't ask anything while it opens these files."));
        ToolTip.SetTip(_quietColor, Localize.Text("Compositor doesn't ask anything while it opens these files."));
        _set.SelectionChanged += (_, _) => ShowActions();
        ShowActions();
        _chooseSource = Pick("Choose…", () => _ = droplet ? ChooseDroplet() : Browse(), 'C');
        var chooser = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10, Height = 56, Children = { _chooseSource, _sourceText } };
        Grid.SetColumn(_sourceText, 1); _chooseSource.VerticalAlignment = VerticalAlignment.Top; _sourceText.VerticalAlignment = VerticalAlignment.Top; _sourceText.Margin = new Thickness(0, 6, 0, 0); _sourceText.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        if (droplet)
        {
            left.Children.Add(DialogKit.Group("Save Droplet In", chooser));
            _sourceText.Text = Localize.Text("Drag image files or folders onto the saved shortcut to run this recipe.");
            foreach (var check in checks) play.Children.Add(check);
            left.Children.Add(DialogKit.Group("Play", play));
        }
        else
        {
            left.Children.Add(DialogKit.Group("Play", play));
            _source.Width = 150;
            var source = new StackPanel { Spacing = 6, Children = { chooser } };
            foreach (var check in checks) source.Children.Add(check);
            left.Children.Add(DialogKit.Group("", source, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { DialogKit.Label("Source"), _source } }));
            _source.SelectionChanged += (_, _) => { _sources.Clear(); _sourceFolder = null; ShowSources(); if (_source.SelectedIndex == 2) AddOpen(); };
            _recursive.IsCheckedChanged += (_, _) => { if (_sourceFolder is { } folder) _ = LoadFolder(folder); };
        }
        _errors.Width = 190; _errorLogButton = Pick("Save As…", () => _ = ChooseErrorLog(), 'E'); _errorLogButton.IsEnabled = false;
        _errors.SelectionChanged += (_, _) => _errorLogButton.IsEnabled = _errors.SelectedIndex == 1;
        left.Children.Add(DialogKit.Group("", new StackPanel { Children = { _errorLogButton } }, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { DialogKit.Label("Errors"), _errors } }));

        // Right: the destination, and how what's saved there is named.
        _destination.Width = 150;
        _chooseDestination = Pick("Choose…", () => _ = ChooseDestination(), 'H');
        var naming = new StackPanel { Spacing = 4 };
        naming.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { DialogKit.Label("Example"), _example } });
        string[] parts = ["None", "Document Name", "Document Name (lowercase)", "Document Name (UPPERCASE)", "1 Digit Serial Number", "2 Digit Serial Number", "3 Digit Serial Number", "4 Digit Serial Number", "yyyymmdd (date)", "Custom Text"];
        for (var i = 0; i < 6; i++)
        {
            var part = Choice(parts, i == 0 ? 1 : 0); part.Height = 26; part.MinHeight = 0;
            var literal = DialogKit.Line("file-name-text"); literal.IsEnabled = false; literal.MaxLength = 100;
            var plus = new TextBlock { Text = i < 5 ? "+" : "", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Skin.SecondaryBrush };
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,160,14"), ColumnSpacing = 6 };
            line.Children.Add(literal); Grid.SetColumn(part, 1); line.Children.Add(part); Grid.SetColumn(plus, 2); line.Children.Add(plus);
            part.SelectionChanged += (_, _) => { literal.IsEnabled = part.SelectedIndex == 9; if (part.SelectedIndex != 9) literal.Text = part.SelectedIndex == 0 ? "" : part.SelectedItem as string; ShowExample(); };
            literal.Text = i == 0 ? part.SelectedItem as string : ""; literal.TextChanged += (_, _) => ShowExample();
            naming.Children.Add(line); _names.Add((part, literal));
        }
        _serial.Width = 80; _serial.HorizontalAlignment = HorizontalAlignment.Left;
        _serial.ValueChanged += (_, _) => ShowExample();
        naming.Children.Add(DialogKit.Row("Starting Serial Number", _serial, 150));
        CheckBox windows = Check("Windows", true), mac = Check("Mac OS", false), unix = Check("Unix", false);
        windows.IsEnabled = mac.IsEnabled = unix.IsEnabled = false;
        ToolTip.SetTip(windows, Localize.Text("Names are always made safe for Windows, which also suits macOS and Unix."));
        naming.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { DialogKit.Label("Compatibility"), windows, mac, unix } });
        _namingGroup = DialogKit.Group("File Naming", naming);
        _quality.Width = 56; _format.Width = 96; _metadata.Width = 190;
        var saveAs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { DialogKit.Label("Format"), _format, DialogKit.Label("Quality"), _quality, DialogKit.Label("Metadata"), _metadata } };
        var destination = new StackPanel
        {
            Spacing = 6,
            Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { _chooseDestination, _destinationText } }, this.Mnemonic(_overrideSave, 'V'), saveAs, _namingGroup },
        };
        _overrideSave.IsEnabled = false;
        ToolTip.SetTip(_overrideSave, Localize.Text("Compositor's actions have no Save step: this format is what's saved."));
        var right = DialogKit.Group("", destination, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { DialogKit.Label("Destination"), _destination } });
        Grid.SetColumn(right, 1); columns.Children.Add(right);
        _format.SelectionChanged += (_, _) => { _quality.IsEnabled = _format.SelectedIndex == 1; _loadedOutputs = null; ShowExample(); };
        _quality.IsEnabled = false;
        _destination.SelectionChanged += (_, _) => ShowDestination();
        ShowSources(); ShowDestination(); ShowExample();
        Closing += (_, e) => { if (_cancellation is not null) { e.Cancel = true; _cancellation.Cancel(); } };
        KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) { Cancel(); e.Handled = true; } };
    }

    private void ShowActions()
    {
        var actions = ActionSets[Math.Max(0, _set.SelectedIndex)].Actions;
        _action.ItemsSource = actions.Select(a => Localize.Text(ActionName(a))).ToArray(); _action.SelectedIndex = 0;
    }
    internal static string ActionName(BatchAction action) => action switch
    {
        BatchAction.None => "None", BatchAction.FitLong1920 => "Fit Long Side to 1920 px", BatchAction.FitLong1280 => "Fit Long Side to 1280 px", BatchAction.FitLong640 => "Fit Long Side to 640 px",
        _ => ScriptEvents.ActionName(action),
    };
    private BatchAction ChosenAction => ActionSets[Math.Max(0, _set.SelectedIndex)].Actions[Math.Max(0, _action.SelectedIndex)];

    private void ShowSources()
    {
        if (_droplet) { _recursive.IsEnabled = true; UpdateAvailability(); return; }
        _chooseSource.IsEnabled = _source.SelectedIndex != 2;
        _recursive.IsEnabled = _source.SelectedIndex == 0;
        _sourceText.Text = _sources.Count == 0 ? "" : (_sourceFolder is { } folder ? folder + " · " : "") + string.Format(Localize.Text("{0} images"), _sources.Count);
        UpdateAvailability();
    }
    private void ShowDestination()
    {
        var folder = _destination.SelectedIndex == 2;
        _chooseDestination.IsEnabled = folder;
        _destinationText.Text = folder ? _folder : "";
        _namingGroup.IsEnabled = folder;
        _format.IsEnabled = _destination.SelectedIndex != 0;
        UpdateAvailability();
    }
    private void ShowExample()
    {
        var naming = Naming();
        var extension = Formats[Math.Max(0, _format.SelectedIndex)] switch { ExportFormat.Jpeg => "jpg", ExportFormat.Tiff => "tif", ExportFormat.Gif => "gif", ExportFormat.Psd => "psd", _ => "png" };
        _example.Text = naming.Length == 0 ? "" : BatchProcessing.BuildName(Localize.Text("My Document"), (int)(_serial.Value ?? 1), DateTime.Today, naming) + "." + extension;
        UpdateAvailability();
    }
    private void UpdateAvailability()
    {
        var ready = _droplet ? _dropletPath.Length > 0 : _sources.Count > 0;
        var folder = _destination.SelectedIndex != 2 || _folder.Length > 0 && Naming().Length > 0;
        _execute.IsEnabled = _cancellation is null && ready && folder;
    }
    private FileNameComponent[] Naming() => [.. _names.Where(n => n.Part.SelectedIndex > 0).Select(n => new FileNameComponent((FileNamePart)(n.Part.SelectedIndex - 1), n.Part.SelectedIndex == 9 ? n.Text.Text ?? "" : ""))];

    private void AddOpen()
    {
        foreach (var source in _open) if (!_sources.Any(s => ReferenceEquals(s.Document, source.Document) && s.Document is not null)) _sources.Add(source);
        ShowSources();
    }
    private void AddPath(string path)
    {
        if (!_sources.Any(s => s.Document is null && string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase)))
            _sources.Add(new AutomationSource(Path.GetFileName(path), path));
    }
    private async Task LoadFolder(string folder)
    {
        var recursive = _recursive.IsChecked == true;
        var paths = await Task.Run(() => FileAutomation.EnumerateImages(folder, recursive));
        _sources.Clear(); _sourceFolder = folder;
        foreach (var file in paths) AddPath(file);
        ShowSources();
    }
    private async Task Browse()
    {
        try
        {
            if (_source.SelectedIndex == 0)
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Localize.Text("Source Folder"), AllowMultiple = false });
                if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) await LoadFolder(path);
                return;
            }
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Source Images"), AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType(Localize.Text("Images")) { Patterns = ImageImporter.Extensions.Select(e => "*" + e).ToArray() }] });
            foreach (var file in files) if (file.TryGetLocalPath() is { } path) AddPath(path);
            ShowSources();
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private async Task ChooseDestination()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Localize.Text("Destination"), AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) { _folder = path; ShowDestination(); }
    }
    private async Task ChooseErrorLog()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Localize.Text("Save As…"), SuggestedFileName = "Batch Errors.json", DefaultExtension = "json", ShowOverwritePrompt = true });
        if (file?.TryGetLocalPath() is { } path) _errorLog = path;
    }
    private async Task ChooseDroplet()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Localize.Text("Save Droplet In"),
            SuggestedFileName = "Compositor Batch.lnk", DefaultExtension = "lnk", ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType(Localize.Text("Windows Shortcut")) { Patterns = ["*.lnk"] }] });
        if (file?.TryGetLocalPath() is not { } path) return;
        _dropletPath = path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? path : path + ".lnk";
        _sourceText.Text = _dropletPath; UpdateAvailability();
    }

    /// <summary>What the panel shows as a recipe. With no destination the results go to a temporary folder and are opened from there.</summary>
    private BatchOptions Options()
    {
        var destination = _destination.SelectedIndex;
        var format = destination == 0 ? ExportFormat.Png : Formats[Math.Max(0, _format.SelectedIndex)];
        var folder = destination switch { 0 => Path.Combine(Path.GetTempPath(), "Compositor Batch " + Guid.NewGuid().ToString("N")), 1 => "", _ => _folder };
        IReadOnlyList<FileNameComponent> naming = destination == 2 ? Naming() : [new FileNameComponent(FileNamePart.DocumentName)];
        return new BatchOptions(folder, ChosenAction, _loadedOutputs ?? [new BatchOutput(format, (int)(_quality.Value ?? 90))], naming,
            (int)(_serial.Value ?? 1), _errors.SelectedIndex == 1, (MetadataPolicy)_metadata.SelectedIndex, SaveBesideSource: destination == 1);
    }

    private async Task Execute()
    {
        if (_cancellation is not null) return;
        try
        {
            if (_sources.Count == 0) throw new ArgumentException(Localize.Text("Add at least one source image."));
            var options = Options(); BatchProcessing.Validate(options);
            _cancellation = new CancellationTokenSource(); _body.IsEnabled = false; UpdateAvailability();
            var progress = new Progress<BatchProgress>(p => _status.Text = $"{p.Completed}/{p.Total}\n{p.Name}");
            var result = await _run(_sources.ToArray(), options, progress, _cancellation.Token);
            _status.Text = Localize.Text("Completed") + $": {result.Succeeded}\n" + Localize.Text("Errors") + $": {result.Failed}";
            if (result.Failed > 0 && options.ContinueOnError)
            {
                var log = _errorLog ?? BatchProcessing.UniquePath(options.OutputDirectory.Length > 0 ? options.OutputDirectory : Path.GetTempPath(), "batch-errors", "json");
                await File.WriteAllTextAsync(log, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                _status.Text += "\n" + log;
            }
            if (_destination.SelectedIndex == 0) ResultsMade?.Invoke([.. result.Items.SelectMany(i => i.Files)]);
        }
        catch (OperationCanceledException) { _status.Text = Localize.Text("Canceled. Completed files have been preserved."); }
        catch (Exception ex) { _status.Text = ex.Message; }
        finally { _cancellation?.Dispose(); _cancellation = null; _body.IsEnabled = true; UpdateAvailability(); }
    }
    private void Cancel() { if (_cancellation is not null) _cancellation.Cancel(); else Close(); }

    private async Task SaveRecipe()
    {
        try
        {
            var options = Options(); BatchProcessing.Validate(options);
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Localize.Text("Save Settings…"), SuggestedFileName = "batch-settings.json", DefaultExtension = "json", ShowOverwritePrompt = true });
            if (file?.TryGetLocalPath() is { } path) new BatchRecipe(1, options, _recursive.IsChecked == true).Save(path);
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
        _folder = options.OutputDirectory; _destination.SelectedIndex = options.SaveBesideSource ? 1 : 2;
        var set = Array.FindIndex(ActionSets, s => s.Actions.Contains(options.Action));
        _set.SelectedIndex = Math.Max(0, set); ShowActions(); _action.SelectedIndex = Math.Max(0, Array.IndexOf(ActionSets[Math.Max(0, set)].Actions, options.Action));
        _errors.SelectedIndex = options.ContinueOnError ? 1 : 0; _serial.Value = options.StartSerial; _metadata.SelectedIndex = (int)options.Metadata;
        for (var i = 0; i < _names.Count; i++)
        {
            var value = options.Naming.ElementAtOrDefault(i);
            _names[i].Part.SelectedIndex = value is null ? 0 : (int)value.Part + 1; if (value?.Part == FileNamePart.Literal) _names[i].Text.Text = value.Text;
        }
        var first = options.Outputs[0];
        _format.SelectedIndex = Math.Max(0, Array.IndexOf(Formats, first.Format)); _quality.Value = first.Quality;
        _loadedOutputs = options.Outputs.Count > 1 || first.Resize || first.ConvertToSrgb || !Formats.Contains(first.Format) ? options.Outputs : null;
        _status.Text = ""; ShowDestination();
    }
    internal async Task RunRecipe(string recipePath, IReadOnlyList<string> inputs)
    {
        try
        {
            var recipe = BatchRecipe.Read(recipePath); ApplyRecipe(recipe);
            _source.SelectedIndex = 1;
            foreach (var input in inputs)
            {
                if (Directory.Exists(input))
                    foreach (var path in await Task.Run(() => FileAutomation.EnumerateImages(input, recipe.IncludeSubfolders))) AddPath(path);
                else AddPath(input);
            }
            ShowSources();
            if (_sources.Count > 0) await Execute();
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private Task CreateDroplet()
    {
        if (!OperatingSystem.IsWindows() || _cancellation is not null || _dropletPath.Length == 0) return Task.CompletedTask;
        try
        {
            var recipe = new BatchRecipe(1, Options(), _recursive.IsChecked == true); BatchProcessing.Validate(recipe.Options);
            var settings = _dropletPath + "." + Guid.NewGuid().ToString("N") + ".batch.json";
            recipe.Save(settings);
            try { BatchShortcut.Create(_dropletPath, settings); }
            catch { File.Delete(settings); throw; }
            Close();
        }
        catch (Exception ex) { _status.Text = ex.Message; }
        return Task.CompletedTask;
    }
    internal static ComboBox Choice(string[] items, int selected) => new() { ItemsSource = items.Select(Localize.Text).ToArray(), SelectedIndex = selected, HorizontalAlignment = HorizontalAlignment.Stretch };
    internal static CheckBox Check(string text, bool value) => new() { Content = Localize.Text(text), IsChecked = value, Tag = text };
    internal static NumericUpDown Number(decimal value, decimal min, decimal max) => new() { Value = value, Minimum = min, Maximum = max, Increment = 1, FormatString = "0", ShowButtonSpinner = false, MinHeight = 0, Height = 26 };
}
