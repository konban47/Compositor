using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Format;
using Compositor.Core.IO;

namespace Compositor.Desktop;

/// <summary>Edit ▸ Find and Replace Text: Find Next walks the type layers one match at a time, Change changes the one found.</summary>
internal sealed class FindReplaceDialog : DialogWindow
{
    internal readonly TextBox Find = DialogKit.Line("find-text"), Change = DialogKit.Line("change-text");
    internal readonly CheckBox AllLayers = new() { Content = Localize.Text("Search All Layers"), IsChecked = true, Tag = "find-all-layers" },
        Forward = new() { Content = Localize.Text("Forward"), IsChecked = true, Tag = "find-forward" }, CaseSensitive = new() { Content = Localize.Text("Case Sensitive"), Tag = "find-case" },
        WholeWord = new() { Content = Localize.Text("Whole Word"), Tag = "find-whole" }, IgnoreAccents = new() { Content = Localize.Text("Ignore Accents"), IsChecked = true, Tag = "find-accents" };
    private readonly Button _next, _change, _all, _changeFind;
    internal readonly TextBlock Status = new() { Foreground = Skin.SecondaryBrush, TextWrapping = TextWrapping.Wrap };

    /// <summary>What the buttons ask the editor to do: "next", "change", "all" or "change-find".</summary>
    internal FindReplaceDialog(Action<string> act)
    {
        Title = Localize.Text("Find and Replace Text"); Width = 660; SizeToContent = SizeToContent.Height; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Button Act(string title, string what, char key) { var button = FormDialog.ActionButton(title, () => act(what)); return this.Mnemonic(button, key); }
        var done = this.Mnemonic(FormDialog.ActionButton("Done", Close), 'D'); done.IsCancel = true;
        _next = Act("Find Next", "next", 'I'); _change = Act("Change", "change", 'H'); _all = Act("Change All", "all", 'A'); _changeFind = Act("Change/Find", "change-find", 'N');
        var actions = DialogKit.ActionColumn(done, new Border { Height = 4 }, _next, _change, _all, _changeFind); actions.Width = 170;
        var fields = new StackPanel { Spacing = 4 };
        fields.Children.Add(this.Mnemonic(new Label { Content = Localize.Text("Find What") + ":", Padding = new Thickness(0) }, 'F')); fields.Children.Add(Find);
        fields.Children.Add(this.Mnemonic(new Label { Content = Localize.Text("Change To") + ":", Padding = new Thickness(0), Margin = new Thickness(0, 6, 0, 0) }, 'C')); fields.Children.Add(Change);
        var checks = DialogKit.Columns("*,*", 24, this.Mnemonic(AllLayers, 'S'), this.Mnemonic(CaseSensitive, 'E'), this.Mnemonic(Forward, 'O'), this.Mnemonic(WholeWord, 'W'), new Border(), this.Mnemonic(IgnoreAccents, 'G'));
        checks.Margin = new Thickness(0, 12, 0, 0); fields.Children.Add(checks); fields.Children.Add(Status);
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(16, 12, 16, 16), Children = { fields } };
        Grid.SetColumn(actions, 1); root.Children.Add(actions); Content = root;
        Find.TextChanged += (_, _) => Update(false); Update(false);
        Opened += (_, _) => Find.Focus();
    }
    /// <summary>Find Next works once there's text to find; the Change buttons once something has been found.</summary>
    internal void Update(bool found)
    {
        var any = !string.IsNullOrEmpty(Find.Text);
        _next.IsEnabled = _all.IsEnabled = any; _change.IsEnabled = _changeFind.IsEnabled = any && found;
    }
    internal CompareOptions Options => (CaseSensitive.IsChecked == true ? CompareOptions.None : CompareOptions.IgnoreCase) | (IgnoreAccents.IsChecked == true ? CompareOptions.IgnoreNonSpace : CompareOptions.None);

    /// <summary>Where <paramref name="find"/> next occurs in <paramref name="text"/> from <paramref name="start"/>, honoring Whole Word.</summary>
    internal static (int Index, int Length)? Next(string text, string find, int start, bool forward, CompareOptions options, bool whole)
    {
        var info = CultureInfo.InvariantCulture.CompareInfo;
        while (forward ? start <= text.Length : start >= 0)
        {
            int at, length;
            if (forward) at = info.IndexOf(text.AsSpan(start), find.AsSpan(), options, out length) is var found and >= 0 ? found + start : -1;
            else { at = info.LastIndexOf(text.AsSpan(0, Math.Min(text.Length, start)), find.AsSpan(), options, out length); }
            if (at < 0) return null;
            bool Letter(int i) => i >= 0 && i < text.Length && char.IsLetterOrDigit(text[i]);
            if (!whole || !Letter(at - 1) && !Letter(at + length)) return (at, length);
            start = forward ? at + 1 : at - 1;
        }
        return null;
    }
}

