using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Compositor.Core.IO;

namespace Compositor.Desktop;

internal sealed class ColorSettingsDialog : DialogWindow
{
    internal ColorManagementSettings Draft { get; private set; }
    private readonly Action<ColorManagementSettings> _preview;
    private readonly ColorManagementSettings _original;
    private TextBlock _description = null!, _error = null!;
    private readonly ContentControl _host = new();
    private bool _showPreview = true, _accepted;
    internal ColorSettingsDialog(ColorManagementSettings value, Action<ColorManagementSettings> preview)
    {
        Draft = value.Copy(); _original = value.Copy(); _preview = preview;
        Title = Localize.Text("Color Settings"); Width = 1120; Height = 780; MinWidth = 860; MinHeight = 580; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Closed += (_, _) => { if (!_accepted) _preview(_original); };
        Content = _host; Build();
    }
    private void Change(ColorManagementSettings value) { Draft = value with { Preset = "Custom" }; if (_showPreview && Draft.IsValid) _preview(Draft); }
    private void Build()
    {
        _description = new() { TextWrapping = TextWrapping.Wrap, MinHeight = 95 };
        _error = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.OrangeRed };
        var root = new Grid { ColumnDefinitions = new("*,128"), RowDefinitions = new("*,Auto,Auto"), Margin = new Thickness(18) };
        var fields = new StackPanel { Spacing = 12 };
        var presets = Choice(ColorPresets.Names, Array.IndexOf(ColorPresets.Names, Draft.Preset), async i => { await Attempt(() => { Draft = ColorPresets.Select(ColorPresets.Names[i], Draft); if (_showPreview) _preview(Draft); Build(); return Task.CompletedTask; }); });
        fields.Children.Add(Row("Settings:", presets, 100));
        var columns = new Grid { ColumnDefinitions = new("1.1*,*") };
        var left = new StackPanel { Spacing = 16, Margin = new Thickness(0, 0, 12, 0) }; var right = new StackPanel { Spacing = 16 };
        var working = new StackPanel { Spacing = 8 };
        ProfileRow(working, "RGB:", "RGB", Draft.Rgb, s => Draft with { Rgb = s });
        ProfileRow(working, "CMYK:", "CMYK", Draft.Cmyk, s => Draft with { Cmyk = s });
        ProfileRow(working, "Gray:", "GRAY", Draft.Gray, s => Draft with { Gray = s });
        ProfileRow(working, "Spot:", "GRAY", Draft.Spot, s => Draft with { Spot = s });
        left.Children.Add(Group("Working Spaces", working));
        var policies = new StackPanel { Spacing = 7 };
        foreach (var (label, mode, policy) in new[] { ("RGB:", 0, Draft.RgbPolicy), ("CMYK:", 1, Draft.CmykPolicy), ("Gray:", 2, Draft.GrayPolicy) })
            policies.Children.Add(Row(label, Choice(["Off", "Preserve Embedded Profiles", "Convert to Working Space"], (int)policy, i => Change(mode == 0 ? Draft with { RgbPolicy = (ColorPolicy)i } : mode == 1 ? Draft with { CmykPolicy = (ColorPolicy)i } : Draft with { GrayPolicy = (ColorPolicy)i })), 68));
        policies.Children.Add(Label("Profile Mismatches:"));
        var mismatch = new WrapPanel(); mismatch.Children.Add(Check("Ask When Opening", Draft.AskMismatchOpen, b => Change(Draft with { AskMismatchOpen = b }))); mismatch.Children.Add(Check("Ask When Pasting", Draft.AskMismatchPaste, b => Change(Draft with { AskMismatchPaste = b }))); policies.Children.Add(mismatch);
        policies.Children.Add(Check("Missing Profiles: Ask When Opening", Draft.AskMissingOpen, b => Change(Draft with { AskMissingOpen = b })));
        left.Children.Add(Group("Color Management Policies", policies));
        var conversion = new StackPanel { Spacing = 7 };
        conversion.Children.Add(Row("Engine:", Choice(["Little CMS", "Microsoft ICM", "ACE Compatible (Independent)"], (int)Draft.Engine, i => { Change(Draft with { Engine = (ColorEngine)i }); Build(); }), 70));
        conversion.Children.Add(Row("Intent:", Choice(["Perceptual", "Saturation", "Relative Colorimetric", "Absolute Colorimetric"], (int)Draft.Intent, i => Change(Draft with { Intent = (ColorIntent)i })), 70));
        var bpc = Check("Use Black Point Compensation", Draft.BlackPointCompensation, b => Change(Draft with { BlackPointCompensation = b })); bpc.IsEnabled = Draft.Engine != ColorEngine.MicrosoftIcm; conversion.Children.Add(bpc);
        conversion.Children.Add(Check("Use Dither (8-bit/channel images)", Draft.Dither, b => Change(Draft with { Dither = b })));
        conversion.Children.Add(Check("Compensate for Scene-referred Profiles", Draft.CompensateSceneProfiles, b => Change(Draft with { CompensateSceneProfiles = b })));
        right.Children.Add(Group("Conversion Options", conversion));
        var advanced = new StackPanel { Spacing = 8 };
        Advanced(advanced, "Desaturate Monitor Colors:", Draft.DesaturateMonitor, Draft.MonitorDesaturation, 0, 100, b => Change(Draft with { DesaturateMonitor = b }), n => Change(Draft with { MonitorDesaturation = n }), "%");
        Advanced(advanced, "Blend RGB Colors Using Gamma:", Draft.BlendRgbGamma, Draft.RgbGamma, .1, 10, b => Change(Draft with { BlendRgbGamma = b }), n => Change(Draft with { RgbGamma = n }));
        Advanced(advanced, "Blend Text Colors Using Gamma:", Draft.BlendTextGamma, Draft.TextGamma, .1, 10, b => Change(Draft with { BlendTextGamma = b }), n => Change(Draft with { TextGamma = n }));
        right.Children.Add(Group("Advanced Controls", advanced));
        columns.Children.Add(left); Grid.SetColumn(right, 1); columns.Children.Add(right); fields.Children.Add(columns);
        root.Children.Add(new ScrollViewer { Content = fields, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        var actions = new StackPanel { Spacing = 12, Margin = new Thickness(14, 0, 0, 0) };
        actions.Children.Add(FormDialog.ActionButton("OK", () => { if (!Draft.IsValid) { _error.Text = Localize.Text("Invalid color settings."); return; } _accepted = true; _preview(Draft); Close(true); }));
        actions.Children.Add(FormDialog.ActionButton("Cancel", () => Close(false)));
        actions.Children.Add(FormDialog.ActionButton("Load…", async () => await Attempt(async () => { var files = await StorageProvider.OpenFilePickerAsync(new() { Title = Localize.Text("Load Color Settings"), FileTypeFilter = [new("Compositor Color Settings") { Patterns = ["*.color-settings.json"] }] }); if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) { Draft = ColorManagementSettings.Load(path); if (_showPreview) _preview(Draft); Build(); } })));
        actions.Children.Add(FormDialog.ActionButton("Save…", async () => await Attempt(async () => { var file = await StorageProvider.SaveFilePickerAsync(new() { SuggestedFileName = "settings.color-settings.json", DefaultExtension = "json" }); if (file?.TryGetLocalPath() is { } path) Draft.Save(path); })));
        actions.Children.Add(Check("Preview", _showPreview, b => { _showPreview = b; _preview(b ? Draft : _original); }));
        Grid.SetColumn(actions, 1); root.Children.Add(actions);
        var description = Group("Description", _description); Grid.SetRow(description, 1); Grid.SetColumnSpan(description, 2); description.Margin = new Thickness(0, 14, 0, 0); root.Children.Add(description);
        Grid.SetRow(_error, 2); Grid.SetColumnSpan(_error, 2); root.Children.Add(_error); _host.Content = root;
        _description.Text = Localize.Text("The independent ACE-compatible engine supports ICC matrix/TRC and v2/v4 LUT profiles. It is not Adobe's proprietary ACE engine. Profiles are converted through the sRGB editing space; unsupported profiles report an error. HDR scene conversion uses OpenColorIO.");
    }
    private void ProfileRow(StackPanel parent, string label, string model, string value, Func<string, ColorManagementSettings> changed)
    {
        var entries = IccProfiles.Catalog(Draft, model).Select(p => p.Name).ToList(); if (!entries.Contains(value)) entries.Insert(0, value);
        var custom = model == "GRAY" ? new[] { "Custom Dot Gain…", "Custom Gray Gamma…", "Load Profile…", "Save Profile…" } : new[] { "Load Profile…", "Save Profile…" };
        var choices = entries.Concat(custom).ToArray();
        var combo = Choice(choices, Array.IndexOf(choices, value), async i => await Attempt(async () =>
        {
            var selected = choices[i];
            if (i < entries.Count) { Change(changed(selected)); return; }
            if (selected == "Save Profile…") { var file = await StorageProvider.SaveFilePickerAsync(new() { SuggestedFileName = value + ".icc", DefaultExtension = "icc" }); if (file?.TryGetLocalPath() is { } path) AtomicFile.Write(path, stream => stream.Write(IccProfiles.Resolve(value, Draft))); }
            else if (selected == "Load Profile…")
            {
                var files = await StorageProvider.OpenFilePickerAsync(new() { FileTypeFilter = [new("ICC Profile") { Patterns = ["*.icc", "*.icm"] }] });
                if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) { if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("ICC profile is too large."); var bytes = await File.ReadAllBytesAsync(path); if (!IccProfiles.Valid(bytes) || IccProfiles.Model(bytes) != model) throw new InvalidDataException("The ICC profile does not match the color model."); Add(IccProfiles.Name(bytes, Path.GetFileNameWithoutExtension(path)), bytes); }
            }
            else
            {
                var dot = selected == "Custom Dot Gain…"; var dialog = new GrayProfileDialog(dot);
                if (await dialog.ShowDialog<bool>(this)) Add(dialog.ProfileName, dialog.ProfileBytes!);
            }
            Build();
            void Add(string name, byte[] bytes) { var profiles = Draft.CustomProfiles.ToDictionary(p => p.Key, p => p.Value); profiles[name] = bytes; Draft = Draft with { CustomProfiles = profiles }; Change(changed(name)); }
        })); parent.Children.Add(Row(label, combo, 65));
    }
    private async Task Attempt(Func<Task> action) { try { _error.Text = ""; await action(); } catch (Exception e) { _error.Text = Localize.Text(e.Message); } }
    private void Advanced(StackPanel parent, string label, bool enabled, double value, double min, double max, Action<bool> toggle, Action<double> change, string unit = "")
    {
        var row = new Grid { ColumnDefinitions = new("*,65,Auto") }; var number = Number(value, min, max, change); number.IsEnabled = enabled;
        row.Children.Add(Check(label, enabled, b => { number.IsEnabled = b; toggle(b); })); Grid.SetColumn(number, 1); row.Children.Add(number); var suffix = Label(unit); suffix.Margin = new Thickness(5, 0, 0, 0); Grid.SetColumn(suffix, 2); row.Children.Add(suffix); parent.Children.Add(row);
    }
    internal static TextBlock Label(string text) => new() { Text = Localize.Text(text), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    internal static Control Group(string title, Control content)
    {
        var box = new StackPanel { Spacing = 8, Margin = new Thickness(12) }; box.Children.Add(Label(title)); box.Children.Add(content); return new Border { Child = box, BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1) };
    }
    internal static Grid Row(string label, Control control, double width = 120)
    {
        var row = new Grid { ColumnDefinitions = new($"{width},*") }; row.Children.Add(Label(label)); control.Tag = label; control.HorizontalAlignment = HorizontalAlignment.Stretch; Grid.SetColumn(control, 1); row.Children.Add(control); return row;
    }
    internal static ComboBox Choice(IEnumerable<string> choices, int selected, Action<int> change)
    {
        var combo = new ComboBox { ItemsSource = choices.Select(Localize.Text).ToArray(), SelectedIndex = Math.Max(0, selected), HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 30 };
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) change(combo.SelectedIndex); }; return combo;
    }
    internal static CheckBox Check(string text, bool value, Action<bool> change)
    {
        var check = new CheckBox { Content = Label(text), IsChecked = value, Tag = text, Margin = new Thickness(0, 0, 10, 0) }; check.IsCheckedChanged += (_, _) => change(check.IsChecked == true); return check;
    }
    internal static NumericUpDown Number(double value, double min, double max, Action<double> change)
    {
        var number = new NumericUpDown { Value = (decimal)value, Minimum = (decimal)min, Maximum = (decimal)max, ShowButtonSpinner = false, FormatString = "0.##", MinHeight = 30 }; number.ValueChanged += (_, _) => { if (number.Value is { } v) change((double)v); }; return number;
    }
}

