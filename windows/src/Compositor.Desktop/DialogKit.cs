using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Compositor.Desktop;

/// <summary>The parts Photoshop's file dialogs are built from, in this app's own controls.</summary>
internal static class DialogKit
{
    internal const double FieldHeight = 28, ActionWidth = 104;
    /// <summary>A folder on disk: "Folder" alone translates as the layers panel's group.</summary>
    internal const string FolderWord = "Folder on Disk";

    /// <summary>A frame with its title set into the top edge; <paramref name="heading"/> puts a control there instead, as Batch's
    /// "Source: [Folder]".</summary>
    internal static Control Group(string title, Control content, Control? heading = null)
    {
        var frame = new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Margin = new Thickness(0, heading is null ? 11 : 16, 0, 0), Padding = new Thickness(12, heading is null ? 16 : 24, 12, 10), Child = content };
        var label = new Border
        {
            Background = Skin.ChromeBrush, Padding = new Thickness(6, 0), Margin = new Thickness(12, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Child = heading ?? new TextBlock { Text = Localize.Text(title), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2) },
        };
        return new Grid { Children = { frame, label } };
    }

    internal static TextBlock Label(string text, HorizontalAlignment align = HorizontalAlignment.Left) => new()
    { Text = Localize.Text(text) + ":", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = align, Foreground = Skin.SecondaryBrush };

    /// <summary>A single-line field short enough for these dialogs, which would otherwise show its scroll bar.</summary>
    internal static TextBox Line(string? tag = null) { var box = new TextBox { MinHeight = 0, Height = 26, Padding = new Thickness(6, 2), VerticalContentAlignment = VerticalAlignment.Center, Tag = tag }; ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Disabled); ScrollViewer.SetHorizontalScrollBarVisibility(box, ScrollBarVisibility.Hidden); return box; }

    /// <summary>A caption and its control on one line; <paramref name="control"/> keeps its own width when it has one.</summary>
    internal static Grid Row(string label, Control control, double labelWidth = 86)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions($"{labelWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)},*"), ColumnSpacing = 8, MinHeight = FieldHeight };
        row.Children.Add(Label(label)); Grid.SetColumn(control, 1); control.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(control); return row;
    }

    /// <summary>Lays controls out in equal rows, as many columns as there are control sizes given.</summary>
    internal static Grid Columns(string columns, double spacing, params Control[] cells)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), ColumnSpacing = spacing, RowSpacing = 6 };
        var count = grid.ColumnDefinitions.Count;
        for (var i = 0; i < (cells.Length + count - 1) / count; i++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var i = 0; i < cells.Length; i++) { Grid.SetRow(cells[i], i / count); Grid.SetColumn(cells[i], i % count); cells[i].VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(cells[i]); }
        return grid;
    }

    /// <summary>The column of OK, Cancel and the rest down a dialog's right side.</summary>
    internal static StackPanel ActionColumn(params Control[] actions)
    {
        var column = new StackPanel { Spacing = 10, Width = ActionWidth, Margin = new Thickness(16, 11, 0, 0) };
        foreach (var action in actions) { if (action is Button) action.HorizontalAlignment = HorizontalAlignment.Stretch; action.Margin = default; column.Children.Add(action); }
        return column;
    }

    /// <summary>Gives a button or a check box its Alt key: in Chinese the letter is shown after the caption, "Browse(B)…", as Photoshop does.</summary>
    internal static T Mnemonic<T>(this Window window, T control, char key) where T : ContentControl
    {
        if (Localize.IsChinese) control.Content = control.Content switch
        {
            string text => WithKey(text, key),
            TextBlock block => new TextBlock { Text = WithKey(block.Text ?? "", key), TextWrapping = block.TextWrapping, VerticalAlignment = block.VerticalAlignment },
            var other => other,
        };
        window.KeyDown += (_, e) =>
        {
            if (e.KeyModifiers != KeyModifiers.Alt || e.Key != Key.A + (char.ToUpperInvariant(key) - 'A') || !control.IsEffectivelyEnabled || !control.IsEffectivelyVisible) return;
            if (control is ToggleButton toggle) toggle.IsChecked = toggle is RadioButton || toggle.IsChecked != true;
            else control.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            e.Handled = true;
        };
        return control;
    }
    private static string WithKey(string text, char key) => text.EndsWith('…') ? $"{text[..^1]}({key})…" : $"{text}({key})";
}
