using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Compositor.Core.Document;
using Compositor.Core.IO;

namespace Compositor.Desktop;

/// <summary>
/// Photoshop's gradient picker, as Gradient Map opens it from its ⌄: the gradients in their folders (Basics, Blues,
/// Purples…), each folder opening and closing, with the ⚙ menu (New Gradient Preset…, Rename, Delete, the five ways of
/// showing them, Append Default Gradients…, Import Gradients…, Export Selected Gradients…) and the + that keeps the
/// current gradient as a preset. The presets are the gradient library the Gradient tool uses.
/// </summary>
internal sealed class GradientPresetPicker
{
    private enum View { TextOnly, SmallThumbnail, LargeThumbnail, SmallList, LargeList }
    private static View _view = View.SmallThumbnail;
    private readonly Window _owner;
    private readonly Action<GradientPreset> _chosen;
    private readonly Func<GradientStopValue[]> _current;
    private readonly Flyout _flyout = new() { Placement = PlacementMode.BottomEdgeAlignedLeft };
    private readonly StackPanel _folders = new() { Spacing = 2 };
    private readonly HashSet<string> _open = ["Basics"];
    private List<GradientPreset> _presets = [];
    private GradientPreset? _selected;
    private readonly MenuItem _rename, _delete, _export;

    internal GradientPresetPicker(Window owner, Action<GradientPreset> chosen, Func<GradientStopValue[]> current)
    {
        _owner = owner; _chosen = chosen; _current = current;
        _rename = new MenuItem { Header = Localize.Text("Rename Gradient…"), Tag = "Rename Gradient…" };
        _delete = new MenuItem { Header = Localize.Text("Delete Gradient…"), Tag = "Delete Gradient…" };
        _export = new MenuItem { Header = Localize.Text("Export Selected Gradients…"), Tag = "Export Selected Gradients…" };
        var make = new MenuItem { Header = Localize.Text("New Gradient Preset…"), Tag = "New Gradient Preset…" };
        var append = new MenuItem { Header = Localize.Text("Append Default Gradients…"), Tag = "Append Default Gradients…" };
        var import = new MenuItem { Header = Localize.Text("Import Gradients…"), Tag = "Import Gradients…" };
        var views = new[] { ("Text Only", View.TextOnly), ("Small Thumbnail", View.SmallThumbnail), ("Large Thumbnail", View.LargeThumbnail), ("Small List", View.SmallList), ("Large List", View.LargeList) }
            .Select(v => { var item = new MenuItem { Header = Localize.Text(v.Item1), ToggleType = MenuItemToggleType.Radio, Tag = v.Item2 }; item.Click += (_, _) => { _view = v.Item2; Fill(); }; return item; }).ToArray();
        var menu = new ContextMenu();
        menu.Items.Add(make); menu.Items.Add(new Separator()); menu.Items.Add(_rename); menu.Items.Add(_delete); menu.Items.Add(new Separator());
        foreach (var item in views) menu.Items.Add(item);
        menu.Items.Add(new Separator()); menu.Items.Add(append); menu.Items.Add(import); menu.Items.Add(_export);
        var gear = new Button { Content = "⚙▾", Width = 36, Height = 30, Padding = default, HorizontalContentAlignment = HorizontalAlignment.Center, Tag = "gradient picker menu" };
        gear.Click += (_, _) =>
        {
            foreach (var item in views) item.IsChecked = Equals(item.Tag, _view);
            _rename.IsEnabled = _delete.IsEnabled = _export.IsEnabled = _selected is not null; menu.Open(gear);
        };
        var add = new Button { Content = "+", Width = 30, Height = 30, Padding = default, HorizontalContentAlignment = HorizontalAlignment.Center, Tag = "new gradient preset" };
        ToolTip.SetTip(add, Localize.Text("New Gradient Preset…"));
        add.Click += async (_, _) => await MakePreset();
        make.Click += async (_, _) => await MakePreset();
        _rename.Click += async (_, _) => await Rename();
        _delete.Click += (_, _) => { if (_selected is null) return; _presets.Remove(_selected); _selected = null; Save(); Fill(); };
        append.Click += (_, _) => { foreach (var preset in GradientLibrary.Defaults()) if (_presets.All(p => p.ID != preset.ID)) _presets.Add(preset); Save(); Fill(); };
        import.Click += async (_, _) => await Import();
        _export.Click += async (_, _) => await Export();
        var scroll = new ScrollViewer { Content = _folders, Height = 220, Width = 330, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Visible };
        var side = new StackPanel { Spacing = 8, Margin = new Thickness(8, 0, 0, 0), Children = { gear, add } };
        var root = new Grid { ColumnDefinitions = new("*,Auto"), Children = { new Border { Background = Skin.SurfaceDarkBrush, Child = scroll } } };
        Grid.SetColumn(side, 1); root.Children.Add(side);
        _flyout.Content = root;
    }

