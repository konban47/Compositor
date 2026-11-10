using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.IO;

namespace Compositor.Desktop;

/// <summary>The Name field every custom profile dialog opens with, its text selected.</summary>
internal static class ProfileFields
{
    internal static TextBox Field(SideDialog dialog, StackPanel body, string name, double width = 520)
    {
        var field = DialogKit.Line("profile-name"); field.Text = name; field.Width = width;
        var label = dialog.Mnemonic(new Label { Content = Localize.Text("Name") + ":", VerticalAlignment = VerticalAlignment.Center, Foreground = Skin.SecondaryBrush }, 'N');
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { label, field } });
        dialog.Opened += (_, _) => { field.Focus(); field.SelectAll(); };
        return field;
    }
    internal static string Format(double value, string format = "0.0000") => value.ToString(format, CultureInfo.InvariantCulture);
    internal static double? Read(TextBox box) => double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;
}

/// <summary>Color Settings ▸ Gray ▸ Custom Dot Gain… and Custom Gray Gamma…: a gray profile from a dot gain curve or a gamma.</summary>
internal sealed class GrayProfileDialog : SideDialog
{
    internal string ProfileName { get; private set; } = "";
    internal byte[]? ProfileBytes { get; private set; }
    private readonly bool _dotGain;
    private readonly TextBox _name;
    private readonly TextBox[] _points = new TextBox[13];
    private readonly NumericUpDown _gamma = Number(2.2, .75, 3, 72);
    private readonly TextBlock _problem = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
    internal GrayProfileDialog(bool dotGain) : base(dotGain ? "Custom Dot Gain" : "Custom Gray Gamma", dotGain ? 660 : 560)
    {
        _dotGain = dotGain;
        _name = ProfileFields.Field(this, Body, Localize.Text(dotGain ? "Custom Dot Gain" : "Custom Gray Gamma"), dotGain ? 400 : 330);
        if (dotGain)
        {
            var curve = new GainCurve(this) { Width = 220, Height = 220 };
            var ramp = new Border { Height = 12, Width = 220, Margin = new Thickness(0, 6, 0, 0), Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative), GradientStops = { new GradientStop(Colors.White, 0), new GradientStop(Colors.Black, 1) } } };
            var fields = new Grid { ColumnDefinitions = new("Auto,Auto,Auto,Auto,Auto,Auto"), ColumnSpacing = 6, RowSpacing = 6 };
            for (var i = 0; i < 7; i++) fields.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var i = 0; i < 13; i++)
            {
                var box = DialogKit.Line("gain-" + CustomColorProfiles.GainKnots[i]); box.Width = 64; box.Text = i == 8 ? "70" : "";
                box.TextChanged += (_, _) => curve.InvalidateVisual(); _points[i] = box;
                var column = i < 7 ? 0 : 3; var row = i < 7 ? i : i - 7;
                var caption = new TextBlock { Text = CustomColorProfiles.GainKnots[i] + ":", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(column == 3 ? 14 : 0, 0, 0, 0) };
                var unit = new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center };
                foreach (var (control, offset) in new (Control, int)[] { (caption, 0), (box, 1), (unit, 2) }) { Grid.SetColumn(control, column + offset); Grid.SetRow(control, row); fields.Children.Add(control); }
            }
            var row2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Children = { new StackPanel { Children = { curve, ramp } }, fields } };
            Body.Children.Add(DialogKit.Group("Dot Gain Curves", row2));
        }
        else Body.Children.Add(DialogKit.Group("Gamma", Line(this.Mnemonic(new Label { Content = Localize.Text("Gamma") + ":", Foreground = Skin.SecondaryBrush }, 'G'), _gamma)));
        Body.Children.Add(_problem);
    }
    /// <summary>The thirteen points with the blanks filled in from the ones given, or null when they do not climb.</summary>
    internal double[]? Values()
    {
        var curve = _points.Select(p => ProfileFields.Read(p) is { } v and >= 0 and <= 100 ? v : double.NaN).ToArray();
        var given = curve.Where(v => !double.IsNaN(v)).ToArray();
        if (given.Zip(given.Skip(1)).Any(p => p.First > p.Second)) return null;
        var settings = new CustomCmykSettings { GainCurve = curve };
        return CustomColorProfiles.GainKnots.Select(k => CustomColorProfiles.Gain(settings, k / 100) * 100).ToArray();
    }
    protected override bool Validate()
    {
        var name = _name.Text?.Trim() ?? "";
        var values = _dotGain ? Values() : null;
        if (name.Length is 0 or > 200 || _dotGain && values is null) { _problem.Text = Localize.Text("Enter a name and an increasing dot-gain curve."); return false; }
        ProfileName = name; ProfileBytes = IccProfiles.Gray(name, _dotGain ? 0 : (double)(_gamma.Value ?? 2.2m), custom: values); return true;
    }
    /// <summary>The curve, drawn through the points given; pressing on it sets the nearest point to where it was pressed.</summary>
    private sealed class GainCurve(GrayProfileDialog owner) : Control
    {
        public override void Render(DrawingContext c)
        {
            c.FillRectangle(Skin.SurfaceDarkBrush, new Rect(Bounds.Size)); var thin = new Pen(Skin.BorderControlBrush, 1);
            for (var i = 0; i <= 10; i++) { c.DrawLine(thin, new(i * Bounds.Width / 10, 0), new(i * Bounds.Width / 10, Bounds.Height)); c.DrawLine(thin, new(0, i * Bounds.Height / 10), new(Bounds.Width, i * Bounds.Height / 10)); }
            var curve = owner._points.Select(p => ProfileFields.Read(p) is { } v and >= 0 and <= 100 ? v : double.NaN).ToArray();
            var settings = new CustomCmykSettings { GainCurve = curve }; var pen = new Pen(Skin.LabelBrush, 1.5); Point? last = null;
            for (var i = 0; i <= 100; i++) { var p = new Point(i / 100d * Bounds.Width, (1 - CustomColorProfiles.Gain(settings, i / 100d)) * Bounds.Height); if (last is { } l) c.DrawLine(pen, l, p); last = p; }
            for (var i = 0; i < 13; i++) if (!double.IsNaN(curve[i])) c.DrawRectangle(null, pen, new Rect(CustomColorProfiles.GainKnots[i] / 100 * Bounds.Width - 3, (1 - curve[i] / 100) * Bounds.Height - 3, 6, 6));
        }
        protected override void OnPointerPressed(PointerPressedEventArgs e) { Set(e.GetPosition(this)); e.Pointer.Capture(this); }
        protected override void OnPointerMoved(PointerEventArgs e) { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) Set(e.GetPosition(this)); }
        private void Set(Point at)
        {
            var x = Math.Clamp(at.X / Bounds.Width * 100, 0, 100); var nearest = Array.IndexOf(CustomColorProfiles.GainKnots, CustomColorProfiles.GainKnots.MinBy(k => Math.Abs(k - x)));
            owner._points[nearest].Text = Math.Round(Math.Clamp((1 - at.Y / Bounds.Height) * 100, 0, 100)).ToString(CultureInfo.InvariantCulture);
        }
    }
}

