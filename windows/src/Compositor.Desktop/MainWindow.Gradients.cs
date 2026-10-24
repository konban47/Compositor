using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;
namespace Compositor.Desktop;
public sealed partial class MainWindow
{
    private Guid? _gradientPreviewID, _loadedGradient, _selectedGradientPreset;
    private int _gradientDisplay = 1;
    private LiveGradientSettings? _loadedGradientSettings;
    private bool ModernGradient => !_options.GradientClassic && !_options.PaintOnMask && _open.ActiveAlpha is null;
    private ImageLayer MakeGradientLayer(CanvasDocument doc, Guid selected, Guid? id = null)
    {
        var previous = doc.Layers.First(l => l.ID == selected);
        var layer = new ImageLayer(id ?? Guid.NewGuid(), null, new Compositor.Core.Model.LayerTransform(0, 0, doc.Width, doc.Height), Localize.Text("Gradient")) { ParentID = previous.ParentID };
        using var clip = doc.Selection.Coverage(SKRectI.Create(doc.Width, doc.Height));
        if (clip is not null) layer.Mask = LayerMask.AssetFrom(Bitmaps.Copy(clip));
        doc.Layers.Insert(doc.Layers.IndexOf(previous) + 1, layer); return layer;
    }
    private bool ApplyGradient(CanvasDocument doc, Guid id, SKPoint start, SKPoint end, bool modern)
    {
        var settings = _optionsBar.GradientSettings;
        if (!modern) return GradientPaint.Fill(doc, id, _options.PaintOnMask, start, end, settings, _options.Brush.Opacity, PaintingOptions.Blend);
        var layer = doc.Layers.First(l => l.ID == id);
        var map = BrushEdits.PixelToDocument(layer.Transform, layer.Asset?.Width ?? doc.Width, layer.Asset?.Height ?? doc.Height); if (!map.TryInvert(out var inverse)) return false;
        var a = inverse.MapPoint(start); var b = inverse.MapPoint(end);
        return GradientPaint.SetLive(doc, layer, new(a.X, a.Y, b.X, b.Y, settings));
    }
    private void BeginGradientDrag()
    {
        _gradientPreviewID = null;
        if (_document is not { } doc || Selected is not { } id) return;
        if (ModernGradient && doc.Layers.First(l => l.ID == id).LiveGradient is null)
        { using var draft = doc.Clone(); var layer = MakeGradientLayer(draft, id); _gradientPreviewID = layer.ID; StartPreview(draft, layer.ID); }
        else StartPreview(doc, id);
    }
    private void PreviewGradientDrag(SKPoint start, SKPoint end)
    {
        if (!GradientEdits.HasLine(start, end)) return;
        var modern = ModernGradient;
        RequestPreview((target, layer) => ApplyGradient(target, layer, start, end, modern));
    }
    private void FinishGradientDrag(SKPoint start, SKPoint end)
    {
        StopPreview(); if (_document is not { } doc || Selected is not { } id || !GradientEdits.HasLine(start, end)) return;
        if (EditActiveAlpha("Gradient Channel", (target, channel) => GradientPaint.Fill(target, channel, false, start, end, _optionsBar.GradientSettings, _options.Brush.Opacity, PaintingOptions.Blend))) return;
        var modern = ModernGradient; var target = id;
        Edit(modern ? "Editable Gradient" : "Classic Gradient", () =>
        {
            if (modern && doc.Layers.First(l => l.ID == id).LiveGradient is null) target = MakeGradientLayer(doc, id, _gradientPreviewID).ID;
            return ApplyGradient(doc, target, start, end, modern);
        });
        _gradientPreviewID = null; _loadedGradient = null; Reselect(target); SyncGradientOptions();
    }
    private void SyncGradientOptions()
    {
        _canvas.GradientGuide = null;
        if (_tool != Tool.Gradient || _options.GradientClassic || _options.PaintOnMask || _document?.Layers.FirstOrDefault(l => l.ID == Selected) is not { LiveGradient: { } gradient } layer)
        { _loadedGradient = null; return; }
        if (_loadedGradient != layer.ID || !ReferenceEquals(_loadedGradientSettings, gradient))
        {
            _loadedGradient = layer.ID; _loadedGradientSettings = gradient; _options.Gradient = gradient.Paint.Shape; _options.GradientMethod = gradient.Paint.Method; _options.GradientDither = gradient.Paint.Dither;
            _options.GradientReversed = gradient.Paint.Reverse; _options.GradientStops = gradient.Paint.Stops.ToArray();
        }
        var map = BrushEdits.PixelToDocument(layer.Transform, layer.Asset!.Width, layer.Asset.Height);
        _canvas.GradientGuide = (map.MapPoint((float)gradient.StartX, (float)gradient.StartY), map.MapPoint((float)gradient.EndX, (float)gradient.EndY));
        _canvas.InvalidateOverlay();
    }
    private void UpdateLiveGradient()
    {
        if (_tool != Tool.Gradient || !ModernGradient || _document is not { } doc || doc.Layers.FirstOrDefault(l => l.ID == Selected) is not { LiveGradient: { } gradient } layer) return;
        var settings = _optionsBar.GradientSettings;
        if (settings.Shape == gradient.Paint.Shape && settings.Method == gradient.Paint.Method && settings.Dither == gradient.Paint.Dither && settings.Reverse == gradient.Paint.Reverse && settings.Stops.SequenceEqual(gradient.Paint.Stops)) return;
        Edit("Edit Gradient", () => GradientPaint.SetLive(doc, layer, gradient with { Paint = settings })); SyncGradientOptions();
    }
    private async Task EditGradientStops()
    {
        var stops = _optionsBar.GradientSettings.Stops.ToList(); var form = new FormDialog("Gradient Editor", 530, 560);
        var preview = new GradientStrip(() => _optionsBar.GradientSettings with { Stops = stops.OrderBy(s => s.Position).ToArray() }) { Height = 44 };
        form.Fields.Children.Add(preview); var rows = new StackPanel { Spacing = 8 }; form.Fields.Children.Add(rows);
        void Reload()
        {
            rows.Children.Clear();
            for (var i = 0; i < stops.Count; i++)
            {
                var index = i; var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                var position = new NumericUpDown { Minimum = 0, Maximum = 100, Value = (decimal)(stops[i].Position * 100), Width = 82, ShowButtonSpinner = false }; ToolTip.SetTip(position, Localize.Text("Position %"));
                position.ValueChanged += (_, _) => { stops[index] = stops[index] with { Position = (double)(position.Value ?? 0) / 100 }; preview.InvalidateVisual(); };
                var c = new SKColor(stops[i].Color); var swatch = new Button { Width = 75, Height = 30, Background = new SolidColorBrush(Color.FromRgb(c.Red, c.Green, c.Blue)), Content = Localize.Text("Color") };
                swatch.Click += async (_, _) => await ColorPickerDialog.Pick(form, "Gradient Stop Color", (new SKColor(stops[index].Color).Red / 255d, new SKColor(stops[index].Color).Green / 255d, new SKColor(stops[index].Color).Blue / 255d), value => { var old = new SKColor(stops[index].Color); var next = new SKColor((byte)(value.Red * 255), (byte)(value.Green * 255), (byte)(value.Blue * 255), old.Alpha); stops[index] = stops[index] with { Color = (uint)next }; swatch.Background = new SolidColorBrush(Color.FromRgb(next.Red, next.Green, next.Blue)); preview.InvalidateVisual(); });
                var opacity = new NumericUpDown { Minimum = 0, Maximum = 100, Value = (decimal)(c.Alpha / 2.55), Width = 82, ShowButtonSpinner = false }; ToolTip.SetTip(opacity, Localize.Text("Opacity %"));
                opacity.ValueChanged += (_, _) => { stops[index] = stops[index] with { Color = (uint)new SKColor(stops[index].Color).WithAlpha((byte)((double)(opacity.Value ?? 0) * 2.55)) }; preview.InvalidateVisual(); };
                var remove = FormDialog.ActionButton("Delete", () => { stops.RemoveAt(index); Reload(); preview.InvalidateVisual(); }); remove.IsEnabled = stops.Count > 2;
                row.Children.Add(position); row.Children.Add(swatch); row.Children.Add(opacity); row.Children.Add(remove); rows.Children.Add(row);
            }
        }
        form.Fields.Children.Add(FormDialog.ActionButton("Add Color Stop", () => { if (stops.Count < 64) { var c = _optionsBar.GradientSettings.Sample(.5); stops.Add(new(.5, (uint)new SKColor((byte)(c.Red * 255), (byte)(c.Green * 255), (byte)(c.Blue * 255)))); Reload(); } }));
        Reload(); if (await form.ShowDialog<bool>(this)) { _options.GradientStops = stops.OrderBy(s => s.Position).ToArray(); OptionsChanged(); }
    }
    private void OpenGradientPicker(Control anchor)
    {
        var presets = GradientLibrary.Load(GradientLibrary.DefaultPath); var popup = new ToolSettingsPopup { MinWidth = 350, MaxWidth = 430 };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; var gear = FormDialog.ActionButton("Preset Options", () => { }); var add = FormDialog.ActionButton("+", () => { }); top.Children.Add(gear); top.Children.Add(add); popup.Children.Add(top);
        var body = new StackPanel { Spacing = 6 }; popup.Children.Add(new ScrollViewer { Content = body, MaxHeight = 300 });
        void Save() { try { GradientLibrary.Save(GradientLibrary.DefaultPath, presets); } catch (Exception error) { Say(error.Message); } }
        async Task Create()
        { var form = new FormDialog("Gradient Name", 490, 320); form.Fields.Children.Add(new GradientStrip(() => _optionsBar.GradientSettings) { Height = 36 }); var name = form.Text("Name", Localize.Text("Custom Gradient")); form.Validate = () => string.IsNullOrWhiteSpace(name.Text) ? "Enter a name." : null; if (await form.ShowDialog<bool>(this)) { var preset = new GradientPreset(Guid.NewGuid(), name.Text!.Trim(), "Custom", _optionsBar.GradientSettings.Stops.ToArray()); presets.Add(preset); _selectedGradientPreset = preset.ID; Save(); Reload(); } }
        void Reload()
        {
            body.Children.Clear();
            foreach (var (name, background) in new[] { ("Foreground to Background", true), ("Foreground to Transparent", false) })
            { var button = new Button { Content = Localize.Text(name), HorizontalAlignment = HorizontalAlignment.Stretch }; button.Click += (_, _) => { _options.GradientStops = null; _options.GradientToBackground = background; OptionsChanged(); }; body.Children.Add(button); }
            foreach (var group in presets.GroupBy(p => p.Category))
            {
                var grid = new WrapPanel(); foreach (var preset in group)
                {
                    var content = new StackPanel { Spacing = 3 };
                    if (_gradientDisplay != 0) content.Children.Add(new GradientStrip(() => _optionsBar.GradientSettings with { Stops = preset.Stops, Reverse = false }) { Width = _gradientDisplay is 2 or 4 ? 140 : 80, Height = _gradientDisplay is 2 or 4 ? 35 : 22 });
                    if (_gradientDisplay is 0 or 3 or 4) content.Children.Add(new TextBlock { Text = Localize.Text(preset.Name), FontSize = 11 });
                    var button = new Button { Content = content, Margin = new Thickness(2), Padding = new Thickness(4), Background = _selectedGradientPreset == preset.ID ? Skin.SurfaceDarkBrush : Brushes.Transparent }; ToolTip.SetTip(button, Localize.Text(preset.Name));
                    button.Click += (_, _) => { _selectedGradientPreset = preset.ID; _options.GradientStops = preset.Stops.ToArray(); OptionsChanged(); Reload(); }; grid.Children.Add(button);
                }
                body.Children.Add(new Expander { Header = Localize.Text(group.Key), Content = grid, IsExpanded = true, HorizontalAlignment = HorizontalAlignment.Stretch });
            }
        }
        add.Click += async (_, _) => await Create(); gear.Click += (_, _) =>
        {
            var menu = new ContextMenu(); var selected = presets.FirstOrDefault(p => p.ID == _selectedGradientPreset);
            menu.Items.Add(Command("New Gradient Preset…", async () => await Create()));
            var rename = Command("Rename Gradient…", async () => { if (selected is null) return; var form = new FormDialog("Gradient Name", 470, 320); var name = form.Text("Name", selected.Name); form.Validate = () => string.IsNullOrWhiteSpace(name.Text) ? "Enter a name." : null; if (await form.ShowDialog<bool>(this)) { presets[presets.IndexOf(selected)] = selected with { Name = name.Text!.Trim() }; Save(); Reload(); } }); rename.IsEnabled = selected is not null; menu.Items.Add(rename);
            var delete = Command("Delete Gradient", () => { if (selected is not null) { presets.Remove(selected); _selectedGradientPreset = null; Save(); Reload(); } }); delete.IsEnabled = selected is not null; menu.Items.Add(delete); menu.Items.Add(new Separator());
            var displays = new[] { "Text Only", "Small Thumbnails", "Large Thumbnails", "Small List", "Large List" };
            for (var i = 0; i < displays.Length; i++) { var index = i; var item = Command(displays[i], () => { _gradientDisplay = index; Reload(); }); item.ToggleType = MenuItemToggleType.CheckBox; item.IsChecked = i == _gradientDisplay; menu.Items.Add(item); }
            menu.Items.Add(new Separator()); menu.Items.Add(Command("Append Default Gradients", () => { var ids = presets.Select(p => p.ID).ToHashSet(); presets.AddRange(GradientLibrary.Defaults().Where(p => !ids.Contains(p.ID))); Save(); Reload(); }));
            menu.Items.Add(Command("Import Gradients…", async () => { var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { FileTypeFilter = [new FilePickerFileType("Compositor Gradients") { Patterns = ["*.json"] }] }); if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) try { var ids = presets.Select(p => p.ID).ToHashSet(); presets.AddRange(GradientLibrary.Load(path).Where(p => !ids.Contains(p.ID))); Save(); Reload(); } catch (Exception error) { Say(error.Message); } }));
            var export = Command("Export Selected Gradient…", async () => { if (selected is null) return; var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = "Compositor-gradient.json", DefaultExtension = "json" }); if (file?.TryGetLocalPath() is { } path) try { GradientLibrary.Save(path, [selected]); } catch (Exception error) { Say(error.Message); } }); export.IsEnabled = selected is not null; menu.Items.Add(export); menu.Open(gear);
        };
        Reload(); popup.Show(anchor);
    }
}