internal sealed class GrayProfileDialog : DialogWindow
{
    internal string ProfileName { get; private set; } = "";
    internal byte[]? ProfileBytes { get; private set; }
    internal GrayProfileDialog(bool dotGain)
    {
        Title = Localize.Text(dotGain ? "Custom Dot Gain" : "Custom Gray Gamma"); Width = dotGain ? 720 : 480; Height = dotGain ? 420 : 200; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var values = new double[] { 2, 4, 6, 8, 10, 20, 30, 40, 50, 60, 70, 80, 90 }; var gamma = 2.2;
        var root = new Grid { ColumnDefinitions = new("*,100"), Margin = new Thickness(16) }; var fields = new StackPanel { Spacing = 12 }; var name = new TextBox { Text = Localize.Text(dotGain ? "Custom Dot Gain" : "Custom Gray Gamma") }; fields.Children.Add(ColorSettingsDialog.Row("Name:", name, 70));
        if (dotGain)
        {
            var grid = new Grid { ColumnDefinitions = new("250,*,*"), RowDefinitions = new("Auto,Auto,Auto,Auto,Auto,Auto,Auto") }; var curve = new DotGainCurve(values) { Width = 240, Height = 240, Margin = new Thickness(0, 5, 10, 0) }; grid.Children.Add(curve); Grid.SetRowSpan(curve, 7);
            for (var i = 0; i < values.Length; i++) { var index = i; var row = ColorSettingsDialog.Row(values[i] + "%:", ColorSettingsDialog.Number(values[i], 0, 100, n => { values[index] = n; curve.InvalidateVisual(); }), 42); row.Margin = new Thickness(4); Grid.SetColumn(row, i < 7 ? 1 : 2); Grid.SetRow(row, i < 7 ? i : i - 7); grid.Children.Add(row); } fields.Children.Add(grid);
        }
        else fields.Children.Add(ColorSettingsDialog.Row("Gamma:", ColorSettingsDialog.Number(gamma, .1, 10, n => gamma = n)));
        var error = ColorSettingsDialog.Label(""); fields.Children.Add(error); root.Children.Add(fields); var actions = new StackPanel { Spacing = 12, Margin = new Thickness(12, 0, 0, 0) };
        actions.Children.Add(FormDialog.ActionButton("OK", () => { if (string.IsNullOrWhiteSpace(name.Text) || name.Text.Length > 200 || dotGain && values.Zip(values.Skip(1)).Any(p => p.First > p.Second)) { error.Text = Localize.Text("Enter a name and an increasing dot-gain curve."); return; } ProfileName = name.Text; ProfileBytes = IccProfiles.Gray(ProfileName, dotGain ? 0 : gamma, custom: dotGain ? values : null); Close(true); })); actions.Children.Add(FormDialog.ActionButton("Cancel", () => Close(false))); Grid.SetColumn(actions, 1); root.Children.Add(actions); Content = root;
    }
    private sealed class DotGainCurve(double[] values) : Control
    {
        public override void Render(DrawingContext c)
        {
            c.FillRectangle(Skin.SurfaceDarkBrush, new Rect(Bounds.Size)); var thin = new Pen(Skin.BorderControlBrush, 1); for (var i = 0; i <= 10; i++) { c.DrawLine(thin, new(i * Bounds.Width / 10, 0), new(i * Bounds.Width / 10, Bounds.Height)); c.DrawLine(thin, new(0, i * Bounds.Height / 10), new(Bounds.Width, i * Bounds.Height / 10)); }
            var knots = new double[] { 0, 2, 4, 6, 8, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 }; var y = new[] { 0d }.Concat(values).Append(100).ToArray(); var pen = new Pen(Skin.LabelBrush, 1.5); for (var i = 1; i < knots.Length; i++) c.DrawLine(pen, new(knots[i - 1] / 100 * Bounds.Width, (1 - y[i - 1] / 100) * Bounds.Height), new(knots[i] / 100 * Bounds.Width, (1 - y[i] / 100) * Bounds.Height));
        }
    }
}
