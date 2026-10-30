using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Compositor.Core.IO;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;

namespace Compositor.Desktop;

public partial class MainWindow
{
    internal async Task RunBatchRecipe(string recipe, IReadOnlyList<string> inputs)
    {
        try
        {
            var dialog = new BatchDialog(false, [], (sources, options, progress, token) =>
                WorkScheduler.Run(cancellation => BatchProcessing.Run(sources, options, progress, cancellation), token));
            dialog.Opened += async (_, _) => await dialog.RunRecipe(recipe, inputs);
            await dialog.ShowDialog(this);
        }
        catch (Exception error) { Say($"Could not process images: {error.Message}"); }
    }

    private async Task ShowBatch(bool processor, bool shortcutMode = false)
    {
        FinishPlacement(true); FinishPen(); CommitText();
        var leases = new List<DocumentLease>();
        try
        {
            var sources = new List<AutomationSource>();
            foreach (var tab in _tabs.Where(t => t.Document is not null))
            {
                var lease = new DocumentLease(tab.Document!); leases.Add(lease);
                sources.Add(new AutomationSource(tab.Name, tab.Path, lease.Document));
            }
            var dialog = new BatchDialog(processor, sources, (inputs, options, progress, token) =>
                WorkScheduler.Run(cancellation => BatchProcessing.Run(inputs, options, progress, cancellation), token), shortcutMode);
            await dialog.ShowDialog(this);
        }
        catch (Exception error) { Say($"Could not process images: {error.Message}"); }
        finally { foreach (var lease in leases) lease.Dispose(); }
    }

    private async Task ShowAutomation(AutomationKind kind)
    {
        FinishPlacement(true); FinishPen(); CommitText();
        var leases = new List<DocumentLease>();
        try
        {
            var sources = new List<AutomationSource>();
            foreach (var tab in _tabs.Where(t => t.Document is not null))
            {
                var lease = new DocumentLease(tab.Document!); leases.Add(lease);
                sources.Add(new AutomationSource(tab.Name, tab.Path, lease.Document));
            }
            AutomationDialog? owner = null;
            var dialog = new AutomationDialog(kind, sources, async (job, token) =>
            {
                if (job.Kind == AutomationKind.HdrPro) { await MergeToHdr(job, owner!, token); return; }
                var documents = await WorkScheduler.Run<IReadOnlyList<CanvasDocument>>(cancellation =>
                {
                    switch (job.Kind)
                    {
                        case AutomationKind.ContactSheet: return FileAutomation.ContactSheets(job.Sources, job.Contact, cancellation);
                        case AutomationKind.LoadLayers: return [FileAutomation.LoadLayers(job.Sources, job.AutoAlign, job.CreateSmartObject, cancellation)];
                        case AutomationKind.Statistics: return [FileAutomation.Statistics(job.Sources, job.Statistic, job.AutoAlign, cancellation)];
                        case AutomationKind.Photomerge:
                        {
                            var images = new List<(string, SkiaSharp.SKBitmap)>();
                            try
                            {
                                foreach (var source in job.Sources) { cancellation.ThrowIfCancellationRequested(); images.Add((source.Name, FileAutomation.RenderSource(source))); }
                                var merged = Photomerge.Merge(images, job.Photomerge ?? new PhotomergeOptions(), cancellation);
                                if (merged.Unplaced.Count > 0) Avalonia.Threading.Dispatcher.UIThread.Post(() => Say(string.Format(Localize.Text("Photomerge could not place: {0}"), string.Join(", ", merged.Unplaced))));
                                return [merged.Document];
                            }
                            finally { foreach (var (_, image) in images) image.Dispose(); }
                        }
                        case AutomationKind.PdfPresentation:
                            FileAutomation.PdfPresentation(job.OutputPath!, job.Sources, job.Pdf, cancellation); return [];
                        default: throw new ArgumentOutOfRangeException(nameof(job));
                    }
                }, token);
                if (token.IsCancellationRequested)
                {
                    foreach (var document in documents) document.Dispose();
                    token.ThrowIfCancellationRequested();
                }
                foreach (var document in documents) AdoptImported(document, Localize.Text(kind.ToString()));
                if (job.OutputPath is { } output) Say($"Saved {output}");
            });
            owner = dialog;
            await dialog.ShowDialog<bool>(this);
        }
        catch (Exception error) { Say($"Could not process images: {error.Message}"); }
        finally { foreach (var lease in leases) lease.Dispose(); }
    }