/// <summary>Type ▸ Manage Missing Fonts: a replacement chosen for each font the document asks for and Windows doesn't have.</summary>
internal sealed class MissingFontsDialog : DialogWindow
{
    internal readonly Dictionary<string, ComboBox> Replacements = [];
    internal readonly CheckBox All = new() { Content = Localize.Text("Replace All Missing Fonts with Default Font"), Tag = "missing-all" };
    internal MissingFontsDialog(IReadOnlyList<string> missing, IReadOnlyList<string> used)
    {
        Title = Localize.Text("Manage Missing Fonts"); Width = 860; SizeToContent = SizeToContent.Height; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var warning = new Grid { Width = 72, Height = 64, Children = { new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M36,4 L70,62 L2,62 Z"), Fill = new SolidColorBrush(Color.FromRgb(0xF5, 0xC2, 0x2B)), Stretch = Stretch.Uniform }, new TextBlock { Text = "!", FontSize = 34, FontWeight = FontWeight.Bold, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) } } };
        var message = new TextBlock { Text = Localize.Text(missing.Count == 0 ? "All requested fonts are installed." : "Some fonts are missing. Replace any font with a default font or a font already used in this document. You can also replace all missing fonts with the default font. All changes apply to the whole document."), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 20, Margin = new Thickness(36, 8, 24, 16), Children = { warning } }; Grid.SetColumn(message, 1); head.Children.Add(message);
        var rows = new StackPanel { Spacing = 0 };
        string[] choices = [Localize.Text("Do Not Replace"), .. used.Where(f => !missing.Contains(f, StringComparer.OrdinalIgnoreCase)), Localize.Text("Default Font")];
        foreach (var name in missing)
        {
            var choice = new ComboBox { ItemsSource = choices, SelectedIndex = 0, Width = 300, MinHeight = 0, Height = 28, Tag = "missing-" + name };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Margin = new Thickness(8, 8, 24, 8), Children = { new TextBlock { Text = name + ":", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center } } };
            Grid.SetColumn(choice, 1); row.Children.Add(choice); rows.Children.Add(new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(0, 0, 0, 1), Child = row }); Replacements[name] = choice;
        }
        var list = new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Background = Skin.SurfaceDarkBrush, Height = 272, Margin = new Thickness(36, 0), Child = new ScrollViewer { Content = rows } };
        var ok = FormDialog.ActionButton("OK", () => Close(true)); ok.IsDefault = true; var cancel = FormDialog.ActionButton("Cancel", () => Close(false)); cancel.IsCancel = true;
        ok.MinWidth = cancel.MinWidth = 140;
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(36, 16, 36, 20), Children = { All } };
        All.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(cancel, 1); Grid.SetColumn(ok, 2); footer.Children.Add(cancel); footer.Children.Add(ok);
        All.IsCheckedChanged += (_, _) => { foreach (var box in Replacements.Values) box.IsEnabled = All.IsChecked != true; };
        Content = new StackPanel { Children = { head, list, footer } };
    }
}

