using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Desktop;

/// <summary>The preview and the saved file always use the same encoded bytes.</summary>
internal sealed partial class WebExportDialog : DialogWindow
{
    private readonly DocumentLease _lease;
    private readonly ComboBox _format = Choice(["GIF", "JPEG", "PNG-8", "PNG-24", "WBMP"]);
    private readonly ComboBox _palette = Choice(["Adaptive", "Perceptual", "Web Safe", "Black and White", "Grayscale", "Custom"]);
    private readonly ComboBox _dither = Choice(["None", "Diffusion", "Pattern", "Noise"], 1);
    private readonly ComboBox _alphaDither = Choice(["None", "Diffusion", "Pattern", "Noise"]);
    private readonly ComboBox _resample = Choice(["Nearest Neighbor", "Bilinear", "Bicubic", "Bicubic Smoother", "Bicubic Sharper"], 2);
    private readonly ComboBox _metadata = Choice(["None", "Copyright", "Copyright and Contact", "All Except Camera Information", "All"], 2);
    private readonly ComboBox _view = Choice(["Original", "Optimized", "2-Up", "4-Up"], 1);
    private readonly ComboBox _saveAs = Choice(["Images Only", "HTML and Images", "HTML Only"]);
    private readonly ComboBox _scope = Choice(["All Slices", "All User Slices"]);
    private readonly ComboBox _loop = Choice(["Forever", "Once", "Other"]);
    private readonly NumericUpDown _loops = Number(3, 1, 65535);
    private readonly TextBlock _frameLabel = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 48, TextAlignment = TextAlignment.Center };
    private readonly List<Button> _frameButtons = [];
    /// <summary>Which frame of a frame animation the preview shows; −1 when the document has none.</summary>
    private int _frame = -1;
    private WebHtmlSettings _html = WebHtmlSettings.Default;
    private readonly NumericUpDown _width, _height;
    private readonly NumericUpDown _quality = Number(80, 1, 100), _colors = Number(256, 2, 256), _amount = Number(100, 0, 100), _snap = Number(0, 0, 100);
    private readonly NumericUpDown _zoom = Number(100, 6, 1600);
    private readonly CheckBox _transparent = Check("Transparency", true), _interlaced = Check("Interlaced"), _srgb = Check("Convert to sRGB", true), _icc = Check("Embed Color Profile"), _linked = Check("Constrain Proportions", true);
    private readonly TextBox _matte = new() { Text = "#FFFFFF" }, _hex = new() { Text = "#000000" };
    private readonly Grid _previews = new();
    private readonly WrapPanel _swatches = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Skin.SecondaryBrush };
    private readonly TextBlock _sample = new() { Text = "RGBA: —", VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _save;
    private readonly StackPanel _fields = new() { Spacing = 7, Margin = new Thickness(12) };
    private readonly List<WriteableBitmap> _images = [];
    private WebExportResult[] _results = [];
    private SKBitmap? _original;
    private uint[] _custom = [], _displayPalette = [];
    private int _selectedColor = -1, _revision;
    private bool _changing, _closed, _rendering, _saving;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private CancellationTokenSource? _renderCancel;
    private Point? _pan;
    private Vector _panOffset;

    /// <summary>Files written while the dialog was open; the editor raises the Export Document script event once if any were.</summary>
    internal int CompletedExports { get; private set; }

    internal WebExportDialog(CanvasDocument document)
    {
        _lease = new DocumentLease(document);
        _width = Number(document.Width, 1, DocumentLimits.MaxSide);
        _height = Number(document.Height, 1, DocumentLimits.MaxSide);
        Title = Localize.Text("Save for Web"); Width = 1180; Height = 820; MinWidth = 840; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _save = FormDialog.ActionButton("Save…", () => _ = Save()); _save.IsEnabled = false;
        var layout = new Grid { RowDefinitions = new("*,Auto"), Margin = new Thickness(10) };
        var body = new Grid { ColumnDefinitions = new("*,330") };
        var previewArea = new Grid { RowDefinitions = new("Auto,*,Auto") };
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(4, 0, 4, 8) };
        foreach (var (label, index) in new[] { "Original", "Optimized", "2-Up", "4-Up" }.Select((label, index) => (label, index)))
            tabs.Children.Add(FormDialog.ActionButton(label, () => _view.SelectedIndex = index));
        previewArea.Children.Add(tabs); Grid.SetRow(_previews, 1); previewArea.Children.Add(_previews);
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(4, 8, 4, 0) };
        navigation.Children.Add(FormDialog.ActionButton("−", () => _zoom.Value = Math.Max(6, (_zoom.Value ?? 100) / 1.25m)));
        _zoom.Width = 95; navigation.Children.Add(_zoom);
        navigation.Children.Add(FormDialog.ActionButton("+", () => _zoom.Value = Math.Min(1600, (_zoom.Value ?? 100) * 1.25m)));
        navigation.Children.Add(FormDialog.ActionButton("Fit", () => { var bitmap = _results.FirstOrDefault()?.Preview ?? _original; if (bitmap is null) return; var rows = _view.SelectedIndex == 3 ? 4 : _view.SelectedIndex == 2 ? 2 : 1; _zoom.Value = (decimal)Math.Clamp(Math.Min((_previews.Bounds.Width - 36) / bitmap.Width, (_previews.Bounds.Height / rows - 65) / bitmap.Height) * 100, 6, 1600); }));
        navigation.Children.Add(_sample); Grid.SetRow(navigation, 2); previewArea.Children.Add(navigation); body.Children.Add(previewArea);
        Add("Format", _format); Add("Quality", _quality); Add("Palette", _palette); Add("Colors", _colors); Add("Dither", _dither); Add("Dither Amount", _amount);
        _fields.Children.Add(_transparent); Add("Transparency Dither", _alphaDither); Add("Matte", _matte); _fields.Children.Add(_interlaced); Add("Web Snap", _snap);
        _fields.Children.Add(new Separator()); _fields.Children.Add(_srgb); _fields.Children.Add(_icc);
        _fields.Children.Add(new TextBlock { Text = Localize.Text("Color Table") });
        _fields.Children.Add(new ScrollViewer { Content = _swatches, Height = 145, Background = Skin.SurfaceDarkBrush });
        Add("Color", _hex);
        var paletteActions = new WrapPanel();
        paletteActions.Children.Add(FormDialog.ActionButton("Add", () => EditPalette(false)));
        paletteActions.Children.Add(FormDialog.ActionButton("Replace", () => EditPalette(true)));
        paletteActions.Children.Add(FormDialog.ActionButton("Delete", DeleteColor));
        paletteActions.Children.Add(FormDialog.ActionButton("Load…", () => _ = LoadPalette()));
        paletteActions.Children.Add(FormDialog.ActionButton("Save…", () => _ = SavePalette()));
        paletteActions.Children.Add(PaletteMenu()); _fields.Children.Add(paletteActions);
        _fields.Children.Add(new Separator()); Add("Width", _width); Add("Height", _height); _fields.Children.Add(_linked); Add("Resample", _resample);
        var advanced = new WrapPanel(); advanced.Children.Add(FormDialog.ActionButton("Optimize File Size…", () => _ = Optimize()));
        advanced.Children.Add(FormDialog.ActionButton("Load Settings…", () => _ = LoadSettings())); advanced.Children.Add(FormDialog.ActionButton("Save Settings…", () => _ = SaveSettings()));
        _fields.Children.Add(advanced);
        Add("Metadata", _metadata);
        _fields.Children.Add(new Separator()); Add("Save As", _saveAs); Add("Slices", _scope);
        var output = new WrapPanel(); output.Children.Add(FormDialog.ActionButton("Output Settings…", () => _ = EditOutput())); _fields.Children.Add(output);
        _fields.Children.Add(new Separator()); _fields.Children.Add(new TextBlock { Text = Localize.Text("Animation") });
        Add("Looping Options", _loop); Add("Play Count", _loops);
        var frames = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var (glyph, tip, step) in new[] { ("⏮", "Select First Frame", int.MinValue), ("◀", "Select Previous Frame", -1), ("▶", "Select Next Frame", 1), ("⏭", "Select Last Frame", int.MaxValue) })
        {
            var button = FormDialog.ActionButton(glyph, () => StepFrame(step)); button.MinWidth = 40; button.Margin = new Thickness(1, 0); ToolTip.SetTip(button, Localize.Text(tip));
            _frameButtons.Add(button); frames.Children.Add(button);
            if (step == -1) frames.Children.Add(_frameLabel);
        }
        _fields.Children.Add(frames);
        var settings = new ScrollViewer { Content = _fields, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(settings, 1); body.Children.Add(settings); layout.Children.Add(body);
        var footer = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(4, 12, 4, 0) };
        footer.Children.Add(_status); var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(_save); actions.Children.Add(FormDialog.ActionButton("Cancel", Close)); actions.Children.Add(FormDialog.ActionButton("Done", Close));
        Grid.SetColumn(actions, 1); footer.Children.Add(actions); Grid.SetRow(footer, 1); layout.Children.Add(footer); Content = layout;
        foreach (var choice in new[] { _format, _palette, _dither, _alphaDither, _resample, _metadata }) choice.SelectionChanged += (_, _) => Queue();
        _loop.SelectionChanged += (_, _) => ShowAnimation();
        var frameCount = WebExport.Frames(document).Count; _frame = frameCount > 0 ? 0 : -1;
        ShowAnimation();
        foreach (var number in new[] { _quality, _colors, _amount, _snap }) number.ValueChanged += (_, _) => Queue();
        foreach (var check in new[] { _transparent, _interlaced, _srgb, _icc }) { check.IsCheckedChanged += (_, _) => Queue(); }
        _matte.TextChanged += (_, _) => Queue();
        _width.ValueChanged += (_, _) => DimensionChanged(true); _height.ValueChanged += (_, _) => DimensionChanged(false);
        _zoom.ValueChanged += (_, _) => ShowPreviews(); _view.SelectionChanged += (_, _) => { ShowPreviews(); Queue(); };
        _timer.Tick += (_, _) => { _timer.Stop(); _ = RenderPreview(); };
        Opened += (_, _) => Queue();
        Closing += (_, e) => { if (_saving) e.Cancel = true; };
        Closed += (_, _) => { _closed = true; _timer.Stop(); _renderCancel?.Cancel(); DisposePreviews(); if (!_rendering) _lease.Dispose(); };
    }

    private static ComboBox Choice(string[] choices, int selected = 0) => new() { ItemsSource = choices.Select(Localize.Text).ToArray(), SelectedIndex = selected, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static NumericUpDown Number(int value, int min, int max) => new() { Value = value, Minimum = min, Maximum = max, Increment = 1, FormatString = "0", ShowButtonSpinner = false };
    private static CheckBox Check(string title, bool value = false) => new() { Content = Localize.Text(title), IsChecked = value };
    private void Add(string title, Control control)
    {
        var row = new Grid { ColumnDefinitions = new("125,*") };
        row.Children.Add(new TextBlock { Text = Localize.Text(title), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        control.MinHeight = 28; Grid.SetColumn(control, 1); row.Children.Add(control); _fields.Children.Add(row);
    }
    private WebExportOptions Options()
    {
        if (!SKColor.TryParse(_matte.Text, out var matte)) throw new InvalidDataException("Enter a valid hexadecimal matte color.");
        return new((int)(_width.Value ?? 1), (int)(_height.Value ?? 1), (WebFormat)_format.SelectedIndex,
            (int)(_quality.Value ?? 80), (int)(_colors.Value ?? 256), (WebPalette)_palette.SelectedIndex,
            (WebDither)_dither.SelectedIndex, (int)(_amount.Value ?? 100), _transparent.IsChecked == true,
            (WebDither)_alphaDither.SelectedIndex, matte, _interlaced.IsChecked == true, (int)(_snap.Value ?? 0),
            _srgb.IsChecked == true, _icc.IsChecked == true, (WebResample)_resample.SelectedIndex, _custom,
        _lockedColors.ToArray(), _transparentColors.ToArray(), (MetadataPolicy)_metadata.SelectedIndex);
    }
    private void DimensionChanged(bool width)
    {
        if (_changing) return; _changing = true;
        if (_linked.IsChecked == true)
        {
            if (width) _height.Value = Math.Clamp(Math.Round((_width.Value ?? 1) * _lease.Document.Height / _lease.Document.Width), 1, DocumentLimits.MaxSide);
            else _width.Value = Math.Clamp(Math.Round((_height.Value ?? 1) * _lease.Document.Width / _lease.Document.Height), 1, DocumentLimits.MaxSide);
        }
        _changing = false; Queue();
    }
    private void Queue()
    {
        if (_changing || _closed) return;
        _revision++; _renderCancel?.Cancel(); _save.IsEnabled = false;
        var indexed = _format.SelectedIndex is 0 or 2 or 4;
        foreach (var c in new Control[] { _palette, _colors, _dither, _amount, _snap, _hex, _swatches }) c.IsEnabled = indexed;
        _quality.IsEnabled = _format.SelectedIndex == 1;
        _transparent.IsEnabled = _format.SelectedIndex is 0 or 2 or 3;
        _alphaDither.IsEnabled = indexed && _transparent.IsChecked == true && _transparent.IsEnabled;
        _icc.IsEnabled = _format.SelectedIndex is 1 or 2 or 3;
        _interlaced.IsEnabled = _format.SelectedIndex != 4;
        ShowAnimation();
        _status.Text = Localize.Text("Rendering preview…"); _timer.Stop(); _timer.Start();
    }
    internal async Task RenderPreview(long? targetBytes = null)
    {
        if (_rendering || _closed) return;
        _rendering = true; var revision = _revision;
        _renderCancel?.Dispose(); _renderCancel = new(); var token = _renderCancel.Token;
        WebExportResult[]? encoded = null; SKBitmap? original = null;
        try
        {
            var options = Options(); options.Validate(); var four = _view.SelectedIndex == 3;
            (encoded, original) = await Task.Run(() =>
            {
                var results = new List<WebExportResult>(); SKBitmap? source = null;
                try
                {
                    results.Add(targetBytes is { } target ? WebExport.Optimize(_lease.Document, options, target, token) : WebExport.Encode(_lease.Document, options, token, _frame));
                    if (four)
                    {
                        var minimum = Math.Min(options.Colors, Math.Max(2, (options.LockedColors?.Length ?? 0) + (options.Transparency ? 1 : 0)));
                        results.Add(WebExport.Encode(_lease.Document, options with { Quality = Math.Max(1, options.Quality / 2), Colors = Math.Max(minimum, options.Colors / 2) }, token, _frame));
                        results.Add(WebExport.Encode(_lease.Document, options with { Quality = Math.Max(1, options.Quality / 4), Colors = Math.Max(minimum, options.Colors / 4), Dither = WebDither.None }, token, _frame));
                    }
                    token.ThrowIfCancellationRequested(); source = DocumentRenderer.Render(_lease.Document);
                    return (results.ToArray(), source);
                }
                catch { foreach (var result in results) result.Dispose(); source?.Dispose(); throw; }
            }, token);
            if (_closed || revision != _revision) return;
            DisposePreviews(); _results = encoded; encoded = null; _original = original; original = null;
            if (targetBytes.HasValue) Apply(_results[0].Options);
            _displayPalette = _results[0].Palette; _selectedColor = -1; SortPalette(); ShowPreviews(); _save.IsEnabled = true;
            _status.Text = Localize.Format($"File size: {_results[0].Bytes.Length / 1024.0:0.0} KB");
            if (targetBytes is { } limit && _results[0].Bytes.Length > limit) _status.Text += " — " + Localize.Text("The requested size cannot be reached at these dimensions and format.");
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_closed && revision == _revision) _status.Text = Localize.Text(error.Message); }
        finally
        {
            if (encoded is not null) foreach (var result in encoded) result.Dispose(); original?.Dispose();
            _rendering = false;
            if (_closed) _lease.Dispose(); else if (revision != _revision) { _timer.Stop(); _timer.Start(); }
        }
    }
    private void ShowPreviews()
    {
        if (_closed) return;
        _previews.Children.Clear(); foreach (var image in _images) image.Dispose(); _images.Clear();
        var count = _view.SelectedIndex == 3 ? 4 : _view.SelectedIndex == 2 ? 2 : 1;
        _previews.RowDefinitions = new(string.Join(',', Enumerable.Repeat("*", count)));
        for (var i = 0; i < count; i++)
        {
            var resultIndex = count == 1 ? (_view.SelectedIndex == 0 ? -1 : 0) : i - 1;
            var result = resultIndex >= 0 && resultIndex < _results.Length ? _results[resultIndex] : null;
            var bitmap = resultIndex < 0 ? _original : result?.Preview; if (bitmap is null) continue;
            var image = CanvasView.ToImage(bitmap); _images.Add(image);
            var visual = new Image { Source = image, Width = bitmap.Width * (double)(_zoom.Value ?? 100) / 100, Height = bitmap.Height * (double)(_zoom.Value ?? 100) / 100, Stretch = Stretch.Fill, Margin = new Thickness(12) };
            var scroll = new ScrollViewer { Content = visual, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Skin.Checker };
            scroll.PointerPressed += (_, e) => { if (e.GetCurrentPoint(scroll).Properties.IsLeftButtonPressed) { _pan = e.GetPosition(scroll); _panOffset = scroll.Offset; e.Pointer.Capture(scroll); } };
            scroll.PointerMoved += (_, e) => { if (_pan is { } start) scroll.Offset = _panOffset - (e.GetPosition(scroll) - start); };
            scroll.PointerReleased += (_, e) => { _pan = null; e.Pointer.Capture(null); };
            visual.PointerMoved += (_, e) =>
            {
                var p = e.GetPosition(visual); var x = (int)(p.X / visual.Width * bitmap.Width); var y = (int)(p.Y / visual.Height * bitmap.Height);
                if (x >= 0 && y >= 0 && x < bitmap.Width && y < bitmap.Height) { var c = bitmap.GetPixel(x, y); _sample.Text = $"RGBA: {c.Red}, {c.Green}, {c.Blue}, {c.Alpha}  #{c.Red:X2}{c.Green:X2}{c.Blue:X2}"; }
            };
            var cell = new Grid { RowDefinitions = new("*,Auto"), Margin = new Thickness(3) }; cell.Children.Add(scroll);
            var label = result is null ? Localize.Text("Original") : $"{result.Options.Format} · {result.Bytes.Length / 1024.0:0.00} KB · {Localize.Text("Quality")}: {result.Options.Quality} · {Localize.Text("Colors")}: {result.Palette.Length}";
            var caption = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(6), IsEnabled = result is not null };
            if (result is not null) caption.Click += (_, _) => { Apply(result.Options); Queue(); };
            Grid.SetRow(caption, 1); cell.Children.Add(caption); Grid.SetRow(cell, i); _previews.Children.Add(cell);
        }
    }
    private void DisposePreviews()
    {
        _previews.Children.Clear(); foreach (var image in _images) image.Dispose(); _images.Clear();
        foreach (var result in _results) result.Dispose(); _results = []; _original?.Dispose(); _original = null;
    }
    private void ShowPalette()
    {
        _swatches.Children.Clear();
        foreach (var (packed, index) in _displayPalette.Select((color, index) => (color, index)))
        {
            SKColor c = new(packed);
            var button = new Button { Width = 18, Height = 18, MinHeight = 0, MinWidth = 0, Padding = new Thickness(0), Margin = new Thickness(1), Background = new SolidColorBrush(Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue)), BorderThickness = new Thickness(index == _selectedColor ? 2 : 1), BorderBrush = index == _selectedColor ? Skin.AccentBrush : Skin.BorderControlBrush };
            ToolTip.SetTip(button, $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2} · A {c.Alpha}" + (_lockedColors.Contains(packed) ? " · " + Localize.Text("Locked") : ""));
            button.Click += (_, _) => { _selectedColor = index; _hex.Text = $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}"; ShowPalette(); }; _swatches.Children.Add(button);
        }
    }
    private void EditPalette(bool replace)
    {
        if (!SKColor.TryParse(_hex.Text, out var color)) { _status.Text = Localize.Text("Enter a valid hexadecimal color."); return; }
        var palette = _displayPalette.ToList();
        if (replace) { if (_selectedColor < 0 || _selectedColor >= palette.Count) return; palette[_selectedColor] = (uint)color.WithAlpha(255); }
        else if (palette.Count < 256) palette.Add((uint)color.WithAlpha(255));
        SetPalette(palette.Where(c => new SKColor(c).Alpha > 0).ToArray());
    }
    private void DeleteColor()
    {
        if (_selectedColor < 0 || _selectedColor >= _displayPalette.Length) return;
        SetPalette(_displayPalette.Where((_, index) => index != _selectedColor).ToArray());
    }
    private void SetPalette(uint[] colors)
    {
        colors = colors.Where(c => new SKColor(c).Alpha > 0).Distinct().ToArray();
        if (colors.Length is < 2 or > 256) { _status.Text = Localize.Text("A custom palette needs between 2 and 256 colors."); return; }
        _lockedColors.IntersectWith(colors); _transparentColors.IntersectWith(colors);
        _changing = true; _custom = colors; _palette.SelectedIndex = (int)WebPalette.Custom; _colors.Value = Math.Min(256, colors.Length + (_transparent.IsChecked == true ? 1 : 0)); _changing = false; Queue();
    }
    private async Task LoadPalette()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = Localize.Text("Load Color Table"), AllowMultiple = false });
            if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            if (new FileInfo(path).Length > 65536) throw new InvalidDataException("The color table is too large.");
            var colors = JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(path)) ?? [];
            SetPalette(colors.Select(c => SKColor.TryParse(c, out var parsed) ? (uint)parsed.WithAlpha(255) : throw new InvalidDataException("Invalid palette color.")).ToArray());
        }
        catch (Exception error) { _status.Text = Localize.Text(error.Message); }
    }
    private async Task SavePalette()
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new() { Title = Localize.Text("Save Color Table"), SuggestedFileName = "palette.json", DefaultExtension = "json" });
            if (file?.TryGetLocalPath() is not { } path) return;
            var colors = _displayPalette.Where(c => new SKColor(c).Alpha > 0).Select(c => { var color = new SKColor(c); return $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}"; }).ToArray();
            AtomicFile.WriteBytes(path, JsonSerializer.SerializeToUtf8Bytes(colors));
        }
        catch (Exception error) { _status.Text = Localize.Text(error.Message); }
    }
    private void Apply(WebExportOptions options)
    {
        _changing = true;
        _width.Value = options.Width; _height.Value = options.Height; _format.SelectedIndex = (int)options.Format;
        _quality.Value = options.Quality; _colors.Value = options.Colors; _palette.SelectedIndex = (int)options.Palette;
        _dither.SelectedIndex = (int)options.Dither; _alphaDither.SelectedIndex = (int)options.AlphaDither; _amount.Value = options.DitherAmount; _snap.Value = options.WebSnap;
        _transparent.IsChecked = options.Transparency; _interlaced.IsChecked = options.Interlaced; _srgb.IsChecked = options.ConvertToSrgb; _icc.IsChecked = options.EmbedProfile;
        _resample.SelectedIndex = (int)options.Resample; _metadata.SelectedIndex = (int)options.Metadata; _custom = options.CustomPalette ?? [];
        _lockedColors.Clear(); _lockedColors.UnionWith(options.LockedColors ?? []);
        _transparentColors.Clear(); _transparentColors.UnionWith(options.TransparentColors ?? []);
        var c = options.Matte ?? SKColors.White; _matte.Text = $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}";
        _changing = false;
    }
    private sealed record SavedSettings(int Version, WebExportOptions Options, string Matte);
    private async Task SaveSettings()
    {
        try
        {
            var options = Options(); options.Validate();
            var file = await StorageProvider.SaveFilePickerAsync(new() { Title = Localize.Text("Save Settings"), SuggestedFileName = "web-export.json", DefaultExtension = "json" });
            if (file?.TryGetLocalPath() is { } path) AtomicFile.WriteBytes(path, JsonSerializer.SerializeToUtf8Bytes(new SavedSettings(1, options with { Matte = null }, _matte.Text ?? "#FFFFFF")));
        }
        catch (Exception error) { _status.Text = Localize.Text(error.Message); }
    }
    private async Task LoadSettings()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = Localize.Text("Load Settings"), AllowMultiple = false });
            if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            if (new FileInfo(path).Length > 65536) throw new InvalidDataException("The settings file is too large.");
            var settings = JsonSerializer.Deserialize<SavedSettings>(await File.ReadAllTextAsync(path));
            if (settings is null || settings.Version != 1 || !SKColor.TryParse(settings.Matte, out var color)) throw new InvalidDataException("Invalid Web export settings.");
            var options = settings.Options with { Matte = color }; options.Validate(); Apply(options); Queue();
        }
        catch (Exception error) { _status.Text = Localize.Text(error.Message); }
    }
    private async Task Optimize()
    {
        if (_rendering || _closed) return;
        var dialog = new FormDialog("Optimize File Size", 420, 250); var size = dialog.Number("Target Size (KB)", 32, 1, 1024000);
        dialog.Description("JPEG quality or the adaptive color count is reduced. Dimensions are preserved.");
        if (!await dialog.ShowDialog<bool>(this) || _closed) return;
        _timer.Stop(); _revision++; _save.IsEnabled = false; _status.Text = Localize.Text("Rendering preview…");
        await RenderPreview((long)(size.Value ?? 32) * 1024);
    }
    private async Task Save()
    {
        if (_results.Length == 0 || !_save.IsEnabled) return;
        // Retain bytes before the picker, which may pump preview and close events.
        var bytes = _results[0].Bytes; var extension = _results[0].Options.Extension; var revision = _revision;
        var options = _results[0].Options; var kind = SaveKind; var document = _lease.Document;
        var sliced = document.Marks.Any(m => m.Kind == Compositor.Core.Format.MarkKind.Slice && m.Visible);
        var animated = options.Format == WebFormat.Gif && WebExport.Frames(document).Count > 0;
        try
        {
            if (kind == WebSaveKind.ImagesOnly && !sliced && !animated)
            {
                var file = await StorageProvider.SaveFilePickerAsync(new() { Title = Localize.Text("Save for Web"), SuggestedFileName = "image." + extension, DefaultExtension = extension, FileTypeChoices = [new FilePickerFileType(extension.ToUpperInvariant()) { Patterns = ["*." + extension] }] });
                if (_closed || file?.TryGetLocalPath() is not { } path) return;
                _saving = true; _save.IsEnabled = false;
                await Task.Run(() => AtomicFile.WriteBytes(path, bytes)); _status.Text = Localize.Text("Export complete.");
                CompletedExports++;
                return;
            }
            var page = kind != WebSaveKind.ImagesOnly;
            var target = await StorageProvider.SaveFilePickerAsync(new()
            {
                Title = Localize.Text("Save for Web"), SuggestedFileName = page ? "index.html" : "image." + (animated ? "gif" : extension),
                DefaultExtension = page ? "html" : extension,
                FileTypeChoices = [page ? new FilePickerFileType("HTML") { Patterns = ["*.html", "*.htm"] } : new FilePickerFileType(extension.ToUpperInvariant()) { Patterns = ["*." + (animated ? "gif" : extension)] }],
            });
            if (_closed || target?.TryGetLocalPath() is not { } chosen) return;
            var htmlPath = page ? chosen : Path.ChangeExtension(chosen, ".html");
            var scope = (WebSliceScope)Math.Max(0, _scope.SelectedIndex); var html = _html; var loops = Loops;
            // The picker asked about the file it was given; the slices' images are asked about here.
            var existing = WebPage.Planned(document, options, html, htmlPath, kind, scope).Where(p => !string.Equals(p, Path.GetFullPath(chosen), StringComparison.OrdinalIgnoreCase) && File.Exists(p)).ToList();
            if (existing.Count > 0 && !await ConfirmDialog.Ask(this, "Save for Web",
                existing.Count == 1 ? string.Format(Localize.Text("“{0}” already exists. Replace it?"), Path.GetFileName(existing[0]))
                    : string.Format(Localize.Text("{0} of these files already exist in the folder. Replace them?"), existing.Count), "Replace", "Cancel")) return;
            if (_closed) return;
            _saving = true; _save.IsEnabled = false; _status.Text = Localize.Text("Saving…");
            var result = await Task.Run(() => WebPage.Save(document, options, html, htmlPath, kind, Path.GetFileNameWithoutExtension(htmlPath), loops, scope));
            _status.Text = string.Format(Localize.Text("Saved {0} files."), result.Files.Count);
            CompletedExports++;
        }
        catch (Exception error) { _status.Text = Localize.Text(error.Message); }
        finally { _saving = false; _save.IsEnabled = !_closed && revision == _revision && _results.Length > 0 && !_rendering; }
    }

    private WebSaveKind SaveKind => _saveAs.SelectedIndex switch { 1 => WebSaveKind.HtmlAndImages, 2 => WebSaveKind.HtmlOnly, _ => WebSaveKind.ImagesOnly };
    private int Loops => _loop.SelectedIndex switch { 0 => 0, 1 => 1, _ => (int)(_loops.Value ?? 1) };

    private async Task EditOutput()
    {
        if (await OutputSettingsDialog.Ask(this, _html) is { } settings) _html = settings;
    }

    /// <summary>The animation controls, live only for a GIF of a document with a frame animation.</summary>
    private void ShowAnimation()
    {
        var count = WebExport.Frames(_lease.Document).Count;
        var live = count > 0 && _format.SelectedIndex == 0;
        _loop.IsEnabled = live; _loops.IsEnabled = live && _loop.SelectedIndex == 2;
        foreach (var button in _frameButtons) button.IsEnabled = live;
        _frameLabel.Text = count > 0 ? $"{_frame + 1}/{count}" : "1/1";
    }

    private void StepFrame(int step)
    {
        var count = WebExport.Frames(_lease.Document).Count;
        if (count == 0) return;
        _frame = step switch { int.MinValue => 0, int.MaxValue => count - 1, _ => (_frame + step + count) % count };
        ShowAnimation(); Queue();
    }
}