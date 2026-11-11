using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;

namespace Compositor.Desktop;

/// <summary>
/// Image ▸ Variables, laid out as Photoshop's: the Define / Data Sets list at the top (Ctrl+1, Ctrl+2); Define picks a
/// layer with ◀ ▶ and gives it Visibility and Pixel Replacement (or Text Replacement on a type layer) variables, with the
/// replacement's Method, Alignment and Clip to Bounding Box; Data Sets names sets of values for them; OK, Cancel, Previous
/// and Next down the right, and a description at the foot.
/// </summary>
internal sealed class VariablesDialog : DialogWindow
{
    private readonly CanvasDocument _document;
    private readonly List<ImageVariable> _variables;
    private readonly List<ImageDataSet> _sets;
    private readonly List<ImageLayer> _layers;
    private readonly ComboBox _page = new() { Width = 150, Tag = "variables page" };
    private readonly ComboBox _layer = new() { Width = 380, Tag = "variables layer" };
    private readonly ContentControl _body = new();
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap, MinHeight = 60 };
    private readonly MethodDiagram _diagram = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private bool _syncing;
    private int _set;
    internal bool Accepted { get; private set; }
    internal bool ApplySet { get; private set; }
    internal IReadOnlyList<ImageVariable> Variables => _variables;
    internal IReadOnlyList<ImageDataSet> DataSets => _sets;
    internal int ChosenSet => _set;
    internal string? ImportedFrom { get; private set; }

    internal VariablesDialog(CanvasDocument document, Guid? selected, bool dataSets, bool apply = false)
    {
        _document = document; _variables = [.. document.Variables]; _sets = [.. document.DataSets];
        _layers = document.Layers.AsEnumerable().Reverse().Where(l => !l.IsGroup).ToList();
        Title = Localize.Text(apply ? "Apply Data Set" : "Variables"); Width = 930; Height = 770; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _page.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<int>((i, _) => new Grid
        {
            ColumnDefinitions = new("*,Auto"), MinWidth = 140,
            Children = { new TextBlock { Text = Localize.Text(i == 0 ? "Define" : "Data Sets") }, new TextBlock { Text = i == 0 ? "Ctrl+1" : "Ctrl+2", [Grid.ColumnProperty] = 1, Margin = new Thickness(16, 0, 0, 0) } },
        });
        _page.SelectionBoxItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<int>((i, _) => new TextBlock { Text = Localize.Text(i == 0 ? "Define" : "Data Sets") });
        _page.ItemsSource = new[] { 0, 1 }; _page.SelectedIndex = dataSets ? 1 : 0;
        _page.SelectionChanged += (_, _) => ShowPage();
        _layer.SelectionChanged += (_, _) => { if (!_syncing) ShowPage(); };
        KeyDown += (_, e) => { if (e.KeyModifiers == KeyModifiers.Control && e.Key is Key.D1 or Key.D2) { _page.SelectedIndex = e.Key == Key.D1 ? 0 : 1; e.Handled = true; } };
        var start = Math.Max(0, _layers.FindIndex(l => l.ID == selected)); FillLayers(start);
        ApplySet = apply;
        var previous = AdjustmentKit.Action(this, "Previous", 'P', () => Step(-1)); var next = AdjustmentKit.Action(this, "Next", 'N', () => Step(1));
        var actions = AdjustmentKit.Actions(this, () => { Accepted = true; Close(true); }, () => Close(false), null, previous, next);
        var main = new StackPanel { Spacing = 8, Children = { _body, AdjustmentKit.Group("Description", new StackPanel { Spacing = 8, Children = { _description, _diagram } }) } };
        Content = AdjustmentKit.Body(AdjustmentKit.Group("", main, _page), actions);
        ShowPage();
    }

    private void FillLayers(int select)
    {
        _syncing = true;
        // A layer with variables is marked with an asterisk, as Photoshop marks it.
        _layer.ItemsSource = _layers.Select(l => l.Name + (_variables.Any(v => v.LayerID == l.ID) ? " *" : "")).ToArray();
        _layer.SelectedIndex = Math.Clamp(select, 0, _layers.Count - 1); _syncing = false;
    }

    private void Step(int by)
    {
        if (_page.SelectedIndex == 0) { if (_layers.Count > 0) _layer.SelectedIndex = (_layer.SelectedIndex + by + _layers.Count) % _layers.Count; }
        else if (_sets.Count > 0) { _set = (_set + by + _sets.Count) % _sets.Count; ShowPage(); }
    }

    private void ShowPage()
    {
        if (_page.SelectedIndex == 1) { ShowSets(); return; }
        _description.Text = Localize.Text("Specify the layer's variables to control its visibility, replace text strings or replace pixels.");
        _diagram.IsVisible = false;
        if (_layers.Count == 0) { _body.Content = new TextBlock { Text = Localize.Text("The document has no layers to define variables for.") }; return; }
        var layer = _layers[Math.Max(0, _layer.SelectedIndex)];
        var visibility = _variables.FirstOrDefault(v => v.LayerID == layer.ID && v.Kind == VariableKind.Visibility);
        var text = layer.LiveText is not null;
        var replacing = _variables.FirstOrDefault(v => v.LayerID == layer.ID && v.Kind == (text ? VariableKind.Text : VariableKind.PixelReplacement));
        var visible = new CheckBox { Content = new TextBlock { Text = Localize.Text("Visibility") + "(V)" }, IsChecked = visibility is not null, Tag = "Visibility" };
        var visibleName = new TextBox { Width = 210, Text = visibility?.Name ?? "", IsEnabled = visibility is not null, Tag = "Visibility name" };
        var replace = new CheckBox { Content = new TextBlock { Text = Localize.Text(text ? "Text Replacement" : "Pixel Replacement") + (text ? "(T)" : "(X)") }, IsChecked = replacing is not null, Tag = text ? "Text Replacement" : "Pixel Replacement" };
        var replaceName = new TextBox { Width = 210, Text = replacing?.Name ?? "", IsEnabled = replacing is not null, Tag = "Replacement name" };
        var methods = new[] { "Fit", "Fill", "As Is", "Conform" };
        var method = new ComboBox { ItemsSource = new[] { AdjustmentKit.Word("Fit", "限制"), AdjustmentKit.Word("Fill", "填充"), AdjustmentKit.Word("As Is", "保持原样"), AdjustmentKit.Word("Conform", "一致") }, SelectedIndex = (int)(replacing?.Method ?? PixelMethod.Fit), Width = 120, Tag = "Method" };
        var alignment = replacing?.Alignment ?? 4;
        var clip = new CheckBox { Content = new TextBlock { Text = Localize.Text("Clip to Bounding Box") + "(C)" }, IsChecked = replacing?.Clip == true, Tag = "Clip to Bounding Box" };
        var grid = new UniformGrid { Rows = 3, Columns = 3, Width = 30, Height = 30, Tag = "Alignment" };
        var cells = Enumerable.Range(0, 9).Select(i => new Border { Margin = new Thickness(1), BorderBrush = Skin.SecondaryBrush, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand) }).ToArray();
        void Cells() { for (var i = 0; i < 9; i++) cells[i].Background = i == alignment ? Brushes.White : Brushes.Transparent; }
        for (var i = 0; i < 9; i++) { var index = i; cells[i].PointerPressed += (_, _) => { if (!grid.IsEnabled) return; alignment = index; Cells(); Save(); }; grid.Children.Add(cells[i]); }
        Cells();
        void Enable()
        {
            visibleName.IsEnabled = visible.IsChecked == true; replaceName.IsEnabled = replace.IsChecked == true;
            var pixels = !text && replace.IsChecked == true; method.IsEnabled = grid.IsEnabled = pixels; clip.IsEnabled = pixels && method.SelectedIndex is 1 or 2; grid.IsEnabled = pixels && method.SelectedIndex != 3;
            _diagram.IsVisible = pixels; _diagram.Method = method.SelectedIndex; _diagram.InvalidateVisual();
        }
        string Unique(string stem) { var n = 1; while (_variables.Any(v => v.Name.Equals($"{stem}{n}", StringComparison.OrdinalIgnoreCase))) n++; return $"{stem}{n}"; }
        void Save()
        {
            if (_syncing) return;
            _variables.RemoveAll(v => v.LayerID == layer.ID);
            if (visible.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(visibleName.Text)) visibleName.Text = Unique(Localize.Text("Visibility Variable"));
                _variables.RemoveAll(v => v.Name.Equals(visibleName.Text!.Trim(), StringComparison.OrdinalIgnoreCase));
                _variables.Add(new ImageVariable(visibleName.Text!.Trim(), layer.ID, VariableKind.Visibility));
            }
            if (replace.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(replaceName.Text)) replaceName.Text = Unique(Localize.Text(text ? "Text Variable" : "Pixel Variable"));
                _variables.RemoveAll(v => v.Name.Equals(replaceName.Text!.Trim(), StringComparison.OrdinalIgnoreCase));
                _variables.Add(text ? new ImageVariable(replaceName.Text!.Trim(), layer.ID, VariableKind.Text)
                    : new ImageVariable(replaceName.Text!.Trim(), layer.ID, VariableKind.PixelReplacement, (PixelMethod)Math.Max(0, method.SelectedIndex), alignment, clip.IsChecked == true));
            }
            FillLayers(_layer.SelectedIndex); Enable();
        }
        visible.IsCheckedChanged += (_, _) => Save(); replace.IsCheckedChanged += (_, _) => Save();
        visibleName.LostFocus += (_, _) => Save(); replaceName.LostFocus += (_, _) => Save();
        method.SelectionChanged += (_, _) => Save(); clip.IsCheckedChanged += (_, _) => Save();
        LayerDialogControls.Key(this, 'V', visible); LayerDialogControls.Key(this, text ? 'T' : 'X', replace); LayerDialogControls.Key(this, 'A', visibleName); LayerDialogControls.Key(this, 'E', replaceName); LayerDialogControls.Key(this, 'C', clip);
        Enable();
        var arrows = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { Arrow("◀", -1), Arrow("▶", 1) } };
        Button Arrow(string glyph, int by) { var b = new Button { Content = glyph, Width = 28, Height = 28, Padding = default, Background = Brushes.Transparent, BorderThickness = default, HorizontalContentAlignment = HorizontalAlignment.Center, Tag = glyph }; b.Click += (_, _) => Step(by); return b; }
        TextBlock Caption(string s, char key) => new() { Text = Localize.Text(s) + $"({key}):", VerticalAlignment = VerticalAlignment.Center, Width = 90 };
        var types = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                visible, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Caption("Name", 'A'), visibleName } },
                replace, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Caption("Name", 'E'), replaceName } },
            },
        };
        if (!text) types.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(150, 0, 0, 0), Children = { new TextBlock { Text = Localize.Text("Method") + ":", VerticalAlignment = VerticalAlignment.Center }, method, new TextBlock { Text = Localize.Text("Alignment") + ":", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) }, grid } });
        if (!text) { clip.Margin = new Thickness(118, 0, 0, 0); types.Children.Add(clip); }
        _body.Content = new StackPanel
        {
            Spacing = 4,
            Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(44, 0, 0, 0), Children = { new TextBlock { Text = Localize.Text("Layer") + ":", VerticalAlignment = VerticalAlignment.Center }, _layer, arrows } }, AdjustmentKit.Group("Variable Type", types) },
        };
    }

    private void ShowSets()
    {
        _description.Text = Localize.Text("Data sets give each variable a value. Import a CSV or tab-separated file whose first row names the variables.");
        _diagram.IsVisible = false;
        _set = Math.Clamp(_set, 0, Math.Max(0, _sets.Count - 1));
        var setBox = new ComboBox { Width = 300, ItemsSource = _sets.Select(s => s.Name).ToArray(), SelectedIndex = _sets.Count > 0 ? _set : -1, Tag = "data set" };
        setBox.SelectionChanged += (_, _) => { if (setBox.SelectedIndex >= 0 && setBox.SelectedIndex != _set) { _set = setBox.SelectedIndex; ShowSets(); } };
        Button Icon(string glyph, string tip, Action click) { var b = new Button { Content = IconSet.Has(glyph) ? new IconGlyph(glyph) { Width = 16, Height = 16 } : glyph, Width = 30, Height = 28, Padding = default, HorizontalContentAlignment = HorizontalAlignment.Center, Tag = tip }; ToolTip.SetTip(b, Localize.Text(tip)); b.Click += (_, _) => click(); return b; }
        var add = Icon("plus", "Create New Data Set", () =>
        {
            var values = _variables.ToDictionary(v => v.Name, v => _sets.Count > 0 && _sets[_set].Values.TryGetValue(v.Name, out var old) ? old : v.Kind == VariableKind.Visibility ? "true" : "", StringComparer.OrdinalIgnoreCase);
            var n = _sets.Count + 1; while (_sets.Any(s => s.Name == Localize.Text("Data Set") + " " + n)) n++;
            _sets.Add(new ImageDataSet(Localize.Text("Data Set") + " " + n, values)); _set = _sets.Count - 1; ShowSets();
        });
        var delete = Icon("delete", "Delete Data Set", () => { if (_sets.Count == 0) return; _sets.RemoveAt(_set); _set = Math.Max(0, _set - 1); ShowSets(); });
        var import = AdjustmentKit.Action(this, "Import…", 'I', async () =>
        {
            var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Import Data Sets…"), FileTypeFilter = [new FilePickerFileType(Localize.Text("Data sets")) { Patterns = ["*.csv", "*.tsv", "*.txt"] }] });
            if (picked.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            try
            {
                var imported = ImageVariables.ParseCsv(await File.ReadAllTextAsync(path)).Select(set => set with
                {
                    Values = set.Values.ToDictionary(v => v.Key, v => _variables.Any(variable => variable.Name.Equals(v.Key, StringComparison.OrdinalIgnoreCase) && variable.Kind == VariableKind.PixelReplacement)
                        ? Path.GetFullPath(v.Value, Path.GetDirectoryName(path)!) : v.Value, StringComparer.OrdinalIgnoreCase),
                });
                _sets.Clear(); _sets.AddRange(imported); _set = 0; ImportedFrom = path; ShowSets();
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { _description.Text = Localize.Text("Could not import data sets.") + " " + e.Message; }
        });
        // The variables of the chosen set, name and value, and a field to change the value.
        var rows = new ListBox { Height = 200, Tag = "data set values" };
        var name = new ComboBox { Width = 200, ItemsSource = _variables.Select(v => v.Name).ToArray(), Tag = "variable name" };
        var value = new TextBox { Width = 300, Tag = "variable value" };
        var invisible = new CheckBox { Content = Localize.Text("Invisible"), Tag = "Invisible" };
        void Rows() => rows.ItemsSource = _sets.Count == 0 ? Array.Empty<string>() : _variables.Select(v => $"{v.Name}\t{(_sets[_set].Values.TryGetValue(v.Name, out var x) ? x : "")}").ToArray();
        void Pick()
        {
            if (_sets.Count == 0 || name.SelectedIndex < 0) return; var variable = _variables[name.SelectedIndex];
            var current = _sets[_set].Values.TryGetValue(variable.Name, out var x) ? x : "";
            value.IsVisible = variable.Kind != VariableKind.Visibility; invisible.IsVisible = !value.IsVisible;
            value.Text = current; invisible.IsChecked = current is "false" or "0";
        }
        void Put(string text)
        {
            if (_sets.Count == 0 || name.SelectedIndex < 0) return;
            var values = new Dictionary<string, string>(_sets[_set].Values, StringComparer.OrdinalIgnoreCase) { [_variables[name.SelectedIndex].Name] = text };
            _sets[_set] = _sets[_set] with { Values = values }; Rows();
        }
        name.SelectionChanged += (_, _) => Pick(); value.LostFocus += (_, _) => Put(value.Text ?? ""); invisible.IsCheckedChanged += (_, _) => Put(invisible.IsChecked == true ? "false" : "true");
        rows.SelectionChanged += (_, _) => { if (rows.SelectedIndex >= 0) name.SelectedIndex = rows.SelectedIndex; };
        if (_variables.Count > 0) name.SelectedIndex = 0;
        Rows(); Pick();
        var arrows = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { Icon("◀", "Previous Data Set", () => Step(-1)), Icon("▶", "Next Data Set", () => Step(1)), add, delete } };
        var apply = new CheckBox { Content = Localize.Text("Apply the chosen data set"), IsChecked = ApplySet, IsEnabled = _sets.Count > 0, Tag = "Apply data set" };
        apply.IsCheckedChanged += (_, _) => ApplySet = apply.IsChecked == true;
        _body.Content = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = Localize.Text("Data Set") + ":", VerticalAlignment = VerticalAlignment.Center }, setBox, arrows, import } },
                AdjustmentKit.Group("Variables", new StackPanel { Spacing = 8, Children = { rows,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = Localize.Text("Name") + ":", VerticalAlignment = VerticalAlignment.Center }, name, new TextBlock { Text = Localize.Text("Value") + ":", VerticalAlignment = VerticalAlignment.Center }, value, invisible } } } }),
                apply,
            },
        };
    }

    /// <summary>How the replacement image sits in the layer's box for the chosen method, as Photoshop's description draws it.</summary>
    private sealed class MethodDiagram : Control
    {
        internal int Method { get; set; }
        protected override Size MeasureOverride(Size availableSize) => new(140, 110);
        public override void Render(DrawingContext context)
        {
            var box = new Rect(40, 10, 60, 90); var image = Method switch { 0 => new Rect(40, 35, 60, 40), 1 => new Rect(10, 10, 120, 90), 2 => new Rect(25, 30, 90, 50), _ => box };
            context.FillRectangle(new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)), image);
            context.DrawRectangle(null, new Pen(Brushes.White, 2), box);
            foreach (var corner in new[] { box.TopLeft, box.TopRight, box.BottomLeft, box.BottomRight }) context.FillRectangle(Brushes.White, new Rect(corner.X - 3, corner.Y - 3, 6, 6));
        }
    }
}

