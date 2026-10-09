using System.Text.Json;
using Compositor.Core.Document;
using Compositor.Core.Model;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private string? _lastFilterName;
    private Func<CanvasDocument, Guid, bool>? _lastFilter;
    private bool _lastWasBackground;
    private Avalonia.Controls.MenuItem? _lastFilterItem;

    private void RememberFilter(string name, Func<CanvasDocument, Guid, bool> apply)
    {
        _lastFilterName = name;
        _lastFilter = apply;
        _lastWasBackground = false;
        UpdateLastFilter();
    }

    private void RememberBackgroundFilter()
    {
        _lastFilterName = "Remove Background"; _lastFilter = null; _lastWasBackground = true;
        UpdateLastFilter();
    }

    private void UpdateLastFilter()
    {
        if (_lastFilterItem is null) return;
        _lastFilterItem.Header = _lastFilterName is null ? Localize.Text("Last Filter")
            : Localize.Format($"Last Filter: {Localize.Text(_lastFilterName)}");
        _lastFilterItem.IsEnabled = (_lastFilter is not null || _lastWasBackground) && _document?.Layers.Any(layer =>
            layer.ID == Selected && !layer.IsGroup && layer.Asset is not null) == true && _cameraRaw is null && !_detecting;
    }

    private void RepeatLastFilter()
    {
        if (_lastWasBackground && !_detecting && _cameraRaw is null) { _ = DetectSubject(true); return; }
        if (_lastFilter is not { } apply || _document is not { } document || Selected is not { } id
            || _cameraRaw is not null) return;
        if (Edit(_lastFilterName ?? "Last Filter", () => apply(document, id))) Reselect(id);
    }

    private static T CopyFilterSettings<T>(T settings) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(settings))!;
}
