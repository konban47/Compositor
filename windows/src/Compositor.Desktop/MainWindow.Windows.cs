using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private sealed class SubjectProgressWindow : DialogWindow;
    private Menu? _mainMenu;
    private Control[] _chrome = [];
    private bool[] _chromeVisibility = [];
    private bool _canvasOnly;
    private WindowState _previousWindowState;
    private bool _confirmingClose;
    private bool _closeApproved;
    private bool _detecting;

    private void SetLanguage(string language)
    {
        try { Localize.SaveLanguage(language); Say("Language takes effect after restarting Compositor."); }
        catch (Exception error) { Say($"Could not save: {error.Message}"); }
    }

    private async void ConfirmWindowClose(object? sender, WindowClosingEventArgs e)
    {
        if (_closeApproved) return;
        CommitText();
        if (_detecting) { e.Cancel = true; return; }
        if (!_tabs.Any(tab => tab.History.IsModified || tab.Saving is not null)) return;
        e.Cancel = true;
        if (_confirmingClose) return;
        _confirmingClose = true;
        try
        {
            foreach (var tab in _tabs.ToArray())
                if (!await MayReplace(tab)) return;
            _closeApproved = true;
            Close();
        }
        finally { _confirmingClose = false; }
    }

    private void RotateCanvas(bool clockwise)
    {
        CommitText(); CancelCrop();
        Change("Rotate Canvas 90°", doc => CanvasRotation.Rotate(doc, clockwise));
        _canvas.Fit();
    }

    private void ToggleCanvasOnly()
    {
        if (!_canvasOnly)
        {
            _previousWindowState = WindowState;
            _chromeVisibility = _chrome.Select(control => control.IsVisible).ToArray();
            foreach (var control in _chrome) control.IsVisible = false;
            WindowState = WindowState.FullScreen;
        }
        else
        {
            WindowState = _previousWindowState;
            for (var i = 0; i < _chrome.Length; i++) _chrome[i].IsVisible = _chromeVisibility[i];
        }
        _canvasOnly = !_canvasOnly;
        _canvas.CanvasOnly = _canvasOnly;
        Background = _canvasOnly ? Brushes.Black : Skin.ChromeBrush;
        _canvas.InvalidateVisual();
        _canvas.Focus();
    }

    private async Task FindCommand()
    {
        if (_mainMenu is null) return;
        var entries = new List<CommandPalette.Entry>();
        foreach (var root in _mainMenu.Items.OfType<MenuItem>()) Add(root, "");
        void Add(MenuItem item, string parent)
        {
            var name = (item.Header?.ToString() ?? "").Replace("_", "");
            var title = parent.Length == 0 ? name : parent + " › " + name;
            var children = item.Items.OfType<MenuItem>().ToArray();
            if (children.Length != 0) { foreach (var child in children) Add(child, title); return; }
            if (!item.IsEnabled || !item.IsVisible || item.Header is null) return;
            entries.Add(new(title, item.InputGesture?.ToString() ?? "", () =>
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent))));
        }
        var selected = await CommandPalette.Pick(this, entries);
        selected?.Execute();
    }

    private async Task DetectSubject(bool removeBackground, SKPoint? point = null,
        Compositor.Core.Document.SelectionMode mode = Compositor.Core.Document.SelectionMode.Replace)
    {
        if (_detecting || _document is not { } document || _canvas.TextEditing) return;
        var id = Selected;
        var layer = document.Layers.FirstOrDefault(l => l.ID == id);
        if (removeBackground && layer is not { IsGroup: false, Asset: not null })
        { Say("Select an image layer to remove its background."); return; }
        using var source = removeBackground ? layer!.Asset!.Image.Copy()
            : SelectionEdits.Sample(document, _options.WandAllLayers ? null : id);
        if (source is null) { Say("Select an image layer first."); return; }
        var revision = _history.CurrentRevision;
        using var cancel = new CancellationTokenSource();
        var progress = new SubjectProgressWindow
        {
            Title = Localize.Text(removeBackground ? "Remove Background" : "Select Subject"),
            Width = 380, CanResize = false, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var stop = new Button { Content = Localize.Text("Cancel"), IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        stop.Click += (_, _) => cancel.Cancel();
        progress.Content = new StackPanel
        {
            Margin = new Thickness(18), Spacing = 14,
            Children =
            {
                new TextBlock { Text = Localize.Text("Finding the subject on this computer…"), Foreground = Skin.LabelBrush },
                new ProgressBar { IsIndeterminate = true }, stop,
            },
        };
        var done = false;
        progress.Closing += (_, e) => { if (!done) { cancel.Cancel(); e.Cancel = true; stop.IsEnabled = false; } };
        _detecting = true;
        var dialog = progress.ShowDialog(this);
        try
        {
            using var matte = await Task.Run(() =>
            {
                using var detector = new SubjectSegmentation();
                return detector.Predict(source, cancel.Token);
            }, cancel.Token);
            cancel.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(document, _document) || _history.CurrentRevision != revision) return;
            if (removeBackground)
            {
                Change("Remove Background", doc => SubjectEdits.RemoveBackground(doc, id!.Value, matte));
                if (_history.CurrentRevision != revision) RememberBackgroundFilter();
            }
            else
            {
                using var outline = SubjectEdits.Outline(matte, point);
                Change(point is null ? "Select Subject" : "Object Selection",
                    doc => SelectionEdits.Apply(doc, outline, mode, _options.SelectionAntialiased));
            }
        }
        catch (OperationCanceledException) { Say("Subject detection cancelled."); }
        catch (Exception error) { Say($"Could not find the subject: {error.Message}"); }
        finally { done = true; progress.Close(); await dialog; _detecting = false; UpdateLastFilter(); }
    }
}