/// <summary>Image ▸ Analysis ▸ Set Measurement Scale ▸ Custom…: Pixel Length, Logical Length and Logical Units, with the
/// presets saved here and the scale read out under them.</summary>
internal sealed class MeasurementScaleDialog : DialogWindow
{
    internal static string PresetPath => Path.Combine(AppPaths.SettingsDirectory, "measurement-scales.json");
    internal static Dictionary<string, MeasurementScale> Presets()
    {
        try { if (File.Exists(PresetPath) && JsonSerializer.Deserialize<Dictionary<string, MeasurementScale>>(File.ReadAllText(PresetPath)) is { } saved) return saved.Where(p => p.Value.IsValid && p.Key.Length <= 128).Take(200).ToDictionary(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        return [];
    }
    private static void Save(Dictionary<string, MeasurementScale> presets)
    {
        try { Directory.CreateDirectory(AppPaths.SettingsDirectory); File.WriteAllText(PresetPath, JsonSerializer.Serialize(presets)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
    internal MeasurementScale? Result { get; private set; }

    internal MeasurementScaleDialog(MeasurementScale start, double? measured)
    {
        Title = Localize.Text("Measurement Scale"); SizeToContent = SizeToContent.WidthAndHeight; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var pixels = new ValueBox(.001, 1000000, measured ?? start.Pixels, "0.####", tag: "Pixel Length") { Width = 120 };
        var logical = new ValueBox(.001, 1000000, start.Units, "0.####", tag: "Logical Length") { Width = 140 };
        var unit = new TextBox { Text = start.Unit, Width = 310, Tag = "Logical Units" };
        var readout = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center };
        void Read() => readout.Text = $"{pixels.Value:0.####} {Localize.Text("Pixels")} = {logical.Value:0.0000} {unit.Text}";
        pixels.ValueChanged += Read; logical.ValueChanged += Read; unit.TextChanged += (_, _) => Read(); Read();
        var presets = Presets();
        var preset = new ComboBox { Width = 400, Tag = "scale preset" };
        void Fill() { preset.ItemsSource = new[] { Localize.Text("Custom") }.Concat(presets.Keys).ToArray(); preset.SelectedIndex = 0; }
        Fill();
        preset.SelectionChanged += (_, _) =>
        {
            if (preset.SelectedIndex <= 0) return; var chosen = presets.ElementAt(preset.SelectedIndex - 1).Value;
            pixels.Value = chosen.Pixels; logical.Value = chosen.Units; unit.Text = chosen.Unit;
        };
        var delete = AdjustmentKit.Action(this, "Delete Preset…", 'D', () => { if (preset.SelectedIndex <= 0) return; presets.Remove(presets.ElementAt(preset.SelectedIndex - 1).Key); Save(presets); Fill(); });
        delete.IsEnabled = false; preset.SelectionChanged += (_, _) => delete.IsEnabled = preset.SelectedIndex > 0;
        var save = AdjustmentKit.Action(this, "Save Preset…", 'S', async () =>
        {
            var ask = new FormDialog("Save Preset", 420, 200); var name = ask.Text("Name", $"{pixels.Value:0.##} px = {logical.Value:0.##} {unit.Text}");
            ask.Validate = () => string.IsNullOrWhiteSpace(name.Text) || name.Text.Length > 128 ? "Enter a name." : null;
            if (!await ask.ShowDialog<bool>(this)) return;
            presets[name.Text!.Trim()] = new MeasurementScale(pixels.Value, logical.Value, string.IsNullOrWhiteSpace(unit.Text) ? "px" : unit.Text.Trim()); Save(presets); Fill();
        });
        LayerDialogControls.Key(this, 'X', pixels); LayerDialogControls.Key(this, 'L', logical); LayerDialogControls.Key(this, 'U', unit);
        TextBlock Right(string text, char key) => new() { Text = Localize.Text(text) + $"({key}):", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var form = new Grid { ColumnDefinitions = new("Auto,Auto"), RowDefinitions = new("Auto,Auto,Auto"), ColumnSpacing = 8, RowSpacing = 8 };
        void Put(Control c, int r, int col) { Grid.SetRow(c, r); Grid.SetColumn(c, col); form.Children.Add(c); }
        Put(Right("Pixel Length", 'X'), 0, 0); Put(pixels, 0, 1); Put(Right("Logical Length", 'L'), 1, 0); Put(logical, 1, 1); Put(Right("Logical Units", 'U'), 2, 0); Put(unit, 2, 1);
        pixels.HorizontalAlignment = logical.HorizontalAlignment = HorizontalAlignment.Left;
        var note = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12,
            Children =
            {
                new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), BorderBrush = Skin.LabelBrush, BorderThickness = new Thickness(2), Child = new TextBlock { Text = "i", FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } },
                new TextBlock { Text = Localize.Text(measured is null ? "Measure a length with the Ruler tool before opening this dialog: its length fills Pixel Length." : "Pixel Length is the length of the last line drawn with the Ruler tool."), TextWrapping = TextWrapping.Wrap, Width = 430, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        var preRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { new TextBlock { Text = Localize.Text("Presets") + ":", VerticalAlignment = VerticalAlignment.Center }, preset } };
        var left = new StackPanel { Width = 520, Spacing = 8, Children = { preRow, new Border { Height = 1, Background = Skin.BorderControlBrush }, form, readout, note } };
        var actions = AdjustmentKit.Actions(this, () =>
        {
            pixels.Commit(); logical.Commit(); var scale = new MeasurementScale(pixels.Value, logical.Value, string.IsNullOrWhiteSpace(unit.Text) ? "px" : unit.Text.Trim());
            if (!scale.IsValid) return; Result = scale; Close(true);
        }, () => Close(false), null, save, delete);
        actions.Width = 160;
        Content = AdjustmentKit.Body(left, actions);
    }
}

/// <summary>Image ▸ Analysis ▸ Select Data Points ▸ Custom…: Photoshop's data points in their four groups, each ticked or not,
/// with Select All and Deselect All and the presets saved here.</summary>
internal sealed class DataPointsDialog : DialogWindow
{
    internal static string PresetPath => Path.Combine(AppPaths.SettingsDirectory, "data-point-presets.json");
    internal static Dictionary<string, string[]> Presets()
    {
        try
        {
            if (File.Exists(PresetPath) && JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(PresetPath)) is { } saved)
                return saved.Where(p => p.Key.Length <= 128 && p.Value.All(ImageAnalysis.DataPoints.Contains)).Take(200).ToDictionary();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        return [];
    }
    internal string[]? Result { get; private set; }

    internal DataPointsDialog(string[] start)
    {
        Title = Localize.Text("Select Data Points"); SizeToContent = SizeToContent.WidthAndHeight; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var boxes = new List<(string Field, CheckBox Box)>();
        var list = new StackPanel { Spacing = 2 };
        foreach (var (group, fields) in ImageAnalysis.DataPointGroups)
        {
            if (list.Children.Count > 0) list.Children.Add(new Border { Height = 1, Background = Skin.BorderControlBrush, Margin = new Thickness(0, 6) });
            list.Children.Add(new TextBlock { Text = Localize.Text(group), Foreground = Skin.SecondaryBrush, Margin = new Thickness(0, 2, 0, 4) });
            foreach (var field in fields)
            {
                // "Mean" and "Count" keep the names older projects saved; Photoshop calls them Gray Value (Mean) and the Count tool's Count.
                var box = new CheckBox { Content = field == "Scale" ? AdjustmentKit.Word("Scale", "比例") : Localize.Text(field == "Mean" ? "Gray Value (Mean)" : field is "Selection Count" or "Ruler Count" ? "Count" : field), IsChecked = start.Contains(field), Margin = new Thickness(14, 0, 0, 0), Tag = field };
                boxes.Add((field, box)); list.Children.Add(box);
            }
        }
        var scroll = new ScrollViewer { Content = list, Height = 900, Width = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Visible, Padding = new Thickness(6) };
        var all = AdjustmentKit.Action(this, "Select All", 'A', () => { foreach (var (_, box) in boxes) box.IsChecked = true; });
        var none = AdjustmentKit.Action(this, "Deselect All", 'L', () => { foreach (var (_, box) in boxes) box.IsChecked = false; });
        void Buttons() { all.IsEnabled = boxes.Any(b => b.Box.IsChecked != true); none.IsEnabled = boxes.Any(b => b.Box.IsChecked == true); }
        foreach (var (_, box) in boxes) box.IsCheckedChanged += (_, _) => Buttons(); Buttons();
        var presets = Presets();
        var preset = new ComboBox { Width = 280, Tag = "data point preset" };
        void Fill() { preset.ItemsSource = new[] { Localize.Text("Custom") }.Concat(presets.Keys).ToArray(); preset.SelectedIndex = 0; }
        Fill();
        preset.SelectionChanged += (_, _) => { if (preset.SelectedIndex <= 0) return; var chosen = presets.ElementAt(preset.SelectedIndex - 1).Value; foreach (var (field, box) in boxes) box.IsChecked = chosen.Contains(field); };
        void Store() { try { Directory.CreateDirectory(AppPaths.SettingsDirectory); File.WriteAllText(PresetPath, JsonSerializer.Serialize(presets)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
        var delete = AdjustmentKit.Action(this, "Delete Preset", null, () => { if (preset.SelectedIndex <= 0) return; presets.Remove(presets.ElementAt(preset.SelectedIndex - 1).Key); Store(); Fill(); });
        delete.IsEnabled = false; preset.SelectionChanged += (_, _) => delete.IsEnabled = preset.SelectedIndex > 0;
        var save = AdjustmentKit.Action(this, "Save Preset…", null, async () =>
        {
            var ask = new FormDialog("Save Preset", 420, 200); var name = ask.Text("Name", Localize.Text("Data Points"));
            ask.Validate = () => string.IsNullOrWhiteSpace(name.Text) || name.Text.Length > 128 ? "Enter a name." : null;
            if (!await ask.ShowDialog<bool>(this)) return;
            presets[name.Text!.Trim()] = boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Field).ToArray(); Store(); Fill();
        });
        var left = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = Localize.Text("Preset") + ":", VerticalAlignment = VerticalAlignment.Center }, preset } },
                AdjustmentKit.Group("Data Points", new StackPanel { Spacing = 10, Children = { new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Child = scroll },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { all, none } } } }),
            },
        };
        all.Width = 114; none.Width = 194;
        var actions = AdjustmentKit.Actions(this, () => { Result = boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Field).ToArray(); Close(true); }, () => Close(false), null, save, delete);
        Content = AdjustmentKit.Body(left, actions);
    }
}

