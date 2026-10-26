using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Compositor.Core.IO;

namespace Compositor.Desktop;

internal sealed class PresetManagerDialog : DialogWindow
{
    protected override bool FullCaption => true;
    private readonly ComboBox _kind = new() { Width = 175, Tag = "preset-type" };
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle, Tag = "preset-list" };
    private readonly TextBlock _error = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
    private readonly Button _save, _rename, _delete;
    private List<PresetItem> _items = [];
    private int _display = 1;
    private string Kind => PresetCatalog.Kinds[Math.Max(0, _kind.SelectedIndex)];
    internal PresetManagerDialog(string kind = "Contour")
    {
        Title = Localize.Text("Preset Manager"); Width = 970; Height = 630; MinWidth = 660; MinHeight = 380; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { ColumnDefinitions = new("*,175"), RowDefinitions = new("*,Auto"), Margin = new Thickness(16) };
        var left = new Grid { RowDefinitions = new("Auto,*"), Margin = new Thickness(12) };
        var header = new Grid { ColumnDefinitions = new("Auto,*,Auto"), Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock { Text = Localize.Text("Preset Type") + ":", Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        _kind.ItemsSource = PresetCatalog.Kinds.Select(k => Localize.Text(k == "Contour" ? "Contours" : k)).ToArray(); _kind.SelectedIndex = Math.Max(0, Array.IndexOf(PresetCatalog.Kinds, kind));
        Grid.SetColumn(_kind, 1); header.Children.Add(_kind);
        var menu = new Button { Content = "⚙", Tag = "preset-manager-menu" }; Grid.SetColumn(menu, 2); header.Children.Add(menu); menu.Click += (_, _) => SettingsMenu(menu);
        left.Children.Add(header); Grid.SetRow(_list, 1); left.Children.Add(_list);
        root.Children.Add(new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Child = left });
        var actions = new StackPanel { Spacing = 16, Margin = new Thickness(24, 8, 0, 0) }; Grid.SetColumn(actions, 1); root.Children.Add(actions);
        Button Action(string title, Action handler) { var button = FormDialog.ActionButton(title, handler); button.HorizontalAlignment = HorizontalAlignment.Stretch; actions.Children.Add(button); return button; }
        Action("Done", () => Close());
        var load = Action("Load…", () => _ = Import(false)); load.Margin = new Thickness(0, 20, 0, 0);
        _save = Action("Save Set…", () => _ = Export());
        _rename = Action("Rename…", () => _ = Rename()); _rename.Margin = new Thickness(0, 20, 0, 0);
        _delete = Action("Delete", Delete);
        Grid.SetRow(_error, 1); Grid.SetColumnSpan(_error, 2); root.Children.Add(_error); Content = root;
        _list.SelectionChanged += (_, _) => UpdateActions();
        _list.Styles.Add(new Style(s => s.OfType<ListBoxItem>()) { Setters = { new Setter(PaddingProperty, new Thickness(2)), new Setter(MarginProperty, new Thickness(0)), new Setter(MinHeightProperty, 0d) } });
        _kind.SelectionChanged += (_, _) => { _error.Text = ""; Load(); };
        _list.KeyDown += (_, e) => { if (e.Key == Key.Delete) { Delete(); e.Handled = true; } if (e.Key == Key.F2) { _ = Rename(); e.Handled = true; } };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Load();
    }
    private void Guard(Action action) { try { action(); _error.Text = ""; } catch (Exception e) { _error.Text = Localize.Text(e.Message); } }
    private void Load() => Guard(() => { _items = PresetCatalog.Load(Kind); Reload(); });
    private PresetItem[] Selected => _list.SelectedItems?.OfType<PresetItem>().ToArray() ?? [];
    private void Reload(IEnumerable<Guid>? selection = null)
    {
        var ids = selection?.ToHashSet() ?? [];
        _list.ItemsPanel = _display is 1 or 2 ? new FuncTemplate<Panel?>(() => new WrapPanel()) : new FuncTemplate<Panel?>(() => new StackPanel());
        _list.ItemTemplate = new FuncDataTemplate<PresetItem>((p, _) =>
        {
            if (p is null) return new TextBlock();
            var small = _display is 1 or 3; var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            if (_display != 0) row.Children.Add(new PresetThumbnail(p) { Width = small ? 44 : 84, Height = small ? 44 : 84 });
            if (_display is 0 or 3 or 4) row.Children.Add(new TextBlock { Text = Localize.Text(p.Name), VerticalAlignment = VerticalAlignment.Center });
            ToolTip.SetTip(row, Localize.Text(p.Name)); return row;
        });
        _list.ItemsSource = _items.ToArray(); _list.SelectedItems?.Clear(); foreach (var item in _items.Where(p => ids.Contains(p.ID))) _list.SelectedItems?.Add(item);
        UpdateActions();
    }
    private void UpdateActions() { _save.IsEnabled = _delete.IsEnabled = Selected.Length > 0; _rename.IsEnabled = Selected.Length == 1; }
    private void Commit(List<PresetItem> items, IEnumerable<Guid>? selection = null) => Guard(() => { PresetCatalog.Save(Kind, items); _items = items; Reload(selection); });
    private void Delete() { var ids = Selected.Select(p => p.ID).ToHashSet(); if (ids.Count > 0) Commit(_items.Where(p => !ids.Contains(p.ID)).ToList()); }
    private async Task Rename()
    {
        if (Selected is not [var item]) return;
        var dialog = new FormDialog("Rename Preset", 430, 230); var name = dialog.Text("Name", item.Name);
        dialog.Validate = () => string.IsNullOrWhiteSpace(name.Text) || name.Text.Length > 200 ? "Enter a name (1–200 characters)." : null;
        if (await dialog.ShowDialog<bool>(this)) Commit(_items.Select(p => p.ID == item.ID ? p.Rename(name.Text!.Trim()) : p).ToList(), [item.ID]);
    }
    private async Task Import(bool replace)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = Localize.Text(replace ? "Replace Presets…" : "Load Presets…"), FileTypeFilter = [new("Compositor JSON") { Patterns = ["*.json"] }] });
            if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            var imported = PresetCatalog.Import(Kind, path); Commit(replace ? imported : PresetCatalog.Append(_items, imported), imported.Select(p => p.ID));
        }
        catch (Exception e) { _error.Text = Localize.Text(e.Message); }
    }
    private async Task Export()
    {
        var selected = Selected; if (selected.Length == 0) return;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new() { SuggestedFileName = "Compositor-" + Kind.ToLowerInvariant() + "-presets.json", DefaultExtension = "json", FileTypeChoices = [new("Compositor JSON") { Patterns = ["*.json"] }] });
            if (file?.TryGetLocalPath() is { } path) { PresetCatalog.Save(Kind, selected, path); _error.Text = ""; }
        }
        catch (Exception e) { _error.Text = Localize.Text(e.Message); }
    }
    private void SettingsMenu(Control anchor)
    {
        var menu = new ContextMenu();
        void Action(string label, Action action, bool? check = null) { var item = new MenuItem { Header = Localize.Text(label) }; if (check is { } value) { item.ToggleType = MenuItemToggleType.CheckBox; item.IsChecked = value; } item.Click += (_, _) => action(); menu.Items.Add(item); }
        var names = new[] { "Text Only", "Small Thumbnails", "Large Thumbnails", "Small List", "Large List" };
        for (var i = 0; i < names.Length; i++) { var index = i; Action(names[i], () => { var ids = Selected.Select(p => p.ID).ToArray(); _display = index; Reload(ids); }, i == _display); }
        menu.Items.Add(new Separator());
        Action(Kind == "Contour" ? "Reset Contours…" : "Reset Presets…", () => Commit(PresetCatalog.Defaults(Kind)));
        Action(Kind == "Contour" ? "Replace Contours…" : "Replace Presets…", () => _ = Import(true));
        menu.Items.Add(new Separator()); Action(Kind == "Contour" ? "Contours" : "Append Default Presets", () => Commit(PresetCatalog.Append(_items, PresetCatalog.Defaults(Kind))));
        menu.Open(anchor);
    }
}

