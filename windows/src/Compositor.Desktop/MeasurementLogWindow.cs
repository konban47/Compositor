using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;

namespace Compositor.Desktop;

/// <summary>
/// The Measurement Log, as Photoshop's panel shows it: Record Measurements at the top left, Select All, Deselect All,
/// Export Selected and Delete Selected at the right, a numbered row for each measurement under a column for each data
/// point chosen, and the ≡ menu with Set Measurement Scale, Select Data Points, Auto Show Panel and Close. It stays open
/// beside the canvas, following whichever document is in front.
/// </summary>
internal sealed class MeasurementLogWindow : DialogWindow
{
    /// <summary>Auto Show Panel: recording a measurement brings the log up.</summary>
    internal static bool AutoShow { get; set; } = true;
    private readonly Func<CanvasDocument?> _document;
    private readonly Action _record;
    private readonly Action<IReadOnlyList<int>> _delete;
    private readonly StackPanel _rows = new();
    private readonly Grid _header = new();
    private readonly HashSet<int> _selected = [];
    private const double Column = 180, Number = 60;
    internal IReadOnlyCollection<int> Selected => _selected;

    internal MeasurementLogWindow(Func<CanvasDocument?> document, Action record, Action<IReadOnlyList<int>> delete, Func<MenuItem> scaleMenu, Func<MenuItem> pointsMenu)
    {
        _document = document; _record = record; _delete = delete;
        Title = Localize.Text("Measurement Log"); Width = 1360; Height = 420; MinWidth = 520; MinHeight = 220; CanResize = true; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var recordButton = FormDialog.ActionButton("Record Measurements", () => _record()); recordButton.Tag = "Record Measurements";
        Button Icon(string glyph, string tip, Action click) { var b = new Button { Content = new IconGlyph(glyph) { Width = 18, Height = 18 }, Width = 34, Height = 30, Padding = default, Background = Brushes.Transparent, BorderThickness = default, HorizontalContentAlignment = HorizontalAlignment.Center, Tag = tip }; ToolTip.SetTip(b, Localize.Text(tip)); b.Click += (_, _) => click(); return b; }
        var selectAll = Icon("selection", "Select All", SelectAll); var deselect = Icon("cancel", "Deselect All", DeselectAll);
        var export = Icon("down", "Export Selected Measurements…", async () => await Export()); var remove = Icon("delete", "Delete Selected Measurements", Delete);
        // The ≡ menu, as the panel's own.
        var recordItem = new MenuItem { Header = Localize.Text("Record Measurements"), Tag = "Record Measurements" }; recordItem.Click += (_, _) => _record();
        var allItem = new MenuItem { Header = Localize.Text("Select All"), Tag = "Select All" }; allItem.Click += (_, _) => SelectAll();
        var noneItem = new MenuItem { Header = Localize.Text("Deselect All"), Tag = "Deselect All" }; noneItem.Click += (_, _) => DeselectAll();
        var exportItem = new MenuItem { Header = Localize.Text("Export Selected Measurements…"), Tag = "Export Selected Measurements…" }; exportItem.Click += async (_, _) => await Export();
        var deleteItem = new MenuItem { Header = Localize.Text("Delete Selected Measurements"), Tag = "Delete Selected Measurements" }; deleteItem.Click += (_, _) => Delete();
        var autoItem = new MenuItem { Header = Localize.Text("Auto Show Panel"), ToggleType = MenuItemToggleType.CheckBox, IsChecked = AutoShow, Tag = "Auto Show Panel" }; autoItem.Click += (_, _) => { AutoShow = !AutoShow; autoItem.IsChecked = AutoShow; };
        var closeItem = new MenuItem { Header = Localize.Text("Close"), Tag = "Close" }; closeItem.Click += (_, _) => Close();
        var menuButton = new Button { Content = "☰", Width = 30, Height = 26, Padding = default, Background = Brushes.Transparent, BorderThickness = default, HorizontalContentAlignment = HorizontalAlignment.Center, Tag = "measurement log menu" };
        menuButton.Click += (_, _) =>
        {
            var has = _document()?.Measurements.Count > 0;
            allItem.IsEnabled = has && _selected.Count < _document()!.Measurements.Count; noneItem.IsEnabled = exportItem.IsEnabled = deleteItem.IsEnabled = _selected.Count > 0;
            var menu = new ContextMenu();
            foreach (var item in new Control[] { scaleMenu(), pointsMenu(), recordItem, new Separator(), allItem, noneItem, exportItem, deleteItem, new Separator(), autoItem, new Separator(), closeItem })
            { if (item.Parent is ItemsControl owner) owner.Items.Remove(item); menu.Items.Add(item); }
            menu.Open(menuButton);
        };
        var bar = new Grid { ColumnDefinitions = new("Auto,*,Auto,Auto,Auto,Auto,Auto"), Margin = new Thickness(10, 6), ColumnSpacing = 4 };
        bar.Children.Add(recordButton);
        foreach (var (control, column) in new[] { ((Control)selectAll, 2), (deselect, 3), (export, 4), (remove, 5), (menuButton, 6) }) { Grid.SetColumn(control, column); bar.Children.Add(control); }
        var table = new StackPanel { Children = { _header, _rows } };
        var scroll = new ScrollViewer { Content = table, HorizontalScrollBarVisibility = ScrollBarVisibility.Visible, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Skin.SurfaceDarkBrush };
        var root = new Grid { RowDefinitions = new("Auto,*"), Children = { bar, scroll } }; Grid.SetRow(scroll, 1);
        Content = root;
        KeyDown += (_, e) => { if (e.Key == Key.Delete) { Delete(); e.Handled = true; } else if (e.Key == Key.A && e.KeyModifiers == KeyModifiers.Control) { SelectAll(); e.Handled = true; } };
        Refresh();
    }

