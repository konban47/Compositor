using Avalonia;
using Avalonia.Controls;

using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Compositor.Core.IO;

namespace Compositor.Desktop;

/// <summary>
/// Edit ▸ Color Settings…, laid out as Photoshop's: the settings preset over working spaces and policies on the left,
/// conversion options and advanced controls on the right, OK and the file buttons down the side, and a description that
/// follows the pointer. The working-space menus carry Custom RGB, Custom CMYK, Custom Dot Gain and Custom Gray Gamma, and
/// load and save their profiles.
/// </summary>
internal sealed class ColorSettingsDialog : DialogWindow
{
    internal ColorManagementSettings Draft { get; private set; }
    private readonly Action<ColorManagementSettings> _preview;
    private readonly ColorManagementSettings _original;
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap, Foreground = Skin.LabelBrush };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.OrangeRed };
    private readonly ContentControl _host = new();
    private bool _showPreview = true, _accepted;
    /// <summary>What the description says when the pointer is over nothing that explains itself.</summary>
    private string _resting = "";
    internal ColorSettingsDialog(ColorManagementSettings value, Action<ColorManagementSettings> preview)
    {
        Draft = value.Copy(); _original = value.Copy(); _preview = preview;
        Title = Localize.Text("Color Settings"); Width = 1080; SizeToContent = SizeToContent.Height; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Closed += (_, _) => { if (!_accepted) _preview(_original); };
        Content = _host; Build();
    }
    private void Change(ColorManagementSettings value) { Draft = value with { Preset = "Custom" }; if (_showPreview && Draft.IsValid) _preview(Draft); }

    private void Build()
    {
        // The description and the error line outlive each rebuild, so they leave the old layout first.
        if (_description.Parent is Border holder) holder.Child = null;
        if (_error.Parent is Panel panel) panel.Children.Remove(_error);
        var fields = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 14, RowDefinitions = new("Auto,Auto") };
        var left = new StackPanel { Spacing = 2 }; var right = new StackPanel { Spacing = 2 };

        var working = new StackPanel { Spacing = 6 };
        ProfileRow(working, "RGB", "RGB", Draft.Rgb, s => Draft with { Rgb = s });
        ProfileRow(working, "CMYK", "CMYK", Draft.Cmyk, s => Draft with { Cmyk = s });
        ProfileRow(working, "Gray", "GRAY", Draft.Gray, s => Draft with { Gray = s });
        ProfileRow(working, "Spot", "SPOT", Draft.Spot, s => Draft with { Spot = s });
        left.Children.Add(Describe(DialogKit.Group("Working Spaces", working), "Working Spaces: the profile each color model of a new document is given, and that untagged documents are assumed to be in."));

        var policies = new StackPanel { Spacing = 6 };
        foreach (var (label, word, policy) in new[] { ("RGB", "RGB", Draft.RgbPolicy), ("CMYK", "CMYK", Draft.CmykPolicy), ("Gray", "Gray", Draft.GrayPolicy) })
        {
            var mode = label; string[] names = ["Off", "Preserve Embedded Profiles", "Convert to Working " + word];
            var combo = Rows(names.Select((n, i) => Pick.Item(i.ToString(), n, PolicyText(i))), ((int)policy).ToString(), key =>
                Change(mode == "RGB" ? Draft with { RgbPolicy = (ColorPolicy)int.Parse(key) } : mode == "CMYK" ? Draft with { CmykPolicy = (ColorPolicy)int.Parse(key) } : Draft with { GrayPolicy = (ColorPolicy)int.Parse(key) }), 240);
            combo.Tag = label + " Policy";
            policies.Children.Add(Line(label, Describe(combo, "Color Management Policies: how documents in this model are treated when they are opened, and when color moves between documents."), 150));
        }
        var askOpen = Check("Ask When Opening", Draft.AskMismatchOpen, b => Change(Draft with { AskMismatchOpen = b }), 'K', "Profile Mismatches");
        var askPaste = Check("Ask When Pasting", Draft.AskMismatchPaste, b => Change(Draft with { AskMismatchPaste = b }), 'W', "Ask When Pasting");
        var askMissing = Check("Ask When Opening", Draft.AskMissingOpen, b => Change(Draft with { AskMissingOpen = b }), 'H', "Missing Profiles");
        Describe(askOpen, "Ask When Opening: when a document's embedded profile differs from the working space, ask which to use instead of following the policy silently.");
        Describe(askPaste, "Ask When Pasting: when pasted or dropped color comes from a different profile, ask whether to convert it or keep its numbers.");
        Describe(askMissing, "Missing Profiles: when a document has no embedded profile, ask which profile to assign.");
        policies.Children.Add(Line("Profile Mismatches", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Children = { askOpen, askPaste } }, 150));
        policies.Children.Add(Line("Missing Profiles", askMissing, 150));
        left.Children.Add(DialogKit.Group("Color Management Policies", policies));

        var conversion = new StackPanel { Spacing = 6 };
        string[] engines = ["Little CMS", "Microsoft ICM", "ACE Compatible (Independent)"];
        var engine = Rows(engines.Select((n, i) => Pick.Item(i.ToString(), n, EngineText(i))), ((int)Draft.Engine).ToString(), key => { Change(Draft with { Engine = (ColorEngine)int.Parse(key) }); Build(); }, 240);
        engine.Tag = "Engine:"; conversion.Children.Add(Line("Engine", engine, 70));
        string[] intents = ["Perceptual", "Saturation", "Relative Colorimetric", "Absolute Colorimetric"];
        var intent = Rows(intents.Select((n, i) => Pick.Item(i.ToString(), n, IntentText(i))), ((int)Draft.Intent).ToString(), key => Change(Draft with { Intent = (ColorIntent)int.Parse(key) }), 240);
        intent.Tag = "Intent:"; conversion.Children.Add(Line("Intent", intent, 70));
        var bpc = Check("Use Black Point Compensation", Draft.BlackPointCompensation, b => Change(Draft with { BlackPointCompensation = b }), 'A'); bpc.IsEnabled = Draft.Engine != ColorEngine.MicrosoftIcm;
        var dither = Check("Use Dither (8-bit/channel images)", Draft.Dither, b => Change(Draft with { Dither = b }), 'D');
        var scene = Check("Compensate for Scene-referred Profiles", Draft.CompensateSceneProfiles, b => Change(Draft with { CompensateSceneProfiles = b }), 'F');
        Describe(bpc, "Use Black Point Compensation: maps the darkest black of the source to the darkest black of the destination, so shadow detail is kept.");
        Describe(dither, "Use Dither: adds fine noise when converting 8-bit images, which hides banding in smooth gradients.");
        Describe(scene, "Compensate for Scene-referred Profiles: matches the contrast of video and HDR profiles when converting to or from a scene-referred profile.");
        foreach (var check in new[] { bpc, dither, scene }) { check.Margin = new Thickness(78, 0, 0, 0); conversion.Children.Add(check); }
        right.Children.Add(DialogKit.Group("Conversion Options", conversion));

        var advanced = new StackPanel { Spacing = 6 };
        Advanced(advanced, "Desaturate Monitor Colors By", 'E', "Desaturate Monitor Colors:", Draft.DesaturateMonitor, Draft.MonitorDesaturation, 0, 100, b => Change(Draft with { DesaturateMonitor = b }), n => Change(Draft with { MonitorDesaturation = n }), "%", "Desaturate Monitor Colors By: shows colors less saturated on screen, so that colors beyond the monitor's gamut can be told apart. What is printed or exported is unchanged.");
        Advanced(advanced, "Blend RGB Colors Using Gamma", 'U', "Blend RGB Colors Using Gamma:", Draft.BlendRgbGamma, Draft.RgbGamma, .1, 10, b => Change(Draft with { BlendRgbGamma = b }), n => Change(Draft with { RgbGamma = n }), "", "Blend RGB Colors Using Gamma: blends RGB colors at this gamma instead of in the working space's own; 1.00 blends light linearly and gives the fewest dark seams.");
        Advanced(advanced, "Blend Text Colors Using Gamma", null, "Blend Text Colors Using Gamma:", Draft.BlendTextGamma, Draft.TextGamma, .1, 10, b => Change(Draft with { BlendTextGamma = b }), n => Change(Draft with { TextGamma = n }), "", "Blend Text Colors Using Gamma: blends the edges of text layers at this gamma, which decides how heavy small type looks.");
        right.Children.Add(DialogKit.Group("Advanced Controls", advanced));
        var info = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 10, Margin = new Thickness(4, 12, 0, 0) };
        info.Children.Add(new Viewbox { Width = 30, Height = 30, Child = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M12,2 A10,10 0 1 1 11.99,2 Z M12,5 A7,7 0 1 1 11.99,5 Z M2,12 H22 M12,2 V22"), Stroke = Skin.LabelBrush, StrokeThickness = 1.5, Width = 24, Height = 24 } });
        var note = new TextBlock { Text = Localize.Text("Applied: every open document and every new one uses these settings, and they are kept for the next time Compositor starts."), TextWrapping = TextWrapping.Wrap, Foreground = Skin.SecondaryBrush, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(note, 1); info.Children.Add(note); right.Children.Add(info);

        Grid.SetRow(left, 1); Grid.SetRow(right, 1); Grid.SetColumn(right, 1); fields.Children.Add(left); fields.Children.Add(right);
        var presetRows = new List<Pick?> { Pick.Item("Custom", "Custom", "Custom: your own working spaces and policies."), null, Pick.Header("Other"), null };
        foreach (var name in ColorPresets.Names.Skip(1)) { if (name == "North America Web/Internet") presetRows.Add(null); presetRows.Add(Pick.Item(name, name, PresetText(name))); }
        var presets = Rows(presetRows, Draft.Preset, async key => await Attempt(() => { Draft = ColorPresets.Select(key, Draft); if (_showPreview) _preview(Draft); Build(); return Task.CompletedTask; }), 360);
        presets.Tag = "Settings:";
        var settings = DialogKit.Group("", new StackPanel { Children = { fields } }, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = Localize.Text("Settings") + ":", VerticalAlignment = VerticalAlignment.Center, Foreground = Skin.SecondaryBrush }, presets } });
        _resting = PresetText(Draft.Preset); _description.Text = Localize.Text(_resting);
        var description = DialogKit.Group("Description", new Border { MinHeight = 96, Child = _description });

        var body = new StackPanel { Spacing = 4, Children = { settings, description, _error } };
        var ok = FormDialog.ActionButton("OK", () => { if (!Draft.IsValid) { _error.Text = Localize.Text("Invalid color settings."); return; } _accepted = true; _preview(Draft); Close(true); }); ok.IsDefault = true;
        var cancel = FormDialog.ActionButton("Cancel", () => Close(false)); cancel.IsCancel = true;
        var load = this.Mnemonic(FormDialog.ActionButton("Load…", async () => await Attempt(async () => { var files = await StorageProvider.OpenFilePickerAsync(new() { Title = Localize.Text("Load Color Settings"), FileTypeFilter = [new("Compositor Color Settings") { Patterns = ["*.color-settings.json"] }] }); if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) { Draft = ColorManagementSettings.Load(path); if (_showPreview) _preview(Draft); Build(); } })), 'L');
        var save = this.Mnemonic(FormDialog.ActionButton("Save…", async () => await Attempt(async () => { var file = await StorageProvider.SaveFilePickerAsync(new() { SuggestedFileName = "settings.color-settings.json", DefaultExtension = "json" }); if (file?.TryGetLocalPath() is { } path) Draft.Save(path); })), 'S');
        var preview = Check("Preview", _showPreview, b => { _showPreview = b; _preview(b ? Draft : _original); }, 'V');
        Describe(load, "Load: reads color settings saved from this dialog."); Describe(save, "Save: writes these settings, custom profiles included, to a file that Load can read.");
        var actions = DialogKit.ActionColumn(ok, cancel, load, save, preview);
        var root = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(16, 6, 16, 16), Children = { body, actions } };
        Grid.SetColumn(actions, 1); actions.Margin = new Thickness(16, 18, 0, 0);
        _host.Content = root;
    }

    /// <summary>One row of a drop-down: a choice with its key, a rule, or a grey heading such as "Other".</summary>
    private sealed record Pick(string Key, string Label, string? Description, bool Enabled)
    {
        internal static Pick Item(string key, string label, string? description = null) => new(key, label, description, true);
        internal static Pick Header(string label) => new("", label, null, false);
    }
    /// <summary>A drop-down built from rows, whose highlighted row explains itself in the description.</summary>
    private ComboBox Rows(IEnumerable<Pick?> rows, string selected, Action<string> pick, double width, bool translate = true)
    {
        var box = new ComboBox { MinHeight = 0, Height = 26, HorizontalAlignment = width > 0 ? HorizontalAlignment.Left : HorizontalAlignment.Stretch }; if (width > 0) box.Width = width;
        foreach (var row in rows)
        {
            if (row is null) { box.Items.Add(new ComboBoxItem { IsEnabled = false, Height = 9, MinHeight = 0, Padding = new Thickness(0), Content = new Separator { Margin = new Thickness(0) } }); continue; }
            var item = new ComboBoxItem { Content = translate || !row.Key.StartsWith("p:", StringComparison.Ordinal) ? Localize.Text(row.Label) : row.Label, Tag = row.Enabled ? row.Key : null, IsEnabled = row.Enabled };
            if (!row.Enabled) item.Foreground = Skin.SecondaryBrush;
            if (row.Description is { } text) Describe(item, text);
            box.Items.Add(item); if (row.Enabled && row.Key == selected) box.SelectedItem = item;
        }
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is ComboBoxItem { Tag: string key } && key != selected) { selected = key; pick(key); } };
        return box;
    }
    private T Describe<T>(T control, string text) where T : Control
    {
        control.PointerEntered += (_, _) => _description.Text = Localize.Text(text);
        control.PointerExited += (_, _) => _description.Text = Localize.Text(_resting);
        return control;
    }
    private static Grid Line(string label, Control control, double width)
    {
        var row = new Grid { ColumnDefinitions = new($"{width},*"), ColumnSpacing = 8, MinHeight = 28 };
        row.Children.Add(new TextBlock { Text = Localize.Text(label) + ":", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = Skin.SecondaryBrush });
        Grid.SetColumn(control, 1); control.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(control); return row;
    }
    private CheckBox Check(string text, bool value, Action<bool> change, char key, string? tag = null)
    {
        var check = this.Mnemonic(new CheckBox { Content = Localize.Text(text), IsChecked = value, Tag = tag ?? text }, key);
        check.IsCheckedChanged += (_, _) => change(check.IsChecked == true); return check;
    }

    /// <summary>A working space: its custom, load and save rows over the profiles of its model.</summary>
    private void ProfileRow(StackPanel parent, string label, string model, string value, Func<string, ColorManagementSettings> changed)
    {
        var profileModel = model == "SPOT" ? "GRAY" : model;
        var entries = IccProfiles.Catalog(Draft, profileModel).Select(p => p.Name).ToList();
        if (model == "SPOT") entries = entries.Where(n => n.StartsWith("Dot Gain", StringComparison.Ordinal) || Draft.CustomProfiles.ContainsKey(n)).ToList();
        if (!entries.Contains(value)) entries.Insert(0, value);
        var word = model switch { "RGB" => "RGB", "CMYK" => "CMYK", "GRAY" => "Gray", _ => "Spot" };
        var rows = new List<Pick?>();
        if (model == "RGB") rows.Add(Pick.Item("custom-rgb", "Custom RGB…", "Custom RGB: builds an RGB profile from a gamma, a white point and three primaries."));
        if (model == "CMYK") rows.Add(Pick.Item("custom-cmyk", "Custom CMYK…", "Custom CMYK: builds a press profile from ink colors, dot gain and the way black is generated."));
        if (model is "GRAY" or "SPOT") rows.Add(Pick.Item("dot-gain", "Custom Dot Gain…", "Custom Dot Gain: builds a gray profile from how much each dot grows on press."));
        if (model == "GRAY") rows.Add(Pick.Item("gray-gamma", "Custom Gray Gamma…", "Custom Gray Gamma: builds a gray profile from a single gamma."));
        rows.AddRange([null, Pick.Item("load", "Load " + word + "…", "Load: adds an ICC profile from a file to this list."), Pick.Item("save", "Save " + word + "…", "Save: writes the selected profile to an ICC file."), null, Pick.Header("Other"), null]);
        var standard = entries.Where(n => model is not ("GRAY" or "SPOT") || n.StartsWith("Dot Gain", StringComparison.Ordinal) || n.StartsWith("Gray Gamma", StringComparison.Ordinal)).ToList();
        foreach (var name in standard) rows.Add(Pick.Item("p:" + name, name, name + ": " + Localize.Text(ProfileText(model))));
        var rest = entries.Except(standard).ToList();
        if (rest.Count > 0) { rows.Add(null); foreach (var name in rest) rows.Add(Pick.Item("p:" + name, name, name + ": " + Localize.Text(ProfileText(model)))); }
        var combo = Rows(rows, "p:" + value, async key => await Attempt(async () =>
        {
            if (key.StartsWith("p:", StringComparison.Ordinal)) { Change(changed(key[2..])); _resting = key[2..] + ": " + Localize.Text(ProfileText(model)); return; }
            if (key == "save")
            {
                var file = await StorageProvider.SaveFilePickerAsync(new() { SuggestedFileName = value + ".icc", DefaultExtension = "icc", FileTypeChoices = [new("ICC Profile") { Patterns = ["*.icc"] }] });
                if (file?.TryGetLocalPath() is { } path) AtomicFile.Write(path, stream => stream.Write(IccProfiles.Resolve(value, Draft)));
            }
            else if (key == "load")
            {
                var files = await StorageProvider.OpenFilePickerAsync(new() { FileTypeFilter = [new("ICC Profile") { Patterns = ["*.icc", "*.icm"] }] });
                if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
                {
                    if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("ICC profile is too large.");
                    var bytes = await File.ReadAllBytesAsync(path);
                    if (!IccProfiles.Valid(bytes) || IccProfiles.Model(bytes) != profileModel) throw new InvalidDataException("The ICC profile does not match the color model.");
                    Add(IccProfiles.Name(bytes, System.IO.Path.GetFileNameWithoutExtension(path)), bytes);
                }
            }
            else if (key == "custom-rgb") { var dialog = new CustomRgbDialog(); if (await dialog.ShowDialog<bool>(this) && dialog.ProfileBytes is { } bytes) Add(dialog.ProfileName, bytes); }
            else if (key == "custom-cmyk") { var dialog = new CustomCmykDialog(); if (await dialog.ShowDialog<bool>(this) && dialog.ProfileBytes is { } bytes) Add(dialog.ProfileName, bytes); }
            else { var dialog = new GrayProfileDialog(key == "dot-gain"); if (await dialog.ShowDialog<bool>(this) && dialog.ProfileBytes is { } bytes) Add(dialog.ProfileName, bytes); }
            Build();
            void Add(string name, byte[] bytes) { var profiles = Draft.CustomProfiles.ToDictionary(p => p.Key, p => p.Value); profiles[name] = bytes; Draft = Draft with { CustomProfiles = profiles }; Change(changed(name)); }
        }), 0, translate: false);
        combo.Tag = label + ":";
        parent.Children.Add(Line(label, Describe(combo, "Working Spaces: the profile each color model of a new document is given, and that untagged documents are assumed to be in."), 70));
    }
    private async Task Attempt(Func<Task> action)
    {
        try { _error.Text = ""; await action(); }
        catch (Exception e) { _error.Text = Localize.Text(e.Message); Build(); }
    }
    private void Advanced(StackPanel parent, string label, char? key, string tag, bool enabled, double value, double min, double max, Action<bool> toggle, Action<double> change, string unit, string description)
    {
        var row = new Grid { ColumnDefinitions = new("*,64,24"), ColumnSpacing = 6 };
        var number = Number(value, min, max, change); number.IsEnabled = enabled; number.Height = 26; number.MinHeight = 0; number.FormatString = unit == "%" ? "0" : "0.00";
        var check = new CheckBox { Content = Localize.Text(label) + ":", IsChecked = enabled, Tag = tag };
        if (key is { } letter) this.Mnemonic(check, letter);
        check.IsCheckedChanged += (_, _) => { number.IsEnabled = check.IsChecked == true; toggle(check.IsChecked == true); };
        row.Children.Add(Describe(check, description)); Grid.SetColumn(number, 1); row.Children.Add(number);
        var suffix = new TextBlock { Text = unit, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(suffix, 2); row.Children.Add(suffix); parent.Children.Add(row);
    }

    private static string PolicyText(int policy) => policy switch
    {
        0 => "Off: new documents are not color managed, and the profiles of opened documents are dropped unless they match the working space.",
        1 => "Preserve Embedded Profiles: keeps the profile a document arrives with, even when it differs from the working space. RGB and gray colors keep their look when moved between documents; CMYK colors keep their numbers.",
        _ => "Convert to Working Space: converts opened documents and incoming color into the working space, so every document shares one profile.",
    };
    private static string EngineText(int engine) => engine switch
    {
        0 => "Little CMS: the open-source color engine bundled with Compositor. Recommended for most work.",
        1 => "Microsoft ICM: the color engine built into Windows. It does not offer black point compensation.",
        _ => "ACE Compatible (Independent): Compositor's own engine for ICC matrix/TRC and v2/v4 LUT profiles. It is not Adobe's engine; unsupported profiles report an error.",
    };
    private static string IntentText(int intent) => intent switch
    {
        0 => "Perceptual: compresses the whole source gamut into the destination, keeping the relationships between colors. Suited to photographs with colors outside the destination gamut.",
        1 => "Saturation: keeps colors vivid at the expense of accuracy. Suited to charts and business graphics.",
        2 => "Relative Colorimetric: matches colors inside the destination gamut exactly, relative to the paper white, and clips the rest. The usual choice for print.",
        _ => "Absolute Colorimetric: matches colors exactly, paper white included, for proofing one press on another.",
    };
    private static string ProfileText(string model) => model switch
    {
        "RGB" => "an RGB working space.", "CMYK" => "a press condition for CMYK separations.", "SPOT" => "how spot color plates print.", _ => "a grayscale working space.",
    };
    private static string PresetText(string name) => name switch
    {
        "Custom" => "Custom: your own working spaces and policies.",
        _ when name.Contains("Web/Internet") => name + ": " + Localize.Text("settings for content published on screen and on the web; RGB is converted to sRGB."),
        _ when name.Contains("Prepress") => name + ": " + Localize.Text("settings for print production; embedded profiles are kept and mismatches are reported."),
        _ when name.Contains("General Purpose") => name + ": " + Localize.Text("general-purpose settings for screen and print; embedded profiles are kept and profile warnings are off."),
        _ when name.Contains("Newspaper") => name + ": " + Localize.Text("settings for newspaper presses."),
        _ when name.Contains("Magazine") => name + ": " + Localize.Text("settings for magazine advertising."),
        _ => name + ": " + Localize.Text("uses the monitor's own colors, for video and on-screen work."),
    };

    // The parts the OpenColorIO dialog shares with this one.
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
