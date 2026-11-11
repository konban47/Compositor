using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

/// <summary>What the New Layer, New Group and New Fill / Adjustment Layer dialogs ask for.</summary>
internal sealed record LayerOptions(string Name, LayerLabel Label, bool ClipToPrevious, LayerBlendMode Mode, double Opacity, bool FillNeutral);

/// <summary>
/// Photoshop's New Layer / New Group dialog, laid out as its screenshots are: Name and Color on the first row, Use Previous
/// Layer to Create Clipping Mask under them, Mode and Opacity on the next, Fill with Neutral Color last; OK and Cancel
/// on the right. Fill and adjustment layers use it without the neutral-color row, groups without either check box.
/// </summary>
internal sealed class LayerOptionsDialog : DialogWindow
{
    internal LayerOptions? Result { get; private set; }
    internal TextBox NameBox { get; }
    internal CheckBox ClipBox { get; } = new() { Tag = "layer-clip" };
    internal CheckBox Neutral { get; } = new() { Tag = "layer-neutral" };
    private LayerOptionsDialog(string title, string name, LayerOptionsKind kind)
    {
        Title = Localize.Text(title); Width = 900; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var group = kind == LayerOptionsKind.Group;
        NameBox = DialogKit.Line("layer-name"); NameBox.Text = name; NameBox.Width = 270; NameBox.Height = 32;
        var color = LayerDialogControls.ColorChoice(LayerLabel.None, out var readColor);
        var mode = LayerDialogControls.ModeChoice(LayerBlendMode.Normal, group, out var readMode);
        var opacity = LayerDialogControls.PercentField(100, "layer-opacity", out var opacityField);
        var swatch = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(4), BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Tag = "layer-neutral-swatch" };
        ClipBox.Content = new TextBlock { Text = Localize.Text("Use Previous Layer to Create Clipping Mask") + "(P)" };
        Neutral.Content = new TextBlock { Text = Localize.Text("Fill with Neutral Color") + "(N)" };
        // Fill with Neutral Color is offered only for modes that have a neutral color, and shows it, as Photoshop's does.
        void Refresh()
        {
            var neutral = NeutralColor(readMode());
            Neutral.IsEnabled = neutral is not null; if (neutral is null) Neutral.IsChecked = false;
            swatch.Background = neutral is { } c ? new SolidColorBrush(Color.FromRgb(c.Red, c.Green, c.Blue)) : Brushes.Transparent;
            swatch.Opacity = neutral is null ? .45 : 1;
        }
        mode.SelectionChanged += (_, _) => Refresh(); Refresh();
        var grid = new Grid { ColumnDefinitions = new("Auto,Auto,Auto,Auto"), ColumnSpacing = 14, RowSpacing = 14 };
        void Put(Control control, int row, int column, int span = 1) { Grid.SetRow(control, row); Grid.SetColumn(control, column); Grid.SetColumnSpan(control, span); control.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(control); }
        var rows = 0;
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Put(LayerDialogControls.Caption("Name", 'N'), rows, 0); Put(NameBox, rows, 1); Put(LayerDialogControls.Caption("Color", 'C'), rows, 2); Put(color, rows, 3);
        if (kind != LayerOptionsKind.Group) { grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); Put(ClipBox, ++rows, 1, 3); }
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); rows++;
        Put(LayerDialogControls.Caption("Mode", 'M'), rows, 0); Put(mode, rows, 1); Put(LayerDialogControls.Caption("Opacity", 'O'), rows, 2); Put(opacity, rows, 3);
        if (kind == LayerOptionsKind.Layer)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); rows++;
            Put(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { Neutral, swatch } }, rows, 1, 3);
        }
        mode.Width = NameBox.Width;
        LayerDialogControls.Key(this, 'N', NameBox); LayerDialogControls.Key(this, 'C', color); LayerDialogControls.Key(this, 'M', mode); LayerDialogControls.Key(this, 'O', opacityField);
        if (kind != LayerOptionsKind.Group) LayerDialogControls.Key(this, 'P', ClipBox);
        var ok = FormDialog.ActionButton("OK", () =>
        {
            var text = NameBox.Text?.Trim() ?? "";
            if (text.Length is 0 or > 256) { NameBox.Focus(); return; }
            Result = new LayerOptions(text, readColor(), ClipBox.IsChecked == true, readMode(), (double)(opacityField.Value ?? 100) / 100, Neutral.IsChecked == true);
            Close(true);
        });
        ok.IsDefault = true;
        var cancel = FormDialog.ActionButton("Cancel", () => Close(false)); cancel.IsCancel = true;
        var buttons = DialogKit.ActionColumn(ok, cancel); buttons.Width = 130; buttons.Margin = new Thickness(18, 0, 0, 0);
        var root = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(22, 20) };
        root.Children.Add(grid); Grid.SetColumn(buttons, 1); root.Children.Add(buttons);
        Content = root;
        Opened += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }
    internal enum LayerOptionsKind { Layer, Group, Content }
    internal static async Task<LayerOptions?> Ask(Window owner, string title, string name, LayerOptionsKind kind)
    {
        var dialog = new LayerOptionsDialog(title, name, kind);
        return await dialog.ShowDialog<bool>(owner) ? dialog.Result : null;
    }
    /// <summary>The color a mode leaves unchanged, which Fill with Neutral Color fills a new layer with; null where there is none.</summary>
    internal static SKColor? NeutralColor(LayerBlendMode mode) => mode switch
    {
        LayerBlendMode.Multiply or LayerBlendMode.ColorBurn or LayerBlendMode.LinearBurn or LayerBlendMode.Darken or LayerBlendMode.DarkerColor => SKColors.White,
        LayerBlendMode.Screen or LayerBlendMode.ColorDodge or LayerBlendMode.LinearDodgeAdd or LayerBlendMode.Lighten or LayerBlendMode.LighterColor
            or LayerBlendMode.Difference or LayerBlendMode.Exclusion or LayerBlendMode.Subtract => SKColors.Black,
        LayerBlendMode.Overlay or LayerBlendMode.SoftLight or LayerBlendMode.HardLight or LayerBlendMode.VividLight or LayerBlendMode.LinearLight or LayerBlendMode.PinLight => new SKColor(128, 128, 128),
        _ => null,
    };
}

