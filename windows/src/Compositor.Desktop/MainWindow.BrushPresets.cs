using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
namespace Compositor.Desktop;
public sealed partial class MainWindow
{
    private BrushSettingsEditor? _brushSettingsEditor;
    private void BuildBrushSettingsPanel(StackPanel body)
    {
        _brushSettingsEditor = new BrushSettingsEditor(() => _options.Brush, b => { _options.Brush = b; OptionsChanged(); }, () => _tool == Tool.MixerBrush,
            () => OpenBrushPicker(_brushSettingsEditor!), () => _ = SaveBrushPreset(), () => _ = ChooseBrushTexture()); body.Children.Add(_brushSettingsEditor);
        _utilityRefresh["Brush Settings"] = () => _brushSettingsEditor.Refresh(); Closed += (_, _) => _brushSettingsEditor.Dispose();
    }
    private async Task ChooseBrushTexture()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Load Texture"), FileTypeFilter = [new FilePickerFileType("Texture") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"] }] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        try { if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Brush texture exceeds 4 MB."); var data = Convert.ToBase64String(await File.ReadAllBytesAsync(path)); using var image = PaintBrushEngine.Decode(data); ChangePainting(p => p with { TextureImage = data, TextureEnabled = true }); }
        catch (Exception error) { Say(error.Message); }
    }
    private void OpenBrushPicker(Control anchor)
    {
        var popup = new ToolSettingsPopup { MinWidth = 380, MaxWidth = 430, Changed = OptionsChanged };
        if (_tool is Tool.ColorReplacement or Tool.BackgroundEraser)
        {
            popup.Number("Size", _options.Brush.Diameter, 1, 5000, n => _options.Brush = _options.Brush with { Diameter = n }, true);
            popup.Number("Hardness %", _options.Brush.Hardness * 100, 0, 100, n => _options.Brush = _options.Brush with { Hardness = n / 100 }, true);
            popup.Children.Add(new BrushTipAngleControl(() => _options.Brush, b => { _options.Brush = b; OptionsChanged(); }));
        }
        else
        {
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*"), ColumnSpacing = 16, Margin = new Thickness(0,0,0,10) };
            header.Children.Add(new BrushTipAngleControl(() => _options.Brush, b => { _options.Brush = b; OptionsChanged(); }) { Width=104, Height=104, VerticalAlignment=VerticalAlignment.Top });
            var numbers=new StackPanel { Spacing=4 }; Grid.SetColumn(numbers,1); header.Children.Add(numbers);
            void Amount(string title,double value,double minimum,double maximum,Action<double> set)
            {
                var row=new Grid { ColumnDefinitions=new ColumnDefinitions("*,90") }; row.Children.Add(new TextBlock {Text=Localize.Text(title), VerticalAlignment=VerticalAlignment.Center});
                var field=new NumericUpDown { Value=(decimal)value, Minimum=(decimal)minimum, Maximum=(decimal)maximum, ShowButtonSpinner=false, FormatString="0.##", Height=28 }; Grid.SetColumn(field,1); row.Children.Add(field); numbers.Children.Add(row);
                var range=new Slider { Minimum=minimum, Maximum=maximum, Value=value, Height=22 }; numbers.Children.Add(range);
                var loading=false; field.ValueChanged+=(_,_)=>{if(loading||field.Value is not {} v)return; loading=true;range.Value=(double)v;set((double)v);OptionsChanged();loading=false;};
                range.ValueChanged+=(_,_)=>field.Value=(decimal)range.Value;
            }
            Amount("Size",_options.Brush.Diameter,1,5000,v=>_options.Brush=_options.Brush with {Diameter=v});
            Amount("Hardness %",_options.Brush.Hardness*100,0,100,v=>_options.Brush=_options.Brush with {Hardness=v/100}); popup.Children.Add(header);
        }
        if (_tool is Tool.ColorReplacement or Tool.BackgroundEraser)
        {
            popup.Number("Spacing %", _options.Brush.SpacingFraction > 0 ? _options.Brush.SpacingFraction * 100 : 25, 1, 1000, n => _options.Brush = _options.Brush with { SpacingFraction = n / 100 }, true);
            popup.Number("Angle", _options.Brush.Angle, -180, 180, n => _options.Brush = _options.Brush with { Angle = n });
            popup.Number("Roundness %", _options.Brush.Roundness * 100, 1, 100, n => _options.Brush = _options.Brush with { Roundness = n / 100 });
            popup.Choice("Size Control", ["Off", "Pen Pressure"], _options.Brush.PressureSize ? 1 : 0, n => _options.Brush = _options.Brush with { PressureSize = n == 1 });
            popup.Choice("Tolerance Control", ["Off", "Pen Pressure"], PaintingOptions.ToleranceControl == BrushControl.PenPressure ? 1 : 0, n => ChangePainting(p => p with { ToleranceControl = n == 1 ? BrushControl.PenPressure : BrushControl.Off }));
        }
        else
        {
            var search = new TextBox { PlaceholderText = Localize.Text("Search Brushes"), Tag = "brush-search" }; popup.Children.Add(search);
            var content = new StackPanel { Spacing = 4 }; var scroll = new ScrollViewer { Content = content, MaxHeight = 380 }; popup.Children.Add(scroll);
            EditorPreset[] presets; try { presets = EditorPresets.Load(EditorPresets.DefaultPath).Where(p => p.Kind == "Brush").ToArray(); } catch (Exception error) { Say(error.Message); presets = []; }
            void Reload()
            {
                content.Children.Clear(); var query = search.Text ?? "";
                foreach (var group in presets.GroupBy(p => BrushPresetLibrary.All.FirstOrDefault(b => b.Name == p.Name)?.Category ?? "Custom Brushes"))
                {
                    var grid = new WrapPanel { Tag = "brush-stroke-grid" };
                    foreach (var preset in group.Where(p => Localize.Text(p.Name).Contains(query, StringComparison.CurrentCultureIgnoreCase)))
                    {
                        var card = new StackPanel { Spacing = 2 };
                        card.Children.Add(new PresetThumbnail(PresetCatalog.Item(preset)) { Width = 160, Height = 45 });
                        card.Children.Add(new TextBlock { Text = Localize.Text(preset.Name), FontSize = 11, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 160 });
                        var builtin = BrushPresetLibrary.All.ToList().FindIndex(b => b.Name == preset.Name);
                        var button = new Button { Width = 174, MinHeight = 70, Padding = new Thickness(5), Margin = new Thickness(1), Content = card, Tag = builtin >= 0 ? "brush-picker-tip-" + builtin : "brush-user-preset", VerticalContentAlignment = VerticalAlignment.Top };
                        ToolTip.SetTip(button, Localize.Text(preset.Name));
                        button.Click += (_, _) => ApplyBrushPreset(preset); grid.Children.Add(button);
                    }
                    if (grid.Children.Count > 0) content.Children.Add(new Expander { Header = Localize.Text(group.Key), IsExpanded = true, Content = grid, HorizontalAlignment = HorizontalAlignment.Stretch });
                }
            }
            search.TextChanged += (_, _) => Reload(); Reload();
            var size = new Slider { Minimum = 140, Maximum = 174, Value = 174 }; ToolTip.SetTip(size, Localize.Text("Thumbnail Size"));
            size.ValueChanged += (_, _) => { foreach (var expander in content.Children.OfType<Expander>()) if (expander.Content is WrapPanel grid) foreach (var button in grid.Children.OfType<Button>()) { button.Width = size.Value; if (button.Content is StackPanel card) { card.Children[0].Width = size.Value - 14; card.Children[1].MaxWidth = size.Value - 14; } } }; popup.Children.Add(size);
        }
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var add = new Button { Content = "+", Tag = "new-brush-preset" }; add.Click += async (_, _) => await SaveBrushPreset(); footer.Children.Add(add);
        var settings = new Button { Content = Localize.Text("Brush Settings") }; settings.Click += (_, _) => OpenUtilityPanel("Brush Settings"); footer.Children.Add(settings);
        var manage = new Button { Content = Localize.Text("Preset Manager…") }; manage.Click += async (_, _) => await PresetManager(); footer.Children.Add(manage); popup.Children.Add(footer); popup.Show(anchor);
    }
    private async Task SaveBrushPreset()
    {
        var form = new FormDialog("New Brush", 490, 330); var name = form.Text("Name", Localize.Text(BrushDabs.TipNames[PaintingOptions.Tip]) + " " + _options.Brush.Diameter.ToString("0"));
        var size = form.Check("Capture Brush Size in Preset", false); var tool = form.Check("Include Tool Settings", true); var color = form.Check("Include Color", false);
        if (!await form.ShowDialog<bool>(this) || string.IsNullOrWhiteSpace(name.Text)) return;
        try
        {
            var brush = _options.Brush with { History = null, Pattern = null, TipImage = null, CloneDocument = null, CloneMapping = null, Pressures = null, Painting = PaintingOptions with { Samples = null, Reservoir = null } };
            var preset = new EditorPreset(Guid.NewGuid(), name.Text.Trim(), "Brush", brush.Diameter, brush.Hardness, brush.Opacity, ToolPresetState.Encode(_options.Brush.TipImage), Brush: brush, CaptureSize: size.IsChecked == true, IncludeTool: tool.IsChecked == true, IncludeColor: color.IsChecked == true, Tool: PaintingTool ? _tool.ToString() : "Brush");
            EditorPresets.Save(EditorPresets.DefaultPath, EditorPresets.Load(EditorPresets.DefaultPath).Append(preset)); Say("Brush preset saved.");
        }
        catch (Exception error) { Say(error.Message); }
    }
    private void ApplyBrushPreset(EditorPreset preset)
    {
        var old = _options.Brush; var current = PaintingOptions;
        var next = preset.Brush ?? old with { Diameter = preset.Diameter, Hardness = preset.Hardness, Opacity = preset.Opacity };
        var parameters = (next.Painting ?? new()) with { };
        if (!preset.IncludeTool && preset.Brush is not null)
        {
            next = old with { Diameter = next.Diameter, Hardness = next.Hardness, Angle = next.Angle, Roundness = next.Roundness, SpacingFraction = next.SpacingFraction };
            var captured = parameters; parameters = current with { };
            foreach (var field in BrushSettingsEditor.Fields.SelectMany(f => f).Concat(BrushSettingsEditor.EnabledFields).Where(f => f.Length > 0).Distinct())
            { var property = typeof(PaintBrushOptions).GetProperty(field)!; property.SetValue(parameters, property.GetValue(captured)); }
        }
        if (!preset.CaptureSize) next = next with { Diameter = old.Diameter };
        if (!preset.IncludeColor) next = next with { Red = old.Red, Green = old.Green, Blue = old.Blue };
        for (var i = 0; i < BrushSettingsEditor.Sections.Length; i++)
        {
            if ((current.LockedSections & (1UL << i)) == 0 && !(i == 3 && current.ProtectTexture)) continue;
            foreach (var field in BrushSettingsEditor.Fields[i].Append(BrushSettingsEditor.EnabledFields[i]).Where(s => s.Length > 0)) { var property = typeof(PaintBrushOptions).GetProperty(field)!; property.SetValue(parameters, property.GetValue(current)); }
            if (i == 0) next = next with { Diameter = old.Diameter, Hardness = old.Hardness, Angle = old.Angle, Roundness = old.Roundness, SpacingFraction = old.SpacingFraction };
        }
        parameters = parameters with { LockedSections = current.LockedSections, ProtectTexture = current.ProtectTexture, Samples = null, Reservoir = null };
        _options.Brush = next with { Painting = parameters, TipImage = (current.LockedSections & 1) != 0 ? old.TipImage : PaintBrushEngine.Decode(preset.Image) };
        if (preset.IncludeTool && Enum.TryParse<Tool>(preset.Tool, out var tool)) SetTool(tool); else if (!PaintingTool) SetTool(Tool.Brush);
        OptionsChanged(); _brushSettingsEditor?.SelectSection(0);
    }
}
