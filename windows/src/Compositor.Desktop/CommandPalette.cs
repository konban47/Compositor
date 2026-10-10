using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Compositor.Desktop;

internal sealed class CommandPalette : DialogWindow
{
    internal sealed record Entry(string Title, string Shortcut, Action Execute)
    {
        public override string ToString() => Shortcut.Length == 0 ? Title : $"{Title}    {Shortcut}";
    }

    private readonly TextBox _query = new()
    {
        PlaceholderText = Localize.Text("Search commands and tools…"),
        Height = 28,
        FontSize = 12,
        CornerRadius = new CornerRadius(2),
        Background = Skin.SurfaceDarkBrush,
        BorderBrush = Skin.BorderControlBrush,
        Foreground = Skin.LabelBrush,
        VerticalContentAlignment = VerticalAlignment.Center,
    };
    private readonly ListBox _results = new()
    {
        Background = Skin.SurfaceDarkBrush,
        BorderBrush = Skin.BorderControlBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(2),
        FontSize = 12,
    };
    private readonly IReadOnlyList<Entry> _entries;
    private Entry? _chosen;

    private CommandPalette(IReadOnlyList<Entry> entries)
    {
        _entries = entries;
        Title = Localize.Text("Find Command");
        Width = 640; Height = 450; MinWidth = 420; MinHeight = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(_query, Dock.Top);
        _query.Margin = new Thickness(0, 0, 0, 10);
        panel.Children.Add(_query); panel.Children.Add(_results);
        Content = panel;
        _query.TextChanged += (_, _) => Search();
        _results.DoubleTapped += (_, _) => Accept();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Enter) Accept();
            else if (e.Key is Key.Down or Key.Up)
            {
                _results.SelectedIndex = Math.Clamp(_results.SelectedIndex + (e.Key == Key.Down ? 1 : -1),
                    0, Math.Max(0, _results.ItemCount - 1));
                if (_results.SelectedItem is { } selected) _results.ScrollIntoView(selected);
            }
            else return;
            e.Handled = true;
        };
        Opened += (_, _) => _query.Focus();
        Search();
    }

    private void Search()
    {
        var words = (_query.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _results.ItemsSource = _entries.Where(entry => words.All(word =>
            (entry.Title.Contains(word, StringComparison.OrdinalIgnoreCase) || string.Join(" ", entry.Title.Split('›').Select(Localize.English)).Contains(word, StringComparison.OrdinalIgnoreCase)))).ToArray();
        _results.SelectedIndex = _results.ItemCount > 0 ? 0 : -1;
    }

    private void Accept()
    {
        if (_results.SelectedItem is not Entry entry) return;
        _chosen = entry;
        Close();
    }

    internal static async Task<Entry?> Pick(Window owner, IReadOnlyList<Entry> entries)
    {
        var palette = new CommandPalette(entries);
        await palette.ShowDialog(owner);
        return palette._chosen;
    }
}