/// <summary>Layer ▸ Duplicate Layer: the copy's name, and the document and artboard it goes to (a new document by name).</summary>
internal sealed class DuplicateLayerDialog : DialogWindow
{
    internal sealed record Choice(string Name, int Document, Guid? Artboard, string NewDocumentName);
    private Choice? _result;
    private DuplicateLayerDialog(string layerName, IReadOnlyList<(string Title, IReadOnlyList<(Guid ID, string Name)> Artboards)> documents, int current)
    {
        Title = Localize.Text("Duplicate Layer"); Width = 640; SizeToContent = SizeToContent.Height; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var name = DialogKit.Line("duplicate-name"); name.Text = layerName + " " + Localize.Text("copy");
        var document = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "duplicate-document", MinHeight = DialogKit.FieldHeight, ItemsSource = documents.Select(d => d.Title).Append(Localize.Text("New")).ToArray(), SelectedIndex = current };
        var artboard = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "duplicate-artboard", MinHeight = DialogKit.FieldHeight };
        var newName = DialogKit.Line("duplicate-new-name"); newName.Text = Localize.Text("Untitled");
        var newCaption = LayerDialogControls.Caption("Name", 'N', true);
        var artboardCaption = new TextBlock { Text = Localize.Text("Artboard") + ":", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        void Refresh()
        {
            var fresh = document.SelectedIndex >= documents.Count;
            var boards = fresh ? [] : documents[document.SelectedIndex].Artboards;
            artboard.ItemsSource = boards.Select(b => b.Name).Prepend(Localize.Text("Canvas")).ToArray(); artboard.SelectedIndex = 0;
            artboard.IsEnabled = artboardCaption.IsEnabled = boards.Count > 0; artboardCaption.Opacity = boards.Count > 0 ? 1 : .5;
            newName.IsEnabled = fresh; newCaption.Opacity = fresh ? 1 : .5;
        }
        document.SelectionChanged += (_, _) => Refresh(); Refresh();
        var grid = new Grid { ColumnDefinitions = new("110,*"), RowDefinitions = new("Auto,Auto"), ColumnSpacing = 10, RowSpacing = 10 };
        void Put(Grid g, Control c, int r, int col) { Grid.SetRow(c, r); Grid.SetColumn(c, col); c.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(c); }
        Put(grid, new TextBlock { Text = Localize.Text("Duplicate") + ":", HorizontalAlignment = HorizontalAlignment.Right }, 0, 0); Put(grid, new TextBlock { Text = layerName, TextTrimming = TextTrimming.CharacterEllipsis, Tag = "duplicate-source" }, 0, 1);
        Put(grid, LayerDialogControls.Caption("As", 'A', true), 1, 0); Put(grid, name, 1, 1);
        var target = new Grid { ColumnDefinitions = new("110,*"), RowDefinitions = new("Auto,Auto,Auto"), ColumnSpacing = 10, RowSpacing = 10 };
        Put(target, new TextBlock { Text = Localize.Text("Document") + ":", HorizontalAlignment = HorizontalAlignment.Right }, 0, 0); Put(target, document, 0, 1);
        Put(target, artboardCaption, 1, 0); Put(target, artboard, 1, 1);
        Put(target, newCaption, 2, 0); Put(target, newName, 2, 1);
        LayerDialogControls.Key(this, 'A', name); LayerDialogControls.Key(this, 'N', newName);
        var body = new StackPanel { Spacing = 8, Children = { grid, DialogKit.Group("Destination", target) } };
        var ok = FormDialog.ActionButton("OK", () =>
        {
            var text = name.Text?.Trim() ?? ""; if (text.Length is 0 or > 256) return;
            var fresh = document.SelectedIndex >= documents.Count;
            Guid? board = !fresh && artboard.SelectedIndex > 0 ? documents[document.SelectedIndex].Artboards[artboard.SelectedIndex - 1].ID : null;
            _result = new Choice(text, fresh ? -1 : document.SelectedIndex, board, newName.Text?.Trim() is { Length: > 0 } n ? n : Localize.Text("Untitled"));
            Close(true);
        });
        ok.IsDefault = true; var cancel = FormDialog.ActionButton("Cancel", () => Close(false)); cancel.IsCancel = true;
        var buttons = DialogKit.ActionColumn(ok, cancel); buttons.Width = 130; buttons.Margin = new Thickness(18, 0, 0, 0);
        var root = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(20, 14, 20, 18) };
        root.Children.Add(body); Grid.SetColumn(buttons, 1); root.Children.Add(buttons); Content = root;
        Opened += (_, _) => { name.Focus(); name.SelectAll(); };
    }
    internal static async Task<Choice?> Ask(Window owner, string layerName, IReadOnlyList<(string Title, IReadOnlyList<(Guid ID, string Name)> Artboards)> documents, int current)
    {
        var dialog = new DuplicateLayerDialog(layerName, documents, current);
        return await dialog.ShowDialog<bool>(owner) ? dialog._result : null;
    }
}

