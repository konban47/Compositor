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
    private readonly ComboBox _palette = Choice(["Perceptual", "Selective", "Adaptive", "Restrictive", "Custom", "Black - White", "Grayscale", "Mac OS", "Windows"], 1);
    private readonly ComboBox _dither = Choice(["No Dither", "Diffusion", "Pattern", "Noise"], 1);
    private readonly ComboBox _alphaDither = Choice(["No Transparency Dither", "Diffusion Transparency Dither", "Pattern Transparency Dither", "Noise Transparency Dither"]);
    private readonly ComboBox _resample = Choice(["Nearest Neighbor", "Bilinear", "Bicubic", "Bicubic Smoother", "Bicubic Sharper"], EditorPreferences.DefaultResample);
    private readonly ComboBox _metadata = Choice(["None", "Copyright", "Copyright and Contact", "All Except Camera Information", "All"], 2);
    private readonly ComboBox _view = Choice(["Original", "Optimized", "2-Up", "4-Up"], 0);
    private readonly ComboBox _saveAs = Choice(["Images Only", "HTML and Images", "HTML Only"]);
    private readonly ComboBox _scope = Choice(["All Slices", "All User Slices"]);
    private readonly ComboBox _loop = Choice(["Forever", "Once", "Other"]);
    private readonly NumericUpDown _loops = Number(3, 1, 65535);
    private readonly TextBlock _frameLabel = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 48, TextAlignment = TextAlignment.Center };
    private readonly List<Button> _frameButtons = [];
    /// <summary>Which frame of a frame animation the preview shows; −1 when the document has none.</summary>
    private int _frame = -1;
    private WebHtmlSettings _html = WebHtmlSettings.Default;
    private readonly NumericUpDown _width, _height, _percent = Number(100, 1, 1600);
    private readonly NumericUpDown _quality = Number(80, 1, 100), _colors = Number(256, 2, 256), _amount = Number(100, 0, 100), _snap = Number(0, 0, 100);
    private readonly NumericUpDown _zoom = Number(100, 6, 1600);
    private readonly CheckBox _transparent = Check("Transparency", true), _interlaced = Check("Interlaced"), _srgb = Check("Convert to sRGB", true), _icc = Check("Embed Color Profile"), _linked = Check("Constrain Proportions", true);
    private readonly TextBox _matte = new() { Text = "#FFFFFF" };
    private readonly Grid _previews = new();
    private readonly WrapPanel _swatches = new() { Orientation = Orientation.Horizontal, Width = 16 * 18 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Skin.SecondaryBrush };
    private readonly TextBlock _sample = new() { Text = "RGBA: —", VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _save;
    private static WebExportOptions? _remembered;
    private readonly List<WriteableBitmap> _images = [];
    private WebExportResult[] _results = [];
    private SKBitmap? _original;
    private uint[] _custom = [], _displayPalette = [];
    private int _revision;
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
        Title = Localize.Text("Save for Web"); Width = 1240; Height = 930; MinWidth = 960; MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _save = FormDialog.ActionButton("Save…", () => _ = Save()); _save.IsEnabled = false;
        BuildLayout();
        foreach (var choice in new[] { _format, _palette, _dither, _alphaDither, _resample, _metadata }) choice.SelectionChanged += (_, _) => { if (!_applyingPreset && !_changing) _preset.SelectedIndex = WebPresets.Length; Queue(); };
        _display.SelectionChanged += (_, _) => ShowPreviews();
        if (_remembered is { } last) Apply(last with { Width = document.Width, Height = document.Height });
        _loop.SelectionChanged += (_, _) => ShowAnimation();
        var frameCount = WebExport.Frames(document).Count; _frame = frameCount > 0 ? 0 : -1;
        ShowAnimation();
        foreach (var number in new[] { _quality, _colors, _amount, _snap, _lossy, _alphaAmount }) number.ValueChanged += (_, _) => { if (!_applyingPreset && !_changing) _preset.SelectedIndex = WebPresets.Length; Queue(); };
        foreach (var check in new[] { _transparent, _interlaced, _srgb, _icc }) { check.IsCheckedChanged += (_, _) => Queue(); }
        _matte.TextChanged += (_, _) => Queue();
        _width.ValueChanged += (_, _) => DimensionChanged(true); _height.ValueChanged += (_, _) => DimensionChanged(false);
        _percent.ValueChanged += (_, _) => { if (_changing || _percent.Value is not { } percent) return; _changing = true; _width.Value = Math.Clamp(Math.Round(_lease.Document.Width * percent / 100), 1, DocumentLimits.MaxSide); _height.Value = Math.Clamp(Math.Round(_lease.Document.Height * percent / 100), 1, DocumentLimits.MaxSide); _changing = false; Queue(); };
        AddHandler(PointerPressedEvent, (_, e) => _modifiers = e.KeyModifiers, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _zoom.ValueChanged += (_, _) => ShowPreviews(); _view.SelectionChanged += (_, _) => { ShowPreviews(); Queue(); };
        _timer.Tick += (_, _) => { _timer.Stop(); _ = RenderPreview(); };
        Opened += (_, _) => Queue();
        Closing += (_, e) => { if (_saving) e.Cancel = true; };
        Closed += (_, _) => { _closed = true; _timer.Stop(); _renderCancel?.Cancel(); DisposePreviews(); if (!_rendering) _lease.Dispose(); };
    }

    private static ComboBox Choice(string[] choices, int selected = 0) => new() { ItemsSource = choices.Select(Localize.Text).ToArray(), SelectedIndex = selected, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static NumericUpDown Number(int value, int min, int max) => new() { Value = value, Minimum = min, Maximum = max, Increment = 1, FormatString = "0", ShowButtonSpinner = false };
    private static CheckBox Check(string title, bool value = false) => new() { Content = Localize.Text(title), IsChecked = value };
    private WebExportOptions Options()
    {
        if (!SKColor.TryParse(_matte.Text, out var matte)) throw new InvalidDataException("Enter a valid hexadecimal matte color.");
        return new((int)(_width.Value ?? 1), (int)(_height.Value ?? 1), (WebFormat)_format.SelectedIndex,
            (int)(_quality.Value ?? 80), (int)(_colors.Value ?? 256), Palettes[Math.Max(0, _palette.SelectedIndex)],
            (WebDither)_dither.SelectedIndex, (int)(_amount.Value ?? 100), _transparent.IsChecked == true,
            (WebDither)_alphaDither.SelectedIndex, matte, _interlaced.IsChecked == true, (int)(_snap.Value ?? 0),
            _srgb.IsChecked == true, _icc.IsChecked == true, (WebResample)_resample.SelectedIndex, _custom,
        _lockedColors.ToArray(), _transparentColors.ToArray(), (MetadataPolicy)_metadata.SelectedIndex, (int)(_lossy.Value ?? 0), (int)(_alphaAmount.Value ?? 100));
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
        foreach (var c in new Control[] { _palette, _colors, _dither, _amount, _snap, _swatches }) c.IsEnabled = indexed;
        foreach (var c in _indexedOnly.OfType<Control>()) c.IsVisible = _format.SelectedIndex != 1;
        _qualityFields.IsVisible = _format.SelectedIndex == 1; _quality.IsEnabled = _format.SelectedIndex == 1;
        _lossy.IsEnabled = _format.SelectedIndex == 0; _alphaAmount.IsEnabled = _alphaDither.SelectedIndex == 1;
        _amount.IsEnabled = indexed && _dither.SelectedIndex == 1;
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
            _displayPalette = _results[0].Palette; _selected.Clear(); SortPalette(); ShowPreviews(); _save.IsEnabled = true;
            _status.Text = "";
            if (targetBytes is { } limit && _results[0].Bytes.Length > limit) _status.Text = Localize.Text("The requested size cannot be reached at these dimensions and format.");
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
    private void DisposePreviews()
    {
        _previews.Children.Clear(); foreach (var image in _images) image.Dispose(); _images.Clear();
        foreach (var result in _results) result.Dispose(); _results = []; _original?.Dispose(); _original = null;
    }
    private void SetPalette(uint[] colors)
    {
        colors = colors.Where(c => new SKColor(c).Alpha > 0).Distinct().ToArray();
        if (colors.Length is < 2 or > 256) { _status.Text = Localize.Text("A custom palette needs between 2 and 256 colors."); return; }
        _lockedColors.IntersectWith(colors); _transparentColors.IntersectWith(colors);
        _changing = true; _custom = colors; _palette.SelectedIndex = Array.IndexOf(Palettes, WebPalette.Custom); _colors.Value = Math.Min(256, colors.Length + (_transparent.IsChecked == true ? 1 : 0)); _changing = false; Queue();
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
        _quality.Value = options.Quality; _colors.Value = options.Colors; _palette.SelectedIndex = Array.IndexOf(Palettes, options.Palette); _lossy.Value = options.Lossy; _alphaAmount.Value = options.AlphaDitherAmount;
        _dither.SelectedIndex = (int)options.Dither; _alphaDither.SelectedIndex = (int)options.AlphaDither; _amount.Value = options.DitherAmount; _snap.Value = options.WebSnap;
        _transparent.IsChecked = options.Transparency; _interlaced.IsChecked = options.Interlaced; _srgb.IsChecked = options.ConvertToSrgb; _icc.IsChecked = options.EmbedProfile;
        _resample.SelectedIndex = (int)options.Resample; _metadata.SelectedIndex = (int)options.Metadata; _custom = options.CustomPalette ?? [];
        _lockedColors.Clear(); _lockedColors.UnionWith(options.LockedColors ?? []);
        _transparentColors.Clear(); _transparentColors.UnionWith(options.TransparentColors ?? []);
        SetMatte(options.Matte ?? SKColors.White);
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