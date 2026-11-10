using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Compositor.Core.IO;

namespace Compositor.Desktop;

/// <summary>
/// Edit ▸ PDF Presets…: the presets Export As can write a PDF with, the built-in ones in brackets, each with its description and a
/// summary of its settings; New, Edit and Delete work on the person's own, and Load and Save As move them between machines.
/// </summary>
internal sealed class PdfPresetsDialog : DialogWindow
{
    private readonly ListBox _list = new() { Height = 128, Tag = "pdf-preset-list" };
    private readonly TextBox _description = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Height = 150, Tag = "pdf-preset-description" };
    private readonly StackPanel _summary = new() { Margin = new Thickness(8, 6) };
    private readonly TextBlock _error = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
    private readonly Button _edit, _delete, _saveAs;
    private readonly HashSet<string> _closed = [];
    private List<PdfPreset> _presets;
    internal PdfPresetsDialog()
    {
        Title = Localize.Text("PDF Presets"); Width = 920; Height = 720; MinWidth = 700; MinHeight = 560; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _presets = [.. PdfPresets.All()];
        var done = FormDialog.ActionButton("Done", () => Close()); done.IsDefault = true;
        var add = this.Mnemonic(FormDialog.ActionButton("New…", async () => await Edit(null)), 'N');
        _edit = this.Mnemonic(FormDialog.ActionButton("Edit…", async () => await Edit(Selected)), 'E');
        _delete = this.Mnemonic(FormDialog.ActionButton("Delete", Delete), 'D');
        var load = this.Mnemonic(FormDialog.ActionButton("Load…", async () => await Load()), 'L');
        _saveAs = this.Mnemonic(FormDialog.ActionButton("Save As…", async () => await SaveAs()), 'A');
        var actions = DialogKit.ActionColumn(done, add, _edit, _delete, load, _saveAs);
        foreach (var (button, gap) in new[] { (add, 18d), (load, 18d) }) button.Margin = new Thickness(0, gap, 0, 0);
        actions.Margin = new Thickness(16, 26, 0, 0);
        var body = new DockPanel { Children =
        {
            Caption("Presets"), _list, Caption("Preset Description"), _description, Caption("Preset Settings Summary"), _error,
            new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Background = Skin.SurfaceDarkBrush, Child = new ScrollViewer { Content = _summary, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } },
        } };
        foreach (var child in body.Children.Take(6)) DockPanel.SetDock(child, child == _error ? Dock.Bottom : Dock.Top);
        var root = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(16), Children = { body, actions } }; Grid.SetColumn(actions, 1);
        Content = root;
        _list.SelectionChanged += (_, _) => Present();
        _list.DoubleTapped += async (_, _) => { if (Selected is { BuiltIn: false } preset) await Edit(preset); };
        Fill(_presets.FirstOrDefault()?.Name);
    }
    private static TextBlock Caption(string text) => new() { Text = Localize.Text(text) + ":", Margin = new Thickness(0, 8, 0, 4), Foreground = Skin.SecondaryBrush };
    private PdfPreset? Selected => (_list.SelectedItem as ListBoxItem)?.Tag as PdfPreset;
    private void Fill(string? selected)
    {
        _list.Items.Clear();
        foreach (var preset in _presets) _list.Items.Add(new ListBoxItem { Content = preset.BuiltIn ? Localize.Text(preset.Name) : preset.Name, Tag = preset, Padding = new Thickness(8, 2), MinHeight = 0 });
        _list.SelectedItem = _list.Items.OfType<ListBoxItem>().FirstOrDefault(i => (i.Tag as PdfPreset)?.Name == selected) ?? _list.Items.OfType<ListBoxItem>().FirstOrDefault();
        Present();
    }
    /// <summary>The selected preset's description and its settings under collapsible headings.</summary>
    private void Present()
    {
        var preset = Selected; _edit.IsEnabled = _delete.IsEnabled = preset is { BuiltIn: false }; _saveAs.IsEnabled = preset is not null;
        _description.Text = preset is null ? "" : preset.BuiltIn ? Localize.Text(preset.Description) : preset.Description;
        _summary.Children.Clear(); if (preset is null) return;
        foreach (var group in preset.Summary().GroupBy(s => s.Heading))
        {
            var open = !_closed.Contains(group.Key); var indent = 14d;
            if (group.Key.Length > 0)
            {
                var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 2), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand), Children =
                {
                    new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(open ? "M0,0 L4,4 L8,0" : "M0,0 L4,4 L0,8"), Stroke = Skin.SecondaryBrush, StrokeThickness = 1.2, Width = 9, Height = 9, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = Localize.Text(group.Key) + ":" },
                } };
                var key = group.Key; heading.PointerPressed += (_, _) => { if (!_closed.Remove(key)) _closed.Add(key); Present(); };
                _summary.Children.Add(heading); indent = 40;
                if (!open) continue;
            }
            foreach (var (_, label, value) in group)
                _summary.Children.Add(new TextBlock { Text = Localize.Text(label) + ": " + (label == "PDF Preset" && !preset.BuiltIn ? value : Localize.Text(value)), Margin = new Thickness(indent, 3, 0, 3), TextWrapping = TextWrapping.Wrap });
        }
    }
    private void Commit(string? selected)
    {
        try { PdfPresets.Save(_presets); _error.Text = ""; } catch (Exception error) { _error.Text = Localize.Text(error.Message); }
        Fill(selected);
    }
    private async Task Edit(PdfPreset? preset)
    {
        var start = preset ?? (Selected ?? PdfPresets.BuiltIn[0]) with { Name = UniqueName(Localize.Text("Custom")), BuiltIn = false, Description = Selected?.BuiltIn == true ? Localize.Text(Selected.Description) : Selected?.Description ?? "" };
        var dialog = new PdfPresetDialog(start, _presets.Where(p => p != preset).Select(p => p.Name).ToHashSet());
        if (!await dialog.ShowDialog<bool>(this)) return;
        if (preset is null) _presets.Add(dialog.Preset); else _presets[_presets.IndexOf(preset)] = dialog.Preset;
        Commit(dialog.Preset.Name);
    }
    private string UniqueName(string stem) { var name = stem; for (var i = 1; _presets.Any(p => p.Name == name); i++) name = $"{stem} {i}"; return name; }
    private void Delete() { if (Selected is { BuiltIn: false } preset) { var at = _presets.IndexOf(preset); _presets.Remove(preset); Commit(_presets.ElementAtOrDefault(Math.Max(0, at - 1))?.Name); } }
    private async Task Load()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = Localize.Text("Load PDF Presets"), FileTypeFilter = [new("Compositor PDF Presets") { Patterns = ["*.pdf-presets.json", "*.json"] }] });
            if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            var loaded = PdfPresets.Read(path).Select(p => p with { Name = _presets.Any(q => q.Name == p.Name) ? UniqueName(p.Name) : p.Name }).ToList();
            _presets.AddRange(loaded); Commit(loaded.FirstOrDefault()?.Name);
        }
        catch (Exception error) { _error.Text = Localize.Text(error.Message); }
    }
    private async Task SaveAs()
    {
        if (Selected is not { } preset) return;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new() { Title = Localize.Text("Save PDF Preset"), SuggestedFileName = preset.Name.Trim('[', ']') + ".pdf-presets.json", DefaultExtension = "json" });
            if (file?.TryGetLocalPath() is { } path) PdfPresets.Save([preset with { BuiltIn = false, Description = preset.BuiltIn ? Localize.Text(preset.Description) : preset.Description }], path);
        }
        catch (Exception error) { _error.Text = Localize.Text(error.Message); }
    }
}