/// <summary>New Artboard, Artboard from Layers and Add Frame to Layer: a name and a size, with the size presets for artboards.</summary>
internal sealed class ContainerDialog : DialogWindow
{
    internal sealed record Choice(string Name, double Width, double Height);
    private Choice? _result;
    private ContainerDialog(string title, string name, double width, double height, bool frame)
    {
        Title = Localize.Text(title); Width = frame ? 640 : 620; SizeToContent = SizeToContent.Height; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var text = DialogKit.Line("container-name"); text.Text = name; text.Width = frame ? 310 : 310;
        NumericUpDown Size(double value, string tag) => new() { Minimum = 1, Maximum = DocumentLimits.MaxSide, Value = (decimal)Math.Clamp(Math.Round(value), 1, DocumentLimits.MaxSide), FormatString = "0 'px'", ShowButtonSpinner = false, Width = 104, MinHeight = DialogKit.FieldHeight, Tag = tag };
        var w = Size(width, "container-width"); var h = Size(height, "container-height");
        var grid = new Grid { ColumnDefinitions = new("Auto,Auto,Auto,Auto"), ColumnSpacing = 12, RowSpacing = 14 };
        void Put(Control c, int r, int col, int span = 1) { while (grid.RowDefinitions.Count <= r) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); Grid.SetRow(c, r); Grid.SetColumn(c, col); Grid.SetColumnSpan(c, span); c.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(c); }
        Put(LayerDialogControls.Caption("Name", 'N', frame), 0, 0); Put(text, 0, 1, 3);
        var row = 1;
        if (!frame)
        {
            var presets = ToolOptionsBar.ArtboardPresets;
            var preset = new ComboBox { Width = 310, MinHeight = DialogKit.FieldHeight, Tag = "artboard-preset" };
            ToolTip.SetTip(preset, Localize.Text("Choose a preset size for the artboard."));
            var rows = new List<int?>();
            for (var i = 0; i < presets.Length; i++)
            {
                if (i is 1 or 15 or 24) { preset.Items.Add(new ComboBoxItem { Content = new Border { Height = 1, Margin = new Thickness(4, 3), Background = Skin.MenuRule }, IsEnabled = false, Focusable = false }); rows.Add(null); }
                preset.Items.Add(new ComboBoxItem { Content = Localize.Text(presets[i].Name) }); rows.Add(i);
            }
            preset.SelectedIndex = 0; var choosing = false;
            preset.SelectionChanged += (_, _) => { if (preset.SelectedIndex > 0 && rows[preset.SelectedIndex] is { } at && presets[at].Width > 0) { choosing = true; w.Value = presets[at].Width; h.Value = presets[at].Height; choosing = false; } };
            w.ValueChanged += (_, _) => { if (!choosing) preset.SelectedIndex = 0; }; h.ValueChanged += (_, _) => { if (!choosing) preset.SelectedIndex = 0; };
            Put(new TextBlock { Text = Localize.Text("Preset"), HorizontalAlignment = HorizontalAlignment.Right }, row, 0); Put(preset, row++, 1, 3);
        }
        Put(LayerDialogControls.Caption("Width", frame ? 'W' : 'D', frame), row, 0); Put(w, row, 1);
        Put(LayerDialogControls.Caption("Height", frame ? 'H' : 'G', frame), row, 2); Put(h, row, 3);
        LayerDialogControls.Key(this, 'N', text); LayerDialogControls.Key(this, frame ? 'W' : 'D', w); LayerDialogControls.Key(this, frame ? 'H' : 'G', h);
        var ok = FormDialog.ActionButton("OK", () =>
        {
            var value = text.Text?.Trim() ?? ""; if (value.Length is 0 or > 256 || w.Value is not { } ww || h.Value is not { } hh || (double)ww * (double)hh > DocumentLimits.MaxSurfacePixels) return;
            _result = new Choice(value, (double)ww, (double)hh); Close(true);
        });
        ok.IsDefault = true; var cancel = FormDialog.ActionButton("Cancel", () => Close(false)); cancel.IsCancel = true;
        var buttons = DialogKit.ActionColumn(ok, cancel); buttons.Width = 130; buttons.Margin = new Thickness(18, 0, 0, 0);
        var root = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(22, 20) };
        root.Children.Add(grid); Grid.SetColumn(buttons, 1); root.Children.Add(buttons); Content = root;
        Opened += (_, _) => { text.Focus(); text.SelectAll(); };
    }
    internal static async Task<Choice?> Ask(Window owner, string title, string name, double width, double height, bool frame)
    {
        var dialog = new ContainerDialog(title, name, width, height, frame);
        return await dialog.ShowDialog<bool>(owner) ? dialog._result : null;
    }
}

