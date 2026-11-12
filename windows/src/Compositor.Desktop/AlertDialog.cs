using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Compositor.Desktop;

/// <summary>
/// Photoshop's alert: a yellow warning sign beside the question and a row of buttons under it, each with its Alt key, as
/// "更改模式会影响图层的外观。是否在更改模式前合并图层？" asks with Merge(M), Cancel and Don't Merge(D). The answer is the
/// index of the button pressed, or -1 when the window was closed.
/// </summary>
internal sealed class AlertDialog : DialogWindow
{
    private int _answer = -1;
    internal IReadOnlyList<Button> Buttons { get; }

    private AlertDialog(string message, (string Text, char? Key)[] buttons, int cancel)
    {
        Title = "Compositor"; SizeToContent = SizeToContent.WidthAndHeight; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var sign = new Canvas { Width = 34, Height = 30, Margin = new Thickness(0, 2, 0, 0) };
        sign.Children.Add(new Polygon { Points = [new Point(17, 1), new Point(33, 29), new Point(1, 29)], Fill = new SolidColorBrush(Color.FromRgb(0xF5, 0xC2, 0x1B)), Stroke = new SolidColorBrush(Color.FromRgb(0xC9, 0x8E, 0x00)), StrokeThickness = 1 });
        sign.Children.Add(new TextBlock { Text = "!", FontWeight = FontWeight.Bold, FontSize = 18, Foreground = Brushes.Black, [Canvas.LeftProperty] = 14.0, [Canvas.TopProperty] = 7.0 });
        var text = new TextBlock { Text = Localize.Text(message), TextWrapping = TextWrapping.Wrap, MaxWidth = 460, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        var made = new List<Button>();
        for (var i = 0; i < buttons.Length; i++)
        {
            var index = i; var (caption, key) = buttons[i];
            var button = FormDialog.ActionButton("", () => { _answer = index; Close(); });
            button.Content = Localize.Text(caption) + (key is { } k ? $"({k})" : ""); button.Tag = caption; button.MinWidth = 120; button.Margin = default;
            ToolTip.SetTip(button, null);
            if (i == 0) button.IsDefault = true;
            if (i == cancel) button.IsCancel = true;
            if (key is { } letter) LayerDialogControls.Key(this, letter, button);
            row.Children.Add(button); made.Add(button);
        }
        Buttons = made;
        Content = new StackPanel
        {
            Margin = new Thickness(24, 20, 20, 16),
            Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Children = { sign, text } }, row },
        };
        Opened += (_, _) => made[0].Focus();
    }

    /// <summary>Asks <paramref name="message"/> with the buttons given; <paramref name="cancel"/> is the one Esc presses.</summary>
    internal static async Task<int> Ask(Window owner, string message, (string Text, char? Key)[] buttons, int cancel)
    {
        var dialog = new AlertDialog(message, buttons, cancel);
        await dialog.ShowDialog(owner);
        return dialog._answer;
    }
}