/// <summary>PDF Presets ▸ New… and Edit…: the name, description, standard, compression and output of one preset.</summary>
internal sealed class PdfPresetDialog : SideDialog
{
    internal PdfPreset Preset { get; private set; }
    private readonly IReadOnlySet<string> _taken;
    private readonly TextBox _name = DialogKit.Line("pdf-preset-name"), _text = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 70 };
    private readonly ComboBox _standard, _downsample, _compression, _quality, _color;
    private readonly NumericUpDown _to, _above;
    private readonly TextBlock _problem = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
    internal PdfPresetDialog(PdfPreset start, IReadOnlySet<string> taken) : base("Edit PDF Preset", 720, "Save Preset")
    {
        Preset = start; _taken = taken; _name.Text = start.Name; _name.Width = 420; _text.Text = start.Description;
        _standard = Grouped([[("None", "None"), ("PdfA2b", "PDF/A-2b")]], start.Standard.ToString(), 200);
        _downsample = Grouped([[("None", "Do Not Downsample"), ("Average", "Average Downsampling To"), ("Bicubic", "Bicubic Downsampling To")]], start.Downsample.ToString(), 260);
        _compression = Grouped([[("Zip", "ZIP"), ("Jpeg", "JPEG")]], start.Compression.ToString(), 120);
        _quality = Grouped([[("10", "Minimum"), ("30", "Low"), ("50", "Medium"), ("80", "High"), ("100", "Maximum")]], new[] { 10, 30, 50, 80, 100 }.MinBy(q => Math.Abs(q - start.ImageQuality)).ToString(), 150);
        _color = Grouped([[("None", "No Conversion"), ("WorkingCmyk", "Convert to Working CMYK")]], start.ColorConversion.ToString(), 260);
        _to = Number(start.DownsampleTo, 9, 2400, 72); _above = Number(start.DownsampleAbove, 9, 4800, 72); _to.FormatString = _above.FormatString = "0";
        Body.Children.Add(DialogKit.Columns("110,*", 8, Caption("Preset", 110), _name, Caption("Standard", 110), _standard));
        Body.Children.Add(DialogKit.Group("Description", _text));
        Body.Children.Add(DialogKit.Group("Compression", new StackPanel { Spacing = 6, Children =
        {
            Line(_downsample, _to, new TextBlock { Text = Localize.Text("pixels/inch") }),
            Line(new TextBlock { Text = Localize.Text("For Images Above") + ":", Width = 260, TextAlignment = TextAlignment.Right }, _above, new TextBlock { Text = Localize.Text("pixels/inch") }),
            Line(Caption("Compression", 110), _compression, Caption("Image Quality", 110), _quality),
        } }));
        Body.Children.Add(DialogKit.Group("Output", Line(Caption("Color Conversion", 110), _color)));
        Body.Children.Add(_problem);
        _downsample.SelectionChanged += (_, _) => Update(); _compression.SelectionChanged += (_, _) => Update(); Update();
    }
    private void Update() { _to.IsEnabled = _above.IsEnabled = Key(_downsample) != "None"; _quality.IsEnabled = Key(_compression) == "Jpeg"; }
    protected override bool Validate()
    {
        var name = _name.Text?.Trim() ?? "";
        var preset = Preset with
        {
            Name = name, Description = _text.Text ?? "", BuiltIn = false, Standard = Enum.Parse<PdfStandard>(Key(_standard) ?? "None"), Downsample = Enum.Parse<PdfDownsample>(Key(_downsample) ?? "None"),
            DownsampleTo = (double)(_to.Value ?? 300), DownsampleAbove = (double)(_above.Value ?? 450), Compression = Enum.Parse<PdfCompression>(Key(_compression) ?? "Zip"),
            ImageQuality = int.Parse(Key(_quality) ?? "100", System.Globalization.CultureInfo.InvariantCulture), ColorConversion = Enum.Parse<PdfColorConversion>(Key(_color) ?? "None"),
        };
        if (name.StartsWith('[') || _taken.Contains(name)) { _problem.Text = Localize.Text("Choose a name that no other preset uses and that does not start with ["); return false; }
        if (!preset.IsValid) { _problem.Text = Localize.Text("Downsample above a resolution at least as high as the one images are brought down to."); return false; }
        Preset = preset; return true;
    }
}
