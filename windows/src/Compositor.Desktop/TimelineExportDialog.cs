using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Compositor.Core.Document;
using Compositor.Core.Model;

namespace Compositor.Desktop;

internal sealed class TimelineExportDialog : DialogWindow
{
    internal string Destination { get; private set; } = "";
    internal int OutputWidth => (int)_width.Value!;
    internal int OutputHeight => (int)_height.Value!;
    internal double Rate => (double)_rate.Value!;
    internal int Quality => new[] { 18, 23, 28 }[_quality.SelectedIndex];
    internal bool IncludeAudio => _audio.IsChecked == true;
    internal bool ColorManaged => _color.IsChecked == true;
    internal double From => _all.IsChecked == true ? 0 : _work.IsChecked == true ? _timeline.WorkStart : (double)_from.Value! / _timeline.FrameRate;
    internal double To => _all.IsChecked == true ? _timeline.Duration : _work.IsChecked == true ? _timeline.WorkEnd : Math.Min(_timeline.Duration, ((double)_to.Value! + 1) / _timeline.FrameRate);
    private readonly NumericUpDown _width, _height, _rate, _from, _to;
    private readonly ComboBox _quality;
    private readonly CheckBox _audio, _color;
    private readonly RadioButton _all, _work;
    private readonly DocumentTimeline _timeline;
    internal TimelineExportDialog(CanvasDocument document, DocumentTimeline timeline, string name)
    {
        _timeline = timeline; Title = Localize.Text("Render Video"); Width = 1040; Height = 735; MinWidth = 850; MinHeight = 650; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new Grid { ColumnDefinitions = new("*,105"), Margin = new Thickness(16) };
        var sections = new StackPanel { Spacing = 18 }; layout.Children.Add(new ScrollViewer { Content = sections });
        var actions = new StackPanel { Spacing = 14, Margin = new Thickness(12, 12, 0, 0) }; Grid.SetColumn(actions, 1); layout.Children.Add(actions);
        var error = new TextBlock { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var location = Section("Location"); var filename = new TextBox { Text = Path.GetFileNameWithoutExtension(name) + ".mp4", Tag = "video-filename" }; location.Children.Add(Row("Name", filename));
        var directory = new TextBlock { Text = folder, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var choose = FormDialog.ActionButton("Select Folder…", async () => { var found = await StorageProvider.OpenFolderPickerAsync(new() { Title = Localize.Text("Select Folder…") }); if (found.FirstOrDefault()?.TryGetLocalPath() is { } path) { folder = path; directory.Text = path; } });
        location.Children.Add(Pair(choose, directory));
        var subfolder = new CheckBox { Content = Localize.Text("Create New Subfolder"), Tag = "video-subfolder-enabled" }; var subname = new TextBox { IsEnabled = false }; subfolder.IsCheckedChanged += (_, _) => subname.IsEnabled = subfolder.IsChecked == true; location.Children.Add(Pair(subfolder, subname));
        var format = Section("FFmpeg"); _quality = Choice(["High Quality", "Medium Quality", "Small File"], 0);
        format.Children.Add(Pair(Row("Format", Choice(["H.264 MP4"], 0)), Row("Preset", _quality)));
        _width = Number(document.Width, 2, 8192); _height = Number(document.Height, 2, 8192);
        var size = Choice(["Document Size", "1920 × 1080", "1280 × 720", "Custom"], 0); var settingSize = false;
        size.SelectionChanged += (_, _) => { settingSize = true; if (size.SelectedIndex < 3) { _width.Value = size.SelectedIndex == 0 ? document.Width : size.SelectedIndex == 1 ? 1920 : 1280; _height.Value = size.SelectedIndex == 0 ? document.Height : size.SelectedIndex == 1 ? 1080 : 720; } settingSize = false; };
        foreach (var number in new[] { _width, _height }) number.ValueChanged += (_, _) => { if (!settingSize) size.SelectedIndex = 3; };
        format.Children.Add(Pair(Row("Size", size), Pair(_width, _height)));
        _rate = Number(timeline.FrameRate, 1, 120); var rates = Choice(["Document Frame Rate", "24", "25", "30", "60", "Custom"], 0); var settingRate = false;
        rates.SelectionChanged += (_, _) => { settingRate = true; if (rates.SelectedIndex < 5) _rate.Value = rates.SelectedIndex == 0 ? (decimal)timeline.FrameRate : new[] { 0, 24, 25, 30, 60 }[rates.SelectedIndex]; settingRate = false; };
        _rate.ValueChanged += (_, _) => { if (!settingRate) rates.SelectedIndex = 5; }; format.Children.Add(Pair(Row("Frame Rate", rates), Row("fps", _rate)));
        format.Children.Add(Pair(Row("Field Order", Choice(["Progressive"], 0)), Row("Pixel Aspect Ratio", Choice(["Square Pixels (1.0)"], 0))));
        _color = new CheckBox { Content = Localize.Text("Use Document Color Settings"), IsChecked = true }; _audio = new CheckBox { Content = Localize.Text("Include Audio"), IsChecked = true }; format.Children.Add(Pair(_color, _audio));
        var bottom = new Grid { ColumnDefinitions = new("*,*") }; sections.Children.Add(bottom);
        var range = MakeSection("Range", out var rangeBorder); bottom.Children.Add(rangeBorder);
        _all = Radio("All Frames"); _work = Radio("Work Area"); _work.IsChecked = true; var custom = Radio("Start Frame");
        _from = Number(Math.Floor(timeline.WorkStart * timeline.FrameRate), 0, Math.Max(0, Math.Ceiling(timeline.Duration * timeline.FrameRate) - 1)); _to = Number(Math.Ceiling(timeline.WorkEnd * timeline.FrameRate) - 1, 0, Math.Max(0, Math.Ceiling(timeline.Duration * timeline.FrameRate) - 1));
        range.Children.Add(_all); range.Children.Add(Pair(Pair(custom, _from), Row("End Frame", _to))); range.Children.Add(Pair(_work, new TextBlock { Text = $"{timeline.WorkStart * timeline.FrameRate:0} – {Math.Ceiling(timeline.WorkEnd * timeline.FrameRate) - 1:0}", VerticalAlignment = VerticalAlignment.Center }));
        custom.IsCheckedChanged += (_, _) => _from.IsEnabled = _to.IsEnabled = custom.IsChecked == true; _from.IsEnabled = _to.IsEnabled = false;
        var render = MakeSection("Render Options", out var renderBorder); Grid.SetColumn(renderBorder, 1); renderBorder.Margin = new Thickness(14, 0, 0, 0); bottom.Children.Add(renderBorder);
        var alpha = Choice(["None"], 0); alpha.IsEnabled = false; render.Children.Add(Row("Alpha Channel", alpha));
        sections.Children.Add(error);
        actions.Children.Add(FormDialog.ActionButton("Render", () => {
            var file = filename.Text?.Trim() ?? ""; var sub = subname.Text?.Trim() ?? "";
            if (!ValidName(file) || subfolder.IsChecked == true && !ValidName(sub)) { error.Text = Localize.Text("Enter a valid file or folder name."); return; }
            if (From >= To) { error.Text = Localize.Text("Start frame must precede end frame."); return; }
            Destination = Path.Combine(subfolder.IsChecked == true ? Path.Combine(folder, sub) : folder, Path.ChangeExtension(file, ".mp4")); Close(true);
        })); actions.Children.Add(FormDialog.ActionButton("Cancel", () => Close(false))); Content = layout;
        StackPanel Section(string title) { var content = MakeSection(title, out var border); sections.Children.Add(border); return content; }
    }
    private static bool ValidName(string name) => name.Length is > 0 and <= 200 && name is not ("." or "..") && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.EndsWith('.') && !name.EndsWith(' ');
    private static NumericUpDown Number(double value, double min, double max) => new() { Value = (decimal)Math.Clamp(value, min, max), Minimum = (decimal)min, Maximum = (decimal)max, ShowButtonSpinner = false, FormatString = "0.###", MinHeight = 30, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static ComboBox Choice(string[] items, int index) => new() { ItemsSource = items.Select(Localize.Text).ToArray(), SelectedIndex = index, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static RadioButton Radio(string text) => new() { Content = Localize.Text(text), GroupName = "video-range" };
    private static Control Row(string title, Control field) { var row = new Grid { ColumnDefinitions = new("115,*") }; row.Children.Add(new TextBlock { Text = Localize.Text(title), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }); Grid.SetColumn(field, 1); row.Children.Add(field); return row; }
    private static Control Pair(Control a, Control b) { var row = new Grid { ColumnDefinitions = new("*,*") }; a.Margin = new Thickness(0, 0, 10, 0); row.Children.Add(a); Grid.SetColumn(b, 1); row.Children.Add(b); return row; }
    private static StackPanel MakeSection(string title, out Border border) { var group = new StackPanel { Spacing = 12, Margin = new Thickness(14) }; group.Children.Add(new TextBlock { Text = Localize.Text(title), FontWeight = FontWeight.Medium }); border = new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Child = group }; return group; }
}