/// <summary>Color Settings ▸ RGB ▸ Custom RGB…: a display profile from a gamma, a white point and primaries.</summary>
internal sealed class CustomRgbDialog : SideDialog
{
    internal string ProfileName { get; private set; } = "";
    internal byte[]? ProfileBytes { get; private set; }
    private readonly TextBox _name;
    private readonly NumericUpDown _gamma = Number(2.2, .75, 3, 72);
    private readonly TextBox[] _white = [DialogKit.Line("white-x"), DialogKit.Line("white-y")];
    private readonly TextBox[] _primaries = Enumerable.Range(0, 6).Select(i => DialogKit.Line("primary-" + i)).ToArray();
    private readonly ComboBox _whiteChoice, _primaryChoice;
    private readonly TextBlock _problem = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
    private bool _setting;
    internal CustomRgbDialog() : base("Custom RGB", 600)
    {
        _name = ProfileFields.Field(this, Body, Localize.Text("Custom RGB"), 400);
        _gamma.FormatString = "0.00";
        Body.Children.Add(DialogKit.Group("Gamma", Line(this.Mnemonic(new Label { Content = Localize.Text("Gamma") + ":", Foreground = Skin.SecondaryBrush }, 'G'), _gamma)));
        _whiteChoice = Grouped([[("custom", "Custom")], CustomColorProfiles.WhitePoints.Select(w => (w.Name, w.Name))], "6500° K (D65)", 190);
        _primaryChoice = Grouped([[("custom", "Custom")], CustomColorProfiles.Primaries.Select(p => (p.Name, p.Name))], "HDTV (ITU-R 709-2)", 220);
        foreach (var box in _white.Concat(_primaries)) { box.Width = 80; box.TextChanged += (_, _) => Edited(); }
        _whiteChoice.SelectionChanged += (_, _) => Fill(); _primaryChoice.SelectionChanged += (_, _) => Fill(); Fill();
        var heads = new[] { new TextBlock(), new TextBlock { Text = "x", HorizontalAlignment = HorizontalAlignment.Center }, new TextBlock { Text = "y", HorizontalAlignment = HorizontalAlignment.Center } };
        var white = DialogKit.Columns("90,80,80", 6, [heads[0], heads[1], heads[2], Mn("White", 'H'), _white[0], _white[1]]);
        Body.Children.Add(DialogKit.Group("", white, Heading("White Point", _whiteChoice)));
        var colors = DialogKit.Columns("90,80,80", 6, [new TextBlock(), new TextBlock { Text = "x", HorizontalAlignment = HorizontalAlignment.Center }, new TextBlock { Text = "y", HorizontalAlignment = HorizontalAlignment.Center },
            Mn("Red", 'R'), _primaries[0], _primaries[1], Mn("Green", 'E'), _primaries[2], _primaries[3], Mn("Blue", 'B'), _primaries[4], _primaries[5]]);
        Body.Children.Add(DialogKit.Group("", colors, Heading("Primaries", _primaryChoice)));
        Body.Children.Add(_problem);
    }
    private Label Mn(string text, char key) => this.Mnemonic(new Label { Content = Localize.Text(text) + ":", HorizontalAlignment = HorizontalAlignment.Right, Foreground = Skin.SecondaryBrush }, key);
    private static Control Heading(string text, ComboBox choice) => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = Localize.Text(text) + ":", VerticalAlignment = VerticalAlignment.Center }, choice } };
    /// <summary>Puts the chosen preset's numbers in the fields.</summary>
    private void Fill()
    {
        _setting = true;
        if (Key(_whiteChoice) is { } w && CustomColorProfiles.WhitePoints.FirstOrDefault(p => p.Name == w) is { Name: not null } white) { _white[0].Text = ProfileFields.Format(white.X); _white[1].Text = ProfileFields.Format(white.Y); }
        if (Key(_primaryChoice) is { } p && CustomColorProfiles.Primaries.FirstOrDefault(q => q.Name == p) is { Name: not null } primaries) for (var i = 0; i < 6; i++) _primaries[i].Text = ProfileFields.Format(primaries.XY[i]);
        _setting = false;
    }
    /// <summary>A number typed by hand makes its preset Custom, as the fields no longer say what it says.</summary>
    private void Edited()
    {
        if (_setting) return;
        bool Same(TextBox box, double value) => ProfileFields.Read(box) is { } v && Math.Abs(v - value) < 5e-5;
        if (Key(_whiteChoice) is { } w && CustomColorProfiles.WhitePoints.FirstOrDefault(p => p.Name == w) is { Name: not null } white && !(Same(_white[0], white.X) && Same(_white[1], white.Y))) Select(_whiteChoice, "custom");
        if (Key(_primaryChoice) is { } p && CustomColorProfiles.Primaries.FirstOrDefault(q => q.Name == p) is { Name: not null } primaries && !Enumerable.Range(0, 6).All(i => Same(_primaries[i], primaries.XY[i]))) Select(_primaryChoice, "custom");
    }
    private void Select(ComboBox box, string key) { _setting = true; box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, key)); _setting = false; }
    protected override bool Validate()
    {
        try
        {
            var numbers = _white.Concat(_primaries).Select(ProfileFields.Read).ToArray();
            if (numbers.Any(n => n is null) || string.IsNullOrWhiteSpace(_name.Text)) throw new ArgumentException("Enter a name and every coordinate.");
            ProfileName = _name.Text!.Trim(); ProfileBytes = CustomColorProfiles.Rgb(ProfileName, (double)(_gamma.Value ?? 2.2m), numbers[0]!.Value, numbers[1]!.Value, numbers.Skip(2).Select(n => n!.Value).ToArray());
            return true;
        }
        catch (ArgumentException error) { _problem.Text = Localize.Text(error.Message); return false; }
    }
}