/// <summary>Layer Style ▸ Global Light…: the light's angle and altitude on one dial, with Preview.</summary>
internal sealed class GlobalLightDialog : DialogWindow
{
    internal (double Angle, double Altitude)? Result { get; private set; }
    internal AngleDial Dial { get; } = new(56) { ShowsAltitude = true };
    private GlobalLightDialog(double angle, double altitude, Action<double, double>? preview)
    {
        Title = Localize.Text("Global Light"); Width = 420; SizeToContent = SizeToContent.Height; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Dial.Angle = angle; Dial.Altitude = altitude;
        NumericUpDown Field(double value, double min, double max, string tag) => new() { Minimum = (decimal)min, Maximum = (decimal)max, Value = (decimal)value, FormatString = "0", ShowButtonSpinner = false, Width = 64, MinHeight = DialogKit.FieldHeight, Tag = tag };
        var angleBox = Field(angle, -180, 180, "global-angle"); var altitudeBox = Field(altitude, 0, 90, "global-altitude");
        var previewBox = new CheckBox { IsChecked = true, Content = Localize.Text("Preview") + "(V)", Tag = "global-preview" };
        var syncing = false;
        void Push() { if (previewBox.IsChecked == true) preview?.Invoke(Dial.Angle, Dial.Altitude); }
        Dial.Changed += () => { syncing = true; angleBox.Value = (decimal)Dial.Angle; altitudeBox.Value = (decimal)Dial.Altitude; syncing = false; Push(); };
        angleBox.ValueChanged += (_, _) => { if (syncing || angleBox.Value is not { } v) return; Dial.Angle = (double)v; Push(); };
        altitudeBox.ValueChanged += (_, _) => { if (syncing || altitudeBox.Value is not { } v) return; Dial.Altitude = (double)v; Push(); };
        previewBox.IsCheckedChanged += (_, _) => { if (previewBox.IsChecked == true) Push(); else preview?.Invoke(angle, altitude); };
        var degrees = () => new TextBlock { Text = Localize.Text("degrees"), VerticalAlignment = VerticalAlignment.Center };
        var grid = new Grid { ColumnDefinitions = new("Auto,Auto,Auto,Auto"), RowDefinitions = new("Auto,Auto,Auto"), ColumnSpacing = 10, RowSpacing = 4 };
        void Put(Control c, int r, int col, int rowSpan = 1) { Grid.SetRow(c, r); Grid.SetColumn(c, col); Grid.SetRowSpan(c, rowSpan); c.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(c); }
        Put(new TextBlock { Text = Localize.Text("Angle") + ":", VerticalAlignment = VerticalAlignment.Top }, 0, 0); Put(angleBox, 0, 2); Put(degrees(), 0, 3);
        Put(Dial, 0, 1, 3);
        Put(new TextBlock { Text = Localize.Text("Altitude") + ":" }, 2, 0); Put(altitudeBox, 2, 2); Put(degrees(), 2, 3);
        var ok = FormDialog.ActionButton("OK", () => { Result = (Dial.Angle, Dial.Altitude); Close(true); }); ok.IsDefault = true;
        var cancel = FormDialog.ActionButton("Cancel", () => { preview?.Invoke(angle, altitude); Close(false); }); cancel.IsCancel = true;
        LayerDialogControls.Key(this, 'V', previewBox);
        var buttons = DialogKit.ActionColumn(ok, cancel, previewBox); buttons.Width = 120;
        var root = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(18, 14) };
        root.Children.Add(grid); Grid.SetColumn(buttons, 1); root.Children.Add(buttons); Content = root;
    }
    internal static async Task<(double Angle, double Altitude)?> Ask(Window owner, double angle, double altitude, Action<double, double>? preview)
    {
        var dialog = new GlobalLightDialog(angle, altitude, preview);
        return await dialog.ShowDialog<bool>(owner) ? dialog.Result : null;
    }
}

