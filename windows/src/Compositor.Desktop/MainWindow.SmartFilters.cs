using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

internal enum SmartFilterAction { ToggleStack, ToggleMask, ToggleMaskEnabled, Clear }

/// <summary>
/// Smart filters, as Photoshop has them: a filter applied to a smart object is kept under the layer instead of being
/// painted in. The layers panel lists them under a Smart Filters row (its eye and filter mask) newest first, each with its
/// eye, name and blending icon; double-clicking a filter opens it again, double-clicking its icon opens its blending
/// options, and the rows' menus switch filters, the stack and the mask off, delete them or clear them.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>What the panel and the history call a filter: its Filter menu title.</summary>
    internal static string SmartFilterName(SmartFilter filter) => filter.Kind switch
    {
        FilterKind.GaussianBlur => "Gaussian Blur", FilterKind.MotionBlur => "Motion Blur", FilterKind.BloomGlow => "Bloom / Glow", FilterKind.AddNoise => "Add Noise",
        FilterKind.TonalContrast => "Tonal Contrast", FilterKind.ReduceNoise => "Reduce Noise", FilterKind.LensCorrection => "Lens Correction",
        FilterKind.ProgressiveBlur => "Progressive Blur", FilterKind.RgbSplit => "RGB Split",
        FilterKind.LensEffects => filter.Settings.Lens.Effect switch { LensEffect.AnamorphicFlare => "Anamorphic Flare", LensEffect.Starburst => "Starburst", _ => "Lens Effects" },
        _ => filter.Kind.ToString(),
    };

    /// <summary>Filter ▸ (any filter) on a smart object: the dialog shows the contents through the filters with the new one on top.</summary>
    private async Task AddSmartFilter(CanvasDocument document, Guid id, FilterKind kind)
    {
        if (document.Layers.FirstOrDefault(l => l.ID == id) is not { SmartObject: { } smart } layer) return;
        if (!LayerWorkflow.Editable(document, [id])) { Say("The layer is locked."); return; }
        using var contents = SmartFilterEdits.Contents(smart);
        var existing = layer.SmartFilters;
        StartPreview(FilterPreview.Begin(document, id), id);
        var asked = await FilterDialog.Ask(this, kind, _filterAmounts, settings =>
        {
            if (settings is not { } amounts) { HidePreview(); return; }
            var stack = existing?.Copy() ?? new SmartFilterStack(); stack.Items.Add(new SmartFilter { Kind = kind, Settings = SmartFilterEdits.Copy(amounts) });
            RequestPreview((target, layerID) => ShowSmartPreview(target, layerID, stack, contents));
        });
        StopPreview();
        if (asked is not { } settings || !ReferenceEquals(document, _document)) return;
        _filterAmounts = settings;
        var name = SmartFilterName(new SmartFilter { Kind = kind, Settings = settings });
        if (!Edit(name, () => SmartFilterEdits.Add(document, id, kind, settings))) { Say("The smart filter could not be added."); return; }
        ShowLayers(document); Reselect(id);
        Say($"{name} added as a smart filter");
    }

    /// <summary>The preview layer drawn with a stack that is not on it yet.</summary>
    private static bool ShowSmartPreview(CanvasDocument target, Guid layerID, SmartFilterStack? stack, SKBitmap contents)
    {
        if (target.Layers.FirstOrDefault(l => l.ID == layerID) is not { } shown) return false;
        shown.Asset = SmartFilterEdits.Bake(stack, contents, shown.Name);
        return true;
    }

    /// <summary>Edit Smart Filter…, and a double-click on a filter: its dialog on its own amounts.</summary>
    private async Task EditSmartFilter(Guid id, int index)
    {
        if (_document is not { } document || document.Layers.FirstOrDefault(l => l.ID == id) is not { SmartObject: { } smart, SmartFilters: { } current } || index < 0 || index >= current.Items.Count) return;
        if (!LayerWorkflow.Editable(document, [id])) { Say("The layer is locked."); return; }
        var filter = current.Items[index];
        using var contents = SmartFilterEdits.Contents(smart);
        StartPreview(FilterPreview.Begin(document, id), id);
        var asked = await FilterDialog.Ask(this, filter.Kind, SmartFilterEdits.Copy(filter.Settings), settings =>
        {
            if (settings is not { } amounts) { HidePreview(); return; }
            var stack = current.Copy(); stack.Items[index].Settings = SmartFilterEdits.Copy(amounts); stack.Items[index].Enabled = true;
            RequestPreview((target, layerID) => ShowSmartPreview(target, layerID, stack, contents));
        });
        StopPreview();
        if (asked is not { } settings || !ReferenceEquals(document, _document)) return;
        Edit("Edit Smart Filter", () => SmartFilterEdits.Change(document, id, index, settings) | SmartFilterEdits.SetEnabled(document, id, index, true));
        ShowLayers(document); Reselect(id);
    }

    /// <summary>Edit Smart Filter Blending Options…: the filter's mode and opacity, previewed as they change.</summary>
    private async Task SmartFilterBlending(Guid id, int index)
    {
        if (_document is not { } document || document.Layers.FirstOrDefault(l => l.ID == id) is not { SmartObject: { } smart, SmartFilters: { } current } || index < 0 || index >= current.Items.Count) return;
        if (!LayerWorkflow.Editable(document, [id])) { Say("The layer is locked."); return; }
        using var contents = SmartFilterEdits.Contents(smart);
        var filter = current.Items[index];
        StartPreview(FilterPreview.Begin(document, id), id);
        var dialog = new SmartFilterBlendingDialog(Localize.Text(SmartFilterName(filter)), filter.Mode, filter.Opacity);
        dialog.Changed = (mode, opacity) =>
        {
            var stack = current.Copy(); stack.Items[index].Mode = mode; stack.Items[index].Opacity = opacity;
            RequestPreview((target, layerID) => ShowSmartPreview(target, layerID, dialog.PreviewEnabled ? stack : current, contents));
        };
        try { await dialog.ShowDialog<bool>(this); } finally { StopPreview(); }
        if (dialog.Result is not { } result || !ReferenceEquals(document, _document)) return;
        Edit("Smart Filter Blending Options", () => SmartFilterEdits.SetBlending(document, id, index, result.Mode, result.Opacity));
        ShowLayers(document); Reselect(id);
    }

    /// <summary>Layer ▸ Smart Filters and the Smart Filters row's menu.</summary>
    private void SmartFilterCommand(SmartFilterAction action, Guid? layerID = null)
    {
        if (_document is not { } document || (layerID ?? Selected) is not { } id || document.Layers.FirstOrDefault(l => l.ID == id)?.SmartFilters is not { } stack) return;
        var (name, edit) = action switch
        {
            SmartFilterAction.ToggleStack => (stack.Enabled ? "Disable Smart Filters" : "Enable Smart Filters", (Func<bool>)(() => SmartFilterEdits.SetStackEnabled(document, id, !stack.Enabled))),
            SmartFilterAction.ToggleMask => (stack.HasMask ? "Delete Filter Mask" : "Add Filter Mask", () => SmartFilterEdits.SetMask(document, id, !stack.HasMask)),
            SmartFilterAction.ToggleMaskEnabled => (stack.MaskEnabled ? "Disable Filter Mask" : "Enable Filter Mask", () => SmartFilterEdits.SetMaskEnabled(document, id, !stack.MaskEnabled)),
            _ => ("Clear Smart Filters", () => SmartFilterEdits.Clear(document, id)),
        };
        Edit(name, edit); ShowLayers(document); Reselect(id);
    }

    /// <summary>
    /// The rows under a smart object layer: Smart Filters with its eye and mask, then each filter newest first with its
    /// eye, name and blending icon, all inside the layer's own row, so dragging the layer takes them along.
    /// </summary>
    private Control? SmartFilterRows(ImageLayer layer)
    {
        if (layer.SmartFilters is not { } stack || layer.SmartObject is null || _open.Collapsed.Contains(layer.ID)) return null;
        var rows = new StackPanel { Tag = "smart-filters", Background = Skin.ChromeBrush };
        Button Eye(bool on, string tip, Action click)
        {
            var eye = new Button { Content = new LayerEye(on) { Width = 16, Height = 16 }, Width = 26, Height = 22, Padding = new Thickness(3), Background = Brushes.Transparent, BorderThickness = default, Focusable = false };
            ToolTip.SetTip(eye, Localize.Text(tip)); eye.Click += (_, e) => { click(); e.Handled = true; }; return eye;
        }
        // Smart Filters: the eye for the whole stack, the filter mask's thumbnail, the name.
        var header = new Grid { ColumnDefinitions = new("46,Auto,*"), Height = 40, Tag = "smart-filters-row" };
        var stackEye = Eye(stack.Enabled, stack.Enabled ? "Disable Smart Filters" : "Enable Smart Filters", () => SmartFilterCommand(SmartFilterAction.ToggleStack, layer.ID));
        stackEye.HorizontalAlignment = HorizontalAlignment.Right; header.Children.Add(stackEye);
        var mask = new Border { Width = 32, Height = 32, Margin = new Thickness(6, 4), BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Background = Brushes.White, IsVisible = stack.HasMask, Opacity = stack.MaskEnabled ? 1 : .45, Tag = "filter-mask" };
        if (stack.Mask is { } encoded) try { using var decoded = SKBitmap.Decode(Convert.FromBase64String(encoded)); if (decoded is not null) mask.Child = new Image { Source = Eyedroppers.Thumbnail(decoded), Stretch = Stretch.Uniform }; } catch (FormatException) { }
        Grid.SetColumn(mask, 1); header.Children.Add(mask);
        var title = new TextBlock { Text = Localize.Text("Smart Filters"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0), FontSize = 12, Opacity = stack.Enabled ? 1 : .55 };
        Grid.SetColumn(title, 2); header.Children.Add(title);
        header.ContextRequested += (_, e) =>
        {
            var menu = new ContextMenu();
            MenuItem Row(string text, Action act) { var item = new MenuItem { Header = Localize.Text(text), Tag = text }; item.Click += (_, _) => act(); return item; }
            menu.Items.Add(Row(stack.Enabled ? "Disable Smart Filters" : "Enable Smart Filters", () => SmartFilterCommand(SmartFilterAction.ToggleStack, layer.ID)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Row(stack.HasMask ? "Delete Filter Mask" : "Add Filter Mask", () => SmartFilterCommand(SmartFilterAction.ToggleMask, layer.ID)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Row("Clear Smart Filters", () => SmartFilterCommand(SmartFilterAction.Clear, layer.ID)));
            menu.Open(header); e.Handled = true;
        };
        rows.Children.Add(header);
        // The filters, newest first, as Photoshop lists them.
        for (var index = stack.Items.Count - 1; index >= 0; index--)
        {
            var at = index; var filter = stack.Items[index];
            var row = new Grid { ColumnDefinitions = new("46,*,Auto"), Height = 22, Tag = "smart-filter " + SmartFilterName(filter), Background = Brushes.Transparent };
            var eye = Eye(filter.Enabled, filter.Enabled ? "Disable Smart Filter" : "Enable Smart Filter", () =>
            { if (_document is { } d) { Edit(filter.Enabled ? "Disable Smart Filter" : "Enable Smart Filter", () => SmartFilterEdits.SetEnabled(d, layer.ID, at, !filter.Enabled)); ShowLayers(d); Reselect(layer.ID); } });
            eye.HorizontalAlignment = HorizontalAlignment.Right; row.Children.Add(eye);
            var name = new TextBlock { Text = Localize.Text(SmartFilterName(filter)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0), FontSize = 12, Opacity = filter.Enabled && stack.Enabled ? 1 : .55 };
            Grid.SetColumn(name, 1); row.Children.Add(name);
            var blending = new Button { Content = new BlendingGlyph { Width = 16, Height = 16 }, Width = 26, Height = 22, Padding = new Thickness(4, 3), Background = Brushes.Transparent, BorderThickness = default, Focusable = false, Tag = "smart-filter-blending" };
            ToolTip.SetTip(blending, Localize.Text("Double-click to edit the filter blending options"));
            blending.DoubleTapped += (_, e) => { _ = SmartFilterBlending(layer.ID, at); e.Handled = true; };
            Grid.SetColumn(blending, 2); row.Children.Add(blending);
            row.DoubleTapped += (_, e) => { if (e.Handled) return; SelectLayerRow(layer.ID); _ = EditSmartFilter(layer.ID, at); e.Handled = true; };
            row.ContextRequested += (_, e) =>
            {
                var menu = new ContextMenu();
                MenuItem Row(string text, Action act) { var item = new MenuItem { Header = Localize.Text(text), Tag = text }; item.Click += (_, _) => act(); return item; }
                menu.Items.Add(Row("Edit Smart Filter Blending Options…", () => _ = SmartFilterBlending(layer.ID, at)));
                menu.Items.Add(Row("Edit Smart Filter…", () => _ = EditSmartFilter(layer.ID, at)));
                menu.Items.Add(new Separator());
                menu.Items.Add(Row(filter.Enabled ? "Disable Smart Filter" : "Enable Smart Filter", () =>
                { if (_document is { } d) { Edit(filter.Enabled ? "Disable Smart Filter" : "Enable Smart Filter", () => SmartFilterEdits.SetEnabled(d, layer.ID, at, !filter.Enabled)); ShowLayers(d); Reselect(layer.ID); } }));
                menu.Items.Add(Row("Delete Smart Filter", () => { if (_document is { } d) { Edit("Delete Smart Filter", () => SmartFilterEdits.Delete(d, layer.ID, at)); ShowLayers(d); Reselect(layer.ID); } }));
                menu.Open(row); e.Handled = true;
            };
            rows.Children.Add(row);
        }
        return rows;
    }
}

/// <summary>Photoshop's Blending Options for one smart filter: Mode and Opacity, with Preview.</summary>
internal sealed class SmartFilterBlendingDialog : DialogWindow
{
    internal (LayerBlendMode Mode, double Opacity)? Result { get; private set; }
    internal Action<LayerBlendMode, double>? Changed { get; set; }
    internal bool PreviewEnabled => _preview.IsChecked == true;
    private readonly CheckBox _preview = AdjustmentKit.Preview(false);
    internal SmartFilterBlendingDialog(string filterName, LayerBlendMode start, double opacity)
    {
        Title = Localize.Text("Blending Options") + " (" + filterName + ")"; SizeToContent = SizeToContent.WidthAndHeight; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var mode = LayerDialogControls.ModeChoice(start, false, out var readMode, 220);
        var amount = LayerDialogControls.PercentField(opacity * 100, "smart-filter-opacity", out var field);
        void Report() => Changed?.Invoke(readMode(), (double)(field.Value ?? 100) / 100);
        mode.SelectionChanged += (_, _) => Report(); field.ValueChanged += (_, _) => Report(); _preview.IsCheckedChanged += (_, _) => Report();
        LayerDialogControls.Key(this, 'M', mode); LayerDialogControls.Key(this, 'O', field);
        var form = new Grid { ColumnDefinitions = new("Auto,Auto"), RowDefinitions = new("Auto,Auto"), ColumnSpacing = 10, RowSpacing = 12 };
        void Put(Control c, int row, int column) { Grid.SetRow(c, row); Grid.SetColumn(c, column); c.VerticalAlignment = VerticalAlignment.Center; form.Children.Add(c); }
        Put(LayerDialogControls.Caption("Mode", 'M', true), 0, 0); Put(mode, 0, 1); Put(LayerDialogControls.Caption("Opacity", 'O', true), 1, 0); Put(amount, 1, 1);
        var actions = AdjustmentKit.Actions(this, () => { Result = (readMode(), (double)(field.Value ?? 100) / 100); Close(true); }, () => Close(false), _preview);
        Content = AdjustmentKit.Body(AdjustmentKit.Group("Blending Options", form), actions);
    }
}

/// <summary>The icon at the right of a smart filter: two sliders, as Photoshop draws its blending options mark.</summary>
internal sealed class BlendingGlyph : Control
{
    public override void Render(DrawingContext context)
    {
        var pen = new Pen(Skin.LabelBrush, 1.2);
        for (var i = 0; i < 2; i++)
        {
            var y = 5 + i * 6;
            context.DrawLine(pen, new Point(1, y), new Point(15, y));
            var x = i == 0 ? 10 : 5;
            var tri = new StreamGeometry();
            using (var g = tri.Open()) { g.BeginFigure(new Point(x - 3, y - 3), true); g.LineTo(new Point(x + 3, y - 3)); g.LineTo(new Point(x, y)); g.EndFigure(true); }
            context.DrawGeometry(Skin.LabelBrush, null, tri);
        }
    }
}

/// <summary>The mark at the right of a layer with smart filters: two overlapping circles.</summary>
internal sealed class SmartFilterGlyph : Control
{
    public override void Render(DrawingContext context)
    {
        var pen = new Pen(Skin.LabelBrush, 1.3);
        context.DrawEllipse(null, pen, new Point(Bounds.Width * .62, Bounds.Height * .42), Bounds.Width * .3, Bounds.Height * .3);
        context.DrawEllipse(null, pen, new Point(Bounds.Width * .4, Bounds.Height * .62), Bounds.Width * .3, Bounds.Height * .3);
    }
}