/// <summary>Edit ▸ Convert to Profile: the document's pixels are converted so it looks the same in the destination profile.</summary>
internal sealed class ConvertProfileDialog : SideDialog
{
    internal readonly ComboBox Destination;
    internal readonly ComboBox Intent = BatchDialog.Choice(["Perceptual", "Saturation", "Relative Colorimetric", "Absolute Colorimetric"], 0);
    internal readonly ComboBox Engine = BatchDialog.Choice(["ACE Compatible (Independent)"], 0);
    internal readonly CheckBox BlackPoint = new() { Content = Localize.Text("Use Black Point Compensation"), IsChecked = true }, Dither = new() { Content = Localize.Text("Use Dither"), IsChecked = true },
        Flatten = new() { Content = Localize.Text("Flatten Image to Preserve Appearance"), IsChecked = true, Tag = "convert-flatten" }, Preview = new() { Content = Localize.Text("Preview"), IsChecked = true, Tag = "convert-preview" };
    private readonly IReadOnlyList<IccEntry> _profiles;
    internal ConvertProfileDialog(string source, ColorManagementSettings settings, Action changed) : base("Convert to Profile", 800)
    {
        _profiles = [.. new[] { "RGB", "CMYK", "GRAY" }.SelectMany(model => IccProfiles.Catalog(settings, model))];
        IEnumerable<(string, string)> Model(string model) => _profiles.Where(p => p.Model == model).Select(p => ("icc:" + p.Name, p.Name));
        Destination = Grouped([[("working-CMYK", Localize.Text("Working CMYK") + " - " + settings.Cmyk), ("working-GRAY", Localize.Text("Working Gray") + " - " + settings.Gray), ("working-RGB", Localize.Text("Working RGB") + " - " + settings.Rgb)], [("lab", "Lab Color")], Model("RGB"), Model("CMYK"), Model("GRAY")], "working-CMYK", 440);
        Destination.Tag = "convert-destination";
        Intent.SelectedIndex = (int)settings.Intent is var i and >= 0 and <= 3 ? i : 0; Intent.Width = 150; Engine.Width = 220; Engine.IsEnabled = false;
        ToolTip.SetTip(Engine, Localize.Text("Compositor converts with its own ACE-compatible engine."));
        Body.Children.Add(DialogKit.Group("Source Space", Line(Caption("Profile"), new TextBlock { Text = source, VerticalAlignment = VerticalAlignment.Center })));
        Body.Children.Add(DialogKit.Group("Destination Space", Line(Caption("Profile"), Destination)));
        Body.Children.Add(DialogKit.Group("Conversion Options", new StackPanel { Spacing = 6, Children = { DialogKit.Columns("80,*", 8, Caption("Engine"), Engine, Caption("Intent"), Intent), this.Mnemonic(BlackPoint, 'K'), this.Mnemonic(Dither, 'D'), this.Mnemonic(Flatten, 'S') } }));
        Actions.Children.Add(this.Mnemonic(Preview, 'P'));
        foreach (var box in new[] { Destination, Intent }) box.SelectionChanged += (_, _) => changed();
        Preview.IsCheckedChanged += (_, _) => changed();
    }
    /// <summary>The destination as a mode and profile; Lab has no profile.</summary>
    internal (ImageMode Mode, byte[]? Profile) Target(ColorManagementSettings settings)
    {
        var key = Key(Destination) ?? "working-CMYK";
        if (key == "lab") return (ImageMode.Lab, null);
        if (key.StartsWith("working-", StringComparison.Ordinal))
        {
            var model = key["working-".Length..];
            var name = model == "RGB" ? settings.Rgb : model == "CMYK" ? settings.Cmyk : settings.Gray;
            return (model == "RGB" ? ImageMode.RGB : model == "CMYK" ? ImageMode.CMYK : ImageMode.Grayscale, IccProfiles.Resolve(name, settings));
        }
        var entry = _profiles.First(p => "icc:" + p.Name == key);
        return (entry.Model == "RGB" ? ImageMode.RGB : entry.Model == "CMYK" ? ImageMode.CMYK : ImageMode.Grayscale, entry.Bytes);
    }
}
