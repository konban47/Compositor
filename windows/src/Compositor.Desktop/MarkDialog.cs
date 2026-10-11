using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Format;

namespace Compositor.Desktop;

internal sealed class MarkDialog : DialogWindow
{
    private DocumentMark? _result;
    private MarkDialog(DocumentMark mark)
    {
        Title = Localize.Text("Edit Annotation…"); Width = 440; MinWidth = 340; MaxHeight = 720;
        SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(18), Spacing = 8 };
        TextBox Text(string label, string value, int limit, bool multiline = false)
        {
            panel.Children.Add(new TextBlock { Text = Localize.Text(label) });
            var box = new TextBox { Text = value, MaxLength = limit, MinHeight = multiline ? 100 : 30, AcceptsReturn = multiline, TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(box); return box;
        }
        NumericUpDown Number(string label, double value, bool positive = false)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("80,*") };
            row.Children.Add(new TextBlock { Text = Localize.Text(label), VerticalAlignment = VerticalAlignment.Center });
            var input = new NumericUpDown { Value = (decimal)value, Minimum = positive ? 1 : -1000000, Maximum = 1000000, MinHeight = 30, FormatString = "0.##" };
            Grid.SetColumn(input, 1); row.Children.Add(input); panel.Children.Add(row); return input;
        }
        var name = Text("Name", mark.Name, 256); var x = Number("X", mark.X); var y = Number("Y", mark.Y);
        NumericUpDown? width = null, height = null;
        if (mark.Kind is MarkKind.Slice or MarkKind.Measure) { width = Number("Width", mark.Width, mark.Kind == MarkKind.Slice); height = Number("Height", mark.Height, mark.Kind == MarkKind.Slice); }
        var note = mark.Kind == MarkKind.Note ? Text("Note", mark.Text, 8192, true) : null;
        var url = mark.Kind == MarkKind.Slice ? Text("URL", mark.Url, 2048) : null;
        var group = mark.Kind == MarkKind.Count ? Text("Count Group", mark.Group, 256) : null;
        var color = Text("Annotation Color (#RRGGBB)", $"#{mark.Color & 0xFFFFFF:X6}", 7);
        var visible = new CheckBox { Content = Localize.Text("Visible"), IsChecked = mark.Visible }; panel.Children.Add(visible);
        var error = new TextBlock { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap }; panel.Children.Add(error);
        var ok = new Button { Content = Localize.Text("OK"), IsDefault = true, MinWidth = 84, MinHeight = 32 };
        var cancel = new Button { Content = Localize.Text("Cancel"), IsCancel = true, MinWidth = 84, MinHeight = 32 };
        ok.Click += (_, _) =>
        {
            if (!Color.TryParse(color.Text, out var tint)) { error.Text = Localize.Text("Enter a color as #RRGGBB."); return; }
            var next = mark with { Name = name.Text ?? "", X = (double)(x.Value ?? 0), Y = (double)(y.Value ?? 0), Width = width is null ? mark.Width : (double)(width.Value ?? 1), Height = height is null ? mark.Height : (double)(height.Value ?? 1),
                Text = note?.Text ?? mark.Text, Url = url?.Text ?? mark.Url, Group = group?.Text ?? mark.Group, Visible = visible.IsChecked == true, Color = tint.ToUInt32() | 0xFF000000 };
            if (!next.IsValid) { error.Text = Localize.Text("Invalid annotation values."); return; }
            _result = next; Close();
        };
        cancel.Click += (_, _) => Close();
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, ok } });
        Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }
    internal static async Task<DocumentMark?> Edit(Window owner, DocumentMark mark)
    { var dialog = new MarkDialog(mark); await dialog.ShowDialog(owner); return dialog._result; }
}