/// <summary>Color Settings ▸ CMYK ▸ Custom CMYK…: ink options and separation options, with the gray ramp they produce.</summary>
internal sealed class CustomCmykDialog : SideDialog
{
    internal string ProfileName { get; private set; } = "";
    internal byte[]? ProfileBytes { get; private set; }
    internal CustomCmykSettings Settings { get; private set; } = new();
    private readonly TextBox _name;
    private readonly ComboBox _inks, _gainKind, _black;
    private readonly NumericUpDown _gain = Number(20, -10, 40, 64), _blackLimit = Number(100, 0, 100, 64), _totalLimit = Number(300, 200, 400, 64), _uca = Number(0, 0, 100, 64);
    private readonly RadioButton _gcr, _ucr;
    private readonly RampGraph _ramp;
    private readonly TextBlock _problem = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
    private bool _named = true, _setting;
    internal CustomCmykDialog() : base("Custom CMYK", 760)
    {
        _name = ProfileFields.Field(this, Body, NameFor(Settings));
        _name.TextChanged += (_, _) => { if (!_setting) _named = false; };
        _inks = Grouped([[("custom", "Custom…")], CustomColorProfiles.InkSets.Select(i => (i.Name, i.Name))], Settings.Inks.Name, 420);
        _gainKind = Grouped([[("standard", "Standard"), ("curves", "Curves…")]], "standard", 300);
        foreach (var box in new[] { _gain, _blackLimit, _totalLimit, _uca }) box.FormatString = "0";
        var gainRow = Line(_gainKind, _gain, new TextBlock { Text = "%" });
        Body.Children.Add(DialogKit.Group("Ink Options", DialogKit.Columns("100,*", 8, Caption("Ink Colors", 100), _inks, Caption("Dot Gain", 100), gainRow)));
        _gcr = this.Mnemonic(new RadioButton { Content = "GCR", IsChecked = true, GroupName = "separation" }, 'G'); _ucr = this.Mnemonic(new RadioButton { Content = "UCR", GroupName = "separation" }, 'U');
        _black = Grouped([[("custom", "Custom…")], Enum.GetValues<BlackGeneration>().Where(b => b != BlackGeneration.Custom).Select(b => (b.ToString(), BlackName(b)))], "Medium", 190);
        var left = DialogKit.Columns("170,*", 8, Caption("Separation Type", 170), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, Children = { _gcr, _ucr } },
            Caption("Black Generation", 170), _black,
            Mn("Black Ink Limit", 'C'), Line(_blackLimit, new TextBlock { Text = "%" }), Mn("Total Ink Limit", 'T'), Line(_totalLimit, new TextBlock { Text = "%" }), Mn("UCA Amount", 'A'), Line(_uca, new TextBlock { Text = "%" }));
        _ramp = new RampGraph { Width = 156, Height = 140 };
        var ramp = new StackPanel { Spacing = 6, Children = { new TextBlock { Text = Localize.Text("Gray Ramp") + ":", HorizontalAlignment = HorizontalAlignment.Center }, _ramp, new Border { Height = 12, Width = 156, Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative), GradientStops = { new GradientStop(Colors.White, 0), new GradientStop(Colors.Black, 1) } } } } };
        var separation = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 18, Children = { left, ramp } }; Grid.SetColumn(ramp, 1);
        Body.Children.Add(DialogKit.Group("Separation Options", separation));
        Body.Children.Add(_problem);
        _inks.SelectionChanged += async (_, _) => { if (_setting) return; if (Key(_inks) == "custom") await CustomInks(); else Changed(); };
        _gainKind.SelectionChanged += async (_, _) => { if (_setting) return; if (Key(_gainKind) == "curves") await GainCurves(); else { Settings = Settings with { GainCurve = null }; Changed(); } };
        _black.SelectionChanged += async (_, _) => { if (_setting) return; if (Key(_black) == "custom") await CustomBlack(); else Changed(); };
        foreach (var box in new[] { _gain, _blackLimit, _totalLimit, _uca }) box.ValueChanged += (_, _) => { if (!_setting) Changed(); };
        _gcr.IsCheckedChanged += (_, _) => { if (!_setting) Changed(); };
        Changed();
    }
    private Label Mn(string text, char key) => this.Mnemonic(new Label { Content = Localize.Text(text) + ":", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = Skin.SecondaryBrush }, key);
    /// <summary>Reads the controls into the settings, renames them while the name is still the made-up one, and redraws the ramp.</summary>
    private void Changed()
    {
        var inks = Key(_inks) is { } name && CustomColorProfiles.InkSets.FirstOrDefault(i => i.Name == name) is { } set ? set : Settings.Inks;
        var black = Enum.TryParse<BlackGeneration>(Key(_black), out var b) ? b : BlackGeneration.Custom;
        var ucr = _ucr.IsChecked == true;
        Settings = Settings with
        {
            Inks = inks, DotGain = (double)(_gain.Value ?? 20), Separation = ucr ? SeparationType.Ucr : SeparationType.Gcr, Black = black,
            BlackLimit = (double)(_blackLimit.Value ?? 100), TotalLimit = (double)(_totalLimit.Value ?? 300), UndercolorAddition = ucr ? 0 : (double)(_uca.Value ?? 0),
        };
        _black.IsEnabled = _uca.IsEnabled = !ucr; _gain.IsEnabled = Settings.GainCurve is null;
        if (_named) { _setting = true; _name.Text = NameFor(Settings); _setting = false; }
        _ramp.Ramp = Settings.IsValid ? CustomColorProfiles.GrayRamp(Settings) : []; _ramp.InvalidateVisual();
    }
    private static string NameFor(CustomCmykSettings s) => $"{s.Inks.Name}, {(s.GainCurve is null ? s.DotGain.ToString("0", CultureInfo.InvariantCulture) + "%" : Localize.Text("Curves"))}, {(s.Separation == SeparationType.Gcr ? "GCR" : "UCR")}, {BlackName(s.Black)}";
    // Black generation's amounts, which in Chinese are not the words "Light" and "Heavy" mean elsewhere in the app.
    private static string BlackName(BlackGeneration black) => Localize.IsChinese ? black switch { BlackGeneration.None => "无", BlackGeneration.Light => "较少", BlackGeneration.Medium => "中", BlackGeneration.Heavy => "较多", BlackGeneration.Maximum => "最大值", _ => "自定" } : black.ToString();
    private void Restore(ComboBox box, string key) { _setting = true; box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, key)); _setting = false; }
    private async Task CustomInks()
    {
        var dialog = new InkColorsDialog(Settings.Inks);
        if (await dialog.ShowDialog<bool>(this)) { Settings = Settings with { Inks = dialog.Inks }; _setting = true; var item = new ComboBoxItem { Content = dialog.Inks.Name, Tag = dialog.Inks.Name }; _inks.Items.Add(item); _inks.SelectedItem = item; _setting = false; Changed(); }
        else Restore(_inks, Settings.Inks.Name);
    }
    private async Task GainCurves()
    {
        var dialog = new GrayProfileDialog(true);
        if (await dialog.ShowDialog<bool>(this) && dialog.Values() is { } values) { Settings = Settings with { GainCurve = values }; Changed(); }
        else Restore(_gainKind, Settings.GainCurve is null ? "standard" : "curves");
    }
    private async Task CustomBlack()
    {
        var dialog = new BlackGenerationDialog(Settings.BlackStart, Settings.BlackAmount);
        if (await dialog.ShowDialog<bool>(this)) { Settings = Settings with { BlackStart = dialog.Start, BlackAmount = dialog.Amount }; Changed(); }
        else Restore(_black, Settings.Black.ToString());
    }
    protected override bool Validate()
    {
        var name = _name.Text?.Trim() ?? "";
        Settings = Settings with { Name = name };
        if (!Settings.IsValid) { _problem.Text = Localize.Text("Enter a name and settings within their limits."); return false; }
        ProfileName = name; ProfileBytes = CustomColorProfiles.Cmyk(Settings); return true;
    }
    /// <summary>The cyan, magenta, yellow and black a neutral ramp from white to black prints with.</summary>
    private sealed class RampGraph : Control
    {
        internal double[][] Ramp { get; set; } = [];
        public override void Render(DrawingContext c)
        {
            c.FillRectangle(Skin.SurfaceDarkBrush, new Rect(Bounds.Size)); var thin = new Pen(Skin.BorderControlBrush, 1);
            for (var i = 0; i <= 4; i++) { c.DrawLine(thin, new(i * Bounds.Width / 4, 0), new(i * Bounds.Width / 4, Bounds.Height)); c.DrawLine(thin, new(0, i * Bounds.Height / 4), new(Bounds.Width, i * Bounds.Height / 4)); }
            var inks = new[] { Color.FromRgb(0x1C, 0xA6, 0xDC), Color.FromRgb(0xF2, 0x3C, 0x6E), Color.FromRgb(0xF0, 0xC8, 0x14), Colors.Black };
            for (var ink = 0; ink < 4 && Ramp.Length > 1; ink++)
            {
                var pen = new Pen(new SolidColorBrush(inks[ink]), 1.4);
                for (var i = 1; i < Ramp.Length; i++) c.DrawLine(pen, Point(i - 1, ink), Point(i, ink));
            }
            Point Point(int i, int ink) => new(i / (double)(Ramp.Length - 1) * Bounds.Width, (1 - Ramp[i][ink]) * Bounds.Height);
        }
    }
}

