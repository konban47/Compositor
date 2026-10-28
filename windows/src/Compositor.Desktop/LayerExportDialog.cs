using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Compositor.Core.IO;
using Compositor.Core.Model;

namespace Compositor.Desktop;

internal sealed class LayerExportDialog : DialogWindow
{
    private static Control Row(string label, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("160,*"), ColumnSpacing = 10 };
        grid.Children.Add(new TextBlock { Text = Localize.Text(label), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }
    private sealed record Item(LayerExportItem Value)
    {
        public override string ToString() => Value.Name;
    }

    public LayerExportDialog(CanvasDocument document, LayerExportKind kind)
    {
        Title = Localize.Text(kind == LayerExportKind.Layers ? "Export Layers to Files" : kind == LayerExportKind.LayerCompsPdf ? "Export Layer Comps to PDF" : "Export Layer Comps to Files");
        Width = 760; Height = 560; MinWidth = 640; MinHeight = 440;
        var list = new ListBox { SelectionMode = SelectionMode.Multiple, MinHeight = 160, Tag = "Export items" };
        var folder = new TextBox { IsReadOnly = true, PlaceholderText = Localize.Text("Select output folder…") };
        var prefix = new TextBox { MaxLength = 100 };
        var format = new ComboBox { ItemsSource = new[] { "PNG", "JPEG", "TIFF", "OpenEXR", "GIF", "PDF" }, SelectedIndex = 0 };
        ExportFormat[] formats = [ExportFormat.Png, ExportFormat.Jpeg, ExportFormat.Tiff, ExportFormat.Exr, ExportFormat.Gif, ExportFormat.Pdf];
        if (kind == LayerExportKind.LayerCompsPdf) { format.SelectedIndex = 5; format.IsEnabled = false; }
        var quality = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 90, IsEnabled = false };
        format.SelectionChanged += (_, _) => quality.IsEnabled = format.SelectedIndex == 1;
        var visible = new CheckBox { Content = Localize.Text("Visible Layers Only"), IsChecked = true, IsVisible = kind == LayerExportKind.Layers };
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var body = new StackPanel { Spacing = 10 };
        CancellationTokenSource? cancellation = null;
        LayerExportOptions Options() => new(folder.Text ?? "", kind, formats[Math.Max(0, format.SelectedIndex)], visible.IsChecked == true,
            (int)(quality.Value ?? 90), prefix.Text ?? "");
        void Refresh()
        {
            var rows = LayerExport.Items(document, Options()).Select(i => new Item(i)).ToArray();
            list.ItemsSource = rows;
            list.SelectAll();
        }
        visible.IsCheckedChanged += (_, _) => Refresh();
        Refresh();
        var browse = FormDialog.ActionButton("Browse…", () => _ = Choose());
        async Task Choose()
        {
            try
            {
                var selected = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Localize.Text("Select output folder…") });
                if (selected.Count > 0) folder.Text = selected[0].TryGetLocalPath();
            }
            catch (Exception error) { status.Text = error.Message; }
        }
        body.Children.Add(Row("Destination", folder));
        body.Children.Add(browse);
        body.Children.Add(Row("File Name Prefix", prefix));
        body.Children.Add(Row("Format", format));
        body.Children.Add(Row("Quality", quality));
        body.Children.Add(visible);
        body.Children.Add(list);
        body.Children.Add(FormDialog.ActionButton("Select All", list.SelectAll));
        body.Children.Add(new TextBlock { Text = Localize.Text("Existing files are preserved. Exports use the document canvas size."), TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        body.Children.Add(status);
        var actions = new StackPanel { Spacing = 12, Margin = new Thickness(16, 0, 0, 0) };
        Button run = null!;
        run = FormDialog.ActionButton("Run", () => _ = Execute());
        actions.Children.Add(run);
        actions.Children.Add(FormDialog.ActionButton("Cancel", () => { if (cancellation is null) Close(false); else cancellation.Cancel(); }));
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,120"), Margin = new Thickness(16) };
        root.Children.Add(new ScrollViewer { Content = body });
        Grid.SetColumn(actions, 1); root.Children.Add(actions); Content = root;
        Closing += (_, e) => { if (cancellation is not null) { e.Cancel = true; cancellation.Cancel(); } };
        KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape && cancellation is not null) { cancellation.Cancel(); e.Handled = true; } };
        async Task Execute()
        {
            if (cancellation is not null) return;
            var ids = list.SelectedItems?.OfType<Item>().Select(i => i.Value.ID).ToArray() ?? [];
            if (ids.Length == 0 || string.IsNullOrWhiteSpace(folder.Text))
            { status.Text = Localize.Text("Select an output folder and at least one item."); return; }
            var options = Options();
            cancellation = new CancellationTokenSource(); body.IsEnabled = false; run.IsEnabled = false;
            try
            {
                var progress = new Progress<BatchProgress>(p => status.Text = $"{p.Completed}/{p.Total} — {p.Name}");
                var files = await WorkScheduler.Run(token => LayerExport.Run(document, options, ids, progress, token), cancellation.Token);
                status.Text = Localize.Text("Export complete.") + $" ({files.Count})";
            }
            catch (OperationCanceledException) { status.Text = Localize.Text("Cancelled. Completed files were kept."); }
            catch (Exception error) { status.Text = error.Message; }
            finally { cancellation.Dispose(); cancellation = null; body.IsEnabled = true; run.IsEnabled = true; }
        }
    }
}
