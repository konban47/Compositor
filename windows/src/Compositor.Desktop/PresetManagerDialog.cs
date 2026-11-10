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
        _kind.ItemsSource = PresetCatalog.Kinds;
        _kind.ItemTemplate = new FuncDataTemplate<string>((k, _) => new Grid { ColumnDefinitions = new("*,Auto"), MinWidth = 150, Children = { new TextBlock { Text = Localize.Text(Plural(k ?? "")) }, new TextBlock { Text = "Ctrl+" + (Array.IndexOf(PresetCatalog.Kinds, k) + 1), [Grid.ColumnProperty] = 1, Foreground = Skin.SecondaryBrush, Margin = new Thickness(18, 0, 0, 0) } } });
        _kind.SelectionBoxItemTemplate = new FuncDataTemplate<string>((k, _) => new TextBlock { Text = Localize.Text(Plural(k ?? "")) }); _kind.SelectedIndex = Math.Max(0, Array.IndexOf(PresetCatalog.Kinds, kind));
        Grid.SetColumn(_kind, 1); header.Children.Add(_kind);
        var menu = new Button { Tag = "preset-manager-menu", Background = Brushes.Transparent, Padding = new Thickness(4, 2), Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(GearIcon), Fill = Skin.LabelBrush, Width = 16, Height = 16, Stretch = Stretch.Uniform }, new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M0,0 L6,0 L3,3 Z"), Fill = Skin.LabelBrush, Width = 6, Height = 3, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Bottom } } } };
        ToolTip.SetTip(menu, Localize.Text("Preset Manager options")); Grid.SetColumn(menu, 2); header.Children.Add(menu); menu.Click += (_, _) => SettingsMenu(menu);
        left.Children.Add(header); Grid.SetRow(_list, 1); left.Children.Add(_list);
        root.Children.Add(new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Child = left });
        var actions = new StackPanel { Spacing = 16, Margin = new Thickness(24, 8, 0, 0) }; Grid.SetColumn(actions, 1); root.Children.Add(actions);
        Button Action(string title, Action handler, char? key = null) { var button = FormDialog.ActionButton(title, handler); if (key is { } letter) this.Mnemonic(button, letter); button.HorizontalAlignment = HorizontalAlignment.Stretch; actions.Children.Add(button); return button; }
        Action("Done", () => Close());
        var load = Action("Load…", () => _ = Import(false), 'L'); load.Margin = new Thickness(0, 20, 0, 0);
        _save = Action("Save Set…", () => _ = Export(), 'S');
        _rename = Action("Rename…", () => _ = Rename(), 'R'); _rename.Margin = new Thickness(0, 20, 0, 0);
        _delete = Action("Delete", Delete, 'D');
        Grid.SetRow(_error, 1); Grid.SetColumnSpan(_error, 2); root.Children.Add(_error); Content = root;
        _list.SelectionChanged += (_, _) => UpdateActions();
        _list.Styles.Add(new Style(s => s.OfType<ListBoxItem>()) { Setters = { new Setter(PaddingProperty, new Thickness(2)), new Setter(MarginProperty, new Thickness(0)), new Setter(MinHeightProperty, 0d) } });
        _kind.SelectionChanged += (_, _) => { _error.Text = ""; Load(); };
        _list.KeyDown += (_, e) => { if (e.Key == Key.Delete) { Delete(); e.Handled = true; } if (e.Key == Key.F2) { _ = Rename(); e.Handled = true; } };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            // Ctrl+1 to Ctrl+5 switch the preset type, as the type menu shows.
            if (e.KeyModifiers == KeyModifiers.Control && e.Key is >= Key.D1 and <= Key.D9 && e.Key - Key.D1 < PresetCatalog.Kinds.Length) { _kind.SelectedIndex = e.Key - Key.D1; e.Handled = true; }
        };
        Load();
    }
    private const string GearIcon = "M10.3,1 H13.7 L14.2,3.6 A8.5,8.5 0 0 1 16.4,4.9 L18.9,4 L20.6,7 L18.6,8.7 A8.5,8.5 0 0 1 18.6,11.3 L20.6,13 L18.9,16 L16.4,15.1 A8.5,8.5 0 0 1 14.2,16.4 L13.7,19 H10.3 L9.8,16.4 A8.5,8.5 0 0 1 7.6,15.1 L5.1,16 L3.4,13 L5.4,11.3 A8.5,8.5 0 0 1 5.4,8.7 L3.4,7 L5.1,4 L7.6,4.9 A8.5,8.5 0 0 1 9.8,3.6 Z M12,7 A3,3 0 1 0 12.01,7 Z";
    internal static string Plural(string kind) => kind switch { "Brush" => "Brushes", "Pattern" => "Patterns", "Gradient" => "Gradients", "Shape" => "Shapes", _ => "Contours" };
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
        // The name dialog Photoshop shows for the type, with the preset drawn beside the name.
        using var thumbnail = Thumbnail(item);
        var dialog = new PresetNameDialog(Kind switch { "Brush" => "Brush Name", "Pattern" => "Pattern Name", "Gradient" => "Gradient Name", "Shape" => "Shape Name", _ => "Contour Name" }, item.Name, thumbnail);
        if (await dialog.ShowDialog<bool>(this)) Commit(_items.Select(p => p.ID == item.ID ? p.Rename(dialog.PresetName) : p).ToList(), [item.ID]);
    }
    /// <summary>Puts the default presets back, after asking whether they replace the current ones or are appended to them.</summary>
    private async Task Reset()
    {
        var ask = new FormDialog("Preset Manager", 480, 200); ask.Description(string.Format(Localize.Text("Replace current {0} with the default {0}?"), Localize.Text(Plural(Kind)).ToLower(System.Globalization.CultureInfo.CurrentCulture)));
        var append = false; ask.Fields.Children.Add(FormDialog.ActionButton("Append", () => { append = true; ask.Close(true); }));
        if (!await ask.ShowDialog<bool>(this)) return;
        Commit(append ? PresetCatalog.Append(_items, PresetCatalog.Defaults(Kind)) : PresetCatalog.Defaults(Kind));
    }
    private static SkiaSharp.SKBitmap? Thumbnail(PresetItem item)
    {
        var bitmap = new SkiaSharp.SKBitmap(64, 64); using var canvas = new SkiaSharp.SKCanvas(bitmap); canvas.Clear(new SkiaSharp.SKColor(0x30, 0x30, 0x30));
        if (item.Preset?.Contour is { } samples)
        {
            using var path = SkiaSharp.SKPath.ParseSvgPathData("M0,64 " + string.Join(" ", Enumerable.Range(0, 65).Select(x => FormattableString.Invariant($"L{x},{64 * (1 - ContourLibrary.Evaluate(samples, x / 64d)):0.##}"))) + " L64,64 Z");
            using var paint = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.White, IsAntialias = true }; canvas.DrawPath(path, paint); return bitmap;
        }
        if (item.Gradient is { } gradient)
        {
            using var shader = SkiaSharp.SKShader.CreateLinearGradient(new(0, 32), new(64, 32), gradient.Stops.Select(s => new SkiaSharp.SKColor(s.Color)).ToArray(), gradient.Stops.Select(s => (float)s.Position).ToArray(), SkiaSharp.SKShaderTileMode.Clamp);
            using var paint = new SkiaSharp.SKPaint { Shader = shader }; canvas.DrawRect(0, 0, 64, 64, paint); return bitmap;
        }
        if (item.Preset is { } preset) { bitmap.Dispose(); try { return PresetThumbnailPixels.Render(preset); } catch (InvalidDataException) { return null; } }
        return bitmap;
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
        Action("Reset " + Plural(Kind) + "…", () => _ = Reset());
        Action("Replace " + Plural(Kind) + "…", () => _ = Import(true));
        menu.Items.Add(new Separator()); Action(Plural(Kind), () => _ = Reset());
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
