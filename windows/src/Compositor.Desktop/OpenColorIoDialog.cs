using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Compositor.Core.IO;
using static Compositor.Desktop.ColorSettingsDialog;

namespace Compositor.Desktop;

internal sealed class OpenColorIoDialog : DialogWindow
{
    internal OcioSettings Draft { get; private set; }
    internal bool Ready => _info is not null;
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.OrangeRed };
    private readonly TextBlock _encoding = Label("");
    private readonly TextBox _path = new() { IsReadOnly = true };
    private readonly TextBox _description = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 95 };
    private readonly TextBox _spaceDescription = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 85 };
    private readonly TextBox _folder = new() { IsReadOnly = true };
    private readonly ComboBox _configs = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 300 };
    private readonly Button _space = new() { MinWidth = 250, HorizontalContentAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _settings = new() { Spacing = 14 };
    private readonly Button _ok;
    private OcioConfigInfo? _info;
    private int _generation;
    private bool _updating;
    internal OpenColorIoDialog(OcioSettings value)
    {
        Draft = value; Title = Localize.Text("OpenColorIO Settings"); Width = 880; Height = 780; MinWidth = 680; MinHeight = 570; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { RowDefinitions = new("*,Auto,Auto"), Margin = new Thickness(18) }; var fields = new StackPanel { Spacing = 14 };
        fields.Children.Add(Check("Enable OpenColorIO", Draft.Enabled, b => { Draft = Draft with { Enabled = b }; _settings.IsEnabled = b; _ok!.IsEnabled = !b || _info is not null; }));
        var folder = new Grid { ColumnDefinitions = new("140,*,110") }; folder.Children.Add(FormDialog.ActionButton("Root Folder…", async () => { var picked = await StorageProvider.OpenFolderPickerAsync(new()); if (picked.FirstOrDefault()?.TryGetLocalPath() is { } path) { Draft = Draft with { RootFolder = path }; RefreshConfigs(); } })); Grid.SetColumn(_folder, 1); folder.Children.Add(_folder);
        var clear = FormDialog.ActionButton("Clear", () => { Draft = Draft with { RootFolder = "" }; RefreshConfigs(); }); Grid.SetColumn(clear, 2); folder.Children.Add(clear); _settings.Children.Add(Group("Root Configuration Folder", folder));
        var config = new StackPanel { Spacing = 9 };
        var env = Check("Allow Environment Configuration Override", Draft.OverrideEnvironment, b => { Draft = Draft with { OverrideEnvironment = b }; _ = Inspect(); });
        env.IsEnabled = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCIO")); config.Children.Add(env);
        var choose = new Grid { ColumnDefinitions = new("*,120") }; choose.Children.Add(_configs); var load = FormDialog.ActionButton("Load Config…", async () => { var files = await StorageProvider.OpenFilePickerAsync(new() { FileTypeFilter = [new("OpenColorIO") { Patterns = ["*.ocio"] }] }); if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) { Draft = Draft with { Configuration = path, OverrideEnvironment = true }; env.IsChecked = true; RefreshConfigs(); await Inspect(); } }); Grid.SetColumn(load, 1); choose.Children.Add(load); config.Children.Add(choose);
        config.Children.Add(Row("Path:", _path, 65)); config.Children.Add(_description); _settings.Children.Add(Group("Default Configuration", config));
        var defaults = new StackPanel { Spacing = 9 }; var spaceRow = new Grid { ColumnDefinitions = new("105,*,160") }; spaceRow.Children.Add(Label("Working Space:")); Grid.SetColumn(_space, 1); spaceRow.Children.Add(_space); Grid.SetColumn(_encoding, 2); _encoding.Margin = new Thickness(12, 0, 0, 0); spaceRow.Children.Add(_encoding); defaults.Children.Add(spaceRow); defaults.Children.Add(_spaceDescription);
        defaults.Children.Add(Row("Document Depth:", Choice(["Default", "8-bit", "16-bit", "32-bit"], Array.IndexOf(new[] { 0, 8, 16, 32 }, Draft.Depth), i => Draft = Draft with { Depth = new[] { 0, 8, 16, 32 }[i] })));
        _settings.Children.Add(Group("Default Settings", defaults)); _settings.IsEnabled = Draft.Enabled; fields.Children.Add(_settings); root.Children.Add(new ScrollViewer { Content = fields, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Grid.SetRow(_error, 1); root.Children.Add(_error); var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), Spacing = 10 };
        actions.Children.Add(FormDialog.ActionButton("Cancel", () => Close(false))); _ok = FormDialog.ActionButton("OK", async () => { _ok!.IsEnabled = false; try { await Task.Run(() => OpenColorIo.Validate(Draft)); Close(true); } catch (Exception e) { _error.Text = Localize.Text(e.Message); _ok.IsEnabled = true; } }); actions.Children.Add(_ok); Grid.SetRow(actions, 2); root.Children.Add(actions); Content = root;
        _configs.SelectionChanged += (_, _) => { if (!_updating && _configs.SelectedItem is string selected) { Draft = Draft with { Configuration = selected }; _ = Inspect(); } };
        _space.Click += (_, _) => ShowSpaces(); RefreshConfigs(); Opened += (_, _) => _ = Inspect();
    }
    private void RefreshConfigs()
    {
        _updating = true;
        try
        {
            _folder.Text = Draft.RootFolder;
            var items = (_info?.Builtins.Select(b => "ocio://" + b.Name) ?? [OcioSettings.Builtin]).Append(Draft.Configuration).ToList();
            if (Directory.Exists(Draft.RootFolder)) items.AddRange(Directory.EnumerateFiles(Draft.RootFolder, "*.ocio", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 5 }).Take(128));
            _configs.ItemsSource = items.Distinct().ToArray(); _configs.SelectedItem = Draft.Configuration;
        }
        catch (Exception e) { _error.Text = e.Message; }
        finally { _updating = false; }
    }
    private async Task Inspect()
    {
        var generation = ++_generation; _ok.IsEnabled = !Draft.Enabled; _error.Text = ""; _path.Text = Draft.EffectiveConfig; _description.Text = Localize.Text("Loading configuration…");
        try
        {
            var info = await Task.Run(() => OpenColorIo.Inspect(Draft.EffectiveConfig)); if (generation != _generation) return;
            _info = info; _description.Text = info.Description; if (info.Find(Draft.WorkingSpace) is null) Draft = Draft with { WorkingSpace = info.Roles.FirstOrDefault(r => r.Name == "scene_linear")?.Name ?? info.Spaces.First().Name };
            RefreshConfigs(); SpaceChanged(); _ok.IsEnabled = true;
        }
        catch (Exception e) { if (generation != _generation) return; _info = null; _description.Text = ""; _spaceDescription.Text = ""; _error.Text = Localize.Text(e.Message); _ok.IsEnabled = !Draft.Enabled; }
    }
    private void SpaceChanged()
    {
        var space = _info?.Find(Draft.WorkingSpace); _space.Content = Draft.WorkingSpace + (space?.Name != Draft.WorkingSpace ? " (" + space?.Name + ")" : "") + "  ⌄"; _encoding.Text = Localize.Text("Encoding:") + " " + space?.Encoding; _spaceDescription.Text = space?.Description ?? "";
    }
    private void ShowSpaces()
    {
        if (_info is null) return; var menu = new ContextMenu();
        MenuItem Item(string title, string value) { var item = new MenuItem { Header = title }; item.Click += (_, _) => { Draft = Draft with { WorkingSpace = value }; SpaceChanged(); }; return item; }
        foreach (var role in _info.Roles) menu.Items.Add(Item(role.Name + " (" + role.Space + ")", role.Name)); menu.Items.Add(new Separator());
        foreach (var family in _info.Spaces.GroupBy(s => string.IsNullOrEmpty(s.Family) ? "Other" : s.Family)) { var parent = new MenuItem { Header = family.Key }; foreach (var space in family) parent.Items.Add(Item(space.Name, space.Name)); menu.Items.Add(parent); }
        menu.Open(_space);
    }
}
