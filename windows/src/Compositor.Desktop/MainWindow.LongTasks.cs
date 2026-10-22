using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Compositor.Core.Model;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private async Task<bool> BackgroundEdit(string name, Func<CanvasDocument, bool> apply)
    {
        if (_document is not { } document) return false;
        if (document.Layers.Sum(l => (long)(l.Asset?.Width ?? 0) * (l.Asset?.Height ?? 0)) <= 262144)
            return Edit(name, () => apply(document));
        var revision = _history.CurrentRevision;
        using var snapshot = new DocumentLease(document);
        var originals = snapshot.Document.Layers.SelectMany(l => new[] { l.Asset, l.Mask?.Asset })
            .Concat(snapshot.Document.Channels.Select(c => c.Asset)).OfType<ImportedImage>().ToHashSet();
        var adopted = false;
        try
        {
            var changed = await RunLongTask(name, token =>
            {
                var result = apply(snapshot.Document); token.ThrowIfCancellationRequested(); return result;
            });
            if (!changed || !ReferenceEquals(document, _document) || revision != _history.CurrentRevision) return false;
            adopted = Edit(name, () => { document.Adopt(snapshot.Document); return true; });
            return adopted;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception error) { Say(error.Message); return false; }
        finally
        {
            if (!adopted)
                foreach (var asset in snapshot.Document.Layers.SelectMany(l => new[] { l.Asset, l.Mask?.Asset })
                    .Concat(snapshot.Document.Channels.Select(c => c.Asset)).OfType<ImportedImage>().Distinct())
                    if (!originals.Contains(asset)) asset.Dispose();
        }
    }

    private async Task<T> RunLongTask<T>(string title, Func<CancellationToken, T> work)
    {
        using var cancel = new CancellationTokenSource();
        var progress = new SubjectProgressWindow { Title = Localize.Text(title), Width = 360, CanResize = false,
            SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var stop = new Button { Content = Localize.Text("Cancel"), HorizontalAlignment = HorizontalAlignment.Right };
        stop.Click += (_, _) => { cancel.Cancel(); stop.IsEnabled = false; };
        progress.Content = new StackPanel { Margin = new Thickness(18), Spacing = 12,
            Children = { new TextBlock { Text = Localize.Text(title) }, new ProgressBar { IsIndeterminate = true }, stop } };
        var finished = false;
        progress.Closing += (_, args) => { if (!finished) { cancel.Cancel(); args.Cancel = true; stop.IsEnabled = false; } };
        var shown = progress.ShowDialog(this);
        try { return await WorkScheduler.Run(work, cancel.Token); }
        finally { finished = true; progress.Close(); await shown; }
    }
}
