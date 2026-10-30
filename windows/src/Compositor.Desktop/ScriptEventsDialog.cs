using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Compositor.Core.IO;

namespace Compositor.Desktop;

/// <summary>
/// File ▸ Scripts ▸ Script Events Manager. The layout follows the reference: the enable switch and association
/// list on top, the event / script / action chooser below, Done, Remove, Remove All and Add down the right.
/// Edits work on a copy; Done keeps them, closing the window or Escape discards them.
/// </summary>
internal sealed partial class ScriptEventsDialog : DialogWindow
{
    private ScriptEventSettings _settings;
    private readonly IReadOnlyList<string> _commands;
    private readonly CheckBox _enabled = new() { Tag = "script-events-enabled" };
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Multiple, MinHeight = 170, Tag = "script-events-list" };
    private readonly TextBlock _empty = new()
    {
        Text = Localize.Text("No scripts/actions associated with events"), Foreground = Skin.SecondaryBrush,
        Margin = new Thickness(6, 4), IsHitTestVisible = false, Tag = "script-events-empty",
    };
    private readonly ComboBox _event = new() { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "script-events-event" };
    private readonly RadioButton _useScript = new() { GroupName = "script-events-target", Tag = "script-events-use-script" };
    private readonly RadioButton _useAction = new() { GroupName = "script-events-target", Tag = "script-events-use-action" };
    private readonly ComboBox _script = new() { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "script-events-script" };
    private readonly TextBox _description = new()
    {
        IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 92, MaxHeight = 92, Tag = "script-events-description",
    };
    private readonly ComboBox _actionSet = new() { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "script-events-action-set" };
    private readonly ComboBox _action = new() { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "script-events-action" };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.OrangeRed, Tag = "script-events-error" };
    private readonly Button _remove;
    private readonly Button _removeAll;
    private readonly List<(ScriptEvent Event, string Command)> _eventRows = [];
    private int _eventAdd = -1;
    private int _scriptBrowse = -1;
    private int _lastEvent;
    private int _lastScript;
    /// <summary>JavaScript files chosen with Browse…, listed after the built-in scripts.</summary>
    private readonly List<string> _files = [];
    private bool _busy;
    private ScriptEventSettings? _result;

    private sealed record AssociationRow(int Index, string Text) { public override string ToString() => Text; }

    /// <summary>For the self check: the picker used by "Add Event…", replaceable so it can run without a pointer.</summary>
    internal Func<IReadOnlyList<string>, Task<string?>>? PickCommand { get; set; }
    internal Func<Task<string?>>? PickScriptFile { get; set; }
    internal ScriptEventSettings Draft => _settings;

    internal ScriptEventsDialog(ScriptEventSettings settings, IReadOnlyList<string> commands)
    {
        _settings = settings; _commands = commands;
        Title = Localize.Text("Script Events Manager");
        Width = 780; Height = 520; MinWidth = 640; MinHeight = 470;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { ColumnDefinitions = new("*,170"), RowDefinitions = new("Auto,*,Auto,Auto"), RowSpacing = 8,
            Margin = new Thickness(16), Tag = "script-events-layout" };
        _enabled.Content = Localize.Text("_Enable Events to Run Scripts/Actions") + ":";
        _enabled.IsChecked = settings.Enabled;
        root.Children.Add(_enabled);
        var listHost = new Panel { Children = { _list, _empty } };
        var listBorder = new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Background = Skin.SurfaceDarkBrush, Child = listHost };
        Grid.SetRow(listBorder, 1); root.Children.Add(listBorder);

        var chooser = new Grid
        {
            ColumnDefinitions = new("Auto,*,*"), RowDefinitions = new("Auto,Auto,Auto,Auto"), RowSpacing = 8, ColumnSpacing = 10, Margin = new Thickness(14, 12),
        };
        var eventLabel = new TextBlock { Text = Localize.Text("Compositor Event") + ":", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 0, 0, 0) };
        chooser.Children.Add(eventLabel);
        Grid.SetColumn(_event, 1); Grid.SetColumnSpan(_event, 2); chooser.Children.Add(_event);
        _useScript.Content = Localize.Text("_Script") + ":"; _useScript.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(_useScript, 1); chooser.Children.Add(_useScript);
        Grid.SetRow(_script, 1); Grid.SetColumn(_script, 1); Grid.SetColumnSpan(_script, 2); chooser.Children.Add(_script);
        Grid.SetRow(_description, 2); Grid.SetColumn(_description, 1); Grid.SetColumnSpan(_description, 2); chooser.Children.Add(_description);
        _useAction.Content = Localize.Text("A_ction") + ":"; _useAction.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(_useAction, 3); chooser.Children.Add(_useAction);
        Grid.SetRow(_actionSet, 3); Grid.SetColumn(_actionSet, 1); chooser.Children.Add(_actionSet);
        Grid.SetRow(_action, 3); Grid.SetColumn(_action, 2); chooser.Children.Add(_action);
        var chooserBorder = new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Child = chooser, Margin = new Thickness(0, 6, 0, 0) };
        Grid.SetRow(chooserBorder, 2); root.Children.Add(chooserBorder);
        Grid.SetRow(_error, 3); root.Children.Add(_error);

        var done = Wide(FormDialog.ActionButton("_Done", Done)); done.Tag = "script-events-done";
        _remove = Wide(FormDialog.ActionButton("_Remove", RemoveSelected)); _remove.Tag = "script-events-remove";
        _removeAll = Wide(FormDialog.ActionButton("Remo_ve All", RemoveAll)); _removeAll.Tag = "script-events-remove-all";
        var add = Wide(FormDialog.ActionButton("_Add", Add)); add.Tag = "script-events-add";
        done.Margin = new Thickness(18, 0, 0, 0); Grid.SetColumn(done, 1); root.Children.Add(done);
        var removal = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(18, 0, 0, 16), Children = { _remove, _removeAll } };
        Grid.SetColumn(removal, 1); Grid.SetRow(removal, 1); root.Children.Add(removal);
        add.Margin = new Thickness(18, 18, 0, 0); add.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(add, 1); Grid.SetRow(add, 2); root.Children.Add(add);
        Content = root;

        FillEvents(ScriptEvent.StartApplication, "");
        FillScripts();
        _actionSet.ItemsSource = new[] { Localize.Text(ScriptEvents.BasicActionSet) }; _actionSet.SelectedIndex = 0;
        _action.ItemsSource = ScriptEvents.Actions.Select(a => Localize.Text(ScriptEvents.ActionName(a))).ToArray(); _action.SelectedIndex = 0;
        _useScript.IsChecked = true;

        _event.SelectionChanged += async (_, _) => await EventChanged();
        _script.SelectionChanged += async (_, _) => await ScriptChanged();
        _useScript.IsCheckedChanged += (_, _) => UpdateTarget();
        _useAction.IsCheckedChanged += (_, _) => UpdateTarget();
        _list.SelectionChanged += (_, _) => UpdateButtons();
        _enabled.IsCheckedChanged += (_, _) => { _settings = _settings with { Enabled = _enabled.IsChecked == true }; UpdateButtons(); };
        KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) { Close(); e.Handled = true; } };
        Refresh(); UpdateTarget();
    }

    internal static async Task<ScriptEventSettings?> Ask(Window owner, ScriptEventSettings settings, IReadOnlyList<string> commands)
    {
        var dialog = new ScriptEventsDialog(settings, commands);
        await dialog.ShowDialog(owner);
        return dialog._result;
    }

    private static Button Wide(Button button)
    {
        button.HorizontalAlignment = HorizontalAlignment.Stretch; button.Margin = new Thickness(0);
        ToolTip.SetTip(button, (button.Content as string)?.Replace("_", ""));
        return button;
    }

    private static ComboBoxItem Rule() => new()
    {
        Content = new Border { Height = 1, Margin = new Thickness(4, 3), Background = Skin.MenuRule, HorizontalAlignment = HorizontalAlignment.Stretch },
        IsEnabled = false, Focusable = false,
    };

    private void FillEvents(ScriptEvent selectEvent, string selectCommand)
    {
        _busy = true;
        _event.Items.Clear(); _eventRows.Clear();
        foreach (var value in ScriptEvents.DocumentEvents)
        {
            _event.Items.Add(new ComboBoxItem { Content = Localize.Text(ScriptEvents.EventName(value)) });
            _eventRows.Add((value, ""));
        }
        foreach (var command in _settings.CustomEvents)
        {
            _event.Items.Add(new ComboBoxItem { Content = Localize.Text(command) });
            _eventRows.Add((ScriptEvent.Command, command));
        }
        _event.Items.Add(Rule()); _eventRows.Add((ScriptEvent.Everything, "\0rule"));
        _eventAdd = _event.Items.Count;
        _event.Items.Add(new ComboBoxItem { Content = Localize.Text("Add Event…"), Tag = "script-events-add-event" });
        var index = _eventRows.FindIndex(row => row.Event == selectEvent && row.Command == selectCommand);
        _event.SelectedIndex = _lastEvent = Math.Max(0, index);
        _busy = false;
    }

    private void FillScripts()
    {
        _busy = true;
        _script.Items.Clear();
        foreach (var script in ScriptEvents.Scripts) _script.Items.Add(new ComboBoxItem { Content = Localize.Text(ScriptEvents.ScriptName(script)) });
        _script.Items.Add(Rule());
        _scriptBrowse = _script.Items.Count;
        _script.Items.Add(new ComboBoxItem { Content = Localize.Text("Browse…"), Tag = "script-events-browse" });
        _script.SelectedIndex = _lastScript = 0;
        _busy = false;
        Describe();
    }

    /// <summary>A chosen JavaScript file, listed after the built-ins and the ones chosen before, and selected.</summary>
    internal void AddFile(string path)
    {
        var at = _files.FindIndex(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        if (at < 0)
        {
            _files.Add(path); at = _files.Count - 1;
            var item = new ComboBoxItem { Content = Path.GetFileName(path) }; ToolTip.SetTip(item, path);
            _script.Items.Insert(ScriptEvents.Scripts.Count + at, item);
            _scriptBrowse++;
        }
        _busy = true; _script.SelectedIndex = _lastScript = ScriptEvents.Scripts.Count + at; _busy = false;
        _error.Text = ""; Describe();
    }

    private void Describe() =>
        _description.Text = _script.SelectedIndex is >= 0 and var index && index < ScriptEvents.Scripts.Count
            ? Localize.Text(ScriptEvents.ScriptDescription(ScriptEvents.Scripts[index]))
            : _script.SelectedIndex - ScriptEvents.Scripts.Count is >= 0 and var file && file < _files.Count
                ? string.Format(Localize.Text("Runs {0} with the JavaScript interpreter: app.activeDocument, its layers and File Info, alert and $.writeln."), Path.GetFileName(_files[file]))
            : Localize.Text("Manage your events by adding and removing them. Select different scripts for a detailed description.");

    private async Task EventChanged()
    {
        if (_busy) return;
        if (_event.SelectedIndex != _eventAdd) { if (_event.SelectedIndex >= 0) _lastEvent = _event.SelectedIndex; return; }
        _busy = true; _event.SelectedIndex = _lastEvent; _busy = false;
        var picked = PickCommand is { } pick ? await pick(_commands) : await AskCommand();
        if (picked is null) return;
        if (!_commands.Contains(picked, StringComparer.Ordinal)) { _error.Text = Localize.Text("Invalid event name."); return; }
        if (!_settings.CustomEvents.Contains(picked, StringComparer.Ordinal))
        {
            if (_settings.CustomEvents.Count >= ScriptEvents.MaximumCustomEvents)
            { _error.Text = Localize.Text($"At most {ScriptEvents.MaximumCustomEvents} added events are supported."); return; }
            _settings = _settings with { CustomEvents = _settings.CustomEvents.Append(picked).ToArray() };
        }
        _error.Text = "";
        FillEvents(ScriptEvent.Command, picked);
    }

    private async Task<string?> AskCommand()
    {
        if (_commands.Count == 0) { _error.Text = Localize.Text("No menu commands are available."); return null; }
        var dialog = new FormDialog("Add Event", 460, 220);
        dialog.Description("Run the association whenever this menu command is chosen.");
        var choice = dialog.Choice("Menu Command", _commands, 0);
        return await dialog.ShowDialog<bool>(this) && choice.SelectedIndex >= 0 ? _commands[choice.SelectedIndex] : null;
    }

    private async Task ScriptChanged()
    {
        if (_busy) return;
        if (_script.SelectedIndex != _scriptBrowse) { if (_script.SelectedIndex >= 0) _lastScript = _script.SelectedIndex; Describe(); return; }
        _busy = true; _script.SelectedIndex = _lastScript; _busy = false; Describe();
        string? path;
        if (PickScriptFile is { } pick) path = await pick();
        else
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Localize.Text("Select a JavaScript File"), AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType(Localize.Text("JavaScript")) { Patterns = ["*.js", "*.jsx", "*.jsxbin"] }],
            });
            path = files.FirstOrDefault()?.TryGetLocalPath();
        }
        if (path is null) return;
        if (Path.GetExtension(path).Equals(".jsxbin", StringComparison.OrdinalIgnoreCase))
        { _error.Text = Localize.Text("Compiled .jsxbin scripts cannot run; choose the .jsx source instead."); return; }
        AddFile(path);
    }

    private void UpdateTarget()
    {
        var script = _useScript.IsChecked == true;
        _script.IsEnabled = _description.IsEnabled = script;
        _actionSet.IsEnabled = _action.IsEnabled = !script;
    }

    private void UpdateButtons()
    {
        _remove.IsEnabled = _list.SelectedItems?.Count > 0;
        _removeAll.IsEnabled = _settings.Bindings.Count > 0;
    }

    private void Refresh()
    {
        _list.ItemsSource = _settings.Bindings.Select((binding, index) => new AssociationRow(index, Row(binding))).ToArray();
        _empty.IsVisible = _settings.Bindings.Count == 0;
        UpdateButtons();
    }

    internal static string Row(ScriptEventBinding binding) =>
        (binding.Event == ScriptEvent.Command ? Localize.Text(binding.Command) : Localize.Text(ScriptEvents.EventName(binding.Event))) + ": "
        + (binding.Target == ScriptEventTarget.Script
            ? binding.ScriptFile.Length > 0 ? Path.GetFileName(binding.ScriptFile) : Localize.Text(ScriptEvents.ScriptName(binding.Script))
            : Localize.Text(ScriptEvents.BasicActionSet) + " ▸ " + Localize.Text(ScriptEvents.ActionName(binding.Action)));

    internal void Add()
    {
        if (_event.SelectedIndex < 0 || _event.SelectedIndex >= _eventRows.Count || _eventRows[_event.SelectedIndex].Command == "\0rule") return;
        var (happened, command) = _eventRows[_event.SelectedIndex];
        var binding = _useScript.IsChecked == true
            ? new ScriptEventBinding(happened, ScriptEventTarget.Script, Script: ScriptEvents.Scripts[Math.Clamp(_lastScript, 0, ScriptEvents.Scripts.Count - 1)], Command: command,
                ScriptFile: _lastScript - ScriptEvents.Scripts.Count is >= 0 and var file && file < _files.Count ? _files[file] : "")
            : new ScriptEventBinding(happened, ScriptEventTarget.Action, Action: ScriptEvents.Actions[Math.Clamp(_action.SelectedIndex, 0, ScriptEvents.Actions.Count - 1)], Command: command);
        try { _settings = ScriptEvents.Add(_settings, binding); _error.Text = ""; }
        catch (InvalidDataException error) { _error.Text = Localize.Text(error.Message); return; }
        Refresh();
        _list.SelectedIndex = _settings.Bindings.Count - 1;
    }

    private void RemoveSelected()
    {
        var indexes = _list.SelectedItems?.OfType<AssociationRow>().Select(row => row.Index).ToArray() ?? [];
        if (indexes.Length == 0) return;
        _settings = ScriptEvents.RemoveAt(_settings, indexes);
        _error.Text = ""; Refresh();
    }

    private void RemoveAll()
    {
        _settings = _settings with { Bindings = [] };
        _error.Text = ""; Refresh();
    }

    private void Done()
    {
        try { ScriptEvents.Validate(_settings); }
        catch (InvalidDataException error) { _error.Text = Localize.Text(error.Message); return; }
        _result = _settings;
        Close();
    }
}