internal sealed class PresetThumbnail(PresetItem item) : Control
{
    private Avalonia.Media.Imaging.Bitmap? _bitmap;
    public override void Render(DrawingContext context)
    {
        context.DrawRectangle(Skin.SurfaceDarkBrush, new Pen(Skin.BorderControlBrush, 1), new Rect(Bounds.Size));
        if (item.Preset?.Contour is { } samples)
        {
            var geometry = new StreamGeometry(); using (var g = geometry.Open()) { g.BeginFigure(new Point(1, Bounds.Height - 1), true); for (var x = 0; x <= 64; x++) g.LineTo(new Point(1 + x / 64d * (Bounds.Width - 2), 1 + (1 - ContourLibrary.Evaluate(samples, x / 64d)) * (Bounds.Height - 2))); g.LineTo(new Point(Bounds.Width - 1, Bounds.Height - 1)); g.EndFigure(true); }
            context.DrawGeometry(Skin.LabelBrush, null, geometry); return;
        }
        if (item.Gradient is { } gradient)
        {
            var stops = new GradientStops(); foreach (var stop in gradient.Stops) stops.Add(new GradientStop(Color.FromUInt32(stop.Color), stop.Position));
            context.DrawRectangle(new LinearGradientBrush { StartPoint = new RelativePoint(0, .5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, .5, RelativeUnit.Relative), GradientStops = stops }, null, new Rect(1, 1, Bounds.Width - 2, Bounds.Height - 2)); return;
        }
        if (_bitmap is null && item.Preset is { } preset)
        {
            using var pixels = PresetThumbnailPixels.Render(preset); _bitmap = CanvasView.ToImage(pixels);
        }
        if (_bitmap is { } bitmap) context.DrawImage(bitmap, new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height), new Rect(2, 2, Bounds.Width - 4, Bounds.Height - 4));
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { _bitmap?.Dispose(); _bitmap = null; base.OnDetachedFromVisualTree(e); }
}
