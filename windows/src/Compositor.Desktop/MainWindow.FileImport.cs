using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;

namespace Compositor.Desktop;

public partial class MainWindow
{
    private async Task ImportWia()
    {
        if (!IsEnabled) return;
        ImportedImage? image = null;
        IsEnabled = false;
        try
        {
            image = await WindowsImageAcquisition.Acquire();
            if (image is null) return;
            AdoptImported(ImageImporter.NewDocument(image), Localize.Text("Scanner Image")); image = null;
        }
        catch (Exception error) { Say(error.Message); }
        finally { image?.Dispose(); IsEnabled = true; Activate(); }
    }

    private async Task ImportDicomFiles()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new()
        {
            Title = Localize.Text("Load Multiple DICOM Files…"), AllowMultiple = true,
            FileTypeFilter = [new("DICOM") { Patterns = ["*.dcm", "*.dicom"] }, FilePickerFileTypes.All]
        });
        var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
        if (paths.Length == 0) return;
        using var cancellation = new CancellationTokenSource();
        var dialog = new TimelineProgressDialog
        {
            Title = Localize.Text("Load Multiple DICOM Files…"), Width = 480, Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(18), Spacing = 12, Children =
            {
                new TextBlock { Text = Localize.Text("Importing DICOM image frames…") },
                new ProgressBar { IsIndeterminate = true, Height = 14 },
                FormDialog.ActionButton("Cancel", cancellation.Cancel)
            } }
        };
        dialog.Closing += (_, _) => cancellation.Cancel();
        var shown = dialog.ShowDialog(this);
        CanvasDocument? imported = null;
        try
        {
            imported = await WorkScheduler.Run(token => DicomImport.Load(paths, token), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            AdoptImported(imported, "DICOM"); imported = null;
        }
        catch (OperationCanceledException) { Say("DICOM import canceled."); }
        catch (Exception error) { Say(error.Message); }
        finally { imported?.Dispose(); dialog.Close(); await shown; }
    }

    private async Task ImportVideoFrames()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new()
        {
            Title = Localize.Text("Video Frames to Layers…"),
            FileTypeFilter = [new("Video") { Patterns = ["*.mp4", "*.mov", "*.mkv", "*.webm", "*.avi", "*.m4v"] }]
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        try
        {
            var media = await WorkScheduler.Run(token => TimelineMedia.Inspect(path, token));
            if (media.Width <= 0 || media.Height <= 0) throw new InvalidDataException("The file has no video stream.");
            var form = BuildVideoImportForm(media, path, out var readOptions);
            if (!await form.ShowDialog<bool>(this)) return;
            var options = readOptions();
            using var cancellation = new CancellationTokenSource();
            var progress = new ProgressBar { Minimum = 0, Maximum = 1, Height = 14, Margin = new Thickness(18) };
            var status = new TextBlock { Text = Localize.Text("Importing Video Frames…"), Margin = new Thickness(18, 8) };
            var dialog = new TimelineProgressDialog
            {
                Title = Localize.Text("Video Frames to Layers…"), Width = 460, Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel { Children = { status, progress, FormDialog.ActionButton("Cancel", cancellation.Cancel) } }
            };
            dialog.Closing += (_, _) => cancellation.Cancel();
            var shown = dialog.ShowDialog(this);
            CanvasDocument? imported = null;
            try
            {
                imported = await WorkScheduler.Run(token => VideoFrameImport.Import(path, media, options,
                    value => Dispatcher.UIThread.Post(() => progress.Value = value), token), cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                AdoptImported(imported, Path.GetFileNameWithoutExtension(path)); imported = null;
            }
            finally { imported?.Dispose(); dialog.Close(); await shown; }
        }
        catch (OperationCanceledException) { Say("Video import canceled."); }
        catch (Exception error) { Say(error.Message); }
    }

    internal static FormDialog BuildVideoImportForm(TimelineMediaInfo media, string path, out Func<VideoFrameImportOptions> readOptions)
    {
        var form = new FormDialog("Video Frames to Layers…", 580, 500);
        form.Description(Path.GetFileName(path));
        var range = form.Choice("Range to Import", ["From Beginning to End", "Selected Range"], 0);
        var start = form.Number("Start (seconds)", 0, 0, media.Duration);
        var end = form.Number("End (seconds)", media.Duration, .001, media.Duration);
        var rate = form.Number("Frame Rate", media.FrameRate, 1, 120);
        var every = form.Number("Import Every Nth Frame", 1, 1, 1000);
        var size = form.Number("Maximum Side (pixels)", Math.Min(1920, Math.Max(media.Width, media.Height)), 1, 8192);
        var animation = form.Check("Make Frame Animation", true);
        var summary = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        form.Fields.Children.Add(summary);
        form.Description("Animation imports support up to 100 frames; layer-only imports support up to 1000. Reduce the size or increase the interval for large videos.");
        VideoFrameImportOptions Options() => new(range.SelectedIndex == 0 ? 0 : (double)(start.Value ?? 0),
            range.SelectedIndex == 0 ? media.Duration : (double)(end.Value ?? 0), (double)(rate.Value ?? 0),
            (int)(every.Value ?? 1), (int)(size.Value ?? 1920), animation.IsChecked == true);
        string? Validate()
        {
            start.IsEnabled = end.IsEnabled = range.SelectedIndex == 1;
            try { summary.Text = $"{Localize.Text("Frames")}: {VideoFrameImport.Times(media, Options()).Length}"; return null; }
            catch (Exception error) { summary.Text = Localize.Text(error.Message); return error.Message; }
        }
        form.Validate = Validate; form.Changed = () => Validate(); Validate();
        readOptions = Options;
        return form;
    }

    private async Task ImportDocumentNotes()
    {
        if (_document is not { } document) return;
        var files = await StorageProvider.OpenFilePickerAsync(new()
        {
            Title = Localize.Text("Import Notes…"),
            FileTypeFilter = [new(Localize.Text("Notes")) { Patterns = ["*.comp-notes.json", "*.pdf", "*.fdf", "*.xfdf"] }]
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        try
        {
            // Acrobat comments come from the first page, placed where they were on it.
            var notes = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? await WorkScheduler.Run(_ => DocumentNotesFile.Read(path))
                : await WorkScheduler.Run(_ =>
                {
                    var (read, pages) = PdfNotes.Read(File.ReadAllBytes(path));
                    var marks = PdfNotes.ToMarks(read, pages, document.Width, document.Height, document.Resolution);
                    if (marks.Length == 0) throw new InvalidDataException("The file has no text notes on its first page.");
                    return marks;
                });
            if (!ReferenceEquals(document, _document)) return;
            if (document.Marks.Count + notes.Length > 10000) throw new InvalidDataException("The document contains too many annotations.");
            Edit("Import Notes…", () => { document.Marks.AddRange(notes); return true; });
        }
        catch (Exception error) { Say(error.Message); }
    }

    private async Task ExportDocumentNotes()
    {
        if (_document is not { } document) return;
        try
        {
            var bytes = DocumentNotesFile.Encode(document.Marks);
            var file = await StorageProvider.SaveFilePickerAsync(new()
            {
                Title = Localize.Text("Export Notes…"), SuggestedFileName = "Notes.comp-notes.json",
                FileTypeChoices = [new("Compositor Notes") { Patterns = ["*.comp-notes.json"] }, new("Acrobat FDF") { Patterns = ["*.fdf"] }, new("Acrobat XFDF") { Patterns = ["*.xfdf"] }]
            });
            if (file is null) return;
            if (file.TryGetLocalPath() is { } target && Path.GetExtension(target).ToLowerInvariant() is ".fdf" or ".xfdf")
                bytes = Path.GetExtension(target).Equals(".fdf", StringComparison.OrdinalIgnoreCase)
                    ? PdfNotes.WriteFdf(document.Marks, document.Height, document.Resolution) : PdfNotes.WriteXfdf(document.Marks, document.Height, document.Resolution);
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0); await stream.WriteAsync(bytes);
        }
        catch (Exception error) { Say(error.Message); }
    }
}