/// <summary>Image ▸ Analysis ▸ Place Scale Marker…: Length in the measurement scale's units, Font and Font Size, Display Text,
/// and where the text goes and what color the marker is.</summary>
internal sealed class ScaleMarkerDialog : DialogWindow
{
    internal (double Length, string Font, double Points, bool ShowText, bool Top, bool White)? Result { get; private set; }
    private static readonly int[] Sizes = [4, 6, 8, 9, 10, 11, 12, 14, 18, 24, 30, 36, 48, 60, 72];

    internal ScaleMarkerDialog(MeasurementScale scale, double length)
    {
        Title = Localize.Text("Measurement Scale Marker"); SizeToContent = SizeToContent.WidthAndHeight; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var lengthBox = new ValueBox(.001, 1000000, length, "0.###", tag: "Length") { Width = 120 };
        var families = FontCatalog.Families;
        var preferred = new[] { "SimSun", "宋体", "Microsoft YaHei", "Segoe UI" }.FirstOrDefault(f => families.Contains(f)) ?? families.FirstOrDefault() ?? "Segoe UI";
        var font = new ComboBox { ItemsSource = families, SelectedItem = preferred, Width = 130, Tag = "Font", MaxDropDownHeight = 400 };
        var size = new ComboBox { ItemsSource = Sizes.Select(s => $"{s} {Localize.Text("pt")}").ToArray(), SelectedIndex = 0, Width = 120, Tag = "Font Size" };
        var show = new CheckBox { Content = new TextBlock { Text = Localize.Text("Display Text") + "(X)" }, IsChecked = true, Tag = "Display Text" };
        var bottom = new RadioButton { Content = AdjustmentKit.Word("Bottom", "底部"), GroupName = "position", IsChecked = true, Tag = "Bottom" };
        var top = new RadioButton { Content = AdjustmentKit.Word("Top", "顶部"), GroupName = "position", Tag = "Top" };
        var black = new RadioButton { Content = Localize.Text("Black"), GroupName = "color", IsChecked = true, Tag = "Black" };
        var white = new RadioButton { Content = Localize.Text("White"), GroupName = "color", Tag = "White" };
        show.IsCheckedChanged += (_, _) => bottom.IsEnabled = top.IsEnabled = show.IsChecked == true;
        LayerDialogControls.Key(this, 'X', show);
        var first = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10,
            Children = { new TextBlock { Text = Localize.Text("Length") + ":", Width = 60, VerticalAlignment = VerticalAlignment.Center }, lengthBox, new TextBlock { Text = $"x {scale.Pixels:0.####} {(scale.Unit == "px" ? Localize.Text("Pixels") : scale.Unit)}", VerticalAlignment = VerticalAlignment.Center } },
        };
        var second = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10,
            Children = { new TextBlock { Text = Localize.Text("Font") + ":", Width = 60, VerticalAlignment = VerticalAlignment.Center }, font, new TextBlock { Text = Localize.Text("Font Size") + ":", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) }, size },
        };
        var groups = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 14,
            Children = { AdjustmentKit.Group("Text Position", new StackPanel { Spacing = 10, Width = 110, Children = { bottom, top } }), AdjustmentKit.Group("Color", new StackPanel { Spacing = 10, Width = 100, Children = { black, white } }) },
        };
        var left = new StackPanel { Spacing = 14, Children = { first, second, show, groups } };
        var actions = AdjustmentKit.Actions(this, () =>
        {
            lengthBox.Commit();
            Result = (lengthBox.Value, font.SelectedItem as string ?? preferred, Sizes[Math.Max(0, size.SelectedIndex)], show.IsChecked == true, top.IsChecked == true, white.IsChecked == true);
            Close(true);
        }, () => Close(false), null);
        actions.Width = 90;
        Content = AdjustmentKit.Body(left, actions);
    }
}

