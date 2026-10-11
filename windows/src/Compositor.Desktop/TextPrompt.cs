using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Compositor.Desktop;

/// <summary>
/// A one-line prompt, for the name a layer is being renamed to. Avalonia ships no such dialog, so this is
/// one: a text box, Enter to accept, Escape to cancel.
/// </summary>
internal sealed class TextPrompt : DialogWindow
{
    private readonly TextBox _box;
    private bool _accepted;

    private TextPrompt(string title, string label, string initial)
    {
        Title = Localize.Text(title);
        Width = 380;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _box = new TextBox
        {
            Text = initial,
            Height = 26,
            FontSize = 12,
            CornerRadius = new CornerRadius(7),
            Background = Skin.SurfaceDarkBrush,
            BorderBrush = Skin.BorderControlBrush,
            Foreground = Skin.LabelBrush,
            Margin = new Thickness(0, 8, 0, 14),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var ok = new Button
        {
            Content = Localize.Text("OK"),
            IsDefault = true,
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
        var cancel = new Button
        {
            Content = Localize.Text("Cancel"),
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
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close();
        _box.KeyDown += (_, pressed) =>
        {
            if (pressed.Key != Key.Enter) return;
            Accept();
            pressed.Handled = true;
        };
        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = Localize.Text(label), FontSize = 12, Foreground = Skin.LabelBrush },
                _box,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, ok },
                },
            },
        };
        Opened += (_, _) =>
        {
            _box.Focus();
            _box.SelectAll();
        };
    }

    private void Accept()
    {
        _accepted = true;
        Close();
    }

    /// <summary>What was typed, or null when the prompt was dismissed.</summary>
    public static async Task<string?> Ask(Window owner, string title, string label, string initial)
    {
        var prompt = new TextPrompt(title, label, initial);
        await prompt.ShowDialog(owner);
        return prompt._accepted ? prompt._box.Text : null;
    }
}