    private async Task CloseDocuments(bool others)
    {
        var active = _open;
        foreach (var tab in _tabs.ToArray())
        {
            if (others && ReferenceEquals(tab, active)) continue;
            await CloseTab(tab);
            // Cancelling one save prompt stops the entire close operation.
            if (_tabs.Contains(tab) && tab.Document is not null) break;
        }
    }

    /// <summary>
    /// Merge to HDR Pro: the exposures merged small for the toning panel's preview, then in full with what was chosen
    /// there, as a new document.
    /// </summary>
    private async Task MergeToHdr(AutomationJob job, Window owner, CancellationToken token)
    {
        var images = await WorkScheduler.Run(cancellation => job.Sources.Select(source => { cancellation.ThrowIfCancellationRequested(); return FileAutomation.RenderSource(source); }).ToArray(), token);
        try
        {
            if (images.Any(i => i.Width != images[0].Width || i.Height != images[0].Height)) throw new InvalidOperationException("The exposures must be the same size.");
            var scale = Math.Min(1, 480.0 / Math.Max(images[0].Width, images[0].Height));
            var (w, h) = (Math.Max(1, (int)Math.Round(images[0].Width * scale)), Math.Max(1, (int)Math.Round(images[0].Height * scale)));
            var preview = await WorkScheduler.Run(cancellation =>
            {
                var small = images.Select(i => i.Resize(new SkiaSharp.SKImageInfo(w, h, i.ColorType, i.AlphaType), new SkiaSharp.SKSamplingOptions(SkiaSharp.SKFilterMode.Linear))!).ToArray();
                try { return HdrMerge.Radiance(small, false, out _, cancellation); }
                finally { foreach (var s in small) s.Dispose(); }
            }, token);
            if (await HdrProDialog.Ask(owner, preview, w, h, job.AutoAlign) is not { } options) throw new OperationCanceledException();
            var result = await WorkScheduler.Run(cancellation => HdrMerge.Merge(job.Sources.Select((s, i) => (s.Name, images[i])).ToArray(), options, cancellation), token);
            AdoptImported(result.Document, Localize.Text("Merge to HDR Pro"));
        }
        finally { foreach (var image in images) image.Dispose(); }
    }
    private async Task SaveCopy()
    {
        FinishPlacement(true); FinishPen(); CommitText();
        if (_document is null) return;
        using var lease = new DocumentLease(_document);
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Localize.Text("Save a Copy…"), SuggestedFileName = "Copy.comp", DefaultExtension = "comp", ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType(Localize.Text("Compositor Project")) { Patterns = ["*.comp"] }, new FilePickerFileType("Photoshop") { Patterns = ["*.psd"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        if (path.EndsWith(".psd", StringComparison.OrdinalIgnoreCase))
        {
            // A layered Photoshop file: what it cannot keep editable is written as it looks and listed.
            try
            {
                var notes = await WorkScheduler.Run(token => Compositor.Core.IO.PSD.PsdWriter.Write(lease.Document, path, token));
                Say($"Saved {path}");
                if (notes.Count > 0)
                    await ConfirmDialog.Tell(this, "Photoshop conversion report", string.Join("\n", notes.Take(30).Select(note =>
                        (note.Layer.Length > 0 ? note.Layer + ": " : "") + Localize.Text(note.Message))) + (notes.Count > 30 ? "\n…" : ""));
            }
            catch (Exception error) { Say($"Could not save: {error.Message}"); }
            return;
        }
        if (!path.EndsWith(".comp", StringComparison.OrdinalIgnoreCase)) path += ".comp";
        try
        {
            var snapshot = ProjectSnapshot.FromDocument(lease.Document);
            await WorkScheduler.Run(token => { ProjectStore.Save(snapshot, path, cancellation: token); return true; });
            Say($"Saved {path}");
        }
        catch (Exception error) { Say($"Could not save: {error.Message}"); }
    }

    private async Task FitImage()
    {
        if (_document is not { } document) return;
        var dialog = new FormDialog("Fit Image", 440, 290);
        var width = dialog.Number("Width", document.Width, 1, 30000);
        var height = dialog.Number("Height", document.Height, 1, 30000);
        var noEnlarge = dialog.Check("Do Not Enlarge", false);
        if (!await dialog.ShowDialog<bool>(this) || !ReferenceEquals(document, _document)) return;
        var size = FileAutomation.Fit(document.Width, document.Height, (int)width.Value!.Value, (int)height.Value!.Value, noEnlarge.IsChecked == true);
        Edit("Fit Image", () => ImageEdits.Resize(document, size.Width, size.Height, document.Resolution, LayerSampling.HighQuality));
    }
}