/// <summary>Custom CMYK ▸ Ink Colors ▸ Custom…: the Lab of each ink, overprint, paper and black.</summary>
internal sealed class InkColorsDialog : SideDialog
{
    internal InkSet Inks { get; private set; }
    private readonly TextBox[] _values = new TextBox[27];
    private readonly TextBlock _problem = new() { Foreground = Brushes.OrangeRed };
    internal InkColorsDialog(InkSet start) : base("Ink Colors", 520)
    {
        Inks = start;
        string[] names = ["Cyan", "Magenta", "Yellow", "MY", "CY", "CM", "CMY", "White", "Black"];
        var cells = new List<Control> { new TextBlock(), new TextBlock { Text = "L*", HorizontalAlignment = HorizontalAlignment.Center }, new TextBlock { Text = "a*", HorizontalAlignment = HorizontalAlignment.Center }, new TextBlock { Text = "b*", HorizontalAlignment = HorizontalAlignment.Center } };
        for (var i = 0; i < 9; i++)
        {
            cells.Add(Caption(names[i], 90));
            for (var j = 0; j < 3; j++) { var box = DialogKit.Line(); box.Width = 70; box.Text = ProfileFields.Format(start.Lab[i * 3 + j], "0.##"); _values[i * 3 + j] = box; cells.Add(box); }
        }
        Body.Children.Add(DialogKit.Group("", DialogKit.Columns("90,70,70,70", 6, cells.ToArray()), new TextBlock { Text = Localize.Text("Ink Colors") }));
        Body.Children.Add(_problem);
    }
    protected override bool Validate()
    {
        var values = _values.Select(ProfileFields.Read).ToArray();
        var set = new InkSet(Localize.Text("Custom Ink Colors"), values.Select(v => v ?? double.NaN).ToArray());
        if (!set.IsValid) { _problem.Text = Localize.Text("Enter L* from 0 to 100 and a*, b* from -128 to 128."); return false; }
        Inks = set; return true;
    }
}

/// <summary>Custom CMYK ▸ Black Generation ▸ Custom…: where black starts and how much of the gray it replaces.</summary>
internal sealed class BlackGenerationDialog : SideDialog
{
    private readonly NumericUpDown _start, _amount;
    internal double Start => (double)(_start.Value ?? 30) / 100;
    internal double Amount => (double)(_amount.Value ?? 100) / 100;
    internal BlackGenerationDialog(double start, double amount) : base("Custom Black Generation", 460)
    {
        _start = Number(start * 100, 0, 99, 64); _amount = Number(amount * 100, 0, 100, 64); _start.FormatString = _amount.FormatString = "0";
        Body.Children.Add(DialogKit.Group("Black Generation", DialogKit.Columns("170,*", 8, Caption("Black Start", 170), Line(_start, new TextBlock { Text = "%" }), Caption("Black Amount", 170), Line(_amount, new TextBlock { Text = "%" }))));
        Body.Children.Add(new TextBlock { Text = Localize.Text("Black replaces the gray component from the start point on, reaching the amount in the darkest neutrals."), TextWrapping = TextWrapping.Wrap, Foreground = Skin.SecondaryBrush, MaxWidth = 300 });
    }
}
