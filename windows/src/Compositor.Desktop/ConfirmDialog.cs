using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Compositor.Desktop;

/// <summary>
/// A question with two answers, for the places where something would be thrown away. Avalonia ships no such
/// dialog, so this is one: the answer is false when it is dismissed by the window's own close button.
/// </summary>
internal sealed class ConfirmDialog : DialogWindow
{
    private bool _answered;

    private ConfirmDialog(string title, string message, string yes, string no)
    {
        Title = Localize.Text(title);
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var keep = new Button
        {
            Content = Localize.Text(no),
            IsCancel = true,
            MinWidth = 76,
            Height = 26,
            FontSize = 12,
            CornerRadius = new CornerRadius(7),
            Background = Skin.SurfaceControlBrush,
            BorderBrush = Skin.BorderControlBrush,
            BorderThickness = new Thickness(1),
            Foreground = Skin.LabelBrush,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        var go = new Button
        {
            Content = Localize.Text(yes),
            IsDefault = false,
            MinWidth = 76,
            Height = 26,
            FontSize = 12,
            CornerRadius = new CornerRadius(7),
            Background = Skin.AccentBrush,
            BorderBrush = Skin.AccentBrush,
            BorderThickness = new Thickness(1),
            Foreground = Avalonia.Media.Brushes.White,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        keep.Click += (_, _) => Close();
        go.Click += (_, _) =>
        {
            _answered = true;
            Close();
        };
        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new ScrollViewer { MaxHeight = 400, Content = new TextBlock { Text = Localize.Text(message), TextWrapping = Avalonia.Media.TextWrapping.Wrap } },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { keep, go },
                },
            },
        };
        Opened += (_, _) => keep.Focus();
    }

    /// <summary>Whether the first answer was given: closing the dialog without choosing is the second one.</summary>
    public static async Task<bool> Ask(Window owner, string title, string message, string yes, string no)
    {
        var dialog = new ConfirmDialog(title, message, yes, no);
        await dialog.ShowDialog(owner);
        return dialog._answered;
    }
}