/// <summary>Layer ▸ Lock Layers…: the locks with Photoshop's small icons; All covers the rest.</summary>
internal sealed class LockLayersDialog : DialogWindow
{
    private LayerLocks? _result;
    private LockLayersDialog(LayerLocks initial)
    {
        Title = Localize.Text("Lock Layers"); Width = 440; SizeToContent = SizeToContent.Height; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var rows = new StackPanel { Spacing = 10 };
        var boxes = new List<(LayerLocks Flag, CheckBox Box)>();
        foreach (var (flag, title, glyph) in new[]
        {
            (LayerLocks.Transparency, "Transparency", "transparency"), (LayerLocks.Pixels, "Image", "brush"),
            (LayerLocks.Position, "Position", "move"), (LayerLocks.Nesting, "Prevent Auto-Nesting", "new-artboard"), (LayerLocks.All, "All", "lock"),
        })
        {
            var box = new CheckBox { Content = Localize.Text(title), IsChecked = initial.HasFlag(flag), Tag = "lock " + title };
            boxes.Add((flag, box));
            rows.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = flag == LayerLocks.All ? new Thickness(0, 10, 0, 0) : default, Children = { new IconGlyph(glyph) { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center }, box } });
        }
        // All implies the others, so they show checked and cannot be turned off while it is on.
        var all = boxes.Last().Box;
        void Sync() { foreach (var (flag, box) in boxes.Where(b => b.Flag != LayerLocks.All)) { box.IsEnabled = all.IsChecked != true; if (all.IsChecked == true) box.IsChecked = true; } }
        all.IsCheckedChanged += (_, _) => Sync(); Sync();
        var ok = FormDialog.ActionButton("OK", () => { _result = all.IsChecked == true ? LayerLocks.All : boxes.Where(b => b.Box.IsChecked == true).Aggregate(LayerLocks.None, (a, b) => a | b.Flag); Close(true); });
        ok.IsDefault = true; var cancel = FormDialog.ActionButton("Cancel", () => Close(false)); cancel.IsCancel = true;
        var buttons = DialogKit.ActionColumn(ok, cancel); buttons.Width = 130;
        var root = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(26, 16, 18, 18) };
        root.Children.Add(rows); Grid.SetColumn(buttons, 1); root.Children.Add(buttons); Content = root;
    }
    internal static async Task<LayerLocks?> Ask(Window owner, LayerLocks initial)
    {
        var dialog = new LockLayersDialog(initial);
        return await dialog.ShowDialog<bool>(owner) ? dialog._result : null;
    }
}

/// <summary>One of the app's icons drawn small, as the Lock Layers dialog shows one beside each lock.</summary>
internal sealed class IconGlyph(string kind) : Control
{
    public override void Render(DrawingContext context) => IconSet.Draw(context, kind, Bounds.Size, Skin.SecondaryBrush);
}
