using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Compositor.Core.Document;
using Compositor.Core.IO;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private void ResetToolOptions(bool all)
    {
        if (all) { _optionsBar.Professional.Reset(); ApplyToolPreset(new() { Tool = _tool }); return; }
        var preset = CaptureToolPreset("Reset"); var defaults = new ToolOptions(); var p = _optionsBar.Professional;
        if (_tool == Tool.Move) { preset.AutoSelect = true; preset.SelectGroup = false; preset.TransformControls = true; }
        else if (_tool == Tool.Artboard) preset.Artboard = new();
        else if (ToolCatalog.HasRoundCursor(_tool)) { preset.Options.Brush = defaults.Brush; preset.Options.Erase = false; preset.Options.PaintOnMask = false; preset.Pattern = preset.Tip = null; }
        else if (_tool is Tool.Wand or Tool.Object or Tool.Bucket or Tool.MagicEraser) { preset.Options.Wand = defaults.Wand; preset.Options.WandAllLayers = false; }
        else if (_tool == Tool.Gradient) { preset.Options.Gradient = defaults.Gradient; preset.Options.GradientReversed = false; preset.Options.GradientToBackground = false; }
        else if (ToolCatalog.IsShape(_tool)) { preset.Options.ShapeSmoothing = false; preset.Options.ShapeCornerRadius = 0; preset.Options.ShapeLineWidth = 4; }
        else if (_tool == Tool.Eyedropper) preset.Options.ShowsSampleRing = true;
        else preset.Options.SelectionAntialiased = true;
        if (_tool is Tool.Marquee or Tool.Ellipse or Tool.SingleRow or Tool.SingleColumn or Tool.Lasso or Tool.Polygon or Tool.MagneticLasso or Tool.SelectionBrush or Tool.QuickSelection or Tool.Object or Tool.Wand) preset.Options.Selection = new();
        if (_tool is Tool.Crop or Tool.PerspectiveCrop or Tool.Slice or Tool.SliceSelect) preset.Options.Crop = new();
        if (_tool == Tool.MagneticLasso) { p.MagneticWidth = 12; p.MagneticContrast = 20; p.MagneticFrequency = 4; }
        if (_tool == Tool.PerspectiveCrop) p.CropWidth = p.CropHeight = 0;
        if (_tool == Tool.Frame) p.EllipseFrame = false;
        if (_tool == Tool.ContentMove) p.ExtendMove = false;
        if (_tool == Tool.Patch) p.PatchDestination = false;
        if (_tool == Tool.RedEye) { p.Pupil = .8; p.Darken = .5; }
        if (_tool == Tool.Count) p.CountGroup = "1";
        if (_tool is Tool.SelectionBrush or Tool.QuickSelection) p.SelectionMode = Compositor.Core.Document.SelectionPaintMode.Add;
        preset.Professional = ToolPresetState.CaptureProfessional(p); ApplyToolPreset(preset); _optionsBar.RefreshProfessional();
    }
    private ToolPresetState CaptureToolPreset(string name)
    {
        var options = new ToolOptions(); ToolPresetState.CopyOptions(_options, options);
        options.Brush = options.Brush with { History = null, Pattern = null, TipImage = null };
        return new ToolPresetState { Name = name, Tool = _tool, Options = options,
            Artboard = new() { Width = _optionsBar.Artboard.Width, Height = _optionsBar.Artboard.Height, Background = _optionsBar.Artboard.Background },
            Professional = ToolPresetState.CaptureProfessional(_optionsBar.Professional), AutoSelect = EditorPreferences.Current.MoveAutoSelect,
            SelectGroup = EditorPreferences.Current.MoveSelectGroup, TransformControls = _transformShown,
            Pattern = ToolPresetState.Encode(_options.Brush.Pattern), Tip = ToolPresetState.Encode(_options.Brush.TipImage) };
    }
    private void ApplyToolPreset(ToolPresetState preset)
    {
        var pattern = ToolPresetState.Decode(preset.Pattern); var tip = ToolPresetState.Decode(preset.Tip);
        FinishPen();
        ToolPresetState.CopyOptions(preset.Options, _options); _options.Brush = _options.Brush with { Pattern = pattern, TipImage = tip };
        preset.ApplyProfessional(_optionsBar.Professional);
        _optionsBar.Artboard.Width = preset.Artboard.Width; _optionsBar.Artboard.Height = preset.Artboard.Height; _optionsBar.Artboard.Background = preset.Artboard.Background;
        EditorPreferences.Current.MoveAutoSelect = preset.AutoSelect; EditorPreferences.Current.MoveSelectGroup = preset.SelectGroup;
        if (_transformShown != preset.TransformControls) ShowTransformControls(); SetTool(preset.Tool); OptionsChanged(); _optionsBar.RefreshProfessional(); SavePreferenceChanges();
    }
    private void OpenToolPresets(Control anchor, bool manager = false)
    {
        List<ToolPresetState> presets;
        try { presets = ToolPresetState.Read(ToolPresetState.FilePath); } catch (Exception error) { Say(error.Message); return; }
        ToolPresetState? selected = null; var p = EditorPreferences.Current;
        var list = new ListBox { Height = 210, Width = 300, Tag = "tool-preset-list" };
        var only = new CheckBox { Content = Localize.Text("Current Tool Only"), IsChecked = p.PresetCurrentOnly };
        var body = new DockPanel { Width = 340, Margin = new Thickness(7) };
        var buttons = new StackPanel { Spacing = 5 }; DockPanel.SetDock(buttons, Dock.Right); body.Children.Add(buttons);
        var gear = ToolOptionsBar.IconButton("settings", "Tool Preset Options", null); buttons.Children.Add(gear);
        var add = ToolOptionsBar.IconButton("plus", "New Tool Preset…", null); buttons.Children.Add(add);
        DockPanel.SetDock(only, Dock.Bottom); body.Children.Add(only); body.Children.Add(list);
        var popup = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        Window? managerWindow = null;
        void Hide() { popup.Hide(); managerWindow?.Close(); }
        void Reload()
        {
            var values = presets.Where(v => manager || !p.PresetCurrentOnly || v.Tool == _tool);
            if (p.PresetSortByTool) values = values.OrderBy(v => ToolCatalog.Title(v.Tool)).ThenBy(v => v.Name);
            list.Items.Clear();
            foreach (var value in values)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Height = p.PresetListSize == 2 ? 42 : 24 };
                if (p.PresetListSize != 0) row.Children.Add(ToolRail.Icon(value.Tool));
                row.Children.Add(new TextBlock { Text = value.Name, VerticalAlignment = VerticalAlignment.Center });
                list.Items.Add(new ListBoxItem { Content = row, Tag = value });
            }
            if (list.ItemCount == 0) list.Items.Add(new ListBoxItem { Content = Localize.Text("No tool presets are defined for the current tool."), IsEnabled = false });
        }
        void Save() { ToolPresetState.Write(ToolPresetState.FilePath, presets); Reload(); }
        async Task New()
        {
            if (await TextPrompt.Ask(this, "New Tool Preset", "Name", Localize.Text(ToolCatalog.Title(_tool))) is not { Length: > 0 and <= 200 } name) return;
            presets.Add(CaptureToolPreset(name)); Save();
        }
        add.Click += async (_, _) => { Hide(); await New(); };
        only.IsCheckedChanged += (_, _) => { p.PresetCurrentOnly = only.IsChecked == true; SavePreferenceChanges(); Reload(); };
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is ListBoxItem { Tag: ToolPresetState value }) { selected = value; try { if (!manager) ApplyToolPreset(value); } catch (Exception error) { Say(error.Message); } } };
        gear.Click += (_, _) =>
        {
            var menu = new ContextMenu();
            void Action(string title, Func<Task> action, bool enabled = true)
            { var item = Command(title, async () => { Hide(); try { await action(); } catch (Exception error) { Say(error.Message); } }); item.IsEnabled = enabled; menu.Items.Add(item); }
            void Choice(string title, bool active, System.Action action)
            { var item = Command(title, () => { action(); SavePreferenceChanges(); Reload(); }); item.ToggleType = MenuItemToggleType.CheckBox; item.IsChecked = active; menu.Items.Add(item); }
            Action("New Tool Preset…", New);
            Action("Rename Tool Preset…", async () => { if (selected is null) return; var name = await TextPrompt.Ask(this, "Rename Tool Preset", "Name", selected.Name); if (name is { Length: > 0 and <= 200 }) { selected.Name = name; Save(); } }, selected is not null);
            Action("Delete Tool Preset", () => { if (selected is not null) presets.Remove(selected); Save(); return Task.CompletedTask; }, selected is not null);
            menu.Items.Add(new Separator()); Choice("Sort by Tool", p.PresetSortByTool, () => p.PresetSortByTool = !p.PresetSortByTool);
            Choice("Show All Tool Presets", !p.PresetCurrentOnly, () => { p.PresetCurrentOnly = false; only.IsChecked = false; });
            Choice("Show Current Tool Presets", p.PresetCurrentOnly, () => { p.PresetCurrentOnly = true; only.IsChecked = true; });
            menu.Items.Add(new Separator());
            foreach (var (title, size) in new[] { ("Text Only",0), ("Small List",1), ("Large List",2) }) Choice(title, p.PresetListSize == size, () => p.PresetListSize = size);
            menu.Items.Add(new Separator());
            Action("Reset Tool", () => { ResetToolOptions(false); return Task.CompletedTask; });
            Action("Reset All Tools", () => { ResetToolOptions(true); return Task.CompletedTask; });
            Action("Preset Manager…", () => { OpenToolPresets(anchor, true); return Task.CompletedTask; });
            menu.Items.Add(new Separator());
            Action("Reset Tool Presets…", () => { if (File.Exists(ToolPresetState.FilePath)) File.Copy(ToolPresetState.FilePath, ToolPresetState.FilePath + ".bak", true); presets.Clear(); Save(); return Task.CompletedTask; });
            async Task Import(bool replace)
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Load Tool Presets"), FileTypeFilter = [new FilePickerFileType("Compositor Tool Presets") { Patterns = ["*.json"] }] });
                if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
                var read = ToolPresetState.Read(path); if (replace) presets.Clear(); var ids = presets.Select(v => v.ID).ToHashSet(); presets.AddRange(read.Where(v => !ids.Contains(v.ID))); Save();
            }
            Action("Load Tool Presets…", () => Import(false));
            Action("Save Tool Presets…", async () => { var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = "tool-presets.json", DefaultExtension = "json" }); if (file?.TryGetLocalPath() is { } path) ToolPresetState.Write(path, presets); });
            Action("Replace Tool Presets…", () => Import(true));
            void Convert(IEnumerable<ToolPresetState> source)
            {
                var brushes = EditorPresets.Load(EditorPresets.DefaultPath).ToList();
                foreach (var value in source.Where(v => ToolCatalog.IsBrush(v.Tool))) brushes.Add(new EditorPreset(Guid.NewGuid(), value.Name, "Brush", value.Options.Brush.Diameter, value.Options.Brush.Hardness, value.Options.Brush.Opacity, value.Tip, null));
                EditorPresets.Save(EditorPresets.DefaultPath, brushes);
            }
            menu.Items.Add(new Separator()); Action("Convert to Brush Preset", () => { if (selected is not null) Convert([selected]); return Task.CompletedTask; }, selected is not null && ToolCatalog.IsBrush(selected.Tool));
            Action("Convert All to Brush Presets…", () => { Convert(presets); return Task.CompletedTask; }, presets.Any(v => ToolCatalog.IsBrush(v.Tool)));
            menu.Items.Add(new Separator()); foreach (var tool in new[] { Tool.Move, Tool.Artboard, Tool.Crop, Tool.Frame, Tool.Type }) menu.Items.Add(Command(ToolCatalog.Title(tool), () => { Hide(); SetTool(tool); }));
            menu.Open(gear);
        };
        Reload();
        if (manager)
        {
            only.IsVisible = false; managerWindow = new ToolPresetManagerWindow { Title = Localize.Text("Tool Preset Manager"), Width = 380, Height = 285, Content = body, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            managerWindow.Show(this);
        }
        else { popup.Content = body; popup.ShowAt(anchor); }
    }
    private sealed class ToolPresetManagerWindow : DialogWindow { }
}