/// <summary>Image ▸ Trap…: the trap width in pixels, points or millimeters.</summary>
internal sealed class TrapDialog : DialogWindow
{
    internal int? Pixels { get; private set; }
    internal TrapDialog(double resolution)
    {
        Title = Localize.Text("Trap"); SizeToContent = SizeToContent.WidthAndHeight; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var width = new ValueBox(.01, 10, 1, "0.##", tag: "Trap Width") { Width = 84 };
        var unit = new ComboBox { ItemsSource = new[] { "Pixels", "Points", "Millimeters" }.Select(Localize.Text).ToArray(), SelectedIndex = 0, Width = 96, Tag = "Trap unit" };
        LayerDialogControls.Key(this, 'W', width);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { new TextBlock { Text = Localize.Text("Width") + "(W)", VerticalAlignment = VerticalAlignment.Center }, width, unit } };
        var actions = AdjustmentKit.Actions(this, () =>
        {
            width.Commit();
            var pixels = unit.SelectedIndex switch { 1 => width.Value / 72 * resolution, 2 => width.Value / 25.4 * resolution, _ => width.Value };
            Pixels = (int)Math.Clamp(Math.Round(pixels), 1, 10); Close(true);
        }, () => Close(false), null);
        actions.Width = 140;
        Content = AdjustmentKit.Body(row, actions);
    }
}