    /// <summary>Redraws the table from the document in front.</summary>
    internal void Refresh()
    {
        var doc = _document(); _header.Children.Clear(); _header.ColumnDefinitions.Clear(); _rows.Children.Clear();
        if (doc is null) return;
        _selected.RemoveWhere(i => i >= doc.Measurements.Count);
        var fields = doc.MeasurementFields;
        _header.ColumnDefinitions.Add(new ColumnDefinition(Number, GridUnitType.Pixel));
        foreach (var _ in fields) _header.ColumnDefinitions.Add(new ColumnDefinition(Column, GridUnitType.Pixel));
        for (var c = 0; c < fields.Length; c++)
        {
            var name = fields[c] == "Mean" ? "Gray Value (Mean)" : fields[c] is "Selection Count" or "Ruler Count" ? "Count" : fields[c];
            var cell = new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(0, 0, 1, 1), Background = Skin.ChromeBrush, Padding = new Thickness(6, 4), Child = new TextBlock { Text = name == "Scale" ? AdjustmentKit.Word("Scale", "比例") : Localize.Text(name), HorizontalAlignment = HorizontalAlignment.Center } };
            Grid.SetColumn(cell, c + 1); _header.Children.Add(cell);
        }
        var number = new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(0, 0, 1, 1), Background = Skin.ChromeBrush }; _header.Children.Add(number);
        for (var r = 0; r < doc.Measurements.Count; r++)
        {
            var record = doc.Measurements[r]; var index = r;
            var row = new Grid { Background = _selected.Contains(r) ? new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5A)) : Brushes.Transparent, Tag = "measurement " + (r + 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition(Number, GridUnitType.Pixel));
            foreach (var _ in fields) row.ColumnDefinitions.Add(new ColumnDefinition(Column, GridUnitType.Pixel));
            row.Children.Add(new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(0, 0, 1, 1), Padding = new Thickness(6, 3), Child = new TextBlock { Text = (r + 1).ToString("0000") } });
            for (var c = 0; c < fields.Length; c++)
            {
                var value = ImageAnalysis.Value(doc, record, fields[c]);
                if (fields[c] == "Source" || fields[c] == "Label" && value.StartsWith("Measurement ", StringComparison.Ordinal)) value = Localize.Text(fields[c] == "Source" ? value : "Measurement") + (fields[c] == "Label" ? value["Measurement".Length..] : "");
                var numeric = double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
                var cell = new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(0, 0, 1, 1), Padding = new Thickness(6, 3), Child = new TextBlock { Text = value, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = numeric ? HorizontalAlignment.Right : HorizontalAlignment.Left } };
                Grid.SetColumn(cell, c + 1); row.Children.Add(cell);
            }
            row.PointerPressed += (_, e) =>
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) { if (!_selected.Remove(index)) _selected.Add(index); }
                else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _selected.Count > 0) { var from = _selected.Min(); for (var i = Math.Min(from, index); i <= Math.Max(from, index); i++) _selected.Add(i); }
                else { _selected.Clear(); _selected.Add(index); }
                Refresh();
            };
            _rows.Children.Add(row);
        }
    }

    internal void SelectAll() { if (_document() is { } doc) { for (var i = 0; i < doc.Measurements.Count; i++) _selected.Add(i); Refresh(); } }
    internal void DeselectAll() { _selected.Clear(); Refresh(); }
    private void Delete() { if (_selected.Count == 0) return; var chosen = _selected.OrderByDescending(i => i).ToArray(); _selected.Clear(); _delete(chosen); Refresh(); }

    private async Task Export()
    {
        if (_document() is not { } doc || _selected.Count == 0) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Localize.Text("Export Selected Measurements…"), SuggestedFileName = "measurements.txt", DefaultExtension = ".txt",
            FileTypeChoices = [new FilePickerFileType(Localize.Text("Tab-delimited text")) { Patterns = ["*.txt"] }, new FilePickerFileType("CSV") { Patterns = ["*.csv"] }] });
        if (file?.TryGetLocalPath() is not { } path) return;
        var records = _selected.Order().Select(i => doc.Measurements[i]).ToArray();
        var text = path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? ImageAnalysis.Csv(WithOnly(doc, records)) : ImageAnalysis.Tsv(doc, records);
        try { await AtomicFile.WriteBytesAsync(path, new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray()); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>A stand-in holding only the chosen rows, so the CSV writer exports just those.</summary>
    private static CanvasDocument WithOnly(CanvasDocument doc, IEnumerable<Compositor.Core.Format.MeasurementRecord> records)
    {
        var copy = new CanvasDocument(Guid.NewGuid(), doc.Width, doc.Height) { MeasurementScale = doc.MeasurementScale, MeasurementFields = doc.MeasurementFields };
        copy.Measurements.AddRange(records); return copy;
    }
}
