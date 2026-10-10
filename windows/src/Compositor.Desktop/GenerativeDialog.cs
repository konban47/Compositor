using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

internal sealed class GenerativeDialog : DialogWindow
{
    internal sealed record Result(GeneratedImage Image, bool NewDocument);
    private sealed record Item(GeneratedImage Image, string Prompt, WriteableBitmap Preview);
    private static string _sessionKey = "", _sessionEndpoint = "";
    private readonly TextBox _endpoint = new() { PlaceholderText = "https://your-provider.example/v1", Tag = "generation-endpoint" };
    private readonly TextBox _model = new() { PlaceholderText = "model", Tag = "generation-model" };
    private readonly TextBox _key = new() { PasswordChar = '●', Tag = "generation-key" };
    private readonly TextBox _prompt = new() { AcceptsReturn = true, Height = 110, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Tag = "generation-prompt" };
    private readonly ComboBox _size = new() { ItemsSource = new[] { "1024x1024", "1536x1024", "1024x1536", "auto" }, SelectedIndex = 0 };
    private readonly NumericUpDown _count = new() { Minimum = 1, Maximum = 4, Value = 1, Width = 130, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly CheckBox _remember = new() { Content = Localize.Text("Save key with Windows encryption") };
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly ListBox _results = new();
    private readonly Image _preview = new() { Stretch = Avalonia.Media.Stretch.Uniform };
    private readonly Button _generate = new() { Content = Localize.Text("Generate"), Tag = "generate" };
    private readonly Button _cancel = new() { Content = Localize.Text("Cancel Generation"), IsEnabled = false };
    private readonly List<Item> _items = [];
    private readonly HttpClient _http;
    private CancellationTokenSource? _request;
    private bool _closed;
    private Item? Picked => (_results.SelectedItem as ListBoxItem)?.Tag as Item;
    internal GenerativeDialog(bool canImport, HttpMessageHandler? handler = null)
    {
        Title = Localize.Text("Generative Workspace"); Width = 1040; Height = 760; MinWidth = 820; MinHeight = 560;
        _http = handler is null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromMinutes(10);
        var saved = GenerationSettings.Load();
        _endpoint.Text = saved.Endpoint; _model.Text = saved.Model;
        _key.Text = _sessionEndpoint == saved.Endpoint ? _sessionKey : saved.Key(); _remember.IsChecked = saved.ProtectedKey is not null;
        if (_size.Items.Contains(saved.Size)) _size.SelectedItem = saved.Size;
        var controls = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 14, 0) };
        void Field(string label, Control value) { controls.Children.Add(new TextBlock { Text = Localize.Text(label) }); controls.Children.Add(value); }
        Field("API Base URL or Full Generation Endpoint", _endpoint); Field("Image Model", _model); Field("API Key", _key); controls.Children.Add(_remember);
        var save = new Button { Content = Localize.Text("Save Connection Settings") }; save.Click += (_, _) => SaveSettings(); controls.Children.Add(save);
        Field("Prompt", _prompt); Field("Image Size", _size); Field("Number of Images", _count);
        controls.Children.Add(new TextBlock { Text = Localize.Text("Generate sends the prompt to your configured provider and may incur its API charges. Project images are not uploaded."), TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12 });
        controls.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _generate, _cancel } }); controls.Children.Add(_status);
        _generate.Click += (_, _) => _ = Generate(); _cancel.Click += (_, _) => _request?.Cancel();
        var previewPanel = new Grid { RowDefinitions = new RowDefinitions("*,180,Auto") };
        previewPanel.Children.Add(_preview); Grid.SetRow(_results, 1); previewPanel.Children.Add(_results);
        _results.SelectionChanged += (_, _) => _preview.Source = Picked?.Preview;
        var footer = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        void Action(string title, Action action, bool enabled = true)
        {
            var button = new Button { Content = Localize.Text(title), IsEnabled = enabled, Margin = new Thickness(3), Tag = title };
            button.Click += (_, _) => action(); footer.Children.Add(button);
        }
        Action("Import as Layer", () => { if (Picked is { } item) Close(new Result(item.Image, false)); }, canImport);
        Action("Open as New Document", () => { if (Picked is { } item) Close(new Result(item.Image, true)); });
        Action("Save Image…", () => _ = SaveImage());
        Action("Reuse Prompt", () => { if (Picked is { } item) _prompt.Text = item.Prompt; });
        Action("Delete Result", DeleteResult);
        Grid.SetRow(footer, 2); previewPanel.Children.Add(footer);
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("330,*"), Margin = new Thickness(18) };
        layout.Children.Add(new ScrollViewer { Content = controls }); Grid.SetColumn(previewPanel, 1); layout.Children.Add(previewPanel); Content = layout;
        Closing += (_, _) => { _closed = true; _request?.Cancel(); };
        Closed += (_, _) => { _http.Dispose(); foreach (var item in _items) item.Preview.Dispose(); _items.Clear(); _key.Text = ""; };
    }
    private bool SaveSettings()
    {
        try
        {
            var endpoint = _endpoint.Text?.Trim() ?? "";
            ImageGenerationClient.Endpoint(endpoint);
            new GenerationSettings { Endpoint = endpoint, Model = _model.Text?.Trim() ?? "", Size = _size.SelectedItem?.ToString() ?? "1024x1024" }.Save(_key.Text ?? "", _remember.IsChecked == true);
            _sessionEndpoint = endpoint; _sessionKey = _key.Text ?? "";
            _status.Text = Localize.Text("Connection settings saved."); return true;
        }
        catch { _status.Text = Localize.Text("Could not save settings. Check the API address and Windows key storage."); return false; }
    }
    internal async Task Generate()
    {
        if (_request is not null) return;
        var request = new GenerationRequest(_endpoint.Text ?? "", _model.Text ?? "", _prompt.Text ?? "", _size.SelectedItem?.ToString() ?? "1024x1024", (int)(_count.Value ?? 1));
        if (!SaveSettings()) return;
        using var cancel = new CancellationTokenSource(); _request = cancel;
        _generate.IsEnabled = false; _cancel.IsEnabled = true; _status.Text = Localize.Text("Generating…");
        try
        {
            var images = await new ImageGenerationClient(_http).Generate(request, _key.Text ?? "", cancel.Token);
            if (_closed) return;
            foreach (var generated in images)
            {
                using var bitmap = SKBitmap.Decode(generated.Bytes) ?? throw new InvalidDataException();
                using var reduced = Bitmaps.Scale(bitmap, Math.Min(bitmap.Width, 640), Math.Max(1, (int)(bitmap.Height * Math.Min(1, 640.0 / bitmap.Width))));
                var item = new Item(generated, request.Prompt, CanvasView.ToImage(reduced)); _items.Add(item);
                while (_items.Count > 12 || _items.Sum(i => (long)i.Image.Bytes.Length) > 256 * 1024 * 1024)
                { _items[0].Preview.Dispose(); _items.RemoveAt(0); }
            }
            RefreshResults(); _results.SelectedIndex = _results.ItemCount - 1;
            _status.Text = Localize.Text("Generation complete. Select an image to import or save.");
        }
        catch (OperationCanceledException) { if (!_closed) _status.Text = Localize.Text("Generation cancelled or timed out."); }
        catch (HttpRequestException e) { if (!_closed) _status.Text = Localize.Text("Generation failed. Check the endpoint, model, key, quota and network.") + (e.StatusCode is { } status ? $" (HTTP {(int)status})" : ""); }
        catch { if (!_closed) _status.Text = Localize.Text("Generation failed. The provider response or image was not supported."); }
        finally { _request = null; if (!_closed) { _generate.IsEnabled = true; _cancel.IsEnabled = false; } }
    }
    private void RefreshResults()
    {
        _results.Items.Clear();
        foreach (var item in _items) _results.Items.Add(new ListBoxItem { Tag = item, Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = {
            new Image { Source = item.Preview, Width = 70, Height = 60, Stretch = Avalonia.Media.Stretch.Uniform },
            new TextBlock { Text = item.Prompt.Length > 90 ? item.Prompt[..90] + "…" : item.Prompt, MaxWidth = 440, TextWrapping = Avalonia.Media.TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }
        } } });
    }
    private void DeleteResult()
    {
        if (Picked is not { } item) return;
        _preview.Source = null; _items.Remove(item); item.Preview.Dispose(); RefreshResults();
    }
    private async Task SaveImage()
    {
        if (Picked is not { } item) return;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new() { Title = Localize.Text("Save Generated Image"), SuggestedFileName = "generated.png", DefaultExtension = "png" });
            if (file?.TryGetLocalPath() is not { } path) return;
            using var bitmap = SKBitmap.Decode(item.Image.Bytes); using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            await File.WriteAllBytesAsync(path, encoded.ToArray()); _status.Text = Localize.Text("Image saved.");
        }
        catch { _status.Text = Localize.Text("Could not save image."); }
    }
}

public sealed partial class MainWindow
{
    private async Task OpenGenerativeWorkspace()
    {
        if (await new GenerativeDialog(_document is not null).ShowDialog<GenerativeDialog.Result?>(this) is not { } result) return;
        ImportGenerated(result);
    }
    private void ImportGenerated(GenerativeDialog.Result result)
    {
        using var decoded = SKBitmap.Decode(result.Image.Bytes);
        if (decoded is null) return;
        if (result.NewDocument || _document is null)
        {
            AdoptImported(ImageImporter.NewDocument(ImportedImage.Create(decoded.Copy(), Localize.Text("Generated Image"))), "Generate Image");
            return;
        }
        Guid? added = null;
        Edit("Import Generated Image", () =>
        {
            added = Compositor.Core.Document.LayerPlacement.AddImage(_document, decoded.Copy(),
                SKRectI.Create((_document.Width - decoded.Width) / 2, (_document.Height - decoded.Height) / 2, decoded.Width, decoded.Height),
                Localize.Text("Generated Image"), Selected);
            return added is not null;
        });
        if (added is { } id) Reselect(id);
        Refresh();
    }
}
