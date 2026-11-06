using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Compositor.Core.IO;

namespace Compositor.Desktop;

/// <summary>Image Processor's four numbered steps: what to process, where to save it, the file types, and the preferences.</summary>
internal sealed class ImageProcessorDialog : DialogWindow
{
    private readonly IReadOnlyList<AutomationSource> _open;
    private readonly Func<IReadOnlyList<AutomationSource>, BatchOptions, IProgress<BatchProgress>, CancellationToken, Task<BatchResult>> _run;
    private readonly RadioButton _useOpen = new() { Content = Localize.Text("Use Open Images"), GroupName = "processor-source", IsChecked = true, Tag = "processor-open" };
    private readonly RadioButton _useFolder = new() { GroupName = "processor-source", Tag = "processor-folder", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _subfolders = BatchDialog.Check("Include All sub-folders", false);
    private readonly CheckBox _openFirst = BatchDialog.Check("Open first image to apply settings", false);
    private readonly TextBlock _sourceText = new() { Foreground = Skin.SecondaryBrush, VerticalAlignment = VerticalAlignment.Center, Text = Localize.Text("No images have been selected"), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 280 };
    private readonly RadioButton _sameLocation = new() { Content = Localize.Text("Save in Same Location"), GroupName = "processor-destination", IsChecked = true, Tag = "processor-same" };
    private readonly RadioButton _toFolder = new() { GroupName = "processor-destination", Tag = "processor-to-folder", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _keepStructure = BatchDialog.Check("Keep folder structure", false);
    private readonly TextBlock _destinationText = new() { Foreground = Skin.SecondaryBrush, VerticalAlignment = VerticalAlignment.Center, Text = Localize.Text("No folder has been selected"), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 280 };
    private readonly Button _chooseSource, _chooseDestination, _start;
    private readonly CheckBox _action = BatchDialog.Check("Run Action", false);
    private readonly ComboBox _set = BatchDialog.Choice(BatchDialog.ActionSets.Select(s => s.Set).ToArray(), 0);
    private readonly ComboBox _actionChoice = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _copyright = DialogKit.Line("Copyright Info");
    private readonly CheckBox _icc = BatchDialog.Check("Include ICC Profile", true);
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _body = new();
    private readonly ScrollViewer _scroll;
    private string? _sourceFolder, _destinationFolder;
    private CancellationTokenSource? _cancellation;
    private sealed record TypeRow(ExportFormat Format, CheckBox Save, CheckBox Resize, NumericUpDown Width, NumericUpDown Height);
    private readonly List<TypeRow> _types = [];
    private readonly NumericUpDown _quality = BatchDialog.Number(5, 0, 12);
    private readonly CheckBox _srgb = BatchDialog.Check("Convert Profile to sRGB", false), _maximize = BatchDialog.Check("Maximize Compatibility", true), _lzw = BatchDialog.Check("LZW Compression", false);
    /// <summary>What the self check reads: true when everything fits without scrolling.</summary>
    internal bool FitsWithoutScrolling => _scroll.Extent.Height <= _scroll.Viewport.Height + 1 && _scroll.Extent.Width <= _scroll.Viewport.Width + 1;

    internal ImageProcessorDialog(IReadOnlyList<AutomationSource> open,
        Func<IReadOnlyList<AutomationSource>, BatchOptions, IProgress<BatchProgress>, CancellationToken, Task<BatchResult>> run)
    {
        _open = open; _run = run;
        Title = Localize.Text("Image Processor"); Width = 780; Height = 780; MinWidth = 700; MinHeight = 560;
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(16, 12, 16, 16) };
        _scroll = new ScrollViewer { Content = _body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        root.Children.Add(_scroll);
        _start = FormDialog.ActionButton("Run", () => _ = Execute()); _start.Tag = "Run batch";
        var load = FormDialog.ActionButton("Load…", () => _ = LoadRecipe()); var save = FormDialog.ActionButton("Save…", () => _ = SaveRecipe());
        var actions = DialogKit.ActionColumn(_start, FormDialog.ActionButton("Cancel", Cancel), new Border { Height = 24 }, this.Mnemonic(load, 'L'), this.Mnemonic(save, 'S'), _status);
        Grid.SetColumn(actions, 1); root.Children.Add(actions); Content = root;
        Button Pick(string title, Action action, char key) { var button = FormDialog.ActionButton(title, action); button.Margin = default; button.Width = 150; return this.Mnemonic(button, key); }

        _chooseSource = Pick("Select Folder…", () => _ = ChooseSource(), 'F');
        var one = new StackPanel { Spacing = 4, Margin = new Thickness(36, 2, 0, 6) };
        one.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, Children = { this.Mnemonic(_useOpen, 'I'), _subfolders } });
        one.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _useFolder, _chooseSource, _sourceText } });
        _openFirst.Margin = new Thickness(24, 0, 0, 0); _openFirst.IsEnabled = false; one.Children.Add(this.Mnemonic(_openFirst, 'O'));
        ToolTip.SetTip(_openFirst, Localize.Text("Compositor doesn't ask anything while it opens these files."));
        Step(1, "Select the images to process", one);

        _chooseDestination = Pick("Select Folder…", () => _ = ChooseDestination(), 'C');
        var two = new StackPanel { Spacing = 4, Margin = new Thickness(36, 2, 0, 6) };
        two.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, Children = { this.Mnemonic(_sameLocation, 'A'), _keepStructure } });
        two.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _toFolder, _chooseDestination, _destinationText } });
        Rule(); Step(2, "Select location to save processed images", two);

        var three = new StackPanel { Margin = new Thickness(36, 2, 0, 6) };
        _quality.Width = 48;
        three.Children.Add(TypeBlock(ExportFormat.Jpeg, "Save as JPEG", 'J', 'R', [new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { DialogKit.Label("Quality"), _quality } }, this.Mnemonic(_srgb, 'V')], true));
        three.Children.Add(new Border { Height = 1, Background = Skin.BorderControlBrush, Margin = new Thickness(0, 4) });
        three.Children.Add(TypeBlock(ExportFormat.Psd, "Save as PSD", 'P', 'E', [this.Mnemonic(_maximize, 'M')], false));
        three.Children.Add(new Border { Height = 1, Background = Skin.BorderControlBrush, Margin = new Thickness(0, 4) });
        three.Children.Add(TypeBlock(ExportFormat.Tiff, "Save as TIFF", 'T', 'Z', [this.Mnemonic(_lzw, 'W')], false));
        _maximize.IsEnabled = false; ToolTip.SetTip(_maximize, Localize.Text("Compositor's PSD files always carry a flattened composite."));
        Rule(); Step(3, "File Type", three);

        var four = new StackPanel { Spacing = 6, Margin = new Thickness(36, 2, 0, 0) };
        var play = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*,*"), ColumnSpacing = 8, Children = { this.Mnemonic(_action, 'U') } };
        Grid.SetColumn(_set, 1); play.Children.Add(_set); Grid.SetColumn(_actionChoice, 2); play.Children.Add(_actionChoice);
        four.Children.Add(play); four.Children.Add(DialogKit.Row("Copyright Info", _copyright, 122)); four.Children.Add(this.Mnemonic(_icc, 'D'));
        Rule(); Step(4, "Preferences", four);

        _set.SelectionChanged += (_, _) => ShowActions(); ShowActions();
        _action.IsCheckedChanged += (_, _) => _set.IsEnabled = _actionChoice.IsEnabled = _action.IsChecked == true;
        _set.IsEnabled = _actionChoice.IsEnabled = false;
        foreach (var radio in new[] { _useOpen, _useFolder, _sameLocation, _toFolder }) radio.IsCheckedChanged += (_, _) => Update();
        foreach (var row in _types) { row.Save.IsCheckedChanged += (_, _) => Update(); row.Resize.IsCheckedChanged += (_, _) => Update(); }
        Update();
        Closing += (_, e) => { if (_cancellation is not null) { e.Cancel = true; _cancellation.Cancel(); } };
        KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) { Cancel(); e.Handled = true; } };
    }

    private void Rule() => _body.Children.Add(new Border { Height = 1, Background = Skin.BorderControlBrush, Margin = new Thickness(0, 2, 0, 6) });
    /// <summary>A numbered heading, its number in a filled circle, with the step's controls under it.</summary>
    private void Step(int number, string title, Control content)
    {
        var mark = new Grid { Width = 22, Height = 22, Children = { new Ellipse { Fill = Skin.LabelBrush }, new TextBlock { Text = number.ToString(System.Globalization.CultureInfo.InvariantCulture), FontWeight = FontWeight.Bold, Foreground = Skin.ChromeBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } } };
        _body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { mark, new TextBlock { Text = Localize.Text(title), VerticalAlignment = VerticalAlignment.Center } } });
        _body.Children.Add(content);
    }
    private Control TypeBlock(ExportFormat format, string title, char saveKey, char resizeKey, Control[] left, bool enabled)
    {
        var save = this.Mnemonic(BatchDialog.Check(title, enabled), saveKey);
        var resize = this.Mnemonic(BatchDialog.Check("Resize to Fit", false), resizeKey);
        var width = BatchDialog.Number(1, 1, 30000); var height = BatchDialog.Number(1, 1, 30000); width.Width = height.Width = 64;
        _types.Add(new TypeRow(format, save, resize, width, height));
        var options = new StackPanel { Spacing = 6, Children = { save } };
        foreach (var control in left) { control.Margin = new Thickness(24, 0, 0, 0); options.Children.Add(control); }
        Control Size(string label, NumericUpDown field) => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(24, 0, 0, 0), Children = { new TextBlock { Text = label, Width = 16, VerticalAlignment = VerticalAlignment.Center, Foreground = Skin.SecondaryBrush }, field, new TextBlock { Text = Localize.Text("Pixels"), VerticalAlignment = VerticalAlignment.Center, Foreground = Skin.SecondaryBrush } } };
        var grid = DialogKit.Columns("*,*", 12, options, new StackPanel { Spacing = 6, Children = { resize, Size("W:", width), Size("H:", height) } });
        options.VerticalAlignment = VerticalAlignment.Top;
        return grid;
    }
    private void ShowActions()
    {
        var actions = BatchDialog.ActionSets[Math.Max(0, _set.SelectedIndex)].Actions;
        _actionChoice.ItemsSource = actions.Select(a => Localize.Text(BatchDialog.ActionName(a))).ToArray(); _actionChoice.SelectedIndex = 0;
    }
    private void Update()
    {
        var folder = _useFolder.IsChecked == true;
        _chooseSource.IsEnabled = _subfolders.IsEnabled = folder;
        _chooseDestination.IsEnabled = _toFolder.IsChecked == true; _keepStructure.IsEnabled = _toFolder.IsChecked == true && folder;
        foreach (var row in _types) { row.Resize.IsEnabled = row.Save.IsChecked == true; row.Width.IsEnabled = row.Height.IsEnabled = row.Save.IsChecked == true && row.Resize.IsChecked == true; }
        _quality.IsEnabled = _srgb.IsEnabled = _types[0].Save.IsChecked == true; _lzw.IsEnabled = _types[2].Save.IsChecked == true;
        _start.IsEnabled = _cancellation is null && _types.Any(t => t.Save.IsChecked == true) && (folder ? _sourceFolder is not null : _open.Count > 0)
            && (_toFolder.IsChecked != true || _destinationFolder is not null);
    }
    private async Task ChooseSource()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Localize.Text("Source Folder"), AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        _sourceFolder = path; _sourceText.Text = path; Update();
    }
    private async Task ChooseDestination()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Localize.Text("Destination"), AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        _destinationFolder = path; _destinationText.Text = path; Update();
    }

    /// <summary>JPEG quality as Photoshop's 0–12 scale.</summary>
    private BatchOutput Output(TypeRow row) => new(row.Format, Math.Clamp((int)Math.Round((double)(_quality.Value ?? 5) * 100 / 12), 1, 100), row.Resize.IsChecked == true,
        (int)(row.Width.Value ?? 1), (int)(row.Height.Value ?? 1), false, row.Format == ExportFormat.Jpeg && _srgb.IsChecked == true, row.Format == ExportFormat.Tiff && _lzw.IsChecked == true);

    private BatchOptions Options()
    {
        var action = _action.IsChecked == true ? BatchDialog.ActionSets[Math.Max(0, _set.SelectedIndex)].Actions[Math.Max(0, _actionChoice.SelectedIndex)] : BatchAction.None;
        return new BatchOptions(_toFolder.IsChecked == true ? _destinationFolder ?? "" : "", action, [.. _types.Where(t => t.Save.IsChecked == true).Select(Output)], [new FileNameComponent(FileNamePart.DocumentName)],
            1, true, MetadataPolicy.CopyrightAndContact, SaveBesideSource: _sameLocation.IsChecked == true, Copyright: _copyright.Text ?? "", EmbedProfile: _icc.IsChecked == true,
            SourceRoot: _keepStructure.IsChecked == true && _keepStructure.IsEnabled ? _sourceFolder : null);
    }
    private async Task Execute()
    {
        if (_cancellation is not null) return;
        try
        {
            IReadOnlyList<AutomationSource> sources = _open;
            if (_useFolder.IsChecked == true)
            {
                var folder = _sourceFolder!; var recursive = _subfolders.IsChecked == true;
                sources = [.. (await Task.Run(() => FileAutomation.EnumerateImages(folder, recursive))).Select(p => new AutomationSource(System.IO.Path.GetFileName(p), p))];
            }
            var options = Options(); BatchProcessing.Validate(options);
            _cancellation = new CancellationTokenSource(); _body.IsEnabled = false; Update();
            var progress = new Progress<BatchProgress>(p => _status.Text = $"{p.Completed}/{p.Total}\n{p.Name}");
            var result = await _run(sources, options, progress, _cancellation.Token);
            _status.Text = Localize.Text("Completed") + $": {result.Succeeded}\n" + Localize.Text("Errors") + $": {result.Failed}";
            if (result.Failed > 0) _status.Text += "\n" + string.Join("\n", result.Items.Where(i => i.Error is not null).Take(3).Select(i => i.Source + ": " + i.Error));
        }
        catch (OperationCanceledException) { _status.Text = Localize.Text("Canceled. Completed files have been preserved."); }
        catch (Exception ex) { _status.Text = ex.Message; }
        finally { _cancellation?.Dispose(); _cancellation = null; _body.IsEnabled = true; Update(); }
    }
    private void Cancel() { if (_cancellation is not null) _cancellation.Cancel(); else Close(); }
    private async Task SaveRecipe()
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Localize.Text("Save Settings…"), SuggestedFileName = "image-processor.json", DefaultExtension = "json", ShowOverwritePrompt = true });
            if (file?.TryGetLocalPath() is { } path) new BatchRecipe(1, Options(), _subfolders.IsChecked == true).Save(path);
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private async Task LoadRecipe()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Load…"), AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }] });
            if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            var recipe = BatchRecipe.Read(path); var options = recipe.Options;
            _subfolders.IsChecked = recipe.IncludeSubfolders; _sameLocation.IsChecked = options.SaveBesideSource; _toFolder.IsChecked = !options.SaveBesideSource;
            if (options.OutputDirectory.Length > 0) { _destinationFolder = options.OutputDirectory; _destinationText.Text = options.OutputDirectory; }
            _copyright.Text = options.Copyright; _icc.IsChecked = options.EmbedProfile;
            foreach (var row in _types)
            {
                var output = options.Outputs.FirstOrDefault(o => o.Format == row.Format); row.Save.IsChecked = output is not null; if (output is null) continue;
                row.Resize.IsChecked = output.Resize; row.Width.Value = output.MaximumWidth; row.Height.Value = output.MaximumHeight;
                if (row.Format == ExportFormat.Jpeg) { _quality.Value = Math.Round(output.Quality * 12m / 100); _srgb.IsChecked = output.ConvertToSrgb; }
                if (row.Format == ExportFormat.Tiff) _lzw.IsChecked = output.Lzw;
            }
            Update();
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
}
