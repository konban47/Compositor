using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Compositor.Desktop;

/// <summary>Responsive settings form: scroll only the fields; actions remain visible at the bottom.</summary>
internal sealed class FormDialog : DialogWindow
{
    internal StackPanel Fields { get; } = new() { Spacing = 10 };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.OrangeRed };
    internal Func<string?>? Validate { get; set; }
    internal Action? Changed { get; set; }
    internal bool PreviewEnabled { get; private set; } = true;
    internal FormDialog(string title, double width = 520, double height = 560)
    {
        Title = Localize.Text(title); Width = width; Height = height; MinWidth = 380; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new Grid { RowDefinitions = new("*,Auto,Auto"), Margin = new Thickness(18) };
        layout.Children.Add(new ScrollViewer { Content = Fields, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Grid.SetRow(_error, 1); layout.Children.Add(_error);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = ActionButton("Cancel", () => Close(false));
        var ok = ActionButton("OK", () => { var error = Validate?.Invoke(); _error.Text = error is null ? "" : Localize.Text(error); if (error is null) Close(true); });
        actions.Children.Add(cancel); actions.Children.Add(ok); Grid.SetRow(actions, 2); layout.Children.Add(actions); Content = layout;
    }
    internal static Button ActionButton(string title, Action action)
    {
        var button = new Button { Content = Localize.Text(title), MinHeight = 32, MinWidth = 82, Margin = new Thickness(4, 0), Padding = new Thickness(12, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(button, Localize.Text(title)); button.Click += (_, _) => action(); return button;
    }
    internal void Description(string text) => Fields.Children.Add(new TextBlock { Text = Localize.Text(text), TextWrapping = TextWrapping.Wrap, Foreground = Skin.SecondaryBrush });
    internal void Row(string label, Control control)
    {
        var row = new Grid { ColumnDefinitions = new("150,*") };
        row.Children.Add(new TextBlock { Text = Localize.Text(label), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        control.MinHeight = 30; control.HorizontalAlignment = HorizontalAlignment.Stretch; Grid.SetColumn(control, 1); row.Children.Add(control);
        Fields.Children.Add(row);
    }
    internal NumericUpDown Number(string label, double value, double min, double max, Action<double>? change = null)
    {
        var input = new NumericUpDown { Value = (decimal)value, Minimum = (decimal)min, Maximum = (decimal)max, Increment = 1, FormatString = "0.###", ShowButtonSpinner = false, Tag = label };
        input.ValueChanged += (_, _) => { if (input.Value is { } v) { change?.Invoke((double)v); Changed?.Invoke(); } };
        Row(label, input); return input;
    }
    internal TextBox Text(string label, string value, bool multiline = false)
    {
        var input = new TextBox { Text = value, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 100 : 30, Tag = label };
        input.TextChanged += (_, _) => Changed?.Invoke(); Row(label, input); return input;
    }
    internal ComboBox Choice(string label, IEnumerable<string> choices, int selected, Action<int>? change = null)
    {
        var choice = new ComboBox { ItemsSource = choices.Select(Localize.Text).ToArray(), SelectedIndex = selected, Tag = label };
        choice.SelectionChanged += (_, _) => { if (choice.SelectedIndex >= 0) { change?.Invoke(choice.SelectedIndex); Changed?.Invoke(); } };
        Row(label, choice); return choice;
    }
    internal CheckBox Check(string label, bool value, Action<bool>? change = null)
    {
        var check = new CheckBox { Content = new TextBlock { Text = Localize.Text(label), TextWrapping = TextWrapping.Wrap }, IsChecked = value, Tag = label };
        check.IsCheckedChanged += (_, _) => { change?.Invoke(check.IsChecked == true); Changed?.Invoke(); }; Fields.Children.Add(check); return check;
    }
    internal void Preview() => Check("Preview", true, value => PreviewEnabled = value);
}