    internal void ShowAt(Control target) { Load(); Fill(); _flyout.ShowAt(target); }
    internal int Count => _presets.Count;

    private void Load()
    {
        try { _presets = GradientLibrary.Load(GradientLibrary.DefaultPath); }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) { _presets = GradientLibrary.Defaults(); }
    }
    private void Save()
    {
        try { GradientLibrary.Save(GradientLibrary.DefaultPath, _presets); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { }
    }

    private static IBrush Brush(GradientPreset preset)
    {
        var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative) };
        foreach (var stop in preset.Stops) brush.GradientStops.Add(new GradientStop(Color.FromUInt32(stop.Color), stop.Position));
        return brush;
    }

    /// <summary>The folders, each with its arrow, and the gradients in the open ones drawn as the view asks.</summary>
    private void Fill()
    {
        _folders.Children.Clear();
        foreach (var folder in _presets.GroupBy(p => p.Category))
        {
            var open = _open.Contains(folder.Key);
            var header = new Button
            {
                Background = Brushes.Transparent, BorderThickness = default, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Tag = "folder " + folder.Key,
                Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = open ? "⌄" : "›", Width = 12 }, new TextBlock { Text = "🗀" }, new TextBlock { Text = Localize.Text(folder.Key) } } },
            };
            header.Click += (_, _) => { if (!_open.Remove(folder.Key)) _open.Add(folder.Key); Fill(); };
            _folders.Children.Add(header);
            if (!open) continue;
            var list = _view is View.SmallThumbnail or View.LargeThumbnail ? (Panel)new WrapPanel { Margin = new Thickness(28, 2, 4, 6) } : new StackPanel { Margin = new Thickness(28, 2, 4, 6), Spacing = 2 };
            foreach (var preset in folder)
            {
                var size = _view == View.LargeThumbnail ? 64 : 44;
                Control content = _view switch
                {
                    View.TextOnly => new TextBlock { Text = Localize.Text(preset.Name) },
                    View.SmallList or View.LargeList => new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 8,
                        Children = { new Border { Width = _view == View.LargeList ? 64 : 32, Height = _view == View.LargeList ? 32 : 16, Background = Brush(preset) }, new TextBlock { Text = Localize.Text(preset.Name), VerticalAlignment = VerticalAlignment.Center } },
                    },
                    _ => new Border { Width = size, Height = size, Background = Brush(preset), BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1) },
                };
                var button = new Button { Content = content, Padding = new Thickness(1), Margin = new Thickness(1), Background = ReferenceEquals(preset, _selected) ? Skin.AccentBrush : Brushes.Transparent, BorderThickness = default, Tag = "gradient " + preset.Name };
                ToolTip.SetTip(button, Localize.Text(preset.Name));
                button.Click += (_, _) => { _selected = preset; _chosen(preset); _flyout.Hide(); };
                list.Children.Add(button);
            }
            _folders.Children.Add(list);
        }
    }

    private async Task<string?> AskName(string title, string start)
    {
        var dialog = new FormDialog(title, 420, 200); var name = dialog.Text("Name", start);
        dialog.Validate = () => string.IsNullOrWhiteSpace(name.Text) || name.Text.Length > 200 ? "Enter a name." : null;
        return await dialog.ShowDialog<bool>(_owner) ? name.Text!.Trim() : null;
    }

    private async Task MakePreset()
    {
        if (await AskName("New Gradient Preset", Localize.Text("Custom")) is not { } name) return;
        Load(); var preset = new GradientPreset(Guid.NewGuid(), name, "Custom", _current()); _presets.Add(preset); _selected = preset; _open.Add("Custom"); Save(); Fill();
    }

    private async Task Rename()
    {
        if (_selected is not { } chosen || await AskName("Rename Gradient", Localize.Text(chosen.Name)) is not { } name) return;
        var index = _presets.IndexOf(chosen); if (index < 0) return;
        _presets[index] = _selected = chosen with { Name = name }; Save(); Fill();
    }

    private async Task Import()
    {
        var picked = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Import Gradients…"), FileTypeFilter = [new FilePickerFileType(Localize.Text("Gradients")) { Patterns = ["*.json"] }] });
        if (picked.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        try { foreach (var preset in GradientLibrary.Load(path)) if (_presets.All(p => p.ID != preset.ID)) _presets.Add(preset); Save(); Fill(); }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) { }
    }

    private async Task Export()
    {
        if (_selected is not { } chosen) return;
        var file = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Localize.Text("Export Selected Gradients…"), SuggestedFileName = chosen.Name + ".json", DefaultExtension = ".json" });
        if (file?.TryGetLocalPath() is not { } path) return;
        try { GradientLibrary.Save(path, [chosen]); } catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { }
    }
}
