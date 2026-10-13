using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Document;
using SkiaSharp;

namespace Compositor.Desktop;

internal static class FontBrowser
{
    private static readonly Dictionary<string, int> Categories = new(StringComparer.OrdinalIgnoreCase);
    private static int Category(string family)
    {
        if (Categories.TryGetValue(family, out var value)) return value;
        using var face = SKTypeface.FromFamilyName(family);
        var bytes = face.GetTableData(0x4F532F32); // OpenType OS/2 sFamilyClass and PANOSE.
        var cls = bytes is { Length: >= 42 } ? bytes[30] : 0;
        value = face.IsFixedPitch ? 5 : cls switch { 5 => 2, >= 1 and <= 7 => 1, 8 => 3, 10 => 4, 9 or 12 => 8, _ => 0 };
        if (bytes is { Length: >= 42 } && bytes[32] == 3) value = bytes[33] is 2 or 3 ? 7 : 6;
        Categories[family] = value; return value;
    }

    public static void Show(Control anchor, string current, string sample, Action<string> choose)
    {
        var flyout = new Flyout { Placement = PlacementMode.LeftEdgeAlignedTop };
        var root = new Grid { Width = 630, Height = 450, RowDefinitions = new("Auto,Auto,*"), Background = Skin.ChromeBrush };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        title.Children.Add(new TextBlock { Text = Localize.Text("Your Fonts"), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(title);
        var search = new TextBox { PlaceholderText = Localize.Text("Search fonts"), Text = "", Width = 320, Tag = "font-search" };
        var favorites = new ToggleButton { Content = "☆", Tag = "font-favorites", Width = 30 };
        ToolTip.SetTip(favorites, Localize.Text("Favorites"));
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6), Children = { search, favorites } };
        Grid.SetRow(top, 1); root.Children.Add(top);
        var categories = new ListBox { Width = 135, ItemsSource = new[] { "All Classes", "Serif", "Slab Serif", "Sans Serif", "Script", "Monospace", "Handwritten", "Blackletter", "Decorative" }.Select(Localize.Text).ToArray(), SelectedIndex = 0, Tag = "font-categories" };
        var list = new ListBox { Tag = "font-candidates" };
        var body = new Grid { ColumnDefinitions = new("140,*") }; body.Children.Add(categories); Grid.SetColumn(list, 1); body.Children.Add(list); Grid.SetRow(body, 2); root.Children.Add(body);
        var preview = sample.Length > 0 ? sample.Replace('\n', ' ')[..Math.Min(18, sample.Length)] : Localize.Text("Font Sample");
        list.ItemTemplate = new FuncDataTemplate<string>((family, _) =>
        {
            if (string.IsNullOrWhiteSpace(family)) return new Border();
            var row = new Grid { ColumnDefinitions = new("30,*,150"), MinHeight = 32 };
            var favorite = new Button { Content = EditorPreferences.Current.FavoriteFonts.Contains(family!) ? "★" : "☆", Padding = new Thickness(2), Width = 27, MinHeight = 24, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            favorite.Click += (_, e) => { if (!EditorPreferences.Current.FavoriteFonts.Add(family!)) EditorPreferences.Current.FavoriteFonts.Remove(family!); favorite.Content = EditorPreferences.Current.FavoriteFonts.Contains(family!) ? "★" : "☆"; EditorPreferences.Current.Save(); e.Handled = true; };
            row.Children.Add(favorite); var label = new TextBlock { Text = family, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }; Grid.SetColumn(label, 1); row.Children.Add(label);
            var text = new TextBlock { Text = preview, FontFamily = new FontFamily(family!), FontSize = new[] { 12, 14, 18, 24, 30, 36 }[EditorPreferences.Current.FontPreviewSize], IsVisible = EditorPreferences.Current.FontPreviewSize != 0, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }; Grid.SetColumn(text, 2); row.Children.Add(text); return row;
        });
        var rebuilding = false;
        void Refresh()
        {
            rebuilding = true;
            list.ItemsSource = FontCatalog.Families.Where(f => f.Contains(search.Text ?? "", StringComparison.CurrentCultureIgnoreCase)
                && (favorites.IsChecked != true || EditorPreferences.Current.FavoriteFonts.Contains(f)) && (categories.SelectedIndex == 0 || Category(f) == categories.SelectedIndex)).ToArray();
            list.SelectedItem = FontCatalog.Family(current); rebuilding = false;
        }
        search.TextChanged += (_, _) => Refresh(); categories.SelectionChanged += (_, _) => Refresh(); favorites.Click += (_, _) => Refresh();
        top.Children.Add(FormDialog.ActionButton("Clear All", () => { search.Text = ""; favorites.IsChecked = false; categories.SelectedIndex = 0; Refresh(); }));
        list.SelectionChanged += (_, _) => { if (!rebuilding && list.SelectedItem is string font) { choose(font); flyout.Hide(); } };
        flyout.Content = root; Refresh(); flyout.ShowAt(anchor); search.Focus();
    }
}
