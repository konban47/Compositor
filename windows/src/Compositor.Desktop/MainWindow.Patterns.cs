using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Compositor.Core.Document;
using Compositor.Core.IO;
using SkiaSharp;
namespace Compositor.Desktop;
public sealed partial class MainWindow
{
    private Guid? _selectedPattern;
    private int _patternDisplay = 1;
    private void OpenPatternPicker(Control anchor)
    {
        var popup = new ToolSettingsPopup { MinWidth = 300, MaxWidth = 410 };
        var search = new TextBox { PlaceholderText = Localize.Text("Search Patterns") }; popup.Children.Add(search);
        var content = new StackPanel { Spacing = 4 }; popup.Children.Add(new ScrollViewer { Content = content, MaxHeight = 300 });
        List<EditorPreset> presets;
        try { presets = EditorPresets.Load(EditorPresets.DefaultPath).ToList(); } catch (Exception error) { Say(error.Message); return; }
        var previews = new List<SKBitmap>();
        void Reload()
        {
            content.Children.Clear(); foreach (var preview in previews) preview.Dispose(); previews.Clear();
            foreach (var group in presets.Where(p => p.Kind == "Pattern" && p.Name.Contains(search.Text ?? "", StringComparison.CurrentCultureIgnoreCase)).GroupBy(p => p.Name.Contains(" / ") ? p.Name.Split(" / ")[0] : "Custom Patterns"))
            {
                var grid = new WrapPanel();
                foreach (var preset in group)
                {
                    var small = _patternDisplay is 1 or 3; var size = small ? 36 : 64; var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    if (_patternDisplay != 0) { var image = PaintBrushEngine.Decode(preset.Image)!; previews.Add(image); row.Children.Add(new PatternSwatch(() => image) { Width = size, Height = size }); }
                    if (_patternDisplay is 0 or 3 or 4) row.Children.Add(new TextBlock { Text = preset.Name, VerticalAlignment = VerticalAlignment.Center });
                    var button = new Button { Content = row, Padding = new Thickness(3), Margin = new Thickness(2), Tag = "pattern-preset", Background = _selectedPattern == preset.ID ? Skin.AccentBrush : Skin.SurfaceControlBrush }; ToolTip.SetTip(button, preset.Name);
                    button.Click += (_, _) => { _selectedPattern = preset.ID; _options.Brush.Pattern?.Dispose(); _options.Brush = _options.Brush with { Pattern = PaintBrushEngine.Decode(preset.Image) }; OptionsChanged(); Reload(); }; grid.Children.Add(button);
                }
                content.Children.Add(new Expander { Header = Localize.Text(group.Key), Content = grid, IsExpanded = true });
            }
        }
        void Save() => EditorPresets.Save(EditorPresets.DefaultPath, presets);
        EditorPreset? SelectedPattern() => presets.FirstOrDefault(p => p.ID == _selectedPattern);
        var settings = new Button { Content = "⚙", HorizontalAlignment = HorizontalAlignment.Right, Tag = "pattern-menu" }; popup.Children.Add(settings);
        var flyout = popup.Show(anchor);
        flyout.Closed += (_, _) => { foreach (var preview in previews) preview.Dispose(); previews.Clear(); };
        settings.Click += (_, _) =>
        {
            var menu = new ContextMenu();
            menu.Items.Add(Command("New Pattern…", () => { flyout.Hide(); _ = DefinePreset("Pattern"); }));
            var rename = Command("Rename Pattern…", async () =>
            {
                if (SelectedPattern() is not { } selected) return; var form = new FormDialog("Rename Pattern", 400, 250); var name = form.Text("Name", selected.Name);
                form.Validate = () => string.IsNullOrWhiteSpace(name.Text) || name.Text.Length > 200 ? "Enter a name (1–200 characters)." : null;
                if (await form.ShowDialog<bool>(this)) { presets[presets.IndexOf(selected)] = selected with { Name = name.Text!.Trim() }; Save(); Reload(); }
            }); rename.IsEnabled = SelectedPattern() is not null; menu.Items.Add(rename);
            var delete = Command("Delete Pattern", () => { if (SelectedPattern() is { } selected) { presets.Remove(selected); _selectedPattern = null; Save(); Reload(); } }); delete.IsEnabled = SelectedPattern() is not null; menu.Items.Add(delete); menu.Items.Add(new Separator());
            var labels = new[] { "Text Only", "Small Thumbnails", "Large Thumbnails", "Small List", "Large List" };
            for (var i = 0; i < labels.Length; i++) { var mode = i; var item = Command(labels[i], () => { _patternDisplay = mode; Reload(); }); item.ToggleType = MenuItemToggleType.CheckBox; item.IsChecked = i == _patternDisplay; menu.Items.Add(item); }
            menu.Items.Add(new Separator());
            menu.Items.Add(Command("Append Default Patterns", () => { var ids = presets.Select(p => p.ID).ToHashSet(); presets.AddRange(PatternLibrary.Defaults().Where(p => !ids.Contains(p.ID))); Save(); Reload(); }));
            menu.Items.Add(Command("Import Patterns…", async () =>
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { FileTypeFilter = [new FilePickerFileType("Patterns") { Patterns = ["*.json", "*.png", "*.jpg", "*.webp", "*.bmp"] }] });
                if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
                try
                {
                    if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)) { var ids = presets.Select(p => p.ID).ToHashSet(); presets.AddRange(EditorPresets.Load(path).Where(p => p.Kind == "Pattern" && !ids.Contains(p.ID))); }
                    else { if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Pattern exceeds 4 MB."); var data = Convert.ToBase64String(await File.ReadAllBytesAsync(path)); using var image = PaintBrushEngine.Decode(data); presets.Add(new EditorPreset(Guid.NewGuid(), Path.GetFileNameWithoutExtension(path), "Pattern", Image: data)); }
                    Save(); Reload();
                }
                catch (Exception error) { Say(error.Message); }
            }));
            var export = Command("Export Selected Pattern…", async () => { if (SelectedPattern() is not { } selected) return; var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = "Compositor-pattern.json", DefaultExtension = "json" }); if (file?.TryGetLocalPath() is { } path) try { EditorPresets.Save(path, [selected]); } catch (Exception error) { Say(error.Message); } }); export.IsEnabled = SelectedPattern() is not null; menu.Items.Add(export); menu.Open(settings);
        };
        search.TextChanged += (_, _) => Reload(); Reload();
    }
}
internal sealed class PatternSwatch(Func<SKBitmap?> get) : Control
{
    public override void Render(DrawingContext context)
    {
        var image = get(); const int count = 16;
        for (var y = 0; y < count; y++) for (var x = 0; x < count; x++)
        {
            var color = image?.GetPixel(x * image.Width / count, y * image.Height / count) ?? ((x / 4 + y / 4) % 2 == 0 ? SKColors.Black : SKColors.White);
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue)), null, new Rect(x * Bounds.Width / count, y * Bounds.Height / count, Bounds.Width / count + .1, Bounds.Height / count + .1));
        }
    }
}
