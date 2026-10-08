using Avalonia.Platform.Storage;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.IO.PSD;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private async Task SaveTab(Tab tab, bool asNew)
    {
        if (ReferenceEquals(tab, _open)) CommitText();
        if (tab.Document is null) return;
        if (tab.Saving is { } pending) { await pending; return; }
        var path = tab.Path;
        if (asNew || path is null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Localize.Text("Save Compositor project folder"), SuggestedFileName = path is null ? "Untitled.comp" : Path.GetFileName(path),
                DefaultExtension = "comp", ShowOverwritePrompt = true,
            });
            if (file?.TryGetLocalPath() is not { } picked) return;
            path = picked.EndsWith(".comp", StringComparison.OrdinalIgnoreCase) ? picked : picked + ".comp";
        }
        if (File.Exists(path)) { Say("Choose a folder name ending in .comp, not an existing file."); return; }
        tab.Saving = WriteTab(tab, path);
        try { await tab.Saving; }
        finally { tab.Saving = null; }
    }

    private async Task<bool> WriteTab(Tab tab, string path)
    {
        var snapshot = ProjectSnapshot.FromDocument(tab.Document!);
        var revision = tab.History.CurrentRevision;
        try
        {
            Say($"Saving {Path.GetFileName(path)}…");
            await Task.Run(() => ProjectStore.Save(snapshot, path));
            tab.Path = path;
            tab.History.MarkSaved(revision);
            tab.Watch = ProjectWatch.For(path);
            NoteRecent(path);
            if (ReferenceEquals(tab, _open)) { WatchProject(); Refresh(); }
            RefreshTabs();
            Say($"Saved {path}");
            return true;
        }
        catch (Exception error) { Say($"Could not save: {error.Message}"); return false; }
    }

    /// <summary>The file picker, command line and shell integration use the same import path.</summary>
    internal async Task OpenPath(string path, bool newTab = true)
    {
        try
        {
            path = Path.GetFullPath(path);
            if (Directory.Exists(path)) { Open(path); return; }
            if (Path.GetFileName(path).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            { Open(Path.GetDirectoryName(path)!); return; }
            var extension = Path.GetExtension(path);
            if (extension.Equals(".psd", StringComparison.OrdinalIgnoreCase) || extension.Equals(".psb", StringComparison.OrdinalIgnoreCase))
            {
                var imported = await Task.Run(() => PsdImporter.Read(path));
                var snapshot = imported.Snapshot();
                if (imported.Notes.Count > 0 && !await ConfirmDialog.Ask(this, "Photoshop conversion report",
                    string.Join(Environment.NewLine, imported.Notes.Select(note => $"{note.Layer}: {note.What}")), "Import", "Cancel"))
                { snapshot.Dispose(); return; }
                AdoptImported(snapshot.ToDocument(), "Import Photoshop");
                Say($"Imported {Path.GetFileName(path)} with {imported.Manifest.Layers.Count} layers.");
                return;
            }
            if (!ImageImporter.LooksImportable(path)) throw new ImportException(ImportError.Unsupported);
            var target = _open;
            var fitting = Fitting();
            var image = await Task.Run(() => ImageImporter.Decode(path, fitting));
            if (ImageImporter.IsRaw(path))
            {
                var developed = await RawDevelopDialog.Develop(this, image);
                if (developed is null) { image.Dispose(); return; }
                image = developed;
            }
            if (newTab || target.Document is null)
            {
                AdoptImported(ImageImporter.NewDocument(image), "Import Image");
                return;
            }
            if (!_tabs.Contains(target)) { image.Dispose(); return; }
            var document = target.Document!;
            var pixels = document.Layers.Sum(layer => (long)(layer.Asset?.Width ?? 0) * (layer.Asset?.Height ?? 0));
            if (pixels + (long)image.Width * image.Height > DocumentLimits.DocumentPixelBudget)
            { image.Dispose(); throw new ImportException(ImportError.TooLarge); }
            var origin = new SKPoint((document.Width - image.Width) / 2f, (document.Height - image.Height) / 2f);
            target.History.Begin("Import Image", document, null);
            document.Layers.Add(new ImageLayer(Guid.NewGuid(), image,
                new LayerTransform(origin.X, origin.Y, image.Width, image.Height), image.Name));
            target.History.End(document, document.Layers[^1].ID);
            if (ReferenceEquals(target, _open)) { Reselect(document.Layers[^1].ID); Refresh(); }
            RefreshTabs();
        }
        catch (Exception error) { Say($"Could not open {Path.GetFileName(path)}: {error.Message}"); }
    }

    private void AdoptImported(CanvasDocument document, string name)
    {
        _open = TabForNew();
        _document = document;
        _projectPath = null;
        _history.Reset();
        _history.MarkUnsaved();
        Show(_open);
        if (document.Layers.Count > 0) Reselect(document.Layers[^1].ID);
        Say($"{name}: {document.Width} × {document.Height}, {document.Layers.Count} layers. Save as .comp to keep editing.");
    }
}
